using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Workbench
{
    static class Native
    {
        // ---------- Fenster / DWM ----------
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);
        [StructLayout(LayoutKind.Sequential)] public struct MARGINS { public int l, r, t, b; }
        [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS m);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);

        public static int Build { get { return Environment.OSVersion.Version.Build; } }
        public static bool KannMica { get { return Environment.OSVersion.Version.Major >= 10 && Build >= 22621; } }

        public static void DunklesFenster(IntPtr h, bool mica)
        {
            int an = 1;
            DwmSetWindowAttribute(h, 20, ref an, 4);              // DWMWA_USE_IMMERSIVE_DARK_MODE
            int rund = 2;
            DwmSetWindowAttribute(h, 33, ref rund, 4);            // DWMWA_WINDOW_CORNER_PREFERENCE = ROUND
            if (mica)
            {
                var m = new MARGINS { l = -1, r = -1, t = -1, b = -1 };
                DwmExtendFrameIntoClientArea(h, ref m);
                int typ = 2;                                       // DWMSBT_MAINWINDOW (Mica)
                DwmSetWindowAttribute(h, 38, ref typ, 4);
            }
        }

        public static void RahmenFarbe(IntPtr h, int bgr)
        {
            DwmSetWindowAttribute(h, 34, ref bgr, 4);             // DWMWA_BORDER_COLOR
        }

        /// <summary>Fenster nimmt beim Anklicken nicht den Fokus und erscheint nicht in Alt+Tab.</summary>
        public static void NichtAktivieren(IntPtr h)
        {
            const int GWL_EXSTYLE = -20, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x80;
            SetWindowLong(h, GWL_EXSTYLE, GetWindowLong(h, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        }

        // ---------- Tastenkürzel ----------
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
        public const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_NOREPEAT = 0x4000;

        // ---------- Ordner ----------
        [DllImport("shell32.dll")] static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);

        public static string DownloadsOrdner()
        {
            IntPtr p;
            if (SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero, out p) == 0)
            {
                string s = Marshal.PtrToStringUni(p);
                Marshal.FreeCoTaskMem(p);
                if (!string.IsNullOrEmpty(s)) return s;
            }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }

        // ---------- Papierkorb ----------
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            public string pFrom;
            public string pTo;
            public ushort fFlags;
            public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public string lpszProgressTitle;
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

        /// <summary>Legt Datei oder Ordner still in den Papierkorb. Wirft bei Fehler (z. B. Datei ist gerade offen).</summary>
        public static void InPapierkorb(string pfad)
        {
            const uint FO_DELETE = 3;
            const ushort FOF_SILENT = 0x4, FOF_NOCONFIRMATION = 0x10, FOF_ALLOWUNDO = 0x40, FOF_NOERRORUI = 0x400;
            var op = new SHFILEOPSTRUCT
            {
                wFunc = FO_DELETE,
                pFrom = pfad + "\0\0",
                fFlags = (ushort)(FOF_SILENT | FOF_NOCONFIRMATION | FOF_ALLOWUNDO | FOF_NOERRORUI)
            };
            int r = SHFileOperation(ref op);
            if (r != 0 || op.fAnyOperationsAborted || File.Exists(pfad) || Directory.Exists(pfad))
                throw new IOException("Papierkorb: Code " + r);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHEmptyRecycleBin(IntPtr hwnd, string root, uint flags);
        public static void PapierkorbLeeren()
        {
            SHEmptyRecycleBin(IntPtr.Zero, null, 0x1 | 0x2 | 0x4);   // ohne Rückfrage, ohne Fortschritt, ohne Ton (Rückfrage macht Workbench selbst)
        }

        [DllImport("shell32.dll")] static extern void SHChangeNotify(int id, uint flags, IntPtr a, IntPtr b);
        [DllImport("shell32.dll")] static extern int SHUpdateRecycleBinIcon();
        public static void ShellAktualisieren()
        {
            try { SHUpdateRecycleBinIcon(); SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); } catch { }
        }

        // ---------- Vorschaubilder ----------
        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItemImageFactory { [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm); }
        [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);
        [StructLayout(LayoutKind.Sequential)] struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }
        [DllImport("gdi32.dll")] static extern int GetObject(IntPtr h, int size, ref BITMAP bm);
        [StructLayout(LayoutKind.Sequential)] struct BITMAPINFOHEADER { public int biSize, biWidth, biHeight; public short biPlanes, biBitCount; public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant; }
        [DllImport("gdi32.dll")] static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, [Out] byte[] bits, ref BITMAPINFOHEADER bmi, uint usage);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);

        /// <summary>Echtes Windows-Vorschaubild (Bilder, PDFs …) oder das Programmsymbol. Muss auf einem STA-Thread laufen.</summary>
        public static BitmapSource Vorschau(string pfad, int px) { return Vorschau(pfad, px, false); }

        public static BitmapSource Vorschau(string pfad, int px, bool nurSymbol) { return Vorschau(pfad, px, nurSymbol ? 0x4 : 0); }

        /// <summary>flags: 0 = Vorschaubild oder Symbol, 0x4 = nur Symbol, 0x8 = nur echtes Vorschaubild (sonst null).</summary>
        public static BitmapSource Vorschau(string pfad, int px, int flags)
        {
            IShellItemImageFactory f;
            try { SHCreateItemFromParsingName(pfad, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out f); }
            catch { return null; }
            IntPtr hbm;
            int hr;
            try { hr = f.GetImage(new SIZE { cx = px, cy = px }, flags, out hbm); }   // 0x4 ICONONLY, 0x8 THUMBNAILONLY
            finally { Marshal.ReleaseComObject(f); }
            if (hr != 0 || hbm == IntPtr.Zero) return null;
            try
            {
                var bm = new BITMAP();
                GetObject(hbm, Marshal.SizeOf(typeof(BITMAP)), ref bm);
                int w = bm.bmWidth, h = Math.Abs(bm.bmHeight);
                if (w <= 0 || h <= 0) return null;
                var bi = new BITMAPINFOHEADER { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
                var px2 = new byte[w * h * 4];
                IntPtr dc = GetDC(IntPtr.Zero);
                GetDIBits(dc, hbm, 0, (uint)h, px2, ref bi, 0);
                ReleaseDC(IntPtr.Zero, dc);
                bool alpha = false;
                for (int i = 3; i < px2.Length; i += 4) if (px2[i] != 0) { alpha = true; break; }
                if (!alpha) for (int i = 3; i < px2.Length; i += 4) px2[i] = 255;
                var bs = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, px2, w * 4);
                bs.Freeze();
                return bs;
            }
            finally { DeleteObject(hbm); }
        }
    }
}
