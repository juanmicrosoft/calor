using System.Reflection;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Verification.Obligations;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests.Analysis;

/// <summary>
/// #1515: operators on a value of a NAMED refinement type (<c>§RTYPE</c>) were rejected with
/// Calor0202 because the type checker saw <c>i32{#i32}</c> and never unwrapped it to its base.
/// An inline refinement (<c>§I{i32:x} | (pred)</c>) was already typed as its base and worked.
///
/// <para>The rule these tests pin: operators see a named refinement as its base type, and the
/// RESULT is the base type, never the refinement. Writing a result back into a refined variable
/// or return is checked by the obligation engine (Subtype / RefinementReturn obligations and
/// runtime guards), exactly as for an inline refinement. The checker's existing refusal to bind
/// a plain base value into a <c>§B{x:Refined}</c> is unchanged.</para>
/// </summary>
public class NamedRefinementArithmeticTests
{
    // Generated-output validation stays ON: an accepted program must also compile as C#, so a
    // test cannot pass on a program the checker accepts but Roslyn rejects.
    private static CompilationResult Check(string source)
        => Program.Compile(source, "t.calr", new CompilationOptions
        {
            EnableTypeChecking = true,
        });

    private static void AssertNoErrors(string source)
    {
        var result = Check(source);
        Assert.False(result.HasErrors,
            string.Join("\n", result.Diagnostics.Errors.Select(d => $"{d.Code}: {d.Message}")));
    }

    private static string Predicate(string baseType) => baseType switch
    {
        "f64" or "f32" => "(> # FLOAT:0.0)",
        _ => "(>= # INT:0)",
    };

    /// <summary>The C# result type of a binary arithmetic operator on two values of this base:
    /// sub-int widths promote to int (C# 12.4.7).</summary>
    private static string ResultOf(string baseType) => baseType switch
    {
        "i16" or "u8" => "i32",
        _ => baseType,
    };

    public static TheoryData<string, string> OperatorsByBase()
    {
        var data = new TheoryData<string, string>();
        foreach (var baseType in new[] { "i32", "i64", "i16", "u8", "u32", "f32", "f64", "decimal" })
        {
            foreach (var op in new[] { "+", "-", "*", "/", "%" })
                data.Add(baseType, op);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(OperatorsByBase))]
    public void Arithmetic_TwoValuesOfTheSameNamedRefinement_TypeChecks(string baseType, string op)
    {
        AssertNoErrors($"""
            §M{"{"}m:R{"}"}
              §RTYPE{"{"}r1:Nat:{baseType}{"}"} {Predicate(baseType)}
              §F{"{"}f1:Op:pub{"}"} (Nat:a, Nat:b) -> {ResultOf(baseType)}
                §R ({op} a b)
            """);
    }

    [Theory]
    [MemberData(nameof(OperatorsByBase))]
    public void Arithmetic_NamedRefinementWithItsBaseType_TypeChecks(string baseType, string op)
    {
        AssertNoErrors($"""
            §M{"{"}m:R{"}"}
              §RTYPE{"{"}r1:Nat:{baseType}{"}"} {Predicate(baseType)}
              §F{"{"}f1:Op:pub{"}"} (Nat:a, {baseType}:b) -> {ResultOf(baseType)}
                §R ({op} b a)
            """);
    }

    [Theory]
    [InlineData("+")]
    [InlineData("-")]
    [InlineData("*")]
    [InlineData("/")]
    [InlineData("%")]
    public void Arithmetic_NamedRefinementWithLiterals_TypeChecks(string op)
    {
        AssertNoErrors($"""
            §M{"{"}m:R{"}"}
              §RTYPE{"{"}r1:Nat:i32{"}"} (>= # INT:0)
              §RTYPE{"{"}r2:Pos:f64{"}"} (> # FLOAT:0.0)
              §F{"{"}f1:I:pub{"}"} (Nat:a) -> i32
                §R ({op} a INT:7)
              §F{"{"}f2:J:pub{"}"} (Nat:a) -> i32
                §R ({op} INT:7 a)
              §F{"{"}f3:F:pub{"}"} (Pos:x) -> f64
                §R ({op} x FLOAT:2.5)
            """);
    }

