using Calor.Compiler.Analysis;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Calor0255 (#727): a §B that emits a new C# local shadowing an enclosing local
/// or parameter is CS0136 in the generated code. The check mirrors the emitter's
/// mutable-rebind rule — a mutable §B reusing a name already declared in the
/// function is a reassignment (valid), not a shadowing declaration.
/// </summary>
public class LocalShadowingTests
{
    private static bool HasShadow(string source)
    {
        var lex = new DiagnosticBag();
        var tokens = new Lexer(source, lex).TokenizeAllForParser();
        Assert.False(lex.HasErrors, "lexer: " + string.Join("; ", lex.Select(d => d.Message)));
        var parseBag = new DiagnosticBag();
        var module = new Parser(tokens, parseBag).Parse();
        Assert.False(parseBag.HasErrors, "parser: " + string.Join("; ", parseBag.Select(d => d.Message)));

        var bag = new DiagnosticBag();
        new BindValidationPass(bag, source, strictInference: true).Check(module);
        return bag.ToList().Any(d => d.Code == DiagnosticCode.BindShadowsEnclosingScope);
    }

    [Fact]
    public void ImmutableInnerBindingShadowingOuterLocal_IsRejected()
    {
        // The #727 repro: an immutable §B inside a block reusing an outer local's
        // name → the emitter re-declares → CS0136.
        Assert.True(HasShadow(
            "§M{m:S}\n" +
            "  §F{f:Do:pub} (bool:flag) -> i32\n" +
            "    §B{~x:i32} 0\n" +
            "    §IF{i1} (== flag true)\n" +
            "      §B{x:str} \"hi\"\n" +
            "      §P x\n" +
            "    §R x\n"));
    }

    [Fact]
    public void MutableRebindOfOuterLocal_IsAccepted()
    {
        // The emitter turns a mutable §B of an already-declared name into an
        // assignment (`x = …`), not a re-declaration — valid C#, so no Calor0255.
        // (This is the Factorial/Fibonacci accumulator idiom.)
        Assert.False(HasShadow(
            "§M{m:S}\n" +
            "  §F{f:Fact:pub} (i32:n) -> i32\n" +
            "    §B{~result} 1\n" +
            "    §B{~i} 2\n" +
            "    §WH{w1} (<= i n)\n" +
            "      §B{~result} (* result i)\n" +
            "      §B{~i} (+ i 1)\n" +
            "    §R result\n"));
    }

    [Fact]
    public void SiblingBlocksReusingAName_IsAccepted()
    {
        // Two non-nested blocks may each declare `x` — sibling scopes, no CS0136.
        Assert.False(HasShadow(
            "§M{m:S}\n" +
            "  §F{f:Do:pub} (bool:a, bool:b) -> i32\n" +
            "    §IF{i1} (== a true)\n" +
            "      §B{x:i32} 1\n" +
            "      §P x\n" +
            "    §IF{i2} (== b true)\n" +
            "      §B{x:i32} 2\n" +
            "      §P x\n" +
            "    §R 0\n"));
    }

    [Fact]
    public void LocalShadowingAField_IsAccepted()
    {
        // C# allows a local to shadow a field (the local wins); not CS0136.
        Assert.False(HasShadow(
            "§M{m:S}\n" +
            "  §CL{c1:Box:pub}\n" +
            "    §FLD{i32:x:priv}\n" +
            "    §MT{mt1:Do:pub} () -> i32\n" +
            "      §B{x:i32} 5\n" +
            "      §R x\n"));
    }

    [Fact]
    public void InnerBindingReusingALoopVariableName_IsRejected()
    {
        // #730 review finding 1: the loop variable is in scope for the body, so a
        // §B reusing its name is CS0136 (for (int i…) { int i; }).
        Assert.True(HasShadow(
            "§M{m:S}\n" +
            "  §F{f:Do:pub} () -> i32\n" +
            "    §L{l1:i:0:3:1}\n" +
            "      §B{i:i32} 9\n" +
            "      §P i\n" +
            "    §R 0\n"));
    }

    [Fact]
    public void InnerBindingReusingAParameterName_IsRejected()
    {
        // A nested local reusing a parameter name is CS0136.
        Assert.True(HasShadow(
            "§M{m:S}\n" +
            "  §F{f:Do:pub} (i32:x) -> i32\n" +
            "    §IF{i1} (> x 0)\n" +
            "      §B{x:i32} 9\n" +
            "      §P x\n" +
            "    §R x\n"));
    }

    [Fact]
    public void LoopVariableShadowingEnclosingLocal_IsRejected()
    {
        // The reverse of the inner-§B case (#743 review finding 4): here the LOOP
        // variable is the shadower — `int x = 0;` then `for (var x = …)` is CS0136.
        // Previously accepted (the loop var was seeded into a fresh scope with no
        // shadowing check), producing broken C#.
        Assert.True(HasShadow(
            "§M{m:S}\n" +
            "  §F{f:Do:pub} () -> i32\n" +
            "    §B{~x} 0\n" +
            "    §L{l1:x:0:3:1}\n" +
            "      §ASSIGN x (+ x 1)\n" +
            "    §R x\n"));
    }

    [Fact]
    public void ForeachVariableShadowingEnclosingLocal_IsRejected()
    {
        Assert.True(HasShadow(
            "§M{m:S}\n" +
            "  §F{f:Do:pub} (str:path) -> i32\n" +
            "    §E{fs:r}\n" +
            "    §B{~x:str} \"a\"\n" +
            "    §B{arr:[str]} §C{File.ReadAllLines} §A path §/C\n" +
            "    §EACH{e1:x} arr\n" +
            "      §P x\n" +
            "    §R 0\n"));
    }

    [Fact]
    public void NonShadowingLoopVariable_IsAccepted()
    {
        // A loop variable whose name is not otherwise in scope is fine.
        Assert.False(HasShadow(
            "§M{m:S}\n" +
            "  §F{f:Do:pub} () -> i32\n" +
            "    §B{~s} 0\n" +
            "    §L{l1:i:0:3:1}\n" +
            "      §ASSIGN s (+ s i)\n" +
            "    §R s\n"));
    }
}
