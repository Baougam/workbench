using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Workbench
{
    /// <summary>
    /// Ebenen-Regler in der Schiene (Variante „unsichtbar bis nah“): in Ruhe nur ein feiner Strich mittig zwischen
    /// Blatt-Symbolen und Papierkorb. In der Nähe oder beim Benutzen wachsen Spur, Kerben und Knopf.
    /// Feder-Physik mit Magnet zu den Kerben; feste Abstände – findet Workbench eine tiefere Ebene, wächst die Spur nach unten.
    /// Ist bekannt, dass es tiefer geht, die Ebene aber noch nicht gelesen: unterste Kerbe blass („da kommt noch was“).
    /// Gezeichnet wird nur, solange sich etwas bewegt.
    /// </summary>
    partial class Hauptfenster
    {
        TiefenRegler tregler;

        void ReglerBauen(Grid schiene, FrameworkElement oben, FrameworkElement unten)
        {
            tregler = new TiefenRegler { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Width = 52, Height = 160, Visibility = Visibility.Collapsed };
            tregler.Schritt = d => StufenSchritt(d);
            tregler.Fertig = () => { if (StufenOffen) StufenFertig(false); };
            tregler.Nahe = nah => { foreach (var z in tiefeZahlen) z.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(nah ? 1 : 0, TimeSpan.FromMilliseconds(nah ? 140 : 220))); };
            schiene.Children.Add(tregler);
            SizeChangedEventHandler legen = (s, e) => ReglerLegen(schiene, oben, unten);
            schiene.SizeChanged += legen; oben.SizeChanged += legen; unten.SizeChanged += legen;
        }

        /// <summary>Genau in die Mitte der Lücke zwischen den Blatt-Symbolen und dem Papierkorb.</summary>
        void ReglerLegen(Grid schiene, FrameworkElement oben, FrameworkElement unten)
        {
            try
            {
                if (oben.ActualHeight < 1 || unten.ActualHeight < 1) return;
                double a = oben.TranslatePoint(new Point(0, oben.ActualHeight), schiene).Y;
                double z = unten.TranslatePoint(new Point(0, 0), schiene).Y;
                double h = Math.Max(50, Math.Min(200, z - a - 24));
                tregler.Height = h;
                tregler.Margin = new Thickness(0, (a + z) / 2 - h / 2, 0, 0);
                tregler.Visibility = z - a > 70 && reglerDa ? Visibility.Visible : Visibility.Collapsed;
            }
            catch { }
        }

        bool reglerDa;

        /// <summary>Nach jedem Zeichnen: Anzahl der Ebenen und die aktuelle an den Regler geben.</summary>
        void ReglerSetzen()
        {
            if (tregler == null) return;
            var b = Akt;
            bool da = IstBlatt(ansicht) && ImOrdner && tiefenAnzeige != null && b != null;
            reglerDa = da;
            if (!da) { tregler.Visibility = Visibility.Collapsed; return; }
            int max;
            if (!tiefeMax.TryGetValue(b.Id, out max)) max = 1;
            int jetzt = Tiefe(b);
            max = Math.Max(max, jetzt);
            bool offen = max > 0 && !tiefeCache.ContainsKey(AktOrdner(b) + "|" + max);
            tregler.Setzen(Math.Min(9, max + 1), jetzt, offen);
            if (tregler.Height >= 50) tregler.Visibility = Visibility.Visible;
            if (!E.Cfg.TiefeHinweis && IsVisible && !bild) { E.Cfg.TiefeHinweis = true; E.Cfg.Speichern(); tregler.Vorstellen(); }
        }

        class TiefenRegler : FrameworkElement
        {
            public Action<int> Schritt;
            public Action Fertig;
            public Action<bool> Nahe;
            int anzahl = 2, stufe;
            bool offenesEnde, aktiv, zieht, tickt;
            double x, vel, eingabe, nah, nahZiel, laenge, laengeZiel, zugStart, zugY;
            DateTime letzt;
            DispatcherTimer ruhe;
            const double Schrittweite = 22, Enger = 11;
            double schritt = Schrittweite;   // tatsächlicher Abstand (passt sich der Höhe an)
            static readonly Brush bahnFarbe = Frieren(new SolidColorBrush(Colors.White));
            static readonly Brush knopfFarbe = Frieren(new SolidColorBrush(Color.FromRgb(0x2A, 0x2E, 0x38)));
            static readonly Pen knopfRand = FrierenPen(new Pen(new SolidColorBrush(Color.FromArgb(0x38, 255, 255, 255)), 1));
            static readonly Pen knopfRandAn = FrierenPen(new Pen(new SolidColorBrush(Color.FromArgb(0xB3, 0xF4, 0xB8, 0x60)), 1));
            static Brush Frieren(Brush b) { b.Freeze(); return b; }
            static Pen FrierenPen(Pen p) { p.Freeze(); return p; }

            public TiefenRegler() { Cursor = Cursors.SizeNS; Focusable = false; }

            public void Setzen(int n, int jetzt, bool offen)
            {
                anzahl = Math.Max(2, n); offenesEnde = offen;
                laengeZiel = anzahl - 1;   // in Ebenen; Pixel ergeben sich aus der Höhe
                if (laenge == 0) laenge = laengeZiel;
                if (!aktiv) { stufe = jetzt; eingabe = jetzt; }
                Laufen();
            }

            /// <summary>Andere Eingabe (Umschalt+Rad, Zusammenziehen) hat die Stufe geändert: mitgehen und sichtbar werden.</summary>
            public void Folgen(int ziel, bool zeigen)
            {
                if (!aktiv) { eingabe = ziel; stufe = ziel; }
                nahZiel = zeigen || IsMouseOver ? 1 : 0;
                Laufen();
            }

            /// <summary>Scrollen (Rad, zwei Finger) – folgt direkt, nach kurzer Ruhe rastet er ein und die Ebene wird geöffnet.</summary>
            public void Eingabe(double d)
            {
                if (!aktiv) eingabe = stufe;
                aktiv = true; nahZiel = 1;
                eingabe = Math.Max(-0.3, Math.Min(anzahl - 1 + 0.3, eingabe + d));
                Pruefen();
                if (ruhe == null) { ruhe = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) }; ruhe.Tick += delegate { ruhe.Stop(); if (!zieht) Loslassen(); }; }
                ruhe.Stop(); ruhe.Start();
                Laufen();
            }

            /// <summary>Einmal beim allerersten Mal: kurz sichtbar werden, damit man ihn entdeckt.</summary>
            public void Vorstellen()
            {
                nahZiel = 1; Laufen();
                var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.6) };
                t.Tick += delegate { t.Stop(); if (!aktiv && !IsMouseOver) { nahZiel = 0; Laufen(); } };
                t.Start();
            }

            void Pruefen()
            {
                int n = (int)Math.Round(Math.Max(0, Math.Min(anzahl - 1, eingabe)));
                while (n != stufe) { int d = Math.Sign(n - stufe); stufe += d; if (Schritt != null) Schritt(d); }
            }

            void Loslassen()
            {
                aktiv = false;
                eingabe = Math.Round(Math.Max(0, Math.Min(anzahl - 1, eingabe)));
                if (!IsMouseOver) nahZiel = 0;
                if (Fertig != null) Fertig();
                Laufen();
            }

            protected override void OnMouseEnter(MouseEventArgs e) { nahZiel = 1; if (Nahe != null) Nahe(true); Laufen(); }
            protected override void OnMouseLeave(MouseEventArgs e) { if (!aktiv && !zieht) nahZiel = 0; if (Nahe != null) Nahe(false); Laufen(); }

            protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
            {
                e.Handled = true; zieht = true; aktiv = true; eingabe = stufe; zugStart = eingabe; zugY = e.GetPosition(this).Y;
                CaptureMouse(); Laufen();
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                if (!zieht) return;
                eingabe = Math.Max(-0.3, Math.Min(anzahl - 1 + 0.3, zugStart + (e.GetPosition(this).Y - zugY) / Math.Max(16, schritt)));
                Pruefen(); Laufen();
            }

            protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
            {
                if (!zieht) return;
                zieht = false; ReleaseMouseCapture(); Loslassen();
            }

            void Laufen()
            {
                if (tickt) return;
                tickt = true; letzt = DateTime.UtcNow;
                CompositionTarget.Rendering += Tick;
            }

            void Tick(object s, EventArgs e)
            {
                var t = DateTime.UtcNow;
                double dt = Math.Min(0.033, Math.Max(0.004, (t - letzt).TotalSeconds)); letzt = t;
                double ziel = aktiv ? Magnet(eingabe) : Math.Round(Math.Max(0, Math.Min(anzahl - 1, eingabe)));
                double k = aktiv ? 240 : 170, c = aktiv ? 26 : 20;          // Feder: kritisch gedämpft, kein Nachschwingen
                vel += ((ziel - x) * k - vel * c) * dt; x += vel * dt;
                nah += (nahZiel - nah) * Math.Min(1, dt * 10);
                laenge += (laengeZiel - laenge) * Math.Min(1, dt * 12);
                InvalidateVisual();
                bool ruhig = Math.Abs(vel) < 0.0008 && Math.Abs(ziel - x) < 0.0008 && Math.Abs(nahZiel - nah) < 0.01 && Math.Abs(laengeZiel - laenge) < 0.01 && !aktiv;
                if (ruhig) { x = ziel; nah = nahZiel; laenge = laengeZiel; InvalidateVisual(); CompositionTarget.Rendering -= Tick; tickt = false; }
            }

            /// <summary>Nahe einer Kerbe zieht es hinein, dazwischen läuft der Knopf frei.</summary>
            static double Magnet(double e)
            {
                double n = Math.Round(e), d = e - n;
                return n + Math.Sign(d) * 0.5 * Math.Pow(Math.Abs(d) * 2, 1.9);
            }

            protected override void OnRender(DrawingContext dc)
            {
                double w = ActualWidth, h = ActualHeight, cx = w / 2;
                dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
                // feste Höhe: erst rücken die Kerben zusammen (bis 11 px), dann wird die Spur ein Fenster, das mit dem Knopf wandert
                double platz = Math.Max(20, h - 28);
                schritt = laenge > 0 ? Math.Max(Enger, Math.Min(Schrittweite, platz / laenge)) : Schrittweite;
                double gesamt = laenge * schritt;
                double oben = gesamt <= platz ? (h - gesamt) / 2 : Math.Max(h - 14 - gesamt, Math.Min(14, h / 2 - x * schritt));
                bool fenster = gesamt > platz;
                dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h)));
                if (fenster) dc.PushOpacityMask(new LinearGradientBrush(new GradientStopCollection { new GradientStop(Color.FromArgb(0, 0, 0, 0), 0), new GradientStop(Colors.Black, 0.16), new GradientStop(Colors.Black, 0.84), new GradientStop(Color.FromArgb(0, 0, 0, 0), 1) }, 90));
                // Spur: in Ruhe unsichtbar; in der Nähe über die ganze Höhe der Lücke – so liest man sofort „Schieber“
                if (nah > 0.01)
                {
                    double bw = 2 + 2 * nah, lang = (h - 8) * (0.6 + 0.4 * nah);
                    var verlauf = new LinearGradientBrush(new GradientStopCollection { new GradientStop(Color.FromArgb(0, 255, 255, 255), 0), new GradientStop(Colors.White, 0.18), new GradientStop(Colors.White, 0.82), new GradientStop(Color.FromArgb(0, 255, 255, 255), 1) }, 90);
                    dc.PushOpacity(0.4 * nah);
                    dc.DrawRoundedRectangle(verlauf, null, new Rect(cx - bw / 2, (h - lang) / 2, bw, lang), bw / 2, bw / 2);
                    dc.Pop();
                }
                // Kerben wachsen aus dem Nichts; die unterste blass, wenn es noch tiefer geht als gelesen
                if (nah > 0.02)
                    for (int i = 0; i < anzahl; i++)
                    {
                        double y = oben + i * schritt;
                        if (i > laenge + 0.05) break;
                        double d = Math.Max(0, 1 - Math.Abs(x - i) * 1.6);
                        double r = 2 * (1 + 0.6 * d) * nah;
                        byte al = (byte)(255 * Math.Min(1, (0.28 + 0.6 * d) * nah * (offenesEnde && i == anzahl - 1 ? 0.45 : 1)));
                        var col = Color.FromArgb(al, (byte)(255 - 11 * d), (byte)(255 - 71 * d), (byte)(255 - 159 * d));
                        dc.DrawEllipse(new SolidColorBrush(col), null, new Point(cx, y), r, r);
                    }
                // Knopf, beim schnellen Bewegen leicht gestreckt
                double sy = 1 + Math.Min(0.5, Math.Abs(vel) * 0.05), sx = 1 / Math.Sqrt(sy);
                double kh0 = Math.Min(22, schritt + 4);
                double kw = 14 * sx, kh = kh0 * sy, ky = oben + x * schritt;
                dc.PushOpacity(0.45 + 0.55 * nah);   // Knopf immer leicht sichtbar
                dc.DrawRoundedRectangle(knopfFarbe, aktiv || zieht ? knopfRandAn : knopfRand, new Rect(cx - kw / 2, ky - kh / 2, kw, kh), kw / 2, kw / 2);
                dc.Pop();
                if (fenster) dc.Pop();
                dc.Pop();
            }
        }
    }
}
