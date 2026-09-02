using System;
using System.Linq;
using Calor.Compiler;
using Calor.Compiler.Diagnostics;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// v0.17 round 4 — the adversarial review of the R2/R4/S1/R3 branch, findings
/// 1 through 6. Each is written so it fails on the code as reviewed; finding 1
/// does not merely fail there, it kills the test host. Finding 7 was a stale XML
/// doc comment and has no observable behaviour, so it has no test here.
/// See docs/plans/roadmap-v0.17.md §10 Round 4.
/// </summary>
public class V017Round4FindingsTests
{
    // =================================================================
    // Finding 1 — cyclic generic constraints crashed the process
    // =================================================================

    /// <summary>
    /// <c>GetImplicitConversionCost</c> recursed through
    /// <c>GetTypeParameterConstraints</c> with no visited set, and
    /// <c>ParseWhereClause</c> accepts any identifier as a <c>TypeName</c>
    /// constraint without checking for circularity. <c>§WHERE T : U</c> plus
    /// <c>§WHERE U : T</c> made the two chase each other until the stack ran
    /// out — a StackOverflowException, which the Calor0932 handler cannot
    /// catch, so the whole process died rather than reporting anything.
    /// </summary>
    [Fact]
    public void CyclicTypeParameterConstraints_Report_InsteadOfOverflowingTheStack()
    {
        var result = Compile("""
            §M{m001:Cyc}
              §CL{c001:Box:pub}<T,U>
                §WHERE T : U
                §WHERE U : T
                §MT{mt001:Take:pub} (str:s) -> i32
                  §E{}
                  §R INT:1
                §MT{mt002:Go:pub} (T:a) -> i32
                  §E{}
                  §R §C{Take} §A a §/C
            """);

        // Reaching this line at all is the finding: before the fix the test
        // host was killed by the stack overflow.
        Assert.Contains(
            result.Diagnostics,
            d => string.Equals(d.Code, DiagnosticCode.NoMatchingOverload, StringComparison.Ordinal));
    }

    // =================================================================
    // Finding 4 — decorated builtins were misread as "invisible"
    // =================================================================

    /// <summary>
    /// <c>IsNominalTypeInvisibleToThisModule</c> decided "is this a builtin?"
    /// by asking whether every letter was upper case. Every DECORATED canonical
    /// builtin fails that test — <c>FLOAT[bits=32]</c> and
    /// <c>INT[bits=8][signed=true]</c> carry lower-case <c>bits</c>,
    /// <c>signed</c> and <c>true</c> — so the type was declared invisible to the
    /// module, which SUPPRESSES Calor0208. Overload checking was silently off
    /// for every call carrying an i8/u8/i16/u16/f32 argument.
    /// </summary>
    [Theory]
    [InlineData("f32")]
    [InlineData("i8")]
    [InlineData("u8")]
    [InlineData("i16")]
    [InlineData("u16")]
    public void DecoratedBuiltinArgument_StillReportsNoMatchingOverload(string calorType)
    {
        var result = Compile($$"""
            §M{m001:Probe}
              §F{f001:Take:pub} (str:s) -> i32
                §E{}
                §R INT:1
              §F{f002:Go:pub} ({{calorType}}:x) -> i32
                §E{}
                §R §C{Take} §A x §/C
            """);

        Assert.Contains(
            result.Diagnostics,
            d => string.Equals(d.Code, DiagnosticCode.NoMatchingOverload, StringComparison.Ordinal));
    }

    /// <summary>
    /// The companion direction: <c>i32</c> was never affected, and must stay
    /// reported. A fix that simply stopped suppressing everything would pass the
    /// theory above while changing nothing about what the binder knows.
    /// </summary>
    [Fact]
    public void UndeclaredNominalArgument_IsStillSuppressed()
    {
        var result = Compile("""
            §M{m001:Probe}
              §F{f001:Take:pub} (str:s) -> i32
                §E{}
                §R INT:1
              §F{f002:Go:pub} (NotDeclaredAnywhere:x) -> i32
                §E{}
                §R §C{Take} §A x §/C
            """);

        Assert.DoesNotContain(
            result.Diagnostics,
            d => string.Equals(d.Code, DiagnosticCode.NoMatchingOverload, StringComparison.Ordinal));
    }

