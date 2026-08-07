# clici Standalone Installer & App Lifecycle — Design

**Date:** 2026-08-07
**Status:** Approved (design), pending implementation plan

## Problem & context

clici is a .NET 10 WinForms tray app. It *already* publishes as a self-contained,
single-file `clici.exe` (no .NET runtime required on the target) via
`tools/Install-Clici.ps1`, which copies the exe to `%LOCALAPPDATA%\Programs\clici`
and drops a desktop shortcut. So the *artifact* is already standalone.

What is missing is the **installed-application lifecycle**: a real installer with a
clean uninstall, Start Menu integration, Add/Remove Programs presence, auto-start at
sign-in, and code signing. This spec covers closing that gap.

## Goals

- A per-user (no-admin) **Inno Setup** installer producing a single `setup.exe`.
- Start Menu shortcut; Add/Remove Programs entry with a working uninstaller.
- Auto-start at sign-in, controllable both at install time and from the tray.
- **Self-signed** Authenticode signing of `clici.exe` and `setup.exe`, wired so a
  real certificate can replace the self-signed one later with no code changes.
- Publish configuration moved into the project (publish profiles) so plain
  `dotnet publish` reproduces the standalone exe.

## Non-goals (YAGNI — deferred)

- CI / GitHub Releases automation.
- MSIX packaging.
- Real OV/EV code-signing certificate.
- Auto-update.
- arm64 as a first-class signed release (profiles may exist; x64 is the shipped target).

## Decisions (locked during brainstorming)

| Decision | Choice |
|---|---|
| Installer technology | Inno Setup, per-user, no admin |
| Install location | `%LOCALAPPDATA%\Programs\clici` (unchanged) |
| Signing | Self-signed cert, local trust; signtool wired as an optional/guarded step |
| Auto-start control | Installer checkbox (default on) **+** tray "Start with Windows" toggle, sharing one `HKCU\...\Run\clici` value |
| Existing `Install-Clici.ps1` | **Kept** as a dev-install convenience alongside the official Inno path |
| Delivery | Three independently-testable phases |

## Architecture & components

### Phase 1 — Installer & packaging

**Versioning (single source of truth).** **Extend** the existing root
`Directory.Build.props` (which already sets `ImplicitUsings`/`Nullable`/`Deterministic`)
by adding `<Version>0.1.0</Version>` to its `PropertyGroup` — do **not** overwrite the
file. This feeds `AssemblyVersion`/`FileVersion`. The build script reads this value and
passes `/DAppVersion=<version>` to Inno so the exe, installer, and ARP entry all report
one version.

**Publish profiles.** Add `src/clici.App/Properties/PublishProfiles/win-x64.pubxml`
(and `win-arm64.pubxml`) with:
- `SelfContained=true`, `RuntimeIdentifier=win-x64`
- `PublishSingleFile=true`, `EnableCompressionInSingleFile=true`
- `DebugType=None`, `DebugSymbols=false`
- **No trimming** (WinForms is not trim-safe).

This moves publish settings out of the PowerShell script; `dotnet publish
-p:PublishProfile=win-x64` reproduces the standalone exe.

**Inno Setup script** `installer/clici.iss`:
- `PrivilegesRequired=lowest` (per-user, no elevation).
- `DefaultDirName={localappdata}\Programs\clici`.
- Stable `AppId` GUID (generated once, fixed forever so upgrades/uninstall track).
- `AppName=clici`, `AppPublisher`, version from `/DAppVersion`, `SetupIconFile` and
  `UninstallDisplayIcon` from the app icon.
- Start Menu shortcut under `{autoprograms}`.
- Inno auto-generates the uninstaller and the Add/Remove Programs entry.
- `[Tasks]` `startup` checkbox — "Start clici when I sign in" (default checked).
- `[Registry]` writes `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value
  `clici` = `"{app}\clici.exe"`, gated on the `startup` task, with `uninsdeletevalue`
  so uninstall removes it.
- **Stop-before-install/uninstall (graceful first).** Prefer Inno's `AppMutex=Local\clici`
  — clici's `SingleInstanceGuard` already holds that exact named mutex for the process
  lifetime, so Inno can detect the running instance through it and prompt/close rather
  than hard-killing. A raw `taskkill /IM clici.exe` risks landing mid-write during
  `_configurationStore.TrySave`. **Plan a short spike**: verify `AppMutex` (± `CloseApplications`)
  actually closes a hidden-window tray app; only if that proves insufficient, fall back
  to `[Code]` `Exec` `taskkill` as a last resort. Do not pick one blind.

**Build orchestrator** `tools/Build-Installer.ps1`:
1. `dotnet publish` with the chosen profile → `artifacts/publish/<rid>/clici.exe`.
2. (Optional) sign `clici.exe` — see Phase 3.
3. Run `ISCC` on `clici.iss` with `/DAppVersion` and the published exe path.
4. (Optional) sign `setup.exe`.
5. Output `artifacts/installer/clici-<version>-<rid>-setup.exe`.
- Fails fast if `ISCC.exe` is not found, with guidance to install Inno Setup.

**README update.** Once `setup.exe` is the official install path, update README's
"Install for the current Windows user" section (currently leads with
`tools/Install-Clici.ps1`) to lead with the installer and mark the PowerShell script as
a dev-only convenience. Folded into Phase 1 deliverables.

**Installer runbook** `docs/installer-test-runbook.md` — the manual verification runbook
(mirroring the existing `docs/v0.1-test-runbook.md`), named explicitly here so the path
is not ambiguous.

### Phase 2 — In-app "Start with Windows" toggle

**`Lifecycle/StartupRegistration.cs`** behind `IStartupRegistration`
(`IsEnabled()`, `Enable()`, `Disable()`), depending on a tiny seam
`IStartupRegistryStore` (`GetValue`/`SetValue`/`DeleteValue`). A registry-backed impl
targets `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value `clici` =
`"<Application.ExecutablePath>"` — the same value name and quoting the installer uses,
so the two never fight. The seam lets logic be unit-tested with a fake — **no real
HKCU writes in CI**.

