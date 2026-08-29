# clici Installer Test Runbook

Manual verification for the Inno Setup installer. Run after `tools/Build-Installer.ps1`.

## Build
1. Run `tools/Build-Installer.ps1`.
2. Confirm `artifacts/installer/clici-<version>-win-x64-setup.exe` exists.

## Fresh install
1. Run `setup.exe`. Leave **Start clici when I sign in** checked. Finish; let clici launch.
2. Confirm the clici icon appears in the notification area.
3. Confirm install location `%LOCALAPPDATA%\Programs\clici\clici.exe` exists.
4. Confirm a **clici** Start Menu shortcut exists.
5. Confirm an **Add or remove programs** entry named **clici** with the correct version.
6. Confirm registry value `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\clici`
   equals `"%LOCALAPPDATA%\Programs\clici\clici.exe"` (quoted).

## Tray auto-start toggle
7. Open the tray menu; confirm **Start with Windows** is checked (matches step 6).
8. Uncheck it; confirm the `Run\clici` value is removed.
9. Re-check it; confirm the value is written back with the quoted installed path.

## Auto-start behaviour
10. With auto-start enabled, sign out and back in; confirm clici starts automatically.
    Result (2026-08-29, build `0.1.0+01f99de8`): **NOT PROVEN.** An earlier run
    reported PASS, but it was a false pass. The evidence it relied on -- a
    `clici.exe` whose start time (14:57:07) was later than the snapshot -- was an
    application restart, not an autostart: the log shows `event name=stopped` at
    14:57:04 followed by `event name=started` at 14:57:10, six seconds apart. No
    sign-out had happened. `Win32_LogonSession`, `quser`, `explorer.exe`'s start
    time and `LastBootUpTime` all agree the only interactive logon began
    2026-08-20 23:33, immediately after boot. A process starting after the
    *snapshot* proves nothing; it has to start after the *logon*.
11. Disable it, sign out/in; confirm clici does NOT start automatically.

Steps 10 and 11 are checked by `tools/proof/Test-LifecycleStep10-11.ps1`, which needs
a snapshot taken *before* signing out:

```powershell
# with the tray toggle already in the state the step calls for
./tools/proof/Test-LifecycleStep10-11.ps1 -Step 11 -Snapshot
# sign out, sign back in, then
./tools/proof/Test-LifecycleStep10-11.ps1 -Step 11
```

Caveats when running steps 10/11 over RDP (4070pc is `rdp-tcp#0`):

- **Sign out, do not disconnect.** Closing the RDP window or hitting Disconnect
  leaves the interactive logon session alive, so the `Run` key never fires again
  and the check cannot mean anything. Use Start menu > account > **Sign out**.
- Signing out kills everything in that session, including the agent session
  driving this test. Run the verify pass from a fresh terminal after signing
  back in.
- The checker reports `session kind` and `logon at` so a disconnect/reconnect is
  visible as `real sign-out? NO` rather than passing silently.

The snapshot records the Run-key value, the `started` count, the running build and
the live PIDs to `%LOCALAPPDATA%\clici\proof\step<N>-snapshot.json`. The verify
pass compares against it and against the current session's logon time, so re-running
it *without* actually signing out reports FAIL rather than a false pass.

## Upgrade / stop-before-install (AppMutex spike result)
12. With clici running, re-run `setup.exe`.
13. Spike result (2026-08-07, silent `/VERYSILENT` automation): **AppMutex +
    CloseApplications alone was INSUFFICIENT** — a re-install while clici was
    running aborted with **exit 1**, because clici's tray app has no top-level
    window for `CloseApplications` to close. The `[Code]`
    `InitializeSetup`/`InitializeUninstall` `taskkill /IM clici.exe /F` fallback
    was added; after it, reinstall-while-running returns **exit 0**.

## Uninstall
14. Uninstall via Add/Remove Programs.
15. Confirm the Start Menu shortcut is gone, the `Run\clici` value is gone, and no
    `clici.exe` process remains running. (Automated silent run confirmed all three,
    plus the ARP entry removed. Note: force-killing a running clici immediately
    before delete can briefly leave the `%LOCALAPPDATA%\Programs\clici` *folder*
    behind due to an AV lock on the just-terminated exe; exiting clici from the
    tray before uninstalling avoids this, and Windows clears the empty folder on
    reboot.)
