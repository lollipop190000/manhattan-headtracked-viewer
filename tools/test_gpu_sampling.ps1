Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'gpu_sampling.ps1')
$samples = @(Convert-NvidiaGpuSamples -SampleUtc '2026-10-10T00:00:00.0000000Z' -Lines @(
    '2026/10/10 09:00:00.000, 0, NVIDIA GeForce RTX 4070 Laptop GPU, 8188, 2345, 75, 591.74, 41.5, P0',
    '2026/10/10 09:00:00.000, 1, Secondary GPU, 4096, 50, 0, 591.74, [N/A], P8'
))
if ($samples.Count -ne 2) { throw 'Multi-GPU rows were lost.' }
if ($samples[0].sampleUtc -ne '2026-10-10T00:00:00.0000000Z' -or $samples[0].driverTimestamp -ne '2026/10/10 09:00:00.000') { throw 'GPU sample timestamps differ.' }
if ($samples[0].memoryTotalMiB -ne '8188' -or $samples[0].memoryUsedMiB -ne '2345' -or $samples[0].gpuUtilizationPercent -ne '75') { throw 'GPU memory/utilization fields differ.' }
if ($samples[0].powerDrawWatts -ne '41.5' -or $null -ne $samples[1].powerDrawWatts) { throw 'Invariant decimal/N/A parsing differs.' }
$roundTrip = @($samples | ConvertTo-Csv -NoTypeInformation | ConvertFrom-Csv)
if ($roundTrip.Count -ne 2 -or $roundTrip[0].gpuName -ne 'NVIDIA GeForce RTX 4070 Laptop GPU') { throw 'GPU CSV round trip failed.' }
Write-Output 'GPU sample parser: 5 assertions passed.'
