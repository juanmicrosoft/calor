using Calor.Compiler.Analysis.BugPatterns;
using Calor.Compiler.Analysis.BugPatterns.Patterns;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;
using Xunit.Abstractions;

namespace Calor.Compiler.Tests.Analysis;

/// <summary>
/// Corpus scan harness for the TIER1A post-mortem's §6 shape-Calor test.
/// Runs both <see cref="NullDereferenceChecker"/> and the reconstructed
/// <see cref="OptionResultFlowChecker"/> against a set of `.calr` files, then
/// reports absolute counts and the INCREMENTAL finding count (sites where
/// TIER1A fires but NullDereferenceChecker does not).
///
/// Skipped by default. Invoke manually with:
///   dotnet test --filter "FullyQualifiedName~OptionResultFlowCheckerCorpusScan"
/// </summary>
public class OptionResultFlowCheckerCorpusScan
{
    private readonly ITestOutputHelper _output;

    public OptionResultFlowCheckerCorpusScan(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void ScanPilot()
    {
        var repoRoot = FindRepoRoot();
        var pilotDir = Path.Combine(repoRoot, "tmp");
        if (!Directory.Exists(pilotDir))
        {
            _output.WriteLine($"[skip] Pilot directory does not exist: {pilotDir}");
            return;
        }
        ScanDirectory(pilotDir, "pilot_");
    }

    [Fact]
    public void ScanScale()
    {
        var repoRoot = FindRepoRoot();
        var pilotDir = Path.Combine(repoRoot, "tmp");
        if (!Directory.Exists(pilotDir))
        {
            _output.WriteLine($"[skip] tmp/ does not exist: {pilotDir}");
            return;
        }
        ScanDirectory(pilotDir, "scale_");
    }

    [Fact(Skip = "Manual diagnostic — runs against tmp/ (d)-test corpus")]
    public void ScanDTest()
    {
        var repoRoot = FindRepoRoot();
        var pilotDir = Path.Combine(repoRoot, "tmp");
        if (!Directory.Exists(pilotDir))
        {
            _output.WriteLine($"[skip] tmp/ does not exist: {pilotDir}");
            return;
        }
        ScanDirectory(pilotDir, "test_d_");
    }

    private void ScanDirectory(string dir, string filenamePrefix)
    {
        var files = Directory.EnumerateFiles(dir, "*.calr", SearchOption.TopDirectoryOnly)
            .Where(p => Path.GetFileName(p).StartsWith(filenamePrefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        _output.WriteLine($"Scanning {files.Count} files from {dir} (prefix '{filenamePrefix}')...");

        int idx = 0;
        int totalParseSkipped = 0;
        int totalNullDeref = 0;
        int totalFlow = 0;
        int totalIncrementalFlow = 0;

        foreach (var file in files)
        {
            idx++;
            var name = Path.GetFileName(file);
            var result = ScanOne(file);
            if (result == null)
            {
                totalParseSkipped++;
                _output.WriteLine($"[{idx:D2}] {name} — PARSE/BIND SKIPPED");
                continue;
            }

            totalNullDeref += result.NullDerefFindings.Count;
            totalFlow += result.FlowFindings.Count;

            // Incremental: TIER1A finds that NullDereferenceChecker does not
            // (by span line + receiver-name overlap).
            var nullDerefSet = new HashSet<string>(
                result.NullDerefFindings.Select(d => $"{d.Span.Line}:{ExtractReceiverHint(d.Message)}"));
            var incremental = result.FlowFindings
                .Where(d => !nullDerefSet.Contains($"{d.Span.Line}:{ExtractReceiverHint(d.Message)}"))
                .ToList();
            totalIncrementalFlow += incremental.Count;

            _output.WriteLine($"[{idx:D2}] {name}");
            _output.WriteLine($"     NullDerefChecker: {result.NullDerefFindings.Count}");
            foreach (var d in result.NullDerefFindings)
                _output.WriteLine($"       line {d.Span.Line}: {d.Code} — {d.Message}");
            _output.WriteLine($"     OptionResultFlowChecker: {result.FlowFindings.Count} ({incremental.Count} incremental)");
            foreach (var d in result.FlowFindings)
                _output.WriteLine($"       line {d.Span.Line}: {d.Code} — {d.Message}");
        }

        _output.WriteLine("");
        _output.WriteLine("========================================");
        _output.WriteLine($"Files scanned: {files.Count}");
        _output.WriteLine($"Parse/bind skipped: {totalParseSkipped}");
        _output.WriteLine($"Total NullDerefChecker findings: {totalNullDeref}");
        _output.WriteLine($"Total OptionResultFlowChecker findings: {totalFlow}");
        _output.WriteLine($"Incremental (TIER1A only): {totalIncrementalFlow}");
        _output.WriteLine("========================================");
    }

    private sealed record ScanResult(
        IReadOnlyList<Diagnostic> NullDerefFindings,
        IReadOnlyList<Diagnostic> FlowFindings);

    private static ScanResult? ScanOne(string path)
    {
        var source = File.ReadAllText(path);
        var parseDiag = new DiagnosticBag();
        parseDiag.SetFilePath(path);

        var tokens = new Lexer(source, parseDiag).TokenizeAll();
        if (parseDiag.HasErrors) return null;
        var module = new Parser(tokens, parseDiag).Parse();
        if (parseDiag.HasErrors) return null;

        var bindDiag = new DiagnosticBag();
        bindDiag.SetFilePath(path);
        var bound = new Binder(bindDiag).Bind(module);
        if (bindDiag.HasErrors) return null;

        var nullDeref = new DiagnosticBag();
        nullDeref.SetFilePath(path);
        var flow = new DiagnosticBag();
        flow.SetFilePath(path);

        var ndOptions = new BugPatternOptions { UseZ3Verification = false };
        var ndChecker = new NullDereferenceChecker(ndOptions);
        var flowChecker = new OptionResultFlowChecker();

        foreach (var func in bound.Functions)
        {
            ndChecker.Check(func, nullDeref);
            flowChecker.Check(func, flow);
        }

        return new ScanResult(
            nullDeref.Where(d =>
                d.Code == DiagnosticCode.UnsafeUnwrap ||
                d.Code == DiagnosticCode.NullDereference).ToList(),
            flow.Where(d => d.Code == DiagnosticCode.UnsafeUnwrapFlow).ToList());
    }

    private static string ExtractReceiverHint(string message)
    {
        // Messages contain 'varname' quoted; extract for overlap matching.
        var first = message.IndexOf('\'');
        if (first < 0) return message;
        var second = message.IndexOf('\'', first + 1);
        if (second <= first) return message;
        return message[(first + 1)..second];
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 20; i++)
        {
            if (Directory.Exists(Path.Combine(dir, ".git"))) return dir;
            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }
        throw new InvalidOperationException("Could not find repo root");
    }
}
