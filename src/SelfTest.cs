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

        static Stream Nachricht(string json)
        {
            var b = System.Text.Encoding.UTF8.GetBytes(json);
            var ms = new MemoryStream();
            ms.Write(BitConverter.GetBytes(b.Length), 0, 4);
            ms.Write(b, 0, b.Length);
            ms.Position = 0;
            return ms;
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
            Ok(!Engine.IstHilfsdatei(@"C:\x\~Notizen.txt"), "Datei mit ~ am Anfang zählt");
            Ok(Engine.IstHilfsdatei(@"C:\x\film.mkv.!qB") && Engine.IstHilfsdatei(@"C:\x\a.crswap"), "qBittorrent/Chrome-Teildateien werden ignoriert");

            // 2) Zeiten
            var j = new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);
            Ok(Dauer.Rest(j.AddMinutes(42), j) == "42 min", "Rest 42 Min");
            Ok(Dauer.Rest(j.AddDays(3), j) == "3 days", "Rest 3 Tage");
            Ok(Dauer.Rest(j.AddDays(21), j) == "3 weeks", "Rest 3 Wochen");
            Ok(Dauer.Spalte(j.AddHours(5), j) == 0 && Dauer.Spalte(j.AddDays(6), j) == 1 && Dauer.Spalte(j.AddDays(20), j) == 2, "Spalten heute/Woche/später");
            Ok(Dauer.Ablauf("1 month", j) == j.AddMonths(1) && Dauer.Ablauf(Dauer.Immer, j) == null, "Dauer → Ablauf");

            // 3) Engine im Testordner
            var cfg = new Einstellungen();
            var E = new Engine(Dispatcher.CurrentDispatcher, cfg);
            var korb = new List<string>();
            E.Recycler = p => { korb.Add(p); if (Directory.Exists(p)) Directory.Delete(p, true); else File.Delete(p); };
            // Umzug aus der deutschen Version: alte Ordner + alte Liste
            Directory.CreateDirectory(Path.Combine(Paths.Wurzel, "1 Tag"));
            File.WriteAllBytes(Path.Combine(Paths.Wurzel, "1 Tag", "alt.pdf"), new byte[10]);
            Directory.CreateDirectory(Path.Combine(Paths.Wurzel, "Dauerhaft"));
            File.WriteAllBytes(Path.Combine(Paths.Wurzel, "Dauerhaft", "behalten.txt"), new byte[10]);
            Directory.CreateDirectory(Paths.Daten);
            File.WriteAllText(Path.Combine(Paths.Daten, "fristen.json"), "{\"Eintraege\":[{\"Pfad\":" + Store.Json().Serialize(Path.Combine(Paths.Wurzel, "1 Tag", "alt.pdf")) + ",\"Hinzu\":\"\\/Date(1795500000000)\\/\",\"Ablauf\":\"\\/Date(1999999999999)\\/\",\"Herkunft\":\"ordner\",\"Gewaehlt\":\"1 Tag\"}],\"Papierkorb\":[],\"LetzteWahl\":{\".zip\":\"1 Woche\"}}");
            E.Start();
            Ok(Directory.Exists(Path.Combine(Paths.Wurzel, "1 week")) && Directory.Exists(Paths.Dauerhaft), "Workbench-Ordner angelegt");
            Ok(!Directory.Exists(Path.Combine(Paths.Wurzel, "1 Tag")) || Paths.Gleich(Path.Combine(Paths.Wurzel, "1 Tag"), Path.Combine(Paths.Wurzel, "1 Day")), "alter Ordner umbenannt");
            Ok(File.Exists(Path.Combine(Paths.Wurzel, "1 Day", "alt.pdf")) && File.Exists(Path.Combine(Paths.Dauerhaft, "behalten.txt")), "Inhalt der alten Ordner übernommen");
            var altE = E.S.Finde(Path.Combine(Paths.Wurzel, "1 Day", "alt.pdf"));
            Ok(altE != null && altE.Gewaehlt == "1 day" && E.S.D.LetzteWahl[".zip"] == "1 week", "alte Fristen übernommen und übersetzt");
            E.S.D.LetzteWahl.Clear();

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
            Ok(dl != null && dl[0].Gewaehlt == "1 day", "Installer bekommt 1 Tag");

            // Merken: wer bei .zip „1 Monat“ wählt, bekommt das beim nächsten .zip vorgeschlagen
            E.Merken(fertig, "1 month");
            Ok(E.Vorschlag(@"C:\x\neu.zip") == "1 month", "Wahl je Dateiart wird gemerkt");

            // 4) Speichern in Zeit-Ordner (wie aus Word/Photoshop)
            Eintrag gespeichert = null;
            E.OrdnerGespeichert += x => gespeichert = x;
            string tag = Path.Combine(Paths.Wurzel, "1 day", "Flyer.pdf");
            File.WriteAllBytes(tag, new byte[3000]);
            Pumpen(8000, () => gespeichert != null);
            Ok(gespeichert != null && gespeichert.Gewaehlt == "1 day" && Math.Abs((gespeichert.Ablauf.Value - DateTime.UtcNow).TotalHours - 24) < 0.1, "Speichern in „1 Tag“ setzt 1 Tag");

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

            // 4c) Download, während Workbench nicht hinschaute (aus, Puffer übergelaufen) → wird nachgeholt; Erledigtes nicht
            dl = null;
            E.S.D.LetzterBlick = DateTime.UtcNow.AddMinutes(-5);
            string verpasst = Path.Combine(Paths.Downloads, "verpasst.pdf");
            E.Stop();
            File.WriteAllBytes(verpasst, new byte[1200]);
            E.DesktopBeobachten(true);
            E.Nachholen();
            Pumpen(8000, () => dl != null);
            Ok(dl != null && dl.Count == 1 && Paths.Gleich(dl[0].Pfad, verpasst), "verpasster Download wird nachgeholt, Erledigtes nicht");
            E.Start();

            // 4d) Browser: „Download with Workbench › For an hour“ – keine Karte, Frist steht schon
            dl = null;
            string ausBrowser = Path.Combine(Paths.Downloads, "Bericht.docx");
            Ok(E.Vormerken(ausBrowser, "1 hour", false) == null, "Browser-Wahl wird vorgemerkt");
            File.WriteAllBytes(ausBrowser, new byte[2500]);
            Pumpen(5000, () => E.S.Finde(ausBrowser) != null);
            Pumpen(800, null);
            var eb = E.S.Finde(ausBrowser);
            Ok(dl == null && eb != null && eb.Gewaehlt == "1 hour", "Download aus dem Browser-Menü: Frist gesetzt, keine Karte");
            string woanders = Path.Combine(Paths.Daten, "Anderswo.pdf");
            File.WriteAllBytes(woanders, new byte[100]);
            var ew = E.Vormerken(woanders, "1 day", true);
            Ok(ew != null && ew.Gewaehlt == "1 day", "Browser-Download außerhalb der Ordner bekommt die Frist direkt");

            // 4e) Erweiterung und Nachrichten
            string ext = Path.Combine(Paths.Daten, "ext-test");
            Browser.Auspacken(ext);
            string mf = File.Exists(Path.Combine(ext, "manifest.json")) ? File.ReadAllText(Path.Combine(ext, "manifest.json")) : "";
            Ok(mf.Contains("\"key\"") && mf.Contains("\"" + Program.Version + "\"") && File.Exists(Path.Combine(ext, "background.js")) && File.Exists(Path.Combine(ext, "icon128.png")), "Erweiterung wird ausgepackt");
            Ok(Browser.NachrichtLesen(Nachricht("{\"op\":\"plan\",\"time\":\"1 day\",\"path\":\"C:\\\\x\\\\a.zip\"}")) == "vormerken\t1 day\tC:\\x\\a.zip", "Browser-Nachricht wird verstanden");
            Ok(Browser.NachrichtLesen(Nachricht("{\"op\":\"hello\",\"browser\":\"Brave\"}")) == "browser\thallo\tBrave" && Browser.NachrichtLesen(Nachricht("{\"op\":\"plan\",\"time\":\"9 years\",\"path\":\"C:\\\\a\"}")) == null, "unbekannte Dauer wird abgelehnt");

            // 5) Word speichert neu: umbenennen in Temp, neu schreiben, Temp löschen → Frist bleibt
            var vorher = E.S.Finde(tag).Ablauf;
            string tmp = Path.Combine(Paths.Wurzel, "1 day", "~WRL0001.tmp");
            File.Move(tag, tmp);
            File.WriteAllBytes(tag, new byte[3500]);
            File.Delete(tmp);
            Pumpen(5500, null);
            var nachher = E.S.Finde(tag);
            Ok(nachher != null && nachher.Ablauf == vorher, "erneutes Speichern behält die Frist");

            // 6) Verschieben in „Dauerhaft“ im Explorer → Frist weg
            string fuerImmer = Path.Combine(Paths.Wurzel, "1 week", "Vertrag.pdf");
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
            E.S.D.Papierkorb.Add(new Recycelt { Pfad = original, Am = DateTime.UtcNow, Dauer = "1 day" });
            var liste = Papierkorb.Lesen(E.S);
            Ok(liste.Count == 1 && liste[0].Name == "Bordkarte.pdf" && liste[0].Groesse == 312 && liste[0].Wb != null, "Papierkorb lesen + Workbench-Herkunft");
            Papierkorb.Wiederherstellen(liste[0]);
            Ok(File.Exists(original) && !File.Exists(Path.Combine(bin, "$RABC123.pdf")) && !File.Exists(Path.Combine(bin, "$IABC123.pdf")), "Wiederherstellen legt die Datei zurück");
            dl = null;
            Pumpen(8000, () => dl != null);
            Ok(dl != null && dl.Any(x => x.Name == "Bordkarte.pdf"), "nach Wiederherstellen wird neu gefragt");

            // 11) Oberfläche baut sich ohne Fehler (alle Blätter + Frage)
            Blaetter.D = Blaetter.Standard(false);
            Blaetter.Speichern();
            foreach (int a in new[] { 0, 1, 2, Hauptfenster.Korb, Hauptfenster.Einst, Hauptfenster.Netzansicht })
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

            // 12) Beim Ziehen am Rand verweilen wechselt die Ebene; in der Mitte nicht
            try
            {
                var w = new Hauptfenster(E, 0, true);
                w.Wurzel.Measure(new Size(1060, 700)); w.Wurzel.Arrange(new Rect(0, 0, 1060, 700));
                var r = w.RandTest;
                r.Melden(new Point(500, 350)); Pumpen(300, null);
                Ok(r.Zone == 0 && w.Ansicht == 0, "Ziehen in der Mitte wechselt nicht");
                r.Melden(new Point(500, 20)); Ok(r.Zone == 0, "ganz oben ist nach oben Schluss");
                Func<int, int, bool> halten = (y, ms) => { var ende = DateTime.UtcNow.AddMilliseconds(ms); int von = w.Ansicht; while (DateTime.UtcNow < ende && w.Ansicht == von) { r.Melden(new Point(500, y)); Pumpen(60, null); } return w.Ansicht != von; };
                Ok(!halten(690, 350), "kurz am Rand wechselt nicht");
                r.Melden(new Point(500, 350));
                Ok(halten(690, 2500) && w.Ansicht == 1, "unten verweilen = nächste Ebene");
                Ok(halten(690, 3000) && w.Ansicht == 2, "weiter verweilen = noch eine");
                r.Melden(new Point(500, 350));
                Ok(halten(20, 2500) && w.Ansicht == 1, "oben verweilen = zurück");
                Pumpen(700, null);
                Ok(r.Zone == 0, "nach dem Loslassen ist die Anzeige weg");
            }
            catch (Exception ex) { Ok(false, "Randwechsel: " + ex); }

            // 12b) Blätter: + wählen, Board sammelt Verweise, umsortieren, entfernen, Leisten
            try
            {
                var w = new Hauptfenster(E, 0, true);
                w.Wurzel.Measure(new Size(1060, 700)); w.Wurzel.Arrange(new Rect(0, 0, 1060, 700));
                int bVorher = Blaetter.Liste.Count;
                w.BlattNeu();
                Ok(Blaetter.Liste.Count == bVorher + 1 && Blaetter.Liste.Last().Art == "empty" && w.Ansicht == bVorher, "+ legt ein leeres Blatt an und zeigt es");
                w.BlattNeu();
                Ok(Blaetter.Liste.Count == bVorher + 1, "ein zweites leeres Blatt gibt es nicht");
                var bb = Blaetter.Liste.Last();
                w.Waehlen(bb, "board", "", "Board", "");
                string a1 = Path.Combine(Paths.Daten, "board-a.txt"), a2 = Path.Combine(Paths.Downloads, "board-b.txt");
                File.WriteAllText(a1, "a"); File.WriteAllText(a2, "b");
                w.BoardHinzu(bb, new[] { a1, a2, a1 });
                Ok(bb.Inhalt.Count == 2 && File.Exists(a1) && File.Exists(a2), "Board sammelt Verweise, verschiebt nichts");
                w.Wurzel.UpdateLayout();
                Ok(w.SymboleTest.ContainsKey(a1) && w.SymboleTest.ContainsKey(a2), "Board zeigt beide");
                w.GruppeTest(g => { });
                w.AllesMarkieren();
                w.EntfernenTest();
                Pumpen(400, null);
                Ok(bb.Inhalt.Count == 0 && File.Exists(a1) && File.Exists(a2), "Entf auf dem Board nimmt nur den Verweis weg");
                int bi = Blaetter.Liste.IndexOf(bb);
                w.BlattVerschieben(bi, 1);
                Ok(Blaetter.Liste[1] == bb && w.Ansicht == 1, "Blatt umsortieren, man bleibt auf ihm");
                Blaetter.LeisteSetzen(bb, "recent", "top");
                Blaetter.LeisteSetzen(bb, "shelf", "bottom");
                Blaetter.LeisteSetzen(bb, "recent", "right");
                Ok(Blaetter.LeisteBei(bb, "right") == "recent" && Blaetter.LeisteBei(bb, "top") == null && Blaetter.LeisteBei(bb, "bottom") == "shelf", "Leiste andocken und umsetzen");
                Blaetter.Parken(new[] { a1, a1, a2 });
                Ok(Blaetter.D.Ablage.Count == 2 && Paths.Gleich(Blaetter.D.Ablage[0], a2), "Ablage parkt ohne Doppelte");
                w.Zeige(1);
                w.Wurzel.UpdateLayout();
                Ok(true, "Blatt mit Leisten baut sich");
                w.BlattWeg(1);
                Ok(!Blaetter.Liste.Contains(bb) && File.Exists(a1), "Blatt entfernen lässt die Dateien in Ruhe");
                w.BlattWeg(0);
                Ok(Blaetter.Liste.Any(x => x.Art == "temp"), "Expiring lässt sich nicht entfernen");
            }
            catch (Exception ex) { Ok(false, "Blätter: " + ex); }
            Blaetter.D = Blaetter.Standard(false);

            // 13) Mehrere markieren: Rahmen aufziehen, alles markieren, gemeinsam löschen
            try
            {
                var mNamen = new[] { "m1.txt", "m2.txt", "m3.txt" }.Select(n => Path.Combine(Paths.Dauerhaft, n)).ToArray();
                foreach (var n in mNamen) File.WriteAllText(n, "x");
                var w = new Hauptfenster(E, 1, true);
                w.Wurzel.Measure(new Size(1060, 700)); w.Wurzel.Arrange(new Rect(0, 0, 1060, 700)); w.Wurzel.UpdateLayout();
                Ok(w.GruppeAnzahl == 0, "anfangs nichts markiert");
                w.Rahmen(new Point(70, 60), new Point(1040, 690));
                Ok(w.GruppeAnzahl == w.SymboleTest.Count && w.GruppeAnzahl >= 3, "Rahmen über alles markiert alles");
                var f = w.SymboleTest[mNamen[0]].TransformToAncestor(w.Wurzel).TransformBounds(new Rect(w.SymboleTest[mNamen[0]].RenderSize));
                w.Rahmen(new Point(f.X + 20, f.Y + 20), new Point(f.X + 40, f.Y + 40));
                Ok(w.GruppeAnzahl == 1, "kleiner Rahmen markiert nur eins");
                w.Rahmen(new Point(1000, 680), new Point(1030, 688));
                Ok(w.GruppeAnzahl == 0, "Rahmen im Leeren hebt auf");
                // gemeinsam löschen: über die Fristen-Ebene, deren Papierkorb im Test umgeleitet ist
                var dann = new[] { "d1.txt", "d2.txt", "d3.txt" }.Select(n => Path.Combine(Paths.Daten, n)).ToArray();
                foreach (var n in dann) { File.WriteAllText(n, "x"); E.FristVonHand(n, "1 day"); }
                var b = new Hauptfenster(E, 0, true);
                b.Wurzel.Measure(new Size(1060, 700)); b.Wurzel.Arrange(new Rect(0, 0, 1060, 700)); b.Wurzel.UpdateLayout();
                b.AllesMarkieren();
                int mVorher = b.GruppeAnzahl;
                b.EntfernenTest();
                Pumpen(600, null);
                Ok(mVorher >= 3 && dann.All(n => E.S.Finde(n) == null && korb.Any(p => Paths.Gleich(p, n))), "Entf nimmt alle Markierten");
            }
            catch (Exception ex) { Ok(false, "Mehrfachauswahl: " + ex); }

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
                double gesamt = wahl == "1 hour" ? 1 : wahl == "1 day" ? 24 : wahl == "1 week" ? 168 : wahl == "1 month" ? 720 : 1300;
                E.S.D.Eintraege.Add(new Eintrag { Pfad = p, Hinzu = ab.AddHours(-gesamt), Ablauf = ab, Herkunft = "download", Gewaehlt = wahl });
            };
            neu("Invoice_October.pdf", 0.7, "1 hour");
            neu("setup_tool_v2.exe", 5, "1 day");
            neu("Concert_Ticket.pdf", 11, "1 day");
            neu("Holiday_Photos.zip", 72, "1 week");
            neu("Screenshot_2026-11-24.png", 144, "1 week");
            neu("Presentation_Draft.pptx", 21 * 24, "1 month");
            neu("Tax_2025.pdf", 52 * 24, "until 15 Jan");
            neu("Moodboard.png", 27 * 24, "1 month");
            foreach (var o in new[] { "Projects", "Applications", "Recipes", "University" }) Directory.CreateDirectory(Path.Combine(Paths.Dauerhaft, o));
            foreach (var d in new[] { "Resume.pdf", "Notes.txt", "Budget.xlsx", "Tax_Receipts.zip" }) File.WriteAllBytes(Path.Combine(Paths.Dauerhaft, d), new byte[1000]);
            Bild(Path.Combine(Paths.Dauerhaft, "Logo_final.png"), 7);
            // Beispiel-Anordnung: frei gelegt, eingefärbt, mit Linien
            var an = Anordnungen.Fuer(Paths.Dauerhaft);
            an.Plaetze["Applications"] = new Platz { X = 40, Y = 40, Farbe = "#8FB4FF" };
            an.Plaetze["Resume.pdf"] = new Platz { X = 230, Y = 40 };
            an.Plaetze["Projects"] = new Platz { X = 40, Y = 250, Farbe = "#F4B860" };
            an.Plaetze["Logo_final.png"] = new Platz { X = 230, Y = 200 };
            an.Plaetze["Notes.txt"] = new Platz { X = 230, Y = 320 };
            an.Plaetze["Recipes"] = new Platz { X = 520, Y = 40, Farbe = "#7ED39B" };
            an.Plaetze["Budget.xlsx"] = new Platz { X = 700, Y = 40 };
            an.Plaetze["University"] = new Platz { X = 520, Y = 250 };
            an.Plaetze["Tax_Receipts.zip"] = new Platz { X = 700, Y = 250 };
            an.Linien.Add(new Linie { A = "Applications", B = "Resume.pdf", Farbe = "#8FB4FF" });
            an.Linien.Add(new Linie { A = "Projects", B = "Logo_final.png", Farbe = "#F4B860" });
            an.Linien.Add(new Linie { A = "Projects", B = "Notes.txt", Farbe = "#F4B860" });
            an.Linien.Add(new Linie { A = "Recipes", B = "Budget.xlsx", Farbe = "#7ED39B" });
            an.Notizen.Add(new Notiz { Id = "n1", X = 430, Y = 160, Text = "Send applications by 15 Jan –\nshorten the resume first" });
            an.Linien.Add(new Linie { A = Anordnungen.NotizPraefix + "n1", B = "Resume.pdf", Farbe = "#8FB4FF" });
            foreach (var u in new[] { @"Projects\Website", @"Projects\Website\Drafts", @"Projects\App", @"Projects\Logo", @"Applications\Cover Letters", @"Applications\References", @"Recipes\Baking", @"Recipes\Cooking", @"University\Semester 5", @"University\Semester 6", @"University\Semester 6\Statistics" })
                Directory.CreateDirectory(Path.Combine(Paths.Dauerhaft, u));
            var web = Anordnungen.Fuer(Path.Combine(Paths.Dauerhaft, "Projects"));
            web.Linien.Add(new Linie { A = "Website", B = "Logo", Farbe = "#F4B860" });
            web.Plaetze["Website"] = new Platz { X = 40, Y = 40, Farbe = "#F4B860" };
            string drive = Path.Combine(test, "Google Drive", "Meine Ablage");
            foreach (var o in new[] { "Photos", "Family", "Work", "Shared with me" }) Directory.CreateDirectory(Path.Combine(drive, o));
            foreach (var d in new[] { "Trip_Plan.docx", "Budget_2027.xlsx", "Talk.pptx" }) File.WriteAllBytes(Path.Combine(drive, d), new byte[1000]);
            E.S.Speichern();

            var korb = new List<BinEintrag>
            {
                new BinEintrag { Name = "driver_update_2026.exe", Original = Path.Combine(Paths.Downloads, "driver_update_2026.exe"), Geloescht = DateTime.Now.AddHours(-3), Groesse = 84L << 20, Wb = new Recycelt { Dauer = "1 hour" } },
                new BinEintrag { Name = "Boarding_Pass_Munich.pdf", Original = "x", Geloescht = DateTime.Now.AddDays(-1), Groesse = 312 << 10, Wb = new Recycelt { Dauer = "1 day" } },
                new BinEintrag { Name = "Font_Pack.zip", Original = "x", Geloescht = DateTime.Now.AddDays(-4), Groesse = 46L << 20, Wb = new Recycelt { Dauer = "1 week" } },
                new BinEintrag { Name = "Meeting_Recording.mp4", Original = "x", Geloescht = DateTime.Now.AddDays(-22), Groesse = 481L << 20, Wb = new Recycelt { Dauer = "1 month" } },
            };

            string[] namen = { "1-befristet", "2-dauerhaft", "3-cloud", "4-papierkorb", "5-einstellungen", "9-netz" };
            int[] ansichten = { 0, 1, 2, Hauptfenster.Korb, Hauptfenster.Einst, Hauptfenster.Netzansicht };
            Blaetter.D = Blaetter.Standard(false);
            Zuletzt.Setzen(E.S.D.Eintraege.Select(x => x.Pfad).Where(File.Exists).ToList());
            for (int a = 0; a < 6; a++)
            {
                var w = new Hauptfenster(E, ansichten[a], true);
                w.KorbFuerBild(korb);
                w.Zeige(ansichten[a]);
                Speichern(w.Wurzel, 1060, 700, Path.Combine(ziel, namen[a] + ".png"));
                if (a == 0)
                {
                    w.RandFuerBild();
                    Speichern(w.Wurzel, 1060, 700, Path.Combine(ziel, "1b-rand.png"));
                }
                if (a == 1)
                {
                    w.Rahmen(new Point(75, 70), new Point(330, 400));
                    Speichern(w.Wurzel, 1060, 700, Path.Combine(ziel, "2c-rahmen.png"));
                    w.Rahmen(new Point(1000, 690), new Point(1001, 691));
                    w.KreisFuerBild("Projects");                  // Kreismenü (gedrückt halten)
                    Speichern(w.Wurzel, 1060, 700, Path.Combine(ziel, "2b-kreis.png"));
                }
                if (a == 5)
                {
                    w.NetzSuche("Seme");                          // Suche im Netz hebt Treffer + Weg hervor
                    Speichern(w.Wurzel, 1060, 700, Path.Combine(ziel, "9b-netzsuche.png"));
                }
            }
            {
                // neues Blatt (+ aufgefächert), Blatt mit Leisten, Board
                var w = new Hauptfenster(E, 0, true);
                w.KorbFuerBild(korb);
                w.BlattNeu();
                Speichern(w.Wurzel, 1060, 700, Path.Combine(ziel, "12-neu.png"));
                var dh = Blaetter.Liste[1];
                Blaetter.LeisteSetzen(dh, "recent", "top");
                Blaetter.LeisteSetzen(dh, "shelf", "bottom");
                Blaetter.Parken(E.S.D.Eintraege.Select(x => x.Pfad).Where(File.Exists).Take(3).ToList());
                w.Zeige(1);
                Speichern(w.Wurzel, 1060, 700, Path.Combine(ziel, "13-leisten.png"));
                var leer = Blaetter.Liste.Last();
                w.Waehlen(leer, "board", "", "Board", "");
                w.BoardHinzu(leer, E.S.D.Eintraege.Select(x => x.Pfad).Where(File.Exists).Take(4).Concat(Directory.EnumerateFileSystemEntries(Paths.Dauerhaft).Take(3)).ToList());
                w.Zeige(Blaetter.Liste.Count - 1);
                Speichern(w.Wurzel, 1060, 700, Path.Combine(ziel, "14-board.png"));
                Blaetter.D = Blaetter.Standard(false);
            }
            foreach (var s in new[] { 0, 1, 3 })
            {
                var w = new Hauptfenster(E, 0, true);
                w.KorbFuerBild(korb);
                Speichern(w.Wurzel, 1060, 700, Path.Combine(ziel, "r" + s + "-rundgang.png"));   // einmal ausmessen
                w.RundgangFuerBild(s);
                Speichern(w.Wurzel, 1060, 700, Path.Combine(ziel, "r" + s + "-rundgang.png"));
            }
            var f = new Frage(E, new List<Eintrag> { E.S.D.Eintraege[3] }, false);
            var k = f.Karte; f.Content = null;
            var r1 = (System.Windows.Controls.Border)Rahmen(k);
            Speichern(r1, 410, 0, Path.Combine(ziel, "6-frage.png"));
            r1.Child = null;
            f.KalenderFuerBild();
            Speichern(Rahmen(k), 410, 0, Path.Combine(ziel, "7-kalender.png"));
            var info = new Frage(E, new List<Eintrag> { new Eintrag { Pfad = Path.Combine(Paths.Wurzel, "1 week", "Flyer_Draft.pdf"), Ablauf = jetzt.AddDays(7), Gewaehlt = "1 week", Herkunft = "ordner" } }, true);
            var ik = info.Karte; info.Content = null;
            Speichern(Rahmen(ik), 410, 0, Path.Combine(ziel, "8-gespeichert.png"));
            Speichern(Rahmen(new BrowserKarte(new Browser.Ziel { Name = "Brave", Exe = "brave.exe", Seite = "brave://extensions" }).FuerBild(0)), 330, 0, Path.Combine(ziel, "10-browser.png"));
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

