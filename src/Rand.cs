using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Workbench
{
    /// <summary>
    /// Beim Ziehen einer Datei am oberen/unteren Rand verweilen = Ebene wechseln. Eine Pille mit dem Namen der nächsten Ebene
    /// füllt sich bernsteinfarben; ist sie voll, wird gewechselt. Liegt über allem, fängt selbst keine Maus ab.
    /// Läuft nur, solange gezogen wird.
    /// </summary>
    class Randwechsel : Grid
    {
        public const double ObenBis = 76, UntenAb = 64, LinksAb = 60;
        const int Verweilen = 700, Pause = 450;

        readonly Func<int, string> nameDerEbene;     // Richtung (-1/+1) → Name, null = dort ist nichts mehr
        readonly Action<int> wechseln, neigen;       // neigen(0) = Blatt zurück
        readonly Border pille;
        readonly TextBlock text;
        readonly ContentControl pfeil = new ContentControl { VerticalAlignment = VerticalAlignment.Center };
        readonly ScaleTransform voll = new ScaleTransform(0, 1);
        readonly TranslateTransform rutsch = new TranslateTransform();
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

            text = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Farbe.Text, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var zeile = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 0, 17, 0) };
            zeile.Children.Add(pfeil);
            zeile.Children.Add(text);
            var innen = new Grid();
            innen.Children.Add(new Border { CornerRadius = new CornerRadius(16), Background = Farbe.B("#59F4B860"), RenderTransform = voll });
            innen.Children.Add(zeile);
            innen.SizeChanged += delegate { innen.Clip = new RectangleGeometry(new Rect(innen.RenderSize), 16, 16); };
            pille = new Border
            {
                Height = 34, CornerRadius = new CornerRadius(17), Background = Farbe.B("#F5191C23"), BorderBrush = Farbe.AkzentRand, BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(60, 52, 18, 30), Opacity = 0, RenderTransform = rutsch, Child = innen
            };
            Children.Add(pille);

            geist = new Border { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Opacity = 0.8, RenderTransform = geistOrt, Visibility = Visibility.Collapsed };
            Children.Add(geist);

            takt = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(30) };
            takt.Tick += delegate { Tick(); };
        }

        /// <summary>Symbol der gezogenen Datei am Zeiger (nur beim eigenen Ziehen).</summary>
        public void Geist(string pfad)
        {
            geist.Child = pfad == null ? null : Vorschau.Glyphe(pfad, 38, 46);
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
            text.Text = nameDerEbene(zone);
            pfeil.Content = UI.Icon(zone < 0 ? "M12 19V6M6 12l6-6 6 6" : "M12 5v13M6 12l6 6 6-6", 14, Farbe.Akzent, 2);
            pille.VerticalAlignment = zone < 0 ? VerticalAlignment.Top : VerticalAlignment.Bottom;
            voll.ScaleX = 0;
            pille.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(140)));
            rutsch.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(zone * 10, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = UI.Weich });
            neigen(zone);
        }

        void Ausblenden()
        {
            pille.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(140)));
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
            voll.ScaleX = t;
            if (t < 1) return;
            int richtung = zone;
            wartet = true;
            seit = DateTime.UtcNow;
            pille.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(200)));
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
            pille.BeginAnimation(OpacityProperty, null);
            rutsch.BeginAnimation(TranslateTransform.YProperty, null);
            pille.Opacity = 1;
            voll.ScaleX = fuell;
        }
    }
}
