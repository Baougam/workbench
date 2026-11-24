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
  **Zeitring** am Symbol statt Zahl (Rest der gewählten Zeit), genaue Angabe im Tooltip.
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

## Nächste Schritte (Vorschläge)

1. Auf dem echten PC installieren und durchklicken (Seitenleiste in Word-Speichern-Dialog, Autostart nach Neustart).
2. Optional: Hinweis bei neuen Dateien auf dem echten Desktop („Wie lange?“), standardmäßig aus.
3. Optional: signierter Installer / Update-Prüfung für andere Nutzer.
4. Bestehende alte Downloads einmalig „aufräumen“ anbieten.
