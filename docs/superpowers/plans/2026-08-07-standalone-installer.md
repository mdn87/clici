# clici Standalone Installer & App Lifecycle — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn clici's existing self-contained exe into a properly installed app via an Inno Setup per-user installer (Start Menu, Add/Remove Programs, uninstall, auto-start) with an in-app "Start with Windows" toggle and optional self-signed Authenticode signing.

**Architecture:** Three independently-testable phases. Phase 1 adds versioning, publish profiles, the Inno Setup script, and a build orchestrator. Phase 2 adds a testable `StartupRegistration` (behind a registry seam) wired into the tray menu. Phase 3 adds a self-signed cert script and an optional `signtool` step in the build.

**Tech Stack:** .NET 10 WinForms (net10.0-windows), Inno Setup 6 (`ISCC.exe`), PowerShell, `signtool.exe`, xUnit.

Spec: `docs/superpowers/specs/2026-08-07-standalone-installer-design.md`

---

## File Structure

**Phase 1 — packaging**
- Modify: `Directory.Build.props` — add `<Version>0.1.0</Version>` to the existing `PropertyGroup`.
- Create: `src/clici.App/Properties/PublishProfiles/win-x64.pubxml` — self-contained single-file publish settings.
- Create: `src/clici.App/Properties/PublishProfiles/win-arm64.pubxml` — arm64 variant.
- Create: `installer/clici.iss` — Inno Setup script.
- Create: `tools/Build-Installer.ps1` — publish → (sign) → ISCC → (sign) orchestrator.
- Create: `docs/installer-test-runbook.md` — manual verification runbook.
- Modify: `README.md` — install section leads with the installer; `Install-Clici.ps1` marked dev-only.

**Phase 2 — startup toggle**
- Create: `src/clici.App/Lifecycle/IStartupRegistryStore.cs` — registry seam.
- Create: `src/clici.App/Lifecycle/RegistryStartupRegistryStore.cs` — HKCU Run impl.
- Create: `src/clici.App/Lifecycle/IStartupRegistration.cs` — enable/disable/isEnabled contract.
- Create: `src/clici.App/Lifecycle/StartupRegistration.cs` — logic over the seam.
- Create: `tests/clici.App.Tests/StartupRegistrationTests.cs` — unit tests with a fake store.
- Modify: `src/clici.App/TrayApplicationContext.cs` — add "Start with Windows" menu item + handler.

**Phase 3 — signing**
- Create: `tools/New-SelfSignedCodeSigningCert.ps1` — generate + locally-trust a self-signed cert, export PFX.
- Modify: `.gitignore` — ignore `tools/.certs/` and `*.pfx`.
- Modify: `tools/Build-Installer.ps1` — signing already wired in Phase 1 as a guarded no-op; Phase 3 delivers the cert it uses. (No further code change unless the spike below requires it.)

---

## Phase 1 — Installer & packaging

### Task 1: Add the single-source version

**Files:**
- Modify: `Directory.Build.props`

- [ ] **Step 1: Add `<Version>` to the existing PropertyGroup**

Do NOT overwrite the file. It currently reads:

```xml
<Project>
  <PropertyGroup>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <Deterministic>true</Deterministic>
  </PropertyGroup>
</Project>
```

Change it to:

```xml
<Project>
  <PropertyGroup>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <Deterministic>true</Deterministic>
    <Version>0.1.0</Version>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: Verify the version flows into the assembly**

Run: `dotnet build src/clici.App/clici.App.csproj -c Release -v quiet`
Then: `powershell -NoProfile -Command "(Get-Item src/clici.App/bin/Release/net10.0-windows/clici.dll).VersionInfo.FileVersion"`
Expected: `0.1.0.0`

- [ ] **Step 3: Commit**

```bash
git add Directory.Build.props
git commit -m "build: add single-source version (0.1.0) to Directory.Build.props"
```

---

### Task 2: Add publish profiles

**Files:**
- Create: `src/clici.App/Properties/PublishProfiles/win-x64.pubxml`
- Create: `src/clici.App/Properties/PublishProfiles/win-arm64.pubxml`

- [ ] **Step 1: Create the win-x64 profile**

`src/clici.App/Properties/PublishProfiles/win-x64.pubxml`:

```xml
<Project>
  <PropertyGroup>
    <Configuration>Release</Configuration>
    <Platform>Any CPU</Platform>
    <PublishProtocol>FileSystem</PublishProtocol>
    <PublishDir>..\..\artifacts\publish\win-x64\</PublishDir>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <SelfContained>true</SelfContained>
    <PublishSingleFile>true</PublishSingleFile>
    <EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
    <PublishReadyToRun>false</PublishReadyToRun>
    <PublishTrimmed>false</PublishTrimmed>
    <DebugType>None</DebugType>
    <DebugSymbols>false</DebugSymbols>
  </PropertyGroup>
