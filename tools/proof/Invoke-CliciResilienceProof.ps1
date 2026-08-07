[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet(
        "Scale",
        "Rapid",
        "MultiFormat",
        "History",
        "SequenceRace",
        "CrashRestart")]
    [string] $Action,

    [int[]] $LineCounts = @(1000, 10000, 100000),

    [int] $TimeoutMilliseconds = 15000,

    [int] $RapidIterations = 50,

    [int] $ExpectedMaximumCharacters = 2000000,

    [int] $RaceLineCount = 250000,

    [int[]] $RaceDelays = @(120),

    [string] $ExecutablePath = (
        Join-Path $env:LOCALAPPDATA "Programs\clici\clici.exe"
    )
)

$ErrorActionPreference = "Stop"

if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne
    [Threading.ApartmentState]::STA) {
    throw "Run this helper in an STA process: pwsh -Sta -File <script> ..."
}

Add-Type -AssemblyName System.Windows.Forms
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class CliciProofNativeClipboard
{
    private const uint GmemMoveable = 0x0002;
    private const uint CfUnicodeText = 13;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr owner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint format, IntPtr memory);

    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr memory);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr memory);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalFree(IntPtr memory);

    public static bool TrySetUnicodeText(string text)
    {
        if (!OpenClipboard(IntPtr.Zero))
        {
            return false;
        }

        IntPtr memory = IntPtr.Zero;
        try
        {
            var bytes = checked((text.Length + 1) * sizeof(char));
            memory = GlobalAlloc(GmemMoveable, (UIntPtr)bytes);
            if (memory == IntPtr.Zero)
            {
                return false;
            }

            var pointer = GlobalLock(memory);
            if (pointer == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                Marshal.Copy(text.ToCharArray(), 0, pointer, text.Length);
                Marshal.WriteInt16(pointer, text.Length * sizeof(char), 0);
            }
            finally
            {
                GlobalUnlock(memory);
            }

            if (!EmptyClipboard() ||
                SetClipboardData(CfUnicodeText, memory) == IntPtr.Zero)
            {
                return false;
            }

            memory = IntPtr.Zero;
            return true;
        }
        finally
        {
            if (memory != IntPtr.Zero)
            {
                GlobalFree(memory);
            }

            CloseClipboard();
        }
    }
}

public static class CliciProofWindowProbe
{
    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(
        EnumWindowsProc callback,
        IntPtr parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(
        IntPtr window,
        StringBuilder text,
        int maximum);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        IntPtr window,
        out uint processId);

    public static uint[] FindListenerProcessIds()
    {
        var processIds = new List<uint>();
        EnumWindows((window, _) =>
        {
            var text = new StringBuilder(256);
            GetWindowText(window, text, text.Capacity);
            if (string.Equals(
                text.ToString(),
                "clici clipboard listener",
                StringComparison.Ordinal))
            {
                GetWindowThreadProcessId(window, out var processId);
                processIds.Add(processId);
            }

            return true;
        }, IntPtr.Zero);

        return processIds.ToArray();
    }
}
'@

function Try-SetClipboardText {
    param([Parameter(Mandatory)][string] $Text)

    for ($attempt = 1; $attempt -le 4; $attempt++) {
        try {
            $data = [Windows.Forms.DataObject]::new()
            $data.SetData(
                [Windows.Forms.DataFormats]::UnicodeText,
                $true,
                $Text)
            [Windows.Forms.Clipboard]::SetDataObject($data, $true, 0, 0)
            return $true
        }
        catch [Runtime.InteropServices.ExternalException] {
            if ($attempt -lt 4) {
                Start-Sleep -Milliseconds 20
            }
        }
    }

    return $false
}

function Set-ClipboardText {
    param([Parameter(Mandatory)][string] $Text)

    if (-not (Try-SetClipboardText -Text $Text)) {
        throw "Clipboard remained busy after four bounded write attempts."
    }
}

