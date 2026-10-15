param([string]$OutputPath, [switch]$Bake, [switch]$AllowDirty, [string]$PythonExe = 'python', [string]$EditorPath,
    [ValidateSet('Full', 'Development')][string]$CityProfile = 'Full', [string]$RegionConfig)
. (Join-Path $PSScriptRoot 'common.ps1')
function Assert-CleanBuildSource {
    param([Parameter(Mandatory)][string]$RepositoryRoot, [switch]$AllowDirty)
    Get-Command git -ErrorAction Stop | Out-Null
    $previousErrorAction = $ErrorActionPreference
    try {
        # PowerShell 5.1 treats native stderr as an error; inspect Git's actual exit code below.
        $ErrorActionPreference = 'Continue'
        $sourceChanges = @(& git -C $RepositoryRoot status --porcelain=v1 --untracked-files=normal 2>$null)
        $statusExitCode = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previousErrorAction }
    if ($statusExitCode -ne 0) { throw "Git source-status check failed (exit code $statusExitCode); a build requires a valid Git checkout." }
    if ($sourceChanges.Count -gt 0 -and -not $AllowDirty) {
        throw 'The build source has tracked or untracked changes. Commit or remove them, or pass -AllowDirty for a deliberately dirty development build.'
    }
}
if ($Bake) { & (Join-Path $PSScriptRoot 'bake.ps1') -PythonExe $PythonExe }
Invoke-Checked $PythonExe @((Join-Path $PSScriptRoot 'build_preflight.py'), '--repository-root', $ViewerRepository)
Assert-CleanBuildSource -RepositoryRoot $ViewerRepository -AllowDirty:$AllowDirty
if (-not $OutputPath) { $OutputPath = Join-Path $ViewerRepository 'Builds\ManhattanViewer\ManhattanViewer.exe' }
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Path (Split-Path -Parent $OutputPath) -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $ViewerRepository 'Logs') -Force | Out-Null
$previousBuild = $env:MANHATTAN_BUILD_PATH
$previousProfile = $env:MANHATTAN_CITY_PROFILE
$previousRegion = $env:MANHATTAN_CITY_REGION
try {
    $env:MANHATTAN_BUILD_PATH = $OutputPath
    $env:MANHATTAN_CITY_PROFILE = $CityProfile
    $env:MANHATTAN_CITY_REGION = if ($RegionConfig) { [System.IO.Path]::GetFullPath($RegionConfig) } else { '' }
    Invoke-ViewerOwnedEditorMethod -Method 'Manhattan.Editor.BuildAutomation.BuildWindows' -EditorPath $EditorPath -Timeout 7200 -BuildTarget 'StandaloneWindows64'
}
finally { $env:MANHATTAN_BUILD_PATH = $previousBuild; $env:MANHATTAN_CITY_PROFILE = $previousProfile; $env:MANHATTAN_CITY_REGION = $previousRegion }
if (-not (Test-Path -LiteralPath $OutputPath)) { throw "Build reported success without producing $OutputPath" }
Invoke-Checked $PythonExe @((Join-Path $PSScriptRoot 'package_notices.py'), '--build-directory', (Split-Path -Parent $OutputPath))
Write-Host "Windows player: $OutputPath"
