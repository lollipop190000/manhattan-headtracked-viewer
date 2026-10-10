import base64
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
SHELL = shutil.which("powershell.exe")
SCRIPT = ROOT / "tools/benchmark.ps1"


@unittest.skipUnless(sys.platform == "win32" and SHELL, "Windows PowerShell 5.1 collector lifetime tests")
class BenchmarkCollectorTests(unittest.TestCase):
    def test_speed_binding_accepts_100_and_rejects_unsupported_requests_without_launch(self):
        result = self.powershell(r"""
            $bind=[ScriptBlock]::Create($ast.ParamBlock.Extent.Text + '; $Speed')
            $accepted=@(10,50,100 | ForEach-Object { & $bind -Speed $_ })
            $rejected=$false
            try { & $bind -Speed 101 | Out-Null } catch { $rejected=$true }
            @{accepted=$accepted;rejected=$rejected} | ConvertTo-Json -Compress
        """)
        self.assertEqual([10, 50, 100], result["accepted"])
        self.assertTrue(result["rejected"])

    def test_suite_dry_run_contains_fifteen_regions_and_five_100_cases_without_launch(self):
        suite = str(ROOT / "tools/benchmark_suite.ps1").replace("'", "''")
        result = self.powershell(r"""
            function Start-Process { throw 'Dry run must never launch a process' }
            $all=(& 'SUITE_PATH' -DryRun -NoTracker -WarningAction SilentlyContinue 6>&1 | Out-String -Width 200)
            $fast=(& 'SUITE_PATH' -DryRun -NoTracker -CaseFilter '*-100' -WarningAction SilentlyContinue 6>&1 | Out-String -Width 200)
            @{all=$all;fast=$fast} | ConvertTo-Json -Compress
        """.replace("SUITE_PATH", suite))
        self.assertIn("cases: 17", result["all"])
        self.assertIn("02:19:40", result["all"])
        self.assertIn("cases: 5", result["fast"])
        for region in ("Midtown", "Downtown", "Park", "Uptown", "Air"):
            self.assertIn(f"{region}-100", result["fast"])
        self.assertNotIn("Midtown-50-proxies", result["fast"])

    def powershell(self, code):
        # Load only the cleanup function; dot-sourcing benchmark.ps1 would launch the game.
        script_path = str(SCRIPT).replace("'", "''")
        setup = r"""
            $ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'
            $tokens=$null; $parseErrors=$null
            $ast=[Management.Automation.Language.Parser]::ParseFile('SCRIPT_PATH',[ref]$tokens,[ref]$parseErrors)
            if($parseErrors.Count) { throw 'Benchmark script parse error' }
            $cleanup=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Stop-OwnedBenchmarkCollector'},$true)
            Set-Item function:Stop-OwnedBenchmarkCollector $cleanup.Body.GetScriptBlock()
        """.replace("SCRIPT_PATH", script_path)
        encoded = base64.b64encode((setup + code).encode("utf-16le")).decode("ascii")
        result = subprocess.run([SHELL, "-NoProfile", "-ExecutionPolicy", "Bypass", "-EncodedCommand", encoded],
                                capture_output=True, text=True, encoding="utf-8", errors="replace", check=True, timeout=15)
        return json.loads(result.stdout.strip().splitlines()[-1])

    def temporary_folder(self):
        scratch = ROOT / ".cache/tests"
        scratch.mkdir(parents=True, exist_ok=True)
        return tempfile.TemporaryDirectory(dir=scratch)

    def test_windows_powershell_known_exit_codes_and_forced_cleanup(self):
        with self.temporary_folder() as temporary:
            folder = temporary.replace("'", "''")
            result = self.powershell(r"""
                $results=@()
                foreach($scenario in @('zero','nonzero','forced')) {
                    $childCode=switch($scenario) { 'zero' {'exit 0'}; 'nonzero' {'exit 7'}; 'forced' {'Start-Sleep -Seconds 10'} }
                    $encodedChild=[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($childCode))
                    $child=Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') -ArgumentList @('-NoProfile','-EncodedCommand',$encodedChild) -WindowStyle Hidden -PassThru
                    [void]$child.Handle
                    if($scenario -ne 'forced') {
                        $deadline=[datetime]::UtcNow.AddSeconds(5)
                        while(-not $child.HasExited -and [datetime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 50; $child.Refresh() }
                        if(-not $child.HasExited) { Stop-Process -Id $child.Id; throw 'Isolated child did not exit in time' }
                    }
                    $signal=Join-Path 'TEMPORARY_FOLDER' ($scenario+'.stop')
                    $warnings=@()
                    $completion=Stop-OwnedBenchmarkCollector -Collector $child -StopFile $signal -Name $scenario -ErrorLog 'isolated-error.log' -WaitMilliseconds 50 -WarningVariable warnings -WarningAction SilentlyContinue
                    $results += @{scenario=$scenario;completion=$completion;warnings=@($warnings | ForEach-Object { $_.ToString() });stopRemoved=(-not (Test-Path -LiteralPath $signal))}
                }
                @{version=$PSVersionTable.PSVersion.Major;results=$results} | ConvertTo-Json -Depth 6 -Compress
            """.replace("TEMPORARY_FOLDER", folder))
            self.assertEqual(5, result["version"])
            by_scenario = {item["scenario"]: item for item in result["results"]}
            zero = by_scenario["zero"]
            self.assertEqual(0, zero["completion"]["exitCode"])
            self.assertFalse(zero["completion"]["forcedCleanup"])
            self.assertEqual([], zero["warnings"])
            nonzero = by_scenario["nonzero"]
            self.assertEqual(7, nonzero["completion"]["exitCode"])
            self.assertFalse(nonzero["completion"]["forcedCleanup"])
            self.assertEqual(1, len(nonzero["warnings"]))
            self.assertIn("failed (exit code 7)", nonzero["warnings"][0])
            forced = by_scenario["forced"]
            self.assertTrue(forced["completion"]["forcedCleanup"])
            self.assertTrue(forced["completion"]["hasExited"])
            self.assertTrue(any("forced termination request" in warning for warning in forced["warnings"]))
            self.assertTrue(all(item["stopRemoved"] for item in result["results"]))

    def test_unavailable_exit_code_is_unknown_and_handle_is_disposed(self):
        with self.temporary_folder() as temporary:
            signal = str(Path(temporary) / "unknown.stop").replace("'", "''")
            result = self.powershell(r"""
                $fake=[pscustomobject]@{HasExited=$true;ExitCode=$null;Disposed=$false}
                $fake | Add-Member ScriptMethod Refresh { }
                $fake | Add-Member ScriptMethod Dispose { $this.Disposed=$true }
                $warnings=@()
                $completion=Stop-OwnedBenchmarkCollector -Collector $fake -StopFile 'STOP_PATH' -Name 'unknown' -ErrorLog 'isolated-error.log' -WarningVariable warnings -WarningAction SilentlyContinue
                @{completion=$completion;warnings=@($warnings | ForEach-Object {$_.ToString()});disposed=$fake.Disposed;stopRemoved=(-not (Test-Path -LiteralPath 'STOP_PATH'))} | ConvertTo-Json -Depth 5 -Compress
            """.replace("STOP_PATH", signal))
            self.assertIsNone(result["completion"]["exitCode"])
            self.assertFalse(result["completion"]["forcedCleanup"])
            self.assertEqual(1, len(result["warnings"]))
            self.assertIn("exit code is unavailable", result["warnings"][0])
            self.assertNotIn("failed", result["warnings"][0])
            self.assertTrue(result["disposed"])
            self.assertTrue(result["stopRemoved"])

    def test_all_collectors_retain_handles_immediately_after_start(self):
        result = self.powershell(r"""
            $starts=@($ast.FindAll({param($node) $node -is [Management.Automation.Language.AssignmentStatementAst] -and $node.Right.Extent.Text -match '^Start-Process '},$true))
            $text=[IO.File]::ReadAllText('SCRIPT_PATH')
            $retained=@()
            foreach($start in $starts) {
                $next=$text.Substring($start.Extent.EndOffset).TrimStart()
                $next=$next -replace '^#[^\r\n]*[\r\n]+\s*',''
                $retained += $next.StartsWith('[void]'+$start.Left.Extent.Text+'.Handle')
            }
            @{count=$starts.Count;retained=$retained} | ConvertTo-Json -Compress
        """.replace("SCRIPT_PATH", str(SCRIPT).replace("'", "''")))
        self.assertEqual(3, result["count"])
        self.assertEqual([True, True, True], result["retained"])


if __name__ == "__main__":
    unittest.main()
