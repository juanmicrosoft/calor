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
        => Program.Compile(source, "t.calr", new CompilationOptions { EnableTypeChecking = true });

    private static void AssertNoErrors(string source)
    {
        var result = Check(source);
        Assert.False(result.HasErrors,
            string.Join("\n", result.Diagnostics.Errors.Select(d => $"{d.Code}: {d.Message}")));
    }

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
        AssertNoErrors($"§M{{m:S}}\n  §F{{f:Do:pub}} ({type}:x) -> void\n    §E{{}}\n    §R\n");
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
        var result = Check($"§M{{m:S}}\n  §F{{f:Do:pub}} () -> void\n    §E{{}}\n    §B{{x:{type}}} BOOL:true\n    §R\n");

        var error = Assert.Single(result.Diagnostics.Errors);
        Assert.Contains(type, error.Message);
    }

    [Theory]
    [InlineData("[str]")]   // the collection-literal spelling in the syntax reference
    [InlineData("[u8]")]
    [InlineData("str[]")]
    public void ArrayTypes_Resolve(string type)
    {
        AssertNoErrors($"§M{{m:S}}\n  §F{{f:Do:pub}} ({type}:xs) -> void\n    §E{{}}\n    §R\n");
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
            $"§M{{m:S}}\n  §F{{f:Do:pub}} ({paramType}:xs) -> void\n    §E{{}}\n    §B{{a:{bindType}}} xs\n    §R\n");

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
        var result = Check($"§M{{m:S}}\n  §F{{f:Do:pub}} () -> void\n    §E{{}}\n    §B{{x:{type}}} STR:\"a\"\n    §R\n");

        Assert.False(result.HasErrors);
        Assert.Contains(result.Diagnostics.Warnings, d => d.Message.Contains($"'{type}' is not known"));
    }

    /// <summary>C# has no implicit decimal↔double conversion; accepting it emits CS0019.</summary>
    [Fact]
    public void DecimalMixedWithFloat_IsRejected()
    {
        var result = Check(
            "§M{m:S}\n  §F{f:Do:pub} (decimal:d, f64:x) -> void\n    §E{}\n    §B{r:decimal} (+ d x)\n    §R\n");

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
            §M{m:S}
              §F{f:Do:pub} (i32:diff) -> i32
                §E{}
                §B{result:i32} §W{sw1:expr} diff
                  §K §VAR{d} §WHEN (> d 0) → d
                  §K _ → 0
                §R result
            """);

    /// <summary>
    /// Arrays are indexable. Making `T[]` resolve without teaching SETIDX about it turned working
    /// programs — including two agent-native benchmark GOLD references — into hard errors, and
    /// dropped one of them from 53 proven contracts to zero.
    /// </summary>
    [Fact]
    public void SetIndexOnArray_IsAccepted()
        => AssertNoErrors("""
            §M{m:S}
              §F{f:Do:pub} (i32[]:xs, i32:i, i32:v) -> i32
                §E{mut}
                §SETIDX{xs} i v
                §R INT:0
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
        var result = Check("§M{m:S}\n  §F{f:Do:pub} () -> i32\n    §B{x:i64} STR:\"a\"\n    §R INT:0\n");

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
        => AssertNoErrors("§M{m:S}\n  §F{f:Do:pub} () -> str\n    §E{}\n    §R (+ STR:\"hello\" STR:\"world\")\n");

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
        var result = Check($"§M{{m:S}}\n  §F{{f:Do:pub}} () -> void\n    §E{{}}\n    §B{{v:object}} {member}\n    §R\n");
        Assert.DoesNotContain(result.Diagnostics.Errors,
            d => d.Message.Contains("Undefined variable"));
    }

    /// <summary>A bare unknown identifier must STILL be an error — the fix above must not weaken it.</summary>
    [Fact]
    public void BareUnknownIdentifier_IsStillReported()
    {
        var result = Check("§M{m:S}\n  §F{f:Do:pub} () -> i32\n    §E{}\n    §R nonexistent_xyz\n");
        Assert.Contains(result.Diagnostics.Errors,
            d => d.Message.Contains("Undefined variable 'nonexistent_xyz'"));
    }

    // ---- Cascades from the checker's own blind spots ----

    /// <summary>
    /// The checker models no BCL surface, so an external call yields an unknown type. Reporting
    /// "IF condition must be bool, got &lt;error&gt;" turns the checker's ignorance into the
    /// user's error — and it cascades to every downstream use of the value. This exact program
    /// shape is the MCP primer's `§M{m3:Files}` module.
    /// </summary>
    [Fact]
    public void UnknownCallResult_DoesNotCascadeIntoAConditionError()
    {
        AssertNoErrors("""
            §M{m:S}
              §F{f:Do:pub} (str:path) -> i32
                §E{fs:r}
                §B{ok:bool} §C{File.Exists} §A path §/C
                §IF{i1} (== ok BOOL:true)
                  §R INT:1
                §R INT:0
            """);
    }

    [Fact]
    public void UnknownReceiver_DoesNotCascadeIntoAFieldAccessError()
        => AssertNoErrors("§M{m:S}\n  §F{f:Do:pub} () -> void\n    §E{}\n    §B{v:object} System.Environment.NewLine\n    §R\n");

    // ---- Diagnostics that belong to another pass ----

    /// <summary>
    /// `BindValidationPass` owns this condition as Calor0250, with a quickfix. The checker
    /// reported it too, as the vaguer Calor0202 — so turning the checker on REPLACED a precise
    /// diagnostic with a worse one for the same program.
    /// </summary>
    [Fact]
    public void BareBinding_ReportsCalor0250_NotTheCheckersOwnTypeMismatch()
    {
        var result = Check("§M{m:S}\n  §F{f:Do:pub} () -> void\n    §E{}\n    §B{x}\n    §R\n");

        Assert.Contains(result.Diagnostics.Errors, d => d.Code == DiagnosticCode.BindRequiresTypeOrInitializer);
        Assert.DoesNotContain(result.Diagnostics.Errors,
            d => d.Message.Contains("Variable binding requires either a type annotation"));
    }

    /// <summary>
    /// The permissive external-type fallback must NOT reach refinement base types: those are a
    /// Calor-level construct the refinement machinery reasons about, so "some .NET type we do not
    /// model" is not an acceptable answer there.
    /// </summary>
    [Fact]
    public void RefinementWithUnknownBaseType_IsStillAnError()
    {
        var result = Check("§M{m:S}\n  §RTYPE{r1:MyType:nonexistent_type_xyz} (>= # INT:0)\n");
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.RefinementUndefinedBaseType);
    }
}