    [Theory]
    [InlineData("i32")]
    [InlineData("i64")]
    [InlineData("f64")]
    public void Arithmetic_TwoDifferentNamedRefinementsOfTheSameBase_TypeChecks(string baseType)
    {
        var lower = baseType == "f64" ? "(> # FLOAT:0.0)" : "(>= # INT:0)";
        var upper = baseType == "f64" ? "(< # FLOAT:100.0)" : "(< # INT:100)";
        AssertNoErrors($"""
            §M{"{"}m:R{"}"}
              §RTYPE{"{"}r1:Low:{baseType}{"}"} {lower}
              §RTYPE{"{"}r2:High:{baseType}{"}"} {upper}
              §F{"{"}f1:Mix:pub{"}"} (Low:a, High:b) -> {baseType}
                §R (- (* (+ a b) (- a b)) (% (/ a b) b))
            """);
    }

    [Fact]
    public void Arithmetic_NamedRefinementMixedWithAnotherNumericBase_FollowsBaseRules()
    {
        // i32 refinement with an f64 value widens to f64, exactly as plain i32 + f64 does.
        AssertNoErrors("""
            §M{m:R}
              §RTYPE{r1:Nat:i32} (>= # INT:0)
              §RTYPE{r2:Pos:f64} (> # FLOAT:0.0)
              §F{f1:Mix:pub} (Nat:a, Pos:x) -> f64
                §B{y:f64} (+ a x)
                §R (* y a)
            """);
    }

    [Theory]
    [InlineData("i32")]
    [InlineData("i64")]
    [InlineData("f64")]
    [InlineData("decimal")]
    public void UnaryMinus_OnNamedRefinement_TypeChecks(string baseType)
    {
        AssertNoErrors($"""
            §M{"{"}m:R{"}"}
              §RTYPE{"{"}r1:Nat:{baseType}{"}"} {Predicate(baseType)}
              §F{"{"}f1:Neg:pub{"}"} (Nat:a) -> {baseType}
                §B{"{"}y:{baseType}{"}"} (- a)
                §R (+ y (- a))
            """);
    }

    /// <summary>
    /// Pins the unary RESULT type. Before #1515 unary minus on a named refinement silently
    /// returned the error type, which is assignable anywhere, so the refined bind below was
    /// accepted. Now the result is the base type, and binding it into the refinement is rejected
    /// exactly like binding any other base value.
    /// </summary>
    [Fact]
    public void UnaryMinus_ResultIsTheBaseType_NotTheRefinementNorAnErrorType()
    {
        var result = Check("""
            §M{m:R}
              §RTYPE{r1:Nat:i32} (>= # INT:0)
              §RTYPE{r2:Pos:f64} (> # FLOAT:0.0)
              §F{f1:Neg:pub} (Nat:a) -> i32
                §B{y:Nat} (- a)
                §R y
              §F{f2:NegF:pub} (Pos:x) -> i32
                §B{z:i32} (- x)
                §R z
            """);

        Assert.Contains(result.Diagnostics.Errors,
            d => d.Message.Contains("Cannot assign i32 to variable of type i32{#i32}"));
        Assert.Contains(result.Diagnostics.Errors,
            d => d.Message.Contains("Cannot assign f64 to variable of type i32"));
    }

    [Theory]
    [InlineData("==")]
    [InlineData("!=")]
    [InlineData("<")]
    [InlineData("<=")]
    [InlineData(">")]
    [InlineData(">=")]
    public void Comparison_OnNamedRefinements_TypeChecksAsBool(string op)
    {
        // Bare comparisons already returned bool before #1515 without looking at operand types;
        // the last comparison compares arithmetic RESULTS, which is what the fix enables.
        AssertNoErrors($"""
            §M{"{"}m:R{"}"}
              §RTYPE{"{"}r1:Nat:i32{"}"} (>= # INT:0)
              §RTYPE{"{"}r2:Small:i32{"}"} (< # INT:100)
              §RTYPE{"{"}r3:Pos:f64{"}"} (> # FLOAT:0.0)
              §F{"{"}f1:Cmp:pub{"}"} (Nat:a, Small:b, Pos:x, i32:c) -> bool
                §B{"{"}p:bool{"}"} ({op} a b)
                §B{"{"}q:bool{"}"} ({op} a c)
                §B{"{"}r:bool{"}"} ({op} x a)
                §R (&& p (&& q (|| r ({op} (+ a b) (* x INT:2)))))
            """);
    }

    [Fact]
    public void BitwiseAndShift_OnIntegerNamedRefinement_TypeChecks()
    {
        AssertNoErrors("""
            §M{m:R}
              §RTYPE{r1:Nat:i32} (>= # INT:0)
              §F{f1:Bits:pub} (Nat:a, Nat:b) -> i32
                §R (^ (& a b) (| (<< a INT:1) (>> b INT:1)))
            """);
    }

