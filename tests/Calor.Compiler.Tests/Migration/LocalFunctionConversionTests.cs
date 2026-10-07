using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.RegularExpressions;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Commands;
using Calor.Compiler.Effects;
using Calor.Compiler.Mcp.Tools;
using Calor.Compiler.Migration;
using Calor.Compiler.Migration.Project;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// 0.25 F3 (#847): non-capturing local functions convert IN PLACE to a nested
/// <c>§F</c>, emitted as a C# <c>static</c> local function, so a call keeps naming
/// the local function (never a hoisted module function or a same-named member).
/// Every registered row is converted on every surface, compiled with DEFAULT options
/// (effects enforced, no <c>--permissive-effects</c>), run, and compared with the
/// original C#. Shapes outside the slice stay preserved as <c>§CSHARP</c>.
/// </summary>
public class LocalFunctionConversionTests
{
    private const string R0Fixtures = "docs/plans/evidence/v0.25-r0-1426/fixtures";

    public enum Surface
    {
        LibraryDefault,
        CliDefault,
        CliPassthrough,
        McpDefault,
        McpPassthroughOnError,
        McpPassthroughOnErrorModuleName,
        ProjectMigrate,
        ProjectMigratePassthrough
    }

    // Beyond the registered rows: a C-1 shadow on an INSTANCE method whose outer
    // body still reads an instance field, mutual recursion, an effectful void local
    // declared after the return (call-before-declaration), and a local parameter
    // that shadows the outer parameter.
    private const string MoreShapes = """
        using System;
        using System.Text;

        public class Shapes
        {
            private int _count = 10;
            public int Foo() => 999;
            public static int Twice(int x) => x * 2;

            public int ShadowOnInstance()
            {
                int Foo() => 1;
                return Foo() + _count;
            }

            public bool Parity(int n)
            {
                bool Even(int k) => k == 0 || Odd(k - 1);
                bool Odd(int k) => k != 0 && Even(k - 1);
                return Even(n);
            }

            public static string Effects()
            {
                var sb = new StringBuilder();
                Append(sb, "x");
                Append(sb, "y");
                return sb.ToString() + Twice(3);

                static void Append(StringBuilder target, string s)
                {
                    target.Append(s);
                    Console.Write("");
                }
            }

            public static int Shadowing(int x)
            {
                int Square(int x) => x * x;
                return Square(x + 1);
            }
        }

        public static class Probe
        {
            public static string Run()
            {
                var s = new Shapes();
                return s.ShadowOnInstance() + "|" + s.Parity(7) + "|" + Shapes.Effects() + "|" + Shapes.Shadowing(3);
            }
        }
        """;

    // Enclosing callables of other kinds: a void expression-bodied local with a
    // compound assignment (once silently lowered to `=`), a local in an async method,
    // a static local after the yields of an iterator, and a local using the
    // enclosing method's type parameter.
    private const string EdgeShapes = """
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;

        public static class Probe
        {
            public static string Log = "";

            public static void Note()
            {
                void Write(string s) => Log += s;
                Write("a");
                Write("b");
            }

            public static async Task<int> Later()
            {
                int Twice(int x) => x * 2;
                await Task.Yield();
                return Twice(21);
            }

            public static IEnumerable<int> Seq(int n)
            {
                for (int i = 0; i < n; i++)
                    yield return Sq(i);

                static int Sq(int x) => x * x;
            }

            public static T Pick<T>(T a, T b)
            {
                T First(T x, T y) => x;
                return First(a, b);
            }

            public static string Run()
            {
                Note();
                int sum = 0;
                foreach (var v in Seq(4)) sum += v;
                return Log + "|" + Later().Result + "|" + sum + "|" + Pick("p", "q");
            }
        }
        """;

    // F3-LOCAL-05 (not fixtured at R0): an async local function stays preserved.
    private const string AsyncLocal = """
        using System.Threading.Tasks;

        public static class Probe
        {
            public static string Run()
            {
                int value = Compute().Result;
                return "" + value;
            }

            private static async Task<int> Compute()
            {
                async Task<int> Inner()
                {
                    await Task.Yield();
                    return 41;
                }
                return await Inner() + 1;
            }
        }
        """;

    // One native-shaped and one capturing local function in the same member: the
    // whole member is preserved, never half-converted.
    private const string MixedMember = """
        public static class Probe
        {
            public static int Mixed(int k)
            {
                int Plain(int x) => x + 1;
                int AddK(int x) => x + k;
                return Plain(1) + AddK(1);
            }

            public static string Run() => "" + Mixed(5);
        }
        """;