**Tray integration.** `TrayApplicationContext` gains a checkbox menu item "Start with
Windows". Its checked state is initialized from `IsEnabled()` when the menu opens;
`CheckedChanged` calls `Enable()`/`Disable()`. Failures are caught → logged via
`Failure("startup-registration", null, <exceptionType>)` and the checkbox reverts to
its actual state.

### Phase 3 — Self-signed signing

**`tools/New-SelfSignedCodeSigningCert.ps1`** creates a self-signed code-signing
certificate, installs it to `CurrentUser\My` and to Trusted Root / Trusted Publishers
(so SmartScreen/UAC trust it locally), and exports a PFX to **`tools/.certs/`**. The
**same change adds a `.gitignore` rule** (`tools/.certs/` and `*.pfx`) so an exported
cert can never be committed — `.gitignore` has no such rule today.

**Signing step** in `Build-Installer.ps1` runs `signtool` on `clici.exe` (before
packaging) and `setup.exe` (after), driven by env vars / a cert reference
(`CLICI_SIGN_CERT_THUMBPRINT` or a PFX path + password). If unconfigured, signing is
**skipped with a warning** — a wired-but-optional hook, so dropping in a real cert
later needs no code change.

## Data flow

```
Build-Installer.ps1
  -> dotnet publish (profile)        -> clici.exe (self-contained, compressed)
  -> signtool (optional)             -> clici.exe (signed)
  -> ISCC clici.iss (/DAppVersion)   -> setup.exe
  -> signtool (optional)             -> setup.exe (signed)

User runs setup.exe
  -> per-user install to %LOCALAPPDATA%\Programs\clici
  -> Start Menu shortcut
  -> optional HKCU\...\Run\clici  (if startup task checked)

Running app
  -> tray "Start with Windows" toggle edits the same HKCU\...\Run\clici value

Uninstall (Add/Remove Programs)
  -> removes files, Start Menu shortcut, and the Run value
```

## Error handling

- Tray toggle failures are caught, logged, and revert the checkbox to actual state.
- Installer detects a running `clici.exe` via `AppMutex=Local\clici` and closes it
  gracefully before replacing/removing files (taskkill only as a spiked fallback).
- Signing is optional and guarded; absence produces a warning, not a failure.
- `Build-Installer.ps1` validates ISCC presence and publish/exe outputs, failing fast
  with actionable messages.

## Testing

**Automated (unit):**
- `StartupRegistration` via the fake `IStartupRegistryStore`:
  - `Enable()` writes value `clici` = the quoted executable path.
  - `Disable()` deletes the value.
  - `IsEnabled()` reflects presence/value.
  - Store exceptions surface so the tray layer can catch/log/revert.

**Manual (installer runbook `docs/installer-test-runbook.md`, mirroring `docs/v0.1-test-runbook.md`):**
- Run `setup.exe`; confirm install location, Start Menu shortcut, ARP entry, and Run
  key (when the startup task is checked).
- Launch; verify tray icon and that the "Start with Windows" checkbox matches the Run
  key; toggle it and confirm the key updates.
- Sign out / in; confirm auto-start when enabled and no auto-start when disabled.
- Uninstall from Add/Remove Programs; confirm files, shortcut, and Run value are gone
  and no `clici.exe` remains running.

## Deliverables by phase

1. **Phase 1:** extend root `Directory.Build.props` (`<Version>`), publish profiles,
   `installer/clici.iss` (with `AppMutex=Local\clici` graceful-close, spiked),
   `tools/Build-Installer.ps1`, `docs/installer-test-runbook.md`, and the README
   install-section update (lead with `setup.exe`, mark `Install-Clici.ps1` dev-only).
2. **Phase 2:** `IStartupRegistration` + `StartupRegistration` + `IStartupRegistryStore`
   (+ registry impl), tray menu wiring, unit tests.
3. **Phase 3:** `tools/New-SelfSignedCodeSigningCert.ps1` (exports PFX to `tools/.certs/`)
   **plus the `.gitignore` rule** for `tools/.certs/` / `*.pfx`, and optional signtool
   wiring in `Build-Installer.ps1`.

## Open risks

- Inno Setup (`ISCC.exe`) must be installed on the build machine; the build script
  gates on this.
- Self-signed signing removes SmartScreen only on machines that trust the cert; other
  machines still warn (accepted for personal use).
- If a user runs a non-installed (dev) build and toggles "Start with Windows", the Run
  value points at that dev exe. This is intended — the toggle always registers the
  currently-running executable.
