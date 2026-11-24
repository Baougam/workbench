using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Workbench
{
    /// <summary>Gemerkte Netz-Positionen: das Netz sieht beim nächsten Öffnen genauso aus.</summary>
    static class NetzOrte
    {
        static Dictionary<string, double[]> orte;
        static DispatcherTimer speichern;
        static string Datei { get { return System.IO.Path.Combine(Paths.Daten, "netz.json"); } }

        public static Dictionary<string, double[]> Alle
        {
            get
            {
                if (orte != null) return orte;
                orte = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    if (File.Exists(Datei))
                    {
                        var d = Store.Json().Deserialize<Dictionary<string, double[]>>(File.ReadAllText(Datei));
                        if (d != null) foreach (var kv in d) if (kv.Value != null && kv.Value.Length == 2) orte[Paths.PfadNeu(kv.Key)] = kv.Value;
                    }
                }
                catch (Exception ex) { Log.Fehler("Netz laden", ex); }
                return orte;
            }
        }

        public static void Merken()
        {
            if (Vorschau.Sofort) return;
            if (speichern == null)
            {
                speichern = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                speichern.Tick += delegate
                {
                    speichern.Stop();
                    try { Directory.CreateDirectory(Paths.Daten); File.WriteAllText(Datei, Store.Json().Serialize(Alle)); }
                    catch (Exception ex) { Log.Fehler("Netz speichern", ex); }
                };
            }
            speichern.Stop();
            speichern.Start();
        }
    }

    /// <summary>
    /// Die Ansicht von oben. Die Anordnung wird vorab im Hintergrund berechnet (und gemerkt) – das Netz erscheint fertig und
    /// blendet weich ein, statt sichtbar herumzurutschen. Nur beim Ziehen eines Punktes federn die Nachbarn kurz nach.
    /// </summary>
    class Netz : Grid
    {
        class Knoten
        {
            public string Name, Pfad;
            public int Art;                 // 0 Wurzel, 1 Ordner, 2 Datei, 3 Notiz
            public bool Wolke, Neu;
            public double X, Y, VX, VY, R;
            public string Farbe;
            public Knoten Eltern, Wurzel;
            public Shape Form;
            public Ellipse Schein;
            public TextBlock Schild;
            public bool SchildImmer;
            public readonly List<Knoten> Nachbarn = new List<Knoten>();
        }
        class Kante { public Knoten A, B; public bool Eigen; public string Farbe; public Line Strich; }

        readonly List<Knoten> knoten = new List<Knoten>();
        readonly List<Kante> kanten = new List<Kante>();
        readonly Canvas welt = new Canvas();
        readonly ScaleTransform zoom = new ScaleTransform(1, 1);
        readonly TranslateTransform pan = new TranslateTransform();
        readonly Action<string, bool> oeffnen;
        readonly bool sofort;
        bool laeuft, eingepasst;
        double alpha;                       // „Wärme“ der Federung – kühlt ab, dann steht alles
        Point? panStart, klickStart;
        Point panVon;
        Knoten zieht;

        public Netz(List<CloudOrt> wurzeln, Action<string, bool> oeffnen)
        {
            this.oeffnen = oeffnen;
            sofort = Vorschau.Sofort;
            Background = Brushes.Transparent;
            ClipToBounds = true;
            var tg = new TransformGroup();
            tg.Children.Add(zoom);
            tg.Children.Add(pan);
            welt.RenderTransform = tg;
            welt.Opacity = 0;
            Children.Add(welt);
            Children.Add(Suchfeld());

            MouseWheel += Zoomen;
            MouseLeftButtonDown += (s, a) => { if (a.OriginalSource == this) { panStart = a.GetPosition(this); panVon = new Point(pan.X, pan.Y); CaptureMouse(); } };
            MouseMove += (s, a) =>
            {
                if (zieht != null)
                {
                    var p = Welt(a.GetPosition(this));
                    zieht.X = p.X; zieht.Y = p.Y; zieht.VX = zieht.VY = 0;
                    Anwerfen(0.35);
                    return;
                }
                if (panStart == null) return;
                var q = a.GetPosition(this);
                pan.BeginAnimation(TranslateTransform.XProperty, null);
                pan.BeginAnimation(TranslateTransform.YProperty, null);
                pan.X = panVon.X + q.X - panStart.Value.X;
                pan.Y = panVon.Y + q.Y - panStart.Value.Y;
            };
            MouseLeftButtonUp += delegate
            {
                panStart = null;
                if (zieht != null) { zieht = null; OrteMerken(); }
                ReleaseMouseCapture();
            };
            Unloaded += delegate { Anhalten(); };
            SizeChanged += delegate { if (!eingepasst && knoten.Count > 0) Einpassen(false); };

            var orte = NetzOrte.Alle;
            if (sofort) { var r = Sammeln(wurzeln); Vorrechnen(r, orte); Aufbauen(r); return; }
            // Lesen und Anordnen im Hintergrund – die Oberfläche bleibt frei, das Netz erscheint fertig
            Task.Factory.StartNew(() => { var r = Sammeln(wurzeln); Vorrechnen(r, orte); return r; })
                .ContinueWith(t => { if (t.Exception == null) Aufbauen(t.Result); }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        Point Welt(Point bild) { return new Point((bild.X - pan.X) / zoom.ScaleX, (bild.Y - pan.Y) / zoom.ScaleY); }

        // ---------- Daten ----------

        class Roh { public List<Knoten> K = new List<Knoten>(); public List<Kante> E = new List<Kante>(); }

        static Roh Sammeln(List<CloudOrt> wurzeln)
        {
            var r = new Roh();
            var nachPfad = new Dictionary<string, Knoten>(StringComparer.OrdinalIgnoreCase);
            int gesamt = 0;
            foreach (var w in wurzeln)
            {
                if (w == null || !Directory.Exists(w.Pfad)) continue;
                bool wolke = w.Name != "Permanent";
                var wk = new Knoten { Name = w.Name, Pfad = w.Pfad.TrimEnd('\\'), Art = 0, Wolke = wolke, R = 9 };
                wk.Wurzel = wk;
                r.K.Add(wk); nachPfad[wk.Pfad] = wk;
                var schlange = new Queue<KeyValuePair<Knoten, int>>();
                schlange.Enqueue(new KeyValuePair<Knoten, int>(wk, 0));
                int tiefe = wolke ? 2 : 4;
                while (schlange.Count > 0 && gesamt < 280)
                {
                    var kv = schlange.Dequeue();
                    string[] unter;
                    try { unter = Directory.GetDirectories(kv.Key.Pfad); } catch { continue; }
                    if (kv.Key.Art != 0) kv.Key.R = 3.5 + Math.Min(6, Math.Sqrt(Anzahl(kv.Key.Pfad)) * 1.3);
                    if (kv.Value >= tiefe) continue;
                    foreach (var u in unter.OrderBy(x => x))
                    {
                        if (Engine.IstHilfsdatei(u) || gesamt >= 280) continue;
                        var k = new Knoten { Name = System.IO.Path.GetFileName(u), Pfad = u.TrimEnd('\\'), Art = 1, Wolke = wolke, Eltern = kv.Key, Wurzel = wk, R = 4 };
                        r.K.Add(k); nachPfad[k.Pfad] = k; gesamt++;
                        r.E.Add(new Kante { A = kv.Key, B = k });
                        schlange.Enqueue(new KeyValuePair<Knoten, int>(k, kv.Value + 1));
                    }
                }
            }
            foreach (var kv in Anordnungen.Alle)
            {
                Knoten ordner;
                if (!nachPfad.TryGetValue(kv.Key.TrimEnd('\\'), out ordner)) continue;
                var A = kv.Value;
                Func<string, Knoten> finde = name =>
                {
                    Knoten k;
                    if (name.StartsWith(Anordnungen.NotizPraefix))
                    {
                        string schl = ordner.Pfad + "|" + name;
                        if (nachPfad.TryGetValue(schl, out k)) return k;
                        var n = A.Notizen.FirstOrDefault(x => x.Id == name.Substring(1));
                        if (n == null) return null;
                        k = new Knoten { Name = Kurz(n.Text), Pfad = schl, Art = 3, Wolke = ordner.Wolke, Eltern = ordner, Wurzel = ordner.Wurzel, R = 3.5, Farbe = n.Farbe };
                    }
                    else
                    {
                        string p = System.IO.Path.Combine(ordner.Pfad, name).TrimEnd('\\');
                        if (nachPfad.TryGetValue(p, out k)) return k;
                        if (!File.Exists(p) && !Directory.Exists(p)) return null;
                        k = new Knoten { Name = name, Pfad = p, Art = Directory.Exists(p) ? 1 : 2, Wolke = ordner.Wolke, Eltern = ordner, Wurzel = ordner.Wurzel, R = 3 };
                    }
                    r.K.Add(k); nachPfad[k.Pfad] = k;
                    r.E.Add(new Kante { A = ordner, B = k });
                    return k;
                };
                foreach (var pl in A.Plaetze) if (!string.IsNullOrEmpty(pl.Value.Farbe)) { var k = finde(pl.Key); if (k != null) k.Farbe = pl.Value.Farbe; }
                foreach (var n in A.Notizen) finde(Anordnungen.NotizPraefix + n.Id);
                foreach (var l in A.Linien)
                {
                    var a = finde(l.A); var b = finde(l.B);
                    if (a != null && b != null) r.E.Add(new Kante { A = a, B = b, Eigen = true, Farbe = l.Farbe });
                }
            }
            return r;
        }

        static int Anzahl(string pfad) { try { return Directory.EnumerateFileSystemEntries(pfad).Take(200).Count(); } catch { return 0; } }

        static string Kurz(string t)
        {
            t = (t ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            return t.Length > 40 ? t.Substring(0, 40) + "…" : t;
        }

        /// <summary>Anordnung vorab: bekannte Punkte an ihren gemerkten Platz, neue neben ihre Eltern, dann unsichtbar einpendeln.</summary>
        static void Vorrechnen(Roh r, Dictionary<string, double[]> orte)
        {
            var zufall = new Random(7);
            int wi = 0, neu = 0;
            foreach (var k in r.K.Where(x => x.Art == 0))
            {
                double[] o;
                if (orte.TryGetValue(k.Pfad, out o)) { k.X = o[0]; k.Y = o[1]; } else { k.X = (wi - 0.5) * 460; k.Y = 0; k.Neu = true; neu++; }
                wi++;
            }
            foreach (var k in r.K.Where(x => x.Art != 0))
            {
                double[] o;
                if (orte.TryGetValue(k.Pfad, out o)) { k.X = o[0]; k.Y = o[1]; continue; }
                var e = k.Eltern ?? k.Wurzel;
                double w = zufall.NextDouble() * Math.PI * 2;
                k.X = e.X + Math.Cos(w) * 30; k.Y = e.Y + Math.Sin(w) * 30;
                k.Neu = true; neu++;
            }
            foreach (var e in r.E) { e.A.Nachbarn.Add(e.B); e.B.Nachbarn.Add(e.A); }
            // alles neu: lang einpendeln; nur ein paar neue: kurz und nur die neuen bewegen sich nennenswert
            bool allesNeu = neu > r.K.Count / 2;
            int runden = allesNeu ? 420 : (neu > 0 ? 160 : 0);
            for (int i = 0; i < runden; i++)
            {
                double a = allesNeu ? Math.Max(0.02, 1 - i / (double)runden) : 0.5 * (1 - i / (double)runden);
                Schritt(r.K, r.E, a, !allesNeu, null);
            }
            foreach (var k in r.K) { k.VX = k.VY = 0; k.Neu = false; }
        }

        /// <summary>Ein Rechenschritt: abstoßen, Federn, sanft zur eigenen Wurzel. alpha = Stärke (kühlt ab).</summary>
        static double Schritt(List<Knoten> knoten, List<Kante> kanten, double alpha, bool nurNeueStark, Knoten fest)
        {
            int n = knoten.Count;
            for (int i = 0; i < n; i++)
            {
                var a = knoten[i];
                for (int j = i + 1; j < n; j++)
                {
                    var b = knoten[j];
                    double dx = a.X - b.X, dy = a.Y - b.Y, d2 = dx * dx + dy * dy + 0.01;
                    if (d2 > 62500) continue;
                    double f = 900 / d2 * alpha, d = Math.Sqrt(d2);
                    double fx = dx / d * f, fy = dy / d * f;
                    a.VX += fx; a.VY += fy; b.VX -= fx; b.VY -= fy;
                }
            }
            foreach (var e in kanten)
            {
                double ruhe = e.Eigen ? 90 : 30 + (e.A.R + e.B.R) * 1.6, k = (e.Eigen ? 0.025 : 0.06) * alpha;
                double dx = e.B.X - e.A.X, dy = e.B.Y - e.A.Y, d = Math.Sqrt(dx * dx + dy * dy) + 0.01;
                double f = (d - ruhe) * k, fx = dx / d * f, fy = dy / d * f;
                e.A.VX += fx; e.A.VY += fy; e.B.VX -= fx; e.B.VY -= fy;
            }
            double max = 0;
            foreach (var k in knoten)
            {
                if (k.Art == 0) { k.VX += -k.X * 0.003 * alpha; k.VY += -k.Y * 0.005 * alpha; }
                else { k.VX += (k.Wurzel.X - k.X) * 0.0012 * alpha; k.VY += (k.Wurzel.Y - k.Y) * 0.0012 * alpha; }
                if (k == fest) { k.VX = k.VY = 0; continue; }
                double bremse = nurNeueStark && !k.Neu ? 0.2 : 0.62;
                k.VX *= bremse; k.VY *= bremse;
                if (Math.Abs(k.VX) > 10) k.VX = Math.Sign(k.VX) * 10;
                if (Math.Abs(k.VY) > 10) k.VY = Math.Sign(k.VY) * 10;
                k.X += k.VX; k.Y += k.VY;
                max = Math.Max(max, Math.Abs(k.VX) + Math.Abs(k.VY));
            }
            return max;
        }

        // ---------- Aufbau ----------

        void Aufbauen(Roh r)
        {
            knoten.AddRange(r.K);
            kanten.AddRange(r.E);
            foreach (var e in kanten)
            {
                e.Strich = new Line { Stroke = e.Eigen ? Tisch.Pinsel(e.Farbe, 0.8) : Farbe.B("#1CFFFFFF"), StrokeThickness = e.Eigen ? 1.5 : 0.8, IsHitTestVisible = false, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
                welt.Children.Add(e.Strich);
            }
            foreach (var k in knoten)
            {
                string hex = !string.IsNullOrEmpty(k.Farbe) ? k.Farbe : k.Art == 0 ? "#F4B860" : null;
                if (hex != null || k.Art == 0)
                {
                    // sanftes Leuchten: statischer Verlauf, kein Effekt
                    var c = (Color)ColorConverter.ConvertFromString(hex);
                    var g = new RadialGradientBrush(Color.FromArgb(k.Art == 0 ? (byte)70 : (byte)55, c.R, c.G, c.B), Color.FromArgb(0, c.R, c.G, c.B));
                    g.Freeze();
                    k.Schein = new Ellipse { Width = k.R * 7, Height = k.R * 7, Fill = g, IsHitTestVisible = false };
                    welt.Children.Add(k.Schein);
                }
            }
            foreach (var k in knoten)
            {
                Brush fuellung = !string.IsNullOrEmpty(k.Farbe) ? Farbe.B(k.Farbe) : k.Art == 0 ? Farbe.B("#FFF6EAD4") : k.Art == 1 ? Farbe.B("#D9E9ECF2") : k.Art == 3 ? Farbe.B("#B3F4B860") : Farbe.B("#80E9ECF2");
                Shape s = k.Art == 3 ? (Shape)new Rectangle { RadiusX = 1.5, RadiusY = 1.5 } : new Ellipse();
                s.Width = s.Height = k.R * 2;
                s.Fill = fuellung;
                s.Cursor = Cursors.Hand;
                s.ToolTip = k.Art == 3 ? "Note: " + k.Name : k.Name;
                ToolTipService.SetInitialShowDelay(s, 350);
                k.Form = s;
                var k2 = k;
                s.MouseEnter += delegate { Hervorheben(k2); };
                s.MouseLeave += delegate { Hervorheben(null); };
                s.MouseLeftButtonDown += (o, a) => { a.Handled = true; zieht = k2; klickStart = a.GetPosition(this); CaptureMouse(); };
                s.MouseLeftButtonUp += (o, a) =>
                {
                    var p = a.GetPosition(this);
                    bool bewegt = klickStart.HasValue && (Math.Abs(p.X - klickStart.Value.X) > 4 || Math.Abs(p.Y - klickStart.Value.Y) > 4);
                    zieht = null; klickStart = null; ReleaseMouseCapture();
                    if (!bewegt) Oeffnen(k2); else OrteMerken();
                    a.Handled = true;
                };
                welt.Children.Add(s);
                if (k.Art <= 1)
                {
                    k.SchildImmer = k.Art == 0 || k.R >= 6.5 || !string.IsNullOrEmpty(k.Farbe);
                    k.Schild = new TextBlock { Text = k.Name, FontSize = k.Art == 0 ? 13 : 11, FontWeight = k.Art == 0 ? FontWeights.SemiBold : FontWeights.Normal, Foreground = Farbe.B(k.Art == 0 ? "#EBFFFFFF" : "#99FFFFFF"), IsHitTestVisible = false };
                    welt.Children.Add(k.Schild);
                }
            }
            Zeichnen();
            Einpassen(false);
            SchilderNachZoom();
            OrteMerken();
            if (sofort) { welt.Opacity = 1; return; }
            // weich erscheinen
            welt.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(380)) { EasingFunction = UI.Weich });
        }

        Point Mitte() { return new Point(ActualWidth / 2, ActualHeight / 2); }

        void Oeffnen(Knoten k)
        {
            if (k.Art == 0 || k.Art == 1) oeffnen(k.Pfad, k.Wolke);
            else if (k.Eltern != null) oeffnen(k.Eltern.Pfad, k.Wolke);
        }

        void OrteMerken()
        {
            var o = NetzOrte.Alle;
            foreach (var k in knoten) o[k.Pfad] = new[] { Math.Round(k.X, 1), Math.Round(k.Y, 1) };
            NetzOrte.Merken();
        }

        /// <summary>Nachbarn hervorheben, Rest weich zurücknehmen.</summary>
        void Hervorheben(Knoten k)
        {
            if (k == null && SucheAktiv) { Markieren(false); return; }   // Suche bleibt sichtbar
            var dauer = TimeSpan.FromMilliseconds(160);
            foreach (var x in knoten)
            {
                bool an = k == null || x == k || k.Nachbarn.Contains(x);
                x.Form.BeginAnimation(OpacityProperty, new DoubleAnimation(an ? 1 : 0.16, dauer));
                if (x.Schein != null) x.Schein.BeginAnimation(OpacityProperty, new DoubleAnimation(an ? 1 : 0.1, dauer));
                if (x.Schild != null)
                {
                    bool zeigen = an && (x.SchildImmer || zoom.ScaleX >= 1.1 || (k != null && (x == k || k.Nachbarn.Contains(x))));
                    x.Schild.BeginAnimation(OpacityProperty, new DoubleAnimation(zeigen ? 1 : (k == null && !x.SchildImmer && zoom.ScaleX < 1.1 ? 0 : 0.12), dauer));
                }
            }
            foreach (var e in kanten) e.Strich.BeginAnimation(OpacityProperty, new DoubleAnimation(k == null || e.A == k || e.B == k ? 1 : 0.1, dauer));
        }

        void SchilderNachZoom()
        {
            foreach (var x in knoten)
                if (x.Schild != null && !x.SchildImmer) x.Schild.BeginAnimation(OpacityProperty, new DoubleAnimation(zoom.ScaleX >= 1.1 ? 1 : 0, TimeSpan.FromMilliseconds(180)));
        }

        // ---------- Suche oben links ----------
        // Tippen = passende Punkte (mit Weg zur Wurzel) hervorheben · Enter = zum nächsten Treffer fliegen ·
        // Umschalt+Enter = tief in den Ordnern suchen, auch was das Netz nicht zeigt · Esc = leeren

        TextBox suchText;
        TextBlock suchZahl;
        Border suchRahmen;
        List<Knoten> treffer = new List<Knoten>();
        int trefferNr = -1;
        readonly HashSet<string> tiefGesucht = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public bool SucheAktiv { get { return suchText != null && suchText.Text.Length > 0; } }

        FrameworkElement Suchfeld()
        {
            suchRahmen = new Border { Width = 250, Height = 32, CornerRadius = new CornerRadius(16), Background = Farbe.B("#B3121418"), BorderBrush = Farbe.B("#1AFFFFFF"), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(14, 12, 0, 0), Cursor = Cursors.IBeam };
            var g = new Grid();
            var lupe = UI.Icon(Ikon.Suche, 14, Farbe.B("#80FFFFFF"), 2);
            lupe.HorizontalAlignment = HorizontalAlignment.Left;
            lupe.Margin = new Thickness(11, 0, 0, 0);
            g.Children.Add(lupe);
            suchText = new TextBox { Style = (Style)Application.Current.FindResource("Suchfeld"), Margin = new Thickness(32, 0, 36, 0), ToolTip = "Enter: next match · Shift+Enter: search inside folders" };
            System.Windows.Automation.AutomationProperties.SetName(suchText, "Search graph");
            g.Children.Add(suchText);
            suchZahl = new TextBlock { FontSize = 11, Foreground = Farbe.B("#80FFFFFF"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            g.Children.Add(suchZahl);
            suchRahmen.Child = g;
            suchRahmen.MouseLeftButtonDown += (s, a) => { suchText.Focus(); a.Handled = true; };
            suchText.TextChanged += delegate { tiefGesucht.Clear(); Markieren(true); };
            suchText.PreviewKeyDown += (s, a) =>
            {
                if (a.Key != Key.Enter) return;
                a.Handled = true;
                if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) TiefSuchen(suchText.Text.Trim());
                else Naechster();
            };
            return suchRahmen;
        }

        public void SucheFokus(string anfang)
        {
            if (suchText == null) return;
            if (!string.IsNullOrEmpty(anfang)) { suchText.Text += anfang; suchText.CaretIndex = suchText.Text.Length; }
            suchText.Focus();
            Keyboard.Focus(suchText);
        }

        public void SucheLeeren() { suchText.Text = ""; Keyboard.ClearFocus(); }

        /// <summary>Treffer hervorheben – samt Weg bis zur Wurzel, damit man sieht, wo sie liegen.</summary>
        void Markieren(bool sprung)
        {
            string t = suchText.Text.Trim();
            treffer = t.Length == 0 ? new List<Knoten>() : knoten.Where(k => k.Art != 0 && k.Name.IndexOf(t, StringComparison.CurrentCultureIgnoreCase) >= 0).ToList();
            trefferNr = -1;
            suchZahl.Text = t.Length == 0 ? "" : treffer.Count.ToString();
            var dauer = TimeSpan.FromMilliseconds(160);
            if (t.Length == 0)
            {
                foreach (var k in knoten) { k.Form.Stroke = null; k.Form.StrokeThickness = 0; }
                Hervorheben(null);
                return;
            }
            var weg = new HashSet<Knoten>();
            foreach (var k in treffer) { var x = k; while (x != null) { weg.Add(x); x = x.Eltern ?? (x.Art == 0 ? null : x.Wurzel); if (x != null && x.Art == 0) { weg.Add(x); break; } } }
            foreach (var k in knoten)
            {
                bool hit = treffer.Contains(k), an = weg.Contains(k);
                k.Form.Stroke = hit ? Farbe.Akzent : null;
                k.Form.StrokeThickness = hit ? 2 : 0;
                k.Form.BeginAnimation(OpacityProperty, new DoubleAnimation(an ? 1 : 0.12, dauer));
                if (k.Schein != null) k.Schein.BeginAnimation(OpacityProperty, new DoubleAnimation(an ? 1 : 0.08, dauer));
                if (k.Schild != null) k.Schild.BeginAnimation(OpacityProperty, new DoubleAnimation(an ? 1 : 0.06, dauer));
            }
            foreach (var e in kanten) e.Strich.BeginAnimation(OpacityProperty, new DoubleAnimation(weg.Contains(e.A) && weg.Contains(e.B) ? 1 : 0.06, dauer));
        }

        void Naechster()
        {
            if (treffer.Count == 0) { if (suchText.Text.Trim().Length > 0) TiefSuchen(suchText.Text.Trim()); return; }
            trefferNr = (trefferNr + 1) % treffer.Count;
            var k = treffer[trefferNr];
            suchZahl.Text = (trefferNr + 1) + "/" + treffer.Count;
            FliegeZu(k);
        }

        void FliegeZu(Knoten k)
        {
            double z = Math.Max(zielZoom, 1.4);
            // Ziel so wählen, dass der Punkt in die Mitte kommt
            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
            var d = TimeSpan.FromMilliseconds(380);
            double px = ActualWidth / 2 - k.X * z, py = ActualHeight / 2 - k.Y * z;
            zielZoom = z;
            var az = new DoubleAnimation(zoom.ScaleX, z, d) { EasingFunction = ease };
            zoom.BeginAnimation(ScaleTransform.ScaleXProperty, az);
            zoom.BeginAnimation(ScaleTransform.ScaleYProperty, az);
            var ax = new DoubleAnimation(pan.X, px, d) { EasingFunction = ease };
            ax.Completed += delegate
            {
                zoom.BeginAnimation(ScaleTransform.ScaleXProperty, null); zoom.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                zoom.ScaleX = zoom.ScaleY = z;
                pan.BeginAnimation(TranslateTransform.XProperty, null); pan.BeginAnimation(TranslateTransform.YProperty, null);
                pan.X = px; pan.Y = py;
            };
            pan.BeginAnimation(TranslateTransform.XProperty, ax);
            pan.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(pan.Y, py, d) { EasingFunction = ease });
            // kurz pulsieren
            var puls = new DoubleAnimation(1, 0.35, TimeSpan.FromMilliseconds(260)) { AutoReverse = true, BeginTime = TimeSpan.FromMilliseconds(380), RepeatBehavior = new RepeatBehavior(2) };
            k.Form.BeginAnimation(OpacityProperty, puls);
        }

        /// <summary>Umschalt+Enter: in allen Ordnern suchen (auch tiefer als das Netz zeigt). Treffer kommen als neue Punkte dazu.</summary>
        void TiefSuchen(string t)
        {
            if (t.Length == 0 || tiefGesucht.Contains(t)) { Naechster(); return; }
            tiefGesucht.Add(t);
            suchZahl.Text = "…";
            var wurzeln = knoten.Where(k => k.Art == 0).Select(k => new KeyValuePair<string, bool>(k.Pfad, k.Wolke)).ToList();
            var bekannt = new HashSet<string>(knoten.Select(k => k.Pfad), StringComparer.OrdinalIgnoreCase);
            Task.Factory.StartNew(() =>
            {
                var gefunden = new List<string>();
                foreach (var w in wurzeln) Durchsuchen(w.Key, t, gefunden, 0, w.Value ? 6 : 12);
                return gefunden.Where(p => !bekannt.Contains(p)).Take(60).ToList();
            }).ContinueWith(r =>
            {
                if (r.Exception != null || suchText.Text.Trim() != t) return;
                var nachPfad = knoten.ToDictionary(k => k.Pfad, k => k, StringComparer.OrdinalIgnoreCase);
                var zufall = new Random();
                foreach (var p in r.Result)
                {
                    // nächster sichtbarer Vorfahr
                    string v = System.IO.Path.GetDirectoryName(p);
                    Knoten eltern = null;
                    while (v != null && !nachPfad.TryGetValue(v.TrimEnd('\\'), out eltern)) v = System.IO.Path.GetDirectoryName(v);
                    if (eltern == null) continue;
                    double w = zufall.NextDouble() * Math.PI * 2;
                    var k = new Knoten { Name = System.IO.Path.GetFileName(p), Pfad = p, Art = Directory.Exists(p) ? 1 : 2, Wolke = eltern.Wolke, Eltern = eltern, Wurzel = eltern.Wurzel, R = 3.5, X = eltern.X + Math.Cos(w) * 34, Y = eltern.Y + Math.Sin(w) * 34, Neu = true };
                    var e = new Kante { A = eltern, B = k };
                    k.Nachbarn.Add(eltern); eltern.Nachbarn.Add(k);
                    knoten.Add(k); kanten.Add(e); nachPfad[p] = k;
                    e.Strich = new Line { Stroke = Farbe.B("#40F4B860"), StrokeThickness = 0.9, StrokeDashArray = new DoubleCollection { 2, 3 }, IsHitTestVisible = false };
                    welt.Children.Insert(0, e.Strich);
                    var s = new Ellipse { Width = k.R * 2, Height = k.R * 2, Fill = Farbe.B("#D9F4B860"), Cursor = Cursors.Hand, ToolTip = p };
                    k.Form = s;
                    var k2 = k;
                    s.MouseLeftButtonUp += (o, a) => { a.Handled = true; Oeffnen(k2); };
                    s.MouseEnter += delegate { Hervorheben(k2); };
                    s.MouseLeave += delegate { Markieren(false); };
                    welt.Children.Add(s);
                    k.Schild = new TextBlock { Text = k.Name, FontSize = 11, Foreground = Farbe.B("#E6F4B860"), IsHitTestVisible = false };
                    k.SchildImmer = true;
                    welt.Children.Add(k.Schild);
                }
                Zeichnen();
                Markieren(false);
                Anwerfen(0.3);
                if (treffer.Count > 0) Naechster(); else suchZahl.Text = "0";
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        static void Durchsuchen(string ordner, string t, List<string> gefunden, int tiefe, int max)
        {
            if (gefunden.Count >= 200 || tiefe > max) return;
            try
            {
                foreach (var e in Directory.EnumerateFileSystemEntries(ordner))
                {
                    string n = System.IO.Path.GetFileName(e);
                    if (Engine.IstHilfsdatei(e)) continue;
                    if (n.IndexOf(t, StringComparison.CurrentCultureIgnoreCase) >= 0) gefunden.Add(e.TrimEnd('\\'));
                    if (gefunden.Count >= 200) return;
                }
                foreach (var d in Directory.EnumerateDirectories(ordner)) Durchsuchen(d, t, gefunden, tiefe + 1, max);
            }
            catch { }
        }

        // ---------- Federn beim Ziehen ----------

        void Anwerfen(double a)
        {
            alpha = Math.Max(alpha, a);
            if (laeuft || sofort) return;
            laeuft = true;
            CompositionTarget.Rendering += Bild;
        }

        void Anhalten()
        {
            if (!laeuft) return;
            laeuft = false;
            CompositionTarget.Rendering -= Bild;
        }

        void Bild(object s, EventArgs e)
        {
            double bewegung = Schritt(knoten, kanten, alpha, false, zieht);
            Zeichnen();
            if (zieht == null) alpha *= 0.92;           // kühlt ab, sobald losgelassen
            if (zieht == null && (alpha < 0.02 || bewegung < 0.03)) { Anhalten(); OrteMerken(); }
        }

        void Zeichnen()
        {
            foreach (var e in kanten) { e.Strich.X1 = e.A.X; e.Strich.Y1 = e.A.Y; e.Strich.X2 = e.B.X; e.Strich.Y2 = e.B.Y; }
            foreach (var k in knoten)
            {
                Canvas.SetLeft(k.Form, k.X - k.R); Canvas.SetTop(k.Form, k.Y - k.R);
                if (k.Schein != null) { Canvas.SetLeft(k.Schein, k.X - k.R * 3.5); Canvas.SetTop(k.Schein, k.Y - k.R * 3.5); }
                if (k.Schild != null) { Canvas.SetLeft(k.Schild, k.X + k.R + 6); Canvas.SetTop(k.Schild, k.Y - 8); }
            }
        }

        // ---------- Zoom ----------

        void Einpassen(bool weich)
        {
            if (knoten.Count == 0 || ActualWidth <= 0) return;
            eingepasst = true;
            double x0 = knoten.Min(k => k.X) - 40, x1 = knoten.Max(k => k.X) + 140, y0 = knoten.Min(k => k.Y) - 40, y1 = knoten.Max(k => k.Y) + 40;
            double s = Math.Min(1.5, Math.Min(ActualWidth / (x1 - x0), ActualHeight / (y1 - y0)));
            zoom.ScaleX = zoom.ScaleY = s;
            zielZoom = s;
            pan.X = ActualWidth / 2 - (x0 + x1) / 2 * s;
            pan.Y = ActualHeight / 2 - (y0 + y1) / 2 * s;
        }

        void Zoomen(object s, MouseWheelEventArgs a)
        {
            double ziel = Math.Max(0.3, Math.Min(4, zielZoom * (a.Delta > 0 ? 1.18 : 1 / 1.18)));
            ZoomAuf(zoom.ScaleX, ziel, 200, a.GetPosition(this));
            a.Handled = true;
        }

        double zielZoom = 1;

        /// <summary>Gleitender Zoom um einen Punkt.</summary>
        void ZoomAuf(double von, double nach, int ms, Point um)
        {
            var w = Welt(um);
            zielZoom = nach;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var d = TimeSpan.FromMilliseconds(ms);
            double panX = um.X - w.X * nach, panY = um.Y - w.Y * nach;
            if (sofort) { zoom.ScaleX = zoom.ScaleY = nach; pan.X = panX; pan.Y = panY; return; }
            var az = new DoubleAnimation(von, nach, d) { EasingFunction = ease };
            zoom.BeginAnimation(ScaleTransform.ScaleXProperty, az);
            zoom.BeginAnimation(ScaleTransform.ScaleYProperty, az);
            var ax = new DoubleAnimation(pan.X, panX, d) { EasingFunction = ease };
            var ay = new DoubleAnimation(pan.Y, panY, d) { EasingFunction = ease };
            ax.Completed += delegate
            {
                // Animation auflösen, Endwerte festschreiben (damit Ziehen/Pannen danach frei sind)
                zoom.BeginAnimation(ScaleTransform.ScaleXProperty, null); zoom.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                zoom.ScaleX = zoom.ScaleY = nach;
                pan.BeginAnimation(TranslateTransform.XProperty, null); pan.BeginAnimation(TranslateTransform.YProperty, null);
                pan.X = panX; pan.Y = panY;
                SchilderNachZoom();
            };
            pan.BeginAnimation(TranslateTransform.XProperty, ax);
            pan.BeginAnimation(TranslateTransform.YProperty, ay);
        }
    }
}
