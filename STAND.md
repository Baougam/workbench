# STAND – Workbench

*Zuerst lesen. Nach jeder Änderung aktualisieren und mit committen.*

## Stand 2026-11-26 – Version 0.2.0 (erste Fassung 0.1.0 vom 2026-11-24)

Erste lauffähige Version. Selbsttest: 34/34 OK (`.\build.ps1 -Test`). Aussehen geprüft über `.\build.ps1 -Bilder`
und einen kurzen Live-Start im Testordner (Download-Karte unten rechts, Vorschlag greift nach Zeitablauf).

## Sprache (vom Nutzer, verbindlich)

- **Die App, Windows-Einträge und die README sind komplett auf Englisch.** Texte im Ton guter englischer Onboardings
  (Linear, Arc, Notion): kurz, aktiv, natürlich – nicht übersetzt klingend. Code und Kommentare bleiben deutsch; diese
  STAND.md bleibt deutsch (Übergabe).
- Begriffe: Ebenen *Expiring · Permanent · Cloud*, *Graph view*, *Recycle Bin*. Ordner `1 Hour`, `1 Day`, `1 Week`, `1 Month`,
  `Permanent`. Dauer-Schlüssel `1 hour`, `1 day`, `1 week`, `1 month`, `Until…`, `Forever`.
- Umzug von 0.1.0 (deutsch): `Paths.OrdnerUmziehen()` benennt alte Ordner um, `Paths.PfadNeu()` / `Dauer.Neu()` übersetzen
  gespeicherte Pfade und Dauern beim Laden (fristen.json, anordnung.json, netz.json, einstellungen.json). Selbsttest prüft das.

## Design-Regeln (vom Nutzer, verbindlich)

- **Desktop-Gefühl**: Hintergrund = das eigene Windows-Hintergrundbild (weich, abgedunkelt), Dateien als Desktop-Symbole
  mit Namen darunter, in Spalten von oben nach unten. Keine Karten, keine Überschriften-Blöcke.
- **Wenig Text**: keine Erklärsätze und keine Anweisungen („Ohne Klick gilt …“, „Zieh hierher …“). Verhalten spricht für sich;
  Namen von Knöpfen nur als Tooltip. Aktionen als Symbole in einer schwebenden Leiste, die nur bei Auswahl erscheint.
- **Ebenen-Schieber**: schmale Punktleiste links, ohne Beschriftung (Name als Tooltip).
- Kein „KI-Look“ (keine Verläufe als Deko, keine Info-Banner, keine Emoji).
- **Eigener Stil**: Akzent *Bernstein* `#F4B860` (= „läuft ab“), sonst Weiß in Abstufungen auf Tinte `#0E1015`.
  Motiv **Schieber**: Ebenen-Punktleiste im Fenster und Zeit-Schieber (1 Std · 1 Tag · 1 Woche · 1 Monat · ∞) in der Nachfrage.
  Motiv **Blattstapel**: die aktuelle Ebene ist ein Blatt, die Kanten der darunterliegenden schauen oben hervor; Wechsel = Blatt gleitet.
  **Füllstand statt Ring** (Wunsch des Nutzers): das Symbol selbst leert sich von oben. Oben das unveränderte Original
  (nur per Clip unten sichtbar), darunter eine einmal berechnete graue Kopie desselben Bildes (`Vorschau.Fuellstand`);
  Füllhöhe bezieht sich auf den sichtbaren (nicht transparenten) Teil. Letzter Tag: geleerter Teil leicht bernstein.
  Funktioniert für jeden Dateityp (Shell-Symbole, Vorschaubilder, Ordner). Genaue Angabe im Tooltip.
- **Leistung**: Hintergrundbild nur 48 px breit laden und weich hochskalieren (keine BlurEffect), keine Schatten-Effekte je Beschriftung.

**Noch nicht auf dem echten System installiert/getestet** (Registry-Seitenleiste, Autostart, Desktop-Symbol, echter
Papierkorb, echtes Google Drive). Das passiert beim ersten Doppelklick auf `bin\Workbench.exe`.

## Entscheidungen

