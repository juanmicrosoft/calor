using System.Reflection;
using System.Runtime.Loader;
using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Migration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// #1600: binding/lambda replacements retain local resolution, effect, capture,
/// verification and round-trip controls. Retired nested §F forms fail explicitly.
/// Conversion from C# is covered separately in LocalFunctionConversionTests.
/// </summary>
public class LocalFunctionLanguageTests
{
    private const string ShadowingModule = """
        §M{Shadow}
          §M{Probe:pub}
            §MT{Add:pub:stat} (i32:x) -> i32
              §E{cw}
              §P "noisy"
              §R (+ x 1000)

            §MT{Quiet:pub:stat} () -> i32
              §E{}
              §B{Add:Func<i32,i32>} §LAM{static:x:i32}
                §R (+ x 1)
              §/LAM
              §R §C{Add} §A 2 §/C

            §MT{Run:pub:stat} () -> str
              §E{}
              §R (+ "" §C{Quiet} §/C)
        """;

    [Fact]
    public void NativeLocal_ShadowsSameNamedMember_ForResolutionAndEffects()
    {
        // The class Add prints; the local Add is pure. Quiet and Run declare §E{}:
        // Charging the class method instead of the lambda would be Calor0410.
        var compilation = Program.Compile(ShadowingModule, "shadow.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Errors));
        Assert.Equal("3", Run(compilation.GeneratedCode));
    }

    [Theory]
    [InlineData("§B{Help:Func<i32,i32>} §LAM{static:x:i32}\n        §P \"hidden\"\n        §R x\n      §/LAM\n      §R §C{Help} §A 1 §/C", "Calor0410")]
    [InlineData("§B{Help:Func<i32,i32>} §LAM{static:x:i32}\n        §R x\n      §/LAM\n      §R §C{Help} §A 1 §/C", null)]
    [InlineData("§B{Help:Func<i32,i32>} §LAM{static:x:i32}\n        §P \"deferred\"\n        §R x\n      §/LAM\n      §R 1", "Calor0410")]
    public void NativeLocal_LambdaBodyEffectsAreChargedToTheEnclosingCallable(string body, string? expectedCode)
    {
        var source = "§M{E}\n  §M{Probe:pub}\n    §MT{Get:pub:stat} () -> i32\n      §E{}\n      "
            + body + "\n";
        var compilation = Program.Compile(source, "effects.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        if (expectedCode == null)
            Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Errors));
        else
            Assert.Contains(compilation.Diagnostics.Errors, d => d.Code == expectedCode);
    }

