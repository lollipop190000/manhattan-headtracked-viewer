param(
    [string]$Revision = 'main',
    [string]$PythonExe = 'python',
    [string]$EditorPath,
    [string]$SourceArchiveDirectory,
    [switch]$StaticOnly,
    [switch]$NoTracker,
    [switch]$Run,
    [ValidateRange(0, 20)][int]$Camera = 0
)
. (Join-Path $PSScriptRoot 'common.ps1')
$manifest = Get-Content -LiteralPath (Join-Path $ViewerRepository 'assets\source-manifest.json') -Raw | ConvertFrom-Json
$verificationRoot = Join-Path $ViewerRepository '.verification'
New-Item -ItemType Directory -Path $verificationRoot -Force | Out-Null
$clone = Join-Path $verificationRoot ('clone-' + [guid]::NewGuid().ToString('N'))
Invoke-Checked 'gh' @('repo', 'clone', $manifest.releaseRepository, $clone)
Invoke-Checked 'git' @('-C', $clone, 'checkout', '--detach', $Revision)
Invoke-Checked 'git' @('-C', $clone, 'lfs', 'install', '--local')
Invoke-Checked 'git' @('-C', $clone, 'lfs', 'pull')
$actualRevision = & git -C $clone rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Clean clone commit could not be read.' }
$cloneManifestPath = Join-Path $clone 'assets/source-manifest.json'
$cloneManifest = Get-Content -LiteralPath $cloneManifestPath -Raw | ConvertFrom-Json
$cloneTechnology = Get-Content -LiteralPath (Join-Path $clone 'tools/technology-version.json') -Raw | ConvertFrom-Json
Invoke-Checked $PythonExe @((Join-Path $clone 'tools\check_repository.py'))
Invoke-Checked 'powershell' @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File',
    (Join-Path $clone 'tools\test.ps1'), '-PythonExe', $PythonExe, '-DataOnly')
if (-not $StaticOnly) {
    $setupParameters = @{ PythonExe = $PythonExe; SkipTracker = $NoTracker }
    if ($EditorPath) { $setupParameters.EditorPath = $EditorPath }
    if ($SourceArchiveDirectory) { $setupParameters.SourceArchiveDirectory = $SourceArchiveDirectory }
    & (Join-Path $clone 'tools\setup.ps1') @setupParameters
    & (Join-Path $clone 'tools\bake.ps1') -PythonExe $PythonExe
    & (Join-Path $clone 'tools\test.ps1') -PythonExe $PythonExe
    & (Join-Path $clone 'tools\build.ps1') -PythonExe $PythonExe -EditorPath $EditorPath
    if ($Run) { & (Join-Path $clone 'tools\run.ps1') -Camera $Camera -NoTracker:$NoTracker }
}
$player = Join-Path $clone 'Builds\ManhattanViewer\ManhattanViewer.exe'
$buildHash = if (Test-Path -LiteralPath $player) { (Get-FileHash -LiteralPath $player -Algorithm SHA256).Hash.ToLowerInvariant() } else { $null }
$record = @{ commit = $actualRevision.Trim(); sourceVersion = $cloneManifest.sourceVersion;
    sourceReleaseRepository = $cloneManifest.releaseRepository; sourceReleaseTag = $cloneManifest.releaseTag;
    sourceManifestSha256 = (Get-FileHash -LiteralPath $cloneManifestPath -Algorithm SHA256).Hash.ToLowerInvariant();
    sourceMode = $(if ($StaticOnly) { 'not-downloaded' } elseif ($SourceArchiveDirectory) { 'explicit-offline-archives' } else { 'authenticated-private-release' });
    technologyRevision = $cloneTechnology.revision;
    trackerRequirementsSha256 = (Get-FileHash -LiteralPath (Join-Path $clone 'tools/tracker-requirements.lock.txt') -Algorithm SHA256).Hash.ToLowerInvariant();
    completedUtc = [DateTime]::UtcNow.ToString('o'); staticOnly = [bool]$StaticOnly;
    trackerPrepared = (-not $StaticOnly -and -not $NoTracker); playerLaunched = (-not $StaticOnly -and [bool]$Run);
    playerSha256 = $buildHash; cloneDirectory = $clone }
$record | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $verificationRoot ('verification-' + $actualRevision.Trim() + '.json')) -Encoding UTF8
Write-Host "Clean clone verified at $($actualRevision.Trim()): $clone"
if ($Run) { Write-Host 'Record observed webcam/head movement checks separately in docs/validation.md.' }
