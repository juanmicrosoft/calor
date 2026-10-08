using System.Reflection;
using System.Runtime.Loader;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Migration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// 0.25 F3 (#847): the native local-function construct, a nested <c>§F</c> in a
/// <c>§F</c>/<c>§MT</c> body emitted in place as a C# <c>static</c> local function:
/// name resolution, effects, Calor0211, capture rejection, verification and the
/// Calor-emitter round trip. Conversion from C# is in LocalFunctionConversionTests.
/// </summary>
public class LocalFunctionLanguageTests
{
    // ---- Native Calor: the construct itself ----

    private const string ShadowingModule = """
        §M{m001:Shadow}
          §CL{c001:Probe:pub:stat}
            §MT{m002:Add:pub:stat} (i32:x) -> i32
              §E{cw}
              §P "noisy"
              §R (+ x 1000)

            §MT{m003:Quiet:pub:stat} () -> i32
              §E{}
              §R §C{Add} §A 2 §/C
              §F{f001:Add} (i32:x) -> i32
                §R (+ x 1)

            §MT{m004:Run:pub:stat} () -> str
              §E{}
              §R (+ "" §C{Quiet} §/C)
        """;

    [Fact]
    public void NativeLocal_ShadowsSameNamedMember_ForResolutionAndEffects()
    {
        // The class Add prints; the local Add is pure. Quiet and Run declare §E{}:
        // charging the class method (a silent rebind in the effect pass) would be
        // Calor0410. The call is emitted in place and runs the local function.
        var compilation = Program.Compile(ShadowingModule, "shadow.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Errors));
        Assert.Equal("3", Run(compilation.GeneratedCode));
    }

    [Theory]
    // A local body's effect is charged to the enclosing callable, called or not.
    [InlineData("§F{f001:Help} (i32:x) -> i32\n        §P \"hidden\"\n        §R x\n      §R 1", "Calor0410")]
    // Escaping as a value: its row is the union of the local bodies, never a member's.
    [InlineData("§F{f001:Help} (i32:x) -> i32\n        §R x\n      §R §C{Help} §A 1 §/C", null)]
    public void NativeLocal_EffectsAreChargedToTheEnclosingCallable(string body, string? expectedCode)
    {
        var source = "§M{m001:E}\n  §CL{c001:Probe:pub:stat}\n    §MT{m002:Get:pub:stat} () -> i32\n      §E{}\n      "
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
            §M{m001:TypeShadow}
              §F{f001:Add:pub} (str:x) -> str
                §R (+ x "!")

              §F{f002:Use:pub} () -> i32
                §B{y} §C{Add} §A 2 §/C
                §R (+ y 1)
                §F{f003:Add} (i32:x) -> i32
                  §R (+ x 1)
            """;
        var compilation = Program.Compile(source, "typeshadow.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Errors));
        Assert.Contains("static int Add(int x)", compilation.GeneratedCode);
    }

    [Fact]
    public void NativeLocal_EscapingDelegate_CarriesTheLocalBodyRow()
    {
        var source = """
            §M{m001:Escape}
              §CL{c001:Probe:pub:stat}
                §MT{m002:Help:pub:stat} (i32:x) -> i32
                  §E{}
                  §R x

                §MT{m003:Get:pub:stat} () -> Func<i32, i32> §E{}
                  §E{cw}
                  §F{f001:Help} (i32:x) -> i32
                    §P "hidden"
                    §R x
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
            §M{m001:Del}
              §CL{c001:Probe:pub:stat}
                §MT{m002:Run:pub:stat} () -> str
                  §E{cw}
                  §B{Func<i32, i32>:f} Later
                  §R (+ "" §C{f} §A 4 §/C)
                  §F{f001:Later} (i32:y) -> i32
                    §P "called"
                    §R (* y 10)
            """;
        var compilation = Program.Compile(source, "del.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Errors));
        Assert.Equal("40", Run(compilation.GeneratedCode));
    }

    [Theory]
    [InlineData("§IF{if1} (> n 0)\n      §F{f001:L} (i32:x) -> i32\n        §R x\n      §R §C{L} §A n §/C\n    §R 0", "must be declared directly")]
    [InlineData("§F{f001:L} (i32:x) -> i32\n      §F{f002:N} (i32:y) -> i32\n        §R y\n      §R §C{N} §A x §/C\n    §R §C{L} §A n §/C", "must be declared directly")]
    [InlineData("§F{f001:L} (i32:x) -> i32\n      §E{}\n      §R x\n    §R §C{L} §A n §/C", "effect row")]
    [InlineData("§F{f001:L} (i32:x) -> i32\n      §Q (> x 0)\n      §R x\n    §R §C{L} §A n §/C", "contracts")]
    [InlineData("§F{f001:L}<T> (T:x) -> T\n      §R x\n    §R §C{L} §A n §/C", "generic")]
    [InlineData("§F{f001:L} (i32:x) -> IEnumerable<i32>\n      §YIELD x\n    §R 0", "iterator")]
    public void NativeLocal_OutsideTheShape_IsCalor0211(string body, string message)
    {
        var source = "§M{m001:Bad}\n  §F{f000:Outer:pub} (i32:n) -> i32\n    " + body + "\n";
        var compilation = Program.Compile(source, "bad.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        var error = Assert.Single(compilation.Diagnostics.Errors, d => d.Code == "Calor0211");
        Assert.Contains(message, error.Message);
    }

    [Fact]
    public void NativeLocal_Capture_IsRejectedEvenWhenAFieldHasTheSameName()
    {
        // `n` is the outer parameter in C#, not the field: the static local function
        // makes that a compile error rather than a silent rebind to the field.
        var source = """
            §M{m001:Cap}
              §CL{c001:Probe:pub:stat}
                §FLD{i32:n:priv:stat} 5

                §MT{m002:Get:pub:stat} (i32:n) -> i32
                  §F{f001:AddN} (i32:x) -> i32
                    §R (+ x n)
                  §R §C{AddN} §A 1 §/C
            """;
        var compilation = Program.Compile(source, "cap.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.Contains(compilation.Diagnostics.Errors,
            d => d.Code == "Calor1002" && d.Message.Contains("CS8421"));
    }

    [Theory]
    [InlineData(6, true)]   // a false postcondition: the runtime guard must fire
    [InlineData(5, false)]  // a true one: still not proven from an omitted body
    public void NativeLocal_Verification_ClaimsNoProofFromTheLocalBody(int returned, bool throws)
    {
        var source = $$"""
            §M{m001:Ver}
              §CL{c001:Probe:pub:stat}
                §MT{m002:Five:pub:stat} () -> i32
                  §S (== result 5)
                  §F{f001:Value} () -> i32
                    §R {{returned}}
                  §R §C{Value} §/C

                §MT{m003:Run:pub:stat} () -> str
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
            §M{m001:Shadow}
              §F{f001:Add:pub} (i32:x) -> i32
                §E{cw}
                §P "wrong"
                §R (+ x 1000)
              §CL{c001:Probe:pub:stat}
                §MT{m002:Run:pub:stat} () -> str
                  §E{}
                  §B{Func<i32, i32>:g} Add
                  §R (+ (+ "" §C{Add} §A 2 §/C) §C{g} §A 0 §/C)
                  §F{f002:Add} (i32:x) -> i32
                    §R (+ x 1)
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
            §M{m001:Cap}
              §F{f001:Add:pub} (i32:x) -> i32
                §R (+ x 1000)
              §CL{c001:Probe:pub:stat}
                §MT{m002:Get:pub:stat} (Func<i32, i32>:Add) -> i32
                  §E{cw}
                  §F{f003:L} () -> i32
                    §R §C{Add} §A 1 §/C
                  §R §C{L} §/C
            """;
        var compilation = Program.Compile(source, "r1b.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.Contains(compilation.Diagnostics.Errors,
            d => d.Code == "Calor1002" && d.Message.Contains("CS8421"));
    }

    [Theory]
    [InlineData("§R §LAM{l001:x:i32} §E{} §C{Help} §A x §/C §/LAM{l001}")]
    [InlineData("§R §LAM{l001:x:i32} §C{Help} §A x §/C §/LAM{l001}")]
    public void Review1_LambdaUsingALocalFunction_CarriesItsEffects(string returned)
    {
        var source = "§M{m001:Escape}\n  §CL{c001:Probe:pub:stat}\n"
            + "    §MT{m002:Get:pub:stat} () -> Func<i32, i32> §E{}\n      §E{cw}\n"
            + "      §F{f001:Help} (i32:x) -> i32\n        §P \"hidden\"\n        §R x\n"
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
            §M{m001:Escape}
              §CL{c001:Probe:pub:stat}
                §FLD{Func<i32, i32>:Help:priv:stat} §E{}
                §MT{m002:Get:pub:stat} () -> Func<i32, i32> §E{}
                  §E{cw}
                  §F{f001:Help} (i32:x) -> i32
                    §P "hidden"
                    §R x
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
            §M{m001:Binder}
              §CL{c001:Probe:pub:stat}
                §MT{m001:Add:pub:stat} (str:x) -> str
                  §R x
                §MT{m002:M:pub:stat} () -> i32
                  §F{f001:Add} (i32:x) -> i32
                    §R (+ x 1)
                  §F{f002:Other} (Func<i32, i32>:Add) -> i32
                    §R §C{Add} §A 10 §/C
                  §R §C{Other} §A Add §/C
            """;
        var compilation = Program.Compile(source, "r4.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.DoesNotContain(compilation.Diagnostics.Errors, d => d.Code == "Calor0208");
        Assert.Contains(compilation.Diagnostics.Errors,
            d => d.Code == "Calor0410" && d.Message.Contains("unknown"));
    }

    [Fact]
    public void Review1_LocalFunctionInsideALambda_IsCalor0211()
    {
        var source = """
            §M{m001:BadPlacement}
              §F{f001:Make:pub} () -> Func<i32, i32> §E{}
                §E{}
                §R §LAM{l001:x:i32} §E{}
                  §F{f002:L} (i32:y) -> i32
                    §R y
                  §R x
                §/LAM{l001}
            """;
        var compilation = Program.Compile(source, "r5.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.Contains(compilation.Diagnostics.Errors,
            d => d.Code == "Calor0211" && d.Message.Contains("must be declared directly"));
    }

    [Fact]
    public void Review1_EmbeddedGenericLocalName_IsCalor0211()
    {
        var source = "§M{m001:Bad}\n  §F{f001:Outer:pub} () -> i32\n    §F{f002:L<T>} (i32:x) -> i32\n      §R x\n    §R 0\n";
        var compilation = Program.Compile(source, "r7.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.Contains(compilation.Diagnostics.Errors,
            d => d.Code == "Calor0211" && d.Message.Contains("generic"));
    }

    // ---- Codex review round 2 regressions ----

    [Fact]
    public void Review2_AnAliasOfALocalFunction_IgnoresASameNamedField()
    {
        // `f` aliases the printing local Help, not the pure field Help; invoked in
        // an escaping lambda it must carry cw.
        var source = """
            §M{m001:Escape}
              §CL{c001:Probe:pub:stat}
                §FLD{Func<i32, i32>:Help:priv:stat} §E{}
                §MT{m002:Get:pub:stat} () -> Func<i32, i32> §E{}
                  §E{cw}
                  §B{Func<i32, i32>:f} Help
                  §R §LAM{l001:x:i32} §C{f} §A x §/C §/LAM{l001}
                  §F{f001:Help} (i32:x) -> i32
                    §P "hidden"
                    §R x
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
            §M{m001:Cap}
              §F{f001:Add:pub} (i32:x) -> i32
                §R (+ x 1000)
              §CL{c001:Probe:pub:stat}
                §MT{m002:Get:pub:stat} (object:o) -> i32
                  §IF{i1} (is o Func<i32, i32> Add)
                    §R 1
                  §F{f003:L} () -> i32
                    §R §C{Add} §A 1 §/C
                  §R §C{L} §/C
            """;
        var compilation = Program.Compile(source, "r2b.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.Contains(compilation.Diagnostics.Errors,
            d => d.Code == "Calor1002" && d.Message.Contains("CS8421"));
    }

    [Fact]
    public void Review2_ExpressionCallSpelling_IgnoresASameNamedField()
    {
        var source = """
            §M{m001:Escape}
              §CL{c001:Probe:pub:stat}
                §FLD{Func<i32, i32>:Help:priv:stat} §E{}
                §MT{m002:Get:pub:stat} () -> Func<i32, i32> §E{}
                  §E{cw}
                  §R §LAM{l001:x:i32} §C Help §A x §/C §/LAM{l001}
                  §F{f001:Help} (i32:x) -> i32
                    §P "hidden"
                    §R x
            """;
        var compilation = Program.Compile(source, "r2c.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.Contains(compilation.Diagnostics.Errors, d => d.Code == "Calor0424");
    }

    [Fact]
    public void Review2_AConditionalReturningALocalFunction_CarriesItsRow()
    {
        var source = """
            §M{m001:Escape}
              §CL{c001:Probe:pub:stat}
                §MT{m002:Get:pub:stat} (bool:c) -> Func<i32, i32> §E{}
                  §E{cw}
                  §R (? c Help Help)
                  §F{f001:Help} (i32:x) -> i32
                    §P "hidden"
                    §R x
            """;
        var compilation = Program.Compile(source, "r2d.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.Contains(compilation.Diagnostics.Errors, d => d.Code == "Calor0424");
    }

    [Fact]
    public void NativeLocal_RoundTripsThroughTheCalorEmitter()
    {
        var parsed = Program.Compile(ShadowingModule, "rt.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(parsed.HasErrors);
        var emitted = new CalorEmitter().Emit(parsed.Ast!);
        Assert.Contains("§F{f001:Add} (i32:x) -> i32", emitted);
        var again = Program.Compile(emitted, "rt2.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(again.HasErrors, string.Join("\n", again.Diagnostics.Errors) + "\n" + emitted);
        Assert.Equal("3", Run(again.GeneratedCode));
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
