using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Workbench
{
    public class Platz { public double X, Y; public string Farbe; }
    public class Linie { public string A, B, Farbe; }
    public class Anordnung
    {
        public Dictionary<string, Platz> Plaetze = new Dictionary<string, Platz>(StringComparer.OrdinalIgnoreCase);
        public List<Linie> Linien = new List<Linie>();
    }

    /// <summary>Wo Symbole liegen, welche Farbe sie haben, welche Linien sie verbinden – je Ordner, klein als JSON.</summary>
    static class Anordnungen
    {
        static Dictionary<string, Anordnung> alle;
        static DispatcherTimer speichern;
        static string Datei { get { return System.IO.Path.Combine(Paths.Daten, "anordnung.json"); } }

        public static Anordnung Fuer(string ordner)
        {
            if (alle == null)
            {
                alle = new Dictionary<string, Anordnung>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    if (File.Exists(Datei))
                    {
                        var d = Store.Json().Deserialize<Dictionary<string, Anordnung>>(File.ReadAllText(Datei));
                        if (d != null) foreach (var kv in d)
                        {
                            var a = kv.Value ?? new Anordnung();
                            var p = new Dictionary<string, Platz>(StringComparer.OrdinalIgnoreCase);
                            if (a.Plaetze != null) foreach (var x in a.Plaetze) p[x.Key] = x.Value;
                            a.Plaetze = p;
                            if (a.Linien == null) a.Linien = new List<Linie>();
                            alle[kv.Key] = a;
                        }
                    }
                }
                catch (Exception ex) { Log.Fehler("Anordnung laden", ex); }
            }
            string k = ordner.TrimEnd('\\');
            Anordnung r;
            if (!alle.TryGetValue(k, out r)) { r = new Anordnung(); alle[k] = r; }
            return r;
        }

        public static void Merken()
        {
            if (Vorschau.Sofort) return;
            if (speichern == null)
            {
                speichern = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
                speichern.Tick += delegate
                {
                    speichern.Stop();
                    try
                    {
                        var leer = alle.Where(kv => kv.Value.Plaetze.Count == 0 && kv.Value.Linien.Count == 0).Select(kv => kv.Key).ToList();
                        foreach (var k in leer) alle.Remove(k);
                        Directory.CreateDirectory(Paths.Daten);
                        File.WriteAllText(Datei, Store.Json().Serialize(alle));
                    }
                    catch (Exception ex) { Log.Fehler("Anordnung speichern", ex); }
                };
            }
            speichern.Stop();
            speichern.Start();
        }

        public static void Umbenennen(string ordner, string alt, string neu)
        {
            var a = Fuer(ordner);
            Platz p;
            if (a.Plaetze.TryGetValue(alt, out p)) { a.Plaetze.Remove(alt); a.Plaetze[neu] = p; }
            foreach (var l in a.Linien) { if (string.Equals(l.A, alt, StringComparison.OrdinalIgnoreCase)) l.A = neu; if (string.Equals(l.B, alt, StringComparison.OrdinalIgnoreCase)) l.B = neu; }
            Merken();
        }
    }

    /// <summary>
    /// Eine freie Fläche wie der Desktop: Symbole frei ziehen (bleiben liegen), mit Umschalt von Symbol zu Symbol eine Linie
    /// ziehen, über Rechtsklick färben. Aus dem Fenster gezogen nimmt ein Symbol die echte Datei mit.
    /// Bewusst schlicht: ein Canvas, keine Effekte – kostet kaum etwas.
    /// </summary>
    class Tisch : Grid
    {
        public const double ZB = 96, ZH = 108;
        static readonly string[] farben = { "#F4B860", "#F08A7E", "#7ED39B", "#8FB4FF", "#C3A6F5", "#B4BBC7" };

        readonly string ordner;
        readonly Anordnung A;
        readonly Canvas flaeche = new Canvas { Background = Brushes.Transparent, ClipToBounds = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        readonly Canvas linienEbene = new Canvas { IsHitTestVisible = true };
        readonly Dictionary<string, Border> symbole = new Dictionary<string, Border>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Border> gruende = new Dictionary<string, Border>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, string> pfade = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly ScrollViewer sv;

        readonly Action<string> waehlen, oeffnen;
        readonly Func<string, List<KeyValuePair<string, Action>>> menueFuer;
        Linie linieGewaehlt;
        Line zug;               // Linie, die gerade gezogen wird
        string zugVon;
        Window fenster;

        public Tisch(string ordner, List<string> eintraege, Func<string, Border> bauen, Action<string> waehlen, Action<string> oeffnen, Func<string, List<KeyValuePair<string, Action>>> menueFuer)
        {
            this.ordner = ordner;
            this.waehlen = waehlen;
            this.oeffnen = oeffnen;
            this.menueFuer = menueFuer;
            A = Anordnungen.Fuer(ordner);

            flaeche.Children.Add(linienEbene);
            foreach (var p in eintraege)
            {
                string name = System.IO.Path.GetFileName(p);
                var b = bauen(p);
                b.Width = ZB - 4;
                symbole[name] = b;
                pfade[name] = p;
                var grund = new Border { Width = ZB - 4, Height = ZH - 6, CornerRadius = new CornerRadius(12), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
                gruende[name] = grund;
                flaeche.Children.Add(grund);
                flaeche.Children.Add(b);
                Bedienen(b, name);
                FarbeZeigen(name);
            }
            sv = new ScrollViewer { Content = flaeche, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
            Children.Add(sv);

            SizeChanged += delegate { Verteilen(); };
            Loaded += delegate
            {
                fenster = Window.GetWindow(this);
                if (fenster != null) fenster.PreviewKeyDown += Taste;
            };
            Unloaded += delegate { if (fenster != null) fenster.PreviewKeyDown -= Taste; };
            flaeche.MouseLeftButtonDown += (s, a) => { if (a.OriginalSource == flaeche) { LinieWaehlen(null); waehlen(null); } };
            Verteilen();
        }

        /// <summary>Symbole ohne festen Platz laufen in Spalten von oben nach unten – wie auf dem Desktop. Feste bleiben, wo sie sind.</summary>
        void Verteilen()
        {
            double h = ActualHeight > 0 ? ActualHeight : 560;
            int zeilen = Math.Max(1, (int)((h - 8) / ZH));
            var belegt = new HashSet<long>();
            foreach (var kv in A.Plaetze) if (symbole.ContainsKey(kv.Key)) belegt.Add(Zelle(kv.Value.X, kv.Value.Y));
            int i = 0;
            foreach (var kv in symbole)
            {
                Platz p;
                double x, y;
                if (A.Plaetze.TryGetValue(kv.Key, out p)) { x = p.X; y = p.Y; }
                else
                {
                    long z;
                    do { x = 4 + (i / zeilen) * ZB; y = 4 + (i % zeilen) * ZH; z = Zelle(x, y); i++; } while (belegt.Contains(z));
                    belegt.Add(z);
                }
                Setzen(kv.Key, x, y);
            }
            Ausdehnen();
            LinienZeichnen();
        }

        static long Zelle(double x, double y) { return ((long)Math.Round(x / ZB) << 20) + (long)Math.Round(y / ZH); }

        void Setzen(string name, double x, double y)
        {
            Canvas.SetLeft(symbole[name], x); Canvas.SetTop(symbole[name], y);
            Canvas.SetLeft(gruende[name], x); Canvas.SetTop(gruende[name], y + 2);
        }

        void Ausdehnen()
        {
            double w = 0, h = 0;
            foreach (var b in symbole.Values) { w = Math.Max(w, Canvas.GetLeft(b) + ZB); h = Math.Max(h, Canvas.GetTop(b) + ZH); }
            // mindestens so groß wie das Blatt – damit überall Platz zum Ablegen und Anklicken ist
            flaeche.Width = Math.Max(w + 8, ActualWidth > 0 ? ActualWidth - 2 : 0);
            flaeche.Height = Math.Max(h + 8, ActualHeight > 0 ? ActualHeight - 2 : 0);
        }

        Point Mitte(string name)
        {
            var b = symbole[name];
            return new Point(Canvas.GetLeft(b) + (ZB - 4) / 2, Canvas.GetTop(b) + 30);
        }

        void LinienZeichnen()
        {
            linienEbene.Children.Clear();
            foreach (var l in A.Linien.ToList())
            {
                if (!symbole.ContainsKey(l.A) || !symbole.ContainsKey(l.B)) continue;
                var a = Mitte(l.A); var b = Mitte(l.B);
                bool gew = l == linieGewaehlt;
                var sichtbar = new Line { X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y, Stroke = Pinsel(l.Farbe, gew ? 1.0 : 0.6), StrokeThickness = gew ? 3 : 2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, IsHitTestVisible = false };
                if (gew) sichtbar.StrokeDashArray = null;
                var greif = new Line { X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y, Stroke = Brushes.Transparent, StrokeThickness = 12, Cursor = Cursors.Hand };
                var l2 = l;
                greif.MouseLeftButtonDown += (s, e) => { LinieWaehlen(l2); waehlen(null); e.Handled = true; };
                greif.MouseRightButtonUp += (s, e) => { LinieWaehlen(l2); LinienMenue(greif, l2); e.Handled = true; };
                linienEbene.Children.Add(sichtbar);
                linienEbene.Children.Add(greif);
            }
        }

        static Brush Pinsel(string hex, double deckkraft)
        {
            var c = (Color)ColorConverter.ConvertFromString(string.IsNullOrEmpty(hex) ? "#FFFFFF" : hex);
            c.A = (byte)(255 * deckkraft * (string.IsNullOrEmpty(hex) ? 0.55 : 1));
            var b = new SolidColorBrush(c); b.Freeze(); return b;
        }

        void FarbeZeigen(string name)
        {
            Platz p;
            var g = gruende[name];
            if (A.Plaetze.TryGetValue(name, out p) && !string.IsNullOrEmpty(p.Farbe))
            {
                var c = (Color)ColorConverter.ConvertFromString(p.Farbe);
                g.Background = new SolidColorBrush(Color.FromArgb(46, c.R, c.G, c.B));
                g.BorderBrush = new SolidColorBrush(Color.FromArgb(90, c.R, c.G, c.B));
                g.BorderThickness = new Thickness(1);
                g.Visibility = Visibility.Visible;
            }
            else g.Visibility = Visibility.Collapsed;
        }

        Platz PlatzVon(string name)
        {
            Platz p;
            if (!A.Plaetze.TryGetValue(name, out p))
            {
                var b = symbole[name];
                p = new Platz { X = Canvas.GetLeft(b), Y = Canvas.GetTop(b) };
                A.Plaetze[name] = p;
            }
            return p;
        }

        void LinieWaehlen(Linie l) { linieGewaehlt = l; LinienZeichnen(); }

        // ---------- Maus ----------

        void Bedienen(Border b, string name)
        {
            Point start = new Point(), anfang = new Point();
            bool gedrueckt = false, schiebt = false;
            b.MouseLeftButtonDown += (s, a) =>
            {
                a.Handled = true;
                LinieWaehlen(null);
                if (zug != null && zugVon != null) { Verbinden(zugVon, name); return; }   // „Verbinden“ aus dem Menü
                if (a.ClickCount == 2) { gedrueckt = false; oeffnen(pfade[name]); return; }
                waehlen(pfade[name]);
                start = a.GetPosition(flaeche);
                anfang = new Point(Canvas.GetLeft(b), Canvas.GetTop(b));
                gedrueckt = true;
                schiebt = false;
                if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) ZugBeginnen(name);
                b.CaptureMouse();
            };
            b.MouseMove += (s, a) =>
            {
                if (!gedrueckt) return;
                var p = a.GetPosition(flaeche);
                if (zug != null) { zug.X2 = p.X; zug.Y2 = p.Y; return; }
                if (!schiebt && Math.Abs(p.X - start.X) < 5 && Math.Abs(p.Y - start.Y) < 5) return;
                schiebt = true;
                // aus dem Fenster heraus? Dann die echte Datei mitnehmen (Mail, Word, Explorer …)
                var imTisch = a.GetPosition(this);
                if (imTisch.X < -4 || imTisch.Y < -4 || imTisch.X > ActualWidth + 4 || imTisch.Y > ActualHeight + 4)
                {
                    gedrueckt = false; schiebt = false;
                    b.ReleaseMouseCapture();
                    Setzen(name, anfang.X, anfang.Y);
                    LinienZeichnen();
                    string pf = pfade[name];
                    if (File.Exists(pf) || Directory.Exists(pf))
                        try { DragDrop.DoDragDrop(b, new DataObject(DataFormats.FileDrop, new[] { pf }), DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link); } catch { }
                    return;
                }
                double x = Math.Max(0, Math.Round((anfang.X + p.X - start.X) / 4) * 4);
                double y = Math.Max(0, Math.Round((anfang.Y + p.Y - start.Y) / 4) * 4);
                Setzen(name, x, y);
                Panel.SetZIndex(b, 10);
                LinienZeichnen();
            };
            b.MouseLeftButtonUp += (s, a) =>
            {
                if (!gedrueckt) return;
                gedrueckt = false;
                b.ReleaseMouseCapture();
                Panel.SetZIndex(b, 0);
                if (zug != null)
                {
                    var ziel = SymbolUnter(a.GetPosition(flaeche));
                    if (ziel != null && ziel != name) Verbinden(name, ziel); else ZugEnde();
                    return;
                }
                if (schiebt)
                {
                    var p = PlatzVon(name);
                    p.X = Canvas.GetLeft(b); p.Y = Canvas.GetTop(b);
                    Anordnungen.Merken();
                    Ausdehnen();
                }
            };
            b.MouseRightButtonUp += (s, a) => { a.Handled = true; waehlen(pfade[name]); Menue(b, name); };
        }

        string SymbolUnter(Point p)
        {
            foreach (var kv in symbole)
            {
                double x = Canvas.GetLeft(kv.Value), y = Canvas.GetTop(kv.Value);
                if (p.X >= x && p.X <= x + ZB && p.Y >= y && p.Y <= y + ZH) return kv.Key;
            }
            return null;
        }

        void ZugBeginnen(string von)
        {
            ZugEnde();
            zugVon = von;
            var m = Mitte(von);
            Platz p;
            string f = A.Plaetze.TryGetValue(von, out p) ? p.Farbe : null;
            zug = new Line { X1 = m.X, Y1 = m.Y, X2 = m.X, Y2 = m.Y, Stroke = Pinsel(f, 0.9), StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 3, 3 }, IsHitTestVisible = false, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            linienEbene.Children.Add(zug);
        }

        void ZugEnde()
        {
            if (zug != null) linienEbene.Children.Remove(zug);
            zug = null; zugVon = null;
            flaeche.MouseMove -= ZugFolgen;
            flaeche.MouseLeftButtonDown -= ZugAbbrechen;
        }

        void ZugFolgen(object s, MouseEventArgs a) { if (zug != null) { var p = a.GetPosition(flaeche); zug.X2 = p.X; zug.Y2 = p.Y; } }
        void ZugAbbrechen(object s, MouseButtonEventArgs a) { if (a.OriginalSource == flaeche) ZugEnde(); }

        void Verbinden(string a, string b)
        {
            ZugEnde();
            if (a == b || A.Linien.Any(l => (l.A == a && l.B == b) || (l.A == b && l.B == a))) return;
            Platz p;
            A.Linien.Add(new Linie { A = a, B = b, Farbe = A.Plaetze.TryGetValue(a, out p) ? p.Farbe : null });
            PlatzVon(a); PlatzVon(b);   // verbundene Symbole behalten ihren Platz
            Anordnungen.Merken();
            LinienZeichnen();
        }

        // ---------- Menüs ----------

        void Menue(Border b, string name)
        {
            var m = menueFuer(pfade[name]);
            m.Insert(0, new KeyValuePair<string, Action>("Verbinden …", () =>
            {
                ZugBeginnen(name);
                flaeche.MouseMove += ZugFolgen;
                flaeche.MouseLeftButtonDown += ZugAbbrechen;
            }));
            Platz p;
            string aktuell = A.Plaetze.TryGetValue(name, out p) ? p.Farbe : null;
            UI.Menue(b, m, false, false, Farbreihe(aktuell, f =>
            {
                PlatzVon(name).Farbe = f;
                Anordnungen.Merken();
                FarbeZeigen(name);
                LinienZeichnen();
            }));
        }

        void LinienMenue(FrameworkElement ziel, Linie l)
        {
            UI.Menue(ziel, new List<KeyValuePair<string, Action>>
            {
                new KeyValuePair<string, Action>("Linie löschen", () => { A.Linien.Remove(l); linieGewaehlt = null; Anordnungen.Merken(); LinienZeichnen(); })
            }, true, false, Farbreihe(l.Farbe, f => { l.Farbe = f; Anordnungen.Merken(); LinienZeichnen(); }));
        }

        /// <summary>Sechs Farbpunkte und „keine“ – oben im Menü, ganz ohne Text.</summary>
        static FrameworkElement Farbreihe(string aktuell, Action<string> setzen)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 6, 8, 6) };
            foreach (var f in new[] { (string)null }.Concat(farben))
            {
                string f2 = f;
                bool an = string.Equals(f, aktuell, StringComparison.OrdinalIgnoreCase) || (f == null && string.IsNullOrEmpty(aktuell));
                var g = new Grid { Width = 26, Height = 26, Cursor = Cursors.Hand, Background = Brushes.Transparent, ToolTip = f == null ? "Keine Farbe" : null };
                if (an) g.Children.Add(new Ellipse { Width = 24, Height = 24, Stroke = Farbe.B("#B3FFFFFF"), StrokeThickness = 1.5 });
                if (f == null)
                {
                    g.Children.Add(new Ellipse { Width = 16, Height = 16, Stroke = Farbe.B("#80FFFFFF"), StrokeThickness = 1.5 });
                    g.Children.Add(new Line { X1 = 8, Y1 = 18, X2 = 18, Y2 = 8, Stroke = Farbe.B("#80FFFFFF"), StrokeThickness = 1.5 });
                }
                else g.Children.Add(new Ellipse { Width = 16, Height = 16, Fill = Farbe.B(f) });
                g.MouseLeftButtonUp += (s, a) =>
                {
                    setzen(f2);
                    var pop = FindePopup(g);
                    if (pop != null) pop.IsOpen = false;
                };
                sp.Children.Add(g);
            }
            return sp;
        }

        static Popup FindePopup(DependencyObject d)
        {
            while (d != null)
            {
                var p = LogicalTreeHelper.GetParent(d) ?? VisualTreeHelper.GetParent(d);
                var pop = p as Popup;
                if (pop != null) return pop;
                d = p;
            }
            return null;
        }

        void Taste(object s, KeyEventArgs a)
        {
            if (a.Key == Key.Escape && zug != null) { ZugEnde(); a.Handled = true; }
            else if (a.Key == Key.Delete && linieGewaehlt != null && !(Keyboard.FocusedElement is TextBox))
            {
                A.Linien.Remove(linieGewaehlt);
                linieGewaehlt = null;
                Anordnungen.Merken();
                LinienZeichnen();
                a.Handled = true;
            }
        }

        /// <summary>„Ordnen“: feste Plätze dieses Ordners vergessen – alles läuft wieder in Spalten.</summary>
        public void Aufraeumen()
        {
            var farbig = A.Plaetze.Where(kv => !string.IsNullOrEmpty(kv.Value.Farbe)).ToDictionary(kv => kv.Key, kv => kv.Value.Farbe);
            A.Plaetze.Clear();
            Verteilen();
            foreach (var kv in farbig) { PlatzVon(kv.Key).Farbe = kv.Value; FarbeZeigen(kv.Key); }
            foreach (var n in symbole.Keys.ToList()) if (A.Linien.Any(l => l.A == n || l.B == n)) PlatzVon(n);
            Anordnungen.Merken();
        }
    }
}
