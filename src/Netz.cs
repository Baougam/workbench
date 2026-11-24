using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Workbench
{
    /// <summary>
    /// Die Ansicht von oben: Ordner als Punkte, „liegt in“ als zarte Linie, eigene Verbindungen farbig und kräftiger,
    /// Notizen als kleine Quadrate. Die Anordnung pendelt sich ein paar Sekunden ein und steht dann still – keine Dauerlast.
    /// Darüberfahren hebt die Nachbarn hervor, Klick auf einen Ordner öffnet ihn.
    /// </summary>
    class Netz : Grid
    {
        class Knoten
        {
            public string Name, Pfad;
            public int Art;                 // 0 Wurzel, 1 Ordner, 2 Datei, 3 Notiz
            public bool Wolke, Fest;
            public double X, Y, VX, VY, R;
            public string Farbe;
            public Knoten Eltern, Wurzel;
            public Shape Form;
            public TextBlock Schild;
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
        int schritte;
        bool laeuft;
        Point? panStart;
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
            Children.Add(welt);

            MouseWheel += Zoomen;
            MouseLeftButtonDown += (s, a) => { if (a.OriginalSource == this) { panStart = a.GetPosition(this); panVon = new Point(pan.X, pan.Y); CaptureMouse(); } };
            MouseMove += (s, a) =>
            {
                if (zieht != null)
                {
                    var p = Welt(a.GetPosition(this));
                    zieht.X = p.X; zieht.Y = p.Y; zieht.VX = zieht.VY = 0;
                    Anwerfen(40);
                    return;
                }
                if (panStart == null) return;
                var q = a.GetPosition(this);
                pan.X = panVon.X + q.X - panStart.Value.X;
                pan.Y = panVon.Y + q.Y - panStart.Value.Y;
            };
            MouseLeftButtonUp += delegate { panStart = null; if (zieht != null) { zieht = null; } ReleaseMouseCapture(); };
            Unloaded += delegate { Anhalten(); };

            if (sofort) { Aufbauen(Sammeln(wurzeln)); return; }
            // Ordner lesen kann bei Cloud-Laufwerken dauern – im Hintergrund, die Oberfläche bleibt frei
            Task.Factory.StartNew(() => Sammeln(wurzeln)).ContinueWith(t => { if (t.Exception == null) Aufbauen(t.Result); }, TaskScheduler.FromCurrentSynchronizationContext());
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
                bool wolke = w.Name != "Dauerhaft";
                var wk = new Knoten { Name = w.Name, Pfad = w.Pfad.TrimEnd('\\'), Art = 0, Wolke = wolke, R = 11 };
                wk.Wurzel = wk;
                r.K.Add(wk); nachPfad[wk.Pfad] = wk;
                var schlange = new Queue<KeyValuePair<Knoten, int>>();
                schlange.Enqueue(new KeyValuePair<Knoten, int>(wk, 0));
                int tiefe = wolke ? 2 : 4;          // Cloud nicht zu tief lesen
                while (schlange.Count > 0 && gesamt < 280)
                {
                    var kv = schlange.Dequeue();
                    string[] unter;
                    try { unter = Directory.GetDirectories(kv.Key.Pfad); } catch { continue; }
                    kv.Key.R = kv.Key.Art == 0 ? 11 : 4 + Math.Min(7, Math.Sqrt(Anzahl(kv.Key.Pfad)) * 1.5);
                    if (kv.Value >= tiefe) continue;
                    foreach (var u in unter.OrderBy(x => x))
                    {
                        if (Engine.IstHilfsdatei(u) || gesamt >= 280) continue;
                        var k = new Knoten { Name = System.IO.Path.GetFileName(u), Pfad = u.TrimEnd('\\'), Art = 1, Wolke = wolke, Eltern = kv.Key, Wurzel = wk, R = 5 };
                        r.K.Add(k); nachPfad[k.Pfad] = k; gesamt++;
                        r.E.Add(new Kante { A = kv.Key, B = k });
                        schlange.Enqueue(new KeyValuePair<Knoten, int>(k, kv.Value + 1));
                    }
                }
            }
            // eigene Verbindungen, Farben und Notizen aus den Anordnungen
            foreach (var kv in Anordnungen.Alle)
            {
                Knoten ordner;
                if (!nachPfad.TryGetValue(kv.Key.TrimEnd('\\'), out ordner)) continue;
                var A = kv.Value;
                Func<string, Knoten> finde = name =>
                {
                    if (name.StartsWith(Anordnungen.NotizPraefix))
                    {
                        string id = name.Substring(1);
                        Knoten nk;
                        if (nachPfad.TryGetValue(ordner.Pfad + "|" + name, out nk)) return nk;
                        var n = A.Notizen.FirstOrDefault(x => x.Id == id);
                        if (n == null) return null;
                        nk = new Knoten { Name = Kurz(n.Text), Pfad = ordner.Pfad + "|" + name, Art = 3, Wolke = ordner.Wolke, Eltern = ordner, Wurzel = ordner.Wurzel, R = 4.5, Farbe = n.Farbe };
                        r.K.Add(nk); nachPfad[nk.Pfad] = nk;
                        r.E.Add(new Kante { A = ordner, B = nk });
                        return nk;
                    }
                    string p = System.IO.Path.Combine(ordner.Pfad, name).TrimEnd('\\');
                    Knoten k;
                    if (nachPfad.TryGetValue(p, out k)) return k;
                    if (!File.Exists(p) && !Directory.Exists(p)) return null;
                    k = new Knoten { Name = name, Pfad = p, Art = Directory.Exists(p) ? 1 : 2, Wolke = ordner.Wolke, Eltern = ordner, Wurzel = ordner.Wurzel, R = 3.5 };
                    r.K.Add(k); nachPfad[p] = k;
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

        static int Anzahl(string pfad)
        {
            try { return Directory.EnumerateFileSystemEntries(pfad).Take(200).Count(); } catch { return 0; }
        }

        static string Kurz(string t)
        {
            t = (t ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            return t.Length > 40 ? t.Substring(0, 40) + "…" : t;
        }

        // ---------- Aufbau ----------

        void Aufbauen(Roh r)
        {
            knoten.AddRange(r.K);
            kanten.AddRange(r.E);
            var zufall = new Random(7);
            int wi = 0;
            foreach (var k in knoten.Where(x => x.Art == 0)) { k.X = (wi++ - 0.5) * 420; k.Y = 0; }
            foreach (var k in knoten.Where(x => x.Art != 0))
            {
                var e = k.Eltern ?? k.Wurzel;
                double w = zufall.NextDouble() * Math.PI * 2;
                k.X = e.X + Math.Cos(w) * 40; k.Y = e.Y + Math.Sin(w) * 40;
            }
            foreach (var e in kanten) { e.A.Nachbarn.Add(e.B); e.B.Nachbarn.Add(e.A); }

            foreach (var e in kanten)
            {
                e.Strich = new Line { Stroke = e.Eigen ? Tisch.Pinsel(e.Farbe, 0.85) : Farbe.B("#26FFFFFF"), StrokeThickness = e.Eigen ? 1.8 : 1, IsHitTestVisible = false, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
                welt.Children.Add(e.Strich);
            }
            foreach (var k in knoten)
            {
                Brush fuellung = !string.IsNullOrEmpty(k.Farbe) ? Farbe.B(k.Farbe) : k.Art == 0 ? Farbe.B("#F2FFFFFF") : k.Art == 1 ? Farbe.B("#BFFFFFFF") : k.Art == 3 ? Farbe.B("#99F4B860") : Farbe.B("#73FFFFFF");
                Shape s = k.Art == 3 ? (Shape)new Rectangle { RadiusX = 2, RadiusY = 2 } : new Ellipse();
                s.Width = s.Height = k.R * 2;
                s.Fill = fuellung;
                if (k.Art == 0) { s.Stroke = Farbe.B("#59F4B860"); s.StrokeThickness = 4; }
                s.Cursor = Cursors.Hand;
                s.ToolTip = k.Art == 3 ? "Notiz: " + k.Name : k.Name;
                ToolTipService.SetInitialShowDelay(s, 300);
                k.Form = s;
                var k2 = k;
                s.MouseEnter += delegate { Hervorheben(k2); };
                s.MouseLeave += delegate { Hervorheben(null); };
                s.MouseLeftButtonDown += (o, a) =>
                {
                    a.Handled = true;
                    if (a.ClickCount == 2 || k2.Art == 0 || true)
                    {
                        // ein Klick genügt: hinein. Gedrückt halten und ziehen = verschieben.
                        zieht = k2; k2.Fest = true; CaptureMouse();
                        klickStart = a.GetPosition(this);
                    }
                };
                s.MouseLeftButtonUp += (o, a) =>
                {
                    bool bewegt = klickStart.HasValue && (Math.Abs(a.GetPosition(this).X - klickStart.Value.X) > 4 || Math.Abs(a.GetPosition(this).Y - klickStart.Value.Y) > 4);
                    zieht = null; klickStart = null; ReleaseMouseCapture();
                    if (!bewegt) Oeffnen(k2);
                    a.Handled = true;
                };
                welt.Children.Add(s);
                if (k.Art <= 1)
                {
                    k.Schild = new TextBlock { Text = k.Name, FontSize = k.Art == 0 ? 12.5 : 11, FontWeight = k.Art == 0 ? FontWeights.SemiBold : FontWeights.Normal, Foreground = Farbe.B(k.Art == 0 ? "#E6FFFFFF" : "#A6FFFFFF"), IsHitTestVisible = false };
                    if (k.Art == 1 && k.R < 6 && k.Nachbarn.Count < 3) k.Schild.Visibility = Visibility.Collapsed;   // kleine nur beim Darüberfahren
                    welt.Children.Add(k.Schild);
                }
            }
            SizeChanged += delegate { if (!laeuft) Einpassen(); };
            if (sofort) { for (int i = 0; i < 400; i++) Schritt(); Zeichnen(); Einpassen(); }
            else { Zeichnen(); Einpassen(); Anwerfen(300); }
        }

        Point? klickStart;

        void Oeffnen(Knoten k)
        {
            if (k.Art == 0 || k.Art == 1) oeffnen(k.Pfad, k.Wolke);
            else if (k.Eltern != null) oeffnen(k.Eltern.Pfad, k.Wolke);
        }

        void Hervorheben(Knoten k)
        {
            foreach (var x in knoten)
            {
                bool an = k == null || x == k || k.Nachbarn.Contains(x);
                x.Form.Opacity = an ? 1 : 0.18;
                if (x.Schild != null)
                {
                    x.Schild.Opacity = an ? 1 : 0.15;
                    if (x.Art == 1 && x.R < 6 && x.Nachbarn.Count < 3) x.Schild.Visibility = k != null && an ? Visibility.Visible : Visibility.Collapsed;
                }
            }
            foreach (var e in kanten) e.Strich.Opacity = k == null || e.A == k || e.B == k ? 1 : 0.12;
        }

        // ---------- Kräfte ----------

        void Anwerfen(int n)
        {
            schritte = Math.Max(schritte, n);
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

        bool erstesEinpassen = true;
        void Bild(object s, EventArgs e)
        {
            double bewegung = Schritt() + Schritt();
            Zeichnen();
            if (erstesEinpassen && schritte < 200) { Einpassen(); }
            schritte -= 2;
            if (schritte <= 0 || bewegung < 0.02)
            {
                Anhalten();
                if (erstesEinpassen) { Einpassen(); erstesEinpassen = false; }
            }
        }

        /// <summary>Ein Rechenschritt: abstoßen, Federn an den Linien, sanft zur eigenen Wurzel. Gibt die größte Bewegung zurück.</summary>
        double Schritt()
        {
            int n = knoten.Count;
            for (int i = 0; i < n; i++)
            {
                var a = knoten[i];
                for (int j = i + 1; j < n; j++)
                {
                    var b = knoten[j];
                    double dx = a.X - b.X, dy = a.Y - b.Y, d2 = dx * dx + dy * dy + 0.01;
                    if (d2 > 90000) continue;
                    double f = 700 / d2, d = Math.Sqrt(d2);
                    double fx = dx / d * f, fy = dy / d * f;
                    a.VX += fx; a.VY += fy; b.VX -= fx; b.VY -= fy;
                }
            }
            foreach (var e in kanten)
            {
                double ruhe = e.Eigen ? 80 : 34 + e.A.R + e.B.R, k = e.Eigen ? 0.02 : 0.05;
                double dx = e.B.X - e.A.X, dy = e.B.Y - e.A.Y, d = Math.Sqrt(dx * dx + dy * dy) + 0.01;
                double f = (d - ruhe) * k;
                double fx = dx / d * f, fy = dy / d * f;
                e.A.VX += fx; e.A.VY += fy; e.B.VX -= fx; e.B.VY -= fy;
            }
            double max = 0;
            foreach (var k in knoten)
            {
                if (k.Art == 0) { k.VX += -k.X * 0.004; k.VY += -k.Y * 0.006; }
                else { k.VX += (k.Wurzel.X - k.X) * 0.0015; k.VY += (k.Wurzel.Y - k.Y) * 0.0015; }
                if (k == zieht) { k.VX = k.VY = 0; continue; }
                k.VX *= 0.78; k.VY *= 0.78;
                if (Math.Abs(k.VX) > 12) k.VX = Math.Sign(k.VX) * 12;
                if (Math.Abs(k.VY) > 12) k.VY = Math.Sign(k.VY) * 12;
                k.X += k.VX; k.Y += k.VY;
                max = Math.Max(max, Math.Abs(k.VX) + Math.Abs(k.VY));
            }
            return max;
        }

        void Zeichnen()
        {
            foreach (var e in kanten) { e.Strich.X1 = e.A.X; e.Strich.Y1 = e.A.Y; e.Strich.X2 = e.B.X; e.Strich.Y2 = e.B.Y; }
            foreach (var k in knoten)
            {
                Canvas.SetLeft(k.Form, k.X - k.R); Canvas.SetTop(k.Form, k.Y - k.R);
                if (k.Schild != null) { Canvas.SetLeft(k.Schild, k.X + k.R + 5); Canvas.SetTop(k.Schild, k.Y - 8); }
            }
        }

        void Einpassen()
        {
            if (knoten.Count == 0 || ActualWidth <= 0) return;
            double x0 = knoten.Min(k => k.X) - 30, x1 = knoten.Max(k => k.X) + 120, y0 = knoten.Min(k => k.Y) - 30, y1 = knoten.Max(k => k.Y) + 30;
            double s = Math.Min(1.6, Math.Min(ActualWidth / (x1 - x0), ActualHeight / (y1 - y0)));
            zoom.ScaleX = zoom.ScaleY = s;
            pan.X = ActualWidth / 2 - (x0 + x1) / 2 * s;
            pan.Y = ActualHeight / 2 - (y0 + y1) / 2 * s;
        }

        void Zoomen(object s, MouseWheelEventArgs a)
        {
            erstesEinpassen = false;
            var p = a.GetPosition(this);
            var w = Welt(p);
            double f = a.Delta > 0 ? 1.12 : 1 / 1.12;
            double neu = Math.Max(0.25, Math.Min(4, zoom.ScaleX * f));
            zoom.ScaleX = zoom.ScaleY = neu;
            pan.X = p.X - w.X * neu;
            pan.Y = p.Y - w.Y * neu;
            a.Handled = true;
        }
    }
}
