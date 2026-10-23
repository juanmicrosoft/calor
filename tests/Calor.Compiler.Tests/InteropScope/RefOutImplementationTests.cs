using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Nodes;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Commands;
using Calor.Compiler.Mcp.Tools;
using Calor.Compiler.Migration;
using Calor.Compiler.Verification.Obligations;
using Calor.Compiler.Verification.Z3;
using Calor.Compiler.Verification.Z3.Cache;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests.InteropScope;

/// <summary>
/// #943 (0.25 F1): ref/out/in arguments per the D1 contract (#1427). Language rows compile Calor
/// and check the refusal or acceptance (with a negative control for every refusal); conversion
/// rows convert every R0 F1 fixture, the F1 interaction fixtures and every D1 C# witness on the
/// library, CLI and MCP surfaces, compile, run, and compare with the original C#.
/// </summary>
public class RefOutImplementationTests
{
    private const string Prelude = """
        §M{m1:P}
          §F{f0:Bump:pub} (i32:x:ref) -> void
            §ASSIGN x (+ x 1)
          §F{f9:SetOut:pub} (i32:x:out) -> void
            §ASSIGN x 7
          §F{f6:SetNeg:pub} (i32:x:out) -> void
            §ASSIGN x -5
          §F{f8:Twice:pub} (i32:x:in) -> i32
            §R (* x 2)
          §F{f7:Id:pub} (i32:x) -> i32
            §R x

        """;

    private static string[] Errors(string body, bool verify = false, CompilationOptions? options = null)
    {
        options ??= new CompilationOptions
        {
            VerifyContracts = verify, VerifyRefinements = verify,
            VerificationCacheOptions = new VerificationCacheOptions { Enabled = false },
        };
        var result = Program.Compile(Prelude + body, "refout.calr", options);
        return result.Diagnostics.Where(d => d.IsError).Select(d => d.Code).Distinct().OrderBy(c => c, StringComparer.Ordinal).ToArray();
    }

