using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Workbench
{
    /// <summary>
    /// Raumansicht: Umschalt + Ziehen auf dem Hintergrund kippt die Ordner-Fläche nahtlos in 3D.
    /// Von oben ist sie genau die heutige Fläche (gleiche Kacheln); je weiter man kippt, desto höher steigen die Ebenen
    /// (I unten, II, III darüber – jede genau über ihrem Ordner). Loslassen rastet ein: flach (zurück), schräg, Seite.
    /// Darüberfahren hebt einen Zweig hervor, Klick wählt aus, Doppelklick fliegt hinein (Ordner öffnet sich), Esc = zurück.
    /// Ein einziges Zeichen-Element; gezeichnet wird nur, solange sich etwas bewegt.
    /// </summary>
    partial class Hauptfenster
    {
        Raum raum;
        public bool RaumOffen { get { return raum != null; } }

        /// <summary>Wo die Kacheln der Fläche gerade liegen (im Fenster) – damit 3D flach exakt darauf beginnt.</summary>
        Rect RaumRahmen()
        {
            try
            {
                var pos = buehne.TransformToAncestor(wurzel).Transform(new Point(0, 0));
                var c = buehne.Children.Count > 0 ? Suchen(buehne.Children[0]) : null;
                if (c != null)
                {
                    var r = (Rect)c.Tag;
                    var p = c.TransformToAncestor(wurzel).Transform(new Point(0, 0));
                    return new Rect(p.X + r.X, p.Y + r.Y, r.Width, r.Height);
                }
                return new Rect(pos.X + 14, pos.Y + 2, Math.Max(200, buehne.ActualWidth - 30), Math.Max(200, buehne.ActualHeight - 30));
            }
            catch { return new Rect(0, 0, 900, 600); }
        }

        /// <summary>Die Kachel-Fläche der Tiefe in der sichtbaren Ansicht finden (sie trägt ihr Rechteck als Tag).</summary>
        static Canvas Suchen(DependencyObject d)
        {
            var c = d as Canvas;
            if (c != null && c.Tag is Rect) return c;
            int n = VisualTreeHelper.GetChildrenCount(d);
            for (int i = 0; i < n; i++) { var x = Suchen(VisualTreeHelper.GetChild(d, i)); if (x != null) return x; }
            return null;
        }

        /// <summary>Umschalt gedrückt und auf dem Hintergrund (nicht auf einem Symbol)?</summary>
        bool AufHintergrund(object quelle)
        {
            for (var d = quelle as DependencyObject; d != null && d != buehne; d = VisualTreeHelper.GetParent(d))
            {
                var fe = d as FrameworkElement;
                if (fe != null && fe.Tag is string && symbole.ContainsKey((string)fe.Tag)) return false;
                if (d is System.Windows.Controls.Primitives.ButtonBase || d is TextBox) return false;
                if (!(d is Visual)) break;
            }
            return true;
        }

        UIElement[] ortVorRaum;

        /// <summary>Umschalt + Drücken auf dem Hintergrund: Raum öffnen (noch flach) und ab jetzt dem Ziehen folgen.</summary>
        bool RaumStarten(MouseButtonEventArgs a)
        {
            var b = Akt;
            if (b == null || !ImOrdner || bild || raum != null) return false;
            string akt = AktOrdner(b);
            var st = RaumDaten(akt);
            raum = new Raum(this, akt, RaumRahmen(), st);
            wurzel.Children.Add(raum);
            raum.Ziehen(a.GetPosition(raum));
            return true;
        }

        /// <summary>Nur für Bilder: Raum fest gekippt zeigen (optional ein Ordner hervorgehoben).</summary>
        public void RaumFuerBild(double t, double azi, string hervor)
        {
            wurzel.UpdateLayout();
            if (raum != null) { wurzel.Children.Remove(raum); raum = null; }
            string akt = AktOrdner(Akt);
            raum = new Raum(this, akt, RaumRahmen(), RaumDaten(akt));
            raum.Fest(t, azi, hervor);
            wurzel.Children.Add(raum);
            RaumUeberblenden(1 - t * 4);
        }

        /// <summary>Bis zu drei Ebenen tief – aus dem Gemerkten (sofort), im Hintergrund still nachgelesen.</summary>
        TStand RaumDaten(string akt)
        {
            TStand st = TiefeLesen(akt, 3);
            if (st != null) return st;
            for (int t = 2; t >= 1; t--) { TStand s; if (tiefeCache.TryGetValue(akt + "|" + t, out s) && s != null) return s; }
            return null;
        }

        void RaumNeueDaten(string ordner, int tiefe)
        {
            if (raum == null || !Paths.Gleich(raum.Ordner, ordner)) return;
            TStand st;
            if (tiefeCache.TryGetValue(ordner + "|" + tiefe, out st) && st != null && tiefe >= raum.Tiefe) raum.Daten(st);
        }

        void RaumZu(bool sofort)
        {
            if (raum == null) return;
            var r = raum; raum = null;
            wurzel.Children.Remove(r);
            r.Aufraeumen();
            if (ortVorRaum != null) { ort.Children.Clear(); foreach (var e in ortVorRaum) ort.Children.Add(e); ortVorRaum = null; }
            if (buehne.Children.Count > 0) { buehne.Children[0].BeginAnimation(OpacityProperty, null); buehne.Children[0].Opacity = 1; }
            Keyboard.Focus(this);
        }

        void RaumOeffnen(string pfad)
        {
            var b = Akt;
            RaumZu(true);
            if (b != null) Betreten(b, pfad);
        }

        /// <summary>Die Bühne darunter blendet beim Kippen aus (und beim Zurückklappen wieder ein).</summary>
        void RaumUeberblenden(double sichtbar)
        {
            if (buehne.Children.Count == 0) return;
            var el = buehne.Children[0];
            el.BeginAnimation(OpacityProperty, null);
            el.Opacity = Math.Max(0, Math.Min(1, sichtbar));
        }

        // =====================================================================================================

        class Raum : FrameworkElement
        {
            class Platte
            {
                public TKnoten N; public Platte Eltern; public int Ebene; public Rect R;      // R = flach, in Pixeln
                public Color Farbe; public List<Brush> Punkte = new List<Brush>(); public string Art; public Brush ArtFarbe;
                public Point[] Oben; public double Naehe; public bool Lose;                                         // pro Bild berechnet
            }

            readonly Hauptfenster w;
            public readonly string Ordner;
            public int Tiefe;
            List<Platte> platten = new List<Platte>();
            double t, tZiel, azi, aziZiel, zoom, flug;  // t: 0 flach … 1 Seite
            Platte hover, gewaehlt, flugZiel;
            Point zugStart; double tStart, aziStart; bool zieht;
            DateTime letzterKlick; Platte letzteKlickPlatte;
            bool tickt;
            static readonly double[] lagen = { 0, 0.55, 1 };
            static readonly Typeface schrift = new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
            static readonly Typeface roemisch = new Typeface(new FontFamily("Cambria, Georgia"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            static readonly Brush ebenenFarbe = Frieren(new SolidColorBrush(Color.FromArgb(190, 244, 184, 96)));
            static readonly Pen rand = FrierenPen(new Pen(new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), 1));
            static readonly Pen randAn = FrierenPen(new Pen(new SolidColorBrush(Color.FromArgb(230, 244, 184, 96)), 1.5));
            static Brush Frieren(Brush b) { b.Freeze(); return b; }
            static Pen FrierenPen(Pen p) { p.Freeze(); return p; }

            public Raum(Hauptfenster w, string ordner, Rect rahmen, TStand st)
            {
                this.w = w; Ordner = ordner;
                HorizontalAlignment = HorizontalAlignment.Left; VerticalAlignment = VerticalAlignment.Top;
                Margin = new Thickness(rahmen.X, rahmen.Y, 0, 0);
                Width = rahmen.Width; Height = rahmen.Height;
                Focusable = true;
                Cursor = Cursors.SizeNS;
                if (st != null) Daten(st);
                Loaded += delegate { Keyboard.Focus(this); };
            }

            public void Fest(double t0, double a0, string hervor)
            {
                t = tZiel = t0; azi = aziZiel = a0;
                if (hervor != null) gewaehlt = platten.FirstOrDefault(p => p.N.Name == hervor);
            }

            public void Aufraeumen() { if (tickt) { CompositionTarget.Rendering -= Tick; tickt = false; } }

            // ---------- Aufbau: dieselben Kacheln wie die flache Fläche ----------
            public void Daten(TStand st)
            {
                var alt = gewaehlt != null ? gewaehlt.N.Pfad : null;
                platten = new List<Platte>();
                Tiefe = 0;
                Legen(st.Wurzel, null, new Rect(0, 0, Width, Height), 0);
                gewaehlt = alt == null ? null : platten.FirstOrDefault(p => Paths.Gleich(p.N.Pfad, alt));
                hover = null;
                Laufen();
            }

            void Legen(TKnoten d, Platte eltern, Rect r, int ebene)
            {
                if (d.Kinder == null) return;
                var ordner = d.Kinder.Where(x => x.Ordner && x.Kinder != null).ToList();
                var dateien = d.Kinder.Where(x => !x.Ordner || x.Kinder == null).ToList();
                var gew = new List<double>();
                if (dateien.Count > 0 || d.Mehr > 0) gew.Add(Math.Sqrt(dateien.Count + d.Mehr) + 1);
                foreach (var o in ordner) gew.Add(Math.Sqrt(Anzahl(o)) + 1.5);
                if (gew.Count == 0) return;
                var rects = Kacheln(gew, r);
                int i0 = dateien.Count > 0 || d.Mehr > 0 ? 1 : 0;
                if (i0 == 1)
                {
                    var rd = rects[0];
                    double l0 = ebene == 0 ? 5 : 4;
                    rd = new Rect(rd.X + l0, rd.Y + l0, Math.Max(2, rd.Width - 2 * l0), Math.Max(2, rd.Height - 2 * l0));
                    var lose = new Platte { N = d, Eltern = eltern, Ebene = ebene + 1, R = rd, Lose = true, Farbe = eltern != null ? eltern.Farbe : Color.FromRgb(70, 76, 90) };
                    foreach (var x in dateien.Take(40)) lose.Punkte.Add(Farbe.Endung(System.IO.Path.GetExtension(x.Name)));
                    platten.Add(lose);
                }
                for (int i = 0; i < ordner.Count; i++)
                {
                    var o = ordner[i];
                    double luft = ebene == 0 ? 5 : 4;
                    var ri = rects[i + i0];
                    ri = new Rect(ri.X + luft, ri.Y + luft, Math.Max(2, ri.Width - 2 * luft), Math.Max(2, ri.Height - 2 * luft));
                    var p = new Platte { N = o, Eltern = eltern, Ebene = ebene + 1, R = ri };
                    p.Farbe = Ton(o, eltern, ebene);
                    Art(p, o);
                    platten.Add(p);
                    Tiefe = Math.Max(Tiefe, ebene + 1);
                    // Kinder etwas eingerückt (Platz für Namen oben), wie in der flachen Box
                    double kopf = Math.Min(18, ri.Height * 0.22);
                    var innen = new Rect(ri.X + 3, ri.Y + kopf, Math.Max(1, ri.Width - 6), Math.Max(1, ri.Height - kopf - 3));
                    if (innen.Width > 10 && innen.Height > 8) Legen(o, p, innen, ebene + 1);
                }
            }

            static Color Ton(TKnoten o, Platte eltern, int ebene)
            {
                if (eltern != null)
                {
                    var c = eltern.Farbe;   // tiefer = dieselbe Tönung, etwas heller
                    return Color.FromRgb((byte)Math.Min(255, c.R + 16), (byte)Math.Min(255, c.G + 16), (byte)Math.Min(255, c.B + 16));
                }
                string f = OrdnerFarbe(o.Pfad);
                if (f != null) try { return Daempfen((Color)ColorConverter.ConvertFromString(f)); } catch { }
                // ohne eigene Farbe: ruhige Tönung aus dem Namen (immer gleich für denselben Ordner)
                int h = 0; foreach (char ch in o.Name.ToLowerInvariant()) h = h * 31 + ch;
                double hue = (Math.Abs(h) % 360) / 360.0;
                return Hsl(hue, 0.30, 0.34);
            }

            static Color Daempfen(Color c) { return Color.FromRgb((byte)(c.R * 0.52 + 14 * 0.48), (byte)(c.G * 0.52 + 16 * 0.48), (byte)(c.B * 0.52 + 21 * 0.48)); }

            static Color Hsl(double h, double s, double l)
            {
                Func<double, double> f = n => { double k = (n + h * 12) % 12; double a = s * Math.Min(l, 1 - l); return l - a * Math.Max(-1, Math.Min(Math.Min(k - 3, 9 - k), 1)); };
                return Color.FromRgb((byte)(f(0) * 255), (byte)(f(8) * 255), (byte)(f(4) * 255));
            }

            /// <summary>Ordnerart auf einen Blick: welche Dateiart überwiegt (Bilder, PDF, Code …) – als kleine farbige Marke.</summary>
            static void Art(Platte p, TKnoten o)
            {
                var zaehl = new Dictionary<string, int>();
                Action<TKnoten> lauf = null;
                lauf = k => { if (k.Kinder == null) return; foreach (var x in k.Kinder) { if (x.Ordner && x.Kinder != null) lauf(x); else if (!x.Ordner) { string e = System.IO.Path.GetExtension(x.Name).ToLowerInvariant(); if (e.Length > 0) { int n; zaehl.TryGetValue(e, out n); zaehl[e] = n + 1; } } } };
                lauf(o);
                foreach (var x in o.Kinder.Where(x => !x.Ordner).Take(24)) p.Punkte.Add(Farbe.Endung(System.IO.Path.GetExtension(x.Name)));
                if (zaehl.Count == 0) return;
                var top = zaehl.OrderByDescending(x => x.Value).First();
                if (top.Value * 2 < zaehl.Values.Sum()) { p.Art = "MIX"; p.ArtFarbe = Farbe.B("#99FFFFFF"); return; }
                p.Art = top.Key.TrimStart('.').ToUpperInvariant(); if (p.Art.Length > 4) p.Art = p.Art.Substring(0, 4);
                p.ArtFarbe = Farbe.Endung(top.Key);
            }

            // ---------- Bewegung ----------
            public void Ziehen(Point p) { zieht = true; zugStart = p; tStart = t; aziStart = azi; CaptureMouse(); Laufen(); }

            protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
            {
                e.Handled = true;
                var p = e.GetPosition(this);
                var hit = Treffen(p);
                if (hit != null && letzteKlickPlatte == hit && (DateTime.UtcNow - letzterKlick).TotalMilliseconds < 420) { Hineinfliegen(hit); return; }
                letzterKlick = DateTime.UtcNow; letzteKlickPlatte = hit;
                Ziehen(p);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                var p = e.GetPosition(this);
                if (zieht)
                {
                    // hochziehen = kippen (Ebenen steigen auf), seitlich = leicht drehen
                    tZiel = t = Math.Max(0, Math.Min(1, tStart + (zugStart.Y - p.Y) / 260.0));
                    aziZiel = azi = Math.Max(-0.45, Math.Min(0.45, aziStart + (p.X - zugStart.X) / 600.0));
                    w.RaumUeberblenden(1 - t * 4);
                    Laufen();
                    return;
                }
                var h = Treffen(p);
                if (h != hover) { hover = h; Cursor = h != null ? Cursors.Hand : Cursors.SizeNS; ToolTip = null; Laufen(); }
            }

            protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
            {
                if (!zieht) return;
                zieht = false; ReleaseMouseCapture();
                var p = e.GetPosition(this);
                bool klick = (p - zugStart).Length < 4;
                if (klick) { gewaehlt = Treffen(p); w.OrtRaum(gewaehlt != null ? gewaehlt.N.Pfad : null); Laufen(); if (t > 0.05) return; }
                // einrasten: flach (zurück zur Fläche), schräg oder Seite
                double best = lagen.OrderBy(l => Math.Abs(l - t)).First();
                if (t < 0.18) best = 0;
                tZiel = best;
                if (best > 0 && Math.Abs(aziZiel) < 0.05) aziZiel = -0.22;
                Laufen();
            }

            protected override void OnMouseLeave(MouseEventArgs e) { if (hover != null) { hover = null; Laufen(); } }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (e.Key == Key.Escape) { e.Handled = true; if (gewaehlt != null) { gewaehlt = null; w.OrtRaum(null); Laufen(); } else { tZiel = 0; Laufen(); } }
                else if (e.Key == Key.Enter && gewaehlt != null) { e.Handled = true; Hineinfliegen(gewaehlt); }
            }

            void Hineinfliegen(Platte p) { flugZiel = p; flug = 0.0001; tZiel = 0; Laufen(); }

            void Laufen() { if (!tickt) { CompositionTarget.Rendering += Tick; tickt = true; } InvalidateVisual(); }

            void Tick(object s, EventArgs e)
            {
                bool bewegt = false;
                if (!zieht)
                {
                    double nt = t + (tZiel - t) * 0.2, na = azi + (aziZiel - azi) * 0.2;
                    if (Math.Abs(nt - tZiel) < 0.002) nt = tZiel;
                    if (Math.Abs(na - aziZiel) < 0.002) na = aziZiel;
                    bewegt = nt != t || na != azi; t = nt; azi = na;
                    w.RaumUeberblenden(flugZiel != null ? 0 : 1 - t * 4);
                }
                if (flugZiel != null)
                {
                    flug = Math.Min(1, flug + 0.075); bewegt = true;
                    if (flug >= 1 && t < 0.01) { var ziel = flugZiel.N.Pfad; flugZiel = null; w.RaumOeffnen(ziel); return; }
                }
                InvalidateVisual();
                if (!bewegt && !zieht)
                {
                    CompositionTarget.Rendering -= Tick; tickt = false;
                    if (t == 0 && flugZiel == null) w.RaumZu(false);   // ganz flach = wieder die normale Fläche
                }
            }

            // ---------- Zeichnen ----------
            Point Projiziere(double x, double z, double y, double a, double b, double k, double oy)
            {
                double cx = Width / 2, cz = Height / 2;
                double X = x - cx, Z = z - cz;
                double X1 = X * Math.Cos(b) - Z * Math.Sin(b), Z1 = X * Math.Sin(b) + Z * Math.Cos(b);
                double naehe = y * Math.Cos(a) + Z1 * Math.Sin(a);
                double s = 1800 / (1800 - naehe);
                return new Point(cx + X1 * s * k, cz + oy + (Z1 * Math.Cos(a) - y * Math.Sin(a)) * s * k);
            }

            double dpi = 1;

            protected override void OnRender(DrawingContext dc)
            {
                try { dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip; } catch { }
                dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, Width, Height));
                if (platten.Count == 0) return;
                double tt = Glatt(t);
                double a = tt * 1.05, b = azi * Math.Min(1, tt * 3);
                double luft = 42 * tt;                       // Abstand der Ebenen: flach 0 → nahtlos auf der Fläche
                double k = 1 - 0.22 * tt;
                double oy = Tiefe * luft * Math.Sin(a) * 0.45 * k;
                // Hineinfliegen: auf die Platte zoomen, bis sie das Blatt füllt
                Rect fz = flugZiel != null ? flugZiel.R : Rect.Empty;
                double f = flugZiel != null ? Glatt(flug) : 0;
                var dicke = 5 * tt;

                foreach (var p in platten)
                {
                    double y = p.Ebene * luft;
                    var r = p.R;
                    var pts = new[] { new Point(r.Left, r.Top), new Point(r.Right, r.Top), new Point(r.Right, r.Bottom), new Point(r.Left, r.Bottom) };
                    p.Oben = new Point[4];
                    for (int i = 0; i < 4; i++) p.Oben[i] = Flug(Projiziere(pts[i].X, pts[i].Y, y, a, b, k, oy), fz, f);
                    double cz = (r.Top + r.Bottom) / 2 - Height / 2, cx = (r.Left + r.Right) / 2 - Width / 2;
                    p.Naehe = y * Math.Cos(a) + (cx * Math.Sin(b) + cz * Math.Cos(b)) * Math.Sin(a) + p.Ebene * 0.01;
                }
                Platte aktiv = hover ?? gewaehlt;
                // Ebenen-Zahlen links, nur wenn gekippt
                if (tt > 0.7)
                    for (int e = 1; e <= Tiefe; e++)
                    {
                        var pt = Flug(Projiziere(-6, Height, e * luft, a, b, k, oy), fz, f);
                        var ft = new FormattedText(roman[Math.Min(e - 1, roman.Length - 1)], CultureInfo.InvariantCulture, FlowDirection.LeftToRight, roemisch, 13, ebenenFarbe, dpi);
                        dc.PushOpacity(Math.Min(1, (tt - 0.7) * 4) * (1 - f));
                        dc.DrawText(ft, new Point(pt.X - 22, pt.Y - 9));
                        dc.Pop();
                    }
                foreach (var p in platten.OrderBy(x => x.Naehe))
                {
                    bool imZweig = aktiv == null || Im(p, aktiv) || Im(aktiv, p);
                    double deck = imZweig ? 1 : 0.22;
                    if (flugZiel != null && !Im(p, flugZiel) && p != flugZiel) deck *= 1 - f;
                    dc.PushOpacity(deck);
                    var c = p.Farbe;
                    var flaeche = new SolidColorBrush(Color.FromArgb((byte)((p.Lose ? 16 : 34) + (p.Lose ? 60 : 190) * tt), c.R, c.G, c.B)); flaeche.Freeze();
                    // Kante nach vorn (Dicke), nur wenn gekippt
                    if (dicke > 0.5)
                    {
                        var seite = new SolidColorBrush(Color.FromArgb(220, (byte)(c.R * 0.55), (byte)(c.G * 0.55), (byte)(c.B * 0.55))); seite.Freeze();
                        var u = new[] { p.Oben[3], p.Oben[2], new Point(p.Oben[2].X, p.Oben[2].Y + dicke), new Point(p.Oben[3].X, p.Oben[3].Y + dicke) };
                        dc.DrawGeometry(seite, null, Vieleck(u));
                    }
                    dc.DrawGeometry(flaeche, p == aktiv ? randAn : (p.Lose ? null : rand), Vieleck(p.Oben));
                    // Dateien als feine farbige Punkte entlang der oberen Kante, Ordnerart als kleine Marke
                    double bw = (p.Oben[1] - p.Oben[0]).Length, bh = (p.Oben[3] - p.Oben[0]).Length;
                    if (bw > 26 && bh > 10 && p.Punkte.Count > 0)
                    {
                        var dx = (p.Oben[1] - p.Oben[0]) / bw; var dy = (p.Oben[3] - p.Oben[0]) / Math.Max(1, bh);
                        int n = Math.Min(p.Punkte.Count, (int)((bw - 12) / 6));
                        for (int i = 0; i < n; i++)
                        {
                            var q = p.Oben[0] + dx * (8 + i * 6) + dy * Math.Min(bh - 4, 5);
                            dc.DrawEllipse(p.Punkte[i], null, q, 1.6, 1.6 * Math.Max(0.4, Math.Cos(a)));
                        }
                    }
                    bool name = !p.Lose && (p.Ebene == 1 || (aktiv != null && (p == aktiv || p.Eltern == aktiv)));
                    if (name && bw > 48 && bh > 14)
                    {
                        var ft = new FormattedText(p.N.Name, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, schrift, 11.5, Brushes.White, dpi)
                        { MaxTextWidth = Math.Max(10, bw - 40), MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
                        var at = p.Oben[0] + (p.Oben[1] - p.Oben[0]) / bw * 7 + (p.Oben[3] - p.Oben[0]) / Math.Max(1, bh) * Math.Min(bh - 2, 9 * Math.Max(0.3, Math.Cos(a)));
                        dc.PushOpacity(0.9);
                        dc.DrawText(ft, new Point(at.X, at.Y - 5));
                        dc.Pop();
                        if (p.Art != null && bw > 90)
                        {
                            var art = new FormattedText(p.Art, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, schrift, 8.5, p.ArtFarbe, dpi);
                            var rp = p.Oben[1] - (p.Oben[1] - p.Oben[0]) / bw * (art.Width + 8);
                            var hg = new Rect(rp.X - 3, at.Y - 5, art.Width + 6, art.Height);
                            dc.DrawRoundedRectangle(Farbe.B("#33000000"), null, hg, 3, 3);
                            dc.DrawText(art, new Point(rp.X, at.Y - 5));
                        }
                    }
                    dc.Pop();
                }
                // Hinweis am gewählten/überfahrenen Ordner: Anzahl
                if (aktiv != null && flugZiel == null)
                {
                    int dat = Zaehlen(aktiv.N), ord = aktiv.N.Kinder.Count(x => x.Ordner);
                    var txt = new FormattedText(aktiv.N.Name + "   " + dat + " files · " + ord + " folders", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, schrift, 11.5, Brushes.White, dpi);
                    var o = aktiv.Oben[0];
                    var hg = new Rect(o.X, o.Y - txt.Height - 12, txt.Width + 16, txt.Height + 8);
                    if (hg.Top < 0) hg.Y = 2;
                    dc.DrawRoundedRectangle(Farbe.B("#E6141821"), null, hg, 7, 7);
                    dc.DrawText(txt, new Point(hg.X + 8, hg.Y + 4));
                }
            }

            static readonly string[] roman = { "I", "II", "III", "IV", "V" };
            static double Glatt(double x) { return x * x * (3 - 2 * x); }
            static bool Im(Platte p, Platte ahn) { for (var x = p; x != null; x = x.Eltern) if (x == ahn) return true; return false; }
            static int Zaehlen(TKnoten k) { if (k.Kinder == null) return 0; int n = k.Mehr; foreach (var x in k.Kinder) n += x.Ordner && x.Kinder != null ? Zaehlen(x) : (x.Ordner ? 0 : 1); return n; }

            Point Flug(Point p, Rect ziel, double f)
            {
                if (f <= 0 || ziel.IsEmpty) return p;
                double s = Math.Min(Width / Math.Max(1, ziel.Width), Height / Math.Max(1, ziel.Height));
                double sf = 1 + (s - 1) * f;
                double zx = ziel.X + ziel.Width / 2, zy = ziel.Y + ziel.Height / 2;
                double mx = zx + (Width / 2 - zx) * f, my = zy + (Height / 2 - zy) * f;
                return new Point(mx + (p.X - zx) * sf, my + (p.Y - zy) * sf);
            }

            static StreamGeometry Vieleck(Point[] p)
            {
                var g = new StreamGeometry();
                using (var c = g.Open()) { c.BeginFigure(p[0], true, true); c.PolyLineTo(new[] { p[1], p[2], p[3] }, true, true); }
                g.Freeze();
                return g;
            }

            Platte Treffen(Point q)
            {
                foreach (var p in platten.OrderByDescending(x => x.Naehe))
                    if (p.Oben != null && !p.Lose && Innen(p.Oben, q)) return p;
                return null;
            }

            static bool Innen(Point[] v, Point q)
            {
                bool drin = false;
                for (int i = 0, j = v.Length - 1; i < v.Length; j = i++)
                    if ((v[i].Y > q.Y) != (v[j].Y > q.Y) && q.X < (v[j].X - v[i].X) * (q.Y - v[i].Y) / (v[j].Y - v[i].Y) + v[i].X) drin = !drin;
                return drin;
            }
        }

        /// <summary>Oben links: der Pfad des gewählten Ordners im Raum (oder wieder der normale Ort).</summary>
        void OrtRaum(string pfad)
        {
            var b = Akt;
            if (b == null) return;
            if (ortVorRaum == null) ortVorRaum = ort.Children.Cast<UIElement>().ToArray();
            if (pfad == null) { ort.Children.Clear(); foreach (var e in ortVorRaum) ort.Children.Add(e); return; }
            ort.Children.Clear();
            string w0 = WurzelVon(b);
            string rel = pfad.Length > (w0 ?? "").Length ? pfad.Substring((w0 ?? "").Length).Trim('\\') : "";
            var teile = new List<string> { Blaetter.NameVon(b, wolke) };
            if (rel.Length > 0) teile.AddRange(rel.Split('\\'));
            for (int i = 0; i < teile.Count; i++)
            {
                if (i > 0) ort.Children.Add(new TextBlock { Text = "›", FontSize = 13, Foreground = Farbe.B("#66FFFFFF"), Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
                ort.Children.Add(new TextBlock { Text = teile[i], FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = i == teile.Count - 1 ? Farbe.B("#E6FFFFFF") : LabelLeise, VerticalAlignment = VerticalAlignment.Center });
            }
        }
    }
}
