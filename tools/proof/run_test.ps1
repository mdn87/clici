$proc = Start-Process -FilePath 'src/clici.App/bin/Release/net10.0-windows/clici.exe' -PassThru
Write-Host "Started clici PID: $($proc.Id)"
Start-Sleep -Seconds 3
Write-Host "HasExited: $($proc.HasExited)"
if ($proc.HasExited) {
    Write-Host "ExitCode: $($proc.ExitCode)"
} else {
    Write-Host "Process is still running!"
}

$logPath = "$env:LOCALAPPDATA\clici\diagnostics.log"
if (Test-Path $logPath) {
    Write-Host "--- Diagnostics Log ---"
    Get-Content $logPath
} else {
    Write-Host "No diagnostics.log found."
}
