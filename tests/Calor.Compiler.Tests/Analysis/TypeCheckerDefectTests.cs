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

    /// <summary>
    /// `object` is excluded from the theory above — a top type cannot reject a `str`, so the
    /// negative test has to run the other way. It was 13 of the original 92 and would otherwise
    /// be the one type with no discriminating pin.
    /// </summary>
    [Fact]
    public void ObjectIsATopType_NotAUniversalEscape()
    {
        // Everything assigns TO object...
        AssertNoErrors("§M{m:S}\n  §F{f:Do:pub} () -> void\n    §E{}\n    §B{o:object} STR:\"a\"\n    §R\n");

        // ...and nothing assigns FROM it without a cast, which is what C# requires.
        var result = Check("§M{m:S}\n  §F{f:Do:pub} (object:o) -> void\n    §E{}\n    §B{n:i32} o\n    §R\n");
        Assert.Contains(result.Diagnostics.Errors, d => d.Message.Contains("Cannot assign"));
    }

    /// <summary>
    /// A declared generic parameter is not an unknown type. `§F{f:Identity<T>:pub}` — the spelling
    /// the shipped sample and the docs use — leaves `<T>` embedded in the function NAME rather
    /// than in `TypeParameters`, so the checker warned that a correct construct might be a typo,
    /// three times per parameter.
    /// </summary>
    [Fact]
    public void DeclaredGenericParameter_IsNotWarnedAbout()
    {
        var result = Check("§M{m:S}\n  §F{f001:Identity<T>:pub} (T:value) -> T\n    §R value\n");
        Assert.DoesNotContain(result.Diagnostics, d => d.Message.Contains("is not known"));
    }

    /// <summary>PUSH must stay silent on a receiver the checker cannot model, like its siblings.</summary>
    [Fact]
    public void PushOnUnmodeledReceiver_IsNotAnError()
        => AssertNoErrors("§M{m:S}\n  §F{f:Do:pub} (SomeUnknownBag:items) -> void\n    §E{mut}\n    §PUSH{items} INT:1\n    §R\n");

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
            §M{m001:ClsTest}
              §CL{c001:Point:pub}
                §FLD{i32:X:pub}
                §CTOR{ctor1:pub} (i32:x)
                  §ASSIGN X x
              §F{f001:Use:pub} () -> void
                §E{}
                §B{p:Point} §NEW{Point} §A INT:3 §/NEW
                §R
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
        var result = Check("§M{m:S}\n  §F{f:Do:pub} () -> void\n    §E{}\n    §B{x:NoSuchTypeXyz} STR:\"a\"\n    §R\n");
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
            §M{m001:PpTest}
              §PP{DEBUG}
                §CL{c001:Dbg:pub}
                  §FLD{i32:N:pub}
              §/PP{DEBUG}
              §CL{c002:Outer:pub}
                §CL{c003:Inner:pub}
                  §FLD{i32:M:pub}
              §F{f001:Use:pub} (Dbg:d) -> void
                §E{}
                §B{i:Outer.Inner}
                §R
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
            §M{m001:NestTest}
              §CL{c002:Outer:pub}
                §CL{c003:Inner:pub}
                  §FLD{i32:M:pub}
              §F{f001:Use:pub} () -> void
                §E{}
                §B{i:Inner}
                §R
            """);

        Assert.Contains(result.Diagnostics, d => d.Message.Contains("'Inner' is not known"));
    }

    /// <summary>
    /// A §RTYPE whose base names a module class must not ALSO warn that a class declared two lines
    /// above "may be a typo". Stated precisely, because an earlier revision of this test claimed
    /// more than is true: such a program is still <c>Calor1102</c> — a module-declared type is an
    /// <c>ExternalType</c>, which <c>RegisterRefinementType</c> rejects as a base outright, before
    /// and after v0.12. The declared-type pass running first buys only the removal of a redundant
    /// second diagnostic on an already-failing program. That is the whole benefit; it is not a
    /// claim that the program now works.
    /// </summary>
    [Fact]
    public void RefinementBaseNamingAModuleClass_DoesNotAlsoWarnUnknownType()
    {
        var result = Check("""
            §M{m001:RtBase}
              §CL{c001:Money:pub}
                §FLD{i32:V:pub}
              §RTYPE{r001:PosMoney:Money} (> value INT:0)
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Message.Contains("'Money' is not known"));
        // The pre-existing rejection is unchanged, and saying so here keeps the test from reading
        // as "this compiles now".
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("undefined base type 'Money'"));
    }

    /// <summary>
    /// A refinement may claim a name the declared-type pass seeded — but only ONE may. The first
    /// cut of that exemption used a set membership test that never expired, so every later §RTYPE
    /// of the same name was exempt too and two conflicting refinements compiled clean, a
    /// regression against main. Found by the third round of release review.
    /// </summary>
    [Fact]
    public void TwoRefinementsSharingAClassName_IsStillADuplicate()
    {
        var result = Check("""
            §M{m001:Dup2}
              §CL{c001:Positive:pub}
                §FLD{i32:V:pub}
              §RTYPE{r001:Positive:i32} (> value INT:0)
              §RTYPE{r002:Positive:i32} (> value INT:1)
            """);

        Assert.Contains(result.Diagnostics, d => d.Message.Contains("Duplicate refinement type name 'Positive'"));
    }

    /// <summary>
    /// §ITYPE declarations are part of the declared-type pass and were unpinned: deleting that
    /// line from the enumeration left the whole suite green while emitting a false Calor0200.
    /// </summary>
    [Fact]
    public void IndexedTypeDeclaration_IsNotReportedAsUnknown()
    {
        const string src = """
            §M{m001:ItypeTest}
              §ITYPE{it1:NonEmptyList:List:n} (> # INT:0)
              §F{f001:Use:pub} (NonEmptyList:a) -> void
                §E{}
                §R
            """;

        var result = Check(src);

        // The fixture must actually be a §ITYPE program. The first version of this test wrote the
        // §RTYPE attribute shape and failed Calor0102 "Missing required attribute 'sizeParam'" —
        // it asserted the absence of a warning on a program that never got far enough to produce
        // one, and passed with the §ITYPE line deleted from the checker entirely.
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.MissingRequiredAttribute);
        Assert.DoesNotContain(result.Diagnostics, d => d.Message.Contains("'NonEmptyList' is not known"));
    }

    /// <summary>
    /// The declared-type pass must not steal a name from a §RTYPE. RegisterRefinementType rejects
    /// an already-defined name, so registering module types first would newly reject a refinement
    /// sharing a class name — a program that compiled before v0.12. Refinements are registered
    /// first and the declared-type pass skips names already taken.
    /// </summary>
    [Fact]
    public void RefinementType_KeepsItsName_AgainstASameNamedClass()
    {
        var result = Check("""
            §M{m001:RtTest}
              §CL{c001:Positive:pub}
                §FLD{i32:V:pub}
              §RTYPE{r001:Positive:i32} (> value INT:0)
              §F{f001:Use:pub} () -> void
                §E{}
                §R
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Message.Contains("Duplicate refinement type name"));
    }

    // The opt-out itself is pinned in CliTypeCheckDefaultTests, against the real CLI. A pin that
    // sets CompilationOptions.EnableTypeChecking directly cannot discriminate: that property has
    // been init-settable since long before the flag existed, so such a test passes with the entire
    // opt-out removed. Release review caught exactly that here.

}
