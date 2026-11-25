using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Workbench
{
    /// <summary>
    /// Der Zeit-Schieber (Hour · Day · Week · Month · ∞) – derselbe in der Download-Karte und in der Karte beim Rechtsklick.
    /// Eine Linie, fünf Stationen, die aktuelle sitzt als Bernstein-Knopf darauf; rechts Kalender und Cloud.
    /// </summary>
    static class ZeitUI
    {
        public static readonly string[] Stationen = { "1 hour", "1 day", "1 week", "1 month", Dauer.Immer };

        public static FrameworkElement Schieber(string aktuell, Action<string> waehlen, Action kalender, CloudOrt wolke, Action<CloudOrt> inWolke)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var leiste = new Grid { Height = 50 };
            var linie = new Border { Height = 2, CornerRadius = new CornerRadius(1), Background = Farbe.B("#26FFFFFF"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 12, 0, 0), IsHitTestVisible = false };
            leiste.SizeChanged += delegate { double h = leiste.ActualWidth / Stationen.Length / 2; linie.Margin = new Thickness(h, 12, h, 0); };
            leiste.Children.Add(linie);
            var raster = new UniformGrid { Columns = Stationen.Length };
            for (int i = 0; i < Stationen.Length; i++)
            {
                string n = Stationen[i];
                bool an = n == aktuell;
                var b = new Button { Style = (Style)Application.Current.FindResource("Leer"), Background = Brushes.Transparent };
                var sp = new StackPanel();
                var punktFlaeche = new Grid { Width = 26, Height = 26, HorizontalAlignment = HorizontalAlignment.Center };
                Ellipse punkt;
                if (an)
                {
                    punktFlaeche.Children.Add(new Ellipse { Width = 22, Height = 22, Fill = Farbe.B("#33F4B860") });
                    punkt = new Ellipse { Width = 14, Height = 14, Fill = Farbe.Akzent };
                }
                else punkt = new Ellipse { Width = 8, Height = 8, Fill = Farbe.B("#8CFFFFFF") };
                punktFlaeche.Children.Add(punkt);
                sp.Children.Add(punktFlaeche);
                var t = new TextBlock { Text = Dauer.Kurz(n), FontSize = i == 4 ? 15 : 12, Foreground = an ? Farbe.Text : Farbe.Text2, FontWeight = an ? FontWeights.SemiBold : FontWeights.Normal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, i == 4 ? -1 : 2, 0, 0) };
                sp.Children.Add(t);
                b.Content = sp;
                b.ToolTip = i == 4 ? "Forever" : null;
                System.Windows.Automation.AutomationProperties.SetName(b, i == 4 ? "Forever" : n);
                if (!an)
                {
                    b.MouseEnter += delegate { punkt.Width = 12; punkt.Height = 12; punkt.Fill = Farbe.B("#F2FFFFFF"); t.Foreground = Farbe.Text; };
                    b.MouseLeave += delegate { punkt.Width = 8; punkt.Height = 8; punkt.Fill = Farbe.B("#8CFFFFFF"); t.Foreground = Farbe.Text2; };
                }
                b.Click += delegate { waehlen(n); };
                raster.Children.Add(b);
            }
            leiste.Children.Add(raster);
            g.Children.Add(leiste);

            var extra = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(6, -2, -4, 0) };
            extra.Children.Add(new Border { Width = 1, Height = 24, Background = Farbe.B("#1FFFFFFF"), Margin = new Thickness(0, 3, 6, 0), VerticalAlignment = VerticalAlignment.Top });
            var kal = new Button { Style = (Style)Application.Current.FindResource("Rund"), Width = 32, Height = 32, Content = UI.Icon(Ikon.Kalender, 16, Farbe.Text2, 1.8), ToolTip = "Until…" };
            ToolTipService.SetPlacement(kal, PlacementMode.Top);
            kal.Click += delegate { kalender(); };
            extra.Children.Add(kal);
            if (wolke != null && inWolke != null)
            {
                var wb = new Button { Style = (Style)Application.Current.FindResource("Rund"), Width = 32, Height = 32, Content = UI.Icon(Ikon.Wolke, 17, Farbe.Text2, 1.8), ToolTip = wolke.Name };
                ToolTipService.SetPlacement(wb, PlacementMode.Top);
                wb.Click += delegate { inWolke(wolke); };
                extra.Children.Add(wb);
            }
            Grid.SetColumn(extra, 1);
            g.Children.Add(extra);
            return g;
        }

        /// <summary>Gleicher Look wie die Download-Karte (für Popups).</summary>
        public static Border Karte(UIElement inhalt, double breite)
        {
            return new Border
            {
                Width = breite,
                Background = Farbe.B("#F7191C23"),
                BorderBrush = Farbe.B("#24FFFFFF"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(16, 14, 14, 12),
                Margin = new Thickness(14),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 28, ShadowDepth = 8, Opacity = 0.5, Direction = 270 },
                Child = inhalt
            };
        }
    }
}
