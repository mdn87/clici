param([Parameter(Mandatory)][string]$SourceDirectory)

$ErrorActionPreference = 'Stop'
$sourceRoot = (Resolve-Path -LiteralPath $SourceDirectory).Path
$expected = '092d3acd6bec3e3a14bdc7e7a2810ab628ab759d'
$actualRoot = git -C $sourceRoot rev-parse --show-toplevel
if ($LASTEXITCODE -ne 0 -or [IO.Path]::GetFullPath($actualRoot) -ne $sourceRoot) {
    throw 'SourceDirectory must be the root of a separate Codex source checkout.'
}
if ((git -C $sourceRoot rev-parse HEAD) -ne $expected) {
    throw "Expected Codex rust-v0.162.1 commit $expected. No files changed."
}
if (git -C $sourceRoot status --porcelain --untracked-files=all) {
    throw 'The Codex checkout must be clean, including untracked files. No files changed.'
}
$destination = Join-Path $sourceRoot 'codex-rs/tui/src'
foreach ($name in @('clici_bridge.rs', 'clici_bridge_protocol.rs', 'clici_bridge_admission.rs')) {
    if (Test-Path -LiteralPath (Join-Path $destination $name)) {
        throw "Refusing to overwrite $name. No files changed."
    }
}
$patch = Join-Path $PSScriptRoot 'codex-0.162.1.patch'
git -C $sourceRoot apply --check --whitespace=error-all $patch
if ($LASTEXITCODE -ne 0) { throw 'Patch preflight failed. No files changed.' }
git -C $sourceRoot apply --whitespace=error-all $patch
if ($LASTEXITCODE -ne 0) { throw 'Patch application failed; inspect the separate checkout.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'clici_bridge.rs') -Destination (Join-Path $destination 'clici_bridge.rs')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'protocol.rs') -Destination (Join-Path $destination 'clici_bridge_protocol.rs')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'clici_bridge_admission.rs') -Destination (Join-Path $destination 'clici_bridge_admission.rs')
Write-Output 'Applied the development renderer bridge to the separate Codex source checkout.'
