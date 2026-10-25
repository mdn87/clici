param(
    [string]$Root = (Join-Path $env:LOCALAPPDATA 'Clici/renderer-navigation'),
    [switch]$Apply
)

$ErrorActionPreference = 'Stop'
$rootPath = (Resolve-Path -LiteralPath $Root).Path
$statePath = Join-Path $rootPath 'installation.json'
$state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
$releaseRoot = [IO.Path]::GetFullPath((Join-Path $rootPath 'releases')) + [IO.Path]::DirectorySeparatorChar
$helpers = @(Get-ChildItem -LiteralPath (Join-Path $rootPath 'releases') -Filter PromptNavigation.Probe.exe -Recurse -File)
$running = @(Get-CimInstance Win32_Process -Filter "Name = 'PromptNavigation.Probe.exe'" | Where-Object {
    $_.ExecutablePath -and $_.ExecutablePath.StartsWith($releaseRoot, [StringComparison]::OrdinalIgnoreCase)
})
$parents = @($running | ForEach-Object {
    $owner = Get-Process -Id $_.ParentProcessId -ErrorAction Stop
    [pscustomobject]@{ id = $owner.Id; started = $owner.StartTime.ToUniversalTime().ToString('o') }
})
$plan = [ordered]@{
    apply = [bool]$Apply
    enabled = $state.enabled
    profiles = @($state.profiles)
    config = $state.config
    helpersToArchive = @($helpers | ForEach-Object { $_.FullName })
    helpersToStop = @($running | ForEach-Object { $_.ProcessId })
    parentsToPreserve = $parents
}
if (-not $Apply) { $plan | ConvertTo-Json -Depth 5; return }
foreach ($helper in $helpers) {
    if (Test-Path -LiteralPath ($helper.FullName + '.retired')) {
        throw "Archive already exists: $($helper.FullName).retired"
    }
}

# Keep the current settings as well as the installer's original backups.
$backup = Join-Path $rootPath ('backups/retirement-' + (Get-Date -Format 'yyyyMMdd-HHmmss-ffff'))
New-Item -ItemType Directory -Path $backup | Out-Null
$files = @($statePath) + @($state.profiles) + @($state.config)
for ($index = 0; $index -lt $files.Count; $index++) {
    Copy-Item -LiteralPath $files[$index] -Destination (Join-Path $backup "$index")
}
$plan | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $backup 'plan.json')
& python (Join-Path $PSScriptRoot 'codex-renderer/install_renderer.py') --root $rootPath --disable
if ($LASTEXITCODE -ne 0) { throw 'Profile disable failed; helpers have not been changed.' }

# Supervisors can restart killed helpers. Archive their launch targets first.
# Rename preserves the binaries and prevents stale shells from starting hooks.
foreach ($helper in $helpers) {
    $archive = $helper.FullName + '.retired'
    Move-Item -LiteralPath $helper.FullName -Destination $archive
}
foreach ($helper in $running) {
    $current = Get-CimInstance Win32_Process -Filter "ProcessId = $($helper.ProcessId)"
    if ($current -and $current.ExecutablePath -eq $helper.ExecutablePath -and $current.CreationDate -eq $helper.CreationDate) {
        Stop-Process -Id $helper.ProcessId -ErrorAction Stop
    }
}
foreach ($owner in $parents) {
    $current = Get-Process -Id $owner.id -ErrorAction Stop
    if ($current.StartTime.ToUniversalTime().ToString('o') -ne $owner.started) { throw 'A parent process changed.' }
}
$remaining = @(Get-CimInstance Win32_Process -Filter "Name = 'PromptNavigation.Probe.exe'" | Where-Object {
    $_.ExecutablePath -and $_.ExecutablePath.StartsWith($releaseRoot, [StringComparison]::OrdinalIgnoreCase)
})
if ($remaining.Count) { throw 'A navigation helper is still running.' }
[pscustomobject]@{ retired = $true; backup = $backup; parentsPreserved = $parents.Count; helpersRemaining = $remaining.Count } | ConvertTo-Json
