using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;

namespace Workbench
{
    class BinEintrag
    {
        public string Name, Original, RPfad, IPfad;
        public DateTime Geloescht;   // lokal
        public long Groesse;
        public bool IstOrdner;
        public Recycelt Wb;          // gesetzt, wenn Workbench die Datei hineingelegt hat
    }

    /// <summary>
    /// Liest den echten Windows-Papierkorb direkt ($Recycle.Bin\&lt;SID&gt;\$I…/$R…). So gehen Wiederherstellen und Löschen
    /// still und genau – ohne Explorer-Dialoge.
    /// </summary>
    static class Papierkorb
    {
        public static string TestOrdner;   // Selbsttest: eigener Ordner statt echter Papierkorb

        static IEnumerable<string> Ordner()
        {
            if (TestOrdner != null) { yield return TestOrdner; yield break; }
            string sid = WindowsIdentity.GetCurrent().User.Value;
            foreach (var d in DriveInfo.GetDrives())
            {
                bool ok;
                try { ok = d.IsReady && d.DriveType == DriveType.Fixed; } catch { ok = false; }
                if (!ok) continue;
                string o = Path.Combine(d.RootDirectory.FullName, "$Recycle.Bin", sid);
                if (Directory.Exists(o)) yield return o;
            }
        }

        public static List<BinEintrag> Lesen(Store s)
        {
            var liste = new List<BinEintrag>();
            foreach (var o in Ordner())
            {
                string[] infos;
                try { infos = Directory.GetFiles(o, "$I*"); } catch { continue; }
                foreach (var i in infos)
                {
                    try
                    {
                        var b = File.ReadAllBytes(i);
                        if (b.Length < 28) continue;
                        long ver = BitConverter.ToInt64(b, 0);
                        long groesse = BitConverter.ToInt64(b, 8);
                        long zeit = BitConverter.ToInt64(b, 16);
                        string pfad;
                        if (ver == 1) pfad = Encoding.Unicode.GetString(b, 24, Math.Min(520, b.Length - 24));
                        else { int n = BitConverter.ToInt32(b, 24); pfad = Encoding.Unicode.GetString(b, 28, Math.Min(n * 2, b.Length - 28)); }
                        int nul = pfad.IndexOf('\0');
                        if (nul >= 0) pfad = pfad.Substring(0, nul);
                        string r = Path.Combine(o, "$R" + Path.GetFileName(i).Substring(2));
                        bool istOrdner = Directory.Exists(r);
                        if (!istOrdner && !File.Exists(r)) continue;
                        liste.Add(new BinEintrag
                        {
                            Name = Path.GetFileName(pfad),
                            Original = pfad,
                            RPfad = r,
                            IPfad = i,
                            Geloescht = DateTime.FromFileTime(zeit),
                            Groesse = groesse,
                            IstOrdner = istOrdner
                        });
                    }
                    catch { }
                }
            }
            // Workbench-Herkunft zuordnen
            if (s != null)
            {
                foreach (var e in liste)
                {
                    Recycelt best = null;
                    foreach (var r in s.D.Papierkorb)
                        if (Paths.Gleich(r.Pfad, e.Original) && (best == null || Math.Abs((r.Am.ToLocalTime() - e.Geloescht).TotalMinutes) < Math.Abs((best.Am.ToLocalTime() - e.Geloescht).TotalMinutes)))
                            best = r;
                    if (best != null && Math.Abs((best.Am.ToLocalTime() - e.Geloescht).TotalMinutes) < 10) e.Wb = best;
                }
            }
            return liste.OrderByDescending(e => e.Geloescht).ToList();
        }

        /// <summary>Zurück an den alten Platz. Ist dort schon etwas, bekommt die Datei einen freien Namen.</summary>
        public static string Wiederherstellen(BinEintrag e)
        {
            string ordner = Path.GetDirectoryName(e.Original);
            Directory.CreateDirectory(ordner);
            string ziel = Paths.FreierName(ordner, e.Name);
            if (e.IstOrdner) Directory.Move(e.RPfad, ziel); else File.Move(e.RPfad, ziel);
            try { File.Delete(e.IPfad); } catch { }
            Native.ShellAktualisieren();
            return ziel;
        }

        public static void Loeschen(BinEintrag e)
        {
            if (e.IstOrdner)
            {
                foreach (var f in Directory.GetFiles(e.RPfad, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(e.RPfad, true);
            }
            else
            {
                File.SetAttributes(e.RPfad, FileAttributes.Normal);
                File.Delete(e.RPfad);
            }
            try { File.Delete(e.IPfad); } catch { }
            Native.ShellAktualisieren();
        }
    }
}
