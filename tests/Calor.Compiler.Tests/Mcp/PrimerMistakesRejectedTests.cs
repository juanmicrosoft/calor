using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Calor.Compiler;
using Calor.Compiler.Effects;
using Calor.Compiler.Mcp;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests.Mcp;

/// <summary>
/// Semantic guard for the <c>calor://primer</c> MCP resource (v0.7 plan, item D2a) — the
/// dual of <see cref="PrimerCompilesTests"/>. PrimerCompilesTests proves every CORRECT
/// module the primer teaches actually compiles; this proves every example in the primer's
/// "Common mistakes (these do NOT compile)" section genuinely FAILS to compile, so the docs
/// cannot lie in the other direction (a "wrong" snippet that silently compiles would
/// mis-teach the agent following Calor's own primer).
///
/// The mistakes are fragments (a §S clause, an §IF header, a §F header, a closer line), so
/// each is rewritten into the smallest complete module where it would naturally appear, then
/// checked for non-compilation at EITHER layer:
///   1. Calor itself reports an error (HasErrors), or
///   2. the generated C# fails to compile under Roslyn — e.g. the 4-field §F header drops the
///      return type, emitting <c>void Add() { return 0; }</c> =&gt; CS0127, which Calor does not
///      flag today but csc does.
/// Two drift guards keep this curated set and the primer text in lock-step.
/// </summary>
public class PrimerMistakesRejectedTests
{
    private sealed record Mistake(string Name, string Fragment, bool TopLevel);

    /// <summary>
    /// Mirrors the primer's "Common mistakes (these do NOT compile)" block, verbatim. Each
    /// fragment must both appear in that block (<see cref="Primer_ListsEachCuratedMistake"/>)
    /// and fail to compile when wrapped (<see cref="Mistake_DoesNotCompile"/>). The 4-field
    /// §F header is placed at module scope (TopLevel); the rest are body/contract clauses
    /// inside a function.
    /// </summary>
    private static readonly Mistake[] Mistakes =
    {
        new("legacy-closers", "§/F §/M §/I §/L", TopLevel: false),
        new("uppercase-result", "§S (>= §RESULT 0)", TopLevel: false),
        new("if-missing-id", "§IF (> x 0)", TopLevel: false),
        new("if-condition-in-braces", "§IF{i1}{x > 0}", TopLevel: false),
        new("nonexistent-keywords", "§K   §ELSE", TopLevel: false),
        new("four-field-header", "§F{f1:Add:i32:pub}", TopLevel: true),
    };

    public static IEnumerable<object[]> MistakeCases() =>
        Mistakes.Select(m => new object[] { m.Name, m.Fragment, m.TopLevel });

    [Theory]
    [MemberData(nameof(MistakeCases))]
    public void Mistake_DoesNotCompile(string name, string fragment, bool topLevel)
    {
        var module = topLevel
            ? $"§M{{m1:Probe}}\n{fragment}\n  §R INT:0\n"
            : $"§M{{m1:Probe}}\n§F{{f1:Probe:pub}} (i32:x) -> i32\n  {fragment}\n  §R x\n";

        var result = Compile(module, "primer-mistake.calr");

        // Layer 1: rejected outright by Calor.
        if (result.Diagnostics.Any(d => d.IsError))
        {
            return;
        }

        // Layer 2: Calor accepted it, so the generated C# must itself fail to compile.
        var csErrors = RoslynErrors(result.GeneratedCode);
        Assert.True(
            csErrors.Count > 0,
            $"Primer mistake '{name}' is documented as NOT compiling, but it produced no Calor " +
            "error AND its generated C# compiled cleanly. Either the example is no longer a " +
            "mistake, or the language now accepts it — fix the primer or this curated set.\n" +
            $"--- module ---\n{module}\n--- generated C# ---\n{result.GeneratedCode}");
    }

    [Fact]
    public void CorrectModule_CompilesAtBothLayers()
    {
        // Sanity for the two-layer check itself: a correct module must pass BOTH layers, so a
        // green above cannot be an artifact of RoslynErrors spuriously rejecting everything.
        const string correct =
            "§M{m1:Probe}\n§F{f1:Add:pub} (i32:a, i32:b) -> i32\n  §R (+ a b)\n";

        var result = Compile(correct, "primer-correct.calr");

        Assert.DoesNotContain(result.Diagnostics, d => d.IsError);
        Assert.Empty(RoslynErrors(result.GeneratedCode));
    }

    [Fact]
    public void Primer_ListsEachCuratedMistake()
    {
        // Drift guard: every curated fragment must still appear (whitespace-normalized) in the
        // primer's mistakes block, so this test and the docs cannot silently diverge.
        var normBlock = Collapse(MistakesBlock());
        foreach (var m in Mistakes)
        {
            Assert.True(
                normBlock.Contains(Collapse(m.Fragment), StringComparison.Ordinal),
                $"Curated mistake '{m.Name}' ({m.Fragment}) is no longer present in the primer's " +
                "\"Common mistakes\" block. Update the curated set to match the primer.");
        }
    }

    [Fact]
    public void Primer_MistakeCount_MatchesCuratedSet()
    {
        // Drift guard the other way: adding a mistake to the primer without a curated case +
        // assertion here fails this test. Mistake lines carry an explanation lead-in.
        var count = MistakesBlock()
            .Split('\n')
            .Count(line =>
                line.Contains("wrong;", StringComparison.Ordinal) ||
                line.Contains("removed;", StringComparison.Ordinal) ||
                line.Contains("no such", StringComparison.Ordinal));

        Assert.Equal(Mistakes.Length, count);
    }

    // --- helpers ---

    private static CompilationResult Compile(string source, string fileName)
    {
        // Mirror the calor_compile MCP tool's default options (CompileTool.cs): EnforceEffects
        // defaults to true; effectMode "default" => Strict policy, non-strict.
        var options = new Calor.Compiler.CompilationOptions
        {
            ContractMode = ContractMode.Debug,
            UnknownCallPolicy = UnknownCallPolicy.Strict,
            StrictEffects = false,
            VerifyContracts = false,
        };

        return Program.Compile(source, fileName, options);
    }

    private static string MistakesBlock()
    {
        var lines = McpResourceValidator.GetPrimer().Replace("\r\n", "\n").Split('\n');
        var start = Array.FindIndex(lines, l => l.Contains("Common mistakes", StringComparison.Ordinal));
        Assert.True(start >= 0, "Primer no longer has a \"Common mistakes\" section.");

        var end = Array.FindIndex(lines, start + 1, l => l.TrimStart().StartsWith("-- ", StringComparison.Ordinal));
        if (end < 0) end = lines.Length;

        return string.Join("\n", lines[(start + 1)..end]);
    }

    private static string Collapse(string s) => Regex.Replace(s, @"\s+", " ").Trim();

    private static IReadOnlyList<Diagnostic> RoslynErrors(string csharpSource)
    {
        var tree = CSharpSyntaxTree.ParseText(csharpSource);

        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(List<>).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Runtime.AssemblyTargetedPatchBandAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!,
                "System.Runtime.dll")),
        };

        var compilation = CSharpCompilation.Create(
            "PrimerMistakeTest",
            new[] { tree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            // The emitter adds `using Calor.Runtime;`; correct, but the runtime isn't
            // referenced in this minimal compilation. Ignore those reference-only errors.
            .Where(d => !d.GetMessage().Contains("'Calor'"))
            .ToArray();
    }
}
