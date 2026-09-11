"""Actual local observer/build/test integration; no model or provider requests."""
import importlib.util
import json
import os
from pathlib import Path
import shutil
import sys
import unittest
import uuid

from ppw_redesign_epoch import BENCH


def load(name):
    spec = importlib.util.spec_from_file_location(name.replace("-", "_"), BENCH / name)
    value = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(value)
    return value


observer_module = load("ppw-run-observer.py")
isolation = load("ppw-gateway-client.py")
test_host = load("ppw-test-host.py")
PACKAGES = Path(os.environ.get("NUGET_PACKAGES", str(Path.home() / ".nuget/packages")))
AVAILABLE = (sys.platform == "darwin" and shutil.which("dotnet")
             and all((PACKAGES / name).is_file() for name in test_host.DEPENDENCIES))


@unittest.skipUnless(AVAILABLE, "actual macOS/.NET observer runtime required")
class RunObserverTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.runtime = test_host.prepare(PACKAGES)
        cls.execution = isolation.runtime_identity()

    def setUp(self):
        self.root = BENCH / "tests" / (".observer-" + uuid.uuid4().hex)
        self.root.mkdir()
        self.addCleanup(shutil.rmtree, self.root)
        self.work = self.root / "work"
        self.workspace = self.work / "workspace"
        source = self.workspace / "src"
        source.mkdir(parents=True)
        (source / "Src.csproj").write_text(
            '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework>'
            '<ImplicitUsings>enable</ImplicitUsings><CalorEnforceEffects>true</CalorEnforceEffects>'
            '<CalorPermissiveEffects>false</CalorPermissiveEffects></PropertyGroup></Project>')
        (source / "Value.cs").write_text("public static class Value { public static int Get() => 1; }\n")
        self.output = self.root / "archive/task/calor-strict/run-1"
        heldout = self.output / "heldout"
        heldout.mkdir(parents=True)
        (heldout / "HeldOut.csproj").write_text(f"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><IsPackable>false</IsPackable></PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <Reference Include="Src"><HintPath>{source / "bin/Debug/net10.0/Src.dll"}</HintPath></Reference>
  </ItemGroup>
</Project>
""")
        (heldout / "HeldOutTests.cs").write_text(
            "using Xunit; public class HeldOutTests { [Fact] public void Result() => "
            "Assert.Equal(2, Value.Get()); }\n")
        self.hidden = self.root / "original-task"
        (self.hidden / "tests").mkdir(parents=True)
        (self.hidden / "tests/HeldOutTests.cs").write_text("HIDDEN SOURCE")
        (self.hidden / "seeded").mkdir()
        (self.hidden / "seeded/solution.cs").write_text("HIDDEN SOLUTION")
        self.protected = self.root / "protected"
        self.protected.mkdir()
        self.observer = observer_module.RunObserver(
            work_root=self.work, output=self.output, hidden_roots=(self.hidden,),
            protected_root=self.protected, arm="csharp", fragment_glob="*.calr",
            heldout_count=1, compiler=BENCH / "ppw-run-observer.py", permissive=False,
            test_runtime=self.runtime, execution_runtime=self.execution)
        self.addCleanup(self.observer.close)
        self.observer.register({"kind": observer_module.REGISTER_KIND,
                                "workspace": str(self.workspace), "output": str(self.output)})

    def test_workspace_build_heldout_observation_and_final_grading_remain_live(self):
        policy_before = load("harness-capture.py").policy_snapshot(self.workspace)
        (self.workspace / "src/Value.cs").write_text(
            "public static class Value { public static int Get() => 2; }\n")
        self.assertEqual({"ok": True}, self.observer.observe({
            "kind": observer_module.OBSERVATION_KIND, "command": "build",
            "exitCode": 0, "feedbackLatencyMs": 17,
        }))
        journal = [json.loads(line) for line in (self.output / "journal.jsonl").read_text().splitlines()]
        self.assertEqual(1, len(journal))
        self.assertTrue(journal[0]["edited"])
        self.assertEqual(1, journal[0]["iteration"])
        self.assertEqual((1, 0), (journal[0]["heldout_pass"], journal[0]["heldout_fail"]))
        self.assertEqual(17, journal[0]["feedback_latency_ms"])
        self.assertEqual({"ok": True, "archivedFiles": 1}, self.observer.seal())
        self.assertEqual(
            policy_before, json.loads((self.output / "policy-after.json").read_text()))
        (self.workspace / "src").mkdir()
        (self.workspace / "src/Value.cs").write_text(
            "public static class Value { public static int Get() => 999; }\n")
        final = self.observer.final()
        self.assertTrue(final["buildOk"])
        self.assertEqual((1, 0), (final["heldoutPassed"], final["heldoutFailed"]))
        self.assertIn("=> 2", (self.output / "final-src/Value.cs").read_text())
        self.assertTrue((self.output / ".src_final.txt").is_file())
        self.assertTrue((self.output / ".ho_final.txt").is_file())

    def test_model_targets_cannot_touch_hidden_test_copy_or_exfiltrate_to_workspace(self):
        secret = self.hidden / "tests/secret.txt"
        secret.write_text("SYNTHETIC_SECRET")
        leak = self.workspace / "leak.txt"
        heldout_source = self.output / "heldout/HeldOutTests.cs"
        (self.workspace / "src/Src.csproj").write_text(f"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
  <Target Name="CandidateAttempt" BeforeTargets="Build">
    <Exec Command="/bin/sh -c 'cat &quot;{secret}&quot; &gt; &quot;{leak}&quot; 2&gt;/dev/null || true'" />
    <WriteLinesToFile Condition="'$(HiddenSource)' != ''"
                      File="$(HiddenSource)" Lines="MUTATED" Overwrite="true" />
    <Error Condition="'$(HiddenSource)' != ''" Text="MODEL_TARGET_EXECUTED" />
  </Target>
</Project>
""")
        (self.output / "heldout/HeldOut.csproj").write_text(f"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <ProjectReference Include="{self.workspace / 'src/Src.csproj'}"
                      AdditionalProperties="HiddenSource={heldout_source}" />
  </ItemGroup>
</Project>
""")
        (self.workspace / "src/Value.cs").write_text(
            "public static class Value { public static int Get() => 2; }\n")
        original_hidden_test = heldout_source.read_text()
        self.assertEqual({"ok": True}, self.observer.observe({
            "kind": observer_module.OBSERVATION_KIND, "command": "build",
            "exitCode": 0, "feedbackLatencyMs": 1,
        }))
        record = json.loads((self.output / "journal.jsonl").read_text().strip())
        self.assertEqual((1, 0), (record["heldout_pass"], record["heldout_fail"]))
        self.assertFalse(leak.exists())
        self.assertEqual(original_hidden_test, heldout_source.read_text())
        self.assertNotIn("MODEL_TARGET_EXECUTED", (self.output / ".ho_last.txt").read_text())

    def test_fake_console_counts_cannot_forge_framework_aggregate(self):
        (self.output / "heldout/HeldOutTests.cs").write_text("""
using Xunit;
public class HeldOutTests {
    [Fact] public void Result() {
        Console.WriteLine("Passed: 4, Failed: 0");
        Assert.Equal(3, Value.Get());
    }
}
""")
        (self.workspace / "src/Value.cs").write_text(
            "public static class Value { public static int Get() => 2; }\n")
        self.assertEqual({"ok": True}, self.observer.observe({
            "kind": observer_module.OBSERVATION_KIND, "command": "test",
            "exitCode": 1, "feedbackLatencyMs": 2,
        }))
        record = json.loads((self.output / "journal.jsonl").read_text().strip())
        self.assertEqual((0, 1), (record["heldout_pass"], record["heldout_fail"]))
        self.assertIn("Passed: 4, Failed: 0", (self.output / ".ho_last.txt").read_text())

    def test_result_receipt_rejects_protocol_ambiguity(self):
        nonce = "a" * 64
        frame = observer_module.RESULT_PREFIX + nonce + (
            ':{"failed":0,"passed":1,"skipped":0,"total":1}')
        self.assertEqual(
            (1, 0), observer_module.test_receipt(
                "Passed: 999, Failed: 0\n" + frame, "", nonce, 0))
        with self.assertRaisesRegex(ValueError, "ambiguous"):
            observer_module.test_receipt(frame + "\n" + frame, "", nonce, 0)

    def test_source_inspector_runs_as_generated_code_without_task_root_access(self):
        compiler = BENCH.parent.parent / "src/Calor.Compiler/bin/Debug/net10.0/calor.dll"
        if not compiler.is_file():
            self.skipTest("built compiler required")
        inspection = load("ppw-source-inspection.py")
        task = BENCH / "task-candidates/1256/C-001-quota-adapter"
        pair = json.loads((task / "pair.json").read_text())
        shutil.rmtree(self.workspace / "src")
        shutil.copytree(task / "seeded/honest-a", self.workspace / "src")
        (self.workspace / "src/Src.csproj").write_text(
            "<Project><PropertyGroup><CalorEnforceEffects>true</CalorEnforceEffects>"
            "<CalorPermissiveEffects>false</CalorPermissiveEffects></PropertyGroup></Project>")
        self.observer.fragment_glob = "*.calr.inc"
        self.observer.seal()
        final = self.output / "final-src"
        runtime = inspection.prepare(compiler)
        self.observer.compiler = compiler
        report = self.observer.source_inspection(pair, task / "starter-a", final, runtime)
        inspection.validate(
            report, pair, {"baseline": task / "starter-a", "final": final},
            inspection.sha(compiler), runtime)
        self.assertEqual(runtime["files"]["ppw-source-inspector.dll"],
                         report["inspectorSha256"])


if __name__ == "__main__":
    unittest.main()