    [Fact]
    public void NativeLocal_ShadowsModuleFunction_ForTypeChecking()
    {
        // The module Add takes and returns str; the local Add takes and returns i32.
        // The type checker must see the local signature, or `(+ y 1)` is a type error.
        var source = """
            §M{TypeShadow}
              §F{Add:pub} (str:x) -> str
                §R (+ x "!")

              §F{Use:pub} () -> i32
                §B{Add:Func<i32,i32>} §LAM{static:x:i32}
                  §R (+ x 1)
                §/LAM
                §B{y} §C{Add} §A 2 §/C
                §R (+ y 1)
            """;
        var compilation = Program.Compile(source, "typeshadow.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Errors));
        var binding = Assert.IsType<BindStatementNode>(compilation.Ast!.Functions.Single(f => f.Name == "Use").Body[0]);
        Assert.Equal("Add", binding.Name);
        Assert.True(Assert.IsType<LambdaExpressionNode>(binding.Initializer).IsStatic);
    }

    [Fact]
    public void NativeLocal_EscapingDelegate_CarriesTheLocalBodyRow()
    {
        var source = """
            §M{Escape}
              §M{Probe:pub}
                §MT{Help:pub:stat} (i32:x) -> i32
                  §E{}
                  §R x

                §MT{Get:pub:stat} () -> Func<i32, i32> §E{}
                  §E{cw}
                  §B{Help:Func<i32,i32>} §LAM{static:x:i32}
                    §P "hidden"
                    §R x
                  §/LAM
                  §R Help
            """;
        var compilation = Program.Compile(source, "escape.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        // The pure class Help must not stand in for the printing local Help.
        Assert.Contains(compilation.Diagnostics.Errors, d => d.Code == "Calor0424");
    }

    [Fact]
    public void NativeLocal_DelegateInvokedInPlace_IsNotUnknown()
    {
        var source = """
            §M{Del}
              §M{Probe:pub}
                §MT{Run:pub:stat} () -> str
                  §E{cw}
                  §B{Later:Func<i32,i32>} §LAM{static:y:i32}
                    §P "called"
                    §R (* y 10)
                  §/LAM
                  §B{Func<i32, i32>:f} Later
                  §R (+ "" §C{f} §A 4 §/C)
            """;
        var compilation = Program.Compile(source, "del.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Errors));
        Assert.Equal("40", Run(compilation.GeneratedCode));
    }

    [Theory]
    [InlineData("§IF (> n 0)\n      §F{L} (i32:x) -> i32\n        §R x\n      §R §C{L} §A n §/C\n    §R 0", "conditional placement", 1)]
    [InlineData("§F{L} (i32:x) -> i32\n      §F{N} (i32:y) -> i32\n        §R y\n      §R §C{N} §A x §/C\n    §R §C{L} §A n §/C", "nested placement", 2)]
    [InlineData("§F{L} (i32:x) -> i32\n      §E{}\n      §R x\n    §R §C{L} §A n §/C", "effect row", 1)]
    [InlineData("§F{L} (i32:x) -> i32\n      §Q (> x 0)\n      §R x\n    §R §C{L} §A n §/C", "contracts", 1)]
    [InlineData("§F{L}<T> (T:x) -> T\n      §R x\n    §R §C{L} §A n §/C", "generic", 1)]
    [InlineData("§F{L} (i32:x) -> IEnumerable<i32>\n      §YIELD x\n    §R 0", "iterator", 1)]
    public void RetiredLocal_OutsideTheShape_IsRejectedWithLambdaGuidance(string body, string message, int expectedCount)
    {
        var source = "§M{Bad}\n  §F{Outer:pub} (i32:n) -> i32\n    " + body + "\n";
        var compilation = Program.Compile(source, "bad.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.True(compilation.HasErrors, message);
        AssertRetiredLocal(compilation, expectedCount);
    }

    [Fact]
    public void NativeLocal_Capture_IsRejectedEvenWhenAFieldHasTheSameName()
    {
        // `n` is the outer parameter in C#, not the field: the static lambda
        // makes that a compile error rather than a silent rebind to the field.
        var source = """
            §M{Cap}
              §M{Probe:pub}
                §FLD{i32:n:priv:stat} 5

                §MT{Get:pub:stat} (i32:n) -> i32
                  §B{AddN:Func<i32,i32>} §LAM{static:x:i32}
                    §R (+ x n)
                  §/LAM
                  §R §C{AddN} §A 1 §/C
            """;
        var compilation = Program.Compile(source, "cap.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.Contains(compilation.Diagnostics.Errors,
            d => d.Code == "Calor1002" && d.Message.Contains("CS8820"));
    }

    [Theory]
    [InlineData(6, true)]   // a false postcondition: the runtime guard must fire
    [InlineData(5, false)]  // a true one: still not proven from an omitted body
    public void NativeLocal_Verification_ClaimsNoProofFromTheLocalBody(int returned, bool throws)
    {
        var source = $$"""
            §M{Ver}
              §M{Probe:pub}
                §MT{Five:pub:stat} () -> i32
                  §S (== result 5)
                  §B{Value:Func<i32>} §LAM{static}
                    §R {{returned}}
                  §/LAM
                  §R §C{Value} §/C

                §MT{Run:pub:stat} () -> str
                  §R (+ "" §C{Five} §/C)
            """;
        var compilation = Program.Compile(source, "ver.calr", new CompilationOptions
        {
            StatusWriter = TextWriter.Null,
            VerifyContracts = true,
            ElideProvenGuards = true
        });
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Errors));
        Assert.Contains("ContractViolationException", compilation.GeneratedCode);
        Assert.DoesNotContain("// PROVEN", compilation.GeneratedCode);
        if (throws)
            Assert.ThrowsAny<Exception>(() => Run(compilation.GeneratedCode));
        else
            Assert.Equal("5", Run(compilation.GeneratedCode));
    }

    // ---- Codex review round 1 regressions ----

    [Fact]
    public void Review1_LocalShadowingAModuleFunction_IsNotQualifiedAway()
    {
        // The module Add prints and adds 1000; the call and the method group must
        // stay on the local Add (emission once qualified both to the module Add).
        var source = """
            §M{Shadow}
              §F{Add:pub} (i32:x) -> i32
                §E{cw}
                §P "wrong"
                §R (+ x 1000)
              §M{Probe:pub}
                §MT{Run:pub:stat} () -> str
                  §E{}
                  §B{Add:Func<i32,i32>} §LAM{static:x:i32}
                    §R (+ x 1)
                  §/LAM
                  §B{Func<i32, i32>:g} Add
                  §R (+ (+ "" §C{Add} §A 2 §/C) §C{g} §A 0 §/C)
            """;
        var compilation = Program.Compile(source, "r1a.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Errors));
        Assert.DoesNotContain("ShadowModule.Add", compilation.GeneratedCode);
        Assert.Equal("31", Run(compilation.GeneratedCode));
    }

    [Fact]
    public void Review1_CaptureOfAnEnclosingName_IsNeverQualifiedToAModuleFunction()
    {
        var source = """
            §M{Cap}
              §F{Add:pub} (i32:x) -> i32
                §R (+ x 1000)
              §M{Probe:pub}
                §MT{Get:pub:stat} (Func<i32, i32>:Add §E{}) -> i32
                  §E{cw}
                  §B{L:Func<i32>} §LAM{static}
                    §R §C{Add} §A 1 §/C
                  §/LAM
                  §R §C{L} §/C
            """;
        var compilation = Program.Compile(source, "r1b.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.Contains(compilation.Diagnostics.Errors,
            d => d.Code == "Calor1002" && d.Message.Contains("CS8820"));
    }

    [Theory]
    [InlineData("§R §LAM{x:i32} §E{} §C{Help} §A x §/C §/LAM")]
    [InlineData("§R §LAM{x:i32} §C{Help} §A x §/C §/LAM")]
    public void Review1_LambdaUsingALocalFunction_CarriesItsEffects(string returned)
    {
        var source = "§M{Escape}\n  §M{Probe:pub}\n"
            + "    §MT{Get:pub:stat} () -> Func<i32, i32> §E{}\n      §E{cw}\n"
            + "      §B{Help:Func<i32,i32>} §LAM{static:x:i32}\n        §P \"hidden\"\n        §R x\n      §/LAM\n"
            + "      " + returned + "\n";
        var compilation = Program.Compile(source, "r2.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.Contains(compilation.Diagnostics.Errors,
            d => d.Code is "Calor0410" or "Calor0424");
    }

    [Fact]
    public void Review1_ASameNamedDelegateField_DoesNotShadowTheLocalFunctionRow()
    {
        var source = """
            §M{Escape}
              §M{Probe:pub}
                §FLD{Func<i32, i32>:Help:priv:stat} §E{}
                §MT{Get:pub:stat} () -> Func<i32, i32> §E{}
                  §E{cw}
                  §B{Help:Func<i32,i32>} §LAM{static:x:i32}
                    §P "hidden"
                    §R x
                  §/LAM
                  §R Help
            """;
        var compilation = Program.Compile(source, "r3.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.Contains(compilation.Diagnostics.Errors, d => d.Code == "Calor0424");
    }

    [Fact]
    public void Review1_AParameterShadowingASiblingLocal_IsInvokedAsAValue()
    {
        // Inside Other, Add is the delegate parameter (C#), not the class Add(str)
        // and not the sibling local Add: no Calor0208, and its row is Unknown.
        var source = """
            §M{Binder}
              §M{Probe:pub}
                §MT{Add:pub:stat} (str:x) -> str
                  §R x
                §MT{M:pub:stat} () -> i32
                  §B{Add:Func<i32,i32>} §LAM{static:x:i32}
                    §R (+ x 1)
                  §/LAM
                  §B{Other:Func<Func<i32,i32>,i32>} §LAM{static:Add:Func<i32,i32>}
                    §R §C{Add} §A 10 §/C
                  §/LAM
                  §R §C{Other} §A Add §/C
            """;
        var compilation = Program.Compile(source, "r4.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.DoesNotContain(compilation.Diagnostics.Errors, d => d.Code == "Calor0208");
        Assert.Contains(compilation.Diagnostics.Errors,
            d => d.Code == "Calor0410" && d.Message.Contains("unknown"));
    }

    [Fact]
    public void Review1_RetiredLocalFunctionInsideALambda_HasReplacementGuidance()
    {
        var source = """
            §M{BadPlacement}
              §F{Make:pub} () -> Func<i32, i32> §E{}
                §E{}
                §R §LAM{x:i32} §E{}
                  §F{L} (i32:y) -> i32
                    §R y
                  §R x
                §/LAM
            """;
        var compilation = Program.Compile(source, "r5.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        AssertRetiredLocal(compilation);
    }

    [Fact]
    public void Review1_RetiredEmbeddedGenericLocalName_HasReplacementGuidance()
    {
        var source = "§M{Bad}\n  §F{Outer:pub} () -> i32\n    §F{L<T>} (i32:x) -> i32\n      §R x\n    §R 0\n";
        var compilation = Program.Compile(source, "r7.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        AssertRetiredLocal(compilation);
    }

    // ---- Codex review round 2 regressions ----

    [Fact]
    public void Review2_AnAliasOfALocalFunction_IgnoresASameNamedField()
    {
        // `f` aliases the printing local Help, not the pure field Help; invoked in
        // an escaping lambda it must carry cw.
        var source = """
            §M{Escape}
              §M{Probe:pub}
                §FLD{Func<i32, i32>:Help:priv:stat} §E{}
                §MT{Get:pub:stat} () -> Func<i32, i32> §E{}
                  §E{cw}
                  §B{Help:Func<i32,i32>} §LAM{static:x:i32}
                    §P "hidden"
                    §R x
                  §/LAM
                  §B{Func<i32, i32>:f} Help
                  §R §LAM{x:i32} §C{f} §A x §/C §/LAM
            """;
        var compilation = Program.Compile(source, "r2a.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.Contains(compilation.Diagnostics.Errors,
            d => d.Code is "Calor0410" or "Calor0424" or "Calor0425");
    }

    [Fact]
    public void Review2_AnEnclosingPatternVariable_IsNeverQualifiedToAModuleFunction()
    {
        var source = """
            §M{Cap}
              §F{Add:pub} (i32:x) -> i32
                §R (+ x 1000)
              §M{Probe:pub}
                §MT{Get:pub:stat} (object:o) -> i32
                  §IF (is o Func<i32, i32> Add)
                    §R 1
                  §B{L:Func<i32>} §LAM{static}
                    §R §C{Add} §A 1 §/C
                  §/LAM
                  §R §C{L} §/C
            """;
        var compilation = Program.Compile(source, "r2b.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.Contains(compilation.Diagnostics.Errors,
            d => d.Code == "Calor1002" && d.Message.Contains("CS8820"));
    }

    [Fact]
    public void Review2_ExpressionCallSpelling_IgnoresASameNamedField()
    {
        var source = """
            §M{Escape}
              §M{Probe:pub}
                §FLD{Func<i32, i32>:Help:priv:stat} §E{}
                §MT{Get:pub:stat} () -> Func<i32, i32> §E{}
                  §E{cw}
                  §B{Help:Func<i32,i32>} §LAM{static:x:i32}
                    §P "hidden"
                    §R x
                  §/LAM
                  §R §LAM{x:i32} §C Help §A x §/C §/LAM
            """;
        var compilation = Program.Compile(source, "r2c.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.Contains(compilation.Diagnostics.Errors, d => d.Code == "Calor0424");
    }

    [Fact]
    public void Review2_AConditionalReturningALocalFunction_CarriesItsRow()
    {
        var source = """
            §M{Escape}
              §M{Probe:pub}
                §MT{Get:pub:stat} (bool:c) -> Func<i32, i32> §E{}
                  §E{cw}
                  §B{Help:Func<i32,i32>} §LAM{static:x:i32}
                    §P "hidden"
                    §R x
                  §/LAM
                  §R (? c Help Help)
            """;
        var compilation = Program.Compile(source, "r2d.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.Contains(compilation.Diagnostics.Errors, d => d.Code == "Calor0424");
    }

    // ---- Codex review round 3 regressions ----

    [Theory]
    // An expression-call argument, a null-coalescing operand, a qualified store:
    // These formerly unchecked local-function positions must not re-enable §F.
    [InlineData("§R §LAM{x:i32} §C Take §A Help §/C §/LAM")]
    [InlineData("§R (?? f Help)")]
    [InlineData("§ASSIGN Probe.Saved Help\n      §R f")]
    public void Review3_RetiredLocalValueInUncheckedPositions_HasReplacementGuidance(string body)
    {
        var source = "§M{Escape}\n  §M{Probe:pub}\n"
            + "    §FLD{Func<i32, i32>:Saved:pub:stat} §E{}\n"
            + "    §MT{Take:pub:stat} (Func<i32, i32>:g §E{}) -> i32\n      §E{}\n      §R §C{g} §A 1 §/C\n"
            + "    §MT{Get:pub:stat} (Func<i32, i32>:f) -> Func<i32, i32> §E{}\n      §E{cw,mut}\n      "
            + body + "\n      §F{Help} (i32:x) -> i32\n        §P \"hidden\"\n        §R x\n";
        var compilation = Program.Compile(source, "r3.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        AssertRetiredLocal(compilation);
    }

    [Theory]
    [InlineData("Add", false)]
    [InlineData("Add.Invoke", true)]
    public void Review3_BindValidation_DoesNotUseAShadowedModuleSignature(string target, bool unknownInvocation)
    {
        var source = $$"""
            §M{Shadow}
              §U{System.Collections.Generic}
              §F{Add:pub} (List<i32>:xs) -> [i32]
                §E{alloc}
                §R §ARR{i32} §/ARR
              §F{Use:pub} (List<i32>:xs) -> List<i32>
                §B{Add:Func<List<i32>,List<i32>>} §LAM{static:xs:List<i32>}
                  §R xs
                §/LAM
                §B{List<i32>:y} §C{{{target}}} §A xs §/C
                §R y
            """;
        var compilation = Program.Compile(source, "r3e.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.DoesNotContain(compilation.Diagnostics.Errors, d => d.Code == "Calor0254");
        AssertBindingOutcome(compilation, unknownInvocation);
    }

    [Theory]
    [InlineData("Pick", false)]
    [InlineData("Pick.Invoke", true)]
    public void LocalValue_ArrayArgumentsDoNotUseTheShadowedModuleSignature(string target, bool unknownInvocation)
    {
        var source = $$"""
            §M{ArgumentShadow}
              §U{System.Collections.Generic}
              §F{Pick:pub} (List<i32>:xs) -> i32
                §R 0
              §F{Use:pub} () -> i32
                §E{alloc}
                §B{Pick:Func<[i32],i32>} §LAM{static:xs:[i32]}
                  §R 1
                §/LAM
                §R §C{{{target}}} §A §ARR{i32} §/ARR §/C
            """;
        var compilation = Program.Compile(source, "argument-shadow.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        AssertBindingOutcome(compilation, unknownInvocation);
    }

    [Fact]
    public void Review3_AnOutOfScopeEnclosingName_FailsLoudlyNotSilently()
    {
        // The retired form must fail before it can silently rebind an out-of-scope
        // enclosing name to the module function.
        var source = """
            §M{Shadow}
              §F{Add:pub} (i32:x) -> i32
                §R (+ x 1000)
              §M{Probe:pub}
                §MT{Get:pub:stat} () -> i32
                  §L{Add:0:1:1}
                    §B{i32:t} Add
                  §F{L} () -> i32
                    §R §C{Add} §A 1 §/C
                  §R §C{L} §/C
            """;
        var compilation = Program.Compile(source, "r3d.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        AssertRetiredLocal(compilation);
    }

    [Fact]
    public void NativeLocal_RoundTripsThroughTheCalorEmitter()
    {
        var parsed = Program.Compile(ShadowingModule, "rt.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(parsed.HasErrors);
        var emitted = new CalorEmitter().Emit(parsed.Ast!);
        Assert.Contains("§LAM{static:x:i32}", emitted);
        Assert.DoesNotContain("§F{Add}", emitted);
        var again = Program.Compile(emitted, "rt2.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(again.HasErrors, string.Join("\n", again.Diagnostics.Errors) + "\n" + emitted);
        Assert.Equal("3", Run(again.GeneratedCode));
    }

    private static void AssertBindingOutcome(CompilationResult compilation, bool unknownInvocation)
    {
        if (unknownInvocation)
        {
            var error = Assert.Single(compilation.Diagnostics.Errors);
            Assert.Equal("Calor0410", error.Code);
            Assert.Equal("Function 'Use' uses effect 'unknown' but does not declare it", error.Message);
        }
        else
        {
            Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Errors));
        }
    }

    private static void AssertRetiredLocal(CompilationResult compilation, int expectedCount = 1)
    {
        var errors = compilation.Diagnostics.Errors.ToArray();
        Assert.Equal(expectedCount, errors.Length);
        Assert.All(errors, error =>
        {
            Assert.Equal("Calor0100", error.Code);
            Assert.Equal("Local §F declarations are removed. Use a §B binding with §LAM instead.", error.Message);
        });
    }

    /// <summary>Compiles C# in memory and returns <c>Probe.Run()</c>.</summary>
    private static string Run(string csharp)
    {
        var compilation = CSharpCompilation.Create("F3_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(csharp)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emission = compilation.Emit(stream);
        Assert.True(emission.Success, string.Join(Environment.NewLine, emission.Diagnostics) + "\n" + csharp);
        stream.Position = 0;
        var context = new AssemblyLoadContext(compilation.AssemblyName!, isCollectible: true);
        try
        {
            var probe = context.LoadFromStream(stream).GetTypes().Single(type => type.Name == "Probe");
            var run = probe.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!;
            try
            {
                return Assert.IsType<string>(run.Invoke(null, null));
            }
            catch (TargetInvocationException invocation) when (invocation.InnerException != null)
            {
                throw invocation.InnerException;
            }
        }
        finally
        {
            context.Unload();
        }
    }
}