    // Each refusal (Calor0212) next to the closest accepted form: the operand rule, not the call, decides.
    [Theory]
    [InlineData("property",
        "  §CL{c1:K:pub}\n    §PROP{p1:N:i32:pub}\n      §GET\n      §SET\n    §MT{m1:Run:pub} () -> void\n      §C{Bump} §A{ref} N §/C",
        "  §CL{c1:K:pub}\n    §FLD{i32:N:pub}\n    §MT{m1:Run:pub} () -> void\n      §C{Bump} §A{ref} N §/C")]
    [InlineData("static readonly field outside its static constructor",
        "  §CL{c1:K:pub:stat}\n    §FLD{i32:n:pub:stat,readonly}\n    §MT{m1:Run:pub:stat} () -> void\n      §C{Bump} §A{ref} n §/C",
        "  §CL{c1:K:pub:stat}\n    §FLD{i32:n:pub:stat,readonly}\n    §MT{m1:Run:pub:stat} () -> i32\n      §R §C{Twice} §A{in} n §/C")]
    [InlineData("in parameter passed as ref",
        "  §F{f1:Run:pub} (i32:p:in) -> void\n    §C{Bump} §A{ref} p §/C",
        "  §F{f1:Run:pub} (i32:p:in) -> i32\n    §R §C{Twice} §A{in} p §/C")]
    [InlineData("call result",
        "  §F{f1:Run:pub} () -> void\n    §C{Bump} §A{ref} §C{Twice} §A{in} 3 §/C §/C",
        "  §F{f1:Run:pub} () -> void\n    §B{~v:i32} §C{Id} §A 3 §/C\n    §C{Bump} §A{ref} v §/C")]
    [InlineData("list indexer element",
        "  §F{f1:Run:pub} (List<i32>:xs) -> void\n    §E{mut}\n    §C{Bump} §A{ref} §IDX{xs} 0 §/C",
        "  §F{f1:Run:pub} (i32[]:xs) -> void\n    §E{mut}\n    §C{Bump} §A{ref} §IDX{xs} 0 §/C")]
    [InlineData("field of an in struct parameter",
        "  §CL{s1:S:pub:struct}\n    §FLD{i32:X:pub}\n  §F{f1:Run:pub} (S:s:in) -> void\n    §E{mut}\n    §C{Bump} §A{ref} s.X §/C",
        "  §CL{s1:S:pub:struct}\n    §FLD{i32:X:pub}\n  §F{f1:Run:pub} (S:s:ref) -> void\n    §E{mut}\n    §C{Bump} §A{ref} s.X §/C")]
    [InlineData("non-generic indexer element",
        "  §F{f1:Run:pub} (System.Collections.ArrayList:xs) -> void\n    §E{mut}\n    §C{SetOut} §A{out} §IDX{xs} 0 §/C",
        "  §F{f1:Run:pub} (i32[]:xs) -> void\n    §E{mut}\n    §C{SetOut} §A{out} §IDX{xs} 0 §/C")]
    [InlineData("scalar metadata property",
        "  §F{f1:Run:pub} (str:s) -> void\n    §E{mut}\n    §C{Bump} §A{ref} s.Length §/C",
        "  §F{f1:Run:pub} (str:s) -> void\n    §E{mut}\n    §B{~n:i32} s.Length\n    §C{Bump} §A{ref} n §/C")]
    [InlineData("readonly intermediate field of a struct path",
        "  §CL{s1:Inner:pub:struct}\n    §FLD{i32:Y:pub}\n  §CL{s2:Outer:pub:struct}\n    §FLD{Inner:X:pub:readonly}\n  §F{f1:Run:pub} (Outer[]:a) -> void\n    §E{mut}\n    §C{Bump} §A{ref} (§IDX{a} 0).X.Y §/C",
        "  §CL{s1:Inner:pub:struct}\n    §FLD{i32:Y:pub}\n  §CL{s2:Outer:pub:struct}\n    §FLD{Inner:X:pub}\n  §F{f1:Run:pub} (Outer[]:a) -> void\n    §E{mut}\n    §C{Bump} §A{ref} (§IDX{a} 0).X.Y §/C")]
    [InlineData("readonly field of another instance in a constructor",
        "  §CL{c1:K:pub}\n    §FLD{i32:n:pub:readonly}\n    §CTOR{ct1:pub} (K:other)\n      §C{Bump} §A{ref} other.n §/C",
        "  §CL{c1:K:pub}\n    §FLD{i32:n:pub:readonly}\n    §CTOR{ct1:pub} (K:other)\n      §C{Bump} §A{ref} n §/C")]
    // Codex round 3: a readonly field reached through a struct path, a property at the end of
    // one, a metadata constant, and a two-index indexer.
    [InlineData("readonly field at the end of a dotted struct path",
        "  §CL{s1:Inner:pub:struct}\n    §FLD{i32:N:pub:readonly}\n  §CL{s2:Outer:pub:struct}\n    §FLD{Inner:F:pub}\n  §F{f1:Run:pub} (Outer:s) -> void\n    §E{mut}\n    §C{Bump} §A{ref} s.F.N §/C",
        "  §CL{s1:Inner:pub:struct}\n    §FLD{i32:N:pub}\n  §CL{s2:Outer:pub:struct}\n    §FLD{Inner:F:pub}\n  §F{f1:Run:pub} (Outer:s) -> void\n    §E{mut}\n    §C{Bump} §A{ref} s.F.N §/C")]
    [InlineData("metadata constant",
        "  §F{f1:Run:pub} () -> void\n    §C{Bump} §A{ref} Int32.MaxValue §/C",
        "  §F{f1:Run:pub} () -> void\n    §B{~m:i32} Int32.MaxValue\n    §C{Bump} §A{ref} m §/C")]
    [InlineData("readonly reference-typed field in an in struct",
        "  §CL{c1:Box:pub}\n    §FLD{i32:N:pub:readonly}\n  §CL{s1:S:pub:struct}\n    §FLD{Box:B:pub}\n  §F{f1:Run:pub} (S:s:in) -> void\n    §E{mut}\n    §C{Bump} §A{ref} s.B.N §/C",
        "  §CL{c1:Box:pub}\n    §FLD{i32:N:pub}\n  §CL{s1:S:pub:struct}\n    §FLD{Box:B:pub}\n  §F{f1:Run:pub} (S:s:in) -> void\n    §E{mut}\n    §C{Bump} §A{ref} s.B.N §/C")]
    [InlineData("two-index indexer element (Codex R3-5)",
        "  §CL{c1:Grid:pub}\n    §IXER{ix1:i32:pub}\n      §I{i32:r}\n      §I{i32:c}\n      §GET\n        §R 1\n  §F{f1:Run:pub} (Grid:g) -> void\n    §E{mut}\n    §C{Bump} §A{ref} §IDX2D g 0 0 §/C",
        "  §F{f1:Run:pub} (i32[,]:g) -> void\n    §E{mut}\n    §C{Bump} §A{ref} §IDX2D g 0 0 §/C")]
    [InlineData("discard bound to a field named _",
        "  §CL{c1:K:pub}\n    §FLD{i32:_:pub}\n    §MT{m1:Run:pub} () -> void\n      §C{SetOut} §A{out} _ §/C",
        "  §CL{c1:K:pub}\n    §MT{m1:Run:pub} () -> void\n      §C{SetOut} §A{out} _ §/C")]
    public void InvalidOperand_IsRefusedBeforeCodegen_AndItsControlCompiles(string shape, string refused, string accepted)
    {
        Assert.True(Errors(refused).SequenceEqual(["Calor0212"]), $"{shape}: [{string.Join(", ", Errors(refused))}]");
        // The control may still hit an unrelated effect gap (struct members are unknown calls), never
        // a by-reference refusal or a generated-C# failure.
        Assert.True(!Errors(accepted).Any(code => code is "Calor0212" or "Calor1002" or "Calor0208"),
            $"{shape} control: [{string.Join(", ", Errors(accepted))}]");
    }

