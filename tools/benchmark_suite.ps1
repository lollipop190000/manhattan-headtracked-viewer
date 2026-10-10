param(
    [ValidateRange(0, 20)][int]$Camera = 0,
    [switch]$NoTracker,
    [string]$CalibrationDirectory,
    [string]$CaseFilter = '*',
    [ValidateRange(1, 7200)][int]$DurationOverride,
    [switch]$ContinueOnError,
    [switch]$DryRun,
    [string]$OutputRoot
)
. (Join-Path $PSScriptRoot 'common.ps1')
$exploratory = $PSBoundParameters.ContainsKey('DurationOverride')
$mode = if ($NoTracker) { 'no-tracker-baseline' } elseif ($exploratory) { 'duration-override-exploratory' } else { 'webcam-target' }
$cases = @()
foreach ($region in @('Midtown', 'Downtown', 'Park', 'Uptown', 'Air')) {
    foreach ($speed in @(10, 50, 100)) {
        $duration = if ($speed -eq 10) { 900 } else { 180 }
        $cases += [pscustomobject]@{ id = "$region-$speed"; region = $region; speed = $speed; duration = $duration; collisionProxies = $false; purpose = 'regional' }
    }
}
$cases += [pscustomobject]@{ id = 'Midtown-50-proxies'; region = 'Midtown'; speed = 50; duration = 180; collisionProxies = $true; purpose = 'paired-proxy' }
# Full route is about 613s/circuit at 50m/s. The player drops the initial partial circuit.
# 1900 measured seconds allow >=3 crossings and >=2 complete recorded circuits.
$cases += [pscustomobject]@{ id = 'Full-50-memory'; region = 'Full'; speed = 50; duration = 1900; collisionProxies = $true; purpose = 'memory-roundtrip' }
$selected = @($cases | Where-Object { $_.id -like $CaseFilter })
if ($selected.Count -eq 0) { throw "No benchmark cases match '$CaseFilter'." }
if ($exploratory) { foreach ($case in $selected) { $case.duration = $DurationOverride } }
$measuredSeconds = [int](($selected | Measure-Object -Property duration -Sum).Sum)
Write-Host ("Suite mode: $mode; cases: $($selected.Count); planned measured time: $([TimeSpan]::FromSeconds($measuredSeconds)); warmup adds $($selected.Count * 15)s plus loading.")
if ($NoTracker -or $exploratory) { Write-Warning 'This suite is explicitly a baseline/exploration; it does not establish webcam release acceptance.' }
if ($DryRun) {
    $selected | Format-Table id, region, speed, duration, collisionProxies, purpose -AutoSize
    Write-Host 'DryRun: no player, tracker, GPU collector or results folder was started.'
    return
}
if (-not $OutputRoot) { $OutputRoot = Join-Path $ViewerRepository 'BenchmarkResults' }
$suiteId = 'suite-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N')
$suiteDirectory = Join-Path ([IO.Path]::GetFullPath($OutputRoot)) $suiteId
New-Item -ItemType Directory -Path $suiteDirectory -ErrorAction Stop | Out-Null
$states = @()
foreach ($case in $selected) {
    $states += [pscustomobject]@{ id = $case.id; region = $case.region; speed = $case.speed; duration = $case.duration;
        collisionProxies = $case.collisionProxies; purpose = $case.purpose; status = 'pending'; startedUtc = $null;
        finishedUtc = $null; outputDirectory = (Join-Path $suiteDirectory $case.id); artifacts = @(); error = $null }
}
$progress = [ordered]@{ schemaVersion = 1; suiteId = $suiteId; mode = $mode; durationOverrideApplied = $exploratory;
    trackerRequested = (-not [bool]$NoTracker); camera = $Camera; calibrationDirectoryConfigured = [bool]$CalibrationDirectory;
    caseFilter = $CaseFilter; plannedMeasuredSeconds = $measuredSeconds; createdUtc = [DateTime]::UtcNow.ToString('o');
    updatedUtc = $null; status = 'running'; completedCases = 0; performanceAcceptance = 'not evaluated'; cases = $states }
function Write-SuiteProgress {
    $progress.updatedUtc = [DateTime]::UtcNow.ToString('o')
    $progress.completedCases = @($states | Where-Object { $_.status -eq 'recorded' }).Count
    $temporary = Join-Path $suiteDirectory 'suite-progress.json.tmp'
    [IO.File]::WriteAllText($temporary, ($progress | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding($false)))
    Move-Item -LiteralPath $temporary -Destination (Join-Path $suiteDirectory 'suite-progress.json') -Force
}
$activeCase = $null
$finishedNormally = $false
Write-SuiteProgress
try {
    foreach ($state in $states) {
        $activeCase = $state
        $state.status = 'running'; $state.startedUtc = [DateTime]::UtcNow.ToString('o'); Write-SuiteProgress
        $parameters = @{ Region = $state.region; Speed = $state.speed; Duration = $state.duration; Camera = $Camera;
            NoTracker = $NoTracker; CollisionProxies = $state.collisionProxies; OutputDirectory = $state.outputDirectory }
        if ($CalibrationDirectory) { $parameters.CalibrationDirectory = $CalibrationDirectory }
        try {
            & (Join-Path $PSScriptRoot 'benchmark.ps1') @parameters
            foreach ($required in @('benchmark.json', 'frames.csv')) {
                if (-not (Test-Path -LiteralPath (Join-Path $state.outputDirectory $required))) { throw "Missing recorded artifact: $required" }
            }
            $state.status = 'recorded' # Collection only; the analyzer decides numeric criteria.
        }
        catch [System.Management.Automation.PipelineStoppedException] { throw }
        catch { $state.status = 'failed'; $state.error = $_.Exception.Message; Write-Warning ("Case $($state.id) failed: " + $state.error) }
        finally {
            $state.finishedUtc = [DateTime]::UtcNow.ToString('o')
            if (Test-Path -LiteralPath $state.outputDirectory) { $state.artifacts = @(Get-ChildItem -LiteralPath $state.outputDirectory -File | ForEach-Object { $_.FullName }) }
            Write-SuiteProgress
        }
        $activeCase = $null
        if ($state.status -eq 'failed' -and -not $ContinueOnError) { break }
    }
    $progress.status = if (@($states | Where-Object { $_.status -eq 'failed' }).Count) {
        if ($ContinueOnError) { 'completed-with-collection-errors' } else { 'stopped-on-collection-error' }
    } else { 'completed' }
    $finishedNormally = $true
}
finally {
    # benchmark.ps1 and the shared launcher finalize their own GPU/player/tracker processes.
    # Do not issue a broad Stop action that could affect an unrelated manual run.
    if (-not $finishedNormally) {
        $progress.status = 'interrupted'
        if ($activeCase -and $activeCase.status -eq 'running') { $activeCase.status = 'interrupted'; $activeCase.finishedUtc = [DateTime]::UtcNow.ToString('o') }
    }
    Write-SuiteProgress
}
Write-Host "Suite artifacts and progress: $suiteDirectory"
Write-Host 'Completed means artifacts were collected; no FPS, memory or webcam acceptance is inferred.'
if (@($states | Where-Object { $_.status -eq 'failed' }).Count) { throw 'Suite contains collection failures; inspect suite-progress.json and retained case logs.' }
