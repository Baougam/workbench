using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace Workbench
{
    /// <summary>
    /// Der Rundgang beim ersten Start: alles abgedunkelt bis auf die Stelle, um die es geht, daneben ein kurzer Satz.
    /// Nur beim ersten Mal; „Nicht mehr zeigen“ beendet ihn für immer, das ✕ nur für jetzt.
    /// </summary>
    class Einfuehrung : Grid
    {
        public class Schritt
        {
            public string Titel, Text;
            public Func<FrameworkElement> Ziel;     // null = mittig, ohne Loch
            public Action Vorher;                    // z. B. Ebene wechseln
            public Func<FrameworkElement> Extra;     // zusätzliche Bedienelemente (Willkommen)
        }

        readonly List<Schritt> schritte;
        readonly FrameworkElement bezug;
        readonly Action<bool> fertig;           // true = nie wieder zeigen
        readonly RectangleGeometry loch = new RectangleGeometry { RadiusX = 16, RadiusY = 16 };
        readonly RectangleGeometry ganz = new RectangleGeometry();
        readonly Path schleier;
        readonly Border blase = new Border();
        int nr = -1;

        public int Nr { get { return nr; } }

        public Einfuehrung(FrameworkElement bezug, List<Schritt> schritte, Action<bool> fertig)
        {
            this.bezug = bezug;
            this.schritte = schritte;
            this.fertig = fertig;
            var geo = new CombinedGeometry(GeometryCombineMode.Exclude, ganz, loch);
            schleier = new Path { Data = geo, Fill = Farbe.B("#B8080A0D") };   // nur die dunkle Fläche fängt Klicks – das Loch bleibt bedienbar
            Children.Add(schleier);
            blase.Width = 320;
            blase.HorizontalAlignment = HorizontalAlignment.Left;
            blase.VerticalAlignment = VerticalAlignment.Top;
            blase.CornerRadius = new CornerRadius(16);
            blase.Background = Farbe.B("#FA1E2128");
            blase.BorderBrush = Farbe.B("#24FFFFFF");
            blase.BorderThickness = new Thickness(1);
            blase.Padding = new Thickness(18, 16, 16, 14);
            blase.Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 8, Opacity = 0.5 };
            blase.RenderTransform = new TranslateTransform();
            Children.Add(blase);
            SizeChanged += delegate { Setzen(false); };
        }

        public void Starten() { Gehe(0); }

        public void Weiter()
        {
            if (nr + 1 >= schritte.Count) { Ende(true); return; }
            Gehe(nr + 1);
        }

        void Gehe(int i)
        {
            nr = i;
            var s = schritte[i];
            if (s.Vorher != null) s.Vorher();
            BlaseFuellen(s);
            // erst nach dem Layout messen, wo das Ziel liegt
            Dispatcher.BeginInvoke(new Action(() => Setzen(true)), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        void BlaseFuellen(Schritt s)
        {
            var sp = new StackPanel();
            var kopf = new Grid();
            kopf.Children.Add(new TextBlock { Text = s.Titel, FontSize = 15.5, FontWeight = FontWeights.SemiBold, Foreground = Farbe.Text, Margin = new Thickness(0, 0, 30, 0), TextWrapping = TextWrapping.Wrap });
            var x = new Button { Style = (Style)Application.Current.FindResource("Rund"), Width = 28, Height = 28, Content = UI.Icon(Ikon.X, 12, Farbe.Text3, 2), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -6, -6, 0), ToolTip = "Later" };
            System.Windows.Automation.AutomationProperties.SetName(x, "Later");
            x.Click += delegate { Ende(false); };
            kopf.Children.Add(x);
            sp.Children.Add(kopf);
            if (!string.IsNullOrEmpty(s.Text))
                sp.Children.Add(new TextBlock { Text = s.Text, FontSize = 13, Foreground = Farbe.Text2, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), LineHeight = 19, LineStackingStrategy = LineStackingStrategy.BlockLineHeight });
            if (s.Extra != null) { var e = s.Extra(); e.Margin = new Thickness(0, 12, 0, 0); sp.Children.Add(e); }

            var fuss = new Grid { Margin = new Thickness(0, 16, 0, 0) };
            var nie = new TextBlock { Text = "Don't show again", FontSize = 12, Foreground = Farbe.Text3, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
            nie.MouseEnter += delegate { nie.Foreground = Farbe.Text2; };
            nie.MouseLeave += delegate { nie.Foreground = Farbe.Text3; };
            nie.MouseLeftButtonUp += delegate { Ende(true); };
            fuss.Children.Add(nie);
            var rechts = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var punkte = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            for (int i = 0; i < schritte.Count; i++)
                punkte.Children.Add(new Ellipse { Width = i == nr ? 14 : 5, Height = 5, Fill = Farbe.B(i == nr ? "#F4B860" : "#40FFFFFF"), Margin = new Thickness(2, 0, 2, 0) });
            // der aktive Punkt als kleine Pille
            ((Ellipse)punkte.Children[nr]).Width = 5;
            punkte.Children.RemoveAt(nr);
            punkte.Children.Insert(nr, new Border { Width = 14, Height = 5, CornerRadius = new CornerRadius(3), Background = Farbe.Akzent, Margin = new Thickness(2, 0, 2, 0) });
            rechts.Children.Add(punkte);
            bool letzter = nr == schritte.Count - 1;
            var weiter = new Button { Style = (Style)Application.Current.FindResource("BtnPri"), Content = letzter ? "Get started" : "Next", Height = 32, Padding = new Thickness(16, 0, 16, 0) };
            weiter.Click += delegate { Weiter(); };
            rechts.Children.Add(weiter);
            fuss.Children.Add(rechts);
            sp.Children.Add(fuss);
            blase.Child = sp;
        }

        /// <summary>Loch zum Ziel gleiten lassen, Blase daneben setzen.</summary>
        void Setzen(bool weich)
        {
            if (nr < 0 || ActualWidth <= 0) return;
            ganz.Rect = new Rect(0, 0, ActualWidth, ActualHeight);
            var s = schritte[nr];
            Rect r;
            var ziel = s.Ziel == null ? null : s.Ziel();
            if (ziel != null && ziel.Visibility == Visibility.Visible && ziel.ActualWidth > 0 && ziel.IsDescendantOf(this.Parent as Visual ?? bezug))
            {
                r = ziel.TransformToVisual(this).TransformBounds(new Rect(0, 0, ziel.ActualWidth, ziel.ActualHeight));
                r.Inflate(8, 8);
            }
            else r = new Rect(ActualWidth / 2, ActualHeight / 2, 0, 0);

            if (weich && !Vorschau.Sofort) loch.BeginAnimation(RectangleGeometry.RectProperty, new RectAnimation(r, TimeSpan.FromMilliseconds(320)) { EasingFunction = UI.Weich });
            else { loch.BeginAnimation(RectangleGeometry.RectProperty, null); loch.Rect = r; }

            blase.Measure(new Size(320, double.PositiveInfinity));
            double bh = blase.DesiredSize.Height, bw = 320, x, y;
            if (r.Width == 0) { x = (ActualWidth - bw) / 2; y = (ActualHeight - bh) / 2; }
            else if (r.Width > ActualWidth * 0.5 && r.Height > ActualHeight * 0.4) { x = r.Left + (r.Width - bw) / 2; y = r.Bottom - bh - 24; }   // große Fläche: Blase unten hinein
            else if (r.Right + 16 + bw < ActualWidth) { x = r.Right + 16; y = r.Top; }
            else if (r.Left - 16 - bw > 0) { x = r.Left - 16 - bw; y = r.Top; }
            else { x = Math.Min(ActualWidth - bw - 12, Math.Max(12, r.Left)); y = r.Bottom + 12; }
            y = Math.Max(12, Math.Min(ActualHeight - bh - 12, y));
            var tt = (TranslateTransform)blase.RenderTransform;
            if (weich && !Vorschau.Sofort)
            {
                tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, TimeSpan.FromMilliseconds(320)) { EasingFunction = UI.Weich });
                tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, TimeSpan.FromMilliseconds(320)) { EasingFunction = UI.Weich });
                blase.BeginAnimation(OpacityProperty, new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(260)));
            }
            else { tt.BeginAnimation(TranslateTransform.XProperty, null); tt.BeginAnimation(TranslateTransform.YProperty, null); tt.X = x; tt.Y = y; }
        }

        void Ende(bool nieWieder)
        {
            var a = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(200));
            a.Completed += delegate { fertig(nieWieder); };
            BeginAnimation(OpacityProperty, a);
        }
    }
}
