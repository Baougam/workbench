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

        public void StufenZeigen()
        {
            var b = Akt;
            if (b == null || !ImOrdner || bild) return;
            if (stufenOffen) return;
            int max;
            if (!tiefeMax.TryGetValue(b.Id, out max)) max = 1;
            stufenMax = Math.Max(1, Math.Min(7, Math.Max(max, Tiefe(b))));
            stufenZiel = Tiefe(b);
            stufenOffen = true;

            stufen = new Grid { IsHitTestVisible = false, Opacity = 0 };
            var pos = buehne.TransformToAncestor(wurzel).Transform(new Point(0, 0));
            stufen.Margin = new Thickness(pos.X, pos.Y, 0, 0);
            stufen.Width = buehne.ActualWidth; stufen.Height = buehne.ActualHeight;
            stufen.HorizontalAlignment = HorizontalAlignment.Left; stufen.VerticalAlignment = VerticalAlignment.Top;
            // Blatt leicht abdunkeln, damit das Modell trägt
            stufen.Children.Add(new Border { CornerRadius = new CornerRadius(18), Background = Farbe.B("#8C0B0D11") });
            var c = new Canvas { Width = 300, Height = 60 + stufenMax * 34 + 40, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            platten.Clear(); stufenText.Clear();
            for (int i = 0; i <= stufenMax; i++)
            {
                double y = 20 + i * 34;
                // isometrische Platte: Raute
                var p = new Polygon
                {
                    Points = new PointCollection { new Point(150, y), new Point(260, y + 26), new Point(150, y + 52), new Point(40, y + 26) },
                    StrokeThickness = 1, StrokeLineJoin = PenLineJoin.Round
                };
                c.Children.Insert(0, p);   // obere Ebenen liegen vorn
                platten.Add(p);
                int n = AnzahlAufEbene(b, i);
                var t = new TextBlock { Text = Griechisch(i), FontSize = 20, Foreground = Farbe.B("#99FFFFFF"), FontFamily = new FontFamily("Cambria, Georgia, Segoe UI") };
                Canvas.SetLeft(t, 10); Canvas.SetTop(t, y + 12);
                c.Children.Add(t);
                stufenText.Add(t);
                if (n > 0)
                {
                    var z = new TextBlock { Text = n.ToString(), FontSize = 11, Foreground = Farbe.B("#66FFFFFF"), FontFamily = UI.Schrift };
                    Canvas.SetLeft(z, 272); Canvas.SetTop(z, y + 19);
                    c.Children.Add(z);
                }
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

        void StufenFaerben()
        {
            int jetzt = Tiefe(Akt);
            for (int i = 0; i < platten.Count; i++)
            {
                bool ziel = i == stufenZiel, hier = i == jetzt;
                var tt = platten[i].RenderTransform as TranslateTransform;
                if (tt == null) { tt = new TranslateTransform(); platten[i].RenderTransform = tt; }
                tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(ziel ? -6 : 0, TimeSpan.FromMilliseconds(160)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 } });
                var tl = stufenText[i].RenderTransform as TranslateTransform;
                if (tl == null) { tl = new TranslateTransform(); stufenText[i].RenderTransform = tl; }
                tl.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(ziel ? -6 : 0, TimeSpan.FromMilliseconds(160)));
                platten[i].Fill = ziel ? (Brush)new LinearGradientBrush(Color.FromArgb(150, 244, 184, 96), Color.FromArgb(40, 244, 184, 96), 90) : Farbe.B(hier ? "#26FFFFFF" : "#0FFFFFFF");
                platten[i].Stroke = Farbe.B(ziel ? "#FFF4B860" : hier ? "#80FFFFFF" : "#40FFFFFF");
                platten[i].StrokeThickness = ziel ? 1.6 : 1;
                stufenText[i].Foreground = Farbe.B(ziel ? "#FFF4B860" : hier ? "#CCFFFFFF" : "#73FFFFFF");
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
            // Einrasten sichtbar: die Platte blitzt kurz auf und federt
            platten[neu].BeginAnimation(Shape.StrokeThicknessProperty, new DoubleAnimation(3.2, 1.6, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            var zt = stufenText[neu];
            var zs = new ScaleTransform(1.25, 1.25); zt.RenderTransformOrigin = new Point(0.5, 0.5);
            var zg = new TransformGroup(); zg.Children.Add(zs); zg.Children.Add(zt.RenderTransform as TranslateTransform ?? new TranslateTransform());
            zt.RenderTransform = zg;
            zs.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(240)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 } });
            zs.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(240)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 } });
            // Ziel-Ebene vorlesen, damit das Fliegen sofort klappt
            var b = Akt;
            if (b != null && neu > 0) TiefeLesen(AktOrdner(b), neu);
        }

        /// <summary>Loslassen: hinfliegen (oder abbrechen).</summary>
        public void StufenFertig(bool abbrechen)
        {
            if (!stufenOffen) return;
            stufenOffen = false;
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
