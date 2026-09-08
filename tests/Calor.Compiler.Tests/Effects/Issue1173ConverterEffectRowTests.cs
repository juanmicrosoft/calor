using Calor.Compiler.Analysis;
using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// #1173 — the C# → Calor converter used to infer its <c>§E</c> rows with a walker
/// maintained beside the emitter, and the two drifted: the emitter wrote a body the
/// walker never visited, the row said pure, and compiling that output reported
/// Calor0410 against code the converter itself had just written.
///
/// <para>The rows are now derived from the compiler's own effect inference, run over
/// the Calor that actually ships. These tests are the guard: each case is a construct
/// the old walker did not visit, and the assertion is not "the row contains alloc" but
/// "compiling the converter's output reports no Calor0410" — the property that was
/// broken, stated as the thing that must hold.</para>
/// </summary>
public class Issue1173ConverterEffectRowTests
{
    public static TheoryData<string, string, string, string> UnderDeclaredConstructs => new()
    {
        {
            "foreach over a collection the loop itself allocates",
            """
            using System.Collections.Generic;
            public class C
            {
                public int Run()
                {
                    int total = 0;
                    foreach (var x in new List<int> { 1, 2 }) total += x;
                    return total;
                }
            }
            """,
            "Run",
            "alloc"
        },
        {
            "a §USE resource expression",
            """
            using System.IO;
            public class C
            {
                public void Run() { using (var stream = new MemoryStream()) { } }
            }
            """,
            "Run",
            "alloc"
        },
        {
            "a lambda body",
            """
            using System;
            public class C
            {
                public Func<object> Run() => () => new object();
            }
            """,
            "Run",
            "alloc"
        },
        {
            "a mutating call on a collection parameter",
            """
            using System.Collections.Generic;
            public class C
            {
                public void Run(List<int> values) { values.Add(1); }
            }
            """,
            "Run",
            "mut"
        },
        {
            "an effect reached only through a call to a sibling method",
            """
            public class C
            {
                private readonly V _value = new V();
                public void Run() { Mutate(1); }
                private void Mutate(int n) { _value.X = n; }
            }
            public class V { public int X; }
            """,
            "Run",
            "mut"
        },
    };

    [Theory]
    [MemberData(nameof(UnderDeclaredConstructs))]
    public void ConvertedCode_DeclaresTheEffectsItsOwnBodyPerforms(
        string description,
        string csharp,
        string declaration,
        string expectedCode)
    {
        var conversion = Convert(csharp);

        // The row on THE declaration under test, read off the AST rather than searched
        // for in the module text: the sibling-call case has a callee that carries the
        // same code, so a text search would pass on the callee's row even when nothing
        // propagated to the caller — which is the only thing that case exists to pin.
        Assert.Contains(
            expectedCode,
            RowOf(conversion, declaration).Split(',').Select(code => code.Trim()));

        Assert.Empty(EffectErrors(conversion.CalorSource!).Select(d => $"{description}: {d.Message}"));
    }

    /// <summary>
    /// The compact <c>§E</c> codes on the named method of the converted module, as the
    /// emitter would write them.
    /// </summary>
    private static string RowOf(ConversionResult conversion, string methodName)
    {
        var method = CallGraphAnalysis.EnumerateClasses(conversion.Ast!)
            .SelectMany(CallGraphAnalysis.EnumerateMethods)
            .Single(m => m.Name == methodName);
        return string.Join(
            ",",
            EffectEnforcementPass.GetDeclaredEffects(method.Effects).Effects
                .Select(e => EffectCodes.ToCompact(e.Kind, e.Value)));
    }