    public static TheoryData<string, string, Surface> NativeRows()
    {
        var data = new TheoryData<string, string, Surface>();
        foreach (var surface in Enum.GetValues<Surface>())
        {
            data.Add("F3-LOCAL-01", "3|120", surface);
            data.Add("F3-LOCAL-02", "3|1002", surface);
            data.Add("F3-LOCAL-03", "6|5|60|3000|502", surface);
            data.Add("more-shapes", "11|False|xy6|16", surface);
            data.Add("edge-shapes", "ab|42|14|p", surface);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(NativeRows))]
    public async Task NativeRows_ConvertInPlace_CompileByDefault_AndRunTheSame(
        string row, string expected, Surface surface)
    {
        var source = row switch
        {
            "more-shapes" => MoreShapes,
            "edge-shapes" => EdgeShapes,
            _ => R0Fixture(row)
        };
        Assert.Equal(expected, Run(source));

        var (calor, losses) = await ConvertAsync(source, surface);

        // Native, in place: nested §F, no interop, no rescue, nothing hoisted.
        Assert.DoesNotContain("§CSHARP", calor);
        Assert.DoesNotContain("§CS{", calor);
        Assert.Empty(losses);
        Assert.Matches(@"\n {6,}§F\{f\d+:[A-Za-z]+\} \(", calor);

        // Default compile: effects enforced, no --permissive-effects.
        var compilation = Program.Compile(calor, row + ".calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(compilation.HasErrors,
            string.Join(Environment.NewLine, compilation.Diagnostics.Errors) + "\n" + calor);
        Assert.Contains("static int ", compilation.GeneratedCode);
        Assert.Equal(expected, Run(compilation.GeneratedCode));
    }

    public static TheoryData<string, string, int, Surface> PreservedRows()
    {
        var data = new TheoryData<string, string, int, Surface>();
        foreach (var surface in Enum.GetValues<Surface>())
        {
            data.Add("F3-LOCAL-04", "5|g3|2", 3, surface);
            data.Add("F3-LOCAL-05", "42", 1, surface);
            data.Add("mixed", "8", 1, surface);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(PreservedRows))]
    public async Task OutsideTheSlice_IsPreservedAsInterop_AndRunsTheSame(
        string row, string expected, int preservedMembers, Surface surface)
    {
        var source = row switch
        {
            "F3-LOCAL-05" => AsyncLocal,
            "mixed" => MixedMember,
            _ => R0Fixture(row)
        };
        Assert.Equal(expected, Run(source));

        var (calor, losses) = await ConvertAsync(source, surface);

        // Honest: each containing member is a counted interop loss, never a native §F.
        Assert.Equal(preservedMembers, Regex.Matches(calor, @"§CSHARP\{").Count);
        Assert.Equal(preservedMembers, losses.Count(feature => feature == "method"));
        Assert.DoesNotContain("post-validation-fallback", losses);
        Assert.DoesNotMatch(@"\n {6,}§F\{", calor);

        // The callers of preserved members charge assumed effects (R0 baseline),
        // so behaviour is compared under --permissive-effects, as at R0.
        var compilation = Program.Compile(calor, row + ".calr", new CompilationOptions
        {
            StatusWriter = TextWriter.Null,
            UnknownCallPolicy = UnknownCallPolicy.Permissive
        });
        Assert.False(compilation.HasErrors,
            string.Join(Environment.NewLine, compilation.Diagnostics.Errors) + "\n" + calor);
        Assert.Equal(expected, Run(compilation.GeneratedCode));
    }

    [Theory]
    [InlineData("F3-LOCAL-04")]
    [InlineData("F3-LOCAL-05")]
    public void NoFallback_RefusesOutsideTheSlice_ByName(string row)
    {
        var result = new CSharpToCalorConverter(NoFallbackOptions())
            .Convert(row == "F3-LOCAL-05" ? AsyncLocal : R0Fixture(row));
        Assert.False(result.Success);
        Assert.Contains(result.Issues, issue => issue.Feature == "local-function"
            && issue.Severity == ConversionIssueSeverity.Error);
    }

