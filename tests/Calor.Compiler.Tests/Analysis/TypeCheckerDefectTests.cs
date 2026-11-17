using Calor.Compiler;
using Calor.Compiler.Diagnostics;
using Xunit;

namespace Calor.Compiler.Tests.Analysis;

/// <summary>
/// The defects that blocked <c>EnableTypeChecking</c> from being default-on (#761 / PP-A1 item 9).
///
/// <para>These were not theoretical. The MCP tools — <c>calor_check</c>, <c>calor_refine</c> —
/// already set <c>EnableTypeChecking = true</c>, so every one of these rejected working programs
/// for agents, today. The shipped MCP primer, two shipped benchmarks and the syntax exemplar were
/// all among the casualties.</para>
///
/// <para>Each test names the program shape that used to fail. The measurement that found them:
/// flipping the default produced 92 failures across the suite.</para>
/// </summary>
public class TypeCheckerDefectTests
{
    private static CompilationResult Check(string source)
        => Program.Compile(source, "t.calr", new CompilationOptions
        {
            EnableTypeChecking = true,
            DeferGeneratedOutputValidation = true,
        });

    private static void AssertNoErrors(string source)
    {
        var result = Check(source);
        Assert.False(result.HasErrors,
            string.Join("\n", result.Diagnostics.Errors.Select(d => $"{d.Code}: {d.Message}")));
    }

    /// <summary>
    /// The native C# spelling of a documented Calor type name. The theories keep the documented
    /// names because the checker echoes them in its diagnostics; the source uses the native type.
    /// </summary>
    private static string Native(string type) => type switch
    {
        "i8" => "sbyte",
        "i16" => "short",
        "i32" => "int",
        "i64" => "long",
        "u8" => "byte",
        "u16" => "ushort",
        "u32" => "uint",
        "u64" => "ulong",
        "f32" => "float",
        "f64" or "FLOAT" => "double",
        "str" => "string",
        ['[', .. var element, ']'] => Native(element) + "[]",
        [.. var element, '[', ']'] => Native(element) + "[]",
        _ => type,
    };

    // ---- Types the checker did not know ----

    [Theory]
    [InlineData("char")]      // 37 of the 92 — the single biggest cause
    [InlineData("object")]    // 13
    [InlineData("decimal")]
    [InlineData("i8")]
    [InlineData("i16")]
    [InlineData("i64")]
    [InlineData("u8")]
    [InlineData("u16")]
    [InlineData("u32")]
    [InlineData("u64")]
    [InlineData("f32")]
    public void DocumentedPrimitiveTypes_Resolve(string type)
    {
        // Every one of these is in docs/syntax-reference/types.md. `Unknown type 'u32'` for a
        // documented type is the checker contradicting the language reference.
        AssertNoErrors($"module S S block\n  func Do ({Native(type)}:x) → void\n    !\n    return\n");
    }

    /// <summary>
    /// The negative half, and the half that actually discriminates. "No errors" alone cannot tell
    /// a RESOLVED type from an unresolved one, because an unresolved name falls back to a
    /// permissive external type that is assignable from anything — so reverting the whole
    /// <c>FromName</c> expansion left the theory above entirely green. Each type must also be
    /// strong enough to REJECT a bad assignment and echo its own name doing it.
    /// </summary>
    [Theory]
    [InlineData("char")]
    [InlineData("decimal")]
    [InlineData("i8")]
    [InlineData("i16")]
    [InlineData("i64")]
    [InlineData("u8")]
    [InlineData("u16")]
    [InlineData("u32")]
    [InlineData("u64")]
    [InlineData("f32")]
    public void DocumentedPrimitiveTypes_AreCheckedNotJustAccepted(string type)
    {
        var result = Check($"module S S block\n  func Do () → void\n    !\n    let {Native(type)}:x = true\n    return\n");

        var error = Assert.Single(result.Diagnostics.Errors);
        Assert.Contains(type, error.Message);
    }

