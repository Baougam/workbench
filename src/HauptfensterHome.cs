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
    /// Home: Bausteine auf einem Raster. Im Alltag ruhig – nur Inhalt, ein leises Zeichen je Baustein, kein Text.
    /// Bearbeiten (Stift oben rechts, Rechtsklick auf leere Stelle, Strg+E): Raster erscheint, Bausteine ziehen = verschieben,
    /// Ecke ziehen = Größe, Zahnrad = Einstellungen (Symbolgröße, Kacheln/Liste, Filter, Ordner, Titel), × = weg (rückgängig),
    /// unten: + Baustein, Vorlagen, Fertig (Esc).
    /// </summary>
    partial class Hauptfenster
    {
        const double Zelle = 120, Luecke = 12;

        static readonly string[][] ModulArten =
        {
            new[] { "expiring", ReiterZeichen.Sanduhr, "Expiring" },
            new[] { "recent", Ikon.Uhr, "Recent" },
            new[] { "downloads", ReiterZeichen.Download, "Downloads" },
            new[] { "pinned", ReiterZeichen.Nadel, "Pinned" },
            new[] { "shelf", ReiterZeichen.Ablage, "Shelf" },
            new[] { "screens", ReiterZeichen.Ausschnitt, "Screenshots" },
            new[] { "folder", ReiterZeichen.Ordner, "Folder" },
            new[] { "note", ReiterZeichen.Notiz, "Note" },
        };

        static string ModulZeichen(string art) { var a = ModulArten.FirstOrDefault(x => x[0] == art); return a != null ? a[1] : Ikon.Plus; }
        static string ModulName(string art) { var a = ModulArten.FirstOrDefault(x => x[0] == art); return a != null ? a[2] : art; }

        static readonly string[][] Filterarten =
        {
            new[] { "", "All" },
            new[] { "docs", "Documents" },
            new[] { "images", "Images" },
            new[] { "archives", "Archives" },
            new[] { "apps", "Installers" },
        };

        static bool PasstFilter(string pfad, string filter)
        {
            if (string.IsNullOrEmpty(filter)) return true;
            string e = System.IO.Path.GetExtension(pfad).ToLowerInvariant();
            switch (filter)
            {
                case "docs": return new[] { ".pdf", ".doc", ".docx", ".odt", ".rtf", ".txt", ".md", ".xls", ".xlsx", ".csv", ".ppt", ".pptx", ".pages", ".numbers", ".key" }.Contains(e);
                case "images": return new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".heic", ".tif", ".tiff", ".svg", ".psd" }.Contains(e);
                case "archives": return new[] { ".zip", ".rar", ".7z", ".gz", ".tar", ".iso", ".dmg" }.Contains(e);
                case "apps": return Dauer.IstInstaller(pfad);
            }
            return true;
        }

        // ================= Neues Blatt direkt am + der Schiene =================

        /// <summary>Das + in der Schiene öffnet gleich die Arten – ein Klick, und das Blatt ist da.</summary>
        void BlattMenue(FrameworkElement ziel)
        {
            var pop = new Popup { PlacementTarget = ziel, Placement = PlacementMode.Right, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade, HorizontalOffset = 6 };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var arten = new List<Tuple<string, string, Func<BlattDef>>>
            {
                Tuple.Create(ReiterZeichen.Haus, "Home", (Func<BlattDef>)(() => { var h = new BlattDef { Art = "home", Name = "Home" }; Vorlage(h, "focus"); h.Vorschlag = true; return h; })),
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
            pop.Child = Pille(sp);
            pop.IsOpen = true;
        }

        static Border Pille(UIElement inhalt)
        {
            return new Border { Background = Farbe.B("#F5191C23"), BorderBrush = Farbe.B("#1FFFFFFF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(24), Padding = new Thickness(4), Margin = new Thickness(6), Child = inhalt };
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

        // ================= Vorlagen =================

        static readonly string[][] Vorlagen = { new[] { "focus", "Focus" }, new[] { "daily", "Daily" }, new[] { "desk", "Desk" }, new[] { "blank", "Blank" } };

        /// <summary>Drei ruhige Vorlagen und „leer“. Ersetzt die Bausteine des Blatts.</summary>
        public static void Vorlage(BlattDef b, string name)
        {
            b.Module.Clear();
            Action<string, int, int, int, int> m = (art, x, y, w, h) => b.Module.Add(new Modul { Art = art, X = x, Y = y, W = w, H = h, Groesse = 1 });
            switch (name)
            {
                case "focus": m("expiring", 0, 0, 6, 3); break;
                case "daily": m("recent", 0, 0, 6, 1); m("expiring", 0, 1, 3, 2); m("downloads", 3, 1, 3, 2); break;
                case "desk": m("pinned", 0, 0, 3, 2); m("note", 3, 0, 3, 2); m("screens", 0, 2, 6, 1); break;
            }
            foreach (var x in b.Module.Where(x => x.Art == "recent" || x.Art == "screens")) x.Groesse = 1;
        }

        // ================= Home =================

        bool homeBearbeiten;
        Modul weggelegt; int weggelegtBei = -1;

        FrameworkElement FlaecheHome(BlattDef b)
        {
            OrtSetzen(homeBearbeiten ? "Edit" : "");
            if (b.Module.Count == 0 && !homeBearbeiten && !b.Vorschlag) { Vorlage(b, "focus"); b.Vorschlag = true; Blaetter.Speichern(); }
            int spalten = Math.Max(2, (int)((Math.Max(600, buehne.ActualWidth > 0 ? buehne.ActualWidth : 1000) - 8) / (Zelle + Luecke)));
            foreach (var m in b.Module) { if (m.W < 1) m.W = 2; if (m.H < 1) m.H = 2; if (m.W > spalten) m.W = spalten; }
            Ordnen(b, spalten);
            int zeilen = b.Module.Count == 0 ? 3 : b.Module.Max(m => m.Y + m.H);
            var flaeche = new Canvas { Width = spalten * (Zelle + Luecke), Height = (zeilen + (homeBearbeiten ? 2 : 0)) * (Zelle + Luecke), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Background = Brushes.Transparent, Margin = new Thickness(4) };
            if (homeBearbeiten)
                for (int y = 0; y < zeilen + 2; y++)
                    for (int x = 0; x < spalten; x++)
                    {
                        var z = new Border { Width = Zelle, Height = Zelle, CornerRadius = new CornerRadius(14), BorderBrush = Farbe.B("#12FFFFFF"), BorderThickness = new Thickness(1), IsHitTestVisible = false };
                        Canvas.SetLeft(z, x * (Zelle + Luecke)); Canvas.SetTop(z, y * (Zelle + Luecke));
                        flaeche.Children.Add(z);
                    }
            foreach (var m in b.Module.ToList()) flaeche.Children.Add(ModulKarte(b, m, flaeche, spalten));

            var sv = new ScrollViewer { Content = flaeche, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
            var g = new Grid { Background = Brushes.Transparent };
            g.Children.Add(sv);
            if (b.Module.Count == 0 && !homeBearbeiten)
            {
                var plus = new Button { Style = (Style)Application.Current.FindResource("Rund"), Width = 64, Height = 64, Content = UI.Icon(Ikon.Plus, 30, Farbe.B("#59FFFFFF"), 1.2), ToolTip = "Add something", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 30) };
                plus.Click += delegate { HomeBearbeiten(true); };
                g.Children.Add(plus);
            }
            g.MouseRightButtonUp += (s, a) =>
            {
                if (a.Handled) return;
                a.Handled = true;
                UI.Menue(g, new List<KeyValuePair<string, Action>> { new KeyValuePair<string, Action>(homeBearbeiten ? "Done" : "Customize", () => HomeBearbeiten(!homeBearbeiten)) }, true);
            };
            if (homeBearbeiten) g.Children.Add(BearbeitenLeiste(b));
            else if (b.Vorschlag) g.Children.Add(VorschlagLeiste(b));
            return g;
        }

        public void HomeBearbeiten(bool an)
        {
            homeBearbeiten = an;
            if (!an) { weggelegt = null; var b = Akt; if (b != null && b.Vorschlag) { b.Vorschlag = false; Blaetter.Speichern(); } }
            Neuzeichnen();
        }

        /// <summary>Freie Plätze suchen: Bausteine ohne gültigen Platz bekommen den ersten freien.</summary>
        static void Ordnen(BlattDef b, int spalten)
        {
            var belegt = new HashSet<long>();
            foreach (var m in b.Module)
            {
                bool ok = m.X >= 0 && m.Y >= 0 && m.X + m.W <= spalten && Frei(belegt, m.X, m.Y, m.W, m.H);
                if (!ok)
                {
                    for (int y = 0; y < 200 && !ok; y++) for (int x = 0; x + m.W <= spalten && !ok; x++) if (Frei(belegt, x, y, m.W, m.H)) { m.X = x; m.Y = y; ok = true; }
                }
                Belegen(belegt, m.X, m.Y, m.W, m.H);
            }
        }

        static bool Frei(HashSet<long> belegt, int x, int y, int w, int h)
        {
            for (int i = x; i < x + w; i++) for (int j = y; j < y + h; j++) if (belegt.Contains(((long)i << 20) + j)) return false;
            return true;
        }

        static void Belegen(HashSet<long> belegt, int x, int y, int w, int h) { for (int i = x; i < x + w; i++) for (int j = y; j < y + h; j++) belegt.Add(((long)i << 20) + j); }

        static bool PlatzFrei(BlattDef b, Modul ohne, int x, int y, int w, int h, int spalten)
        {
            if (x < 0 || y < 0 || x + w > spalten) return false;
            var belegt = new HashSet<long>();
            foreach (var m in b.Module) if (m != ohne) Belegen(belegt, m.X, m.Y, m.W, m.H);
            return Frei(belegt, x, y, w, h);
        }

        public void ModulNeu(BlattDef b, string art)
        {
            var m = new Modul { Art = art, W = art == "recent" || art == "screens" ? 4 : 2, H = art == "recent" || art == "screens" ? 1 : 2, X = -1, Y = -1, Groesse = 1 };
            if (art == "folder") { m.Pfad = bild ? Paths.Dauerhaft : OrdnerFragen(null); if (m.Pfad == null) return; }
            b.Module.Add(m);
            b.Vorschlag = false;
            Blaetter.Speichern();
            Neuzeichnen();
        }

        static double Px(int zellen) { return zellen * Zelle + (zellen - 1) * Luecke; }

        FrameworkElement ModulKarte(BlattDef b, Modul m, Canvas flaeche, int spalten)
        {
            var karte = new Border { Width = Px(m.W), Height = Px(m.H), CornerRadius = new CornerRadius(16), Background = Farbe.B(homeBearbeiten ? "#F2202329" : "#08FFFFFF"), BorderBrush = Farbe.B(homeBearbeiten ? "#33FFFFFF" : "#00FFFFFF"), BorderThickness = new Thickness(1), Tag = m };
            Canvas.SetLeft(karte, m.X * (Zelle + Luecke)); Canvas.SetTop(karte, m.Y * (Zelle + Luecke));
            var g = new Grid();
            var kopf = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 10, 0, 0), IsHitTestVisible = false };
            kopf.Children.Add(UI.Icon(ModulZeichen(m.Art), 12, Farbe.B(homeBearbeiten ? "#99FFFFFF" : "#40FFFFFF"), 1.6));
            if (homeBearbeiten) kopf.Children.Add(new TextBlock { Text = string.IsNullOrEmpty(m.Titel) ? ModulName(m.Art) : m.Titel, FontSize = 11.5, Foreground = Farbe.B("#B3FFFFFF"), Margin = new Thickness(6, -1, 0, 0) });
            else if (!string.IsNullOrEmpty(m.Titel)) { }
            g.Children.Add(kopf);
            var inhalt = ModulInhalt(b, m, karte);
            inhalt.Margin = new Thickness(10, 28, 10, 8);
            if (homeBearbeiten) { inhalt.IsHitTestVisible = false; inhalt.Opacity = 0.55; }
            g.Children.Add(inhalt);
            karte.Child = g;
            karte.MouseLeftButtonDown += (s, a) => a.Handled = true;      // kein Rahmen-Aufziehen auf Bausteinen
            if (!homeBearbeiten)
            {
                karte.MouseEnter += delegate { karte.Background = Farbe.B("#0DFFFFFF"); };
                karte.MouseLeave += delegate { karte.Background = Farbe.B("#08FFFFFF"); };
                karte.MouseRightButtonUp += (s, a) =>
                {
                    if (a.Handled) return;
                    a.Handled = true;
                    UI.Menue(karte, new List<KeyValuePair<string, Action>> { new KeyValuePair<string, Action>("Customize", () => HomeBearbeiten(true)) }, true);
                };
                return karte;
            }

            // ---- Bearbeiten: Zahnrad, ×, Ecke, ziehen ----
            var knoepfe = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 4, 0) };
            var rad = new Button { Style = (Style)Application.Current.FindResource("Rund"), Width = 26, Height = 26, Content = UI.Icon(Ikon.Regler, 12, Farbe.B("#CCFFFFFF"), 1.6), ToolTip = "Settings" };
            rad.Click += delegate { ModulEinstellen(rad, b, m); };
            var weg = new Button { Style = (Style)Application.Current.FindResource("Rund"), Width = 26, Height = 26, Content = UI.Icon(Ikon.X, 11, Farbe.B("#CCFFFFFF"), 1.8), ToolTip = "Remove" };
            weg.Click += delegate { weggelegtBei = b.Module.IndexOf(m); weggelegt = m; b.Module.Remove(m); Blaetter.Speichern(); Neuzeichnen(); };
            knoepfe.Children.Add(rad); knoepfe.Children.Add(weg);
            g.Children.Add(knoepfe);
            var ecke = new Border { Width = 18, Height = 18, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Background = Brushes.Transparent, Cursor = Cursors.SizeNWSE, Margin = new Thickness(0, 0, 3, 3) };
            ecke.Child = UI.Icon("M20 10v10H10M20 15l-5 5", 14, Farbe.B("#99FFFFFF"), 1.8);
            g.Children.Add(ecke);
            karte.Cursor = Cursors.SizeAll;

            Point start = new Point(); bool zieht = false, groesse = false;
            int x0 = m.X, y0 = m.Y, w0 = m.W, h0 = m.H;
            var tt = new TranslateTransform();
            karte.RenderTransform = tt;
            MouseButtonEventHandler los = null;
            los = (s, a) =>
            {
                if (a.OriginalSource is DependencyObject && (IstIn((DependencyObject)a.OriginalSource, rad) || IstIn((DependencyObject)a.OriginalSource, weg))) return;
                groesse = IstIn((DependencyObject)a.OriginalSource, ecke);
                start = a.GetPosition(flaeche); zieht = true; x0 = m.X; y0 = m.Y; w0 = m.W; h0 = m.H;
                karte.CaptureMouse(); Panel.SetZIndex(karte, 10);
                a.Handled = true;
            };
            karte.PreviewMouseLeftButtonDown += los;
            karte.MouseMove += (s, a) =>
            {
                if (!zieht) return;
                var p = a.GetPosition(flaeche);
                double dx = p.X - start.X, dy = p.Y - start.Y;
                if (groesse)
                {
                    int w = Math.Max(1, Math.Min(spalten - m.X, w0 + (int)Math.Round(dx / (Zelle + Luecke))));
                    int h = Math.Max(1, Math.Min(4, h0 + (int)Math.Round(dy / (Zelle + Luecke))));
                    karte.Width = Math.Max(Zelle * 0.6, Px(w0) + dx); karte.Height = Math.Max(Zelle * 0.6, Px(h0) + dy);
                    karte.Tag = new int[] { w, h };
                }
                else { tt.X = dx; tt.Y = dy; }
            };
            karte.MouseLeftButtonUp += (s, a) =>
            {
                if (!zieht) return;
                zieht = false;
                karte.ReleaseMouseCapture();
                var p = a.GetPosition(flaeche);
                double dx = p.X - start.X, dy = p.Y - start.Y;
                if (groesse)
                {
                    var wh = karte.Tag as int[];
                    if (wh != null && PlatzFrei(b, m, m.X, m.Y, wh[0], wh[1], spalten)) { m.W = wh[0]; m.H = wh[1]; }
                }
                else
                {
                    int nx = x0 + (int)Math.Round(dx / (Zelle + Luecke)), ny = y0 + (int)Math.Round(dy / (Zelle + Luecke));
                    if (PlatzFrei(b, m, nx, ny, m.W, m.H, spalten)) { m.X = nx; m.Y = ny; }
                    else
                    {
                        // auf einen anderen Baustein gezogen: Plätze tauschen
                        var ziel = b.Module.FirstOrDefault(o => o != m && nx >= o.X && nx < o.X + o.W && ny >= o.Y && ny < o.Y + o.H);
                        if (ziel != null) { int zx = ziel.X, zy = ziel.Y; ziel.X = m.X; ziel.Y = m.Y; m.X = zx; m.Y = zy; }
                    }
                }
                Blaetter.Speichern();
                Neuzeichnen();
            };
            return karte;
        }

        static bool IstIn(DependencyObject el, DependencyObject ziel)
        {
            while (el != null) { if (el == ziel) return true; el = el is Visual || el is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(el) : LogicalTreeHelper.GetParent(el); }
            return false;
        }

        /// <summary>Einstellungen eines Bausteins – direkt daneben, nur was wirklich etwas ändert.</summary>
        void ModulEinstellen(FrameworkElement ziel, BlattDef b, Modul m)
        {
            var pop = new Popup { PlacementTarget = ziel, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade };
            var sp = new StackPanel { Width = 248, Margin = new Thickness(6) };
            Func<string, FrameworkElement, FrameworkElement> zeile = (t, el) =>
            {
                var z = new Grid { Margin = new Thickness(4, 6, 4, 6) };
                z.Children.Add(new TextBlock { Text = t, FontSize = 12, Foreground = Farbe.Text3, VerticalAlignment = VerticalAlignment.Center });
                el.HorizontalAlignment = HorizontalAlignment.Right; z.Children.Add(el);
                return z;
            };
            Action neu = () => { Blaetter.Speichern(); Neuzeichnen(); };
            Func<string, bool, Action, ToggleButton> chip = (t, an, tun) =>
            {
                var c = new ToggleButton { Style = (Style)Application.Current.FindResource("Chip"), Content = t, IsChecked = an, Height = 26, Padding = new Thickness(8, 0, 8, 0), FontSize = 11.5, Margin = new Thickness(3, 0, 0, 0) };
                c.Click += delegate { pop.IsOpen = false; tun(); neu(); };
                return c;
            };
            if (m.Art != "note")
            {
                var g = new StackPanel { Orientation = Orientation.Horizontal };
                string[] gr = { "S", "M", "L" };
                for (int i = 0; i < 3; i++) { int i2 = i; g.Children.Add(chip(gr[i], m.Groesse == i, () => m.Groesse = i2)); }
                sp.Children.Add(zeile("Icons", g));
                var ans = new StackPanel { Orientation = Orientation.Horizontal };
                ans.Children.Add(chip("Tiles", !m.Liste, () => m.Liste = false));
                ans.Children.Add(chip("List", m.Liste, () => m.Liste = true));
                sp.Children.Add(zeile("View", ans));
            }
            if (m.Art == "recent" || m.Art == "downloads" || m.Art == "folder" || m.Art == "pinned")
            {
                var f = new WrapPanel { Width = 170, HorizontalAlignment = HorizontalAlignment.Right };
                foreach (var x in Filterarten) { var x2 = x; f.Children.Add(chip(x[1], (m.Filter ?? "") == x[0], () => m.Filter = x2[0])); }
                foreach (UIElement c in f.Children) ((FrameworkElement)c).Margin = new Thickness(3, 0, 0, 4);
                sp.Children.Add(zeile("Show", f));
            }
            if (m.Art == "folder")
            {
                var k = UI.Knopf(string.IsNullOrEmpty(m.Pfad) ? "Choose" : System.IO.Path.GetFileName(m.Pfad.TrimEnd('\\')), null, "Btn");
                k.Height = 26; k.FontSize = 11.5; k.MaxWidth = 150;
                k.Click += delegate { pop.IsOpen = false; var p = OrdnerFragen(m.Pfad); if (p != null) { m.Pfad = p; neu(); } };
                sp.Children.Add(zeile("Folder", k));
            }
            var titel = new TextBox { Text = m.Titel ?? "", Width = 150, FontSize = 12, Foreground = Farbe.Text, CaretBrush = Farbe.Akzent, Background = Farbe.B("#14FFFFFF"), BorderThickness = new Thickness(0), Padding = new Thickness(6, 3, 6, 3) };
            titel.LostKeyboardFocus += delegate { if ((m.Titel ?? "") != titel.Text.Trim()) { m.Titel = titel.Text.Trim(); neu(); } };
            titel.KeyDown += (s, a) => { if (a.Key == Key.Enter) { pop.IsOpen = false; m.Titel = titel.Text.Trim(); neu(); } };
            sp.Children.Add(zeile("Title", titel));
            pop.Child = new Border { Background = Farbe.B("#F5191C23"), BorderBrush = Farbe.B("#1FFFFFFF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(6), Margin = new Thickness(6), Child = sp };
            pop.IsOpen = true;
        }

        /// <summary>Unten beim Bearbeiten: + Baustein · Vorlagen · Rückgängig · Fertig.</summary>
        FrameworkElement BearbeitenLeiste(BlattDef b)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var plus = new Button { Style = (Style)Application.Current.FindResource("Btn"), Height = 34, Padding = new Thickness(12, 0, 14, 0), Content = Mit2(Ikon.Plus, "Add"), ToolTip = "Add a module", Margin = new Thickness(2, 0, 2, 0) };
            plus.Click += delegate { ModulWahl(plus, b); };
            var vorl = new Button { Style = (Style)Application.Current.FindResource("Btn"), Height = 34, Padding = new Thickness(12, 0, 14, 0), Content = Mit2(ReiterZeichen.Board, "Layouts"), ToolTip = "Start from a layout", Margin = new Thickness(2, 0, 2, 0) };
            vorl.Click += delegate { VorlagenWahl(vorl, b, true); };
            sp.Children.Add(plus); sp.Children.Add(vorl);
            if (weggelegt != null)
            {
                var zurueck = new Button { Style = (Style)Application.Current.FindResource("Btn"), Height = 34, Padding = new Thickness(12, 0, 14, 0), Content = Mit2(Ikon.Zurueck, "Undo"), Margin = new Thickness(2, 0, 2, 0) };
                zurueck.Click += delegate { b.Module.Insert(Math.Max(0, Math.Min(weggelegtBei, b.Module.Count)), weggelegt); weggelegt = null; Blaetter.Speichern(); Neuzeichnen(); };
                sp.Children.Add(zurueck);
            }
            var fertig = new Button { Style = (Style)Application.Current.FindResource("BtnPri"), Height = 34, Padding = new Thickness(16, 0, 16, 0), Content = "Done", Margin = new Thickness(8, 0, 2, 0) };
            fertig.Click += delegate { HomeBearbeiten(false); };
            sp.Children.Add(fertig);
            var p = Pille(sp);
            p.HorizontalAlignment = HorizontalAlignment.Center; p.VerticalAlignment = VerticalAlignment.Bottom; p.Margin = new Thickness(0, 0, 0, 4);
            return p;
        }

        static FrameworkElement Mit2(string icon, string text)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(UI.Icon(icon, 14, Farbe.B("#CCFFFFFF"), 1.6));
            sp.Children.Add(new TextBlock { Text = text, FontSize = 12.5, Foreground = Farbe.Text, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            return sp;
        }

        void ModulWahl(FrameworkElement ziel, BlattDef b)
        {
            var pop = new Popup { PlacementTarget = ziel, Placement = PlacementMode.Top, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade, VerticalOffset = -6 };
            var wp = new WrapPanel { Width = 4 * 84 };
            foreach (var a in ModulArten)
            {
                var a2 = a;
                var k = new Button { Style = (Style)Application.Current.FindResource("Leer"), Width = 80, Height = 70, Margin = new Thickness(2), Cursor = Cursors.Hand };
                var st = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var ic = UI.Icon(a[1], 20, Farbe.B("#D9FFFFFF"), 1.5); ic.HorizontalAlignment = HorizontalAlignment.Center;
                st.Children.Add(ic);
                st.Children.Add(new TextBlock { Text = a[2], FontSize = 11.5, Foreground = Farbe.Text2, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 7, 0, 0) });
                k.Content = st;
                k.MouseEnter += delegate { k.Background = Farbe.B("#14FFFFFF"); };
                k.MouseLeave += delegate { k.Background = Brushes.Transparent; };
                k.Click += delegate { pop.IsOpen = false; ModulNeu(b, a2[0]); };
                wp.Children.Add(k);
            }
            pop.Child = new Border { Background = Farbe.B("#F5191C23"), BorderBrush = Farbe.B("#1FFFFFFF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(6), Margin = new Thickness(6), Child = wp };
            pop.IsOpen = true;
        }

        /// <summary>Vorlagen als kleine Skizzen. Beim ersten Mal unten auf Home, später im Bearbeiten-Modus.</summary>
        FrameworkElement VorlagenReihe(BlattDef b, Action danach)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var v in Vorlagen)
            {
                var v2 = v;
                var probe = new BlattDef(); Vorlage(probe, v[0]);
                var skizze = new Canvas { Width = 66, Height = 40 };
                double zs = 66 / 6.0;
                foreach (var m in probe.Module)
                {
                    var r = new Border { Width = m.W * zs - 2, Height = Math.Min(40, m.H * zs * 1.15) - 2, CornerRadius = new CornerRadius(3), Background = Farbe.B("#33FFFFFF") };
                    Canvas.SetLeft(r, m.X * zs + 1); Canvas.SetTop(r, m.Y * zs * 1.15 + 1);
                    skizze.Children.Add(r);
                }
                if (probe.Module.Count == 0) skizze.Children.Add(new Border { Width = 64, Height = 38, CornerRadius = new CornerRadius(4), BorderBrush = Farbe.B("#33FFFFFF"), BorderThickness = new Thickness(1) });
                var k = new Button { Style = (Style)Application.Current.FindResource("Leer"), Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(2), ToolTip = v[1], Cursor = Cursors.Hand };
                var st = new StackPanel();
                st.Children.Add(skizze);
                st.Children.Add(new TextBlock { Text = v[1], FontSize = 10.5, Foreground = Farbe.Text3, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) });
                k.Content = st;
                k.MouseEnter += delegate { k.Background = Farbe.B("#14FFFFFF"); };
                k.MouseLeave += delegate { k.Background = Brushes.Transparent; };
                k.Click += delegate { Vorlage(b, v2[0]); Blaetter.Speichern(); if (danach != null) danach(); Neuzeichnen(); };
                sp.Children.Add(k);
            }
            return sp;
        }

        void VorlagenWahl(FrameworkElement ziel, BlattDef b, bool bearbeiten)
        {
            var pop = new Popup { PlacementTarget = ziel, Placement = PlacementMode.Top, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade, VerticalOffset = -6 };
            pop.Child = new Border { Background = Farbe.B("#F5191C23"), BorderBrush = Farbe.B("#1FFFFFFF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(6), Margin = new Thickness(6), Child = VorlagenReihe(b, () => pop.IsOpen = false) };
            pop.IsOpen = true;
        }

        /// <summary>Erster Besuch: leise Vorlagen unten, dazu ✓ = so behalten. Verschwindet nach der Wahl.</summary>
        FrameworkElement VorschlagLeiste(BlattDef b)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(VorlagenReihe(b, null));
            var ok = new Button { Style = (Style)Application.Current.FindResource("Rund"), Width = 40, Height = 40, Content = UI.Icon(Ikon.Haken, 16, Farbe.Akzent, 2), ToolTip = "Keep this", Margin = new Thickness(6, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center };
            ok.Click += delegate { b.Vorschlag = false; Blaetter.Speichern(); Neuzeichnen(); };
            sp.Children.Add(ok);
            var p = Pille(sp);
            p.CornerRadius = new CornerRadius(18);
            p.HorizontalAlignment = HorizontalAlignment.Center; p.VerticalAlignment = VerticalAlignment.Bottom; p.Margin = new Thickness(0, 0, 0, 4);
            p.Opacity = 0.9;
            return p;
        }

        // ================= Inhalt der Bausteine =================

        static readonly double[] SymbolPx = { 26, 38, 56 };

        FrameworkElement ModulInhalt(BlattDef b, Modul m, Border karte)
        {
            if (m.Art == "note")
            {
                var tb = new TextBox { Text = m.Text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Farbe.Text, CaretBrush = Farbe.Akzent, FontSize = 13.5, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Padding = new Thickness(2, 0, 2, 0) };
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
                t.Tick += delegate { t.Stop(); m.Text = tb.Text; Blaetter.Speichern(); };
                tb.TextChanged += delegate { t.Stop(); t.Start(); };
                tb.LostKeyboardFocus += delegate { if (m.Text != tb.Text) { m.Text = tb.Text; Blaetter.Speichern(); } };
                return tb;
            }
            double s = SymbolPx[Math.Max(0, Math.Min(2, m.Groesse))];
            double breite = Px(m.W) - 20, hoehe = Px(m.H) - 36;
            int proZeile = m.Liste ? 1 : Math.Max(1, (int)(breite / (s + 26)));
            int zeilen = Math.Max(1, (int)(hoehe / (m.Liste ? 30 : s + 30)));
            int max = Math.Max(1, proZeile * zeilen);
            if (m.Art == "screens") { max = Math.Max(1, (int)(breite / 116)) * Math.Max(1, (int)(hoehe / 86)); }

            List<string> pfade;
            if (m.Art == "recent") pfade = Zuletzt.Liste(E).Where(p => PasstFilter(p, m.Filter)).Take(max).ToList();
            else if (m.Art == "shelf") pfade = Blaetter.D.Ablage.Where(p => File.Exists(p) || Directory.Exists(p)).Take(max).ToList();
            else if (m.Art == "pinned") pfade = m.Inhalt.Where(p => (File.Exists(p) || Directory.Exists(p)) && PasstFilter(p, m.Filter)).Take(max).ToList();
            else if (m.Art == "expiring") pfade = E.S.D.Eintraege.Where(x => x.Ablauf.HasValue && (File.Exists(x.Pfad) || Directory.Exists(x.Pfad))).OrderBy(x => x.Ablauf.Value).Take(max).Select(x => x.Pfad).ToList();
            else if (m.Art == "downloads") pfade = Neueste(Paths.Downloads, 200, false).Where(p => PasstFilter(p, m.Filter)).Take(max).ToList();
            else if (m.Art == "screens") pfade = Neueste(Blaetter.Screenshots, max, true);
            else pfade = Neueste(m.Pfad, 300, false, true).Where(p => Directory.Exists(p) || PasstFilter(p, m.Filter)).Take(max).ToList();

            if (m.Art == "shelf" || m.Art == "pinned") AblageAufKarte(karte, b, m);
            if (m.Art == "recent") recentFuellen = () => { if (karte.IsLoaded && !homeBearbeiten) Neuzeichnen(); };
            if (pfade.Count == 0)
            {
                var leer = UI.Icon(ModulZeichen(m.Art), 26, Farbe.B("#1FFFFFFF"), 1.3);
                leer.HorizontalAlignment = HorizontalAlignment.Center; leer.VerticalAlignment = VerticalAlignment.Center; leer.Margin = new Thickness(0, 0, 0, 16);
                if (m.Art == "shelf" || m.Art == "pinned") ((FrameworkElement)leer).ToolTip = "Drop files here";
                var g = new Grid(); g.Children.Add(leer);
                return g;
            }
            if (m.Art == "screens")
            {
                var wp2 = new WrapPanel { Orientation = Orientation.Horizontal };
                foreach (var p in pfade) wp2.Children.Add(Vorschaukachel(p));
                return wp2;
            }
            var wp = new WrapPanel { Orientation = m.Liste ? Orientation.Vertical : Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
            foreach (var p in pfade) wp.Children.Add(m.Liste ? Zeile(p, m) : Stueck(p, m, s));
            return wp;
        }

        /// <summary>Ein Eintrag als Kachel: echtes Vorschaubild oder Typ-Marke, kleiner Name darunter (nur bei M/L).</summary>
        FrameworkElement Stueck(string p, Modul m, double s)
        {
            var e = m.Art == "expiring" ? E.S.Finde(p) : null;
            var st = new StackPanel { Width = s + 22 };
            var bildEl = e != null && e.Ablauf.HasValue ? Leerend(p, s, Rest(e)) : Vorschau.Echt(p, s);
            bildEl.HorizontalAlignment = HorizontalAlignment.Center;
            st.Children.Add(bildEl);
            if (m.Groesse > 0)
                st.Children.Add(new TextBlock { Text = System.IO.Path.GetFileNameWithoutExtension(p), FontSize = 10.5, Foreground = Farbe.B("#99FFFFFF"), TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 4, 0, 0) });
            return EintragHuelle(st, p, m, e, new Thickness(2, 2, 2, 4));
        }

        /// <summary>Ein Eintrag als Zeile: kleines Bild, Name, rechts leise wann (oder Restzeit).</summary>
        FrameworkElement Zeile(string p, Modul m)
        {
            var e = m.Art == "expiring" ? E.S.Finde(p) : null;
            var g = new Grid { Height = 28 };
            var bildEl = e != null && e.Ablauf.HasValue ? Leerend(p, 20, Rest(e)) : Vorschau.Echt(p, 20);
            bildEl.HorizontalAlignment = HorizontalAlignment.Left; bildEl.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(bildEl);
            string wann = "";
            try { wann = e != null && e.Ablauf.HasValue ? Kurz(e.Ablauf.Value, DateTime.UtcNow) : Alter(File.Exists(p) ? File.GetLastWriteTimeUtc(p) : Directory.GetLastWriteTimeUtc(p)); } catch { }
            g.Children.Add(new TextBlock { Text = System.IO.Path.GetFileName(p), FontSize = 12, Foreground = Farbe.B("#D9FFFFFF"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(28, 0, 44, 0) });
            g.Children.Add(new TextBlock { Text = wann, FontSize = 11, Foreground = Farbe.B("#66FFFFFF"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center });
            var h = EintragHuelle(g, p, m, e, new Thickness(0, 0, 0, 1));
            h.Width = Px(m.W) - 24;
            return h;
        }

        /// <summary>Restzeit am Bild selbst: unten das Bild in Farbe bis zur Füllhöhe, darüber nur ein Schatten davon.</summary>
        static FrameworkElement Leerend(string p, double s, double rest)
        {
            var g = new Grid { Width = s, Height = s };
            var grau = Vorschau.Echt(p, s); grau.Opacity = 0.28;
            var farbig = Vorschau.Echt(p, s);
            double a = Math.Max(0.06, Math.Min(1, rest));
            farbig.Clip = new RectangleGeometry(new Rect(0, s * (1 - a), s, s * a));
            g.Children.Add(grau); g.Children.Add(farbig);
            return g;
        }

        static double Rest(Eintrag e) { return (e.Ablauf.Value - DateTime.UtcNow).TotalSeconds / Math.Max(1, (e.Ablauf.Value - e.Hinzu).TotalSeconds); }

        static string Alter(DateTime utc)
        {
            var d = DateTime.UtcNow - utc;
            if (d.TotalMinutes < 60) return Math.Max(1, (int)d.TotalMinutes) + "m";
            if (d.TotalHours < 24) return (int)d.TotalHours + "h";
            if (d.TotalDays < 14) return (int)d.TotalDays + "d";
            return utc.ToLocalTime().ToString("d MMM", Dauer.En);
        }

        /// <summary>Klick öffnet, Ziehen nimmt mit, Rechtsklick: Öffnen, Zeigen – bei Expiring zusätzlich Behalten / +1 Woche.</summary>
        Border EintragHuelle(FrameworkElement inhalt, string p, Modul m, Eintrag e, Thickness rand)
        {
            var k = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(4, 3, 4, 3), Margin = rand, Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = System.IO.Path.GetFileName(p) + (e != null && e.Ablauf.HasValue ? "\n" + Kurz(e.Ablauf.Value, DateTime.UtcNow) + " left" : ""), Child = inhalt };
            ToolTipService.SetInitialShowDelay(k, 400);
            k.MouseEnter += delegate { k.Background = Farbe.B("#14FFFFFF"); };
            k.MouseLeave += delegate { k.Background = Brushes.Transparent; };
            Point start = new Point(); bool druck = false;
            k.MouseLeftButtonDown += (s, a) => { a.Handled = true; start = a.GetPosition(this); druck = true; };
            k.MouseMove += (s, a) =>
            {
                if (!druck || a.LeftButton != MouseButtonState.Pressed) return;
                var q = a.GetPosition(this);
                if (Math.Abs(q.X - start.X) < 6 && Math.Abs(q.Y - start.Y) < 6) return;
                druck = false;
                Ziehen(new[] { p });
            };
            k.MouseLeftButtonUp += (s, a) => { if (druck) { druck = false; Oeffnen(p); } };
            k.MouseRightButtonUp += (s, a) =>
            {
                a.Handled = true;
                var menue = new List<KeyValuePair<string, Action>>
                {
                    new KeyValuePair<string, Action>("Open", () => Oeffnen(p)),
                    new KeyValuePair<string, Action>("Show in Explorer", () => ImOrdnerZeigen(p)),
                };
                if (e != null)
                {
                    menue.Add(new KeyValuePair<string, Action>("-", null));
                    menue.Add(new KeyValuePair<string, Action>("+1 week", () => Verlaengern(e, "1 week")));
                    menue.Add(new KeyValuePair<string, Action>("Keep permanently", () => { E.Verschieben(e, Paths.Dauerhaft); Neuzeichnen(); }));
                }
                if (m.Art == "shelf") { menue.Add(new KeyValuePair<string, Action>("-", null)); menue.Add(new KeyValuePair<string, Action>("Remove", () => { Blaetter.D.Ablage.RemoveAll(x => Paths.Gleich(x, p)); Blaetter.Speichern(); Neuzeichnen(); })); }
                if (m.Art == "pinned") { menue.Add(new KeyValuePair<string, Action>("-", null)); menue.Add(new KeyValuePair<string, Action>("Unpin", () => { m.Inhalt.RemoveAll(x => Paths.Gleich(x, p)); Blaetter.Speichern(); Neuzeichnen(); })); }
                UI.Menue(k, menue, true);
            };
            return k;
        }

        void AblageAufKarte(Border karte, BlattDef b, Modul m)
        {
            karte.AllowDrop = true;
            karte.DragOver += (s, a) => { if (a.Data.GetDataPresent(DataFormats.FileDrop)) { a.Effects = DragDropEffects.Link; a.Handled = true; } };
            karte.DragEnter += delegate { karte.BorderBrush = Farbe.B("#40FFFFFF"); };
            karte.DragLeave += delegate { karte.BorderBrush = Brushes.Transparent; };
            karte.Drop += (s, a) =>
            {
                var pfade = a.Data.GetData(DataFormats.FileDrop) as string[];
                if (pfade == null) return;
                a.Handled = true;
                if (m.Art == "pinned") { foreach (var p in pfade) if (!m.Inhalt.Any(x => Paths.Gleich(x, p))) m.Inhalt.Add(p); Blaetter.Speichern(); }
                else Blaetter.Parken(pfade);
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

        /// <summary>Screenshots als echte kleine Vorschau.</summary>
        FrameworkElement Vorschaukachel(string p)
        {
            var bildEl = Vorschau.Bild(p, 104, 74);
            bildEl.HorizontalAlignment = HorizontalAlignment.Center; bildEl.VerticalAlignment = VerticalAlignment.Center;
            var h = EintragHuelle(bildEl, p, new Modul { Art = "screens" }, null, new Thickness(2));
            h.Padding = new Thickness(2);
            return h;
        }
    }
}
