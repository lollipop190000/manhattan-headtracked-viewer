Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ViewerRepository = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$ViewerProject = Join-Path $ViewerRepository 'ManhattanViewer'
$ViewerEditorVersion = '6000.3.22f1'

function Invoke-Checked {
    param([Parameter(Mandatory)][string]$Command, [string[]]$Arguments = @())
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Command failed with exit code $LASTEXITCODE" }
}

function Get-TechnologyVersion {
    $version = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'technology-version.json') -Raw | ConvertFrom-Json
    if ($version.revision -notmatch '^[a-f0-9]{40}$') {
        throw 'The technology revision is not pinned. Update tools/technology-version.json after package validation.'
    }
    return $version
}

function Invoke-ViewerUnityMethod {
    param([Parameter(Mandatory)][string]$Method, [int]$Timeout = 3600)
    $logDirectory = Join-Path $ViewerRepository 'Logs'
    New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
    $logPath = Join-Path $logDirectory ($Method.Replace('.', '-') + '.log')
    Invoke-Checked 'unity' @('run', $ViewerProject, '--editor-version', $ViewerEditorVersion,
        '--timeout', [string]$Timeout, '--', '-executeMethod', $Method, '-logFile', $logPath)
}

function Resolve-ViewerEditor {
    param([string]$EditorPath)
    if (-not $EditorPath) {
        $inventoryText = & unity editors --installed --format json
        if ($LASTEXITCODE -ne 0) { throw 'Unable to locate the pinned Unity Editor. Pass -EditorPath.' }
        $inventory = $inventoryText | ConvertFrom-Json
        if (-not $inventory.success) { throw 'Unable to locate the pinned Unity Editor. Pass -EditorPath.' }
        $editor = @($inventory.data | Where-Object { $_.version -eq $ViewerEditorVersion }) | Select-Object -First 1
        if ($editor -and $editor.location) {
            $EditorPath = if ($editor.location.EndsWith('Unity.exe', [StringComparison]::OrdinalIgnoreCase)) {
                $editor.location
            } else { Join-Path $editor.location 'Editor\Unity.exe' }
        }
    }
    if (-not $EditorPath -or -not (Test-Path -LiteralPath $EditorPath -PathType Leaf)) {
        throw "Unity $ViewerEditorVersion executable could not be found. Pass -EditorPath."
    }
    $EditorPath = (Resolve-Path -LiteralPath $EditorPath).Path
    $productVersion = (Get-Item -LiteralPath $EditorPath).VersionInfo.ProductVersion
    if (-not $productVersion -or ($productVersion.Split('_')[0] -ne $ViewerEditorVersion)) {
        throw "The Editor executable must be Unity $ViewerEditorVersion."
    }
    return $EditorPath
}

function Invoke-ViewerOwnedEditorMethod {
    param(
        [Parameter(Mandatory)][string]$Method,
        [string]$EditorPath,
        [ValidateRange(1, 86400)][int]$Timeout = 7200,
        [string]$BuildTarget
    )
    $EditorPath = Resolve-ViewerEditor -EditorPath $EditorPath
    $logDirectory = Join-Path $ViewerRepository 'Logs'
    New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
    $logPath = Join-Path $logDirectory ($Method.Replace('.', '-') + '-' + [Guid]::NewGuid().ToString('N') + '.log')
    # Async entrypoints own EditorApplication.Exit after their awaited work.
    # -quit would exit at the first incomplete await and truncate generation.
    $arguments = @('-batchmode', '-projectPath', ('"' + $ViewerProject + '"'),
        '-executeMethod', $Method, '-logFile', ('"' + $logPath + '"'))
    if ($BuildTarget) { $arguments += @('-buildTarget', $BuildTarget) }
    $process = $null
    try {
        $process = Start-Process -FilePath $EditorPath -ArgumentList $arguments -WorkingDirectory $ViewerProject -WindowStyle Hidden -PassThru
        [void]$process.Handle
        if (-not $process.WaitForExit($Timeout * 1000)) { throw "Owned Unity job timed out. See $logPath" }
        $process.Refresh()
        if ($process.ExitCode -ne 0) { throw "Unity job failed ($($process.ExitCode)). See $logPath" }
    }
    finally {
        if ($process) {
            if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit(10000) | Out-Null }
            $process.Dispose()
        }
    }
    Write-Host "Unity job completed. Log: $logPath"
}

function Invoke-ViewerPackageInstall {
    param([string]$EditorPath)
    # PackageManager.Client requests continue on EditorApplication.update.
    # unity run adds -quit; use an owned Editor process without -quit here.
    if (-not $EditorPath) {
        $inventoryText = & unity editors --installed --format json
        if ($LASTEXITCODE -eq 0) {
            $inventory = $inventoryText | ConvertFrom-Json
            if ($inventory.success) {
                $editor = @($inventory.data | Where-Object { $_.version -eq $ViewerEditorVersion }) | Select-Object -First 1
                if ($editor -and $editor.location) {
                    $EditorPath = if ($editor.location.EndsWith('Unity.exe', [StringComparison]::OrdinalIgnoreCase)) {
                        $editor.location
                    } else { Join-Path $editor.location 'Editor\Unity.exe' }
                }
            }
        }
        if (-not $EditorPath) {
            $candidates = @(
                (Join-Path $env:ProgramFiles "Unity\Hub\Editor\$ViewerEditorVersion\Editor\Unity.exe"),
                (Join-Path $env:LOCALAPPDATA "UnityEditors\$ViewerEditorVersion\Editor\Unity.exe")
            )
            $EditorPath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
        }
    }
    if (-not $EditorPath -or -not (Test-Path -LiteralPath $EditorPath)) {
        throw "Unity $ViewerEditorVersion executable could not be found. Pass -EditorPath to setup.ps1."
    }
    $EditorPath = (Resolve-Path -LiteralPath $EditorPath).Path
    $logDirectory = Join-Path $ViewerRepository 'Logs'
    New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
    $logPath = Join-Path $logDirectory 'install-packages.log'
    $process = Start-Process -FilePath $EditorPath -ArgumentList @('-batchmode', '-projectPath',
        ('"' + $ViewerProject + '"'), '-executeMethod', 'Manhattan.Editor.BuildAutomation.InstallPackages',
        '-logFile', ('"' + $logPath + '"')) -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(1800000)) {
        Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
        throw "Unity package installation timed out. See $logPath"
    }
    $process.Refresh()
    if ($process.ExitCode -ne 0) { throw "Unity package installation failed ($($process.ExitCode)). See $logPath" }
}
