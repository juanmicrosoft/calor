using Calor.Compiler.Analysis;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Calor0257 (#738): a mutable §B rebind of a §EACH/§EACHKV iteration variable is
/// rejected. A foreach iteration variable is read-only in C#, so the emitter has no
/// valid emission — reassigning it is CS1656 and re-declaring it shadows it (CS0136).
/// This is distinct from a §L for-loop variable, which IS reassignable and stays legal.
/// </summary>
public class IterationVariableRebindTests
{
    private static bool RebindsIterationVar(string source)
    {
        var lex = new DiagnosticBag();
        var tokens = new Lexer(source, lex).TokenizeAllForParser();
        Assert.False(lex.HasErrors, "lexer: " + string.Join("; ", lex.Select(d => d.Message)));
        var parseBag = new DiagnosticBag();
        var module = new Parser(tokens, parseBag).Parse();
        Assert.False(parseBag.HasErrors, "parser: " + string.Join("; ", parseBag.Select(d => d.Message)));

        var bag = new DiagnosticBag();
        new BindValidationPass(bag, source, strictInference: true).Check(module);
        return bag.ToList().Any(d => d.Code == DiagnosticCode.BindReassignsIterationVariable);
    }

    [Fact]
    public void RebindOfForeachVariable_IsRejected()
    {
        Assert.True(RebindsIterationVar(
            "§M{m:S}\n  §F{f:Do:pub} (str:path) -> i32\n    §E{fs:r}\n" +
            "    §B{arr:[str]} §C{File.ReadAllLines} §A path §/C\n" +
            "    §EACH{e1:x} arr\n      §B{~x:str} \"y\"\n    §R 0\n"));
    }

    [Fact]
    public void RebindOfEachKvKey_IsRejected()
    {
        Assert.True(RebindsIterationVar(
            "§M{m:S}\n  §F{f:Do:pub} () -> i32\n" +
            "    §B{d:Dictionary<str:i32>} §NEW{Dictionary<str:i32>} §/NEW\n" +
            "    §EACHKV{e2:k:v} d\n      §B{~k:str} \"z\"\n    §R 0\n"));
    }

    [Fact]
    public void RebindOfEachKvValue_IsRejected()
    {
        Assert.True(RebindsIterationVar(
            "§M{m:S}\n  §F{f:Do:pub} () -> i32\n" +
            "    §B{d:Dictionary<str:i32>} §NEW{Dictionary<str:i32>} §/NEW\n" +
            "    §EACHKV{e2:k:v} d\n      §B{~v:i32} 9\n    §R 0\n"));
    }

    [Fact]
    public void RebindOfForLoopVariable_IsAccepted()
    {
        // A §L for-loop variable is a plain local that IS reassignable in C#, so
        // rebinding it is a legal reassignment — Calor0257 must NOT fire.
        Assert.False(RebindsIterationVar(
            "§M{m:S}\n  §F{f:Do:pub} () -> i32\n    §B{~s} 0\n" +
            "    §L{l1:i:0:3:1}\n      §B{~i:i32} 9\n      §ASSIGN s (+ s i)\n    §R s\n"));
    }

    [Fact]
    public void BindingAfterForeachEnds_IsAccepted()
    {
        // Once the §EACH block closes, the iteration variable is out of scope, so a
        // fresh §B of the same name is a new declaration, not a rebind of the iter var.
        Assert.False(RebindsIterationVar(
            "§M{m:S}\n  §F{f:Do:pub} (str:path) -> i32\n    §E{fs:r}\n" +
            "    §B{arr:[str]} §C{File.ReadAllLines} §A path §/C\n" +
            "    §EACH{e1:x} arr\n      §P x\n    §B{~x:str} \"y\"\n    §R 0\n"));
    }

    [Fact]
    public void ReadingForeachVariable_IsAccepted()
    {
        // Merely using (not rebinding) the iteration variable is always fine.
        Assert.False(RebindsIterationVar(
            "§M{m:S}\n  §F{f:Do:pub} (str:path) -> i32\n    §E{fs:r,cw}\n" +
            "    §B{arr:[str]} §C{File.ReadAllLines} §A path §/C\n" +
            "    §EACH{e1:x} arr\n      §P x\n    §R 0\n"));
    }
}
