using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace Workbench
{
    /// <summary>Lädt echte Vorschaubilder auf einem eigenen Thread, damit die Oberfläche nie hängt.</summary>
    static class Vorschau
    {
        public static bool Sofort;   // Render-Modus: synchron laden
        static readonly BlockingCollection<Action> auftraege = new BlockingCollection<Action>();
        static readonly Dictionary<string, BitmapSource> cache = new Dictionary<string, BitmapSource>();
        static Thread arbeiter;

        static string Schluessel(string pfad, int px)
        {
            long t = 0;
            try { t = File.GetLastWriteTimeUtc(pfad).Ticks; } catch { }
            return pfad.ToLowerInvariant() + "|" + px + "|" + t;
        }

        public static void Laden(string pfad, int px, bool nurSymbol, Action<BitmapSource> fertig)
        {
            string k = Schluessel(pfad, px) + (nurSymbol ? "|s" : "");
            BitmapSource bs;
            lock (cache) { if (cache.TryGetValue(k, out bs)) { fertig(bs); return; } }
            if (Sofort) { bs = Native.Vorschau(pfad, px, nurSymbol); Merken(k, bs); fertig(bs); return; }
            Starten();
            var disp = Application.Current.Dispatcher;
            auftraege.Add(() =>
            {
                BitmapSource b = null;
                try { b = Native.Vorschau(pfad, px, nurSymbol); } catch { }
                Merken(k, b);
                disp.BeginInvoke(new Action(() => fertig(b)));
            });
        }

        static void Merken(string k, BitmapSource b)
        {
            lock (cache) { if (cache.Count > 400) cache.Clear(); cache[k] = b; }
        }

        static void Starten()
        {
            if (arbeiter != null) return;
            arbeiter = new Thread(() => { foreach (var a in auftraege.GetConsumingEnumerable()) a(); });
            arbeiter.IsBackground = true;
            arbeiter.SetApartmentState(ApartmentState.STA);
            arbeiter.Priority = ThreadPriority.BelowNormal;
            arbeiter.Start();
        }

        /// <summary>Bild einer Datei: erst ein Platzhalter in Endform, dann blendet das echte Vorschaubild weich ein.</summary>
        public static FrameworkElement Bild(string pfad, double breite, double hoehe) { return Bild(pfad, breite, hoehe, false); }

        public static FrameworkElement Bild(string pfad, double breite, double hoehe, bool nurSymbol)
        {
            var g = new Grid { Width = breite, Height = hoehe };
            if (Directory.Exists(pfad))
            {
                var o = UI.OrdnerBild(breite);
                o.VerticalAlignment = VerticalAlignment.Center;
                g.Children.Add(o);
                return g;
            }
            var platz = UI.DateiPlatzhalter(Path.GetExtension(pfad), breite * 0.82, hoehe);
            g.Children.Add(platz);
            int px = (int)Math.Ceiling(Math.Max(breite, hoehe) * 2);
            Laden(pfad, px, nurSymbol, bs =>
            {
                if (bs == null) return;
                var img = new Image { Source = bs, Stretch = Stretch.Uniform, Width = breite, Height = hoehe };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                g.Children.Add(img);
                if (Sofort) { g.Children.Remove(platz); return; }
                img.Opacity = 0;
                var a = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180));
                a.Completed += delegate { g.Children.Remove(platz); };
                img.BeginAnimation(UIElement.OpacityProperty, a);
            });
            return g;
        }
    }

    /// <summary>Kleiner Monatskalender. Ein Klick auf einen Tag oder eine Schnellwahl genügt.</summary>
    class Kalender : StackPanel
    {
        public event Action<DateTime> Gewaehlt;
        DateTime monat;
        readonly UniformGrid tage = new UniformGrid { Columns = 7 };
        readonly TextBlock titel = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Farbe.Text, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        static readonly string[] monate = { "Januar", "Februar", "März", "April", "Mai", "Juni", "Juli", "August", "September", "Oktober", "November", "Dezember" };

        public Kalender()
        {
            var heute = DateTime.Today;
            monat = new DateTime(heute.Year, heute.Month, 1);

            var schnell = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            int bisSonntag = ((int)DayOfWeek.Sunday - (int)heute.DayOfWeek + 7) % 7;
            if (bisSonntag == 0) bisSonntag = 7;
            schnell.Children.Add(Schnell("Sonntag", heute.AddDays(bisSonntag)));
            schnell.Children.Add(Schnell("Monatsende", new DateTime(heute.Year, heute.Month, 1).AddMonths(1).AddDays(-1)));
            schnell.Children.Add(Schnell("Jahresende", new DateTime(heute.Year, 12, 31)));
            Children.Add(schnell);

            var kopf = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            var zur = UI.IconKnopf(Ikon.Links, "Voriger Monat", Farbe.Text2);
            zur.Style = (Style)Application.Current.FindResource("BtnFlach"); zur.Width = 36; zur.Padding = new Thickness(0);
            zur.HorizontalAlignment = HorizontalAlignment.Left;
            zur.Click += delegate { if (monat > new DateTime(heute.Year, heute.Month, 1)) { monat = monat.AddMonths(-1); Zeichnen(); } };
            var vor = UI.IconKnopf(Ikon.Rechts, "Nächster Monat", Farbe.Text2);
            vor.Style = zur.Style; vor.Width = 36; vor.Padding = new Thickness(0);
            vor.HorizontalAlignment = HorizontalAlignment.Right;
            vor.Click += delegate { monat = monat.AddMonths(1); Zeichnen(); };
            kopf.Children.Add(zur); kopf.Children.Add(titel); kopf.Children.Add(vor);
            Children.Add(kopf);

            var wt = new UniformGrid { Columns = 7, Margin = new Thickness(0, 2, 0, 2) };
            foreach (var t in new[] { "Mo", "Di", "Mi", "Do", "Fr", "Sa", "So" })
                wt.Children.Add(new TextBlock { Text = t, FontSize = 11.5, Foreground = Farbe.Text3, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 4) });
            Children.Add(wt);
            Children.Add(tage);
            Zeichnen();
        }

        Button Schnell(string text, DateTime tag)
        {
            var b = new Button { Style = (Style)Application.Current.FindResource("Btn"), Content = text, Height = 32, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 0, 6, 6), FontSize = 12.5 };
            b.ToolTip = Dauer.Wochentag(tag) + ", " + tag.ToString("dd.MM.yyyy");
            b.Click += delegate { if (Gewaehlt != null) Gewaehlt(tag); };
            return b;
        }

        void Zeichnen()
        {
            titel.Text = monate[monat.Month - 1] + " " + monat.Year;
            tage.Children.Clear();
            int versatz = ((int)monat.DayOfWeek + 6) % 7;      // Montag zuerst
            for (int i = 0; i < versatz; i++) tage.Children.Add(new Border());
            int n = DateTime.DaysInMonth(monat.Year, monat.Month);
            var heute = DateTime.Today;
            for (int d = 1; d <= n; d++)
            {
                var tag = new DateTime(monat.Year, monat.Month, d);
                var b = new Button { Style = (Style)Application.Current.FindResource("Tag"), Content = d.ToString(), Background = Brushes.Transparent, Margin = new Thickness(1) };
                if (tag < heute) b.IsEnabled = false;
                if (tag == heute) { b.Foreground = Farbe.Akzent; b.FontWeight = FontWeights.SemiBold; }
                var t2 = tag;
                b.Click += delegate { if (Gewaehlt != null) Gewaehlt(t2); };
                tage.Children.Add(b);
            }
            int rest = 42 - (versatz + n);
            for (int i = 0; i < rest; i++) tage.Children.Add(new Border { Height = 38 });   // immer 6 Zeilen: Kalender springt nicht in der Höhe
        }
    }
}
