using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Workbench
{
    /// <summary>
    /// Ein Blatt im Stapel. Art: „temp“ (Expiring, gibt es immer genau einmal), „folder“ (ein Ordner als Fläche – auch Desktop,
    /// Screenshots), „board“ (Sammlung von Verweisen, verschiebt nichts), „cloud“ (Cloud-Ordner, ohne Vorschaubilder) oder
    /// „empty“ (noch nicht gewählt: großes leises +).
    /// </summary>
    public class BlattDef
    {
        public string Id = Guid.NewGuid().ToString("N").Substring(0, 8);
        public string Art = "empty";
        public string Pfad = "";
        public string Name = "";
        public string Symbol = "";                          // Ikon für das Blatt in der Schiene (leer = nach Art)
        public List<string> Leisten = new List<string>();   // „recent@top“, „shelf@bottom“ …
        public List<string> Inhalt = new List<string>();    // nur Board: Pfade
        public Dictionary<string, int> LeistenGroesse = new Dictionary<string, int>();   // Leiste → 0 S, 1 M, 2 L
        public List<Modul> Module = new List<Modul>();      // nur Home: Bausteine auf dem Raster
        public bool Vorschlag;                              // Home: Vorlagen-Auswahl unten zeigen (bis „behalten“ oder gewählt)
    }

    /// <summary>Ein Baustein auf Home: recent, shelf, expiring, downloads, screens, folder (Pfad), note (Text).</summary>
    public class Modul
    {
        public string Id = Guid.NewGuid().ToString("N").Substring(0, 8);
        public string Art = "";
        public string Pfad = "";
        public string Text = "";
        public bool Breit;                                  // alt (vor dem Raster)
        public int X = -1, Y = -1, W, H;                    // Platz auf dem Raster (Zellen)
        public int Groesse = 1;                             // Symbole: 0 S, 1 M, 2 L
        public bool Liste;                                  // Zeilen statt Kacheln
        public string Filter = "";                          // "", docs, images, archives, apps
        public string Titel = "";
        public List<string> Inhalt = new List<string>();    // pinned: Pfade
    }

    public class BlaetterDaten
    {
        public List<BlattDef> Liste = new List<BlattDef>();
        public List<string> Ablage = new List<string>();    // geparkte Dateien (Verweise, für alle Blätter gleich)
        public int Version;                                 // 2 = Mitte ist das eigene Blatt (leeres Permanent wurde zum +)
    }

    /// <summary>Die Blätter des Nutzers: %LOCALAPPDATA%\Workbench\blaetter.json.</summary>
    static class Blaetter
    {
        public const int Max = 6;
        public static readonly string[] Kanten = { "top", "bottom", "right" };
        public static BlaetterDaten D;
        /// <summary>Diese Sitzung neu angelegt (erster Start): dann startet Workbench auf dem leeren Blatt.</summary>
        public static bool Frisch;

        static string Datei { get { return Path.Combine(Paths.Daten, "blaetter.json"); } }

        public static List<BlattDef> Liste { get { if (D == null) Laden(); return D.Liste; } }

        public static void Laden()
        {
            try
            {
                if (File.Exists(Datei))
                {
                    D = Store.Json().Deserialize<BlaetterDaten>(File.ReadAllText(Datei));
                    if (D != null && D.Liste != null && D.Liste.Count > 0)
                    {
                        if (D.Ablage == null) D.Ablage = new List<string>();
                        foreach (var b in D.Liste) { if (b.Leisten == null) b.Leisten = new List<string>(); if (b.Inhalt == null) b.Inhalt = new List<string>(); if (b.Module == null) b.Module = new List<Modul>(); if (b.LeistenGroesse == null) b.LeistenGroesse = new Dictionary<string, int>(); foreach (var m in b.Module) { if (m.Inhalt == null) m.Inhalt = new List<string>(); if (m.W < 1) { m.W = m.Breit ? 4 : 2; m.H = m.Art == "recent" || m.Art == "screens" ? 1 : 2; } } }
                        if (!D.Liste.Any(b => b.Art == "temp")) D.Liste.Insert(0, new BlattDef { Art = "temp", Name = "Expiring" });
                        if (D.Version < 3) Umstellen();
                        return;
                    }
                }
            }
            catch (Exception ex) { Log.Fehler("Blaetter.Laden", ex); }
            bool neu = !Bisher();
            D = Standard(neu);
            Frisch = neu;
            Speichern();
        }

        /// <summary>Gab es schon die alten festen Ebenen (Permanent mit Inhalt oder ein angeordneter Tisch)?</summary>
        static bool Bisher()
        {
            try
            {
                return Directory.Exists(Paths.Dauerhaft) && Directory.EnumerateFileSystemEntries(Paths.Dauerhaft).Any(p => !Engine.IstHilfsdatei(p));
            }
            catch { return false; }
        }

        /// <summary>Neu: Expiring und ein leeres Blatt in der Mitte. Bisher: die alten drei Ebenen bleiben, wie sie waren.</summary>
        public static BlaetterDaten Standard(bool neu)
        {
            var d = new BlaetterDaten { Version = 3 };
            d.Liste.Add(new BlattDef { Art = "temp", Name = "Expiring" });
            if (neu) { var h = new BlattDef { Art = "home", Name = "Home", Vorschlag = true }; Hauptfenster.Vorlage(h, "focus"); d.Liste.Add(h); }
            else
            {
                d.Liste.Add(new BlattDef { Art = "folder", Pfad = Paths.Dauerhaft, Name = "Permanent" });
                d.Liste.Add(new BlattDef { Art = "cloud", Name = "Cloud" });
            }
            return d;
        }

        /// <summary>
        /// Einmalig: ein leeres, unverändertes „Permanent“ aus der alten Einteilung wird zum + – die Mitte gehört dem Nutzer.
        /// Liegt dort etwas, bleibt alles, wie es ist.
        /// </summary>
        static void Umstellen()
        {
            if (D.Version < 2)
                foreach (var b in D.Liste)
                {
                    if (b.Art != "folder" || !Paths.Gleich(b.Pfad, Paths.Dauerhaft) || b.Leisten.Count > 0 || Bisher()) continue;
                    b.Art = "empty"; b.Pfad = ""; b.Name = ""; b.Symbol = "";
                }
            // 3: das leere + wird zu Home (die Art des Blattes wählt man jetzt direkt am + der Schiene)
            foreach (var b in D.Liste) if (b.Art == "empty") { b.Art = "home"; b.Name = "Home"; b.Vorschlag = true; Hauptfenster.Vorlage(b, "focus"); }
            D.Version = 3;
            Speichern();
        }

        public static void Speichern()
        {
            if (D == null) return;
            try
            {
                Directory.CreateDirectory(Paths.Daten);
                string tmp = Datei + ".tmp";
                File.WriteAllText(tmp, Store.Json().Serialize(D));
                if (File.Exists(Datei)) File.Replace(tmp, Datei, null); else File.Move(tmp, Datei);
            }
            catch (Exception ex) { Log.Fehler("Blaetter.Speichern", ex); }
        }

        public static int Index(string art) { return Liste.FindIndex(b => b.Art == art); }
        public static int Mitte { get { return Liste.Count / 2 - (Liste.Count % 2 == 0 ? 1 : 0); } }

        public static bool IstOrdnerArt(BlattDef b) { return b != null && (b.Art == "folder" || b.Art == "cloud"); }

        /// <summary>Wurzelordner eines Ordner-Blatts (Cloud: der gewählte oder erkannte Cloud-Ordner).</summary>
        public static string Wurzel(BlattDef b, CloudOrt wolke)
        {
            if (b == null) return null;
            if (b.Art == "folder") return string.IsNullOrEmpty(b.Pfad) ? null : b.Pfad;
            if (b.Art == "cloud") return !string.IsNullOrEmpty(b.Pfad) ? b.Pfad : (wolke != null ? wolke.Pfad : null);
            return null;
        }

        public static string NameVon(BlattDef b, CloudOrt wolke)
        {
            if (b == null) return "";
            if (b.Art == "temp") return "Expiring";
            if (b.Art == "empty" || b.Art == "home") return "Home";
            if (b.Art == "board") return string.IsNullOrEmpty(b.Name) ? "Board" : b.Name;
            if (b.Art == "cloud" && string.IsNullOrEmpty(b.Pfad)) return wolke != null ? wolke.Name : "Cloud";
            if (!string.IsNullOrEmpty(b.Name)) return b.Name;
            string p = (b.Pfad ?? "").TrimEnd('\\');
            return p.Length == 0 ? "Folder" : Path.GetFileName(p);
        }

        // ---------- Leisten ----------

        public static string LeisteBei(BlattDef b, string kante)
        {
            foreach (var l in b.Leisten) { var t = l.Split('@'); if (t.Length == 2 && t[1] == kante) return t[0]; }
            return null;
        }

        public static void LeisteSetzen(BlattDef b, string art, string kante)
        {
            b.Leisten.RemoveAll(l => l.StartsWith(art + "@") || l.EndsWith("@" + kante));
            if (kante != null) b.Leisten.Add(art + "@" + kante);
            Speichern();
        }

        public static int LeisteGroesse(BlattDef b, string art) { int g; return b.LeistenGroesse != null && b.LeistenGroesse.TryGetValue(art, out g) ? Math.Max(0, Math.Min(2, g)) : 1; }
        public static void LeisteGroesseSetzen(BlattDef b, string art, int g) { if (b.LeistenGroesse == null) b.LeistenGroesse = new Dictionary<string, int>(); b.LeistenGroesse[art] = g; Speichern(); }

        public static void LeisteWeg(BlattDef b, string art) { b.Leisten.RemoveAll(l => l.StartsWith(art + "@")); Speichern(); }

        /// <summary>Ablage: Verweise hinzufügen (ohne zu verschieben), höchstens 40.</summary>
        public static void Parken(IEnumerable<string> pfade)
        {
            foreach (var p in pfade)
            {
                D.Ablage.RemoveAll(x => Paths.Gleich(x, p));
                D.Ablage.Insert(0, p);
            }
            if (D.Ablage.Count > 40) D.Ablage.RemoveRange(40, D.Ablage.Count - 40);
            Speichern();
        }

        public static string Screenshots
        {
            get
            {
                string bilder = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                foreach (var n in new[] { "Screenshots", "Bildschirmfotos" })
                {
                    string p = Path.Combine(bilder, n);
                    if (Directory.Exists(p)) return p;
                }
                return Path.Combine(bilder, "Screenshots");
            }
        }
    }

    /// <summary>
    /// Zuletzt benutzte Dateien: die Windows-Liste „Zuletzt verwendet“ (%APPDATA%\Microsoft\Windows\Recent, Verknüpfungen)
    /// plus die eigenen letzten Downloads. Im Hintergrund gelesen, 30 s gemerkt.
    /// </summary>
    static class Zuletzt
    {
        static List<string> merk = new List<string>();
        static DateTime stand = DateTime.MinValue;
        static bool liest;
        public static event Action Neu;

        public static List<string> Liste(Engine e)
        {
            if (!liest && (DateTime.UtcNow - stand).TotalSeconds > 30)
            {
                liest = true;
                var eigene = e.S.D.Eintraege.OrderByDescending(x => x.Hinzu).Take(15).Select(x => new KeyValuePair<string, DateTime>(x.Pfad, x.Hinzu)).ToList();
                var ui = System.Windows.Threading.Dispatcher.CurrentDispatcher;
                Task.Factory.StartNew(() => Lesen(eigene)).ContinueWith(t =>
                {
                    liest = false;
                    stand = DateTime.UtcNow;
                    if (t.Exception != null || t.Result.SequenceEqual(merk, StringComparer.OrdinalIgnoreCase)) return;
                    merk = t.Result;
                    ui.BeginInvoke(new Action(() => { if (Neu != null) Neu(); }));
                });
            }
            return merk;
        }

        static List<string> Lesen(List<KeyValuePair<string, DateTime>> eigene)
        {
            var l = new List<KeyValuePair<string, DateTime>>(eigene);
            if (!Paths.Test)
            {
                try
                {
                    string ordner = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
                    var lnks = new DirectoryInfo(ordner).GetFiles("*.lnk").OrderByDescending(f => f.LastWriteTimeUtc).Take(60).ToList();
                    var t = Type.GetTypeFromProgID("WScript.Shell");
                    dynamic sh = Activator.CreateInstance(t);
                    foreach (var f in lnks)
                    {
                        try
                        {
                            dynamic k = sh.CreateShortcut(f.FullName);
                            string ziel = k.TargetPath;
                            if (!string.IsNullOrEmpty(ziel) && File.Exists(ziel)) l.Add(new KeyValuePair<string, DateTime>(ziel, f.LastWriteTimeUtc));
                        }
                        catch { }
                    }
                }
                catch (Exception ex) { Log.Fehler("Zuletzt", ex); }
            }
            var gesehen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return l.Where(x => File.Exists(x.Key) || Directory.Exists(x.Key)).OrderByDescending(x => x.Value)
                .Where(x => gesehen.Add(x.Key) && !Engine.IstHilfsdatei(x.Key)).Take(40).Select(x => x.Key).ToList();
        }

        /// <summary>Nur für Test und Bild.</summary>
        public static void Setzen(List<string> l) { merk = l; stand = DateTime.UtcNow; }

        /// <summary>Der zuletzt gelesene Stand (ohne neu zu lesen).</summary>
        public static List<string> Stand { get { return merk; } }
    }

    /// <summary>
    /// Was in Workbench geöffnet wird (Ordner betreten, Dateien öffnen) – je Pfad ein Wert, der alle 14 Tage halb so viel zählt.
    /// Klein: höchstens 400 Einträge in nutzung.json. Daraus wird im Netz die „Wärme“ der Wege.
    /// </summary>
    static class Nutzung
    {
        static Dictionary<string, double[]> d;
        static System.Windows.Threading.DispatcherTimer speichern;
        static string Datei { get { return Path.Combine(Paths.Daten, "nutzung.json"); } }

        static Dictionary<string, double[]> Daten
        {
            get
            {
                if (d != null) return d;
                d = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    if (File.Exists(Datei))
                    {
                        var x = Store.Json().Deserialize<Dictionary<string, double[]>>(File.ReadAllText(Datei));
                        if (x != null) foreach (var kv in x) if (kv.Value != null && kv.Value.Length == 2) d[kv.Key] = kv.Value;
                    }
                }
                catch (Exception ex) { Log.Fehler("Nutzung laden", ex); }
                return d;
            }
        }

        static double Wert(double[] v, double jetztTage) { return v[0] * Math.Pow(0.5, (jetztTage - v[1]) / 14.0); }
        static double Tage { get { return DateTime.UtcNow.Ticks / (double)TimeSpan.TicksPerDay; } }

        public static void Merken(string pfad)
        {
            if (string.IsNullOrEmpty(pfad)) return;
            try
            {
                var t = Tage;
                double[] v;
                var dd = Daten;
                dd[pfad] = new[] { (dd.TryGetValue(pfad, out v) ? Wert(v, t) : 0) + 1, t };
                if (dd.Count > 400) foreach (var k in dd.OrderBy(kv => Wert(kv.Value, t)).Take(dd.Count - 400).Select(kv => kv.Key).ToList()) dd.Remove(k);
                if (Vorschau.Sofort) return;
                if (speichern == null)
                {
                    speichern = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                    speichern.Tick += delegate
                    {
                        speichern.Stop();
                        try { Directory.CreateDirectory(Paths.Daten); File.WriteAllText(Datei, Store.Json().Serialize(Daten)); }
                        catch (Exception ex) { Log.Fehler("Nutzung speichern", ex); }
                    };
                }
                speichern.Stop(); speichern.Start();
            }
            catch (Exception ex) { Log.Fehler("Nutzung", ex); }
        }

        /// <summary>Schnappschuss mit Abkühlung – für die Berechnung im Hintergrund.</summary>
        public static Dictionary<string, double> Alle()
        {
            var t = Tage;
            return Daten.ToDictionary(kv => kv.Key, kv => Wert(kv.Value, t), StringComparer.OrdinalIgnoreCase);
        }
    }
}
