using System.Diagnostics;
using System.Text;
using Calor.Compiler.Migration;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// End-to-end integration test for the migration pipeline:
///
///   1. Write a synthetic legacy `.calr` file with structural IDs.
///   2. Run <see cref="StructuralIdDropper"/> in-process (the same code
///      the `calor fix` CLI invokes) to produce a migrated file plus a
///      <c>migration.log.json</c>.
///   3. Shell out to <c>scripts/byte_preservation_check.py</c> (the
///      Python verifier shipped with the repo) and confirm it agrees
///      the migration is byte-preserving.
///   4. Round-trip: revert the migration via the recorded log and
///      assert byte-equality with the original.
///
/// This is the "gap-b" integration test from the gap-closure plan — it
/// proves the C# producer and the Python consumer agree on the
/// migration log schema. The test is skipped (not failed) when
/// <c>python</c> is not on PATH so it remains green on minimal CI
/// images.
/// </summary>
public class EndToEndMigrationTests
{
    private static readonly StructuralIdDropper Dropper = new();
    private static readonly UTF8Encoding Utf8 = new(false);

    [Fact]
    public void PipelineRoundTripsAndPythonVerifierAgrees()
    {
        var python = ResolvePython();
        if (python is null)
        {
            // Document the skip in the test log so it is visible in CI.
            Console.WriteLine("[skip] python not on PATH; end-to-end test skipped");
            return;
        }

        var script = ResolveScript("byte_preservation_check.py");
        if (script is null)
        {
            Console.WriteLine("[skip] byte_preservation_check.py not found; test skipped");
            return;
        }

        var tmp = Directory.CreateTempSubdirectory("calor-e2e-");
        try
        {
            // 1. Synthesize a legacy file with multiple structural IDs.
            //    The migrator records files relative to the migration
            //    root, so we keep the backup in a sibling directory
            //    with the same basename for the Python verifier to
            //    match by name.
            var rel = "calc.calr";
            var origDir = Directory.CreateDirectory(Path.Combine(tmp.FullName, "orig"));
            var migDir = Directory.CreateDirectory(Path.Combine(tmp.FullName, "mig"));
            var origPath = Path.Combine(origDir.FullName, rel);
            var migPath = Path.Combine(migDir.FullName, rel);

            const string ulidA = "m_01j5x7abcdef01j5x7abcdef01";
            const string ulidB = "f_01j5x7abcdef01j5x7abcdef02";
            var originalSource =
                $"§M{{{ulidA}:Calculator}}\n" +
                $"§F{{{ulidB}:add:i32:public}}\n" +
                $"§/F{{{ulidB}}}\n" +
                $"§/M{{{ulidA}}}\n";
            File.WriteAllText(origPath, originalSource, Utf8);

            // 2. Run the dropper in-process (mirrors what `calor fix
            //    --drop-structural-ids` does).
            var (migrated, removals) = Dropper.Process(originalSource, rel);
            File.WriteAllText(migPath, migrated, Utf8);

            var logPath = Path.Combine(tmp.FullName, "migration.log.json");
            var log = new StructuralIdDropper.MigrationLog();
            log.Entries.AddRange(removals);
            File.WriteAllText(logPath, StructuralIdDropper.SerializeLog(log), Utf8);

            // 3. Invoke the Python verifier. Exit 0 = byte-preservation
            //    holds. Pass the originals and migrated files keeping
            //    the same basename so the verifier's name-based log
            //    matching succeeds.
            var psi = new ProcessStartInfo(python)
            {
                ArgumentList =
                {
                    script,
                    origPath,
                    migPath,
                    "--log",
                    logPath,
                },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var proc = Process.Start(psi)!;
            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit();

            Assert.True(
                proc.ExitCode == 0,
                $"python verifier failed with exit {proc.ExitCode}.\n" +
                $"stdout:\n{stdout}\nstderr:\n{stderr}");

            // 4. Round-trip in-process via the BytePreservationVerifier.
            var ok = BytePreservationVerifier.Verify(
                Utf8.GetBytes(originalSource),
                Utf8.GetBytes(migrated),
                removals,
                out var reason);
            Assert.True(ok, reason);
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void PipelineIsNoOpForCompactFormCorpus()
    {
        // Sanity: when there is nothing legacy to migrate, the dropper
        // must report zero removals and leave files untouched.
        var tmp = Directory.CreateTempSubdirectory("calor-e2e-noop-");
        try
        {
            var path = Path.Combine(tmp.FullName, "compact.calr");
            const string compact = "§M{Calculator}\n§F{add:i32:public}\n§/F\n§/M\n";
            File.WriteAllText(path, compact, Utf8);

            var (out_, removals) = Dropper.Process(compact, "compact.calr");
            Assert.Equal(compact, out_);
            Assert.Empty(removals);
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { /* best-effort */ }
        }
    }

    private static string? ResolvePython()
    {
        foreach (var name in new[] { "python3", "python", "py" })
        {
            try
            {
                var psi = new ProcessStartInfo(name)
                {
                    ArgumentList = { "--version" },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                };
                using var p = Process.Start(psi);
                if (p == null) continue;
                p.WaitForExit(5000);
                if (p.ExitCode == 0) return name;
            }
            catch
            {
                // try next candidate
            }
        }
        return null;
    }

    private static string? ResolveScript(string name)
    {
        // Walk upward from the test assembly looking for scripts/<name>.
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir != null; i++)
        {
            var candidate = Path.Combine(dir, "scripts", name);
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }
}
