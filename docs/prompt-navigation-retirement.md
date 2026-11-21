# Prompt navigation retirement

On October 25, 2026, Matt reported that turn tracking and keyboard jumping
remained unreliable after the October 24 repairs and approved retirement.
The feature is retired. Further qualification and tray integration are cancelled.

The experiment ties native message identity to colored blank rows in Terminal
scrollback, then admits keyboard shortcuts through short-lived native leases
and verifies scrolling through COM. Repairs addressed nonce matching, scan
deadlines, and damaged older paint. The Claude renderer still loses markers in
some real sessions; its cause is unresolved. Passing fixtures and one successful
live check did not establish sustained reliability. No new root cause is claimed
for the October 25 report. Another local workaround would not establish it either.

## Applied shutdown

- The existing installer removed its managed PowerShell and bash startup blocks
  and restored the previous Codex screen setting. Later unrelated settings remain.
- All 15 installed release helper executables were renamed with `.retired`
  suffixes. This prevents supervisors and shells with inherited activation
  variables from starting new keyboard hooks. Bundles intentionally fail their
  original manifest verification while retired.
- Three active helpers were stopped. All three owning Codex processes were
  verified to retain their original process identities; conversations were not
  restarted. No navigation helpers remained after shutdown.
- The clipboard tray app and its join-lines shortcut were not changed.

The tested retirement command is:

```powershell
./tools/proof/prompt-navigation/Retire-RendererNavigation.ps1
./tools/proof/prompt-navigation/Retire-RendererNavigation.ps1 -Apply
```

The first command previews the exact settings, helpers, and owning processes.
The second backs up current settings, uses the existing disable implementation,
archives helper launch targets, stops only matching installed helper processes,
and verifies their parents survived. It does not delete files.

The applied settings snapshot and process plan are under
`%LOCALAPPDATA%/Clici/renderer-navigation/backups/retirement-20261025-070541-2683`.
Original installer backups, renderer bundles, source, and historical proof remain.
Reinstating this experiment would require an explicit decision and restoring the
archived executables before using the original installation instructions.

Existing terminals can retain their inherited PATH and renderer variables,
and existing CLI processes retain their renderer patches until they exit.
Their helpers cannot restart. Fresh terminal tabs use the ordinary CLI startup
path. The pinned binaries were not replaced inside live conversations.

## Local cleanup on November 20, 2026

Matt approved removing the regenerable material this experiment left in the
local checkout. Removed from `artifacts/` and `tools/proof/prompt-navigation/`:
the Rust package cache, the Codex source checkouts, compiler output, the
downloaded Windows Terminal and Claude copies, and every built renderer bundle
executable (10.35 GB in 110,295 files). The `artifacts/` paths cited by the
prompt-navigation documents no longer hold bundles or binaries.

Still present locally: the small evidence, audit, log, and session files in
their original folders, and
`artifacts/retired-navigation-evidence-20261120.zip`, which holds a copy of
those evidence files plus the three patch drafts from the removed Codex
checkouts. Installed bundles and backups under
`%LOCALAPPDATA%/Clici/renderer-navigation` were not touched. The tracked patch
and proof tooling in `tools/proof/prompt-navigation` are unchanged, so a
bundle can be rebuilt from source.
