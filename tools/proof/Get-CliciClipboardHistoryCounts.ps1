[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $SourceBase64,

    [Parameter(Mandatory)]
    [string] $ExpectedBase64
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Runtime.WindowsRuntime

$clipboardType = [Windows.ApplicationModel.DataTransfer.Clipboard, Windows.ApplicationModel.DataTransfer, ContentType = WindowsRuntime]
$historyResultType = [Windows.ApplicationModel.DataTransfer.ClipboardHistoryItemsResult, Windows.ApplicationModel.DataTransfer, ContentType = WindowsRuntime]
$standardFormatsType = [Windows.ApplicationModel.DataTransfer.StandardDataFormats, Windows.ApplicationModel.DataTransfer, ContentType = WindowsRuntime]
$asTaskMethod = [System.WindowsRuntimeSystemExtensions].GetMethods() |
    Where-Object {
        $_.Name -eq "AsTask" -and
        $_.IsGenericMethod -and
        $_.GetParameters().Count -eq 1
    } |
    Select-Object -First 1

function Wait-WindowsRuntimeOperation {
    param(
        [Parameter(Mandatory)] $Operation,
        [Parameter(Mandatory)][Type] $ResultType
    )

    $task = $asTaskMethod.MakeGenericMethod($ResultType).Invoke(
        $null,
        @($Operation))
    $task.Wait()
    return $task.Result
}

$source = [Text.Encoding]::Unicode.GetString(
    [Convert]::FromBase64String($SourceBase64))
$expected = [Text.Encoding]::Unicode.GetString(
    [Convert]::FromBase64String($ExpectedBase64))

if (-not $clipboardType::IsHistoryEnabled()) {
    [pscustomobject]@{
        Status = "HistoryDisabled"
        TotalItems = 0
        SourceMatches = 0
        ExpectedMatches = 0
    } | ConvertTo-Json -Compress
    return
}

$history = Wait-WindowsRuntimeOperation `
    -Operation $clipboardType::GetHistoryItemsAsync() `
    -ResultType $historyResultType
$sourceMatches = 0
$expectedMatches = 0
$totalItems = 0

if ($history.Status.ToString() -eq "Success") {
    foreach ($item in $history.Items) {
        $totalItems++
        $content = $item.Content
        if (-not $content.Contains($standardFormatsType::Text)) {
            continue
        }

        $text = Wait-WindowsRuntimeOperation `
            -Operation $content.GetTextAsync() `
            -ResultType ([string])
        if ([string]::Equals($text, $source, [StringComparison]::Ordinal)) {
            $sourceMatches++
        }
        if ([string]::Equals($text, $expected, [StringComparison]::Ordinal)) {
            $expectedMatches++
        }
    }
}

[pscustomobject]@{
    Status = $history.Status.ToString()
    TotalItems = $totalItems
    SourceMatches = $sourceMatches
    ExpectedMatches = $expectedMatches
} | ConvertTo-Json -Compress
