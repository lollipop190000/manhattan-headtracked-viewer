Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'gpu_sampling.ps1')
. (Join-Path $PSScriptRoot 'benchmark_environment.ps1')
$gpus = @(Convert-NvidiaEnvironmentSamples -Lines @(
    '0, NVIDIA GeForce RTX 4070 Laptop GPU, 8188, 591.74, [N/A], 71.57, 55.00, 5.00, 80.00',
    '1, Synthetic GPU, 4096, 600.00, 65.00, N/A, 50.00, 10.00, 70.00'))
if ($gpus.Count -ne 2 -or $gpus[0].memoryTotalMiB -ne 8188 -or $gpus[0].driverVersion -ne '591.74') { throw 'GPU identity parsing failed.' }
if ($gpus[0].powerLimitAvailable -or $null -ne $gpus[0].powerLimitWatts -or -not $gpus[0].enforcedPowerLimitAvailable -or $gpus[0].enforcedPowerLimitWatts -ne 71.57) { throw 'Unsupported power-limit availability was not preserved.' }
$roundTrip = ConvertFrom-Json -InputObject (ConvertTo-Json -InputObject $gpus -Depth 5)
if (-not $roundTrip[1].powerLimitAvailable -or $roundTrip[1].powerLimitWatts -ne 65 -or $roundTrip[1].enforcedPowerLimitAvailable) { throw 'Environment JSON round trip failed.' }
Write-Host 'Benchmark environment parser: 3 assertions passed.'
