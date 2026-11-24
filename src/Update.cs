using System;
using System.Net;
using System.Threading.Tasks;
using System.Windows;

namespace Workbench
{
    /// <summary>
    /// Schaut höchstens einmal am Tag still nach, ob es eine neuere Version gibt (Datei VERSION im GitHub-Repo).
    /// Lädt nichts herunter und installiert nichts – merkt sich nur die Nummer. Sendet nichts außer der Abfrage selbst.
    /// </summary>
    static class Update
    {
        public const string Seite = "https://github.com/Baougam/workbench/raw/main/download/Workbench.exe";   // ein Klick lädt die neue Version
        const string VersionUrl = "https://raw.githubusercontent.com/Baougam/workbench/main/VERSION";

        public static event Action Gefunden;

        /// <summary>Neuere Version, falls bekannt – sonst null.</summary>
        public static string Neu(Einstellungen c)
        {
            return Neuer(c.NeueVersion) ? c.NeueVersion : null;
        }

        static bool Neuer(string v)
        {
            Version a, b;
            return !string.IsNullOrEmpty(v) && Version.TryParse(v, out a) && Version.TryParse(Program.Version, out b) && a > b;
        }

        public static void Pruefen(Einstellungen c)
        {
            if (Paths.Test || (DateTime.UtcNow - c.LetztePruefung).TotalHours < 20) return;
            var disp = Application.Current.Dispatcher;
            Task.Factory.StartNew(() =>
            {
                try
                {
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                    using (var w = new WebClient())
                    {
                        w.Headers[HttpRequestHeader.UserAgent] = "Workbench";
                        return w.DownloadString(VersionUrl).Trim();
                    }
                }
                catch { return null; }
            }).ContinueWith(t =>
            {
                disp.BeginInvoke(new Action(() =>
                {
                    c.LetztePruefung = DateTime.UtcNow;
                    if (t.Result != null) c.NeueVersion = t.Result;
                    c.Speichern();
                    if (Neuer(t.Result) && Gefunden != null) Gefunden();
                }));
            });
        }
    }
}
