using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Pins for the 0.30 round-trip findings on the corpus legs (milestone-0.30 CI triage, R9 and R10):
/// the converter writes native member access and raw holes, keeps every preprocessor branch when it
/// preserves a whole file, and never writes a temp or a type guess that its own C# cannot compile.
/// </summary>
public class NativeConverterEmissionTests
{
    private static ConversionOptions HarnessLossyOptions(params string[] symbols) => new()
    {
        Fidelity = ConversionFidelity.Lossy,
        PreprocessorMode = PreprocessorConversionMode.SelectActiveBranchLossy,
        ParseOptions = new CSharpParseOptions(LanguageVersion.Preview, DocumentationMode.Parse,
            SourceCodeKind.Regular, preprocessorSymbols: symbols),
        GracefulFallback = true,
        PassthroughOnError = true,
        ValidateRoundTripCSharp = false,
        PreserveComments = true,
        AutoGenerateIds = true,
    };

    private static CompilationResult CompileCalor(string calor) =>
        Program.Compile(calor, "native-emission.calr", new CompilationOptions
        {
            EnforceEffects = false,
            ContractMode = ContractMode.Off,
            DeferGeneratedOutputValidation = true,
        });

    private static string Diagnostics(CompilationResult result) =>
        string.Join("\n", result.Diagnostics.Select(diagnostic => diagnostic.ToString()));