    [Theory]
    [InlineData("§C{Bump} §A{ref} k.n §/C", "§E{}", "Calor0410")]
    [InlineData("§C{SetOut} §A{out} k.n §/C", "§E{}", "Calor0410")]
    [InlineData("§C{Bump} §A{ref} k.n §/C", "§E{mut}", null)]
    [InlineData("§C{Twice} §A{in} k.n §/C", "§E{}", null)]
    [InlineData("§ASSIGN k.n 3", "§E{}", "Calor0410")]
    public void ReceiverFieldWrite_ChargesLikeAssign(string statement, string effects, string? error)
    {
        var errors = Errors($$"""
              §CL{c1:K:pub}
                §FLD{i32:n:pub}
              §F{f1:Run:pub} (K:k) -> void
                {{effects}}
                {{statement}}
            """);
        Assert.Equal(error is null ? [] : [error], errors);
    }

    // RO-NULL-2 controls: invalidation is limited to by-reference parameters that may alias.
    [Theory]
    [InlineData("(?str:a:ref)", "§B{i32:z} 1", true)]
    [InlineData("(?str:a)", "§C{ClearSlot} §/C", true)]
    [InlineData("(?str:a:ref, ?str:b)", "§ASSIGN b null", true)]
    [InlineData("(?str:a:ref, ?str:b:ref)", "§B{i32:z} 1", true)]
    [InlineData("(?str:a:ref)", "§C{ClearSlot} §/C", false)]
    [InlineData("(?str:a:ref, ?str:b:ref)", "§ASSIGN b null", false)]
    [InlineData("(?str:a:in, ?str:b:ref)", "§ASSIGN b null", false)]
    [InlineData("(?str:a:ref, Func<i32>:f)", "§B{i32:z} §C f §/C", false)]   // expression-target call (Codex R2-1)
    public void AliasNarrowing_IsInvalidatedOnlyWhenStorageMayBeShared(string parameters, string step, bool keepsNarrowing)
    {
        var errors = Errors($$"""
              §CL{c1:K:pub:stat}
                §FLD{?str:slot:pub:stat}
                §MT{m1:ClearSlot:pub:stat} ()
                  §ASSIGN slot null
                §MT{m2:Check:pub:stat} {{parameters}} -> i32
                  §IF{i1} (!= a null)
                    {{step}}
                    §B{str:u} a
                    §R (len u)
                  §R 0
            """);
        Assert.Equal(keepsNarrowing ? [] : ["Calor0272"], errors);
    }

    // Codex round 1: a write later in a loop body reaches the next iteration's reads (RO-NULL-2).
    [Theory]
    [InlineData("(?str:a:ref, ?str:b:ref)", "a", "§ASSIGN b null", false)]
    [InlineData("(?str:a:ref, ?str:b:ref)", "a", "§B{i32:z} 1", true)]
    [InlineData("(?str:a)", "a", "§C{Clear} §A{ref} a §/C", false)]
    [InlineData("(?str:a)", "a", "§C{Keep} §A a §/C", true)]
    [InlineData("(?str:a:ref, ?str:b)", "a", "§B{i32:z} (len b)", true)]
    public void LoopBackEdge_EndsNarrowingBeforeTheBody(string parameters, string name, string step, bool keepsNarrowing)
    {
        var errors = Errors($$"""
              §F{f2:Clear:pub} (?str:s:ref) -> void
                §ASSIGN s null
              §F{f3:Keep:pub} (?str:s) -> void
                §R
              §F{f1:Check:pub} {{parameters}} -> i32
                §IF{i1} (!= {{name}} null)
                  §L{l1:i:0:1:1}
                    §B{str:u} {{name}}
                    {{step}}
                §R 0
            """);
        Assert.Equal(keepsNarrowing ? [] : ["Calor0272"], errors);
    }

    // Codex round 3: an overloaded operator and a disposal can run code that writes aliased storage.
    [Theory]
    [InlineData("§B{Box:y} (+ x x)", false)]
    [InlineData("§B{i32:y} (+ 1 2)", true)]
    public void OverloadedOperator_EndsAliasedNarrowing(string step, bool keepsNarrowing)
    {
        var errors = Errors($$"""
              §CL{c001:Box:pub}
                §OP{op001:+:pub} (Box:x, Box:y) -> Box
                  §R x
              §CL{c002:K:pub:stat}
                §MT{mt002:Check:pub:stat} (?str:a:ref, Box:x) -> i32
                  §IF{if001} (!= a null)
                    {{step}}
                    §B{str:u} a
                    §R (len u)
                  §R 0
            """);
        Assert.Equal(keepsNarrowing ? [] : ["Calor0272"], errors);
    }

    [Fact]
    public void UsingInALoop_EndsAliasedNarrowingAtTheBackEdge()
    {
        var errors = Errors("""
              §CL{c001:D:pub}
                §IMPL{System.IDisposable}
                §MT{mt001:Dispose:pub} () -> void
                  §R
              §CL{c002:K:pub:stat}
                §MT{mt002:Check:pub:stat} (?str:a:ref, D:d) -> i32
                  §IF{if001} (!= a null)
                    §L{l001:i:0:1:1}
                      §B{str:u} a
                      §USE{use001:r} d
                        §B{i32:z} 1
                  §R 0
            """);
        Assert.Contains("Calor0272", errors);
    }

