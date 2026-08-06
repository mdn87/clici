# clici v0.1 proof run ledger

- Run ID:
- Commit:
- Windows:
- .NET SDK:
- Operator:

Do not paste clipboard text or fragments into this ledger. Refer to synthetic
fixtures only by ID.

| Case | Spec step | Source / process | Destination | Fixture | Expected | Actual category | Result | Evidence / notes |
|---|---:|---|---|---|---|---|---|---|
| BASELINE | — | N/A | N/A | N/A | Restore, format, build, tests pass |  |  |  |
| M01 | 1 | clici tray | N/A | N/A | One tray icon; no main window |  |  |  |
| M02 | 2 | Windows Terminal + PowerShell | Notepad | FX-01–09 | Eligible normalizes once; ineligible unchanged |  |  |  |
| M03 | 3 | Windows PowerShell and cmd | Notepad | FX-01–04 | Expected policy result |  |  |  |
| M04 | 4 | Codex and Claude Code hosts | Notepad | FX-01–04 | Expected policy result |  |  |  |
| M05 | 5 | Representative approved source | Terminal/editor targets | FX-01–03 | Paste preserves expected text |  |  |  |
| M06 | 6 | Disallowed foreground app | Notepad | FX-01 | Unchanged |  |  |  |
| M07 | 7 | Background writer; terminal foreground | N/A | FX-01 | Observation for TG-01 |  |  |  |
| M08 | 8 | Allowed and excluded process | N/A | FX-01 | Exclusion wins; unchanged |  |  |  |
| M09 | 9 | Tray Enabled toggle | N/A | FX-01 | Disabled unchanged; re-enabled normalizes |  |  |  |
| M10 | 10 | Tray Pause/Resume | N/A | FX-01 | Paused unchanged; persisted Enabled unchanged |  |  |  |
| M11 | 11 | Tray open actions | Config file/folder | N/A | Both locations open |  |  |  |
| M12 | 12 | Malformed config fallback | N/A | N/A | Toggle is run-only; malformed bytes unchanged |  |  |  |
| M13 | 13 | Rapid copies | N/A | FX-01/04/03 | No stale suppression or repeated stripping |  |  |  |
| M14 | 14 | Win+V history | N/A | FX-01/03 | Expected restoration behavior recorded |  |  |  |
| M15 | 15 | RDP/remote clipboard | Local/remote | FX-01/03/04 | Behavior recorded or prerequisite blocked |  |  |  |
| M16 | 16 | Two clici processes | N/A | N/A | Second exits; one listener |  |  |  |
| M17 | 17 | Clipboard contention | N/A | FX-01 | Fails safely and remains responsive |  |  |  |
| M18 | 18 | Tray Exit | N/A | N/A | Process exits; zero listener windows |  |  |  |
| M19 | 19 | Rich-text source | N/A | FX-10 | Format inventory and loss recorded |  |  |  |
