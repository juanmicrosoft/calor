using System.Reflection;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.SelfCheck;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Makes the agent syntax exemplar's own promise — "every complete §M program
/// compiles (the compiler's own tests prove it)" — literally true, and guards the
/// E1a array-vs-list trap (#712). The exemplar is served to agents verbatim as
/// <c>calor://primer</c>, so a type error in a copyable line ships straight into
/// agent-authored code. These tests run in every CI environment (no Z3, no dotnet
/// subprocess): the deep guard is a full Roslyn semantic compile of the generated C#.
/// </summary>
public class ExemplarCompilesTests
{
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
        Assert.All(programs, p => Assert.StartsWith("§M", p.Source.TrimStart()));
    }

    [Fact]
    public void ReadAllLinesBoundToList_IsCaughtByTheCompilePipeline()
    {
        // The E1a bug caught by the two-stage guard. Today Calor emits it happily
        // and only the C# compiler rejects it (CS0029: string[] -> List<string>);
        // when the binder learns to reject it (issue #722) `calorCaught` flips to
        // true. Either layer catching it keeps the exemplar safe, so this asserts
        // the disjunction rather than pinning the current (missing) binder behavior.
        const string calor =
            "§M{m:Files}\n" +
            "  §F{f:CountLines:pub} (str:path) -> i32\n" +
            "    §E{fs:r}\n" +
            "    §B{lines:List<str>} §C{File.ReadAllLines} §A path §/C\n" +
            "    §R (len lines)\n";

        var result = Program.Compile(calor, "mutation.calr");
        var calorCaught = result.Diagnostics.HasErrors;
        var roslynCaught = !calorCaught &&
            ExemplarCompileChecker.RoslynErrors(result.GeneratedCode)
                .Any(e => e.StartsWith("CS0029", StringComparison.Ordinal));

        Assert.True(
            calorCaught || roslynCaught,
            "The array-to-List trap must be caught at the Calor or the C# layer.");
    }

    [Fact]
    public void ReadAllLinesBoundToList_FailsTheArrayTrapLint()
    {
        // The same bug on a copyable fragment line (cannot be compiled standalone).
        var doc = new DocFile(
            ExemplarCompileChecker.RelativePath,
            "## Calls\n\n```\n§B{lines:List<str>} §C{File.ReadAllLines} §A path §/C\n```\n");

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
            "```\n§B{lines:[str]} §C{File.ReadAllLines} §A path §/C\n```\n");

        var findings = new List<Diagnostic>();
        ExemplarCompileChecker.CheckArrayBindingTraps(doc, findings);

        Assert.Empty(findings);
    }

    [Fact]
    public void ArrayTrapLint_HonorsDriftIgnoreMarker()
    {
        // A deliberate negative example ("do NOT write this") is a normal way to
        // teach the trap; the repo-wide drift:ignore escape must exempt it.
        var doc = new DocFile(
            ExemplarCompileChecker.RelativePath,
            "## Common mistakes\n\n<!-- drift:ignore -->\n" +
            "§B{lines:List<str>} §C{File.ReadAllLines} §A path §/C\n");

        var findings = new List<Diagnostic>();
        ExemplarCompileChecker.CheckArrayBindingTraps(doc, findings);

        Assert.Empty(findings);
    }
}
