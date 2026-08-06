[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet(
        'List',
        'ShowInput',
        'ShowExpected',
        'SetInput',
        'SetExpected',
        'Classify',
        'InventoryFormats',
        'SetInputAndHold')]
    [string]$Action,

    [ValidatePattern('^FX-\d{2}$')]
    [string]$FixtureId,

    [ValidateRange(0, 10000)]
    [int]$DelayMilliseconds = 0,

    [ValidateRange(1, 5000)]
    [int]$HoldMilliseconds = 500,

    [string]$FixturePath = (Join-Path $PSScriptRoot 'fixtures.json')
)

$ErrorActionPreference = 'Stop'

$fixtures = @(Get-Content -Raw -LiteralPath $FixturePath | ConvertFrom-Json)

if ($Action -eq 'List') {
    $fixtures |
        Select-Object id, purpose, expectedStatus
    return
}

if ($Action -eq 'InventoryFormats') {
    Add-Type -AssemblyName System.Windows.Forms
    $dataObject = [System.Windows.Forms.Clipboard]::GetDataObject()
    if ($null -eq $dataObject) {
        'NoFormats'
        return
    }

    @($dataObject.GetFormats()) |
        Sort-Object -Unique
    return
}

if ([string]::IsNullOrWhiteSpace($FixtureId)) {
    throw "FixtureId is required for action '$Action'."
}

$fixture = @($fixtures | Where-Object id -EQ $FixtureId)
if ($fixture.Count -ne 1) {
    throw "Fixture '$FixtureId' was not found exactly once."
}
$fixture = $fixture[0]

if ($Action -eq 'ShowInput') {
    [Console]::Out.Write([string]$fixture.input)
    return
}

if ($Action -eq 'ShowExpected') {
    [Console]::Out.Write([string]$fixture.expectedOutput)
    return
}

Add-Type -AssemblyName System.Windows.Forms

if ($Action -eq 'Classify') {
    $actual = [System.Windows.Forms.Clipboard]::GetText(
        [System.Windows.Forms.TextDataFormat]::UnicodeText)
    $matchesInput = [string]::Equals(
        $actual,
        [string]$fixture.input,
        [System.StringComparison]::Ordinal)
    $matchesExpected = [string]::Equals(
        $actual,
        [string]$fixture.expectedOutput,
        [System.StringComparison]::Ordinal)

    $classification = if ($matchesInput -and $matchesExpected) {
        'MatchesInputAndExpected'
    }
    elseif ($matchesExpected) {
        'MatchesExpected'
    }
    elseif ($matchesInput) {
        'MatchesInput'
    }
    else {
        'UnexpectedContent'
    }

    [pscustomobject]@{
        FixtureId = $fixture.id
        ExpectedStatus = $fixture.expectedStatus
        Classification = $classification
    }
    return
}

if ([System.Threading.Thread]::CurrentThread.GetApartmentState() -ne
    [System.Threading.ApartmentState]::STA) {
    throw 'Clipboard writes require STA. Run this helper with: pwsh -Sta -File <script> ...'
}

if ($Action -eq 'SetInputAndHold') {
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

public sealed class CliciProofClipboardLock : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr newOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    public static CliciProofClipboardLock Acquire()
    {
        if (!OpenClipboard(IntPtr.Zero))
        {
            throw new Win32Exception();
        }

        return new CliciProofClipboardLock();
    }

    public void Dispose()
    {
        CloseClipboard();
    }
}
'@
}

if ($DelayMilliseconds -gt 0) {
    Start-Sleep -Milliseconds $DelayMilliseconds
}

$value = if ($Action -eq 'SetExpected') {
    [string]$fixture.expectedOutput
}
else {
    [string]$fixture.input
}

[System.Windows.Forms.Clipboard]::SetText(
    $value,
    [System.Windows.Forms.TextDataFormat]::UnicodeText)

if ($Action -ne 'SetInputAndHold') {
    return
}

$clipboardLock = [CliciProofClipboardLock]::Acquire()
try {
    Start-Sleep -Milliseconds $HoldMilliseconds
}
finally {
    $clipboardLock.Dispose()
}
