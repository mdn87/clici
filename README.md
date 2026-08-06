# clici

clici is a tiny Windows tray utility that corrects the unwanted common
left margin sometimes added when copying multiline output from CLI agents such
as Codex and Claude Code. Copy and paste normally: there is no cleanup window
and no extra paste step.

This behavior is **margin normalization**, not whitespace trimming. clici
removes one confidently detected common margin while preserving meaningful
nested indentation.

The normative requirements and Windows proof plan are in the
[introductory-slice specification](docs/introductory-slice-spec.md).

## Example

Before:

```text
  First line
  Second line
    Nested line
```

After:

```text
First line
Second line
  Nested line
```

## Conservative normalization rules

The default policy considers text only when it contains at least two nonblank
lines. Blank lines are excluded from all percentages.

Normalization occurs only when:

- at least 70% of nonblank lines begin with at least two ASCII spaces; and
- fewer than 20% of nonblank lines begin at column zero.

When both conditions pass, clici removes exactly two ASCII space characters
from every line that has them. It does not use `TrimStart`, does not interpret
tabs as spaces, and does not change lines with fewer than two leading spaces.
Four spaces therefore become two spaces.

CRLF, LF, mixed line endings, and trailing newlines are retained exactly.
Unicode text is preserved. If the confidence checks fail, the original .NET
string is returned unchanged. Clipboard replacement is skipped when the result
equals the source.

The confidence ratios and margin width are configurable.

## Windows and process scope

clici targets Windows and uses WinForms, `NotifyIcon`, and
`AddClipboardFormatListener`. It has no main window, web UI, local server, or
network access.

The first version watches clipboard changes only while an approved terminal
process owns the foreground window. It does **not** install a global keyboard
hook. Foreground-window detection is behind an interface so another targeting
strategy can be evaluated later without changing the normalization core.

Default approved process-name candidates are:

- `WindowsTerminal`
- `pwsh`
- `powershell`
- `cmd`
- `conhost`
- `claude`
- `codex`

These are editable candidates, not an exhaustive compatibility claim.
Excluded process names take precedence over allowed names.

## Tray controls

Right-click the tray icon to:

- enable or disable clici (the choice is saved);
- pause or resume normalization for the current run;
- open the configuration file;
- open the configuration folder; or
- exit.

Exit unregisters the clipboard listener before the application context shuts
down.

## Configuration

On first run, clici creates:

```text
%LOCALAPPDATA%\clici\config.json
```

The default file is equivalent to:

```json
{
  "enabled": true,
  "allowedProcessNames": [
    "WindowsTerminal",
    "pwsh",
    "powershell",
    "cmd",
    "conhost",
    "claude",
    "codex"
  ],
  "excludedProcessNames": [],
  "minimumMarginLineRatio": 0.7,
  "maximumColumnZeroLineRatio": 0.2,
  "marginSpacesToRemove": 2,
  "diagnosticLogging": false
}
```

Ratios must be from `0` through `1`, and the margin width must be from `1`
through `16`. Invalid fields fall back to safe defaults. A missing or malformed
file does not terminate the application. Restart clici after manually editing
the configuration.

## Privacy

clici is local-only:

- no telemetry;
- no analytics;
- no network access;
- no clipboard-content logging.

Optional diagnostic logging is disabled by default. When enabled, it records
only timestamps, process names, decision types, exception types, and aggregate
line counts. It never records clipboard contents or copied fragments.

## Build and test

Requirements:

- Windows 10 or later;
- .NET 10 SDK.

From the repository root:

```powershell
dotnet restore clici.sln
dotnet build clici.sln --configuration Release --no-restore
dotnet test clici.sln --configuration Release --no-build
```

The core library targets plain `net10.0` and has no WinForms or other Windows UI
dependency. The tray application targets `net10.0-windows`.

## Run locally

```powershell
dotnet run --project src/clici.App/clici.App.csproj --configuration Release
```

Find the clici icon in the Windows notification area. Right-click it and choose
**Exit** to stop the application cleanly.

## Current limitations

- The initial text write path replaces the clipboard with Unicode text. If a
  qualifying clipboard item also contains non-text formats, those formats may
  be lost. Clipboard replacement is isolated behind `IClipboardService` so
  multi-format preservation can be added without touching normalization.
- Clipboard operations are attempted up to four times with short bounded
  delays; clici fails safely if another process continues to hold the clipboard.
- Process matching uses the process that owns the foreground window at the time
  of the clipboard notification. A background writer can therefore be
  misattributed when a terminal is foreground, and a fast focus change can miss
  a terminal copy. Terminal host and shell behavior also varies, so the default
  names may need local adjustment.
- Every successful normalization is a new clipboard write and may create a
  duplicate entry in Windows clipboard history or synced clipboard tools.
- clici allows one running instance per Windows session; a second instance exits
  before creating a tray icon or clipboard listener.
- Configuration editing is file-based and changes require a restart.
- There is no installer, startup registration, auto-update, or global keyboard
  hook.

## Planned next steps

- exercise clipboard behavior across Windows Terminal, PowerShell, cmd, Codex,
  and Claude Code;
- preserve additional clipboard formats during eligible text replacement;
- evaluate clipboard-owner correlation, Windows clipboard history, and RDP
  behavior;
- add optional start-with-Windows support;
- add packaging only after runtime behavior is validated;
- evaluate process-targeting refinements before considering any keyboard hook.

## License

[MIT](LICENSE)
