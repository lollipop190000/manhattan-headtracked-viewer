param([string]$PythonExe = 'python', [string]$SourceRoot)
. (Join-Path $PSScriptRoot 'common.ps1')
if (-not $SourceRoot) { $SourceRoot = Join-Path $ViewerRepository '3d_modeling' }
$SourceRoot = (Resolve-Path -LiteralPath $SourceRoot).Path
Invoke-Checked $PythonExe @((Join-Path $PSScriptRoot 'source_assets.py'), 'verify', '--extracted', '--source-root', $SourceRoot)
$previousSource = $env:MANHATTAN_SOURCE_ROOT
try {
    $env:MANHATTAN_SOURCE_ROOT = $SourceRoot
    Invoke-ViewerUnityMethod 'Manhattan.Editor.CityBake.BuildAll' 7200
}
finally { $env:MANHATTAN_SOURCE_ROOT = $previousSource }
Invoke-Checked $PythonExe @((Join-Path $PSScriptRoot 'catalog_validation.py'), '--require-full')
