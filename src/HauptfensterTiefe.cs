using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Workbench
{
    /// <summary>
    /// Tiefe: auf Ordner-Blättern alle Ordner gleichzeitig eine Ebene aufklappen – bis zum letzten. Tiefe 0 ist die freie Fläche
    /// wie bisher. Ab Tiefe 1 wird jeder Ordner ein Rechteck mit seinem Inhalt (getönt in der Farbe des Ordners), tiefer
    /// verschachtelt. Links eine feine Anzeige (ein Strich je Ebene, klickbar). Umschalt+Rad, Zusammenziehen, Umschalt+↑/↓.
    /// Gelesen wird im Hintergrund, mit Obergrenzen; jeder Wechsel gleitet weich.
    /// </summary>
    partial class Hauptfenster
    {
        class TKnoten { public string Pfad, Name; public bool Ordner; public List<TKnoten> Kinder; public int Mehr; }
        class TStand { public TKnoten Wurzel; public bool Tiefer; public DateTime Zeit; }

        readonly Dictionary<string, int> tiefeVon = new Dictionary<string, int>();
        readonly Dictionary<string, int> tiefeMax = new Dictionary<string, int>();
        readonly Dictionary<string, TStand> tiefeCache = new Dictionary<string, TStand>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> tiefeLaedt = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        DateTime tiefeSperre;
        int tiefeRichtung;

        public int TiefeJetzt { get { return Tiefe(Akt); } }
        int Tiefe(BlattDef b) { int t; return b != null && tiefeVon.TryGetValue(b.Id, out t) ? t : 0; }

        /// <summary>Eine Ebene tiefer (+1) oder höher (-1). Weich, mit kurzer Sperre gegen Rattern.</summary>
        public void TiefeAendern(int d)
        {
            var b = Akt;
            if (b == null || !ImOrdner || (!bild && DateTime.UtcNow < tiefeSperre)) return;
            int jetzt = Tiefe(b), max;
            if (!tiefeMax.TryGetValue(b.Id, out max)) max = 1;
            int neu = Math.Max(0, Math.Min(Math.Max(max, jetzt), jetzt + d));
            if (neu == jetzt) { Wackeln(d); return; }
            TiefeSetzen(b, neu);
        }

        void TiefeSetzen(BlattDef b, int neu)
        {
            int jetzt = Tiefe(b);
            tiefeVon[b.Id] = neu;
            tiefeRichtung = Math.Sign(neu - jetzt);
            tiefeSperre = DateTime.UtcNow.AddMilliseconds(150);
            auswahl = null;
            offenVon.Remove(b.Id);
            seiteVon.Remove(b.Id);
            letzteBoxen.Clear();
            var alt = bild ? null : AltesBild();
            Neuzeichnen();
            if (!bild) Uebergang(alt, tiefeRichtung);
        }

        /// <summary>
        /// Statt eines Bildschirmfotos: die alte Ansicht selbst wandert kurz in die Ebene darüber und blendet dort aus.
        /// Kostet nichts (kein Rendern in eine Bitmap) und bleibt scharf.
        /// </summary>
        FrameworkElement AltesBild()
        {
            if (buehne.Children.Count == 0 || buehne.ActualWidth < 10) return null;
            var el = buehne.Children[0] as FrameworkElement;
            if (el == null) return null;
            var pos = buehne.TransformToAncestor(wurzel).Transform(new Point(0, 0));
            double w = buehne.ActualWidth, h = buehne.ActualHeight;
            buehne.Children.Remove(el);
            var huelle = new Grid { Width = w, Height = h, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(pos.X, pos.Y, 0, 0), IsHitTestVisible = false, ClipToBounds = true };
            huelle.Children.Add(el);
            return huelle;
        }

        /// <summary>Das bisherige Blatt als Bild (einmalig, klein) – fliegt beim Wechsel an einem vorbei.</summary>
        Image Schnappschuss()
        {
            try
            {
                if (buehne.ActualWidth < 10) return null;
                var bmp = new RenderTargetBitmap((int)buehne.ActualWidth, (int)buehne.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bmp.Render(buehne);
                bmp.Freeze();
                var pos = buehne.TransformToAncestor(wurzel).Transform(new Point(0, 0));
                return new Image { Source = bmp, Width = buehne.ActualWidth, Height = buehne.ActualHeight, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(pos.X, pos.Y, 0, 0), IsHitTestVisible = false };
            }
            catch { return null; }
        }

        /// <summary>
        /// In die Tiefe: die alte Ebene wird groß und durchsichtig (man fliegt hindurch), die neue kommt von hinten,
        /// eine feine Bernstein-Linie tastet das Blatt ab. Hinauf: genau umgekehrt. Nur während des Wechsels (≈ 0,35 s).
        /// </summary>
        void Uebergang(FrameworkElement alt, int richtung)
        {
            var dauer = TimeSpan.FromMilliseconds(260);
            var ez = new CubicEase { EasingMode = EasingMode.EaseOut };
            if (alt != null)
            {
                wurzel.Children.Add(alt);
                var st = new ScaleTransform(1, 1);
                alt.RenderTransformOrigin = new Point(0.5, 0.5);
                alt.RenderTransform = st;
                double ziel = richtung > 0 ? 1.18 : 0.86;
                st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, ziel, dauer) { EasingFunction = ez });
                st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, ziel, dauer) { EasingFunction = ez });
                var weg = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(210));
                weg.Completed += delegate { wurzel.Children.Remove(alt); };
                alt.BeginAnimation(OpacityProperty, weg);
            }
            if (buehne.Children.Count > 0)
            {
                var el = (FrameworkElement)buehne.Children[0];
                var st = new ScaleTransform(1, 1);
                el.RenderTransformOrigin = new Point(0.5, 0.5);
                el.RenderTransform = st;
                double von = richtung > 0 ? 0.9 : 1.1;
                st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(von, 1, dauer) { EasingFunction = ez });
                st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(von, 1, dauer) { EasingFunction = ez });
                el.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { BeginTime = TimeSpan.FromMilliseconds(30) });
            }
        }

        /// <summary>Am Ende angekommen: die Anzeige nickt kurz statt nichts zu tun.</summary>
        void Wackeln(int d)
        {
            if (tiefenAnzeige == null || bild) return;
            var tt = new TranslateTransform();
            tiefenAnzeige.RenderTransform = tt;
            tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, d * 4, TimeSpan.FromMilliseconds(110)) { AutoReverse = true });
        }

        /// <summary>Ordner im Hintergrund lesen (bis zur gewünschten Tiefe), dann neu zeichnen. Obergrenzen: 900 Einträge, 40 je Ordner.</summary>
        TStand TiefeLesen(string ordner, int tiefe)
        {
            string k = ordner + "|" + tiefe;
            TStand st;
            TiefeLaden();
            tiefeCache.TryGetValue(k, out st);
            if ((st == null || (DateTime.UtcNow - st.Zeit).TotalSeconds > 20) && tiefeLaedt.Add(k))
            {
                var ui = Dispatcher;
                Func<TStand> lesen = () =>
                {
                    int budget = 900;
                    bool tiefer = false;
                    var w = Scannen(ordner, 0, tiefe, ref budget, ref tiefer);
                    return new TStand { Wurzel = w, Tiefer = tiefer, Zeit = DateTime.UtcNow };
                };
                if (bild) { tiefeLaedt.Remove(k); st = lesen(); tiefeCache[k] = st; return st; }   // Bilder/Test: sofort
                Task.Factory.StartNew(lesen).ContinueWith(t => ui.BeginInvoke(new Action(() =>
                {
                    tiefeLaedt.Remove(k);
                    if (t.Exception != null) return;
                    TStand vorher; tiefeCache.TryGetValue(k, out vorher);
                    bool gleich = vorher != null && Zeichen(vorher.Wurzel) == Zeichen(t.Result.Wurzel) && vorher.Tiefer == t.Result.Tiefer;
                    if (gleich) vorher.Zeit = t.Result.Zeit; else tiefeCache[k] = t.Result;   // gleich: derselbe Stand bleibt (Vorgebautes bleibt gültig)
                    TiefeMerken();
                    if (gleich) return;   // gemerkter Stand war richtig – nichts neu zeichnen
                    var b = Akt;
                    if (b != null && ImOrdner && Paths.Gleich(AktOrdner(b), ordner) && Tiefe(b) == tiefe) { Neuzeichnen(); if (vorher == null && buehne.Children.Count > 0) buehne.Children[0].BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))); }
                })));
            }
            return st;
        }

        static TKnoten Scannen(string pfad, int ebene, int tiefe, ref int budget, ref bool tiefer)
        {
            var k = new TKnoten { Pfad = pfad, Name = Path.GetFileName(pfad.TrimEnd('\\')), Ordner = true, Kinder = new List<TKnoten>() };
            if (ebene > tiefe) return k;
            List<string> dirs, files;
            if (!Schnell.Lesen(pfad, out dirs, out files)) return k;
            int n = 0, grenze = ebene == 0 ? 400 : 40;   // oben: alle Ordner (dafür gibt es Seiten)
            foreach (var d in dirs)
            {
                if (budget <= 0 || n >= grenze) { k.Mehr++; continue; }
                budget--; n++;
                if (ebene < tiefe) k.Kinder.Add(Scannen(d, ebene + 1, tiefe, ref budget, ref tiefer));
                else
                {
                    k.Kinder.Add(new TKnoten { Pfad = d, Name = Path.GetFileName(d), Ordner = true });
                    if (!tiefer) tiefer = Schnell.HatInhalt(d);
                }
            }
            foreach (var f in files)
            {
                if (budget <= 0 || n >= 40) { k.Mehr++; continue; }
                budget--; n++;
                k.Kinder.Add(new TKnoten { Pfad = f, Name = Path.GetFileName(f) });
            }
            return k;
        }

        // ================= Boxen: alles passt ins Bild =================

        readonly Dictionary<string, string> offenVon = new Dictionary<string, string>();   // Blatt → ausgefahrener Ordner
        readonly Dictionary<string, Rect> letzteBoxen = new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
        Size tiefeGroesse, tiefeGemessen;
        readonly Dictionary<string, int> seiteVon = new Dictionary<string, int>();
        int seitenAnzahl = 1;

        /// <summary>
        /// Die aufgeklappte Fläche: jeder Ordner eine Box, zusammen genau so groß wie das Blatt (Kacheln nach Inhalt gewichtet).
        /// Jede Box zeigt, was hineinpasst (+N für den Rest). Klick = ausfahren (andere werden kleiner), Doppelklick auf den Namen = hinein.
        /// </summary>
        FrameworkElement TiefenFlaeche(BlattDef b, string akt, int tiefe)
        {
            var st = TiefeLesen(akt, tiefe);
            var huelle = new Grid { Background = Brushes.Transparent, ClipToBounds = true };
            if (st == null)
            {
                var l = Leer(ReiterZeichen.Fuer(b));
                l.Opacity = 0.4;
                huelle.Children.Add(l);
                return huelle;
            }
            tiefeMax[b.Id] = Math.Max(tiefe + (st.Tiefer ? 1 : 0), tiefe);
            double w = tiefeGemessen.Width > 100 ? tiefeGemessen.Width : (buehne.ActualWidth > 100 ? buehne.ActualWidth - 30 : 1000);
            double h = tiefeGemessen.Height > 100 ? tiefeGemessen.Height : (buehne.ActualHeight > 100 ? buehne.ActualHeight - 30 : 620);
            tiefeGroesse = new Size(w, h);
            var c = new Canvas { Width = w, Height = h, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(14, 2, 0, 0) };
            huelle.Children.Add(c);
            // einmal nachmessen, falls Leisten Platz nehmen
            if (!bild) huelle.SizeChanged += (s, a) =>
            {
                var echt = new Size(Math.Max(100, a.NewSize.Width - 18), Math.Max(100, a.NewSize.Height - 6));
                if (Math.Abs(echt.Width - w) < 24 && Math.Abs(echt.Height - h) < 24) return;   // passt – nichts tun (keine Schleife)
                tiefeGemessen = echt;
                buehne.Dispatcher.BeginInvoke(new Action(() => { if (ImOrdner && Tiefe(Akt) > 0) Neuzeichnen(); }));
            };
            string offen; offenVon.TryGetValue(b.Id, out offen);
            var neu = new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
            // Seiten erst, wenn eine Box sonst zu klein würde. Dann rechts/links Platz für die Pfeile.
            var wurzel0 = st.Wurzel;
            var ordnerAlle = wurzel0.Kinder.Where(x => x.Ordner && x.Kinder != null).ToList();
            var dateienOben = wurzel0.Kinder.Where(x => !(x.Ordner && x.Kinder != null)).ToList();
            int nDat = dateienOben.Count(x => Passt(x.Name)) + wurzel0.Mehr;
            double datGewicht = nDat > 0 ? Math.Sqrt(nDat) + 1 : 0;
            var flaeche = new Rect(0, 0, w, h);
            // lieber voll als verteilt: erst kleinere Boxen zulassen (aufklappen macht sie groß), höchstens 2 Seiten – 3+ nur im Notfall
            List<int> starts = null;
            var innen = new Rect(Rand, 0, Math.Max(200, w - 2 * Rand), Math.Max(200, h - 20));
            foreach (var m in Mindest)
            {
                if (SeitePasst(ordnerAlle, 0, ordnerAlle.Count, datGewicht, flaeche, m)) { starts = new List<int> { 0 }; break; }
                var st2 = SeitenStarts(ordnerAlle, datGewicht, innen, m);
                if (st2.Count <= 2 || m == Mindest[Mindest.Length - 1]) { starts = st2; flaeche = innen; break; }
            }
            seitenAnzahl = starts.Count;
            int seite; seiteVon.TryGetValue(b.Id, out seite);
            seite = Math.Max(0, Math.Min(seitenAnzahl - 1, seite)); seiteVon[b.Id] = seite;
            int von = starts[seite], bis = seite + 1 < starts.Count ? starts[seite + 1] : ordnerAlle.Count;
            var teil = new TKnoten { Pfad = wurzel0.Pfad, Name = wurzel0.Name, Ordner = true, Mehr = seite == 0 ? wurzel0.Mehr : 0,
                Kinder = (seite == 0 ? dateienOben : new List<TKnoten>()).Concat(ordnerAlle.Skip(von).Take(bis - von)).ToList() };
            Fuellen(b, teil, c, flaeche, 0, offen, neu, true);
            if (seitenAnzahl > 1) huelle.Children.Add(SeitenAnzeige(b, seite, tiefe, ordnerAlle.Count - bis));
            // FLIP: was schon da war, gleitet von seinem alten Platz an den neuen
            if (!bild && letzteBoxen.Count > 0)
                foreach (UIElement el in c.Children)
                {
                    var box = el as Border; string p = box != null ? box.Tag as string : null;
                    Rect vorher, jetzt;
                    if (p == null || !letzteBoxen.TryGetValue(p, out vorher) || !neu.TryGetValue(p, out jetzt) || jetzt.Width < 1) continue;
                    var tg = new TransformGroup(); var sk = new ScaleTransform(vorher.Width / jetzt.Width, vorher.Height / jetzt.Height); var tr = new TranslateTransform(vorher.X - jetzt.X, vorher.Y - jetzt.Y);
                    tg.Children.Add(sk); tg.Children.Add(tr);
                    box.RenderTransform = tg;
                    var d = TimeSpan.FromMilliseconds(300); var ez = new CubicEase { EasingMode = EasingMode.EaseOut };
                    sk.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, d) { EasingFunction = ez });
                    sk.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, d) { EasingFunction = ez });
                    tr.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, d) { EasingFunction = ez });
                    tr.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, d) { EasingFunction = ez });
                }
            letzteBoxen.Clear();
            foreach (var kv in neu) letzteBoxen[kv.Key] = kv.Value;
            AblageZiel(huelle, null, () => AktOrdner(b));
            return huelle;
        }

        /// <summary>Den Inhalt eines Ordners in ein Rechteck legen: Dateien als eine Gruppe ohne Rahmen, Unterordner als Boxen.</summary>
        void Fuellen(BlattDef b, TKnoten d, Canvas c, Rect r, int ebene, string offen, Dictionary<string, Rect> merk, bool oben)
        {
            var dateien = d.Kinder.Where(x => !x.Ordner || x.Kinder == null).Where(x => Passt(x.Name)).ToList();
            var ordner = d.Kinder.Where(x => x.Ordner && x.Kinder != null).ToList();
            var teile = new List<object>();
            var gewichte = new List<double>();
            if (dateien.Count > 0 || d.Mehr > 0) { teile.Add(dateien); gewichte.Add(Math.Sqrt(dateien.Count + d.Mehr) + 1); }
            foreach (var o in ordner)
            {
                teile.Add(o);
                double g = Math.Sqrt(Anzahl(o)) + 1.5;
                if (offen != null && (Paths.Gleich(o.Pfad, offen) || offen.StartsWith(o.Pfad + "\\", StringComparison.OrdinalIgnoreCase))) g *= 7;
                gewichte.Add(g);
            }
            if (teile.Count == 0) return;
            var rects = Kacheln(gewichte, r);
            for (int i = 0; i < teile.Count; i++)
            {
                var ri = rects[i];
                var liste = teile[i] as List<TKnoten>;
                if (liste != null) { Dateien(b, liste, d.Mehr, c, ri, ebene); continue; }
                Box(b, (TKnoten)teile[i], c, ri, ebene + 1, offen, merk);
            }
        }

        const double Rand = 58;
        /// <summary>Mindestgröße je Box, von bequem bis knapp (Name + „+N“ passt immer noch).</summary>
        static readonly Size[] Mindest = { new Size(118, 80), new Size(100, 68), new Size(86, 58), new Size(74, 50) };

        /// <summary>Passen diese Ordner (plus Dateigruppe) so, dass jede Box mindestens MinBreite × MinHoehe hat?</summary>
        static bool SeitePasst(List<TKnoten> ordner, int von, int n, double datGewicht, Rect r, Size min)
        {
            var g = new List<double>();
            if (datGewicht > 0) g.Add(datGewicht);
            for (int i = von; i < von + n; i++) g.Add(Math.Sqrt(Anzahl(ordner[i])) + 1.5);
            if (g.Count == 0) return true;
            foreach (var x in Kacheln(g, r)) if (x.Width < min.Width || x.Height < min.Height) return false;
            return true;
        }

        /// <summary>Seitenanfänge: je Seite so viele Ordner wie gut lesbar passen (Halbierungssuche – schnell auch bei 400 Ordnern).</summary>
        static List<int> SeitenStarts(List<TKnoten> ordner, double datGewicht, Rect r, Size min)
        {
            var starts = new List<int> { 0 };
            int i = 0;
            while (i < ordner.Count)
            {
                double dg = starts.Count == 1 ? datGewicht : 0;
                int lo = 1, hi = ordner.Count - i;
                while (lo < hi) { int mid = (lo + hi + 1) / 2; if (SeitePasst(ordner, i, mid, dg, r, min)) lo = mid; else hi = mid - 1; }
                i += lo;
                if (i < ordner.Count) starts.Add(i);
            }
            return starts;
        }

        readonly HashSet<string> pfeilGesehen = new HashSet<string>();

        /// <summary>
        /// Mehr als eine Seite: ein deutlicher runder Pfeil in einem eigenen Streifen am Rand (nichts liegt darunter),
        /// darunter leise „+N“. Beim ersten Erscheinen je Ebene stupst er zweimal kurz nach rechts, danach steht er still.
        /// </summary>
        FrameworkElement SeitenAnzeige(BlattDef b, int seite, int tiefe, int rest)
        {
            var g = new Grid { IsHitTestVisible = true };
            Func<bool, FrameworkElement> pfeil = rechts =>
            {
                var k = new Button { Style = (Style)Application.Current.FindResource("Rund"), Width = 46, Height = 46, Content = UI.Icon(rechts ? Ikon.Rechts : Ikon.Links, 20, Farbe.B(rechts ? "#FFF4B860" : "#CCFFFFFF"), 2.2), Background = Farbe.B(rechts ? "#33F4B860" : "#26FFFFFF"), BorderBrush = Farbe.B(rechts ? "#80F4B860" : "#33FFFFFF"), BorderThickness = new Thickness(1), ToolTip = rechts ? "Next  →" : "Back  ←" };
                k.Click += delegate { SeiteWechseln(rechts ? 1 : -1); };
                var sp = new StackPanel { HorizontalAlignment = rechts ? HorizontalAlignment.Right : HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = rechts ? new Thickness(0, 0, 4, 0) : new Thickness(18, 0, 0, 0) };
                sp.Children.Add(k);
                if (rechts && rest > 0) sp.Children.Add(new TextBlock { Text = "+" + rest, FontSize = 11, Foreground = Farbe.B("#99F4B860"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0), IsHitTestVisible = false });
                if (rechts && !bild && pfeilGesehen.Add(b.Id + "|" + tiefe))
                {
                    var tt = new TranslateTransform(); k.RenderTransform = tt;
                    var a = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(450) };
                    var ez = new SineEase { EasingMode = EasingMode.EaseInOut };
                    a.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                    a.KeyFrames.Add(new EasingDoubleKeyFrame(9, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220)), ez));
                    a.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(440)), ez));
                    a.KeyFrames.Add(new EasingDoubleKeyFrame(6, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(640)), ez));
                    a.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(860)), ez));
                    tt.BeginAnimation(TranslateTransform.XProperty, a);
                }
                return sp;
            };
            if (seite < seitenAnzahl - 1) g.Children.Add(pfeil(true));
            if (seite > 0) g.Children.Add(pfeil(false));
            var punkte = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, -2), IsHitTestVisible = false };
            for (int i = 0; i < seitenAnzahl; i++) punkte.Children.Add(new Border { Width = i == seite ? 14 : 5, Height = 5, CornerRadius = new CornerRadius(2.5), Background = Farbe.B(i == seite ? "#CCF4B860" : "#40FFFFFF"), Margin = new Thickness(3, 0, 3, 0) });
            g.Children.Add(punkte);
            return g;
        }

        /// <summary>Nächste/vorige Seite – gleitet seitlich. true = es gab etwas zu blättern.</summary>
        public bool SeiteWechseln(int d)
        {
            var b = Akt;
            if (b == null || !ImOrdner || Tiefe(b) == 0 || seitenAnzahl <= 1) return false;
            int s; seiteVon.TryGetValue(b.Id, out s);
            int neu = Math.Max(0, Math.Min(seitenAnzahl - 1, s + d));
            if (neu == s) { Wackeln(d); return true; }
            seiteVon[b.Id] = neu;
            letzteBoxen.Clear();
            Neuzeichnen();
            if (!bild && buehne.Children.Count > 0)
            {
                var el = buehne.Children[0];
                var tt = new TranslateTransform();
                el.RenderTransform = tt;
                tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(d * 60, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                el.BeginAnimation(OpacityProperty, new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(220)));
            }
            return true;
        }

        static int Anzahl(TKnoten k) { return k.Kinder == null ? 1 : k.Kinder.Sum(x => x.Ordner && x.Kinder != null ? Anzahl(x) : 1) + k.Mehr; }

        void Box(BlattDef b, TKnoten d, Canvas c, Rect r, int ebene, string offen, Dictionary<string, Rect> merk)
        {
            const double luft = 5;
            var ri = new Rect(r.X + luft, r.Y + luft, Math.Max(0, r.Width - 2 * luft), Math.Max(0, r.Height - 2 * luft));
            if (ri.Width < 30 || ri.Height < 26) return;
            string farbe = OrdnerFarbe(d.Pfad);
            bool istOffen = offen != null && Paths.Gleich(d.Pfad, offen);
            var box = new Border
            {
                Width = ri.Width, Height = ri.Height, CornerRadius = new CornerRadius(Math.Min(14, ri.Height / 4)),
                Background = farbe != null ? Tisch.Pinsel(farbe, 0.10 + 0.03 * ebene) : Farbe.B(ebene % 2 == 1 ? "#0AFFFFFF" : "#0FFFFFFF"),
                BorderBrush = istOffen ? Farbe.AkzentRand : (farbe != null ? Tisch.Pinsel(farbe, 0.45) : Farbe.B("#1FFFFFFF")),
                BorderThickness = new Thickness(1), Tag = d.Pfad, ClipToBounds = true, Cursor = Cursors.Hand,
            };
            Canvas.SetLeft(box, ri.X); Canvas.SetTop(box, ri.Y);
            merk[d.Pfad] = ri;
            var inner = new Canvas { Width = ri.Width, Height = ri.Height };
            box.Child = inner;
            // Kopf: Punkt, Name, Anzahl
            var kopf = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(9, 6, 0, 0), Background = Brushes.Transparent, ToolTip = d.Pfad };
            kopf.Children.Add(new System.Windows.Shapes.Ellipse { Width = 6, Height = 6, Fill = farbe != null ? Tisch.Pinsel(farbe, 0.95) : Farbe.B("#59FFFFFF"), VerticalAlignment = VerticalAlignment.Center });
            kopf.Children.Add(new TextBlock { Text = d.Name, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Farbe.B("#D9FFFFFF"), Margin = new Thickness(6, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = Math.Max(20, ri.Width - 60) });
            kopf.Children.Add(new TextBlock { Text = Anzahl(d).ToString(), FontSize = 10.5, Foreground = Farbe.B("#59FFFFFF"), Margin = new Thickness(6, 1, 0, 0) });
            inner.Children.Add(kopf);
            kopf.MouseLeftButtonDown += (s, a) => { if (a.ClickCount == 2) { a.Handled = true; tiefeVon[b.Id] = 0; Betreten(b, d.Pfad); } };
            // Klick ins Leere der Box = ausfahren / einfahren
            box.MouseLeftButtonUp += (s, a) =>
            {
                if (a.Handled) return;
                a.Handled = true;
                string jetzt; offenVon.TryGetValue(b.Id, out jetzt);
                if (jetzt != null && Paths.Gleich(jetzt, d.Pfad)) offenVon.Remove(b.Id); else offenVon[b.Id] = d.Pfad;
                Neuzeichnen();
            };
            box.MouseLeftButtonDown += (s, a) => a.Handled = true;
            box.MouseEnter += delegate { if (!istOffen) box.BorderBrush = farbe != null ? Tisch.Pinsel(farbe, 0.7) : Farbe.B("#40FFFFFF"); };
            box.MouseLeave += delegate { if (!istOffen) box.BorderBrush = farbe != null ? Tisch.Pinsel(farbe, 0.45) : Farbe.B("#1FFFFFFF"); };
            box.MouseRightButtonUp += (s, a) => { if (a.Handled) return; a.Handled = true; UI.Menue(box, KontextOrdner(d.Pfad, b), true); };
            AblageZiel(box, null, () => d.Pfad, () => { offenVon[b.Id] = d.Pfad; Neuzeichnen(); }, null);
            c.Children.Add(box);
            // Inhalt: in die Box, unter den Kopf
            if (ri.Height > 48 && d.Kinder != null) Fuellen(b, d, inner, new Rect(4, 24, ri.Width - 8, ri.Height - 28), ebene, offen, merk, false);
        }

        /// <summary>Dateien in ein Rechteck: Symbolgröße nach Platz, so viele wie passen, Rest als +N.</summary>
        void Dateien(BlattDef b, List<TKnoten> liste, int mehr, Canvas c, Rect r, int ebene)
        {
            int n = liste.Count + (mehr > 0 ? 1 : 0);
            if (n == 0 || r.Width < 24 || r.Height < 24) return;
            double s = Math.Max(20, Math.Min(56, Math.Sqrt(r.Width * r.Height / n) * 0.5));
            bool namen = s >= 30;
            double zw = s + (namen ? 30 : 10), zh = s + (namen ? 24 : 8);
            int spalten = Math.Max(1, (int)(r.Width / zw)), zeilen = Math.Max(1, (int)(r.Height / zh));
            int platz = spalten * zeilen;
            int zeigen = liste.Count <= platz ? liste.Count : Math.Max(0, platz - 1);
            int rest = liste.Count - zeigen + mehr;
            bool cloud = Akt != null && Akt.Art == "cloud";
            for (int i = 0; i < zeigen; i++)
            {
                var p = liste[i].Pfad;
                var el = TiefStueck(b, p, s, zw, namen, cloud, liste[i].Ordner);
                Canvas.SetLeft(el, r.X + (i % spalten) * zw); Canvas.SetTop(el, r.Y + (i / spalten) * zh);
                c.Children.Add(el);
            }
            if (rest > 0)
            {
                var m = MehrPille(rest);
                int i = zeigen;
                Canvas.SetLeft(m, r.X + (i % spalten) * zw + 4); Canvas.SetTop(m, r.Y + (i / spalten) * zh + s / 3);
                c.Children.Add(m);
            }
        }

        /// <summary>Ein Eintrag in der Tiefe: echte Vorschau (oder Typ-Marke), kleiner Name; wie auf dem Desktop bedienbar.</summary>
        FrameworkElement TiefStueck(BlattDef b, string p, double s, double breite, bool name, bool cloud, bool ordner)
        {
            var k = new Border { Width = breite - 2, Padding = new Thickness(1, 2, 1, 2), CornerRadius = new CornerRadius(7), Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(1), ToolTip = System.IO.Path.GetFileName(p) };
            var st = new StackPanel();
            var bildEl = (cloud || s < 34) && !ordner ? Vorschau.Glyphe(p, Math.Round(s * 0.78), s * 0.94, true, false) : Vorschau.Echt(p, s, ordner);   // klein = schnelle Typ-Marke
            bildEl.HorizontalAlignment = HorizontalAlignment.Center;
            st.Children.Add(bildEl);
            if (name) st.Children.Add(new TextBlock { Text = System.IO.Path.GetFileNameWithoutExtension(p), FontSize = 10, Foreground = Farbe.B("#A6FFFFFF"), TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 3, 0, 0) });
            k.Child = st;
            symbole[p] = k;
            k.MouseEnter += delegate { Stil(k, gruppe.Contains(p), true); };
            k.MouseLeave += delegate { Stil(k, gruppe.Contains(p), false); };
            Bedienbar(k, p, () => { if (ordner) { tiefeVon[b.Id] = 0; Betreten(b, p); } else Oeffnen(p); }, el => UI.Menue(el, KontextOrdner(p, b), true));
            k.MouseLeftButtonUp += (s2, a) => a.Handled = true;
            if (ordner) AblageZiel(k, k, () => p);
            return k;
        }

        static FrameworkElement MehrPille(int n)
        {
            return new Border { CornerRadius = new CornerRadius(10), Background = Farbe.B("#14FFFFFF"), Padding = new Thickness(8, 2, 8, 3), Child = new TextBlock { Text = "+" + n, FontSize = 11, Foreground = Farbe.B("#99FFFFFF") } };
        }

        /// <summary>Quadratische Kacheln (squarified treemap): Flächen nach Gewicht, möglichst wenig lang gezogen.</summary>
        static List<Rect> Kacheln(List<double> gewichte, Rect r)
        {
            var erg = new Rect[gewichte.Count];
            double summe = gewichte.Sum();
            if (summe <= 0) summe = 1;
            var idx = Enumerable.Range(0, gewichte.Count).OrderByDescending(i => gewichte[i]).ToList();
            var flaeche = idx.Select(i => gewichte[i] / summe * r.Width * r.Height).ToList();
            var rest = r;
            int start = 0;
            while (start < idx.Count)
            {
                double kurz = Math.Min(rest.Width, rest.Height);
                int ende = start;
                double schlechtest = double.MaxValue, reihe = 0;
                while (ende < idx.Count)
                {
                    double neu = reihe + flaeche[ende];
                    double mx = 0, mn = double.MaxValue;
                    for (int j = start; j <= ende; j++) { mx = Math.Max(mx, flaeche[j]); mn = Math.Min(mn, flaeche[j]); }
                    double s = Math.Max((kurz * kurz * mx) / (neu * neu), (neu * neu) / (kurz * kurz * mn));
                    if (s > schlechtest) break;
                    schlechtest = s; reihe = neu; ende++;
                }
                double d2 = reihe / kurz;
                double pos = 0;
                bool waagrecht = rest.Width >= rest.Height;   // Reihe entlang der kurzen Seite
                for (int j = start; j < ende; j++)
                {
                    double laenge = flaeche[j] / d2;
                    erg[idx[j]] = waagrecht ? new Rect(rest.X, rest.Y + pos, d2, laenge) : new Rect(rest.X + pos, rest.Y, laenge, d2);
                    pos += laenge;
                }
                rest = waagrecht ? new Rect(rest.X + d2, rest.Y, Math.Max(0, rest.Width - d2), rest.Height) : new Rect(rest.X, rest.Y + d2, rest.Width, Math.Max(0, rest.Height - d2));
                start = ende;
            }
            return erg.ToList();
        }

        /// <summary>Farbe, die der Ordner auf der freien Fläche seines Elternordners bekommen hat.</summary>
        static string OrdnerFarbe(string pfad)
        {
            try
            {
                var a = Anordnungen.Fuer(System.IO.Path.GetDirectoryName(pfad.TrimEnd('\\')));
                Platz pl;
                return a.Plaetze.TryGetValue(System.IO.Path.GetFileName(pfad.TrimEnd('\\')), out pl) && !string.IsNullOrEmpty(pl.Farbe) ? pl.Farbe : null;
            }
            catch { return null; }
        }

        FrameworkElement tiefenAnzeige;

        /// <summary>Links am Rand: ein feiner Strich je Ebene, der aktuelle in Bernstein. Klick = dorthin. Nur wenn es Unterordner gibt.</summary>
        FrameworkElement TiefenAnzeige(BlattDef b, bool hatUnterordner)
        {
            int jetzt = Tiefe(b), max;
            if (!tiefeMax.TryGetValue(b.Id, out max)) max = hatUnterordner ? 1 : 0;
            max = Math.Max(max, jetzt);
            if (max == 0) { tiefenAnzeige = null; return null; }
            var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(-12, 0, 0, 0), Background = Brushes.Transparent };
            var zahlen = new List<TextBlock>();
            // Darüberfahren: die Zahlen erscheinen weich neben den Strichen – ein Klick springt direkt (auch zwei Ebenen)
            sp.MouseEnter += delegate { foreach (var z in zahlen) z.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(140))); };
            sp.MouseLeave += delegate { foreach (var z in zahlen) z.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(200))); };
            for (int i = 0; i <= Math.Min(max, 8); i++)
            {
                int i2 = i;
                bool an = i == jetzt;
                var feld = new Border { Width = 54, Height = 17, Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = i == 0 ? "Surface" : null };
                ToolTipService.SetInitialShowDelay(feld, 500);
                var reihe = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                var strich = new Border { Width = an ? 14 : 8, Height = an ? 2.5 : 1.5, CornerRadius = new CornerRadius(1), Background = Farbe.B(an ? "#FFF4B860" : "#4DFFFFFF"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
                var zahl = new TextBlock { Text = Griechisch(i), FontSize = 12, FontFamily = new FontFamily("Cambria, Georgia, Segoe UI"), Foreground = Farbe.B(an ? "#FFF4B860" : "#B3FFFFFF"), Margin = new Thickness(an ? 6 : 12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Opacity = 0, IsHitTestVisible = false };
                reihe.Children.Add(strich); reihe.Children.Add(zahl);
                feld.Child = reihe;
                zahlen.Add(zahl);
                feld.MouseEnter += delegate { if (!an) { strich.Background = Farbe.B("#E6FFFFFF"); strich.Width = 12; zahl.Foreground = Brushes.White; } };
                feld.MouseLeave += delegate { if (!an) { strich.Background = Farbe.B("#4DFFFFFF"); strich.Width = 8; zahl.Foreground = Farbe.B("#B3FFFFFF"); } };
                feld.MouseLeftButtonDown += (s, a) => { a.Handled = true; if (!an) TiefeSetzen(b, i2); };
                sp.Children.Add(feld);
            }
            tiefenAnzeige = sp;
            return sp;
        }

        /// <summary>Etwas hat sich geändert: alles gilt als „nachsehen“, bleibt aber als Platzhalter stehen (kein leeres Bild).</summary>
        void TiefeVergessen() { foreach (var st in tiefeCache.Values) st.Zeit = DateTime.MinValue; }

        static string Zeichen(TKnoten k)
        {
            var sb = new System.Text.StringBuilder();
            Action<TKnoten> lauf = null;
            lauf = x => { sb.Append(x.Pfad).Append('|').Append(x.Mehr).Append('\n'); if (x.Kinder != null) foreach (var y in x.Kinder) lauf(y); };
            lauf(k);
            return sb.ToString();
        }

        // ---------- Gemerkte Ebenen: auf der Platte, damit die Tiefe nach dem Start sofort stimmt ----------

        bool tiefeGeladen;
        DispatcherTimer tiefeSpeicher;
        static string TiefeDatei { get { return Path.Combine(Paths.Daten, "tiefe.bin"); } }

        void TiefeLaden()
        {
            if (tiefeGeladen) return;
            tiefeGeladen = true;
            if (bild || Paths.Test) return;
            try
            {
                if (!File.Exists(TiefeDatei)) return;
                using (var r = new BinaryReader(File.OpenRead(TiefeDatei), System.Text.Encoding.UTF8))
                {
                    if (r.ReadInt32() != 1) return;
                    int n = r.ReadInt32();
                    for (int i = 0; i < n; i++)
                    {
                        string k = r.ReadString();
                        var st = new TStand { Tiefer = r.ReadBoolean(), Zeit = DateTime.MinValue, Wurzel = KnotenLesen(r) };   // gilt sofort, wird still nachgeprüft
                        if (!tiefeCache.ContainsKey(k)) tiefeCache[k] = st;
                    }
                }
            }
            catch { }
        }

        static TKnoten KnotenLesen(BinaryReader r)
        {
            var k = new TKnoten { Pfad = r.ReadString(), Ordner = r.ReadBoolean(), Mehr = r.ReadInt32() };
            k.Name = Path.GetFileName(k.Pfad.TrimEnd('\\'));
            int n = r.ReadInt32();
            if (n >= 0) { k.Kinder = new List<TKnoten>(n); for (int i = 0; i < n; i++) k.Kinder.Add(KnotenLesen(r)); }
            return k;
        }

        static void KnotenSchreiben(BinaryWriter w, TKnoten k)
        {
            w.Write(k.Pfad ?? ""); w.Write(k.Ordner); w.Write(k.Mehr);
            w.Write(k.Kinder == null ? -1 : k.Kinder.Count);
            if (k.Kinder != null) foreach (var x in k.Kinder) KnotenSchreiben(w, x);
        }

        /// <summary>Gesammelt nach kurzer Ruhe schreiben, im Hintergrund – nie mitten im Wechsel.</summary>
        void TiefeMerken()
        {
            if (bild || Paths.Test) return;
            if (tiefeSpeicher == null)
            {
                tiefeSpeicher = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromSeconds(4) };
                tiefeSpeicher.Tick += delegate
                {
                    tiefeSpeicher.Stop();
                    var stand = tiefeCache.Where(x => x.Value != null && x.Value.Wurzel != null).OrderByDescending(x => x.Value.Zeit).Take(60).ToList();
                    Task.Factory.StartNew(() =>
                    {
                        try
                        {
                            string tmp = TiefeDatei + ".neu";
                            using (var w = new BinaryWriter(File.Create(tmp), System.Text.Encoding.UTF8))
                            {
                                w.Write(1); w.Write(stand.Count);
                                foreach (var x in stand) { w.Write(x.Key); w.Write(x.Value.Tiefer); KnotenSchreiben(w, x.Value.Wurzel); }
                            }
                            if (File.Exists(TiefeDatei)) File.Replace(tmp, TiefeDatei, null); else File.Move(tmp, TiefeDatei);
                        }
                        catch { }
                    }, TaskCreationOptions.LongRunning);
                };
            }
            tiefeSpeicher.Stop(); tiefeSpeicher.Start();
        }

        static readonly string[] griechisch = { "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX" };
        static string Griechisch(int i) { return i >= 0 && i < griechisch.Length ? griechisch[i] : i.ToString(); }
    }
}
