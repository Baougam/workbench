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
    public class Notiz { public string Id, Text, Farbe; public double X, Y; }
    public class Anordnung
    {
        public Dictionary<string, Platz> Plaetze = new Dictionary<string, Platz>(StringComparer.OrdinalIgnoreCase);
        public List<Linie> Linien = new List<Linie>();
        public List<Notiz> Notizen = new List<Notiz>();
    }

    /// <summary>Wo Symbole liegen, welche Farbe sie haben, welche Linien und Notizen es gibt – je Ordner, klein als JSON.</summary>
    static class Anordnungen
    {
        static Dictionary<string, Anordnung> alle;
        static DispatcherTimer speichern;
        static string Datei { get { return System.IO.Path.Combine(Paths.Daten, "anordnung.json"); } }

        public const string NotizPraefix = "§";

        static void Laden()
        {
            if (alle != null) return;
            alle = new Dictionary<string, Anordnung>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(Datei)) return;
                var d = Store.Json().Deserialize<Dictionary<string, Anordnung>>(File.ReadAllText(Datei));
                if (d == null) return;
                foreach (var kv in d)
                {
                    var a = kv.Value ?? new Anordnung();
                    var p = new Dictionary<string, Platz>(StringComparer.OrdinalIgnoreCase);
                    if (a.Plaetze != null) foreach (var x in a.Plaetze) p[x.Key] = x.Value;
                    a.Plaetze = p;
                    if (a.Linien == null) a.Linien = new List<Linie>();
                    if (a.Notizen == null) a.Notizen = new List<Notiz>();
                    alle[kv.Key] = a;
                }
            }
            catch (Exception ex) { Log.Fehler("Anordnung laden", ex); }
        }

        public static Anordnung Fuer(string ordner)
        {
            Laden();
            string k = ordner.TrimEnd('\\');
            Anordnung r;
            if (!alle.TryGetValue(k, out r)) { r = new Anordnung(); alle[k] = r; }
            return r;
        }

        /// <summary>Für die Netz-Ansicht: alle Ordner mit Anordnung.</summary>
        public static IEnumerable<KeyValuePair<string, Anordnung>> Alle { get { Laden(); return alle.ToList(); } }

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
                        var leer = alle.Where(kv => kv.Value.Plaetze.Count == 0 && kv.Value.Linien.Count == 0 && kv.Value.Notizen.Count == 0).Select(kv => kv.Key).ToList();
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
            // ein umbenannter Ordner nimmt seine eigene Anordnung mit
            string altPfad = System.IO.Path.Combine(ordner, alt).TrimEnd('\\'), neuPfad = System.IO.Path.Combine(ordner, neu).TrimEnd('\\');
            Anordnung u;
            if (alle.TryGetValue(altPfad, out u)) { alle.Remove(altPfad); alle[neuPfad] = u; }
            Merken();
        }
    }

    /// <summary>
    /// Eine freie Fläche wie der Desktop: Symbole und Notizen frei ziehen (bleiben liegen), mit Umschalt von einem zum anderen eine
    /// Linie ziehen, über Rechtsklick färben. Doppelklick auf eine leere Stelle = neue Notiz. Aus dem Fenster gezogen nimmt ein
    /// Symbol die echte Datei mit. Bewusst schlicht: ein Canvas, keine Effekte – kostet kaum etwas.
    /// </summary>
    class Tisch : Grid
    {
        public const double ZB = 96, ZH = 108;
        public static readonly string[] Farben = { "#F4B860", "#F08A7E", "#7ED39B", "#8FB4FF", "#C3A6F5", "#B4BBC7" };

        readonly Anordnung A;
        readonly Canvas flaeche = new Canvas { Background = Brushes.Transparent, ClipToBounds = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        readonly Canvas linienEbene = new Canvas();
        // alle beweglichen Dinge – Symbole (Schlüssel = Dateiname) und Notizen (Schlüssel = § + Id)
        readonly Dictionary<string, FrameworkElement> dinge = new Dictionary<string, FrameworkElement>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Border> gruende = new Dictionary<string, Border>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, string> pfade = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly ScrollViewer sv;

        readonly Action<string> waehlen, oeffnen;
        readonly Func<string, List<KeyValuePair<string, Action>>> menueFuer;
        Linie linieGewaehlt;
        Line zug;
        string zugVon;
        Window fenster;

        public Tisch(string ordner, List<string> eintraege, Func<string, Border> bauen, Action<string> waehlen, Action<string> oeffnen, Func<string, List<KeyValuePair<string, Action>>> menueFuer)
        {
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
                pfade[name] = p;
                var grund = new Border { Width = ZB - 4, Height = ZH - 6, CornerRadius = new CornerRadius(12), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
                gruende[name] = grund;
                flaeche.Children.Add(grund);
                Hinzu(name, b);
                FarbeZeigen(name);
            }
            foreach (var n in A.Notizen.ToList()) NotizBauen(n, false);

            sv = new ScrollViewer { Content = flaeche, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
            Children.Add(sv);

            SizeChanged += delegate { Verteilen(false); };
            Loaded += delegate { fenster = Window.GetWindow(this); if (fenster != null) fenster.PreviewKeyDown += Taste; };
            Unloaded += delegate { if (fenster != null) fenster.PreviewKeyDown -= Taste; };
            flaeche.MouseLeftButtonDown += (s, a) =>
            {
                if (a.OriginalSource != flaeche) return;
                LinieWaehlen(null);
                waehlen(null);
                if (a.ClickCount == 2) { NeueNotiz(a.GetPosition(flaeche)); a.Handled = true; }
            };
            // Erst verteilen, wenn die Größe bekannt ist (passiert vor dem ersten Bild) – sonst springen Symbole einmal um.
            if (Vorschau.Sofort) Verteilen(true);
        }

        int letzteZeilen = -1;

        void Hinzu(string schluessel, FrameworkElement el)
        {
            dinge[schluessel] = el;
            flaeche.Children.Add(el);
            Bedienen(el, schluessel);
        }

        static bool IstNotiz(string k) { return k.StartsWith(Anordnungen.NotizPraefix); }

        /// <summary>Symbole ohne festen Platz laufen in Spalten von oben nach unten – wie auf dem Desktop. Feste bleiben, wo sie sind.</summary>
        void Verteilen() { Verteilen(true); }

        void Verteilen(bool immer)
        {
            double h = ActualHeight > 0 ? ActualHeight : 560;
            int zeilen = Math.Max(1, (int)((h - 8) / ZH));
            if (!immer && zeilen == letzteZeilen) { Ausdehnen(); return; }   // nur Größe nachziehen, nichts verschieben
            letzteZeilen = zeilen;
            var belegt = new HashSet<long>();
            foreach (var kv in A.Plaetze) if (dinge.ContainsKey(kv.Key)) belegt.Add(Zelle(kv.Value.X, kv.Value.Y));
            foreach (var n in A.Notizen) belegt.Add(Zelle(n.X, n.Y));
            int i = 0;
            foreach (var kv in dinge)
            {
                if (IstNotiz(kv.Key)) continue;
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

        void Setzen(string k, double x, double y)
        {
            Canvas.SetLeft(dinge[k], x); Canvas.SetTop(dinge[k], y);
            Border g;
            if (gruende.TryGetValue(k, out g)) { Canvas.SetLeft(g, x); Canvas.SetTop(g, y + 2); }
        }

        void Ausdehnen()
        {
            double w = 0, h = 0;
            foreach (var el in dinge.Values)
            {
                double bw = el.ActualWidth > 0 ? el.ActualWidth : ZB, bh = el.ActualHeight > 0 ? el.ActualHeight : ZH;
                w = Math.Max(w, Canvas.GetLeft(el) + bw); h = Math.Max(h, Canvas.GetTop(el) + bh);
            }
            flaeche.Width = Math.Max(w + 8, ActualWidth > 0 ? ActualWidth - 2 : 0);
            flaeche.Height = Math.Max(h + 8, ActualHeight > 0 ? ActualHeight - 2 : 0);
        }

        Point Mitte(string k)
        {
            var el = dinge[k];
            double x = Canvas.GetLeft(el), y = Canvas.GetTop(el);
            if (IstNotiz(k)) return new Point(x + (el.ActualWidth > 0 ? el.ActualWidth : 160) / 2, y + (el.ActualHeight > 0 ? el.ActualHeight : 40) / 2);
            return new Point(x + (ZB - 4) / 2, y + 30);
        }

        void LinienZeichnen()
        {
            linienEbene.Children.Clear();
            foreach (var l in A.Linien.ToList())
            {
                if (!dinge.ContainsKey(l.A) || !dinge.ContainsKey(l.B)) continue;
                var a = Mitte(l.A); var b = Mitte(l.B);
                bool gew = l == linieGewaehlt;
                linienEbene.Children.Add(new Line { X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y, Stroke = Pinsel(l.Farbe, gew ? 1.0 : 0.6), StrokeThickness = gew ? 3 : 2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, IsHitTestVisible = false });
                var greif = new Line { X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y, Stroke = Brushes.Transparent, StrokeThickness = 12, Cursor = Cursors.Hand };
                var l2 = l;
                greif.MouseLeftButtonDown += (s, e) => { LinieWaehlen(l2); waehlen(null); e.Handled = true; };
                greif.MouseRightButtonUp += (s, e) => { LinieWaehlen(l2); LinienMenue(greif, l2); e.Handled = true; };
                linienEbene.Children.Add(greif);
            }
        }

        public static Brush Pinsel(string hex, double deckkraft)
        {
            var c = (Color)ColorConverter.ConvertFromString(string.IsNullOrEmpty(hex) ? "#FFFFFF" : hex);
            c.A = (byte)(255 * deckkraft * (string.IsNullOrEmpty(hex) ? 0.55 : 1));
            var b = new SolidColorBrush(c); b.Freeze(); return b;
        }

        static Brush Tint(string hex, byte alpha)
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            var b = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B)); b.Freeze(); return b;
        }

        void FarbeZeigen(string k)
        {
            Border g;
            if (!gruende.TryGetValue(k, out g)) return;
            Platz p;
            if (A.Plaetze.TryGetValue(k, out p) && !string.IsNullOrEmpty(p.Farbe))
            {
                g.Background = Tint(p.Farbe, 46);
                g.BorderBrush = Tint(p.Farbe, 90);
                g.BorderThickness = new Thickness(1);
                g.Visibility = Visibility.Visible;
            }
            else g.Visibility = Visibility.Collapsed;
        }

        Platz PlatzVon(string k)
        {
            Platz p;
            if (!A.Plaetze.TryGetValue(k, out p))
            {
                var el = dinge[k];
                p = new Platz { X = Canvas.GetLeft(el), Y = Canvas.GetTop(el) };
                A.Plaetze[k] = p;
            }
            return p;
        }

        void LinieWaehlen(Linie l) { linieGewaehlt = l; LinienZeichnen(); }

        // ---------- Notizen ----------

        Notiz NotizVon(string k) { string id = k.Substring(1); return A.Notizen.FirstOrDefault(n => n.Id == id); }

        void NeueNotiz(Point p)
        {
            var n = new Notiz { Id = Guid.NewGuid().ToString("N").Substring(0, 10), X = Math.Max(0, p.X - 20), Y = Math.Max(0, p.Y - 14), Text = "" };
            A.Notizen.Add(n);
            var el = NotizBauen(n, true);
            Ausdehnen();
        }

        /// <summary>Eine Notiz: ruhige Karte, nur Text. Doppelklick = schreiben, leer = weg.</summary>
        FrameworkElement NotizBauen(Notiz n, bool sofortSchreiben)
        {
            string k = Anordnungen.NotizPraefix + n.Id;
            var b = new Border { MinWidth = 120, MaxWidth = 220, Padding = new Thickness(11, 8, 11, 9), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Cursor = Cursors.Hand };
            NotizFarbe(b, n);
            var t = new TextBlock { Text = n.Text, FontSize = 12.5, Foreground = Farbe.B("#EBFFFFFF"), TextWrapping = TextWrapping.Wrap, LineHeight = 17, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
            b.Child = t;
            b.Tag = t;
            Canvas.SetLeft(b, n.X); Canvas.SetTop(b, n.Y);
            Panel.SetZIndex(b, 2);
            Hinzu(k, b);
            b.SizeChanged += delegate { LinienZeichnen(); };
            if (sofortSchreiben) Dispatcher.BeginInvoke(new Action(() => Schreiben(b, n)), DispatcherPriority.Loaded);
            return b;
        }

        static void NotizFarbe(Border b, Notiz n)
        {
            // deckend, damit Linien nicht durch den Text laufen – Farbe nur als leichter Hauch auf dunklem Grund
            var dunkel = Color.FromRgb(0x26, 0x29, 0x30);
            if (string.IsNullOrEmpty(n.Farbe)) { b.Background = new SolidColorBrush(dunkel); b.BorderBrush = Farbe.B("#33FFFFFF"); }
            else
            {
                var c = (Color)ColorConverter.ConvertFromString(n.Farbe);
                Func<byte, byte, byte> mix = (x, y) => (byte)(x * 0.72 + y * 0.28);
                b.Background = new SolidColorBrush(Color.FromRgb(mix(dunkel.R, c.R), mix(dunkel.G, c.G), mix(dunkel.B, c.B)));
                b.BorderBrush = Tint(n.Farbe, 140);
            }
        }

        void Schreiben(Border b, Notiz n)
        {
            var tb = new TextBox
            {
                Text = n.Text, FontSize = 12.5, Foreground = Farbe.Text, CaretBrush = Farbe.Text, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinWidth = 98, Padding = new Thickness(0), SelectionBrush = Farbe.Akzent
            };
            b.Child = tb;
            bool fertig = false;
            Action ende = () =>
            {
                if (fertig) return;
                fertig = true;
                n.Text = tb.Text.TrimEnd();
                if (n.Text.Length == 0) { NotizWeg(n); return; }
                var t = (TextBlock)b.Tag;
                t.Text = n.Text;
                b.Child = t;
                Anordnungen.Merken();
            };
            tb.LostKeyboardFocus += delegate { ende(); };
            tb.PreviewKeyDown += (s, a) =>
            {
                if (a.Key == Key.Escape || (a.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0)) { ende(); Keyboard.ClearFocus(); a.Handled = true; }
            };
            tb.Focus();
            tb.CaretIndex = tb.Text.Length;
            Keyboard.Focus(tb);
        }

        void NotizWeg(Notiz n)
        {
            string k = Anordnungen.NotizPraefix + n.Id;
            A.Notizen.Remove(n);
            A.Linien.RemoveAll(l => l.A == k || l.B == k);
            FrameworkElement el;
            if (dinge.TryGetValue(k, out el)) { flaeche.Children.Remove(el); dinge.Remove(k); }
            Anordnungen.Merken();
            LinienZeichnen();
        }

        // ---------- Maus ----------

        void Bedienen(FrameworkElement el, string k)
        {
            Point start = new Point(), anfang = new Point();
            bool gedrueckt = false, schiebt = false;
            bool notiz = IstNotiz(k);
            el.MouseLeftButtonDown += (s, a) =>
            {
                if (notiz && ((Border)el).Child is TextBox) return;      // gerade am Schreiben
                a.Handled = true;
                LinieWaehlen(null);
                if (zug != null && zugVon != null) { Verbinden(zugVon, k); return; }
                if (a.ClickCount == 2)
                {
                    gedrueckt = false;
                    if (notiz) Schreiben((Border)el, NotizVon(k)); else oeffnen(pfade[k]);
                    return;
                }
                waehlen(notiz ? null : pfade[k]);
                start = a.GetPosition(flaeche);
                anfang = new Point(Canvas.GetLeft(el), Canvas.GetTop(el));
                gedrueckt = true;
                schiebt = false;
                if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) ZugBeginnen(k);
                el.CaptureMouse();
            };
            el.MouseMove += (s, a) =>
            {
                if (!gedrueckt) return;
                var p = a.GetPosition(flaeche);
                if (zug != null) { zug.X2 = p.X; zug.Y2 = p.Y; return; }
                if (!schiebt && Math.Abs(p.X - start.X) < 5 && Math.Abs(p.Y - start.Y) < 5) return;
                schiebt = true;
                var imTisch = a.GetPosition(this);
                if (!notiz && (imTisch.X < -4 || imTisch.Y < -4 || imTisch.X > ActualWidth + 4 || imTisch.Y > ActualHeight + 4))
                {
                    // aus dem Fenster heraus: echte Datei mitnehmen (Mail, Word, Explorer …)
                    gedrueckt = false; schiebt = false;
                    el.ReleaseMouseCapture();
                    Setzen(k, anfang.X, anfang.Y);
                    LinienZeichnen();
                    string pf = pfade[k];
                    if (File.Exists(pf) || Directory.Exists(pf))
                        try { DragDrop.DoDragDrop(el, new DataObject(DataFormats.FileDrop, new[] { pf }), DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link); } catch { }
                    return;
                }
                Setzen(k, Math.Max(0, Math.Round((anfang.X + p.X - start.X) / 4) * 4), Math.Max(0, Math.Round((anfang.Y + p.Y - start.Y) / 4) * 4));
                Panel.SetZIndex(el, 10);
                LinienZeichnen();
            };
            el.MouseLeftButtonUp += (s, a) =>
            {
                if (!gedrueckt) return;
                gedrueckt = false;
                el.ReleaseMouseCapture();
                Panel.SetZIndex(el, notiz ? 2 : 0);
                if (zug != null)
                {
                    var ziel = DingUnter(a.GetPosition(flaeche));
                    if (ziel != null && ziel != k) Verbinden(k, ziel); else ZugEnde();
                    return;
                }
                if (schiebt)
                {
                    if (notiz) { var n = NotizVon(k); n.X = Canvas.GetLeft(el); n.Y = Canvas.GetTop(el); }
                    else { var p = PlatzVon(k); p.X = Canvas.GetLeft(el); p.Y = Canvas.GetTop(el); }
                    Anordnungen.Merken();
                    Ausdehnen();
                }
            };
            el.MouseRightButtonUp += (s, a) =>
            {
                a.Handled = true;
                if (notiz) NotizMenue((Border)el, NotizVon(k));
                else { waehlen(pfade[k]); Menue(el, k); }
            };
        }

        string DingUnter(Point p)
        {
            foreach (var kv in dinge)
            {
                var el = kv.Value;
                double x = Canvas.GetLeft(el), y = Canvas.GetTop(el), w = el.ActualWidth > 0 ? el.ActualWidth : ZB, h = el.ActualHeight > 0 ? el.ActualHeight : ZH;
                if (p.X >= x && p.X <= x + w && p.Y >= y && p.Y <= y + h) return kv.Key;
            }
            return null;
        }

        string FarbeVon(string k)
        {
            if (IstNotiz(k)) { var n = NotizVon(k); return n == null ? null : n.Farbe; }
            Platz p;
            return A.Plaetze.TryGetValue(k, out p) ? p.Farbe : null;
        }

        void ZugBeginnen(string von)
        {
            ZugEnde();
            zugVon = von;
            var m = Mitte(von);
            zug = new Line { X1 = m.X, Y1 = m.Y, X2 = m.X, Y2 = m.Y, Stroke = Pinsel(FarbeVon(von), 0.9), StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 3, 3 }, IsHitTestVisible = false, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
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

        void VerbindenAusMenue(string k)
        {
            ZugBeginnen(k);
            flaeche.MouseMove += ZugFolgen;
            flaeche.MouseLeftButtonDown += ZugAbbrechen;
        }

        void Verbinden(string a, string b)
        {
            ZugEnde();
            if (a == b || A.Linien.Any(l => (l.A == a && l.B == b) || (l.A == b && l.B == a))) return;
            A.Linien.Add(new Linie { A = a, B = b, Farbe = FarbeVon(a) ?? FarbeVon(b) });
            if (!IstNotiz(a)) PlatzVon(a);
            if (!IstNotiz(b)) PlatzVon(b);    // verbundene Symbole behalten ihren Platz
            Anordnungen.Merken();
            LinienZeichnen();
        }

        // ---------- Menüs ----------

        void Menue(FrameworkElement el, string k)
        {
            var m = menueFuer(pfade[k]);
            m.Insert(0, new KeyValuePair<string, Action>("Verbinden …", () => VerbindenAusMenue(k)));
            UI.Menue(el, m, false, false, Farbreihe(FarbeVon(k), f =>
            {
                PlatzVon(k).Farbe = f;
                Anordnungen.Merken();
                FarbeZeigen(k);
                LinienZeichnen();
            }));
        }

        void NotizMenue(Border b, Notiz n)
        {
            string k = Anordnungen.NotizPraefix + n.Id;
            UI.Menue(b, new List<KeyValuePair<string, Action>>
            {
                new KeyValuePair<string, Action>("Schreiben", () => Schreiben(b, n)),
                new KeyValuePair<string, Action>("Verbinden …", () => VerbindenAusMenue(k)),
                new KeyValuePair<string, Action>("-", null),
                new KeyValuePair<string, Action>("Notiz löschen", () => NotizWeg(n)),
            }, false, false, Farbreihe(n.Farbe, f => { n.Farbe = f; NotizFarbe(b, n); Anordnungen.Merken(); }));
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
            foreach (var f in new[] { (string)null }.Concat(Farben))
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
            if (Keyboard.FocusedElement is TextBox) return;
            if (a.Key == Key.Escape && zug != null) { ZugEnde(); a.Handled = true; }
            else if (a.Key == Key.Delete && linieGewaehlt != null)
            {
                A.Linien.Remove(linieGewaehlt);
                linieGewaehlt = null;
                Anordnungen.Merken();
                LinienZeichnen();
                a.Handled = true;
            }
        }

        /// <summary>„Ordnen“: feste Plätze dieses Ordners vergessen – Symbole laufen wieder in Spalten. Farben, Linien, Notizen bleiben.</summary>
        public void Aufraeumen()
        {
            var farbig = A.Plaetze.Where(kv => !string.IsNullOrEmpty(kv.Value.Farbe)).ToDictionary(kv => kv.Key, kv => kv.Value.Farbe);
            A.Plaetze.Clear();
            Verteilen();
            foreach (var kv in farbig) if (dinge.ContainsKey(kv.Key)) { PlatzVon(kv.Key).Farbe = kv.Value; FarbeZeigen(kv.Key); }
            foreach (var n in dinge.Keys.ToList()) if (!IstNotiz(n) && A.Linien.Any(l => l.A == n || l.B == n)) PlatzVon(n);
            Anordnungen.Merken();
        }
    }
}
