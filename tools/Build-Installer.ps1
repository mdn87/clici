[CmdletBinding()]
param(
    [ValidateSet("win-x64", "win-arm64")]
    [string] $RuntimeIdentifier = "win-x64",

    # When set (or CLICI_SIGN_CERT_THUMBPRINT is present), clici.exe and setup.exe
    # are Authenticode-signed. When empty, signing is skipped with a warning.
    [string] $CertThumbprint = $env:CLICI_SIGN_CERT_THUMBPRINT
)

$ErrorActionPreference = "Stop"

$projectRoot   = Split-Path -Parent $PSScriptRoot
$projectFile   = Join-Path $projectRoot "src\clici.App\clici.App.csproj"
$publishDir    = Join-Path $projectRoot "artifacts\publish\$RuntimeIdentifier"
$publishedExe  = Join-Path $publishDir "clici.exe"
$issFile       = Join-Path $projectRoot "installer\clici.iss"
$propsFile     = Join-Path $projectRoot "Directory.Build.props"

# 1. Version from the single source of truth.
[xml] $props = Get-Content -LiteralPath $propsFile
$version = ($props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ }) | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "No <Version> found in $propsFile."
}

# 2. Publish the self-contained single-file exe.
Write-Host "Publishing clici $version for $RuntimeIdentifier..."
& dotnet publish $projectFile -p:PublishProfile=$RuntimeIdentifier --configuration Release
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }
if (-not (Test-Path -LiteralPath $publishedExe -PathType Leaf)) {
    throw "Published executable not found at '$publishedExe'."
}

# 3. Optional Authenticode signing (guarded — no-op without a thumbprint).
function Invoke-Sign([string] $path) {
    if ([string]::IsNullOrWhiteSpace($CertThumbprint)) {
        Write-Warning "No signing thumbprint set; skipping signature for $path."
        return
    }
    # Self-signed personal use: no public timestamp. Add /tr + /td when a real cert is used.
    & signtool.exe sign /sha1 $CertThumbprint /fd SHA256 $path
    if ($LASTEXITCODE -ne 0) { throw "signtool failed for '$path' ($LASTEXITCODE)." }
    Write-Host "Signed $path"
}
Invoke-Sign $publishedExe

# 4. Locate ISCC.exe (Inno Setup 6).
$isccCommand = Get-Command ISCC.exe -ErrorAction SilentlyContinue
if ($isccCommand) {
    $iscc = $isccCommand.Source
}
else {
    $iscc = Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"
    if (-not (Test-Path -LiteralPath $iscc)) {
        throw "ISCC.exe (Inno Setup 6) not found. Install from https://jrsoftware.org/isdl.php."
    }
}

# 5. Compile the installer.
Write-Host "Compiling installer with $iscc ..."
& $iscc "/DAppVersion=$version" "/DSourceExe=$publishedExe" "/DRid=$RuntimeIdentifier" $issFile
if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit code $LASTEXITCODE." }

$setupExe = Join-Path $projectRoot "artifacts\installer\clici-$version-$RuntimeIdentifier-setup.exe"
if (-not (Test-Path -LiteralPath $setupExe -PathType Leaf)) {
    throw "Installer was not produced at '$setupExe'."
}

# 6. Optionally sign the installer too.
Invoke-Sign $setupExe

Write-Host ""
Write-Host "Installer built: $setupExe"
