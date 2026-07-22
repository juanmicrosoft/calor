using Calor.Compiler.Analysis;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
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
    private static IReadOnlyList<Diagnostic> Validate(string source)
    {
        var lex = new DiagnosticBag();
        var tokens = new Lexer(source, lex).TokenizeAllForParser();
        Assert.False(lex.HasErrors, "lexer errors: " + string.Join("; ", lex.Select(d => d.Message)));

        var parseBag = new DiagnosticBag();
        var module = new Parser(tokens, parseBag).Parse();
        Assert.False(parseBag.HasErrors, "parser errors: " + string.Join("; ", parseBag.Select(d => d.Message)));

        var bag = new DiagnosticBag();
        new BindValidationPass(bag, source, strictInference: true).Check(module);
        return bag.ToList();
    }

    private static bool HasArrayTrap(IReadOnlyList<Diagnostic> diags) =>
        diags.Any(d => d.Code == DiagnosticCode.BindArrayToConcreteCollection);

    [Fact]
    public void ReadAllLines_BoundToList_IsRejected()
    {
        var diags = Validate(
            "§M{m:Files}\n" +
            "  §F{f:CountLines:pub} (str:path) -> i32\n" +
            "    §E{fs:r}\n" +
            "    §B{lines:List<str>} §C{File.ReadAllLines} §A path §/C\n" +
            "    §R (len lines)\n");

        Assert.True(HasArrayTrap(diags));
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
}
