using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace Workbench
{
    static class Program
    {
        public const string Version = "0.2.0";
        public const string MutexName = "Local\\Workbench.Instanz";
        public const string ZeigenName = "Local\\Workbench.Zeigen";
        public const string BeendenName = "Local\\Workbench.Beenden";
        public const string AuftragName = "Local\\Workbench.Auftrag";

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int pid);

        static int BrowserHost()
        {
            try
            {
                string auftrag = Browser.NachrichtLesen(Console.OpenStandardInput());
                using (var aus = Console.OpenStandardOutput()) Browser.Antworten(aus);
                if (auftrag == null) return 0;
                Auftraege.Schreiben(auftrag);
                EventWaitHandle ev;
                if (EventWaitHandle.TryOpenExisting(AuftragName, out ev)) { using (ev) ev.Set(); }
                else if (File.Exists(Install.Ziel)) System.Diagnostics.Process.Start(Install.Ziel, "--background");   // Workbench lief nicht: starten, holt den Auftrag ab
                return 0;
            }
            catch (Exception ex) { Log.Fehler("BrowserHost", ex); return 1; }
        }

        [STAThread]
        static int Main(string[] args)
        {
            // Scharfe Darstellung je Monitor (Windows-Skalierung pro Bildschirm), ohne extra .config-Datei.
            AppContext.SetSwitch("Switch.System.Windows.DoNotScaleForDpiChanges", false);
            string modus = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            try
            {
                if (modus == "--selftest") return SelfTest.Run();
                if (modus == "--render") return Render.Run(args.Length > 1 ? args[1] : ".");
                if (modus == "--messen") return Render.Messen(args.Length > 1 ? args[1] : ".", args.Length > 2 ? args[2] : ".");
                // Browser-Erweiterung (Native Messaging): eine Nachricht lesen, als Auftrag einstellen, antworten, fertig.
                if (modus.StartsWith("chrome-extension://")) return BrowserHost();
                if (modus == "--uninstall") { Install.Deinstallieren(args.Length > 1 && args[1] == "--still"); return 0; }

                // Aufträge von außen: Rechtsklick › Workbench › 1 Tag (--frist "1 day" "C:\…") oder Taskleisten-Sprungliste (--zeige 1)
                string auftrag = null;
                if (modus == "--frist" && args.Length > 2) auftrag = "frist\t" + args[1] + "\t" + System.IO.Path.GetFullPath(args[2]);
                if (modus == "--zeige" && args.Length > 1) auftrag = "zeige\t" + args[1];

                // Erster Start aus dem Download-Ordner o. ä.: sich selbst installieren und die installierte Kopie starten.
                if (modus != "--portable" && auftrag == null && !Install.IstInstallierteKopie())
                {
                    if (Install.Installieren()) return 0;
                }

                bool neu;
                var mutex = new Mutex(true, MutexName, out neu);
                // nach einem Update: kurz warten, bis die alte Version Platz gemacht hat
                for (int i = 0; !neu && modus == "--nach-update" && i < 50; i++)
                {
                    mutex.Dispose(); Thread.Sleep(150);
                    mutex = new Mutex(true, MutexName, out neu);
                }
                if (!neu)
                {
                    AllowSetForegroundWindow(-1);   // die laufende Workbench darf ihr Fenster nach vorn holen
                    if (auftrag != null)
                    {
                        Auftraege.Schreiben(auftrag);
                        using (var ev = new EventWaitHandle(false, EventResetMode.AutoReset, AuftragName)) ev.Set();
                    }
                    else using (var zeigen = new EventWaitHandle(false, EventResetMode.AutoReset, ZeigenName)) zeigen.Set();
                    return 0;
                }
                if (auftrag != null) Auftraege.Schreiben(auftrag);
                // Schnellerer Start: .NET merkt sich, was beim Start übersetzt wird, und macht es beim nächsten Mal parallel vorab.
                try { Directory.CreateDirectory(Paths.Daten); System.Runtime.ProfileOptimization.SetProfileRoot(Paths.Daten); System.Runtime.ProfileOptimization.StartProfile("start.profil"); } catch { }
                var app = new WbApp(modus == "--background" || modus == "--frist");
                app.Run();
                GC.KeepAlive(mutex);
                return 0;
            }
            catch (Exception ex)
            {
                Log.Fehler("Main", ex);
                if (modus != "--background")
                    MessageBox.Show("Workbench couldn't start:\n\n" + ex.Message, "Workbench", MessageBoxButton.OK, MessageBoxImage.Warning);
                return 1;
            }
        }
    }

    /// <summary>Kleine Warteschlange für Aufträge von außen (mehrere markierte Dateien = mehrere Aufrufe kurz nacheinander).</summary>
    static class Auftraege
    {
        static string Datei { get { return Path.Combine(Paths.Daten, "auftraege.txt"); } }

        public static void Schreiben(string zeile)
        {
            Directory.CreateDirectory(Paths.Daten);
            for (int i = 0; i < 20; i++)
            {
                try { using (var m = new Mutex(false, "Local\\Workbench.Auftraege")) { m.WaitOne(2000); try { File.AppendAllText(Datei, zeile + Environment.NewLine); } finally { m.ReleaseMutex(); } } return; }
                catch { Thread.Sleep(50); }
            }
        }

        public static string[] Holen()
        {
            try
            {
                using (var m = new Mutex(false, "Local\\Workbench.Auftraege"))
                {
                    m.WaitOne(2000);
                    try
                    {
                        if (!File.Exists(Datei)) return new string[0];
                        var z = File.ReadAllLines(Datei);
                        File.Delete(Datei);
                        return z;
                    }
                    finally { m.ReleaseMutex(); }
                }
            }
            catch { return new string[0]; }
        }
    }

    static class Log
    {
        static readonly object sperre = new object();
        public static void Info(string text)
        {
            try
            {
                lock (sperre)
                {
                    Directory.CreateDirectory(Paths.Daten);
                    string datei = Path.Combine(Paths.Daten, "log.txt");
                    var fi = new FileInfo(datei);
                    if (fi.Exists && fi.Length > 512 * 1024) File.Delete(datei);
                    File.AppendAllText(datei, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + text + Environment.NewLine);
                }
            }
            catch { }
        }
        public static void Fehler(string wo, Exception ex) { Info("FEHLER " + wo + ": " + ex); }
    }
}
