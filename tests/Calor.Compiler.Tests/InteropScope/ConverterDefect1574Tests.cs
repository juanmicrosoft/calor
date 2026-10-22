using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using Surface = Calor.Compiler.Tests.InteropScope.RefOutImplementationTests.Surface;

namespace Calor.Compiler.Tests.InteropScope;

/// <summary>
/// #1574: four converter defects found in F1 PR 2. Every conversion row is converted on the
/// library, CLI and MCP surfaces, compiled, run, and compared with the original C#; a row also
/// states whether its statement must convert natively or be kept as C#.
/// </summary>
public class ConverterDefect1574Tests
{
    // Logging probes: `log` records every call, getter and setter in the order C# runs them.
    private const string Log = "static string log = \"\"; static int G() { log += \"G\"; return log.Length; } static int I() { log += \"I\"; return 0; } ";

    private static IEnumerable<(string Id, string Source, string Allowed)> Rows() =>
    [
        // ---- Item 1: chained assignment runs its right-hand side once. ----
        ("CH-locals", "public static class Probe { " + Log + "public static string Run() { int x = 0, y = 0; x = y = G(); return log + \":\" + x + \":\" + y; } }", "native"),
        ("CH-three-targets", "public static class Probe { " + Log + "public static string Run() { int x, y, z; x = y = z = G(); return log + x + y + z; } }", "native"),
        ("CH-declaration", "public static class Probe { " + Log + "public static string Run() { int y = 0; int z = y = G(); return log + \":\" + z + \":\" + y; } }", "native"),
        ("CH-var-declaration", "public static class Probe { " + Log + "public static string Run() { long y; var z = y = G(); return log + z.GetType().Name + y; } }", "native"),
        ("CH-return", "public static class Probe { " + Log + "static int x; static int M() { return x = G(); } public static string Run() { var r = M(); return log + r + x; } }", "native"),
        ("CH-fields", "public static class Probe { " + Log + "static int f; static long g; public static string Run() { f = (int)(g = G()); return log + f + g; } }", "preserved"),
        ("CH-static-fields", "public static class Other { public static int F; public static int H; } public static class Probe { " + Log + "public static string Run() { Other.F = Other.H = G(); return log + Other.F + Other.H; } }", "native"),
        // A property target: the value is the one passed to the setter; no getter runs.
        ("CH-properties", "public class Probe { string log = \"\"; int p, q; public int P { get { log += \"gP\"; return p; } set { log += \"sP\" + value; p = value; } } public int Q { get { log += \"gQ\"; return q; } set { log += \"sQ\" + value; q = value; } } int G() { log += \"G\"; return 5; } string Go() { P = Q = G(); long r = P = Q = G(); return log + \":\" + r; } public static string Run() => new Probe().Go(); }", "native"),
        // Each target converts the value of the assignment to its right: long, then boxed once.
        ("CH-widening-then-boxing", "public static class Probe { " + Log + "public static string Run() { object o; long l; int i; o = l = i = G(); return log + o.GetType().Name + l + i; } }", "native"),
        ("CH-literal-boxing-identity", "public static class Probe { public static string Run() { object a, b; a = b = 5; object o; long l; o = l = 5; return ReferenceEquals(a, b) + \":\" + o.GetType().Name + l; } }", "native"),
        ("CH-literal-same-type", "public static class Probe { public static string Run() { int a, b; a = b = 7; string? s, t; s = t = null; return a + \":\" + b + (s == null) + (t == null); } }", "native"),
        // A user-defined conversion runs once, for the target it converts into.
        ("CH-user-conversion", "public class W { public static int conv; public int V; public static implicit operator W(int v) { conv++; return new W { V = v }; } } public static class Probe { static int G() => 4; public static string Run() { W a, b; a = b = G(); W w; int i; w = i = G(); return W.conv + \":\" + a.V + b.V + w.V + i + ReferenceEquals(a, b); } }", "native"),
        ("CH-compound-inner", "public static class Probe { " + Log + "static int y = 1; public static string Run() { int x; x = y += G(); return log + x + \":\" + y; } }", "native"),
        ("CH-self-reference", "public static class Probe { public static string Run() { int x = 1, y; y = x = x + 1; return x + \":\" + y; } }", "native"),
        // Hoisted statements are flushed only by a block, so a chain converts natively only when
        // its statement sits directly in a block; an unbraced if/else or loop body and a switch
        // section keep the statement as C# (they would otherwise land before the enclosing statement).
        ("CH-unbraced-if", "public static class Probe { " + Log + "public static string Run() { int x = 0, y = 0; if (log.Length == 0) x = y = G(); else x = y = I(); if (log.Length == 0) x = y = I(); else y = x = G(); return log + x + y; } }", "preserved"),
        ("CH-braced-if", "public static class Probe { " + Log + "public static string Run() { int x = 0, y = 0; if (log.Length == 0) { x = y = G(); } else { x = y = I(); } if (log.Length == 0) { x = y = I(); } else { y = x = G(); } return log + x + y; } }", "native"),
        ("CH-switch-section", "public static class Probe { " + Log + "public static string Run() { int x = 0, y = 0; switch (log.Length) { case 0: x = y = G(); break; default: x = y = I(); break; } return log + x + y; } }", "preserved"),
        ("CH-switch-section-block", "public static class Probe { " + Log + "public static string Run() { int x = 0, y = 0; switch (log.Length) { case 0: { x = y = G(); break; } default: { x = y = I(); break; } } return log + x + y; } }", "native-or-preserved"),
        ("CH-loop-body", "public static class Probe { " + Log + "public static string Run() { int x = 0, y = 0; for (int k = 0; k < 3; k++) x = y = G(); return log + x + y; } }", "preserved"),
        ("CH-braced-loop-body", "public static class Probe { " + Log + "public static string Run() { int x = 0, y = 0; for (int k = 0; k < 3; k++) { x = y = G(); } return log + x + y; } }", "native"),
        ("CH-block-lambda", "public static class Probe { " + Log + "public static string Run() { int x = 0, y = 0; System.Action a = () => { x = y = G(); }; a(); a(); return log + x + y; } }", "native-or-preserved"),
        ("CH-expression-lambda", "public static class Probe { " + Log + "public static string Run() { int x = 0, y = 0; System.Action a = () => x = y = G(); a(); a(); return log + x + y; } }", "preserved"),
        ("CH-expression-bodied-member", "public class Probe { " + Log + "int x, y; void Set() => x = y = G(); public static string Run() { var p = new Probe(); p.Set(); return log + p.x + p.y; } }", "preserved"),
        ("CH-field-initializer", "public static class Probe { " + Log + "static int y; static int x = y = G(); public static string Run() => log + x + y; }", "native-or-preserved"),
        // Targets that evaluate code before the right-hand side, and other value positions.
        ("CH-indexer-and-member-target", "public class B { public int P { get { Probe.log += \"get\"; return 9; } set { Probe.log += \"set\" + value; } } } public static class Probe { public static string log = \"\"; static int G() { log += \"G\"; return log.Length; } static int I() { log += \"I\"; return 0; } public static string Run() { var a = new int[2]; var b = new B(); a[I()] = b.P = G(); return log + a[0]; } }", "preserved"),
        ("CH-receiver-reassigned", "public class B { public int N; } public static class Probe { static B b = new B(); static B second = new B(); static int G() { b = second; return 3; } public static string Run() { var first = b; int x; x = b.N = G(); return first.N + \":\" + second.N + x; } }", "preserved"),
        ("CH-if-condition", "public static class Probe { " + Log + "public static string Run() { int x = 0; if ((x = G()) > 0) { log += x; } return log; } }", "preserved"),
        ("CH-while-condition", "public static class Probe { " + Log + "public static string Run() { int x = 0; while ((x = G()) < 3) { log += x; } return log; } }", "preserved"),
        ("CH-argument-order", "public static class Probe { " + Log + "static void Use(int a, int v) { log += \"U\" + v; } public static string Run() { int x = 0; Use(I(), x = G()); return log + x; } }", "preserved"),
        ("CH-operand", "public static class Probe { " + Log + "public static string Run() { int x = 0; return (x = G()) + \":\" + log; } }", "preserved"),
        ("CH-compound-outer", "public static class Probe { public static string Run() { int x = 2; x += x = 5; return x.ToString(); } }", "preserved"),
        ("CH-two-declarators", "public static class Probe { " + Log + "public static string Run() { int y; int a = I(), z = y = G(); return log + a + z + y; } }", "preserved"),
        // Codex review round 1 reproducers.
        ("R1-literal-conversion-selection", "public class W { public string Kind = \"\"; public static implicit operator W(byte v) => new W { Kind = \"byte\" }; public static implicit operator W(long v) => new W { Kind = \"long\" }; } public static class Probe { static W F() { int x; return x = 1; } public static string Run() => F().Kind; }", "native"),
        ("R1-collection-conversion", "using System.Collections.Generic; public class W { public static int Count; public static implicit operator W(List<int> xs) { Count++; return new W(); } } public static class Probe { public static string Run() { W a, b; a = b = new List<int> { 1 }; return W.Count + \":\" + ReferenceEquals(a, b); } }", "preserved"),
        ("R1-compound-rhs-hoist", "public class C { public int N() => 2; } public static class Probe { static int x = 1; static C F() { x = 10; return new C(); } static int M() { return x += F().N(); } public static string Run() => M() + \":\" + x; }", "native-or-preserved"),
        ("R1-null-coalescing-assignment", "public static class Probe { static int F() { int? n = null; return n ??= 1; } public static string Run() => F().ToString(); }", "preserved"),
        ("R1-preprocessor-region", "public static class Probe { " + Log + "static int x, y; static int Set() {\n#if true\n        x = y = G();\n        return x = I();\n#endif\n    } public static string Run() { var r = Set(); return log + r + x + y; } }", "native-or-preserved"),
        ("R1-anonymous-type-argument", "public static class Probe { public static string Run() { var a = new { X = 1 }; var xs = System.Linq.Enumerable.Repeat(a, 1); var ys = xs = xs; return ReferenceEquals(xs, ys) ? \"same\" : \"different\"; } }", "native-or-preserved"),
        ("R1-compound-chain", "public static class Probe { " + Log + "static int y = 1; public static string Run() { long x; x = y *= G(); return log + x + \":\" + y; } }", "native"),
        // Codex review round 2 reproducers.
        ("R2-self-initializer", "public static class Probe { public static string Run() { int x = x = 1; return x.ToString(); } }", "preserved"),
        ("R2-type-named-like-builtin", "namespace N { public sealed class String { } public static class Probe { public static string Run() { N.String a; N.String b; a = b = new N.String(); return object.ReferenceEquals(a, b) ? \"same\" : \"different\"; } } }", "preserved"),
        ("R2-oblivious-target", "#nullable enable\npublic static class Probe {\n#nullable disable annotations\n    static string value;\n#nullable enable annotations\n    public static string Run() { object? r = value = null; return r == null ? \"null\" : \"set\"; } }", "native"),
        // Codex review round 3 reproducers.
        ("R3-type-named-like-calor-alias", "namespace N { public sealed class Str { } public static class Probe { public static string Run() { N.Str a; N.Str b; a = b = new N.Str(); return object.ReferenceEquals(a, b) ? \"same\" : \"different\"; } } }", "preserved"),
        ("R3-allow-null-target", "#nullable enable\nusing System.Diagnostics.CodeAnalysis;\npublic static class Probe { [AllowNull] public static string Value = \"\"; static string? F() { return Value = null; } public static string Run() => F() == null ? \"null\" : \"set\"; }", "preserved"),
        // Codex verification pass reproducers (approved exception): `using` aliases in the temp's
        // displayed type, and nullability attributes on a property setter's parameter.
        ("V-using-alias", "using I32 = N.Widget; namespace N { public sealed class Widget { public int V; public Widget(int v) { V = v; } public static implicit operator int(Widget w) => w.V; public static implicit operator Widget(int v) => new Widget(v + 1); } public static class Probe { public static string Run() { N.Widget a; N.Widget b; a = b = new N.Widget(0); return a.V + \":\" + b.V; } } }", "preserved"),
        ("V-generic-alias", "using Str = N.Widget; namespace N { public sealed class Widget { public int V; } public static class Probe { public static string Run() { System.Collections.Generic.List<N.Widget> a; System.Collections.Generic.List<N.Widget> b; a = b = new System.Collections.Generic.List<N.Widget>(); return object.ReferenceEquals(a, b) ? \"same\" : \"different\"; } } }", "preserved"),
        ("V-nested-alias", "using In = N.Outer.Inner; namespace N { public static class Outer { public sealed class Inner { } } public static class Probe { public static string Run() { N.Outer.Inner a; N.Outer.Inner b; a = b = new N.Outer.Inner(); return object.ReferenceEquals(a, b) ? \"same\" : \"different\"; } } }", "preserved"),
        ("V-setter-allow-null", "#nullable enable\nusing System.Diagnostics.CodeAnalysis;\npublic static class Probe { static string v = \"\"; public static string Value { get => v; [param: AllowNull] set { v = value ?? \"was-null\"; } } static string? F() { return Value = null; } public static string Run() => (F() ?? \"null\") + \":\" + Value; }", "preserved"),
        ("V-control-no-alias", "namespace N { public sealed class Widget { public int V; } public static class Probe { public static string Run() { N.Widget a; N.Widget b; a = b = new N.Widget(); return object.ReferenceEquals(a, b) ? \"same\" : \"different\"; } } }", "native"),
        // Negative controls: one assignment per statement converts as before.
        ("CH-control-plain", "public static class Probe { " + Log + "public static string Run() { int x, y; y = G(); x = y; return log + x + y; } }", "native"),
        ("CH-control-declaration", "public static class Probe { " + Log + "public static string Run() { int z = G(); return log + z; } }", "native"),

        // ---- Item 2 and #1573: `default` next to a member named @default. ----
        ("DF-uninitialized-local", "public class Probe { public static int reads; public static int @default { get { reads++; return 3; } } public static string Run() { int x; x = 1; string s; s = \"a\"; return reads + \":\" + x + s; } }", "preserved"),
        ("DF-default-literal", "public class Probe { public static int reads; public static int @default { get { reads++; return 3; } } public static string Run() { int x = default; return reads + \":\" + x; } }", "preserved"),
        ("DF-default-of-T", "public struct S { public int A; } public class Probe { public static int reads; public static S @default { get { reads++; return new S { A = 3 }; } } public static string Run() { var x = default(S); return reads + \":\" + x.A; } }", "preserved"),
        ("DF-private-base-member", "public class Base { private int @default = 9; } public class Probe : Base { public static string Run() { bool ok = int.TryParse(\"5\", out var n); int k; k = 2; return n + \":\" + ok + k; } }", "preserved"),
        ("DF-control", "public class Probe { public static string Run() { int x; x = 1; int y = default; return x + \":\" + y; } }", "native"),

        // ---- Item 4: a lowered `out string` and an uninitialized string compile without CS8600. ----
        ("OS-out-string", "public static class Probe { static bool Set(out string value) { value = \"ok\"; return true; } public static string Run() { Set(out string value); string t; t = value; return t; } }", "native"),
    ];

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
    public async Task Conversion_MatchesTheOriginal(string id, Surface surface)
    {
        var (_, source, allowed) = Rows().Single(row => row.Id == id);
        var original = RefOutImplementationTests.Run(source);
        var (calor, losses) = await RefOutImplementationTests.ConvertAsync(source, surface);
        var interop = calor.Contains("§CSHARP{", StringComparison.Ordinal) || calor.Contains("§CS{", StringComparison.Ordinal)
            || calor.Contains("§RAW", StringComparison.Ordinal) || losses.Count > 0;
        var strict = Program.Compile(calor, id + ".calr", new CompilationOptions { StatusWriter = TextWriter.Null });
        // Struct and cross-class members are unknown calls for effects (a pre-existing gap); the
        // comparison is about values, so an effects-only refusal is compiled without enforcement.
        var relaxed = strict.HasErrors && strict.Diagnostics.Errors.All(e => e.Code.StartsWith("Calor04", StringComparison.Ordinal))
            ? Program.Compile(calor, id + ".calr", new CompilationOptions { StatusWriter = TextWriter.Null, EnforceEffects = false })
            : strict;
        Assert.False(relaxed.HasErrors, $"{id} on {surface}: {string.Join("; ", relaxed.Diagnostics.Errors)}\n{calor}");
        Assert.Equal(original, RefOutImplementationTests.Run(relaxed.GeneratedCode)); // never a second evaluation or a reorder
        Assert.True(allowed.Split("-or-").Contains(interop ? "preserved" : "native"), $"{id} on {surface}: expected {allowed}\n{calor}");
    }

