param(
    [ValidateSet(10, 50)][int]$Speed = 10,
    [ValidateSet('Midtown', 'Downtown', 'Park', 'Uptown', 'Air', 'Full')][string]$Region = 'Midtown',
    [ValidateRange(1, 7200)][int]$Duration = 1200,
    [ValidateRange(0, 20)][int]$Camera = 0,
    [switch]$CollisionProxies,
    [switch]$NoTracker,
    [switch]$SkipGpuLog,
    [ValidateRange(500, 60000)][int]$GpuIntervalMs = 1000,
    [string]$OutputDirectory,
    [string]$CalibrationDirectory
)
. (Join-Path $PSScriptRoot 'common.ps1')
. (Join-Path $PSScriptRoot 'gpu_sampling.ps1')
. (Join-Path $PSScriptRoot 'benchmark_environment.ps1')
. (Join-Path $PSScriptRoot 'measure_process.ps1')
$expectedPlayerPath = [System.IO.Path]::GetFullPath((Join-Path $ViewerRepository 'Builds\ManhattanViewer\ManhattanViewer.exe'))
$trackerExe = Join-Path $ViewerRepository '.tools\head-tracked-display\python_tracker\.venv\Scripts\python.exe'
Assert-NoExistingTrackedProcess -ExpectedPlayerPath $expectedPlayerPath
if (-not $NoTracker) { Assert-NoExistingTrackedProcess -ExpectedPlayerPath $trackerExe }
if (-not $OutputDirectory) {
    $runName = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + $Region + '-' + $Speed + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
    $OutputDirectory = Join-Path (Join-Path $ViewerRepository 'BenchmarkResults') $runName
}
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
if ($OutputDirectory.Contains('"')) { throw 'Output directory cannot contain a double quote.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
foreach ($existingResult in @('benchmark.json', 'gpu.csv', 'process.csv', 'tracker-process.csv', 'benchmark-environment.json')) {
    if (Test-Path -LiteralPath (Join-Path $OutputDirectory $existingResult)) { throw 'Benchmark results already exist. Choose a new output directory.' }
}
$environment = Get-BenchmarkEnvironment
$environment['benchmarkOptions'] = [ordered]@{ region = $Region; speedMetresPerSecond = $Speed; durationSeconds = $Duration;
    collisionProxies = [bool]$CollisionProxies; trackerRequested = (-not [bool]$NoTracker); gpuSamplingIntervalMs = $GpuIntervalMs; gpuLogRequested = (-not [bool]$SkipGpuLog); processSamplingIntervalMs = 1000; processLogRequested = $true; trackerProcessLogRequested = (-not [bool]$NoTracker); trackerProcessScope = 'exact virtualenv root plus verified descendants; current ancestry verified for every counted sample' }
$environment['sourceVersion'] = (Get-Content -LiteralPath (Join-Path $ViewerRepository 'assets/source-manifest.json') -Raw | ConvertFrom-Json).sourceVersion
$environment['technologyRevision'] = (Get-TechnologyVersion).revision
$environment | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'benchmark-environment.json') -Encoding UTF8
$playerArguments = '-screen-fullscreen 1 -window-mode exclusive -screen-width 1920 -screen-height 1200 --benchmark --benchmark-speed ' + $Speed + ' --benchmark-region ' + $Region + ' --benchmark-duration ' + $Duration +
    ' --benchmark-out "' + $OutputDirectory + '" --quit-after-benchmark'
