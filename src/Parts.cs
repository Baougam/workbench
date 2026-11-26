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
        static readonly BlockingCollection<Action> auftraege = new BlockingCollection<Action>();
        static readonly Dictionary<string, BitmapSource> cache = new Dictionary<string, BitmapSource>();
        static Thread arbeiter;

        static string Schluessel(string pfad, int px)
        {
            long t = 0;
            try { t = File.GetLastWriteTimeUtc(pfad).Ticks; } catch { }
            return pfad.ToLowerInvariant() + "|" + px + "|" + t;
        }

        public static void Laden(string pfad, int px, bool nurSymbol, Action<BitmapSource> fertig)
        {
            string k = Schluessel(pfad, px) + (nurSymbol ? "|s" : "");
            BitmapSource bs;
            lock (cache) { if (cache.TryGetValue(k, out bs)) { fertig(bs); return; } }
            if (Sofort) { bs = Native.Vorschau(pfad, px, nurSymbol); Merken(k, bs); fertig(bs); return; }
            Starten();
            var disp = Application.Current.Dispatcher;
            auftraege.Add(() =>
            {
                BitmapSource b = null;
                try { b = Native.Vorschau(pfad, px, nurSymbol); } catch { }
                Merken(k, b);
                disp.BeginInvoke(new Action(() => fertig(b)));
            });
        }

        static void Merken(string k, BitmapSource b)
        {
            lock (cache) { if (cache.Count > 400) cache.Clear(); cache[k] = b; }
        }

        static void Starten()
        {
            if (arbeiter != null) return;
            arbeiter = new Thread(() => { foreach (var a in auftraege.GetConsumingEnumerable()) a(); });
            arbeiter.IsBackground = true;
            arbeiter.SetApartmentState(ApartmentState.STA);
            arbeiter.Priority = ThreadPriority.BelowNormal;
            arbeiter.Start();
        }

        /// <summary>Bild einer Datei: erst ein Platzhalter in Endform, dann blendet das echte Vorschaubild weich ein.</summary>
        public static FrameworkElement Bild(string pfad, double breite, double hoehe) { return Bild(pfad, breite, hoehe, false); }

        public static FrameworkElement Bild(string pfad, double breite, double hoehe, bool nurSymbol) { return Bild(pfad, breite, hoehe, nurSymbol, null, false); }

        /// <summary>
        /// anteil = wie viel Zeit noch übrig ist (0…1): das Symbol „leert“ sich von oben. Oben liegt das unveränderte Original,
        /// nur unten herum abgeschnitten; darunter eine graue Kopie desselben Symbols. Funktioniert für jeden Dateityp.
        /// </summary>
        public static FrameworkElement Bild(string pfad, double breite, double hoehe, bool nurSymbol, double? anteil, bool bald)
        {
            var g = new Grid { Width = breite, Height = hoehe };
            if (Directory.Exists(pfad))
            {
                // Ordner: unser Ordnerbild als Bitmap behandeln, damit auch er sich leeren kann
                if (anteil.HasValue) { g.Children.Add(Fuellstand(OrdnerBitmap((int)(breite * 2)), breite, hoehe, anteil.Value, bald)); return g; }
                var o = UI.OrdnerBild(breite);
                o.VerticalAlignment = VerticalAlignment.Center;
                g.Children.Add(o);
                return g;
            }
            int px = (int)Math.Ceiling(Math.Max(breite, hoehe) * 2);
            // schon geladen? Dann sofort zeigen – kein Platzhalter, kein Überblenden, nichts springt.
            BitmapSource fertigesBild;
            lock (cache) cache.TryGetValue(Schluessel(pfad, px) + (nurSymbol ? "|s" : ""), out fertigesBild);
            if (fertigesBild != null)
            {
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
        public static FrameworkElement Echt(string pfad, double groesse)
        {
            var g = new Grid { Width = groesse, Height = groesse };
            if (Directory.Exists(pfad)) { var o = UI.OrdnerBild(groesse); o.VerticalAlignment = VerticalAlignment.Center; g.Children.Add(o); return g; }
            var marke = Glyphe(pfad, Math.Round(groesse * 0.78), groesse * 0.94);
            marke.HorizontalAlignment = HorizontalAlignment.Center; marke.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(marke);
            int px = (int)Math.Ceiling(groesse * 2);
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
            bool da;
            lock (cache) da = cache.TryGetValue(k, out fertig);
            if (da) { setzen(fertig); return g; }
            if (Sofort) { var b0 = Native.Vorschau(pfad, px, 0x8); Merken(k, b0); setzen(b0); return g; }
            Starten();
            var disp = Application.Current.Dispatcher;
            auftraege.Add(() =>
            {
                BitmapSource b = null;
                try { b = Native.Vorschau(pfad, px, 0x8); } catch { }
                Merken(k, b);
                if (b != null) disp.BeginInvoke(new Action(() => setzen(b)));
            });
            return g;
        }

        static readonly string[] bildEndungen = { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".heic", ".tif", ".tiff", ".mp4", ".mov", ".mkv", ".webm", ".avi" };

        /// <summary>
        /// Workbench-eigenes Datei-Zeichen für die Karte: ruhiges Dokument mit Eselsohr in der Farbe des Typs und der Endung –
        /// statt des Symbols der App, mit der die Datei geöffnet wird. Bilder und Videos zeigen ihr echtes Vorschaubild.
        /// </summary>
        public static FrameworkElement Glyphe(string pfad, double b, double h)
        {
            var g = new Grid { Width = b, Height = h };
            if (Directory.Exists(pfad))
            {
                var o = UI.OrdnerBild(b);
                o.VerticalAlignment = VerticalAlignment.Center;
                g.Children.Add(o);
                return g;
            }
            string ext = Path.GetExtension(pfad).ToLowerInvariant();
            if (Array.IndexOf(bildEndungen, ext) >= 0)
            {
                var rahmen = new Border { Width = b, Height = b, CornerRadius = new CornerRadius(10), Background = Farbe.B("#1AFFFFFF"), VerticalAlignment = VerticalAlignment.Center, BorderBrush = Farbe.B("#26FFFFFF"), BorderThickness = new Thickness(1) };
                g.Children.Add(rahmen);
                Laden(pfad, (int)(b * 2), false, bs => { if (bs != null) rahmen.Background = new ImageBrush(bs) { Stretch = Stretch.UniformToFill }; });
                return g;
            }
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            double r = 6, c = Math.Round(b * 0.34);
            string koerper = string.Format(ci, "M{0},0 H{1} L{2},{3} V{4} A{0},{0} 0 0 1 {5},{6} H{0} A{0},{0} 0 0 1 0,{4} V{0} A{0},{0} 0 0 1 {0},0 Z", r, b - c, b, c, h - r, b - r, h);
            string ohr = string.Format(ci, "M{0},0 V{1} A3,3 0 0 0 {2},{3} H{4} Z", b - c, c - 3, b - c + 3, c, b);
            g.Children.Add(new System.Windows.Shapes.Path { Data = Geometry.Parse(koerper), Fill = Farbe.Endung(ext) });
            g.Children.Add(new System.Windows.Shapes.Path { Data = Geometry.Parse(koerper), Fill = new LinearGradientBrush(Color.FromArgb(40, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90) });
            g.Children.Add(new System.Windows.Shapes.Path { Data = Geometry.Parse(ohr), Fill = Farbe.B("#73FFFFFF") });
            string e = ext.TrimStart('.').ToUpperInvariant();
            if (e.Length > 4) e = e.Substring(0, 4);
            if (e.Length == 0) e = "FILE";
            g.Children.Add(new TextBlock { Text = e, FontSize = Math.Max(8, b * 0.21), FontWeight = FontWeights.Bold, Foreground = Farbe.B("#C01C1405"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, h * 0.12) });
            return g;
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
                    double l = px[i + 2] * 0.3 + px[i + 1] * 0.59 + px[i] * 0.11;   // vormultipliziert – bleibt stimmig
                    l = l * 0.55;                                                    // geleert = gedämpft
                    double r = l, gg = l, bb = l;
                    if (bald)
                    {
                        // letzter Tag: geleerter Teil bekommt einen Hauch Bernstein
                        double fa = a / 255.0;
                        r = l * 0.62 + 244 * fa * 0.38; gg = l * 0.62 + 184 * fa * 0.38; bb = l * 0.62 + 96 * fa * 0.38;
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