function Start-RaceWriter {
    param(
        [Parameter(Mandatory)][string] $Text,
        [Parameter(Mandatory)][Threading.ManualResetEventSlim] $Gate,
        [int] $DelayMilliseconds
    )

    $runspace = [Management.Automation.Runspaces.RunspaceFactory]::CreateRunspace()
    $runspace.ApartmentState = [Threading.ApartmentState]::STA
    $runspace.ThreadOptions = [Management.Automation.Runspaces.PSThreadOptions]::ReuseThread
    $runspace.Open()

    $powershell = [PowerShell]::Create()
    $powershell.Runspace = $runspace
    [void] $powershell.AddScript({
        param($Value, $StartGate, $Delay)

        $StartGate.Wait()
        Start-Sleep -Milliseconds $Delay
        for ($attempt = 1; $attempt -le 4; $attempt++) {
            if ([CliciProofNativeClipboard]::TrySetUnicodeText($Value)) {
                return $true
            }

            if ($attempt -lt 4) {
                Start-Sleep -Milliseconds 20
            }
        }

        return $false
    })
    [void] $powershell.AddArgument($Text)
    [void] $powershell.AddArgument($Gate)
    [void] $powershell.AddArgument($DelayMilliseconds)

    [pscustomobject]@{
        PowerShell = $powershell
        Runspace = $runspace
        AsyncResult = $powershell.BeginInvoke()
    }
}

function Complete-RaceWriter {
    param(
        [Parameter(Mandatory)] $Writer,
        [int] $Timeout = $TimeoutMilliseconds
    )

    try {
        if (-not $Writer.AsyncResult.AsyncWaitHandle.WaitOne($Timeout)) {
            return $false
        }

        $results = @($Writer.PowerShell.EndInvoke($Writer.AsyncResult))
        return $results.Count -gt 0 -and [bool]$results[-1]
    }
    finally {
        $Writer.PowerShell.Dispose()
        $Writer.Runspace.Dispose()
    }
}

function Wait-ForClipboardText {
    param(
        [Parameter(Mandatory)][string] $Expected,
        [int] $Timeout = $TimeoutMilliseconds
    )

    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    do {
        try {
            $actual = [Windows.Forms.Clipboard]::GetText(
                [Windows.Forms.TextDataFormat]::UnicodeText)
            if ([string]::Equals(
                    $actual,
                    $Expected,
                    [StringComparison]::Ordinal)) {
                return $stopwatch.ElapsedMilliseconds
            }
        }
        catch [Runtime.InteropServices.ExternalException] {
            # A clipboard participant has it open; retry within the bounded wait.
        }

        Start-Sleep -Milliseconds 20
    } while ($stopwatch.ElapsedMilliseconds -lt $Timeout)

    return $null
}

function Wait-ForListener {
    param(
        [int] $ExpectedCount,
        [int] $ExpectedProcessId = 0
    )

    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    do {
        $owners = @([CliciProofWindowProbe]::FindListenerProcessIds())
        $ownerMatches = (
            $ExpectedProcessId -eq 0 -or
            ($owners.Count -eq 1 -and $owners[0] -eq $ExpectedProcessId)
        )
        if ($owners.Count -eq $ExpectedCount -and $ownerMatches) {
            return $owners
        }

        Start-Sleep -Milliseconds 50
    } while ($stopwatch.ElapsedMilliseconds -lt $TimeoutMilliseconds)

    throw "Timed out waiting for $ExpectedCount clici listener(s)."
}

function New-LargeFixture {
    param([int] $LineCount)

    $inputBuilder = [Text.StringBuilder]::new($LineCount * 48)
    $expectedBuilder = [Text.StringBuilder]::new($LineCount * 46)

    for ($index = 0; $index -lt $LineCount; $index++) {
        $content = "[clici-scale-{0:D7}] alpha beta gamma delta" -f $index
        [void] $inputBuilder.Append("  ").Append($content).Append("`r`n")
        [void] $expectedBuilder.Append($content).Append("`r`n")
    }

    [pscustomobject]@{
        Input = $inputBuilder.ToString()
        Expected = $expectedBuilder.ToString()
    }
}