if ($CollisionProxies) { $playerArguments += ' --collision-proxies' }
if ($NoTracker) { $playerArguments += ' --no-tracker' }
$gpuCollector = $null
$processCollector = $null
$trackerCollector = $null
$stopFile = Join-Path $OutputDirectory ('.gpu-stop-' + [guid]::NewGuid().ToString('N'))
$processStopFile = Join-Path $OutputDirectory ('.process-stop-' + [guid]::NewGuid().ToString('N'))
$trackerStopFile = Join-Path $OutputDirectory ('.tracker-process-stop-' + [guid]::NewGuid().ToString('N'))
try {
    $shell = (Get-Command 'powershell.exe' -ErrorAction Stop).Source
    $processCollector = Start-Process -FilePath $shell -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass',
        '-File', ('"' + (Join-Path $PSScriptRoot 'measure_process.ps1') + '"'),
        '-ExpectedPlayerPath', ('"' + $expectedPlayerPath + '"'), '-DurationSec', [string]($Duration + 300), '-IntervalMs', '1000',
        '-OutputPath', ('"' + (Join-Path $OutputDirectory 'process.csv') + '"'), '-StopFile', ('"' + $processStopFile + '"')) `
        -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $OutputDirectory 'process-collector.log') `
        -RedirectStandardError (Join-Path $OutputDirectory 'process-collector-error.log')
    if (-not $NoTracker) {
        $trackerCollector = Start-Process -FilePath $shell -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass',
            '-File', ('"' + (Join-Path $PSScriptRoot 'measure_process.ps1') + '"'), '-ExpectedPlayerPath', ('"' + $trackerExe + '"'),
            '-IncludeDescendants', '-DurationSec', [string]($Duration + 300), '-IntervalMs', '1000',
            '-OutputPath', ('"' + (Join-Path $OutputDirectory 'tracker-process.csv') + '"'), '-StopFile', ('"' + $trackerStopFile + '"')) `
            -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $OutputDirectory 'tracker-process-collector.log') `
            -RedirectStandardError (Join-Path $OutputDirectory 'tracker-process-collector-error.log')
    }
    if (-not $SkipGpuLog) {
        if (Get-Command 'nvidia-smi' -ErrorAction SilentlyContinue) {
            $shell = (Get-Command 'powershell.exe' -ErrorAction Stop).Source
            $gpuCollector = Start-Process -FilePath $shell -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass',
                '-File', ('"' + (Join-Path $PSScriptRoot 'measure_gpu.ps1') + '"'),
                '-DurationSec', [string]($Duration + 300), '-IntervalMs', [string]$GpuIntervalMs,
                '-OutputPath', ('"' + (Join-Path $OutputDirectory 'gpu.csv') + '"'), '-StopFile', ('"' + $stopFile + '"')) `
                -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $OutputDirectory 'gpu-collector.log') `
                -RedirectStandardError (Join-Path $OutputDirectory 'gpu-collector-error.log')
        }
        else { Write-Warning 'nvidia-smi is unavailable; this benchmark has no driver memory samples.' }
    }
    $runParameters = @{ Camera = $Camera; NoTracker = $NoTracker; PlayerArguments = $playerArguments; PlayerPath = $expectedPlayerPath }
    if ($CalibrationDirectory) { $runParameters.CalibrationDirectory = $CalibrationDirectory }
    & (Join-Path $PSScriptRoot 'run.ps1') @runParameters
}
finally {
    if ($trackerCollector) {
        [System.IO.File]::WriteAllText($trackerStopFile, '')
        $trackerCollector.Refresh()
        if (-not $trackerCollector.HasExited -and -not $trackerCollector.WaitForExit(2500)) {
            Stop-Process -Id $trackerCollector.Id -ErrorAction SilentlyContinue
            if (-not $trackerCollector.WaitForExit(2500)) { Write-Warning 'Owned tracker collector did not exit after termination; inspect its PID and collector log.' }
        }
        $trackerCollector.Refresh()
        if ($trackerCollector.HasExited -and $trackerCollector.ExitCode -ne 0) { Write-Warning 'Tracker collector failed or required forced cleanup; inspect tracker-process-collector-error.log.' }
        Remove-Item -LiteralPath $trackerStopFile -ErrorAction SilentlyContinue
    }
    if ($processCollector) {
        [System.IO.File]::WriteAllText($processStopFile, '')
        $processCollector.Refresh()
        if (-not $processCollector.HasExited -and -not $processCollector.WaitForExit(2500)) {
            Stop-Process -Id $processCollector.Id -ErrorAction SilentlyContinue
            if (-not $processCollector.WaitForExit(2500)) { Write-Warning 'Owned process collector did not exit after termination; inspect its PID and collector log.' }
        }
        $processCollector.Refresh()
        if ($processCollector.HasExited -and $processCollector.ExitCode -ne 0) { Write-Warning 'Process collector failed or required forced cleanup; inspect process-collector-error.log.' }
        Remove-Item -LiteralPath $processStopFile -ErrorAction SilentlyContinue
    }
    if ($gpuCollector) {
        [System.IO.File]::WriteAllText($stopFile, '')
        $gpuCollector.Refresh()
        if (-not $gpuCollector.HasExited -and -not $gpuCollector.WaitForExit(2500)) {
            Stop-Process -Id $gpuCollector.Id -ErrorAction SilentlyContinue
            if (-not $gpuCollector.WaitForExit(2500)) { Write-Warning 'Owned GPU collector did not exit after termination; inspect its PID and collector log.' }
        }
        $gpuCollector.Refresh()
        if ($gpuCollector.HasExited -and $gpuCollector.ExitCode -ne 0) { Write-Warning 'GPU collector failed or required forced cleanup; inspect gpu-collector-error.log.' }
        Remove-Item -LiteralPath $stopFile -ErrorAction SilentlyContinue
    }
}
Write-Host "Benchmark output: $OutputDirectory"
