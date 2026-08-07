[CmdletBinding()]
param(
    [string] $Subject = "CN=clici self-signed",

    [string] $PfxPath = (Join-Path $PSScriptRoot ".certs\clici-selfsigned.pfx"),

    [securestring] $Password = (Read-Host -AsSecureString -Prompt "PFX export password")
)

$ErrorActionPreference = "Stop"

# 1. Create a code-signing cert in the current user's personal store.
$cert = New-SelfSignedCertificate `
    -Type CodeSigningCert `
    -Subject $Subject `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -KeyExportPolicy Exportable `
    -KeyUsage DigitalSignature `
    -NotAfter (Get-Date).AddYears(5)

# 2. Trust it locally so SmartScreen/UAC accept signatures on THIS machine.
foreach ($storeName in @("Root", "TrustedPublisher")) {
    $store = New-Object System.Security.Cryptography.X509Certificates.X509Store(
        $storeName, "CurrentUser")
    $store.Open("ReadWrite")
    try { $store.Add($cert) } finally { $store.Close() }
}

# 3. Export the PFX to the gitignored certs directory.
$certDir = Split-Path -Parent $PfxPath
New-Item -ItemType Directory -Force -Path $certDir | Out-Null
Export-PfxCertificate -Cert $cert -FilePath $PfxPath -Password $Password | Out-Null

Write-Host ""
Write-Host "Self-signed code-signing certificate created and locally trusted."
Write-Host "Thumbprint : $($cert.Thumbprint)"
Write-Host "PFX        : $PfxPath"
Write-Host ""
Write-Host "To sign during a build, set the thumbprint and re-run the installer build:"
Write-Host "  `$env:CLICI_SIGN_CERT_THUMBPRINT = '$($cert.Thumbprint)'"
Write-Host "  .\tools\Build-Installer.ps1"
