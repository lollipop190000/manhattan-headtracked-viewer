param(
    [ValidateRange(1, 86400)][int]$DurationSec = 1200,
    [ValidateRange(500, 60000)][int]$IntervalMs = 1000,
    [Parameter(Mandatory)][string]$OutputPath,
    [string]$StopFile
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'gpu_sampling.ps1')
$nvidia = Get-Command 'nvidia-smi' -ErrorAction Stop
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $OutputPath) { throw 'GPU output already exists; choose a new CSV to avoid mixing runs.' }
New-Item -ItemType Directory -Path (Split-Path -Parent $OutputPath) -Force | Out-Null
$writer = New-Object System.IO.StreamWriter($OutputPath, $false, (New-Object System.Text.UTF8Encoding($false)))
$watch = [System.Diagnostics.Stopwatch]::StartNew()
$headerWritten = $false
try {
    while ($watch.Elapsed.TotalSeconds -lt $DurationSec) {
        if ($StopFile -and (Test-Path -LiteralPath $StopFile)) { break }
        $query = & $nvidia.Source '--query-gpu=timestamp,index,name,memory.total,memory.used,utilization.gpu,driver_version,power.draw,pstate' '--format=csv,noheader,nounits'
        if ($LASTEXITCODE -ne 0) { throw "nvidia-smi query failed with exit code $LASTEXITCODE" }
        $sampleUtc = [DateTime]::UtcNow.ToString('o')
        $samples = @(Convert-NvidiaGpuSamples -Lines $query -SampleUtc $sampleUtc)
        if ($samples.Count -eq 0) { throw 'nvidia-smi returned no GPU samples.' }
        $csv = @($samples | ConvertTo-Csv -NoTypeInformation)
        if (-not $headerWritten) { $writer.WriteLine($csv[0]); $headerWritten = $true }
        for ($index = 1; $index -lt $csv.Count; $index++) { $writer.WriteLine($csv[$index]) }
        $writer.Flush()
        $remaining = [int][Math]::Max(0, ($DurationSec - $watch.Elapsed.TotalSeconds) * 1000)
        if ($remaining -gt 0) { Start-Sleep -Milliseconds ([Math]::Min($IntervalMs, $remaining)) }
    }
}
finally { $writer.Dispose(); $watch.Stop() }
Write-Output "GPU samples: $OutputPath"
