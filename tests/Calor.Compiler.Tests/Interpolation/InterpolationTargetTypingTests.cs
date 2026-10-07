using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Commands;
using Calor.Compiler.Mcp.Tools;
using Calor.Compiler.Migration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using Xunit.Abstractions;

namespace Calor.Compiler.Tests.Interpolation;

/// <summary>
/// 0.25 F2 (#906): interpolation target typing. Each registered C# row is converted on the
/// CLI surface (the options <c>calor convert</c> builds) and the MCP <c>calor_convert</c>
/// surface (no options), compiled by Calor, and the generated C# is run against the original.
/// </summary>
public class InterpolationTargetTypingTests(ITestOutputHelper output)
{
    public enum Surface { Cli, Mcp }

    public enum Outcome { Native, Preserved }

    private const string Usings = "using System;\nusing System.Globalization;\nusing System.Runtime.CompilerServices;\nusing System.Text;\n";

    // F2-INTERP-01 (control): string targets with format, alignment, escaped braces, null holes
    // and left-to-right hole evaluation.
    private const string Row01 = """
        public static class Probe
        {
            private static int calls = 0;
            public static int Next() { calls = calls + 1; return calls; }
            public static string Run()
            {
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                double amount = 3.14159;
                int n = 42;
                string missing = null;
                string formatted = $"{amount:F2}";
                string aligned = $"[{n,5}][{n,-4}]";
                string braces = $"{{{n}}}";
                string nulls = $"<{missing}>";
                string order = $"{Next()}-{Next()}";
                return formatted + "|" + aligned + "|" + braces + "|" + nulls + "|" + order;
            }
        }
        """;

    // F2-INTERP-02: FormattableString / IFormattable targets; Kind(FormattableString) vs Kind(object).
    private const string Row02 = """
        public static class Probe
        {
            public static string Kind(FormattableString f) { return "fs:" + f.Format + ":" + f.ArgumentCount; }
            public static string Kind(object o) { return "object:" + o; }
            public static string Run()
            {
                int x = 5;
                FormattableString f = $"x={x}";
                IFormattable g = $"y={x:D3}";
                string viaInvariant = FormattableString.Invariant($"{1.5}");
                return f.Format + "|" + f.ArgumentCount + "|" + g.ToString(null, CultureInfo.InvariantCulture)
                    + "|" + viaInvariant + "|" + Kind($"k={x}");
            }
        }
        """;

    // F2-INTERP-03: a custom [InterpolatedStringHandler] parameter. The handler logs each append.
    private const string Row03 = """
        [InterpolatedStringHandler]
        public ref struct RecordingHandler
        {
            private StringBuilder builder;
            public RecordingHandler(int literalLength, int formattedCount)
            {
                builder = new StringBuilder();
                builder.Append("H(" + literalLength + "," + formattedCount + ")");
            }
            public void AppendLiteral(string s) { builder.Append("L[" + s + "]"); }
            public void AppendFormatted<T>(T value) { builder.Append("F[" + value + "]"); }
            public string Result() { return builder.ToString(); }
        }

        public static class Probe
        {
            public static string Log(RecordingHandler handler) { return handler.Result(); }
            public static string Run()
            {
                int x = 7;
                return Log($"a{x}b");
            }
        }
        """;

    // F2-INTERP-04 (control): ordinary literals containing ${...} and {...} stay literal.
    private const string Row04 = """
        public static class Probe
        {
            public static string Run()
            {
                int x = 9;
                string dollar = "${x}";
                string braces = "{x}";
                string real = $"{x}";
                return dollar + "|" + braces + "|" + real;
            }
        }
        """;

    // F2-INTERP-07 (control): de-DE current culture through a string target versus an
    // invariant FormattableString.
    private const string Row07 = """
        public static class Probe
        {
            public static string Run()
            {
                double d = 1234.5;
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                string current = $"{d:N1}|{d}";
                FormattableString f = $"{d:N1}";
                string invariant = f.ToString(CultureInfo.InvariantCulture);
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                return current + "|" + invariant;
            }
        }
        """;

