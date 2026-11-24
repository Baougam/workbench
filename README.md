# Workbench

Downloads und Dateien, die sich selbst aufräumen – und ein Desktop, der aufgeräumt bleibt. Für Windows 10/11.

## Installieren

1. **[Workbench.exe herunterladen](https://github.com/Baougam/workbench/releases/latest)** (unter *Assets*).
2. Doppelklicken.
   Windows zeigt beim ersten Mal vielleicht *„Der Computer wurde durch Windows geschützt“* – das kommt bei jedem
   Programm ohne gekaufte Signatur. **Weitere Informationen › Trotzdem ausführen.**
3. Fertig. Ein kurzer Rundgang erklärt alles (lässt sich überspringen und später unter *Einstellungen › Rundgang* wieder ansehen).

Workbench installiert sich dabei selbst – **ohne Adminrechte** – nach `%LOCALAPPDATA%\Programs\Workbench` und richtet ein:

| | |
|---|---|
| Desktop-Symbol & Startmenü | im Rundgang abwählbar |
| Mit Windows starten | damit Fristen auch ablaufen, wenn das Fenster zu ist (im Leerlauf praktisch 0 % CPU) |
| *Workbench* links im Explorer | und damit in jedem Speichern-Fenster (Word, Photoshop …) |
| Rechtsklick › *Workbench* | auf jeder Datei und jedem Ordner (Windows 11: unter *Weitere Optionen anzeigen*) |
| *Einstellungen › Apps* | zum Entfernen |

**An die Taskleiste heften:** Rechtsklick auf das Workbench-Symbol unten in der Taskleiste › *An Taskleiste anheften*.
(Windows erlaubt Programmen nicht, das selbst zu tun – der Rundgang zeigt, wo.)

**Update:** neue `Workbench.exe` herunterladen und doppelklicken – sie ersetzt die alte, Fristen und Anordnungen bleiben.
Gibt es eine neue Version, erscheint ein kleiner Punkt am Zahnrad.

**Entfernen:** *Einstellungen › Apps › Workbench* oder in Workbench *Einstellungen › Entfernen*. Deine Dateien bleiben, wo sie sind.

## Was es kann

- **Nach jedem Download** fragt unten rechts eine kleine Karte, wie lange die Datei bleiben soll: ein Zeit-Schieber
  *1 Std · 1 Tag · 1 Woche · 1 Monat · ∞*, dazu *Bis …* und *In die Cloud*. Ein Klick genügt; wer nichts tut, bekommt still
  den Vorschlag (Installer 1 Tag, sonst 1 Woche – und was du für eine Dateiart wählst, wird der neue Vorschlag).
  Auch neue Dateien auf dem Desktop werden gefragt.
- **Aus jedem Programm speichern**: links im Speichern-Fenster steht *Workbench* mit `1 Stunde`, `1 Tag`, `1 Woche`,
  `1 Monat`, `Dauerhaft`. Wer dort speichert, hat die Dauer schon gewählt.
- **Ist die Zeit um**, wandert die Datei in den normalen Windows-Papierkorb – nichts ist sofort weg.
- **Drei Ebenen** wie Blätter übereinander: *Befristet* (mit Zeitring am Symbol), *Dauerhaft* (ein aufgeräumter Desktop)
  und *Cloud* (Google Drive, OneDrive, Dropbox – wird erkannt).
  Auf Dauerhaft und Cloud: frei anordnen, **gedrückt halten** für den Kreis (verbinden, färben, Notiz …), Notizen per Doppelklick.
- **Von oben**: alle Ordner als Netz – mit Suche (Enter = nächster Treffer, Umschalt+Enter = tief in den Ordnern).

| Bedienung | |
|---|---|
| Zwei Finger hoch/runter · Mausrad · ↑ ↓ | Ebene wechseln |
| Zwei Finger links/rechts · ← → · Maus-Seitentasten | im Ordner zurück/vor |
| `Strg+Alt+W` | Workbench öffnen (von überall) |
| `Strg+G` | Netz von oben |
| Rechtsklick auf das Taskleisten-Symbol | direkt zu Befristet, Dauerhaft, Cloud, Netz |

## Selbst bauen

Kein Visual Studio, kein SDK – nur der in Windows eingebaute C#-Compiler (.NET Framework 4.8):

```powershell
git clone https://github.com/Baougam/workbench.git
cd workbench
.\build.ps1           # bin\Workbench.exe
.\build.ps1 -Test     # + Selbsttest (in einem Testordner, fasst nichts Echtes an)
.\build.ps1 -Bilder   # + alle Ansichten als PNG nach bin\bilder
```

Gefahrlos ausprobieren, ohne zu installieren: `$env:WORKBENCH_TESTROOT = "$env:TEMP\wb"; .\bin\Workbench.exe --portable`

**Neue Version veröffentlichen:** Nummer in `VERSION` und `Program.Version` gleich hochzählen, bauen, auf GitHub
*Releases › Draft a new release* (Tag z. B. `v0.2.0`) und `bin\Workbench.exe` anhängen.

## Aufbau

| Datei | Inhalt |
|---|---|
| `src/Engine.cs` | Beobachter (Downloads, Desktop, Workbench-Ordner), Download-fertig-Erkennung, Fristen, Ablauf → Papierkorb |
| `src/Model.cs` | Pfade, Dauern, Fristen-Liste (`%LOCALAPPDATA%\Workbench\fristen.json`), Einstellungen |
| `src/Frage.cs` | Die Karte nach dem Download / beim Speichern (Zeit-Schieber) |
| `src/Hauptfenster.cs` | Fenster: Ebenen, Befristet, Papierkorb, Einstellungen, Gesten, Verlauf |
| `src/Tisch.cs` | Freie Fläche: anordnen, Linien, Farben, Notizen, Kreismenü |
| `src/Netz.cs` | Ansicht von oben (Netz) mit Suche |
| `src/Einfuehrung.cs` | Rundgang beim ersten Start |
| `src/Papierkorb.cs` | Liest den echten Papierkorb (`$Recycle.Bin`) – Wiederherstellen/Löschen ohne Dialoge |
| `src/Cloud.cs` | Findet Google Drive, OneDrive, Dropbox, iCloud |
| `src/Install.cs` | Selbst-Installation, Autostart, Explorer-Seitenleiste, Rechtsklick-Menü, Entfernen |
| `src/Update.cs` | Stiller Update-Hinweis (liest `VERSION`) |
| `src/Theme.cs`, `src/Parts.cs` | Aussehen, Symbole, Vorschaubilder, Kalender |
| `src/SelfTest.cs` | `--selftest` und `--render` |
