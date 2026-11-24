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
            Width = 384;
            FontFamily = UI.Schrift;
            Title = "Workbench";

            bahn = new Border { Height = 2, CornerRadius = new CornerRadius(1), Background = Farbe.B("#14FFFFFF"), Margin = new Thickness(2, 14, 2, 0) };
            bahn.Child = new Border { CornerRadius = new CornerRadius(1), Background = Farbe.B("#B38FB4FF"), RenderTransform = balken, RenderTransformOrigin = new Point(0, 0.5) };

            karte = new Border
            {
                Background = Farbe.B("#F21E2128"),
                BorderBrush = Farbe.B("#1AFFFFFF"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(14, 14, 14, 12),
                Margin = new Thickness(20),
                Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 8, Opacity = 0.5, Direction = 270 },
                RenderTransform = schiebe,
                Opacity = 0
            };
            var sp = new StackPanel();
            sp.Children.Add(inhalt);
            sp.Children.Add(bahn);
            karte.Child = sp;
            Content = karte;

            karte.MouseEnter += delegate { if (uhr != null && !fertig) uhr.Controller.Pause(); };
            karte.MouseLeave += delegate { if (uhr != null && !fertig && inhalt.Tag as string != "kalender") uhr.Controller.Resume(); };
            SourceInitialized += delegate { Native.NichtAktivieren(new WindowInteropHelper(this).Handle); };
            SizeChanged += delegate { Platzieren(); };
            Loaded += delegate
            {
                Platzieren();
                karte.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = UI.Weich });
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
                var b = new Button { Style = (Style)Application.Current.FindResource("Rund"), Width = 32, Height = 32, Content = UI.Icon(Ikon.Links, 15, Farbe.Text2, 2), ToolTip = "Zurück", Margin = new Thickness(-6, 0, 6, 0) };
                b.Click += delegate { Wahl(); if (uhr != null && !IsMouseOver) uhr.Controller.Resume(); };
                links = b;
            }
            else
            {
                var bilder = new Grid { Width = 40, Height = 42, Margin = new Thickness(0, 0, 12, 0) };
                int n = Math.Min(3, dateien.Count);
                for (int i = n - 1; i >= 0; i--)
                {
                    var b = Vorschau.Bild(dateien[i].Pfad, 34, 40);
                    b.HorizontalAlignment = HorizontalAlignment.Left;
                    b.Margin = new Thickness(i * 3, 0, 0, 0);
                    if (i > 0) b.Opacity = 0.5;
                    bilder.Children.Add(b);
                }
                links = bilder;
            }
            g.Children.Add(links);
            var name = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var t = UI.T(dateien[0].Name, 13.5, Farbe.Text, FontWeights.SemiBold);
            t.MaxWidth = dateien.Count > 1 ? 210 : 250;
            name.Children.Add(t);
            if (dateien.Count > 1)
            {
                name.Children.Add(new Border { CornerRadius = new CornerRadius(9), Background = Farbe.B("#1FFFFFFF"), Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Child = UI.T("+" + (dateien.Count - 1), 11, Farbe.Text2, FontWeights.SemiBold) });
            }
            Grid.SetColumn(name, 1);
            g.Children.Add(name);
            var x = new Button { Style = (Style)Application.Current.FindResource("Rund"), Width = 30, Height = 30, Content = UI.Icon(Ikon.X, 12, Farbe.Text3, 2), Margin = new Thickness(0, -4, -6, -4), VerticalAlignment = VerticalAlignment.Top };
            System.Windows.Automation.AutomationProperties.SetName(x, "Schließen");
            x.Click += delegate { if (info) Schliessen(); else Anwenden(vorschlag, null, false); };
            Grid.SetColumn(x, 2);
            g.Children.Add(x);
            return g;
        }

        void Wahl()
        {
            var sp = new StackPanel();
            sp.Children.Add(Kopf(false));
            var reihe1 = new UniformGrid { Columns = 4, Margin = new Thickness(-3, 12, -3, 0) };
            foreach (var n in Dauer.Auswahl) reihe1.Children.Add(Knopf(n, null, null));
            sp.Children.Add(reihe1);
            var w = Cloud.Aktuell(E.Cfg);
            var reihe2 = new UniformGrid { Columns = w != null ? 3 : 2, Margin = new Thickness(-3, 0, -3, 0) };
            reihe2.Children.Add(Knopf(Dauer.Bis, Ikon.Kalender, "Bis …"));
            reihe2.Children.Add(Knopf(Dauer.Immer, Ikon.Unendlich, "Für immer"));
            if (w != null) reihe2.Children.Add(Knopf("wolke", Ikon.Wolke, w.Name));
            sp.Children.Add(reihe2);
            inhalt.Tag = "wahl";
            inhalt.Content = sp;
            if (uhr == null) Balken(info ? 6 : E.Cfg.Sekunden);
        }

        ToggleButton Knopf(string name, string icon, string hilfe)
        {
            var t = new ToggleButton { Style = (Style)Application.Current.FindResource("Wahl"), Margin = new Thickness(3), Height = 38, FontSize = 13 };
            bool an = name == vorschlag;
            t.IsChecked = an;
            if (icon == null) t.Content = name;
            else
            {
                t.Content = UI.Icon(icon, 17, an ? Farbe.AkzentText : Farbe.Text, 1.8);
                t.ToolTip = hilfe;
                ToolTipService.SetPlacement(t, PlacementMode.Top);
            }
            System.Windows.Automation.AutomationProperties.SetName(t, hilfe ?? name);
            t.Click += delegate
            {
                t.IsChecked = true;
                if (name == Dauer.Bis) { Kalender(); return; }
                if (name == "wolke") { InWolke(Cloud.Aktuell(E.Cfg)); return; }
                Anwenden(name, null, true);
            };
            return t;
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
                E.FristSetzen(e, ablauf, wahl == Dauer.Bis ? "bis " + bis.Value.ToLocalTime().ToString("dd.MM.") : wahl);
            }
            if (aktiv) E.S.Speichern();
            string text = ablauf == null ? "Dauerhaft" : (wahl == Dauer.Bis ? "bis " + bis.Value.ToLocalTime().ToString("dd.MM.") : wahl);
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
            var a = new DoubleAnimation(karte.Opacity, 0, TimeSpan.FromMilliseconds(200));
            a.Completed += delegate { Close(); };
            karte.BeginAnimation(OpacityProperty, a);
            schiebe.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, 16, TimeSpan.FromMilliseconds(220)) { EasingFunction = UI.Weich });
        }

        /// <summary>Nur für Bilder im Render-Modus.</summary>
        public FrameworkElement Karte { get { karte.Opacity = 1; schiebe.Y = 0; if (uhr != null) uhr.Controller.Pause(); return karte; } }
        public void KalenderFuerBild() { Kalender(); }
    }
}