    // F2-INTERP-08: overload sets where the target type observably selects the method,
    // including shapes where a converter temporary would lose the target type.
    private const string Row08 = """
        public static class Probe
        {
            private static string trace = "";
            public static string Tag(string s) { trace = trace + s; return s; }
            public static int Next() { trace = trace + "n"; return 1; }
            public static string Pick(string s) { return "string:" + s; }
            public static string Pick(FormattableString f) { return "fs:" + f.Format; }
            public static string Kind(FormattableString f) { return "fs:" + f.Format + ":" + f.ArgumentCount; }
            public static string Kind(object o) { return "object:" + o; }
            public static string Pair(int a, FormattableString f) { return "pair:" + a + ":" + f.Format; }
            public static FormattableString Make(int v) { return $"m={v:D2}"; }
            public static string Run()
            {
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                int x = 3;
                bool flag = x > 2;
                string a = Pick($"p={x}");
                string b = Kind($"k={x}");
                string c = Kind(flag ? $"c={x}" : $"d={x}");
                string d = Kind(flag ? (FormattableString)$"e={x}" : $"f={x}");
                string e = Pair(Next(), $"q={Tag("t")}");
                bool skip = x > 5;
                string g = skip ? $"{Tag("z")}" : "skip";
                Func<FormattableString> lazy = () => $"l={x}";
                string h = Kind(Make(x)) + ":" + Kind(lazy());
                return a + "|" + b + "|" + c + "|" + d + "|" + e + "|" + g + "|" + h + "|" + trace;
            }
        }
        """;

    // F2-INTERP-10: string return targets with format, alignment and culture.
    private const string Row10 = """
        public static class Probe
        {
            public static string Money(decimal amount, int width) { return $"[{amount,10:N2}]{width}"; }
            public static string Plain(double d) => $"{d:F3}|{d,8}";
            public static string Run()
            {
                CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
                string fr = Money(1234.5m, 3) + "|" + Plain(2.5);
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                return fr + "|" + Money(1234.5m, 3) + "|" + Plain(2.5);
            }
        }
        """;

    public static TheoryData<string, Surface, Outcome> Rows()
    {
        var data = new TheoryData<string, Surface, Outcome>();
        foreach (var surface in new[] { Surface.Cli, Surface.Mcp })
        {
            data.Add("F2-INTERP-01", surface, Outcome.Native);
            data.Add("F2-INTERP-02", surface, Outcome.Native);
            data.Add("F2-INTERP-03", surface, Outcome.Preserved);
            data.Add("F2-INTERP-04", surface, Outcome.Native);
            data.Add("F2-INTERP-07", surface, Outcome.Native);
            data.Add("F2-INTERP-08", surface, Outcome.Native);
            data.Add("F2-INTERP-10", surface, Outcome.Native);
        }
        return data;
    }

    private static string Source(string row) => Usings + row switch
    {
        "F2-INTERP-01" => Row01,
        "F2-INTERP-02" => Row02,
        "F2-INTERP-03" => Row03,
        "F2-INTERP-04" => Row04,
        "F2-INTERP-07" => Row07,
        "F2-INTERP-08" => Row08,
        "F2-INTERP-10" => Row10,
        _ => throw new ArgumentOutOfRangeException(nameof(row))
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task RegisteredRow_ConvertsCompilesAndMatchesTheOriginal(string row, Surface surface, Outcome outcome)
    {
        var source = Source(row);
        var expected = Execute(source);
        var (calor, preserved) = await Convert(source, surface);
        output.WriteLine(calor);
        Assert.Equal(outcome == Outcome.Preserved, preserved);

        var compilation = Program.Compile(calor, $"{row}.calr",
            new CompilationOptions { EnforceEffects = false, StatusWriter = TextWriter.Null });
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics.Errors));
        output.WriteLine(compilation.GeneratedCode);
        Assert.Equal(expected, Execute(compilation.GeneratedCode));

