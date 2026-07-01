// migrate_inline_calor: skip
using Calor.Compiler.Analysis;
using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Pins the corpus-clean result for <c>Calor0205 ReturnValueInVoidOwner</c>.
///
/// <para><see cref="ReturnValidationPass"/> is always-on and reports a hard
/// error, so the single most important guarantee is that it never fires on
/// legal in-repo code. This test runs the pass against every <c>.calr</c> file
/// under <c>samples/</c> and <c>tests/TestData/Benchmarks/</c> and asserts zero
/// firings. If a corpus file would emit a <c>Calor0205</c>, the test fails with
/// the file path and message so the regression is immediately attributable.</para>
///
/// <para>Lexer/parser failures are tolerated (some corpus files use
/// experimental or migration-pending shapes unrelated to return validation);
/// only the well-parsed subset is audited.</para>
/// </summary>
public class ReturnValidationCorpusCleanTests
{
    [Fact]
    public void Corpus_HasZeroReturnValidationFirings()
    {
        var roots = ResolveCorpusRoots();
        Assert.NotEmpty(roots);

        var calrFiles = roots
            .SelectMany(r => Directory.EnumerateFiles(r, "*.calr", SearchOption.AllDirectories))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(calrFiles);

        var failures = new List<string>();

        foreach (var file in calrFiles)
        {
            var source = File.ReadAllText(file);
            var diagnostics = new DiagnosticBag();

            var lexer = new Lexer(source, diagnostics);
            var tokens = lexer.TokenizeAllForParser();

            // Skip files with lex errors — not in scope for this audit.
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

            // Skip parse failures (corpus contains intentionally broken fixtures
            // + migration-pending shapes outside our scope).
            if (diagnostics.HasErrors) continue;

            var passDiagnostics = new DiagnosticBag();
            var pass = new ReturnValidationPass(passDiagnostics);
            pass.Check(module);

            foreach (var d in passDiagnostics)
            {
                if (d.Code == DiagnosticCode.ReturnValueInVoidOwner)
                {
                    failures.Add($"{file}: {d.Code} — {d.Message}");
                }
            }
        }

        Assert.True(
            failures.Count == 0,
            $"Return-validation corpus audit failed: expected zero firings of Calor0205 across " +
            $"{calrFiles.Count} .calr files, found {failures.Count}:\n  " +
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
        var assemblyDir = Path.GetDirectoryName(typeof(ReturnValidationCorpusCleanTests).Assembly.Location);
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
