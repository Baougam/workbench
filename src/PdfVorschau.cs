using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Workbench
{
    /// <summary>
    /// Erste Seite eines PDFs als Bild – mit dem PDF-Renderer, den Windows 10/11 selbst mitbringt (kein Zusatzprogramm).
    /// Läuft nur im Vorschau-Hintergrundfaden, Ergebnis wird auf der Platte gemerkt (%LOCALAPPDATA%\Workbench\vorschau):
    /// jede Datei wird höchstens einmal gerendert, danach kostet sie praktisch nichts.
    /// </summary>
    static class PdfVorschau
    {
        static string Ordner { get { return Path.Combine(Paths.Daten, "vorschau"); } }

        public static BitmapSource Seite(string pfad, int px)
        {
            try
            {
                var fi = new FileInfo(pfad);
                if (!fi.Exists || fi.Length > 400L << 20) return null;
                string datei = Path.Combine(Ordner, Hash(pfad.ToLowerInvariant() + "|" + fi.LastWriteTimeUtc.Ticks + "|" + px) + ".png");
                byte[] png = File.Exists(datei) ? File.ReadAllBytes(datei) : null;
                if (png == null)
                {
                    png = Rendern(pfad, px);
                    if (png == null) return null;
                    try { Directory.CreateDirectory(Ordner); File.WriteAllBytes(datei, png); Aufraeumen(); } catch { }
                }
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.StreamSource = new MemoryStream(png);
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch { return null; }
        }

        static T Warten<T>(IAsyncOperation<T> op)
        {
            var bis = DateTime.UtcNow.AddSeconds(8);
            while (op.Status == AsyncStatus.Started && DateTime.UtcNow < bis) Thread.Sleep(3);
            return op.Status == AsyncStatus.Completed ? op.GetResults() : default(T);
        }

        static bool Warten(IAsyncAction op)
        {
            var bis = DateTime.UtcNow.AddSeconds(8);
            while (op.Status == AsyncStatus.Started && DateTime.UtcNow < bis) Thread.Sleep(3);
            return op.Status == AsyncStatus.Completed;
        }

        static byte[] Rendern(string pfad, int px)
        {
            var f = Warten(StorageFile.GetFileFromPathAsync(pfad));
            if (f == null) return null;
            var doc = Warten(PdfDocument.LoadFromFileAsync(f));
            if (doc == null || doc.PageCount == 0) return null;
            using (var seite = doc.GetPage(0))
            using (var mem = new InMemoryRandomAccessStream())
            {
                var opt = new PdfPageRenderOptions { DestinationWidth = (uint)px };
                if (!Warten(seite.RenderToStreamAsync(mem, opt))) return null;
                var bytes = new byte[mem.Size];
                var rd = new DataReader(mem.GetInputStreamAt(0));
                Warten(rd.LoadAsync((uint)mem.Size));
                rd.ReadBytes(bytes);
                return bytes;
            }
        }

        static string Hash(string s)
        {
            using (var sha = SHA1.Create())
            {
                var b = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
                var sb = new StringBuilder();
                for (int i = 0; i < 10; i++) sb.Append(b[i].ToString("x2"));
                return sb.ToString();
            }
        }

        static DateTime zuletztAufgeraeumt;
        /// <summary>Höchstens 600 Vorschauen behalten (die ältesten fallen weg).</summary>
        static void Aufraeumen()
        {
            if ((DateTime.UtcNow - zuletztAufgeraeumt).TotalHours < 6) return;
            zuletztAufgeraeumt = DateTime.UtcNow;
            var d = new DirectoryInfo(Ordner).GetFiles("*.png");
            if (d.Length <= 600) return;
            Array.Sort(d, (a, b) => a.LastAccessTimeUtc.CompareTo(b.LastAccessTimeUtc));
            for (int i = 0; i < d.Length - 600; i++) try { d[i].Delete(); } catch { }
        }
    }
}
