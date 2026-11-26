using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Workbench
{
    /// <summary>
    /// Schaut still nach, ob es eine neuere Version gibt (Datei VERSION im GitHub-Repo), und lädt sie leise im Hintergrund vor.
    /// Erst wenn sie fertig daliegt, erscheint unten rechts „update x.y“ – ein Klick tauscht das Programm aus und startet neu (≈ 1 s).
    /// Sendet nichts außer den beiden Abfragen selbst.
    /// </summary>
    static class Update
    {
        const string ExeUrl = "https://github.com/Baougam/workbench/raw/main/download/Workbench.exe";
        const string VersionUrl = "https://raw.githubusercontent.com/Baougam/workbench/main/VERSION";

        public static event Action Gefunden;
        static int laedt;

        static string Exe { get { return Process.GetCurrentProcess().MainModule.FileName; } }
        static string NeuDatei { get { return Path.Combine(Paths.Daten, "Workbench.neu.exe"); } }

        /// <summary>Neuere Version, die schon fertig geladen ist – sonst null.</summary>
        public static string Neu(Einstellungen c)
        {
            return Neuer(c.NeueVersion) && File.Exists(NeuDatei) ? c.NeueVersion : null;
        }

        static bool Neuer(string v)
        {
            Version a, b;
            return !string.IsNullOrEmpty(v) && Version.TryParse(v, out a) && Version.TryParse(Program.Version, out b) && a > b;
        }

        /// <summary>Beim Start: Reste eines früheren Tauschs wegräumen.</summary>
        public static void Aufraeumen(Einstellungen c)
        {
            try { string alt = Exe + ".alt"; if (File.Exists(alt)) File.Delete(alt); } catch { }
            try { if (!Neuer(c.NeueVersion) && File.Exists(NeuDatei)) File.Delete(NeuDatei); } catch { }
        }

        public static void Pruefen(Einstellungen c)
        {
            if (Paths.Test) return;
            bool faellig = (DateTime.UtcNow - c.LetztePruefung).TotalHours >= 20;
            bool fehlt = Neuer(c.NeueVersion) && !File.Exists(NeuDatei);
            if (!faellig && !fehlt) return;
            if (Interlocked.Exchange(ref laedt, 1) == 1) return;
            var disp = Application.Current.Dispatcher;
            Task.Factory.StartNew(() =>
            {
                string v = null;
                try
                {
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                    using (var w = new WebClient())
                    {
                        w.Headers[HttpRequestHeader.UserAgent] = "Workbench";
                        v = faellig ? w.DownloadString(VersionUrl).Trim() : c.NeueVersion;
                        if (Neuer(v)) Laden(w);
                    }
                }
                catch (Exception ex) { Log.Fehler("Update", ex); }
                return v;
            }, TaskCreationOptions.LongRunning).ContinueWith(t =>
            {
                Interlocked.Exchange(ref laedt, 0);
                disp.BeginInvoke(new Action(() =>
                {
                    if (faellig) c.LetztePruefung = DateTime.UtcNow;
                    if (t.Result != null) c.NeueVersion = t.Result;
                    c.Speichern();
                    if (Neu(c) != null && Gefunden != null) Gefunden();
                }));
            });
        }

        /// <summary>Herunterladen in eine Nebendatei; nur behalten, wenn es wirklich ein Programm ist.</summary>
        static void Laden(WebClient w)
        {
            string tmp = NeuDatei + ".teil";
            w.Headers[HttpRequestHeader.UserAgent] = "Workbench";
            w.DownloadFile(ExeUrl, tmp);
            var fi = new FileInfo(tmp);
            bool ok = fi.Length > 100000;
            if (ok) using (var f = File.OpenRead(tmp)) ok = f.ReadByte() == 'M' && f.ReadByte() == 'Z';
            if (!ok) { File.Delete(tmp); return; }
            if (File.Exists(NeuDatei)) File.Delete(NeuDatei);
            File.Move(tmp, NeuDatei);
        }

        /// <summary>Tauschen und neu starten. Das laufende Programm darf umbenannt werden – die neue Datei nimmt seinen Platz ein.</summary>
        public static void Anwenden()
        {
            try
            {
                string exe = Exe, alt = exe + ".alt";
                if (File.Exists(alt)) File.Delete(alt);
                File.Move(exe, alt);
                try { File.Move(NeuDatei, exe); }
                catch { File.Move(alt, exe); throw; }
                Process.Start(new ProcessStartInfo(exe, "--nach-update") { UseShellExecute = false });
                using (var ev = new EventWaitHandle(false, EventResetMode.AutoReset, Program.BeendenName)) ev.Set();
            }
            catch (Exception ex) { Log.Fehler("Update.Anwenden", ex); }
        }
    }
}
