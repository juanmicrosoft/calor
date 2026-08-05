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

    [Theory]
    [InlineData("[str]")]   // the collection-literal spelling in the syntax reference
    [InlineData("[u8]")]
    public void ArrayTypes_Resolve(string type)
    {
        AssertNoErrors($"§M{{m:S}}\n  §F{{f:Do:pub}} ({type}:xs) -> void\n    §E{{}}\n    §R\n");
    }

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