    // Soundness: no proof obligation is Discharged from a fact a by-reference write may have killed.
    [Theory]
    [InlineData("§C{Bump} §A{ref} x §/C", "Unsupported")]
    [InlineData("§C{SetNeg} §A{out} x §/C", "Unsupported")]
    [InlineData("§IF{i9} (> x 0)\n    §C{SetNeg} §A{out} x §/C", "Unsupported")]
    [InlineData("§B{d:i32} §C{Twice} §A{in} x §/C", "Discharged")]
    [InlineData("§B{d:i32} §C{Id} §A x §/C", "Discharged")]
    public void ByReferenceWrite_NeverDischargesAStaleFact(string call, string expected)
    {
        Assert.True(Z3ContextFactory.IsAvailable, "proof cases need Z3");
        var options = new CompilationOptions
        {
            VerifyContracts = true, VerifyRefinements = true, ContractMode = ContractMode.Debug,
            VerificationCacheOptions = new VerificationCacheOptions { Enabled = false },
        };
        var errors = Errors($$"""
              §F{f1:Run:pub} (i32:x) -> i32
                §Q (> x 0)
                {{call}}
                §PROOF{pr1} (> x 0)
                §R x
            """, options: options);
        Assert.Empty(errors);
        var proof = Assert.Single(options.ObligationResults!.Obligations, o => o.Kind == ObligationKind.ProofObligation);
        Assert.Equal(expected, proof.Status.ToString());
    }

    // D1-ANA-06 (RO-ANA-5): the division checker must not keep a ref operand's pre-call value,
    // in either direction: d == 1 is no longer "safe" and d == 0 no longer "guaranteed" after Zero(ref d).
    [Theory]
    [InlineData(1, "§B{z:i32} 1", null)]
    [InlineData(1, "§C{Zero} §A{ref} d §/C", false)]
    [InlineData(0, "§B{z:i32} 1", true)]
    [InlineData(0, "§C{Zero} §A{ref} d §/C", false)]
    public void DivisionByZeroChecker_DoesNotUseThePreCallValueOfARefOperand(int initial, string step, bool? guaranteed)
    {
        var result = Program.Compile(Prelude + $$"""
              §F{f5:Zero:pub} (i32:x:ref) -> void
                §ASSIGN x 0
              §F{f1:Run:pub} () -> i32
                §B{~d:i32} {{initial}}
                {{step}}
                §R (/ 10 d)
            """, "ana06.calr", new CompilationOptions
            {
                EnableVerificationAnalyses = true,
                VerificationAnalysisOptions = new Calor.Compiler.Analysis.VerificationAnalysisOptions(),
            });
        var findings = result.Diagnostics.Where(d => d.Code == Calor.Compiler.Diagnostics.DiagnosticCode.DivisionByZero).ToList();
        if (guaranteed is null)
            Assert.Empty(findings);
        else
            Assert.Equal(guaranteed.Value, Assert.Single(findings).IsError);
    }

    // X-INT-05 (F1 x F3) on the language side: a native local function's by-reference parameters
    // follow the same operand, effect and stale-fact rules as any callee.
    [Theory]
    [InlineData("§C{Inc} §A{ref} x §/C", "§E{alloc}", "", "Unsupported")]
    [InlineData("§C{Inc} §A{ref} (+ x 1) §/C", "§E{alloc}", "Calor0212", null)]
    [InlineData("§C{Inc} §A{ref} §IDX{arr} 0 §/C", "§E{alloc}", "Calor0410", null)]
    [InlineData("§C{Inc} §A{ref} §IDX{arr} 0 §/C", "§E{alloc,mut}", "", "Discharged")]
    public void LocalFunction_ByReferenceArguments_FollowTheContract(string call, string effects, string error, string? proof)
    {
        var options = new CompilationOptions
        {
            VerifyContracts = true, VerifyRefinements = true, ContractMode = ContractMode.Debug,
            VerificationCacheOptions = new VerificationCacheOptions { Enabled = false },
        };
        var result = Program.Compile($$"""
            §M{m1:P}
              §F{f1:Run:pub} (i32:x) -> i32
                {{effects}}
                §Q (> x 0)
                §ARR{arr:i32}
                  1
                {{call}}
                §PROOF{pr1} (> x 0)
                §R x
                §F{f2:Inc} (i32:v:ref) -> void
                  §ASSIGN v (+ v 1)
            """, "local.calr", options);
        var errors = result.Diagnostics.Where(d => d.IsError).Select(d => d.Code).Distinct().ToArray();
        Assert.Equal(error.Length == 0 ? [] : [error], errors);
        if (proof != null)
        {
            Assert.Contains("Inc(ref ", result.GeneratedCode);
            Assert.Equal(proof, Assert.Single(options.ObligationResults!.Obligations, o => o.Kind == ObligationKind.ProofObligation).Status.ToString());
        }
    }