- **Eigenes Windows-Programm** (keine Chrome-Erweiterung): erfasst Downloads aller Browser über den Download-Ordner.
- **Bauen nur mit Bordmitteln**: `csc.exe` aus .NET Framework 4.8 (C# 5!), WPF ohne XAML-Compiler (Stile per `XamlReader`).
  Kein `dotnet`, kein `$""`, kein `?.`, kein `nameof`.
- **Downloads bleiben, wo sie sind** (Browser-„Öffnen“ funktioniert weiter). „Für immer“ verschiebt nach `Workbench\Dauerhaft`.
- **Speichern aus anderen Programmen** über Ordner `%USERPROFILE%\Workbench\{1 Stunde,1 Tag,1 Woche,1 Monat,Dauerhaft}`,
  eingeblendet im Explorer-Navigationsbereich (Shell-Namespace-Eintrag unter HKCU, wie Cloud-Anbieter es machen).
  Das eigentliche Speichern-Fenster fremder Programme wird nicht verändert.
- **Ablauf → Windows-Papierkorb** (nie sofort löschen). Datei gerade offen → in 10 Min erneut.
- **Wenig mitbekommen**: Karte nimmt nie den Fokus, Vorschlag nach 8 s, Wahl je Dateiart wird gemerkt, Gefährliches mit
  Zwei-Klick-Knopf statt Dialog.
- **Ebene 3 = Cloud** (Wunsch des Nutzers): Google Drive für Desktop (Laufwerk „Google Drive“ › „Meine Ablage“), OneDrive,
  Dropbox, iCloud oder beliebiger Ordner. Vorschaubilder dort nur als Symbol, damit nichts aus dem Netz geladen wird.

## Seit 0.1.0 dazugekommen

- **Freie Fläche (Ebene 2 + 3)**: Symbole ziehen = anordnen (bleibt liegen), Umschalt+ziehen oder Rechtsklick › „Verbinden …“ = Linie,
  Rechtsklick › Farbpunkte = Farbe, Linie anklicken + Entf = löschen, Rechtsklick auf leere Fläche › „Ordnen“. Aus dem Fenster
  gezogen = echte Datei mitnehmen. Gespeichert in `%LOCALAPPDATA%\Workbench\anordnung.json` (je Ordner, nach Namen).
- **Desktop beobachten** (Standard an): neue Dateien auf dem echten Desktop werden gefragt; .lnk/.url und Ordner nicht;
  „Für immer“ lässt sie dort liegen.
- **Schneller**: Fenster wird im Leerlauf vorbereitet, Schließen = verstecken + Speicher zurückgeben; versteckt wird nicht neu
  gezeichnet; Multicore-JIT-Startprofil (`start.profil`).
- **Touchpad**: zwei Finger seitlich = Ebene wechseln, hoch/runter = durch Symbole blättern.
- **Strg+Alt+W** jetzt standardmäßig an.
- **Update-Hinweis**: höchstens 1×/Tag liest Workbench `VERSION` aus dem Repo (raw.githubusercontent.com). Neuer → kleiner
  Bernstein-Punkt am Zahnrad + „x.y.z ist da“ unter den Einstellungen (Link auf GitHub-Releases). Lädt/installiert nichts.
  **Bei jeder neuen Version `VERSION` und `Program.Version` gleich hochzählen.**

- **Notizen** auf Ebene 2/3: Doppelklick auf leere Stelle = neue Notiz, Doppelklick auf Notiz = schreiben (Esc/Strg+Enter fertig,
  leer = weg), verschieben, färben, mit Linien verbinden. In `anordnung.json` (Notizen je Ordner, Linien-Schlüssel `§<Id>`).
- **Netz (Ansicht von oben)**: unscheinbares Symbol oben neben der Lupe oder Strg+G. Dauerhaft (4 Ebenen tief) + Cloud (2 tief),
  max. 280 Ordner, im Hintergrund gelesen. Kräfte-Layout pendelt sich ein und stoppt (keine Dauerlast). Darüberfahren =
  Nachbarn hervorheben, Klick = Ordner öffnen, ziehen = verschieben, Strg+Rad/Zusammenziehen = Zoom, Esc = zurück.
- **Gesten (Wunsch des Nutzers, verbindlich)**: zwei Finger **hoch/runter** (Mausrad) = **Ebene**; zwei Finger **links/rechts**
  = im Ordner **zurück/vor** (rechts wischen = zurück). Pfeiltasten ↑↓ = Ebene, ←→ = zurück/vor; seitliche Maustasten, Alt+←/→.
  Graue ‹ › vor dem Ordnernamen erscheinen, sobald es einen Verlauf gibt.

- **Kreismenü**: gedrückt halten (420 ms, ohne zu ziehen) auf Symbol/Notiz → Kreis (Öffnen, Verbinden, Farbe, Notiz,
  Umbenennen, Papierkorb); hingleiten + loslassen oder antippen.
- **Netz**: Layout vorab im Hintergrund, Positionen in `netz.json` gemerkt, weiches Einblenden, Federn nur beim Ziehen.
  Suche oben links (Enter = nächster Treffer, Umschalt+Enter = tief suchen).
- **Rundgang** beim ersten Start (`Einfuehrung.cs`, 7 Schritte, Spotlight + Blase). „Nicht mehr zeigen“ = `EinfuehrungFertig`,
  ✕ = nur für diese Sitzung. Schritt 1 fragt Desktop-Symbol (Schalter) und Taskleiste (zeigt nur, wo – Windows erlaubt kein
  Anheften per Programm). Wieder starten: Einstellungen › Rundgang.
- **Tiefer in Windows**: Rechtsklick › Workbench › Dauer auf jeder Datei/jedem Ordner (`HKCU\Software\Classes\*\shell\Workbench`,
  Win11 unter „Weitere Optionen“), Aufruf `Workbench.exe --frist "1 Tag" "<Pfad>"` → Warteschlange `auftraege.txt` + Event an
  die laufende Instanz. Taskleisten-Sprungliste (`--zeige <n>`).
- README hat jetzt eine Installationsanleitung für andere (Releases, SmartScreen, Update, Entfernen, Selbst bauen).

## macOS-Version (seit 2026-11-25)

- Eigenes Swift/SwiftUI-Programm in `mac/` (Model, Engine, Prompt, MainView, App). Gebaut **nur** über GitHub Actions
  (`.github/workflows/mac.yml`, `macos-latest`): universal (arm64 + x86_64), ad-hoc signiert, Ergebnis wird als
  `download/Workbench-mac.zip` nach main committet ([skip ci]). Bei Fehlern landet das Build-Log auf Branch `ci-log`
  (`ci/mac-build.log`) – so lässt es sich ohne GitHub-Login lesen (`git fetch origin ci-log`).
- Kein Apple-Entwicklerkonto (Nutzerentscheidung): Erststart per Rechtsklick › Öffnen bzw. „Open Anyway“; in README.
- Kein Mac zum Testen vorhanden (Nutzer) → nur Kompilier-Prüfung durch CI, kein echter Klicktest.
- Umfang v1: Downloads/Desktop/Workbench-Ordner beobachten (FSEvents), Download-Karte (NSPanel, nicht aktivierend),
  Fristen → Papierkorb, Menüleisten-Symbol, Anmeldestart (SMAppService), drei Ebenen mit Blattstapel und leerenden
  Symbolen, Wischen/Pfeiltasten, Drop auf Blätter/Ordner mit Aufklappen, Einstellungen, Rundgang, Services-Menü,
  Update-Hinweis. Noch nicht: freies Anordnen/Linien/Notizen, Netz, Kreismenü, Papierkorb-Ansicht (öffnet Finder-Papierkorb),
  Finder-Seitenleiste (Nutzer zieht Ordner selbst hinein – kein öffentliches API).

## Nächste Schritte (Vorschläge)

1. Auf dem echten PC installieren und durchklicken (Seitenleiste in Word-Speichern-Dialog, Autostart nach Neustart).
2. Optional: Hinweis bei neuen Dateien auf dem echten Desktop („Wie lange?“), standardmäßig aus.
3. Optional: signierter Installer / Update-Prüfung für andere Nutzer.
4. Bestehende alte Downloads einmalig „aufräumen“ anbieten.

## Download-Erkennung (2026-11-25)

- `~`-Namen zählen wieder (ignoriert nur `~$`, `~wr`, `.~`). Mehr Teil-Endungen: `.!qb`, `.crswap`, `.bc!`, `.dlpart`.
- **Nachholen**: `StoreDaten.LetzterBlick` (UTC) = bis wann Downloads/Desktop gesehen wurden. Beim Start und nach
  Beobachter-Überlauf fragt `Engine.Nachholen()` Dateien nach, die seitdem entstanden sind (max. 14 Tage zurück, max. 30).
  Erster Start setzt nur den Zeitpunkt. Mac: nur Endungen + 48 h, Nachholen fehlt dort noch.
- Wartende Downloads werden erst nach 48 h aufgegeben (vorher 6 h).
- Idee des Nutzers, offen: Browser-Rechtsklick „Download with Workbench › Dauer“ (bräuchte Browser-Erweiterung).

## Browser-Erweiterung (2026-11-25, Windows)

- Wunsch des Nutzers: Rechtsklick im Browser auf Link/Bild › „Download with Workbench“ › For an hour/day/week/month/Keep forever.
- `browser/` (MV3: contextMenus, downloads, nativeMessaging, storage) steckt als Ressource in der exe (`build.ps1` /resource,
  Symbole aus `tools/icon.ps1 -Png`). Fester `key` im manifest → feste ID `mffnoocgaogljpgadglahpfhiaodlgab`
  (privater Schlüssel verworfen; für einen Store-Eintrag dessen ID zusätzlich in `allowed_origins`).
- Ablauf: Settings › Browser › Add (auch im Rundgang-Willkommen) → `Browser.Einrichten`: auspacken nach
  `%LOCALAPPDATA%\Workbench\Browser`, Host `com.workbench.host` unter HKCU für Chrome/Edge/Brave/Chromium/Vivaldi,
  ein Knopf je installiertem Browser (App Paths); Adresse `…://extensions` in Zwischenablage (Browser öffnen sie nicht
  per Befehl, geprüft), `BrowserKarte` (4 Schritte); Ordner-Fenster (#32770 mit direktem Edit 1152 – NICHT 1148/1001, das sind
  Öffnen/Speichern) bekommt den Pfad eingetragen. Erweiterung meldet „hello“ → „Connected“.
- Host: Browser startet `Workbench.exe chrome-extension://…/` → `Program.BrowserHost` liest 1 Nachricht, Auftrag
  `vormerken|fertig\t<dauer>\t<pfad>` bzw. `browser\thallo`, weckt die Instanz (oder startet sie). `Engine.Vormerken`:
  Wahl gilt still (keine Karte); außerhalb der Ordner setzt „done“ die Frist direkt. Live getestet (Host + Warteschlange).
- Fremde Erweiterungen kann kein Programm selbst installieren → Entwicklermodus-Hinweis von Chrome bleibt; später Store (Edge gratis).
- Mac: `mac/Sources/Browser.swift` (Chrome/Brave/Edge/Vivaldi, Host = App-Binary, Warteschlange `BrowserQueue`). Safari nur Dienste-Menü
  (lädt selbst, ohne Anmeldung der Seite). Nur kompiliert, ungetestet. Echte Safari-Erweiterung bräuchte eine Safari-Web-Extension mit Apple-Konto, sonst muss man „Unsigned extensions“ nach jedem Safari-Start neu erlauben.

## Ziehen am Rand (2026-11-25)

- `src/Rand.cs`: Datei ziehen und oben/unten verweilen (700 ms) = Ebene wechseln; weisser transparenter Schein waechst am Rand (Nutzerwunsch, keine Pille). Freie Flaeche uebergibt dabei an
  ein echtes Windows-Ziehen (Quelle = Fenster). Expiring-Flaeche nimmt jetzt Ablegen an (Standard-Dauer). Logik im Selbsttest; echte Mausbedienung nicht automatisiert getestet.
- Browser end-to-end mit Brave + Edge geprueft (eigenes Testprofil): Menue -> Download -> Auftrag. Nicht geprueft: Installation ueber Load unpacked von Hand.

## Mehrfachauswahl (2026-11-25)

- Ins Leere druecken + ziehen = Rahmen; schneller Strich hoch/runter (unter 280 ms, ueber 90 px) = Ebene. Strg+Klick, Strg+A. `gruppe` in Hauptfenster, `Mit(...)` = alle Markierten.
- Gemeinsam: ziehen, Zeit aendern, behalten, Cloud, Papierkorb, wiederherstellen; auf der freien Flaeche zusammen verschieben. Logik im Selbsttest, echte Maus nicht.
- Mac: Build vom 25.11. (Browser) wurde von GitHub abgebrochen (kein Runner frei), neu angestossen.
- Mac: `mac/Sources/Select.swift` (Rahmen, ⌘-Klick, ⌘A, ⌘⌫, Leiste, Rand-Schein). Nach außen zieht SwiftUI nur die eine Datei unter dem Zeiger; innerhalb alle.

## Blätter statt fester Ebenen (2026-11-26)

- Wunsch des Nutzers: leere, modulare Workbench. `src/Blaetter.cs` (Modell, `blaetter.json`): Liste von `BlattDef` – Art `temp`
  (Expiring, immer genau einmal, nicht entfernbar), `folder` (auch Desktop/Screenshots, `Symbol` = Reiter-Zeichen), `board`
  (Verweise, verschiebt nie), `cloud` (Pfad leer = erkannter Cloud-Ordner), `empty` (großes leises +). Max. 6.
  Bisherige Nutzer bekommen Expiring/Permanent/Cloud, neue Expiring + leeres Blatt (Start dort, `Blaetter.Frisch`).
- `src/HauptfensterBlaetter.cs` (partial): Schiene mit `Reiter` (`src/Reiter.cs`, Nutzerwahl: Kachel 40×28 im Bildschirm-Format, aktive bernstein getönt, keine helle Fläche (sonst wirkt sie durch Kontrast, als würde sie schimmern), 1 echter Pixel Rand,
  Zeichen 17 px; Sanduhr/Ordner/Wolke/Bildschirm/Board/Ausschnitt/+). Reiter ziehen = umsortieren, nach rechts heraus = entfernen.
  + erscheint nur bei Maus über der Schiene (außer nur 1 Blatt). Ansichten: Blätter 0..n-1, Korb=100, Einst=101, Netz=102.
  Start: mittleres Blatt (leer → Expiring); nach >10 min versteckt wieder Start, sonst wo man war.
- Leisten (Recent = Windows „Zuletzt verwendet“ + eigene Downloads; Shelf = `Ablage`, Verweise): je Blatt oben/unten/rechts,
  + an freien Kanten erscheint bei Mausnähe; Griff ziehen = andere Kante (tauscht) oder in die Mitte = weg. Laufen ins Transparente
  aus, beim Darüberfahren ganz sichtbar; Mausrad/zwei Finger rollen sie (`LeisteRollen`).
- Netz: Wärme je Ordner (`Nutzung` = in Workbench geöffnet, `nutzung.json`, Halbwertszeit 14 Tage; Recent-Dateien; LastWrite)
  → aufsummiert = Adern (Dicke/Helligkeit), Punkte 38–100 % Deckkraft, Top 3 bernstein, Namen nur Wurzeln/Gefärbtes/14 wärmste ohne
  Überlappung. Maus in der Nähe: Punkte wachsen, Namen tauchen auf (nur bei Mausbewegung, keine Animation).
- Mac: noch offen (Blätter, Leisten, Reiter, Netz gibt es dort noch nicht).
- Rechtsklick auf einen Reiter: „Show in Explorer“ / „Remove sheet“ (Expiring nicht). `BlaetterDaten.Version` 2: einmalig wird ein
  leeres, unverändertes Permanent-Blatt zum + (Mitte gehört dem Nutzer, Start dort). `Bisher()` zählt nur noch Inhalt von Permanent.
- Mac (2026-11-26): `mac/Sources/Sheets.swift` (Sheet-Modell `sheets.json`, RecentFiles über Spotlight, RailTile, SheetReorder,
  NewSheetView, BoardView, Bar/BarItem/WithBars); `MainModel` jetzt `sheet` (Index) + `settings`; Ordner-Verlauf je Sheet-Id.
  Reiter ziehen = umsortieren, Rechtsklick = entfernen. Nur per CI kompiliert, ungetestet. Netz gibt es auf dem Mac weiterhin nicht.
- Home (2026-11-26, Nutzerwunsch): Art `home` mit `Module` (recent, shelf, expiring, downloads, screens, folder, note), `src/HauptfensterHome.cs`. + fächert sich in Bausteine auf; Bausteine ziehen = umsortieren, Rechtsklick = Wide/Narrow, Choose folder, Remove. Das + der Schiene öffnet direkt die Blatt-Arten (Home, Desktop, Folder, Board, Screenshots, Cloud) – kein leeres Zwischenblatt mehr; alte `empty` werden zu `home` (Version 3).
- Home v2 (2026-11-26, nach 4 Test-Personas: Studentin, IT-Admin, Designerin, Buero): Raster 120 px (`Modul.X/Y/W/H`), Bearbeiten-Modus (Stift oben rechts, Rechtsklick, Strg+E; Esc/Done): Ziehen = verschieben/tauschen, Ecke = Groesse, Zahnrad = Icons S/M/L, Tiles/List, Filter (Docs/Images/Archives/Installers), Ordner, Titel; x = weg mit Undo. Vorlagen Focus/Daily/Desk/Blank (`Hauptfenster.Vorlage`); neues Home = Focus (Expiring) + leise Vorlagen-Leiste mit Haken (`BlattDef.Vorschlag`). Neu: Pinned. Expiring-Eintraege: Rechtsklick +1 week / Keep. `Vorschau.Echt`: nur echtes Vorschaubild (SIIGBF_THUMBNAILONLY), sonst Typ-Marke – Recent sieht nicht mehr gleich aus. Titel nur im Bearbeiten-Modus. Offen: Regeln + Sammelkarte fuer Downloads, Drop-Aktionen, Mac-Version davon.
- Leisten v2 + Anpassen ueberall (2026-11-26): Karten mit echter Vorschau + Name, kein Hintergrund, beide Enden weich, ‹ › beim Darueberfahren (weiches Rollen), Groesse S/M/L je Leiste (`BlattDef.LeistenGroesse`). Stift oben rechts auf jedem Blatt / Strg+E / Rechtsklick `Customize`: Leisten zeigen Name, Zahnrad, x, Griff; freie Kanten zeigen gestrichelte Plaetze `+ Recent`/`+ Shelf`; Done. `src/PdfVorschau.cs`: erste PDF-Seite ueber Windows.Data.Pdf (WinRT, nur WinMetadata zum Bauen, Warten per Status-Abfrage statt AsTask), Plattencache `vorschau\` (max 600). Tisch-Scrollbalken versteckt.
- Werkzeuge (2026-11-26): `src/Werkzeuge.cs` – Smaller (1600 px JPG), JPG, Bilder->PDF (eigener kleiner PDF-Schreiber, JPEG eingebettet; vom Windows-Renderer geprueft), Zip/Unzip (System.IO.Compression), Pfad, SHA-256. Als Leiste `tools` und Home-Baustein; Drop = anwenden, Klick = Dateien waehlen; Ergebnis neben Original (sonst Downloads) + in die Ablage + Hinweis-Pille unten. Alle Arten als Leiste (recent, tools, shelf, downloads, pinned, expiring, screens), 1/2 Reihen (`BlaetterDaten.Reihen`), Pinned-Leiste `Angeheftet`. Verlauf an den Leistenenden staerker.
- Netz: Ausweichen vor der Maus weich nachgefuehrt (`Gleiten` per Rendering, stoppt bei Stillstand), waermere Ordner groesser und obenauf, Lichtfluss (gestrichelte Linie, DashOffset) auf den heissen Adern 4,5 s nach dem Oeffnen und auf dem Weg zur Wurzel beim Darueberfahren – nie dauerhaft. Namen auch beim Zoomen ohne Ueberlappung. Leerlauf gemessen: 0–0,2 % eines Kerns. Sprungliste --zeige beim Kaltstart repariert.
- Ankommen = Home (2026-11-26, Nutzer: Dashboard fehlte, weil seine Mitte ein Board war): `StartAnsicht` = erstes Home. Version 4: wer kein Home hat, bekommt eins an Stelle 2. Standard-Vorlage `start` (Workbench): Recent oben, Expiring + Downloads (Liste) nebeneinander, Tools unten – ueber die volle Spaltenzahl (`Vorlage(b, name, n)`; solange `Vorschlag`, passt sie sich der Fensterbreite an). Vorlagen-Leiste: Workbench/Focus/Daily/Desk/Blank + Haken; Blank bleibt leer.
- Tiefe (2026-11-26, Nutzeridee): Ordner-Blaetter koennen alle Ordner gleichzeitig aufklappen (`src/HauptfensterTiefe.cs`): Boxen per squarified Treemap, passen immer ins Bild, Klick = ausfahren (FLIP-Gleiten), Doppelklick Name = hinein, Ordnerfarbe tönt die Box, Inhalt passt sich an (+N). Uebergang: Schnappschuss fliegt gross/durchsichtig weg, neue Ebene von hinten, Bernstein-Scanlinie (nur waehrend Wechsel). Ebenen-Waehler `src/HauptfensterStufen.cs`: Umschalt+Rad / Zusammenziehen / Umschalt+Pfeil zeigt Stufenmodell (isometrische Platten + Anzahl), Loslassen (Umschalt los, Touchpad 0,5 s Ruhe) fliegt hin, Esc bricht ab. Naechste Tiefe wird im Leerlauf vorgelesen. Cloud: nur Typ-Marken (nie herunterladen). Behoben: Endlosschleife SizeChanged (120 % CPU). Leerlauf Ebene 0: 0 %; Ebene 1 nach Cloud-Fix noch nicht neu gemessen.

## Tiefe schneller, Seiten, Update per Klick (2026-11-26, Version 0.2.0)

- **Ebenen-Wähler**: römische Zahlen statt „Level n / Surface“ (I = Oberfläche, II, III …, `Griechisch(i)`).
  Gewählte Platte hebt sich (BackEase) mit Bernstein-Verlauf; Anzahl rechts klein. Scanlinie beim Wechsel entfernt (Wunsch).
- **Schnell lesen** (`src/Schnell.cs`): FindFirstFileEx (Basic + LARGE_FETCH), Versteckt/System aus den Find-Daten,
  Hilfsdateien nur nach Namen (`Engine.IstHilfsName`), `HatInhalt` für die Frage „geht es tiefer?“.
- **Gemerkte Ebenen**: `tiefeCache` wird nach 4 s Ruhe im Hintergrund nach `%LOCALAPPDATA%\Workbench\tiefe.bin` geschrieben
  (max. 60 Stände) und beim ersten Bedarf geladen. Gemerktes gilt sofort als Platzhalter, wird still nachgelesen; neu
  gezeichnet wird nur, wenn sich wirklich etwas geändert hat (`Zeichen`-Vergleich). `TiefeVergessen` leert nicht mehr,
  sondern markiert nur „nachsehen“.
- **Seiten**: erst wenn eine Box kleiner als 150×104 px würde (`SeitePasst`, Halbierungssuche `SeitenStarts`). Dann eigener Randstreifen (58 px) für einen deutlichen Bernstein-Pfeil mit „+N“; beim ersten Erscheinen je Ebene stupst er zweimal nach rechts, danach still. Unten Punkte.
  ←/→ und Wischen seitlich blättern in der Tiefe (`SeiteWechseln`), sonst wie bisher zurück/vor. Oberste Ebene liest bis 400 Ordner.
- **Update per Klick** (`src/Update.cs`): prüft VERSION auf GitHub (≤ 1×/20 h), lädt die neue exe still nach
  `%LOCALAPPDATA%\Workbench\Workbench.neu.exe` (Prüfung MZ + Größe). Erst dann erscheint „update x.y“ unten rechts.
  Klick: laufende exe → `.alt` umbenennen, neue an ihren Platz, Start mit `--nach-update` (wartet auf den Mutex), alte beendet sich.
  `.alt` wird beim nächsten Start gelöscht. **Bei jedem Release: `Program.Version` und `VERSION` gemeinsam erhöhen.**
- Leerlauf nach Start gemessen: 0,01 % CPU.
