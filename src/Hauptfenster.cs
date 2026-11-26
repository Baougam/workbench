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
    partial class Hauptfenster : Window
    {
        public const int Korb = 100, Einst = 101, Netzansicht = 102;
        const double Abstand = 34;

        readonly Engine E;
        readonly bool bild;
        int ansicht = -1, letzteEbene;
        CloudOrt wolke;

        readonly Grid wurzel = new Grid();
        readonly Grid buehne = new Grid();
        readonly Border blatt = new Border();
        readonly TranslateTransform blattY = new TranslateTransform();
        readonly Border[] hinterBlatt = new Border[2];
        readonly StackPanel ort = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        TextBox suche;
        Border suchFeld;
        Point leerStart;
        bool leerZug;
        Button btnKorb, btnEinst, btnNetz;
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
            wurzel.Children.Add(VersionLeise());
            rand = new Randwechsel(d => IstBlatt(ansicht) && IstBlatt(ansicht + d) ? BlattName(ansicht + d) : null, RandWechsel, RandNeigen);
            wurzel.Children.Add(rand);
            var rahmenEbene = new Canvas { IsHitTestVisible = false };
            rahmenEbene.Children.Add(rahmen);
            wurzel.Children.Add(rahmenEbene);
            RandAblage();

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
                PreviewKeyUp += (s, a) => { if ((a.Key == Key.LeftShift || a.Key == Key.RightShift) && StufenOffen) StufenFertig(false); };
                Deactivated += delegate { if (StufenOffen) StufenFertig(true); };
                // Klick woanders: Eingabefeld (Suche, Netz-Suche, Notiz) verliert den Fokus – leere Suche klappt zu
                PreviewMouseDown += (s, a) =>
                {
                    var tb = Keyboard.FocusedElement as TextBox;
                    if (tb == null || tb.IsMouseOver) return;
                    if (tb == suche && suchFeld.IsMouseOver) return;
                    Keyboard.ClearFocus();
                    if (suche.Text.Length == 0) SucheZu();
                };
                PreviewMouseWheel += Rad;
                TextInput += Tippen;
                IsVisibleChanged += delegate { if (IsVisible && veraltet) Neuzeichnen(); };
                Loaded += delegate { KorbLesen(); };
                buehne.MouseLeftButtonDown += (s, a) =>
                {
                    if (!(a.Source == buehne || a.OriginalSource is ScrollViewer || a.OriginalSource is WrapPanel || a.OriginalSource is Grid || a.OriginalSource is Canvas)) return;
                    Waehle(null);
                    // ins Leere drücken und hoch/runter ziehen = Ebene wechseln
                    if (a.ClickCount == 1 && (IstBlatt(ansicht) || ansicht == Korb))
                    {
                        leerStart = a.GetPosition(wurzel); leerSeit = DateTime.UtcNow; leerZug = true; aufziehen = false;
                        basis = StrgGedrueckt ? gruppe.ToList() : new List<string>();
                        buehne.CaptureMouse();
                    }
                };
                buehne.MouseMove += (s, a) =>
                {
                    if (!leerZug || a.LeftButton != MouseButtonState.Pressed) return;
                    var p = a.GetPosition(wurzel);
                    double dy = p.Y - leerStart.Y, dx = p.X - leerStart.X;
                    if (!aufziehen)
                    {
                        if (Math.Abs(dy) < 6 && Math.Abs(dx) < 6) return;
                        // schneller Strich hoch/runter = Ebene; alles andere = Rahmen
                        bool strich = IstBlatt(ansicht) && !StrgGedrueckt && Math.Abs(dy) > Math.Abs(dx) * 1.6 && (DateTime.UtcNow - leerSeit).TotalMilliseconds < 280;
                        if (strich)
                        {
                            blattY.BeginAnimation(TranslateTransform.YProperty, null);
                            blattY.Y = Math.Max(-46, Math.Min(46, dy * 0.3));          // das Blatt folgt leicht dem Finger
                            if (Math.Abs(dy) <= 90) return;
                            leerZug = false;
                            buehne.ReleaseMouseCapture();
                            int vorher = ansicht;
                            Schritt(dy < 0 ? 1 : -1);
                            if (ansicht == vorher) BlattZurueck();
                            return;
                        }
                        aufziehen = true;
                        if (blattY.Y != 0) BlattZurueck();
                    }
                    Rahmen(leerStart, p);
                };
                buehne.MouseLeftButtonUp += delegate { if (leerZug) buehne.ReleaseMouseCapture(); };
                buehne.LostMouseCapture += delegate
                {
                    if (!leerZug) return;
                    leerZug = false;
                    rahmen.Visibility = Visibility.Collapsed;
                    if (!aufziehen && blattY.Y != 0) BlattZurueck();
                    if (aufziehen) LeisteFuellen();
                    aufziehen = false;
                };
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
            System.Windows.Automation.AutomationProperties.SetName(suche, "Search");
            sg.Children.Add(suche);
            suchFeld.Child = sg;
            suchFeld.ToolTip = "Search (Ctrl+F)";
            suchFeld.MouseLeftButtonDown += delegate { SucheAuf(); };
            suche.TextChanged += delegate { Neuzeichnen(); };
            suche.LostKeyboardFocus += delegate { if (suche.Text.Length == 0) SucheZu(); };
            WindowChrome.SetIsHitTestVisibleInChrome(suchFeld, true);
            // unscheinbar: die Ansicht von oben (Netz)
            btnNetz = new Button { Style = (Style)Application.Current.FindResource("Rund"), Width = 30, Height = 30, Content = UI.Icon(Ikon.Netz, 15, LabelLeise, 1.7), ToolTip = "Graph view (Ctrl+G)", Margin = new Thickness(0, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center };
            System.Windows.Automation.AutomationProperties.SetName(btnNetz, "Graph view");
            WindowChrome.SetIsHitTestVisibleInChrome(btnNetz, true);
            btnNetz.Click += delegate { Zeige(ansicht == Netzansicht ? letzteEbene : Netzansicht); };
            rechts.Children.Add(btnNetz);
            rechts.Children.Add(suchFeld);
            rechts.Children.Add(FensterKnopf(Ikon.Minus, "Minimize", "Fensterknopf", () => WindowState = WindowState.Minimized));
            rechts.Children.Add(FensterKnopf(Ikon.Quadrat, "Maximize", "Fensterknopf", () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized));
            rechts.Children.Add(FensterKnopf(Ikon.X, "Close", "FensterknopfX", Schliessen));
            g.Children.Add(rechts);
            return g;
        }

        void SucheAuf()
        {
            if (ansicht == Netzansicht && netz != null) { netz.SucheFokus(null); return; }   // im Netz: die Suche oben links
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

        Ellipse updatePunkt;
        TextBlock updateLink;
        void UpdateDa()
        {
            updatePunkt.Visibility = Visibility.Visible;
            if (updateLink != null) { updateLink.Text = "update " + Update.Neu(E.Cfg); updateLink.Visibility = Visibility.Visible; }
            if (ansicht == Einst) Neuzeichnen();
        }

        /// <summary>Ganz unten rechts, kaum sichtbar: Version – und nur bei einem Update ein leiser Link (lädt die neue Version).</summary>
        FrameworkElement VersionLeise()
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 26, 2) };
            sp.Children.Add(new TextBlock { Text = "v" + Program.Version, FontSize = 10.5, Foreground = Farbe.B("#40FFFFFF") });
            string neu = Update.Neu(E.Cfg);
            updateLink = new TextBlock { Text = "update " + neu, FontSize = 10.5, Foreground = Farbe.B("#99F4B860"), Margin = new Thickness(8, 0, 0, 0), Cursor = Cursors.Hand, ToolTip = "Restart into the new version", Visibility = neu != null ? Visibility.Visible : Visibility.Collapsed };
            updateLink.MouseEnter += delegate { updateLink.Foreground = Farbe.Akzent; updateLink.TextDecorations = TextDecorations.Underline; };
            updateLink.MouseLeave += delegate { updateLink.Foreground = Farbe.B("#99F4B860"); updateLink.TextDecorations = null; };
            updateLink.MouseLeftButtonUp += delegate { updateLink.Text = "updating…"; Update.Anwenden(); };
            sp.Children.Add(updateLink);
            return sp;
        }

        Button RundKnopf(string icon, string name, Action a)
        {
            var b = new Button { Style = (Style)Application.Current.FindResource("Rund"), Content = UI.Icon(icon, 17, LabelLeise, 1.8), ToolTip = name };
            ToolTipService.SetPlacement(b, PlacementMode.Right);
            System.Windows.Automation.AutomationProperties.SetName(b, name);
            b.Click += delegate { a(); };
            return b;
        }

        // ================= Beim Ziehen am Rand verweilen = Ebene wechseln =================

        Randwechsel rand;
        // ---------- Mehrere auswählen: Rahmen aufziehen, Strg+Klick, Strg+A ----------

        /// <summary>Alles Markierte; „auswahl“ ist eines davon.</summary>
        readonly HashSet<string> gruppe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly Border rahmen = new Border { Visibility = Visibility.Collapsed, CornerRadius = new CornerRadius(6), Background = Farbe.B("#1FF4B860"), BorderBrush = Farbe.AkzentRand, BorderThickness = new Thickness(1) };
        List<string> basis = new List<string>();
        DateTime leerSeit;
        bool aufziehen;

        static bool StrgGedrueckt { get { return (Keyboard.Modifiers & ModifierKeys.Control) != 0; } }

        void BlattZurueck() { blattY.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(220)) { EasingFunction = UI.Weich }); }

        /// <summary>Rahmen zeichnen und alles markieren, was er berührt.</summary>
        public void Rahmen(Point von, Point bis)
        {
            var r = new Rect(von, bis);
            var feld = blatt.TransformToAncestor(wurzel).TransformBounds(new Rect(blatt.RenderSize));
            r.Intersect(feld);
            if (r.IsEmpty) r = new Rect(von, new Size(0, 0));
            Canvas.SetLeft(rahmen, r.X); Canvas.SetTop(rahmen, r.Y);
            rahmen.Width = r.Width; rahmen.Height = r.Height;
            rahmen.Visibility = Visibility.Visible;
            var neu = new HashSet<string>(basis, StringComparer.OrdinalIgnoreCase);
            foreach (var kv in symbole)
            {
                if (!kv.Value.IsDescendantOf(wurzel)) continue;
                var f = kv.Value.TransformToAncestor(wurzel).TransformBounds(new Rect(kv.Value.RenderSize));
                f.Inflate(-10, -8);
                if (r.IntersectsWith(f)) neu.Add(kv.Key);
            }
            if (neu.SetEquals(gruppe)) return;
            GruppeSetzen(neu);
        }

        void GruppeSetzen(IEnumerable<string> neu)
        {
            var n = new HashSet<string>(neu.Where(x => symbole.ContainsKey(x)), StringComparer.OrdinalIgnoreCase);
            foreach (var p in gruppe) { Border b; if (!n.Contains(p) && symbole.TryGetValue(p, out b)) Stil(b, false, false); }
            foreach (var p in n) Stil(symbole[p], true, false);
            gruppe.Clear();
            foreach (var p in n) gruppe.Add(p);
            if (auswahl == null || !gruppe.Contains(auswahl)) auswahl = gruppe.FirstOrDefault();
            if (!leerZug) LeisteFuellen();
        }

        /// <summary>Maus auf etwas gedrückt: Strg schaltet um; was schon mitmarkiert ist, bleibt (damit man alle zusammen ziehen kann).</summary>
        void Druecken(string pfad)
        {
            if (StrgGedrueckt)
            {
                if (pfad == null) return;
                var n = new HashSet<string>(gruppe, StringComparer.OrdinalIgnoreCase);
                if (Mouse.LeftButton == MouseButtonState.Pressed && !n.Remove(pfad)) n.Add(pfad); else n.Add(pfad);
                GruppeSetzen(n);
            }
            else if (pfad == null || !gruppe.Contains(pfad)) Waehle(pfad);
        }

        /// <summary>Losgelassen ohne zu ziehen: nur noch dieses eine.</summary>
        void Loslassen(string pfad) { if (!StrgGedrueckt && gruppe.Count > 1) Waehle(pfad); }

        /// <summary>Alles, was mit diesem Ding zusammen markiert ist (sonst nur es selbst).</summary>
        string[] Mit(string pfad) { return gruppe.Count > 1 && gruppe.Contains(pfad) ? gruppe.ToArray() : new[] { pfad }; }
        List<Eintrag> Mit(Eintrag e)
        {
            var l = Mit(e.Pfad).Select(p => E.S.Finde(p)).Where(x => x != null).ToList();
            return l.Count > 0 ? l : new List<Eintrag> { e };
        }

        /// <summary>Alle zusammen ausblenden, dann einmal handeln und neu zeichnen.</summary>
        void Gemeinsam(string[] pfade, double y, Action<string> tun)
        {
            int offen = pfade.Length;
            foreach (var p in pfade)
                Ausblenden(p, y, () =>
                {
                    if (--offen > 0) return;
                    foreach (var q in pfade) try { tun(q); } catch (Exception ex) { Log.Fehler("Gemeinsam", ex); }
                    auswahl = null;
                    Neuzeichnen();
                    KorbLesen();
                });
        }

        public int GruppeAnzahl { get { return gruppe.Count; } }
        public void GruppeTest(Action<HashSet<string>> f) { f(gruppe); }
        public Dictionary<string, Border> SymboleTest { get { return symbole; } }
        public void AllesMarkieren() { GruppeSetzen(symbole.Keys.ToList()); }
        public void EntfernenTest() { Entfernen(); }

        void Entfernen()
        {
            if (auswahl == null) return;
            if (ansicht == Befristet) { var e = Gewaehlt; if (e != null) Wegwerfen(e); }
            else if (IstBoard) VomBoard(Mit(auswahl));
            else if (ImOrdner) InKorb(auswahl);
        }

        string tischZug;          // Datei, die gerade auf der freien Fläche verschoben wird (noch kein Windows-Drag)

        void RandAblage()
        {
            wurzel.PreviewDragOver += (s, a) => { if (a.Data.GetDataPresent(DataFormats.FileDrop)) rand.Melden(a.GetPosition(wurzel)); };
            wurzel.PreviewDrop += delegate { rand.Ende(); };
            wurzel.AllowDrop = true;
            // Am Rand gibt es kein eigenes Ziel: kein Verbotszeichen zeigen, ein Loslassen dort tut aber nichts
            wurzel.DragOver += (s, a) =>
            {
                if (!a.Data.GetDataPresent(DataFormats.FileDrop)) return;
                a.Effects = rand.Zone != 0 || FuerBefristet(a) != null ? DragDropEffects.Move : DragDropEffects.None;
                a.Handled = true;
            };
            wurzel.Drop += (s, a) =>
            {
                var pfade = FuerBefristet(a);
                a.Effects = DragDropEffects.None;
                a.Handled = true;
                if (pfade == null) return;
                a.Effects = DragDropEffects.Move;
                foreach (var p in pfade) Verschieben(p, BefristetOrdner());
                E.MeldeGeaendert();
            };
        }

        /// <summary>Auf der Expiring-Fläche ablegen: bekommt die Standard-Dauer. Was schon eine Frist hat, bleibt, wie es ist.</summary>
        string[] FuerBefristet(DragEventArgs a)
        {
            if (ansicht != Befristet) return null;
            var p = a.GetPosition(wurzel);
            if (p.X < Randwechsel.LinksAb || p.Y < 44) return null;
            var pfade = a.Data.GetData(DataFormats.FileDrop) as string[];
            if (pfade == null) return null;
            pfade = pfade.Where(x => E.S.Finde(x) == null).ToArray();
            return pfade.Length > 0 ? pfade : null;
        }

        void RandNeigen(int richtung)
        {
            if (bild) return;
            leiste.BeginAnimation(OpacityProperty, new DoubleAnimation(richtung > 0 ? 0 : 1, TimeSpan.FromMilliseconds(140)));
            blattY.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-richtung * 10, TimeSpan.FromMilliseconds(260)) { EasingFunction = UI.Weich });
        }

        void RandWechsel(int richtung)
        {
            if (!bild) leiste.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(140)));
            string pfad = tischZug;
            string[] alle = pfad == null ? null : Mit(pfad);
            if (pfad != null)
            {
                // von der freien Fläche: Symbol zurück an seinen Platz, ab hier als echtes Windows-Ziehen weiter
                tischZug = null;
                if (aktuellerTisch != null) aktuellerTisch.ZugAbbrechen();
                rand.Zeiger = null;
            }
            Schritt(richtung);
            if (pfad != null && Mouse.LeftButton == MouseButtonState.Pressed)
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => Ziehen(alle)));
        }

        /// <summary>Datei mitnehmen – das Fenster ist die Quelle, damit ein Ebenenwechsel das Ziehen nicht abreißt.</summary>
        void Ziehen(string[] pfade)
        {
            pfade = pfade.Where(x => File.Exists(x) || Directory.Exists(x)).ToArray();
            if (pfade.Length == 0) return;
            rand.Geist(pfade[0], pfade.Length);
            try { DragDrop.DoDragDrop(this, new DataObject(DataFormats.FileDrop, pfade), DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link); }
            catch (Exception ex) { Log.Fehler("Ziehen", ex); }
            finally { rand.Ende(); }
        }

        void TischZug(string pfad)
        {
            tischZug = pfad;
            if (pfad == null) { rand.Ende(); return; }
            rand.Zeiger = () => tischZug != null && Mouse.LeftButton == MouseButtonState.Pressed ? (Point?)Mouse.GetPosition(wurzel) : null;
            rand.Melden(Mouse.GetPosition(wurzel));
        }

        public void RandFuerBild() { rand.FuerBild(1, 0.6); }
        public Randwechsel RandTest { get { return rand; } }
        public int Ansicht { get { return ansicht; } }

        // ================= Ebenen wechseln =================

        bool veraltet;

        /// <summary>Daten haben sich geändert. Versteckt wird nichts gezeichnet – erst beim nächsten Zeigen.</summary>
        public void Auffrischen()
        {
            TiefeVergessen();
            if (!bild && !IsVisible) { veraltet = true; korb = null; return; }
            if (ansicht == Korb) KorbLesen(); else { Neuzeichnen(); KorbLesen(); }
        }

        void Neuzeichnen()
        {
            if (ansicht < 0) return;
            veraltet = false;
            symbole.Clear();
            var v = Flaeche();
            buehne.Children.Clear();
            buehne.Children.Add(v);
            if (auswahl == null) gruppe.Clear();
            else gruppe.Add(auswahl);
            gruppe.RemoveWhere(x => !symbole.ContainsKey(x));
            if (auswahl != null && !gruppe.Contains(auswahl)) auswahl = gruppe.FirstOrDefault();
            foreach (var g in gruppe) Stil(symbole[g], true, false);
            LeisteFuellen();
        }

        string Filter { get { return suche == null ? "" : suche.Text.Trim(); } }
        bool Passt(string name) { return Filter.Length == 0 || name.IndexOf(Filter, StringComparison.OrdinalIgnoreCase) >= 0; }

        Netz netz;
        /// <summary>Nur für Bilder im Render-Modus.</summary>
        public void NetzSuche(string t) { if (netz != null) netz.SucheFokus(t); }

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
            // Restzeit: das Symbol selbst leert sich (Original bleibt unverändert, nur von oben ausgeblendet)
            bool bald = anteil.HasValue && ringFarbe == Farbe.Akzent;
            var bild2 = Vorschau.Bild(bildPfad, 48, 48, nurSymbol, anteil, bald);
            bild2.HorizontalAlignment = HorizontalAlignment.Center;
            bild2.VerticalAlignment = VerticalAlignment.Center;
            kopf.Children.Add(bild2);
            sp.Children.Add(kopf);
            var t = new TextBlock
            {
                Text = name, FontSize = 12, Foreground = Label, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 32, LineHeight = 16, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                Margin = new Thickness(0, 6, 0, 0)
            };
            sp.Children.Add(t);
            b.Child = sp;
            b.MouseEnter += delegate { Stil(b, gruppe.Contains(pfad), true); };
            b.MouseLeave += delegate { Stil(b, gruppe.Contains(pfad), false); };
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
            foreach (var g in gruppe) if (symbole.TryGetValue(g, out b)) Stil(b, false, false);
            gruppe.Clear();
            auswahl = pfad;
            if (pfad != null) gruppe.Add(pfad);
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
                Druecken(pfad);
            };
            el.MouseLeftButtonUp += delegate { if (gedrueckt) Loslassen(pfad); gedrueckt = false; };
            el.MouseMove += (s, a) =>
            {
                if (!gedrueckt || a.LeftButton != MouseButtonState.Pressed) return;
                var p = a.GetPosition(this);
                if (Math.Abs(p.X - start.X) < 6 && Math.Abs(p.Y - start.Y) < 6) return;
                gedrueckt = false;
                string echt = pfad;
                if (ansicht == Korb) return;
                Ziehen(Mit(echt));
            };
            el.MouseRightButtonUp += (s, a) => { if (!gruppe.Contains(pfad)) Waehle(pfad); kontext(el); a.Handled = true; };
        }

        static WrapPanel Spalten()
        {
            return new WrapPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Stretch };
        }

        /// <summary>Symbole laufen wie auf dem Desktop in Spalten. Mausrad / zwei Finger hoch-runter blättern seitwärts durch.</summary>
        static ScrollViewer Rollen(UIElement inhalt)
        {
            return new ScrollViewer { Content = inhalt, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, CanContentScroll = false };
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
            OrtSetzen("Expiring");
            var jetzt = DateTime.UtcNow;
            var liste = E.S.D.Eintraege.Where(x => x.Ablauf.HasValue && (File.Exists(x.Pfad) || Directory.Exists(x.Pfad)) && Passt(x.Name)).OrderBy(x => x.Ablauf.Value).ToList();
            if (liste.Count == 0) return Leer(Filter.Length > 0 ? Ikon.Suche : Ikon.Haken);

            var reihe = new StackPanel { Orientation = Orientation.Horizontal };
            string[] titel = { "Today", "This week", "Later" };
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
                    var sym = Symbol(e.Pfad, e.Pfad, e.Name, rest, ring[s], false, e.Name + "\n" + Kurz(e.Ablauf.Value, jetzt) + " left · " + Dauer.Wann(e.Ablauf.Value));
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
            if (r.TotalMinutes < 60) return Math.Max(1, (int)Math.Ceiling(r.TotalMinutes)) + "m";
            if (r.TotalHours < 24) return (int)Math.Round(r.TotalHours) + "h";
            if (r.TotalDays < 14) return (int)Math.Round(r.TotalDays) + "d";
            if (r.TotalDays < 60) return (int)Math.Round(r.TotalDays / 7) + "w";
            return ablaufUtc.ToLocalTime().ToString("d MMM", Dauer.En);
        }

        Eintrag Gewaehlt { get { return auswahl == null ? null : E.S.Finde(auswahl); } }

        /// <summary>
        /// Rechtsklick auf eine befristete Datei: keine lange Liste, sondern eine kleine Karte – oben der Zeit-Schieber
        /// (ein Klick setzt die neue Dauer ab jetzt), darunter ein paar Symbole.
        /// </summary>
        void KontextBefristet(FrameworkElement ziel, Eintrag e) { Zeitkarte(ziel, e, false); }

        void Zeitkarte(FrameworkElement ziel, Eintrag e, bool oben)
        {
            var pop = new Popup { PlacementTarget = ziel, Placement = oben ? PlacementMode.Top : PlacementMode.Right, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade, HorizontalOffset = oben ? -150 : -6 };
            var sp = new StackPanel();
            var kopf = new Grid();
            var mit = Mit(e);
            kopf.Children.Add(new TextBlock { Text = mit.Count > 1 ? mit.Count + " files" : e.Name, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = Farbe.Text, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 70, 0) });
            if (e.Ablauf.HasValue && mit.Count == 1)
                kopf.Children.Add(new TextBlock { Text = Kurz(e.Ablauf.Value, DateTime.UtcNow) + " left", FontSize = 12, Foreground = Farbe.Text3, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, ToolTip = Dauer.Wann(e.Ablauf.Value) });
            sp.Children.Add(kopf);
            string aktuell = Array.IndexOf(ZeitUI.Stationen, e.Gewaehlt) >= 0 && mit.All(x => x.Gewaehlt == e.Gewaehlt) ? e.Gewaehlt : null;
            var schieber = ZeitUI.Schieber(aktuell, n =>
            {
                pop.IsOpen = false;
                if (n == Dauer.Immer) { Wegschieben(e, Paths.Dauerhaft); return; }
                foreach (var x in mit) E.FristSetzen(x, Dauer.Ablauf(n, DateTime.UtcNow), n);
                Neuzeichnen();
            }, () => { pop.IsOpen = false; BisWaehlen(ziel, e); }, wolke, w => { pop.IsOpen = false; Wegschieben(e, w.Pfad); });
            schieber.Margin = new Thickness(0, 12, 0, 0);
            sp.Children.Add(schieber);
            sp.Children.Add(new Border { Height = 1, Background = Farbe.B("#14FFFFFF"), Margin = new Thickness(-2, 8, -2, 8) });
            var reihe = new StackPanel { Orientation = Orientation.Horizontal };
            Action<string, string, Brush, Action> knopf = (icon, name, farbe, tun) =>
            {
                var b = new Button { Style = (Style)Application.Current.FindResource("Rund"), Width = 34, Height = 34, Content = UI.Icon(icon, 16, farbe, 1.8), ToolTip = name, Margin = new Thickness(0, 0, 2, 0) };
                ToolTipService.SetPlacement(b, PlacementMode.Bottom);
                System.Windows.Automation.AutomationProperties.SetName(b, name);
                b.Click += delegate { pop.IsOpen = false; tun(); };
                reihe.Children.Add(b);
            };
            if (mit.Count == 1) knopf(Ikon.Oeffnen, "Open", Farbe.Text, () => Oeffnen(e.Pfad));
            if (mit.Count == 1) knopf(Ikon.Ordner, "Show in folder", Farbe.Text, () => ImOrdnerZeigen(e.Pfad));
            var rechts = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var weg = new Button { Style = (Style)Application.Current.FindResource("Rund"), Width = 34, Height = 34, Content = UI.Icon(Ikon.Muell, 16, Farbe.Gefahr, 1.8), ToolTip = "Move to Recycle Bin" };
            System.Windows.Automation.AutomationProperties.SetName(weg, "Move to Recycle Bin");
            weg.Click += delegate { pop.IsOpen = false; Wegwerfen(e); };
            var unten = new Grid();
            unten.Children.Add(reihe);
            weg.HorizontalAlignment = HorizontalAlignment.Right;
            unten.Children.Add(weg);
            sp.Children.Add(unten);
            pop.Child = ZeitUI.Karte(sp, 380);
            pop.IsOpen = true;
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
            var mit = Mit(e);
            k.Gewaehlt += tag => { pop.IsOpen = false; foreach (var x in mit) E.FristSetzen(x, Dauer.BisTag(tag), "until " + tag.ToString("d MMM", Dauer.En)); Neuzeichnen(); };
            pop.Child = new Border { Width = 330, Background = Farbe.B("#FA22252D"), BorderBrush = Farbe.B("#1FFFFFFF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(14), Margin = new Thickness(12), Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Opacity = 0.5 }, Child = k };
            pop.IsOpen = true;
        }

        void Wegschieben(Eintrag e, string ordner)
        {
            Gemeinsam(Mit(e).Select(x => x.Pfad).ToArray(), -12, p => { var x = E.S.Finde(p); if (x != null) E.Verschieben(x, ordner); });
        }

        void Wegwerfen(Eintrag e)
        {
            Gemeinsam(Mit(e).Select(x => x.Pfad).ToArray(), 12, p => { var x = E.S.Finde(p); if (x != null) E.JetztWeg(x); });
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

        Tisch aktuellerTisch;
        /// <summary>Nur für Bilder im Render-Modus.</summary>
        public void KreisFuerBild(string name) { if (aktuellerTisch != null) aktuellerTisch.KreisFuerBild(name, false); }

        void InKorb(string pfad)
        {
            Gemeinsam(Mit(pfad), 12, p => Native.InPapierkorb(p));
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
                    Anordnungen.Umbenennen(System.IO.Path.GetDirectoryName(pfad), System.IO.Path.GetFileName(pfad), neu);
                }
                catch (Exception ex) { Log.Fehler("Rename", ex); }
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
        void AblageZiel(FrameworkElement el, Border rahmen, Func<string> zielOrdner) { AblageZiel(el, rahmen, zielOrdner, null, null); }

        /// <summary>
        /// Was hier abgelegt wird, wandert in den Zielordner (mit Strg: kopieren). federn = kurz darüber halten öffnet das Ziel
        /// (Ebene oder Ordner), damit man direkt weiter hineinziehen kann. hover = sichtbares Echo beim Darüberziehen.
        /// </summary>
        void AblageZiel(FrameworkElement el, Border rahmen, Func<string> zielOrdner, Action federn, Action<bool> hover)
        {
            el.AllowDrop = true;
            Brush alt = rahmen != null ? rahmen.BorderBrush : null;
            DispatcherTimer feder = null;
            Action aus = () => { if (feder != null) feder.Stop(); if (rahmen != null) rahmen.BorderBrush = alt; if (hover != null) hover(false); };
            el.DragEnter += (s, a) =>
            {
                if (!a.Data.GetDataPresent(DataFormats.FileDrop)) return;
                if (rahmen != null) rahmen.BorderBrush = AuswahlRand;
                if (hover != null) hover(true);
                if (federn != null)
                {
                    if (feder == null) { feder = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) }; feder.Tick += delegate { feder.Stop(); federn(); }; }
                    feder.Stop(); feder.Start();
                }
            };
            el.DragLeave += (s, a) => aus();
            el.DragOver += (s, a) =>
            {
                if (!a.Data.GetDataPresent(DataFormats.FileDrop) || zielOrdner() == null) a.Effects = DragDropEffects.None;
                else a.Effects = (a.KeyStates & DragDropKeyStates.ControlKey) != 0 ? DragDropEffects.Copy : DragDropEffects.Move;
                a.Handled = true;
            };
            el.Drop += (s, a) =>
            {
                aus();
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
                int zielAnsicht = AnsichtFuer(ziel);
                if (zielAnsicht < 0 || ansicht == zielAnsicht || ImOrdner) Neuzeichnen(); else Zeige(zielAnsicht);
            };
        }

        /// <summary>Ablegen auf „Expiring“: in den Workbench-Ordner der Standard-Dauer – die Frist setzt sich von selbst.</summary>
        string BefristetOrdner()
        {
            foreach (var z in Paths.ZeitOrdner) if (z[1] == E.Cfg.Standard) return System.IO.Path.Combine(Paths.Wurzel, z[0]);
            return System.IO.Path.Combine(Paths.Wurzel, Paths.ZeitOrdner[2][0]);
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
            OrtSetzen("Recycle Bin");
            if (korb == null) { KorbLesen(); return new Grid(); }
            var liste = KorbSichtbar.Take(500).ToList();
            if (liste.Count == 0) return Leer(Filter.Length > 0 ? Ikon.Suche : Ikon.Muell);
            var wp = Spalten();
            foreach (var x in liste)
            {
                var x2 = x;
                string hilfe = x.Name + "\n" + Wann(x.Geloescht) + " · " + (x.IstOrdner ? "Folder" : Dauer.Groesse(x.Groesse)) + (x.Wb != null ? " · kept for " + Dauer.Lang(x.Wb.Dauer) : "") + "\nfrom " + System.IO.Path.GetDirectoryName(x.Original);
                var sym = Symbol(x.RPfad ?? x.Name, x.RPfad ?? x.Name, x.Name, null, null, false, hilfe);
                sym.Opacity = 0.85;
                Bedienbar(sym, x.RPfad ?? x.Name, () => Zurueckholen(x2), el => UI.Menue(el, new List<KeyValuePair<string, Action>>
                {
                    new KeyValuePair<string, Action>("Restore", () => Zurueckholen(x2)),
                    new KeyValuePair<string, Action>("Delete permanently", () => Loeschen(x2)),
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
                catch (Exception ex) { Log.Fehler("Restore", ex); }
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
            if (lokal.Date == heute) return "today, " + lokal.ToString("HH:mm");
            if (lokal.Date == heute.AddDays(-1)) return "yesterday, " + lokal.ToString("HH:mm");
            return lokal.ToString("d MMM yyyy", Dauer.En);
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
                    if (gruppe.Count > 1) Anzahl(); else LKnopf(Ikon.Oeffnen, "Open", Farbe.Text, () => Oeffnen(e.Pfad));
                    LKnopf(Ikon.Uhr, "Change time", Farbe.Text, null).Click += (s, a) => Zeitkarte((Button)s, e, true);
                    LKnopf(Ikon.Unendlich, "Keep permanently", Farbe.Akzent, () => Wegschieben(e, Paths.Dauerhaft));
                    if (wolke != null) LKnopf(Ikon.Wolke, "Move to " + wolke.Name, Farbe.Text, () => Wegschieben(e, wolke.Pfad));
                    Trenner();
                    LKnopf(Ikon.Muell, "Move to Recycle Bin", Farbe.Gefahr, () => Wegwerfen(e));
                }
            }
            else if (ImOrdner || IstBoard) LeisteOrdner();
            else if (ansicht == Korb && korb != null)
            {
                var x = KorbGewaehlt;
                if (x != null && gruppe.Count > 1)
                {
                    var alle = korb.Where(y => gruppe.Contains(y.RPfad ?? y.Name)).ToList();
                    Anzahl();
                    LKnopf(Ikon.Zurueck, "Restore", Farbe.Akzent, () => { foreach (var y in alle) Zurueckholen(y); });
                    ZweiKlick(LKnopf(Ikon.X, "Delete permanently", Farbe.Gefahr, null), () => { foreach (var y in alle) Loeschen(y); });
                }
                else if (x != null)
                {
                    LKnopf(Ikon.Zurueck, "Restore", Farbe.Akzent, () => Zurueckholen(x));
                    ZweiKlick(LKnopf(Ikon.X, "Delete permanently", Farbe.Gefahr, null), () => Loeschen(x));
                }
                else if (korb.Count > 0)
                {
                    LKnopf(Ikon.Zurueck, "Restore all", Farbe.Text, () => { foreach (var y in KorbSichtbar.ToList()) try { Papierkorb.Wiederherstellen(y); if (y.Wb != null) E.S.D.Papierkorb.Remove(y.Wb); } catch { } E.S.Speichern(); KorbLesen(); });
                    ZweiKlick(LKnopf(Ikon.Muell, "Empty Recycle Bin", Farbe.Gefahr, null), () =>
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

        void Anzahl()
        {
            leisteInhalt.Children.Add(new TextBlock { Text = gruppe.Count.ToString(), FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Farbe.Akzent, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 8, 0) });
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
                if (!scharf) { scharf = true; b.Background = Farbe.B("#40FF6B5E"); b.ToolTip = "Click again to confirm"; t.Start(); return; }
                t.Stop(); scharf = false; b.Background = Brushes.Transparent;
                los();
            };
        }

        // ================= Einstellungen =================

        FrameworkElement FlaecheEinstellungen()
        {
            OrtSetzen("Settings");
            var c = E.Cfg;
            var tafel = new Border { Width = 470, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(16), Background = Farbe.B("#D91A1D24"), BorderBrush = Farbe.B("#1AFFFFFF"), BorderThickness = new Thickness(1), Padding = new Thickness(20, 8, 20, 8), Margin = new Thickness(0, 0, 0, 30) };
            var sp = new StackPanel();
            sp.Children.Add(Zeile("Ask after downloads", Schalter(c.Fragen, v => c.Fragen = v), true));
            var chips = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var d in Dauer.Auswahl)
            {
                string d2 = d;
                var t = new ToggleButton { Style = (Style)Application.Current.FindResource("Chip"), Content = Dauer.Kurz(d), IsChecked = c.Standard == d, Margin = new Thickness(4, 0, 0, 0), Height = 28, Padding = new Thickness(9, 0, 9, 0), FontSize = 12 };
                t.Click += delegate { c.Standard = d2; c.Speichern(); Neuzeichnen(); };
                chips.Children.Add(t);
            }
            sp.Children.Add(Zeile("Default timer", chips, true));
            var wolken = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var o in Cloud.Finden())
            {
                var o2 = o;
                var t = new ToggleButton { Style = (Style)Application.Current.FindResource("Chip"), Content = o.Name, IsChecked = wolke != null && Paths.Gleich(wolke.Pfad, o.Pfad), Margin = new Thickness(4, 0, 0, 0), Height = 28, Padding = new Thickness(9, 0, 9, 0), FontSize = 12, ToolTip = o.Pfad };
                t.Click += delegate { WolkeSetzen(o2); };
                wolken.Children.Add(t);
            }
            var mehr = new Button { Style = (Style)Application.Current.FindResource("Btn"), Content = "…", Width = 34, Height = 28, Padding = new Thickness(0), Margin = new Thickness(4, 0, 0, 0), ToolTip = wolke != null ? wolke.Pfad : "Choose folder" };
            mehr.Click += delegate { WolkeWaehlen(); };
            wolken.Children.Add(mehr);
            sp.Children.Add(Zeile("Cloud", wolken, true));
            sp.Children.Add(Zeile("Ask for new desktop files", Schalter(c.Desktop, v => { c.Desktop = v; E.DesktopBeobachten(v); }), true));
            sp.Children.Add(Zeile("Right-click menu", Schalter(c.Rechtsklick, v => { c.Rechtsklick = v; Install.Rechtsklick(v); }), true));
            sp.Children.Add(Zeile("Browser", BrowserKnopf(), true));
            var rund = new Button { Style = (Style)Application.Current.FindResource("Btn"), Content = "Show", Height = 28, Padding = new Thickness(12, 0, 12, 0), FontSize = 12 };
            rund.Click += delegate { EinfuehrungStarten(); };
            sp.Children.Add(Zeile("Tour", rund, true));
            sp.Children.Add(Zeile("Confirm saves to Workbench", Schalter(c.HinweisSpeichern, v => c.HinweisSpeichern = v), true));
            sp.Children.Add(Zeile("Show in Explorer", Schalter(c.Seitenleiste, v => { c.Seitenleiste = v; Install.Seitenleiste(v); }), true));
            sp.Children.Add(Zeile("Start with Windows", Schalter(c.Autostart, v => { c.Autostart = v; Install.Autostart(v); }), true));
            sp.Children.Add(Zeile("Ctrl + Alt + W", Schalter(c.Tastenkuerzel, v => { c.Tastenkuerzel = v; var app = Application.Current as WbApp; if (app != null) app.Tastenkuerzel(v); }), true));
            var weg = new Button { Style = (Style)Application.Current.FindResource("Leer"), Content = new TextBlock { Text = "Remove", Foreground = Farbe.Gefahr, FontSize = 13 }, Cursor = Cursors.Hand, ToolTip = "Removes Workbench — your files stay where they are" };
            bool scharf = false;
            weg.Click += delegate
            {
                if (!scharf) { scharf = true; ((TextBlock)weg.Content).Text = "Remove Workbench?"; return; }
                Install.Deinstallieren(true);
            };
            // ganz unten, ganz leise: Version – und nur wenn es etwas Neues gibt, ein kleiner Hinweis
            var fuss = new StackPanel { Orientation = Orientation.Horizontal };
            fuss.Children.Add(new TextBlock { Text = "v" + Program.Version, FontSize = 11.5, Foreground = Farbe.B("#59FFFFFF"), VerticalAlignment = VerticalAlignment.Center });
            string neu = Update.Neu(c);
            if (neu != null)
            {
                var link = new TextBlock { Text = "Update to " + neu, FontSize = 11.5, Foreground = Farbe.Akzent, Margin = new Thickness(10, 0, 0, 0), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Restart into the new version" };
                link.MouseLeftButtonUp += delegate { link.Text = "updating…"; Update.Anwenden(); };
                fuss.Children.Add(link);
            }
            var unten = new Grid { MinHeight = 44 };
            unten.Children.Add(fuss);
            weg.HorizontalAlignment = HorizontalAlignment.Right;
            unten.Children.Add(weg);
            sp.Children.Add(unten);
            tafel.Child = sp;
            return tafel;
        }

        /// <summary>„Download with Workbench“ im Browser-Rechtsklick: einrichten bzw. verbunden.</summary>
        FrameworkElement BrowserKnopf()
        {
            var reihe = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var z in Browser.Finden())
            {
                var z2 = z;
                bool da = Browser.IstVerbunden(E.Cfg, z.Name);
                var t = new ToggleButton { Style = (Style)Application.Current.FindResource("Chip"), Content = z.Name, IsChecked = da, Margin = new Thickness(4, 0, 0, 0), Height = 28, Padding = new Thickness(9, 0, 9, 0), FontSize = 12, ToolTip = da ? "Connected — click to set up again" : "Right-click a link › Download with Workbench" };
                t.Click += delegate { t.IsChecked = da; if (!bild) Browser.Einrichten(E.Cfg, z2); };
                reihe.Children.Add(t);
            }
            return reihe;
        }

        FrameworkElement Zeile(string text, FrameworkElement rechts, bool linie)
        {
            var g = new Grid { MinHeight = 44 };
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
            try { Process.Start(new ProcessStartInfo(p) { UseShellExecute = true }); Nutzung.Merken(File.Exists(p) ? System.IO.Path.GetDirectoryName(p) : p); } catch (Exception ex) { Log.Fehler("Open", ex); }
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
            if (ansicht == Netzansicht && netz != null) { netz.SucheFokus(a.Text); a.Handled = true; return; }
            SucheAuf();
            suche.Text += a.Text;
            suche.CaretIndex = suche.Text.Length;
            a.Handled = true;
        }

        void Tasten(object s, KeyEventArgs a)
        {
            bool strg = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            bool imText = Keyboard.FocusedElement is TextBox;
            if (strg && a.Key >= Key.D1 && a.Key <= Key.D9 && IstBlatt(a.Key - Key.D1)) { Zeige(a.Key - Key.D1); a.Handled = true; }
            else if (strg && a.Key == Key.F) { SucheAuf(); suche.SelectAll(); a.Handled = true; }
            else if (strg && a.Key == Key.E && Akt != null) { HomeBearbeiten(!homeBearbeiten); a.Handled = true; }
            else if (a.Key == Key.Escape && homeBearbeiten && !(Keyboard.FocusedElement is TextBox)) { HomeBearbeiten(false); a.Handled = true; }
            else if (a.Key == Key.Escape && StufenOffen) { StufenFertig(true); a.Handled = true; }
            else if (a.Key == Key.Escape)
            {
                if (ansicht == Netzansicht && netz != null && netz.SucheAktiv) netz.SucheLeeren();
                else if (suche.Text.Length > 0) { suche.Text = ""; SucheZu(); Keyboard.ClearFocus(); }
                else if (ansicht == Netzansicht) Zeige(letzteEbene);
                else if (auswahl != null) Waehle(null);
                else Schliessen();
                a.Handled = true;
            }
            else if (imText) return;
            else if (strg && a.Key == Key.A) { if (IstBlatt(ansicht) || ansicht == Korb) AllesMarkieren(); a.Handled = true; }
            else if (strg && a.Key == Key.G) { Zeige(ansicht == Netzansicht ? letzteEbene : Netzansicht); a.Handled = true; }
            else if (ImOrdner && (Keyboard.Modifiers & ModifierKeys.Shift) != 0 && (a.Key == Key.Down || a.Key == Key.Up)) { StufenSchritt(a.Key == Key.Down ? 1 : -1); a.Handled = true; }
            else if (a.Key == Key.Down || a.Key == Key.PageDown) { Schritt(1); a.Handled = true; }
            else if (a.Key == Key.Up || a.Key == Key.PageUp) { Schritt(-1); a.Handled = true; }
            else if (a.Key == Key.Left || (a.SystemKey == Key.Left)) { if (!SeiteWechseln(-1)) Zurueck(); a.Handled = true; }
            else if (a.Key == Key.Right || (a.SystemKey == Key.Right)) { if (!SeiteWechseln(1)) Vor(); a.Handled = true; }
            else if (a.Key == Key.Back && ImOrdner)
            {
                var c = Akt;
                string akt = AktOrdner(c), w = WurzelVon(c);
                if (akt != null && w != null && !Paths.Gleich(akt, w)) Betreten(c, System.IO.Path.GetDirectoryName(akt));
                a.Handled = true;
            }
            else if (a.Key == Key.Enter && auswahl != null && gruppe.Count < 2)
            {
                if (Directory.Exists(auswahl) && ImOrdner) Betreten(Akt, auswahl);
                else if (ansicht != Korb) Oeffnen(auswahl);
                a.Handled = true;
            }
            else if (a.Key == Key.Delete && auswahl != null)
            {
                Entfernen();
                a.Handled = true;
            }
            else if (a.Key == Key.F2 && auswahl != null && gruppe.Count < 2 && ImOrdner)
            {
                Border b;
                if (symbole.TryGetValue(auswahl, out b)) Umbenennen(b, auswahl);
                a.Handled = true;
            }
        }

        // ---------- Gesten ----------
        // Zwei Finger hoch/runter (Mausrad) = Ebene wechseln. Zwei Finger links/rechts = im Ordner zurück/vor.
        // Jede Wischbewegung zählt einmal: Summe sammeln, dann kurz sperren – sonst rattert es durch.
        int wischSumme, radSumme, tiefeRad;
        DateTime wischSperre, radSperre;

        void Rad(object s, MouseWheelEventArgs a)
        {
            if (ImOrdner && (Keyboard.Modifiers & (ModifierKeys.Shift | ModifierKeys.Control)) != 0 && ansicht != Netzansicht)
            {
                // Umschalt+Rad oder Zusammenziehen/Auseinanderziehen: in die Tiefe der Ordner
                a.Handled = true;
                if (!StufenOffen) StufenZeigen();
                tiefeRad += a.Delta;
                if (Math.Abs(tiefeRad) >= 100) { StufenSchritt(tiefeRad < 0 ? 1 : -1); tiefeRad = 0; }
                if ((Keyboard.Modifiers & ModifierKeys.Shift) == 0) StufenNachRuhe();   // Zusammenziehen: kurz Ruhe = loslassen
                return;
            }
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) return;    // Strg+Rad / Zusammenziehen: Zoom im Netz
            if (Keyboard.FocusedElement is TextBox && ((TextBox)Keyboard.FocusedElement).AcceptsReturn) return;
            a.Handled = true;
            if (LeisteRollen(a.Delta, false)) return;
            if (DateTime.UtcNow < radSperre) { radSumme = 0; return; }
            radSumme += a.Delta;
            if (Math.Abs(radSumme) >= 120)
            {
                Schritt(radSumme < 0 ? 1 : -1);
                radSumme = 0;
                radSperre = DateTime.UtcNow.AddMilliseconds(520);
            }
        }

        IntPtr Wischen(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (msg == 0x020B)   // WM_XBUTTONUP: seitliche Maustasten
            {
                int knopf = (int)((w.ToInt64() >> 16) & 0xFFFF);
                if (knopf == 1) Zurueck(); else if (knopf == 2) Vor();
                return IntPtr.Zero;
            }
            if (msg != 0x020E) return IntPtr.Zero;    // WM_MOUSEHWHEEL
            int delta = (short)((w.ToInt64() >> 16) & 0xFFFF);
            handled = true;
            if (LeisteRollen(delta, true)) return IntPtr.Zero;
            if (DateTime.UtcNow < wischSperre) { wischSumme = 0; return IntPtr.Zero; }
            wischSumme += delta;
            if (Math.Abs(wischSumme) >= 160)
            {
                if (!SeiteWechseln(wischSumme < 0 ? -1 : 1)) { if (wischSumme < 0) Zurueck(); else Vor(); }     // in der Tiefe: Seiten; sonst Finger nach rechts = zurück
                wischSumme = 0;
                wischSperre = DateTime.UtcNow.AddMilliseconds(520);
            }
            return IntPtr.Zero;
        }

        Button Pfeil(string icon, string name, bool an, Action a)
        {
            var b = new Button { Style = (Style)Application.Current.FindResource("Rund"), Width = 26, Height = 26, Content = UI.Icon(icon, 13, Farbe.B(an ? "#8CFFFFFF" : "#2EFFFFFF"), 2), IsEnabled = an, ToolTip = name };
            System.Windows.Automation.AutomationProperties.SetName(b, name);
            WindowChrome.SetIsHitTestVisibleInChrome(b, true);
            b.Click += delegate { a(); };
            return b;
        }

        /// <summary>
        /// Schließen = verstecken: das Fenster bleibt gebaut und ist beim nächsten Mal sofort da.
        /// Den Speicher gibt die App danach an Windows zurück (siehe WbApp).
        /// </summary>
        public bool Endgueltig;
        DateTime verstecktSeit;
        public event Action Versteckt;

        void Schliessen()
        {
            var a = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(120));
            a.Completed += delegate
            {
                verstecktSeit = DateTime.UtcNow;
                Hide();
                if (Versteckt != null) Versteckt();
            };
            wurzel.BeginAnimation(OpacityProperty, a);
        }

        /// <summary>Weich zeigen – auch nach dem Verstecken. Beim ersten Mal startet der Rundgang.</summary>
        public void Hereinholen()
        {
            if (!IsVisible)
            {
                if (verstecktSeit != default(DateTime) && (DateTime.UtcNow - verstecktSeit).TotalMinutes > 10) Zeige(StartAnsicht());
                wurzel.BeginAnimation(OpacityProperty, null);
                wurzel.Opacity = 0;
                Show();
                wurzel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
            }
            if (!E.Cfg.EinfuehrungFertig && einfuehrung == null && !rundgangDieseSitzung)
            {
                rundgangDieseSitzung = true;
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                t.Tick += delegate { t.Stop(); EinfuehrungStarten(); };
                t.Start();
            }
        }

        // ================= Rundgang =================

        FrameworkElement reglerEl;
        Einfuehrung einfuehrung;
        bool rundgangDieseSitzung;

        public void EinfuehrungStarten()
        {
            if (einfuehrung != null) return;
            var schritte = new List<Einfuehrung.Schritt>
            {
                new Einfuehrung.Schritt { Titel = "Welcome to Workbench", Text = "Downloads that clean up after themselves, and a desktop that stays tidy.", Extra = Willkommen },
                new Einfuehrung.Schritt { Titel = "Sheets", Text = "Each sheet is one place. Swipe up or down to switch. Drag a sheet on the left to reorder it.", Ziel = () => reglerEl, Vorher = () => Zeige(Befristet) },
                new Einfuehrung.Schritt { Titel = "After every download", Text = "A small card like this one asks how long you need the file. Pick a time with one click — or ignore it, and Workbench picks a sensible default.", Vorher = Beispielkarte },
                new Einfuehrung.Schritt { Titel = "Expiring", Text = "New downloads land here. Each icon drains as time runs out, then the file moves to the Recycle Bin.", Ziel = () => blatt, Vorher = () => Zeige(Befristet) },
                new Einfuehrung.Schritt { Titel = "Make it yours", Text = "Point at the sheets and click +. A sheet can be your desktop, any folder, a board or your cloud.", Ziel = () => reglerEl, Vorher = () => { PlusZeigen(true); } },
                new Einfuehrung.Schritt { Titel = "See everything at once", Text = "The graph view shows all your folders and how they connect. Start typing to find one.", Ziel = () => btnNetz },
                new Einfuehrung.Schritt { Titel = "Nothing is gone for good", Text = "Expired files wait in the Recycle Bin until you empty it.", Ziel = () => btnKorb },
                new Einfuehrung.Schritt { Titel = "Works across Windows", Text = "Right-click any file and choose Workbench to set a timer. You'll also find Workbench in every Save dialog.", Vorher = () => Zeige(Befristet) },
            };
            einfuehrung = new Einfuehrung(wurzel, schritte, nieWieder =>
            {
                if (nieWieder) { E.Cfg.EinfuehrungFertig = true; E.Cfg.Speichern(); }
                wurzel.Children.Remove(einfuehrung);
                einfuehrung = null;
            });
            wurzel.Children.Add(einfuehrung);
            einfuehrung.Starten();
        }

        Frage beispiel;

        /// <summary>
        /// Im Rundgang einmal die echte Download-Karte zeigen – mit einer Beispieldatei, die Workbench nicht kennt:
        /// ein Klick darauf ändert nichts an echten Dateien.
        /// </summary>
        void Beispielkarte()
        {
            if (bild || (beispiel != null && beispiel.Offen)) return;
            try
            {
                string ordner = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Workbench tour");
                Directory.CreateDirectory(ordner);
                string datei = System.IO.Path.Combine(ordner, "Example.pdf");
                if (!File.Exists(datei)) File.WriteAllBytes(datei, new byte[0]);
                var e = new Eintrag { Pfad = datei, Gewaehlt = E.Cfg.Standard, Hinzu = DateTime.UtcNow };
                beispiel = new Frage(E, new List<Eintrag> { e }, false);
                beispiel.Show();
            }
            catch (Exception ex) { Log.Fehler("Beispielkarte", ex); }
        }

        /// <summary>Selbst ausprobieren zählt: wer im Ebenen-Schritt selbst die Ebene wechselt, kommt weiter.</summary>
        void RundgangMitgehen()
        {
            if (einfuehrung != null && einfuehrung.Nr == 1) einfuehrung.Weiter();
        }

        FrameworkElement Willkommen()
        {
            var sp = new StackPanel();
            var z1 = new Grid { MinHeight = 40 };
            z1.Children.Add(new TextBlock { Text = "Desktop shortcut", FontSize = 13, Foreground = Farbe.Text, VerticalAlignment = VerticalAlignment.Center });
            var s1 = new CheckBox { Style = (Style)Application.Current.FindResource("Schalter"), IsChecked = bild || Install.HatDesktopSymbol, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            s1.Click += delegate { Install.DesktopSymbol(s1.IsChecked == true); };
            z1.Children.Add(s1);
            sp.Children.Add(z1);
            var z2 = new Grid { MinHeight = 40 };
            z2.Children.Add(new TextBlock { Text = "Taskbar", FontSize = 13, Foreground = Farbe.Text, VerticalAlignment = VerticalAlignment.Center });
            var b = new Button { Style = (Style)Application.Current.FindResource("Btn"), Content = "Pin", Height = 30, Padding = new Thickness(12, 0, 12, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            z2.Children.Add(b);
            sp.Children.Add(z2);
            var hinweis = new StackPanel { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 2, 0, 0) };
            var pfeil = UI.Icon("M12 4v15M6 13l6 6 6-6", 16, Farbe.Akzent, 2);
            pfeil.VerticalAlignment = VerticalAlignment.Top;
            hinweis.Children.Add(pfeil);
            hinweis.Children.Add(new TextBlock { Text = "Right-click Workbench in the taskbar, then choose Pin to taskbar.", FontSize = 12.5, Foreground = Farbe.Text2, TextWrapping = TextWrapping.Wrap, Width = 250, Margin = new Thickness(8, 0, 0, 0) });
            sp.Children.Add(hinweis);
            var z3 = new Grid { MinHeight = 40, ToolTip = "Right-click a link › Download with Workbench" };
            z3.Children.Add(new TextBlock { Text = "Browser", FontSize = 13, Foreground = Farbe.Text, VerticalAlignment = VerticalAlignment.Center });
            var bb = BrowserKnopf();
            bb.HorizontalAlignment = HorizontalAlignment.Right; bb.VerticalAlignment = VerticalAlignment.Center;
            z3.Children.Add(bb);
            sp.Children.Add(z3);
            b.Click += delegate
            {
                // Windows erlaubt Programmen das Anheften nicht selbst – wir zeigen, wo es geht, und lassen den Pfeil nicken
                hinweis.Visibility = Visibility.Visible;
                b.Visibility = Visibility.Collapsed;
                var tt = new TranslateTransform();
                pfeil.RenderTransform = tt;
                tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, 5, TimeSpan.FromMilliseconds(420)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(4), EasingFunction = UI.Weich });
            };
            return sp;
        }

        /// <summary>Nur für Bilder im Render-Modus.</summary>
        public void RundgangFuerBild(int schritt)
        {
            EinfuehrungStarten();
            for (int i = 0; i < schritt; i++) einfuehrung.Weiter();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!Endgueltig) { e.Cancel = true; Schliessen(); }   // Alt+F4, Taskleiste › Schließen
            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            minuten.Stop();
            Update.Gefunden -= UpdateDa;
            base.OnClosed(e);
        }
    }
}
