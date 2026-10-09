param([string]$OutputPath, [switch]$Bake, [switch]$AllowDirty, [string]$PythonExe = 'python')
. (Join-Path $PSScriptRoot 'common.ps1')
if ($Bake) { & (Join-Path $PSScriptRoot 'bake.ps1') -PythonExe $PythonExe }
if (-not $OutputPath) { $OutputPath = Join-Path $ViewerRepository 'Builds\ManhattanViewer\ManhattanViewer.exe' }
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Path (Split-Path -Parent $OutputPath) -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $ViewerRepository 'Logs') -Force | Out-Null
$previousBuild = $env:MANHATTAN_BUILD_PATH
try {
    $env:MANHATTAN_BUILD_PATH = $OutputPath
    $arguments = @('build', $ViewerProject, '--editor-version', $ViewerEditorVersion,
        '--target', 'StandaloneWindows64', '--execute-method', 'Manhattan.Editor.BuildAutomation.BuildWindows',
        '--output-path', $OutputPath, '--timeout', '7200')
    if ($AllowDirty) { $arguments += '--allow-dirty-build' }
    Invoke-Checked 'unity' $arguments
}
finally { $env:MANHATTAN_BUILD_PATH = $previousBuild }
if (-not (Test-Path -LiteralPath $OutputPath)) { throw "Build reported success without producing $OutputPath" }
Invoke-Checked $PythonExe @((Join-Path $PSScriptRoot 'package_notices.py'), '--build-directory', (Split-Path -Parent $OutputPath))
Write-Host "Windows player: $OutputPath"
