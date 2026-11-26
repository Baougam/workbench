using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace Workbench
{
    /// <summary>
    /// Ordner lesen ohne Umweg: FindFirstFileEx mit „Basic“-Infos und Large-Fetch (weniger Systemaufrufe, keine kurzen Namen),
    /// Versteckt/System gleich aus denselben Daten – keine zweite Abfrage je Datei. Deutlich schneller als Directory.Enumerate*.
    /// </summary>
    static class Schnell
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WIN32_FIND_DATA
        {
            public uint dwFileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME c, a, w;
            public uint hi, lo, r0, r1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string cFileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string alt;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr FindFirstFileEx(string name, int info, out WIN32_FIND_DATA data, int search, IntPtr filter, int flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool FindNextFile(IntPtr h, out WIN32_FIND_DATA data);
        [DllImport("kernel32.dll")] static extern bool FindClose(IntPtr h);

        const uint VERSTECKT = 0x2, SYSTEM = 0x4, ORDNER = 0x10, TEMPORAER = 0x100;

        /// <summary>Liegt überhaupt etwas drin? Ein einziger Aufruf, bricht beim ersten Eintrag ab.</summary>
        public static bool HatInhalt(string ordner)
        {
            WIN32_FIND_DATA d;
            var h = FindFirstFileEx(Path.Combine(ordner, "*"), 1, out d, 0, IntPtr.Zero, 0);
            if (h == new IntPtr(-1)) return false;
            try
            {
                do { if (d.cFileName != "." && d.cFileName != "..") return true; } while (FindNextFile(h, out d));
                return false;
            }
            finally { FindClose(h); }
        }

        class Stand { public DateTime Geaendert; public List<string> Dirs, Files; }
        static readonly Dictionary<string, Stand> listen = new Dictionary<string, Stand>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Inhalt eines Ordners – aus dem Gedächtnis, solange sich der Ordner nicht geändert hat (Windows setzt seinen
        /// Änderungszeitpunkt bei jedem Hinzufügen/Löschen/Umbenennen darin). Eine Abfrage statt einer Liste.
        /// </summary>
        public static void Liste(string ordner, out List<string> dirs, out List<string> files)
        {
            DateTime t;
            try { t = Directory.GetLastWriteTimeUtc(ordner); } catch { t = DateTime.MinValue; }
            Stand st;
            lock (listen) listen.TryGetValue(ordner, out st);
            if (st != null && st.Geaendert == t && t != DateTime.MinValue) { dirs = st.Dirs; files = st.Files; return; }
            if (!Lesen(ordner, out dirs, out files)) { dirs = new List<string>(); files = new List<string>(); return; }
            lock (listen)
            {
                if (listen.Count > 300) listen.Clear();
                listen[ordner] = new Stand { Geaendert = t, Dirs = dirs, Files = files };
            }
        }

        /// <summary>Im Hintergrund vorlesen (z. B. beim Darüberfahren) – das Hineingehen ist dann sofort da.</summary>
        public static void Vorlesen(string ordner)
        {
            System.Threading.ThreadPool.QueueUserWorkItem(_ => { try { List<string> d, f; Liste(ordner, out d, out f); } catch { } });
        }

        public static bool Lesen(string ordner, out List<string> dirs, out List<string> files)
        {
            dirs = new List<string>(); files = new List<string>();
            WIN32_FIND_DATA d;
            var h = FindFirstFileEx(Path.Combine(ordner, "*"), 1 /* Basic */, out d, 0, IntPtr.Zero, 2 /* LARGE_FETCH */);
            if (h == new IntPtr(-1)) return false;
            try
            {
                do
                {
                    string n = d.cFileName;
                    if (n == "." || n == "..") continue;
                    if ((d.dwFileAttributes & (VERSTECKT | SYSTEM | TEMPORAER)) != 0) continue;
                    string p = Path.Combine(ordner, n);
                    if ((d.dwFileAttributes & ORDNER) != 0) dirs.Add(p);
                    else if (!Engine.IstHilfsName(n)) files.Add(p);
                } while (FindNextFile(h, out d));
            }
            finally { FindClose(h); }
            dirs.Sort(StringComparer.CurrentCultureIgnoreCase);
            files.Sort(StringComparer.CurrentCultureIgnoreCase);
            return true;
        }
    }
}