    // =================================================================
    // Finding 5 — module-level functions never pushed their type parameters
    // =================================================================

    /// <summary>
    /// <c>PushMemberTypeParameters</c> was called only from <c>BindMethod</c>,
    /// so a module-level generic function fell back to
    /// <c>_currentClass?.TypeParameters</c> — null at module scope. A call whose
    /// argument is the function's own type parameter, against an overload taking
    /// that parameter's constraint interface, still reported Calor0208.
    /// </summary>
    [Fact]
    public void ModuleLevelGenericFunction_ResolvesThroughItsOwnConstraint()
    {
        var result = Compile("""
            §M{m001:Reach}
              §IFACE{i001:INote:pub}
              §F{f001:Accept:pub} (INote:n) -> i32
                §E{}
                §R INT:1
              §F{f002:Publish:pub}<TNote> (TNote:n) -> i32
                §WHERE TNote : INote
                §E{}
                §R §C{Accept} §A n §/C
            """);

        Assert.DoesNotContain(
            result.Diagnostics,
            d => string.Equals(d.Code, DiagnosticCode.NoMatchingOverload, StringComparison.Ordinal));
    }

    // =================================================================
    // Finding 3 — pattern variables were invisible to the qualifier
    // =================================================================

    /// <summary>
    /// R4(c) made <c>Visit(ReferenceNode)</c> qualify identifiers through
    /// <c>QualifyCrossModuleTarget</c>, which skips locals "in scope". An
    /// <c>IsPatternNode</c> variable was never passed to
    /// <c>DeclareVarInScope</c>, so it was not in scope by that test and a
    /// reference to it was qualified to the MODULE member it shadows. Loud
    /// (CS0428) when the shapes differ; silently the wrong call when both are
    /// delegate-shaped — the exact outcome #823's suppression exists to prevent.
    /// </summary>
    [Fact]
    public void PatternVariable_ShadowsModuleFunction_AndIsNotQualified()
    {
        var result = Compile("""
            §M{m001:RefProbe}
              §F{f001:value:pub} () -> i32
                §E{}
                §R INT:7
              §CL{c001:Holder:pub}
                §MT{mt001:Use:pub} (any:o) -> i32
                  §E{}
                  §IF{i1} (is o i32 value)
                    §R value
                  §R INT:0
            """);

        Assert.Contains("return value;", result.GeneratedCode);
        Assert.DoesNotContain("RefProbeModule.value;", result.GeneratedCode);
    }

    // =================================================================
    // Finding 6 — §PROP's row was parsed but never emitted back
    // =================================================================

    /// <summary>
    /// S1 gave <c>PropertyNode</c> a <c>Row</c> and taught the parser to read it
    /// in both the compact and block forms, but neither <c>§PROP</c> emission
    /// site in <c>CalorEmitter</c> wrote it out. Anything that round-trips
    /// through that emitter — <c>calor fix</c>, the indent migrator, the C#
    /// converter — stripped a hand-written row, reinstating the escape S1 closed
    /// and without the parse error that used to make it visible.
    /// </summary>
    [Theory]
    [InlineData("§PROP{p001:Name:str:pub:get,set} §E{cw}")]
    [InlineData("§PROP{p001:Name:str:pub} §E{cw}")]
    public void PropertyRow_SurvivesACalorToCalorReEmit(string propertyLine)
    {
        var source = """
            §M{m001:Holder}
              §CL{c001:Box:pub}

            """ + "    " + propertyLine + "\n";

        var reemitted = ReEmit(source);

        Assert.Contains("§E{cw}", reemitted);
    }

