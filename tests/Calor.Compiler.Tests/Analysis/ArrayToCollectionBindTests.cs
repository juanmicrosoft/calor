using Calor.Compiler.Analysis;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Calor.Compiler.SelfCheck;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// The language-level E1a fix (#722): the binder rejects an array-typed
/// initializer bound to a concrete generic collection (<c>List&lt;T&gt;</c>, …),
/// which C# would reject with CS0029 — instead of emitting broken C# and exiting 0.
/// Mirrors C#'s rule: arrays satisfy the collection interfaces but not the classes.
/// </summary>
public class ArrayToCollectionBindTests
{
    private static IReadOnlyList<Diagnostic> Validate(string source, bool strictInference = true)
    {
        var lex = new DiagnosticBag();
        var tokens = new Lexer(source, lex).TokenizeAllForParser();
        Assert.False(lex.HasErrors, "lexer errors: " + string.Join("; ", lex.Select(d => d.Message)));

        var parseBag = new DiagnosticBag();
        var module = new Parser(tokens, parseBag).Parse();
        Assert.False(parseBag.HasErrors, "parser errors: " + string.Join("; ", parseBag.Select(d => d.Message)));

        var bag = new DiagnosticBag();
        new BindValidationPass(bag, source, strictInference).Check(module);
        return bag.ToList();
    }

    private const string ReadAllLinesToList =
        "§M{m:Files}\n" +
        "  §F{f:CountLines:pub} (str:path) -> i32\n" +
        "    §E{fs:r}\n" +
        "    §B{lines:List<str>} §C{File.ReadAllLines} §A path §/C\n" +
        "    §R (len lines)\n";

    private static bool HasArrayTrap(IReadOnlyList<Diagnostic> diags) =>
        diags.Any(d => d.Code == DiagnosticCode.BindArrayToConcreteCollection);

    [Fact]
    public void ReadAllLines_BoundToList_IsRejected()
    {
        Assert.True(HasArrayTrap(Validate(ReadAllLinesToList)));
    }

    [Fact]
    public void IsAlwaysOn_EvenWithStrictInferenceDisabled()
    {
        // Calor0254 is a hard type error, NOT a strict-inference diagnostic — it
        // must fire regardless of --strict-bind-inference (the CLI default is
        // non-strict). Pins the "always-on" design claim so a future refactor
        // cannot silently fold it into the strict-gated block.
        Assert.True(HasArrayTrap(Validate(ReadAllLinesToList, strictInference: false)));
    }

    [Fact]
    public void ReadAllLines_BoundToArrayForm_IsAccepted()
    {
        var diags = Validate(
            "§M{m:Files}\n" +
            "  §F{f:CountLines:pub} (str:path) -> i32\n" +
            "    §E{fs:r}\n" +
            "    §B{lines:[str]} §C{File.ReadAllLines} §A path §/C\n" +
            "    §R (len lines)\n");

        Assert.False(HasArrayTrap(diags));
    }

    [Fact]
    public void ArrayReturningUserFunction_BoundToList_IsRejected()
    {
        // Generality beyond the BCL list: a user function declared -> [str].
        var diags = Validate(
            "§M{m:Test}\n" +
            "  §F{f1:GetItems:pub} (str:path) -> [str]\n" +
            "    §E{fs:r}\n" +
            "    §R §C{File.ReadAllLines} §A path §/C\n" +
            "  §F{f2:Use:pub} (str:path) -> i32\n" +
            "    §E{fs:r}\n" +
            "    §B{items:List<str>} §C{GetItems} §A path §/C\n" +
            "    §R (len items)\n");

        Assert.True(HasArrayTrap(diags));
    }

    [Fact]
    public void ArrayBoundToCollectionInterface_IsAccepted()
    {
        // C# DOES allow array -> IEnumerable<T>/IList<T>/… — only the concrete
        // classes are rejected. The check must not over-fire on interfaces.
        var diags = Validate(
            "§M{m:Files}\n" +
            "  §F{f:Read:pub} (str:path) -> i32\n" +
            "    §E{fs:r}\n" +
            "    §B{lines:IEnumerable<str>} §C{File.ReadAllLines} §A path §/C\n" +
            "    §R INT:0\n");

        Assert.False(HasArrayTrap(diags));
    }

