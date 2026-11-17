using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Calor.Compiler.Analysis.Dataflow;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.SelfCheck;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Makes the agent syntax exemplar's own promise — "every complete module program
/// compiles (the compiler's own tests prove it)" — literally true, and guards the
/// E1a array-vs-list trap (#712). The exemplar is served to agents verbatim as
/// <c>calor://primer</c>, so a type error in a copyable line ships straight into
/// agent-authored code. These tests run in every CI environment (no Z3, no dotnet
/// subprocess): the deep guard is a full Roslyn semantic compile of the generated C#.
/// </summary>
public class ExemplarCompilesTests
{
    private static string TeachingRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, ExemplarCompileChecker.TeachingSourcePath)))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Teaching source not found.");
    }

    [Fact]
    public void TeachingOutputs_AreByteStableCurrentAndPackaged()
    {
        var root = TeachingRoot();
        var source = File.ReadAllText(Path.Combine(root, ExemplarCompileChecker.TeachingSourcePath));
        var outputs = ExemplarCompileChecker.GenerateTeachingResources(source);
        var again = ExemplarCompileChecker.GenerateTeachingResources(source);
        Assert.Equal(16, outputs.Count);
        foreach (var (path, expected) in outputs)
        {
            Assert.Equal(expected, again[path]);
            Assert.Equal(Encoding.UTF8.GetBytes(expected), File.ReadAllBytes(Path.Combine(root, path)));
            if (path.Contains("/Resources/Teaching/Generated/", StringComparison.Ordinal))
                Assert.Equal(expected, Init.EmbeddedResourceHelper.ReadResource(
                    "Calor.Compiler.Resources." + Path.GetFileName(path)));
        }
    }

    [Fact]
    public void TeachingGeneration_RejectsMissingExampleReferences()
    {
        var source = JsonNode.Parse(File.ReadAllText(Path.Combine(TeachingRoot(), ExemplarCompileChecker.TeachingSourcePath)))!;
        source["constructs"]![0]!["programs"] = new JsonArray("missing-example");
        Assert.Throws<InvalidDataException>(() => ExemplarCompileChecker.GenerateTeachingResources(source.ToJsonString()));
    }

    [Fact]
    public void TeachingGeneration_RequiresExamplesForAuthoredConstructs()
    {
        var source = JsonNode.Parse(File.ReadAllText(Path.Combine(TeachingRoot(), ExemplarCompileChecker.TeachingSourcePath)))!;
        source["constructs"]![0]!["programs"] = new JsonArray();
        Assert.Throws<InvalidDataException>(() => ExemplarCompileChecker.GenerateTeachingResources(source.ToJsonString()));
    }

    [Fact]
    public void TeachingGeneration_RejectsEffectCatalogDrift()
    {
        var source = JsonNode.Parse(File.ReadAllText(Path.Combine(TeachingRoot(), ExemplarCompileChecker.TeachingSourcePath)))!;
        source["catalogs"]!["effects.json"]!["effects"]!.AsArray().RemoveAt(0);
        Assert.Throws<InvalidDataException>(() => ExemplarCompileChecker.GenerateTeachingResources(source.ToJsonString()));
    }

    [Fact]
    public void TeachingSource_HasOnlyCompleteCurrentExamplesAndDerivedHover()
    {
        var source = JsonNode.Parse(File.ReadAllText(Path.Combine(TeachingRoot(), ExemplarCompileChecker.TeachingSourcePath)))!;
        foreach (var program in source["programs"]!.AsArray())
        {
            var text = program!["calor"]!.GetValue<string>();
            Assert.StartsWith("module ", text);
            Assert.DoesNotContain("§", text);
            Assert.DoesNotContain("(inc ", text);
            Assert.DoesNotContain("(dec ", text);
        }
        var outputs = ExemplarCompileChecker.GenerateTeachingResources(source.ToJsonString());
        var lookup = JsonNode.Parse(outputs["src/Calor.Compiler/Resources/calor-syntax-documentation.json"])!;
        foreach (var (name, tag) in source["tags"]!.AsObject())
        {
            if (tag!["construct"] is not { } target)
                continue;
            Assert.Null(tag["syntax"]);
            Assert.Null(tag["description"]);
            var construct = source["constructs"]!.AsArray().Single(c => c!["id"]!.GetValue<string>() == target.GetValue<string>())!;
            Assert.Equal(construct["calorSyntax"]!.GetValue<string>(), lookup["tags"]![name]!["syntax"]!.GetValue<string>());
            Assert.Equal(construct["description"]!.GetValue<string>(), lookup["tags"]![name]!["description"]!.GetValue<string>());
        }
        var diagnostics = new List<Diagnostic>();
        var inputs = DocDriftChecker.LoadTeachingFromRepository(TeachingRoot(), diagnostics);
        diagnostics.AddRange(DocDriftChecker.Check(inputs));
        Assert.True(diagnostics.Count == 0, string.Join("\n", diagnostics));
    }

    [Fact]
    public void TeachingExamples_UseMeasuredExpressionsAndTypesWithoutInventingHeaders()
    {
        var source = JsonNode.Parse(File.ReadAllText(Path.Combine(TeachingRoot(), ExemplarCompileChecker.TeachingSourcePath)))!;
        foreach (var program in source["programs"]!.AsArray())
        {
            var text = program!["calor"]!.GetValue<string>();
            Assert.StartsWith("module ", text);
            Assert.DoesNotMatch(@"\((?:\+|-|\*|/|%|==|!=|<=|>=|<|>|&&|\|\||&|\||\^|<<|>>|\?\?|\?|->|!|~)\s", text);
            Assert.DoesNotMatch(@"\b(?:i32|i64|i16|i8|u8|u16|u32|u64|f32|f64|dec|str|Seq)\b", text);
            Assert.DoesNotMatch(@"\b(?:INT|LONG|UINT|ULONG|FLOAT|DOUBLE|DECIMAL|DEC|STR|BOOL):", text);
            Assert.DoesNotContain("§C{", text);
            if (program["id"]!.GetValue<string>() == "complete_linq")
            {
                // 0.30 rewrote this example as explicit loops over native arrays (3b439c47a);
                // it no longer calls System.Linq. It must still compile with every effect
                // resolved, and its result is pinned by the runtime rows below.
                var result = Program.Compile(text);
                Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
                Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code is "Calor0410" or "Calor0411");
                var diagnostics = new DiagnosticBag();
                new Binding.Binder(diagnostics).Bind(Assert.IsType<Ast.ModuleNode>(result.Ast));
                Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
            }
        }
    }

    [Theory]
    [InlineData("complete_records", "GlobalModule.Shift(GlobalModule.Create()).X", "3")]
    [InlineData("copilot_smoke", "GlobalModule.Collect(3)", "3")]
    [InlineData("complete_arrays", "GlobalModule.First()", "10")]
    [InlineData("task_8", "GlobalModule.SafeDivide(84, 2)", "42")]
    [InlineData("task_8", "GlobalModule.SafeDivide(1, 0)", "!ArgumentException")]
    [InlineData("task_14", "GlobalModule.SumList()", "60")]
    [InlineData("task_39", "GlobalModule.AllPositive(new int[] { 1, 2 }, 2)", "True")]
    [InlineData("task_39", "GlobalModule.AllPositive(new int[] { 1, 0 }, 2)", "False")]
    [InlineData("complete_linq", "GlobalModule.Positive(new int[] { -1, 2, 3 }).Length", "2")]
    [InlineData("complete_ranges", "GlobalModule.FirstTwo(new int[] { 1, 2, 3 }).Length", "2")]
    [InlineData("complete_reevaluated_for", "GlobalModule.Count()", "4")]
    [InlineData("complete_enum_extensions", "ModeExtensions.IsActive(Mode.Active)", "True")]
    [InlineData("task_23", "typeof(EventPublisher).GetEvent(\"OnDataReceived\").EventHandlerType.FullName", "System.EventHandler")]
    public void NativeTeachingPrograms_RetainTheirActualClrObservations(string id, string expression, string expected)
    {
        var source = JsonNode.Parse(File.ReadAllText(Path.Combine(TeachingRoot(), ExemplarCompileChecker.TeachingSourcePath)))!;
        var program = source["programs"]!.AsArray().Single(p => p!["id"]!.GetValue<string>() == id)!;
        var compiled = Program.Compile(program["calor"]!.GetValue<string>(), id + ".calr.txt");
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        Assert.Equal(expected, InteropScope.RefOutImplementationTests.Run(compiled.GeneratedCode + $$"""

            public static class Probe
            {
                public static object? Run() => {{expression}};
            }
            """));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("", "a")]
    [InlineData("abc", "")]
    [InlineData("abc", "abc")]
    [InlineData("abc", "abcd")]
    [InlineData("abc", "bc")]
    [InlineData("abc", "cd")]
    [InlineData("aaab", "aab")]
    [InlineData("ababa", "baba")]
    [InlineData("A", "a")]
    [InlineData("a\0b", "\0b")]
    [InlineData("\ud83d\ude00x", "\ude00")]
    public void TeachingStringContains_PreservesOrdinalSubstringBehavior(string text, string part)
    {
        var code = StrictTeachingCode("task_37");
        var expected = text.Contains(part).ToString();
        Assert.Equal(expected, InteropScope.RefOutImplementationTests.Run(code + $$"""

            public static class Probe
            {
                public static object Run() => GlobalModule.StringContains({{CSharpLiteral(text)}}, {{CSharpLiteral(part)}});
            }
            """));
    }

    [Theory]
    [InlineData("string?:s, string:sub")]
    [InlineData("string:s, string?:sub")]
    public void TeachingStringContains_DoesNotBroadenItsNonNullInputContract(string parameters)
    {
        var result = Program.Compile(TeachingProgramSource("task_37") + $$"""

              func NullableCaller ({{parameters}}) → bool
                !
                return StringContains(s, sub)
            """, "nullable-teaching.calr.txt", new CompilationOptions { StrictEffects = true });
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Calor0274" && diagnostic.IsError);
    }

    [Theory]
    [InlineData("")]
    [InlineData(",")]
    [InlineData(",tail")]
    [InlineData("head,tail,rest")]
    [InlineData("no delimiter")]
    [InlineData("a\0b,tail")]
    [InlineData("\ud83d\ude00,tail")]
    public void TeachingFirstField_PreservesOrdinalPrefixBehavior(string line)
    {
        var code = StrictTeachingCode("primer_strings");
        var end = line.IndexOf(",", StringComparison.Ordinal);
        var expected = end < 0 ? line : line.Substring(0, end);
        Assert.Equal(expected, InteropScope.RefOutImplementationTests.Run(code + $$"""

            public static class Probe
            {
                public static object Run() => GlobalModule.FirstField({{CSharpLiteral(line)}});
            }
            """));
    }

    [Theory]
    [InlineData("new int[0]")]
    [InlineData("new int[] { -3, 0, -1 }")]
    [InlineData("new int[] { 3, 1, 3 }")]
    [InlineData("new int[] { -1, 2, 0, 3, 2 }")]
    [InlineData("new int[] { int.MinValue, int.MaxValue, 0, 1 }")]
    public void TeachingArrayFilter_PreservesValuesOrderDuplicatesAndInput(string expression)
    {
        var code = StrictTeachingCode("complete_linq");
        Assert.Equal("True", InteropScope.RefOutImplementationTests.Run(code + $$"""

            public static class Probe
            {
                public static object Run()
                {
                    var input = {{expression}};
                    var before = (int[])input.Clone();
                    var expected = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Where(input, value => value > 0));
                    var actual = GlobalModule.Positive(input);
                    return System.Linq.Enumerable.SequenceEqual(expected, actual)
                        && System.Linq.Enumerable.SequenceEqual(before, input);
                }
            }
            """));
    }

    private static string TeachingProgramSource(string id)
    {
        var source = JsonNode.Parse(File.ReadAllText(Path.Combine(TeachingRoot(), ExemplarCompileChecker.TeachingSourcePath)))!;
        return source["programs"]!.AsArray().Single(program => program!["id"]!.GetValue<string>() == id)!["calor"]!.GetValue<string>();
    }

    private static string StrictTeachingCode(string id)
    {
        var result = Program.Compile(TeachingProgramSource(id), id + ".calr.txt",
            new CompilationOptions { StrictEffects = true });
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code is "Calor0411" or "Calor0419" or "Calor0425");
        return result.GeneratedCode;
    }

    private static string CSharpLiteral(string text) =>
        "\"" + string.Concat(text.Select(character => $"\\u{(int)character:x4}")) + "\"";

    [Fact]
    public void TeachingMirrorCheck_RejectsStaleAndMissingOutput()
    {
        var outputs = ExemplarCompileChecker.GenerateTeachingResources(File.ReadAllText(
            Path.Combine(TeachingRoot(), ExemplarCompileChecker.TeachingSourcePath)));
        foreach (var (path, expected) in outputs)
        {
            foreach (var actual in new[] { expected + "stale", null })
            {
                var inputs = new DocDriftInputs
                {
                    Version = "", LexerKeywords = [], DiagnosticCodes = [],
                    KnownEffectCodes = [], DocumentedEffectCodes = [],
                    MirrorDocs = [new(path, ExemplarCompileChecker.TeachingSourcePath, actual, expected)]
                };
                Assert.Contains(DocDriftChecker.Check(inputs),
                    diagnostic => diagnostic.Code is DiagnosticCode.DocDriftMirrorOutOfSync or DiagnosticCode.DocDriftMissingInput);
            }
        }
    }

    private const string ResourceName = "Calor.Compiler.Resources.agent-syntax-exemplar.md";

    private static string ExemplarContent()
    {
        var assembly = typeof(ExemplarCompileChecker).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static DocFile ExemplarDoc() =>
        new(ExemplarCompileChecker.RelativePath, ExemplarContent());

    [Fact]
    public void EveryCompleteProgram_CompilesToValidCSharp()
    {
        var findings = ExemplarCompileChecker.Check(ExemplarDoc());

        // Any finding here means a complete program in the exemplar no longer
        // compiles (Calor1330) or a copyable line hides the array trap (Calor1331).
        Assert.True(
            findings.Count == 0,
            "Exemplar drift:\n" + string.Join("\n", findings.Select(f => f.ToString())));
    }

    [Fact]
    public void ExtractsTheExpectedNumberOfCompletePrograms()
    {
        // Guards against a fence-tag regression silently dropping programs from the
        // deep check (a ```calor block quietly reverting to bare ``` would make this
        // fall to 8 and the compile guard would stop covering that program).
        var programs = ExemplarCompileChecker.ExtractCompletePrograms(ExemplarContent());
        Assert.Equal(9, programs.Count);
        Assert.All(programs, p => Assert.StartsWith("module ", p.Source.TrimStart()));
    }

    [Fact]
    public void ReadAllLinesBoundToList_IsCaughtByTheCompilePipeline()
    {
        // The E1a bug. Since #722 the binder rejects it at the language level
        // (Calor0254) so `calor -i` fails instead of emitting broken C#; the
        // Roslyn layer remains as backstop for any array source the binder can't
        // yet see. Asserting the disjunction keeps the test robust if either
        // layer's coverage shifts, but today the Calor layer catches this case.
        const string calor =
            "module Files\n" +
            "  func CountLines (string:path) → int\n" +
            "    ! fs:r\n" +
            "    let List<string>:lines = File.ReadAllLines(path)\n" +
            "    return lines.Count\n";

        // #722: the binder now catches the binding-position case directly.
        var result = Program.Compile(calor, "mutation.calr");
        Assert.True(result.Diagnostics.HasErrors, "Calor should reject the array-to-List trap.");
        Assert.Contains(result.Diagnostics,
            d => d.Code == DiagnosticCode.BindArrayToConcreteCollection);
    }

    [Fact]
    public void ReturnPositionArrayToList_IsCaughtAtTheCalorLayer()
    {
        // Return position is now caught at the language level (#724): a
        // List<str>-returning function that returns File.ReadAllLines (an array)
        // fails calor -i with Calor0254 rather than emitting broken C#.
        const string calor =
            "module Files\n" +
            "  func Get (string:path) → List<string>\n" +
            "    ! fs:r\n" +
            "    return File.ReadAllLines(path)\n";

        var result = Program.Compile(calor, "return-mutation.calr");
        Assert.Contains(result.Diagnostics,
            d => d.Code == DiagnosticCode.BindArrayToConcreteCollection);
    }

    [Fact]
    public void ReadAllLinesBoundToList_FailsTheArrayTrapLint()
    {
        // The same bug on a copyable fragment line (cannot be compiled standalone).
        var doc = new DocFile(
            ExemplarCompileChecker.RelativePath,
            "## Calls\n\n```\nlet List<string>:lines = File.ReadAllLines(path)\n```\n");

        var findings = new List<Diagnostic>();
        ExemplarCompileChecker.CheckArrayBindingTraps(doc, findings);

        Assert.Contains(findings, f => f.Code == DiagnosticCode.DocDriftArrayBindingTrap);
    }

    [Fact]
    public void ArrayFormBinding_IsAccepted()
    {
        // The correct idiom must NOT be flagged.
        var doc = new DocFile(
            ExemplarCompileChecker.RelativePath,
            "```\nlet string[]:lines = File.ReadAllLines(path)\n```\n");

        var findings = new List<Diagnostic>();
        ExemplarCompileChecker.CheckArrayBindingTraps(doc, findings);

        Assert.Empty(findings);
    }

    [Theory]
    [InlineData("List<string>", true)]
    [InlineData("IEnumerable<string>", true)]
    [InlineData("string[]", false)]
    public void PostfixArrayReturningBinding_PreservesFragmentTrapGuard(string type, bool rejected)
    {
        var doc = new DocFile(ExemplarCompileChecker.RelativePath,
            $"```\nlet {type}:lines = File.ReadAllLines(path)\n```\n");
        var findings = new List<Diagnostic>();
        ExemplarCompileChecker.CheckArrayBindingTraps(doc, findings);
        Assert.Equal(rejected, findings.Any(f => f.Code == DiagnosticCode.DocDriftArrayBindingTrap));
    }

    [Fact]
    public void ArrayTrapLint_HonorsDriftIgnoreMarker()
    {
        // A deliberate negative example ("do NOT write this") is a normal way to
        // teach the trap; the repo-wide drift:ignore escape must exempt it.
        var doc = new DocFile(
            ExemplarCompileChecker.RelativePath,
            "## Common mistakes\n\n<!-- drift:ignore -->\n" +
            "let List<string>:lines = File.ReadAllLines(path)\n");

        var findings = new List<Diagnostic>();
        ExemplarCompileChecker.CheckArrayBindingTraps(doc, findings);

        Assert.Empty(findings);
    }
}