    [Theory]
    [InlineData("float", "f32")]
    [InlineData("FLOAT", "f64")]
    [InlineData("double", "f64")]
    public void FloatingTypeSpellings_PreserveDiagnosticWidth(string spelling, string surface)
    {
        Assert.Equal(surface, TypeChecking.PrimitiveType.FromName(spelling)!.SurfaceName);
        var result = Check($"module S S block\n  func Do () → void\n    !\n    let {Native(spelling)}:x = true\n");
        Assert.Contains(surface, Assert.Single(result.Diagnostics.Errors).Message);
    }

    /// <summary>
    /// `object` is excluded from the theory above — a top type cannot reject a `str`, so the
    /// negative test has to run the other way. It was 13 of the original 92 and would otherwise
    /// be the one type with no discriminating pin.
    /// </summary>
    [Fact]
    public void ObjectIsATopType_NotAUniversalEscape()
    {
        // Everything assigns TO object...
        AssertNoErrors("module S S block\n  func Do () → void\n    !\n    let object:o = \"a\"\n    return\n");

        // ...and nothing assigns FROM it without a cast, which is what C# requires.
        var result = Check("module S S block\n  func Do (object:o) → void\n    !\n    let int:n = o\n    return\n");
        Assert.Contains(result.Diagnostics.Errors, d => d.Message.Contains("Cannot assign"));
    }

    /// <summary>
    /// A declared generic parameter is not an unknown type. `§F{Identity<T>:pub}` — the spelling
    /// the shipped sample and the docs use — leaves `<T>` embedded in the function NAME rather
    /// than in `TypeParameters`, so the checker warned that a correct construct might be a typo,
    /// three times per parameter.
    /// </summary>
    [Fact]
    public void DeclaredGenericParameter_IsNotWarnedAbout()
    {
        var result = Check("module S S block\n  func Identity<T> (T:value) → T\n    !\n    return value\n");
        Assert.DoesNotContain(result.Diagnostics, d => d.Message.Contains("is not known"));
    }

    /// <summary>PUSH must stay silent on a receiver the checker cannot model, like its siblings.</summary>
    [Fact]
    public void PushOnUnmodeledReceiver_IsNotAnError()
        => AssertNoErrors("module S S block\n  func Do (SomeUnknownBag:items) → void\n    ! unknown\n    items.Add(1)\n    return\n");

    [Theory]
    [InlineData("[str]")]   // the collection-literal spelling in the syntax reference
    [InlineData("[u8]")]
    [InlineData("str[]")]
    public void ArrayTypes_Resolve(string type)
    {
        AssertNoErrors($"module S S block\n  func Do ({Native(type)}:xs) → void\n    !\n    return\n");
    }

    /// <summary>
    /// Arrays must be checked, not merely accepted — and BOTH spellings. `[T]` reaches the checker
    /// expanded (`ARRAY[element=…]`), so a fix that only retried the primitive table left it
    /// falling through to the permissive external type: `§B{a:[str]} someIntArray` was accepted.
    /// </summary>
    [Theory]
    [InlineData("[i32]", "[str]")]
    [InlineData("i32[]", "str[]")]
    public void ArrayTypes_RejectMismatchedElements(string paramType, string bindType)
    {
        var result = Check(
            $"module S S block\n  func Do ({Native(paramType)}:xs) → void\n    !\n    let {Native(bindType)}:a = xs\n    return\n");

        Assert.Contains(result.Diagnostics.Errors, d => d.Message.Contains("Cannot assign"));
    }

    /// <summary>
    /// An unresolved type must still be REPORTED — as a warning, not silence and not an error.
    ///
    /// <para>Silence was the first attempt and it is wrong: on `calor_check`, `calor_refine` and
    /// `calor -i/-o` nothing else compiles the generated C#, so a misspelt type would vanish
    /// rather than resurface as CS0246, contrary to what an earlier revision of this change
    /// claimed. An error is also wrong — it rejects every interop type and is most of what this
    /// change set fixes. A heuristic (lower-case = Calor typo, PascalCase = external) was tried
    /// and rejected: it missed `Strng` while firing on working programs.</para>
    /// </summary>
    [Theory]
    [InlineData("strng")]   // a misspelt Calor type
    [InlineData("Strng")]   // ...and one that looks external, which a case heuristic would miss
    public void UnresolvedType_IsWarnedAboutNotSilentlyAccepted(string type)
    {
        var result = Check($"module S S block\n  func Do () → void\n    !\n    let {type}:x = \"a\"\n    return\n");

        Assert.False(result.HasErrors);
        Assert.Contains(result.Diagnostics.Warnings, d => d.Message.Contains($"'{type}' is not known"));
    }

