using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Workbench
{
    /// <summary>
    /// Workbench.exe --selftest &lt;ordner&gt;: prüft alles in einem Testordner – fasst weder echte Downloads noch Registry noch
    /// den echten Papierkorb an. Ergebnis in &lt;ordner&gt;\selftest.txt.
    /// </summary>
    static class SelfTest
    {
        static readonly StringBuilder log = new StringBuilder();
        static int fehler;

        static void Ok(bool bedingung, string was)
        {
            log.AppendLine((bedingung ? "OK    " : "FEHLER") + "  " + was);
            if (!bedingung) fehler++;
        }

        public static string TestWurzel(string[] args)
        {
            string t = Environment.GetCommandLineArgs().Length > 2 ? Environment.GetCommandLineArgs()[2] : Path.Combine(Path.GetTempPath(), "workbench-test");
            if (Directory.Exists(t)) Directory.Delete(t, true);
            Directory.CreateDirectory(t);
            Environment.SetEnvironmentVariable("WORKBENCH_TESTROOT", t);
            return t;
        }

        static void Pumpen(int ms, Func<bool> bis)
        {
            var ende = DateTime.Now.AddMilliseconds(ms);
            while (DateTime.Now < ende && (bis == null || !bis()))
            {
                var f = new DispatcherFrame();
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                t.Tick += delegate { t.Stop(); f.Continue = false; };
                t.Start();
                Dispatcher.PushFrame(f);
            }
        }

        public static int Run()
        {
            string wurzel = TestWurzel(null);
            try { Laufen(wurzel); }
            catch (Exception ex) { Ok(false, "Ausnahme: " + ex); }
            log.AppendLine(fehler == 0 ? "ALLES OK" : fehler + " FEHLER");
            File.WriteAllText(Path.Combine(wurzel, "selftest.txt"), log.ToString(), Encoding.UTF8);
            return fehler == 0 ? 0 : 1;
        }

        static void Laufen(string wurzel)
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            UI.ThemaLaden(app.Resources);
            Vorschau.Sofort = true;

            // 1) Hilfsdateien erkennen
            Ok(Engine.IstHilfsdatei(@"C:\x\a.zip.crdownload"), "Chrome-Teildatei wird ignoriert");
            Ok(Engine.IstHilfsdatei(@"C:\x\a.zip.part"), "Firefox-Teildatei wird ignoriert");
            Ok(Engine.IstHilfsdatei(@"C:\x\~$Brief.docx"), "Word-Sperrdatei wird ignoriert");
            Ok(Engine.IstHilfsdatei(@"C:\x\~WRL0001.tmp"), "Word-Zwischendatei wird ignoriert");
            Ok(!Engine.IstHilfsdatei(@"C:\x\Rechnung.pdf"), "normale Datei zählt");

            // 2) Zeiten
            var j = new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);
            Ok(Dauer.Rest(j.AddMinutes(42), j) == "42 Min", "Rest 42 Min");
            Ok(Dauer.Rest(j.AddDays(3), j) == "3 Tage", "Rest 3 Tage");
            Ok(Dauer.Rest(j.AddDays(21), j) == "3 Wochen", "Rest 3 Wochen");
            Ok(Dauer.Spalte(j.AddHours(5), j) == 0 && Dauer.Spalte(j.AddDays(6), j) == 1 && Dauer.Spalte(j.AddDays(20), j) == 2, "Spalten heute/Woche/später");
            Ok(Dauer.Ablauf("1 Monat", j) == j.AddMonths(1) && Dauer.Ablauf(Dauer.Immer, j) == null, "Dauer → Ablauf");

            // 3) Engine im Testordner
            var cfg = new Einstellungen();
            var E = new Engine(Dispatcher.CurrentDispatcher, cfg);
            var korb = new List<string>();
            E.Recycler = p => { korb.Add(p); if (Directory.Exists(p)) Directory.Delete(p, true); else File.Delete(p); };
            E.Start();
            Ok(Directory.Exists(Path.Combine(Paths.Wurzel, "1 Woche")) && Directory.Exists(Paths.Dauerhaft), "Workbench-Ordner angelegt");

            List<Eintrag> dl = null;
            E.DownloadsFertig += l => dl = l;
            string teil = Path.Combine(Paths.Downloads, "Urlaubsfotos.zip.crdownload");
            File.WriteAllBytes(teil, new byte[5000]);
            Pumpen(1500, null);
            Ok(dl == null, "kein Alarm, solange der Download läuft");
            string fertig = Path.Combine(Paths.Downloads, "Urlaubsfotos.zip");
            File.Move(teil, fertig);
            Pumpen(8000, () => dl != null);
            Ok(dl != null && dl.Count == 1 && Paths.Gleich(dl[0].Pfad, fertig), "fertiger Download wird erkannt");
            var e1 = E.S.Finde(fertig);
            Ok(e1 != null && e1.Ablauf.HasValue && Math.Abs((e1.Ablauf.Value - DateTime.UtcNow).TotalDays - 7) < 0.01, "Vorschlag 1 Woche gesetzt");

            dl = null;
            File.WriteAllBytes(Path.Combine(Paths.Downloads, "setup_tool.exe"), new byte[2000]);
            Pumpen(8000, () => dl != null);
            Ok(dl != null && dl[0].Gewaehlt == "1 Tag", "Installer bekommt 1 Tag");

            // Merken: wer bei .zip „1 Monat“ wählt, bekommt das beim nächsten .zip vorgeschlagen
            E.Merken(fertig, "1 Monat");
            Ok(E.Vorschlag(@"C:\x\neu.zip") == "1 Monat", "Wahl je Dateiart wird gemerkt");

            // 4) Speichern in Zeit-Ordner (wie aus Word/Photoshop)
            Eintrag gespeichert = null;
            E.OrdnerGespeichert += x => gespeichert = x;
            string tag = Path.Combine(Paths.Wurzel, "1 Tag", "Flyer.pdf");
            File.WriteAllBytes(tag, new byte[3000]);
            Pumpen(8000, () => gespeichert != null);
            Ok(gespeichert != null && gespeichert.Gewaehlt == "1 Tag" && Math.Abs((gespeichert.Ablauf.Value - DateTime.UtcNow).TotalHours - 24) < 0.1, "Speichern in „1 Tag“ setzt 1 Tag");

            // 4b) Desktop: „Bild speichern unter › Desktop“ wird gefragt, Verknüpfungen nicht; „für immer“ lässt es liegen
            dl = null;
            File.WriteAllBytes(Path.Combine(Paths.Desktop, "Neue Verknüpfung.lnk"), new byte[300]);
            Pumpen(2500, () => dl != null);
            Ok(dl == null, "Verknüpfung auf dem Desktop wird nicht gefragt");
            string deskBild = Path.Combine(Paths.Desktop, "katze.jpg");
            File.WriteAllBytes(deskBild, new byte[4000]);
            Pumpen(8000, () => dl != null);
            Ok(dl != null && dl[0].Herkunft == "desktop", "Bild auf dem Desktop wird gefragt");
            E.FristSetzen(E.S.Finde(deskBild), null, Dauer.Immer);
            Ok(File.Exists(deskBild) && E.S.Finde(deskBild) == null, "„Für immer“ vom Desktop: bleibt auf dem Desktop");

            // 5) Word speichert neu: umbenennen in Temp, neu schreiben, Temp löschen → Frist bleibt
            var vorher = E.S.Finde(tag).Ablauf;
            string tmp = Path.Combine(Paths.Wurzel, "1 Tag", "~WRL0001.tmp");
            File.Move(tag, tmp);
            File.WriteAllBytes(tag, new byte[3500]);
            File.Delete(tmp);
            Pumpen(5500, null);
            var nachher = E.S.Finde(tag);
            Ok(nachher != null && nachher.Ablauf == vorher, "erneutes Speichern behält die Frist");

            // 6) Verschieben in „Dauerhaft“ im Explorer → Frist weg
            string fuerImmer = Path.Combine(Paths.Wurzel, "1 Woche", "Vertrag.pdf");
            gespeichert = null;
            File.WriteAllBytes(fuerImmer, new byte[100]);
            Pumpen(8000, () => gespeichert != null);
            File.Move(fuerImmer, Path.Combine(Paths.Dauerhaft, "Vertrag.pdf"));
            Pumpen(5500, null);
            Ok(E.S.Finde(fuerImmer) == null && E.S.D.Eintraege.All(x => !x.Pfad.EndsWith("Vertrag.pdf")), "nach „Dauerhaft“ verschoben = keine Frist mehr");

            // 7) Für immer → wandert nach Dauerhaft
            var zip = E.S.Finde(fertig);
            string neuPfad = E.FristSetzen(zip, null, Dauer.Immer);
            Ok(File.Exists(neuPfad) && Paths.Gleich(Path.GetDirectoryName(neuPfad), Paths.Dauerhaft) && E.S.Finde(fertig) == null, "„Für immer“ legt die Datei in Dauerhaft");

            // 8) Ablauf: 2 Tage später → „1 Tag“-Datei im Papierkorb, Installer auch
            E.Jetzt = () => DateTime.UtcNow.AddDays(2);
            E.Aufraeumen();
            Ok(korb.Any(p => p.EndsWith("Flyer.pdf")) && korb.Any(p => p.EndsWith("setup_tool.exe")), "Abgelaufenes kommt in den Papierkorb");
            Ok(!File.Exists(tag) && E.S.Finde(tag) == null && E.S.D.Papierkorb.Count >= 2, "und ist aus der Liste raus, im Papierkorb vermerkt");
            E.Jetzt = () => DateTime.UtcNow;

            // 9) Speichern/Laden
            var s2 = new Store();
            s2.Laden();
            Ok(s2.D.Papierkorb.Count == E.S.D.Papierkorb.Count && s2.D.LetzteWahl.ContainsKey(".zip"), "Liste übersteht Neustart");

            // 10) Papierkorb lesen ($I v2) und wiederherstellen
            string bin = Path.Combine(wurzel, "Bin");
            Directory.CreateDirectory(bin);
            Papierkorb.TestOrdner = bin;
            string original = Path.Combine(Paths.Downloads, "Bordkarte.pdf");
            var namen = Encoding.Unicode.GetBytes(original + "\0");
            var ib = new List<byte>();
            ib.AddRange(BitConverter.GetBytes(2L));
            ib.AddRange(BitConverter.GetBytes(312L));
            ib.AddRange(BitConverter.GetBytes(DateTime.Now.ToFileTime()));
            ib.AddRange(BitConverter.GetBytes(original.Length + 1));
            ib.AddRange(namen);
            File.WriteAllBytes(Path.Combine(bin, "$IABC123.pdf"), ib.ToArray());
            File.WriteAllBytes(Path.Combine(bin, "$RABC123.pdf"), new byte[312]);
            E.S.D.Papierkorb.Add(new Recycelt { Pfad = original, Am = DateTime.UtcNow, Dauer = "1 Tag" });
            var liste = Papierkorb.Lesen(E.S);
            Ok(liste.Count == 1 && liste[0].Name == "Bordkarte.pdf" && liste[0].Groesse == 312 && liste[0].Wb != null, "Papierkorb lesen + Workbench-Herkunft");
            Papierkorb.Wiederherstellen(liste[0]);
            Ok(File.Exists(original) && !File.Exists(Path.Combine(bin, "$RABC123.pdf")) && !File.Exists(Path.Combine(bin, "$IABC123.pdf")), "Wiederherstellen legt die Datei zurück");
            dl = null;
            Pumpen(8000, () => dl != null);
            Ok(dl != null && dl.Any(x => x.Name == "Bordkarte.pdf"), "nach Wiederherstellen wird neu gefragt");

            // 11) Oberfläche baut sich ohne Fehler (alle Ebenen + Frage)
            foreach (int a in new[] { 0, 1, 2, 3, 4 })
            {
                try
                {
                    var w = new Hauptfenster(E, a, true);
                    w.KorbFuerBild(liste);
                    w.Zeige(a);
                    var el = w.Wurzel;
                    el.Measure(new Size(1060, 700));
                    el.Arrange(new Rect(0, 0, 1060, 700));
                    el.UpdateLayout();
                    Ok(true, "Ansicht " + a + " baut sich");
                }
                catch (Exception ex) { Ok(false, "Ansicht " + a + ": " + ex.Message); }
            }
            try
            {
                var f = new Frage(E, new List<Eintrag> { E.S.D.Eintraege.First(x => x.Ablauf.HasValue) }, false);
                var k = f.Karte; f.Content = null;
                k.Measure(new Size(410, 800)); k.Arrange(new Rect(k.DesiredSize));
                f.KalenderFuerBild();
                k.Measure(new Size(410, 900)); k.Arrange(new Rect(k.DesiredSize));
                Ok(true, "Frage + Kalender bauen sich");
            }
            catch (Exception ex) { Ok(false, "Frage: " + ex.Message); }

            E.Stop();
        }
    }

    /// <summary>Workbench.exe --render &lt;ordner&gt;: zeichnet alle Ansichten mit Beispieldaten als PNG (zum Prüfen des Aussehens).</summary>
    static class Render
    {
        public static int Run(string ziel)
        {
            ziel = Path.GetFullPath(ziel);
            Directory.CreateDirectory(ziel);
            string test = Path.Combine(ziel, "daten");
            if (Directory.Exists(test)) Directory.Delete(test, true);
            Environment.SetEnvironmentVariable("WORKBENCH_TESTROOT", test);
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            UI.ThemaLaden(app.Resources);
            Vorschau.Sofort = true;
            Paths.OrdnerAnlegen();

            var cfg = new Einstellungen();
            var E = new Engine(Dispatcher.CurrentDispatcher, cfg);
            var jetzt = DateTime.UtcNow;
            Action<string, double, string> neu = (name, stunden, wahl) =>
            {
                string p = Path.Combine(Paths.Downloads, name);
                if (name.EndsWith(".png")) Bild(p, name.GetHashCode());
                else File.WriteAllBytes(p, new byte[Math.Abs(name.GetHashCode()) % 900000 + 20000]);
                var ab = jetzt.AddHours(stunden);
                double gesamt = wahl == "1 Std" ? 1 : wahl == "1 Tag" ? 24 : wahl == "1 Woche" ? 168 : wahl == "1 Monat" ? 720 : 1300;
                E.S.D.Eintraege.Add(new Eintrag { Pfad = p, Hinzu = ab.AddHours(-gesamt), Ablauf = ab, Herkunft = "download", Gewaehlt = wahl });
            };
            neu("Rechnung_Oktober.pdf", 0.7, "1 Std");
            neu("setup_tool_v2.exe", 5, "1 Tag");
            neu("Ticket_Konzert.pdf", 11, "1 Tag");
            neu("Urlaubsfotos.zip", 72, "1 Woche");
            neu("Screenshot_2026-11-24.png", 144, "1 Woche");
            neu("Praesentation_Entwurf.pptx", 21 * 24, "1 Monat");
            neu("Steuer_2025.pdf", 52 * 24, "bis 15.01.");
            neu("Moodboard.png", 27 * 24, "1 Monat");
            foreach (var o in new[] { "Projekte", "Bewerbung 2027", "Rezepte", "Uni" }) Directory.CreateDirectory(Path.Combine(Paths.Dauerhaft, o));
            foreach (var d in new[] { "Lebenslauf.pdf", "Notizen.txt", "Haushaltsbuch.xlsx", "Steuer_Belege.zip" }) File.WriteAllBytes(Path.Combine(Paths.Dauerhaft, d), new byte[1000]);
            Bild(Path.Combine(Paths.Dauerhaft, "Logo_final.png"), 7);
            // Beispiel-Anordnung: frei gelegt, eingefärbt, mit Linien
            var an = Anordnungen.Fuer(Paths.Dauerhaft);
            an.Plaetze["Bewerbung 2027"] = new Platz { X = 40, Y = 40, Farbe = "#8FB4FF" };
            an.Plaetze["Lebenslauf.pdf"] = new Platz { X = 230, Y = 40 };
            an.Plaetze["Projekte"] = new Platz { X = 40, Y = 250, Farbe = "#F4B860" };
            an.Plaetze["Logo_final.png"] = new Platz { X = 230, Y = 200 };
            an.Plaetze["Notizen.txt"] = new Platz { X = 230, Y = 320 };
            an.Plaetze["Rezepte"] = new Platz { X = 520, Y = 40, Farbe = "#7ED39B" };
            an.Plaetze["Haushaltsbuch.xlsx"] = new Platz { X = 700, Y = 40 };
            an.Plaetze["Uni"] = new Platz { X = 520, Y = 250 };
            an.Plaetze["Steuer_Belege.zip"] = new Platz { X = 700, Y = 250 };
            an.Linien.Add(new Linie { A = "Bewerbung 2027", B = "Lebenslauf.pdf", Farbe = "#8FB4FF" });
            an.Linien.Add(new Linie { A = "Projekte", B = "Logo_final.png", Farbe = "#F4B860" });
            an.Linien.Add(new Linie { A = "Projekte", B = "Notizen.txt", Farbe = "#F4B860" });
            an.Linien.Add(new Linie { A = "Rezepte", B = "Haushaltsbuch.xlsx", Farbe = "#7ED39B" });
            string drive = Path.Combine(test, "Google Drive", "Meine Ablage");
            foreach (var o in new[] { "Fotos", "Familie", "Arbeit", "Geteilt mit mir" }) Directory.CreateDirectory(Path.Combine(drive, o));
            foreach (var d in new[] { "Reiseplan.docx", "Budget 2027.xlsx", "Vortrag.pptx" }) File.WriteAllBytes(Path.Combine(drive, d), new byte[1000]);
            E.S.Speichern();

            var korb = new List<BinEintrag>
            {
                new BinEintrag { Name = "treiber_update_2026.exe", Original = Path.Combine(Paths.Downloads, "treiber_update_2026.exe"), Geloescht = DateTime.Now.AddHours(-3), Groesse = 84L << 20, Wb = new Recycelt { Dauer = "1 Std" } },
                new BinEintrag { Name = "Bordkarte_München.pdf", Original = "x", Geloescht = DateTime.Now.AddDays(-1), Groesse = 312 << 10, Wb = new Recycelt { Dauer = "1 Tag" } },
                new BinEintrag { Name = "Schriftarten_Paket.zip", Original = "x", Geloescht = DateTime.Now.AddDays(-4), Groesse = 46L << 20, Wb = new Recycelt { Dauer = "1 Woche" } },
                new BinEintrag { Name = "Bildschirmaufnahme_Meeting.mp4", Original = "x", Geloescht = DateTime.Now.AddDays(-22), Groesse = 481L << 20, Wb = new Recycelt { Dauer = "1 Monat" } },
            };

            string[] namen = { "1-befristet", "2-dauerhaft", "3-cloud", "4-papierkorb", "5-einstellungen" };
            for (int a = 0; a < 5; a++)
            {
                var w = new Hauptfenster(E, a, true);
                w.KorbFuerBild(korb);
                w.Zeige(a);
                Speichern(w.Wurzel, 1060, 700, Path.Combine(ziel, namen[a] + ".png"));
            }
            var f = new Frage(E, new List<Eintrag> { E.S.D.Eintraege[3] }, false);
            var k = f.Karte; f.Content = null;
            var r1 = (System.Windows.Controls.Border)Rahmen(k);
            Speichern(r1, 410, 0, Path.Combine(ziel, "6-frage.png"));
            r1.Child = null;
            f.KalenderFuerBild();
            Speichern(Rahmen(k), 410, 0, Path.Combine(ziel, "7-kalender.png"));
            var info = new Frage(E, new List<Eintrag> { new Eintrag { Pfad = Path.Combine(Paths.Wurzel, "1 Woche", "Flyer_Entwurf.pdf"), Ablauf = jetzt.AddDays(7), Gewaehlt = "1 Woche", Herkunft = "ordner" } }, true);
            var ik = info.Karte; info.Content = null;
            Speichern(Rahmen(ik), 410, 0, Path.Combine(ziel, "8-gespeichert.png"));
            return 0;
        }

        static FrameworkElement Rahmen(FrameworkElement k)
        {
            return new System.Windows.Controls.Border { Background = Farbe.B("#0D1015"), Child = k };
        }

        static void Bild(string pfad, int saat)
        {
            var r = new Random(saat);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb((byte)r.Next(40, 200), (byte)r.Next(60, 200), (byte)r.Next(100, 255)), Color.FromRgb((byte)r.Next(150, 255), (byte)r.Next(80, 200), (byte)r.Next(40, 160)), 45), null, new Rect(0, 0, 256, 192));
                dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), null, new Point(80, 70), 40, 40);
            }
            var bmp = new RenderTargetBitmap(256, 192, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var s = File.Create(pfad)) enc.Save(s);
        }

        static void Speichern(FrameworkElement el, double b, double h, string datei)
        {
            el.Measure(new Size(b, h > 0 ? h : double.PositiveInfinity));
            double hh = h > 0 ? h : el.DesiredSize.Height;
            el.Arrange(new Rect(0, 0, b, hh));
            el.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(new Action(() => { }), DispatcherPriority.ContextIdle);
            var bmp = new RenderTargetBitmap((int)(b * 1.5), (int)(hh * 1.5), 144, 144, PixelFormats.Pbgra32);
            bmp.Render(el);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var s = File.Create(datei)) enc.Save(s);
        }
    }
}