if ($Action -eq "Scale") {
    foreach ($lineCount in $LineCounts) {
        $fixture = New-LargeFixture -LineCount $lineCount
        $before = Get-Process -Name "clici" -ErrorAction Stop |
            Select-Object -First 1
        $writeTimer = [Diagnostics.Stopwatch]::StartNew()
        Set-ClipboardText -Text $fixture.Input
        $writeTimer.Stop()

        $expectedOverLimit = $fixture.Input.Length -gt $ExpectedMaximumCharacters
        if ($expectedOverLimit) {
            Start-Sleep -Milliseconds 750
            $actual = [Windows.Forms.Clipboard]::GetText(
                [Windows.Forms.TextDataFormat]::UnicodeText)
            $normalizeMilliseconds = $null
            $outcome = if ([string]::Equals(
                    $actual,
                    $fixture.Input,
                    [StringComparison]::Ordinal)) {
                "SkippedOverLimit"
            }
            elseif ([string]::Equals(
                    $actual,
                    $fixture.Expected,
                    [StringComparison]::Ordinal)) {
                "UnexpectedlyNormalizedOverLimit"
            }
            else {
                "UnexpectedContent"
            }
        }
        else {
            $normalizeMilliseconds = Wait-ForClipboardText -Expected $fixture.Expected
            $outcome = if ($null -ne $normalizeMilliseconds) {
                "Normalized"
            }
            else {
                "TimedOut"
            }
        }
        $after = Get-Process -Name "clici" -ErrorAction Stop |
            Select-Object -First 1

        [pscustomobject]@{
            Case = "Scale"
            Lines = $lineCount
            InputCharacters = $fixture.Input.Length
            ClipboardWriteMilliseconds = $writeTimer.ElapsedMilliseconds
            NormalizeMilliseconds = $normalizeMilliseconds
            Outcome = $outcome
            ProcessAlive = -not $after.HasExited
            WorkingSetBeforeMB = [math]::Round($before.WorkingSet64 / 1MB, 1)
            WorkingSetAfterMB = [math]::Round($after.WorkingSet64 / 1MB, 1)
            PrivateMemoryAfterMB = [math]::Round($after.PrivateMemorySize64 / 1MB, 1)
        }
    }

    return
}

if ($Action -eq "Rapid") {
    $finalInput = $null
    $finalExpected = $null
    $successfulWrites = 0
    $busyWrites = 0
    $timer = [Diagnostics.Stopwatch]::StartNew()

    for ($iteration = 0; $iteration -lt $RapidIterations; $iteration++) {
        $first = "  [clici-rapid-{0:D4}] first`r`n" -f $iteration
        $second = "    nested`r`n"
        $finalInput = $first + $second
        $finalExpected = $first.Substring(2) + $second.Substring(2)
        if (Try-SetClipboardText -Text $finalInput) {
            $successfulWrites++
        }
        else {
            $busyWrites++
        }
    }

    Set-ClipboardText -Text $finalInput
    $normalizeMilliseconds = Wait-ForClipboardText -Expected $finalExpected
    $timer.Stop()
    $process = Get-Process -Name "clici" -ErrorAction Stop |
        Select-Object -First 1

    [pscustomobject]@{
        Case = "Rapid"
        Writes = $RapidIterations
        SuccessfulBurstWrites = $successfulWrites
        BusyBurstWrites = $busyWrites
        TotalMilliseconds = $timer.ElapsedMilliseconds
        FinalNormalized = $null -ne $normalizeMilliseconds
        SettleMilliseconds = $normalizeMilliseconds
        ProcessAlive = -not $process.HasExited
        WorkingSetMB = [math]::Round($process.WorkingSet64 / 1MB, 1)
    }
    return
}

