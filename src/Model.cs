using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace Workbench
{
    /// <summary>Alle Orte, die Workbench benutzt. Mit WORKBENCH_TESTROOT lässt sich alles in einen Testordner umlenken.</summary>
    static class Paths
    {
        public static readonly string Wurzel;      // %USERPROFILE%\Workbench – sichtbarer Ordner (Explorer, Speichern-Dialoge)
        public static readonly string Daten;       // %LOCALAPPDATA%\Workbench – Liste der Fristen, Einstellungen, Log
        public static readonly string Downloads;
        public static readonly string Desktop;
        public static readonly string Programm;    // %LOCALAPPDATA%\Programs\Workbench
        public static readonly bool Test;

        public const string DauerhaftName = "Dauerhaft";
        // Ordnername → Dauer. Wer etwas in einen dieser Ordner speichert, hat die Dauer damit schon gewählt.
        public static readonly string[][] ZeitOrdner =
        {
            new[] { "1 Stunde", "1 Std" },
            new[] { "1 Tag", "1 Tag" },
            new[] { "1 Woche", "1 Woche" },
            new[] { "1 Monat", "1 Monat" },
        };

        static Paths()
        {
            string test = Environment.GetEnvironmentVariable("WORKBENCH_TESTROOT");
            if (!string.IsNullOrEmpty(test))
            {
                Test = true;
                Wurzel = Path.Combine(test, "Workbench");
                Daten = Path.Combine(test, "Daten");
                Downloads = Path.Combine(test, "Downloads");
                Desktop = Path.Combine(test, "Desktop");
                Programm = Path.Combine(test, "Programm");
                return;
            }
            Desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            Wurzel = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Workbench");
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            Daten = Path.Combine(local, "Workbench");
            Programm = Path.Combine(local, "Programs", "Workbench");
            Downloads = Native.DownloadsOrdner();
        }

        public static string Dauerhaft { get { return Path.Combine(Wurzel, DauerhaftName); } }

        public static void OrdnerAnlegen()
        {
            Directory.CreateDirectory(Wurzel);
            Directory.CreateDirectory(Dauerhaft);
            foreach (var z in ZeitOrdner) Directory.CreateDirectory(Path.Combine(Wurzel, z[0]));
            Directory.CreateDirectory(Daten);
            Directory.CreateDirectory(Downloads);
            if (Test) Directory.CreateDirectory(Desktop);
        }

        /// <summary>Liegt der Pfad direkt in einem Zeit-Ordner? Dann die Dauer, sonst null. "Dauerhaft" für den Dauerhaft-Ordner.</summary>
        public static string DauerVonOrdner(string pfad)
        {
            string eltern = Path.GetDirectoryName(pfad);
            if (eltern == null) return null;
            if (Gleich(eltern, Dauerhaft)) return "Dauerhaft";
            foreach (var z in ZeitOrdner)
                if (Gleich(eltern, Path.Combine(Wurzel, z[0]))) return z[1];
            return null;
        }

        public static bool Gleich(string a, string b)
        {
            return string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }

        public static string FreierName(string ordner, string name)
        {
            string ziel = Path.Combine(ordner, name);
            if (!File.Exists(ziel) && !Directory.Exists(ziel)) return ziel;
            string basis = Path.GetFileNameWithoutExtension(name), ext = Path.GetExtension(name);
            for (int i = 2; ; i++)
            {
                ziel = Path.Combine(ordner, basis + " (" + i + ")" + ext);
                if (!File.Exists(ziel) && !Directory.Exists(ziel)) return ziel;
            }
        }
    }

    static class Dauer
    {
        public static readonly string[] Auswahl = { "1 Std", "1 Tag", "1 Woche", "1 Monat" };
        public const string Bis = "Bis …";
        public const string Immer = "Für immer";

        public static DateTime? Ablauf(string name, DateTime vonUtc)
        {
            switch (name)
            {
                case "1 Std": return vonUtc.AddHours(1);
                case "1 Tag": return vonUtc.AddDays(1);
                case "1 Woche": return vonUtc.AddDays(7);
                case "1 Monat": return vonUtc.AddMonths(1);
                case Immer: return null;
            }
            return vonUtc.AddDays(7);
        }

        /// <summary>Ende des gewählten Tages (lokal), als UTC.</summary>
        public static DateTime BisTag(DateTime tagLokal)
        {
            return new DateTime(tagLokal.Year, tagLokal.Month, tagLokal.Day, 23, 59, 0, DateTimeKind.Local).ToUniversalTime();
        }

        static readonly string[] installer = { ".exe", ".msi", ".msix", ".msixbundle", ".appx", ".appxbundle", ".iso", ".img" };

        public static bool IstInstaller(string pfad)
        {
            return installer.Contains(Path.GetExtension(pfad).ToLowerInvariant());
        }

        /// <summary>„noch 42 Min“, „noch 3 Tage“, „bis 15.01.“</summary>
        public static string Rest(DateTime ablaufUtc, DateTime jetztUtc)
        {
            var r = ablaufUtc - jetztUtc;
            if (r.TotalMinutes < 1) return "gleich";
            if (r.TotalMinutes < 60) return (int)Math.Ceiling(r.TotalMinutes) + " Min";
            if (r.TotalHours < 24) return Math.Max(1, (int)Math.Round(r.TotalHours)) + " Std";
            if (r.TotalDays < 14) { int d = (int)Math.Round(r.TotalDays); return d + (d == 1 ? " Tag" : " Tage"); }
            if (r.TotalDays < 60) { int w = (int)Math.Round(r.TotalDays / 7); return w + (w == 1 ? " Woche" : " Wochen"); }
            return "bis " + ablaufUtc.ToLocalTime().ToString("dd.MM.yy");
        }

        /// <summary>0 = heute (&lt; 24 Std), 1 = diese Woche, 2 = später</summary>
        public static int Spalte(DateTime ablaufUtc, DateTime jetztUtc)
        {
            var r = ablaufUtc - jetztUtc;
            if (r.TotalHours < 24) return 0;
            if (r.TotalDays <= 7.01) return 1;
            return 2;
        }

        public static string Wann(DateTime ablaufUtc)
        {
            var l = ablaufUtc.ToLocalTime();
            var heute = DateTime.Now.Date;
            string zeit = l.ToString("HH:mm");
            if (l.Date == heute) return "heute um " + zeit;
            if (l.Date == heute.AddDays(1)) return "morgen um " + zeit;
            return Wochentag(l) + ", " + l.ToString("dd.MM.yyyy") + " um " + zeit;
        }

        public static string Wochentag(DateTime d)
        {
            string[] t = { "So", "Mo", "Di", "Mi", "Do", "Fr", "Sa" };
            return t[(int)d.DayOfWeek];
        }

        public static string Groesse(long b)
        {
            if (b < 1024) return b + " B";
            if (b < 1024 * 1024) return (b / 1024.0).ToString("0") + " KB";
            if (b < 1024L * 1024 * 1024) return (b / 1024.0 / 1024).ToString(b < 10 * 1024 * 1024 ? "0.0" : "0") + " MB";
            return (b / 1024.0 / 1024 / 1024).ToString("0.0") + " GB";
        }
    }

    public class Eintrag
    {
        public string Pfad;
        public DateTime Hinzu;        // UTC
        public DateTime? Ablauf;      // UTC, null = für immer
        public string Herkunft;       // "download" | "ordner"
        public string Gewaehlt;       // Dauer, die gewählt wurde (für die Papierkorb-Ansicht)

        public string Name { get { return System.IO.Path.GetFileName(Pfad); } }
    }

    public class Recycelt
    {
        public string Pfad;           // ursprünglicher Pfad
        public DateTime Am;           // UTC
        public string Dauer;
    }

    public class StoreDaten
    {
        public List<Eintrag> Eintraege = new List<Eintrag>();
        public List<Recycelt> Papierkorb = new List<Recycelt>();
        public Dictionary<string, string> LetzteWahl = new Dictionary<string, string>();
    }

    /// <summary>Die Liste aller befristeten Dateien. Klein, als JSON gespeichert, atomar geschrieben.</summary>
    class Store
    {
        public StoreDaten D = new StoreDaten();
        readonly string datei;

        public Store() { datei = Path.Combine(Paths.Daten, "fristen.json"); }

        public static JavaScriptSerializer Json()
        {
            var j = new JavaScriptSerializer();
            j.MaxJsonLength = int.MaxValue;
            return j;
        }

        public void Laden()
        {
            try
            {
                if (File.Exists(datei))
                {
                    D = Json().Deserialize<StoreDaten>(File.ReadAllText(datei));
                    if (D.Eintraege == null) D.Eintraege = new List<Eintrag>();
                    if (D.Papierkorb == null) D.Papierkorb = new List<Recycelt>();
                    if (D.LetzteWahl == null) D.LetzteWahl = new Dictionary<string, string>();
                    foreach (var e in D.Eintraege)
                    {
                        e.Hinzu = Utc(e.Hinzu);
                        if (e.Ablauf.HasValue) e.Ablauf = Utc(e.Ablauf.Value);
                    }
                    foreach (var r in D.Papierkorb) r.Am = Utc(r.Am);
                }
            }
            catch (Exception ex) { Log.Fehler("Store.Laden", ex); D = new StoreDaten(); }
        }

        static DateTime Utc(DateTime d)
        {
            if (d.Kind == DateTimeKind.Utc) return d;
            if (d.Kind == DateTimeKind.Local) return d.ToUniversalTime();
            return DateTime.SpecifyKind(d, DateTimeKind.Utc);
        }

        public void Speichern()
        {
            try
            {
                Directory.CreateDirectory(Paths.Daten);
                string tmp = datei + ".tmp";
                File.WriteAllText(tmp, Json().Serialize(D));
                if (File.Exists(datei)) File.Replace(tmp, datei, null); else File.Move(tmp, datei);
            }
            catch (Exception ex) { Log.Fehler("Store.Speichern", ex); }
        }

        public Eintrag Finde(string pfad)
        {
            foreach (var e in D.Eintraege) if (Paths.Gleich(e.Pfad, pfad)) return e;
            return null;
        }

        public void Entfernen(string pfad)
        {
            D.Eintraege.RemoveAll(e => Paths.Gleich(e.Pfad, pfad));
        }
    }

    public class Einstellungen
    {
        public bool Fragen = true;                 // nach Downloads fragen
        public string Standard = "1 Woche";        // wenn nicht gewählt wird
        public int Sekunden = 8;                   // so lange bleibt die Frage stehen
        public bool HinweisSpeichern = true;       // kurzer Hinweis beim Speichern in Workbench-Ordner
        public bool Autostart = true;
        public bool Tastenkuerzel = true;          // Strg + Alt + W
        public bool Desktop = true;                // auch neue Dateien auf dem echten Desktop fragen
        public bool Rechtsklick = true;            // „Workbench ▸ 1 Tag …“ im Rechtsklick-Menü jeder Datei
        public bool EinfuehrungFertig = false;     // Rundgang beim ersten Start
        public DateTime LetztePruefung;            // Update-Prüfung höchstens einmal am Tag
        public string NeueVersion = "";
        public bool Seitenleiste = true;           // Explorer-Navigationsbereich / Speichern-Dialoge
        public bool TippTaskleiste = true;         // einmaliger Tipp zum Anheften
        public string CloudPfad = "";              // Ebene 3; leer = automatisch (Google Drive, OneDrive, Dropbox …)
        public string CloudName = "";

        static string Datei { get { return Path.Combine(Paths.Daten, "einstellungen.json"); } }

        public static Einstellungen Laden()
        {
            try
            {
                if (File.Exists(Datei)) return Store.Json().Deserialize<Einstellungen>(File.ReadAllText(Datei)) ?? new Einstellungen();
            }
            catch (Exception ex) { Log.Fehler("Einstellungen.Laden", ex); }
            return new Einstellungen();
        }

        public void Speichern()
        {
            try
            {
                Directory.CreateDirectory(Paths.Daten);
                File.WriteAllText(Datei, Store.Json().Serialize(this));
            }
            catch (Exception ex) { Log.Fehler("Einstellungen.Speichern", ex); }
        }
    }
}
