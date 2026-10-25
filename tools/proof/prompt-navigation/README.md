> Retired on October 25, 2026. This document is historical; activation and further qualification are cancelled. See the [retirement record](../../../docs/prompt-navigation-retirement.md).

# Windows Terminal navigation probe

Development instrumentation for NAV-001 through NAV-005. It is independent of
the tray application and never accesses the clipboard. Basic fixture commands
do not recognize Claude/Codex sessions or message roles or install input hooks.
Read the [proof report](../../../docs/prompt-navigation-proof-report.md) before
using its findings as support evidence.

The opt-in [Codex renderer experiment](codex-renderer/README.md) adds a separate
source patch and private-pipe helper with separately opted-in keyboard interception.
`renderer-inspect HWND bindings-or-report.json`
observes painted RGB anchors without scrolling. `anchor-fixture directory`
prints repeated synthetic prompts and writes `anchors.json`; `show HWND`
restores a proof window (Windows may still withhold foreground focus).
`renderer-bridge parentPID` is the native renderer's private child protocol.
These commands do not enable navigation in the tray app.

The probe targets .NET 10 on Windows; the helper scripts require PowerShell 7.
It uses the COM `IUIAutomation` client
from the pinned NuGet package `Interop.UIAutomationClient` 10.19041.0. It does
not use the managed `System.Windows.Automation` client. CI builds this project
with the solution; live desktop experiments remain explicit manual tests.

## Isolated fixture

Download the official Windows Terminal **1.24.12741.0 x64 portable ZIP** from
its [release](https://github.com/microsoft/terminal/releases/tag/v1.24.12741.0).
Verify its SHA-256 against the release asset digest before extracting:
`7aaa8321064c45d5af79205db13deb87f146f644bd8abdfae29f0155d7ce744e`.
Use a fresh extraction below the repository's ignored `artifacts/` directory.
Do not point the launcher at an installed Terminal or an existing settings store.

```powershell
./tools/proof/prompt-navigation/Start-Fixture.ps1 `
  -TerminalDirectory ./artifacts/terminal-portable `
  -RunDirectory ./artifacts/navigation-clean
```

For an OSC 133 command-mark experiment, use another fresh portable extraction
and run directory with `-Osc`. The launcher refuses existing settings and run
directories. It creates `.portable` and an independent settings store with
1,000 history rows and Ctrl+Up/Down native mark bindings. Those bindings affect
every profile **in that disposable instance**. The fixture logs received keys.
Enter submits a synthetic turn; S adds 35 streaming rows; Q exits.

To append output without keyboard input (essential for the live-follow test):

```powershell
Add-Content ./artifacts/navigation-clean/input-events.jsonl.control 'stream'
```

## Probe commands

Run `dotnet <run>/probe/PromptNavigation.Probe.dll windows` to get the HWND.
Use `Invoke-Probe.ps1` for a disposable worker with a ten-second deadline:

```powershell
./tools/proof/prompt-navigation/Invoke-Probe.ps1 `
  -ProbeDll ./artifacts/navigation-clean/probe/PromptNavigation.Probe.dll `
  -EvidenceFile ./artifacts/navigation-clean/snapshot.json `
  -ProbeArguments @('snapshot', '<HWND>', '32768')
```

Commands after the DLL path:

| Command | Purpose |
| --- | --- |
| `windows` | List windows titled `CLICI-PROOF-*` from a portable Terminal. |
| `snapshot HWND [characters]` | Read the focused `TermControl`, visible ranges, bounded document tail, selection, pane runtime IDs, focus, and bounds. Default 32,768; maximum 262,144 characters. |
| `inspect HWND CLICI_FIXTURE_marker` | Inspect up to 20 marker occurrences and text attributes, with a 262,144-character search-text bound. This is diagnostic evidence, not a role classifier. |
| `jump HWND CLICI_FIXTURE_marker` | Scroll to the first exact synthetic marker. Duplicate-aware production navigation must use verified positions instead. |
| `select HWND CLICI_FIXTURE_marker` | Deliberately select a synthetic marker to test selection preservation across subsequent scrolls. |
| `live HWND` | Scroll a fresh document range collapsed to its end into view. |
| `watch HWND seconds` | Record up to 100 UIA focus events from the test Terminal process for 1–30 seconds. Set the worker deadline above the observation duration. |
| `key HWND enter\|alt-shift-comma\|alt-shift-period\|ctrl-up\|ctrl-down\|stream\|finish\|cancel` | Send only these test keys; cancel means Ctrl+C for exiting a disposable CLI. |
| `place HWND x y width height` | Position the test window for a cropped screenshot. |
| `activate HWND` | Request UIA focus; Windows may reject foreground activation. |

Mutating commands require an explicitly named portable test window. Scrolls
also require that exact window and pane to remain active. Key delivery verifies
the foreground window. A title and executable guard are test safeguards, not
a production ownership/eligibility contract. Use only synthetic disposable tabs
in that Terminal process. Close them when finished.

`CurrentHasKeyboardFocus` can remain true while another application's window
is foreground; both signals matter. Focus event registration/removal runs on
the same MTA worker. Focus events alone do not identify the agent running in a
pane. The separate renderer bridge adds native identity, painted-anchor checks,
and bounded pre-Terminal key admission; see its runbook for qualification limits.

Snapshot text is padded by the provider. A character limit can truncate a
visible range; it must not be interpreted as the real viewport bottom. Reported
timings cover text/range operations after discovery, not cold worker startup.
The wrapper timeout kills only its own worker; it cannot reverse a scroll
already started. The probe itself is not a production COM cancellation design.

## Evidence to retain

Record versions, renderer options, Terminal settings, snapshots, key logs,
focus events, and screenshots cropped to the synthetic pane. Check the actual
viewport rather than trusting a successful COM return. Test clean and OSC
command-mark fixtures separately. Use passive output for live-follow checks;
sending a key may activate Terminal's snap-on-input behavior and confound them.

The current `PromptNavigation` core takes already classified, complete prompt
occurrences. Its caller must prove eligibility, coverage, and stable positions;
revalidate before scrolling; and commit a cursor only after checking the result.
It does not infer those facts from this probe's text or wire keyboard input.