    /// <summary>
    /// The reason synthesis runs on the RE-PARSED text rather than on the tree in
    /// hand: the emitter desugars a method chain into <c>§B</c> temporaries, so an
    /// effect charged through a chained call is not visible on the pre-emit tree.
    /// This case regressed exactly that way during development.
    /// </summary>
    [Fact]
    public void ChainedCalls_AreCoveredBecauseTheRowComesFromTheEmittedText()
    {
        var conversion = Convert(
            """
            public class C
            {
                private readonly V _value = new V();
                public void Run()
                {
                    _value.Wrap().Set(1);
                }
            }
            public class V
            {
                public int X;
                public V Wrap() { return this; }
                public void Set(int n) { X = n; }
            }
            """);

        Assert.Contains("§B{_chain", conversion.CalorSource!);
        Assert.Empty(EffectErrors(conversion.CalorSource!).Select(d => d.Message));
    }

    /// <summary>
    /// Synthesising a truthful row on an implementing method must not trade
    /// Calor0410 for Calor0421 — an implementation may not broaden the interface's
    /// row, so the interface member is widened in the same round.
    /// </summary>
    [Fact]
    public void InterfaceMembers_AreWidenedWithTheirImplementations()
    {
        var conversion = Convert(
            """
            public interface IRunner { object Run(); }
            public class Runner : IRunner
            {
                public object Run() { return new object(); }
            }
            """);

        Assert.Empty(
            Compile(conversion.CalorSource!).Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.Message));
    }

    /// <summary>
    /// The same for overrides (Calor0420): a base method's row must cover every
    /// override's.
    /// </summary>
    [Fact]
    public void BaseMethods_AreWidenedWithTheirOverrides()
    {
        var conversion = Convert(
            """
            public class Base { public virtual object Run() { return null; } }
            public class Derived : Base
            {
                public override object Run() { return new object(); }
            }
            """);

        Assert.Empty(
            Compile(conversion.CalorSource!).Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.Message));
    }

    /// <summary>
    /// A pure body still gets no row: synthesis adds what inference charges and
    /// nothing else, so <c>§E</c> does not appear on declarations that need none.
    /// </summary>
    [Fact]
    public void PureBodies_KeepNoRow()
    {
        var conversion = Convert(
            """
            public class C
            {
                public int Add(int left, int right) { return left + right; }
            }
            """);

        Assert.DoesNotContain("§E{", conversion.CalorSource!);
    }

    /// <summary>
    /// The row synthesiser is the inverse of the checker, so a row it writes must
    /// read back as the set it was built from — this is the property that makes the
    /// two unable to disagree.
    /// </summary>
    [Fact]
    public void SynthesizedRows_ReadBackAsTheSetTheyWereBuiltFrom()
    {
        var module = Parse(
            """
            §M{m001:RoundTrip}
              §F{f001:Run:pub} () -> void
                §B{buffer} §NEW{List<i32>} §/NEW
                §P "done"
            """);

        Assert.True(EffectEnforcementPass.SynthesizeDeclaredRows(module));

        var function = Assert.Single(module.Functions);
        var declared = EffectEnforcementPass.GetDeclaredEffects(function.Effects);
        Assert.Equal("cw, alloc", declared.ToDisplayString());

        // And what it wrote survives the text round trip unchanged.
        var emitted = new CalorEmitter(new ConversionContext()).Emit(module);
        var reparsed = Parse(emitted);
        Assert.Equal(
            declared.ToDisplayString(),
            EffectEnforcementPass.GetDeclaredEffects(
                Assert.Single(reparsed.Functions).Effects).ToDisplayString());
    }

    /// <summary>
    /// Synthesis only ever widens. A row an author already wrote keeps every code it
    /// had, so running the synthesiser over hand-written Calor cannot narrow a
    /// declaration.
    /// </summary>
    [Fact]
    public void SynthesisWidensAndNeverNarrows()
    {
        var module = Parse(
            """
            §M{m001:Widen}
              §F{f001:Run:pub} () -> void
                §E{fs:w}
                §P "done"
            """);

        EffectEnforcementPass.SynthesizeDeclaredRows(module);

        var declared = EffectEnforcementPass.GetDeclaredEffects(
            Assert.Single(module.Functions).Effects);
        Assert.Equal("cw, fs:w", declared.ToDisplayString());
    }

    /// <summary>
    /// The rows are a DIAGNOSTIC surface and nothing else: effect codes erase at
    /// codegen, so stripping every <c>§E</c> from converted output must produce the
    /// same C#. Without this, synthesis could change what converted code MEANS and the
    /// only evidence would be that the tests happened to still pass.
    /// </summary>
    [Fact]
    public void EffectRows_DoNotChangeGeneratedCode()
    {
        var conversion = Convert(
            """
            using System.Collections.Generic;
            using System.IO;
            public class C
            {
                public int Run(List<int> values)
                {
                    values.Add(1);
                    using (var stream = new MemoryStream()) { }
                    var total = 0;
                    foreach (var x in new List<int> { 1, 2 }) total += x;
                    return total;
                }
            }
            """);

        Assert.Contains("§E{", conversion.CalorSource!);
        var stripped = string.Join(
            "\n",
            conversion.CalorSource!.Split('\n')
                .Where(line => !line.TrimStart().StartsWith("§E{", StringComparison.Ordinal)));

        // Enforcement OFF on both sides: the question here is what the rows do to
        // CODEGEN, and leaving it on would compare "the C# for the stripped module"
        // against nothing at all, since the stripped module fails Calor0410 and an
        // effect error stops code generation.
        Assert.Equal(
            CompileWithoutEffects(stripped).GeneratedCode,
            CompileWithoutEffects(conversion.CalorSource!).GeneratedCode);
    }

    /// <summary>
    /// Conversion runs in parallel — <c>ProjectMigrator</c> fans out under a semaphore,
    /// the round-trip harness under <c>Parallel.ForEachAsync</c> — and synthesis rents a
    /// pooled <see cref="EffectResolver"/>, whose memoisation is not thread-safe. Same
    /// input, converted concurrently, must give the same rows as converting it serially.
    /// </summary>
    [Fact]
    public void ParallelConversion_ProducesTheSameRowsAsSerialConversion()
    {
        const string csharp =
            """
            using System.Collections.Generic;
            public class C
            {
                public List<int> Make() { return new List<int> { 1 }; }
                public void Fill(List<int> values) { values.Add(2); }
            }
            """;

        var expected = Convert(csharp).CalorSource;

        var actual = new string?[32];
        Parallel.For(0, actual.Length, i => actual[i] = Convert(csharp).CalorSource);

        Assert.All(actual, source => Assert.Equal(expected, source));
    }

    private static ConversionResult Convert(string csharp)
    {
        var conversion = new CSharpToCalorConverter(new ConversionOptions
        {
            Fidelity = ConversionFidelity.Lossy,
            ModuleName = "Issue1173",
            AutoGenerateIds = true,
            ValidateRoundTripCSharp = false
        }).Convert(csharp);

        Assert.True(
            conversion.Success,
            string.Join("; ", conversion.Issues.Select(issue => issue.Message)));
        Assert.NotNull(conversion.CalorSource);
        return conversion;
    }

    private static CompilationResult CompileWithoutEffects(string calorSource)
        => Program.Compile(calorSource, null, new CompilationOptions
        {
            DeferGeneratedOutputValidation = true,
            EnforceEffects = false,
            UnknownCallPolicy = UnknownCallPolicy.Permissive
        });

    private static CompilationResult Compile(string calorSource)
        => Program.Compile(calorSource, null, new CompilationOptions
        {
            DeferGeneratedOutputValidation = true,
            EnforceEffects = true,
            UnknownCallPolicy = UnknownCallPolicy.Permissive
        });

    private static IReadOnlyList<Diagnostic> EffectErrors(string calorSource)
        => Compile(calorSource).Diagnostics
            .Where(d => d.Code == DiagnosticCode.ForbiddenEffect
                && d.Severity == DiagnosticSeverity.Error)
            .ToList();

    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var module = new Parser(lexer.TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(
            diagnostics.HasErrors,
            string.Join(Environment.NewLine, diagnostics.Select(d => d.ToString())));
        return module;
    }
}