    /// <summary>
    /// The same round trip on a property with NO row must not invent one.
    /// </summary>
    [Fact]
    public void PropertyWithoutRow_GainsNoneOnReEmit()
    {
        var reemitted = ReEmit("""
            §M{m001:Holder}
              §CL{c001:Box:pub}
                §PROP{p001:Name:str:pub:get,set}
            """);

        Assert.DoesNotContain("§E{", reemitted);
    }

    // =================================================================
    // Finding 2 — unnameable types were written into the §B header
    // =================================================================

    /// <summary>
    /// R4(a) types a <c>var</c> whose initializer is an invocation from the
    /// semantic model. The guard covered error types and <c>void</c> but not
    /// anonymous types or tuples, and <c>MinimallyQualifiedFormat</c> renders
    /// those as <c>&lt;anonymous type: string Name, int Len&gt;</c> and
    /// <c>(bool ok, int count)</c> — both carrying <c>:</c>, <c>,</c> and
    /// spaces, exactly what <c>ParseAttributes</c> splits the header on. The
    /// emitted Calor no longer compiled. An untyped binding is the right answer
    /// when the type cannot be spelled, which is what main produced.
    /// </summary>
    [Theory]
    [InlineData("var q = items.Select(x => new { Name = x, Len = x.Length });", "q")]
    [InlineData("var t = Split(\"a,b\");", "t")]
    public void UnspellableInferredType_LeavesTheBindingUntyped(string statement, string name)
    {
        var calor = ConvertToCalor($$"""
            using System;
            using System.Collections.Generic;
            using System.Linq;

            public class Probe
            {
                public static (bool ok, int count) Split(string s) => (true, s.Length);

                public void Run(List<string> items)
                {
                    {{statement}}
                }
            }
            """);

        Assert.Contains($"§B{{{name}}}", calor);
        Assert.DoesNotContain("anonymous", calor);
    }

    /// <summary>
    /// R4(a)'s intended case must keep working: a LINQ chain still gets its
    /// element type written down, which is what restored the type chain that
    /// #1128 was about.
    /// </summary>
    [Fact]
    public void NameableInferredType_IsStillWrittenDown()
    {
        var calor = ConvertToCalor("""
            using System;
            using System.Collections.Generic;
            using System.Linq;

            public class Probe
            {
                public void Run(List<int> numbers)
                {
                    var evens = numbers.Where(n => n % 2 == 0);
                    Console.WriteLine(evens.Count());
                }
            }
            """);

        Assert.Contains("§B{Seq<i32>:evens}", calor);
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private static string ConvertToCalor(string csharpSource)
    {
        var converter = new Calor.Compiler.Migration.CSharpToCalorConverter(
            new Calor.Compiler.Migration.ConversionOptions
            {
                Fidelity = Calor.Compiler.Migration.ConversionFidelity.Lossy,
            });
        var result = converter.Convert(csharpSource);
        Assert.True(result.Success);
        return new Calor.Compiler.Migration.CalorEmitter().Emit(result.Ast!);
    }

    private static CompilationResult Compile(string source)
    {
        var options = new CompilationOptions
        {
            ContractMode = ContractMode.Debug,
            StrictEffects = false,
            VerifyContracts = false,
        };
        return Program.Compile(source, "v017-round3.calr", options);
    }

    /// <summary>
    /// Parses Calor and emits Calor again through
    /// <see cref="Calor.Compiler.Migration.CalorEmitter"/> — the path
    /// <c>calor fix</c> and the indent migrator take.
    /// </summary>
    private static string ReEmit(string source)
    {
        var diagnostics = new DiagnosticBag();
        var tokens = new Calor.Compiler.Parsing.Lexer(source, diagnostics).Tokenize();
        var ast = new Calor.Compiler.Parsing.Parser(tokens, diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("; ", diagnostics.Select(d => d.Code + " " + d.Message)));
        Assert.NotNull(ast);
        return new Calor.Compiler.Migration.CalorEmitter().Emit(ast!);
    }
}
