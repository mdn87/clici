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
The ordered execution work is in the
[v0.1 implementation-completion and proof plan](docs/v0.1-implementation-and-proof-plan.md).
The step-by-step operator procedure is in the
[v0.1 Windows test runbook](docs/v0.1-test-runbook.md).
The adversarial scale, race, crash/restart, rich-format, and Win+V findings are
in the [v0.1 resilience report](docs/v0.1-resilience-report.md).
The pre-release documentation-sync gate is the
[release checklist](docs/release-checklist.md).

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

clici considers text only when it contains at least **two** nonblank lines (a
wrapped command line plus its indented continuation is the smallest real case).
Blank lines are ignored when measuring indentation.

It detects the shared base margin from the actual leading indentation and only
accepts a base of exactly **two or four** ASCII spaces. It then removes that
width from every line, so relative nested indentation is preserved (a four-space
base collapses to column zero; a deeper nested line keeps the difference).

A line is treated as a **conflict** that blocks normalization — leaving the item
untouched — when it:

- begins at column zero while others are indented;
- begins with a single leading space (a one-space outlier); or
- is indented with a tab.

This is deliberately stricter than a ratio vote: a single one-space or tab line
is enough to refuse, because dedenting around it would reverse its relative
indentation.

One exception: a column-zero **first** nonblank line is treated as a selection
artifact, not a conflict — a drag selection that starts at the first visible
character captures that line without its margin. The first line is left
unchanged and excluded from margin detection, and the remaining lines must
satisfy the rules above on their own.

## Wrapped-line joining

A long command wrapped at the terminal's right edge copies as multiple lines,
which breaks it when pasted back into a shell. For trusted sources, clici
rejoins a copy into one line when it carries the **wrap signature**: no blank
lines, every line except the last at least 60 characters and within 15
characters of the longest line (word wrap leaves a ragged edge), the last line
no longer than that width, and no table or box-drawing framing. Fragments are
trimmed and joined with single spaces. Ordinary multiline content — code,
lists, paragraphs, tables — does not match and falls through to margin
normalization. Set `joinWrappedLines: false` to disable.

When the signature refuses a copy you know is one command, the global hotkey
(default `Ctrl+Alt+J`, configurable via `joinLinesHotkey`, empty to disable)
joins every nonblank line of the current clipboard unconditionally. The hotkey
skips the source allowlist — pressing it is the authorization — but still
honors the privacy, size, and rich-format gates.

clici does not use `TrimStart` and does not interpret tabs as spaces. CRLF, LF,
mixed line endings, and trailing newlines are retained exactly. Unicode text is
preserved. If the confidence checks fail, the original .NET string is returned
unchanged, and clipboard replacement is skipped when the result equals the
source. Removing the full base margin makes a second pass a no-op.

A fixed margin width is available as a profile override
(`autoDetectMarginWidth: false` with `marginSpacesToRemove`).

## Windows and process scope

clici targets Windows and uses WinForms, `NotifyIcon`, and
`AddClipboardFormatListener`. It has no main window, web UI, local server, or
network access.

clici attributes a copy to its source primarily by the clipboard **owner
process** (`GetClipboardOwner`), falling back to the foreground process only
when the owner is unknown. This means a disallowed background process that
writes the clipboard while a terminal is in the foreground is not misattributed,
and a terminal copy survives a focus change. It does **not** install a global
keyboard hook. Both signals are behind interfaces so another targeting strategy
can be evaluated later without changing the normalization core.

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

