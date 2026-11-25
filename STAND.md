# STAND – Workbench

*Zuerst lesen. Nach jeder Änderung aktualisieren und mit committen.*

## Stand 2026-11-24 – Version 0.1.0

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
