using Calor.Compiler;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// v0.6.7 Item 1a — the parser flags a malformed four-field function header
/// <c>§F{id:name:type:vis}</c> / <c>§AF{id:name:type:vis}</c> with
/// <c>Calor0116 MalformedFunctionHeader</c> and an auto-applicable
/// <see cref="SuggestedFix"/>.
///
/// <para>
/// Canonical function headers are three-field (<c>§F{id:name:vis}</c>) with the
/// return type declared in the signature (<c>(...) -&gt; type</c>). A four-field
/// header silently misreads the return type as the visibility, drops the real
/// visibility, and emits a <c>void</c> method that returns a value — a CS0127 in
/// the generated C# with no Calor-level explanation. This diagnostic closes that
/// gap. It is deliberately narrow: it fires ONLY when the fourth field is a
/// visibility token, and it is wired ONLY into <c>§F</c>/<c>§AF</c> — never into
/// <c>§MT</c>/<c>§AMT</c>, whose fourth field is a legitimate modifier.
/// </para>
/// </summary>
public class MalformedFunctionHeaderTests
{
    private static (IList<Diagnostic> Diagnostics, bool HasErrors) Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        _ = parser.Parse();
        return (diagnostics.ToList(), diagnostics.HasErrors);
    }

    private static DiagnosticBag ParseBag(string source)
    {
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        _ = parser.Parse();
        return diagnostics;
    }

    // ---------------------------------------------------------------------
    // Positive — fires on malformed four-field §F / §AF headers.
    // ---------------------------------------------------------------------

    [Fact]
    public void Function_FourFieldHeader_EmitsCalor0116()
    {
        const string src = """
            §M{m1:Calc}
              §F{f1:Add:i32:pub}
                §R (+ 1 2)
            """;
        var (diags, _) = Parse(src);

        var malformed = diags.Where(d => d.Code == DiagnosticCode.MalformedFunctionHeader).ToList();
        Assert.Single(malformed);
        Assert.Contains("four fields", malformed[0].Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("i32", malformed[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AsyncFunction_FourFieldHeader_EmitsCalor0116()
    {
        const string src = """
            §M{m1:Calc}
              §AF{f1:FetchAsync:i32:pub}
                §R (+ 1 2)
            """;
        var (diags, _) = Parse(src);

        var malformed = diags.Where(d => d.Code == DiagnosticCode.MalformedFunctionHeader).ToList();
        Assert.Single(malformed);
        Assert.Contains("four fields", malformed[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------------
    // Negative — valid forms must NOT fire.
    // ---------------------------------------------------------------------

    [Fact]
    public void Function_ThreeFieldHeader_NoCalor0116()
    {
        const string src = """
            §M{m1:Calc}
              §F{f1:Add:pub} (i32:a, i32:b) -> i32
                §R (+ a b)
            """;
        var (diags, hasErrors) = Parse(src);

        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCode.MalformedFunctionHeader);
        Assert.False(hasErrors, "Canonical three-field header must parse cleanly");
    }

    [Fact]
    public void Method_FourFieldModifier_NoCalor0116()
    {
        // §MT's fourth field is a legitimate modifier (virt) — never Calor0116.
        const string src = """
            §M{m1:Calc}
              §CL{c1:Greeter:pub}
                §MT{mt1:Name:pub:virt} () -> str
                  §R STR:"x"
            """;
        var (diags, _) = Parse(src);

        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCode.MalformedFunctionHeader);
    }

    [Fact]
    public void AsyncMethod_FourFieldModifier_NoCalor0116()
    {
        // §AMT's fourth field is a legitimate modifier (virt) — never Calor0116.
        const string src = """
            §M{m1:Calc}
              §CL{c1:Svc:pub}
                §AMT{mt1:GetAsync:pub:virt} () -> i32
                  §R (+ 1 2)
            """;
        var (diags, _) = Parse(src);

        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCode.MalformedFunctionHeader);
    }

    [Fact]
    public void Function_FourFieldHeader_NonVisibilityFourthField_NoCalor0116()
    {
        // Narrowness guard: the diagnostic requires the FOURTH field to be a
        // visibility token. A non-visibility fourth field is some other shape
        // and must not be misattributed to the malformed-header pattern.
        const string src = """
            §M{m1:Calc}
              §F{f1:Add:i32:frobnicate}
                §R (+ 1 2)
            """;
        var (diags, _) = Parse(src);

        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCode.MalformedFunctionHeader);
    }

    // ---------------------------------------------------------------------
    // Fix — structure and healing behaviour.
    // ---------------------------------------------------------------------

    [Fact]
    public void Fix_HeaderOnly_CarriesReplaceAndSignatureInsert()
    {
        const string src = """
            §M{m1:Calc}
              §F{f1:Add:i32:pub}
                §R (+ 1 2)
            """;
        var bag = ParseBag(src);

        var dwf = Assert.Single(
            bag.DiagnosticsWithFixes.Where(d => d.Code == DiagnosticCode.MalformedFunctionHeader));

        // Two edits: rewrite the header to three fields, then append a signature
        // so the healed function keeps its return type instead of becoming void.
        Assert.Equal(2, dwf.Fix.Edits.Count);
        Assert.Contains(dwf.Fix.Edits, e => e.NewText == "{f1:Add:pub}");
        Assert.Contains(dwf.Fix.Edits, e => e.NewText == " () -> i32");
    }

    [Fact]
    public void ApplyingFix_HeaderOnly_HealsToReturningFunction()
    {
        const string src = """
            §M{m1:Calc}
              §F{f1:Add:i32:pub}
                §R (+ 1 2)
            """;
        var bag = ParseBag(src);
        var healed = ApplyFixes(src, bag);

        Assert.Contains("§F{f1:Add:pub} () -> i32", healed);
        Assert.DoesNotContain("i32:pub", healed);

        // Healed source parses cleanly (no residual Calor0116).
        var (diags, hasErrors) = Parse(healed);
        Assert.False(hasErrors, "Healed source must parse cleanly");
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCode.MalformedFunctionHeader);

        // And it compiles to a value-returning method, not a void one.
        var result = Compile(healed);
        Assert.DoesNotContain(result.Diagnostics, d => d.IsError);
        Assert.Contains("int Add(", result.GeneratedCode);
        Assert.DoesNotContain("void Add(", result.GeneratedCode);
    }

    [Fact]
    public void ApplyingFix_WithInlineSignature_DropsHeaderTypeOnly()
    {
        // When an inline signature already follows the header, the fix must drop
        // ONLY the header type — it must not append a second signature.
        const string src = """
            §M{m1:Calc}
              §F{f1:Add:i32:pub} (i32:a) -> i32
                §R a
            """;
        var bag = ParseBag(src);

        var dwf = Assert.Single(
            bag.DiagnosticsWithFixes.Where(d => d.Code == DiagnosticCode.MalformedFunctionHeader));
        var edit = Assert.Single(dwf.Fix.Edits);
        Assert.Equal("{f1:Add:pub}", edit.NewText);

        var healed = ApplyFixes(src, bag);
        Assert.Contains("§F{f1:Add:pub} (i32:a) -> i32", healed);
        // Exactly one signature arrow — no duplicated "() -> i32" tail.
        Assert.Equal(1, CountOccurrences(healed, "-> i32"));

        var (diags, hasErrors) = Parse(healed);
        Assert.False(hasErrors, "Healed source must parse cleanly");
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCode.MalformedFunctionHeader);
    }

    // ---------------------------------------------------------------------
    // Corpus pin — the in-repo corpus contains zero malformed four-field
    // §F/§AF headers, so Calor0116 must never fire across it. This also
    // guards the never-touch invariant: the §MT-rich benchmark corpus
    // (DesignPatterns etc.) uses many valid four-field modifier headers.
    // ---------------------------------------------------------------------

    [Fact]
    public void Corpus_HasZeroMalformedFunctionHeaderFirings()
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

            try
            {
                var parser = new Parser(tokens, diagnostics);
                _ = parser.Parse();
            }
            catch
            {
                // Corpus contains some intentionally broken / migration-pending
                // fixtures; a parser throw is unrelated to this audit.
                continue;
            }

            foreach (var d in diagnostics)
            {
                if (d.Code == DiagnosticCode.MalformedFunctionHeader)
                {
                    failures.Add($"{file}: {d.Message}");
                }
            }
        }

        Assert.True(
            failures.Count == 0,
            $"Malformed-function-header corpus audit failed: expected zero firings of Calor0116 " +
            $"across {calrFiles.Count} .calr files, found {failures.Count}:\n  " +
            string.Join("\n  ", failures));
    }

    // ---------------------------------------------------------------------
    // Helpers.
    // ---------------------------------------------------------------------

    private static CompilationResult Compile(string source)
    {
        var options = new CompilationOptions
        {
            ContractMode = ContractMode.Debug,
            UnknownCallPolicy = UnknownCallPolicy.Strict,
            StrictEffects = false,
            VerifyContracts = false,
        };
        return Program.Compile(source, "malformed-header.calr", options);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }

    /// <summary>
    /// Mirrors the line/column edit application used by the LSP code-action
    /// handler and the calor_check MCP tool (apply path): edits are applied
    /// bottom-up (and right-to-left within a line) so earlier positions stay
    /// valid.
    /// </summary>
    private static string ApplyFixes(string source, DiagnosticBag bag)
    {
        var edits = bag.DiagnosticsWithFixes
            .SelectMany(d => d.Fix.Edits)
            .OrderByDescending(e => e.StartLine)
            .ThenByDescending(e => e.StartColumn)
            .ToList();

        var lines = source.Replace("\r\n", "\n").Split('\n');

        foreach (var edit in edits)
        {
            var startLine = edit.StartLine - 1;
            var startCol = edit.StartColumn - 1;
            var endLine = edit.EndLine - 1;
            var endCol = edit.EndColumn - 1;

            if (startLine < 0 || startLine >= lines.Length) continue;
            if (endLine < 0 || endLine >= lines.Length) continue;

            var beforeEdit = startCol >= 0 && startCol <= lines[startLine].Length
                ? lines[startLine][..startCol]
                : lines[startLine];
            var afterEdit = endCol >= 0 && endCol <= lines[endLine].Length
                ? lines[endLine][endCol..]
                : "";

            var newContent = beforeEdit + edit.NewText + afterEdit;
            var newLines = newContent.Split('\n');

            var lineList = lines.ToList();
            lineList.RemoveRange(startLine, endLine - startLine + 1);
            lineList.InsertRange(startLine, newLines);
            lines = lineList.ToArray();
        }

        return string.Join('\n', lines);
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
        var assemblyDir = Path.GetDirectoryName(typeof(MalformedFunctionHeaderTests).Assembly.Location);
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