    /// <summary>C# has no implicit decimal↔double conversion; accepting it emits CS0019.</summary>
    [Fact]
    public void DecimalMixedWithFloat_IsRejected()
    {
        var result = Check(
            "module S S block\n  func Do (decimal:d, double:x) → void\n    !\n    let decimal:r = (d + x)\n    return\n");

        Assert.Contains(result.Diagnostics.Errors, d => d.Message.Contains("decimal"));
    }

    /// <summary>
    /// `§VAR{d}` in a switch-expression arm binds `d`. `CheckPattern` modelled 7 of the AST's 19
    /// pattern kinds and hard-errored on the rest, and the match-EXPRESSION path never entered a
    /// scope or bound patterns at all — so this program reported `Undefined variable 'd'` twice
    /// plus `Unsupported pattern type`, on a program whose emitted C# compiles.
    /// </summary>
    [Fact]
    public void SwitchExpressionPatternVariable_IsBound()
        => AssertNoErrors("""
            module S S block
              func Do (int:diff) → int
                !
                let int:result = match diff
                  case var d when (d > 0) → d
                  case _ → 0
                return result
            """);

    /// <summary>
    /// Arrays are indexable. Making `T[]` resolve without teaching SETIDX about it turned working
    /// programs — including two agent-native benchmark GOLD references — into hard errors, and
    /// dropped one of them from 53 proven contracts to zero.
    /// </summary>
    [Fact]
    public void SetIndexOnArray_IsAccepted()
        => AssertNoErrors("""
            module S S block
              func Do (int[]:xs, int:i, int:v) → int
                ! mut
                set xs[i] v
                return 0
            """);

    /// <summary>
    /// A width still has to be ECHOED correctly once it resolves. Resolving `i64` to the collapsed
    /// Int and then reporting "expected i32" would trade a false error for a misleading message —
    /// and it nearly did: sized types reach the checker EXPANDED (`INT[bits=64][signed=true]`), so
    /// the obvious fix silently made every sized binding assignable from anything.
    /// </summary>
    [Fact]
    public void SizedType_MismatchEchoesTheWrittenWidth()
    {
        var result = Check("module S S block\n  func Do () → int\n    !\n    let long:x = \"a\"\n    return 0\n");

        var error = Assert.Single(result.Diagnostics.Errors);
        Assert.Contains("i64", error.Message);
        Assert.DoesNotContain("i32", error.Message);
    }

    // ---- Operators the checker got wrong ----

    /// <summary>
    /// `+` on strings is concatenation, in Calor and in the emitted C#. `calor run` on this prints
    /// `helloworld`; the checker called it "Arithmetic operators require numeric operands".
    /// </summary>
    [Fact]
    public void StringConcatenation_IsNotAnArithmeticError()
        => AssertNoErrors("module S S block\n  func Do () → string\n    !\n    return (\"hello\" + \"world\")\n");

    // ---- References the checker mistook for variables ----

    [Theory]
    [InlineData("Math.PI")]
    [InlineData("int.MaxValue")]
    [InlineData("StringComparison.Ordinal")]
    [InlineData("System.Environment.NewLine")]
    public void StaticMemberAccess_IsNotAnUndefinedVariable(string member)
    {
        // These are member accesses the emitter passes through to C# verbatim. Reporting them as
        // undefined VARIABLES rejected code that compiles and runs.
        var result = Check($"module S S block\n  func Do () → void\n    !\n    let object:v = {member}\n    return\n");
        Assert.DoesNotContain(result.Diagnostics.Errors,
            d => d.Message.Contains("Undefined variable"));
    }

    /// <summary>A bare unknown identifier must STILL be an error — the fix above must not weaken it.</summary>
    [Fact]
    public void BareUnknownIdentifier_IsStillReported()
    {
        var result = Check("module S S block\n  func Do () → int\n    !\n    return nonexistent_xyz\n");
        Assert.Contains(result.Diagnostics.Errors,
            d => d.Message.Contains("Undefined variable 'nonexistent_xyz'"));
    }

