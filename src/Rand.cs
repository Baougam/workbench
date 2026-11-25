using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Workbench
{
    /// <summary>
    /// Beim Ziehen einer Datei am oberen/unteren Rand verweilen = Ebene wechseln. Am Rand wächst ein durchsichtiger weißer Schein;
    /// ist er ganz da, wird gewechselt. Liegt über allem, fängt selbst keine Maus ab.
    /// Läuft nur, solange gezogen wird.
    /// </summary>
    class Randwechsel : Grid
    {
        public const double ObenBis = 76, UntenAb = 64, LinksAb = 60;
        const int Verweilen = 700, Pause = 450;

        readonly Func<int, string> nameDerEbene;     // Richtung (-1/+1) → Name, null = dort ist nichts mehr
        readonly Action<int> wechseln, neigen;       // neigen(0) = Blatt zurück
        readonly Border schein;
        readonly ScaleTransform wuchs = new ScaleTransform(1, 0.35);
        readonly Border geist;
        readonly TranslateTransform geistOrt = new TranslateTransform();
        readonly DispatcherTimer takt;
        int zone;
        bool wartet;
        DateTime seit, zuletzt;

        public int Zone { get { return zone; } }
        /// <summary>Aktuelle Zeigerposition während eines Ziehens ohne Windows-Drag (Tisch); null = kommt über Melden().</summary>
        public Func<Point?> Zeiger;

        public Randwechsel(Func<int, string> nameDerEbene, Action<int> wechseln, Action<int> neigen)
        {
            this.nameDerEbene = nameDerEbene;
            this.wechseln = wechseln;
            this.neigen = neigen;
            IsHitTestVisible = false;

            schein = new Border { Height = 110, Margin = new Thickness(63, 59, 19, 19), Opacity = 0, RenderTransform = wuchs };
            Children.Add(schein);

            geist = new Border { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Opacity = 0.8, RenderTransform = geistOrt, Visibility = Visibility.Collapsed };
            Children.Add(geist);

            takt = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(30) };
            takt.Tick += delegate { Tick(); };
        }

        /// <summary>Symbol der gezogenen Datei am Zeiger (nur beim eigenen Ziehen).</summary>
        public void Geist(string pfad) { Geist(pfad, 1); }

        public void Geist(string pfad, int anzahl)
        {
            Grid g = null;
            if (pfad != null)
            {
                g = new Grid { Width = 50, Height = 54 };
                g.Children.Add(Vorschau.Glyphe(pfad, 38, 46));
                if (anzahl > 1)
                    g.Children.Add(new Border
                    {
                        MinWidth = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = Farbe.Akzent, Padding = new Thickness(5, 0, 5, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                        Child = new TextBlock { Text = anzahl.ToString(), FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Farbe.AkzentText, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                    });
            }
            geist.Child = g;
            if (pfad == null) geist.Visibility = Visibility.Collapsed;
        }

        /// <summary>Zeiger ist beim Ziehen hier (Koordinaten des Fensterinhalts).</summary>
        public void Melden(Point p)
        {
            zuletzt = DateTime.UtcNow;
            if (geist.Child != null) { geistOrt.X = p.X + 12; geistOrt.Y = p.Y + 10; geist.Visibility = Visibility.Visible; }
            ZoneSetzen(ZoneBei(p, ActualWidth, ActualHeight));
        }

        public static int ZoneBei(Point p, double breite, double hoehe)
        {
            if (p.X < LinksAb || p.X > breite || p.Y < 0 || p.Y > hoehe) return 0;
            return p.Y < ObenBis ? -1 : p.Y > hoehe - UntenAb ? 1 : 0;
        }

        void ZoneSetzen(int z)
        {
            if (z != 0 && nameDerEbene(z) == null) z = 0;
            if (!takt.IsEnabled) takt.Start();
            if (z == zone) return;
            zone = z;
            wartet = false;
            if (z == 0) Ausblenden(); else Beginnen();
        }

        void Beginnen()
        {
            seit = DateTime.UtcNow;
            bool oben = zone < 0;
            schein.VerticalAlignment = oben ? VerticalAlignment.Top : VerticalAlignment.Bottom;
            schein.CornerRadius = oben ? new CornerRadius(17, 17, 0, 0) : new CornerRadius(0, 0, 17, 17);
            schein.RenderTransformOrigin = new Point(0.5, oben ? 0 : 1);
            var g = new LinearGradientBrush { StartPoint = new Point(0, oben ? 0 : 1), EndPoint = new Point(0, oben ? 1 : 0) };
            g.GradientStops.Add(new GradientStop(Color.FromArgb(92, 255, 255, 255), 0));
            g.GradientStops.Add(new GradientStop(Color.FromArgb(38, 255, 255, 255), 0.35));
            g.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
            g.Freeze();
            schein.Background = g;
            Stand(0);
            neigen(zone);
        }

        /// <summary>0…1: der Schein wird heller und wächst ins Blatt hinein.</summary>
        void Stand(double t)
        {
            double w = 1 - (1 - t) * (1 - t);
            schein.BeginAnimation(OpacityProperty, null);
            schein.Opacity = 0.15 + 0.85 * w;
            wuchs.ScaleY = 0.35 + 0.65 * w;
        }

        void Ausblenden()
        {
            schein.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(180)));
            neigen(0);
        }

        void Tick()
        {
            if (Zeiger != null)
            {
                var p = Zeiger();
                if (p == null) { Ende(); return; }
                Melden(p.Value);
            }
            else if ((DateTime.UtcNow - zuletzt).TotalMilliseconds > 400) { Ende(); return; }   // Ziehen vorbei oder Fenster verlassen
            if (zone == 0) return;
            double ms = (DateTime.UtcNow - seit).TotalMilliseconds;
            if (wartet)
            {
                // nach dem Wechsel kurz Luft, dann (noch am Rand) zur nächsten Ebene
                if (ms < Pause) return;
                wartet = false;
                if (nameDerEbene(zone) == null) { zone = 0; Ausblenden(); return; }
                Beginnen();
                return;
            }
            double t = Math.Min(1, ms / Verweilen);
            Stand(t);
            if (t < 1) return;
            int richtung = zone;
            wartet = true;
            seit = DateTime.UtcNow;
            schein.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(260)));
            wechseln(richtung);
        }

        public void Ende()
        {
            takt.Stop();
            Zeiger = null;
            Geist(null);
            if (zone != 0) { zone = 0; Ausblenden(); }
            wartet = false;
        }

        /// <summary>Nur für --render.</summary>
        public void FuerBild(int richtung, double fuell)
        {
            zone = richtung;
            Beginnen();
            Stand(fuell);
        }
    }
}
