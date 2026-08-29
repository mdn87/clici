try {
    $m = [System.Threading.Mutex]::OpenExisting('Local\clici')
    Write-Host "Mutex Local\clici exists!"
    $acq = $m.WaitOne(0, $false)
    Write-Host "Acquired: $acq"
    if ($acq) {
        $m.ReleaseMutex()
    }
} catch [System.Threading.WaitHandleCannotBeOpenedException] {
    Write-Host "Mutex Local\clici does NOT exist."
} catch [System.Threading.AbandonedMutexException] {
    Write-Host "Mutex was ABANDONED! Successfully acquired."
} catch {
    Write-Host "Other exception: $_"
}