    private static IReadOnlyList<Microsoft.CodeAnalysis.Diagnostic> CSharpErrors(string generated) =>
        CSharpCompilation.Create("NativeEmission",
                [CSharpSyntaxTree.ParseText(generated)],
                GeneratedCSharpCompiler.References,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .ToList();

    // ---- R10: member access ----

    [Fact]
    public void MemberAccessAfterASelfDelimitingReceiver_IsUnspacedAndReparsesAsAMemberAccess()
    {
        var csharp = """
            using System.Text;
            public class Probe
            {
                private int _count;
                public static Probe Make() => new Probe();
                public int Sum(StringBuilder sb) => this._count + Make()._count + sb.ToString().Length;
            }
            """;
        var result = new CSharpToCalorConverter().Convert(csharp);
        Assert.True(result.Success, string.Join("\n", result.Issues));
        var calor = new CalorEmitter().Emit(result.Ast!);
        Assert.Contains("this._count", calor);
        Assert.Contains("Make()._count", calor);
        Assert.True(calor.Contains("((sb).ToString()).Length"), calor);

        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(calor, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var members = Calor.Compiler.Analysis.RecursiveAstWalker.GetAllChildren(module)
            .SelectMany(Descendants).OfType<FieldAccessNode>().Select(access => access.FieldName).ToList();
        Assert.Equal(2, members.Count(name => name == "_count"));
        Assert.Contains("Length", members);

        var compiled = CompileCalor(calor);
        Assert.False(compiled.HasErrors, Diagnostics(compiled));
        Assert.Empty(CSharpErrors(compiled.GeneratedCode));

        static IEnumerable<AstNode> Descendants(AstNode node) =>
            new[] { node }.Concat(Calor.Compiler.Analysis.RecursiveAstWalker.GetAllChildren(node).SelectMany(Descendants));
    }

    [Fact]
    public void MemberAccessAfterAnIdentifier_KeepsTheSpacedFormUntilDottedNamesBindAlike()
    {
        // An unspaced `x.Field` lexes as one dotted name, which the binder does not yet type like
        // a FieldAccessNode (metadata member typing, static-field selection). Spacing keeps it one.
        var span = new TextSpan(0, 0, 1, 1);
        var access = new FieldAccessNode(span, new ReferenceNode(span, "items"), "Count");
        Assert.Equal("items . Count", access.Accept(new CalorEmitter()));
    }

    [Fact]
    public void NumericLiteralReceiver_IsParenthesizedSoItCannotLexAsANumber()
    {
        var span = new TextSpan(0, 0, 1, 1);
        var access = new FieldAccessNode(span, new IntLiteralNode(span, 1), "Length");
        Assert.Equal("(1).Length", access.Accept(new CalorEmitter()));
    }

    // ---- R10: raw C# expressions ----

    [Fact]
    public void RawInterpolationHole_IsWrittenBackAsItsCSharpText()
    {
        // No producer builds a raw hole today (the parser lifts holes to native nodes), but an
        // AST that carries one must still print as 0.30 source: the hole text is C# already.
        var span = new TextSpan(0, 0, 1, 1);
        var interpolated = new InterpolatedStringNode(span,
        [
            new InterpolatedStringTextNode(span, "v="),
            new InterpolatedStringExpressionNode(span, new RawCSharpExpressionNode(span, "a ?? b")),
        ]);
        var emitted = interpolated.Accept(new CalorEmitter());
        Assert.Equal("\"v=${a ?? b}\"", emitted);

        var calor = $$"""
            module Probe
              func Show public (string?:a, string:b) → string
                !
                return {{emitted}}
            """;
        var compiled = CompileCalor(calor);
        Assert.False(compiled.HasErrors, Diagnostics(compiled));
        Assert.Contains("$\"v={a ?? b}\"", compiled.GeneratedCode);
    }

    [Fact]
    public void RawExpressionOutsideAHole_HasNoNativeSpellingAndIsRefused()
    {
        var span = new TextSpan(0, 0, 1, 1);
        var emitter = new CalorEmitter();
        Assert.Throws<InvalidOperationException>(() => new RawCSharpExpressionNode(span, "a ?? b").Accept(emitter));
        Assert.Throws<InvalidOperationException>(() =>
            new FallbackExpressionNode(span, "stackalloc int[1]", "stackalloc").Accept(emitter));
    }

    // ---- R9: whole-file preservation keeps every branch ----

    private const string SyncOnlySource = """
        namespace Witness;

        using System;

        public class Probe
        {
            public int Read()
            {
        #if SYNC_ONLY
                throw new InvalidOperationException("sync");
        #endif
                return 1;
            }
        }

        """;

    [Fact]
    public void LossyWholeFilePreservation_KeepsTheInactiveBranchVerbatim()
    {
        var result = new CSharpToCalorConverter(HarnessLossyOptions()).Convert(SyncOnlySource, "Probe.cs");
        Assert.True(result.Success, string.Join("\n", result.Issues));
        Assert.Contains(result.Losses, loss => loss.Feature == "namespace-topology");
        Assert.DoesNotContain(result.Losses, loss => loss.Kind == ConversionLossKind.PreprocessorStripped);
        Assert.Contains("#if SYNC_ONLY", result.CalorSource);

        var compiled = CompileCalor(result.CalorSource!);
        Assert.False(compiled.HasErrors, Diagnostics(compiled));
        Assert.Contains("#if SYNC_ONLY", compiled.GeneratedCode);
        Assert.Contains("throw new InvalidOperationException(\"sync\");", compiled.GeneratedCode);
        Assert.EndsWith(SyncOnlySource.ReplaceLineEndings("\n"), compiled.GeneratedCode.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void LossyWholeUnitPassthrough_KeepsTheInactiveBranchVerbatim()
    {
        const string source = """
            [assembly: System.CLSCompliant(true)]

            public class Probe
            {
                public int Read()
                {
            #if SYNC_ONLY
                    return 2;
            #else
                    return 1;
            #endif
                }
            }

            """;
        var result = new CSharpToCalorConverter(HarnessLossyOptions()).Convert(source, "Probe.cs");
        Assert.True(result.Success, string.Join("\n", result.Issues));
        Assert.Contains(result.Losses, loss => loss.Feature == "compilation-unit-attribute");
        Assert.DoesNotContain(result.Losses, loss => loss.Kind == ConversionLossKind.PreprocessorStripped);

        var compiled = CompileCalor(result.CalorSource!);
        Assert.False(compiled.HasErrors, Diagnostics(compiled));
        Assert.Contains("return 2;", compiled.GeneratedCode);
        Assert.Contains("#else", compiled.GeneratedCode);
    }

    [Fact]
    public void LossyNativeConversion_StillRecordsTheBranchItSelected()
    {
        const string source = """
            public class Probe
            {
                public int Read()
                {
            #if SYNC_ONLY
                    return 2;
            #endif
                    return 1;
                }
            }
            """;
        var result = new CSharpToCalorConverter(HarnessLossyOptions()).Convert(source, "Probe.cs");
        Assert.True(result.Success, string.Join("\n", result.Issues));
        Assert.Equal(2, result.Losses.Count(loss => loss.Kind == ConversionLossKind.PreprocessorStripped));
        Assert.DoesNotContain("return 2", result.CalorSource);
    }

    // ---- Temps the C# cannot compile ----

    [Fact]
    public void ExpressionLambdaArguments_StayInPlaceWithoutUntypedTemps()
    {
        // `Unknown` does not resolve, so the lambda parameter has no inferred type; an untyped
        // `let _lam = (x) => …` temp would be `var _lam = x => …` in C# (CS8917).
        const string csharp = """
            using System.Linq;
            public class Probe
            {
                public void Register(Unknown services, int[] items)
                {
                    services.Add(sp => sp.Get());
                    foreach (var item in items.Where(x => x > 1))
                        services.Use(item);
                }
            }
            """;
        var result = new CSharpToCalorConverter(HarnessLossyOptions()).Convert(csharp, "Probe.cs");
        Assert.True(result.Success, string.Join("\n", result.Issues));
        Assert.DoesNotContain("_lam", result.CalorSource);
        Assert.Contains("services.Add((sp) => sp.Get())", result.CalorSource);
        Assert.True(result.CalorSource!.Contains("items.Where((int:x) => (x > 1))"), result.CalorSource);
    }

    [Fact]
    public void LinqChainLambdas_StayInPlaceAndCompile()
    {
        const string csharp = """
            using System.Collections.Generic;
            using System.Linq;
            public class Probe
            {
                public List<int> Positive(List<int> items) => items.Where(x => x > 0).Select(x => x * 2).ToList();
            }
            """;
        var result = new CSharpToCalorConverter().Convert(csharp);
        Assert.True(result.Success, string.Join("\n", result.Issues));
        Assert.DoesNotContain("_lam", result.CalorSource);
        var compiled = CompileCalor(result.CalorSource!);
        Assert.False(compiled.HasErrors, Diagnostics(compiled));
        Assert.Empty(CSharpErrors(compiled.GeneratedCode));
    }

    [Fact]
    public void UntypedBlockLambdaArgument_PreservesItsMemberInsteadOfAnUntypedTemp()
    {
        const string csharp = """
            public class Probe
            {
                public void Register(Unknown services)
                {
                    services.Add(e =>
                    {
                        e.Run();
                        return e;
                    });
                }
            }
            """;
        var result = new CSharpToCalorConverter(HarnessLossyOptions()).Convert(csharp, "Probe.cs");
        Assert.True(result.Success, string.Join("\n", result.Issues));
        Assert.DoesNotContain("let _lam", result.CalorSource);
        Assert.Contains(result.Issues, issue => issue.Feature == "untyped-block-lambda");
        Assert.True(FeatureSupport.IsKnownPreservationReason("untyped-block-lambda"));
        var compiled = CompileCalor(result.CalorSource!);
        Assert.False(compiled.HasErrors, Diagnostics(compiled));
    }

    [Fact]
    public void ConstructorInitializerArguments_StayInPlaceAndCompile()
    {
        const string csharp = """
            using System.Collections.Generic;
            public class Probe
            {
                private readonly Dictionary<string, int> _map;
                private readonly int _seed;
                public Probe(int seed) : this(seed, new Dictionary<string, int>()) { }
                public Probe(int seed, Dictionary<string, int> map) { _seed = seed; _map = map; }
                public int Count() => _map.Count + _seed;
            }
            """;
        var result = new CSharpToCalorConverter().Convert(csharp);
        Assert.True(result.Success, string.Join("\n", result.Issues));
        Assert.DoesNotContain("_new", result.CalorSource);
        Assert.DoesNotContain("_arg", result.CalorSource);
        var compiled = CompileCalor(result.CalorSource!);
        Assert.False(compiled.HasErrors, Diagnostics(compiled));
        Assert.Empty(CSharpErrors(compiled.GeneratedCode));
        Assert.Contains(": this(seed, new Dictionary<string, int>())", compiled.GeneratedCode);
    }

    [Theory]
    [InlineData("x is IDisposable")]
    [InlineData("x is not IDisposable")]
    public void BareTypeNamePattern_IsATypeTestNotAnEquality(string test)
    {
        var csharp = $$"""
            using System;
            public class Probe
            {
                public bool Check(object x) => {{test}};
            }
            """;
        var result = new CSharpToCalorConverter().Convert(csharp);
        Assert.True(result.Success, string.Join("\n", result.Issues));
        Assert.DoesNotContain("== IDisposable", result.CalorSource);
        Assert.DoesNotContain("!= IDisposable", result.CalorSource);
        var compiled = CompileCalor(result.CalorSource!);
        Assert.False(compiled.HasErrors, Diagnostics(compiled));
        Assert.Empty(CSharpErrors(compiled.GeneratedCode));
        Assert.Contains("is IDisposable", compiled.GeneratedCode);
    }

    [Theory]
    [InlineData("Target = new(1, 2);")]
    [InlineData("Target = new();")]
    public void TargetTypedNewWithAnUnknownTarget_IsPreservedNotGuessed(string assignment)
    {
        // `Holder.Target` is declared in a file this conversion cannot see, so `new(…)` has no
        // known type: `new object(1, 2)` does not compile, and `default` for `new()` is null.
        var csharp = $$"""
            public partial class Probe : Holder
            {
                public void Reset() { {{assignment}} }
            }
            """;
        var result = new CSharpToCalorConverter(HarnessLossyOptions()).Convert(csharp, "Probe.cs");
        Assert.True(result.Success, string.Join("\n", result.Issues));
        Assert.DoesNotContain("new object(", result.CalorSource);
        Assert.DoesNotContain("Target default", result.CalorSource);
        Assert.Contains(assignment, result.CalorSource);
    }

    [Fact]
    public void UnresolvedBareNamePattern_IsPreservedNotLoweredToEquality()
    {
        // No `using System;`: the project supplies it as an implicit global using, which a
        // single-file conversion does not see. `IDisposable` binds to nothing, so it is not
        // known to be a value; `!= IDisposable` would not compile (CS0119).
        const string csharp = """
            public class Probe
            {
                public object Check(object wrapped)
                {
                    if (wrapped is not IDisposable)
                        return "plain";
                    return wrapped;
                }
            }
            """;
        var result = new CSharpToCalorConverter(HarnessLossyOptions()).Convert(csharp, "Probe.cs");
        Assert.True(result.Success, string.Join("\n", result.Issues));
        Assert.DoesNotContain("!= IDisposable", result.CalorSource);
        Assert.Contains(result.Issues, issue => issue.Feature == "complex-is-pattern");
    }

    [Theory]
    [InlineData("public event System.EventHandler<MissingArgs>? Changed;", "Changed")]
    public void ConditionalInvokeOnAnUnresolvedNullableReceiver_IsPreservedNotGivenValue(string declaration, string receiver)
    {
        var csharp = $$"""
            public class Probe
            {
                {{declaration}}
                public void Raise() { {{receiver}}?.Invoke(this, null); }
            }
            """;
        var result = new CSharpToCalorConverter(HarnessLossyOptions()).Convert(csharp, "Probe.cs");
        Assert.True(result.Success, string.Join("\n", result.Issues));
        Assert.DoesNotContain(".Value.Invoke", result.CalorSource);
    }
}
