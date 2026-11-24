using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Workbench
{
    /// <summary>
    /// Das Workbench-Fenster fühlt sich an wie ein zweiter Desktop: dein Hintergrundbild, Symbole mit Namen, sonst fast nichts.
    /// Links eine schmale Punktleiste für die Ebenen (Befristet · Dauerhaft · Cloud), darunter Papierkorb und Einstellungen.
    /// </summary>
    class Hauptfenster : Window
    {
        public const int Befristet = 0, Dauerhaft = 1, Wolke = 2, Korb = 3, Einst = 4;
        const int Ebenen = 3;
        const double Abstand = 34;

        readonly Engine E;
        readonly bool bild;
        int ansicht = -1, letzteEbene;
        CloudOrt wolke;
        string ordnerDauerhaft, ordnerWolke;

        readonly Grid wurzel = new Grid();
        readonly Grid buehne = new Grid();
        readonly Border blatt = new Border();
        readonly TranslateTransform blattY = new TranslateTransform();
        readonly Border[] hinterBlatt = new Border[2];
        readonly StackPanel ort = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        TextBox suche;
        Border suchFeld;
        readonly TranslateTransform knopfY = new TranslateTransform();
        FrameworkElement knopf;
        readonly List<Button> stufen = new List<Button>();
        Button btnKorb, btnEinst;
        Ellipse korbPunkt;
        readonly Border leiste = new Border();
        readonly StackPanel leisteInhalt = new StackPanel { Orientation = Orientation.Horizontal };
        readonly DispatcherTimer minuten;

        string auswahl;
        readonly Dictionary<string, Border> symbole = new Dictionary<string, Border>(StringComparer.OrdinalIgnoreCase);
        List<BinEintrag> korb;
        bool korbLiest;

        static readonly Brush Label = Farbe.B("#F2FFFFFF");
        static readonly Brush LabelLeise = Farbe.B("#99FFFFFF");
        static readonly Brush Auswahl = Farbe.B("#2BFFFFFF");
        static readonly Brush AuswahlRand = Farbe.B("#4DFFFFFF");
        static readonly Brush Schwebe = Farbe.B("#1FFFFFFF");

        public FrameworkElement Wurzel { get { return wurzel; } }

        public Hauptfenster(Engine e, int start, bool renderModus)
        {
            E = e;
            bild = renderModus;
            wolke = Cloud.Aktuell(e.Cfg);

            Title = "Workbench";
            Width = 1060; Height = 680;
            MinWidth = 640; MinHeight = 420;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = UI.Schrift;
            Foreground = Farbe.Text;
            UseLayoutRounding = true;
            Background = Farbe.B("#0E1014");

            wurzel.Children.Add(Hintergrund());
            var lagen = new Grid();
            lagen.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
            lagen.RowDefinitions.Add(new RowDefinition());
            lagen.Children.Add(Kopfleiste());
            var mitte = new Grid();
            mitte.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            mitte.ColumnDefinitions.Add(new ColumnDefinition());
            mitte.Children.Add(Schiene());
            // Der Ebenen-Stapel: die aktuelle Fläche liegt als Blatt oben, die Kanten der übrigen schauen darüber hervor.
            var stapel = new Grid { Margin = new Thickness(2, 2, 18, 18) };
            for (int i = 0; i < 2; i++)
            {
                // nur die Oberkante eines Blattes – schaut über dem vorderen hervor, scheint nicht hindurch
                hinterBlatt[i] = new Border { CornerRadius = new CornerRadius(16, 16, 0, 0), Background = Farbe.B(i == 0 ? "#0DFFFFFF" : "#07FFFFFF"), BorderBrush = Farbe.B(i == 0 ? "#1FFFFFFF" : "#14FFFFFF"), BorderThickness = new Thickness(1, 1, 1, 0), Height = 8, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(18 + i * 18, 6 - i * 6, 18 + i * 18, 0), IsHitTestVisible = false };
                stapel.Children.Insert(0, hinterBlatt[i]);
            }
            blatt.CornerRadius = new CornerRadius(18);
            blatt.Background = Farbe.B("#10FFFFFF");
            blatt.BorderBrush = Farbe.B("#17FFFFFF");
            blatt.BorderThickness = new Thickness(1);
            blatt.Margin = new Thickness(0, 12, 0, 0);
            blatt.Padding = new Thickness(14, 12, 14, 12);
            blatt.RenderTransform = blattY;
            buehne.Background = Brushes.Transparent;
            blatt.Child = buehne;
            stapel.Children.Add(blatt);
            Grid.SetColumn(stapel, 1);
            mitte.Children.Add(stapel);
            Grid.SetRow(mitte, 1);
            lagen.Children.Add(mitte);
            wurzel.Children.Add(lagen);

            leiste.HorizontalAlignment = HorizontalAlignment.Center;
            leiste.VerticalAlignment = VerticalAlignment.Bottom;
            leiste.Margin = new Thickness(60, 0, 0, 22);
            leiste.CornerRadius = new CornerRadius(24);
            leiste.Padding = new Thickness(6);
            leiste.Background = Farbe.B("#E61C1F26");
            leiste.BorderBrush = Farbe.B("#1FFFFFFF");
            leiste.BorderThickness = new Thickness(1);
            leiste.Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Opacity = 0.45 };
            leiste.Child = leisteInhalt;
            leiste.Visibility = Visibility.Collapsed;
            leiste.RenderTransform = new TranslateTransform();
            wurzel.Children.Add(leiste);

            if (!bild)
            {
                Content = wurzel;
                WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 44, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), UseAeroCaptionButtons = false, CornerRadius = new CornerRadius(0) });
                SourceInitialized += delegate
                {
                    var h = new WindowInteropHelper(this).Handle;
                    Native.DunklesFenster(h, false);
                    HwndSource.FromHwnd(h).AddHook(Wischen);
                };
                StateChanged += delegate { wurzel.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0); };
                PreviewKeyDown += Tasten;
                TextInput += Tippen;
                Activated += delegate { if (ansicht == Befristet) Neuzeichnen(); };
                Loaded += delegate { KorbLesen(); };
                buehne.MouseLeftButtonDown += (s, a) => { if (a.Source == buehne || a.OriginalSource is ScrollViewer || a.OriginalSource is WrapPanel || a.OriginalSource is Grid) Waehle(null); };
            }
            minuten = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            minuten.Tick += delegate { if (ansicht == Befristet) Neuzeichnen(); };
            if (!bild) IsVisibleChanged += delegate { if (IsVisible) minuten.Start(); else minuten.Stop(); };
            Zeige(start);
        }

        // ================= Rahmen =================

        /// <summary>Dein eigenes Desktop-Hintergrundbild – weich und abgedunkelt. Ohne Bild: ruhiger Verlauf.</summary>
        /// <summary>
        /// Leistungssparend: das Bild wird nur 48 px breit geladen und weich hochskaliert – ergibt von selbst eine Unschärfe,
        /// ohne dass die Grafikkarte bei jedem Bild einen Weichzeichner rechnet.
        /// </summary>
        FrameworkElement Hintergrund()
        {
            var g = new Grid { ClipToBounds = true, Background = Farbe.B("#0E1015") };
            var datei = Vorschau.Sofort ? null : Tapete();
            bool bildDa = false;
            if (datei != null)
            {
                try
                {
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.UriSource = new Uri(datei);
                    bi.DecodePixelWidth = 48;
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                    bi.EndInit();
                    bi.Freeze();
                    var img = new Image { Source = bi, Stretch = Stretch.UniformToFill, Margin = new Thickness(-30) };
                    RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                    g.Children.Add(img);
                    bildDa = true;
                }
                catch (Exception ex) { Log.Fehler("Hintergrundbild", ex); }
            }
            if (!bildDa)
            {
                // eigener ruhiger Grund: Tinte mit zwei weichen Lichtern (statisch, kostet nichts)
                g.Children.Add(new Border { Background = new RadialGradientBrush(Color.FromArgb(70, 0xF4, 0xB8, 0x60), Colors.Transparent) { Center = new Point(0.12, 1.05), GradientOrigin = new Point(0.12, 1.05), RadiusX = 0.7, RadiusY = 0.8 } });
                g.Children.Add(new Border { Background = new RadialGradientBrush(Color.FromArgb(60, 0x3E, 0x8E, 0x9C), Colors.Transparent) { Center = new Point(0.95, -0.05), GradientOrigin = new Point(0.95, -0.05), RadiusX = 0.75, RadiusY = 0.85 } });
            }
            g.Children.Add(new Border { Background = Farbe.B(bildDa ? "#A60B0C10" : "#330B0C10") });
            return g;
        }

        static string Tapete()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop"))
                {
                    var p = k == null ? null : k.GetValue("WallPaper") as string;
                    if (!string.IsNullOrEmpty(p) && File.Exists(p)) return p;
                }
                string t = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Themes\TranscodedWallpaper");
                if (File.Exists(t)) return t;
            }
            catch { }
            return null;
        }

        FrameworkElement Kopfleiste()
        {
            var g = new Grid();
            ort.Margin = new Thickness(64, 0, 0, 0);
            WindowChrome.SetIsHitTestVisibleInChrome(ort, true);
            g.Children.Add(ort);

            var rechts = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            suchFeld = new Border { Height = 30, Width = 30, CornerRadius = new CornerRadius(15), Background = Brushes.Transparent, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.IBeam };
            var sg = new Grid();
            var lupe = UI.Icon(Ikon.Suche, 15, LabelLeise, 2);
            lupe.HorizontalAlignment = HorizontalAlignment.Left;
            lupe.Margin = new Thickness(8, 0, 0, 0);
            sg.Children.Add(lupe);
            suche = new TextBox { Style = (Style)Application.Current.FindResource("Suchfeld"), Margin = new Thickness(30, 0, 10, 0) };
            System.Windows.Automation.AutomationProperties.SetName(suche, "Suchen");
            sg.Children.Add(suche);
            suchFeld.Child = sg;
            suchFeld.ToolTip = "Suchen (Strg+F)";
            suchFeld.MouseLeftButtonDown += delegate { SucheAuf(); };
            suche.TextChanged += delegate { Neuzeichnen(); };
            suche.LostKeyboardFocus += delegate { if (suche.Text.Length == 0) SucheZu(); };
            WindowChrome.SetIsHitTestVisibleInChrome(suchFeld, true);
            rechts.Children.Add(suchFeld);
            rechts.Children.Add(FensterKnopf(Ikon.Minus, "Minimieren", "Fensterknopf", () => WindowState = WindowState.Minimized));
            rechts.Children.Add(FensterKnopf(Ikon.Quadrat, "Maximieren", "Fensterknopf", () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized));
            rechts.Children.Add(FensterKnopf(Ikon.X, "Schließen", "FensterknopfX", Schliessen));
            g.Children.Add(rechts);
            return g;
        }

        void SucheAuf()
        {
            suchFeld.Background = Farbe.B("#26FFFFFF");
            suchFeld.BeginAnimation(WidthProperty, new DoubleAnimation(220, TimeSpan.FromMilliseconds(180)) { EasingFunction = UI.Weich });
            suche.Focus();
        }

        void SucheZu()
        {
            suchFeld.Background = Brushes.Transparent;
            suchFeld.BeginAnimation(WidthProperty, new DoubleAnimation(30, TimeSpan.FromMilliseconds(160)) { EasingFunction = UI.Weich });
        }

        Button FensterKnopf(string icon, string name, string stil, Action a)
        {
            var b = new Button { Style = (Style)Application.Current.FindResource(stil), Content = UI.Icon(icon, 11, Farbe.B("#D9FFFFFF"), 1.6), ToolTip = name };
            System.Windows.Automation.AutomationProperties.SetName(b, name);
            WindowChrome.SetIsHitTestVisibleInChrome(b, true);
            b.Click += delegate { a(); };
            return b;
        }

        /// <summary>Schmale Punktleiste: drei Ebenen. Namen nur beim Darüberfahren.</summary>
        FrameworkElement Schiene()
        {
            var g = new Grid();
            var regler = new Grid { Width = 40, Height = 28 + (Ebenen - 1) * Abstand, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Background = Brushes.Transparent, Margin = new Thickness(0, -60, 0, 0) };
            regler.Children.Add(new Border { Width = 2, Height = (Ebenen - 1) * Abstand, Background = Farbe.B("#33FFFFFF"), CornerRadius = new CornerRadius(1), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 14, 0, 0), IsHitTestVisible = false });
            for (int i = 0; i < Ebenen; i++)
            {
                regler.Children.Add(new Ellipse { Width = 7, Height = 7, Fill = Farbe.B("#80FFFFFF"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 14 - 3.5 + i * Abstand, 0, 0), IsHitTestVisible = false });
            }
            var k = new Ellipse { Width = 14, Height = 14, Fill = Farbe.B("#F2FFFFFF"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 7, 0, 0), IsHitTestVisible = false, RenderTransform = knopfY, Effect = new DropShadowEffect { Color = Colors.White, BlurRadius = 12, ShadowDepth = 0, Opacity = 0.55 } };
            knopf = k;
            string[] namen = { "Befristet", "Dauerhaft", wolke != null ? wolke.Name : "Cloud" };
            for (int i = 0; i < Ebenen; i++)
            {
                int ebene = i;
                var b = new Button { Style = (Style)Application.Current.FindResource("Leer"), Width = 40, Height = 28, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, i * Abstand, 0, 0), ToolTip = namen[i] };
                ToolTipService.SetPlacement(b, PlacementMode.Right);
                ToolTipService.SetInitialShowDelay(b, 150);
                System.Windows.Automation.AutomationProperties.SetName(b, namen[i]);
                b.Click += delegate { Zeige(ebene); };
                if (i == 1) AblageZiel(b, null, () => Paths.Dauerhaft);
                if (i == 2) AblageZiel(b, null, () => wolke != null ? wolke.Pfad : null);
                stufen.Add(b);
                regler.Children.Add(b);
            }
            regler.Children.Add(k);
            regler.MouseWheel += (s, e) => { Schritt(e.Delta < 0 ? 1 : -1); e.Handled = true; };
            g.Children.Add(regler);

            var unten = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 16) };
            btnKorb = RundKnopf(Ikon.Muell, "Papierkorb", () => Zeige(ansicht == Korb ? letzteEbene : Korb));
            var kg = new Grid();
            kg.Children.Add(btnKorb);
            korbPunkt = new Ellipse { Width = 7, Height = 7, Fill = Farbe.Bald, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 7, 7, 0), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
            kg.Children.Add(korbPunkt);
            AblageZielKorb(btnKorb);
            btnEinst = RundKnopf(Ikon.Regler, "Einstellungen", () => Zeige(ansicht == Einst ? letzteEbene : Einst));
            btnEinst.Margin = new Thickness(0, 6, 0, 0);
            unten.Children.Add(kg);
            unten.Children.Add(btnEinst);
            g.Children.Add(unten);
            return g;
        }

        Button RundKnopf(string icon, string name, Action a)
        {
            var b = new Button { Style = (Style)Application.Current.FindResource("Rund"), Content = UI.Icon(icon, 17, LabelLeise, 1.8), ToolTip = name };
            ToolTipService.SetPlacement(b, PlacementMode.Right);
            System.Windows.Automation.AutomationProperties.SetName(b, name);
            b.Click += delegate { a(); };
            return b;
        }

        // ================= Ebenen wechseln =================

        public void Zeige(int neu)
        {
            bool wechsel = neu != ansicht;
            int richtung = neu < Ebenen && ansicht >= 0 && ansicht < Ebenen ? Math.Sign(neu - ansicht) : 0;
            ansicht = neu;
            if (neu < Ebenen) letzteEbene = neu;
            double ziel = letzteEbene * Abstand;
            if (bild) { knopfY.Y = ziel; knopf.Opacity = neu < Ebenen ? 1 : 0.35; }
            else
            {
                knopfY.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(ziel, TimeSpan.FromMilliseconds(300)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 } });
                knopf.BeginAnimation(OpacityProperty, new DoubleAnimation(neu < Ebenen ? 1 : 0.35, TimeSpan.FromMilliseconds(180)));
            }
            btnKorb.Background = neu == Korb ? Schwebe : Brushes.Transparent;
            btnEinst.Background = neu == Einst ? Schwebe : Brushes.Transparent;
            // so viele Blattkanten, wie Ebenen darunter liegen
            int darunter = neu < Ebenen ? Ebenen - 1 - neu : 0;
            for (int i = 0; i < 2; i++)
            {
                double ziel2 = i < darunter ? 1 : 0;
                if (bild) hinterBlatt[i].Opacity = ziel2;
                else hinterBlatt[i].BeginAnimation(OpacityProperty, new DoubleAnimation(ziel2, TimeSpan.FromMilliseconds(260)));
            }
            if (wechsel) { auswahl = null; if (suche.Text.Length > 0) suche.Text = ""; SucheZu(); }
            Neuzeichnen();
            if (wechsel && !bild)
            {
                // das Blatt gleitet: tiefer = von unten herein, zurück = von oben
                double von = richtung == 0 ? 10 : richtung * 40;
                blattY.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(von, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = UI.Weich });
                blatt.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(260)));
            }
        }

        void Schritt(int d)
        {
            int von = ansicht < Ebenen ? ansicht : letzteEbene;
            int neu = Math.Max(0, Math.Min(Ebenen - 1, von + d));
            if (neu != ansicht) Zeige(neu);
        }

        public void Auffrischen()
        {
            if (ansicht == Korb) KorbLesen(); else { Neuzeichnen(); KorbLesen(); }
        }

        void Neuzeichnen()
        {
            if (ansicht < 0) return;
            symbole.Clear();
            FrameworkElement v;
            switch (ansicht)
            {
                case Befristet: v = FlaecheBefristet(); break;
                case Dauerhaft: v = FlaecheOrdner(false); break;
                case Wolke: v = FlaecheOrdner(true); break;
                case Korb: v = FlaecheKorb(); break;
                default: v = FlaecheEinstellungen(); break;
            }
            buehne.Children.Clear();
            buehne.Children.Add(v);
            if (auswahl != null && !symbole.ContainsKey(auswahl)) auswahl = null;
            Border sb;
            if (auswahl != null && symbole.TryGetValue(auswahl, out sb)) Stil(sb, true, false);
            LeisteFuellen();
        }

        string Filter { get { return suche == null ? "" : suche.Text.Trim(); } }
        bool Passt(string name) { return Filter.Length == 0 || name.IndexOf(Filter, StringComparison.OrdinalIgnoreCase) >= 0; }

        void OrtSetzen(string titel)
        {
            ort.Children.Clear();
            ort.Children.Add(new TextBlock { Text = titel, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = LabelLeise, VerticalAlignment = VerticalAlignment.Center });
        }

        // ================= Symbole =================

        /// <summary>Ein Desktop-Symbol: Bild, Name darunter, optional ein kleines Abzeichen (Restzeit).</summary>
        Border Symbol(string pfad, string bildPfad, string name, double? anteil, Brush ringFarbe, bool nurSymbol, string hilfe)
        {
            var b = new Border { Width = 92, Padding = new Thickness(4, 8, 4, 6), Margin = new Thickness(1), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, ToolTip = hilfe ?? name, Tag = pfad };
            ToolTipService.SetInitialShowDelay(b, 700);
            var sp = new StackPanel();
            var kopf = new Grid { Width = 56, Height = 50, HorizontalAlignment = HorizontalAlignment.Center };
            var bild2 = Vorschau.Bild(bildPfad, 48, 48, nurSymbol);
            bild2.HorizontalAlignment = HorizontalAlignment.Center;
            bild2.VerticalAlignment = VerticalAlignment.Center;
            kopf.Children.Add(bild2);
            if (anteil.HasValue)
            {
                var r = Zeitring(anteil.Value, ringFarbe, 18);
                r.HorizontalAlignment = HorizontalAlignment.Right;
                r.VerticalAlignment = VerticalAlignment.Top;
                r.Margin = new Thickness(0, -5, -7, 0);
                kopf.Children.Add(r);
            }
            sp.Children.Add(kopf);
            var t = new TextBlock
            {
                Text = name, FontSize = 12, Foreground = Label, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 32, LineHeight = 16, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                Margin = new Thickness(0, 6, 0, 0)
            };
            sp.Children.Add(t);
            b.Child = sp;
            b.MouseEnter += delegate { Stil(b, auswahl == pfad, true); };
            b.MouseLeave += delegate { Stil(b, auswahl == pfad, false); };
            symbole[pfad] = b;
            return b;
        }

        /// <summary>Der Zeitring: wie viel von der gewählten Zeit noch übrig ist – ohne eine Zahl.</summary>
        static FrameworkElement Zeitring(double anteil, Brush farbe, double d)
        {
            anteil = Math.Max(0.04, Math.Min(1, anteil));
            var g = new Grid { Width = d, Height = d, IsHitTestVisible = false };
            g.Children.Add(new Ellipse { Fill = Farbe.B("#E614161B") });
            double c = d / 2, r = c - 3;
            g.Children.Add(new Ellipse { Width = r * 2, Height = r * 2, Stroke = Farbe.B("#2EFFFFFF"), StrokeThickness = 2 });
            if (anteil >= 0.995) g.Children.Add(new Ellipse { Width = r * 2, Height = r * 2, Stroke = farbe, StrokeThickness = 2 });
            else
            {
                double w = anteil * 2 * Math.PI;
                var start = new Point(c, c - r);
                var ende = new Point(c + r * Math.Sin(w), c - r * Math.Cos(w));
                var fig = new PathFigure { StartPoint = start, IsClosed = false };
                fig.Segments.Add(new ArcSegment(ende, new Size(r, r), 0, w > Math.PI, SweepDirection.Clockwise, true));
                var geo = new PathGeometry();
                geo.Figures.Add(fig);
                geo.Freeze();
                g.Children.Add(new System.Windows.Shapes.Path { Data = geo, Stroke = farbe, StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
            }
            return g;
        }

        static void Stil(Border b, bool gewaehlt, bool hover)
        {
            b.Background = gewaehlt ? Auswahl : (hover ? Schwebe : Brushes.Transparent);
            b.BorderBrush = gewaehlt ? AuswahlRand : Brushes.Transparent;
        }

        void Waehle(string pfad)
        {
            Border b;
            if (auswahl != null && symbole.TryGetValue(auswahl, out b)) Stil(b, false, false);
            auswahl = pfad;
            if (pfad != null && symbole.TryGetValue(pfad, out b)) Stil(b, true, true);
            LeisteFuellen();
        }

        /// <summary>Klick wählt, Doppelklick öffnet, Ziehen nimmt die echte Datei mit.</summary>
        void Bedienbar(Border el, string pfad, Action doppel, Action<FrameworkElement> kontext)
        {
            Point start = new Point();
            bool gedrueckt = false;
            el.MouseLeftButtonDown += (s, a) =>
            {
                a.Handled = true;
                if (a.ClickCount == 2) { gedrueckt = false; doppel(); return; }
                start = a.GetPosition(this);
                gedrueckt = true;
                Waehle(pfad);
            };
            el.MouseLeftButtonUp += delegate { gedrueckt = false; };
            el.MouseMove += (s, a) =>
            {
                if (!gedrueckt || a.LeftButton != MouseButtonState.Pressed) return;
                var p = a.GetPosition(this);
                if (Math.Abs(p.X - start.X) < 6 && Math.Abs(p.Y - start.Y) < 6) return;
                gedrueckt = false;
                string echt = pfad;
                if (ansicht == Korb) return;
                if (!File.Exists(echt) && !Directory.Exists(echt)) return;
                try { DragDrop.DoDragDrop(el, new DataObject(DataFormats.FileDrop, new[] { echt }), DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link); } catch { }
            };
            el.MouseRightButtonUp += (s, a) => { Waehle(pfad); kontext(el); a.Handled = true; };
        }

        static WrapPanel Spalten()
        {
            return new WrapPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Stretch };
        }

        static ScrollViewer Rollen(UIElement inhalt)
        {
            return new ScrollViewer { Content = inhalt, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
        }

        static FrameworkElement Leer(string icon)
        {
            var i = UI.Icon(icon, 40, Farbe.B("#40FFFFFF"), 1.4);
            i.HorizontalAlignment = HorizontalAlignment.Center;
            i.VerticalAlignment = VerticalAlignment.Center;
            i.Margin = new Thickness(0, 0, 0, 40);
            return i;
        }

        // ================= Ebene 1: Befristet =================

        FrameworkElement FlaecheBefristet()
        {
            OrtSetzen("Befristet");
            var jetzt = DateTime.UtcNow;
            var liste = E.S.D.Eintraege.Where(x => x.Ablauf.HasValue && (File.Exists(x.Pfad) || Directory.Exists(x.Pfad)) && Passt(x.Name)).OrderBy(x => x.Ablauf.Value).ToList();
            if (liste.Count == 0) return Leer(Filter.Length > 0 ? Ikon.Suche : Ikon.Haken);

            var reihe = new StackPanel { Orientation = Orientation.Horizontal };
            string[] titel = { "Heute", "Diese Woche", "Später" };
            Brush[] ring = { Farbe.Akzent, Farbe.B("#E6FFFFFF"), Farbe.B("#80FFFFFF") };
            for (int s = 0; s < 3; s++)
            {
                int s2 = s;
                var teil = liste.Where(x => Dauer.Spalte(x.Ablauf.Value, jetzt) == s2).ToList();
                if (teil.Count == 0) continue;
                var dp = new DockPanel { Margin = new Thickness(0, 0, 22, 0) };
                var t = new TextBlock { Text = titel[s], FontSize = 11, Foreground = LabelLeise, Margin = new Thickness(8, 0, 0, 6) };
                DockPanel.SetDock(t, Dock.Top);
                dp.Children.Add(t);
                var wp = Spalten();
                foreach (var e in teil)
                {
                    var e2 = e;
                    double gesamt = Math.Max(1, (e.Ablauf.Value - e.Hinzu).TotalSeconds);
                    double rest = (e.Ablauf.Value - jetzt).TotalSeconds / gesamt;
                    var sym = Symbol(e.Pfad, e.Pfad, e.Name, rest, ring[s], false, e.Name + "\nnoch " + Kurz(e.Ablauf.Value, jetzt) + " · " + Dauer.Wann(e.Ablauf.Value));
                    Bedienbar(sym, e.Pfad, () => Oeffnen(e2.Pfad), el => KontextBefristet(el, e2));
                    wp.Children.Add(sym);
                }
                dp.Children.Add(wp);
                reihe.Children.Add(dp);
            }
            return Rollen(reihe);
        }

        /// <summary>Kurze Restzeit fürs Abzeichen: 42 min · 5 h · 3 T · 2 W · 15.01.</summary>
        static string Kurz(DateTime ablaufUtc, DateTime jetztUtc)
        {
            var r = ablaufUtc - jetztUtc;
            if (r.TotalMinutes < 60) return Math.Max(1, (int)Math.Ceiling(r.TotalMinutes)) + " min";
            if (r.TotalHours < 24) return (int)Math.Round(r.TotalHours) + " h";
            if (r.TotalDays < 14) return (int)Math.Round(r.TotalDays) + " T";
            if (r.TotalDays < 60) return (int)Math.Round(r.TotalDays / 7) + " W";
            return ablaufUtc.ToLocalTime().ToString("dd.MM.");
        }

        Eintrag Gewaehlt { get { return auswahl == null ? null : E.S.Finde(auswahl); } }

        void KontextBefristet(FrameworkElement ziel, Eintrag e)
        {
            var m = new List<KeyValuePair<string, Action>>
            {
                new KeyValuePair<string, Action>("Öffnen", () => Oeffnen(e.Pfad)),
                new KeyValuePair<string, Action>("Im Ordner zeigen", () => ImOrdnerZeigen(e.Pfad)),
                new KeyValuePair<string, Action>("-", null),
                new KeyValuePair<string, Action>("+ 1 Tag", () => Verlaengern(e, "1 Tag")),
                new KeyValuePair<string, Action>("+ 1 Woche", () => Verlaengern(e, "1 Woche")),
                new KeyValuePair<string, Action>("+ 1 Monat", () => Verlaengern(e, "1 Monat")),
                new KeyValuePair<string, Action>("Bis …", () => BisWaehlen(ziel, e)),
                new KeyValuePair<string, Action>("-", null),
                new KeyValuePair<string, Action>("Dauerhaft behalten", () => Wegschieben(e, Paths.Dauerhaft)),
            };
            if (wolke != null) m.Add(new KeyValuePair<string, Action>("In " + wolke.Name, () => Wegschieben(e, wolke.Pfad)));
            m.Add(new KeyValuePair<string, Action>("In den Papierkorb", () => Wegwerfen(e)));
            UI.Menue(ziel, m);
        }

        void Verlaengern(Eintrag e, string um)
        {
            var basis = e.Ablauf.HasValue && e.Ablauf.Value > DateTime.UtcNow ? e.Ablauf.Value : DateTime.UtcNow;
            E.FristSetzen(e, Dauer.Ablauf(um, basis), e.Gewaehlt);
            Neuzeichnen();
        }

        void BisWaehlen(FrameworkElement ziel, Eintrag e)
        {
            var pop = new Popup { PlacementTarget = ziel, Placement = PlacementMode.Top, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade, VerticalOffset = -8 };
            var k = new Kalender();
            k.Gewaehlt += tag => { pop.IsOpen = false; E.FristSetzen(e, Dauer.BisTag(tag), "bis " + tag.ToString("dd.MM.")); Neuzeichnen(); };
            pop.Child = new Border { Width = 330, Background = Farbe.B("#FA22252D"), BorderBrush = Farbe.B("#1FFFFFFF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(14), Margin = new Thickness(12), Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Opacity = 0.5 }, Child = k };
            pop.IsOpen = true;
        }

        void Wegschieben(Eintrag e, string ordner)
        {
            Action los = () => { E.Verschieben(e, ordner); auswahl = null; Neuzeichnen(); };
            Ausblenden(e.Pfad, -12, los);
        }

        void Wegwerfen(Eintrag e)
        {
            Action los = () => { E.JetztWeg(e); auswahl = null; Neuzeichnen(); KorbLesen(); };
            Ausblenden(e.Pfad, 12, los);
        }

        void Ausblenden(string pfad, double y, Action danach)
        {
            Border b;
            if (bild || !symbole.TryGetValue(pfad, out b)) { danach(); return; }
            var tt = new TranslateTransform();
            b.RenderTransform = tt;
            tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, y, TimeSpan.FromMilliseconds(170)) { EasingFunction = UI.Weich });
            UI.Ausblenden(b, 170, danach);
        }

        // ================= Ebene 2 + 3: Dauerhaft und Cloud =================

        string WurzelVon(bool cloud) { return cloud ? (wolke != null ? wolke.Pfad : null) : Paths.Dauerhaft; }

        string AktOrdner(bool cloud)
        {
            string w = WurzelVon(cloud);
            if (w == null) return null;
            string akt = cloud ? ordnerWolke : ordnerDauerhaft;
            if (akt == null || !Directory.Exists(akt) || !akt.StartsWith(w, StringComparison.OrdinalIgnoreCase)) return w;
            return akt;
        }

        void Betreten(bool cloud, string ordner)
        {
            if (cloud) ordnerWolke = ordner; else ordnerDauerhaft = ordner;
            auswahl = null;
            if (Filter.Length > 0) suche.Text = "";
            Neuzeichnen();
            if (!bild && buehne.Children.Count > 0) UI.Einblenden(buehne.Children[0], 6, 180);
        }

        FrameworkElement FlaecheOrdner(bool cloud)
        {
            if (cloud && wolke == null) { OrtSetzen("Cloud"); return KeineWolke(); }
            string w = WurzelVon(cloud), akt = AktOrdner(cloud);

            // Brotkrumen klein oben links – nur Namen, klickbar
            ort.Children.Clear();
            var teile = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>(cloud ? wolke.Name : "Dauerhaft", w) };
            string rel = akt.Length > w.Length ? akt.Substring(w.Length).Trim('\\') : "", lauf = w;
            if (rel.Length > 0) foreach (var t in rel.Split('\\')) { lauf = System.IO.Path.Combine(lauf, t); teile.Add(new KeyValuePair<string, string>(t, lauf)); }
            for (int i = 0; i < teile.Count; i++)
            {
                bool letzt = i == teile.Count - 1;
                if (i > 0) ort.Children.Add(new TextBlock { Text = "›", FontSize = 13, Foreground = Farbe.B("#66FFFFFF"), Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
                var tb = new TextBlock { Text = teile[i].Key, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = letzt ? Farbe.B("#E6FFFFFF") : LabelLeise, VerticalAlignment = VerticalAlignment.Center, Cursor = letzt ? null : Cursors.Hand };
                if (!letzt)
                {
                    string ziel = teile[i].Value;
                    tb.MouseLeftButtonUp += delegate { Betreten(cloud, ziel); };
                    tb.AllowDrop = true;
                    AblageZiel(tb, null, () => ziel);
                }
                ort.Children.Add(tb);
            }

            var eintraege = new List<string>();
            try
            {
                foreach (var d in Directory.EnumerateDirectories(akt).OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)) if (!Engine.IstHilfsdatei(d) && Passt(System.IO.Path.GetFileName(d))) eintraege.Add(d);
                foreach (var f in Directory.EnumerateFiles(akt).OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)) if (!Engine.IstHilfsdatei(f) && Passt(System.IO.Path.GetFileName(f))) eintraege.Add(f);
            }
            catch (Exception ex) { Log.Fehler("Ordner lesen", ex); }

            var flaeche = new Grid { Background = Brushes.Transparent };
            if (eintraege.Count == 0) flaeche.Children.Add(Leer(cloud ? Ikon.Wolke : Ikon.Ordner));
            else
            {
                var wp = Spalten();
                foreach (var p in eintraege.Take(800))
                {
                    string p2 = p;
                    bool ordner = Directory.Exists(p);
                    var sym = Symbol(p, p, System.IO.Path.GetFileName(p), null, null, cloud, null);
                    Action oeffnen = () => { if (ordner) Betreten(cloud, p2); else Oeffnen(p2); };
                    Bedienbar(sym, p, oeffnen, el => KontextOrdner(el, p2, cloud, oeffnen));
                    if (ordner) AblageZiel(sym, sym, () => p2);
                    wp.Children.Add(sym);
                }
                flaeche.Children.Add(Rollen(wp));
            }
            var rahmen = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1.5), BorderBrush = Brushes.Transparent, Child = flaeche };
            AblageZiel(flaeche, rahmen, () => AktOrdner(cloud));
            flaeche.MouseRightButtonUp += (s, a) =>
            {
                if (a.Handled) return;
                UI.Menue(flaeche, new List<KeyValuePair<string, Action>>
                {
                    new KeyValuePair<string, Action>("Neuer Ordner", () => NeuerOrdner(cloud)),
                    new KeyValuePair<string, Action>("Im Explorer öffnen", () => Oeffnen(AktOrdner(cloud))),
                }, true);
            };
            return rahmen;
        }

        void KontextOrdner(FrameworkElement el, string pfad, bool cloud, Action oeffnen)
        {
            var m = new List<KeyValuePair<string, Action>>
            {
                new KeyValuePair<string, Action>("Öffnen", oeffnen),
                new KeyValuePair<string, Action>("Im Explorer zeigen", () => ImOrdnerZeigen(pfad)),
                new KeyValuePair<string, Action>("Umbenennen", () => Umbenennen(el, pfad)),
            };
            if (!cloud && wolke != null) m.Add(new KeyValuePair<string, Action>("In " + wolke.Name, () => { Verschieben(pfad, wolke.Pfad); Neuzeichnen(); }));
            if (cloud) m.Add(new KeyValuePair<string, Action>("Nach Dauerhaft", () => { Verschieben(pfad, Paths.Dauerhaft); Neuzeichnen(); }));
            m.Add(new KeyValuePair<string, Action>("-", null));
            m.Add(new KeyValuePair<string, Action>("In den Papierkorb", () => InKorb(pfad)));
            UI.Menue(el, m);
        }

        void InKorb(string pfad)
        {
            Ausblenden(pfad, 12, () => { try { Native.InPapierkorb(pfad); } catch (Exception ex) { Log.Fehler("Papierkorb", ex); } auswahl = null; Neuzeichnen(); KorbLesen(); });
        }

        FrameworkElement KeineWolke()
        {
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 40) };
            var ic = UI.Icon(Ikon.Wolke, 44, Farbe.B("#59FFFFFF"), 1.4);
            ic.HorizontalAlignment = HorizontalAlignment.Center;
            sp.Children.Add(ic);
            var b = UI.Knopf("Ordner wählen", null, "Btn");
            b.HorizontalAlignment = HorizontalAlignment.Center;
            b.Margin = new Thickness(0, 16, 0, 0);
            b.ToolTip = "Google Drive, OneDrive, Dropbox … werden sonst von selbst erkannt";
            b.Click += delegate { WolkeWaehlen(); };
            sp.Children.Add(b);
            return sp;
        }

        void WolkeWaehlen()
        {
            using (var d = new System.Windows.Forms.FolderBrowserDialog { Description = "Ordner für Ebene 3", ShowNewFolderButton = true })
            {
                if (wolke != null) d.SelectedPath = wolke.Pfad;
                if (d.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                WolkeSetzen(new CloudOrt { Name = System.IO.Path.GetFileName(d.SelectedPath.TrimEnd('\\')), Pfad = d.SelectedPath });
            }
        }

        void WolkeSetzen(CloudOrt o)
        {
            wolke = o;
            ordnerWolke = null;
            E.Cfg.CloudPfad = o == null ? "" : o.Pfad;
            E.Cfg.CloudName = o == null ? "" : o.Name;
            E.Cfg.Speichern();
            if (stufen.Count > 2) stufen[2].ToolTip = o == null ? "Cloud" : o.Name;
            Neuzeichnen();
        }

        void NeuerOrdner(bool cloud)
        {
            string p = Paths.FreierName(AktOrdner(cloud), "Neuer Ordner");
            try { Directory.CreateDirectory(p); } catch (Exception ex) { Log.Fehler("Neuer Ordner", ex); return; }
            if (Filter.Length > 0) suche.Text = "";
            Neuzeichnen();
            Dispatcher.BeginInvoke(new Action(() => { Border b; if (symbole.TryGetValue(p, out b)) { Waehle(p); Umbenennen(b, p); } }), DispatcherPriority.Loaded);
        }

        void Umbenennen(FrameworkElement ziel, string pfad)
        {
            var pop = new Popup { PlacementTarget = ziel, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, VerticalOffset = -38 };
            var tb = new TextBox { Text = System.IO.Path.GetFileName(pfad), FontSize = 12.5, Foreground = Farbe.Text, CaretBrush = Farbe.Text, Background = Farbe.B("#FF22252D"), BorderBrush = Farbe.Akzent, BorderThickness = new Thickness(1), Padding = new Thickness(6, 3, 6, 3), Width = Math.Max(140, ziel.ActualWidth + 40) };
            bool fertig = false;
            Action uebernehmen = () =>
            {
                if (fertig) return;
                fertig = true;
                pop.IsOpen = false;
                string neu = tb.Text.Trim();
                if (neu.Length == 0 || neu == System.IO.Path.GetFileName(pfad) || neu.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0) return;
                try
                {
                    string z = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(pfad), neu);
                    if (Directory.Exists(pfad)) Directory.Move(pfad, z); else File.Move(pfad, z);
                }
                catch (Exception ex) { Log.Fehler("Umbenennen", ex); }
                Neuzeichnen();
            };
            tb.KeyDown += (s, a) =>
            {
                if (a.Key == Key.Enter) { uebernehmen(); a.Handled = true; }
                if (a.Key == Key.Escape) { fertig = true; pop.IsOpen = false; a.Handled = true; }
            };
            pop.Closed += delegate { uebernehmen(); };
            pop.Child = tb;
            pop.IsOpen = true;
            tb.Focus();
            int punkt = tb.Text.LastIndexOf('.');
            if (punkt > 0 && File.Exists(pfad)) tb.Select(0, punkt); else tb.SelectAll();
            Keyboard.Focus(tb);
        }

        /// <summary>Was hier abgelegt wird, wandert in den Zielordner (mit Strg: kopieren).</summary>
        void AblageZiel(FrameworkElement el, Border rahmen, Func<string> zielOrdner)
        {
            el.AllowDrop = true;
            Brush alt = rahmen != null ? rahmen.BorderBrush : null;
            el.DragEnter += (s, a) => { if (rahmen != null && a.Data.GetDataPresent(DataFormats.FileDrop)) rahmen.BorderBrush = AuswahlRand; };
            el.DragLeave += (s, a) => { if (rahmen != null) rahmen.BorderBrush = alt; };
            el.DragOver += (s, a) =>
            {
                if (!a.Data.GetDataPresent(DataFormats.FileDrop) || zielOrdner() == null) a.Effects = DragDropEffects.None;
                else a.Effects = (a.KeyStates & DragDropKeyStates.ControlKey) != 0 ? DragDropEffects.Copy : DragDropEffects.Move;
                a.Handled = true;
            };
            el.Drop += (s, a) =>
            {
                if (rahmen != null) rahmen.BorderBrush = alt;
                a.Handled = true;
                var pfade = a.Data.GetData(DataFormats.FileDrop) as string[];
                string ziel = zielOrdner();
                if (pfade == null || ziel == null) return;
                bool kopieren = (a.KeyStates & DragDropKeyStates.ControlKey) != 0;
                foreach (var p in pfade)
                {
                    if (kopieren) { try { Kopieren(p, ziel); } catch (Exception ex) { Log.Fehler("Kopieren", ex); } }
                    else Verschieben(p, ziel);
                }
                E.MeldeGeaendert();
                bool inWolke = wolke != null && ziel.StartsWith(wolke.Pfad, StringComparison.OrdinalIgnoreCase);
                if (ansicht == Dauerhaft || ansicht == Wolke) Neuzeichnen(); else Zeige(inWolke ? Wolke : Dauerhaft);
            };
        }

        void AblageZielKorb(FrameworkElement el)
        {
            el.AllowDrop = true;
            el.DragOver += (s, a) => { a.Effects = a.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Move : DragDropEffects.None; a.Handled = true; };
            el.Drop += (s, a) =>
            {
                a.Handled = true;
                var pfade = a.Data.GetData(DataFormats.FileDrop) as string[];
                if (pfade == null) return;
                foreach (var p in pfade)
                {
                    var e = E.S.Finde(p);
                    if (e != null) E.JetztWeg(e);
                    else try { Native.InPapierkorb(p); } catch (Exception ex) { Log.Fehler("Papierkorb", ex); }
                }
                Neuzeichnen();
                KorbLesen();
            };
        }

        void Verschieben(string p, string ordner)
        {
            try
            {
                if (Paths.Gleich(System.IO.Path.GetDirectoryName(p) ?? "", ordner)) return;
                if (Directory.Exists(p) && (ordner + "\\").StartsWith(p.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) return;
                var e = E.S.Finde(p);
                if (e != null) { E.Verschieben(e, ordner); return; }
                Engine.DateiVerschieben(p, ordner);
            }
            catch (Exception ex) { Log.Fehler("Verschieben", ex); }
        }

        static void Kopieren(string p, string ordner)
        {
            string ziel = Paths.FreierName(ordner, System.IO.Path.GetFileName(p));
            if (Directory.Exists(p)) Engine.OrdnerKopieren(p, ziel); else File.Copy(p, ziel);
        }

        // ================= Papierkorb =================

        void KorbLesen()
        {
            if (korbLiest || bild) return;
            korbLiest = true;
            var s = E.S;
            Task.Factory.StartNew(() => { try { return Papierkorb.Lesen(s); } catch { return new List<BinEintrag>(); } })
                .ContinueWith(t =>
                {
                    korbLiest = false;
                    korb = t.Result;
                    korbPunkt.Visibility = korb.Any(x => x.Wb != null) ? Visibility.Visible : Visibility.Collapsed;
                    if (ansicht == Korb) Neuzeichnen();
                }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        public void KorbFuerBild(List<BinEintrag> l)
        {
            korb = l;
            korbPunkt.Visibility = l.Any(x => x.Wb != null) ? Visibility.Visible : Visibility.Collapsed;
        }

        IEnumerable<BinEintrag> KorbSichtbar { get { return (korb ?? new List<BinEintrag>()).Where(x => Passt(x.Name)); } }

        FrameworkElement FlaecheKorb()
        {
            OrtSetzen("Papierkorb");
            if (korb == null) { KorbLesen(); return new Grid(); }
            var liste = KorbSichtbar.Take(500).ToList();
            if (liste.Count == 0) return Leer(Filter.Length > 0 ? Ikon.Suche : Ikon.Muell);
            var wp = Spalten();
            foreach (var x in liste)
            {
                var x2 = x;
                string hilfe = x.Name + "\n" + Wann(x.Geloescht) + " · " + (x.IstOrdner ? "Ordner" : Dauer.Groesse(x.Groesse)) + (x.Wb != null ? " · war " + x.Wb.Dauer : "") + "\naus " + System.IO.Path.GetDirectoryName(x.Original);
                var sym = Symbol(x.RPfad ?? x.Name, x.RPfad ?? x.Name, x.Name, null, null, false, hilfe);
                sym.Opacity = 0.85;
                Bedienbar(sym, x.RPfad ?? x.Name, () => Zurueckholen(x2), el => UI.Menue(el, new List<KeyValuePair<string, Action>>
                {
                    new KeyValuePair<string, Action>("Wiederherstellen", () => Zurueckholen(x2)),
                    new KeyValuePair<string, Action>("Endgültig löschen", () => Loeschen(x2)),
                }));
                wp.Children.Add(sym);
            }
            return Rollen(wp);
        }

        BinEintrag KorbGewaehlt { get { return korb == null || auswahl == null ? null : korb.FirstOrDefault(x => string.Equals(x.RPfad ?? x.Name, auswahl, StringComparison.OrdinalIgnoreCase)); } }

        void Zurueckholen(BinEintrag x)
        {
            Ausblenden(x.RPfad ?? x.Name, -12, () =>
            {
                try
                {
                    if (!bild) Papierkorb.Wiederherstellen(x);
                    if (x.Wb != null) { E.S.D.Papierkorb.Remove(x.Wb); E.S.Speichern(); }
                }
                catch (Exception ex) { Log.Fehler("Wiederherstellen", ex); }
                if (korb != null) korb.Remove(x);
                auswahl = null;
                Neuzeichnen();
                KorbLesen();
            });
        }

        void Loeschen(BinEintrag x)
        {
            Ausblenden(x.RPfad ?? x.Name, 12, () =>
            {
                try { if (!bild) Papierkorb.Loeschen(x); } catch (Exception ex) { Log.Fehler("Löschen", ex); }
                if (korb != null) korb.Remove(x);
                auswahl = null;
                Neuzeichnen();
                KorbLesen();
            });
        }

        static string Wann(DateTime lokal)
        {
            var heute = DateTime.Today;
            if (lokal.Date == heute) return "heute, " + lokal.ToString("HH:mm");
            if (lokal.Date == heute.AddDays(-1)) return "gestern, " + lokal.ToString("HH:mm");
            return lokal.ToString("dd.MM.yyyy");
        }

        // ================= Schwebende Leiste =================

        /// <summary>Erscheint nur, wenn etwas gewählt ist (im Papierkorb immer): nur Symbole, Namen als Tooltip.</summary>
        void LeisteFuellen()
        {
            leisteInhalt.Children.Clear();
            if (ansicht == Befristet)
            {
                var e = Gewaehlt;
                if (e != null)
                {
                    LKnopf(Ikon.Oeffnen, "Öffnen", Farbe.Text, () => Oeffnen(e.Pfad));
                    LKnopf(Ikon.Uhr, "Verlängern", Farbe.Text, null).Click += (s, a) =>
                    {
                        var b = (Button)s;
                        UI.Menue(b, new List<KeyValuePair<string, Action>>
                        {
                            new KeyValuePair<string, Action>("+ 1 Tag", () => Verlaengern(e, "1 Tag")),
                            new KeyValuePair<string, Action>("+ 1 Woche", () => Verlaengern(e, "1 Woche")),
                            new KeyValuePair<string, Action>("+ 1 Monat", () => Verlaengern(e, "1 Monat")),
                            new KeyValuePair<string, Action>("Bis …", () => BisWaehlen(b, e)),
                        }, false, true);
                    };
                    LKnopf(Ikon.Unendlich, "Dauerhaft behalten", Farbe.Akzent, () => Wegschieben(e, Paths.Dauerhaft));
                    if (wolke != null) LKnopf(Ikon.Wolke, "In " + wolke.Name, Farbe.Text, () => Wegschieben(e, wolke.Pfad));
                    Trenner();
                    LKnopf(Ikon.Muell, "In den Papierkorb", Farbe.Gefahr, () => Wegwerfen(e));
                }
            }
            else if (ansicht == Dauerhaft || ansicht == Wolke)
            {
                string p = auswahl;
                if (p != null && (File.Exists(p) || Directory.Exists(p)))
                {
                    bool cloud = ansicht == Wolke, ordner = Directory.Exists(p);
                    LKnopf(Ikon.Oeffnen, "Öffnen", Farbe.Text, () => { if (ordner) Betreten(cloud, p); else Oeffnen(p); });
                    Border sb;
                    LKnopf(Ikon.Stift, "Umbenennen", Farbe.Text, () => { if (symbole.TryGetValue(p, out sb)) Umbenennen(sb, p); });
                    if (!cloud && wolke != null) LKnopf(Ikon.Wolke, "In " + wolke.Name, Farbe.Text, () => { Verschieben(p, wolke.Pfad); auswahl = null; Neuzeichnen(); });
                    if (cloud) LKnopf(Ikon.Unendlich, "Nach Dauerhaft", Farbe.Text, () => { Verschieben(p, Paths.Dauerhaft); auswahl = null; Neuzeichnen(); });
                    Trenner();
                    LKnopf(Ikon.Muell, "In den Papierkorb", Farbe.Gefahr, () => InKorb(p));
                }
            }
            else if (ansicht == Korb && korb != null)
            {
                var x = KorbGewaehlt;
                if (x != null)
                {
                    LKnopf(Ikon.Zurueck, "Wiederherstellen", Farbe.Akzent, () => Zurueckholen(x));
                    ZweiKlick(LKnopf(Ikon.X, "Endgültig löschen", Farbe.Gefahr, null), () => Loeschen(x));
                }
                else if (korb.Count > 0)
                {
                    LKnopf(Ikon.Zurueck, "Alles wiederherstellen", Farbe.Text, () => { foreach (var y in KorbSichtbar.ToList()) try { Papierkorb.Wiederherstellen(y); if (y.Wb != null) E.S.D.Papierkorb.Remove(y.Wb); } catch { } E.S.Speichern(); KorbLesen(); });
                    ZweiKlick(LKnopf(Ikon.Muell, "Papierkorb leeren", Farbe.Gefahr, null), () =>
                    {
                        if (Filter.Length > 0) { foreach (var y in KorbSichtbar.ToList()) try { Papierkorb.Loeschen(y); } catch { } }
                        else Native.PapierkorbLeeren();
                        KorbLesen();
                    });
                }
            }

            bool zeigen = leisteInhalt.Children.Count > 0;
            if (zeigen && leiste.Visibility != Visibility.Visible)
            {
                leiste.Visibility = Visibility.Visible;
                if (!bild)
                {
                    leiste.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
                    ((TranslateTransform)leiste.RenderTransform).BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(200)) { EasingFunction = UI.Weich });
                }
            }
            else if (!zeigen) leiste.Visibility = Visibility.Collapsed;
        }

        Button LKnopf(string icon, string name, Brush farbe, Action a)
        {
            var b = new Button { Style = (Style)Application.Current.FindResource("Rund"), Content = UI.Icon(icon, 17, farbe, 1.9), ToolTip = name, Margin = new Thickness(1, 0, 1, 0) };
            ToolTipService.SetPlacement(b, PlacementMode.Top);
            ToolTipService.SetInitialShowDelay(b, 250);
            System.Windows.Automation.AutomationProperties.SetName(b, name);
            if (a != null) b.Click += delegate { a(); };
            leisteInhalt.Children.Add(b);
            return b;
        }

        void Trenner()
        {
            leisteInhalt.Children.Add(new Border { Width = 1, Height = 22, Background = Farbe.B("#26FFFFFF"), Margin = new Thickness(5, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center });
        }

        /// <summary>Gefährliches braucht zwei Klicks – ohne Dialog: das Symbol wird rot gefüllt und wartet kurz.</summary>
        static void ZweiKlick(Button b, Action los)
        {
            bool scharf = false;
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            t.Tick += delegate { t.Stop(); scharf = false; b.Background = Brushes.Transparent; };
            b.Click += delegate
            {
                if (!scharf) { scharf = true; b.Background = Farbe.B("#40FF6B5E"); b.ToolTip = "Nochmal klicken"; t.Start(); return; }
                t.Stop(); scharf = false; b.Background = Brushes.Transparent;
                los();
            };
        }

        // ================= Einstellungen =================

        FrameworkElement FlaecheEinstellungen()
        {
            OrtSetzen("Einstellungen");
            var c = E.Cfg;
            var tafel = new Border { Width = 470, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(16), Background = Farbe.B("#D91A1D24"), BorderBrush = Farbe.B("#1AFFFFFF"), BorderThickness = new Thickness(1), Padding = new Thickness(20, 8, 20, 8), Margin = new Thickness(0, 0, 0, 30) };
            var sp = new StackPanel();
            sp.Children.Add(Zeile("Nach Downloads fragen", Schalter(c.Fragen, v => c.Fragen = v), true));
            var chips = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var d in Dauer.Auswahl)
            {
                string d2 = d;
                var t = new ToggleButton { Style = (Style)Application.Current.FindResource("Chip"), Content = d, IsChecked = c.Standard == d, Margin = new Thickness(4, 0, 0, 0), Height = 28, Padding = new Thickness(9, 0, 9, 0), FontSize = 12 };
                t.Click += delegate { c.Standard = d2; c.Speichern(); Neuzeichnen(); };
                chips.Children.Add(t);
            }
            sp.Children.Add(Zeile("Vorschlag", chips, true));
            var wolken = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var o in Cloud.Finden())
            {
                var o2 = o;
                var t = new ToggleButton { Style = (Style)Application.Current.FindResource("Chip"), Content = o.Name, IsChecked = wolke != null && Paths.Gleich(wolke.Pfad, o.Pfad), Margin = new Thickness(4, 0, 0, 0), Height = 28, Padding = new Thickness(9, 0, 9, 0), FontSize = 12, ToolTip = o.Pfad };
                t.Click += delegate { WolkeSetzen(o2); };
                wolken.Children.Add(t);
            }
            var mehr = new Button { Style = (Style)Application.Current.FindResource("Btn"), Content = "…", Width = 34, Height = 28, Padding = new Thickness(0), Margin = new Thickness(4, 0, 0, 0), ToolTip = wolke != null ? wolke.Pfad : "Ordner wählen" };
            mehr.Click += delegate { WolkeWaehlen(); };
            wolken.Children.Add(mehr);
            sp.Children.Add(Zeile("Cloud", wolken, true));
            sp.Children.Add(Zeile("Hinweis beim Speichern", Schalter(c.HinweisSpeichern, v => c.HinweisSpeichern = v), true));
            sp.Children.Add(Zeile("Im Explorer zeigen", Schalter(c.Seitenleiste, v => { c.Seitenleiste = v; Install.Seitenleiste(v); }), true));
            sp.Children.Add(Zeile("Mit Windows starten", Schalter(c.Autostart, v => { c.Autostart = v; Install.Autostart(v); }), true));
            sp.Children.Add(Zeile("Strg + Alt + W", Schalter(c.Tastenkuerzel, v => { c.Tastenkuerzel = v; var app = Application.Current as WbApp; if (app != null) app.Tastenkuerzel(v); }), true));
            var weg = new Button { Style = (Style)Application.Current.FindResource("Leer"), Content = new TextBlock { Text = "Entfernen", Foreground = Farbe.Gefahr, FontSize = 13 }, Cursor = Cursors.Hand, ToolTip = "Workbench entfernen – deine Dateien bleiben" };
            bool scharf = false;
            weg.Click += delegate
            {
                if (!scharf) { scharf = true; ((TextBlock)weg.Content).Text = "Wirklich entfernen?"; return; }
                Install.Deinstallieren(true);
            };
            sp.Children.Add(Zeile("Version " + Program.Version, weg, false));
            tafel.Child = sp;
            return tafel;
        }

        FrameworkElement Zeile(string text, FrameworkElement rechts, bool linie)
        {
            var g = new Grid { MinHeight = 50 };
            var t = new TextBlock { Text = text, FontSize = 13.5, Foreground = linie ? Farbe.Text : Farbe.Text3, VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(t);
            rechts.HorizontalAlignment = HorizontalAlignment.Right;
            rechts.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(rechts);
            return new Border { BorderBrush = Farbe.B("#12FFFFFF"), BorderThickness = new Thickness(0, 0, 0, linie ? 1 : 0), Child = g };
        }

        CheckBox Schalter(bool an, Action<bool> setzen)
        {
            var cb = new CheckBox { Style = (Style)Application.Current.FindResource("Schalter"), IsChecked = an };
            cb.Click += delegate { setzen(cb.IsChecked == true); E.Cfg.Speichern(); };
            return cb;
        }

        // ================= Öffnen, Tastatur, Wischen =================

        static void Oeffnen(string p)
        {
            try { Process.Start(new ProcessStartInfo(p) { UseShellExecute = true }); } catch (Exception ex) { Log.Fehler("Öffnen", ex); }
        }

        static void ImOrdnerZeigen(string p)
        {
            try { Process.Start("explorer.exe", "/select,\"" + p + "\""); } catch { }
        }

        void Tippen(object s, TextCompositionEventArgs a)
        {
            // Einfach lostippen = suchen (wie im Startmenü)
            if (suche.IsKeyboardFocused || string.IsNullOrEmpty(a.Text) || char.IsControl(a.Text[0]) || Keyboard.FocusedElement is TextBox) return;
            if (ansicht == Einst) return;
            SucheAuf();
            suche.Text += a.Text;
            suche.CaretIndex = suche.Text.Length;
            a.Handled = true;
        }

        void Tasten(object s, KeyEventArgs a)
        {
            bool strg = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            bool imText = Keyboard.FocusedElement is TextBox;
            if (strg && a.Key == Key.D1) { Zeige(Befristet); a.Handled = true; }
            else if (strg && a.Key == Key.D2) { Zeige(Dauerhaft); a.Handled = true; }
            else if (strg && a.Key == Key.D3) { Zeige(Wolke); a.Handled = true; }
            else if (strg && a.Key == Key.F) { SucheAuf(); suche.SelectAll(); a.Handled = true; }
            else if (a.Key == Key.Escape)
            {
                if (suche.Text.Length > 0) { suche.Text = ""; SucheZu(); Keyboard.ClearFocus(); }
                else if (auswahl != null) Waehle(null);
                else Schliessen();
                a.Handled = true;
            }
            else if (imText) return;
            else if (a.Key == Key.Right || a.Key == Key.Down || a.Key == Key.PageDown) { Schritt(1); a.Handled = true; }
            else if (a.Key == Key.Left || a.Key == Key.Up || a.Key == Key.PageUp) { Schritt(-1); a.Handled = true; }
            else if (a.Key == Key.Back && (ansicht == Dauerhaft || ansicht == Wolke))
            {
                bool c = ansicht == Wolke;
                string akt = AktOrdner(c), w = WurzelVon(c);
                if (akt != null && w != null && !Paths.Gleich(akt, w)) Betreten(c, System.IO.Path.GetDirectoryName(akt));
                a.Handled = true;
            }
            else if (a.Key == Key.Enter && auswahl != null)
            {
                if (Directory.Exists(auswahl) && (ansicht == Dauerhaft || ansicht == Wolke)) Betreten(ansicht == Wolke, auswahl);
                else if (ansicht != Korb) Oeffnen(auswahl);
                a.Handled = true;
            }
            else if (a.Key == Key.Delete && auswahl != null)
            {
                var e = Gewaehlt;
                if (ansicht == Befristet && e != null) Wegwerfen(e);
                else if (ansicht == Dauerhaft || ansicht == Wolke) InKorb(auswahl);
                a.Handled = true;
            }
            else if (a.Key == Key.F2 && auswahl != null && (ansicht == Dauerhaft || ansicht == Wolke))
            {
                Border b;
                if (symbole.TryGetValue(auswahl, out b)) Umbenennen(b, auswahl);
                a.Handled = true;
            }
        }

        // Touchpad: zwei Finger seitlich wischen wechselt die Ebene (WM_MOUSEHWHEEL).
        int wischSumme;
        DateTime wischSperre;
        IntPtr Wischen(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (msg != 0x020E) return IntPtr.Zero;
            int delta = (short)((w.ToInt64() >> 16) & 0xFFFF);
            handled = true;
            if (DateTime.UtcNow < wischSperre) return IntPtr.Zero;
            wischSumme += delta;
            if (Math.Abs(wischSumme) >= 240)
            {
                Schritt(wischSumme > 0 ? 1 : -1);
                wischSumme = 0;
                wischSperre = DateTime.UtcNow.AddMilliseconds(450);
            }
            return IntPtr.Zero;
        }

        void Schliessen()
        {
            var a = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(130));
            a.Completed += delegate { Close(); };
            wurzel.BeginAnimation(OpacityProperty, a);
        }

        protected override void OnClosed(EventArgs e)
        {
            minuten.Stop();
            base.OnClosed(e);
        }
    }
}