    [Fact]
    public void NamedRefinementOfBool_WorksInLogicalOperatorsAndConditions()
    {
        AssertNoErrors("""
            §M{m:R}
              §RTYPE{r1:Yes:bool} (== # true)
              §F{f1:Logic:pub} (Yes:f, bool:g) -> bool
                §IF{i1} f
                  §R (|| (&& f g) (! f))
                §R false
            """);
    }

    [Fact]
    public void NamedRefinement_AsLoopBoundAndArithmeticInBody_TypeChecks()
    {
        AssertNoErrors("""
            §M{m:R}
              §RTYPE{r1:Nat:i32} (>= # INT:0)
              §F{f1:Sum:pub} (Nat:n) -> i32
                §B{~s:i32} INT:0
                §L{l1:i:0:n:1}
                  §ASSIGN s (+ s (* i n))
                §R s
            """);
    }

    // ---- The result type is the BASE type, not the refinement ----

    [Fact]
    public void ArithmeticResult_IsTheBaseType_SoAnExplicitRefinedBindIsStillRejected()
    {
        // `Nat - Nat` can be negative. The checker must not hand the result the refinement type;
        // binding a plain base value into an explicitly refined §B was rejected before #1515 (even
        // for a literal) and still is.
        var result = Check("""
            §M{m:R}
              §RTYPE{r1:Nat:i32} (>= # INT:0)
              §F{f1:Sub:pub} (Nat:a, Nat:b) -> i32
                §B{d:Nat} (- a b)
                §R d
            """);

        Assert.Contains(result.Diagnostics.Errors, d => d.Code == DiagnosticCode.TypeMismatch
            && d.Message.Contains("Cannot assign i32 to variable of type i32{#i32}"));
        Assert.DoesNotContain(result.Diagnostics.Errors,
            d => d.Message.Contains("Arithmetic operators require numeric operands"));
    }

    [Fact]
    public void ArithmeticResult_OfFloatRefinement_IsFloat_NotAssignableToInt()
    {
        var result = Check("""
            §M{m:R}
              §RTYPE{r1:Pos:f64} (> # FLOAT:0.0)
              §F{f1:Half:pub} (Pos:x) -> i32
                §B{y:i32} (/ x INT:2)
                §R y
            """);

        Assert.Contains(result.Diagnostics.Errors, d => d.Code == DiagnosticCode.TypeMismatch
            && d.Message.Contains("Cannot assign f64 to variable of type i32"));
    }

    [Fact]
    public void DecimalRefinement_MixedWithFloat_IsStillRejected()
    {
        var result = Check("""
            §M{m:R}
              §RTYPE{r1:Money:decimal} (>= # INT:0)
              §F{f1:Mix:pub} (Money:m, f64:x) -> decimal
                §R (+ m x)
            """);

        Assert.Contains(result.Diagnostics.Errors,
            d => d.Message.Contains("Cannot mix decimal and f64 in arithmetic"));
    }

    [Fact]
    public void DecimalRefinementArithmetic_IsDecimal()
    {
        AssertNoErrors("""
            §M{m:R}
              §RTYPE{r1:Money:decimal} (>= # INT:0)
              §F{f1:Sum:pub} (Money:a, Money:b) -> decimal
                §B{boxed:Option<decimal>} §SM (+ a b)
                §B{total:decimal} (* (+ a b) INT:2)
                §R total
            """);

        var narrowed = Check("""
            §M{m:R}
              §RTYPE{r1:Money:decimal} (>= # INT:0)
              §F{f1:Sum:pub} (Money:a, Money:b) -> i32
                §B{x:i32} (+ a b)
                §R x
            """);
        Assert.Contains(narrowed.Diagnostics.Errors, d => d.Code == DiagnosticCode.TypeMismatch
            && d.Message.Contains("Cannot assign decimal to variable of type i32"));

        var nested = Check("""
            §M{m:R}
              §RTYPE{r1:Money:decimal} (>= # INT:0)
              §F{f1:Mix:pub} (Money:a, Money:b, f64:f) -> decimal
                §R (+ (+ a b) f)
            """);
        Assert.Contains(nested.Diagnostics.Errors,
            d => d.Message.Contains("Cannot mix decimal and f64 in arithmetic"));
    }

    [Fact]
    public void NonNumericRefinement_IsStillRejectedInArithmetic()
    {
        var result = Check("""
            §M{m:R}
              §RTYPE{r1:NonEmpty:str} (> (len #) INT:0)
              §F{f1:Bad:pub} (NonEmpty:s) -> i32
                §R (* s INT:2)
            """);

        Assert.Contains(result.Diagnostics.Errors,
            d => d.Message.Contains("Arithmetic operators require numeric operands, got str"));
    }

