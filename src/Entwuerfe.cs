using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Workbench
{
    /// <summary>Nur für --render: zehn Entwürfe, wie man die Ebenen (die „Zwiebel“) zeigen kann. Ein Bild zum Vergleichen.</summary>
    static class Entwuerfe
    {
        static Brush B(string h) { return Farbe.B(h); }
        const double W = 340, H = 220;

        public static void Ebenen(string datei)
        {
            var titel = new[]
            {
                "1  Depth stack — sheets behind, smaller and darker",
                "2  Spines — one thin tab per sheet on the right",
                "3  Floor gauge — a fine scale, amber marker",
                "4  Paper edge — the stack lies beneath, at the bottom",
                "5  Shine-through — next sheet glows faintly above/below",
                "6  Tinted edges — up/down neighbours as coloured lines",
                "7  Stacked rail — the tiles themselves overlap like cards",
                "8  Mini stack in the title — small isometric layers",
                "9  Peek on approach — at rest clean, edge reveals next",
                "10 Motion only — clean at rest, depth when switching",
            };
            var grid = new WrapPanel { Width = (W + 24) * 2 + 24, Background = B("#07080B") };
            for (int i = 0; i < 10; i++)
            {
                var feld = new StackPanel { Margin = new Thickness(12) };
                feld.Children.Add(Szene(i));
                feld.Children.Add(new TextBlock { Text = titel[i], Foreground = B("#B3FFFFFF"), FontSize = 12, Margin = new Thickness(2, 8, 0, 0), FontFamily = UI.Schrift });
                grid.Children.Add(feld);
            }
            grid.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            grid.Arrange(new Rect(grid.DesiredSize));
            grid.UpdateLayout();
            var bmp = new RenderTargetBitmap((int)(grid.ActualWidth * 1.5), (int)(grid.ActualHeight * 1.5), 144, 144, PixelFormats.Pbgra32);
            bmp.Render(grid);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var s = File.Create(datei)) enc.Save(s);
        }

        static string[] zeichen = { ReiterZeichen.Sanduhr, ReiterZeichen.Haus, ReiterZeichen.Board, ReiterZeichen.Wolke };

        /// <summary>Ein kleines Fenster: Hintergrund, Schiene, aktives Blatt (Home) mit ein paar Platzhaltern.</summary>
        static FrameworkElement Szene(int v)
        {
            var g = new Grid { Width = W, Height = H, ClipToBounds = true };
            g.Children.Add(new Border { CornerRadius = new CornerRadius(10), Background = new RadialGradientBrush(Color.FromRgb(0x1D, 0x3A, 0x36), Color.FromRgb(0x0E, 0x10, 0x15)) { Center = new Point(0.6, 0.9), GradientOrigin = new Point(0.6, 0.9), RadiusX = 0.9, RadiusY = 0.9 } });
            int aktiv = 1;
            // Schiene
            var schiene = new Canvas { Width = 34, HorizontalAlignment = HorizontalAlignment.Left };
            for (int i = 0; i < 4; i++)
            {
                bool an = i == aktiv;
                double y = 70 + i * 22;
                if (v == 6)
                {
                    // gestapelte Kacheln: überlappen wie Karten, aktive vorn und herausgezogen
                    var t = new Border { Width = 22, Height = 15, CornerRadius = new CornerRadius(4), Background = B(an ? "#FF3A3122" : "#FF1C2028"), BorderBrush = B(an ? "#99F4B860" : "#33FFFFFF"), BorderThickness = new Thickness(0.7) };
                    t.Child = Ic(zeichen[i], 8, an ? "#FFF4B860" : "#66FFFFFF");
                    Canvas.SetLeft(t, 6 + (an ? 4 : 0)); Canvas.SetTop(t, 80 + i * 11);
                    Panel.SetZIndex(t, an ? 10 : 4 - i);
                    schiene.Children.Add(t);
                    continue;
                }
                var k = new Border { Width = 24, Height = 17, CornerRadius = new CornerRadius(5), Background = B(an ? "#2EF4B860" : "#00FFFFFF"), BorderBrush = B(an ? "#66F4B860" : "#00FFFFFF"), BorderThickness = new Thickness(0.7) };
                k.Child = Ic(zeichen[i], 9, an ? "#FFF4B860" : "#73FFFFFF");
                Canvas.SetLeft(k, 5); Canvas.SetTop(k, y);
                schiene.Children.Add(k);
            }
            g.Children.Add(schiene);

            var flaeche = new Grid { Margin = new Thickness(38, 26, 12, 12) };
            g.Children.Add(flaeche);
            Func<Thickness, double, Border> blatt = (m, o) => new Border { Margin = m, CornerRadius = new CornerRadius(10), Background = B("#FF1A1D23"), BorderBrush = B("#1FFFFFFF"), BorderThickness = new Thickness(0.7), Opacity = o };

            switch (v)
            {
                case 0: // Tiefenstapel nach hinten rechts
                    flaeche.Children.Add(blatt(new Thickness(22, 0, -2, 22), 0.35));
                    flaeche.Children.Add(blatt(new Thickness(11, 6, 5, 16), 0.6));
                    break;
                case 3: // Papierkante unten
                    for (int i = 2; i >= 1; i--) flaeche.Children.Add(new Border { Margin = new Thickness(6 * i, 0, 6 * i, -4 * i), CornerRadius = new CornerRadius(10), Background = B(i == 1 ? "#FF15181D" : "#FF111317"), BorderBrush = B("#1AFFFFFF"), BorderThickness = new Thickness(0.7) });
                    break;
            }
            var haupt = blatt(new Thickness(0, 0, v == 0 ? 22 : (v == 1 ? 14 : 0), v == 0 ? 22 : (v == 3 ? 8 : 0)), 1);
            haupt.Background = B("#F21B1F25");
            var inhalt = new Grid();
            // Platzhalter: eine Reihe Vorschauen, eine Reihe kleiner
            var reihe = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 18, 0, 0), VerticalAlignment = VerticalAlignment.Top };
            for (int i = 0; i < 5; i++) reihe.Children.Add(new Border { Width = 26, Height = 32, CornerRadius = new CornerRadius(3), Background = B(i % 2 == 0 ? "#E6F2F2F2" : "#CCDDE6F0"), Margin = new Thickness(0, 0, 12, 0) });
            inhalt.Children.Add(reihe);
            var reihe2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 76, 0, 0), VerticalAlignment = VerticalAlignment.Top };
            for (int i = 0; i < 4; i++) reihe2.Children.Add(new Border { Width = 20, Height = 24, CornerRadius = new CornerRadius(3), Background = B("#59F4B860"), Margin = new Thickness(0, 0, 12, 0), Opacity = 0.7 });
            inhalt.Children.Add(reihe2);
            haupt.Child = inhalt;
            flaeche.Children.Add(haupt);

            switch (v)
            {
                case 1: // Buchrücken rechts: ein Streifen je Blatt
                    {
                        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, -2, 0) };
                        for (int i = 0; i < 4; i++)
                        {
                            bool an = i == aktiv;
                            var s = new Border { Width = an ? 14 : 10, Height = 30, CornerRadius = new CornerRadius(0, 5, 5, 0), Background = B(an ? "#FF2A2418" : "#FF16191E"), BorderBrush = B(an ? "#80F4B860" : "#26FFFFFF"), BorderThickness = new Thickness(0, 0.7, 0.7, 0.7), Margin = new Thickness(0, 2, 0, 2), HorizontalAlignment = HorizontalAlignment.Right };
                            s.Child = Ic(zeichen[i], 7, an ? "#FFF4B860" : "#59FFFFFF");
                            sp.Children.Add(s);
                        }
                        flaeche.Children.Add(sp);
                        break;
                    }
                case 2: // Höhenmesser: feine Skala, Bernstein-Marke
                    {
                        var c = new Canvas { Width = 10, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 30, 6, 30) };
                        for (int i = 0; i < 4; i++)
                        {
                            var l = new Rectangle { Width = i == aktiv ? 10 : 5, Height = i == aktiv ? 2 : 1, Fill = B(i == aktiv ? "#FFF4B860" : "#4DFFFFFF"), RadiusX = 1, RadiusY = 1 };
                            Canvas.SetLeft(l, i == aktiv ? 0 : 5); Canvas.SetTop(l, 20 + i * 30);
                            c.Children.Add(l);
                        }
                        c.Children.Add(new Rectangle { Width = 1, Height = 92, Fill = B("#1AFFFFFF") });
                        Canvas.SetLeft(c.Children[c.Children.Count - 1], 9); Canvas.SetTop(c.Children[c.Children.Count - 1], 18);
                        flaeche.Children.Add(c);
                        break;
                    }
                case 4: // Durchscheinen: oben und unten liegt das Nachbarblatt als weicher Schein
                    {
                        flaeche.Children.Add(new Border { Height = 18, VerticalAlignment = VerticalAlignment.Top, CornerRadius = new CornerRadius(10, 10, 0, 0), Background = new LinearGradientBrush(Color.FromArgb(70, 244, 184, 96), Color.FromArgb(0, 244, 184, 96), 90), IsHitTestVisible = false });
                        flaeche.Children.Add(new Border { Height = 18, VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(0, 0, 10, 10), Background = new LinearGradientBrush(Color.FromArgb(0, 150, 190, 255), Color.FromArgb(60, 150, 190, 255), 90), IsHitTestVisible = false });
                        break;
                    }
                case 5: // getönte Kanten: feine Linie in der Farbe des Nachbarn oben/unten
                    {
                        flaeche.Children.Add(new Border { Height = 2, Margin = new Thickness(60, -1, 60, 0), VerticalAlignment = VerticalAlignment.Top, CornerRadius = new CornerRadius(1), Background = B("#B3F4B860") });
                        flaeche.Children.Add(new Border { Height = 2, Margin = new Thickness(60, 0, 60, -1), VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(1), Background = B("#998FB4FF") });
                        var oben = Ic(zeichen[0], 8, "#B3F4B860"); oben.VerticalAlignment = VerticalAlignment.Top; oben.Margin = new Thickness(0, -14, 0, 0);
                        var unten = Ic(zeichen[2], 8, "#998FB4FF"); unten.VerticalAlignment = VerticalAlignment.Bottom; unten.Margin = new Thickness(0, 0, 0, -13);
                        flaeche.Children.Add(oben); flaeche.Children.Add(unten);
                        break;
                    }
                case 7: // kleiner isometrischer Stapel im Titel
                    {
                        var c = new Canvas { Width = 30, Height = 22, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(-6, -24, 0, 0) };
                        for (int i = 3; i >= 0; i--)
                        {
                            bool an = i == aktiv;
                            var p = new Polygon { Points = new PointCollection { new Point(12, i * 4), new Point(24, 4 + i * 4), new Point(12, 8 + i * 4), new Point(0, 4 + i * 4) }, Fill = B(an ? "#E6F4B860" : "#26FFFFFF"), Stroke = B(an ? "#FFF4B860" : "#40FFFFFF"), StrokeThickness = 0.6 };
                            c.Children.Add(p);
                        }
                        flaeche.Children.Add(c);
                        flaeche.Children.Add(new TextBlock { Text = "Home", Foreground = B("#CCFFFFFF"), FontSize = 10.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(26, -22, 0, 0), VerticalAlignment = VerticalAlignment.Top });
                        break;
                    }
                case 8: // Annähern: Maus am unteren Rand – das nächste Blatt schiebt sich ein Stück herein
                    {
                        var naechstes = new Border { Height = 26, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(14, 0, 14, -8), CornerRadius = new CornerRadius(10, 10, 0, 0), Background = B("#FF20242C"), BorderBrush = B("#33FFFFFF"), BorderThickness = new Thickness(0.7, 0.7, 0.7, 0) };
                        var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 5, 0, 0) };
                        sp.Children.Add(Ic(zeichen[2], 9, "#B3FFFFFF"));
                        sp.Children.Add(new TextBlock { Text = "Board", Foreground = B("#B3FFFFFF"), FontSize = 9.5, Margin = new Thickness(5, 0, 0, 0) });
                        naechstes.Child = sp;
                        flaeche.Children.Add(naechstes);
                        var maus = new Polygon { Points = new PointCollection { new Point(0, 0), new Point(0, 11), new Point(3, 8), new Point(6, 13), new Point(8, 12), new Point(5, 7), new Point(9, 7) }, Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 0.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(40, 0, 0, 24) };
                        flaeche.Children.Add(maus);
                        break;
                    }
                case 9: // nur Bewegung: Momentaufnahme beim Wechsel – altes Blatt sinkt nach hinten, neues kommt vor
                    {
                        haupt.RenderTransform = new ScaleTransform(0.93, 0.93); haupt.RenderTransformOrigin = new Point(0.5, 0.5); haupt.Opacity = 0.45;
                        var neu = blatt(new Thickness(0, 34, 0, -34), 1);
                        neu.Background = B("#F2202329");
                        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 14, 0, 0) };
                        sp.Children.Add(Ic(zeichen[2], 10, "#CCFFFFFF"));
                        neu.Child = sp;
                        flaeche.Children.Add(neu);
                        var pfeil = Ic("M12 19V5M6 11l6-6 6 6", 14, "#80FFFFFF"); pfeil.HorizontalAlignment = HorizontalAlignment.Right; pfeil.VerticalAlignment = VerticalAlignment.Center; pfeil.Margin = new Thickness(0, 0, 10, 0);
                        flaeche.Children.Add(pfeil);
                        break;
                    }
            }
            return g;
        }

        static FrameworkElement Ic(string d, double gr, string farbe)
        {
            var i = UI.Icon(d, gr, Farbe.B(farbe), 1.8);
            i.HorizontalAlignment = HorizontalAlignment.Center; i.VerticalAlignment = VerticalAlignment.Center;
            return i;
        }
    }
}