    // ---- Cascades from the checker's own blind spots ----

    /// <summary>
    /// The checker models no BCL surface, so an external call yields an unknown type. Reporting
    /// "IF condition must be bool, got &lt;error&gt;" turns the checker's ignorance into the
    /// user's error — and it cascades to every downstream use of the value. This exact program
    /// shape is the MCP primer's `§M{Files}` module.
    /// </summary>
    [Fact]
    public void UnknownCallResult_DoesNotCascadeIntoAConditionError()
    {
        AssertNoErrors("""
            module S S block
              func Do (string:path) → int
                ! fs:r
                let bool:ok = File.Exists(path)
                if (ok == true)
                  return 1
                return 0
            """);
    }

    [Fact]
    public void UnknownReceiver_DoesNotCascadeIntoAFieldAccessError()
        => AssertNoErrors("module S S block\n  func Do () → void\n    !\n    let object:v = System.Environment.NewLine\n    return\n");
    /// <summary>
    /// A type the module DECLARES is not an unknown external type. Found in release review of
    /// v0.12.0: the checker registered only type parameters, so a class declared eight lines
    /// above was reported as possibly a typo — on a program that compiles and runs. It escaped
    /// the corpus sweep because no shipped sample binds a locally-declared class to a typed §B.
    /// </summary>
    [Fact]
    public void ModuleDeclaredTypes_AreNotReportedAsUnknown()
    {
        var result = Check("""
            module ClsTest ClsTest block
              class Point
                field int:X
                ctor (int:x)
                  set X x

              func Use () → void
                !
                let Point:p = new Point(3)
                return
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Message.Contains("is not known"));
    }

    /// <summary>
    /// ...and the guard that keeps the fix honest: a name the module does NOT declare is still
    /// reported, so registering declarations did not simply silence the check.
    /// </summary>
    [Fact]
    public void UndeclaredType_IsStillReported()
    {
        var result = Check("module S S block\n  func Do () → void\n    !\n    let NoSuchTypeXyz:x = \"a\"\n    return\n");
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("'NoSuchTypeXyz' is not known"));
    }

    /// <summary>
    /// The §PP, nested-type and §ITYPE gaps in the first cut of the declared-type pass, which
    /// hand-enumerated four module collections and missed every declaration not sitting directly
    /// on the module — in a release whose own headline is that hand-enumeration failed at three
    /// successive levels. Found by release review.
    /// </summary>
    [Fact]
    public void PreprocessorWrappedAndNestedDeclarations_AreNotReportedAsUnknown()
    {
        var result = Check("""
            module PpTest PpTest block
              pp DEBUG
                class Dbg
                  field int:N

              class Outer
                class Inner
                  field int:M

              func Use (Dbg:d) → void
                !
                let Outer.Inner:i
                return
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Message.Contains("is not known"));
    }

    /// <summary>
    /// The counterpart, and the reason nested types are registered QUALIFIED. Module functions are
    /// emitted into a sibling static class, so the bare spelling produces C# that csc rejects with
    /// CS0246. The first cut of the fix registered nested types bare — silencing this true positive
    /// while leaving `Outer.Inner`, the spelling that does compile, still warning. Second-round
    /// release review caught it, and an earlier revision of the test above cemented it.
    /// </summary>
    [Fact]
    public void BareNestedTypeName_IsStillReported()
    {
        var result = Check("""
            module NestTest NestTest block
              class Outer
                class Inner
                  field int:M

              func Use () → void
                !
                let Inner:i
                return
            """);

        Assert.Contains(result.Diagnostics, d => d.Message.Contains("'Inner' is not known"));
    }

    // The opt-out itself is pinned in CliTypeCheckDefaultTests, against the real CLI. A pin that
    // sets CompilationOptions.EnableTypeChecking directly cannot discriminate: that property has
    // been init-settable since long before the flag existed, so such a test passes with the entire
    // opt-out removed. Release review caught exactly that here.

}
