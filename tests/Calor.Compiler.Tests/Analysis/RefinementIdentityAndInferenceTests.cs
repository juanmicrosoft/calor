using System.Reflection;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.TypeChecking;
using Calor.Compiler.Verification.Obligations;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests.Analysis;

/// <summary>
/// Two refinement soundness fixes (epic #1592, "no silent wrong answers").
///
/// <para><b>#1529.</b> An unannotated local inferred from a refined value (<c>§B{~x} a</c>, with
/// <c>a: Nat</c>) is typed <c>Nat</c> by the type checker, but the emitter and the obligation
/// generator only looked at the bind's annotation. So <c>§ASSIGN x INT:-5</c> compiled with no
/// guard and no obligation. The inferred local now carries the refinement: it is declared
/// exactly as <c>§B{~x:Nat} a</c> would be, and every later write is guarded.</para>
///
/// <para><b>#1530.</b> Every named refinement's <c>PredicateText</c> was <c>#&lt;base&gt;</c>, so
/// <c>Nat</c> and <c>Pos</c> over <c>i32</c> compared equal and either was accepted where the
/// other was expected. Named refinements are now nominal: binding, passing, returning or
/// assigning one where a different one is expected is <c>Calor0202</c>. Runtime guards are
/// unchanged.</para>
/// </summary>
public class RefinementIdentityAndInferenceTests
{
    private const string Header = """
        §M{m:Rt}
          §RTYPE{r1:Nat:i32} (>= # INT:0)
          §RTYPE{r2:Pos:i32} (> # INT:0)
        """;

    private static CompilationResult Compile(string body, CompilationOptions? options = null)
        => Program.Compile(Header + "\n" + body, "refinement-identity.calr", options ?? new CompilationOptions
        {
            ContractMode = ContractMode.Debug,
            EnableTypeChecking = true,
        });

    private static string Errors(CompilationResult result)
        => string.Join("\n", result.Diagnostics.Errors.Select(d => $"{d.Code}: {d.Message}"));

