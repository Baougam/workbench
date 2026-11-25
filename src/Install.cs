using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using Microsoft.Win32;

namespace Workbench
{
    /// <summary>
    /// Die .exe ist ihr eigener Installer: beim ersten Start kopiert sie sich nach %LOCALAPPDATA%\Programs\Workbench, legt Desktop-
    /// und Startmenü-Symbol an, startet mit Windows, erscheint in „Apps“ zum Entfernen und links im Explorer. Alles ohne Adminrechte.
    /// </summary>
    static class Install
    {
        const string NsGuid = "{6F3C1A52-8E0B-4C77-9B1E-3D2A5F7C9B41}";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Workbench";

        static string Exe { get { return Assembly.GetExecutingAssembly().Location; } }
        public static string Ziel { get { return Path.Combine(Paths.Programm, "Workbench.exe"); } }

        public static bool IstInstallierteKopie()
        {
            return Paths.Gleich(Path.GetDirectoryName(Exe), Paths.Programm);
        }

        public static bool Installieren()
        {
            try
            {
                // Läuft schon eine (ältere) Workbench? Freundlich beenden, dann ersetzen.
                try { using (var ev = EventWaitHandle.OpenExisting(Program.BeendenName)) ev.Set(); } catch { }
                Directory.CreateDirectory(Paths.Programm);
                Exception letzter = null;
                for (int i = 0; i < 50; i++)
                {
                    try { File.Copy(Exe, Ziel, true); letzter = null; break; }
                    catch (Exception ex) { letzter = ex; Thread.Sleep(100); }
                }
                if (letzter != null) throw letzter;

                Paths.OrdnerAnlegen();
                var cfg = Einstellungen.Laden();
                Verknuepfungen();
                Autostart(cfg.Autostart);
                Seitenleiste(cfg.Seitenleiste);
                Rechtsklick(cfg.Rechtsklick);
                Eintragen();
                Log.Info("Installiert: " + Ziel);
                Process.Start(Ziel);
                return true;
            }
            catch (Exception ex)
            {
                Log.Fehler("Installieren", ex);
                return false;   // dann läuft Workbench einfach von hier
            }
        }