</Project>
```

Note: `PublishDir` is relative to the project file (`src/clici.App/`), resolving to `artifacts/publish/win-x64/` at the repo root.

- [ ] **Step 2: Create the win-arm64 profile**

`src/clici.App/Properties/PublishProfiles/win-arm64.pubxml` — identical except the RID and PublishDir:

```xml
<Project>
  <PropertyGroup>
    <Configuration>Release</Configuration>
    <Platform>Any CPU</Platform>
    <PublishProtocol>FileSystem</PublishProtocol>
    <PublishDir>..\..\artifacts\publish\win-arm64\</PublishDir>
    <RuntimeIdentifier>win-arm64</RuntimeIdentifier>
    <SelfContained>true</SelfContained>
    <PublishSingleFile>true</PublishSingleFile>
    <EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
    <PublishReadyToRun>false</PublishReadyToRun>
    <PublishTrimmed>false</PublishTrimmed>
    <DebugType>None</DebugType>
    <DebugSymbols>false</DebugSymbols>
  </PropertyGroup>
</Project>
```

- [ ] **Step 3: Verify plain `dotnet publish` produces the standalone exe**

Run: `dotnet publish src/clici.App/clici.App.csproj -p:PublishProfile=win-x64`
Expected: exit 0, and `artifacts/publish/win-x64/clici.exe` exists.
Verify: `powershell -NoProfile -Command "Test-Path artifacts/publish/win-x64/clici.exe"` → `True`

- [ ] **Step 4: Commit**

```bash
git add src/clici.App/Properties/PublishProfiles/win-x64.pubxml src/clici.App/Properties/PublishProfiles/win-arm64.pubxml
git commit -m "build: add self-contained single-file publish profiles"
```

---

### Task 3: Author the Inno Setup script

**Files:**
- Create: `installer/clici.iss`

Prerequisite: Inno Setup 6 must be installed (`ISCC.exe`). If missing, install from https://jrsoftware.org/isdl.php.

- [ ] **Step 1: Create `installer/clici.iss`**

```ini
; clici installer — per-user, no admin. Compile via tools/Build-Installer.ps1.
#define AppName "clici"
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceExe
  #define SourceExe "..\artifacts\publish\win-x64\clici.exe"
#endif
#ifndef Rid
  #define Rid "win-x64"
#endif