    private static Assembly CompileAndLoad(
        string body, out string generatedCode, CompilationOptions? options = null)
    {
        var result = Compile(body, options);
        Assert.False(result.HasErrors, Errors(result));
        generatedCode = result.GeneratedCode;

        var syntaxTree = CSharpSyntaxTree.ParseText(
            GeneratedCSharpCompiler.GlobalUsingsPreamble + generatedCode);
        var compilation = CSharpCompilation.Create(
            $"RefinementIdentity_{Guid.NewGuid():N}",
            [syntaxTree],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        return Assembly.Load(stream.ToArray());
    }

    private static object? Invoke(Assembly assembly, string type, string method, params object?[] args)
    {
        var declaring = Assert.Single(assembly.GetTypes(), t => t.Name == type);
        var info = declaring.GetMethod(method)!;
        var target = info.IsStatic ? null : Activator.CreateInstance(declaring);
        return info.Invoke(target, args);
    }

    private static Exception Throws(Assembly assembly, string type, string method, params object?[] args)
        => Assert.Throws<TargetInvocationException>(() => Invoke(assembly, type, method, args))
            .InnerException!;

    private static void AssertDistinctRefinementError(CompilationResult result, string source, string target)
        => Assert.Contains(result.Diagnostics.Errors, d => d.Code == DiagnosticCode.TypeMismatch
            && (d.Message.Contains($"Cannot use {source} as {target}")
                || d.Message.Contains($"Cannot assign {source} to variable of type {target}")));

    // ───────────────────────── #1529: inferred locals carry the refinement ─────────────────────────

    private const string InferredLocals = """
          §F{f1:FromParam:pub} (Nat:a, i32:v) -> i32
            §B{~x} a
            §ASSIGN x v
            §R x
          §F{f2:WriteBack:pub} (Nat:a, i32:d) -> i32
            §B{~x} a
            §ASSIGN x (- x d)
            §R x
          §F{f3:Decrement:pub} (Nat:a) -> i32
            §B{~x} a
            (dec x)
            §R x
          §F{f4:GetNat:pub} (i32:v) -> Nat
            §R v
          §F{f5:FromCall:pub} (i32:seed, i32:v) -> i32
            §B{~x} §C{GetNat} §A seed §/C
            §ASSIGN x v
            §R x
          §F{f6:Chained:pub} (Nat:a, i32:v) -> i32
            §B{~x} a
            §B{~y} x
            §ASSIGN y v
            §R y
          §F{f7:FromBase:pub} (i32:a, i32:v) -> i32
            §B{~x} a
            §ASSIGN x v
            §R x
          §F{f8:FromInline:pub}
            §I{i32:a} | (>= # INT:0)
            §I{i32:v}
            §O{i32}
            §B{~x} a
            §ASSIGN x v
            §R x
          §F{f9:Rebound:pub} (Nat:a, i32:v) -> i32
            §B{~x:i32} INT:1
            §B{~x} a
            §ASSIGN x v
            §R x
          §CL{c1:Holder:pub}
            §MT{mt1:InMethod:pub} (Nat:a, i32:v) -> i32
              §E{}
              §B{~x} a
              §ASSIGN x v
              §R x
        """;

    [Theory]
    [InlineData("FromParam")]
    [InlineData("FromCall")]
    [InlineData("Chained")]
    [InlineData("Rebound")]
    public void Runtime_InferredLocal_RejectsAnInvalidWrite(string method)
    {
        var assembly = CompileAndLoad(InferredLocals, out _);

        // Positive control: a valid write still goes through.
        Assert.Equal(5, Invoke(assembly, "RtModule", method, 3, 5));
        // The bug: -5 used to be written silently. Now the Nat guard fires.
        var error = Throws(assembly, "RtModule", method, 3, -5);
        Assert.IsType<ArgumentOutOfRangeException>(error);
        Assert.Contains("Nat", error.Message);
    }

    [Fact]
    public void Runtime_InferredLocal_ArithmeticWriteBackAndDecrementAreGuarded()
    {
        // (The parser has no compound-assignment form; `§ASSIGN x (- x d)` is how source writes it.)
        var assembly = CompileAndLoad(InferredLocals, out _);

        Assert.Equal(2, Invoke(assembly, "RtModule", "WriteBack", 5, 3));
        Assert.IsType<ArgumentOutOfRangeException>(Throws(assembly, "RtModule", "WriteBack", 5, 6));

        Assert.Equal(0, Invoke(assembly, "RtModule", "Decrement", 1));
        Assert.IsType<ArgumentOutOfRangeException>(Throws(assembly, "RtModule", "Decrement", 0));
    }

    [Fact]
    public void Runtime_InferredLocalInAClassMethod_RejectsAnInvalidWrite()
    {
        // The type checker does not visit class members; the emitter still recognizes a direct
        // read of a named-refinement parameter.
        var assembly = CompileAndLoad(InferredLocals, out _);

        Assert.Equal(5, Invoke(assembly, "Holder", "InMethod", 3, 5));
        Assert.IsType<ArgumentOutOfRangeException>(Throws(assembly, "Holder", "InMethod", 3, -5));
    }

    [Fact]
    public void Runtime_LocalsWithoutANamedRefinement_AreUnconstrained()
    {
        // Negative controls. A local inferred from a plain i32, or from a parameter with only an
        // inline refinement (which the checker types as its base i32), has no refinement.
        var assembly = CompileAndLoad(InferredLocals, out _);

        Assert.Equal(-5, Invoke(assembly, "RtModule", "FromBase", 3, -5));
        Assert.Equal(-5, Invoke(assembly, "RtModule", "FromInline", 3, -5));
    }

    [Fact]
    public void Emit_InferredLocal_GetsTheSameAssignmentGuardAsAnAnnotatedOne()
    {
        var annotated = Compile("""
              §F{f1:Write:pub} (Nat:a, i32:v) -> i32
                §B{~x:Nat} a
                §ASSIGN x v
                §R x
            """);
        var inferred = Compile("""
              §F{f1:Write:pub} (Nat:a, i32:v) -> i32
                §B{~x} a
                §ASSIGN x v
                §R x
            """);
        Assert.False(annotated.HasErrors, Errors(annotated));
        Assert.False(inferred.HasErrors, Errors(inferred));

        const string guard = "if (!((__refinementCandidate0 >= 0))) throw new ArgumentOutOfRangeException(nameof(x), \"Value violates refinement type 'Nat'\");";
        Assert.Contains(guard, annotated.GeneratedCode);
        Assert.Contains(guard, inferred.GeneratedCode);
    }

    [Fact]
    public void TypeChecker_RecordsOnlyUnannotatedBindsInferredAsANamedRefinement()
    {
        var result = Compile("""
              §F{f1:Mix:pub} (Nat:a, i32:b) -> i32
                §B{~x} a
                §B{~y} b
                §B{~z:Nat} a
                §B{~w} (+ a INT:1)
                §R (+ x (+ y (+ z w)))
            """);
        Assert.False(result.HasErrors, Errors(result));

        var module = result.Ast!;
        var inferred = TypeChecker.InferUnannotatedRefinements(module);
        var entry = Assert.Single(inferred);
        Assert.Equal("x", Assert.IsType<Ast.BindStatementNode>(entry.Key).Name);
        Assert.Equal("Nat", entry.Value);
    }

    [Fact]
    public void Verify_InferredLocal_WriteHasASubtypeObligationThatIsNeverDischarged()
    {
        var options = new CompilationOptions
        {
            VerifyRefinements = true,
            EnableTypeChecking = true,
            ObligationPolicy = ObligationPolicy.Permissive,
        };
        var result = Compile("""
              §F{f1:Bad:priv} (Nat:a) -> i32
                §B{~x} a
                §ASSIGN x INT:-5
                §R x
            """, options);

        Assert.NotNull(options.ObligationResults);
        var obligation = Assert.Single(options.ObligationResults!.Obligations,
            o => o.Kind == ObligationKind.Subtype
                && o.ParameterName == "x"
                && o.Description.StartsWith("Assignment to 'x'", StringComparison.Ordinal));
        // -5 >= 0 is false for every input: never Discharged; Failed whenever Z3 can decide it.
        Assert.NotEqual(ObligationStatus.Discharged, obligation.Status);
        if (Verification.Z3.Z3ContextFactory.IsAvailable)
            Assert.Equal(ObligationStatus.Failed, obligation.Status);
        Assert.Contains("Value violates refinement type 'Nat'", result.GeneratedCode);
    }

    // ───────────────────────── #1530: named refinements are nominal ─────────────────────────

    [Fact]
    public void RefinedType_IdentityIncludesTheRefinementName()
    {
        var predicate = new Ast.IntLiteralNode(new Parsing.TextSpan(0, 0, 1, 1), 0);
        var nat = new RefinedType(PrimitiveType.Int, "#i32", predicate, "Nat");
        var pos = new RefinedType(PrimitiveType.Int, "#i32", predicate, "Pos");
        var natAgain = new RefinedType(PrimitiveType.Int, "#i32", predicate, "Nat");

        Assert.NotEqual<CalorType>(nat, pos);
        Assert.Equal<CalorType>(nat, natAgain);
        Assert.Equal(nat.GetHashCode(), natAgain.GetHashCode());
        Assert.Equal("Nat", nat.SurfaceName);
    }

    [Fact]
    public void Bind_DifferentNamedRefinement_IsRejected_SameOneIsAccepted()
    {
        var rejected = Compile("""
              §F{f1:Bad:pub} (Nat:a) -> i32
                §B{y:Pos} a
                §R y
            """);
        AssertDistinctRefinementError(rejected, "Nat", "Pos");

        var accepted = Compile("""
              §F{f1:Good:pub} (Nat:a) -> i32
                §B{y:Nat} a
                §R y
            """);
        Assert.False(accepted.HasErrors, Errors(accepted));
    }

    [Fact]
    public void Call_DifferentNamedRefinementArgument_IsRejected_SameOneIsAccepted()
    {
        var rejected = Compile("""
              §F{f1:TakeNat:pub} (Nat:n) -> i32
                §R n
              §F{f2:Bad:pub} (Pos:p) -> i32
                §R §C{TakeNat} §A p §/C
            """);
        AssertDistinctRefinementError(rejected, "Pos", "Nat");

        var accepted = Compile("""
              §F{f1:TakeNat:pub} (Nat:n) -> i32
                §R n
              §F{f2:Good:pub} (Nat:a) -> i32
                §R §C{TakeNat} §A a §/C
            """);
        Assert.False(accepted.HasErrors, Errors(accepted));
    }

    [Fact]
    public void Return_DifferentNamedRefinement_IsRejected_BaseValueStaysGuarded()
    {
        var rejected = Compile("""
              §F{f1:Bad:pub} (Pos:p) -> Nat
                §R p
            """);
        AssertDistinctRefinementError(rejected, "Pos", "Nat");

        // Unchanged: returning a plain base value into a refined return is accepted and guarded.
        var accepted = Compile("""
              §F{f1:Good:pub} (Nat:a, i32:v) -> Nat
                §R v
              §F{f2:Same:pub} (Nat:a) -> Nat
                §R a
            """);
        Assert.False(accepted.HasErrors, Errors(accepted));
        Assert.Contains("Return value violates refinement type 'Nat'", accepted.GeneratedCode);
    }

    [Fact]
    public void Assign_DifferentNamedRefinement_IsRejected_ArithmeticResultStaysGuarded()
    {
        var rejected = Compile("""
              §F{f1:Bad:pub} (Nat:a, Pos:p) -> i32
                §B{~n:Nat} a
                §ASSIGN n p
                §R n
            """);
        AssertDistinctRefinementError(rejected, "Pos", "Nat");

        var accepted = Compile("""
              §F{f1:Good:pub} (Nat:a, Pos:p) -> i32
                §B{~n:Nat} a
                §ASSIGN n (+ n p)
                §ASSIGN n a
                §R n
            """);
        Assert.False(accepted.HasErrors, Errors(accepted));
    }

    [Fact]
    public void Conditional_OfTwoDifferentRefinements_IsTheirBaseType()
    {
        // The join holds neither predicate, so it is the erased base: usable as i32, not as Nat.
        var asBase = Compile("""
              §F{f1:Pick:pub} (bool:c, Nat:a, Pos:p) -> i32
                §B{j} (? c a p)
                §R (+ j INT:1)
            """);
        Assert.False(asBase.HasErrors, Errors(asBase));

        var asNat = Compile("""
              §F{f1:Pick:pub} (bool:c, Nat:a, Pos:p) -> i32
                §B{j:Nat} (? c a p)
                §R j
            """);
        AssertDistinctRefinementError(asNat, "i32", "Nat");
    }

    [Fact]
    public void Runtime_ExplicitConversionThroughABaseTypedFunction_IsGuarded()
    {
        // The documented way to turn one refinement into another: a function that takes the base
        // type and returns the target refinement. Its return guard checks the value.
        var assembly = CompileAndLoad("""
              §F{f1:ToPos:pub} (i32:v) -> Pos
                §R v
              §F{f2:Convert:pub} (Nat:a) -> i32
                §B{p:Pos} §C{ToPos} §A a §/C
                §R p
            """, out var code);

        Assert.Equal(4, Invoke(assembly, "RtModule", "Convert", 4));
        var error = Throws(assembly, "RtModule", "Convert", 0);
        Assert.IsType<InvalidOperationException>(error);
        Assert.Contains("Pos", error.Message);
        // The bind guard on `p` is still emitted (runtime guards are unchanged by #1530).
        Assert.Contains("Value violates refinement type 'Pos'", code);
    }

    // ───────────────────────── Review round 1 findings ─────────────────────────

    [Fact]
    public void Rebind_ThatChangesTheNamedRefinement_IsRejected_SameRefinementIsAccepted()
    {
        var rejected = Compile("""
              §F{f1:Bad:pub} (Nat:a, Pos:p) -> i32
                §B{~x} a
                §B{~x} p
                §ASSIGN x INT:0
                §R x
            """);
        Assert.Contains(rejected.Diagnostics.Errors, d => d.Code == DiagnosticCode.TypeMismatch
            && d.Message.Contains("Cannot rebind 'x' of type Nat as Pos"));

        var accepted = Compile("""
              §F{f1:Good:pub} (Nat:a, Nat:b) -> i32
                §B{~x} a
                §B{~x} b
                §R x
            """);
        Assert.False(accepted.HasErrors, Errors(accepted));
    }

    [Fact]
    public void Runtime_RebindInAClassMethod_KeepsTheEstablishedRefinement()
    {
        // #1676: the type checker now visits class members, so this rebind (Nat, then Pos) is the
        // same Calor0202 as in a module function. With the checker off, the emitter keeps the
        // established typed-rebind semantics (ObligationTests
        // CSharpEmit_TypedRebind_PreservesPriorContract...): the rebind checks both refinements,
        // and later writes keep the first one, Nat.
        const string Body = """
              §CL{c1:Holder:pub}
                §MT{mt1:Rebind:pub} (Nat:a, Pos:p, i32:v) -> i32
                  §E{}
                  §B{~x} a
                  §B{~x} p
                  §ASSIGN x v
                  §R x
            """;
        Assert.Contains(Compile(Body).Diagnostics.Errors, d => d.Code == DiagnosticCode.TypeMismatch
            && d.Message.Contains("Cannot rebind 'x' of type Nat as Pos"));
        var assembly = CompileAndLoad(Body, out _, new CompilationOptions
        {
            ContractMode = ContractMode.Debug,
            EnableTypeChecking = false,
        });

        Assert.Equal(4, Invoke(assembly, "Holder", "Rebind", 1, 2, 4));
        Assert.Equal(0, Invoke(assembly, "Holder", "Rebind", 1, 2, 0));
        Assert.IsType<ArgumentOutOfRangeException>(Throws(assembly, "Holder", "Rebind", 1, 2, -1));
    }

    [Theory]
    [InlineData("§B{~x} a")]
    [InlineData("§B{~x:Nat} a")]
    public void OutArgument_IntoARefinedLocal_IsRejected(string bind)
    {
        var rejected = Compile($$"""
              §F{f1:Bad:pub} (Nat:a, str:s) -> i32
                {{bind}}
                §C{int.TryParse} §A s §A{out} x §/C
                §R x
            """);
        Assert.Contains(rejected.Diagnostics.Errors, d => d.Code == DiagnosticCode.RefinedVariableByReference
            && d.Message.Contains("'x'"));
    }

    [Fact]
    public void Runtime_OutArgumentThroughABaseTypedTemporary_IsGuardedOnAssignBack()
    {
        var assembly = CompileAndLoad("""
              §F{f1:Parse:pub} (Nat:a, str:s) -> i32
                §B{~x} a
                §B{~t:i32} INT:0
                §C{int.TryParse} §A s §A{out} t §/C
                §ASSIGN x t
                §R x
            """, out _);

        Assert.Equal(7, Invoke(assembly, "RtModule", "Parse", 1, "7"));
        Assert.IsType<ArgumentOutOfRangeException>(Throws(assembly, "RtModule", "Parse", 1, "-5"));
    }

    [Fact]
    public void Runtime_LambdaParameterTypedByARefinedDelegate_IsGuarded()
    {
        var assembly = CompileAndLoad("""
              §DEL{d1:Mapper:pub}
                §I{Nat:p}
                §O{i32}
              §F{f1:Apply:pub} (Nat:a) -> i32
                §B{f:Mapper} §LAM{l1:p}
                  §ASSIGN p (- p INT:10)
                  §R p
                §/LAM{l1}
                §R §C{f} §A a §/C
            """, out _);

        Assert.Equal(10, Invoke(assembly, "RtModule", "Apply", 20));
        Assert.IsType<ArgumentOutOfRangeException>(Throws(assembly, "RtModule", "Apply", 5));
    }

    [Fact]
    public void TypePattern_OnARefinementType_IsRejected_BaseTypePatternIsAccepted()
    {
        var rejected = Compile("""
              §F{f1:Bad:pub} (Nat:a) -> i32
                §IF{i1} (is a Pos p)
                  §R p
                §R INT:1
            """);
        Assert.Contains(rejected.Diagnostics.Errors, d => d.Code == DiagnosticCode.TypeMismatch
            && d.Message.Contains("A type pattern cannot test the refinement type Pos"));

        var accepted = Compile("""
              §F{f1:Good:pub} (Nat:a) -> i32
                §IF{i1} (is a i32 p)
                  §R p
                §R INT:1
            """);
        Assert.False(accepted.HasErrors, Errors(accepted));
    }

    [Theory]
    [InlineData("§B{~x} a")]
    [InlineData("§B{~x:i32} INT:0")]
    public void Assign_NestedCallWithADifferentRefinement_IsRejected(string bind)
    {
        // The call is nested in an assignment's right-hand side, which the checker used to skip.
        var rejected = Compile($$"""
              §F{f1:TakePos:pub} (Pos:p) -> i32
                §R p
              §F{f2:Bad:pub} (Nat:a) -> i32
                {{bind}}
                §ASSIGN x §C{TakePos} §A a §/C
                §R x
            """);
        AssertDistinctRefinementError(rejected, "Nat", "Pos");
    }

    // ───────────────────────── Review round 2 findings ─────────────────────────

    private static Assembly CompileAndLoadVerified(string body, bool enableTypeChecking = true)
    {
        var options = new CompilationOptions
        {
            ContractMode = ContractMode.Debug,
            EnableTypeChecking = enableTypeChecking,
            VerifyRefinements = true,
            ObligationPolicy = ObligationPolicy.Permissive,
        };
        var result = Compile(body, options);
        Assert.False(result.HasErrors, Errors(result));
        var syntaxTree = CSharpSyntaxTree.ParseText(
            GeneratedCSharpCompiler.GlobalUsingsPreamble + result.GeneratedCode);
        var compilation = CSharpCompilation.Create(
            $"RefinementIdentityVerified_{Guid.NewGuid():N}",
            [syntaxTree],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        return Assembly.Load(stream.ToArray());
    }

    [Fact]
    public void Runtime_RejectedOverloadDoesNotLeaveItsRefinementOnALambdaParameter()
    {
        // Overload trials type the lambda against each candidate. Only the selected candidate's
        // typing (PosMapper, so p: Pos) may decide the guard; NatMapper's must be rolled back.
        var assembly = CompileAndLoad("""
              §DEL{d1:PosMapper:pub}
                §I{Pos:p}
                §O{i32}
              §DEL{d2:NatMapper:pub}
                §I{Nat:p}
                §O{i32}
              §F{f1:Pick:pub} (PosMapper:fn, Pos:tag) -> i32
                §R §C{fn} §A tag §/C
              §F{f2:Pick:pub} (NatMapper:fn, str:tag) -> i32
                §R INT:0
              §F{f3:Use:pub} (Pos:a, i32:v) -> i32
                §R §C{Pick}
                  §A §LAM{l1:p}
                    §ASSIGN p v
                    §R p
                  §/LAM{l1}
                  §A a
                §/C
            """, out _, new CompilationOptions
            {
                ContractMode = ContractMode.Debug,
                EnableTypeChecking = true,
                // Invoking a delegate parameter is an undeclared 'unknown' effect; effects are
                // not what this test is about.
                EnforceEffects = false,
            });

        Assert.Equal(3, Invoke(assembly, "RtModule", "Use", 1, 3));
        var error = Throws(assembly, "RtModule", "Use", 1, 0);
        Assert.IsType<ArgumentOutOfRangeException>(error);
        Assert.Contains("Pos", error.Message);
    }

    [Fact]
    public void Runtime_Verified_ClassMethodRebind_DoesNotElideTheEstablishedGuard()
    {
        // The emitter keeps x: Pos across the rebind, so y (inferred from x) is Pos. The
        // obligation generator must not prove a Nat obligation and drop the Pos guard.
        // #1676: the checker now visits class members and rejects the rebind (Calor0202), so the
        // guard is exercised with the checker off.
        const string Body = """
              §CL{c1:Holder:pub}
                §MT{mt1:Run:pub} (Pos:x, Nat:a, i32:v) -> i32
                  §E{}
                  §B{~x} a
                  §B{~y} x
                  §ASSIGN y v
                  §R y
            """;
        Assert.Contains(Compile(Body).Diagnostics.Errors, d => d.Code == DiagnosticCode.TypeMismatch
            && d.Message.Contains("Cannot rebind 'x' of type Pos as Nat"));
        var assembly = CompileAndLoadVerified(Body, enableTypeChecking: false);

        Assert.Equal(5, Invoke(assembly, "Holder", "Run", 1, 2, 5));
        Assert.IsType<ArgumentOutOfRangeException>(Throws(assembly, "Holder", "Run", 1, 2, 0));
    }

    [Fact]
    public void Runtime_Verified_LambdaParameterShadowingARefinedLocal_KeepsItsOwnGuard()
    {
        var assembly = CompileAndLoadVerified("""
              §DEL{d1:Mapper:pub}
                §I{Pos:p}
                §O{i32}
              §F{f1:Run:pub} (Nat:a, Pos:b, i32:v) -> i32
                §IF{i1} (> a INT:0)
                  §B{n:Nat} a
                §B{fn:Mapper} §LAM{l1:n}
                  §ASSIGN n v
                  §R n
                §/LAM{l1}
                §R §C{fn} §A b §/C
            """);

        Assert.Equal(4, Invoke(assembly, "RtModule", "Run", 1, 2, 4));
        Assert.IsType<ArgumentOutOfRangeException>(Throws(assembly, "RtModule", "Run", 1, 2, 0));
    }

    [Fact]
    public void Runtime_LambdaAssignedToARefinedDelegateVariable_IsGuarded()
    {
        var assembly = CompileAndLoad("""
              §DEL{d1:Mapper:pub}
                §I{Pos:p}
                §O{i32}
              §F{f1:Run:pub} (Pos:a, i32:v) -> i32
                §B{~fn:Mapper} §LAM{l1:p}
                  §R p
                §/LAM{l1}
                §ASSIGN fn §LAM{l2:p}
                  §ASSIGN p v
                  §R p
                §/LAM{l2}
                §R §C{fn} §A a §/C
            """, out _);

        Assert.Equal(6, Invoke(assembly, "RtModule", "Run", 1, 6));
        Assert.IsType<ArgumentOutOfRangeException>(Throws(assembly, "RtModule", "Run", 1, 0));
    }

    [Fact]
    public void Assign_NestedDelegateCallWithADifferentRefinement_IsRejected()
    {
        var rejected = Compile("""
              §DEL{d1:Mapper:pub}
                §I{Pos:p}
                §O{i32}
              §F{f1:Run:pub} (Nat:a) -> i32
                §B{fn:Mapper} §LAM{l1:p}
                  §R p
                §/LAM{l1}
                §B{~x:i32} INT:0
                §ASSIGN x §C{fn} §A a §/C
                §R x
            """);
        AssertDistinctRefinementError(rejected, "Nat", "Pos");
    }

    [Fact]
    public void Assign_NestedNamedArgumentWithADifferentRefinement_IsRejected()
    {
        var rejected = Compile("""
              §F{f1:TakePos:pub} (Pos:p) -> i32
                §R p
              §F{f2:Run:pub} (Nat:a) -> i32
                §B{~x:i32} INT:0
                §ASSIGN x §C{TakePos} §A[p] a §/C
                §R x
            """);
        AssertDistinctRefinementError(rejected, "Nat", "Pos");
    }

    [Fact]
    public void Rebind_ThroughABaseTypedIntermediate_StillCannotChangeTheRefinement()
    {
        var rejected = Compile("""
              §F{f1:Bad:pub} (Nat:a, Pos:p) -> i32
                §B{~x} a
                §B{~x:i32} a
                §B{~x} p
                §ASSIGN x INT:0
                §R x
            """);
        Assert.Contains(rejected.Diagnostics.Errors, d => d.Code == DiagnosticCode.TypeMismatch
            && d.Message.Contains("Cannot rebind 'x' of type Nat as Pos"));
    }

    [Fact]
    public void Rebind_OfAnInlineRefinedParameterAsANamedRefinement_IsRejected()
    {
        var rejected = Compile("""
              §F{f1:Bad:pub}
                §I{i32:a} | (>= # INT:0)
                §I{Pos:p}
                §O{i32}
                §B{~a} p
                §R a
            """);
        Assert.Contains(rejected.Diagnostics.Errors, d => d.Code == DiagnosticCode.TypeMismatch
            && d.Message.Contains("Cannot rebind parameter 'a', which has an inline refinement, as Pos"));
    }

    [Fact]
    public void Runtime_MatchOfBaseValuesAssignedToARefinedLocal_IsAcceptedAndGuarded()
    {
        var assembly = CompileAndLoad("""
              §F{f1:Run:pub} (Nat:a, i32:b) -> i32
                §B{~x} a
                §ASSIGN x §W{w1} b
                  §K INT:0 → INT:1
                  §K _ → b
                §R x
            """, out _);

        Assert.Equal(1, Invoke(assembly, "RtModule", "Run", 3, 0));
        Assert.Equal(7, Invoke(assembly, "RtModule", "Run", 3, 7));
        Assert.IsType<ArgumentOutOfRangeException>(Throws(assembly, "RtModule", "Run", 3, -2));
    }

    [Fact]
    public void Bind_DifferentNamedRefinementInsideATryBody_IsStillRejected()
    {
        // The checker's flow cache drops ordinary diagnostics inside structural statements;
        // refinement-soundness errors are preserved.
        var rejected = Compile("""
              §F{f1:Bad:pub} (Nat:a) -> i32
                §TR{t1}
                  §B{y:Pos} a
                  §R y
                §CA{Exception:ex}
                  §R INT:0
            """);
        AssertDistinctRefinementError(rejected, "Nat", "Pos");
    }

    // ───────────────────────── Review round 3 findings ─────────────────────────

    [Fact]
    public void Assign_LambdaReturningADifferentRefinement_IsRejected_SameRefinementIsAccepted()
    {
        var rejected = Compile("""
              §DEL{d1:Factory:pub}
                §O{Pos}
              §F{f1:Run:pub} (Nat:a, Pos:p) -> i32
                §B{~fn:Factory} §LAM{l1}
                  §R p
                §/LAM{l1}
                §ASSIGN fn §LAM{l2}
                  §R a
                §/LAM{l2}
                §R §C{fn} §/C
            """);
        AssertDistinctRefinementError(rejected, "Nat", "Pos");

        var accepted = Compile("""
              §DEL{d1:Factory:pub}
                §O{Pos}
              §F{f1:Run:pub} (Nat:a, Pos:p) -> i32
                §B{~fn:Factory} §LAM{l1}
                  §R p
                §/LAM{l1}
                §ASSIGN fn §LAM{l2}
                  §R p
                §/LAM{l2}
                §R §C{fn} §/C
            """);
        Assert.False(accepted.HasErrors, Errors(accepted));
    }

    [Fact]
    public void Return_DifferentRefinementFromACallInsideATryBody_IsRejected()
    {
        var rejected = Compile("""
              §F{f1:GetPos:pub} () -> Pos
                §R INT:1
              §F{f2:Bad:pub} () -> Nat
                §TR{t1}
                  §R §C{GetPos} §/C
                §CA{Exception:ex}
                  §R INT:0
            """);
        AssertDistinctRefinementError(rejected, "Pos", "Nat");
    }

    [Fact]
    public void RejectedOverloadInsideATryBody_DoesNotLeakItsDiagnostics()
    {
        // Typing the lambda against the rejected NatMapper would make `§B{q:Pos} p` a mismatch;
        // that speculative diagnostic must not survive the try body's flow cache.
        var result = Compile("""
              §DEL{d1:PosMapper:pub}
                §I{Pos:p}
                §O{i32}
              §DEL{d2:NatMapper:pub}
                §I{Nat:p}
                §O{i32}
              §F{f1:Pick:pub} (PosMapper:fn, Pos:tag) -> i32
                §R INT:1
              §F{f2:Pick:pub} (NatMapper:fn, str:tag) -> i32
                §R INT:0
              §F{f3:Use:pub} (Pos:a) -> i32
                §TR{t1}
                  §R §C{Pick}
                    §A §LAM{l1:p}
                      §B{q:Pos} p
                      §R q
                    §/LAM{l1}
                    §A a
                  §/C
                §CA{Exception:ex}
                  §R INT:0
            """, new CompilationOptions
            {
                ContractMode = ContractMode.Debug,
                EnableTypeChecking = true,
                EnforceEffects = false,
            });
        Assert.False(result.HasErrors, Errors(result));
    }

    [Fact]
    public void Rebind_WithABaseAnnotationAndADifferentRefinementInitializer_IsRejected()
    {
        var rejected = Compile("""
              §F{f1:Run:pub} (Nat:a, Pos:p) -> i32
                §B{~x} a
                §B{~x:i32} p
                §R x
            """);
        Assert.Contains(rejected.Diagnostics.Errors, d => d.Code == DiagnosticCode.TypeMismatch
            && d.Message.Contains("Cannot rebind 'x' of type Nat as Pos"));

        var accepted = Compile("""
              §F{f1:Run:pub} (Nat:a, Nat:b) -> i32
                §B{~x} a
                §B{~x:i32} b
                §R x
            """);
        Assert.False(accepted.HasErrors, Errors(accepted));
    }

    [Fact]
    public void LambdaParameter_ShadowingAnInlineRefinedParameter_CanBeRebound()
    {
        var accepted = Compile("""
              §DEL{d1:Mapper:pub}
                §I{i32:a}
                §O{i32}
              §F{f1:Run:pub}
                §I{i32:a} | (>= # INT:0)
                §I{Pos:p}
                §O{i32}
                §B{fn:Mapper} §LAM{l1:a}
                  §B{~a} p
                  §R a
                §/LAM{l1}
                §R §C{fn} §A INT:1 §/C
            """);
        Assert.False(accepted.HasErrors, Errors(accepted));
    }
}
