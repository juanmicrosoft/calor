#!/usr/bin/env python3
"""Exercise the shared runner's compiler build selection without calling agents."""

import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest


HELPER = Path(__file__).resolve().parent / "lib/helpers.sh"
REPO_ROOT = HELPER.parents[4]


class CompilerSelectionTests(unittest.TestCase):
    def run_build(self, root=None, configuration=None, fail=False):
        with tempfile.TemporaryDirectory(prefix="calor-build-selection-") as tmp:
            directory = Path(tmp)
            calls = directory / "calls.jsonl"
            dotnet = directory / "dotnet"
            dotnet.write_text(
                "#!/usr/bin/env python3\n"
                "import json, os, sys\n"
                "from pathlib import Path\n"
                "with Path(os.environ['BUILD_CALLS']).open('a') as output:\n"
                "    output.write(json.dumps(sys.argv[1:]) + '\\n')\n"
                "if os.environ['BUILD_FAIL'] == '1':\n"
                "    print('selected compiler build failed', file=sys.stderr)\n"
                "    sys.exit(23)\n"
                "if not Path(sys.argv[2]).is_file():\n"
                "    print('selected compiler project missing', file=sys.stderr)\n"
                "    sys.exit(24)\n",
                encoding="utf-8",
            )
            dotnet.chmod(0o755)
            env = os.environ.copy()
            env.pop("CALOR_COMPILER_ROOT", None)
            env.pop("CALOR_COMPILER_CONFIGURATION", None)
            if root is not None:
                env["CALOR_COMPILER_ROOT"] = str(root)
            if configuration is not None:
                env["CALOR_COMPILER_CONFIGURATION"] = configuration
            env.update(
                PATH=f"{directory}{os.pathsep}{env['PATH']}",
                BUILD_CALLS=str(calls),
                BUILD_FAIL="1" if fail else "0",
            )
            result = subprocess.run(
                [
                    "bash",
                    "-c",
                    'source "$1"; printf "SELECTED_CLI=%s\\n" "$COMPILER"; build_compiler',
                    "compiler-selection",
                    str(HELPER),
                ],
                env=env,
                capture_output=True,
                text=True,
                timeout=15,
            )
            observed = [json.loads(line) for line in calls.read_text().splitlines()]
            return result, observed

    def assert_selection(self, root, configuration, result, calls):
        project = root / "src/Calor.Compiler/Calor.Compiler.csproj"
        self.assertEqual(
            calls,
            [["build", str(project), "-c", configuration, "--nologo", "-v", "q"]],
        )
        cli = root / f"src/Calor.Compiler/bin/{configuration}/net10.0/calor"
        self.assertIn(f"SELECTED_CLI={cli}\n", result.stdout)

    def test_default_checkout_and_debug(self):
        result, calls = self.run_build()
        self.assert_selection(REPO_ROOT, "Debug", result, calls)
        self.assertEqual(result.returncode, 0, result.stderr)

    def test_selected_configuration(self):
        result, calls = self.run_build(configuration="Release")
        self.assert_selection(REPO_ROOT, "Release", result, calls)
        self.assertEqual(result.returncode, 0, result.stderr)

    def test_selected_root_and_configuration_with_spaces(self):
        with tempfile.TemporaryDirectory(prefix="calor selected build ") as tmp:
            root = Path(tmp)
            project = root / "src/Calor.Compiler/Calor.Compiler.csproj"
            project.parent.mkdir(parents=True)
            project.write_text("<Project />\n", encoding="utf-8")
            result, calls = self.run_build(root, "Release")
            self.assert_selection(root, "Release", result, calls)
            self.assertEqual(result.returncode, 0, result.stderr)

    def test_missing_selected_project_has_no_parent_fallback(self):
        with tempfile.TemporaryDirectory(prefix="calor-missing-build-") as tmp:
            root = Path(tmp)
            result, calls = self.run_build(root)
            self.assert_selection(root, "Debug", result, calls)
            self.assertNotEqual(result.returncode, 0)
            self.assertIn("selected compiler project missing", result.stderr)
            self.assertIn("Failed to build compiler", result.stdout)
            self.assertNotIn("Compiler built successfully", result.stdout)

    def test_build_failure_is_visible_and_has_no_retry(self):
        result, calls = self.run_build(configuration="Release", fail=True)
        self.assert_selection(REPO_ROOT, "Release", result, calls)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("selected compiler build failed", result.stderr)
        self.assertIn("Failed to build compiler", result.stdout)
        self.assertNotIn("Compiler built successfully", result.stdout)


if __name__ == "__main__":
    unittest.main(verbosity=2)