        static void Verknuepfungen()
        {
            if (Paths.Test) return;
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string start = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs));
            Verknuepfung(Path.Combine(desktop, "Workbench.lnk"));
            Verknuepfung(Path.Combine(start, "Workbench.lnk"));
        }

        static void Verknuepfung(string lnk)
        {
            try
            {
                var t = Type.GetTypeFromProgID("WScript.Shell");
                dynamic sh = Activator.CreateInstance(t);
                dynamic s = sh.CreateShortcut(lnk);
                s.TargetPath = Ziel;
                s.WorkingDirectory = Paths.Programm;
                s.IconLocation = Ziel + ",0";
                s.Description = "Workbench — downloads and files that clean up after themselves";
                s.Save();
            }
            catch (Exception ex) { Log.Fehler("Verknüpfung " + lnk, ex); }
        }

        public static void Autostart(bool an)
        {
            if (Paths.Test) return;
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (an) k.SetValue("Workbench", "\"" + Ziel + "\" --background");
                    else k.DeleteValue("Workbench", false);
                }
            }
            catch (Exception ex) { Log.Fehler("Autostart", ex); }
        }

        /// <summary>„Workbench“ links im Explorer-Navigationsbereich – damit auch in jedem Speichern-Fenster (Word, Photoshop …).</summary>
        public static void Seitenleiste(bool an)
        {
            if (Paths.Test) return;
            string clsid = @"Software\Classes\CLSID\" + NsGuid;
            string ns = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace\" + NsGuid;
            string versteckt = @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel";
            try
            {
                if (!an)
                {
                    Registry.CurrentUser.DeleteSubKeyTree(clsid, false);
                    Registry.CurrentUser.DeleteSubKeyTree(ns, false);
                    using (var k = Registry.CurrentUser.OpenSubKey(versteckt, true)) if (k != null) k.DeleteValue(NsGuid, false);
                }
                else
                {
                    using (var k = Registry.CurrentUser.CreateSubKey(clsid))
                    {
                        k.SetValue("", "Workbench");
                        k.SetValue("System.IsPinnedToNameSpaceTree", 1, RegistryValueKind.DWord);
                        k.SetValue("SortOrderIndex", 0x42, RegistryValueKind.DWord);
                        using (var i = k.CreateSubKey("DefaultIcon")) i.SetValue("", Ziel + ",0");
                        using (var i = k.CreateSubKey("InProcServer32")) i.SetValue("", @"%SystemRoot%\system32\shell32.dll", RegistryValueKind.ExpandString);
                        using (var i = k.CreateSubKey("Instance"))
                        {
                            i.SetValue("CLSID", "{0E5AAE11-A475-4c5b-AB00-C66DE400274E}");
                            using (var b = i.CreateSubKey("InitPropertyBag"))
                            {
                                b.SetValue("Attributes", 0x11, RegistryValueKind.DWord);
                                b.SetValue("TargetFolderPath", Paths.Wurzel);
                            }
                        }
                        using (var f = k.CreateSubKey("ShellFolder"))
                        {
                            f.SetValue("FolderValueFlags", 0x28, RegistryValueKind.DWord);
                            f.SetValue("Attributes", unchecked((int)0xF080004D), RegistryValueKind.DWord);
                        }
                    }
                    using (var k = Registry.CurrentUser.CreateSubKey(ns)) k.SetValue("", "Workbench");
                    using (var k = Registry.CurrentUser.CreateSubKey(versteckt)) k.SetValue(NsGuid, 1, RegistryValueKind.DWord);
                    OrdnerSymbol();
                }
                Native.ShellAktualisieren();
            }
            catch (Exception ex) { Log.Fehler("Seitenleiste", ex); }
        }

        static void OrdnerSymbol()
        {
            try
            {
                string ini = Path.Combine(Paths.Wurzel, "desktop.ini");
                if (File.Exists(ini)) File.SetAttributes(ini, FileAttributes.Normal);
                File.WriteAllText(ini, "[.ShellClassInfo]\r\nIconResource=" + Ziel + ",0\r\nInfoTip=Files that clean up after themselves\r\n");
                File.SetAttributes(ini, FileAttributes.Hidden | FileAttributes.System);
                var d = new DirectoryInfo(Paths.Wurzel);
                d.Attributes |= FileAttributes.ReadOnly;
            }
            catch (Exception ex) { Log.Fehler("Ordnersymbol", ex); }
        }

        static void Eintragen()
        {
            if (Paths.Test) return;
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
                {
                    k.SetValue("DisplayName", "Workbench");
                    k.SetValue("DisplayVersion", Program.Version);
                    k.SetValue("Publisher", "Workbench");
                    k.SetValue("DisplayIcon", Ziel + ",0");
                    k.SetValue("InstallLocation", Paths.Programm);
                    k.SetValue("UninstallString", "\"" + Ziel + "\" --uninstall");
                    k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    k.SetValue("EstimatedSize", (int)(new FileInfo(Ziel).Length / 1024), RegistryValueKind.DWord);
                }
            }
            catch (Exception ex) { Log.Fehler("Eintragen", ex); }
        }

        /// <summary>Beim Start der installierten Kopie: Einträge auffrischen (falls etwas fehlt).</summary>
        public static void Pruefen(Einstellungen cfg)
        {
            if (Paths.Test || !IstInstallierteKopie()) return;
            Autostart(cfg.Autostart);
            Seitenleiste(cfg.Seitenleiste);
            Rechtsklick(cfg.Rechtsklick);
        }

        /// <summary>
        /// Rechtsklick auf jede Datei / jeden Ordner › Workbench › 1 Stunde … Für immer. (Windows 11: unter „Weitere Optionen anzeigen“.)
        /// </summary>
        public static void Rechtsklick(bool an)
        {
            if (Paths.Test) return;
            foreach (var art in new[] { "*", "Directory" })
            {
                string basis = @"Software\Classes\" + art + @"\shell\Workbench";
                try
                {
                    Registry.CurrentUser.DeleteSubKeyTree(basis, false);
                    if (!an) continue;
                    using (var k = Registry.CurrentUser.CreateSubKey(basis))
                    {
                        k.SetValue("MUIVerb", "Workbench");
                        k.SetValue("Icon", Ziel + ",0");
                        k.SetValue("SubCommands", "");
                        string[][] eintraege = { new[] { "a1", "For an hour", "1 hour" }, new[] { "a2", "For a day", "1 day" }, new[] { "a3", "For a week", "1 week" }, new[] { "a4", "For a month", "1 month" }, new[] { "a5", "Forever", Dauer.Immer } };
                        foreach (var e in eintraege)
                            using (var s = k.CreateSubKey(@"shell\" + e[0]))
                            {
                                s.SetValue("MUIVerb", e[1]);
                                if (e[0] == "a5") s.SetValue("CommandFlags", 0x20, RegistryValueKind.DWord);   // Trennlinie davor
                                using (var c = s.CreateSubKey("command")) c.SetValue("", "\"" + Ziel + "\" --frist \"" + e[2] + "\" \"%1\"");
                            }
                    }
                }
                catch (Exception ex) { Log.Fehler("Rechtsklick", ex); }
            }
        }

        public static void DesktopSymbol(bool an)
        {
            if (Paths.Test) return;
            string lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Workbench.lnk");
            try { if (an) { if (!File.Exists(lnk)) Verknuepfung(lnk); } else if (File.Exists(lnk)) File.Delete(lnk); }
            catch (Exception ex) { Log.Fehler("Desktop-Symbol", ex); }
        }

        public static bool HatDesktopSymbol
        {
            get { return File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Workbench.lnk")); }
        }

        public static void Deinstallieren(bool still)
        {
            if (!still && MessageBox.Show("Remove Workbench?\n\nYour files stay where they are, including everything in the Workbench folder.", "Workbench", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            if (!Paths.Test)
            {
                Autostart(false);
                Seitenleiste(false);
                Rechtsklick(false);
                Browser.Abmelden();
                try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
                foreach (var lnk in new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Workbench.lnk"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Workbench.lnk")
                })
                    try { if (File.Exists(lnk)) File.Delete(lnk); } catch { }
                try
                {
                    string ini = Path.Combine(Paths.Wurzel, "desktop.ini");
                    if (File.Exists(ini)) { File.SetAttributes(ini, FileAttributes.Normal); File.Delete(ini); }
                }
                catch { }
            }
            // laufende Workbench beenden (falls wir es nicht selbst sind)
            try { using (var ev = EventWaitHandle.OpenExisting(Program.BeendenName)) ev.Set(); } catch { }
            // Programmordner kurz nach dem Beenden löschen
            if (!Paths.Test)
            {
                try
                {
                    Process.Start(new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"" + Paths.Programm + "\"")
                    { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
                }
                catch { }
            }
            if (Application.Current != null) Application.Current.Shutdown();
        }
    }
}
