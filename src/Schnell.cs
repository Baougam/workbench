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
