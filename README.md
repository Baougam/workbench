# Workbench

Downloads und Dateien, die sich selbst aufräumen – für Windows 11.

- **Nach jedem Download** fragt unten rechts eine kleine Karte: *1 Std · 1 Tag · 1 Woche · 1 Monat · Bis … · Für immer*.
  Ein Klick genügt. Wer nichts tut, bekommt nach ein paar Sekunden still den Vorschlag (Installer: 1 Tag, sonst 1 Woche –
  und was du für eine Dateiart wählst, wird der neue Vorschlag). Ganz still geht auch: Einstellungen › „Nach Downloads fragen“ aus.
- **Aus jedem Programm speichern** (Word, Photoshop …): links im Speichern-Fenster steht *Workbench* mit den Ordnern
  `1 Stunde`, `1 Tag`, `1 Woche`, `1 Monat`, `Dauerhaft`. Wer dort speichert, hat die Dauer schon gewählt.
- **Ist die Zeit um**, wandert die Datei in den normalen Windows-Papierkorb – nichts ist sofort weg.
- **Das Fenster** (Desktop-Symbol, Taskleiste, optional `Strg+Alt+W`) hat einen Ebenen-Schieber:
  1. **Befristet** – sortiert nach „Heute“, „Diese Woche“, „Später“
  2. **Dauerhaft** – ein aufgeräumter Desktop (echter Ordner, auch im Explorer)
  3. **Cloud** – Google Drive / OneDrive / Dropbox als zweiter Desktop (wird automatisch erkannt)

  Wechseln per Klick, Mausrad, Pfeiltasten ←/→ oder seitlichem Wischen auf dem Touchpad. Dateien lassen sich
  herausziehen (in Mails, Word …) und hineinziehen.
- **Papierkorb-Ansicht**: wiederherstellen (Workbench fragt dann neu) oder endgültig löschen.

Läuft unsichtbar im Hintergrund. Windows meldet neue Dateien selbst, und der Ablauf-Timer wacht nur zur nächsten Frist auf:
im Leerlauf praktisch 0 % CPU. Das Fenster wird beim Schließen ganz freigegeben.

## Installieren

`Workbench.exe` doppelklicken. Sie installiert sich selbst nach `%LOCALAPPDATA%\Programs\Workbench` (ohne Adminrechte),
legt Desktop- und Startmenü-Symbol an, startet mit Windows und erscheint unter *Einstellungen › Apps* zum Entfernen.
An die Taskleiste anheften: Rechtsklick auf das Symbol in der Taskleiste › „An Taskleiste anheften“
(Windows erlaubt Programmen nicht, das selbst zu tun).

## Bauen

Keine Installation nötig – nur der in Windows eingebaute C#-Compiler (.NET Framework 4.8):

```powershell
.\build.ps1           # bin\Workbench.exe
.\build.ps1 -Test     # + Selbsttest (in einem Testordner, fasst nichts Echtes an)
.\build.ps1 -Bilder   # + alle Ansichten als PNG nach bin\bilder
```

Zum gefahrlosen Ausprobieren ohne Installation: `$env:WORKBENCH_TESTROOT = "$env:TEMP\wb"; .\bin\Workbench.exe --portable`

## Aufbau

| Datei | Inhalt |
|---|---|
| `src/Engine.cs` | Beobachter (Downloads + Workbench-Ordner), Download-fertig-Erkennung, Fristen, Ablauf → Papierkorb |
| `src/Model.cs` | Pfade, Dauern, Fristen-Liste (`%LOCALAPPDATA%\Workbench\fristen.json`), Einstellungen |
| `src/Frage.cs` | Die Karte nach dem Download / beim Speichern |
| `src/Hauptfenster.cs` | Fenster mit Ebenen-Schieber, Befristet, Dauerhaft, Cloud, Papierkorb, Einstellungen |
| `src/Papierkorb.cs` | Liest den echten Papierkorb (`$Recycle.Bin`) – Wiederherstellen/Löschen ohne Dialoge |
| `src/Cloud.cs` | Findet Google Drive, OneDrive, Dropbox, iCloud |
| `src/Install.cs` | Selbst-Installation, Autostart, Explorer-Seitenleiste, Entfernen |
| `src/Theme.cs`, `src/Parts.cs` | Aussehen, Symbole, Vorschaubilder, Kalender |
| `src/SelfTest.cs` | `--selftest` und `--render` |
