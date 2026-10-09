Set-StrictMode -Version Latest

function Convert-GpuMetric {
    param([string]$Text)
    $value = 0.0
    if ([double]::TryParse($Text.Trim(), [Globalization.NumberStyles]::Float,
        [Globalization.CultureInfo]::InvariantCulture, [ref]$value)) {
        return $value.ToString('0.###', [Globalization.CultureInfo]::InvariantCulture)
    }
    return $null
}

function Convert-NvidiaGpuSamples {
    param([string[]]$Lines, [string]$SampleUtc)
    $headers = @('driverTimestamp', 'gpuIndex', 'gpuName', 'memoryTotalMiB', 'memoryUsedMiB',
        'gpuUtilizationPercent', 'driverVersion', 'powerDrawWatts', 'pstate')
    foreach ($line in $Lines) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $sample = $line | ConvertFrom-Csv -Header $headers
        [PSCustomObject][ordered]@{
            sampleUtc = $SampleUtc
            driverTimestamp = $sample.driverTimestamp.Trim()
            gpuIndex = $sample.gpuIndex.Trim()
            gpuName = $sample.gpuName.Trim()
            memoryTotalMiB = Convert-GpuMetric $sample.memoryTotalMiB
            memoryUsedMiB = Convert-GpuMetric $sample.memoryUsedMiB
            gpuUtilizationPercent = Convert-GpuMetric $sample.gpuUtilizationPercent
            driverVersion = $sample.driverVersion.Trim()
            powerDrawWatts = Convert-GpuMetric $sample.powerDrawWatts
            pstate = $sample.pstate.Trim()
        }
    }
}