        // Recorded, not asserted: whether the converted output also passes the default compile
        // (effect enforcement on). Converted code declares no effect rows, so Calor0410 is expected
        // wherever the original performs effects (rows.json, defaultCompile).
        var strict = Program.Compile(calor, $"{row}.calr", new CompilationOptions { StatusWriter = TextWriter.Null });
        output.WriteLine($"DEFAULT-COMPILE {row} {surface}: "
            + string.Join(",", strict.Diagnostics.Errors.Select(d => d.Code).Distinct().Order()));
    }

    [Theory]
    [InlineData(Surface.Cli)]
    [InlineData(Surface.Mcp)]
    public async Task FormattableTargets_ConvertWithAnExplicitCast_StringTargetsDoNot(Surface surface)
    {
        var (calor, _) = await Convert(Source("F2-INTERP-02"), surface);
        output.WriteLine(calor);
        Assert.Contains("§B{FormattableString:f} (cast global::System.FormattableString \"x=${x}\")", calor);
        Assert.Contains("§B{IFormattable:g} (cast global::System.IFormattable \"y=${x:D3}\")", calor);
        Assert.Contains("§C{FormattableString.Invariant} (cast global::System.FormattableString \"${1.5}\")", calor);
        Assert.Contains("(cast global::System.FormattableString \"k=${x}\")", calor);

        (calor, _) = await Convert(Source("F2-INTERP-01"), surface);
        Assert.DoesNotContain("(cast", calor);
    }

    [Theory]
    [InlineData(Surface.Cli)]
    [InlineData(Surface.Mcp)]
    public async Task HandlerTarget_PreservesTheEnclosingCallAtConversionTime(Surface surface)
    {
        var (calor, preserved) = await Convert(Source("F2-INTERP-03"), surface);
        Assert.True(preserved);
        // The call and its handler argument stay one C# expression; no native STRING argument.
        Assert.Contains("Log($\"a{x}b\")", calor);
        Assert.DoesNotContain("\"a${x}b\"", calor);
    }

    [Theory]
    [InlineData(Surface.Cli)]
    [InlineData(Surface.Mcp)]
    public async Task BclHandlerTargets_ArePreservedAndMatch(Surface surface)
    {
        // StringBuilder.Append and string.Create(IFormatProvider, ...) select BCL handler
        // overloads. They are handlers too, so they are kept as C#, not flattened to a string.
        var source = Usings + """
            public static class Probe
            {
                public static string Run()
                {
                    double d = 1234.5;
                    var sb = new StringBuilder();
                    sb.Append($"a={d:N1};");
                    string inv = string.Create(CultureInfo.InvariantCulture, $"{d:N1}");
                    return sb.ToString() + "|" + inv;
                }
            }
            """;
        var expected = Execute(source);
        var (calor, preserved) = await Convert(source, surface);
        Assert.True(preserved, calor);
        Assert.DoesNotContain("\"a=${d:N1};\"", calor);
        var compilation = Program.Compile(calor, "f2-bcl-handlers.calr",
            new CompilationOptions { EnforceEffects = false, StatusWriter = TextWriter.Null });
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics.Errors));
        Assert.Equal(expected, Execute(compilation.GeneratedCode));
    }

    // Review round 1 witnesses: shapes that must keep their C# meaning even when not native.
    public static TheoryData<string, Surface, bool> HoleShapes()
    {
        var shapes = new (string Body, bool Preserved)[]
        {
            // No holes: FormattableString.Invariant("hello") would not compile.
            ("return FormattableString.Invariant($\"hello\") + \"|\" + Kind($\"{{x}}\");", true),
            // A concatenation of interpolated strings converts to the handler as one operand.
            ("int x = 1; return Log($\"a{x}\" + $\"b{x}\");", true),
            // A literal ${ inside a hole argument stays literal.
            ("int x = 1; return $\"{Tag(\"${x}\")}|{Tag(\"q\")}\";", false),
            // Nested FormattableString target and holes outside the native subset.
            ("int x = 2; int[] a = { 5, 6 }; Holder h = null; return $\"{Kind($\"{x}\")}|{a[x - 1]}|{h?.Name}|{Pad(width: x, s: \"z\")}|{Same<int>(x + 1)}|{(x > 1 ? \"big\" : \"small\")}\";", true),
            // Native subset: calls with operator and member-access arguments, prefix minus.
            ("int x = 3; string s = \"abc\"; return $\"{Pad(\"q\", x + 1)}|{Math.Max(-1, s.Length)}|{-x}|{!(x > 2)}|{nameof(s)}|{typeof(Holder).Name}|{Tag(nameof(x))}\";", false),
        };
        shapes =
        [
            .. shapes,
            // Review round 2: a digit-only hole must not read back as {0}-style literal text.
            ("int n = 2; FormattableString f = $\"{1}|{n}|{3,4}\"; string s = $\"{1}|{7:D3}\"; return f.Format + \":\" + f.ArgumentCount + \":\" + s;", false),
            // Review round 2: typed literal arguments (decimal, long) and integral doubles stay C#.
            ("return $\"{Num(1.5m)}|{Num(2.0)}|{Num(3L)}|{4.0}|{x2(2.0)}\";", true),
        ];
        var data = new TheoryData<string, Surface, bool>();
        foreach (var surface in new[] { Surface.Cli, Surface.Mcp })
            foreach (var (body, preserved) in shapes)
                data.Add(body, surface, preserved);
        return data;
    }

    [Theory]
    [MemberData(nameof(HoleShapes))]
    public async Task HoleAndTargetShapes_KeepTheirCSharpMeaning(string body, Surface surface, bool preserved)
    {
        // The handler type is itself kept as C# interop, so it is only declared when used.
        var usesHandler = body.Contains("Log(", StringComparison.Ordinal);
        var handler = usesHandler ? Row03[..Row03.IndexOf("public static class Probe", StringComparison.Ordinal)] : "";
        var logs = usesHandler
            ? "public static string Log(RecordingHandler handler) { return \"handler:\" + handler.Result(); }\n"
              + "public static string Log(object o) { return \"object:\" + o; }\n"
            : "";
        var source = Usings + handler
            + """
            public sealed class Holder { public string Name = "n"; }
            public static class Probe
            {
                LOGS
                public static string Kind(FormattableString f) { return "fs:" + f.Format + ":" + f.ArgumentCount; }
                public static string Kind(object o) { return "object:" + o; }
                public static string Tag(string s) { return "<" + s + ">"; }
                public static string Pad(string s, int width) { return s.PadLeft(width, '.'); }
                public static T Same<T>(T value) { return value; }
                public static string Num(decimal value) { return "decimal"; }
                public static string Num(double value) { return "double"; }
                public static string Num(long value) { return "long"; }
                public static string Num(int value) { return "int"; }
                public static double x2(double value) { return value / 4; }
                public static string Run()
                {
                    BODY
                }
            }
            """.Replace("BODY", body).Replace("LOGS", logs);
        var expected = Execute(source);
        var (calor, wasPreserved) = await Convert(source, surface);
        output.WriteLine(calor);
        Assert.Equal(preserved, wasPreserved);
        var compilation = Program.Compile(calor, "f2-hole-shapes.calr",
            new CompilationOptions { EnforceEffects = false, StatusWriter = TextWriter.Null });
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics.Errors));
        output.WriteLine(compilation.GeneratedCode);
        Assert.Equal(expected, Execute(compilation.GeneratedCode));
    }

    [Fact]
    public void NativeHole_StringArgumentWithACSharpOnlyEscape_StaysRaw()
    {
        // Review round 2: Calor keeps "\x41" as four characters; C# reads "A". Not lifted.
        const string calor = """
            §M{m001:InterpEscape}
              §F{f001:Show:pub} () -> str
                §R "${String.Concat("\x41")}"
            """;
        var result = Program.Compile(calor, "f2-escape-arg.calr",
            new CompilationOptions { EnforceEffects = false, StatusWriter = TextWriter.Null });
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
        Assert.Contains("String.Concat(\"\\x41\")", result.GeneratedCode);
    }

    [Fact]
    public void NativeHole_StringArgumentWithDollarBrace_StaysLiteral()
    {
        const string calor = """
            §M{m001:InterpLiteralArg}
              §F{f001:Show:pub} (i32:x) -> str
                §R "${String.Concat("${x}")}"
            """;
        var result = Program.Compile(calor, "f2-literal-arg.calr",
            new CompilationOptions { EnforceEffects = false, StatusWriter = TextWriter.Null });
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
        Assert.Contains("String.Concat(\"${x}\")", result.GeneratedCode);
    }

    [Fact]
    public void HandlerTarget_WithoutGracefulFallback_IsRefusedAtConversion()
    {
        var result = new CSharpToCalorConverter(new ConversionOptions { GracefulFallback = false })
            .Convert(Source("F2-INTERP-03"));
        Assert.Contains(result.Issues, issue => issue.Feature == "string-interpolation-handler");
    }

    [Fact]
    public void HoleWithStringLiteral_StaysInPlace()
    {
        // Pre-0.25 the converter hoisted `Tag("t")` into a temporary ahead of the statement,
        // running it before Next() and even when an enclosing conditional skipped it.
        var result = new CSharpToCalorConverter().Convert(Source("F2-INTERP-08"));
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Issues));
        Assert.DoesNotContain("_interp", result.CalorSource);
        Assert.Contains("\"q=${Tag(\"t\")}\"", result.CalorSource);
    }

    // F2-INTERP-05: an undefined name inside a hole is reported at the hole.
    [Fact]
    public void UndefinedHoleName_IsReportedAtTheHole()
    {
        const string calor = """
            §M{m001:InterpBinding}
              §F{f001:Show:pub} (i32:n) -> str
                §R "value=${nope}"
            """;
        var result = Program.Compile(calor, "f2-interp-05.calr");
        var error = Assert.Single(result.Diagnostics.Errors, d => d.Code == "Calor0200");
        Assert.Equal((3, 17), (error.Span.Line, error.Span.Column));
    }

    [Fact]
    public void HoleDiagnostic_OnALaterLine_KeepsItsLineAndColumn()
    {
        const string calor = """
            §M{m001:InterpBinding}
              §F{f001:Show:pub} (i32:n) -> str
                §B{str:a} "first ${n}"
                §R "x=${n} y=${(+ n missing)}"
            """;
        var result = Program.Compile(calor, "f2-interp-05b.calr");
        var error = Assert.Single(result.Diagnostics.Errors, d => d.Code == "Calor0200");
        Assert.Equal((4, 25), (error.Span.Line, error.Span.Column));
    }

    // F2-INTERP-06: a call hole is analyzed as a call. A pure function that reads the console
    // through a hole fails with Calor0410 instead of passing with an assumed-effects warning.
    [Fact]
    public void CallHole_InAPureFunction_IsAnUndeclaredEffect()
    {
        const string calor = """
            §M{m001:InterpEffects}
              §F{f001:Greet:pub} () -> str
                §E{}
                §R "hello ${Console.ReadLine()}"
            """;
        var result = Program.Compile(calor, "f2-interp-06.calr");
        Assert.Contains(result.Diagnostics.Errors, d => d.Code == "Calor0410" && d.Message.Contains("'cr'"));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "Calor0419");
    }

    [Fact]
    public void CallHole_WithTheDeclaredEffect_Compiles()
    {
        const string calor = """
            §M{m001:InterpEffects}
              §F{f001:Greet:pub} () -> str
                §E{cr}
                §R "hello ${Console.ReadLine()} ${Math.Max(1, 2)}"
            """;
        var result = Program.Compile(calor, "f2-interp-06b.calr");
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "Calor0419");
        Assert.Contains("$\"hello {Console.ReadLine()} {Math.Max(1, 2)}\"", result.GeneratedCode);
    }

    [Fact]
    public void CallHole_ThatCannotBeLifted_StillReportsAssumedEffects()
    {
        // Negative control: a named argument keeps the hole raw C#; it must not become pure.
        const string calor = """
            §M{m001:InterpEffects}
              §F{f001:Show:pub} (i32:n) -> str
                §E{}
                §R "v=${Math.Max(val1: n, val2: 3)}"
            """;
        var result = Program.Compile(calor, "f2-interp-06c.calr");
        Assert.Contains(result.Diagnostics, d => d.Code == "Calor0419");
    }

    [Fact]
    public void NameofHole_IsNotTreatedAsACall()
    {
        const string calor = """
            §M{m001:InterpNameof}
              §F{f001:Show:pub} (i32:n) -> str
                §R "${nameof(n)}=${n}"
            """;
        var result = Program.Compile(calor, "f2-interp-nameof.calr");
        Assert.DoesNotContain(result.Diagnostics, d => d.Code is "Calor0411" or "Calor0410");
        Assert.Contains("$\"{nameof(n)}={n}\"", result.GeneratedCode);
    }

    [Fact]
    public void LiteralDollarText_StaysLiteral()
    {
        // F2-INTERP-04 negative control at the compiler: an escaped \${x} is text, not a hole.
        const string calor = """
            §M{m001:Literal}
              §F{f001:Show:pub} (i32:x) -> str
                §R "\${x}|{x}"
            """;
        var result = Program.Compile(calor, "f2-interp-04.calr");
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
        Assert.Contains("\"${x}|{x}\"", result.GeneratedCode);
    }

    internal static async Task<(string Calor, bool Preserved)> Convert(string source, Surface surface)
    {
        if (surface == Surface.Cli)
        {
            var options = ConvertCommand.BuildCSharpToCalorOptions(
                benchmark: false, verbose: false, explain: false, noFallback: false,
                passthrough: false, explicitCallClosers: false);
            var result = new CSharpToCalorConverter(options).Convert(source);
            Assert.True(result.Success, string.Join(Environment.NewLine, result.Issues));
            return (result.CalorSource!, result.Losses.Any(loss => loss.Kind == ConversionLossKind.InteropPreserved));
        }

        var args = JsonDocument.Parse(JsonSerializer.Serialize(new { source })).RootElement;
        var tool = await new ConvertTool().ExecuteAsync(args);
        var text = tool.Content[0].Text!;
        Assert.False(tool.IsError, text);
        var root = JsonDocument.Parse(text).RootElement;
        var calor = Find(root, "calorSource")?.GetString();
        Assert.False(string.IsNullOrEmpty(calor), text);
        var interop = Find(root, "interopPreservations");
        return (calor!, interop is { ValueKind: JsonValueKind.Number } count && count.GetInt32() > 0);
    }

    private static JsonElement? Find(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name == name)
                    return property.Value;
                if (Find(property.Value, name) is { } nested)
                    return nested;
            }
        }
        return null;
    }

    internal static string Execute(string source)
    {
        var compilation = CSharpCompilation.Create("F2Interp_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble + source)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emission = compilation.Emit(stream);
        Assert.True(emission.Success, string.Join(Environment.NewLine, emission.Diagnostics) + "\n" + source);
        stream.Position = 0;
        var context = new AssemblyLoadContext(compilation.AssemblyName!, isCollectible: true);
        var saved = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            var assembly = context.LoadFromStream(stream);
            var run = assembly.GetTypes()
                .Where(type => type.Name == "Probe")
                .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                .Single(method => method.Name == "Run");
            return Assert.IsType<string>(run.Invoke(null, null));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = saved;
            context.Unload();
        }
    }
}
