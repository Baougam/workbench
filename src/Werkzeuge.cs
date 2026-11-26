using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Workbench
{
    /// <summary>
    /// Werkzeuge per Drop: verkleinern, in JPG, Bilder → PDF, zippen, entpacken, Pfad kopieren, SHA-256.
    /// Alles lokal, ohne Zusatzprogramme. Ergebnisse landen neben dem Original (sonst in Downloads) und werden zurückgegeben,
    /// damit Workbench sie in die Ablage legen kann. Läuft im Hintergrund.
    /// </summary>
    static class Werkzeuge
    {
        public class Art { public string Id, Zeichen, Name, Hilfe; }

        public static readonly Art[] Alle =
        {
            new Art { Id = "shrink", Zeichen = "M4 14v6h6M20 10V4h-6M4 20l7-7M20 4l-7 7", Name = "Smaller", Hilfe = "Images to 1600 px, JPG" },
            new Art { Id = "jpg", Zeichen = "M5 5h14v14H5zM5 15l4-4 3 3 3-3 4 4M14.5 9a1 1 0 1 0 0-.01", Name = "JPG", Hilfe = "Convert images to JPG" },
            new Art { Id = "pdf", Zeichen = "M7 3.5h7l4 4v13H7zM14 3.5v4h4M9.5 13h6M9.5 16.5h4", Name = "PDF", Hilfe = "Images into one PDF" },
            new Art { Id = "zip", Zeichen = "M6 3.5h12v17H6zM12 3.5v2M12 7.5v2M12 11.5v2M10.5 15.5h3v3h-3z", Name = "Zip", Hilfe = "Pack into a .zip" },
            new Art { Id = "unzip", Zeichen = "M4 8.5h16v11H4zM4 8.5l2-4h12l2 4M12 11v6M9.5 14.5L12 17l2.5-2.5", Name = "Unzip", Hilfe = "Extract next to the archive" },
            new Art { Id = "path", Zeichen = "M10 14a4 4 0 0 0 5.7 0l3-3a4 4 0 0 0-5.7-5.7l-1 1M14 10a4 4 0 0 0-5.7 0l-3 3a4 4 0 0 0 5.7 5.7l1-1", Name = "Path", Hilfe = "Copy the full path" },
            new Art { Id = "hash", Zeichen = "M9 4L7.5 20M16.5 4L15 20M4.5 9h16M3.5 15h16", Name = "SHA-256", Hilfe = "Copy the checksum" },
        };

        static readonly string[] bilder = { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".heic", ".heif", ".tif", ".tiff" };
        static bool IstBild(string p) { return bilder.Contains(Path.GetExtension(p).ToLowerInvariant()); }

        public class Ergebnis { public List<string> Dateien = new List<string>(); public string Text, Kopieren; }

        public static Ergebnis Ausfuehren(string id, string[] pfade)
        {
            var r = new Ergebnis();
            switch (id)
            {
                case "shrink":
                    foreach (var p in pfade.Where(IstBild)) { var z = Ziel(p, Path.GetFileNameWithoutExtension(p) + " (small).jpg"); Speichern(Laden(p, 1600), z, 85); r.Dateien.Add(z); }
                    break;
                case "jpg":
                    foreach (var p in pfade.Where(IstBild)) { var z = Ziel(p, Path.GetFileNameWithoutExtension(p) + ".jpg"); Speichern(Laden(p, 0), z, 92); r.Dateien.Add(z); }
                    break;
                case "pdf":
                    {
                        var b = pfade.Where(IstBild).ToList();
                        if (b.Count == 0) break;
                        var z = Ziel(b[0], (b.Count == 1 ? Path.GetFileNameWithoutExtension(b[0]) : "Images") + ".pdf");
                        BilderZuPdf(b, z);
                        r.Dateien.Add(z);
                    }
                    break;
                case "zip":
                    {
                        if (pfade.Length == 0) break;
                        var z = Ziel(pfade[0], (pfade.Length == 1 ? Path.GetFileNameWithoutExtension(pfade[0].TrimEnd('\\')) : "Archive") + ".zip");
                        using (var zip = ZipFile.Open(z, ZipArchiveMode.Create))
                            foreach (var p in pfade)
                            {
                                if (Directory.Exists(p))
                                {
                                    string basis = Path.GetDirectoryName(p.TrimEnd('\\'));
                                    foreach (var f in Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories))
                                        zip.CreateEntryFromFile(f, f.Substring(basis.Length).TrimStart('\\').Replace('\\', '/'), CompressionLevel.Optimal);
                                }
                                else if (File.Exists(p) && !Paths.Gleich(p, z)) zip.CreateEntryFromFile(p, Path.GetFileName(p), CompressionLevel.Optimal);
                            }
                        r.Dateien.Add(z);
                    }
                    break;
                case "unzip":
                    foreach (var p in pfade.Where(x => x.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
                    {
                        string ordner = Paths.FreierName(Path.GetDirectoryName(p), Path.GetFileNameWithoutExtension(p));
                        ZipFile.ExtractToDirectory(p, ordner);
                        r.Dateien.Add(ordner);
                    }
                    break;
                case "path":
                    r.Kopieren = string.Join(Environment.NewLine, pfade);
                    r.Text = pfade.Length == 1 ? "Path copied" : pfade.Length + " paths copied";
                    break;
                case "hash":
                    {
                        var sb = new StringBuilder();
                        foreach (var p in pfade.Where(File.Exists))
                            using (var s = File.OpenRead(p)) using (var sha = SHA256.Create())
                                sb.AppendLine(BitConverter.ToString(sha.ComputeHash(s)).Replace("-", "").ToLowerInvariant() + "  " + Path.GetFileName(p));
                        r.Kopieren = sb.ToString().TrimEnd();
                        r.Text = "SHA-256 copied";
                    }
                    break;
            }
            if (r.Text == null) r.Text = r.Dateien.Count == 0 ? "Nothing to do" : (r.Dateien.Count == 1 ? Path.GetFileName(r.Dateien[0].TrimEnd('\\')) : r.Dateien.Count + " files");
            return r;
        }

        /// <summary>Neben dem Original – wenn dort nicht geschrieben werden darf, in Downloads. Nie überschreiben.</summary>
        static string Ziel(string quelle, string name)
        {
            string ordner = Path.GetDirectoryName(quelle.TrimEnd('\\'));
            try { string t = Path.Combine(ordner, ".wb-" + Guid.NewGuid().ToString("N")); File.WriteAllText(t, ""); File.Delete(t); }
            catch { ordner = Paths.Downloads; }
            return Paths.FreierName(ordner, name);
        }

        static BitmapSource Laden(string p, int max)
        {
            BitmapFrame f;
            using (var s = File.OpenRead(p))
            {
                var d = BitmapDecoder.Create(s, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
                f = d.Frames[0];
            }
            BitmapSource b = f;
            // Drehung aus den Kamera-Daten übernehmen
            try
            {
                var meta = f.Metadata as BitmapMetadata;
                object o = meta != null ? meta.GetQuery("/app1/ifd/{ushort=274}") : null;
                if (o != null)
                {
                    int ori = Convert.ToInt32(o);
                    double winkel = ori == 6 ? 90 : ori == 3 ? 180 : ori == 8 ? 270 : 0;
                    if (winkel != 0) b = new TransformedBitmap(b, new RotateTransform(winkel));
                }
            }
            catch { }
            if (max > 0 && Math.Max(b.PixelWidth, b.PixelHeight) > max)
            {
                double s = (double)max / Math.Max(b.PixelWidth, b.PixelHeight);
                b = new TransformedBitmap(b, new ScaleTransform(s, s));
            }
            // JPEG kennt keine Transparenz: auf Weiß legen
            if (b.Format.Masks != null && b.Format.BitsPerPixel == 32)
            {
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    dc.DrawRectangle(Brushes.White, null, new System.Windows.Rect(0, 0, b.PixelWidth, b.PixelHeight));
                    dc.DrawImage(b, new System.Windows.Rect(0, 0, b.PixelWidth, b.PixelHeight));
                }
                var rt = new RenderTargetBitmap(b.PixelWidth, b.PixelHeight, 96, 96, PixelFormats.Pbgra32);
                rt.Render(dv);
                b = rt;
            }
            b.Freeze();
            return b;
        }

        static void Speichern(BitmapSource b, string z, int qualitaet)
        {
            var enc = new JpegBitmapEncoder { QualityLevel = qualitaet };
            enc.Frames.Add(BitmapFrame.Create(b));
            using (var s = File.Create(z)) enc.Save(s);
        }

        /// <summary>Kleiner PDF-Schreiber: je Bild eine Seite (längste Seite A4), Bild als JPEG eingebettet.</summary>
        static void BilderZuPdf(List<string> bilderListe, string ziel)
        {
            var objekte = new List<byte[]>();
            var seiten = new List<int>();
            Func<string, byte[]> a = t => Encoding.ASCII.GetBytes(t);
            objekte.Add(null); objekte.Add(null);                      // 1 Katalog, 2 Seiten – später
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var p in bilderListe)
            {
                var b = Laden(p, 2400);
                byte[] jpg;
                using (var ms = new MemoryStream()) { var enc = new JpegBitmapEncoder { QualityLevel = 88 }; enc.Frames.Add(BitmapFrame.Create(b)); enc.Save(ms); jpg = ms.ToArray(); }
                double s = 842.0 / Math.Max(b.PixelWidth, b.PixelHeight);
                double w = Math.Round(b.PixelWidth * s, 2), h = Math.Round(b.PixelHeight * s, 2);
                int bildNr = objekte.Count + 1;
                var kopf = a(string.Format(ci, "<</Type/XObject/Subtype/Image/Width {0}/Height {1}/ColorSpace/DeviceRGB/BitsPerComponent 8/Filter/DCTDecode/Length {2}>>\nstream\n", b.PixelWidth, b.PixelHeight, jpg.Length));
                objekte.Add(kopf.Concat(jpg).Concat(a("\nendstream")).ToArray());
                string inhalt = string.Format(ci, "q {0} 0 0 {1} 0 0 cm /Im0 Do Q", w, h);
                int inhaltNr = objekte.Count + 1;
                objekte.Add(a("<</Length " + inhalt.Length + ">>\nstream\n" + inhalt + "\nendstream"));
                int seiteNr = objekte.Count + 1;
                objekte.Add(a(string.Format(ci, "<</Type/Page/Parent 2 0 R/MediaBox[0 0 {0} {1}]/Resources<</XObject<</Im0 {2} 0 R>>>>/Contents {3} 0 R>>", w, h, bildNr, inhaltNr)));
                seiten.Add(seiteNr);
            }
            objekte[0] = a("<</Type/Catalog/Pages 2 0 R>>");
            objekte[1] = a("<</Type/Pages/Kids[" + string.Join(" ", seiten.Select(n => n + " 0 R")) + "]/Count " + seiten.Count + ">>");
            using (var fs = File.Create(ziel))
            {
                var kopfzeile = a("%PDF-1.4\n%\xE2\xE3\xCF\xD3\n");
                fs.Write(kopfzeile, 0, kopfzeile.Length);
                var orte = new List<long>();
                for (int i = 0; i < objekte.Count; i++)
                {
                    orte.Add(fs.Position);
                    var o = a((i + 1) + " 0 obj\n").Concat(objekte[i]).Concat(a("\nendobj\n")).ToArray();
                    fs.Write(o, 0, o.Length);
                }
                long xref = fs.Position;
                var sb = new StringBuilder();
                sb.Append("xref\n0 " + (objekte.Count + 1) + "\n0000000000 65535 f \n");
                foreach (var o in orte) sb.Append(o.ToString("D10") + " 00000 n \n");
                sb.Append("trailer\n<</Size " + (objekte.Count + 1) + "/Root 1 0 R>>\nstartxref\n" + xref + "\n%%EOF\n");
                var ende = a(sb.ToString());
                fs.Write(ende, 0, ende.Length);
            }
        }
    }
}
