param(
    [string]$BundleDirectory,
    [switch]$Disable
)

# Dot-source in the PowerShell session where normal `codex` will be launched.
# No profile, registry, installed binary, or persistent environment is changed.
if ($MyInvocation.InvocationName -ne '.') {
    throw 'Dot-source this script: . ./Enable-RendererNavigation.ps1 -BundleDirectory <directory>'
}
if ($Disable) {
    if (Get-Variable CliciRendererPreviousEnvironment -Scope Global -ErrorAction SilentlyContinue) {
        foreach ($entry in $global:CliciRendererPreviousEnvironment.GetEnumerator()) {
            [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, 'Process')
        }
        Remove-Variable CliciRendererPreviousEnvironment -Scope Global
    }
    Write-Output 'Renderer navigation disabled for future launches in this shell. Close an existing opted-in Codex session to stop its helper.'
    return
}
if (-not $IsWindows -or -not $env:WT_SESSION) {
    throw 'Use a local, non-elevated PowerShell 7 session in Windows Terminal.'
}
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Elevated terminals are not supported.'
}
if (Get-Variable CliciRendererPreviousEnvironment -Scope Global -ErrorAction SilentlyContinue) {
    throw 'Already enabled in this shell. Disable before selecting another bundle.'
}
$bundleRoot = (Resolve-Path -LiteralPath $BundleDirectory -ErrorAction Stop).Path
$manifestPath = Join-Path $bundleRoot 'renderer-bundle.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw -ErrorAction Stop | ConvertFrom-Json
if ($manifest.upstreamCommit -ne '092d3acd6bec3e3a14bdc7e7a2810ab628ab759d') {
    throw 'This activation script supports only the pinned Codex 0.162.1 renderer.'
}
foreach ($file in $manifest.files) {
    $path = [IO.Path]::GetFullPath((Join-Path $bundleRoot $file.path))
    if (-not $path.StartsWith($bundleRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
        (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.sha256) {
        throw 'Renderer bundle integrity check failed. No environment changed.'
    }
}
$native = Join-Path $bundleRoot 'bin/codex.exe'
$helper = Join-Path $bundleRoot 'helper/PromptNavigation.Probe.exe'
if (-not (Test-Path -LiteralPath $native) -or -not (Test-Path -LiteralPath $helper)) {
    throw 'Bundle must contain the full Codex CLI and renderer helper.'
}
$resolved = Get-Command codex -ErrorAction SilentlyContinue
if ($resolved -and $resolved.CommandType -in @('Alias', 'Function')) {
    throw 'A codex alias/function would override PATH. Keep it unchanged and invoke the bundled bin/codex.exe explicitly.'
}
$global:CliciRendererPreviousEnvironment = @{}
foreach ($name in @('PATH', 'CLICI_RENDERER_BRIDGE', 'CLICI_RENDERER_KEYS', 'CLICI_RENDERER_TERMINALS', 'CLICI_NATIVE_KEY_PROBE')) {
    $global:CliciRendererPreviousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
$env:PATH = (Join-Path $bundleRoot 'bin') + [IO.Path]::PathSeparator + $env:PATH
$env:CLICI_RENDERER_BRIDGE = $helper
$env:CLICI_RENDERER_KEYS = '1'
$env:CLICI_RENDERER_TERMINALS = '1'
$env:CLICI_NATIVE_KEY_PROBE = $null
Write-Output 'Enabled for new Codex sessions in this shell: run codex --no-alt-screen. Ctrl+Up/Down navigate verified user turns; split panes and unknown contexts pass through.'