The first menu entry is the running build, for example
`clici 0.1.0+00860cdd708d`. See **Which build is running** below.

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
  "autoDetectMarginWidth": true,
  "marginSpacesToRemove": 2,
  "maximumTextCharacters": 2000000,
  "diagnosticLogging": false,
  "schemaVersion": 1
}
```

When `autoDetectMarginWidth` is `true` (default), the margin width is detected
from the copied text and constrained to two or four spaces. Set it to `false`
to force exactly `marginSpacesToRemove`, which must be from `1` through `16`.
`maximumTextCharacters` must be from `1` through `100000000`; the
two-million-character default prevents unusually large clipboard items from
freezing the tray thread or causing excessive memory use. Items above the limit
remain unchanged. `schemaVersion` identifies the configuration format and must
be at least `1`. Invalid fields fall back to safe defaults. A missing or
malformed file does not terminate the application. Restart clici after manually
editing the configuration.

## Clipboard privacy policy and rich text

clici **preserves** the source item's Windows clipboard privacy formats rather
than overriding them. It reads `CanIncludeInClipboardHistory`,
`CanUploadToCloudClipboard`, and `ExcludeClipboardContentFromMonitorProcessing`;
carries any explicit `0`/`1` value through to the rewrite unchanged; and adds
nothing when the source is silent. It never forces history or cloud inclusion,
and it treats clipboard history and cross-device cloud synchronization as
separate policies. When a source marks its content excluded from monitor
processing, clici skips the item entirely, so a private copy keeps its
protection through a rewrite.

Automatic mode is plain-text only: clici requires native Unicode text and
permits only known metadata and privacy formats. Items carrying HTML, RTF, CSV,
file lists, images, or unknown application formats are **skipped**, so clici
never leaves modified plain text sitting beside stale rich content that a
rich-text destination might paste with the original margin instead.

## Privacy

clici is local-only:

- no telemetry;
- no analytics;
- no network access;
- no clipboard-content logging.

clici performs no network activity of its own. Preserving a source's existing
`CanUploadToCloudClipboard` value is a Windows OS policy carried on behalf of
the source; clici never adds it.

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

## Install for the current Windows user

Build and run the installer from the repository root:

```powershell
.\tools\Build-Installer.ps1
```

This produces `artifacts\installer\clici-<version>-win-x64-setup.exe`. Run that
`setup.exe` to install clici for the current user (no administrator rights
required). It installs a self-contained copy to `%LOCALAPPDATA%\Programs\clici`,
adds a **clici** Start Menu shortcut, registers an Add/Remove Programs entry
with an uninstaller, and — if you leave **Start clici when I sign in** checked —
starts clici automatically at sign-in. You can change auto-start any time from
the tray menu's **Start with Windows** item. The installed copy does not require
the .NET SDK or runtime.

### Developer quick-install (no installer)

For a fast local install straight from source without building `setup.exe`:

```powershell
.\tools\Install-Clici.ps1
```

This publishes a self-contained copy to `%LOCALAPPDATA%\Programs\clici` and
creates a desktop shortcut. It is a developer convenience; the `setup.exe`
installer above is the supported distribution path.

## Which build is running

`Version` is a fixed `0.1.0`, so `FileVersion` cannot distinguish one build
from another and an installed copy that lags the source looks identical to a
current one. A feature that is merely absent from the installed binary then
reads as a feature that is broken.

The commit is recorded instead. The .NET SDK appends it to
`AssemblyInformationalVersion`, and `Directory.Build.targets` appends `.dirty`
when tracked files differ from `HEAD`, so a build carrying uncommitted edits
does not claim to be that commit. Three ways to read it, in order of effort:

- **Tray menu.** The first entry shows the version with a shortened commit.
- **The executable**, without launching it:

  ```powershell
  (Get-Item "$env:LOCALAPPDATA\Programs\clici\clici.exe").VersionInfo.ProductVersion
  ```

- **The log**, when `diagnosticLogging` is enabled. The `started` event carries
  the full stamp.

Compare the commit against `git log`. If the installed commit predates a change
you are testing, reinstall before investigating further.

## Current limitations

- Automatic mode is plain-text only. Items carrying HTML, RTF, CSV, files,
  images, or unknown application formats are skipped rather than rewritten;
  consistent rich-format normalization is future work.
- Source attribution combines the clipboard owner process and the foreground
  process, but the owner signal is not perfectly reliable (clipboard brokers and
  ownerless states exist). Integrated-terminal hosts such as VS Code and Cursor
  share one process across editor and terminal, so owner-process matching alone
  cannot yet distinguish a terminal copy from an editor copy in those hosts.
- Clipboard operations are attempted up to four times with short bounded
  delays; clici fails safely if another process continues to hold the clipboard.
- clici skips text above the configured size ceiling rather than risk a long UI
  stall or excessive transient memory use.
- Third-party clipboard managers may still record both the source copy and
  clici's rewrite.
- clici allows one running instance per Windows session; a second instance exits
  before creating a tray icon or clipboard listener.
- Configuration editing is file-based and changes require a restart.
- There is no MSI, auto-update, or global keyboard hook. (Per-user startup
  registration is supported — see **Start with Windows** in the tray menu.)

## Planned next steps

- exercise clipboard behavior across Windows Terminal, PowerShell, cmd, WSL,
  Codex, and Claude Code, and build a real source-fingerprint matrix;
- add focused-control detection (UI Automation) so VS Code and Cursor
  integrated terminals can be distinguished from editor copies;
- move clipboard operations to a dedicated STA worker thread;
- add tray last-action/last-skip status, one-shot normalization, and
  sequence-safe undo;
- evaluate consistent rich-format (HTML/RTF) normalization;
- evaluate signed/MSI packaging and RDP clipboard behavior.

## License

[MIT](LICENSE)
