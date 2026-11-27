using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace Workbench
{
    /// <summary>Lädt echte Vorschaubilder auf einem eigenen Thread, damit die Oberfläche nie hängt.</summary>
    static class Vorschau
    {
        public static bool Sofort;   // Render-Modus: synchron laden
        // Aufträge: neueste zuerst (was man gerade sieht, kommt vor dem, was man schon verlassen hat)
        static readonly ConcurrentStack<Action> auftraege = new ConcurrentStack<Action>();
        static readonly SemaphoreSlim signal = new SemaphoreSlim(0);
        static readonly Dictionary<string, BitmapSource> cache = new Dictionary<string, BitmapSource>();
        static readonly Dictionary<string, long> cacheZeit = new Dictionary<string, long>();      // Änderungszeit der Datei beim Laden
        static readonly LinkedList<string> cacheReihe = new LinkedList<string>();
        static readonly Dictionary<string, LinkedListNode<string>> cacheKnoten = new Dictionary<string, LinkedListNode<string>>();
        static Thread[] arbeiter;
        const int CacheMax = 3000;                       // Einträge (auch „kein Bild“ zählt, kostet aber nichts)
        const long WarmMax = 14L << 20, KaltMax = 10L << 20;   // entpackt höchstens 14 MB, gepackt höchstens 10 MB
        static long warmBytes, kaltBytes;
        static readonly Dictionary<string, byte[]> kalt = new Dictionary<string, byte[]>();
        static readonly LinkedList<string> kaltReihe = new LinkedList<string>();

        /// <summary>Bildschirm-Skalierung (1 = 100 %, 1,5 = 150 %): Vorschaubilder genau so groß laden, wie sie gezeigt werden.</summary>
        public static double Skala = 1.5;

        static long Bytes(BitmapSource b) { return b == null ? 0 : (long)b.PixelWidth * b.PixelHeight * 4; }

        /// <summary>Schlüssel ohne Dateizugriff (früher: eine Windows-Abfrage je Symbol mitten im Aufbau). Geprüft wird still im Hintergrund.</summary>
        static string Schluessel(string pfad, int px) { return pfad.ToLowerInvariant() + "|" + px; }

        static void Auftrag(Action a) { Starten(); auftraege.Push(a); signal.Release(); }

        // fertige Bilder gebündelt einsetzen: ein Aufruf je Bildschirmbild statt einer je Bild, und nie mitten in einem Übergang
        static readonly ConcurrentQueue<Action> fertigListe = new ConcurrentQueue<Action>();
        static int fertigGeplant;
        static DateTime haltenBis;
        public static void Halten(int ms) { var t = DateTime.UtcNow.AddMilliseconds(ms); if (t > haltenBis) haltenBis = t; }

        static void Fertig(Action a)
        {
            fertigListe.Enqueue(a);
            if (Interlocked.Exchange(ref fertigGeplant, 1) == 0) Planen(0);
        }

        static void Planen(int ms)
        {
            var disp = Application.Current.Dispatcher;
            if (ms <= 0) { disp.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(Einsetzen)); return; }
            var t = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background, disp) { Interval = TimeSpan.FromMilliseconds(ms) };
            t.Tick += delegate { t.Stop(); Einsetzen(); };
            t.Start();
        }

        static void Einsetzen()
        {
            var warten = haltenBis - DateTime.UtcNow;
            if (warten.TotalMilliseconds > 1) { Planen((int)warten.TotalMilliseconds + 10); return; }
            var uhr = System.Diagnostics.Stopwatch.StartNew();
            Action a;
            while (uhr.ElapsedMilliseconds < 8 && fertigListe.TryDequeue(out a)) { try { a(); } catch { } }
            if (!fertigListe.IsEmpty) { Planen(16); return; }   // Rest im nächsten Bild – der Bildschirm bleibt flüssig
            Interlocked.Exchange(ref fertigGeplant, 0);
            if (!fertigListe.IsEmpty && Interlocked.Exchange(ref fertigGeplant, 1) == 0) Planen(0);
        }

        static bool AusCache(string k, out BitmapSource bs)
        {
            byte[] png = null;
            lock (cache)
            {
                if (cache.TryGetValue(k, out bs))
                {
                    LinkedListNode<string> n;
                    if (cacheKnoten.TryGetValue(k, out n)) { cacheReihe.Remove(n); cacheReihe.AddLast(n); }
                    return true;
                }
                if (kalt.TryGetValue(k, out png)) { kalt.Remove(k); kaltReihe.Remove(k); kaltBytes -= png.Length; }
            }
            if (png == null) return false;
            // aus dem gepackten Vorrat: winziges Bild, in Bruchteilen einer Millisekunde entpackt
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.StreamSource = new MemoryStream(png); bi.EndInit(); bi.Freeze();
                bs = bi;
                long t; lock (cache) cacheZeit.TryGetValue(k, out t);
                Einlagern(k, bs, t);
                return true;
            }
            catch { return false; }
        }

        /// <summary>Entpackt merken; was zu viel wird, wandert gepackt (PNG, ≈ 5–10× kleiner) in den kalten Vorrat – nicht weg.</summary>
        static void Einlagern(string k, BitmapSource b, long t)
        {
            var raus = new List<KeyValuePair<string, BitmapSource>>();
            lock (cache)
            {
                if (cache.ContainsKey(k)) Vergessen(k);
                cache[k] = b; cacheZeit[k] = t; warmBytes += Bytes(b);
                cacheKnoten[k] = cacheReihe.AddLast(k);
                while ((warmBytes > WarmMax || cache.Count > CacheMax) && cacheReihe.First != null)
                {
                    string alt = cacheReihe.First.Value;
                    BitmapSource ab; cache.TryGetValue(alt, out ab);
                    if (ab != null) raus.Add(new KeyValuePair<string, BitmapSource>(alt, ab));
                    long tz; cacheZeit.TryGetValue(alt, out tz);
                    Vergessen(alt);
                    cacheZeit[alt] = tz;   // Zeit für den kalten Vorrat behalten
                }
            }
            if (raus.Count > 0) Auftrag(() => { foreach (var kv in raus) Packen(kv.Key, kv.Value); });   // packen nie auf der Oberfläche
        }

        static void Packen(string k, BitmapSource b)
        {
            try
            {
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(b));
                byte[] png;
                using (var ms = new MemoryStream()) { enc.Save(ms); png = ms.ToArray(); }
                lock (cache)
                {
                    if (cache.ContainsKey(k) || kalt.ContainsKey(k)) return;
                    kalt[k] = png; kaltReihe.AddLast(k); kaltBytes += png.Length;
                    while (kaltBytes > KaltMax && kaltReihe.First != null)
                    {
                        string alt = kaltReihe.First.Value; kaltReihe.RemoveFirst();
                        byte[] ap; if (kalt.TryGetValue(alt, out ap)) { kaltBytes -= ap.Length; kalt.Remove(alt); cacheZeit.Remove(alt); }
                    }
                }
            }
            catch { }
        }

        /// <summary>Fenster länger versteckt: entpackte Bilder freigeben (der gepackte Vorrat bleibt – beim Öffnen sofort wieder da).</summary>
        public static void Ruhen()
        {
            var raus = new List<KeyValuePair<string, BitmapSource>>();
            lock (cache)
            {
                foreach (var kv in cache) if (kv.Value != null) raus.Add(kv);
                foreach (var kv in raus) { long tz; cacheZeit.TryGetValue(kv.Key, out tz); Vergessen(kv.Key); cacheZeit[kv.Key] = tz; }
            }
            if (raus.Count > 0) Auftrag(() => { foreach (var kv in raus) Packen(kv.Key, kv.Value); });
            lock (grau) grau.Clear();
        }

        /// <summary>Still im Hintergrund: hat sich die Datei seit dem Laden geändert, wird das Bild beim nächsten Mal neu geholt.</summary>
        static void Nachpruefen(string k, string pfad)
        {
            if (Sofort) return;
            Auftrag(() =>
            {
                long t = 0;
                try { t = File.GetLastWriteTimeUtc(pfad).Ticks; } catch { }
                lock (cache) { long alt; if (cacheZeit.TryGetValue(k, out alt) && alt != t) { Vergessen(k); byte[] p; if (kalt.TryGetValue(k, out p)) { kalt.Remove(k); kaltReihe.Remove(k); kaltBytes -= p.Length; } } }
            });
        }

        static void Vergessen(string k)
        {
            BitmapSource b;
            if (cache.TryGetValue(k, out b)) warmBytes -= Bytes(b);
            cache.Remove(k); cacheZeit.Remove(k);
            LinkedListNode<string> n;
            if (cacheKnoten.TryGetValue(k, out n)) { cacheReihe.Remove(n); cacheKnoten.Remove(k); }
        }

        public static void Laden(string pfad, int px, bool nurSymbol, Action<BitmapSource> fertig)
        {
            string k = Schluessel(pfad, px) + (nurSymbol ? "|s" : "");
            BitmapSource bs;
            if (AusCache(k, out bs)) { fertig(bs); Nachpruefen(k, pfad); return; }
            if (Sofort) { bs = Native.Vorschau(pfad, px, nurSymbol); Merken(k, bs, pfad); fertig(bs); return; }
            Auftrag(() =>
            {
                BitmapSource b = null;
                try { b = Native.Vorschau(pfad, px, nurSymbol); } catch { }
                Merken(k, b, pfad);
                Fertig(() => fertig(b));
            });
        }

        /// <summary>Merken mit Verdrängen des am längsten nicht Gesehenen (statt bei 400 alles wegzuwerfen und neu zu laden).</summary>
        static void Merken(string k, BitmapSource b, string pfad)
        {
            long t = 0;
            if (pfad != null) try { t = File.GetLastWriteTimeUtc(pfad).Ticks; } catch { }
            Einlagern(k, b, t);
        }

        static readonly object startSperre = new object();
        static void Starten()
        {
            lock (startSperre)
            {
                if (arbeiter != null) return;
                arbeiter = new Thread[2];    // zwei leise Helfer: Vorschaubilder kommen doppelt so schnell, ohne die Oberfläche zu berühren
                for (int i = 0; i < arbeiter.Length; i++)
                {
                    arbeiter[i] = new Thread(() =>
                    {
                        while (true)
                        {
                            signal.Wait();
                            Action a;
                            if (auftraege.TryPop(out a)) { try { a(); } catch { } }
                        }
                    });
                    arbeiter[i].IsBackground = true;
                    arbeiter[i].SetApartmentState(ApartmentState.STA);
                    arbeiter[i].Priority = ThreadPriority.BelowNormal;
                    arbeiter[i].Start();
                }
            }
        }

        /// <summary>Bild einer Datei: erst ein Platzhalter in Endform, dann blendet das echte Vorschaubild weich ein.</summary>
        public static FrameworkElement Bild(string pfad, double breite, double hoehe) { return Bild(pfad, breite, hoehe, false); }

        public static FrameworkElement Bild(string pfad, double breite, double hoehe, bool nurSymbol) { return Bild(pfad, breite, hoehe, nurSymbol, null, false); }

        /// <summary>
        /// anteil = wie viel Zeit noch übrig ist (0…1): das Symbol „leert“ sich von oben. Oben liegt das unveränderte Original,
        /// nur unten herum abgeschnitten; darunter eine graue Kopie desselben Symbols. Funktioniert für jeden Dateityp.
        /// </summary>
        public static FrameworkElement Bild(string pfad, double breite, double hoehe, bool nurSymbol, double? anteil, bool bald, bool? ordner = null)
        {
            var g = new Grid { Width = breite, Height = hoehe };
            if (ordner ?? Directory.Exists(pfad))
            {
                // Ordner: unser Ordnerbild als Bitmap behandeln, damit auch er sich leeren kann
                if (anteil.HasValue) { g.Children.Add(Fuellstand(OrdnerBitmap((int)(breite * 2)), breite, hoehe, anteil.Value, bald)); return g; }
                var o = UI.OrdnerBild(breite);
                o.VerticalAlignment = VerticalAlignment.Center;
                g.Children.Add(o);
                return g;
            }
            int px = (int)Math.Ceiling(Math.Max(breite, hoehe) * Skala);
            // schon geladen? Dann sofort zeigen – kein Platzhalter, kein Überblenden, nichts springt.
            BitmapSource fertigesBild;
            string fk = Schluessel(pfad, px) + (nurSymbol ? "|s" : "");
            AusCache(fk, out fertigesBild);
            if (fertigesBild != null)
            {
                Nachpruefen(fk, pfad);
                g.Children.Add(anteil.HasValue ? Fuellstand(fertigesBild, breite, hoehe, anteil.Value, bald) : Einfach(fertigesBild, breite, hoehe));
                return g;
            }
            // noch nicht: ruhige Fläche in Endgröße, dann blendet das echte Bild weich ein
            var platz = new Border { Width = breite * 0.72, Height = hoehe * 0.8, CornerRadius = new CornerRadius(8), Background = Farbe.B("#12FFFFFF"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(platz);
            Laden(pfad, px, nurSymbol, bs =>
            {
                if (bs == null) return;
                var img = anteil.HasValue ? Fuellstand(bs, breite, hoehe, anteil.Value, bald) : Einfach(bs, breite, hoehe);
                g.Children.Add(img);
                if (Sofort) { g.Children.Remove(platz); return; }
                img.Opacity = 0;
                var a = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180));
                a.Completed += delegate { g.Children.Remove(platz); };
                img.BeginAnimation(UIElement.OpacityProperty, a);
            });
            return g;
        }

        /// <summary>
        /// Für Recent & Co.: das echte Vorschaubild (erste Seite, Foto, Video-Standbild), wenn Windows eines hat – sonst
        /// Workbench' Typ-Marke (farbiges Blatt mit Endung) statt des Symbols der öffnenden App. So sieht nichts gleich aus.
        /// </summary>
        public static FrameworkElement Echt(string pfad, double groesse, bool? ordner = null)
        {
            var g = new Grid { Width = groesse, Height = groesse };
            if (ordner ?? Directory.Exists(pfad)) { var o = UI.OrdnerBild(groesse); o.VerticalAlignment = VerticalAlignment.Center; g.Children.Add(o); return g; }
            var marke = Glyphe(pfad, Math.Round(groesse * 0.78), groesse * 0.94, false, false);
            marke.HorizontalAlignment = HorizontalAlignment.Center; marke.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(marke);
            int px = (int)Math.Ceiling(groesse * Skala);
            string k = Schluessel(pfad, px) + "|t";
            Action<BitmapSource> setzen = bs =>
            {
                if (bs == null) return;
                g.Children.Clear();
                var rahmen = new Border { CornerRadius = new CornerRadius(Math.Max(4, groesse / 7)), ClipToBounds = true, Background = new ImageBrush(bs) { Stretch = Stretch.UniformToFill }, BorderBrush = Farbe.B("#26FFFFFF"), BorderThickness = new Thickness(1) };
                double s = Math.Min(groesse / bs.PixelWidth, groesse / bs.PixelHeight);
                rahmen.Width = Math.Max(groesse * 0.6, bs.PixelWidth * s); rahmen.Height = Math.Max(groesse * 0.6, bs.PixelHeight * s);
                rahmen.HorizontalAlignment = HorizontalAlignment.Center; rahmen.VerticalAlignment = VerticalAlignment.Center;
                g.Children.Add(rahmen);
            };
            BitmapSource fertig;
            if (AusCache(k, out fertig)) { setzen(fertig); Nachpruefen(k, pfad); return g; }
            if (Sofort) { var b0 = Native.Vorschau(pfad, px, 0x8); if (b0 == null && pfad.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) b0 = PdfVorschau.Seite(pfad, px); Merken(k, b0, pfad); setzen(b0); return g; }
            Auftrag(() =>
            {
                BitmapSource b = null;
                try { b = Native.Vorschau(pfad, px, 0x8); } catch { }
                if (b == null && pfad.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) b = PdfVorschau.Seite(pfad, px);
                Merken(k, b, pfad);
                if (b != null) Fertig(() => setzen(b));
            });
            return g;
        }

        static readonly string[] bildEndungen = { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".heic", ".tif", ".tiff", ".mp4", ".mov", ".mkv", ".webm", ".avi" };

        /// <summary>
        /// Workbench-eigenes Datei-Zeichen für die Karte: ruhiges Dokument mit Eselsohr in der Farbe des Typs und der Endung –
        /// statt des Symbols der App, mit der die Datei geöffnet wird. Bilder und Videos zeigen ihr echtes Vorschaubild.
        /// </summary>
        public static FrameworkElement Glyphe(string pfad, double b, double h) { return Glyphe(pfad, b, h, false); }

        /// <summary>nurMarke: auch bei Bildern kein Vorschaubild laden (Cloud: nie etwas herunterladen; kleine Symbole: schnell).</summary>
        public static FrameworkElement Glyphe(string pfad, double b, double h, bool nurMarke, bool? ordner = null)
        {
            var g = new Grid { Width = b, Height = h };
            if (ordner ?? Directory.Exists(pfad))
            {
                var o = UI.OrdnerBild(b);
                o.VerticalAlignment = VerticalAlignment.Center;
                g.Children.Add(o);
                return g;
            }
            string ext = Path.GetExtension(pfad).ToLowerInvariant();
            if (!nurMarke && Array.IndexOf(bildEndungen, ext) >= 0)
            {
                var rahmen = new Border { Width = b, Height = b, CornerRadius = new CornerRadius(10), Background = Farbe.B("#1AFFFFFF"), VerticalAlignment = VerticalAlignment.Center, BorderBrush = Farbe.B("#26FFFFFF"), BorderThickness = new Thickness(1) };
                g.Children.Add(rahmen);
                Laden(pfad, (int)Math.Ceiling(b * Skala), false, bs => { if (bs != null) rahmen.Background = new ImageBrush(bs) { Stretch = Stretch.UniformToFill }; });
                return g;
            }
            var form = Form(b, h);
            g.Children.Add(new System.Windows.Shapes.Path { Data = form[0], Fill = Farbe.Endung(ext) });
            g.Children.Add(new System.Windows.Shapes.Path { Data = form[0], Fill = glanz });
            g.Children.Add(new System.Windows.Shapes.Path { Data = form[1], Fill = ohrFarbe });
            string e = ext.TrimStart('.').ToUpperInvariant();
            if (e.Length > 4) e = e.Substring(0, 4);
            if (e.Length == 0) e = "FILE";
            g.Children.Add(new TextBlock { Text = e, FontSize = Math.Max(8, b * 0.21), FontWeight = FontWeights.Bold, Foreground = Farbe.B("#C01C1405"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, h * 0.12) });
            return g;
        }

        // Blatt-Form je Größe nur einmal berechnen (eingefroren, von allen Symbolen geteilt) – sonst wird sie für jedes Symbol neu aus Text gelesen
        static readonly Dictionary<string, Geometry[]> formen = new Dictionary<string, Geometry[]>();
        static readonly Brush glanz = Frieren(new LinearGradientBrush(Color.FromArgb(40, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90));
        static readonly Brush ohrFarbe = Frieren(new SolidColorBrush(Color.FromArgb(0x73, 255, 255, 255)));
        static Brush Frieren(Brush x) { x.Freeze(); return x; }

        static Geometry[] Form(double b, double h)
        {
            string k = b.ToString("0.#") + "|" + h.ToString("0.#");
            Geometry[] f;
            if (formen.TryGetValue(k, out f)) return f;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            double r = 6, c = Math.Round(b * 0.34);
            string koerper = string.Format(ci, "M{0},0 H{1} L{2},{3} V{4} A{0},{0} 0 0 1 {5},{6} H{0} A{0},{0} 0 0 1 0,{4} V{0} A{0},{0} 0 0 1 {0},0 Z", r, b - c, b, c, h - r, b - r, h);
            string ohr = string.Format(ci, "M{0},0 V{1} A3,3 0 0 0 {2},{3} H{4} Z", b - c, c - 3, b - c + 3, c, b);
            f = new[] { Geometry.Parse(koerper), Geometry.Parse(ohr) };
            f[0].Freeze(); f[1].Freeze();
            formen[k] = f;
            return f;
        }

        static Image Einfach(BitmapSource bs, double b, double h)
        {
            var img = new Image { Source = bs, Stretch = Stretch.Uniform, Width = b, Height = h };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            return img;
        }

        class Grauwert { public BitmapSource Bild; public double Oben, Unten; }   // Oben/Unten: sichtbarer Teil (0…1 der Höhe)
        static readonly Dictionary<string, Grauwert> grau = new Dictionary<string, Grauwert>();

        /// <summary>Graue Kopie (Transparenz bleibt) und wo das Symbol wirklich anfängt/aufhört – einmal je Bild, gemerkt.</summary>
        static Grauwert Grau(BitmapSource bs, bool bald)
        {
            string k = bs.GetHashCode() + (bald ? "|b" : "");
            Grauwert gw;
            lock (grau) if (grau.TryGetValue(k, out gw)) return gw;
            var src = bs.Format == PixelFormats.Pbgra32 ? bs : new FormatConvertedBitmap(bs, PixelFormats.Pbgra32, null, 0);
            int w = src.PixelWidth, h = src.PixelHeight, stride = w * 4;
            var px = new byte[stride * h];
            src.CopyPixels(px, stride, 0);
            int oben = h, unten = -1;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * stride + x * 4;
                    byte a = px[i + 3];
                    if (a > 24) { if (y < oben) oben = y; if (y > unten) unten = y; }
                    // geleert = nur noch die Form, eine ruhige einfarbige Fläche – kein Inhalt (kein schwarzer Text, keine Bildreste)
                    double fa = a / 255.0;
                    double r = 70 * fa, gg = 75 * fa, bb = 86 * fa;          // vormultipliziert
                    if (bald)
                    {
                        // letzter Tag: geleerter Teil bekommt einen Hauch Bernstein
                        r = r * 0.6 + 244 * fa * 0.22; gg = gg * 0.6 + 184 * fa * 0.22; bb = bb * 0.6 + 96 * fa * 0.22;
                    }
                    px[i + 2] = (byte)Math.Max(0, Math.Min(a, r));
                    px[i + 1] = (byte)Math.Max(0, Math.Min(a, gg));
                    px[i] = (byte)Math.Max(0, Math.Min(a, bb));
                }
            }
            var neu = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, px, stride);
            neu.Freeze();
            gw = new Grauwert { Bild = neu, Oben = unten < 0 ? 0 : oben / (double)h, Unten = unten < 0 ? 1 : (unten + 1) / (double)h };
            lock (grau) { if (grau.Count > 300) grau.Clear(); grau[k] = gw; }
            return gw;
        }

        /// <summary>Graue Kopie unten, Original oben – das Original nur unterhalb des Füllstands sichtbar.</summary>
        static FrameworkElement Fuellstand(BitmapSource bs, double b, double h, double anteil, bool bald)
        {
            var gw = Grau(bs, bald);
            var g = new Grid { Width = b, Height = h };
            g.Children.Add(Einfach(gw.Bild, b, h));
            var farbig = Einfach(bs, b, h);
            // tatsächliche Bildfläche bei Stretch=Uniform
            double s = Math.Min(b / bs.PixelWidth, h / bs.PixelHeight), bh = bs.PixelHeight * s, top = (h - bh) / 2;
            double y0 = top + bh * gw.Oben, y1 = top + bh * gw.Unten;
            double a = Math.Max(0.05, Math.Min(1, anteil));
            double grenze = y1 - (y1 - y0) * a;
            farbig.Clip = new RectangleGeometry(new Rect(0, grenze, b, h - grenze));
            g.Children.Add(farbig);
            return g;
        }

        static BitmapSource ordnerBmp;
        static BitmapSource OrdnerBitmap(int px)
        {
            if (ordnerBmp != null) return ordnerBmp;
            var o = UI.OrdnerBild(px);
            o.Measure(new Size(px, px)); o.Arrange(new Rect(0, 0, px, px));
            var rtb = new RenderTargetBitmap(px, px, 96, 96, PixelFormats.Pbgra32);
            var rahmen = new Grid { Width = px, Height = px };
            rahmen.Children.Add(o);
            o.VerticalAlignment = VerticalAlignment.Center;
            rahmen.Measure(new Size(px, px)); rahmen.Arrange(new Rect(0, 0, px, px));
            rtb.Render(rahmen);
            rtb.Freeze();
            ordnerBmp = rtb;
            return rtb;
        }
    }

    /// <summary>Kleiner Monatskalender. Ein Klick auf einen Tag oder eine Schnellwahl genügt.</summary>
    class Kalender : StackPanel
    {
        public event Action<DateTime> Gewaehlt;
        DateTime monat;
        readonly UniformGrid tage = new UniformGrid { Columns = 7 };
        readonly TextBlock titel = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Farbe.Text, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        static readonly string[] monate = { "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" };

        public Kalender()
        {
            var heute = DateTime.Today;
            monat = new DateTime(heute.Year, heute.Month, 1);

            var schnell = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            int bisSonntag = ((int)DayOfWeek.Sunday - (int)heute.DayOfWeek + 7) % 7;
            if (bisSonntag == 0) bisSonntag = 7;
            schnell.Children.Add(Schnell("Sunday", heute.AddDays(bisSonntag)));
            schnell.Children.Add(Schnell("End of month", new DateTime(heute.Year, heute.Month, 1).AddMonths(1).AddDays(-1)));
            schnell.Children.Add(Schnell("End of year", new DateTime(heute.Year, 12, 31)));
            Children.Add(schnell);

            var kopf = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            var zur = UI.IconKnopf(Ikon.Links, "Previous month", Farbe.Text2);
            zur.Style = (Style)Application.Current.FindResource("BtnFlach"); zur.Width = 36; zur.Padding = new Thickness(0);
            zur.HorizontalAlignment = HorizontalAlignment.Left;
            zur.Click += delegate { if (monat > new DateTime(heute.Year, heute.Month, 1)) { monat = monat.AddMonths(-1); Zeichnen(); } };
            var vor = UI.IconKnopf(Ikon.Rechts, "Next month", Farbe.Text2);
            vor.Style = zur.Style; vor.Width = 36; vor.Padding = new Thickness(0);
            vor.HorizontalAlignment = HorizontalAlignment.Right;
            vor.Click += delegate { monat = monat.AddMonths(1); Zeichnen(); };
            kopf.Children.Add(zur); kopf.Children.Add(titel); kopf.Children.Add(vor);
            Children.Add(kopf);

            var wt = new UniformGrid { Columns = 7, Margin = new Thickness(0, 2, 0, 2) };
            foreach (var t in new[] { "Mo", "Tu", "We", "Th", "Fr", "Sa", "Su" })
                wt.Children.Add(new TextBlock { Text = t, FontSize = 11.5, Foreground = Farbe.Text3, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 4) });
            Children.Add(wt);
            Children.Add(tage);
            Zeichnen();
        }

        Button Schnell(string text, DateTime tag)
        {
            var b = new Button { Style = (Style)Application.Current.FindResource("Btn"), Content = text, Height = 32, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 0, 6, 6), FontSize = 12.5 };
            b.ToolTip = tag.ToString("dddd, d MMMM", Dauer.En);
            b.Click += delegate { if (Gewaehlt != null) Gewaehlt(tag); };
            return b;
        }

        void Zeichnen()
        {
            titel.Text = monate[monat.Month - 1] + " " + monat.Year;
            tage.Children.Clear();
            int versatz = ((int)monat.DayOfWeek + 6) % 7;      // Montag zuerst
            for (int i = 0; i < versatz; i++) tage.Children.Add(new Border());
            int n = DateTime.DaysInMonth(monat.Year, monat.Month);
            var heute = DateTime.Today;
            for (int d = 1; d <= n; d++)
            {
                var tag = new DateTime(monat.Year, monat.Month, d);
                var b = new Button { Style = (Style)Application.Current.FindResource("Tag"), Content = d.ToString(), Background = Brushes.Transparent, Margin = new Thickness(1) };
                if (tag < heute) b.IsEnabled = false;
                if (tag == heute) { b.Foreground = Farbe.Akzent; b.FontWeight = FontWeights.SemiBold; }
                var t2 = tag;
                b.Click += delegate { if (Gewaehlt != null) Gewaehlt(t2); };
                tage.Children.Add(b);
            }
            int rest = 42 - (versatz + n);
            for (int i = 0; i < rest; i++) tage.Children.Add(new Border { Height = 38 });   // immer 6 Zeilen: Kalender springt nicht in der Höhe
        }
    }
}
