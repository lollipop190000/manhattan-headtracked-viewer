param([string]$PythonExe = 'python', [switch]$DataOnly)
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
    Invoke-Checked 'unity' @('test', $ViewerProject, '--editor-version', $ViewerEditorVersion,
        '--mode', 'EditMode', '--report-format', 'junit', '--output', (Join-Path $results 'editmode.xml'), '--timeout', '1800')
    Invoke-Checked 'unity' @('test', $ViewerProject, '--editor-version', $ViewerEditorVersion,
        '--mode', 'PlayMode', '--report-format', 'junit', '--output', (Join-Path $results 'playmode.xml'), '--timeout', '1800')
}