if ($Action -eq "MultiFormat") {
    $input = "  [clici-rich] first`r`n  second`r`n    nested"
    $expected = "[clici-rich] first`r`nsecond`r`n  nested"
    $data = [Windows.Forms.DataObject]::new()
    $data.SetData([Windows.Forms.DataFormats]::UnicodeText, $true, $input)
    $data.SetData(
        [Windows.Forms.DataFormats]::Html,
        $false,
        "<pre><strong>  [clici-rich] first</strong>`r`n  second`r`n    nested</pre>")
    $data.SetData(
        [Windows.Forms.DataFormats]::Rtf,
        $false,
        "{\rtf1\ansi   [clici-rich] first\line   second\line     nested}")

    $beforeFormats = @($data.GetFormats($false) | Sort-Object -Unique)
    [Windows.Forms.Clipboard]::SetDataObject($data, $true, 4, 20)
    $normalizeMilliseconds = Wait-ForClipboardText -Expected $expected
    $afterData = [Windows.Forms.Clipboard]::GetDataObject()
    $afterFormats = @($afterData.GetFormats($false) | Sort-Object -Unique)

    [pscustomobject]@{
        Case = "MultiFormat"
        Normalized = $null -ne $normalizeMilliseconds
        BeforeFormats = $beforeFormats -join ", "
        AfterFormats = $afterFormats -join ", "
        HtmlPreserved = $afterFormats -contains [Windows.Forms.DataFormats]::Html
        RtfPreserved = $afterFormats -contains [Windows.Forms.DataFormats]::Rtf
    }
    return
}

if ($Action -eq "History") {
    $marker = "[clici-history-{0}]" -f ([Guid]::NewGuid().ToString("N"))
    $source = "  $marker first`r`n  second"
    $expected = "$marker first`r`nsecond"
    $sourceBase64 = [Convert]::ToBase64String(
        [Text.Encoding]::Unicode.GetBytes($source))
    $expectedBase64 = [Convert]::ToBase64String(
        [Text.Encoding]::Unicode.GetBytes($expected))
    $historyHelper = Join-Path $PSScriptRoot "Get-CliciClipboardHistoryCounts.ps1"

    $beforeJson = & powershell.exe `
        -NoProfile `
        -File $historyHelper `
        -SourceBase64 $sourceBase64 `
        -ExpectedBase64 $expectedBase64
    if ($LASTEXITCODE -ne 0) {
        throw "Clipboard history helper failed before the test."
    }
    $before = $beforeJson | ConvertFrom-Json
    Set-ClipboardText -Text $source
    $normalizeMilliseconds = Wait-ForClipboardText -Expected $expected
    Start-Sleep -Milliseconds 1000
    $afterJson = & powershell.exe `
        -NoProfile `
        -File $historyHelper `
        -SourceBase64 $sourceBase64 `
        -ExpectedBase64 $expectedBase64
    if ($LASTEXITCODE -ne 0) {
        throw "Clipboard history helper failed after the test."
    }
    $after = $afterJson | ConvertFrom-Json

    $data = [Windows.Forms.Clipboard]::GetDataObject()
    $historyFlag = $data.GetData("CanIncludeInClipboardHistory", $false)
    $cloudFlag = $data.GetData("CanUploadToCloudClipboard", $false)
    $historyBytes = if ($historyFlag -is [IO.MemoryStream]) {
        $historyFlag.ToArray()
    }
    else {
        @()
    }
    $cloudBytes = if ($cloudFlag -is [IO.MemoryStream]) {
        $cloudFlag.ToArray()
    }
    else {
        @()
    }

    [pscustomobject]@{
        Case = "History"
        Status = $after.Status
        Normalized = $null -ne $normalizeMilliseconds
        SourceHistoryDelta = $after.SourceMatches - $before.SourceMatches
        NormalizedHistoryDelta = $after.ExpectedMatches - $before.ExpectedMatches
        HistoryRequestIsOne = (
            $historyBytes.Count -eq 4 -and
            [BitConverter]::ToUInt32($historyBytes, 0) -eq 1)
        CloudRequestIsOne = (
            $cloudBytes.Count -eq 4 -and
            [BitConverter]::ToUInt32($cloudBytes, 0) -eq 1)
        TotalHistoryItems = $after.TotalItems
    }
    return
}

