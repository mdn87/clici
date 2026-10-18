#requires -Version 7.0
param(
    [Parameter(Mandatory)][string] $ProbeDll,
    [Parameter(Mandatory)][string] $EvidenceFile,
    [Parameter(Mandatory)][string[]] $ProbeArguments,
    [ValidateRange(1, 35)][int] $TimeoutSeconds = 10
)

$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $EvidenceFile) { throw 'Choose a new evidence file; earlier results will not be overwritten.' }
$start = [Diagnostics.ProcessStartInfo]::new('dotnet')
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.ArgumentList.Add([IO.Path]::GetFullPath($ProbeDll))
foreach ($argument in $ProbeArguments) { $start.ArgumentList.Add($argument) }
$process = [Diagnostics.Process]::Start($start)
$stdout = $process.StandardOutput.ReadToEndAsync()
$stderr = $process.StandardError.ReadToEndAsync()
if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
    $process.Kill($true)
    $process.WaitForExit()
    @{ result = 'TimedOut'; seconds = $TimeoutSeconds; warning = 'A COM scroll already started cannot be undone by this timeout.' } |
        ConvertTo-Json | Set-Content -LiteralPath $EvidenceFile
    throw 'The isolated probe worker exceeded its deadline.'
}
$output = $stdout.GetAwaiter().GetResult()
$output | Set-Content -LiteralPath $EvidenceFile
if ($process.ExitCode -ne 0) {
    throw "Probe refused or failed (exit $($process.ExitCode)); see $EvidenceFile. $($stderr.GetAwaiter().GetResult())"
}
$output | ConvertFrom-Json
