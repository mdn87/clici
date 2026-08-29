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
    Result (2026-08-29, build `0.1.0+01f99de8`): **PASS**, on the second attempt.
    After a reboot at 15:14:12 the box took RDP logons at 15:33:40 and 15:35:36.
    With the `Run` value present (recorded at 15:37:20 by a snapshot run from an
    interactive shell, not the agent session -- see step 11), clici logged
    `event name=started` at 15:34:41 and
    15:35:57 -- 61s and 21s after those logons -- and pid 22624 was live from the
    installed path. Neither start has a preceding `event name=stopped`, which is
    what separates a logon autostart from a manual relaunch.

    The first attempt reported PASS and was wrong. It concluded clici had
    autostarted because the process start time was later than the snapshot, but
    that only showed the app restarted: the log has `stopped` at 14:57:04 and
    `started` at 14:57:10, six seconds apart, with no sign-out between them.
    `Win32_LogonSession`, `quser`, `explorer.exe` and `LastBootUpTime` all put the
    only logon at that point back at 2026-08-20 23:33. A process starting after the
    *snapshot* proves nothing; it has to start after the *logon*, with no `stopped`
    immediately before it.
11. Disable it, sign out/in; confirm clici does NOT start automatically.
    Result (2026-08-29, build `0.1.0+01f99de8`): **PASS**, on the third attempt
    and the first one that was actually run. Snapshot at 16:49:13 recorded the
    `Run` value absent, 40 `event name=started` entries and pid 31864 live, in
    the logon that began at 16:01:09. After signing out and back in, the verify
    pass found the `Run` value still absent, **zero** clici instances, still 40
    `started` entries, and a last log line of 16:49:08 -- older than the
    snapshot itself. Nothing started.

    The sign-out was real, not a disconnect. `Win32_LogonSession` shows a new
    LogonType 10 session (ids 57542865/57543293) at 16:49:44 against the
    snapshot's 16:01:09, and `explorer.exe` is pid 32476 started 16:49:44; a
    reconnect leaves the old `explorer.exe` alive, so a restarted shell is what
    separates the two. Boot was 15:14:12, so this was a sign-out and not a
    reboot.

    The pre-state holds up on its own evidence: the value read absent before the
    sign-out and absent after it, nothing writes it but the tray toggle and the
    installer, and in both earlier attempts a present value produced an autostart
    within a minute of logon. Here there was none.

    **Read the Run value from an ordinary interactive shell, not from an agent
    session.** The two earlier attempts were staged from an agent session whose
    processes cannot see the `clici` value under
    `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`; both recorded the value
    as absent -- "auto-start is off" -- while it was present, and clici
    autostarted as configured. Verified 2026-08-29 16:42-16:43 with
    `tools/proof/Compare-RunKeyView.ps1` run from both: identical machine, user,
    SID, session and elevation, and an identical list of the other 20 values, but
    the agent-side view enumerates 20 values and the interactive view 21. The
    agent side reads the value as absent through all four of .NET `HKCU`, .NET
    `HKEY_USERS\<sid>`, `reg.exe`, and a raw `RegistryKey` handle; the
    interactive side reads it as present through all four. Freshly spawned
    processes on the agent side behave the same way, so it is not a stale handle,
    and disabling the tool sandbox did not change it. Cause not identified; the
    asymmetry is reproducible and that is enough to distrust the agent-side
    reading. The 16:49 snapshot and verify above were run interactively, which is
    why they count, and an interactive `Compare-RunKeyView.ps1` at 17:06 confirms
    the value is really gone: `count = 20`, `clici` absent through all four read
    paths. At 16:42, with auto-start still on, that same interactive view had 21
    values including `clici`, against the agent side's 20; both now read 20.

    An earlier version of this entry blamed an unidentified external writer for
    resurrecting the value. That was wrong -- it was the measurement, not the
    machine. No clici defect came out of any of it.

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
