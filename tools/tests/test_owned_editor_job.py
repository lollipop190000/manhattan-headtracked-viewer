import base64
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
COMMON = ROOT / "tools/common.ps1"
SHELL = shutil.which("powershell.exe")


def ps_quote(value):
    return "'" + str(value).replace("'", "''") + "'"


@unittest.skipUnless(sys.platform == "win32" and SHELL, "Windows PowerShell owned Editor tests")
class OwnedEditorJobTests(unittest.TestCase):
    def setUp(self):
        scratch = ROOT / ".cache/tests"
        scratch.mkdir(parents=True, exist_ok=True)
        self.temporary = tempfile.TemporaryDirectory(dir=scratch, prefix="owned editor ")
        self.addCleanup(self.temporary.cleanup)
        self.repository = Path(self.temporary.name)

    def run_script(self, body):
        # Extract only the supported helpers. Dot-sourcing the build entrypoint
        # would run source preflight and launch an actual Unity Editor.
        setup = r"""
            Set-StrictMode -Version Latest
            $ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'
            $tokens=$null; $parseErrors=$null
            $ast=[Management.Automation.Language.Parser]::ParseFile(COMMON_PATH,[ref]$tokens,[ref]$parseErrors)
            if($parseErrors.Count) { throw 'Common script parse error' }
            foreach($name in @('Resolve-ViewerEditor','Invoke-ViewerOwnedEditorMethod')) {
                $definition=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name},$true)
                if(-not $definition) { throw "Missing supported helper $name" }
                Set-Item "function:$name" $definition.Body.GetScriptBlock()
            }
            $ViewerRepository=REPOSITORY_PATH
            $ViewerProject=Join-Path $ViewerRepository 'Project with spaces'
            $ViewerEditorVersion='6000.3.22f1'
        """.replace("COMMON_PATH", ps_quote(COMMON)).replace("REPOSITORY_PATH", ps_quote(self.repository))
        encoded = base64.b64encode((setup + body).encode("utf-16le")).decode("ascii")
        result = subprocess.run(
            [SHELL, "-NoProfile", "-ExecutionPolicy", "Bypass", "-EncodedCommand", encoded],
            capture_output=True, text=True, encoding="utf-8", errors="replace", check=True, timeout=20,
        )
        data = json.loads(result.stdout.strip().splitlines()[-1])
        self.assertEqual(5, data["version"], "These integration checks must exercise Windows PowerShell 5.1.")
        return data

    def run_owned_child(self, *, delay_ms, exit_code, timeout=4, unrelated=False):
        completion = self.repository / "child-completed.txt"
        child = f"Start-Sleep -Milliseconds {delay_ms}; [IO.File]::WriteAllText({ps_quote(completion)}, 'completed'); exit {exit_code}"
        encoded_child = base64.b64encode(child.encode("utf-16le")).decode("ascii")
        body = r"""
            function Resolve-ViewerEditor { param([string]$EditorPath) return $EditorPath }
            $script:ownedId=0; $script:ownedObserver=$null; $script:launch=$null
            function Start-Process {
                param([string]$FilePath,[string[]]$ArgumentList,[string]$WorkingDirectory,[string]$WindowStyle,[switch]$PassThru)
                $script:launch=@{file=$FilePath;arguments=@($ArgumentList);workingDirectory=$WorkingDirectory;windowStyle=$WindowStyle;passThru=[bool]$PassThru}
                # Return a real process to exercise WaitForExit, exit-code reads,
                # timeout cleanup and Dispose rather than mocked process state.
                $child=Microsoft.PowerShell.Management\Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') -ArgumentList @('-NoProfile','-EncodedCommand','CHILD_COMMAND') -WindowStyle Hidden -PassThru
                $script:ownedId=$child.Id
                $script:ownedObserver=[Diagnostics.Process]::GetProcessById($child.Id)
                [void]$script:ownedObserver.Handle
                return $child
            }
            $other=$null
            $success=$false; $message=''; $otherAlive=$null
            $watch=[Diagnostics.Stopwatch]::StartNew()
            try {
                if (UNRELATED) {
                    $otherCommand=[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes('Start-Sleep -Seconds 30'))
                    $other=Microsoft.PowerShell.Management\Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') -ArgumentList @('-NoProfile','-EncodedCommand',$otherCommand) -WindowStyle Hidden -PassThru
                    [void]$other.Handle
                }
                try {
                    Invoke-ViewerOwnedEditorMethod -Method 'Manhattan.Editor.TestJob' -EditorPath 'test-owned-editor.exe' -Timeout TIMEOUT -BuildTarget 'StandaloneWindows64'
                    $success=$true
                } catch { $message=$_.Exception.Message }
                $watch.Stop()
                $script:ownedObserver.Refresh()
                $ownedExited=$script:ownedObserver.HasExited
                if($other) { $other.Refresh(); $otherAlive=-not $other.HasExited }
                @{success=$success;error=$message;elapsedMs=$watch.ElapsedMilliseconds;ownedExited=$ownedExited;
                    unrelatedAlive=$otherAlive;completed=(Test-Path -LiteralPath COMPLETION_PATH);
                    launch=$script:launch;version=$PSVersionTable.PSVersion.Major} | ConvertTo-Json -Compress -Depth 5
            } finally {
                # Fixture cleanup is PID-specific as well. Never enumerate or
                # terminate processes by executable name.
                if($script:ownedObserver) {
                    if(-not $script:ownedObserver.HasExited) { $script:ownedObserver.Kill(); [void]$script:ownedObserver.WaitForExit(10000) }
                    $script:ownedObserver.Dispose()
                }
                if($other) {
                    if(-not $other.HasExited) { $other.Kill(); [void]$other.WaitForExit(10000) }
                    $other.Dispose()
                }
            }
        """.replace("CHILD_COMMAND", encoded_child).replace("COMPLETION_PATH", ps_quote(completion))
        body = body.replace("UNRELATED", "$true" if unrelated else "$false").replace("TIMEOUT", str(timeout))
        return self.run_script(body)

    def test_waits_for_real_delayed_success_and_preserves_batch_arguments(self):
        data = self.run_owned_child(delay_ms=500, exit_code=0)
        self.assertTrue(data["success"], data["error"])
        self.assertTrue(data["completed"], "Success must follow the child's completed work.")
        self.assertTrue(data["ownedExited"])
        self.assertGreaterEqual(data["elapsedMs"], 500)
        launch = data["launch"]
        self.assertEqual("test-owned-editor.exe", launch["file"])
        self.assertEqual("Hidden", launch["windowStyle"])
        self.assertEqual(str(self.repository / "Project with spaces"), launch["workingDirectory"])
        self.assertTrue(launch["passThru"])
        arguments = launch["arguments"]
        self.assertIn("-batchmode", arguments)
        self.assertNotIn("-quit", arguments, "An incomplete async Editor entrypoint must remain alive.")
        self.assertEqual('"' + str(self.repository / "Project with spaces") + '"', arguments[arguments.index("-projectPath") + 1])
        self.assertEqual("Manhattan.Editor.TestJob", arguments[arguments.index("-executeMethod") + 1])
        self.assertEqual("StandaloneWindows64", arguments[arguments.index("-buildTarget") + 1])
        log_path = Path(arguments[arguments.index("-logFile") + 1].strip('"'))
        self.assertEqual(self.repository / "Logs", log_path.parent)
        self.assertTrue(log_path.name.startswith("Manhattan-Editor-TestJob-"))

    def test_nonzero_real_child_exit_is_reported_as_failure(self):
        data = self.run_owned_child(delay_ms=200, exit_code=7)
        self.assertFalse(data["success"])
        self.assertTrue(data["completed"])
        self.assertTrue(data["ownedExited"])
        self.assertIn("Unity job failed (7)", data["error"])
        self.assertIn(".log", data["error"])

    def test_timeout_stops_only_owned_child_and_keeps_unrelated_process_alive(self):
        data = self.run_owned_child(delay_ms=10000, exit_code=0, timeout=1, unrelated=True)
        self.assertFalse(data["success"])
        self.assertIn("Owned Unity job timed out", data["error"])
        self.assertFalse(data["completed"], "Timed-out work must not report successful completion.")
        self.assertTrue(data["ownedExited"], "The timed-out child must be reaped before returning.")
        self.assertTrue(data["unrelatedAlive"], "Timeout cleanup must not stop an unrelated process.")
        self.assertGreaterEqual(data["elapsedMs"], 1000)
        self.assertLess(data["elapsedMs"], 8000)

    def test_resolver_rejects_existing_wrong_version_executable_without_launch(self):
        body = r"""
            function Start-Process { throw 'Version rejection must happen before launch' }
            $rejected=$false; $message=''
            try { Resolve-ViewerEditor -EditorPath WRONG_EDITOR | Out-Null } catch { $rejected=$true; $message=$_.Exception.Message }
            @{rejected=$rejected;error=$message;version=$PSVersionTable.PSVersion.Major} | ConvertTo-Json -Compress
        """.replace("WRONG_EDITOR", ps_quote(SHELL))
        data = self.run_script(body)
        self.assertTrue(data["rejected"])
        self.assertIn("The Editor executable must be Unity 6000.3.22f1", data["error"])

    def test_resolver_rejects_missing_executable_without_launch(self):
        body = r"""
            function Start-Process { throw 'Missing executable must fail before launch' }
            $rejected=$false; $message=''
            try { Resolve-ViewerEditor -EditorPath MISSING_EDITOR | Out-Null } catch { $rejected=$true; $message=$_.Exception.Message }
            @{rejected=$rejected;error=$message;version=$PSVersionTable.PSVersion.Major} | ConvertTo-Json -Compress
        """.replace("MISSING_EDITOR", ps_quote(self.repository / "missing Unity.exe"))
        data = self.run_script(body)
        self.assertTrue(data["rejected"])
        self.assertIn("Unity 6000.3.22f1 executable could not be found", data["error"])


if __name__ == "__main__":
    unittest.main()
