# Task Priority Memory

A tiny Windows tray utility that does **one job well**: it lets you change a
program's **CPU priority** and **CPU affinity** — and then *remembers* those
choices. Whenever that program launches again, Task Priority Memory detects it
and re-applies your saved settings automatically.

Think "the two most useful columns of Task Manager, made permanent."

## What it does

- **Only priority & affinity.** No killing processes, no memory pokes — just the
  two knobs that actually make apps run faster/quieter, so it's safe to leave on.
- **Remembers per program.** Set `chrome` to *Below Normal* on CPUs 0–3 once;
  it sticks forever, across reboots.
- **Auto-applies on launch.** A lightweight background watcher notices new
  processes and enforces the matching saved preference.
- **GUI.** A Processes tab to pick a running app and set its priority/affinity,
  and a Saved Preferences tab to view / toggle / remove remembered programs.
- **Extremely low usage.** No busy loops — a single timer diffs the process list
  every few seconds and idles at ~0% CPU. Runs on the built-in .NET Framework
  (no runtime to install).
- **Starts with Windows.** One click adds a per-user startup entry (no admin,
  no UAC prompt at logon).
- **Hides in the tray.** Lives in the notification area (the taskbar
  "show hidden icons" ^ flyout). Double-click the icon to open the window;
  closing the window just hides it again.

## Requirements

- Windows 8.1 / 10 / 11
- .NET Framework 4.8 (already present on all supported Windows versions)

## Usage

1. Launch `TaskPrioMemory.exe`. It appears as an icon in the tray.
2. Double-click the tray icon to open the window.
3. **Processes** tab → pick a program → choose a **Priority** and/or check the
   **CPU affinity** boxes.
   - **Apply now** — changes the running instance(s) immediately.
   - **Save + remember** — also stores the preference for next time.
4. **Saved Preferences** tab → see everything you've remembered. Untick a row to
   temporarily disable it, or select and **Remove** it.
5. Right-click the tray icon for **Run at Windows startup**, **Apply preferences
   to running apps now**, or **Exit**.

### A note on permissions

Changing your own apps needs no special rights. To manage processes owned by
another user or by an elevated app, run Task Priority Memory **as
administrator** (right-click → *Run as administrator*). When a change is denied,
the app tells you instead of failing silently.

### Where settings are stored

`%AppData%\TaskPrioMemory\`
- `rules.json` — your remembered programs
- `settings.json` — poll interval and options

Use **Open data folder** on the Saved Preferences tab to jump there.

## Building

The project targets **.NET Framework 4.8** with an SDK-style project, so it
builds with either Visual Studio or the `dotnet` CLI **on Windows**:

```powershell
dotnet build TaskPrioMemory.sln -c Release
# output: src/TaskPrioMemory/bin/Release/net48/TaskPrioMemory.exe
```

Or open `TaskPrioMemory.sln` in Visual Studio 2022 and press F5.

CI (`.github/workflows/build.yml`) builds every push on `windows-latest` and
uploads the compiled exe as an artifact.

## Project layout

```
src/TaskPrioMemory/
  Program.cs                 entry point + single-instance guard
  Model/                     ProcessRule, AppSettings, affinity formatting
  Storage/                   JSON persistence + RuleStore
  Core/
    ProcessManager.cs        the ONLY mutations: set priority / set affinity
    ProcessWatcher.cs        low-usage timer that auto-applies rules
    StartupManager.cs        HKCU "run at startup" toggle
  UI/
    TrayApplicationContext.cs  tray icon, menu, app lifetime
    MainForm.cs                the two-tab window
    IconFactory.cs             runtime-generated icon (no binary asset)
```

## License

MIT — see [LICENSE](LICENSE).
