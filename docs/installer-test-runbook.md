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

### Result -- Build 1-2 and steps 1-9 (2026-08-29, build `0.1.0+b3df73a`)

**PASS, all nine.** Driven by `tools/proof/Test-LifecycleStep1-9.ps1`, which
records machine assertions and operator observations as separate kinds in
`%LOCALAPPDATA%\clici\proof\steps1-9-result.json`. A y/n from a person is not an
install log and the record does not treat them alike.

**Build 1-2.** `tools/Build-Installer.ps1` produced
`artifacts/installer/clici-0.1.0-win-x64-setup.exe` at 18:22:59, 45 MB,
publishing `0.1.0+b3df73a`. Unsigned, as designed.

**The install was made genuinely fresh first.** The `0.1.0+a891d0a0` build left
behind by the steps 12/13 run was uninstalled at 18:23, exit 0; the uninstall log
records deleting `clici.exe`, the shortcut, the ARP key and the `Run\clici`
value. Only the empty `Programs\clici` folder survived -- the AV-lock behaviour
already noted at step 15.

1. *machine.* Log opened 18:28:00 with `/SL5=... /LOG=...` and **no** `/SILENT`,
   so it was the wizard. `Installation process succeeded` at 18:28:08, and clici
   logged `event name=started version=0.1.0+b3df73a` at 18:28:11 as pid 32512.
   That launch is itself evidence the run was not silent: `[Run]` carries
   `skipifsilent`, so a silent install would have started nothing.
2. *operator.* clici icon present in the notification area.
3. *machine.* `%LOCALAPPDATA%\Programs\clici\clici.exe` present, ProductVersion
   `0.1.0+b3df73a29ae7b3cdbc5ff6654b2ee00e8847697d`.
4. *machine.* `clici.lnk` present in the Start Menu; the install log records
   creating the icon.
5. *machine.* Interactive shell reads DisplayName `clici`, DisplayVersion
   `0.1.0`; the install log records `Creating new uninstall key`.
6. *machine.* Interactive shell reads the value as
   `"C:\Users\Matt\AppData\Local\Programs\clici\clici.exe"`, byte-equal to the
   expected quoted path, with 21 values under `Run`; the install log records
   `Successfully created or set the value`.
7. *operator.* Tray menu shows **Start with Windows** checked, agreeing with 6.
8. *machine.* After unchecking, the value reads absent and the count drops 21 to
   20.
9. *machine.* After re-checking, the value reads the quoted path again and the
   count returns 20 to 21.

**The registry reads above came from an interactive shell, and the contrast is
now sharply timed.** Step 9's read at 18:35:05 saw 21 values with `clici`
present. Twenty-eight seconds later, at 18:35:33, an agent-session read on the
same machine, user and session saw 20 values with `clici` absent, and could not
see the ARP uninstall key created at 18:28:08 either -- neither by a direct key
test nor by enumerating the `Uninstall` subkeys -- although an agent-side read
had seen that same ARP entry at 17:35. So the blindness is not confined to the
`Run` value, and it is not stable within a session. Treat every HKCU read from
an agent session on this machine as void.

The first attempt at this walkthrough installed cleanly and then stopped before
writing any result file, leaving steps 2 and 6-9 with no evidence and no trace of
where it stopped. The script now writes its result from a `finally` block and
takes `-Resume` to re-enter at step 2; steps 2-9 above were completed that way at
18:35.

**State left behind:** auto-start is **ON** (step 9 re-checked it), and this
machine now runs `0.1.0+b3df73a`, a build from `main` -- still not the released
`01f99de` artifact.

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

> **Silent installs do not launch clici.** The installer's `[Run]` entry carries
> `skipifsilent`, so `/VERYSILENT` and `/SILENT` install everything — files,
> shortcut, ARP entry, and the `Run` value when auto-start is selected — but do
> not start clici at the end of setup. This is Inno Setup behaving correctly, not
> a defect. It matters twice here: an automated run of steps 12–15 must launch
> clici itself before testing reinstall-while-running, and anyone deploying clici
> silently should expect it to start at the *next* sign-in rather than
> immediately.

