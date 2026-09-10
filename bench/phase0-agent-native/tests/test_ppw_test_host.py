"""Real existing xUnit engine parity; deterministic fixtures, never model calls."""
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import unittest
import uuid

BENCH = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("ppw_xunit_test", BENCH / "ppw-test-host.py")
host = importlib.util.module_from_spec(spec)
spec.loader.exec_module(host)
PACKAGES = Path(os.environ.get("NUGET_PACKAGES", str(Path.home() / ".nuget/packages")))
AVAILABLE = shutil.which("dotnet") and all((PACKAGES / name).is_file() for name in host.DEPENDENCIES)


@unittest.skipUnless(AVAILABLE, "existing .NET and registered xUnit packages required")
class HostTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.runtime = host.prepare(PACKAGES)

    def setUp(self):
        self.root = BENCH / "tests" / (".xunit-parity-" + uuid.uuid4().hex)
        self.root.mkdir()
        self.addCleanup(shutil.rmtree, self.root)
        self.environment = dict(os.environ, PPW_XUNIT_RUNTIME=self.runtime["manifest"],
                                PPW_REAL_DOTNET=shutil.which("dotnet"), NUGET_PACKAGES=str(PACKAGES))

    def project(self, source):
        path = self.root / "Parity.csproj"
        path.write_text("""<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><TargetFramework>net10.0</TargetFramework><IsPackable>false</IsPackable>
<ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
<ItemGroup><PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
<PackageReference Include="xunit" Version="2.9.2" />
<PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" /></ItemGroup>
</Project>""")
        (self.root / "Cases.cs").write_text(source)
        result = subprocess.run(["dotnet", "build", str(path), "--nologo", "-v", "q"],
                                capture_output=True, text=True, env=self.environment, timeout=120)
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        return path

    def execute(self, project):
        return subprocess.run([self.environment["PPW_REAL_DOTNET"], self.runtime["binary"],
                               str(project.parent / "bin/Debug/net10.0/Parity.dll")],
                              capture_output=True, text=True, env=self.environment, timeout=60)

    @staticmethod
    def outcomes(output):
        return sorted(re.findall(r"^\s+(Passed|Failed|Skipped) (.*?) \[", output, re.MULTILINE))

    def test_real_theory_async_fixture_failure_and_skip_match_vstest(self):
        project = self.project("""
using Xunit;
public sealed class Shared : IAsyncLifetime {
    public int Value;
    public Task InitializeAsync() { Value = 7; return Task.CompletedTask; }
    public Task DisposeAsync() => Task.CompletedTask;
}
public sealed class Cases : IClassFixture<Shared> {
    readonly Shared shared;
    public Cases(Shared shared) { this.shared = shared; }
    [Fact] public void Fixture() => Assert.Equal(7, shared.Value);
    [Theory] [InlineData(1)] [InlineData(2)]
    public void Theory(int value) => Assert.True(value > 0);
    [Fact] public async Task Async() { await Task.Yield(); Assert.Equal(7, shared.Value); }
    [Fact] public void Failure() => Assert.Equal(8, shared.Value);
    [Fact(Skip = "DETERMINISTIC_SKIP")] public void Skipped() { }
}""")
        baseline = subprocess.run(["dotnet", "test", str(project), "--no-build", "--nologo",
                                   "--logger", "console;verbosity=normal"],
                                  capture_output=True, text=True, env=self.environment, timeout=60)
        result = self.execute(project)
        self.assertEqual(1, baseline.returncode, baseline.stdout + baseline.stderr)
        self.assertEqual(1, result.returncode, result.stdout + result.stderr)
        self.assertEqual(self.outcomes(baseline.stdout), self.outcomes(result.stdout))
        self.assertEqual(6, len(self.outcomes(result.stdout)))
        self.assertIn("Failed: 1, Passed: 4, Skipped: 1, Total: 6", result.stdout)

    def test_fixture_cleanup_error_has_no_completed_success_summary(self):
        project = self.project("""
using Xunit;
public sealed class Broken : IDisposable {
    public void Dispose() => throw new InvalidOperationException("DETERMINISTIC_CLEANUP_FAILURE");
}
public sealed class Cases : IClassFixture<Broken> {
    public Cases(Broken fixture) { }
    [Fact] public void PassingBody() => Assert.True(true);
}""")
        result = self.execute(project)
        self.assertEqual(2, result.returncode, result.stdout + result.stderr)
        self.assertIn("DETERMINISTIC_CLEANUP_FAILURE", result.stderr)
        self.assertNotIn("Passed!", result.stdout)
        self.assertEqual([], self.outcomes(result.stdout))

    def test_cli_rejects_unsupported_options_before_building_or_running(self):
        import sys
        result = subprocess.run([sys.executable, str(BENCH / "ppw-test-host.py"),
                                 "test", "--collect", "SYNTHETIC-unregistered"],
                                capture_output=True, text=True, env=self.environment)
        self.assertEqual(2, result.returncode)
        self.assertIn("unrecognized arguments", result.stderr)

    def test_runtime_manifest_cannot_silently_change(self):
        runtime = dict(self.runtime, binaries=dict(self.runtime["binaries"]))
        runtime["binaries"]["ppw-xunit-host.dll"] = "0" * 64
        with self.assertRaisesRegex(ValueError, "manifest changed"):
            host.validate_runtime(runtime)
        self.assertEqual(Path(self.runtime["binary"]), host.validate_runtime(self.runtime))


if __name__ == "__main__":
    unittest.main()
