using Calor.Compiler.Analysis;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Calor0256 (#733): a mutable §B rebind that re-annotates a variable with a
/// different type contradicts its declaration. The emitter emits <c>x = value</c>
/// against the original type, so a mismatched value fails to compile (CS0029/CS0266).
/// A same-type rebind, or an unannotated rebind (the accumulator idiom), is unaffected.
/// </summary>
public class RebindTypeMismatchTests
{
    private static bool HasMismatch(string source)
    {
        var lex = new DiagnosticBag();
        var tokens = new Lexer(source, lex).TokenizeAllForParser();
        Assert.False(lex.HasErrors, "lexer: " + string.Join("; ", lex.Select(d => d.Message)));
        var parseBag = new DiagnosticBag();
        var module = new Parser(tokens, parseBag).Parse();
        Assert.False(parseBag.HasErrors, "parser: " + string.Join("; ", parseBag.Select(d => d.Message)));

        var bag = new DiagnosticBag();
        new BindValidationPass(bag, source, strictInference: true).Check(module);
        return bag.ToList().Any(d => d.Code == DiagnosticCode.BindRebindTypeMismatch);
    }

    [Fact]
    public void RebindWithDifferentType_IsRejected()
    {
        Assert.True(HasMismatch(
            "§M{m:S}\n  §F{f:Do:pub} () -> i32\n    §B{~x:i32} 0\n    §B{~x:str} \"hi\"\n    §R 0\n"));
    }

    [Fact]
    public void RebindWithSameType_IsAccepted()
    {
        Assert.False(HasMismatch(
            "§M{m:S}\n  §F{f:Do:pub} () -> i32\n    §B{~x:i32} 0\n    §B{~x:i32} 5\n    §R x\n"));
    }

    [Fact]
    public void UnannotatedRebind_IsAccepted()
    {
        // The accumulator idiom: the rebind carries no type annotation, so there is
        // nothing to contradict — unaffected.
        Assert.False(HasMismatch(
            "§M{m:S}\n  §F{f:Fact:pub} (i32:n) -> i32\n    §B{~result} 1\n" +
            "    §WH{w1} (<= 1 n)\n      §B{~result} (* result 2)\n    §R result\n"));
    }

    [Fact]
    public void RebindOfUntypedOriginal_IsAccepted()
    {
        // The first declaration is unannotated (inferred), so there is no known
        // original type to compare against — conservatively not flagged.
        Assert.False(HasMismatch(
            "§M{m:S}\n  §F{f:Do:pub} () -> i32\n    §B{~x} 0\n    §B{~x:i32} 5\n    §R x\n"));
    }

    [Fact]
    public void SiblingSameNameDifferentTypes_IsNotAMismatch()
    {
        // Independent variables in sibling blocks (not a reassignment) may differ in
        // type — the second is a new declaration, not a rebind of the first.
        Assert.False(HasMismatch(
            "§M{m:S}\n  §F{f:Do:pub} (bool:a, bool:b) -> i32\n" +
            "    §IF{i1} (== a true)\n      §B{~x:i32} 1\n      §R x\n" +
            "    §IF{i2} (== b true)\n      §B{~x:str} \"y\"\n      §R 0\n    §R 0\n"));
    }
}