    [Fact]
    public void StringRefinement_ConcatenatesLikeItsBase()
    {
        AssertNoErrors("""
            §M{m:R}
              §RTYPE{r1:NonEmpty:str} (> (len #) INT:0)
              §F{f1:Greet:pub} (NonEmpty:s) -> str
                §R (+ STR:"hi " s)
            """);
    }

    // ---- Compile, emit and run with the type checker ON ----

    private const string RuntimeSource = """
        §M{m:Rt}
          §RTYPE{r1:Nat:i32} (>= # INT:0)
          §RTYPE{r2:Small:i32} (< # INT:100)
          §RTYPE{r3:BigNat:i64} (>= # INT:0)
          §RTYPE{r4:Pos:f64} (> # FLOAT:0.0)
          §F{f1:Calc:pub} (Nat:a, Small:b, i32:c) -> i32
            §R (+ (- (* a b) (/ a INT:2)) (% (+ a c) INT:7))
          §F{f2:Neg:pub} (Nat:a) -> i32
            §R (- a)
          §F{f3:Less:pub} (Nat:a, Small:b) -> bool
            §R (< a b)
          §F{f4:Wide:pub} (BigNat:a, BigNat:b) -> i64
            §R (* a b)
          §F{f5:Halve:pub} (Pos:x) -> f64
            §R (/ x FLOAT:2.0)
          §F{f6:Down:pub} (Nat:a) -> Nat
            §R (- a INT:3)
          §F{f7:Step:pub} (Nat:a) -> i32
            §B{~x:Nat} a
            §ASSIGN x (- x INT:5)
            §R x
          §F{f8:Dec:pub} (Nat:a) -> i32
            §B{~x:Nat} a
            (dec x)
            §R x
        """;

