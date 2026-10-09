param(
    [string]$ExpectedPlayerPath,
    [string]$OutputPath,
    [ValidateRange(1, 86400)][int]$DurationSec = 1200,
    [ValidateRange(500, 60000)][int]$IntervalMs = 1000,
    [string]$StopFile,
    [switch]$IncludeDescendants
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-ProcessMemorySample {
    param([Parameter(Mandatory)]$Process, [Parameter(Mandatory)][string]$ExpectedPlayerPath, [string]$SampleUtc)
    try {
        $Process.Refresh()
        if ($Process.HasExited -or -not $Process.Path) { return $null }
        $actualPath = [System.IO.Path]::GetFullPath($Process.Path)
        $expectedPath = [System.IO.Path]::GetFullPath($ExpectedPlayerPath)
        if (-not [string]::Equals($actualPath, $expectedPath, [StringComparison]::OrdinalIgnoreCase)) { return $null }
        $processId = [int]$Process.Id
    }
    catch { return $null }
    $workingSet = $null
    $privateMemory = $null
    try { $value = [long]$Process.WorkingSet64; if ($value -gt 0) { $workingSet = $value } } catch { }
    try { $value = [long]$Process.PrivateMemorySize64; if ($value -gt 0) { $privateMemory = $value } } catch { }
    if (-not $SampleUtc) { $SampleUtc = [DateTime]::UtcNow.ToString('o') }
    [pscustomobject][ordered]@{ sampleUtc = $SampleUtc; pid = $processId; workingSetBytes = $workingSet; privateMemoryBytes = $privateMemory }
}

function Find-PlayerMemorySample {
    param([string]$ExpectedPlayerPath, [Nullable[int]]$PinnedProcessId)
    $processName = [System.IO.Path]::GetFileNameWithoutExtension($ExpectedPlayerPath)
    $candidates = if ($null -ne $PinnedProcessId) { @(Get-Process -Id $PinnedProcessId -ErrorAction SilentlyContinue) }
        else { @(Get-Process -Name $processName -ErrorAction SilentlyContinue) }
    try {
        foreach ($candidate in $candidates) {
            $sample = Get-ProcessMemorySample -Process $candidate -ExpectedPlayerPath $ExpectedPlayerPath
            if ($null -ne $sample) { return $sample }
        }
    }
    finally { foreach ($candidate in $candidates) { $candidate.Dispose() } }
    return $null
}

function Get-OwnedProcessIds {
    param([int]$RootProcessId, [object[]]$Inventory)
    $owned = New-Object 'System.Collections.Generic.HashSet[int]'
    [void]$owned.Add($RootProcessId)
    do {
        $added = $false
        foreach ($record in $Inventory) {
            if ($owned.Contains([int]$record.ParentProcessId) -and $owned.Add([int]$record.ProcessId)) { $added = $true }
        }
    } while ($added)
    @($owned | Sort-Object)
}

function New-ProcessTreeSnapshot {
    param([int]$RootProcessId, [object[]]$Inventory)
    $ids = @(Get-OwnedProcessIds -RootProcessId $RootProcessId -Inventory $Inventory)
    $verified = @($Inventory | Where-Object { [int]$_.ProcessId -eq $RootProcessId }).Count -eq 1
    $identities = @{}
    foreach ($processId in $ids) {
        $member = $null
        try {
            $member = Get-Process -Id $processId -ErrorAction Stop
            $member.Refresh(); $identities[$processId] = $member.StartTime.ToUniversalTime().Ticks
        }
        catch { $verified = $false }
        finally { if ($member) { $member.Dispose() } }
    }
    [pscustomobject]@{ ProcessIds = $ids; StartTicks = $identities; Verified = $verified; DiscoveryUtc = [DateTime]::UtcNow.ToString('o') }
}

function Get-ProcessTreeMemorySample {
    param($RootSample, $Snapshot)
    $verified = [bool]$Snapshot.Verified
    $workingSet = [long]0; $privateMemory = [long]0
    $workingComplete = $true; $privateComplete = $true
    foreach ($processId in $Snapshot.ProcessIds) {
        $member = $null
        try {
            $member = Get-Process -Id $processId -ErrorAction Stop
            $member.Refresh()
            if ($member.HasExited -or -not $Snapshot.StartTicks.ContainsKey($processId) -or
                $member.StartTime.ToUniversalTime().Ticks -ne $Snapshot.StartTicks[$processId]) { throw 'Process tree member identity unavailable or changed.' }
            $sample = Get-ProcessMemorySample -Process $member -ExpectedPlayerPath $member.Path -SampleUtc $RootSample.sampleUtc
            if ($null -eq $sample) { throw 'Process tree member counters unavailable.' }
            if ($null -eq $sample.workingSetBytes) { $workingComplete = $false } else { $workingSet += $sample.workingSetBytes }
            if ($null -eq $sample.privateMemoryBytes) { $privateComplete = $false } else { $privateMemory += $sample.privateMemoryBytes }
        }
        catch { $verified = $false; $workingComplete = $false; $privateComplete = $false }
        finally { if ($member) { $member.Dispose() } }
    }
    [pscustomobject][ordered]@{ sampleUtc = [DateTime]::UtcNow.ToString('o'); pid = $RootSample.pid;
        workingSetBytes = $(if ($verified -and $workingComplete) { $workingSet } else { $null });
        privateMemoryBytes = $(if ($verified -and $privateComplete) { $privateMemory } else { $null });
        scopePids = ($Snapshot.ProcessIds -join ';'); treeCoverageVerified = $verified; treeDiscoveryUtc = $Snapshot.DiscoveryUtc }
}

function Invoke-ProcessMemoryCollector {
    param([Parameter(Mandatory)][string]$ExpectedPlayerPath, [Parameter(Mandatory)][string]$OutputPath,
        [int]$DurationSec = 1200, [int]$IntervalMs = 1000, [string]$StopFile, [switch]$IncludeDescendants)
    $expectedPath = [System.IO.Path]::GetFullPath($ExpectedPlayerPath)
    $csvPath = [System.IO.Path]::GetFullPath($OutputPath)
    if (Test-Path -LiteralPath $csvPath) { throw 'Process output already exists; choose a new CSV to avoid mixing runs.' }
    New-Item -ItemType Directory -Path (Split-Path -Parent $csvPath) -Force | Out-Null
    $writer = New-Object System.IO.StreamWriter($csvPath, $false, (New-Object System.Text.UTF8Encoding($false)))
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    $sampledProcessId = $null
    $treeSnapshot = $null; $lastDiscoverySeconds = -10
    try {
        $header = '"sampleUtc","pid","workingSetBytes","privateMemoryBytes"'
        if ($IncludeDescendants) { $header += ',"scopePids","treeCoverageVerified","treeDiscoveryUtc"' }
        $writer.WriteLine($header); $writer.Flush()
        while ($watch.Elapsed.TotalSeconds -lt $DurationSec) {
            if ($StopFile -and (Test-Path -LiteralPath $StopFile)) { break }
            $sample = Find-PlayerMemorySample -ExpectedPlayerPath $expectedPath -PinnedProcessId $sampledProcessId
            if ($null -ne $sample) {
                $sampledProcessId = $sample.pid
                if ($IncludeDescendants) {
                    if ($watch.Elapsed.TotalSeconds - $lastDiscoverySeconds -ge 10) {
                        try {
                            # Read ancestry identifiers only; never request command lines.
                            $inventory = @(Get-CimInstance -ClassName Win32_Process -Property ProcessId, ParentProcessId -ErrorAction Stop)
                            $treeSnapshot = New-ProcessTreeSnapshot -RootProcessId $sampledProcessId -Inventory $inventory
                        }
                        catch { $treeSnapshot = [pscustomobject]@{ ProcessIds = @($sampledProcessId); StartTicks = @{}; Verified = $false; DiscoveryUtc = [DateTime]::UtcNow.ToString('o') } }
                        $lastDiscoverySeconds = $watch.Elapsed.TotalSeconds
                    }
                    $sample = Get-ProcessTreeMemorySample -RootSample $sample -Snapshot $treeSnapshot
                }
                $csv = @($sample | ConvertTo-Csv -NoTypeInformation)
                $writer.WriteLine($csv[1]); $writer.Flush()
            }
            elseif ($null -ne $sampledProcessId) { break }
            $remaining = [int][Math]::Max(0, ($DurationSec - $watch.Elapsed.TotalSeconds) * 1000)
            if ($remaining -gt 0) { Start-Sleep -Milliseconds ([Math]::Min($IntervalMs, $remaining)) }
        }
    }
    finally { $writer.Dispose(); $watch.Stop() }
    Write-Output "Process samples: $csvPath"
}

# Dot sourcing exposes the sampling functions for data-only tests.
if ($MyInvocation.InvocationName -ne '.') {
    Invoke-ProcessMemoryCollector -ExpectedPlayerPath $ExpectedPlayerPath -OutputPath $OutputPath -DurationSec $DurationSec -IntervalMs $IntervalMs -StopFile $StopFile -IncludeDescendants:$IncludeDescendants
}