    [Fact]
    public void ListFromNonArrayInitializer_IsAccepted()
    {
        // A List<str> bound to something that is not an array must not fire.
        var diags = Validate(
            "§M{m:Test}\n" +
            "  §F{f1:Make:pub} () -> List<str>\n" +
            "    §R §C{MakeList} §/C\n" +
            "  §F{f2:Use:pub} () -> i32\n" +
            "    §B{items:List<str>} §C{Make} §/C\n" +
            "    §R INT:0\n");

        Assert.False(HasArrayTrap(diags));
    }

    [Fact]
    public void ReturnPosition_ArrayToList_IsRejected()
    {
        // §R returning an array from a List<str>-typed function (#724). This is the
        // signature variant the exemplar's own trap text warns about.
        var diags = Validate(
            "§M{m:Files}\n" +
            "  §F{f:Get:pub} (str:path) -> List<str>\n" +
            "    §E{fs:r}\n" +
            "    §R §C{File.ReadAllLines} §A path §/C\n");

        Assert.True(HasArrayTrap(diags));
    }

    [Fact]
    public void ReturnPosition_ArrayFormReturnType_IsAccepted()
    {
        var diags = Validate(
            "§M{m:Files}\n" +
            "  §F{f:Get:pub} (str:path) -> [str]\n" +
            "    §E{fs:r}\n" +
            "    §R §C{File.ReadAllLines} §A path §/C\n");

        Assert.False(HasArrayTrap(diags));
    }

    [Fact]
    public void AssignPosition_ArrayToList_IsRejected()
    {
        // §ASSIGN reassigning an array into a List<str>-typed mutable variable (#724).
        var diags = Validate(
            "§M{m:Files}\n" +
            "  §F{f:Count:pub} (str:path) -> i32\n" +
            "    §E{fs:r}\n" +
            "    §B{~items:List<str>}\n" +
            "    §ASSIGN items §C{File.ReadAllLines} §A path §/C\n" +
            "    §R (len items)\n");

        Assert.True(HasArrayTrap(diags));
    }

    [Fact]
    public void AssignPosition_ArrayFormVariable_IsAccepted()
    {
        var diags = Validate(
            "§M{m:Files}\n" +
            "  §F{f:Count:pub} (str:path) -> i32\n" +
            "    §E{fs:r}\n" +
            "    §B{~items:[str]}\n" +
            "    §ASSIGN items §C{File.ReadAllLines} §A path §/C\n" +
            "    §R (len items)\n");

        Assert.False(HasArrayTrap(diags));
    }

    [Fact]
    public void HashSetFromArray_IsRejected()
    {
        // Generality on the collection side: not just List<T>.
        var diags = Validate(
            "§M{m:Files}\n" +
            "  §F{f:Read:pub} (str:dir) -> i32\n" +
            "    §E{fs:r}\n" +
            "    §B{names:HashSet<str>} §C{Directory.GetFiles} §A dir §/C\n" +
            "    §R INT:0\n");

        Assert.True(HasArrayTrap(diags));
    }

    [Fact]
    public void UserFunctionShadowingBclName_IsJudgedByItsRealReturnType()
    {
        // A user function named like a BCL method must win over the built-in
        // heuristic: here File.ReadAllLines is a user method returning List<str>
        // (not an array), so binding it to List<str> is correct — no false Calor0254.
        var diags = Validate(
            "§M{m:Shadow}\n" +
            "  §CL{c1:File:pub}\n" +
            "    §MT{mt1:ReadAllLines:pub} (str:path) -> List<str>\n" +
            "      §R §C{MakeList} §/C\n" +
            "  §F{f1:Use:pub} (str:path) -> i32\n" +
            "    §B{items:List<str>} §C{File.ReadAllLines} §A path §/C\n" +
            "    §R INT:0\n");

        Assert.False(HasArrayTrap(diags));
    }

    [Fact]
    public void DocsAndCompilerGuards_ShareOneBclApiList()
    {
        // The Calor0254 compiler check and the Calor1331 docs lint must recognize
        // the same array-returning APIs. They now read the same shared table; this
        // pins that so a future edit to one path can't silently diverge.
        foreach (var api in Calor.Compiler.Analysis.ArrayReturningBcl.Methods.Keys)
        {
            var doc = new DocFile(
                Calor.Compiler.SelfCheck.ExemplarCompileChecker.RelativePath,
                $"```\n§B{{x:List<str>}} §C{{{api}}} §A a §/C\n```\n");
            var findings = new List<Diagnostic>();
            Calor.Compiler.SelfCheck.ExemplarCompileChecker.CheckArrayBindingTraps(doc, findings);
            Assert.Contains(findings, f => f.Code == DiagnosticCode.DocDriftArrayBindingTrap);
        }
    }
}
