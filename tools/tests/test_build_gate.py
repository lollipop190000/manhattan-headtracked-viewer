import base64
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
SHELL = shutil.which("powershell.exe")
GIT = shutil.which("git")
SCRIPT = ROOT / "tools/build.ps1"


@unittest.skipUnless(sys.platform == "win32" and SHELL and GIT, "Windows PowerShell Git build gate tests")
class BuildGateTests(unittest.TestCase):
    def setUp(self):
        scratch = ROOT / ".cache/tests"
        scratch.mkdir(parents=True, exist_ok=True)
        self.temporary = tempfile.TemporaryDirectory(dir=scratch)
        self.addCleanup(self.temporary.cleanup)
        self.repository = Path(self.temporary.name)
        self.git("init", "--quiet")
        (self.repository / ".gitignore").write_text("IgnoredBuilds/\n", encoding="utf-8")
        (self.repository / "authored.txt").write_text("committed source\n", encoding="utf-8")
        self.git("add", ".")
        self.git("-c", "user.name=Build gate test", "-c", "user.email=build-gate@example.invalid", "commit", "--quiet", "-m", "Fixture")

    def git(self, *arguments):
        subprocess.run([GIT, "-C", str(self.repository), *arguments], capture_output=True,
                       text=True, encoding="utf-8", errors="replace", check=True, timeout=10)

    def assert_gate(self, allow_dirty=False, repository=None, git_ceiling=None):
        # Load only the gate; executing the complete build tool would start Unity.
        path = str(SCRIPT).replace("'", "''")
        repository_path = str(repository or self.repository).replace("'", "''")
        command = r"""
            $ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'
            $tokens=$null; $parseErrors=$null
            $ast=[Management.Automation.Language.Parser]::ParseFile('SCRIPT_PATH',[ref]$tokens,[ref]$parseErrors)
            if($parseErrors.Count) { throw 'Build script parse error' }
            $gate=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Assert-CleanBuildSource'},$true)
            Set-Item function:Assert-CleanBuildSource $gate.Body.GetScriptBlock()
            $allowed=$true; $errorMessage=''
            try { Assert-CleanBuildSource -RepositoryRoot 'REPOSITORY_PATH' -AllowDirty:ALLOW_DIRTY } catch { $allowed=$false; $errorMessage=$_.Exception.Message }
            @{allowed=$allowed;error=$errorMessage;version=$PSVersionTable.PSVersion.Major} | ConvertTo-Json -Compress
        """.replace("SCRIPT_PATH", path).replace("REPOSITORY_PATH", repository_path).replace("ALLOW_DIRTY", "$true" if allow_dirty else "$false")
        encoded = base64.b64encode(command.encode("utf-16le")).decode("ascii")
        environment = os.environ.copy()
        if git_ceiling is not None:
            environment["GIT_CEILING_DIRECTORIES"] = str(git_ceiling)
        result = subprocess.run([SHELL, "-NoProfile", "-ExecutionPolicy", "Bypass", "-EncodedCommand", encoded],
                                capture_output=True, text=True, encoding="utf-8", errors="replace", check=True, timeout=15, env=environment)
        data = json.loads(result.stdout.strip().splitlines()[-1])
        self.assertEqual(5, data["version"])
        return data

    def test_clean_checkout_and_ignored_build_output_are_allowed(self):
        self.assertTrue(self.assert_gate()["allowed"])
        ignored = self.repository / "IgnoredBuilds"
        ignored.mkdir()
        (ignored / "player.exe").write_bytes(b"ignored fixture")
        self.assertTrue(self.assert_gate()["allowed"])

    def test_tracked_changes_and_explicit_allow_dirty(self):
        (self.repository / "authored.txt").write_text("changed source\n", encoding="utf-8")
        result = self.assert_gate()
        self.assertFalse(result["allowed"])
        self.assertIn("tracked or untracked changes", result["error"])
        self.assertTrue(self.assert_gate(allow_dirty=True)["allowed"])
        self.git("add", "authored.txt")
        self.assertFalse(self.assert_gate()["allowed"])

    def test_nonignored_untracked_sources_are_blocked(self):
        authored = self.repository / "NewAuthoredContent"
        authored.mkdir()
        (authored / "new-source.cs").write_text("// authored fixture\n", encoding="utf-8")
        self.assertFalse(self.assert_gate()["allowed"])
        self.assertTrue(self.assert_gate(allow_dirty=True)["allowed"])

    def test_git_failure_is_rejected_even_with_allow_dirty(self):
        scratch = ROOT / ".cache/tests"
        with tempfile.TemporaryDirectory(dir=scratch) as temporary:
            # Do not let Git discover the real viewer checkout above the fixture.
            result = self.assert_gate(allow_dirty=True, repository=Path(temporary), git_ceiling=scratch)
        self.assertFalse(result["allowed"])
        self.assertIn("Git source-status check failed", result["error"])

    def test_gate_runs_after_preflight_and_before_unity(self):
        source = SCRIPT.read_text(encoding="utf-8")
        preflight = source.index("Invoke-Checked $PythonExe @((Join-Path $PSScriptRoot 'build_preflight.py')")
        gate = source.index("Assert-CleanBuildSource -RepositoryRoot $ViewerRepository -AllowDirty:$AllowDirty")
        unity = source.index("Invoke-Checked 'unity' $arguments")
        self.assertLess(preflight, gate)
        self.assertLess(gate, unity)
        self.assertIn("--allow-dirty-build", source)


if __name__ == "__main__":
    unittest.main()