    private static Assembly CompileAndLoad(string source, out string generatedCode)
    {
        var result = Program.Compile(source, "named-refinement.calr", new CompilationOptions
        {
            ContractMode = ContractMode.Debug,
            EnableTypeChecking = true,
        });
        Assert.False(result.HasErrors,
            string.Join(Environment.NewLine, result.Diagnostics.Errors.Select(d => $"{d.Code}: {d.Message}")));
        generatedCode = result.GeneratedCode;

        var syntaxTree = CSharpSyntaxTree.ParseText(
            GeneratedCSharpCompiler.GlobalUsingsPreamble + generatedCode);
        var compilation = CSharpCompilation.Create(
            $"NamedRefinementArithmetic_{Guid.NewGuid():N}",
            [syntaxTree],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        return Assembly.Load(stream.ToArray());
    }

    private static object? Invoke(Assembly assembly, string method, params object?[] args)
        => Assert.Single(assembly.GetTypes(), t => t.Name == "RtModule")
            .GetMethod(method)!
            .Invoke(null, args);

    [Fact]
    public void Runtime_ArithmeticOnNamedRefinements_ComputesBaseTypeResults()
    {
        var assembly = CompileAndLoad(RuntimeSource, out var code);

        // 9*10 - 9/2 + (9+5)%7 = 90 - 4 + 0
        Assert.Equal(86, Invoke(assembly, "Calc", 9, 10, 5));
        Assert.Equal(-4, Invoke(assembly, "Neg", 4));
        Assert.Equal(true, Invoke(assembly, "Less", 3, 10));
        Assert.Equal(false, Invoke(assembly, "Less", 30, 10));
        Assert.Equal(6_000_000_000L, Invoke(assembly, "Wide", 3_000_000_000L, 2L));
        Assert.Equal(1.25, Invoke(assembly, "Halve", 2.5));

        // Refinements erase: the emitted signatures use the base C# types.
        Assert.Contains("public static long Wide(long a, long b)", code);
        Assert.Contains("public static double Halve(double x)", code);
    }

    [Fact]
    public void Runtime_ResultWrittenBackIntoARefinement_IsStillGuarded()
    {
        var assembly = CompileAndLoad(RuntimeSource, out _);

        // Return into §O{Nat}: fine when the result stays non-negative, guarded when not.
        Assert.Equal(7, Invoke(assembly, "Down", 10));
        var ret = Assert.Throws<TargetInvocationException>(() => Invoke(assembly, "Down", 1));
        Assert.IsType<InvalidOperationException>(ret.InnerException);
        Assert.Contains("Nat", ret.InnerException!.Message);

        // §ASSIGN into a Nat-typed mutable: same guard an inline refinement gets.
        Assert.Equal(5, Invoke(assembly, "Step", 10));
        var asg = Assert.Throws<TargetInvocationException>(() => Invoke(assembly, "Step", 2));
        Assert.IsType<ArgumentOutOfRangeException>(asg.InnerException);
        Assert.Contains("Nat", asg.InnerException!.Message);

        // Decrement of a Nat-typed mutable is guarded as well.
        Assert.Equal(2, Invoke(assembly, "Dec", 3));
        var dec = Assert.Throws<TargetInvocationException>(() => Invoke(assembly, "Dec", 0));
        Assert.IsType<ArgumentOutOfRangeException>(dec.InnerException);
    }

    [Fact]
    public void Emit_NamedAndInlineRefinement_GetTheSameAssignmentGuardShape()
    {
        var result = Program.Compile("""
            §M{m:G}
              §RTYPE{r1:Nat:i32} (>= # INT:0)
              §F{f1:Named:pub} (Nat:a) -> i32
                §ASSIGN a (- a INT:5)
                §R a
              §F{f2:Inline:pub}
                §I{i32:a} | (>= # INT:0)
                §O{i32}
                §ASSIGN a (- a INT:5)
                §R a
            """, "guard.calr", new CompilationOptions { EnableTypeChecking = true });

        Assert.False(result.HasErrors,
            string.Join("\n", result.Diagnostics.Errors.Select(d => $"{d.Code}: {d.Message}")));
        var code = result.GeneratedCode;
        Assert.Contains("Value violates refinement type 'Nat'", code);
        Assert.Contains("Value violates inline refinement for parameter 'a'", code);
        Assert.Equal(2, CountOccurrences(code, "int __refinementCandidate0 = checked(a - 5);"));
    }

    private static int CountOccurrences(string text, string needle)
    {
        var count = 0;
        for (var i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = text.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    // ---- Verification: obligations on the result are generated and solved soundly ----

    /// <summary>
    /// Parity with inline refinements: the Subtype obligation for an arithmetic result assigned
    /// back into a refined parameter is generated and solved the same way for a named refinement
    /// as for the equivalent inline one, and one that can fail is never discharged.
    /// </summary>
    [SkippableFact]
    public void Verify_AssignmentObligationOnArithmeticResult_MatchesInlineRefinement()
    {
        Skip.IfNot(Verification.Z3.Z3ContextFactory.IsAvailable, "Z3 not available");

        var options = new CompilationOptions { VerifyRefinements = true, EnableTypeChecking = true };
        var result = Program.Compile("""
            §M{m:V}
              §RTYPE{r1:Nat:i32} (>= # INT:0)
              §F{n1:NamedDown:priv} (Nat:a) -> i32
                §ASSIGN a (- a INT:5)
                §R a
              §F{n2:NamedUp:priv} (Nat:a) -> i32
                §ASSIGN a (+ a INT:1)
                §R a
              §F{i1:InlineDown:priv}
                §I{i32:a} | (>= # INT:0)
                §O{i32}
                §ASSIGN a (- a INT:5)
                §R a
              §F{i2:InlineUp:priv}
                §I{i32:a} | (>= # INT:0)
                §O{i32}
                §ASSIGN a (+ a INT:1)
                §R a
            """, "verify.calr", options);

        Assert.DoesNotContain(result.Diagnostics.Errors,
            d => d.Message.Contains("Arithmetic operators require numeric operands"));
        Assert.NotNull(options.ObligationResults);
        ObligationStatus StatusOf(string functionId) => Assert.Single(
            options.ObligationResults!.Obligations,
            o => o.FunctionId == functionId && o.Kind == ObligationKind.Subtype).Status;

        Assert.Equal(StatusOf("i1"), StatusOf("n1"));
        Assert.Equal(StatusOf("i2"), StatusOf("n2"));
        // a = 0 is a counterexample to a - 5 >= 0, so it must never be discharged. (Today the
        // solver reports assignment obligations on parameters as Unsupported, for named and
        // inline refinements alike; either way the runtime guard is kept.)
        Assert.NotEqual(ObligationStatus.Discharged, StatusOf("n1"));
        Assert.NotEqual(ObligationStatus.Discharged, StatusOf("n2"));
        // Both named-refinement assignment guards are emitted.
        Assert.Equal(2, CountOccurrences(result.GeneratedCode, "Value violates refinement type 'Nat'"));
    }
}
