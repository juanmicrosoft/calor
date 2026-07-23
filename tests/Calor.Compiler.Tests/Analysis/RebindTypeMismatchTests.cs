using Calor.Compiler.Analysis;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Calor0256 (#733, #740): a mutable §B rebind whose value is of a primitive category
/// (string / bool / numeric) that is not implicitly convertible to the variable's
/// declared type. The emitter emits <c>x = value</c> against the original type, so a
/// cross-category value fails to compile (CS0029). The rebind's type comes from its
/// annotation, or is inferred from the value — a literal, a reference to a typed local,
/// or a call with a known return type (#740). Comparison is by category, so implicit
/// numeric widening (i32→i64) is never a false positive; a same-type rebind and a
/// non-inferable value (the accumulator idiom) are unaffected.
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

    private static IReadOnlyList<Diagnostic> Validate(string source)
    {
        var lex = new DiagnosticBag();
        var tokens = new Lexer(source, lex).TokenizeAllForParser();
        var parseBag = new DiagnosticBag();
        var module = new Parser(tokens, parseBag).Parse();
        var bag = new DiagnosticBag();
        new BindValidationPass(bag, source, strictInference: true).Check(module);
        return bag.ToList();
    }

    [Fact]
    public void RebindWithDifferentType_IsRejected()
    {
        Assert.True(HasMismatch(
            "§M{m:S}\n  §F{f:Do:pub} () -> i32\n    §B{~x:i32} 0\n    §B{~x:str} \"hi\"\n    §R 0\n"));
    }

    [Fact]
    public void UnannotatedRebindWithMismatchedLiteral_IsRejected()
    {
        // #740 common case: no annotation, but the literal's type is statically known.
        Assert.True(HasMismatch(
            "§M{m:S}\n  §F{f:Do:pub} () -> i32\n    §B{~x:i32} 0\n    §B{~x} \"hi\"\n    §R 0\n"));
    }

    [Fact]
    public void Message_UsesSurfaceSpellings_NotInternalForms()
    {
        // The message must teach the syntax the user writes (i32/str), never the
        // expanded internal spelling (INT/STRING) — which isn't valid annotation syntax.
        var diag = Validate(
            "§M{m:S}\n  §F{f:Do:pub} () -> i32\n    §B{~x:i32} 0\n    §B{~x:str} \"hi\"\n    §R 0\n")
            .Single(d => d.Code == DiagnosticCode.BindRebindTypeMismatch);

        Assert.Contains("'i32'", diag.Message);
        Assert.Contains("'str'", diag.Message);
        Assert.DoesNotContain("INT", diag.Message);
        Assert.DoesNotContain("STRING", diag.Message);
    }

    [Fact]
    public void UnannotatedRebindWithMismatchedCallReturn_IsRejected()
    {
        // #740: no annotation, value is a string-returning BCL call — inferred as str,
        // rebinding an i32 → CS0029.
        Assert.True(HasMismatch(
            "§M{m:S}\n  §F{f:Do:pub} (str:p) -> i32\n    §E{fs:r}\n" +
            "    §B{~x:i32} 0\n    §B{~x} §C{File.ReadAllText} §A p §/C\n    §R 0\n"));
    }

    [Fact]
    public void UnannotatedRebindWithMismatchedReference_IsRejected()
    {
        // #740: value is a reference to a str parameter — inferred as str, rebinding i32.
        Assert.True(HasMismatch(
            "§M{m:S}\n  §F{f:Do:pub} (str:s) -> i32\n    §B{~x:i32} 0\n    §B{~x} s\n    §R 0\n"));
    }

    [Fact]
    public void NumericWideningRebind_IsAccepted()
    {
        // Regression guard: `long x = 0; x = 5;` is valid C# (int literal widens to long).
        // Category comparison must NOT flag it — a bug the earlier exact-equality check had.
        Assert.False(HasMismatch(
            "§M{m:S}\n  §F{f:Do:pub} () -> i64\n    §B{~x:i64} 0\n    §B{~x} 5\n    §R x\n"));
    }

    [Fact]
    public void NumericReferenceRebind_IsAccepted()
    {
        // Two numeric types share a category, so a numeric reference into a numeric local
        // is never flagged (a narrowing that needs a cast is a conservative miss, not a FP).
        Assert.False(HasMismatch(
            "§M{m:S}\n  §F{f:Do:pub} (i64:n) -> i32\n    §B{~x:i32} 0\n    §B{~x} n\n    §R 0\n"));
    }

    [Fact]
    public void NonInferableRebind_IsAccepted()
    {
        // A value whose type can't be inferred (here a binary expression) is a
        // conservative miss, never a false positive — the accumulator idiom relies on it.
        Assert.False(HasMismatch(
            "§M{m:S}\n  §F{f:Do:pub} (i32:n) -> i32\n    §B{~result} 1\n" +
            "    §B{~result} (* result n)\n    §R result\n"));
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
