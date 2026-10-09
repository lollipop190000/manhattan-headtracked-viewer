import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
SHELL = shutil.which("powershell.exe") or shutil.which("pwsh")
SCRIPT = ROOT / "tools/measure_process.ps1"


@unittest.skipUnless(sys.platform == "win32" and SHELL, "Windows PowerShell process-counter tests")
class ProcessSamplingTests(unittest.TestCase):
    def powershell(self, code):
        script_path = str(SCRIPT).replace("'", "''")
        result = subprocess.run([SHELL, "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command",
                                 ". '" + script_path + "'; " + code], capture_output=True, text=True, encoding="utf-8", errors="replace", check=True, timeout=15)
        return json.loads(result.stdout.strip().splitlines()[-1])

    def test_exact_path_and_refresh_counters_only(self):
        sample = self.powershell(r"""
            $fake = [pscustomobject]@{ Id=42; HasExited=$false; Path='C:\City\ManhattanViewer.exe'; WorkingSet64=123456; PrivateMemorySize64=234567 }
            $fake | Add-Member ScriptMethod Refresh { $this.WorkingSet64 = 345678 }
            $valid = Get-ProcessMemorySample -Process $fake -ExpectedPlayerPath 'c:\city\ManhattanViewer.exe' -SampleUtc '2026-10-10T00:00:00Z'
            $other = Get-ProcessMemorySample -Process $fake -ExpectedPlayerPath 'C:\Unrelated\ManhattanViewer.exe'
            @{ valid=$valid; otherRejected=($null -eq $other) } | ConvertTo-Json -Compress
        """)
        self.assertTrue(sample["otherRejected"])
        self.assertEqual(42, sample["valid"]["pid"])
        self.assertEqual(345678, sample["valid"]["workingSetBytes"])
        self.assertEqual(234567, sample["valid"]["privateMemoryBytes"])
        self.assertEqual({"sampleUtc", "pid", "workingSetBytes", "privateMemoryBytes"}, set(sample["valid"]))

    def test_unknown_counters_are_empty_instead_of_zero(self):
        sample = self.powershell(r"""
            $fake = [pscustomobject]@{ Id=42; HasExited=$false; Path='C:\City\ManhattanViewer.exe'; WorkingSet64=0 }
            $fake | Add-Member ScriptMethod Refresh { }
            $fake | Add-Member ScriptProperty PrivateMemorySize64 { throw 'Counter unavailable' }
            Get-ProcessMemorySample -Process $fake -ExpectedPlayerPath 'C:\City\ManhattanViewer.exe' | ConvertTo-Json -Compress
        """)
        self.assertEqual(42, sample["pid"])
        self.assertIsNone(sample["workingSetBytes"])
        self.assertIsNone(sample["privateMemoryBytes"])

    def test_startup_path_filter_and_subsequent_pid_lookup(self):
        sample = self.powershell(r"""
            $wrong = [pscustomobject]@{ Id=99; HasExited=$false; Path='C:\Other\ManhattanViewer.exe'; WorkingSet64=9000; PrivateMemorySize64=9000 }
            $correct = [pscustomobject]@{ Id=42; HasExited=$false; Path='C:\City\ManhattanViewer.exe'; WorkingSet64=1000; PrivateMemorySize64=2000 }
            foreach($fake in @($wrong,$correct)) { $fake | Add-Member ScriptMethod Refresh { }; $fake | Add-Member ScriptMethod Dispose { } }
            $script:queries = @()
            function Get-Process {
                param([string]$Name, [int]$Id, [string]$ErrorAction)
                if ($PSBoundParameters.ContainsKey('Id')) { $script:queries += 'pid:' + $Id; return $correct }
                $script:queries += 'name:' + $Name; return @($wrong,$correct)
            }
            $first = Find-PlayerMemorySample -ExpectedPlayerPath 'C:\City\ManhattanViewer.exe' -PinnedProcessId $null
            $second = Find-PlayerMemorySample -ExpectedPlayerPath 'C:\City\ManhattanViewer.exe' -PinnedProcessId $first.pid
            @{ firstPid=$first.pid; secondPid=$second.pid; queries=$script:queries } | ConvertTo-Json -Compress
        """)
        self.assertEqual(42, sample["firstPid"])
        self.assertEqual(42, sample["secondPid"])
        self.assertEqual(["name:ManhattanViewer", "pid:42"], sample["queries"])

    def test_stop_signal_closes_csv_and_existing_results_are_preserved(self):
        temporary_root = ROOT / ".cache/tests"
        temporary_root.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(dir=temporary_root) as temporary:
            folder = Path(temporary)
            csv_path = folder / "process.csv"
            stop_path = folder / "stop"
            stop_path.write_text("", encoding="utf-8")
            output = str(csv_path).replace("'", "''")
            stop = str(stop_path).replace("'", "''")
            result = self.powershell(
                "Invoke-ProcessMemoryCollector -ExpectedPlayerPath 'C:\\City\\ManhattanViewer.exe' -OutputPath '" + output +
                "' -DurationSec 1 -StopFile '" + stop + "' | Out-Null; "
                "$stream = [IO.File]::Open('" + output + "', [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None); $stream.Dispose(); "
                "$rejected=$false; try { Invoke-ProcessMemoryCollector -ExpectedPlayerPath 'C:\\City\\ManhattanViewer.exe' -OutputPath '" + output +
                "' -DurationSec 1 -StopFile '" + stop + "' | Out-Null } catch { $rejected=$true }; @{ closed=$true; overwriteRejected=$rejected } | ConvertTo-Json -Compress")
            self.assertTrue(result["closed"])
            self.assertTrue(result["overwriteRejected"])
            self.assertEqual('"sampleUtc","pid","workingSetBytes","privateMemoryBytes"\n', csv_path.read_text(encoding="utf-8"))

    def test_descendant_chain_excludes_unrelated_processes_and_cycles(self):
        result = self.powershell(r"""
            $inventory = @(
                [pscustomobject]@{ProcessId=42;ParentProcessId=5},
                [pscustomobject]@{ProcessId=43;ParentProcessId=42},
                [pscustomobject]@{ProcessId=44;ParentProcessId=43},
                [pscustomobject]@{ProcessId=99;ParentProcessId=5},
                [pscustomobject]@{ProcessId=100;ParentProcessId=101},
                [pscustomobject]@{ProcessId=101;ParentProcessId=100})
            @(Get-OwnedProcessIds -RootProcessId 42 -Inventory $inventory) | ConvertTo-Json -Compress
        """)
        self.assertEqual([42, 43, 44], result)

    def test_tree_sum_unknown_counter_and_recycled_pid_identity(self):
        result = self.powershell(r"""
            $root = [pscustomobject]@{Id=42;HasExited=$false;Path='C:\Venv\python.exe';WorkingSet64=1000;PrivateMemorySize64=2000;StartTime=[datetime]'2026-10-10T00:00:00Z'}
            $child = [pscustomobject]@{Id=43;HasExited=$false;Path='C:\Python313\python.exe';WorkingSet64=3000;PrivateMemorySize64=4000;StartTime=[datetime]'2026-10-10T00:00:01Z'}
            foreach($fake in @($root,$child)) { $fake | Add-Member ScriptMethod Refresh { }; $fake | Add-Member ScriptMethod Dispose { } }
            function Get-Process { param([int]$Id,[string]$ErrorAction); if($Id -eq 42) { return $root }; if($Id -eq 43) { return $child }; throw 'Unexpected PID' }
            $inventory=@([pscustomobject]@{ProcessId=42;ParentProcessId=5},[pscustomobject]@{ProcessId=43;ParentProcessId=42})
            $snapshot=New-ProcessTreeSnapshot -RootProcessId 42 -Inventory $inventory
            $rootSample=Get-ProcessMemorySample -Process $root -ExpectedPlayerPath $root.Path
            $complete=Get-ProcessTreeMemorySample -RootSample $rootSample -Snapshot $snapshot
            $child.PrivateMemorySize64=0
            $unknown=Get-ProcessTreeMemorySample -RootSample $rootSample -Snapshot $snapshot
            $child.StartTime=[datetime]'2026-10-10T00:00:30Z'
            $recycled=Get-ProcessTreeMemorySample -RootSample $rootSample -Snapshot $snapshot
            @{complete=$complete;unknown=$unknown;recycled=$recycled} | ConvertTo-Json -Compress
        """)
        self.assertTrue(result["complete"]["treeCoverageVerified"])
        self.assertEqual("42;43", result["complete"]["scopePids"])
        self.assertEqual(4000, result["complete"]["workingSetBytes"])
        self.assertEqual(6000, result["complete"]["privateMemoryBytes"])
        self.assertIsNone(result["unknown"]["privateMemoryBytes"])
        self.assertFalse(result["recycled"]["treeCoverageVerified"])
        self.assertIsNone(result["recycled"]["privateMemoryBytes"])


if __name__ == "__main__":
    unittest.main()