    // ---- Conversion: convert, compile, run, compare (the R0/D1 oracle on every surface). ----

    public enum Surface { Library, CliDefault, CliPassthrough, McpDefault, McpPassthroughOnError }

    public static TheoryData<string, Surface> ConversionRows()
    {
        var data = new TheoryData<string, Surface>();
        foreach (var (id, _, _) in Rows())
            foreach (var surface in Enum.GetValues<Surface>())
                data.Add(id, surface);
        return data;
    }

    [Theory]
    [MemberData(nameof(ConversionRows))]
    public async Task Conversion_KeepsByReferenceIdentity_OrPreservesTheShape(string id, Surface surface)
    {
        var (_, source, allowed) = Rows().Single(row => row.Id == id);
        var original = Run(source);
        var (calor, losses) = await ConvertAsync(source, surface);
        var interop = calor.Contains("§CSHARP{", StringComparison.Ordinal) || calor.Contains("§CS{", StringComparison.Ordinal)
            || calor.Contains("§RAW", StringComparison.Ordinal) || losses.Count > 0;
        var strict = Program.Compile(calor, id + ".calr", new CompilationOptions { StatusWriter = TextWriter.Null });
        var relaxed = strict.HasErrors && strict.Diagnostics.Errors.All(e => e.Code.StartsWith("Calor04", StringComparison.Ordinal))
            ? Program.Compile(calor, id + ".calr", new CompilationOptions { StatusWriter = TextWriter.Null, EnforceEffects = false })
            : strict;
        string outcome;
        if (relaxed.HasErrors)
            outcome = "rejected";
        else
        {
            Assert.Equal(original, Run(relaxed.GeneratedCode)); // never a silent copy or reorder
            outcome = interop ? "preserved" : "native";
        }
        Assert.True(allowed.Split("-or-").Contains(outcome),
            $"{id} on {surface}: {outcome}, allowed {allowed}: {string.Join("; ", relaxed.Diagnostics.Errors)}\n{calor}");
        // The F1-REFOUT-02 shape: an out declaration is never kept as inline §CS{} with assumed effects.
        Assert.DoesNotMatch(@"§CS\{[^}]*out var", calor);
    }

    [Fact]
    public void OutVar_IsLoweredBeforeItsStatement()
    {
        var calor = new CSharpToCalorConverter().Convert(R0Fixture("F1-REFOUT-02")).CalorSource!;
        Assert.Contains("§B{~n:i32} `default`\n        §R (? §C{int.TryParse} §A text §A{out} n §/C n (- 1))", calor.Replace("\r\n", "\n"));
        Assert.Contains("§A{out} _ §/C", calor);
    }

    [Theory]
    [InlineData("while (int.TryParse(s, out var n)) { s = \"\"; }", false)]          // loop header
    [InlineData("System.Func<bool> f = () => int.TryParse(s, out var n);", false)]     // lambda
    [InlineData("if (int.TryParse(s, out var Field)) { }", false)]                     // collides with a field
    [InlineData("{ int m = 0; } if (int.TryParse(s, out var m)) { }", false)]          // declared elsewhere in scope
    [InlineData("int k = 1; if (int.TryParse(s, out var m) && m > k) { }", true)]      // control: inside the bound
    public void OutVar_OutsideTheBound_IsPreserved(string statement, bool native)
    {
        var result = new CSharpToCalorConverter().Convert($$"""
            public class C
            {
                private int Field;
                public void M(string s)
                {
                    {{statement}}
                }
            }
            """);
        Assert.Equal(native, !result.CalorSource!.Contains("§CSHARP{", StringComparison.Ordinal) && result.Losses.Count == 0);
    }

    [Fact]
    public void RefArgumentToAnInParameter_ConvertsToIn()
    {
        var calor = new CSharpToCalorConverter().Convert("""
            public static class Probe
            {
                static int Twice(in int x) => x * 2;
                public static string Run() { int v = 4; return Twice(ref v).ToString(); }
            }
            """).CalorSource!;
        Assert.Contains("§A{in} v", calor);
        Assert.DoesNotContain("§A{ref}", calor);
    }

