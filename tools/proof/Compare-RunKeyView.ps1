<#
  Dumps this process's complete view of the Run key, by several independent
  paths, so two shells that disagree about one value can be compared broadly.
  If the full value lists differ, the two processes are not reading the same
  hive and no single-value argument is worth anything.
#>
$sid = ([System.Security.Principal.WindowsIdentity]::GetCurrent()).User.Value
"machine  : $env:COMPUTERNAME   session: $((Get-Process -Id $PID).SessionId)   sid: $sid"
"read at  : $((Get-Date).ToString('HH:mm:ss.fff'))"
""

"== HKCU Run: all values (name -> length) =="
$hkcu = Get-Item 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$names = $hkcu.GetValueNames() | Sort-Object
"  count = $($names.Count)"
$names | ForEach-Object { "   $_" }
""

"== clici, four ways =="
$a = $hkcu.GetValue('clici', $null)
"  1 .NET HKCU        : $(if ([string]::IsNullOrWhiteSpace($a)) { '(absent)' } else { $a })"

$b = try { (Get-Item "Registry::HKEY_USERS\$sid\Software\Microsoft\Windows\CurrentVersion\Run").GetValue('clici', $null) } catch { "(error: $($_.Exception.GetType().Name))" }
"  2 .NET HKU\<sid>   : $(if ([string]::IsNullOrWhiteSpace($b)) { '(absent)' } else { $b })"

$c = (reg query 'HKCU\Software\Microsoft\Windows\CurrentVersion\Run' /v clici 2>&1 | Out-String).Trim()
"  3 reg.exe HKCU     : $(if ($c -match 'clici\s+REG_SZ\s+(.+)') { $Matches[1].Trim() } else { '(absent)' })"

$d = try {
    $k = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run')
    $k.GetValue('clici', $null)
} catch { "(error: $($_.Exception.GetType().Name))" }
"  4 raw RegistryKey  : $(if ([string]::IsNullOrWhiteSpace($d)) { '(absent)' } else { $d })"
""
Read-Host 'Press Enter to close' | Out-Null
