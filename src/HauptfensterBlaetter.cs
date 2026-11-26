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
    /// Blätter statt fester Ebenen: Expiring gibt es immer, alles andere wählt der Nutzer über das + (Desktop, Ordner, Board,
    /// Screenshots, Cloud). Links die Schiene mit einem Reiter je Blatt (ziehen = umsortieren, nach rechts herausziehen = weg).
    /// An jedes Blatt lassen sich Leisten andocken (Recent, Ablage) – oben, unten oder rechts.
    /// </summary>
    partial class Hauptfenster
    {
        public static int Befristet { get { return Math.Max(0, Blaetter.Index("temp")); } }
        int N { get { return Blaetter.Liste.Count; } }
        bool IstBlatt(int a) { return a >= 0 && a < N; }
        BlattDef Akt { get { return IstBlatt(ansicht) ? Blaetter.Liste[ansicht] : null; } }
        bool ImOrdner { get { return Blaetter.IstOrdnerArt(Akt); } }
        bool IstBoard { get { var b = Akt; return b != null && b.Art == "board"; } }
        string BlattName(int i) { return Blaetter.NameVon(Blaetter.Liste[i], wolke); }

        /// <summary>Start: das mittlere Blatt. Ist es noch leer und nicht der erste Start, dann Expiring.</summary>
        public static int StartAnsicht()
        {
            int m = Blaetter.Mitte;
            if (Blaetter.Liste[m].Art == "empty" && !Blaetter.Frisch) return Befristet;
            return m;
        }

        // ================= Schiene =================

        readonly List<Reiter> reiter = new List<Reiter>();
        int aktivesBlatt = -1;
        Grid regler;
        Reiter plusReiter;
        bool ordnetGerade;

        FrameworkElement Schiene()
        {
            var g = new Grid();
            regler = new Grid { Width = 44, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Background = Brushes.Transparent, Margin = new Thickness(0, -60, 0, 0) };
            regler.MouseEnter += delegate { PlusZeigen(true); };
            regler.MouseLeave += delegate { PlusZeigen(false); };
            SchieneFuellen();
            reglerEl = regler;
            Zuletzt.Neu += ZuletztNeu;
            Closed += delegate { Zuletzt.Neu -= ZuletztNeu; };
            g.Children.Add(regler);

            var unten = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 16) };
            btnKorb = RundKnopf(Ikon.Muell, "Recycle Bin", () => Zeige(ansicht == Korb ? letzteEbene : Korb));
            var kg = new Grid();
            kg.Children.Add(btnKorb);
            korbPunkt = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = Farbe.Bald, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 7, 7, 0), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
            kg.Children.Add(korbPunkt);
            AblageZielKorb(btnKorb);
            btnEinst = RundKnopf(Ikon.Regler, "Settings", () => Zeige(ansicht == Einst ? letzteEbene : Einst));
            var eg = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            eg.Children.Add(btnEinst);
            updatePunkt = new System.Windows.Shapes.Ellipse { Width = 6, Height = 6, Fill = Farbe.Akzent, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 8, 8, 0), IsHitTestVisible = false, Visibility = Update.Neu(E.Cfg) != null ? Visibility.Visible : Visibility.Collapsed };
            eg.Children.Add(updatePunkt);
            Update.Gefunden += UpdateDa;
            unten.Children.Add(kg);
            unten.Children.Add(eg);
            g.Children.Add(unten);
            return g;
        }

        /// <summary>Reiter neu aufbauen (nach Hinzufügen, Umsortieren, Wählen).</summary>
        void SchieneFuellen()
        {
            regler.Children.Clear();
            reiter.Clear();
            plusReiter = null;
            var l = Blaetter.Liste;
            bool plus = l.Count < Blaetter.Max && !l.Any(b => b.Art == "empty");
            regler.Height = 24 + (l.Count - 1 + (plus ? 1 : 0)) * Abstand;
            for (int i = 0; i < l.Count; i++)
            {
                var r = new Reiter(ReiterZeichen.Fuer(l[i])) { Margin = new Thickness(3, i * Abstand + 3, 0, 0) };
                reiter.Add(r);
                regler.Children.Add(r);
            }
            for (int i = 0; i < l.Count; i++)
            {
                int nr = i;
                var b = l[i];
                var knopf = new Button { Style = (Style)Application.Current.FindResource("Leer"), Width = 40, Height = 23, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, i * Abstand, 0, 0), ToolTip = BlattName(i) };
                ToolTipService.SetPlacement(knopf, PlacementMode.Right);
                ToolTipService.SetInitialShowDelay(knopf, 150);
                System.Windows.Automation.AutomationProperties.SetName(knopf, BlattName(i));
                knopf.Click += delegate { if (!ordnetGerade) { Zeige(nr); RundgangMitgehen(); } };
                Action federn = () => Zeige(nr);
                Action<bool> echo = an => { reiter[nr].Ziel = an; };
                if (b.Art == "temp") AblageZiel(knopf, null, BefristetOrdner, federn, echo);
                else if (b.Art == "board") BoardZiel(knopf, null, b, federn, echo);
                else AblageZiel(knopf, null, () => WurzelVon(b), federn, echo);
                knopf.MouseEnter += delegate { reiter[nr].Ueber = true; if (nr != aktivesBlatt) Schieben(reiter[nr], 3); };
                knopf.MouseLeave += delegate { reiter[nr].Ueber = false; if (nr != aktivesBlatt) Schieben(reiter[nr], 0); };
                Ordnen(knopf, nr);
                regler.Children.Add(knopf);
            }
            if (plus)
            {
                plusReiter = new Reiter(ReiterZeichen.Plus) { Margin = new Thickness(3, l.Count * Abstand + 3, 0, 0), Opacity = l.Count <= 1 || bild ? 0.6 : 0 };
                regler.Children.Add(plusReiter);
                var pk = new Button { Style = (Style)Application.Current.FindResource("Leer"), Width = 40, Height = 23, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, l.Count * Abstand, 0, 0), ToolTip = "New sheet" };
                ToolTipService.SetPlacement(pk, PlacementMode.Right);
                ToolTipService.SetInitialShowDelay(pk, 300);
                pk.Click += delegate { BlattNeu(); };
                pk.MouseEnter += delegate { if (plusReiter != null) plusReiter.Ueber = true; };
                pk.MouseLeave += delegate { if (plusReiter != null) plusReiter.Ueber = false; };
                regler.Children.Add(pk);
            }
            aktivesBlatt = -1;
            ReiterSetzen();
        }

        /// <summary>Das + erscheint nur, wenn die Maus bei den Blättern ist – außer es gibt erst ein Blatt.</summary>
        public void PlusZeigen(bool an)
        {
            if (plusReiter == null || bild || Blaetter.Liste.Count <= 1) return;
            plusReiter.BeginAnimation(OpacityProperty, new DoubleAnimation(an ? 0.6 : 0, TimeSpan.FromMilliseconds(150)));
        }

        void Schieben(Reiter r, double x)
        {
            x = r.Rund(x);
            if (bild) { r.Schub.X = x; return; }
            r.Schub.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, TimeSpan.FromMilliseconds(130)));
        }

        /// <summary>Aktives Blatt heraus und hell, die anderen ruhig. Auf Papierkorb/Einstellungen: aktives gedämpft.</summary>
        void ReiterSetzen()
        {
            int aktiv = letzteEbene;
            bool aufBlatt = IstBlatt(ansicht);
            bool neuAktiv = aktiv != aktivesBlatt;
            aktivesBlatt = aktiv;
            for (int i = 0; i < reiter.Count; i++)
            {
                var r = reiter[i];
                bool an = i == aktiv;
                r.An = an;
                double x = r.Rund(an ? 9 : 0), o = an && !aufBlatt ? 0.5 : 1;
                if (bild) { r.Schub.X = x; r.Opacity = o; continue; }
                if (neuAktiv || !an) r.Schub.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, TimeSpan.FromMilliseconds(an ? 320 : 220)) { EasingFunction = an ? (IEasingFunction)new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 } : UI.Weich });
                r.BeginAnimation(OpacityProperty, new DoubleAnimation(o, TimeSpan.FromMilliseconds(180)));
            }
        }

        /// <summary>Reiter ziehen: hoch/runter = umsortieren, nach rechts heraus = Blatt entfernen (Dateien bleiben).</summary>
        void Ordnen(Button knopf, int i)
        {
            Point start = new Point();
            bool druck = false, zieht = false;
            knopf.PreviewMouseLeftButtonDown += (s, a) => { start = a.GetPosition(regler); druck = true; zieht = false; ordnetGerade = false; };
            knopf.PreviewMouseMove += (s, a) =>
            {
                if (!druck || a.LeftButton != MouseButtonState.Pressed || i >= reiter.Count) return;
                var p = a.GetPosition(regler);
                double dy = p.Y - start.Y, dx = p.X - start.X;
                if (!zieht)
                {
                    if (Math.Abs(dy) < 7 && dx < 12) return;
                    zieht = true; ordnetGerade = true;
                    knopf.CaptureMouse();
                }
                var r = reiter[i];
                bool raus = dx > 40 && Blaetter.Liste[i].Art != "temp";
                r.Schub.BeginAnimation(TranslateTransform.YProperty, null);
                r.Schub.BeginAnimation(TranslateTransform.XProperty, null);
                r.Schub.Y = Math.Max(-i * Abstand - 6, Math.Min((N - 1 - i) * Abstand + 6, dy));
                r.Schub.X = Math.Max(0, Math.Min(56, dx));
                r.Opacity = raus ? 0.3 : 1;
                int ziel = raus ? -1 : Zielplatz(i, dy);
                for (int j = 0; j < reiter.Count; j++)
                {
                    if (j == i) continue;
                    double y = ziel < 0 ? 0 : (ziel > i && j > i && j <= ziel ? -Abstand : (ziel < i && j < i && j >= ziel ? Abstand : 0));
                    if (Math.Abs(reiter[j].Schub.Y - y) > 0.5) reiter[j].Schub.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, TimeSpan.FromMilliseconds(140)) { EasingFunction = UI.Weich });
                }
            };
            knopf.PreviewMouseLeftButtonUp += (s, a) =>
            {
                if (!druck) return;
                druck = false;
                if (!zieht) return;
                a.Handled = true;
                zieht = false;
                knopf.ReleaseMouseCapture();
                var p = a.GetPosition(regler);
                double dy = p.Y - start.Y, dx = p.X - start.X;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    ordnetGerade = false;
                    if (dx > 40 && Blaetter.Liste[i].Art != "temp") BlattWeg(i);
                    else { int z = Zielplatz(i, dy); if (z != i) BlattVerschieben(i, z); else SchieneFuellen(); }
                }));
            };
        }

        int Zielplatz(int i, double dy) { return Math.Max(0, Math.Min(N - 1, i + (int)Math.Round(dy / Abstand))); }

        public void BlattVerschieben(int von, int nach)
        {
            var l = Blaetter.Liste;
            var akt = Akt;
            var letzt = IstBlatt(letzteEbene) ? l[letzteEbene] : null;
            var b = l[von];
            l.RemoveAt(von);
            l.Insert(nach, b);
            Blaetter.Speichern();
            if (letzt != null) letzteEbene = l.IndexOf(letzt);
            if (akt != null) ansicht = l.IndexOf(akt);
            SchieneFuellen();
            Zeige(ansicht);
        }

        public void BlattWeg(int i)
        {
            var l = Blaetter.Liste;
            if (!IstBlatt(i) || l[i].Art == "temp") return;
            var akt = Akt;
            l.RemoveAt(i);
            Blaetter.Speichern();
            int neu = akt == null ? ansicht : l.IndexOf(akt);
            if (akt != null && neu < 0) neu = Math.Max(0, Math.Min(i, l.Count - 1));
            if (IstBlatt(letzteEbene) == false || letzteEbene >= l.Count) letzteEbene = Math.Max(0, l.Count - 1);
            SchieneFuellen();
            ansicht = -1;
            Zeige(neu);
        }

        public void BlattNeu()
        {
            var l = Blaetter.Liste;
            int leer = l.FindIndex(b => b.Art == "empty");
            if (leer < 0)
            {
                if (l.Count >= Blaetter.Max) return;
                l.Add(new BlattDef { Art = "empty" });
                leer = l.Count - 1;
                Blaetter.Speichern();
                SchieneFuellen();
            }
            Zeige(leer);
        }

        // ================= Blatt wechseln =================

        public void Zeige(int neu)
        {
            if (!IstBlatt(neu) && neu != Korb && neu != Einst && neu != Netzansicht) neu = Befristet;
            bool wechsel = neu != ansicht;
            int richtung = IstBlatt(neu) && IstBlatt(ansicht) ? Math.Sign(neu - ansicht) : 0;
            ansicht = neu;
            if (IstBlatt(neu)) letzteEbene = neu;
            if (!IstBlatt(letzteEbene)) letzteEbene = Befristet;
            ReiterSetzen();
            btnKorb.Background = neu == Korb ? Schwebe : Brushes.Transparent;
            btnEinst.Background = neu == Einst ? Schwebe : Brushes.Transparent;
            btnNetz.Background = neu == Netzansicht ? Schwebe : Brushes.Transparent;
            // so viele Blattkanten, wie Blätter darunter liegen (höchstens zwei)
            int darunter = IstBlatt(neu) ? Math.Min(2, N - 1 - neu) : 0;
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
                double von = richtung == 0 ? 10 : richtung * 40;
                blattY.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(von, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = UI.Weich });
                blatt.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(260)));
            }
        }

        void Schritt(int d)
        {
            int von = IstBlatt(ansicht) ? ansicht : letzteEbene;
            int neu = Math.Max(0, Math.Min(N - 1, von + d));
            if (neu != ansicht) { Zeige(neu); RundgangMitgehen(); }
        }

        FrameworkElement Flaeche()
        {
            aktuellerTisch = null;
            if (ansicht == Korb) return FlaecheKorb();
            if (ansicht == Netzansicht) return FlaecheNetz();
            if (ansicht == Einst) return FlaecheEinstellungen();
            var b = Akt;
            FrameworkElement v;
            if (b.Art == "temp") v = FlaecheBefristet();
            else if (b.Art == "board") v = FlaecheBoard(b);
            else if (b.Art == "empty") v = FlaecheNeu(b);
            else v = FlaecheOrdner(b);
            return b.Art == "empty" ? v : MitLeisten(b, v);
        }

        /// <summary>Welches Blatt zeigt diesen Ordner? (längste passende Wurzel; Zeit-Ordner = Expiring; sonst -1)</summary>
        int AnsichtFuer(string ordner)
        {
            int best = -1, len = -1;
            var l = Blaetter.Liste;
            for (int i = 0; i < l.Count; i++)
            {
                string w = WurzelVon(l[i]);
                if (w == null) continue;
                if ((ordner.TrimEnd('\\') + "\\").StartsWith(w.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase) && w.Length > len) { best = i; len = w.Length; }
            }
            if (best >= 0) return best;
            if (Paths.DauerVonOrdner(System.IO.Path.Combine(ordner, "x")) != null) return Befristet;
            return -1;
        }

        // ================= Neues Blatt: großes, leises + =================

        FrameworkElement FlaecheNeu(BlattDef b)
        {
            OrtSetzen("");
            var mitte = new Grid { Width = 300, Height = 300, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent, Margin = new Thickness(0, 0, 0, 30) };
            var plus = UI.Icon(Ikon.Plus, 56, Farbe.B("#38FFFFFF"), 1.0);
            plus.HorizontalAlignment = HorizontalAlignment.Center;
            plus.VerticalAlignment = VerticalAlignment.Center;
            mitte.Children.Add(plus);
            var wahl = new List<Tuple<string, string, Action>>
            {
                Tuple.Create(ReiterZeichen.Bildschirm, "Desktop", (Action)(() => Waehlen(b, "folder", Paths.Desktop, "Desktop", ReiterZeichen.Bildschirm))),
                Tuple.Create(ReiterZeichen.Ordner, "Folder", (Action)(() => OrdnerWaehlen(b))),
                Tuple.Create(ReiterZeichen.Board, "Board", (Action)(() => Waehlen(b, "board", "", "Board", ""))),
                Tuple.Create(ReiterZeichen.Ausschnitt, "Screenshots", (Action)(() => { try { Directory.CreateDirectory(Blaetter.Screenshots); } catch { } Waehlen(b, "folder", Blaetter.Screenshots, "Screenshots", ReiterZeichen.Ausschnitt); })),
                Tuple.Create(ReiterZeichen.Wolke, wolke != null ? wolke.Name : "Cloud", (Action)(() => { if (wolke != null) Waehlen(b, "cloud", "", "", ""); else OrdnerWaehlen(b, "cloud"); })),
            };
            var knoepfe = new List<FrameworkElement>();
            for (int i = 0; i < wahl.Count; i++)
            {
                double w = -Math.PI / 2 + i * 2 * Math.PI / wahl.Count, r = 96;
                var w2 = wahl[i];
                var k = new Border { Width = 48, Height = 48, CornerRadius = new CornerRadius(24), Background = Farbe.B("#0FFFFFFF"), BorderBrush = Farbe.B("#1AFFFFFF"), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, ToolTip = w2.Item2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                ToolTipService.SetInitialShowDelay(k, 200);
                System.Windows.Automation.AutomationProperties.SetName(k, w2.Item2);
                var ic = UI.Icon(w2.Item1, 21, Farbe.B("#B3FFFFFF"), 1.5);
                k.Child = ic;
                var tt = new TranslateTransform(Math.Cos(w) * r, Math.Sin(w) * r);
                var st = new ScaleTransform(1, 1);
                var tg = new TransformGroup(); tg.Children.Add(st); tg.Children.Add(tt);
                k.RenderTransform = tg;
                k.RenderTransformOrigin = new Point(0.5, 0.5);
                k.Tag = new Point(Math.Cos(w) * r, Math.Sin(w) * r);
                k.MouseEnter += delegate { k.Background = Farbe.B("#24FFFFFF"); k.BorderBrush = Farbe.B("#40FFFFFF"); };
                k.MouseLeave += delegate { k.Background = Farbe.B("#0FFFFFFF"); k.BorderBrush = Farbe.B("#1AFFFFFF"); };
                k.MouseLeftButtonUp += (s, a) => { a.Handled = true; w2.Item3(); };
                k.MouseLeftButtonDown += (s, a) => a.Handled = true;
                if (!bild) { k.Opacity = 0; st.ScaleX = st.ScaleY = 0.6; tt.X = tt.Y = 0; k.IsHitTestVisible = false; }
                knoepfe.Add(k);
                mitte.Children.Add(k);
            }
            Action<bool> auf = an =>
            {
                plus.BeginAnimation(OpacityProperty, new DoubleAnimation(an ? 0.15 : 1, TimeSpan.FromMilliseconds(180)));
                for (int i = 0; i < knoepfe.Count; i++)
                {
                    var k = knoepfe[i];
                    var ziel = (Point)k.Tag;
                    var tg = (TransformGroup)k.RenderTransform;
                    var st = (ScaleTransform)tg.Children[0];
                    var tt = (TranslateTransform)tg.Children[1];
                    var dauer = TimeSpan.FromMilliseconds(an ? 260 : 160);
                    var verz = TimeSpan.FromMilliseconds(an ? i * 22 : 0);
                    IEasingFunction ez = an ? (IEasingFunction)new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 } : UI.Weich;
                    tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(an ? ziel.X : 0, dauer) { BeginTime = verz, EasingFunction = ez });
                    tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(an ? ziel.Y : 0, dauer) { BeginTime = verz, EasingFunction = ez });
                    st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(an ? 1 : 0.6, dauer) { BeginTime = verz, EasingFunction = ez });
                    st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(an ? 1 : 0.6, dauer) { BeginTime = verz, EasingFunction = ez });
                    k.BeginAnimation(OpacityProperty, new DoubleAnimation(an ? 1 : 0, TimeSpan.FromMilliseconds(an ? 180 : 120)) { BeginTime = verz });
                    k.IsHitTestVisible = an;
                }
            };
            if (!bild)
            {
                mitte.MouseEnter += delegate { auf(true); };
                mitte.MouseLeave += delegate { auf(false); };
            }
            else plus.Opacity = 0.15;
            var g = new Grid { Background = Brushes.Transparent };
            g.Children.Add(mitte);
            return g;
        }

        public void Waehlen(BlattDef b, string art, string pfad, string name, string symbol)
        {
            b.Art = art; b.Pfad = pfad ?? ""; b.Name = name ?? ""; b.Symbol = symbol ?? "";
            Blaetter.Speichern();
            SchieneFuellen();
            Neuzeichnen();
            if (!bild && buehne.Children.Count > 0) UI.Einblenden(buehne.Children[0], 8, 220);
        }

        void OrdnerWaehlen(BlattDef b) { OrdnerWaehlen(b, "folder"); }

        void OrdnerWaehlen(BlattDef b, string art)
        {
            using (var d = new System.Windows.Forms.FolderBrowserDialog { ShowNewFolderButton = true })
            {
                if (!string.IsNullOrEmpty(b.Pfad) && Directory.Exists(b.Pfad)) d.SelectedPath = b.Pfad;
                if (d.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                string p = d.SelectedPath;
                Waehlen(b, art, p, System.IO.Path.GetFileName(p.TrimEnd('\\')), Paths.Gleich(p, Paths.Desktop) ? ReiterZeichen.Bildschirm : "");
            }
        }

        // ================= Ordner-Blätter =================

        readonly Dictionary<string, string> ordnerVon = new Dictionary<string, string>();
        readonly Dictionary<string, Stack<string>> zurueckVon = new Dictionary<string, Stack<string>>(), vorVon = new Dictionary<string, Stack<string>>();

        static Stack<string> Stapel(Dictionary<string, Stack<string>> d, BlattDef b)
        {
            Stack<string> s;
            if (!d.TryGetValue(b.Id, out s)) d[b.Id] = s = new Stack<string>();
            return s;
        }

        string WurzelVon(BlattDef b) { return Blaetter.Wurzel(b, wolke); }

        string AktOrdner(BlattDef b)
        {
            string w = WurzelVon(b);
            if (w == null) return null;
            string akt;
            ordnerVon.TryGetValue(b.Id, out akt);
            if (akt == null || !Directory.Exists(akt) || !akt.StartsWith(w, StringComparison.OrdinalIgnoreCase)) return w;
            return akt;
        }

        void Betreten(BlattDef b, string ordner)
        {
            string jetzt = AktOrdner(b);
            if (jetzt != null && !Paths.Gleich(jetzt, ordner)) { Stapel(zurueckVon, b).Push(jetzt); Stapel(vorVon, b).Clear(); }
            ordnerVon[b.Id] = ordner;
            Nutzung.Merken(ordner);
            auswahl = null;
            if (Filter.Length > 0) suche.Text = "";
            Neuzeichnen();
            if (!bild && buehne.Children.Count > 0) UI.Einblenden(buehne.Children[0], 6, 180);
        }

        FrameworkElement FlaecheOrdner(BlattDef b)
        {
            bool cloud = b.Art == "cloud";
            string w = WurzelVon(b), akt = AktOrdner(b);
            if (w == null || !Directory.Exists(w)) { OrtSetzen(Blaetter.NameVon(b, wolke)); return OrdnerFehlt(b); }

            // Brotkrumen klein oben links – nur Namen, klickbar
            ort.Children.Clear();
            var teile = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>(Blaetter.NameVon(b, wolke), w) };
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
                    tb.MouseLeftButtonUp += delegate { Betreten(b, ziel); };
                    AblageZiel(tb, null, () => ziel, () => Betreten(b, ziel), null);
                }
                ort.Children.Add(tb);
            }
            PfeileSetzen(b);

            var eintraege = new List<string>();
            try
            {
                foreach (var d in Directory.EnumerateDirectories(akt).OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)) if (!Engine.IstHilfsdatei(d) && Passt(System.IO.Path.GetFileName(d))) eintraege.Add(d);
                foreach (var f in Directory.EnumerateFiles(akt).OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)) if (!Engine.IstHilfsdatei(f) && Passt(System.IO.Path.GetFileName(f))) eintraege.Add(f);
            }
            catch (Exception ex) { Log.Fehler("Ordner lesen", ex); }

            var flaeche = new Grid { Background = Brushes.Transparent };
            Tisch tisch = null;
            if (eintraege.Count == 0) flaeche.Children.Add(Leer(ReiterZeichen.Fuer(b)));
            else
            {
                tisch = new Tisch(akt, eintraege.Take(800).ToList(),
                    p =>
                    {
                        var sym = Symbol(p, p, System.IO.Path.GetFileName(p), null, null, cloud, null);
                        if (Directory.Exists(p)) { string p2 = p; AblageZiel(sym, sym, () => p2, () => Betreten(b, p2), null); }
                        return sym;
                    },
                    p => Druecken(p),
                    p => { if (Directory.Exists(p)) Betreten(b, p); else Oeffnen(p); },
                    p => KontextOrdner(p, b));
                tisch.ZugBewegt = TischZug;
                tisch.Gruppe = Mit;
                tisch.Losgelassen = Loslassen;
                flaeche.Children.Add(tisch);
            }
            aktuellerTisch = tisch;
            var rahmen = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1.5), BorderBrush = Brushes.Transparent, Child = flaeche };
            AblageZiel(flaeche, rahmen, () => AktOrdner(b));
            flaeche.MouseRightButtonUp += (s, a) =>
            {
                if (a.Handled) return;
                var m = new List<KeyValuePair<string, Action>>
                {
                    new KeyValuePair<string, Action>("New folder", () => NeuerOrdner(b)),
                    new KeyValuePair<string, Action>("Open in Explorer", () => Oeffnen(AktOrdner(b))),
                };
                if (tisch != null) m.Add(new KeyValuePair<string, Action>("Tidy up", () => tisch.Aufraeumen()));
                UI.Menue(flaeche, m, true);
            };
            return rahmen;
        }

        /// <summary>Andere Ordner-Blätter als Ziel fürs Verschieben (höchstens drei).</summary>
        IEnumerable<BlattDef> AndereOrdner(BlattDef b)
        {
            return Blaetter.Liste.Where(x => x != b && Blaetter.IstOrdnerArt(x) && WurzelVon(x) != null && Directory.Exists(WurzelVon(x))).Take(3);
        }

        List<KeyValuePair<string, Action>> KontextOrdner(string pfad, BlattDef b)
        {
            bool ordner = Directory.Exists(pfad);
            var m = new List<KeyValuePair<string, Action>>
            {
                new KeyValuePair<string, Action>("Open", () => { if (ordner) Betreten(b, pfad); else Oeffnen(pfad); }),
                new KeyValuePair<string, Action>("Show in Explorer", () => ImOrdnerZeigen(pfad)),
                new KeyValuePair<string, Action>("Rename", () => { Border sb; if (symbole.TryGetValue(pfad, out sb)) Umbenennen(sb, pfad); }),
            };
            foreach (var o in AndereOrdner(b)) { var o2 = o; m.Add(new KeyValuePair<string, Action>("Move to " + Blaetter.NameVon(o2, wolke), () => Gemeinsam(Mit(pfad), -12, q => Verschieben(q, WurzelVon(o2))))); }
            m.Add(new KeyValuePair<string, Action>("-", null));
            m.Add(new KeyValuePair<string, Action>("Move to Recycle Bin", () => InKorb(pfad)));
            return m;
        }

        /// <summary>Schwebende Leiste für Ordner- und Board-Blätter.</summary>
        void LeisteOrdner()
        {
            string p = auswahl;
            if (p == null || !(File.Exists(p) || Directory.Exists(p))) return;
            var b = Akt;
            bool ordner = Directory.Exists(p), board = IstBoard;
            Border sb;
            if (gruppe.Count > 1) Anzahl();
            else LKnopf(Ikon.Oeffnen, "Open", Farbe.Text, () => { if (ordner && !board) Betreten(b, p); else Oeffnen(p); });
            if (!board && gruppe.Count == 1) LKnopf(Ikon.Stift, "Rename", Farbe.Text, () => { if (symbole.TryGetValue(p, out sb)) Umbenennen(sb, p); });
            if (!board)
                foreach (var o in AndereOrdner(b)) { var o2 = o; LKnopf(ReiterZeichen.Fuer(o2), "Move to " + Blaetter.NameVon(o2, wolke), Farbe.Text, () => Gemeinsam(Mit(p), -12, q => Verschieben(q, WurzelVon(o2)))); }
            Trenner();
            if (board) LKnopf(Ikon.X, "Remove from board", Farbe.Text, () => VomBoard(Mit(p)));
            else LKnopf(Ikon.Muell, "Move to Recycle Bin", Farbe.Gefahr, () => InKorb(p));
        }

        /// <summary>Ordner fehlt (Cloud nicht gefunden, Ordner gelöscht): ein leises Zeichen und „Choose folder“.</summary>
        FrameworkElement OrdnerFehlt(BlattDef b)
        {
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 40) };
            var ic = UI.Icon(ReiterZeichen.Fuer(b), 44, Farbe.B("#59FFFFFF"), 1.3);
            ic.HorizontalAlignment = HorizontalAlignment.Center;
            sp.Children.Add(ic);
            var k = UI.Knopf("Choose folder", null, "Btn");
            k.HorizontalAlignment = HorizontalAlignment.Center;
            k.Margin = new Thickness(0, 16, 0, 0);
            if (b.Art == "cloud") k.ToolTip = "Google Drive, OneDrive and Dropbox are found automatically";
            k.Click += delegate { OrdnerWaehlen(b, b.Art); };
            sp.Children.Add(k);
            return sp;
        }

        /// <summary>Einstellungen › Cloud „…“: einen beliebigen Ordner als Cloud wählen.</summary>
        void WolkeWaehlen()
        {
            using (var d = new System.Windows.Forms.FolderBrowserDialog { ShowNewFolderButton = true })
            {
                if (wolke != null) d.SelectedPath = wolke.Pfad;
                if (d.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                WolkeSetzen(new CloudOrt { Name = System.IO.Path.GetFileName(d.SelectedPath.TrimEnd('\\')), Pfad = d.SelectedPath });
            }
        }

        void WolkeSetzen(CloudOrt o)
        {
            wolke = o;
            foreach (var b in Blaetter.Liste.Where(x => x.Art == "cloud")) { ordnerVon.Remove(b.Id); b.Pfad = ""; }
            Blaetter.Speichern();
            E.Cfg.CloudPfad = o == null ? "" : o.Pfad;
            E.Cfg.CloudName = o == null ? "" : o.Name;
            E.Cfg.Speichern();
            SchieneFuellen();
            Neuzeichnen();
        }

        void NeuerOrdner(BlattDef b)
        {
            string p = Paths.FreierName(AktOrdner(b), "New folder");
            try { Directory.CreateDirectory(p); } catch (Exception ex) { Log.Fehler("New folder", ex); return; }
            if (Filter.Length > 0) suche.Text = "";
            Neuzeichnen();
            Dispatcher.BeginInvoke(new Action(() => { Border sb; if (symbole.TryGetValue(p, out sb)) { Waehle(p); Umbenennen(sb, p); } }), DispatcherPriority.Loaded);
        }

        // ---------- Ordner-Verlauf ----------

        void Zurueck()
        {
            var b = Akt;
            if (!ImOrdner || Stapel(zurueckVon, b).Count == 0) return;
            Stapel(vorVon, b).Push(AktOrdner(b));
            Gehen(b, Stapel(zurueckVon, b).Pop(), -1);
        }

        void Vor()
        {
            var b = Akt;
            if (!ImOrdner || Stapel(vorVon, b).Count == 0) return;
            Stapel(zurueckVon, b).Push(AktOrdner(b));
            Gehen(b, Stapel(vorVon, b).Pop(), 1);
        }

        void Gehen(BlattDef b, string ordner, int richtung)
        {
            ordnerVon[b.Id] = ordner;
            auswahl = null;
            if (Filter.Length > 0) suche.Text = "";
            Neuzeichnen();
            if (!bild && buehne.Children.Count > 0)
            {
                var el = buehne.Children[0];
                var tt = new TranslateTransform();
                el.RenderTransform = tt;
                tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(richtung * 36, 0, TimeSpan.FromMilliseconds(240)) { EasingFunction = UI.Weich });
                el.BeginAnimation(OpacityProperty, new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(200)));
            }
        }

        /// <summary>Graue ‹ › vor dem Ordnernamen – nur sichtbar, sobald man in einem Ordner unterwegs ist.</summary>
        void PfeileSetzen(BlattDef b)
        {
            bool zur = Stapel(zurueckVon, b).Count > 0, vor = Stapel(vorVon, b).Count > 0;
            if (!zur && !vor) return;
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(Pfeil(Ikon.Links, "Back", zur, Zurueck));
            sp.Children.Add(Pfeil(Ikon.Rechts, "Forward", vor, Vor));
            ort.Children.Insert(0, sp);
        }

        // ---------- Netz: alle Ordner-Blätter ----------

        FrameworkElement FlaecheNetz()
        {
            OrtSetzen("Graph");
            var wurzeln = new List<CloudOrt>();
            foreach (var b in Blaetter.Liste.Where(Blaetter.IstOrdnerArt))
            {
                string w = WurzelVon(b);
                if (w != null && Directory.Exists(w) && !wurzeln.Any(x => Paths.Gleich(x.Pfad, w))) wurzeln.Add(new CloudOrt { Name = Blaetter.NameVon(b, wolke), Pfad = w, Fern = b.Art == "cloud" });
            }
            return netz = new Netz(wurzeln, (pfad, fern) =>
            {
                int i = AnsichtFuer(pfad);
                if (!IstBlatt(i)) return;
                var b = Blaetter.Liste[i];
                string jetzt = AktOrdner(b);
                if (jetzt != null && !Paths.Gleich(jetzt, pfad)) { Stapel(zurueckVon, b).Push(jetzt); Stapel(vorVon, b).Clear(); }
                ordnerVon[b.Id] = pfad;
                Zeige(i);
            });
        }

        // ================= Board: sammelt Verweise, verschiebt nichts =================

        FrameworkElement FlaecheBoard(BlattDef b)
        {
            OrtSetzen(Blaetter.NameVon(b, wolke));
            var namen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var eintraege = b.Inhalt.Where(p => (File.Exists(p) || Directory.Exists(p)) && Passt(System.IO.Path.GetFileName(p)) && namen.Add(System.IO.Path.GetFileName(p))).ToList();
            var flaeche = new Grid { Background = Brushes.Transparent };
            Tisch tisch = null;
            if (eintraege.Count == 0) flaeche.Children.Add(Leer(ReiterZeichen.Board));
            else
            {
                tisch = new Tisch("board:" + b.Id, eintraege,
                    p => Symbol(p, p, System.IO.Path.GetFileName(p), null, null, false, p),
                    p => Druecken(p),
                    p => Oeffnen(p),
                    p => new List<KeyValuePair<string, Action>>
                    {
                        new KeyValuePair<string, Action>("Open", () => Oeffnen(p)),
                        new KeyValuePair<string, Action>("Show in Explorer", () => ImOrdnerZeigen(p)),
                        new KeyValuePair<string, Action>("-", null),
                        new KeyValuePair<string, Action>("Remove from board", () => VomBoard(Mit(p))),
                    });
                tisch.ZugBewegt = TischZug;
                tisch.Gruppe = Mit;
                tisch.Losgelassen = Loslassen;
                flaeche.Children.Add(tisch);
            }
            aktuellerTisch = tisch;
            var rahmen = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1.5), BorderBrush = Brushes.Transparent, Child = flaeche };
            BoardZiel(flaeche, rahmen, b, null, null);
            return rahmen;
        }

        /// <summary>Was hier abgelegt wird, kommt als Verweis aufs Board – die Datei bleibt, wo sie ist.</summary>
        void BoardZiel(FrameworkElement el, Border rahmen, BlattDef b, Action federn, Action<bool> hover)
        {
            el.AllowDrop = true;
            DispatcherTimer feder = null;
            Brush alt = rahmen != null ? rahmen.BorderBrush : null;
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
            el.DragOver += (s, a) => { a.Effects = a.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None; a.Handled = true; };
            el.Drop += (s, a) =>
            {
                aus();
                a.Handled = true;
                var pfade = a.Data.GetData(DataFormats.FileDrop) as string[];
                if (pfade == null) return;
                BoardHinzu(b, pfade);
                a.Effects = DragDropEffects.Link;
            };
        }

        public void BoardHinzu(BlattDef b, IEnumerable<string> pfade)
        {
            foreach (var p in pfade) if (!b.Inhalt.Any(x => Paths.Gleich(x, p))) b.Inhalt.Add(p);
            Blaetter.Speichern();
            if (Akt == b) Neuzeichnen();
        }

        void VomBoard(string[] pfade)
        {
            var b = Akt;
            if (b == null || b.Art != "board") return;
            b.Inhalt.RemoveAll(x => pfade.Any(p => Paths.Gleich(p, x)));
            Blaetter.Speichern();
            auswahl = null;
            Neuzeichnen();
        }

        // ================= Leisten: Recent und Ablage, andockbar oben/unten/rechts =================

        static readonly string[] LeistenArten = { "recent", "shelf" };
        static string LeisteZeichen(string art) { return art == "recent" ? Ikon.Uhr : ReiterZeichen.Ablage; }
        static string LeisteName(string art) { return art == "recent" ? "Recent" : "Shelf"; }

        FrameworkElement MitLeisten(BlattDef b, FrameworkElement inhalt)
        {
            var g = new Grid();
            var dock = new DockPanel { LastChildFill = true };
            var marke = new Border { CornerRadius = new CornerRadius(2), Background = Farbe.B("#59FFFFFF"), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
            Action<string> kanteZeigen = k =>
            {
                if (k == null) { marke.Visibility = Visibility.Collapsed; return; }
                marke.Visibility = Visibility.Visible;
                marke.Width = k == "right" ? 3 : double.NaN; marke.Height = k == "right" ? double.NaN : 3;
                marke.HorizontalAlignment = k == "right" ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
                marke.VerticalAlignment = k == "top" ? VerticalAlignment.Top : k == "bottom" ? VerticalAlignment.Bottom : VerticalAlignment.Stretch;
                marke.Margin = k == "right" ? new Thickness(0, 40, -8, 40) : new Thickness(80, k == "top" ? -8 : 0, 80, k == "bottom" ? -8 : 0);
            };
            foreach (var k in Blaetter.Kanten)
            {
                var art = Blaetter.LeisteBei(b, k);
                if (art == null) continue;
                var el = LeisteBauen(b, art, k, g, kanteZeigen);
                DockPanel.SetDock(el, k == "top" ? Dock.Top : k == "bottom" ? Dock.Bottom : Dock.Right);
                dock.Children.Add(el);
            }
            dock.Children.Add(inhalt);
            g.Children.Add(dock);
            g.Children.Add(marke);

            // Andocken: an freien Kanten erscheint ein kleines +, wenn die Maus in die Nähe kommt
            var frei = LeistenArten.Where(a => !b.Leisten.Any(l => l.StartsWith(a + "@"))).ToList();
            if (frei.Count > 0 && !bild)
            {
                var plusse = new List<Tuple<string, Button>>();
                foreach (var k in Blaetter.Kanten)
                {
                    if (Blaetter.LeisteBei(b, k) != null) continue;
                    string k2 = k;
                    var pk = new Button { Style = (Style)Application.Current.FindResource("Rund"), Width = 24, Height = 24, Content = UI.Icon(Ikon.Plus, 11, Farbe.B("#99FFFFFF"), 2), Opacity = 0, ToolTip = "Add a bar" };
                    ToolTipService.SetInitialShowDelay(pk, 400);
                    pk.HorizontalAlignment = k == "right" ? HorizontalAlignment.Right : HorizontalAlignment.Center;
                    pk.VerticalAlignment = k == "top" ? VerticalAlignment.Top : k == "bottom" ? VerticalAlignment.Bottom : VerticalAlignment.Center;
                    pk.Margin = k == "top" ? new Thickness(0, -10, 0, 0) : k == "bottom" ? new Thickness(0, 0, 0, -10) : new Thickness(0, 0, -10, 0);
                    pk.Click += delegate { LeisteWaehlen(pk, b, k2); };
                    plusse.Add(Tuple.Create(k, pk));
                    g.Children.Add(pk);
                }
                g.PreviewMouseMove += (s, a) =>
                {
                    if (a.LeftButton == MouseButtonState.Pressed) return;
                    var p = a.GetPosition(g);
                    foreach (var t in plusse)
                    {
                        double d = t.Item1 == "top" ? p.Y : t.Item1 == "bottom" ? g.ActualHeight - p.Y : g.ActualWidth - p.X;
                        bool nah = d < 30 && (t.Item1 == "right" ? Math.Abs(p.Y - g.ActualHeight / 2) < 160 : Math.Abs(p.X - g.ActualWidth / 2) < 220);
                        double ziel = nah ? 1 : 0;
                        if ((t.Item2.Tag as string) == (nah ? "an" : "aus")) continue;
                        t.Item2.Tag = nah ? "an" : "aus";
                        t.Item2.BeginAnimation(OpacityProperty, new DoubleAnimation(ziel, TimeSpan.FromMilliseconds(nah ? 140 : 220)));
                    }
                };
                g.MouseLeave += delegate { foreach (var t in plusse) { t.Item2.Tag = "aus"; t.Item2.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(220))); } };
            }
            return g;
        }

        void LeisteWaehlen(FrameworkElement ziel, BlattDef b, string kante)
        {
            var frei = LeistenArten.Where(a => !b.Leisten.Any(l => l.StartsWith(a + "@"))).ToList();
            if (frei.Count == 1) { Blaetter.LeisteSetzen(b, frei[0], kante); Neuzeichnen(); return; }
            var pop = new Popup { PlacementTarget = ziel, Placement = kante == "bottom" ? PlacementMode.Top : kante == "right" ? PlacementMode.Left : PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var art in frei)
            {
                string a2 = art;
                var k = new Button { Style = (Style)Application.Current.FindResource("Rund"), Width = 38, Height = 38, Content = UI.Icon(LeisteZeichen(art), 17, Farbe.Text, 1.6), ToolTip = LeisteName(art), Margin = new Thickness(2) };
                k.Click += delegate { pop.IsOpen = false; Blaetter.LeisteSetzen(b, a2, kante); Neuzeichnen(); };
                sp.Children.Add(k);
            }
            pop.Child = new Border { Background = Farbe.B("#F5191C23"), BorderBrush = Farbe.B("#1FFFFFFF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(22), Padding = new Thickness(4), Margin = new Thickness(6), Child = sp };
            pop.IsOpen = true;
        }

        /// <summary>
        /// Eine Leiste: links (oben) ein leises Zeichen als Griff, daneben Symbole ohne Text. Läuft zum Ende hin ins Transparente;
        /// beim Darüberfahren wird sie ganz sichtbar, Mausrad/zwei Finger rollen sie. Griff ziehen = an andere Kante oder heraus = weg.
        /// </summary>
        FrameworkElement LeisteBauen(BlattDef b, string art, string kante, FrameworkElement feld, Action<string> kanteZeigen)
        {
            bool quer = kante != "right";
            var rahmen = new Border
            {
                CornerRadius = new CornerRadius(12), Background = Farbe.B("#08FFFFFF"),
                Margin = quer ? new Thickness(0, kante == "top" ? 0 : 8, 0, kante == "top" ? 8 : 0) : new Thickness(8, 0, 0, 0),
                Height = quer ? 52 : double.NaN, Width = quer ? double.NaN : 52, Tag = "leiste:" + art,
                HorizontalAlignment = quer ? HorizontalAlignment.Left : HorizontalAlignment.Stretch, VerticalAlignment = quer ? VerticalAlignment.Stretch : VerticalAlignment.Top
            };
            var dp = new DockPanel();
            var griff = new Border { Width = quer ? 34 : 52, Height = quer ? 52 : 34, Background = Brushes.Transparent, Cursor = Cursors.SizeAll, ToolTip = LeisteName(art) };
            ToolTipService.SetInitialShowDelay(griff, 500);
            var gz = UI.Icon(LeisteZeichen(art), 14, Farbe.B("#4DFFFFFF"), 1.6);
            gz.HorizontalAlignment = HorizontalAlignment.Center; gz.VerticalAlignment = VerticalAlignment.Center;
            griff.Child = gz;
            DockPanel.SetDock(griff, quer ? Dock.Left : Dock.Top);
            dp.Children.Add(griff);

            var reihe = new StackPanel { Orientation = quer ? Orientation.Horizontal : Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = quer ? HorizontalAlignment.Left : HorizontalAlignment.Center };
            var sv = new ScrollViewer { Content = reihe, HorizontalScrollBarVisibility = quer ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = quer ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Hidden, Focusable = false, Tag = "leiste" };
            var stop = new GradientStop(Colors.Black, 0.5);
            var maske = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = quer ? new Point(1, 0) : new Point(0, 1) };
            maske.GradientStops.Add(new GradientStop(Colors.Black, 0));
            maske.GradientStops.Add(stop);
            maske.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
            sv.OpacityMask = maske;
            dp.Children.Add(sv);
            rahmen.Child = dp;
            rahmen.MouseEnter += delegate { stop.BeginAnimation(GradientStop.OffsetProperty, new DoubleAnimation(0.96, TimeSpan.FromMilliseconds(220)) { EasingFunction = UI.Weich }); };
            rahmen.MouseLeave += delegate { stop.BeginAnimation(GradientStop.OffsetProperty, new DoubleAnimation(0.5, TimeSpan.FromMilliseconds(320)) { EasingFunction = UI.Weich }); };
            rahmen.MouseLeftButtonDown += (s, a) => a.Handled = true;    // kein Rahmen-Aufziehen über der Leiste

            Action fuellen = () =>
            {
                reihe.Children.Clear();
                var liste = art == "recent" ? Zuletzt.Liste(E).Take(30).ToList() : Blaetter.D.Ablage.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
                foreach (var p in liste) reihe.Children.Add(Kachel(p, art));
                if (art == "shelf" && liste.Count == 0)
                {
                    var slot = new Border { Width = quer ? 72 : 38, Height = quer ? 38 : 72, CornerRadius = new CornerRadius(9), BorderBrush = Farbe.B("#24FFFFFF"), BorderThickness = new Thickness(1), Margin = new Thickness(4), ToolTip = "Drop files here to park them" };
                    slot.Child = new System.Windows.Shapes.Rectangle { StrokeDashArray = new DoubleCollection { 3, 3 }, Stroke = Farbe.B("#1AFFFFFF"), RadiusX = 8, RadiusY = 8 };
                    slot.BorderThickness = new Thickness(0);
                    reihe.Children.Add(slot);
                }
            };
            fuellen();
            if (art == "recent") recentFuellen = () => { if (rahmen.IsLoaded || bild) fuellen(); };
            if (art == "shelf")
            {
                rahmen.AllowDrop = true;
                rahmen.DragOver += (s, a) => { a.Effects = a.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None; a.Handled = true; };
                rahmen.DragEnter += delegate { rahmen.Background = Farbe.B("#14FFFFFF"); };
                rahmen.DragLeave += delegate { rahmen.Background = Farbe.B("#08FFFFFF"); };
                rahmen.Drop += (s, a) =>
                {
                    a.Handled = true;
                    rahmen.Background = Farbe.B("#08FFFFFF");
                    var pfade = a.Data.GetData(DataFormats.FileDrop) as string[];
                    if (pfade == null) return;
                    Blaetter.Parken(pfade);
                    a.Effects = DragDropEffects.Link;
                    fuellen();
                };
                ablageFuellen = fuellen;
            }

            // Griff ziehen: an eine andere Kante oder heraus (in die Mitte) = Leiste weg
            bool zieht = false;
            string ziel = kante;
            griff.MouseLeftButtonDown += (s, a) => { a.Handled = true; zieht = true; ziel = kante; griff.CaptureMouse(); };
            griff.MouseMove += (s, a) =>
            {
                if (!zieht) return;
                var p = a.GetPosition(feld);
                double w = feld.ActualWidth, h = feld.ActualHeight;
                double dTop = p.Y, dBot = h - p.Y, dRight = w - p.X;
                double min = Math.Min(dTop, Math.Min(dBot, dRight));
                ziel = min > Math.Min(w, h) * 0.22 ? null : (min == dTop ? "top" : min == dBot ? "bottom" : "right");
                rahmen.Opacity = ziel == null ? 0.35 : 0.85;
                kanteZeigen(ziel != null && ziel != kante ? ziel : null);
            };
            griff.MouseLeftButtonUp += (s, a) =>
            {
                if (!zieht) return;
                zieht = false;
                griff.ReleaseMouseCapture();
                kanteZeigen(null);
                rahmen.Opacity = 1;
                if (ziel == null) { Blaetter.LeisteWeg(b, art); Neuzeichnen(); return; }
                if (ziel == kante) return;
                string dort = Blaetter.LeisteBei(b, ziel);
                Blaetter.LeisteSetzen(b, art, ziel);
                if (dort != null) Blaetter.LeisteSetzen(b, dort, kante);     // Plätze tauschen
                Neuzeichnen();
            };
            return rahmen;
        }

        Action recentFuellen, ablageFuellen;
        void ZuletztNeu() { if (recentFuellen != null) recentFuellen(); }

        /// <summary>Ein Symbol in einer Leiste: nur Bild, Name im Tooltip. Klick öffnet, Ziehen nimmt die Datei mit.</summary>
        FrameworkElement Kachel(string p, string art)
        {
            var k = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(9), Margin = new Thickness(2), Background = Brushes.Transparent, ToolTip = System.IO.Path.GetFileName(p), Cursor = Cursors.Hand };
            ToolTipService.SetInitialShowDelay(k, 350);
            var bildEl = Vorschau.Bild(p, 30, 30, Blaetter.Liste.Any(x => x.Art == "cloud" && WurzelVon(x) != null && p.StartsWith(WurzelVon(x), StringComparison.OrdinalIgnoreCase)), null, false);
            bildEl.HorizontalAlignment = HorizontalAlignment.Center; bildEl.VerticalAlignment = VerticalAlignment.Center;
            k.Child = bildEl;
            k.MouseEnter += delegate { k.Background = Farbe.B("#1AFFFFFF"); };
            k.MouseLeave += delegate { k.Background = Brushes.Transparent; };
            Point start = new Point();
            bool druck = false;
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
                var m = new List<KeyValuePair<string, Action>>
                {
                    new KeyValuePair<string, Action>("Open", () => Oeffnen(p)),
                    new KeyValuePair<string, Action>("Show in Explorer", () => ImOrdnerZeigen(p)),
                };
                if (art == "shelf") { m.Add(new KeyValuePair<string, Action>("-", null)); m.Add(new KeyValuePair<string, Action>("Remove", () => { Blaetter.D.Ablage.RemoveAll(x => Paths.Gleich(x, p)); Blaetter.Speichern(); if (ablageFuellen != null) ablageFuellen(); })); }
                UI.Menue(k, m, true);
            };
            return k;
        }

        /// <summary>Mausrad / zwei Finger über einer Leiste rollen die Leiste statt das Blatt zu wechseln.</summary>
        bool LeisteRollen(int delta, bool seitlich)
        {
            DependencyObject el = Mouse.DirectlyOver as DependencyObject;
            while (el != null)
            {
                var sv = el as ScrollViewer;
                if (sv != null && "leiste".Equals(sv.Tag))
                {
                    if (sv.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled) sv.ScrollToHorizontalOffset(sv.HorizontalOffset + (seitlich ? delta : -delta) * 0.5);
                    else sv.ScrollToVerticalOffset(sv.VerticalOffset - delta * 0.5);
                    return true;
                }
                el = el is Visual || el is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(el) : LogicalTreeHelper.GetParent(el);
            }
            return false;
        }
    }
}