    // The right-hand side is bound once, to a temp of the innermost target's type.
    [Fact]
    public void ChainedAssignment_BindsTheRightHandSideOnce()
    {
        var calor = Convert(Rows().Single(r => r.Id == "CH-locals").Source);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(calor, @"§C\{G\}"));
        Assert.Matches(@"§B\{~(_asg\d+):i32\} §C\{G\}\s+§ASSIGN y \1\s+§ASSIGN x \1", calor);
    }

    [Fact]
    public void PreservedChain_IsReportedAsAnEmbeddedAssignmentLoss()
    {
        var result = new CSharpToCalorConverter(new ConversionOptions()).Convert(Rows().Single(r => r.Id == "CH-if-condition").Source);
        Assert.Contains(result.Losses, loss => loss.Feature == "embedded-assignment");
        Assert.NotNull(FeatureSupport.GetFeatureInfo("embedded-assignment"));
        Assert.Equal(SupportLevel.NotSupported, FeatureSupport.GetFeatureInfo("embedded-assignment")!.Support);
    }

    [Theory]
    [InlineData("DF-uninitialized-local", "default-member-collision")] // the statement is kept as C#
    [InlineData("DF-private-base-member", "method")]                    // #1573: the out-var escalation keeps the member
    public void DefaultCollision_IsReportedAndNeverEmitsAtDefault(string id, string feature)
    {
        var result = new CSharpToCalorConverter(new ConversionOptions()).Convert(Rows().Single(r => r.Id == id).Source);
        Assert.Contains(result.Losses, loss => loss.Feature == feature);
        var compiled = Program.Compile(result.CalorSource!, id + ".calr", new CompilationOptions { StatusWriter = TextWriter.Null, EnforceEffects = false });
        Assert.False(compiled.HasErrors, string.Join("; ", compiled.Diagnostics.Errors));
        Assert.DoesNotContain("= @default", compiled.GeneratedCode);
    }

    // Item 4: `default!` for a type that may be a non-nullable reference; built-in value types keep `default`.
    [Fact]
    public void LoweredOutString_CompilesWithoutCS8600()
    {
        var calor = Convert(Rows().Single(r => r.Id == "OS-out-string").Source);
        var compiled = Program.Compile(calor, "os.calr", new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(compiled.HasErrors, string.Join("; ", compiled.Diagnostics.Errors));
        Assert.Contains("string value = default!;", compiled.GeneratedCode);
        Assert.DoesNotContain(CSharpDiagnostics(compiled.GeneratedCode), id => id == "CS8600");
        // Control: the same program with `default` raises CS8600.
        Assert.Contains(CSharpDiagnostics(compiled.GeneratedCode.Replace("default!;", "default;")), id => id == "CS8600");
        var ints = Program.Compile("§M{m1:P}\n  §F{f1:Run:pub} () -> i32\n    §E{}\n    §B{~n:i32} `default`\n    §R n\n",
            "i.calr", new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.Contains("int n = default;", ints.GeneratedCode);
    }

    // Codex round 2: the temp for an oblivious reference target is nullable, like the target's own
    // converted declaration, so the generated C# gains no CS8600.
    [Fact]
    public void ObliviousTarget_TempIsNullable_AndCompilesWithoutCS8600()
    {
        var calor = Convert(Rows().Single(r => r.Id == "R2-oblivious-target").Source);
        Assert.Matches(@"§B\{~_asg\d+:\?str\} null", calor);
        var compiled = Program.Compile(calor, "oblivious.calr", new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(compiled.HasErrors, string.Join("; ", compiled.Diagnostics.Errors));
        Assert.DoesNotContain(CSharpDiagnostics(compiled.GeneratedCode), id => id == "CS8600");
    }

    // ---- Item 3: a bare binding's next-line initializer keeps its meaning and now warns. ----

    [Theory]
    [InlineData("    §B{~n:i32}\n    §C{System.Math.Abs} §A -7 §/C\n", true)]   // the pitfall: same indent
    [InlineData("    §B{n:i32}\n    §C{System.Math.Abs} §A -7 §/C\n", true)]
    [InlineData("    §B{~n:i32} §C{System.Math.Abs} §A -7 §/C\n", false)]   // control: same line
    public void BareBindingNextLineInitializer_WarnsWithoutChangingMeaning(string binding, bool warns)
    {
        var result = Program.Compile("§M{m1:P}\n  §F{f1:Run:pub} () -> i32\n    §E{}\n" + binding + "    §R n\n",
            "next.calr", new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(result.HasErrors, string.Join("; ", result.Diagnostics.Errors));
        Assert.Equal(warns, result.Diagnostics.Any(d => d.Code == DiagnosticCode.BareBindingNextLineInitializer && !d.IsError));
        Assert.Equal("7", RefOutImplementationTests.Run(result.GeneratedCode + "\npublic static class Probe { public static string Run() => P.PModule.Run().ToString(); }"));
    }

    [Fact]
    public void ExplicitDefaultInitializer_DoesNotWarn()
    {
        var result = Program.Compile("§M{m1:P}\n  §F{f1:Run:pub} () -> i32\n    §E{}\n    §B{~n:i32} `default`\n    §ASSIGN n 7\n    §R n\n",
            "default.calr", new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(result.HasErrors, string.Join("; ", result.Diagnostics.Errors));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.BareBindingNextLineInitializer);
    }

    private static string Convert(string source)
        => new CSharpToCalorConverter(new ConversionOptions()).Convert(source).CalorSource!.Replace("\r\n", "\n");

    private static IEnumerable<string> CSharpDiagnostics(string csharp)
        => CSharpCompilation.Create("D1574",
                [CSharpSyntaxTree.ParseText(csharp, new CSharpParseOptions(LanguageVersion.Preview))],
                GeneratedCSharpCompiler.References,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable))
            .GetDiagnostics().Select(d => d.Id);
}
