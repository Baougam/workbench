# STAND – Workbench

*Zuerst lesen. Nach jeder Änderung aktualisieren und mit committen.*

## Stand 2026-11-24 – Version 0.1.0

Erste lauffähige Version. Selbsttest: 34/34 OK (`.\build.ps1 -Test`). Aussehen geprüft über `.\build.ps1 -Bilder`
und einen kurzen Live-Start im Testordner (Download-Karte unten rechts, Vorschlag greift nach Zeitablauf).

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

## Nächste Schritte (Vorschläge)

1. Auf dem echten PC installieren und durchklicken (Seitenleiste in Word-Speichern-Dialog, Autostart nach Neustart).
2. Optional: Hinweis bei neuen Dateien auf dem echten Desktop („Wie lange?“), standardmäßig aus.
3. Optional: signierter Installer / Update-Prüfung für andere Nutzer.
4. Bestehende alte Downloads einmalig „aufräumen“ anbieten.