if ($Action -eq "CrashRestart") {
    $resolvedExecutable = (Resolve-Path -LiteralPath $ExecutablePath).Path
    $original = Get-Process -Name "clici" -ErrorAction Stop |
        Select-Object -First 1
    $originalId = $original.Id
    Stop-Process -Id $originalId -Force
    [void] (Wait-ForListener -ExpectedCount 0)

    $firstRestart = Start-Process -FilePath $resolvedExecutable -PassThru
    [void] (Wait-ForListener `
        -ExpectedCount 1 `
        -ExpectedProcessId $firstRestart.Id)

    $secondary = Start-Process -FilePath $resolvedExecutable -PassThru
    $secondaryExited = $secondary.WaitForExit($TimeoutMilliseconds)
    $secondaryExitCode = if ($secondaryExited) {
        $secondary.ExitCode
    }
    else {
        $null
    }

    Stop-Process -Id $firstRestart.Id -Force
    [void] (Wait-ForListener -ExpectedCount 0)
    $secondRestart = Start-Process -FilePath $resolvedExecutable -PassThru
    $owners = @(Wait-ForListener `
        -ExpectedCount 1 `
        -ExpectedProcessId $secondRestart.Id)

    [pscustomobject]@{
        Case = "CrashRestart"
        ForcedOriginalProcessId = $originalId
        FirstRestartProcessId = $firstRestart.Id
        SecondaryExited = $secondaryExited
        SecondaryExitCode = $secondaryExitCode
        SecondRestartProcessId = $secondRestart.Id
        FinalProcessAlive = -not $secondRestart.HasExited
        FinalProcessCount = @(
            Get-Process -Name "clici" -ErrorAction SilentlyContinue
        ).Count
        FinalListenerCount = $owners.Count
        FinalListenerOwner = $owners | Select-Object -First 1
    }
    return
}

$sentinel = "[clici-race-sentinel] newer clipboard item"
$raceFixture = New-LargeFixture -LineCount $RaceLineCount
if ($raceFixture.Input.Length -gt $ExpectedMaximumCharacters) {
    throw (
        "SequenceRace requires the generated source to be within clici's " +
        "configured ceiling. Temporarily raise maximumTextCharacters and pass " +
        "the same value through -ExpectedMaximumCharacters."
    )
}

$diagnosticPath = Join-Path $env:LOCALAPPDATA "clici\clici.log"
$results = foreach ($delay in $RaceDelays) {
    $staleEventsBefore = if (Test-Path -LiteralPath $diagnosticPath) {
        @(
            Select-String `
                -LiteralPath $diagnosticPath `
                -SimpleMatch "event name=skipped-stale-clipboard-write"
        ).Count
    }
    else {
        0
    }
    $gate = [Threading.ManualResetEventSlim]::new($false)
    try {
        $writer = Start-RaceWriter `
            -Text $sentinel `
            -Gate $gate `
            -DelayMilliseconds $delay
        Set-ClipboardText -Text $raceFixture.Input
        $gate.Set()
        $writerSucceeded = Complete-RaceWriter -Writer $writer
    }
    finally {
        $gate.Dispose()
    }
    Start-Sleep -Milliseconds 750
    $staleEventsAfter = if (Test-Path -LiteralPath $diagnosticPath) {
        @(
            Select-String `
                -LiteralPath $diagnosticPath `
                -SimpleMatch "event name=skipped-stale-clipboard-write"
        ).Count
    }
    else {
        0
    }

    $actual = [Windows.Forms.Clipboard]::GetText(
        [Windows.Forms.TextDataFormat]::UnicodeText)
    $outcome = if ([string]::Equals($actual, $sentinel, [StringComparison]::Ordinal)) {
        "NewerItemPreserved"
    }
    elseif ([string]::Equals(
            $actual,
            $raceFixture.Expected,
            [StringComparison]::Ordinal)) {
        "OlderNormalizationOverwroteNewerItem"
    }
    elseif ([string]::Equals(
            $actual,
            $raceFixture.Input,
            [StringComparison]::Ordinal)) {
        "OlderInputRemained"
    }
    else {
        "UnexpectedContent"
    }

    [pscustomobject]@{
        Case = "SequenceRace"
        DelayMilliseconds = $delay
        NewerWriterSucceeded = $writerSucceeded
        Outcome = $outcome
        StaleGuardObserved = $staleEventsAfter -gt $staleEventsBefore
        ProcessAlive = $null -ne (
            Get-Process -Name "clici" -ErrorAction SilentlyContinue |
                Select-Object -First 1)
    }
}

$results
