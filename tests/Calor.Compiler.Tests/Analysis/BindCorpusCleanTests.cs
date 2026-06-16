// migrate_inline_calor: skip
using Calor.Compiler.Analysis;
using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Pins the v0.6.4 corpus-clean result for the bind-inference diagnostics
/// (<c>Calor0250 BindRequiresTypeOrInitializer</c> plus the strict-mode
/// trio <c>Calor0251 BindCannotInferNullLiteral</c>,
/// <c>Calor0252 BindCannotInferGenericReturn</c>,
/// <c>Calor0253 BindAmbiguousNumeric</c>).
///
/// <para>
/// Runs <see cref="BindValidationPass"/> with strict inference on against every
/// <c>.calr</c> file under <c>samples/</c> and <c>tests/TestData/Benchmarks/</c>
/// and asserts zero firings. Closes the v0.6 bind-inference-formalization
/// RFC §7 open question ("Should <c>Calor0250</c> be promoted from warning
/// to error in v0.7?") with a permanent CI-enforced pin: the diagnostic has
/// been a hard error since v0.6.0 (see <c>Binder.cs:279</c> and
/// <c>BindValidationPass.cs:223</c>) and the in-repo corpus has zero firings.
/// </para>
///
/// <para>
/// Lexer/parser failures are tolerated (some corpus files use experimental or
/// migration-pending shapes that are unrelated to bind-inference); only the
/// well-parsed subset is audited. If a corpus file would emit a Calor025x
/// firing, this test fails with the file path and diagnostic message so the
/// regression is immediately attributable.
/// </para>
/// </summary>
public class BindCorpusCleanTests
{
    [Fact]
    public void Corpus_HasZeroBindInferenceFirings()
    {
        var roots = ResolveCorpusRoots();
        Assert.NotEmpty(roots);

        var calrFiles = roots
            .SelectMany(r => Directory.EnumerateFiles(r, "*.calr", SearchOption.AllDirectories))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(calrFiles);

        var bindCodes = new[]
        {
            DiagnosticCode.BindRequiresTypeOrInitializer,
            DiagnosticCode.BindCannotInferNullLiteral,
            DiagnosticCode.BindCannotInferGenericReturn,
            DiagnosticCode.BindAmbiguousNumeric,
        };

        var failures = new List<string>();

        foreach (var file in calrFiles)
        {
            var source = File.ReadAllText(file);
            var diagnostics = new DiagnosticBag();

            var lexer = new Lexer(source, diagnostics);
            var tokens = lexer.TokenizeAllForParser();

            // Skip files with lex errors — they're not in scope for this audit.
            if (diagnostics.HasErrors) continue;

            var parser = new Parser(tokens, diagnostics);
            ModuleNode module;
            try
            {
                module = parser.Parse();
            }
            catch
            {
                continue;
            }

            // Skip parse failures (corpus contains some intentionally broken
            // fixtures + bench/migration-pending shapes outside our scope).
            if (diagnostics.HasErrors) continue;

            var bindDiagnostics = new DiagnosticBag();
            var validator = new BindValidationPass(bindDiagnostics, source, strictInference: true);
            validator.Check(module);

            foreach (var d in bindDiagnostics)
            {
                if (bindCodes.Contains(d.Code))
                {
                    failures.Add($"{file}: {d.Code} — {d.Message}");
                }
            }
        }

        Assert.True(
            failures.Count == 0,
            $"Bind-inference corpus audit failed: expected zero firings of Calor0250/0251/0252/0253 across {calrFiles.Count} .calr files, found {failures.Count}:\n  " +
            string.Join("\n  ", failures));
    }

    private static IReadOnlyList<string> ResolveCorpusRoots()
    {
        var repoRoot = FindRepoRoot();
        if (repoRoot == null) return Array.Empty<string>();

        var roots = new List<string>();
        foreach (var rel in new[]
        {
            Path.Combine("samples"),
            Path.Combine("tests", "TestData", "Benchmarks"),
        })
        {
            var full = Path.Combine(repoRoot, rel);
            if (Directory.Exists(full)) roots.Add(full);
        }
        return roots;
    }

    private static string? FindRepoRoot()
    {
        // Walk up from the test assembly's location looking for a directory that
        // contains both `samples` and `tests/TestData/Benchmarks` — the two
        // corpus roots we audit.
        var assemblyDir = Path.GetDirectoryName(typeof(BindCorpusCleanTests).Assembly.Location);
        var dir = assemblyDir != null ? new DirectoryInfo(assemblyDir) : null;
        while (dir != null)
        {
            var samples = Path.Combine(dir.FullName, "samples");
            var benchmarks = Path.Combine(dir.FullName, "tests", "TestData", "Benchmarks");
            if (Directory.Exists(samples) && Directory.Exists(benchmarks))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        return null;
    }
}
