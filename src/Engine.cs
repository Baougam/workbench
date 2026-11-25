using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Workbench
{
    /// <summary>
    /// Das Herz im Hintergrund: beobachtet Downloads und die Workbench-Ordner (Windows meldet Änderungen selbst – kein Dauer-Suchen),
    /// und legt Abgelaufenes in den Papierkorb. Der Ablauf-Timer wacht nur zur nächsten Frist auf.
    /// </summary>
    class Engine
    {
        public readonly Store S = new Store();
        public Einstellungen Cfg;
        readonly Dispatcher ui;

        public event Action<List<Eintrag>> DownloadsFertig;      // neue Downloads, Frist ist vorläufig gesetzt
        public event Action<Eintrag> OrdnerGespeichert;          // in einen Zeit-Ordner gespeichert
        public event Action Geaendert;

        public Action<string> Recycler = Native.InPapierkorb;
        public Func<DateTime> Jetzt = () => DateTime.UtcNow;

        FileSystemWatcher wDl, wRoot, wDesk;
        readonly Dictionary<string, Warte> warten = new Dictionary<string, Warte>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, DateTime> weg = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        readonly DispatcherTimer tWarte, tAblauf, tGeaendert;

        class Warte { public long Groesse = -1; public int Stabil; public DateTime Seit; public string Herkunft; }

        public Engine(Dispatcher d, Einstellungen cfg)
        {
            ui = d;
            Cfg = cfg;
            tWarte = new DispatcherTimer(DispatcherPriority.Background, d) { Interval = TimeSpan.FromMilliseconds(700) };
            tWarte.Tick += delegate { WarteTick(); };
            tAblauf = new DispatcherTimer(DispatcherPriority.Background, d);
            tAblauf.Tick += delegate { tAblauf.Stop(); Aufraeumen(); Planen(); };
            tGeaendert = new DispatcherTimer(DispatcherPriority.Background, d) { Interval = TimeSpan.FromMilliseconds(250) };
            tGeaendert.Tick += delegate { tGeaendert.Stop(); if (Geaendert != null) Geaendert(); };
        }

        public void Start()
        {
            Paths.OrdnerAnlegen();
            S.Laden();
            Abgleichen();

            wDl = Beobachter(Paths.Downloads, false);
            wRoot = Beobachter(Paths.Wurzel, true);
            DesktopBeobachten(Cfg.Desktop);
            Nachholen();

            SystemEvents.PowerModeChanged += (s, e) => { if (e.Mode == PowerModes.Resume) ui.BeginInvoke(new Action(() => { Aufraeumen(); Planen(); })); };
            SystemEvents.TimeChanged += (s, e) => ui.BeginInvoke(new Action(() => { Aufraeumen(); Planen(); }));

            Aufraeumen();
            Planen();
        }

        public void Stop()
        {
            if (warten.Count == 0) { S.D.LetzterBlick = DateTime.UtcNow; S.Speichern(); }
            if (wDl != null) wDl.Dispose();
            if (wRoot != null) wRoot.Dispose();
            if (wDesk != null) wDesk.Dispose();
        }

        /// <summary>Neue Dateien auf dem echten Desktop (z. B. „Bild speichern unter … › Desktop“) bekommen dieselbe Frage.</summary>
        public void DesktopBeobachten(bool an)
        {
            if (wDesk != null) { wDesk.Dispose(); wDesk = null; }
            if (!an || string.IsNullOrEmpty(Paths.Desktop) || !Directory.Exists(Paths.Desktop)) return;
            try { wDesk = Beobachter(Paths.Desktop, false); } catch (Exception ex) { Log.Fehler("Desktop beobachten", ex); }
        }

        bool AufDemDesktop(string pfad)
        {
            string el = Path.GetDirectoryName(pfad);
            return wDesk != null && el != null && Paths.Gleich(el, Paths.Desktop);
        }

        static bool IstVerknuepfung(string pfad)
        {
            string e = Path.GetExtension(pfad).ToLowerInvariant();
            return e == ".lnk" || e == ".url" || e == ".website" || e == ".appref-ms" || e == ".ini";
        }

        FileSystemWatcher Beobachter(string ordner, bool unterordner)
        {
            var w = new FileSystemWatcher(ordner)
            {
                IncludeSubdirectories = unterordner,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                InternalBufferSize = 64 * 1024
            };
            w.Created += (s, e) => ui.BeginInvoke(new Action(() => Neu(e.FullPath)));
            w.Deleted += (s, e) => ui.BeginInvoke(new Action(() => Geloescht(e.FullPath)));
            w.Renamed += (s, e) => ui.BeginInvoke(new Action(() => Umbenannt(e.OldFullPath, e.FullPath)));
            w.Error += (s, e) => ui.BeginInvoke(new Action(() => { Log.Info("Beobachter-Puffer voll, gleiche ab"); Abgleichen(); Nachholen(); MeldeGeaendert(); }));
            w.EnableRaisingEvents = true;
            return w;
        }

        // ---------- Ereignisse aus dem Dateisystem ----------

        public static bool IstHilfsdatei(string pfad)
        {
            string n = Path.GetFileName(pfad);
            if (string.IsNullOrEmpty(n)) return true;
            string l = n.ToLowerInvariant();
            if (l.StartsWith("~$") || l.StartsWith("~wr") || l.StartsWith(".~")) return true;
            if (l == "desktop.ini" || l == "thumbs.db") return true;
            string[] endungen = { ".crdownload", ".part", ".partial", ".download", ".opdownload", ".tmp", ".temp", ".!ut", ".aria2", ".tmp.part", ".~tmp", ".!qb", ".crswap", ".bc!", ".dlpart" };
            foreach (var e in endungen) if (l.EndsWith(e)) return true;
            if (l.StartsWith("unconfirmed ") && l.EndsWith(".crdownload")) return true;
            try
            {
                var a = File.GetAttributes(pfad);
                if ((a & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.Temporary)) != 0) return true;
            }
            catch { }
            return false;
        }

        bool ImDownloadOrdner(string pfad)
        {
            string el = Path.GetDirectoryName(pfad);
            return el != null && Paths.Gleich(el, Paths.Downloads);
        }

        void Neu(string pfad)
        {
            bool dl = ImDownloadOrdner(pfad);
            bool desk = !dl && AufDemDesktop(pfad);
            string zeit = dl || desk ? null : Paths.DauerVonOrdner(pfad);
            if (!dl && !desk && zeit == null) { MeldeGeaendert(); return; }   // irgendwo tiefer – nur Ansicht auffrischen
            if (IstHilfsdatei(pfad)) return;
            if (weg.ContainsKey(pfad)) { weg.Remove(pfad); MeldeGeaendert(); return; }   // neu gespeichert (Word & Co.): Frist bleibt
            if (zeit == "Permanent") { MeldeGeaendert(); return; }
            if (S.Finde(pfad) != null) return;
            if ((dl || desk) && Directory.Exists(pfad)) return;         // Ordner (entpackt, „Neuer Ordner“) nicht nachfragen
            if (desk && IstVerknuepfung(pfad)) return;                   // Verknüpfungen von Installern nicht nachfragen
            warten[pfad] = new Warte { Seit = DateTime.UtcNow, Herkunft = dl ? "download" : desk ? "desktop" : "ordner" };
            if (!tWarte.IsEnabled) tWarte.Start();
        }

        void Geloescht(string pfad)
        {
            warten.Remove(pfad);
            if (S.Finde(pfad) != null) { weg[pfad] = DateTime.UtcNow; if (!tWarte.IsEnabled) tWarte.Start(); }
            MeldeGeaendert();
        }

        void Umbenannt(string alt, string neu)
        {
            var e = S.Finde(alt);
            if (e != null)
            {
                bool gleicherOrt = Paths.Gleich(Path.GetDirectoryName(alt), Path.GetDirectoryName(neu));
                if (IstHilfsdatei(neu) || !gleicherOrt) { Geloescht(alt); if (!IstHilfsdatei(neu)) Neu(neu); return; }
                e.Pfad = neu;                                             // vom Nutzer umbenannt: Frist bleibt
                S.Speichern();
                MeldeGeaendert();
                return;
            }
            Warte w;
            if (warten.TryGetValue(alt, out w)) warten.Remove(alt);
            Neu(neu);                                                     // z. B. Chrome: .crdownload → fertige Datei
        }

        static bool LaeuftNoch(string pfad)
        {
            string[] teil = { ".part", ".crdownload", ".download", ".partial" };
            foreach (var t in teil) if (File.Exists(pfad + t)) return true;
            return false;
        }

        static bool Lesbar(string pfad)
        {
            try { using (new FileStream(pfad, FileMode.Open, FileAccess.Read, FileShare.Read)) return true; }
            catch { return false; }
        }

        void WarteTick()
        {
            var jetzt = DateTime.UtcNow;
            var fertigDl = new List<Eintrag>();
            foreach (var kv in warten.ToList())
            {
                string p = kv.Key; var w = kv.Value;
                bool istOrdner = Directory.Exists(p);
                if (!istOrdner && !File.Exists(p)) { warten.Remove(p); continue; }
                if ((jetzt - w.Seit).TotalHours > 48) { warten.Remove(p); continue; }
                bool bereit;
                if (istOrdner) bereit = ++w.Stabil >= 2;
                else if (LaeuftNoch(p)) { w.Stabil = 0; bereit = false; }
                else
                {
                    long g = new FileInfo(p).Length;
                    if (g == w.Groesse && Lesbar(p)) w.Stabil++; else { w.Stabil = 0; w.Groesse = g; }
                    bereit = w.Stabil >= 2 && (g > 0 || (jetzt - w.Seit).TotalSeconds > 6);
                }
                if (!bereit) continue;
                warten.Remove(p);
                if (S.Finde(p) != null) continue;
                if (w.Herkunft != "ordner")
                {
                    string vorschlag = Vorschlag(p);
                    var e = new Eintrag { Pfad = p, Hinzu = Jetzt(), Ablauf = Dauer.Ablauf(vorschlag, Jetzt()), Herkunft = w.Herkunft, Gewaehlt = vorschlag };
                    S.D.Eintraege.Add(e);
                    fertigDl.Add(e);
                }
                else
                {
                    string d = Paths.DauerVonOrdner(p);
                    if (d == null || d == "Permanent") continue;
                    var e = new Eintrag { Pfad = p, Hinzu = Jetzt(), Ablauf = Dauer.Ablauf(d, Jetzt()), Herkunft = "ordner", Gewaehlt = d };
                    S.D.Eintraege.Add(e);
                    S.Speichern();
                    if (OrdnerGespeichert != null) OrdnerGespeichert(e);
                }
            }
            // Gelöschte erst nach kurzer Gnadenfrist austragen – Word & Co. löschen beim Speichern kurz und legen neu an.
            foreach (var kv in weg.ToList())
            {
                if ((jetzt - kv.Value).TotalSeconds < 4) continue;
                weg.Remove(kv.Key);
                if (!File.Exists(kv.Key) && !Directory.Exists(kv.Key)) S.Entfernen(kv.Key);
                S.Speichern();
                MeldeGeaendert();
            }
            if (fertigDl.Count > 0)
            {
                S.Speichern();
                Planen();
                MeldeGeaendert();
                if (DownloadsFertig != null) DownloadsFertig(fertigDl);
            }
            if (warten.Count == 0)
            {
                S.D.LetzterBlick = jetzt;
                if (fertigDl.Count > 0) S.Speichern();
            }
            if (warten.Count == 0 && weg.Count == 0) tWarte.Stop();
        }

        // ---------- Fristen ----------

        /// <summary>Vorschlag für die Dauer: was zuletzt für diese Dateiart gewählt wurde, Installer 1 Tag, sonst der Standard.</summary>
        public string Vorschlag(string pfad)
        {
            string ext = Path.GetExtension(pfad).ToLowerInvariant();
            string v;
            if (ext.Length > 0 && S.D.LetzteWahl.TryGetValue(ext, out v)) return v;
            if (Dauer.IstInstaller(pfad)) return "1 day";
            return Cfg.Standard;
        }

        public void Merken(string pfad, string wahl)
        {
            string ext = Path.GetExtension(pfad).ToLowerInvariant();
            if (ext.Length > 0 && wahl != Dauer.Bis) S.D.LetzteWahl[ext] = wahl;
        }

        /// <summary>Frist setzen. ablaufUtc = null heißt für immer: die Datei wandert nach „Dauerhaft“.</summary>
        public string FristSetzen(Eintrag e, DateTime? ablaufUtc, string gewaehlt)
        {
            string neuerPfad = e.Pfad;
            if (ablaufUtc == null && (e.Herkunft == "desktop" || e.Herkunft == "hand"))
            {
                S.Entfernen(e.Pfad);                     // vom Desktop: „für immer“ heißt einfach liegen lassen
                S.Speichern();
            }
            else if (ablaufUtc == null)
            {
                neuerPfad = Behalten(e);
            }
            else
            {
                e.Ablauf = ablaufUtc;
                e.Gewaehlt = gewaehlt;
                S.Speichern();
            }
            Planen();
            MeldeGeaendert();
            return neuerPfad;
        }

        public string Behalten(Eintrag e) { return Verschieben(e, Paths.Dauerhaft); }

        /// <summary>Rechtsklick › Workbench › Dauer – für jede Datei, egal wo. „Für immer“ nimmt die Frist wieder weg (Datei bleibt, wo sie ist).</summary>
        public Eintrag FristVonHand(string pfad, string dauer)
        {
            if (!File.Exists(pfad) && !Directory.Exists(pfad)) return null;
            var e = S.Finde(pfad);
            if (dauer == Dauer.Immer)
            {
                if (e != null) { S.Entfernen(pfad); S.Speichern(); Planen(); MeldeGeaendert(); }
                return null;
            }
            if (e == null) { e = new Eintrag { Pfad = pfad, Hinzu = Jetzt(), Herkunft = "hand" }; S.D.Eintraege.Add(e); }
            e.Hinzu = Jetzt();
            e.Ablauf = Dauer.Ablauf(dauer, Jetzt());
            e.Gewaehlt = dauer;
            S.Speichern();
            Planen();
            MeldeGeaendert();
            return e;
        }

        /// <summary>Befristete Datei woanders hin (Dauerhaft, Cloud …) – damit gilt sie als behalten.</summary>
        public string Verschieben(Eintrag e, string ordner)
        {
            string ziel = e.Pfad;
            S.Entfernen(e.Pfad);                       // vorher austragen: der Beobachter sieht dann nur noch „weg“
            try
            {
                if (File.Exists(e.Pfad) || Directory.Exists(e.Pfad)) ziel = DateiVerschieben(e.Pfad, ordner);
            }
            catch (Exception ex) { Log.Fehler("Verschieben", ex); S.D.Eintraege.Add(e); }
            S.Speichern();
            Planen();
            MeldeGeaendert();
            return ziel;
        }

        public static string DateiVerschieben(string p, string ordner)
        {
            Directory.CreateDirectory(ordner);
            string ziel = Paths.FreierName(ordner, Path.GetFileName(p));
            bool andereFestplatte = !string.Equals(Path.GetPathRoot(p), Path.GetPathRoot(ziel), StringComparison.OrdinalIgnoreCase);
            if (Directory.Exists(p))
            {
                if (andereFestplatte) { OrdnerKopieren(p, ziel); Directory.Delete(p, true); }
                else Directory.Move(p, ziel);
            }
            else File.Move(p, ziel);                   // geht auch über Laufwerke (kopiert + löscht)
            return ziel;
        }

        public static void OrdnerKopieren(string von, string nach)
        {
            Directory.CreateDirectory(nach);
            foreach (var f in Directory.GetFiles(von)) File.Copy(f, Path.Combine(nach, Path.GetFileName(f)));
            foreach (var d in Directory.GetDirectories(von)) OrdnerKopieren(d, Path.Combine(nach, Path.GetFileName(d)));
        }

        public bool JetztWeg(Eintrag e)
        {
            try
            {
                if (File.Exists(e.Pfad) || Directory.Exists(e.Pfad)) Recycler(e.Pfad);
                S.D.Papierkorb.Add(new Recycelt { Pfad = e.Pfad, Am = Jetzt(), Dauer = e.Gewaehlt });
                S.Entfernen(e.Pfad);
                S.Speichern();
                Planen();
                MeldeGeaendert();
                return true;
            }
            catch (Exception ex) { Log.Fehler("JetztWeg", ex); return false; }
        }

        /// <summary>Alles Abgelaufene in den Papierkorb. Ist eine Datei gerade offen, wird es in 10 Minuten wieder versucht.</summary>
        public void Aufraeumen()
        {
            var jetzt = Jetzt();
            bool geaendert = false;
            foreach (var e in S.D.Eintraege.ToList())
            {
                if (!e.Ablauf.HasValue || e.Ablauf.Value > jetzt) continue;
                geaendert = true;
                if (!File.Exists(e.Pfad) && !Directory.Exists(e.Pfad)) { S.D.Eintraege.Remove(e); continue; }
                try
                {
                    Recycler(e.Pfad);
                    S.D.Papierkorb.Add(new Recycelt { Pfad = e.Pfad, Am = jetzt, Dauer = e.Gewaehlt });
                    S.D.Eintraege.Remove(e);
                    Log.Info("Abgelaufen, im Papierkorb: " + e.Pfad);
                }
                catch (Exception ex)
                {
                    e.Ablauf = jetzt.AddMinutes(10);
                    Log.Info("Noch in Benutzung, später nochmal: " + e.Pfad + " (" + ex.Message + ")");
                }
            }
            if (S.D.Papierkorb.Count > 500) S.D.Papierkorb.RemoveRange(0, S.D.Papierkorb.Count - 500);
            if (geaendert) { S.Speichern(); MeldeGeaendert(); }
        }

        public void Planen()
        {
            tAblauf.Stop();
            var naechste = S.D.Eintraege.Where(e => e.Ablauf.HasValue).Select(e => e.Ablauf.Value).DefaultIfEmpty(DateTime.MaxValue).Min();
            if (naechste == DateTime.MaxValue) return;
            var bis = naechste - Jetzt();
            if (bis < TimeSpan.FromSeconds(1)) bis = TimeSpan.FromSeconds(1);
            if (bis > TimeSpan.FromHours(6)) bis = TimeSpan.FromHours(6);   // sicherheitshalber, falls die Uhr springt
            tAblauf.Interval = bis;
            tAblauf.Start();
        }

        /// <summary>Beim Start: Einträge ohne Datei entfernen, Dateien in Zeit-Ordnern ohne Eintrag aufnehmen.</summary>
        public void Abgleichen()
        {
            int vorher = S.D.Eintraege.Count;
            S.D.Eintraege.RemoveAll(e => !File.Exists(e.Pfad) && !Directory.Exists(e.Pfad));
            bool neu = vorher != S.D.Eintraege.Count;
            foreach (var z in Paths.ZeitOrdner)
            {
                string ordner = Path.Combine(Paths.Wurzel, z[0]);
                if (!Directory.Exists(ordner)) continue;
                foreach (var p in Directory.EnumerateFileSystemEntries(ordner))
                {
                    if (IstHilfsdatei(p) || S.Finde(p) != null) continue;
                    DateTime seit;
                    try { seit = File.GetCreationTimeUtc(p); } catch { seit = Jetzt(); }
                    if (seit > Jetzt()) seit = Jetzt();
                    S.D.Eintraege.Add(new Eintrag { Pfad = p, Hinzu = seit, Ablauf = Dauer.Ablauf(z[1], seit), Herkunft = "ordner", Gewaehlt = z[1] });
                    neu = true;
                }
            }
            if (neu) S.Speichern();
        }

        /// <summary>Downloads, die ankamen, während Workbench nicht lief (oder der Beobachter überlief), nachträglich fragen.</summary>
        public void Nachholen()
        {
            var seit = S.D.LetzterBlick;
            if (seit == default(DateTime)) { S.D.LetzterBlick = DateTime.UtcNow; S.Speichern(); return; }   // erster Start: Altbestand nicht fragen
            var grenze = DateTime.UtcNow.AddDays(-14);
            if (seit < grenze) seit = grenze;
            var ordner = new List<string> { Paths.Downloads };
            if (wDesk != null) ordner.Add(Paths.Desktop);
            var neu = new List<KeyValuePair<string, DateTime>>();
            foreach (var o in ordner)
            {
                if (string.IsNullOrEmpty(o) || !Directory.Exists(o)) continue;
                try
                {
                    foreach (var p in Directory.EnumerateFiles(o))
                    {
                        if (warten.ContainsKey(p) || S.Finde(p) != null || IstHilfsdatei(p) || IstVerknuepfung(p)) continue;
                        DateTime c;
                        try { c = File.GetCreationTimeUtc(p); } catch { continue; }
                        if (c > seit) neu.Add(new KeyValuePair<string, DateTime>(p, c));
                    }
                }
                catch (Exception ex) { Log.Fehler("Nachholen", ex); }
            }
            foreach (var kv in neu.OrderByDescending(k => k.Value).Take(30))
            {
                bool dl = ImDownloadOrdner(kv.Key);
                warten[kv.Key] = new Warte { Seit = DateTime.UtcNow, Herkunft = dl ? "download" : "desktop" };
            }
            if (neu.Count > 0) { Log.Info("Nachgeholt: " + neu.Count); if (!tWarte.IsEnabled) tWarte.Start(); }
        }

        public void MeldeGeaendert()
        {
            tGeaendert.Stop();
            tGeaendert.Start();
        }
    }
}
