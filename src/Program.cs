using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace Workbench
{
    static class Program
    {
        public const string Version = "0.1.0";
        public const string MutexName = "Local\\Workbench.Instanz";
        public const string ZeigenName = "Local\\Workbench.Zeigen";
        public const string BeendenName = "Local\\Workbench.Beenden";

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int pid);

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
                if (modus == "--uninstall") { Install.Deinstallieren(args.Length > 1 && args[1] == "--still"); return 0; }

                // Erster Start aus dem Download-Ordner o. ä.: sich selbst installieren und die installierte Kopie starten.
                if (modus != "--portable" && !Install.IstInstallierteKopie())
                {
                    if (Install.Installieren()) return 0;
                }

                bool neu;
                var mutex = new Mutex(true, MutexName, out neu);
                if (!neu)
                {
                    AllowSetForegroundWindow(-1);   // die laufende Workbench darf ihr Fenster nach vorn holen
                    using (var zeigen = new EventWaitHandle(false, EventResetMode.AutoReset, ZeigenName)) zeigen.Set();
                    return 0;
                }
                var app = new WbApp(modus == "--background");
                app.Run();
                GC.KeepAlive(mutex);
                return 0;
            }
            catch (Exception ex)
            {
                Log.Fehler("Main", ex);
                if (modus != "--background")
                    MessageBox.Show("Workbench konnte nicht starten:\n\n" + ex.Message, "Workbench", MessageBoxButton.OK, MessageBoxImage.Warning);
                return 1;
            }
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
