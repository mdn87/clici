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
11. Disable it, sign out/in; confirm clici does NOT start automatically.

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
