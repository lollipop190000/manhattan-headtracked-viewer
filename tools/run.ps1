param(
    [ValidateRange(0, 20)][int]$Camera = 0,
    [switch]$NoTracker,
    [switch]$Stop,
    [string]$PlayerPath,
    [string]$PlayerArguments,
    [string]$CalibrationDirectory
)
. (Join-Path $PSScriptRoot 'common.ps1')
if (-not $PlayerPath) { $PlayerPath = Join-Path $ViewerRepository 'Builds\ManhattanViewer\ManhattanViewer.exe' }
$technologyRoot = Join-Path $ViewerRepository '.tools\head-tracked-display'
$launcher = Join-Path $technologyRoot 'Run-HeadTrackedDemo.cmd'
if (-not (Test-Path -LiteralPath $launcher)) { throw 'Run tools/setup.ps1 first to prepare the pinned technology launcher.' }
$arguments = @('-UnityProject', $ViewerProject, '-PlayerPath', $PlayerPath,
    '-TrackerRoot', (Join-Path $technologyRoot 'python_tracker'), '-Camera', [string]$Camera)
if ($NoTracker) { $arguments += '-NoTracker' }
if ($Stop) { $arguments += '-Stop' }
if ($CalibrationDirectory) { $arguments += @('-CalibrationDirectory', $CalibrationDirectory) }
if ($PlayerArguments) { $arguments += @('-PlayerArguments', $PlayerArguments) }
# One CMD entry point is maintained by the technology package.
# For a quoted player argument string use the same backing PowerShell script,
# avoiding a second CMD parsing layer for paths containing shell characters.
if ($PlayerArguments) {
    $parameters = @{ UnityProject = $ViewerProject; PlayerPath = $PlayerPath;
        TrackerRoot = (Join-Path $technologyRoot 'python_tracker'); Camera = $Camera;
        NoTracker = $NoTracker; Stop = $Stop; PlayerArguments = $PlayerArguments }
    if ($CalibrationDirectory) { $parameters.CalibrationDirectory = $CalibrationDirectory }
    & (Join-Path $technologyRoot 'tools\run_demo.ps1') @parameters
}
else {
    & $launcher @arguments
    if ($LASTEXITCODE -ne 0) { throw "Viewer launcher failed with exit code $LASTEXITCODE" }
}
