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
        # Stop only renderer children of the bundled native CLI launched beneath
        # this shell. The CLI and its draft/session remain alive.
        $processes = @(Get-CimInstance Win32_Process -Property ProcessId,ParentProcessId,ExecutablePath)
        $byId = @{}
        foreach ($process in $processes) { $byId[[uint32]$process.ProcessId] = $process }
        foreach ($process in $processes) {
            if ($process.ExecutablePath -ne $global:CliciRendererHelperPath) { continue }
            $ancestor = $byId[[uint32]$process.ParentProcessId]
            if (-not $ancestor -or $ancestor.ExecutablePath -ne $global:CliciRendererNativePath) { continue }
            $seen = @{}
            while ($ancestor -and -not $seen.ContainsKey([uint32]$ancestor.ProcessId)) {
                if ($ancestor.ProcessId -eq $PID) {
                    Stop-Process -Id $process.ProcessId -ErrorAction SilentlyContinue
                    break
                }
                $seen[[uint32]$ancestor.ProcessId] = $true
                $ancestor = $byId[[uint32]$ancestor.ParentProcessId]
            }
        }
        foreach ($entry in $global:CliciRendererPreviousEnvironment.GetEnumerator()) {
            $restoreValue = if ($null -eq $entry.Value) { [System.Management.Automation.Language.NullString]::Value } else { $entry.Value }
            [Environment]::SetEnvironmentVariable($entry.Key, $restoreValue, 'Process')
        }
        Remove-Variable CliciRendererPreviousEnvironment -Scope Global
        Remove-Variable CliciRendererHelperPath,CliciRendererNativePath -Scope Global
    }
    Write-Output 'Renderer navigation disabled in this shell and its running Codex children. Native sessions remain open.'
    return
}
if (-not $IsWindows -or -not $env:WT_SESSION) {
    throw 'Use a local, non-elevated PowerShell 7 session in Windows Terminal.'
}
if (Test-Path Env:NO_COLOR) {
    throw 'NO_COLOR disables renderer anchors. Use a color-enabled Terminal session; this script preserves your color preference.'
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
$global:CliciRendererHelperPath = $helper
$global:CliciRendererNativePath = $native
foreach ($name in @('PATH', 'CLICI_RENDERER_BRIDGE', 'CLICI_RENDERER_KEYS', 'CLICI_RENDERER_TERMINALS', 'CLICI_NATIVE_KEY_PROBE')) {
    $global:CliciRendererPreviousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
$env:PATH = (Join-Path $bundleRoot 'bin') + [IO.Path]::PathSeparator + $env:PATH
$env:CLICI_RENDERER_BRIDGE = $helper
$env:CLICI_RENDERER_KEYS = '1'
$env:CLICI_RENDERER_TERMINALS = '1'
$env:CLICI_NATIVE_KEY_PROBE = $null
Write-Output 'Enabled for new Codex sessions in this shell: run codex --no-alt-screen. Alt+Shift+Comma / Alt+Shift+Period navigate verified user turns; split panes and unknown contexts pass through.'
