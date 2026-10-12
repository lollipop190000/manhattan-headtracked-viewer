param([string]$PythonExe = 'python', [switch]$DataOnly, [switch]$EditorPlayMode, [string]$EditorPath)
. (Join-Path $PSScriptRoot 'common.ps1')
$previousTemp = $env:TEMP
$previousTmp = $env:TMP
$pythonScratch = Join-Path $ViewerRepository '.cache/python-temp'
New-Item -ItemType Directory -Path $pythonScratch -Force | Out-Null
try {
    $env:TEMP = $pythonScratch
    $env:TMP = $pythonScratch
    Invoke-Checked $PythonExe @('-m', 'unittest', 'discover', '-s', (Join-Path $PSScriptRoot 'tests'), '-p', 'test_*.py')
    Invoke-Checked $PythonExe @((Join-Path $PSScriptRoot 'source_assets.py'), 'validate')
}
finally { $env:TEMP = $previousTemp; $env:TMP = $previousTmp }
& (Join-Path $PSScriptRoot 'test_gpu_sampling.ps1')
& (Join-Path $PSScriptRoot 'test_benchmark_environment.ps1')
if (-not $DataOnly) {
    $results = Join-Path $ViewerRepository 'TestResults'
    New-Item -ItemType Directory -Path $results -Force | Out-Null
    $previousCalibration = $env:HEADTRACKED_CALIBRATION_DIRECTORY
    try {
        $env:HEADTRACKED_CALIBRATION_DIRECTORY = Join-Path $results ('disposable-calibration-' + [Guid]::NewGuid().ToString('N'))
        Invoke-Checked 'unity' @('test', $ViewerProject, '--editor-version', $ViewerEditorVersion,
            '--mode', 'EditMode', '--report-format', 'junit', '--output', (Join-Path $results 'editmode.xml'), '--timeout', '1800', '--', '--no-tracker')
        if ($EditorPlayMode) {
            if (-not $EditorPath) {
                $inventory = (& unity editors --installed --format json) | ConvertFrom-Json
                if ($LASTEXITCODE -ne 0 -or -not $inventory.success) { throw 'Unable to locate the pinned Unity Editor.' }
                $editor = @($inventory.data | Where-Object version -eq $ViewerEditorVersion) | Select-Object -First 1
                if (-not $editor -or -not $editor.location) { throw 'Pinned Unity Editor not installed. Pass -EditorPath.' }
                $EditorPath = if ($editor.location.EndsWith('Unity.exe', [StringComparison]::OrdinalIgnoreCase)) { $editor.location } else { Join-Path $editor.location 'Editor\Unity.exe' }
            }
            $runId = [Guid]::NewGuid().ToString('N')
            $report = Join-Path $results ("playmode-$runId-nunit.xml")
            # This explicit option opens the GPU Game View. Batch mode cannot invoke WaitForEndOfFrame.
            $process = Start-Process -FilePath $EditorPath -ArgumentList @('-projectPath', ('"' + $ViewerProject + '"'),
                '-executeMethod', 'Manhattan.Editor.ViewerTestAutomation.PreparePlayMode', '-runTests', '-testPlatform', 'PlayMode',
                '-testResults', ('"' + $report + '"'), '--no-tracker', '-logFile', ('"' + (Join-Path $results "playmode-$runId-editor.log") + '"')) -WindowStyle Normal -PassThru
            try {
                if (-not $process.WaitForExit(1800000)) { throw 'Owned Editor PlayMode tests timed out.' }
                $process.Refresh()
                if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $report)) { throw 'Editor PlayMode tests did not complete successfully.' }
                $run = ([xml](Get-Content -LiteralPath $report -Raw)).'test-run'
                if ([int]$run.total -le 0 -or [int]$run.failed -ne 0 -or [int]$run.inconclusive -ne 0) { throw 'PlayMode tests failed or were inconclusive. Inspect the NUnit report.' }
                Copy-Item -LiteralPath $report -Destination (Join-Path $results 'playmode-nunit.xml') -Force
                Write-Output "PlayMode: $($run.passed)/$($run.total) passed; $($run.skipped) skipped."
            } finally {
                if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit(10000) | Out-Null }
                $process.Dispose()
            }
        } else {
            Invoke-Checked 'unity' @('test', $ViewerProject, '--editor-version', $ViewerEditorVersion,
                '--mode', 'PlayMode', '--report-format', 'junit', '--output', (Join-Path $results 'playmode.xml'), '--timeout', '1800', '--', '--no-tracker')
        }
    } finally { $env:HEADTRACKED_CALIBRATION_DIRECTORY = $previousCalibration }
}
