# Workbench

Downloads that clean up after themselves, and a desktop that stays tidy. For Windows 10/11 and macOS 13 or later.

## Install on Windows

1. **[Download Workbench.exe](https://github.com/Baougam/workbench/raw/main/download/Workbench.exe)**
2. Double-click it.
   The first time, Windows may say *“Windows protected your PC”*. That happens with any app that isn’t signed with a
   paid certificate. Click **More info › Run anyway**.
3. That’s it. A short tour shows you around. You can skip it and bring it back any time from *Settings › Tour*.

Workbench installs itself to `%LOCALAPPDATA%\Programs\Workbench` — **no admin rights needed** — and sets up:

| | |
|---|---|
| Desktop and Start menu shortcut | the desktop shortcut can be turned off in the tour |
| Start with Windows | so timers keep running when the window is closed (it uses next to no CPU while idle) |
| *Workbench* in File Explorer | which also puts it in every Save dialog (Word, Photoshop, …) |
| Right-click › *Workbench* | on any file or folder (Windows 11: under *Show more options*) |
| *Settings › Apps* | so you can uninstall it like any other app |

**Browser (Chrome, Edge, Brave, Vivaldi):** *Settings › Browser* (also offered in the tour) adds
*Download with Workbench* to the right-click menu on links and images — pick the time before the download even starts.
Browsers only let you add extensions from outside their store by hand, so a small card walks you through it: paste the
address Workbench copied, turn on *Developer mode*, click *Load unpacked*, then *Select Folder* (already filled in).

**Pin it to the taskbar:** right-click Workbench in the taskbar and choose *Pin to taskbar*.
Windows doesn’t let apps pin themselves, so the tour shows you where to click.

**Update:** download the new `Workbench.exe` and double-click it. It replaces the old version and keeps your timers,
layouts and notes. When an update is out, a small dot appears on the settings icon.

**Uninstall:** *Settings › Apps › Workbench*, or *Settings › Remove* inside Workbench. Your files stay where they are.

## Install on Mac

1. **[Download Workbench-mac.zip](https://github.com/Baougam/workbench/raw/main/download/Workbench-mac.zip)** and double-click it to unzip.
2. **Right-click Workbench › Open › Open.**
   The app isn’t signed with a paid Apple certificate, so the first time macOS asks to be sure. On recent macOS you may
   instead see “Workbench can’t be opened” — then go to *System Settings › Privacy & Security* and click **Open Anyway**.
   You only do this once.
3. Workbench offers to move itself to *Applications* — click **Move**. When macOS asks whether Workbench may access
   your Downloads and Desktop folders, click **Allow**. That’s how it notices new files.

Workbench then lives in the menu bar (the little stack icon), starts at login and asks after every download.
**Tip:** drag the *Workbench* folder from your home folder into Finder’s sidebar — then it’s in every Save dialog.
To set a timer on any file: right-click it › *Quick Actions* (or *Services*) › *Workbench: Keep for a day*.

**Browser:** *Settings › Browser* adds *Download with Workbench* to Chrome, Brave, Edge and Vivaldi. Safari only loads
extensions signed with an Apple account, so there it works through the Services menu: select a web address, right-click ›
*Services › Download with Workbench*. That download doesn’t use your Safari login, so it only works for public files.

The Mac version has the core of Workbench: the download card, timers, the three layers, the Recycle Bin (Trash),
cloud folders, gestures and the tour. Free arrangement with lines and notes, the graph view and the press-and-hold menu
are coming next.

## What it does

- **After every download**, a small card asks how long you need the file: *1 hour · 1 day · 1 week · 1 month · ∞*,
  plus *Until…* and *Move to cloud*. One click is enough. If you ignore it, Workbench quietly uses a sensible default
  (1 day for installers, 1 week for everything else — and it learns what you pick for each file type).
  New files on your desktop get the same question.
- **Save from any app** into the *Workbench* folders in the sidebar — `1 Hour`, `1 Day`, `1 Week`, `1 Month`,
  `Permanent` — and the timer is already set.
- **When time runs out**, the file moves to the regular Windows Recycle Bin. Nothing is deleted outright.
- **Three layers**, stacked like sheets: *Expiring* (with a ring showing the time left), *Permanent* (a desktop that
  stays tidy) and *Cloud* (Google Drive, OneDrive or Dropbox — detected automatically).
  On Permanent and Cloud you can arrange things freely, **press and hold** for more options (connect, color, add a note)
  and double-click empty space to write a note.
- **Graph view** shows all your folders and how they connect, with search
  (Enter jumps to the next match, Shift+Enter searches inside folders too).

| Shortcut | |
|---|---|
| Two-finger swipe up/down · mouse wheel · ↑ ↓ | switch layers |
| Two-finger swipe left/right · ← → · mouse back/forward | go back/forward in folders |
| `Ctrl+Alt+W` | open Workbench from anywhere |
| `Ctrl+G` | graph view |
| Right-click the taskbar icon | jump to Expiring, Permanent, Cloud or Graph |

## Build it yourself

No Visual Studio, no SDK — just the C# compiler that ships with Windows (.NET Framework 4.8):

```powershell
git clone https://github.com/Baougam/workbench.git
cd workbench
.\build.ps1           # bin\Workbench.exe
.\build.ps1 -Test     # + self-test (runs in a scratch folder, touches nothing real)
.\build.ps1 -Bilder   # + renders every screen to bin\bilder as PNG
```

Try it without installing: `$env:WORKBENCH_TESTROOT = "$env:TEMP\wb"; .\bin\Workbench.exe --portable`

**Releasing a new version:** bump the number in `VERSION` and `Program.Version`, build, then on GitHub go to
*Releases › Draft a new release* (tag e.g. `v0.2.0`) and attach `bin\Workbench.exe`.

## Code layout

The code and comments are in German; the app itself is in English.

| File | What’s in it |
|---|---|
| `src/Engine.cs` | Watchers (Downloads, Desktop, Workbench folders), download detection, timers, expiry → Recycle Bin |
| `src/Model.cs` | Paths, durations, the timer list (`%LOCALAPPDATA%\Workbench\fristen.json`), settings |
| `src/Frage.cs` | The card after a download or save (time slider) |
| `src/Hauptfenster.cs` | Main window: layers, Expiring, Recycle Bin, Settings, gestures, history |
| `src/Tisch.cs` | Free-form surface: arranging, lines, colors, notes, press-and-hold menu |
| `src/Netz.cs` | Graph view with search |
| `src/Einfuehrung.cs` | First-run tour |
| `src/Papierkorb.cs` | Reads the real Recycle Bin (`$Recycle.Bin`) — restore and delete without dialogs |
| `src/Cloud.cs` | Finds Google Drive, OneDrive, Dropbox, iCloud |
| `src/Install.cs` | Self-install, autostart, Explorer sidebar, right-click menu, uninstall |
| `src/Browser.cs`, `browser/` | Browser extension (embedded in the exe), native messaging host, guided setup |
| `src/Update.cs` | Quiet update check (reads `VERSION`) |
| `src/Theme.cs`, `src/Parts.cs` | Look and feel, icons, thumbnails, calendar |
| `src/SelfTest.cs` | `--selftest` and `--render` |
