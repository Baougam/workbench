using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Workbench
{
    /// <summary>
    /// Gefühlt null Verzögerung: fertig gebaute Ansichten bleiben kurz im Speicher (höchstens 8). Zurück auf ein Blatt,
    /// eine Ebene oder Seite = die fertige Ansicht wird nur eingesetzt. In Ruhe (ApplicationIdle, einmal nach jedem Wechsel)
    /// werden die Nachbarn vorgebaut: Blatt davor/danach, nächste Ebene, nächste Seite.
    /// Gültig, solange sich nichts geändert hat: Daten-Stand, Änderungszeit des Ordners bzw. gelesener Tiefen-Stand, höchstens 60 s.
    /// </summary>
    partial class Hauptfenster
    {
        class Gebaut
        {
            public FrameworkElement El;
            public UIElement[] Ort;
            public Dictionary<string, Border> Sym;
            public Tisch Tisch;
            public FrameworkElement TiefAnz;
            public int Seiten, Spalten;
            public DateTime Zeit, Geaendert;
            public long Stand;
            public object Tief;
        }

        readonly Dictionary<string, Gebaut> gebaut = new Dictionary<string, Gebaut>();
        readonly List<string> gebautReihe = new List<string>();
        long datenStand;
        string angezeigt;
        DispatcherTimer vorbauUhr;
        public bool VorbauImTest;
        bool VorbauAn { get { return !bild || VorbauImTest; } }
        public object AnsichtJetzt { get { return buehne.Children.Count > 0 ? buehne.Children[0] : null; } }

        string AnsichtSchluessel()
        {
            if (!IstBlatt(ansicht) || homeBearbeiten || Filter.Length > 0) return null;
            var b = Akt;
            if (b == null) return null;
            string akt = ImOrdner ? AktOrdner(b) : "";
            int s; seiteVon.TryGetValue(b.Id, out s);
            string o; offenVon.TryGetValue(b.Id, out o);
            return ansicht + "|" + b.Id + "|" + akt + "|" + Tiefe(b) + "|" + s + "|" + o + "|" + (int)(buehne.ActualWidth / 8) + "x" + (int)(buehne.ActualHeight / 8);
        }

        void Pruefstand(out DateTime geaendert, out object tief)
        {
            geaendert = DateTime.MinValue; tief = null;
            var b = Akt;
            if (b == null || !ImOrdner) return;
            string akt = AktOrdner(b);
            int t = Tiefe(b);
            if (t > 0) { TStand st; tiefeCache.TryGetValue(akt + "|" + t, out st); tief = st; }
            try { geaendert = Directory.GetLastWriteTimeUtc(akt); } catch { }
        }

        /// <summary>Statt Flaeche(): fertige Ansicht nehmen, wenn sie noch stimmt – sonst bauen und merken.</summary>
        FrameworkElement FlaecheGemerkt()
        {
            string key = VorbauAn ? AnsichtSchluessel() : null;
            // was eben markiert war, im alten Bild zurücksetzen (es könnte später wieder eingesetzt werden)
            foreach (var p in gruppe) { Border bo; if (symbole.TryGetValue(p, out bo)) Stil(bo, false, false); }
            if (key != null && key != angezeigt)
            {
                Gebaut g;
                if (gebaut.TryGetValue(key, out g) && Gueltig(g)) { Einsetzen(g); angezeigt = key; Vorn(key); return g.El; }
            }
            symbole.Clear();
            var v = Flaeche();
            angezeigt = key;
            if (key != null) Merken(key, v);
            return v;
        }

        bool Gueltig(Gebaut g)
        {
            if (g.Stand != datenStand || (DateTime.UtcNow - g.Zeit).TotalSeconds > 60) return false;
            DateTime ge; object tief;
            Pruefstand(out ge, out tief);
            return ge == g.Geaendert && ReferenceEquals(tief, g.Tief);
        }

        void Merken(string key, FrameworkElement v)
        {
            DateTime ge; object tief;
            Pruefstand(out ge, out tief);
            gebaut[key] = new Gebaut
            {
                El = v, Ort = ort.Children.Cast<UIElement>().ToArray(), Sym = new Dictionary<string, Border>(symbole, StringComparer.OrdinalIgnoreCase),
                Tisch = aktuellerTisch, TiefAnz = tiefenAnzeige, Seiten = seitenAnzahl, Spalten = spaltenJetzt,
                Zeit = DateTime.UtcNow, Geaendert = ge, Stand = datenStand, Tief = tief
            };
            Vorn(key);
            while (gebautReihe.Count > 8) { gebaut.Remove(gebautReihe[0]); gebautReihe.RemoveAt(0); }
        }

        void Vorn(string key) { gebautReihe.Remove(key); gebautReihe.Add(key); }

        void Einsetzen(Gebaut g)
        {
            var eltern = VisualTreeHelper.GetParent(g.El) as Panel;
            if (eltern != null) eltern.Children.Remove(g.El);
            g.El.BeginAnimation(OpacityProperty, null); g.El.Opacity = 1;
            g.El.RenderTransform = Transform.Identity;
            ort.Children.Clear();
            foreach (var e in g.Ort)
            {
                var p = VisualTreeHelper.GetParent(e) as Panel;
                if (p != null) p.Children.Remove(e);
                ort.Children.Add(e);
            }
            symbole.Clear();
            foreach (var kv in g.Sym) symbole[kv.Key] = kv.Value;
            aktuellerTisch = g.Tisch; tiefenAnzeige = g.TiefAnz; seitenAnzahl = g.Seiten; spaltenJetzt = g.Spalten;
            letzteBoxen.Clear();
        }

        /// <summary>Etwas hat sich geändert (Dateien, Fristen …): alles Vorgebaute verwerfen.</summary>
        void VorbauVergessen() { datenStand++; gebaut.Clear(); gebautReihe.Clear(); angezeigt = null; }

        /// <summary>Nach einem Wechsel einmal in Ruhe die Nachbarn vorbauen. Nie, solange das Fenster versteckt ist.</summary>
        void VorbauPlanen()
        {
            if (bild || !IsVisible) return;
            if (vorbauUhr == null)
            {
                vorbauUhr = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromMilliseconds(350) };
                vorbauUhr.Tick += delegate { vorbauUhr.Stop(); Nachbarn(); };
            }
            vorbauUhr.Stop(); vorbauUhr.Start();
        }

        public void Nachbarn()
        {
            if (!IstBlatt(ansicht) || !VorbauAn) return;
            int hier = ansicht;
            var b = Akt;
            // nächste Ebene (wenn schon gelesen) und nächste Seite
            if (b != null && ImOrdner)
            {
                int t = Tiefe(b), max;
                if (!tiefeMax.TryGetValue(b.Id, out max)) max = 0;
                TStand st;
                if (t < max && tiefeCache.TryGetValue(AktOrdner(b) + "|" + (t + 1), out st) && st != null)
                    Vorbauen(() => { tiefeVon[b.Id] = t + 1; offenVon.Remove(b.Id); seiteVon.Remove(b.Id); });
                if (t > 0)
                {
                    int s; seiteVon.TryGetValue(b.Id, out s);
                    if (s + 1 < seitenAnzahl) Vorbauen(() => { seiteVon[b.Id] = s + 1; });
                }
            }
            // Blatt davor und danach (beim Verlassen springt ein Blatt auf seine Oberfläche – also die bauen)
            foreach (int n in new[] { hier + 1, hier - 1 })
                if (IstBlatt(n)) Vorbauen(() => { ansicht = n; var nb = Blaetter.Liste[n]; tiefeVon.Remove(nb.Id); offenVon.Remove(nb.Id); seiteVon.Remove(nb.Id); });
        }

        /// <summary>Unsichtbar bauen: Zustand kurz umstellen, bauen, merken, alles exakt zurücksetzen.</summary>
        void Vorbauen(Action umstellen)
        {
            int a0 = ansicht;
            var b0 = Akt;
            var tv = new Dictionary<string, int>(tiefeVon); var ov = new Dictionary<string, string>(offenVon); var sv = new Dictionary<string, int>(seiteVon);
            var ort0 = ort.Children.Cast<UIElement>().ToArray();
            var sym0 = new Dictionary<string, Border>(symbole, StringComparer.OrdinalIgnoreCase);
            var tisch0 = aktuellerTisch; var anz0 = tiefenAnzeige; int seiten0 = seitenAnzahl, spalten0 = spaltenJetzt;
            var boxen0 = new Dictionary<string, Rect>(letzteBoxen, StringComparer.OrdinalIgnoreCase);
            try
            {
                umstellen();
                letzteBoxen.Clear();
                string key = AnsichtSchluessel();
                Gebaut g;
                if (key != null && key != angezeigt && !(gebaut.TryGetValue(key, out g) && Gueltig(g)))
                {
                    symbole.Clear();
                    var v = Flaeche();
                    Merken(key, v);
                }
            }
            catch (Exception ex) { Log.Fehler("Vorbauen", ex); }
            finally
            {
                ansicht = a0;
                Kopieren(tv, tiefeVon); Kopieren(ov, offenVon); Kopieren(sv, seiteVon);
                ort.Children.Clear();
                foreach (var e in ort0) { var p = VisualTreeHelper.GetParent(e) as Panel; if (p != null) p.Children.Remove(e); ort.Children.Add(e); }
                symbole.Clear(); foreach (var kv in sym0) symbole[kv.Key] = kv.Value;
                aktuellerTisch = tisch0; tiefenAnzeige = anz0; seitenAnzahl = seiten0; spaltenJetzt = spalten0;
                letzteBoxen.Clear(); foreach (var kv in boxen0) letzteBoxen[kv.Key] = kv.Value;
            }
        }

        static void Kopieren<T>(Dictionary<string, T> von, Dictionary<string, T> nach) { nach.Clear(); foreach (var kv in von) nach[kv.Key] = kv.Value; }
    }
}
