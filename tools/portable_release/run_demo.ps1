param(
    [switch]$NoTracker, [switch]$Webcam, [switch]$Check, [switch]$ListCameras,
    [switch]$Verify, [switch]$InstallRuntime, [switch]$Stop,
    [ValidateRange(0,20)][int]$Camera = 0,
    [ValidateSet('dshow','msmf')][string]$CameraBackend = 'dshow',
    [string]$CalibrationDirectory, [string]$PlayerArguments
)
$ErrorActionPreference = 'Stop'
$releaseRoot = Split-Path -Parent $PSScriptRoot
$pythonExe = Join-Path $releaseRoot '.runtime\tracker\.venv\Scripts\python.exe'
$interactive = $PSBoundParameters.Count -eq 0
try {
    if (-not [Environment]::Is64BitOperatingSystem) { throw 'This release requires 64-bit Windows 10 or 11.' }
    if ($NoTracker -and $Webcam) { throw 'Choose either -NoTracker or -Webcam.' }
    if ($interactive) {
        Write-Host ''
        Write-Host 'Manhattan Viewer v0.1.1'
        Write-Host '  1  Explore without a webcam'
        Write-Host '  2  Use webcam head tracking'
        Write-Host '  3  Check setup and list webcams'
        Write-Host '  4  Install Microsoft runtime (for missing-DLL errors)'
        Write-Host '  5  Stop the viewer and tracker'
        Write-Host '  0  Exit'
        $choice = Read-Host 'Choose 0-5'
        switch ($choice) {
            '0' { exit 0 }
            '1' { $NoTracker = $true }
            '2' { $Webcam = $true }
            '3' { $Check = $true; $ListCameras = $true }
            '4' { $InstallRuntime = $true }
            '5' { $Stop = $true }
            default { throw 'Please choose a number from 0 to 5.' }
        }
    }
    if ($InstallRuntime) {
        # Download from Microsoft directly; no third-party installer is redistributed.
        $pin = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'portable-runtime.json') -Raw | ConvertFrom-Json).vcRuntime
        $cache = Join-Path $env:LOCALAPPDATA 'ManhattanViewer\Prerequisites'
        New-Item -ItemType Directory -Path $cache -Force | Out-Null
        $installer = Join-Path $cache $pin.filename
        Write-Host 'Downloading the Microsoft Visual C++ runtime...'
        Invoke-WebRequest -Uri $pin.url -OutFile $installer -UseBasicParsing
        if ((Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant() -ne $pin.sha256) {
            throw 'Microsoft runtime download failed its checksum check. Please retry.'
        }
        $signature = Get-AuthenticodeSignature -LiteralPath $installer
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') {
            throw 'Microsoft runtime signature could not be verified.'
        }
        Write-Host 'Follow the Microsoft installer, then run this launcher again.'
        $install = Start-Process -FilePath $installer -ArgumentList '/install /passive /norestart' -WindowStyle Hidden -PassThru -Wait
        if ($install.ExitCode -notin @(0,1638,3010)) { throw "Microsoft installer returned $($install.ExitCode)." }
        if ($install.ExitCode -eq 3010) { Write-Host 'Windows requests a restart before continuing.' }
        else { Write-Host 'Microsoft runtime is ready.' }
        exit 0
    }
    if ($Verify) {
        $receipt = Get-Content -LiteralPath (Join-Path $releaseRoot 'INTEGRITY.json') -Raw | ConvertFrom-Json
        foreach ($record in $receipt.files) {
            $path = [IO.Path]::GetFullPath((Join-Path $releaseRoot $record.path))
            if (-not $path.StartsWith($releaseRoot + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe integrity path.' }
            if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -ne $record.bytes -or
                (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $record.sha256) { throw "File is missing or changed: $($record.path). Extract the ZIP again." }
        }
        Write-Host "All $($receipt.fileCount) release files passed verification."
        exit 0
    }
    if ($Check -or $ListCameras -or (-not $NoTracker -and -not $Stop)) {
        if (-not (Test-Path -LiteralPath $pythonExe)) { throw 'Bundled tracker is missing. Extract the whole Windows ZIP again.' }
        $checkArguments = @('-B','-X','utf8',(Join-Path $PSScriptRoot 'portable_check.py'))
        if ($ListCameras) { $checkArguments += '--list-cameras' }
        Write-Host 'Checking the bundled webcam tracker...'
        $checkLog = Join-Path $releaseRoot 'tracker-check.log'
        $nativePreference = $ErrorActionPreference
        try {
            # Windows PowerShell 5.1 maps native stderr to error records. Native
            # MediaPipe informational warnings are not a failed model check.
            $ErrorActionPreference = 'Continue'
            & $pythonExe @checkArguments 2> $checkLog
            $checkExit = $LASTEXITCODE
        }
        finally { $ErrorActionPreference = $nativePreference }
        if ($checkExit -ne 0) { throw 'Webcam setup check failed. See tracker-check.log. For a missing DLL, choose Install Microsoft runtime; otherwise extract the ZIP again and check Windows camera permissions.' }
        if ($Check -or $ListCameras) {
            if ($interactive) { Read-Host 'Press Enter to close' | Out-Null }
            exit 0
        }
    }
    $launch = @{
        UnityProject = (Join-Path $releaseRoot '.runtime\viewer')
        PlayerPath = (Join-Path $releaseRoot 'ManhattanViewer.exe')
        TrackerRoot = (Join-Path $releaseRoot '.runtime\tracker')
        NoTracker = $NoTracker; Stop = $Stop; CameraBackend = $CameraBackend
    }
    if ($PSBoundParameters.ContainsKey('Camera')) { $launch.Camera = $Camera }
    if ($CalibrationDirectory) { $launch.CalibrationDirectory = $CalibrationDirectory }
    if ($PlayerArguments) { $launch.PlayerArguments = $PlayerArguments }
    $previousCatalog = $env:MANHATTAN_OWNERSHIP_CATALOG_PATH
    try {
        $env:MANHATTAN_OWNERSHIP_CATALOG_PATH = $null
        & (Join-Path $releaseRoot '.runtime\technology\tools\run_demo.ps1') @launch
    }
    finally { $env:MANHATTAN_OWNERSHIP_CATALOG_PATH = $previousCatalog }
}
catch {
    Write-Host ''
    Write-Host ('Could not start: ' + $_.Exception.Message) -ForegroundColor Red
    Write-Host 'See README-Windows.md. For webcam errors, close other camera apps, allow desktop camera access, or use -NoTracker.'
    exit 1
}
