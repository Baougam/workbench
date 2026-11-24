using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace Workbench
{
    /// <summary>
    /// Die kleine Karte unten rechts: Datei, Dauern, ein dünner Zeitbalken. Kein Erklärtext – nimmt nie den Fokus,
    /// ein Klick genügt, und läuft der Balken ab, gilt still der Vorschlag.
    /// </summary>
    class Frage : Window
    {
        readonly Engine E;
        readonly List<Eintrag> dateien = new List<Eintrag>();
        readonly bool info;
        string vorschlag;
        bool fertig;

        readonly Border karte;
        readonly Grid huelle;
        readonly TranslateTransform schiebe = new TranslateTransform(0, 24);
        readonly ContentControl inhalt = new ContentControl();
        readonly ScaleTransform balken = new ScaleTransform(1, 1);
        readonly Border bahn;
        AnimationClock uhr;

        public bool Offen { get { return !fertig; } }
        public bool IstInfo { get { return info; } }

        public Frage(Engine e, List<Eintrag> neu, bool infoModus)
        {
            E = e;
            info = infoModus;
            dateien.AddRange(neu);
            vorschlag = neu[0].Gewaehlt ?? e.Cfg.Standard;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height;
            Width = 410;
            FontFamily = UI.Schrift;
            Title = "Workbench";

            // Ablauf: feine Bernstein-Linie an der Unterkante
            bahn = new Border { Height = 2, CornerRadius = new CornerRadius(1), Background = Farbe.B("#0AFFFFFF"), Margin = new Thickness(4, 12, 4, -4) };
            bahn.Child = new Border { CornerRadius = new CornerRadius(1), Background = Farbe.B("#B3F4B860"), RenderTransform = balken, RenderTransformOrigin = new Point(0, 0.5) };

            karte = new Border
            {
                Background = Farbe.B("#F5191C23"),
                BorderBrush = Farbe.B("#1FFFFFFF"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(16, 16, 16, 12),
                Margin = new Thickness(0, 10, 0, 0),
                Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 8, Opacity = 0.5, Direction = 270 }
            };
            var sp = new StackPanel();
            sp.Children.Add(inhalt);
            sp.Children.Add(bahn);
            karte.Child = sp;
            // Blattstapel wie im Hauptfenster: zwei Kanten schauen über der Karte hervor
            huelle = new Grid { Margin = new Thickness(20), RenderTransform = schiebe, Opacity = 0 };
            huelle.Children.Add(new Border { Height = 10, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(36, 0, 36, 0), CornerRadius = new CornerRadius(14, 14, 0, 0), Background = Farbe.B("#C02A2E38"), BorderBrush = Farbe.B("#24FFFFFF"), BorderThickness = new Thickness(1, 1, 1, 0) });
            huelle.Children.Add(new Border { Height = 10, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(18, 5, 18, 0), CornerRadius = new CornerRadius(16, 16, 0, 0), Background = Farbe.B("#E0242833"), BorderBrush = Farbe.B("#2EFFFFFF"), BorderThickness = new Thickness(1, 1, 1, 0) });
            huelle.Children.Add(karte);
            Content = huelle;

            karte.MouseEnter += delegate { if (uhr != null && !fertig) uhr.Controller.Pause(); };
            karte.MouseLeave += delegate { if (uhr != null && !fertig && inhalt.Tag as string != "kalender") uhr.Controller.Resume(); };
            SourceInitialized += delegate { Native.NichtAktivieren(new WindowInteropHelper(this).Handle); };
            SizeChanged += delegate { Platzieren(); };
            Loaded += delegate
            {
                Platzieren();
                huelle.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = UI.Weich });
                schiebe.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(24, 0, TimeSpan.FromMilliseconds(280)) { EasingFunction = UI.Weich });
            };

            Wahl();
        }

        /// <summary>Unten rechts auf dem Bildschirm mit der Maus – in echten Pixeln gerechnet, damit jede Skalierung stimmt.</summary>
        void Platzieren()
        {
            var src = PresentationSource.FromVisual(this);
            var wa = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea;
            Matrix m = src != null && src.CompositionTarget != null ? src.CompositionTarget.TransformFromDevice : Matrix.Identity;
            var unten = m.Transform(new Point(wa.Right, wa.Bottom));
            var oben = m.Transform(new Point(wa.Left, wa.Top));
            Left = Math.Max(oben.X, unten.X - Width + 6);
            Top = Math.Max(oben.Y, unten.Y - ActualHeight + 6);
        }

        FrameworkElement Kopf(bool zurueck)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            FrameworkElement links;
            if (zurueck)
            {
                var b = new Button { Style = (Style)Application.Current.FindResource("Rund"), Width = 32, Height = 32, Content = UI.Icon(Ikon.Links, 15, Farbe.Text2, 2), ToolTip = "Back", Margin = new Thickness(-6, 0, 6, 0) };
                b.Click += delegate { Wahl(); if (uhr != null && !IsMouseOver) uhr.Controller.Resume(); };
                links = b;
            }
            else
            {
                // eigenes Workbench-Zeichen; mehrere Dateien als kleiner Stapel
                var bilder = new Grid { Width = 46, Height = 50, Margin = new Thickness(0, 0, 14, 0) };
                int n = Math.Min(3, dateien.Count);
                for (int i = n - 1; i >= 0; i--)
                {
                    var b = Vorschau.Glyphe(dateien[i].Pfad, 38, 46);
                    b.HorizontalAlignment = HorizontalAlignment.Left;
                    b.VerticalAlignment = VerticalAlignment.Bottom;
                    b.Margin = new Thickness(i * 4, 0, 0, i * 3);
                    if (i > 0) b.Opacity = 0.45;
                    bilder.Children.Add(b);
                }
                links = bilder;
            }
            g.Children.Add(links);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var name = new StackPanel { Orientation = Orientation.Horizontal };
            var t = UI.T(dateien[0].Name, 14, Farbe.Text, FontWeights.SemiBold);
            t.MaxWidth = dateien.Count > 1 ? 210 : 250;
            name.Children.Add(t);
            if (dateien.Count > 1)
                name.Children.Add(new Border { CornerRadius = new CornerRadius(9), Background = Farbe.B("#1FFFFFFF"), Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Child = UI.T("+" + (dateien.Count - 1), 11, Farbe.Text2, FontWeights.SemiBold) });
            text.Children.Add(name);
            if (!zurueck)
            {
                string ext = System.IO.Path.GetExtension(dateien[0].Pfad).TrimStart('.').ToUpperInvariant();
                long groesse = 0;
                foreach (var d in dateien) try { if (System.IO.File.Exists(d.Pfad)) groesse += new System.IO.FileInfo(d.Pfad).Length; } catch { }
                string meta = (dateien.Count > 1 ? dateien.Count + " files" : (ext.Length > 0 ? ext : "File")) + (groesse > 0 ? " · " + Dauer.Groesse(groesse) : "");
                var m = UI.T(meta, 12, Farbe.Text3);
                m.Margin = new Thickness(0, 3, 0, 0);
                text.Children.Add(m);
            }
            Grid.SetColumn(text, 1);
            g.Children.Add(text);
            var x = new Button { Style = (Style)Application.Current.FindResource("Rund"), Width = 30, Height = 30, Content = UI.Icon(Ikon.X, 12, Farbe.Text3, 2), Margin = new Thickness(0, -4, -6, -4), VerticalAlignment = VerticalAlignment.Top };
            System.Windows.Automation.AutomationProperties.SetName(x, "Close");
            x.Click += delegate { if (info) Schliessen(); else Anwenden(vorschlag, null, false); };
            Grid.SetColumn(x, 2);
            g.Children.Add(x);
            return g;
        }

        void Wahl()
        {
            var sp = new StackPanel();
            sp.Children.Add(Kopf(false));
            sp.Children.Add(Zeitleiste());
            inhalt.Tag = "wahl";
            inhalt.Content = sp;
            if (uhr == null) Balken(info ? 6 : E.Cfg.Sekunden);
        }

        /// <summary>
        /// Der Zeit-Schieber – dasselbe Motiv wie der Ebenen-Schieber im Fenster: eine Linie, fünf Stationen
        /// (1 Std · 1 Tag · 1 Woche · 1 Monat · ∞), der Vorschlag sitzt als Knopf darauf. Ein Klick auf eine Station genügt.
        /// Rechts daneben: Kalender und Cloud als Symbole.
        /// </summary>
        FrameworkElement Zeitleiste()
        {
            var g = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            string[] stationen = { "1 hour", "1 day", "1 week", "1 month", Dauer.Immer };
            string[] beschriftung = { "Hour", "Day", "Week", "Month", "∞" };
            var leiste = new Grid { Height = 50 };
            var linie = new Border { Height = 2, CornerRadius = new CornerRadius(1), Background = Farbe.B("#26FFFFFF"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 12, 0, 0), IsHitTestVisible = false };
            leiste.SizeChanged += delegate { double h = leiste.ActualWidth / stationen.Length / 2; linie.Margin = new Thickness(h, 12, h, 0); };
            leiste.Children.Add(linie);
            var raster = new UniformGrid { Columns = stationen.Length };
            for (int i = 0; i < stationen.Length; i++)
            {
                string n = stationen[i];
                bool an = n == vorschlag;
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
                var t = new TextBlock { Text = beschriftung[i], FontSize = i == 4 ? 15 : 12, Foreground = an ? Farbe.Text : Farbe.Text2, FontWeight = an ? FontWeights.SemiBold : FontWeights.Normal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, i == 4 ? -1 : 2, 0, 0) };
                sp.Children.Add(t);
                b.Content = sp;
                b.ToolTip = i == 4 ? "Forever" : null;
                System.Windows.Automation.AutomationProperties.SetName(b, i == 4 ? "Forever" : n);
                if (!an)
                {
                    b.MouseEnter += delegate { punkt.Width = 12; punkt.Height = 12; punkt.Fill = Farbe.B("#F2FFFFFF"); t.Foreground = Farbe.Text; };
                    b.MouseLeave += delegate { punkt.Width = 8; punkt.Height = 8; punkt.Fill = Farbe.B("#8CFFFFFF"); t.Foreground = Farbe.Text2; };
                }
                b.Click += delegate { Anwenden(n, null, true); };
                raster.Children.Add(b);
            }
            leiste.Children.Add(raster);
            g.Children.Add(leiste);

            var extra = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(6, -2, -4, 0) };
            extra.Children.Add(new Border { Width = 1, Height = 24, Background = Farbe.B("#1FFFFFFF"), Margin = new Thickness(0, 3, 6, 0), VerticalAlignment = VerticalAlignment.Top });
            var kal = new Button { Style = (Style)Application.Current.FindResource("Rund"), Width = 32, Height = 32, Content = UI.Icon(Ikon.Kalender, 16, Farbe.Text2, 1.8), ToolTip = "Until…" };
            ToolTipService.SetPlacement(kal, PlacementMode.Top);
            kal.Click += delegate { Kalender(); };
            extra.Children.Add(kal);
            var w = Cloud.Aktuell(E.Cfg);
            if (w != null)
            {
                var wb = new Button { Style = (Style)Application.Current.FindResource("Rund"), Width = 32, Height = 32, Content = UI.Icon(Ikon.Wolke, 17, Farbe.Text2, 1.8), ToolTip = w.Name };
                ToolTipService.SetPlacement(wb, PlacementMode.Top);
                wb.Click += delegate { InWolke(w); };
                extra.Children.Add(wb);
            }
            Grid.SetColumn(extra, 1);
            g.Children.Add(extra);
            return g;
        }

        void Kalender()
        {
            if (uhr != null) uhr.Controller.Pause();
            var sp = new StackPanel();
            sp.Children.Add(Kopf(true));
            var k = new Kalender { Margin = new Thickness(0, 10, 0, 0) };
            k.Gewaehlt += tag => Anwenden(Dauer.Bis, Dauer.BisTag(tag), true);
            sp.Children.Add(k);
            inhalt.Tag = "kalender";
            inhalt.Content = sp;
            UI.Einblenden(sp, 6, 160);
        }

        void Balken(int sekunden)
        {
            var anim = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(Math.Max(2, sekunden)));
            var c = anim.CreateClock();
            uhr = c;
            c.Completed += delegate { if (!fertig && uhr == c && c.CurrentProgress >= 1) { if (info) Schliessen(); else Anwenden(vorschlag, null, false); } };
            balken.ApplyAnimationClock(ScaleTransform.ScaleXProperty, c);
            if (IsMouseOver) c.Controller.Pause();
        }

        /// <summary>Weitere Downloads, während die Karte offen ist: die Wahl gilt dann für alle.</summary>
        public void Hinzufuegen(List<Eintrag> neu)
        {
            if (fertig || info) return;
            dateien.AddRange(neu);
            if (inhalt.Tag as string == "wahl") Wahl();
            if (uhr != null) { uhr.Controller.Begin(); if (IsMouseOver) uhr.Controller.Pause(); }
        }

        void Anwenden(string wahl, DateTime? bis, bool aktiv)
        {
            if (fertig) return;
            fertig = true;
            if (uhr != null) uhr.Controller.Pause();
            if (info && !aktiv) { Wegfahren(); return; }
            DateTime? ablauf = bis ?? Dauer.Ablauf(wahl, DateTime.UtcNow);
            foreach (var d in dateien.ToList())
            {
                var e = E.S.Finde(d.Pfad);
                if (e == null) continue;
                if (aktiv) E.Merken(d.Pfad, wahl);
                E.FristSetzen(e, ablauf, wahl == Dauer.Bis ? "until " + bis.Value.ToLocalTime().ToString("d MMM", Dauer.En) : wahl);
            }
            if (aktiv) E.S.Speichern();
            string text = ablauf == null ? "Kept forever" : "Stays until " + Dauer.Wann(ablauf.Value);
            Bestaetigen(ablauf == null ? Ikon.Unendlich : Ikon.Haken, text, aktiv);
        }

        void InWolke(CloudOrt w)
        {
            if (fertig || w == null) return;
            fertig = true;
            if (uhr != null) uhr.Controller.Pause();
            foreach (var d in dateien.ToList())
            {
                var e = E.S.Finde(d.Pfad);
                if (e != null) E.Verschieben(e, w.Pfad);
            }
            Bestaetigen(Ikon.Wolke, w.Name, true);
        }

        void Bestaetigen(string icon, string text, bool aktiv)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 2) };
            sp.Children.Add(new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = Farbe.AkzentWeich, Child = UI.Icon(icon, 16, Farbe.Akzent, 2.2) });
            var t = UI.T(text, 14, Farbe.Text, FontWeights.SemiBold);
            t.VerticalAlignment = VerticalAlignment.Center;
            t.Margin = new Thickness(10, 0, 0, 0);
            sp.Children.Add(t);
            var name = UI.T(dateien[0].Name + (dateien.Count > 1 ? " +" + (dateien.Count - 1) : ""), 12.5, Farbe.Text3);
            name.VerticalAlignment = VerticalAlignment.Center;
            name.Margin = new Thickness(10, 0, 0, 0);
            name.MaxWidth = 200;
            sp.Children.Add(name);
            inhalt.Content = sp;
            bahn.Visibility = Visibility.Collapsed;
            UI.Einblenden(sp, 4, 160);
            var warte = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(aktiv ? 1000 : 600) };
            warte.Tick += delegate { warte.Stop(); Wegfahren(); };
            warte.Start();
        }

        void Schliessen()
        {
            fertig = true;
            Wegfahren();
        }

        void Wegfahren()
        {
            var a = new DoubleAnimation(huelle.Opacity, 0, TimeSpan.FromMilliseconds(200));
            a.Completed += delegate { Close(); };
            huelle.BeginAnimation(OpacityProperty, a);
            schiebe.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, 16, TimeSpan.FromMilliseconds(220)) { EasingFunction = UI.Weich });
        }

        /// <summary>Nur für Bilder im Render-Modus.</summary>
        public FrameworkElement Karte { get { huelle.Opacity = 1; schiebe.Y = 0; if (uhr != null) uhr.Controller.Pause(); var h = huelle; Content = null; return h; } }
        public void KalenderFuerBild() { Kalender(); }
    }
}
