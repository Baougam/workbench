using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Workbench
{
    class CloudOrt
    {
        public string Name;   // „Google Drive“, „OneDrive“ …
        public string Pfad;
    }

    /// <summary>Findet Cloud-Ordner, die Windows schon kennt: Google Drive für Desktop, OneDrive, Dropbox, iCloud.</summary>
    static class Cloud
    {
        public static List<CloudOrt> Finden()
        {
            var l = new List<CloudOrt>();
            if (Paths.Test)
            {
                string t = Path.Combine(Path.GetDirectoryName(Paths.Wurzel), "Google Drive", "Meine Ablage");
                if (Directory.Exists(t)) l.Add(new CloudOrt { Name = "Google Drive", Pfad = t });
                return l;
            }
            // Google Drive für Desktop hängt sich als eigenes Laufwerk ein (Bezeichnung „Google Drive“).
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (!d.IsReady) continue;
                    if (d.VolumeLabel.IndexOf("Google Drive", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    foreach (var n in new[] { "Meine Ablage", "My Drive" })
                    {
                        string p = Path.Combine(d.RootDirectory.FullName, n);
                        if (Directory.Exists(p)) { l.Add(new CloudOrt { Name = "Google Drive", Pfad = p }); break; }
                    }
                }
                catch { }
            }
            string profil = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (var n in new[] { "Google Drive", "Google Drive\\Meine Ablage", "Google Drive\\My Drive" })
            {
                string p = Path.Combine(profil, n);
                if (Directory.Exists(p) && !l.Exists(x => x.Name == "Google Drive")) { if (!Directory.Exists(Path.Combine(p, "Meine Ablage"))) l.Add(new CloudOrt { Name = "Google Drive", Pfad = p }); }
            }
            foreach (var v in new[] { "OneDriveConsumer", "OneDrive", "OneDriveCommercial" })
            {
                string p = Environment.GetEnvironmentVariable(v);
                if (!string.IsNullOrEmpty(p) && Directory.Exists(p) && !l.Exists(x => Paths.Gleich(x.Pfad, p)))
                    l.Add(new CloudOrt { Name = v == "OneDriveCommercial" ? "OneDrive (Arbeit)" : "OneDrive", Pfad = p });
            }
            foreach (var basis in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) })
            {
                try
                {
                    string info = Path.Combine(basis, "Dropbox", "info.json");
                    if (!File.Exists(info)) continue;
                    var m = Regex.Match(File.ReadAllText(info), "\"path\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
                    if (!m.Success) continue;
                    string p = Regex.Unescape(m.Groups[1].Value);
                    if (Directory.Exists(p) && !l.Exists(x => Paths.Gleich(x.Pfad, p))) l.Add(new CloudOrt { Name = "Dropbox", Pfad = p });
                }
                catch { }
            }
            string icloud = Path.Combine(profil, "iCloudDrive");
            if (Directory.Exists(icloud)) l.Add(new CloudOrt { Name = "iCloud Drive", Pfad = icloud });
            return l;
        }

        /// <summary>Gewählte Cloud aus den Einstellungen, sonst die erste gefundene (Google Drive zuerst).</summary>
        public static CloudOrt Aktuell(Einstellungen c)
        {
            if (!string.IsNullOrEmpty(c.CloudPfad) && Directory.Exists(c.CloudPfad))
                return new CloudOrt { Name = string.IsNullOrEmpty(c.CloudName) ? Path.GetFileName(c.CloudPfad) : c.CloudName, Pfad = c.CloudPfad };
            var l = Finden();
            return l.Count > 0 ? l[0] : null;
        }
    }
}