    [Theory]
    [InlineData("F3-LOCAL-01")]
    [InlineData("F3-LOCAL-02")]
    [InlineData("F3-LOCAL-03")]
    public void NoFallback_AcceptsTheSlice(string row)
    {
        var result = new CSharpToCalorConverter(NoFallbackOptions()).Convert(R0Fixture(row));
        Assert.True(result.Success, string.Join("\n", result.Issues));
        Assert.DoesNotContain(result.Issues, issue => issue.Feature == "local-function");
    }

    [Theory]
    // Implicit and explicit `this`, an outer local, an outer parameter through a lambda.
    [InlineData("private int _f = 1; public int M() { int L() => _f; return L(); }")]
    [InlineData("private int _f = 1; public int M() { int L() => this._f; return L(); }")]
    [InlineData("public int Inst() => 1; public int M() { int L() => Inst(); return L(); }")]
    [InlineData("public int M() { int k = 2; int L() => k; return L(); }")]
    [InlineData("public int M(int p) { int L() { System.Func<int> f = () => p; return f(); } return L(); }")]
    // Placement and shape outside the slice.
    [InlineData("public int M(bool b) { if (b) { int L() => 1; return L(); } return 0; }")]
    [InlineData("public int M() { int L() { int N() => 1; return N(); } return L(); }")]
    [InlineData("public int M() { int L(int x = 1) => x; return L(); }")]
    [InlineData("public int M() { int v = 1; int L(ref int x) => x; return L(ref v); }")]
    [InlineData("public int M() { static T L<T>(T x) => x; return L(1); }")]
    [InlineData("public C() { int L() => 1; L(); }")]
    public void CapturingOrUnsupportedShapes_PreserveTheMember(string members)
    {
        var result = new CSharpToCalorConverter().Convert("public class C\n{\n" + members + "\n}\n");
        Assert.True(result.Success, string.Join("\n", result.Issues));
        Assert.Contains(result.Issues, issue => issue.Feature == "local-function");
        Assert.Contains("§CSHARP", result.CalorSource);
        Assert.DoesNotMatch(@"\n {6,}§F\{", result.CalorSource!);
    }

    [Theory]
    // Names that look like captures but are not: a member access on a parameter,
    // an object-initializer member, a named argument, a static member, a static local.
    [InlineData("public int M() { int L(System.Text.StringBuilder sb) => sb.Length; return L(new System.Text.StringBuilder(\"ab\")); }", true)]
    // Compiled elsewhere: a nested type's constructor is an unknown call to the effect pass.
    [InlineData("public class P { public int X; } public int M() { int L() => new P { X = 2 }.X; return L(); }", false)]
    [InlineData("public static int S(int value) => value; public int M() { int L() => S(value: 3); return L(); }", true)]
    [InlineData("private static int _s = 4; public int M() { int L() => _s; return L(); }", true)]
    [InlineData("private int _f = 1; public int M() { static int L(int x) => x; return L(_f); }", true)]
    public void NonCapturingShapes_ConvertNatively(string members, bool compile)
    {
        var source = "public class C\n{\n" + members + "\n}\n";
        var result = new CSharpToCalorConverter().Convert(source);
        Assert.True(result.Success, string.Join("\n", result.Issues));
        Assert.DoesNotContain(result.Issues, issue => issue.Feature == "local-function");
        Assert.DoesNotContain("§CSHARP", result.CalorSource);
        Assert.Matches(@"\n {6,}§F\{f\d+:L\} \(", result.CalorSource!);
        if (!compile)
            return;
        var compilation = Program.Compile(result.CalorSource!, "shape.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(compilation.HasErrors,
            string.Join("\n", compilation.Diagnostics.Errors) + "\n" + result.CalorSource);
    }

