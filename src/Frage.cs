using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace Workbench
{
    /// <summary>
    /// Einheitlicher Pop-up-Stil „Glas-Pille“: dunkel, leicht durchscheinend, feiner Rand, eine Zeile, ganz rund.
    /// Kein Titel, kein Logo, keine gestapelten Kanten.
    /// </summary>
    static class Pille
    {
        public static Border Neu(UIElement inhalt)
        {
            return new Border
            {
                Background = Farbe.B("#E6161920"), BorderBrush = Farbe.B("#1CFFFFFF"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(22), Padding = new Thickness(14, 7, 8, 7), Child = inhalt,
                Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Opacity = 0.35, Direction = 270 }
            };
        }

        /// <summary>Kleines Wort als Knopf in der Pille (z. B. „1d“); aktiv = Bernstein.</summary>
        public static Border Wort(string text, bool an, string tip, Action klick)
        {
            var t = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.Medium, Foreground = an ? Farbe.Akzent : Farbe.Text2, VerticalAlignment = VerticalAlignment.Center };
            var b = new Border { Child = t, Padding = new Thickness(8, 4, 8, 4), CornerRadius = new CornerRadius(9), Background = an ? Farbe.B("#2EF4B860") : Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = tip, VerticalAlignment = VerticalAlignment.Center };
            b.MouseEnter += delegate { if (!an) { b.Background = Farbe.B("#14FFFFFF"); t.Foreground = Farbe.Text; } };
            b.MouseLeave += delegate { if (!an) { b.Background = Brushes.Transparent; t.Foreground = Farbe.Text2; } };
            b.MouseLeftButtonUp += (s, a) => { a.Handled = true; klick(); };
            return b;
        }

        public static Border Symbol(string icon, string tip, Brush farbe, Action klick)
        {
            var b = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = tip, VerticalAlignment = VerticalAlignment.Center, Child = UI.Icon(icon, 14, farbe, 1.8) };
            ((FrameworkElement)b.Child).HorizontalAlignment = HorizontalAlignment.Center; ((FrameworkElement)b.Child).VerticalAlignment = VerticalAlignment.Center;
            b.MouseEnter += delegate { b.Background = Farbe.B("#14FFFFFF"); };
            b.MouseLeave += delegate { b.Background = Brushes.Transparent; };
            b.MouseLeftButtonUp += (s, a) => { a.Handled = true; klick(); };
            return b;
        }
    }

    /// <summary>
    /// Nach einem Download: eine Glas-Pille unten mittig – Datei, Dauern (1h · 1d · 1w · 1m · ∞), Kalender, Cloud.
    /// Nimmt nie den Fokus; ein Klick genügt; läuft die feine Linie ab, gilt still der Vorschlag.
    /// </summary>
    class Frage : Window
    {
        readonly Engine E;
        readonly List<Eintrag> dateien = new List<Eintrag>();
        readonly bool info;
        string vorschlag;
        bool fertig;

        readonly StackPanel stapel = new StackPanel();
        readonly Grid huelle;
        readonly TranslateTransform schiebe = new TranslateTransform(0, 18);
        readonly ContentControl zeile = new ContentControl();
        readonly ContentControl oben = new ContentControl();
        readonly ScaleTransform balken = new ScaleTransform(1, 1);
        readonly Border bahn;
        AnimationClock uhr;

        public bool Offen { get { return !fertig; } }
        public bool IstInfo { get { return info; } }

        static readonly string[][] stationen = { new[] { "1h", "1 hour" }, new[] { "1d", "1 day" }, new[] { "1w", "1 week" }, new[] { "1m", "1 month" }, new[] { "∞", Dauer.Immer } };

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
            SizeToContent = SizeToContent.WidthAndHeight;
            FontFamily = UI.Schrift;
            Title = "Workbench";

            // Ablauf: feine Bernstein-Linie innen an der Unterkante der Pille
            bahn = new Border { Height = 2, CornerRadius = new CornerRadius(1), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(18, 0, 18, 1), IsHitTestVisible = false,
                Background = Farbe.B("#B3F4B860"), RenderTransform = balken, RenderTransformOrigin = new Point(0, 0.5) };
            var pg = new Grid();
            pg.Children.Add(Pille.Neu(zeile));
            pg.Children.Add(bahn);
            stapel.Children.Add(oben);
            stapel.Children.Add(pg);
            huelle = new Grid { Margin = new Thickness(24, 12, 24, 18), RenderTransform = schiebe, Opacity = 0 };
            huelle.Children.Add(stapel);
            Content = huelle;

            pg.MouseEnter += delegate { if (uhr != null && !fertig) uhr.Controller.Pause(); };
            pg.MouseLeave += delegate { if (uhr != null && !fertig && oben.Content == null) uhr.Controller.Resume(); };
            SourceInitialized += delegate { Native.NichtAktivieren(new WindowInteropHelper(this).Handle); };
            SizeChanged += delegate { Platzieren(); };
            Loaded += delegate
            {
                Platzieren();
                huelle.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = UI.Weich });
                schiebe.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = UI.Weich });
            };

            Wahl();
        }

        /// <summary>Unten mittig auf dem Bildschirm mit der Maus – in echten Pixeln gerechnet, damit jede Skalierung stimmt.</summary>
        void Platzieren()
        {
            var src = PresentationSource.FromVisual(this);
            var wa = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea;
            Matrix m = src != null && src.CompositionTarget != null ? src.CompositionTarget.TransformFromDevice : Matrix.Identity;
            var u = m.Transform(new Point(wa.Right, wa.Bottom));
            var o = m.Transform(new Point(wa.Left, wa.Top));
            Left = o.X + (u.X - o.X - ActualWidth) / 2;
            Top = Math.Max(o.Y, u.Y - ActualHeight);
        }

        void Wahl()
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            // Datei: kleine Typ-Marke, Name, bei mehreren „+2“
            var marke = Vorschau.Glyphe(dateien[0].Pfad, 15, 18, true);
            marke.VerticalAlignment = VerticalAlignment.Center;
            sp.Children.Add(marke);
            var name = new TextBlock { Text = dateien[0].Name, FontSize = 12.5, Foreground = Farbe.Text, MaxWidth = 170, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(9, 0, 0, 0), ToolTip = string.Join("\n", dateien.Select(d => d.Name)) };
            sp.Children.Add(name);
            if (dateien.Count > 1) sp.Children.Add(new TextBlock { Text = "+" + (dateien.Count - 1), FontSize = 11.5, Foreground = Farbe.Text3, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
            sp.Children.Add(new Border { Width = 1, Height = 16, Background = Farbe.B("#1FFFFFFF"), Margin = new Thickness(12, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            foreach (var st in stationen)
            {
                string wahl = st[1];
                sp.Children.Add(Pille.Wort(st[0], wahl == vorschlag, wahl == Dauer.Immer ? "Keep" : wahl, () => Anwenden(wahl, null, true)));
            }
            sp.Children.Add(Pille.Symbol(Ikon.Kalender, "Until a date", Farbe.Text2, Kalender));
            var w = Cloud.Aktuell(E.Cfg);
            if (w != null) sp.Children.Add(Pille.Symbol(Ikon.Wolke, "Move to " + w.Name, Farbe.Text2, () => InWolke(w)));
            sp.Children.Add(Pille.Symbol(Ikon.X, "Close", Farbe.Text3, () => { if (info) Schliessen(); else Anwenden(vorschlag, null, false); }));
            zeile.Content = sp;
            oben.Content = null;
            if (uhr == null) Balken(info ? 6 : E.Cfg.Sekunden);
        }

        /// <summary>Kalender als kleines Glas-Feld über der Pille.</summary>
        void Kalender()
        {
            if (uhr != null) uhr.Controller.Pause();
            var k = new Kalender();
            k.Gewaehlt += tag => Anwenden(Dauer.Bis, Dauer.BisTag(tag), true);
            var feld = new Border { Background = Farbe.B("#EB161920"), BorderBrush = Farbe.B("#1CFFFFFF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Center, Child = k };
            oben.Content = feld;
            UI.Einblenden(feld, 6, 160);
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

        /// <summary>Weitere Downloads, während die Pille offen ist: die Wahl gilt dann für alle.</summary>
        public void Hinzufuegen(List<Eintrag> neu)
        {
            if (fertig || info) return;
            dateien.AddRange(neu);
            if (oben.Content == null) Wahl();
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
            Bestaetigen(ablauf == null ? Ikon.Unendlich : Ikon.Haken, ablauf == null ? "Kept" : "Stays until " + Dauer.Wann(ablauf.Value), aktiv);
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
            Bestaetigen(Ikon.Wolke, "Moved to " + w.Name, true);
        }

        /// <summary>Die Pille wird kurz zur Bestätigung (eine Zeile) und gleitet dann weg.</summary>
        void Bestaetigen(string icon, string text, bool aktiv)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 6, 3) };
            sp.Children.Add(UI.Icon(icon, 14, Farbe.Akzent, 2));
            sp.Children.Add(new TextBlock { Text = text, FontSize = 12.5, Foreground = Farbe.Text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
            sp.Children.Add(new TextBlock { Text = dateien[0].Name + (dateien.Count > 1 ? " +" + (dateien.Count - 1) : ""), FontSize = 12, Foreground = Farbe.Text3, MaxWidth = 180, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) });
            oben.Content = null;
            zeile.Content = sp;
            bahn.Visibility = Visibility.Collapsed;
            UI.Einblenden(sp, 3, 160);
            var warte = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(aktiv ? 1100 : 700) };
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
            var a = new DoubleAnimation(huelle.Opacity, 0, TimeSpan.FromMilliseconds(180));
            a.Completed += delegate { Close(); };
            huelle.BeginAnimation(OpacityProperty, a);
            schiebe.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, 12, TimeSpan.FromMilliseconds(200)) { EasingFunction = UI.Weich });
        }

        /// <summary>Nur für Bilder im Render-Modus.</summary>
        public FrameworkElement Karte { get { huelle.Opacity = 1; schiebe.Y = 0; if (uhr != null) uhr.Controller.Pause(); var h = huelle; Content = null; return h; } }
        public void KalenderFuerBild() { Kalender(); }
    }
}