[Setup]
; Stable AppId — never change it, or upgrades/uninstall will not track.
AppId={{B7A6E4C2-1F3D-4E8A-9C5B-8D2E1A6F4B90}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=clici
DefaultDirName={localappdata}\Programs\clici
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\artifacts\installer
OutputBaseFilename=clici-{#AppVersion}-{#Rid}-setup
SetupIconFile=..\src\clici.App\Assets\clici.ico
UninstallDisplayIcon={app}\clici.exe
UninstallDisplayName={#AppName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; Detect a running instance through clici's own SingleInstanceGuard mutex and
; close it gracefully before install/uninstall (see spike in Task 4).
AppMutex=Local\clici
CloseApplications=yes
CloseApplicationsFilter=clici.exe

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\clici"; Filename: "{app}\clici.exe"; WorkingDir: "{app}"

[Tasks]
Name: "startup"; Description: "Start clici when I sign in"; GroupDescription: "Startup:"

[Registry]
; Value name and quoting MUST match StartupRegistration (value 'clici' = quoted exe path).
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "clici"; ValueData: """{app}\clici.exe"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\clici.exe"; Description: "Launch clici now"; Flags: nowait postinstall skipifsilent
```

- [ ] **Step 2: Commit (compilation is verified in Task 4 via the build script)**

```bash
git add installer/clici.iss
git commit -m "build: add Inno Setup per-user installer script"
```

---

### Task 4: Build orchestrator + AppMutex spike

**Files:**
- Create: `tools/Build-Installer.ps1`

- [ ] **Step 1: Create `tools/Build-Installer.ps1`**

```powershell
[CmdletBinding()]
param(
    [ValidateSet("win-x64", "win-arm64")]
    [string] $RuntimeIdentifier = "win-x64",

    # When set (or CLICI_SIGN_CERT_THUMBPRINT is present), clici.exe and setup.exe
    # are Authenticode-signed. When empty, signing is skipped with a warning.
    [string] $CertThumbprint = $env:CLICI_SIGN_CERT_THUMBPRINT
)

$ErrorActionPreference = "Stop"

$projectRoot   = Split-Path -Parent $PSScriptRoot
$projectFile   = Join-Path $projectRoot "src\clici.App\clici.App.csproj"
$publishDir    = Join-Path $projectRoot "artifacts\publish\$RuntimeIdentifier"
$publishedExe  = Join-Path $publishDir "clici.exe"
$issFile       = Join-Path $projectRoot "installer\clici.iss"
$propsFile     = Join-Path $projectRoot "Directory.Build.props"

# 1. Version from the single source of truth.
[xml] $props = Get-Content -LiteralPath $propsFile
$version = ($props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ }) | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "No <Version> found in $propsFile."
}

# 2. Publish the self-contained single-file exe.
Write-Host "Publishing clici $version for $RuntimeIdentifier..."
& dotnet publish $projectFile -p:PublishProfile=$RuntimeIdentifier --configuration Release
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }
if (-not (Test-Path -LiteralPath $publishedExe -PathType Leaf)) {
    throw "Published executable not found at '$publishedExe'."
}

# 3. Optional Authenticode signing (guarded — no-op without a thumbprint).
function Invoke-Sign([string] $path) {
    if ([string]::IsNullOrWhiteSpace($CertThumbprint)) {
        Write-Warning "No signing thumbprint set; skipping signature for $path."
        return
    }
    # Self-signed personal use: no public timestamp. Add /tr + /td when a real cert is used.
    & signtool.exe sign /sha1 $CertThumbprint /fd SHA256 $path
    if ($LASTEXITCODE -ne 0) { throw "signtool failed for '$path' ($LASTEXITCODE)." }
    Write-Host "Signed $path"
}
Invoke-Sign $publishedExe

# 4. Locate ISCC.exe (Inno Setup 6).
$isccCommand = Get-Command ISCC.exe -ErrorAction SilentlyContinue
if ($isccCommand) {
    $iscc = $isccCommand.Source
}
else {
    $iscc = Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"
    if (-not (Test-Path -LiteralPath $iscc)) {
        throw "ISCC.exe (Inno Setup 6) not found. Install from https://jrsoftware.org/isdl.php."
    }
}

# 5. Compile the installer.
Write-Host "Compiling installer with $iscc ..."
& $iscc "/DAppVersion=$version" "/DSourceExe=$publishedExe" "/DRid=$RuntimeIdentifier" $issFile
if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit code $LASTEXITCODE." }

$setupExe = Join-Path $projectRoot "artifacts\installer\clici-$version-$RuntimeIdentifier-setup.exe"
if (-not (Test-Path -LiteralPath $setupExe -PathType Leaf)) {
    throw "Installer was not produced at '$setupExe'."
}

# 6. Optionally sign the installer too.
Invoke-Sign $setupExe

Write-Host ""
Write-Host "Installer built: $setupExe"
```

- [ ] **Step 2: Run the build (produces the installer)**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File tools/Build-Installer.ps1`
Expected: publishes, warns "No signing thumbprint set" (twice), compiles, and prints `Installer built: ...\artifacts\installer\clici-0.1.0-win-x64-setup.exe`.
If it throws "ISCC.exe … not found", install Inno Setup 6 and re-run.

- [ ] **Step 3: AppMutex spike — verify graceful close of the hidden-window tray app**

This is a decision spike, not a guess. Do this manually:

1. Install once: run the produced `setup.exe` and let clici launch (it runs as a hidden-window tray app holding mutex `Local\clici`).
2. Re-run `setup.exe` (simulating an upgrade) while clici is running.
3. Observe whether Inno's `AppMutex` + `CloseApplications` detects and closes the running instance without a "file in use" error and without a forced kill.

- If graceful close works (no reboot prompt, no locked-file error, clici exits cleanly): **keep the script as-is.** Record the result in `docs/installer-test-runbook.md` (Task 6).
- If it does NOT close a hidden-window tray app (Inno cannot close a window that has none): add a `[Code]` fallback to `installer/clici.iss`:

```pascal
[Code]
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  Exec('taskkill.exe', '/IM clici.exe /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := True;
end;
```

Note the trade-off in the runbook: `taskkill /F` can interrupt an in-flight `_configurationStore.TrySave`, though that write is atomic (temp file + move) and self-guarded, so the risk is a lost *pending* config edit, not a corrupt file. Prefer AppMutex if it works.

- [ ] **Step 4: Commit**

```bash
git add tools/Build-Installer.ps1
# If the spike required the [Code] fallback, also: git add installer/clici.iss
git commit -m "build: add installer build orchestrator; resolve stop-before-install via AppMutex spike"
```

---

### Task 5: README install-section update

**Files:**
- Modify: `README.md` (section "## Install for the current Windows user", lines ~189-201)

- [ ] **Step 1: Replace the install section body**

Replace exactly this block:

```markdown
## Install for the current Windows user

Run the per-user installer from the repository root:

```powershell
.\tools\Install-Clici.ps1
```

The installer publishes a self-contained copy to
`%LOCALAPPDATA%\Programs\clici`, creates a **clici** shortcut on the desktop,
and launches the tray application. The installed copy does not require the
.NET SDK or runtime. Double-click the desktop shortcut to start clici again
after exiting it or restarting Windows.
```

with:

```markdown
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
```

- [ ] **Step 2: Commit**

```bash
git add README.md
git commit -m "docs: lead install instructions with setup.exe; mark script dev-only"
```

---

### Task 6: Installer test runbook

**Files:**
- Create: `docs/installer-test-runbook.md`

- [ ] **Step 1: Create `docs/installer-test-runbook.md`**

```markdown
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

## Upgrade / stop-before-install (AppMutex spike result)
12. With clici running, re-run `setup.exe`.
13. Record here whether AppMutex + CloseApplications closed the running instance
    gracefully (no locked-file error), or whether the `[Code]` taskkill fallback
    was required: __________________________________________________

## Uninstall
14. Uninstall via Add/Remove Programs.
15. Confirm `%LOCALAPPDATA%\Programs\clici` is removed, the Start Menu shortcut is gone,
    the `Run\clici` value is gone, and no `clici.exe` process remains running.
```

- [ ] **Step 2: Commit**

```bash
git add docs/installer-test-runbook.md
git commit -m "docs: add installer test runbook"
```

---

## Phase 2 — In-app "Start with Windows" toggle

### Task 7: Registry seam interface

**Files:**
- Create: `src/clici.App/Lifecycle/IStartupRegistryStore.cs`

- [ ] **Step 1: Create the interface**

```csharp
namespace Clici.App.Lifecycle;

/// <summary>
/// Minimal seam over the per-user Run registry key so startup logic is testable
/// without touching the real HKCU hive.
/// </summary>
internal interface IStartupRegistryStore
{
    string? GetValue(string name);

    void SetValue(string name, string value);

    void DeleteValue(string name);
}
```

- [ ] **Step 2: Commit**

```bash
git add src/clici.App/Lifecycle/IStartupRegistryStore.cs
git commit -m "feat: add startup registry store seam"
```

---

### Task 8: StartupRegistration logic (TDD)

**Files:**
- Create: `src/clici.App/Lifecycle/IStartupRegistration.cs`
- Create: `src/clici.App/Lifecycle/StartupRegistration.cs`
- Test: `tests/clici.App.Tests/StartupRegistrationTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/clici.App.Tests/StartupRegistrationTests.cs`:

```csharp
using Clici.App.Lifecycle;

namespace Clici.App.Tests;

public sealed class StartupRegistrationTests
{
    private const string ExePath = @"C:\Users\me\AppData\Local\Programs\clici\clici.exe";
    private static readonly string QuotedExePath = $"\"{ExePath}\"";

    [Fact]
    public void EnableWritesTheQuotedExecutablePathUnderTheCliciValue()
    {
        var store = new FakeStore();
        var registration = new StartupRegistration(store, ExePath);

        registration.Enable();

        Assert.Equal(QuotedExePath, store.Values["clici"]);
    }

    [Fact]
    public void DisableRemovesTheCliciValue()
    {
        var store = new FakeStore();
        store.Values["clici"] = QuotedExePath;
        var registration = new StartupRegistration(store, ExePath);

        registration.Disable();

        Assert.False(store.Values.ContainsKey("clici"));
    }

    [Fact]
    public void IsEnabledIsTrueWhenTheValueMatchesThisExecutable()
    {
        var store = new FakeStore();
        store.Values["clici"] = QuotedExePath;
        var registration = new StartupRegistration(store, ExePath);

        Assert.True(registration.IsEnabled());
    }

    [Fact]
    public void IsEnabledIsFalseWhenNoValueIsPresent()
    {
        var registration = new StartupRegistration(new FakeStore(), ExePath);

        Assert.False(registration.IsEnabled());
    }

    [Fact]
    public void IsEnabledIsFalseWhenTheValuePointsAtADifferentExecutable()
    {
        var store = new FakeStore();
        store.Values["clici"] = "\"C:\\somewhere\\else\\clici.exe\"";
        var registration = new StartupRegistration(store, ExePath);

        Assert.False(registration.IsEnabled());
    }

    [Fact]
    public void StoreExceptionsPropagateSoTheCallerCanHandleThem()
    {
        var registration = new StartupRegistration(new ThrowingStore(), ExePath);

        Assert.Throws<InvalidOperationException>(() => registration.Enable());
    }

    private sealed class FakeStore : IStartupRegistryStore
    {
        public Dictionary<string, string> Values { get; } = [];

        public string? GetValue(string name) =>
            Values.TryGetValue(name, out var value) ? value : null;

        public void SetValue(string name, string value) => Values[name] = value;

        public void DeleteValue(string name) => Values.Remove(name);
    }

    private sealed class ThrowingStore : IStartupRegistryStore
    {
        public string? GetValue(string name) => throw new InvalidOperationException();

        public void SetValue(string name, string value) => throw new InvalidOperationException();

        public void DeleteValue(string name) => throw new InvalidOperationException();
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/clici.App.Tests/clici.App.Tests.csproj --filter "FullyQualifiedName~StartupRegistrationTests"`
Expected: FAIL — `StartupRegistration` and `IStartupRegistration` do not exist (compile error).

- [ ] **Step 3: Create the interface**

`src/clici.App/Lifecycle/IStartupRegistration.cs`:

```csharp
namespace Clici.App.Lifecycle;

/// <summary>
/// Controls whether clici launches at user sign-in, via the per-user Run key.
/// </summary>
internal interface IStartupRegistration
{
    bool IsEnabled();

    void Enable();

    void Disable();
}
```

- [ ] **Step 4: Implement `StartupRegistration`**

`src/clici.App/Lifecycle/StartupRegistration.cs`:

```csharp
namespace Clici.App.Lifecycle;

internal sealed class StartupRegistration : IStartupRegistration
{
    private const string ValueName = "clici";
    private readonly IStartupRegistryStore _store;
    private readonly string _quotedExecutablePath;

    public StartupRegistration(IStartupRegistryStore store, string executablePath)
    {
        _store = store;
        _quotedExecutablePath = $"\"{executablePath}\"";
    }

    public bool IsEnabled() =>
        string.Equals(
            _store.GetValue(ValueName),
            _quotedExecutablePath,
            StringComparison.OrdinalIgnoreCase);

    public void Enable() => _store.SetValue(ValueName, _quotedExecutablePath);

    public void Disable() => _store.DeleteValue(ValueName);
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/clici.App.Tests/clici.App.Tests.csproj --filter "FullyQualifiedName~StartupRegistrationTests"`
Expected: PASS — 6 passed.

- [ ] **Step 6: Commit**

```bash
git add src/clici.App/Lifecycle/IStartupRegistration.cs src/clici.App/Lifecycle/StartupRegistration.cs tests/clici.App.Tests/StartupRegistrationTests.cs
git commit -m "feat: add testable StartupRegistration over the Run key"
```

---

### Task 9: Registry-backed store implementation

**Files:**
- Create: `src/clici.App/Lifecycle/RegistryStartupRegistryStore.cs`

Note: `Microsoft.Win32.Registry` is available to this WinForms (`net10.0-windows`) target via the Windows Desktop framework — no package reference is expected. If the build fails with `CS0234` on `Microsoft.Win32`, add `<PackageReference Include="Microsoft.Win32.Registry" />` to `src/clici.App/clici.App.csproj` and rebuild.

- [ ] **Step 1: Implement the registry store**

`src/clici.App/Lifecycle/RegistryStartupRegistryStore.cs`:

```csharp
using Microsoft.Win32;

namespace Clici.App.Lifecycle;

internal sealed class RegistryStartupRegistryStore : IStartupRegistryStore
{
    private const string RunKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Run";

    public string? GetValue(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(name) as string;
    }

    public void SetValue(string name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        key.SetValue(name, value, RegistryValueKind.String);
    }

    public void DeleteValue(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}
```

- [ ] **Step 2: Verify the project still builds**

Run: `dotnet build src/clici.App/clici.App.csproj -c Release -v quiet`
Expected: Build succeeded, 0 errors. (If `CS0234`, apply the package-reference note above and rebuild.)

- [ ] **Step 3: Commit**

```bash
git add src/clici.App/Lifecycle/RegistryStartupRegistryStore.cs
git commit -m "feat: add HKCU Run registry store for startup registration"
```

---

### Task 10: Wire the tray menu item

**Files:**
- Modify: `src/clici.App/TrayApplicationContext.cs`

- [ ] **Step 1: Add the two fields**

In `src/clici.App/TrayApplicationContext.cs`, find the existing field block:

```csharp
    private readonly ToolStripMenuItem _enabledMenuItem;
    private readonly ToolStripMenuItem _pauseMenuItem;
```

Add below them:

```csharp
    private readonly ToolStripMenuItem _startWithWindowsMenuItem;
    private readonly IStartupRegistration _startupRegistration;
```

Ensure the file's using directives include `using Clici.App.Lifecycle;` (add it if absent).

- [ ] **Step 2: Construct the registration and menu item**

Find this block in the constructor:

```csharp
        _pauseMenuItem = new ToolStripMenuItem("Pause normalization")
        {
            CheckOnClick = true
        };
        _pauseMenuItem.CheckedChanged += PauseMenuItemOnCheckedChanged;
```

Insert immediately after it:

```csharp
        _startupRegistration = new StartupRegistration(
            new RegistryStartupRegistryStore(),
            Application.ExecutablePath);

        _startWithWindowsMenuItem = new ToolStripMenuItem("Start with Windows")
        {
            CheckOnClick = true
        };
        RefreshStartWithWindowsChecked();
        _startWithWindowsMenuItem.CheckedChanged += StartWithWindowsMenuItemOnCheckedChanged;
```

- [ ] **Step 3: Add the menu item to the tray menu**

Find:

```csharp
        _trayMenu = new ContextMenuStrip();
        _trayMenu.Items.Add(_enabledMenuItem);
        _trayMenu.Items.Add(_pauseMenuItem);
        _trayMenu.Items.Add(new ToolStripSeparator());
```

Change the middle to insert the new item after pause:

```csharp
        _trayMenu = new ContextMenuStrip();
        _trayMenu.Items.Add(_enabledMenuItem);
        _trayMenu.Items.Add(_pauseMenuItem);
        _trayMenu.Items.Add(_startWithWindowsMenuItem);
        _trayMenu.Items.Add(new ToolStripSeparator());
```

- [ ] **Step 4: Add the handler and refresh helper**

Find the existing handler:

```csharp
    private void PauseMenuItemOnCheckedChanged(object? sender, EventArgs eventArgs)
    {
        _coordinator.SetPaused(_pauseMenuItem.Checked);
        _pauseMenuItem.Text = _pauseMenuItem.Checked
            ? "Resume normalization"
            : "Pause normalization";
    }
```

Insert immediately after it:

```csharp
    private void StartWithWindowsMenuItemOnCheckedChanged(
        object? sender,
        EventArgs eventArgs)
    {
        try
        {
            if (_startWithWindowsMenuItem.Checked)
            {
                _startupRegistration.Enable();
            }
            else
            {
                _startupRegistration.Disable();
            }
        }
        catch (Exception exception)
        {
            _logger.Failure("startup-registration", null, exception.GetType().Name);
            RefreshStartWithWindowsChecked();
        }
    }

    private void RefreshStartWithWindowsChecked()
    {
        bool enabled;
        try
        {
            enabled = _startupRegistration.IsEnabled();
        }
        catch (Exception exception)
        {
            _logger.Failure("startup-registration", null, exception.GetType().Name);
            return;
        }

        // Detach while correcting the checkbox so we do not re-enter the handler.
        _startWithWindowsMenuItem.CheckedChanged -= StartWithWindowsMenuItemOnCheckedChanged;
        _startWithWindowsMenuItem.Checked = enabled;
        _startWithWindowsMenuItem.CheckedChanged += StartWithWindowsMenuItemOnCheckedChanged;
    }
```

Note: in Step 2, `RefreshStartWithWindowsChecked()` is called *before* the handler is subscribed, so its detach/attach is harmless there (nothing is subscribed yet); it becomes meaningful on the error-revert path.

- [ ] **Step 5: Build and run the full suite**

Run: `dotnet build src/clici.App/clici.App.csproj -c Release -v quiet`
Expected: Build succeeded, 0 errors.
Run: `dotnet test -v quiet`
Expected: all tests pass (the 13 existing App tests + 6 new `StartupRegistration` tests + 37 Core tests).

- [ ] **Step 6: Manual smoke — the toggle edits the Run key**

Run the app: `powershell -NoProfile -Command "Start-Process src/clici.App/bin/Release/net10.0-windows/clici.exe"`
1. Open the tray menu → click **Start with Windows** (check it).
2. Verify: `powershell -NoProfile -Command "Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name clici"` shows the quoted exe path.
3. Uncheck it; verify the value is gone:
   `powershell -NoProfile -Command "(Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name clici -ErrorAction SilentlyContinue) -eq $null"` → `True`
4. Exit clici from the tray.

- [ ] **Step 7: Commit**

```bash
git add src/clici.App/TrayApplicationContext.cs
git commit -m "feat: add Start with Windows tray toggle backed by StartupRegistration"
```

---

## Phase 3 — Self-signed signing

### Task 11: gitignore the cert directory (do this first, before any cert exists)

**Files:**
- Modify: `.gitignore`

- [ ] **Step 1: Append the ignore rules**

Add to the end of `.gitignore`:

```gitignore

## Local code-signing certificates (never commit)
tools/.certs/
*.pfx
```

- [ ] **Step 2: Commit**

```bash
git add .gitignore
git commit -m "chore: ignore local code-signing certs (tools/.certs, *.pfx)"
```

---

### Task 12: Self-signed cert generation script

**Files:**
- Create: `tools/New-SelfSignedCodeSigningCert.ps1`

- [ ] **Step 1: Create the script**

`tools/New-SelfSignedCodeSigningCert.ps1`:

```powershell
[CmdletBinding()]
param(
    [string] $Subject = "CN=clici self-signed",

    [string] $PfxPath = (Join-Path $PSScriptRoot ".certs\clici-selfsigned.pfx"),

    [securestring] $Password = (Read-Host -AsSecureString -Prompt "PFX export password")
)

$ErrorActionPreference = "Stop"

# 1. Create a code-signing cert in the current user's personal store.
$cert = New-SelfSignedCertificate `
    -Type CodeSigningCert `
    -Subject $Subject `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -KeyExportPolicy Exportable `
    -KeyUsage DigitalSignature `
    -NotAfter (Get-Date).AddYears(5)

# 2. Trust it locally so SmartScreen/UAC accept signatures on THIS machine.
foreach ($storeName in @("Root", "TrustedPublisher")) {
    $store = New-Object System.Security.Cryptography.X509Certificates.X509Store(
        $storeName, "CurrentUser")
    $store.Open("ReadWrite")
    try { $store.Add($cert) } finally { $store.Close() }
}

# 3. Export the PFX to the gitignored certs directory.
$certDir = Split-Path -Parent $PfxPath
New-Item -ItemType Directory -Force -Path $certDir | Out-Null
Export-PfxCertificate -Cert $cert -FilePath $PfxPath -Password $Password | Out-Null

Write-Host ""
Write-Host "Self-signed code-signing certificate created and locally trusted."
Write-Host "Thumbprint : $($cert.Thumbprint)"
Write-Host "PFX        : $PfxPath"
Write-Host ""
Write-Host "To sign during a build, set the thumbprint and re-run the installer build:"
Write-Host "  `$env:CLICI_SIGN_CERT_THUMBPRINT = '$($cert.Thumbprint)'"
Write-Host "  .\tools\Build-Installer.ps1"
```

- [ ] **Step 2: Generate the cert and verify signing works end-to-end**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File tools/New-SelfSignedCodeSigningCert.ps1`
(Enter a password when prompted.)
Expected: prints a thumbprint and PFX path; `tools/.certs/clici-selfsigned.pfx` exists.

Then build with signing on:

```powershell
$env:CLICI_SIGN_CERT_THUMBPRINT = '<thumbprint from above>'
.\tools\Build-Installer.ps1
```

Expected: no "skipping signature" warnings; prints `Signed ...clici.exe` and `Signed ...setup.exe`.
Verify the signature: `powershell -NoProfile -Command "(Get-AuthenticodeSignature artifacts/installer/clici-0.1.0-win-x64-setup.exe).Status"` → `Valid`.

- [ ] **Step 3: Confirm the PFX is NOT staged by git**

Run: `git status --porcelain`
Expected: `tools/.certs/` and any `*.pfx` do NOT appear (ignored). Only `tools/New-SelfSignedCodeSigningCert.ps1` is new.

- [ ] **Step 4: Commit**

```bash
git add tools/New-SelfSignedCodeSigningCert.ps1
git commit -m "build: add self-signed code-signing cert generator"
```

---

## Final verification

- [ ] **Run the full automated suite**

Run: `dotnet test -v quiet`
Expected: all green (Core + App, including the 6 new `StartupRegistration` tests).

- [ ] **Walk the installer runbook**

Complete `docs/installer-test-runbook.md` end-to-end against a freshly built (and, if the cert exists, signed) `setup.exe`, including the AppMutex spike result in step 13.

- [ ] **Push**

```bash
git push
```

---

## Notes for the implementer

- **Tooling prerequisite:** Inno Setup 6 (`ISCC.exe`) must be installed for Tasks 4, 6, and 12. If absent, `Build-Installer.ps1` fails fast with the download URL.
- **Value-name/quoting contract:** the installer (`clici.iss` `[Registry]`) and the app (`StartupRegistration`) both write `HKCU\...\Run` value **`clici`** = the **quoted** exe path. Keep them identical or the tray checkbox and installer will disagree.
- **Self-signed scope:** signing removes SmartScreen only on machines that trust the generated cert. Other machines still warn — accepted for personal use per the spec.
- **`Install-Clici.ps1` stays** as a developer convenience (kept per review); the `setup.exe` path is the supported one.
```
