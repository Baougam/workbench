using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Workbench
{
    /// <summary>
    /// Home: ein leeres Blatt aus Bausteinen. Nichts ist von selbst da – beim Darüberfahren erscheint ein leises +, das sich
    /// in die Bausteine auffächert (Recent, Shelf, Expiring, Downloads, Screenshots, Ordner, Notiz). Bausteine ohne Überschrift,
    /// nur ein kaum sichtbares Zeichen in der Ecke; ziehen = umsortieren, Rechtsklick = schmal/breit, Ordner wählen, entfernen.
    /// </summary>
    partial class Hauptfenster
    {
        const double ModulB = 252, ModulH = 176, ModulAbstand = 12;

        static readonly string[][] ModulArten =
        {
            new[] { "recent", Ikon.Uhr, "Recent" },
            new[] { "shelf", ReiterZeichen.Ablage, "Shelf" },
            new[] { "expiring", ReiterZeichen.Sanduhr, "Expiring" },
            new[] { "downloads", ReiterZeichen.Download, "Downloads" },
            new[] { "screens", ReiterZeichen.Ausschnitt, "Screenshots" },
            new[] { "folder", ReiterZeichen.Ordner, "Folder" },
            new[] { "note", ReiterZeichen.Notiz, "Note" },
        };

        static string ModulZeichen(string art) { var a = ModulArten.FirstOrDefault(x => x[0] == art); return a != null ? a[1] : Ikon.Plus; }
        static string ModulName(string art) { var a = ModulArten.FirstOrDefault(x => x[0] == art); return a != null ? a[2] : art; }

        // ================= Neues Blatt direkt am + der Schiene =================

        /// <summary>Das + in der Schiene öffnet gleich die Arten – ein Klick, und das Blatt ist da.</summary>
        void BlattMenue(FrameworkElement ziel)
        {
            var pop = new Popup { PlacementTarget = ziel, Placement = PlacementMode.Right, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade, HorizontalOffset = 6 };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var arten = new List<Tuple<string, string, Func<BlattDef>>>
            {
                Tuple.Create(ReiterZeichen.Haus, "Home", (Func<BlattDef>)(() => new BlattDef { Art = "home", Name = "Home" })),
                Tuple.Create(ReiterZeichen.Bildschirm, "Desktop", (Func<BlattDef>)(() => new BlattDef { Art = "folder", Pfad = Paths.Desktop, Name = "Desktop", Symbol = ReiterZeichen.Bildschirm })),
                Tuple.Create(ReiterZeichen.Ordner, "Folder", (Func<BlattDef>)(() => { var p = OrdnerFragen(null); return p == null ? null : new BlattDef { Art = "folder", Pfad = p, Name = System.IO.Path.GetFileName(p.TrimEnd('\\')) }; })),
                Tuple.Create(ReiterZeichen.Board, "Board", (Func<BlattDef>)(() => new BlattDef { Art = "board", Name = "Board" })),
                Tuple.Create(ReiterZeichen.Ausschnitt, "Screenshots", (Func<BlattDef>)(() => { try { Directory.CreateDirectory(Blaetter.Screenshots); } catch { } return new BlattDef { Art = "folder", Pfad = Blaetter.Screenshots, Name = "Screenshots", Symbol = ReiterZeichen.Ausschnitt }; })),
                Tuple.Create(ReiterZeichen.Wolke, wolke != null ? wolke.Name : "Cloud", (Func<BlattDef>)(() => { if (wolke != null) return new BlattDef { Art = "cloud" }; var p = OrdnerFragen(null); return p == null ? null : new BlattDef { Art = "cloud", Pfad = p }; })),
            };
            foreach (var a in arten)
            {
                var a2 = a;
                var k = new Button { Style = (Style)Application.Current.FindResource("Rund"), Width = 40, Height = 40, Content = UI.Icon(a.Item1, 18, Farbe.B("#CCFFFFFF"), 1.5), ToolTip = a.Item2, Margin = new Thickness(1) };
                ToolTipService.SetInitialShowDelay(k, 200);
                System.Windows.Automation.AutomationProperties.SetName(k, a.Item2);
                k.Click += delegate { pop.IsOpen = false; var b = a2.Item3(); if (b != null) BlattHinzu(b); };
                sp.Children.Add(k);
            }
            pop.Child = new Border { Background = Farbe.B("#F5191C23"), BorderBrush = Farbe.B("#1FFFFFFF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(24), Padding = new Thickness(4), Margin = new Thickness(6), Child = sp };
            pop.IsOpen = true;
        }

        static string OrdnerFragen(string start)
        {
            using (var d = new System.Windows.Forms.FolderBrowserDialog { ShowNewFolderButton = true })
            {
                if (!string.IsNullOrEmpty(start) && Directory.Exists(start)) d.SelectedPath = start;
                return d.ShowDialog() == System.Windows.Forms.DialogResult.OK ? d.SelectedPath : null;
            }
        }

        public void BlattHinzu(BlattDef b)
        {
            var l = Blaetter.Liste;
            if (l.Count >= Blaetter.Max) return;
            l.Add(b);
            Blaetter.Speichern();
            SchieneFuellen();
            Zeige(l.Count - 1);
        }

        // ================= Home =================

        FrameworkElement FlaecheHome(BlattDef b)
        {
            OrtSetzen("");
            var wp = new WrapPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(4) };
            foreach (var m in b.Module.ToList()) wp.Children.Add(ModulKarte(b, m));
            bool leer = b.Module.Count == 0;
            var plus = ModulPlus(b, leer);
            if (leer)
            {
                // ganz leer: nur das +, mittig
                var g = new Grid { Background = Brushes.Transparent };
                plus.HorizontalAlignment = HorizontalAlignment.Center;
                plus.VerticalAlignment = VerticalAlignment.Center;
                plus.Margin = new Thickness(0, 0, 0, 30);
                g.Children.Add(plus);
                return g;
            }
            wp.Children.Add(plus);
            var sv = new ScrollViewer { Content = wp, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
            var rahmen = new Grid { Background = Brushes.Transparent };
            rahmen.Children.Add(sv);
            if (!bild)
            {
                plus.Opacity = 0;
                rahmen.MouseEnter += delegate { plus.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200))); };
                rahmen.MouseLeave += delegate { plus.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(260))); };
            }
            return rahmen;
        }

        /// <summary>Das + für einen neuen Baustein: fächert sich beim Darüberfahren in die Bausteine auf.</summary>
        FrameworkElement ModulPlus(BlattDef b, bool gross)
        {
            double w = gross ? 300 : ModulB, h = gross ? 300 : ModulH;
            var feld = new Grid { Width = w, Height = h, Margin = new Thickness(ModulAbstand / 2), Background = Brushes.Transparent };
            var rand = new Border { CornerRadius = new CornerRadius(16), BorderBrush = Farbe.B("#14FFFFFF"), BorderThickness = new Thickness(gross ? 0 : 1), IsHitTestVisible = false };
            feld.Children.Add(rand);
            var plus = UI.Icon(Ikon.Plus, gross ? 52 : 30, Farbe.B("#38FFFFFF"), 1.0);
            plus.HorizontalAlignment = HorizontalAlignment.Center; plus.VerticalAlignment = VerticalAlignment.Center;
            feld.Children.Add(plus);
            double r = gross ? 96 : 62, kd = gross ? 46 : 38;
            var knoepfe = new List<FrameworkElement>();
            for (int i = 0; i < ModulArten.Length; i++)
            {
                double winkel = -Math.PI / 2 + i * 2 * Math.PI / ModulArten.Length;
                var art = ModulArten[i];
                var k = new Border { Width = kd, Height = kd, CornerRadius = new CornerRadius(kd / 2), Background = Farbe.B("#12FFFFFF"), BorderBrush = Farbe.B("#1AFFFFFF"), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, ToolTip = art[2], HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                ToolTipService.SetInitialShowDelay(k, 200);
                k.Child = UI.Icon(art[1], gross ? 20 : 17, Farbe.B("#B3FFFFFF"), 1.5);
                var ziel = new Point(Math.Cos(winkel) * r, Math.Sin(winkel) * r * (gross ? 1 : 0.92));
                var tt = new TranslateTransform(ziel.X, ziel.Y); var st = new ScaleTransform(1, 1);
                var tg = new TransformGroup(); tg.Children.Add(st); tg.Children.Add(tt);
                k.RenderTransform = tg; k.RenderTransformOrigin = new Point(0.5, 0.5); k.Tag = ziel;
                k.MouseEnter += delegate { k.Background = Farbe.B("#26FFFFFF"); };
                k.MouseLeave += delegate { k.Background = Farbe.B("#12FFFFFF"); };
                string a2 = art[0];
                k.MouseLeftButtonDown += (s, a) => a.Handled = true;
                k.MouseLeftButtonUp += (s, a) => { a.Handled = true; ModulNeu(b, a2); };
                if (!bild) { k.Opacity = 0; st.ScaleX = st.ScaleY = 0.6; tt.X = tt.Y = 0; k.IsHitTestVisible = false; }
                knoepfe.Add(k);
                feld.Children.Add(k);
            }
            if (bild) { plus.Opacity = 0.15; return feld; }
            Action<bool> auf = an =>
            {
                plus.BeginAnimation(OpacityProperty, new DoubleAnimation(an ? 0.12 : 1, TimeSpan.FromMilliseconds(180)));
                rand.BeginAnimation(OpacityProperty, new DoubleAnimation(an ? 0 : 1, TimeSpan.FromMilliseconds(180)));
                for (int i = 0; i < knoepfe.Count; i++)
                {
                    var k = knoepfe[i]; var ziel = (Point)k.Tag;
                    var tg = (TransformGroup)k.RenderTransform; var st = (ScaleTransform)tg.Children[0]; var tt = (TranslateTransform)tg.Children[1];
                    var dauer = TimeSpan.FromMilliseconds(an ? 260 : 150); var verz = TimeSpan.FromMilliseconds(an ? i * 20 : 0);
                    IEasingFunction ez = an ? (IEasingFunction)new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 } : UI.Weich;
                    tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(an ? ziel.X : 0, dauer) { BeginTime = verz, EasingFunction = ez });
                    tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(an ? ziel.Y : 0, dauer) { BeginTime = verz, EasingFunction = ez });
                    st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(an ? 1 : 0.6, dauer) { BeginTime = verz, EasingFunction = ez });
                    st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(an ? 1 : 0.6, dauer) { BeginTime = verz, EasingFunction = ez });
                    k.BeginAnimation(OpacityProperty, new DoubleAnimation(an ? 1 : 0, TimeSpan.FromMilliseconds(an ? 170 : 110)) { BeginTime = verz });
                    k.IsHitTestVisible = an;
                }
            };
            feld.MouseEnter += delegate { auf(true); };
            feld.MouseLeave += delegate { auf(false); };
            return feld;
        }

        public void ModulNeu(BlattDef b, string art)
        {
            var m = new Modul { Art = art };
            if (art == "folder") { m.Pfad = bild ? Paths.Dauerhaft : OrdnerFragen(null); if (m.Pfad == null) return; }
            if (art == "recent" || art == "screens" || art == "downloads") m.Breit = true;
            b.Module.Add(m);
            Blaetter.Speichern();
            Neuzeichnen();
        }

        FrameworkElement ModulKarte(BlattDef b, Modul m)
        {
            var karte = new Border { Width = m.Breit ? ModulB * 2 + ModulAbstand : ModulB, Height = ModulH, Margin = new Thickness(ModulAbstand / 2), CornerRadius = new CornerRadius(16), Background = Farbe.B("#08FFFFFF"), Tag = m };
            var g = new Grid();
            var ecke = UI.Icon(ModulZeichen(m.Art), 12, Farbe.B("#40FFFFFF"), 1.6);
            ecke.HorizontalAlignment = HorizontalAlignment.Left; ecke.VerticalAlignment = VerticalAlignment.Top; ecke.Margin = new Thickness(12, 11, 0, 0);
            g.Children.Add(ecke);
            var inhalt = ModulInhalt(b, m, karte);
            inhalt.Margin = new Thickness(10, 30, 10, 10);
            g.Children.Add(inhalt);
            karte.Child = g;
            karte.MouseEnter += delegate { karte.Background = Farbe.B("#0DFFFFFF"); };
            karte.MouseLeave += delegate { karte.Background = Farbe.B("#08FFFFFF"); };
            karte.MouseLeftButtonDown += (s, a) => a.Handled = true;      // kein Rahmen-Aufziehen auf Bausteinen

            // ziehen (an der Ecke oder einer leeren Stelle) = umsortieren
            Point start = new Point(); bool druck = false;
            karte.PreviewMouseLeftButtonDown += (s, a) => { if (a.OriginalSource is TextBox) return; start = a.GetPosition(this); druck = true; };
            karte.PreviewMouseMove += (s, a) =>
            {
                if (!druck || a.LeftButton != MouseButtonState.Pressed) return;
                if (Keyboard.FocusedElement is TextBox && karte.IsKeyboardFocusWithin) return;
                var p = a.GetPosition(this);
                if (Math.Abs(p.X - start.X) < 8 && Math.Abs(p.Y - start.Y) < 8) return;
                if (!(a.OriginalSource == karte || a.OriginalSource == g || a.OriginalSource is System.Windows.Shapes.Path || a.OriginalSource is Canvas || a.OriginalSource is Viewbox)) return;
                druck = false;
                karte.Opacity = 0.5;
                try { DragDrop.DoDragDrop(karte, new DataObject("wb-modul", m.Id), DragDropEffects.Move); } catch { }
                karte.Opacity = 1;
            };
            karte.PreviewMouseLeftButtonUp += delegate { druck = false; };
            karte.AllowDrop = true;
            karte.DragOver += (s, a) => { if (a.Data.GetDataPresent("wb-modul")) { a.Effects = DragDropEffects.Move; a.Handled = true; } };
            karte.Drop += (s, a) =>
            {
                if (!a.Data.GetDataPresent("wb-modul")) return;
                a.Handled = true;
                var id = a.Data.GetData("wb-modul") as string;
                var von = b.Module.FindIndex(x => x.Id == id); var nach = b.Module.IndexOf(m);
                if (von < 0 || nach < 0 || von == nach) return;
                var x2 = b.Module[von]; b.Module.RemoveAt(von); b.Module.Insert(nach, x2);
                Blaetter.Speichern();
                Neuzeichnen();
            };
            karte.MouseRightButtonUp += (s, a) =>
            {
                if (a.Handled) return;
                a.Handled = true;
                var menue = new List<KeyValuePair<string, Action>>
                {
                    new KeyValuePair<string, Action>(m.Breit ? "Narrow" : "Wide", () => { m.Breit = !m.Breit; Blaetter.Speichern(); Neuzeichnen(); }),
                };
                if (m.Art == "folder") menue.Add(new KeyValuePair<string, Action>("Choose folder", () => { var p = OrdnerFragen(m.Pfad); if (p != null) { m.Pfad = p; Blaetter.Speichern(); Neuzeichnen(); } }));
                menue.Add(new KeyValuePair<string, Action>("-", null));
                menue.Add(new KeyValuePair<string, Action>("Remove", () => { b.Module.Remove(m); Blaetter.Speichern(); Neuzeichnen(); }));
                UI.Menue(karte, menue, true);
            };
            return karte;
        }

        /// <summary>Was in einem Baustein steht – Symbole ohne Text (Name im Tooltip), Notiz als freies Schreibfeld.</summary>
        FrameworkElement ModulInhalt(BlattDef b, Modul m, Border karte)
        {
            int max = m.Breit ? 22 : 10;
            if (m.Art == "note")
            {
                var tb = new TextBox { Text = m.Text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Farbe.Text, CaretBrush = Farbe.Akzent, FontSize = 13.5, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Padding = new Thickness(2, 0, 2, 0) };
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
                t.Tick += delegate { t.Stop(); m.Text = tb.Text; Blaetter.Speichern(); };
                tb.TextChanged += delegate { t.Stop(); t.Start(); };
                tb.LostKeyboardFocus += delegate { if (m.Text != tb.Text) { m.Text = tb.Text; Blaetter.Speichern(); } };
                return tb;
            }
            List<string> pfade;
            if (m.Art == "recent") pfade = Zuletzt.Liste(E).Take(max).ToList();
            else if (m.Art == "shelf") pfade = Blaetter.D.Ablage.Where(p => File.Exists(p) || Directory.Exists(p)).Take(max).ToList();
            else if (m.Art == "expiring") pfade = E.S.D.Eintraege.Where(x => x.Ablauf.HasValue && (File.Exists(x.Pfad) || Directory.Exists(x.Pfad))).OrderBy(x => x.Ablauf.Value).Take(max).Select(x => x.Pfad).ToList();
            else if (m.Art == "downloads") pfade = Neueste(Paths.Downloads, max, false);
            else if (m.Art == "screens") pfade = Neueste(Blaetter.Screenshots, m.Breit ? 6 : 3, true);
            else pfade = Neueste(m.Pfad, max, false, true);

            var wp = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
            if (m.Art == "screens")
                foreach (var p in pfade) wp.Children.Add(Vorschaukachel(p));
            else
                foreach (var p in pfade) wp.Children.Add(m.Art == "expiring" ? AblaufKachel(p) : Kachel(p, m.Art == "shelf" ? "shelf" : "recent"));
            if (pfade.Count == 0)
            {
                var leer = UI.Icon(ModulZeichen(m.Art), 26, Farbe.B("#1FFFFFFF"), 1.3);
                leer.HorizontalAlignment = HorizontalAlignment.Center; leer.VerticalAlignment = VerticalAlignment.Center; leer.Margin = new Thickness(0, 0, 0, 20);
                if (m.Art == "shelf") ((FrameworkElement)leer).ToolTip = "Drop files here to park them";
                var g = new Grid(); g.Children.Add(leer);
                if (m.Art == "shelf") AblageAufKarte(karte, m);
                return g;
            }
            if (m.Art == "shelf") AblageAufKarte(karte, m);
            if (m.Art == "recent") recentFuellen = () => { if (karte.IsLoaded) Neuzeichnen(); };
            return wp;
        }

        void AblageAufKarte(Border karte, Modul m)
        {
            karte.DragOver += (s, a) => { if (a.Data.GetDataPresent(DataFormats.FileDrop)) { a.Effects = DragDropEffects.Link; a.Handled = true; } };
            karte.Drop += (s, a) =>
            {
                var pfade = a.Data.GetData(DataFormats.FileDrop) as string[];
                if (pfade == null) return;
                a.Handled = true;
                Blaetter.Parken(pfade);
                Neuzeichnen();
            };
        }

        static List<string> Neueste(string ordner, int max, bool nurBilder, bool auchOrdner = false)
        {
            try
            {
                if (string.IsNullOrEmpty(ordner) || !Directory.Exists(ordner)) return new List<string>();
                var di = new DirectoryInfo(ordner);
                IEnumerable<FileSystemInfo> l = di.EnumerateFiles();
                if (auchOrdner) l = di.EnumerateDirectories().Cast<FileSystemInfo>().Concat(l);
                string[] bilder = { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp" };
                return l.Where(f => !Engine.IstHilfsdatei(f.FullName) && (!nurBilder || bilder.Contains(f.Extension.ToLowerInvariant())))
                    .OrderByDescending(f => f.LastWriteTimeUtc).Take(max).Select(f => f.FullName).ToList();
            }
            catch { return new List<string>(); }
        }

        /// <summary>Wie Kachel, aber mit dem leerenden Symbol (Restzeit).</summary>
        FrameworkElement AblaufKachel(string p)
        {
            var k = (Border)Kachel(p, "recent");
            var e = E.S.Finde(p);
            if (e != null && e.Ablauf.HasValue)
            {
                double rest = (e.Ablauf.Value - DateTime.UtcNow).TotalSeconds / Math.Max(1, (e.Ablauf.Value - e.Hinzu).TotalSeconds);
                var bildEl = Vorschau.Bild(p, 30, 30, false, rest, (e.Ablauf.Value - DateTime.UtcNow).TotalHours < 24);
                bildEl.HorizontalAlignment = HorizontalAlignment.Center; bildEl.VerticalAlignment = VerticalAlignment.Center;
                k.Child = bildEl;
                k.ToolTip = System.IO.Path.GetFileName(p) + "\n" + Kurz(e.Ablauf.Value, DateTime.UtcNow) + " left";
            }
            return k;
        }

        /// <summary>Screenshots als echte kleine Vorschau.</summary>
        FrameworkElement Vorschaukachel(string p)
        {
            var k = (Border)Kachel(p, "recent");
            k.Width = 74; k.Height = 58; k.CornerRadius = new CornerRadius(8); k.Margin = new Thickness(3);
            var bildEl = Vorschau.Bild(p, 70, 54);
            bildEl.HorizontalAlignment = HorizontalAlignment.Center; bildEl.VerticalAlignment = VerticalAlignment.Center;
            k.Child = bildEl;
            return k;
        }
    }
}
