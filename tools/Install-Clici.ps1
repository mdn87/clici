[CmdletBinding()]
param(
    [ValidateSet("win-x64", "win-arm64")]
    [string] $RuntimeIdentifier = "win-x64",

    [string] $InstallDirectory = (Join-Path `
        ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) `
        "Programs\clici"),

    [string] $ShortcutDirectory = (
        [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
    ),

    [switch] $NoLaunch
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectRoot "src\clici.App\clici.App.csproj"
$publishDirectory = Join-Path $projectRoot "artifacts\publish\$RuntimeIdentifier"
$publishedExecutable = Join-Path $publishDirectory "clici.exe"
$installedExecutable = Join-Path $InstallDirectory "clici.exe"
$shortcutPath = Join-Path $ShortcutDirectory "clici.lnk"

Write-Host "Publishing clici for $RuntimeIdentifier..."
& dotnet publish $projectFile `
    --configuration Release `
    --runtime $RuntimeIdentifier `
    --self-contained true `
    --output $publishDirectory `
    -p:PublishSingleFile=true `
    -p:DebugSymbols=false `
    -p:DebugType=None

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path -LiteralPath $publishedExecutable -PathType Leaf)) {
    throw "Published executable was not found at '$publishedExecutable'."
}

New-Item -ItemType Directory -Path $InstallDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $ShortcutDirectory -Force | Out-Null

Get-Process -Name "clici" -ErrorAction SilentlyContinue |
    Where-Object {
        try {
            [StringComparer]::OrdinalIgnoreCase.Equals($_.Path, $installedExecutable)
        }
        catch {
            $false
        }
    } |
    Stop-Process -Force

Copy-Item -LiteralPath $publishedExecutable -Destination $installedExecutable -Force

$shell = New-Object -ComObject WScript.Shell
try {
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $installedExecutable
    $shortcut.WorkingDirectory = $InstallDirectory
    $shortcut.IconLocation = "$installedExecutable,0"
    $shortcut.Description = "clici margin normalization"
    $shortcut.Save()
}
finally {
    if ($null -ne $shortcut) {
        [void] [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut)
    }
    [void] [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
}

if (-not $NoLaunch) {
    Start-Process -FilePath $installedExecutable
}

Write-Host ""
Write-Host "clici is installed."
Write-Host "Desktop shortcut: $shortcutPath"
Write-Host "Application:      $installedExecutable"
if ($NoLaunch) {
    Write-Host "Launch was skipped because -NoLaunch was supplied."
}
else {
    Write-Host "clici is running in the Windows notification area."
}
