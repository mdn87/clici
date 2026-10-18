<#
  Prints the identity and registry view of whatever shell runs it, so two
  disagreeing readings of the clici Run value can be attributed to a machine,
  session, account, or elevation difference rather than guessed at.
#>
$id = [System.Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($id)

"machine    : $env:COMPUTERNAME"
"user       : $($id.Name)"
"SID        : $($id.User.Value)"
"session    : $((Get-Process -Id $PID).SessionId)"
"elevated   : $($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))"
"profile    : $env:USERPROFILE"
"64-bit     : $([Environment]::Is64BitProcess)"
""

$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$value = try { (Get-Item $key).GetValue('clici', $null) } catch { "(error: $($_.Exception.GetType().Name))" }
"clici val  : $(if ([string]::IsNullOrWhiteSpace($value)) { '(absent)' } else { $value })"
"read at    : $((Get-Date).ToString('HH:mm:ss.fff'))"
""

"reg.exe    :"
reg query 'HKCU\Software\Microsoft\Windows\CurrentVersion\Run' /v clici 2>&1 | ForEach-Object { "   $_" }
""
Read-Host 'Press Enter to close' | Out-Null