## Upgrade / stop-before-install (AppMutex spike result)
12. With clici running, re-run `setup.exe`.
    Result (2026-08-29 17:35, local build `0.1.0+a891d0a0`): **PASS**. clici was
    launched from the installed path (pid 10780 at 17:35:14) and setup was run at
    17:35:18 as `/VERYSILENT /SUPPRESSMSGBOXES /TASKS="" /LOG=...`. It returned
    **exit 0** in 2s. Afterwards: zero clici processes, `clici.exe` replaced (the
    log records the existing 13:17:16 file overwritten by ours stamped 08:49:04),
    the Start Menu shortcut recreated, and the ARP entry `clici 0.1.0` present.

    The `[Code]` fallback is what carried it, and the log says so: `RestartManager
    found no applications using one of our files`, four seconds after clici was
    confirmed running. Nothing was left for `CloseApplications` to find because
    `InitializeSetup`'s `taskkill /IM clici.exe /F` had already closed it. This is
    the 2026-08-07 spike result reproduced end to end on the shipping installer.

    It also confirms the `skipifsilent` note from the other side: setup finished
    with clici *not* running, and had to be started by hand afterwards.

    `/TASKS=""` was passed on purpose so the `startup` task stayed deselected and
    the `Run` value was not rewritten. The install log shows no `[Registry]`
    entry, which is the only way to check that without trusting an agent-side
    registry read (see step 11). Auto-start is still off, as step 11 left it.
    A default silent run would have re-selected the task and turned it back on.

    Build caveat: `artifacts/installer/clici-0.1.0-win-x64-setup.exe` is a local
    08:49 build from `a891d0a`, *not* the released v0.1.0 artifact (CI-built from
    `01f99de`, published 09:17 and now attached to the GitHub release). This run
    installed the local one over the release build that was on the box.
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

### Result -- steps 14-15 (2026-08-29, build removed: `0.1.0+a891d0a0`)

**PASS, with one scope limit stated up front:** the uninstall was driven by the
installed uninstaller, not by clicking through **Add or remove programs**.
`%LOCALAPPDATA%\Programs\clici\unins000.exe /VERYSILENT /SUPPRESSMSGBOXES /LOG=`
at 18:23:21, exit 0. That is the same binary ARP invokes, but the Settings UI
path itself was not exercised, so step 14's wording is only partly covered. This
ran as the teardown before the fresh install recorded at steps 1-9, so the build
removed was `0.1.0+a891d0a0`, the one the steps 12/13 run left behind.

clici **was running** when it started, so the `[Code]` `InitializeUninstall`
force-close path was exercised too, not just the file deletes.

*machine.* The uninstall log records, in order at 18:23:21:
`Deleting registry key HKEY_CURRENT_USER\...\Uninstall\{B7A6E4C2-...}_is1`,
`Deleting file ...\Start Menu\Programs\clici.lnk`,
`Deleting file ...\Programs\clici\clici.exe`,
`Deleting registry value HKEY_CURRENT_USER\...\Run\clici`, then
`Deleting Uninstall data files`.

*machine.* State immediately after: `clici.exe` gone, `clici.lnk` gone, zero
clici processes. `%LOCALAPPDATA%\Programs\clici` remained, empty -- the AV-lock
behaviour described above, and this run force-killed a running clici, which is
precisely the condition that provokes it.

**The ARP entry's removal rests on the uninstall log alone.** The post-uninstall
registry check was made from an agent session, and by the rule established at
steps 1-9 every HKCU read from an agent session on this machine is void: that
same session could not see that key at 18:30 when it demonstrably *did* exist.
The log is the evidence here; the read is not, in either direction.

Log kept at
`artifacts/v0.1-proof/installer-steps1-9-20260829/clici-step1-uninstall.log`
(untracked -- `artifacts/` is gitignored).
