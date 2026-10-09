param(
    [string]$SourceArchiveDirectory,
    [string]$PythonExe = 'python',
    [string]$EditorPath,
    [switch]$SkipTracker,
    [switch]$SkipUnity,
    [switch]$SkipSource
)
. (Join-Path $PSScriptRoot 'common.ps1')
$version = Get-TechnologyVersion
$technologyRoot = Join-Path $ViewerRepository '.tools\head-tracked-display'
$sourceRoot = Join-Path $ViewerRepository '3d_modeling'
$manifestPath = Join-Path $ViewerRepository 'assets\source-manifest.json'

Invoke-Checked $PythonExe @((Join-Path $PSScriptRoot 'source_assets.py'), 'validate')
if (-not $SkipSource) {
    if ($SourceArchiveDirectory) {
        $SourceArchiveDirectory = (Resolve-Path -LiteralPath $SourceArchiveDirectory).Path
    }
    else {
        $SourceArchiveDirectory = Join-Path $ViewerRepository '.cache\source-downloads'
        New-Item -ItemType Directory -Path $SourceArchiveDirectory -Force | Out-Null
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        Invoke-Checked 'gh' @('auth', 'status')
        foreach ($pack in $manifest.packs) {
            $archive = Join-Path $SourceArchiveDirectory $pack.asset
            if (-not (Test-Path -LiteralPath $archive)) {
                Invoke-Checked 'gh' @('release', 'download', $manifest.releaseTag, '--repo',
                    $manifest.releaseRepository, '--pattern', $pack.asset, '--dir', $SourceArchiveDirectory)
            }
        }
    }
    Invoke-Checked $PythonExe @((Join-Path $PSScriptRoot 'source_assets.py'), 'extract',
        '--archive-root', $SourceArchiveDirectory, '--source-root', $sourceRoot)
}

$newTechnologyCheckout = $false
if (-not (Test-Path -LiteralPath (Join-Path $technologyRoot '.git'))) {
    if (Test-Path -LiteralPath $technologyRoot) { throw "Existing non-Git folder: $technologyRoot" }
    New-Item -ItemType Directory -Path (Split-Path -Parent $technologyRoot) -Force | Out-Null
    Invoke-Checked 'git' @('clone', '--filter=blob:none', '--no-checkout', $version.repository, $technologyRoot)
    $newTechnologyCheckout = $true
}
$remote = & git -C $technologyRoot remote get-url origin
if ($LASTEXITCODE -ne 0 -or $remote.Trim() -ne $version.repository) { throw 'Technology checkout origin does not match the pinned repository.' }
if (-not $newTechnologyCheckout) {
    $changes = & git -C $technologyRoot status --porcelain
    if ($LASTEXITCODE -ne 0 -or $changes) { throw 'The local technology checkout has changes. Preserve them before updating it.' }
}
Invoke-Checked 'git' @('-C', $technologyRoot, 'fetch', 'origin', $version.revision)
Invoke-Checked 'git' @('-C', $technologyRoot, 'checkout', '--detach', $version.revision)
$actual = & git -C $technologyRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $actual.Trim() -ne $version.revision) { throw 'Technology checkout revision verification failed.' }

if (-not $SkipTracker) {
    $previousTemp = $env:TEMP
    $previousTmp = $env:TMP
    $pythonScratch = Join-Path $ViewerRepository '.cache\python-temp'
    New-Item -ItemType Directory -Path $pythonScratch -Force | Out-Null
    try {
    $env:TEMP = $pythonScratch
    $env:TMP = $pythonScratch
    $actualPythonVersion = & $PythonExe -c 'import platform; print(platform.python_version())'
    if ($LASTEXITCODE -ne 0 -or $actualPythonVersion.Trim() -ne $version.pythonVersion) {
        throw "Use Python $($version.pythonVersion). Pass -PythonExe with that Python executable."
    }
    $trackerRoot = Join-Path $technologyRoot 'python_tracker'
    $venvPython = Join-Path $trackerRoot '.venv\Scripts\python.exe'
    if (-not (Test-Path -LiteralPath $venvPython)) {
        Invoke-Checked $PythonExe @('-m', 'venv', (Join-Path $trackerRoot '.venv'))
    }
    $requirements = Join-Path $PSScriptRoot 'tracker-requirements.lock.txt'
    if (-not (Test-Path -LiteralPath $requirements)) {
        throw 'The tested tracker requirements lock is missing. Restore it from this viewer revision.'
    }
    Invoke-Checked $venvPython @('-m', 'pip', 'install', '--disable-pip-version-check', '-r', $requirements)
    Invoke-Checked $venvPython @('-m', 'pip', 'check')
    Invoke-Checked $venvPython @((Join-Path $trackerRoot 'download_model.py'))
    Invoke-Checked $venvPython @('-m', 'unittest', 'discover', '-s', $trackerRoot, '-p', 'test_*.py')
    Invoke-Checked $venvPython @((Join-Path $PSScriptRoot 'test_tracker.py'), '--model', (Join-Path $trackerRoot 'face_landmarker.task'))
    }
    finally { $env:TEMP = $previousTemp; $env:TMP = $previousTmp }
}

if (-not $SkipUnity) {
    $previousRevision = $env:MANHATTAN_TECHNOLOGY_REVISION
    try {
        $env:MANHATTAN_TECHNOLOGY_REVISION = $version.revision
        Invoke-ViewerPackageInstall $EditorPath
    }
    finally { $env:MANHATTAN_TECHNOLOGY_REVISION = $previousRevision }
}
Write-Host 'Setup complete. Run tools/bake.ps1, then tools/build.ps1.'