    private static IEnumerable<(string Id, string Source, string Allowed)> Rows()
    {
        var root = CliTestHarness.FindRepoRoot();
        var r0 = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "tests/TestData/InteropScope/R0/cases.json")))!;
        foreach (var c in r0["cases"]!.AsArray().Where(c => c!["id"]!.GetValue<string>().StartsWith("F1-REFOUT-0", StringComparison.Ordinal)))
            yield return (c!["id"]!.GetValue<string>(), R0Fixture(c["id"]!.GetValue<string>()), c["expected"]!.GetValue<string>());
        // Codex review round 1 reproducers: a nested value-type field path, a by-reference call in
        // a decomposed chain with a by-value overload, a by-reference element before a hoisted
        // argument that throws, and an out declaration in a statement that is preserved.
        foreach (var (id, source) in ReviewWitnesses)
            yield return (id, source, "native-or-preserved");
        foreach (var (id, source, allowed) in Pr2Witnesses)
            yield return (id, source, allowed);
        foreach (var id in new[] { "X-INT-05", "X-INT-08" })
            yield return (id, File.ReadAllText(Path.Combine(root, "tests/TestData/InteropScope/F1/fixtures", id + ".cs.txt")), "native-or-preserved");
        foreach (var c in RefOutContractCaseTests.Contract()["cases"]!.AsArray().Where(c => c!["kind"]!.GetValue<string>() == "csharp"))
        {
            var src = c!["source"]!;
            // D1-CS-16 is native for its by-reference calls; its unrelated `[1] = "one"` index
            // initializer is preserved inline (dictionary-initializer), so the row allows preserved.
            var allowed = c["id"]!.GetValue<string>() == "D1-CS-16" ? "native-or-preserved" : c["conversion"]!.GetValue<string>();
            yield return (c["id"]!.GetValue<string>(), src is JsonArray a ? string.Join("\n", a.Select(l => l!.GetValue<string>())) : src.GetValue<string>(), allowed);
        }
    }

    private static readonly (string Id, string Source)[] ReviewWitnesses =
    [
        ("R1-nested-struct-path", "public struct Inner { public int Y; } public struct Outer { public Inner X; } public static class Probe { static void Bump(ref int x) { x = 7; } public static string Run() { var a = new Outer[] { new Outer() }; Bump(ref a[0].X.Y); return a[0].X.Y.ToString(); } }"),
        ("R1-chain-overload", "public class Box { public Box Next() => this; public int Use(ref int x) { x = 7; return 1; } public int Use(int x) => 2; } public static class Probe { public static string Run() { var b = new Box(); int x = 0; int k = b.Next().Use(ref x); return (x * 10 + k).ToString(); } }"),
        ("R1-element-before-hoist", "public class Bomb { public static string Log = \"\"; public Bomb() { Log += \"b\"; throw new System.InvalidOperationException(); } } public static class Probe { static void Use(ref int x, Bomb b) { } public static string Run() { int[] a = new int[0]; try { Use(ref a[0], new Bomb()); } catch (System.Exception e) { return e.GetType().Name + Bomb.Log; } return \"none\"; } }"),
        ("R1-out-var-preserved-statement", "public static class Probe { static string Seen = \"\"; static void Use(bool b, in int x) { Seen = b + \":\" + x; } public static string Run() { Use(int.TryParse(\"1\", out var n), 2); return Seen + \"|\" + n; } }"),
        // Codex review round 2: a by-reference call at the root of a chain, a field through a
        // ref-returning call, a receiver call after an earlier argument, and a block lambda.
        ("R2-chain-root", "public class Box { public int Use() => 1; } public class Probe { int Use() => 2; static Box Make(ref int x) { x = 7; return new Box(); } public string Go() { int x = 0; int z = Make(ref x).Use(); return x + \":\" + z; } public static string Run() => new Probe().Go(); }"),
        ("R2-ref-return-receiver", "public struct S { public int X; } public static class Probe { static S slot; static ref S Get() { return ref slot; } static void Use(ref int x) { x = 7; } public static string Run() { Use(ref Get().X); return slot.X.ToString(); } }"),
        ("R2-receiver-call-order", "public class Box { public int N; } public static class Probe { static Box b = new Box(); static string log = \"\"; static Box Get() { log += \"g\"; return b; } static int Before() { log += \"b\"; return 0; } static void Use(int i, ref int x) { x = 7; } public static string Run() { Use(Before(), ref Get().N); return log + \":\" + b.N; } }"),
        ("R2-block-lambda", "using System; public static class Probe { public static string Run() { Func<bool> f = () => { return int.TryParse(\"1\", out var n); }; return f().ToString(); } }"),
        // Codex review round 3: the receiver of a by-reference call is captured before a later
        // argument runs, and a write through a reference-typed field of an `in` struct is valid.
        ("R3-receiver-before-hoist", "public class Box { public int N; public Box(int n) { N = n; } public void Use(ref int x, Bomb b) { x = N; } } public class Bomb { public Bomb() { Probe.b = Probe.second; } } public static class Probe { public static Box b = new Box(1), second = new Box(2); public static string Run() { int x = 0; b.Use(ref x, new Bomb()); return x.ToString(); } }"),
        ("R3-reference-field-of-in-struct", "public class Box { public int N; } public struct S { public Box B; } public static class Probe { static void Bump(ref int n) { n = 7; } static void Use(in S s) { Bump(ref s.B.N); } public static string Run() { var s = new S { B = new Box() }; Use(in s); return s.B.N.ToString(); } }"),
    ];

    // F1 PR 2 (W1 findings). A bare §B{~n:i32} took the next sibling statement as its initializer
    // (a call statement, an §ARR declaration), so a lowered binding starts at `default`, as the
    // converter already writes `int x;`. That `default` would read a symbol named `default`, so
    // such code is preserved (Codex PR 2 round 1). A chained assignment evaluates its right side
    // once per target when converted, so it is preserved too (round 2). The parser is unchanged
    // (round 3: a next-line initializer is valid Calor). An expression-bodied ref return is
    // preserved like a block-bodied one.
    private static readonly (string Id, string Source, string Allowed)[] Pr2Witnesses =
    [
        ("P2-out-var-call-statement", "public static class Probe { public static string Run() { int.TryParse(\"5\", out var n); return n.ToString(); } }", "native"),
        ("P2-out-var-statement-then-call", "public static class Probe { static string Log = \"\"; static void Use(int x) { Log += x; } public static string Run() { int.TryParse(\"5\", out var n); Use(n); int.TryParse(\"x\", out int m); Use(m); return Log; } }", "native"),
        ("P2-out-var-string-statement", "using System.Collections.Generic; public static class Probe { public static string Run() { var d = new Dictionary<int, string>(); d.Add(1, \"one\"); d.TryGetValue(1, out var v); return v ?? \"null\"; } }", "native"),
        ("P2-out-var-array-declaration", "public static class Probe { static int Set(out int[] n) { n = new[] { 7 }; return 1; } public static string Run() { int[] a = new[] { Set(out var n) }; return n[0] + \":\" + a[0]; } }", "native"),
        ("P2-out-var-chained-assignment", "public static class Probe { static int calls; static bool Set(out int n) { n = ++calls; return true; } public static string Run() { bool x = false, y = false; x = y = Set(out var n); return calls + \":\" + n + \":\" + x; } }", "preserved"),
        ("P2-out-var-assignment-statement", "public static class Probe { public static string Run() { bool ok; ok = int.TryParse(\"6\", out var n); return ok + \":\" + n; } }", "native"),
        ("P2-out-var-local-declaration", "public static class Probe { public static string Run() { bool ok = int.TryParse(\"7\", out var n); return ok + \":\" + n; } }", "native"),
        ("P2-out-var-return", "public static class Probe { static int Get(string s) { return int.TryParse(s, out var n) ? n : -1; } public static string Run() => Get(\"8\") + \"|\" + Get(\"x\"); }", "native"),
        ("P2-out-var-if", "public static class Probe { public static string Run() { if (int.TryParse(\"9\", out var n)) { return n.ToString(); } return \"none\"; } }", "native"),
        ("P2-out-var-member-named-default", "public static class Probe { public static int reads; public static int @default { get { reads++; return 0; } } public static string Run() { bool ok = int.TryParse(\"5\", out var n); return reads + \":\" + n + \":\" + ok; } }", "preserved"),
        ("P2-ref-return-expression", "public static class Probe { static ref int First(int[] a) => ref a[0]; public static string Run() { var a = new[] { 1, 2 }; First(a) = 5; return a[0].ToString(); } }", "preserved"),
        ("P2-ref-return-property", "public static class Probe { static int slot = 3; static ref int Slot => ref slot; public static string Run() { Slot = 4; return slot.ToString(); } }", "preserved"),
    ];

    // Codex PR 2 round 3: hand-written Calor keeps its meaning; a next-line expression is still the
    // initializer of a bare binding, which is why the converter writes `default` instead.
    [Fact]
    public void HandWrittenNextLineInitializer_KeepsItsMeaning()
    {
        var result = Program.Compile("§M{m1:P}\n  §F{f1:Run:pub} () -> i32\n    §E{}\n    §B{n:i32}\n    §C{System.Math.Abs} §A -7 §/C\n    §R n\n",
            "next.calr", new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(result.HasErrors, string.Join("; ", result.Diagnostics.Errors));
        Assert.Equal("7", Run(result.GeneratedCode + "\npublic static class Probe { public static string Run() => P.PModule.Run().ToString(); }"));
    }

    // The lowered binding carries an explicit `default`, so the next statement is never its initializer.
    [Fact]
    public void OutDeclaration_IsLoweredWithAnExplicitDefault()
    {
        var calor = new CSharpToCalorConverter().Convert("public static class Probe { static bool Set(out string value) { value = \"ok\"; return true; } public static string Run() { Set(out string value); return value; } }").CalorSource!;
        Assert.Contains("§B{~value:str} `default`\n        §C{Set} §A{out} value §/C", calor.Replace("\r\n", "\n"));
    }

    [Theory]
    [InlineData("static void Use(ref int x) { x++; } static void M(int[] a) { Use(ref a[Next()]); }", "ref-parameter")]
    [InlineData("static void Use(out int x) { x = 1; } static void M(int[] a) { Use(out a[Next()]); }", "out-parameter")]
    [InlineData("static void Use(ref readonly int x) { } static void M(int[] a) { Use(in a[Next()]); }", "ref-parameter")]
    [InlineData("static void Use<T>(ref T x) { } static void M() { int x = 0; Use<int>(ref x); }", "ref-parameter")]
    [InlineData("static void Use<T>(in T x) { } static void M() { int x = 0; Use<int>(in x); }", "in-parameter")]
    [InlineData("static void Use(in int x) { } static void M(int[] a) { Use(in a[Next()]); }", "in-parameter")] // control
    [InlineData("static void Use(in int x) { } static void M(int v) { Use(v); }", "in-parameter")]                  // control
    public void ByReferenceStatementLoss_IsReportedUnderTheParametersFeature(string members, string feature)
    {
        var result = new CSharpToCalorConverter().Convert("public static class C { static int Next() => 0; " + members + " }");
        var features = result.Losses.Select(loss => loss.Feature).Concat(result.Issues.Where(i => i.Feature?.EndsWith("-parameter") == true && i.Message.Contains("Use", StringComparison.Ordinal))
            .Select(i => i.Feature!)).Distinct().ToArray();
        Assert.Contains(feature, features); // a `ref readonly` parameter also preserves its declaring member (`method`)
        Assert.All(features, f => Assert.True(f == feature || f == "method", f));
    }

    [Fact]
    public void ExpressionBodiedRefReturn_IsPreserved_NotStripped()
    {
        var result = new CSharpToCalorConverter().Convert("public static class C { public static ref int First(int[] a) => ref a[0]; }");
        Assert.Contains("§CSHARP{", result.CalorSource);
        Assert.DoesNotContain("-> ref", result.CalorSource);
        var block = new CSharpToCalorConverter().Convert("public static class C { public static ref int First(int[] a) { return ref a[0]; } }");
        Assert.Equal(block.Losses.Select(loss => loss.Feature), result.Losses.Select(loss => loss.Feature)); // the same report
        Assert.Contains(result.Issues, issue => issue.Feature == "ref-parameter");
    }

    // FeatureSupport states what converts (W1 finding 4); no level is promoted (D-S1.5).
    [Theory]
    [InlineData("ref-expression", "stripped", SupportLevel.Full)]
    [InlineData("multidim-array", "fully supported", SupportLevel.Full)]
    [InlineData("out-parameter", "[NotNullWhen] are preserved", SupportLevel.Partial)]
    [InlineData("in-parameter", "pass a value to an in parameter", SupportLevel.Full)]
    [InlineData("out-var", "Chained assignments", SupportLevel.Partial)]
    public void FeatureSupportText_MatchesTheConverter(string feature, string phrase, SupportLevel level)
    {
        var info = FeatureSupport.GetFeatureInfo(feature)!;
        Assert.Equal(level, info.Support);
        Assert.Equal(feature == "out-var", info.Description!.Contains(phrase, StringComparison.Ordinal));
        Assert.Contains("C#", info.Description);
    }

    private static string R0Fixture(string id) => File.ReadAllText(Path.Combine(
        CliTestHarness.FindRepoRoot(), "tests/TestData/InteropScope/R0/fixtures", id + ".cs.txt"));

    internal static async Task<(string Calor, List<string> Losses)> ConvertAsync(string source, Surface surface)
    {
        if (surface is Surface.McpDefault or Surface.McpPassthroughOnError)
        {
            var args = new Dictionary<string, object> { ["source"] = source };
            if (surface == Surface.McpPassthroughOnError)
                args["passthroughOnError"] = true;
            var tool = await new ConvertTool().ExecuteAsync(JsonDocument.Parse(JsonSerializer.Serialize(args)).RootElement);
            using var payload = JsonDocument.Parse(tool.Content[0].Text!);
            var root = payload.RootElement;
            var losses = root.TryGetProperty("lossSummary", out var summary)
                ? summary.GetProperty("locations").EnumerateArray().Select(l => l.GetProperty("feature").GetString()!).ToList()
                : [];
            return (root.GetProperty("calorSource").GetString() ?? "", losses);
        }
        var options = surface == Surface.Library ? new ConversionOptions()
            : ConvertCommand.BuildCSharpToCalorOptions(benchmark: false, verbose: false, explain: false, noFallback: false,
                passthrough: surface == Surface.CliPassthrough, explicitCallClosers: false);
        var result = new CSharpToCalorConverter(options).Convert(source);
        return (result.CalorSource ?? "", result.Losses.Select(loss => loss.Feature).ToList());
    }

    internal static string Run(string csharp)
    {
        var compilation = CSharpCompilation.Create("F1_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(csharp, new CSharpParseOptions(LanguageVersion.Preview))],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable, allowUnsafe: true));
        using var stream = new MemoryStream();
        var emission = compilation.Emit(stream);
        Assert.True(emission.Success, string.Join(Environment.NewLine, emission.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)) + "\n" + csharp);
        stream.Position = 0;
        var context = new AssemblyLoadContext(compilation.AssemblyName!, isCollectible: true);
        try
        {
            var run = context.LoadFromStream(stream).GetTypes().Single(t => t.Name == "Probe").GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!;
            try { return run.Invoke(null, null)?.ToString() ?? "<null>"; }
            catch (TargetInvocationException e) { return "!" + e.InnerException!.GetType().Name; }
        }
        finally
        {
            context.Unload();
        }
    }
}
