using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Workbench
{
    /// <summary>
    /// Ebenen-Wähler als „Hologramm“: Umschalt+Rad / Zusammenziehen / Umschalt+↑↓ zeigt ein Stufenmodell (gestapelte Platten),
    /// weiter scrollen lässt die Ziel-Ebene aufleuchten, Loslassen fliegt dorthin. Esc = abbrechen.
    /// Nur sichtbar, solange man wählt – sonst existiert es nicht (keine Kosten).
    /// </summary>
    partial class Hauptfenster
    {
        Grid stufen;
        readonly List<Shape> platten = new List<Shape>();
        readonly List<TextBlock> stufenText = new List<TextBlock>();
        int stufenZiel, stufenMax;
        bool stufenOffen;
        DispatcherTimer stufenTimer;

        public bool StufenOffen { get { return stufenOffen; } }
        bool stufenBild;

        /// <summary>Nur für Bilder: Wähler offen auf Ebene ziel, alle Bewegungen sofort am Ende.</summary>
        public void StufenFuerBild(int ziel)
        {
            stufenBild = true;
            StufenZeigen();
            stufenZiel = Math.Min(stufenMax, ziel);
            StufenFaerben();
            Action<DependencyObject> fertig = null;
            fertig = d =>
            {
                var el = d as UIElement;
                if (el != null)
                {
                    double o = el.Opacity; el.BeginAnimation(UIElement.OpacityProperty, null); el.Opacity = o;
                    var tt = el.RenderTransform as TranslateTransform; if (tt != null) { tt.BeginAnimation(TranslateTransform.YProperty, null); }
                }
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) fertig(VisualTreeHelper.GetChild(d, i));
            };
            stufen.Opacity = 1; stufen.RenderTransform = null;
            // Zielwerte setzen (die Animationen laufen im Bild nicht)
            for (int i = 0; i < platten.Count; i++)
            {
                int dd = Math.Abs(i - stufenZiel); double deck = dd == 0 ? 1 : dd == 1 ? 0.7 : dd == 2 ? 0.25 : 0;
                platten[i].BeginAnimation(UIElement.OpacityProperty, null); platten[i].Opacity = deck;
                stufenText[i].BeginAnimation(UIElement.OpacityProperty, null); stufenText[i].Opacity = deck;
                stufenGlanz[i].BeginAnimation(UIElement.OpacityProperty, null); stufenGlanz[i].Opacity = i == stufenZiel ? 1 : 0;
                stufenAnzahl[i].BeginAnimation(UIElement.OpacityProperty, null); stufenAnzahl[i].Opacity = i == stufenZiel ? 1 : 0;
                var tt = platten[i].RenderTransform as TranslateTransform; if (tt != null) { tt.BeginAnimation(TranslateTransform.YProperty, null); tt.Y = i == stufenZiel ? -5 : 0; }
                var tl = stufenText[i].RenderTransform as TranslateTransform; if (tl != null) { tl.BeginAnimation(TranslateTransform.YProperty, null); tl.Y = i == stufenZiel ? -5 : 0; }
            }
            var feld = (TranslateTransform)stufenFeld.RenderTransform; feld.BeginAnimation(TranslateTransform.YProperty, null); feld.Y = FeldY(stufenZiel);
        }

        public void StufenZeigen()
        {
            var b = Akt;
            if (b == null || !ImOrdner || (bild && !stufenBild)) return;
            if (stufenOffen) return;
            int max;
            if (!tiefeMax.TryGetValue(b.Id, out max)) max = 1;
            stufenMax = Math.Max(1, Math.Min(8, Math.Max(max, Tiefe(b))));
            stufenZiel = Tiefe(b);
            stufenOffen = true;
            RilleAktiv(true);

            stufen = new Grid { IsHitTestVisible = false, Opacity = 0 };
            var pos = buehne.TransformToAncestor(wurzel).Transform(new Point(0, 0));
            stufen.Margin = new Thickness(pos.X, pos.Y, 0, 0);
            stufen.Width = buehne.ActualWidth; stufen.Height = buehne.ActualHeight;
            stufen.HorizontalAlignment = HorizontalAlignment.Left; stufen.VerticalAlignment = VerticalAlignment.Top;
            // Blatt kräftig abdunkeln, damit das Modell ruhig steht
            stufen.Children.Add(new Border { CornerRadius = new CornerRadius(18), Background = new RadialGradientBrush(Color.FromArgb(0xE6, 0x0B, 0x0D, 0x11), Color.FromArgb(0xB3, 0x0B, 0x0D, 0x11)) });
            // Ausschnitt: nur die Ebenen um die gewählte herum; die Fläche gleitet mit, die gewählte bleibt in der Mitte
            var c = new Canvas { Width = 320, Height = 5 * Abst + 64, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, ClipToBounds = false };
            stufenFeld = c;
            c.RenderTransform = new TranslateTransform(0, FeldY(stufenZiel));
            platten.Clear(); stufenText.Clear(); stufenGlanz.Clear(); stufenAnzahl.Clear();
            for (int i = 0; i <= stufenMax; i++)
            {
                double y = 22 + i * Abst;
                var pts = new PointCollection { new Point(170, y), new Point(270, y + 22), new Point(170, y + 44), new Point(70, y + 22) };
                // weiches Leuchten unter der gewählten Platte
                var glanz = new Polygon { Points = pts, StrokeThickness = 7, Stroke = Farbe.B("#2EF4B860"), StrokeLineJoin = PenLineJoin.Round, Opacity = 0, IsHitTestVisible = false };
                var p = new Polygon { Points = pts, StrokeThickness = 1, StrokeLineJoin = PenLineJoin.Round };
                c.Children.Insert(0, p); c.Children.Insert(0, glanz);   // obere Ebenen liegen vorn
                platten.Add(p); stufenGlanz.Add(glanz);
                var t = new TextBlock { Text = Griechisch(i), FontSize = 15, FontFamily = new FontFamily("Cambria, Georgia, Segoe UI"), TextAlignment = TextAlignment.Right, Width = 44 };
                Canvas.SetLeft(t, 14); Canvas.SetTop(t, y + 12);
                c.Children.Add(t); stufenText.Add(t);
                int n = AnzahlAufEbene(b, i);
                var z = new TextBlock { Text = n > 0 ? n + " items" : "", FontSize = 11, Foreground = Farbe.B("#B3F4B860"), FontFamily = UI.Schrift, Opacity = 0 };
                Canvas.SetLeft(z, 282); Canvas.SetTop(z, y + 15);
                c.Children.Add(z); stufenAnzahl.Add(z);
            }
            stufen.Children.Add(c);
            wurzel.Children.Add(stufen);
            StufenFaerben();
            var st = new ScaleTransform(0.94, 0.94);
            stufen.RenderTransformOrigin = new Point(0.5, 0.5);
            stufen.RenderTransform = st;
            stufen.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(140)));
            st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        int AnzahlAufEbene(BlattDef b, int ebene)
        {
            if (ebene == 0) return 0;
            TStand st;
            if (!tiefeCache.TryGetValue(AktOrdner(b) + "|" + ebene, out st) || st == null) return 0;
            return Zaehlen(st.Wurzel, 0, ebene);
        }

        static int Zaehlen(TKnoten k, int e, int bis)
        {
            if (k.Kinder == null) return 0;
            int n = 0;
            foreach (var x in k.Kinder) n += x.Ordner && x.Kinder != null && e + 1 <= bis ? Zaehlen(x, e + 1, bis) : 1;
            return n + k.Mehr;
        }

        const double Abst = 30;
        Canvas stufenFeld;
        readonly List<Shape> stufenGlanz = new List<Shape>();
        readonly List<TextBlock> stufenAnzahl = new List<TextBlock>();
        double FeldY(int ziel) { return (5 * Abst + 64) / 2 - (22 + ziel * Abst + 22); }

        void StufenFaerben()
        {
            int jetzt = Tiefe(Akt);
            var ez = new CubicEase { EasingMode = EasingMode.EaseOut };
            var feld = stufenFeld != null ? stufenFeld.RenderTransform as TranslateTransform : null;
            if (feld != null) feld.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(FeldY(stufenZiel), TimeSpan.FromMilliseconds(200)) { EasingFunction = ez });
            for (int i = 0; i < platten.Count; i++)
            {
                bool ziel = i == stufenZiel, hier = i == jetzt;
                int d = Math.Abs(i - stufenZiel);
                double deck = d == 0 ? 1 : d == 1 ? 0.7 : d == 2 ? 0.25 : 0;      // nur die Nachbarn klar, eine weitere blass, Rest weg
                foreach (UIElement el in new UIElement[] { platten[i], stufenText[i] })
                    el.BeginAnimation(OpacityProperty, new DoubleAnimation(deck, TimeSpan.FromMilliseconds(180)));
                var tt = platten[i].RenderTransform as TranslateTransform;
                if (tt == null) { tt = new TranslateTransform(); platten[i].RenderTransform = tt; stufenGlanz[i].RenderTransform = tt; }
                tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(ziel ? -5 : 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 } });
                var tl = stufenText[i].RenderTransform as TranslateTransform;
                if (tl == null) { tl = new TranslateTransform(); stufenText[i].RenderTransform = tl; stufenAnzahl[i].RenderTransform = tl; }
                tl.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(ziel ? -5 : 0, TimeSpan.FromMilliseconds(180)));
                // Glas: oben heller, unten fast durchsichtig; die gewählte in warmem Bernstein
                platten[i].Fill = ziel ? new LinearGradientBrush(Color.FromArgb(150, 244, 184, 96), Color.FromArgb(36, 244, 184, 96), 90)
                                       : new LinearGradientBrush(Color.FromArgb((byte)(hier ? 46 : 26), 255, 255, 255), Color.FromArgb(4, 255, 255, 255), 90);
                platten[i].Stroke = Farbe.B(ziel ? "#E6F4B860" : hier ? "#66FFFFFF" : "#38FFFFFF");
                platten[i].StrokeThickness = ziel ? 1.4 : 1;
                stufenGlanz[i].BeginAnimation(OpacityProperty, new DoubleAnimation(ziel ? 1 : 0, TimeSpan.FromMilliseconds(200)));
                stufenAnzahl[i].BeginAnimation(OpacityProperty, new DoubleAnimation(ziel ? 1 : 0, TimeSpan.FromMilliseconds(180)));
                stufenText[i].Foreground = Farbe.B(ziel ? "#FFF4B860" : hier ? "#D9FFFFFF" : "#99FFFFFF");
                stufenText[i].FontWeight = ziel ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }

        /// <summary>Ziel eine Stufe weiter; beim Rand nickt das Modell nur.</summary>
        public void StufenSchritt(int d)
        {
            if (!stufenOffen) StufenZeigen();
            if (!stufenOffen) return;
            int neu = Math.Max(0, Math.Min(stufenMax, stufenZiel + d));
            if (neu == stufenZiel)
            {
                var tt = new TranslateTransform(); stufen.Children[1].RenderTransform = tt;
                tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, d * 5, TimeSpan.FromMilliseconds(90)) { AutoReverse = true });
                return;
            }
            stufenZiel = neu;
            StufenFaerben();
            if (tregler != null) tregler.Folgen(neu, true);
            // Einrasten sichtbar: die Platte blitzt kurz auf und federt
            platten[neu].BeginAnimation(Shape.StrokeThicknessProperty, new DoubleAnimation(3.2, 1.6, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            var zt = stufenText[neu];
            var zs = new ScaleTransform(1.25, 1.25); zt.RenderTransformOrigin = new Point(0.5, 0.5);
            var zg = new TransformGroup(); zg.Children.Add(zs); zg.Children.Add(zt.RenderTransform as TranslateTransform ?? new TranslateTransform());
            zt.RenderTransform = zg;
            zs.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(240)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 } });
            zs.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(240)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 } });
            // Ziel-Ebene vorlesen und schon fertig bauen, damit das Fliegen sofort klappt
            var b = Akt;
            if (b != null && neu > 0) TiefeLesen(AktOrdner(b), neu);
            StufenVorbauen();
        }

        DispatcherTimer stufenVorbau;

        /// <summary>Kurz nachdem das Ziel steht (man hält inne), die Zielebene unsichtbar fertig bauen.</summary>
        void StufenVorbauen()
        {
            if (bild) return;
            if (stufenVorbau == null)
            {
                stufenVorbau = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(90) };
                stufenVorbau.Tick += delegate
                {
                    stufenVorbau.Stop();
                    var b = Akt;
                    if (!stufenOffen || b == null || stufenZiel == Tiefe(b)) return;
                    TStand st;
                    if (stufenZiel > 0 && (!tiefeCache.TryGetValue(AktOrdner(b) + "|" + stufenZiel, out st) || st == null)) return;   // noch nicht gelesen: kommt nach dem Lesen
                    int z = stufenZiel;
                    Vorbauen(() => { tiefeVon[b.Id] = z; offenVon.Remove(b.Id); seiteVon.Remove(b.Id); });
                };
            }
            stufenVorbau.Stop(); stufenVorbau.Start();
        }

        /// <summary>Loslassen: hinfliegen (oder abbrechen).</summary>
        public void StufenFertig(bool abbrechen)
        {
            if (!stufenOffen) return;
            stufenOffen = false;
            RilleAktiv(false);
            if (stufenTimer != null) stufenTimer.Stop();
            var weg = stufen;
            var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(160));
            a.Completed += delegate { wurzel.Children.Remove(weg); };
            weg.BeginAnimation(OpacityProperty, a);
            var b = Akt;
            if (!abbrechen && b != null && stufenZiel != Tiefe(b)) TiefeSetzen(b, stufenZiel);
        }

        /// <summary>Touchpad hat kein „Loslassen“: kurz nichts mehr = loslassen.</summary>
        void StufenNachRuhe()
        {
            if (stufenTimer == null) { stufenTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(520) }; stufenTimer.Tick += delegate { StufenFertig(false); }; }
            stufenTimer.Stop(); stufenTimer.Start();
        }
    }
}
