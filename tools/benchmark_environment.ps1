function Convert-EnvironmentGpuMetric {
    param([string]$Text)
    $metric = Convert-GpuMetric $Text
    if ($null -eq $metric) { return $null }
    return [double]::Parse($metric, [Globalization.CultureInfo]::InvariantCulture)
}

function Convert-NvidiaEnvironmentSamples {
    param([string[]]$Lines)
    $columns = @('gpuIndex', 'gpuName', 'memoryTotalMiB', 'driverVersion', 'powerLimitWatts',
        'enforcedPowerLimitWatts', 'defaultPowerLimitWatts', 'minimumPowerLimitWatts', 'maximumPowerLimitWatts')
    foreach ($line in $Lines) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $row = $line | ConvertFrom-Csv -Header $columns
        $gpu = [ordered]@{ gpuIndex = [int]$row.gpuIndex.Trim(); gpuName = $row.gpuName.Trim();
            memoryTotalMiB = Convert-EnvironmentGpuMetric $row.memoryTotalMiB; driverVersion = $row.driverVersion.Trim() }
        foreach ($field in @('powerLimitWatts', 'enforcedPowerLimitWatts', 'defaultPowerLimitWatts', 'minimumPowerLimitWatts', 'maximumPowerLimitWatts')) {
            $gpu[$field] = Convert-EnvironmentGpuMetric $row.$field
            $gpu[$field.Replace('Watts', 'Available')] = ($null -ne $gpu[$field])
        }
        [pscustomobject]$gpu
    }
}

function Get-BenchmarkDesktopMode {
    # EnumDisplaySettingsW returns physical pixels, independent of DPI scaling:
    # https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumdisplaysettingsw
    if (-not ('ManhattanBenchmarkHardwareV1' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ManhattanBenchmarkHardwareV1 {
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EnumDisplaySettingsW(string deviceName, int mode, IntPtr devMode);
    public static int[] DesktopMode() {
        // DEVMODEW is 220 bytes. Offsets follow the public Win32 structure.
        IntPtr data = Marshal.AllocHGlobal(220);
        try {
            for (int index = 0; index < 220; index++) Marshal.WriteByte(data, index, 0);
            Marshal.WriteInt16(data, 68, 220); // dmSize
            if (!EnumDisplaySettingsW(null, -1, data)) return null; // ENUM_CURRENT_SETTINGS
            return new[] { Marshal.ReadInt32(data, 172), Marshal.ReadInt32(data, 176), Marshal.ReadInt32(data, 184) };
        } finally { Marshal.FreeHGlobal(data); }
    }
}
'@
    }
    $mode = [ManhattanBenchmarkHardwareV1]::DesktopMode()
    if (-not $mode -or $mode[0] -le 0 -or $mode[1] -le 0) { throw 'Windows did not return a current desktop display mode.' }
    $frequency = if ($mode[2] -gt 1) { $mode[2] } else { $null }
    return [ordered]@{ width = $mode[0]; height = $mode[1]; refreshHz = $frequency;
        refreshHzAvailable = ($null -ne $frequency); source = 'EnumDisplaySettingsW/currentSettings/physicalPixels' }
}

function Get-BenchmarkEnvironment {
    $diagnostics = [System.Collections.Generic.List[string]]::new()
    $desktop = $null
    try { $desktop = Get-BenchmarkDesktopMode }
    catch { $diagnostics.Add('Desktop display mode unavailable: ' + $_.Exception.Message) }
    $acOnline = $null
    $acStatus = 'Unknown'
    try {
        Add-Type -AssemblyName System.Windows.Forms
        $acStatus = [System.Windows.Forms.SystemInformation]::PowerStatus.PowerLineStatus.ToString()
        if ($acStatus -eq 'Online') { $acOnline = $true }
        elseif ($acStatus -eq 'Offline') { $acOnline = $false }
    }
    catch { $diagnostics.Add('AC status unavailable: ' + $_.Exception.Message) }
    $gpus = @()
    $nvidia = Get-Command 'nvidia-smi' -ErrorAction SilentlyContinue
    if ($nvidia) {
        try {
            $query = & $nvidia.Source '--query-gpu=index,name,memory.total,driver_version,power.limit,enforced.power.limit,power.default_limit,power.min_limit,power.max_limit' '--format=csv,noheader,nounits'
            if ($LASTEXITCODE -ne 0) { throw "nvidia-smi environment query failed with exit code $LASTEXITCODE" }
            $gpus = @(Convert-NvidiaEnvironmentSamples -Lines $query)
        }
        catch {
            $diagnostics.Add('NVIDIA power-limit query unavailable: ' + $_.Exception.Message)
            try {
                $query = & $nvidia.Source '--query-gpu=index,name,memory.total,driver_version' '--format=csv,noheader,nounits'
                if ($LASTEXITCODE -ne 0) { throw "nvidia-smi identity query failed with exit code $LASTEXITCODE" }
                $gpus = @(Convert-NvidiaEnvironmentSamples -Lines @($query | ForEach-Object { $_ + ', N/A, N/A, N/A, N/A, N/A' }))
            }
            catch { $diagnostics.Add('NVIDIA identity query unavailable: ' + $_.Exception.Message) }
        }
    }
    else { $diagnostics.Add('nvidia-smi unavailable.') }
    return [ordered]@{ schemaVersion = 1; capturedUtc = [DateTime]::UtcNow.ToString('o');
        requestedRenderResolution = [ordered]@{ width = 1920; height = 1200; fullscreen = $true; windowMode = 'exclusive' };
        prelaunchDesktop = $desktop; acOnline = $acOnline; acStatus = $acStatus;
        gpus = $gpus; diagnostics = @($diagnostics.ToArray());
        note = 'Requested resolution must be checked against benchmark.json runtime Screen.width/height. Desktop mode is captured before exclusive fullscreen. Power-limit values are exposed driver readings, not proof of a fixed laptop performance profile.' }
}
