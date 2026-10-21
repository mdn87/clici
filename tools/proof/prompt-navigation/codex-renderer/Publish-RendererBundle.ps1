param(
    [Parameter(Mandatory)][string]$NativeExecutable,
    [Parameter(Mandatory)][string]$HelperDirectory,
    [Parameter(Mandatory)][string]$SourceDirectory,
    [Parameter(Mandatory)][string]$Destination
)
$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $Destination) { throw 'Choose a new bundle directory; existing bundles are preserved.' }
$native = (Resolve-Path -LiteralPath $NativeExecutable).Path
$helperRoot = (Resolve-Path -LiteralPath $HelperDirectory).Path
$sourceRoot = (Resolve-Path -LiteralPath $SourceDirectory).Path
$upstream = git -C $sourceRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $upstream -ne '092d3acd6bec3e3a14bdc7e7a2810ab628ab759d') { throw 'Unexpected native source version.' }
if ((& $native --version) -ne 'codex-cli 0.162.1') { throw 'Expected the full Codex 0.162.1 CLI executable.' }
# Code Mode (and so `codex exec`) needs the host beside codex.exe; build it from the same checkout.
$codeModeHost = Join-Path (Split-Path -Parent $native) 'codex-code-mode-host.exe'
if (-not (Test-Path -LiteralPath $codeModeHost)) { throw 'Missing codex-code-mode-host.exe beside the native executable.' }
foreach ($name in @('PromptNavigation.Probe.exe', 'PromptNavigation.Probe.dll', 'PromptNavigation.Probe.runtimeconfig.json', 'clici.Core.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $helperRoot $name))) { throw "Missing helper artifact: $name" }
}
$bundleRoot = [IO.Path]::GetFullPath($Destination)
foreach ($directory in @('bin','helper','source')) { New-Item -ItemType Directory -Path (Join-Path $bundleRoot $directory) -Force | Out-Null }
Copy-Item -LiteralPath $native -Destination (Join-Path $bundleRoot 'bin/codex.exe')
Copy-Item -LiteralPath $codeModeHost -Destination (Join-Path $bundleRoot 'bin/codex-code-mode-host.exe')
Get-ChildItem -LiteralPath $helperRoot -File | Where-Object Extension -ne '.pdb' | Copy-Item -Destination (Join-Path $bundleRoot 'helper')
foreach ($name in @('LICENSE','NOTICE')) {
    if (Test-Path -LiteralPath (Join-Path $sourceRoot $name)) { Copy-Item -LiteralPath (Join-Path $sourceRoot $name) -Destination (Join-Path $bundleRoot $name) }
}
foreach ($name in @('codex-0.162.1.patch','clici_bridge.rs','protocol.rs','clici_bridge_admission.rs','README.md')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $bundleRoot "source/$name")
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Enable-RendererNavigation.ps1') -Destination $bundleRoot
$plugin = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../claude-renderer')).Path
foreach ($name in @('.claude-plugin/plugin.json', 'hooks/hooks.json', 'hooks/register.ts', 'hooks/anchors.ts')) {
    $target = Join-Path $bundleRoot "claude-plugin/$name"
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $plugin $name) -Destination $target
}
$files = @(Get-ChildItem -LiteralPath $bundleRoot -Recurse -File | ForEach-Object {
    @{ path=[IO.Path]::GetRelativePath($bundleRoot,$_.FullName).Replace('\','/'); sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
})
@{ upstreamCommit=$upstream; version='0.162.1'; mode='experimental-single-pane'; files=$files } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $bundleRoot 'renderer-bundle.json') -Encoding utf8
Write-Output "Created $bundleRoot. Nothing installed or enabled."