    [Fact]
    public void ExpressionBodiedCompoundAssignment_KeepsItsOperator()
    {
        // Found while testing F3: `=> Log += s` on a method was converted to `Log = s`.
        var source = """
            public static class Probe
            {
                public static string Log = "";
                public static void Write(string s) => Log += s;
                public static string Run()
                {
                    Write("a");
                    Write("b");
                    return Log;
                }
            }
            """;
        Assert.Equal("ab", Run(source));
        var result = new CSharpToCalorConverter().Convert(source);
        Assert.True(result.Success, string.Join("\n", result.Issues));
        var compilation = Program.Compile(result.CalorSource!, "compound.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Errors));
        Assert.Equal("ab", Run(compilation.GeneratedCode));
    }

    [Fact]
    public void Registry_AdvertisesThePartialSlice()
    {
        var info = FeatureSupport.GetFeatureInfo("local-function")!;
        Assert.Equal(SupportLevel.Partial, info.Support);
        Assert.Contains("non-capturing", info.Description);
        Assert.Contains("§CSHARP", info.Description);
    }

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
        var result = new CSharpToCalorConverter().Convert("""
            public static class Probe
            {
                public static string Add(string x) => x;
                public static int M()
                {
                    int Add(int x) => x + 1;
                    int Other(System.Func<int, int> Add) => Add(10);
                    return Other(Add);
                }
            }
            """);
        Assert.True(result.Success, string.Join("\n", result.Issues));
        var compilation = Program.Compile(result.CalorSource!, "r4.calr",
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
    public void Review1_UnresolvedNames_AreNotProofOfNoCapture()
    {
        // `_f` is declared in another part of the partial class, invisible here.
        var result = new CSharpToCalorConverter().Convert(
            "public partial class C { public int M() { int L() => _f; return L(); } }");
        Assert.Contains(result.Issues, issue => issue.Feature == "local-function");
        Assert.DoesNotMatch(@"\n {6,}§F\{", result.CalorSource ?? "");
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

    // ---- harness ----

    private static ConversionOptions NoFallbackOptions() => ConvertCommand.BuildCSharpToCalorOptions(
        benchmark: false, verbose: false, explain: false, noFallback: true,
        passthrough: false, explicitCallClosers: false);

    private static string R0Fixture(string id) => File.ReadAllText(Path.Combine(
        CliTestHarness.FindRepoRoot(), R0Fixtures, id + ".cs.txt"));

    /// <summary>Converts on one surface; returns the Calor and the loss features.</summary>
    private static async Task<(string Calor, List<string> Losses)> ConvertAsync(string source, Surface surface)
    {
        switch (surface)
        {
            case Surface.LibraryDefault:
                return Library(new ConversionOptions());
            case Surface.CliDefault or Surface.CliPassthrough:
                return Library(ConvertCommand.BuildCSharpToCalorOptions(
                    benchmark: false, verbose: false, explain: false, noFallback: false,
                    passthrough: surface == Surface.CliPassthrough, explicitCallClosers: false));
            case Surface.McpDefault or Surface.McpPassthroughOnError or Surface.McpPassthroughOnErrorModuleName:
            {
                var args = new Dictionary<string, object> { ["source"] = source };
                if (surface != Surface.McpDefault)
                    args["passthroughOnError"] = true;
                if (surface == Surface.McpPassthroughOnErrorModuleName)
                    args["moduleName"] = "Custom";
                var result = await new ConvertTool().ExecuteAsync(
                    JsonDocument.Parse(JsonSerializer.Serialize(args)).RootElement);
                Assert.False(result.IsError, result.Content[0].Text);
                using var payload = JsonDocument.Parse(result.Content[0].Text!);
                var root = payload.RootElement;
                var losses = root.GetProperty("lossSummary").GetProperty("locations").EnumerateArray()
                    .Select(location => location.GetProperty("feature").GetString()!).ToList();
                return (root.GetProperty("calorSource").GetString()!, losses);
            }
            default:
            {
                var dir = Path.Combine(Path.GetTempPath(), "calor-f3-847-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                try
                {
                    File.WriteAllText(Path.Combine(dir, "Fixture.cs"), source);
                    var migrator = new ProjectMigrator(new MigrationPlanOptions
                    {
                        Parallel = false,
                        SkipAnalyze = true,
                        SkipVerify = true,
                        PassthroughOnError = surface == Surface.ProjectMigratePassthrough
                    });
                    var plan = await migrator.CreatePlanAsync(dir, MigrationDirection.CSharpToCalor);
                    var report = await migrator.ExecuteAsync(plan);
                    var file = Assert.Single(report.FileResults);
                    Assert.NotEqual(FileMigrationStatus.Failed, file.Status);
                    return (File.ReadAllText(file.OutputPath!), file.Losses.Select(loss => loss.Feature).ToList());
                }
                finally
                {
                    try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
                }
            }
        }

        (string, List<string>) Library(ConversionOptions options)
        {
            var result = new CSharpToCalorConverter(options).Convert(source);
            Assert.True(result.Success, string.Join(Environment.NewLine, result.Issues));
            return (result.CalorSource!, result.Losses.Select(loss => loss.Feature).ToList());
        }
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
