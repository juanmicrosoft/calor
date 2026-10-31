using System.Reflection;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Calor.Compiler.TypeChecking;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests.Analysis;

/// <summary>
/// #1676: four places where the refinement checks of #1529/#1530 did not reach. Mostly a runtime
/// refinement guard already fired and only the static <c>Calor0202</c> was missing; a lambda
/// passed inside <c>§P</c> also had no runtime guard on its parameter.
///
/// <list type="number">
/// <item><c>§P</c> statements were never type-checked, so a wrong-refinement call inside one was
/// silent, and a lambda passed inside one got no refinement on its parameter.</item>
/// <item><c>§ASSIGN x &lt;match&gt;</c> inferred the match against the erased target, so a
/// <c>Pos</c> arm written into a <c>Nat</c> local was accepted.</item>
/// <item>Inside a <c>§TR</c> body, a match-arm mismatch between two named refinements was an
/// ordinary diagnostic, which the try statement's flow cache drops.</item>
/// <item>The checker did not visit class members (methods, constructors, accessors,
/// operators).</item>
/// </list>
/// Each fix has negative controls (the error), positive controls (the same refinement or a base
/// value is still accepted), and compiled runs showing the runtime guards still fire.
/// </summary>
public class RefinementStaticGapsTests
{
    private const string Header = """
        §M{Rt}
          §RTYPE{Nat:i32} (>= # INT:0)
          §RTYPE{Pos:i32} (> # INT:0)
          §F{TakePos:pub} (Pos:p) -> i32
            §R p
          §F{TakeNat:pub} (Nat:n) -> i32
            §R n
        """;

    private static CompilationOptions Options(bool typeCheck = true) => new()
    {
        ContractMode = ContractMode.Debug,
        EnableTypeChecking = typeCheck,
        // Invoking a delegate parameter is an undeclared 'unknown' effect, and §P is console
        // output; effects are not what these tests are about.
        EnforceEffects = false,
    };

    private static CompilationResult Compile(string body, bool typeCheck = true)
        => Program.Compile(Header + "\n" + body, "refinement-static-gaps.calr", Options(typeCheck));

    private static string Errors(CompilationResult result)
        => string.Join("\n", result.Diagnostics.Errors.Select(d => $"{d.Code}: {d.Message}"));

    private static void AssertDistinct(CompilationResult result, string source, string target, string position)
        => Assert.Contains(result.Diagnostics.Errors, d => d.Code == DiagnosticCode.TypeMismatch
            && d.Message.Contains($"Cannot use {source} as {target} in {position}"));

    private static void AssertNoRefinementError(CompilationResult result)
        => Assert.False(result.HasErrors, Errors(result));

    private static Assembly CompileAndLoad(string body, bool typeCheck = true)
    {
        var result = Compile(body, typeCheck);
        Assert.False(result.HasErrors, Errors(result));
        var syntaxTree = CSharpSyntaxTree.ParseText(
            GeneratedCSharpCompiler.GlobalUsingsPreamble + result.GeneratedCode);
        var compilation = CSharpCompilation.Create(
            $"RefinementStaticGaps_{Guid.NewGuid():N}",
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

    // ───────────────────────── 1. §P statements ─────────────────────────

    [Fact]
    public void Print_CallWithADifferentRefinement_IsRejected()
    {
        var result = Compile("""
              §F{Main:pub} (Nat:n) -> void
                §P §C{TakePos} §A n §/C
            """);
        AssertDistinct(result, "Nat", "Pos", "argument 1 of 'TakePos'");
    }

    [Fact]
    public void Print_CallWithTheSameRefinementOrABaseValue_IsAccepted()
    {
        AssertNoRefinementError(Compile("""
              §F{Main:pub} (Pos:p, i32:k) -> void
                §P §C{TakePos} §A p §/C
                §P (+ p k)
                §P "text"
            """));
    }

    [Fact]
    public void Print_NestedWrongRefinementInsideAnInterpolation_IsRejected()
    {
        var result = Compile("""
              §F{Main:pub} (Nat:n) -> void
                §P (+ STR:"v=" §C{TakePos} §A n §/C)
            """);
        AssertDistinct(result, "Nat", "Pos", "argument 1 of 'TakePos'");
    }

    private const string PrintLambda = """
          §DEL{Mapper}
            §I{Nat:p}
            §O{i32}
          §F{Apply:pub} (Mapper:fn, Nat:a) -> i32
            §R §C{fn} §A a §/C
          §F{Run:pub} (Nat:a) -> void
            §P §C{Apply}
              §A §LAM{p}
                §ASSIGN p (- p INT:10)
                §R p
              §/LAM
              §A a
            §/C
        """;

    [Fact]
    public void Runtime_LambdaPassedInsidePrint_HasItsParameterGuarded()
    {
        var assembly = CompileAndLoad(PrintLambda);

        Invoke(assembly, "RtModule", "Run", 20);
        var error = Throws(assembly, "RtModule", "Run", 5);
        Assert.IsType<ArgumentOutOfRangeException>(error);
        Assert.Contains("Nat", error.Message);
    }

    [Fact]
    public void Runtime_LambdaPassedInsidePrint_IsGuardedUnderNoTypeCheckToo()
    {
        // The emitter takes inferred refinements from the checker whether or not its
        // diagnostics are enabled (#1529), so --no-type-check keeps the guard.
        var assembly = CompileAndLoad(PrintLambda, typeCheck: false);

        Assert.IsType<ArgumentOutOfRangeException>(Throws(assembly, "RtModule", "Run", 5));
    }

    [Fact]
    public void Print_LambdaReturningADifferentRefinement_IsRejected()
    {
        var result = Compile("""
              §DEL{ToPos}
                §I{i32:v}
                §O{Pos}
              §F{Apply:pub} (ToPos:fn) -> i32
                §R §C{fn} §A INT:1 §/C
              §F{Run:pub} (Nat:a) -> void
                §P §C{Apply}
                  §A §LAM{v}
                    §R a
                  §/LAM
                §/C
            """);
        AssertDistinct(result, "Nat", "Pos", "a return");
    }

    [Fact]
    public void Print_InAModuleWithoutRefinements_StillCompiles()
    {
        var result = Program.Compile("""
            §M{P}
              §F{Twice:pub} (i32:x) -> i32
                §R (* x INT:2)
              §F{Main:pub} () -> void
                §E{cw}
                §P §C{Twice} §A INT:21 §/C
                §P (+ STR:"a" STR:"b")
            """, "print-plain.calr", new CompilationOptions { EnableTypeChecking = true });
        Assert.False(result.HasErrors, Errors(result));
    }

    // ───────────────────────── 2. §ASSIGN x <match> ─────────────────────────

    [Fact]
    public void AssignMatch_EveryArmADifferentRefinement_IsRejected()
    {
        var result = Compile("""
              §F{Run:pub} (Pos:p, Nat:seed, i32:k) -> i32
                §B{~x:Nat} seed
                §ASSIGN x §W k
                  §K INT:1 → p
                  §K _ → p
                §R x
            """);
        AssertDistinct(result, "Pos", "Nat", "a match arm of the assignment to 'x'");
    }

    [Fact]
    public void AssignMatch_OneArmADifferentRefinement_IsRejected()
    {
        var result = Compile("""
              §F{Run:pub} (Pos:p, Nat:seed, i32:k) -> i32
                §B{~x:Nat} seed
                §ASSIGN x §W k
                  §K INT:1 → p
                  §K _ → k
                §R x
            """);
        AssertDistinct(result, "Pos", "Nat", "a match arm of the assignment to 'x'");
    }

    [Fact]
    public void AssignMatch_NestedMatchArmADifferentRefinement_IsRejected()
    {
        var result = Compile("""
              §F{Run:pub} (Pos:p, Nat:seed, i32:k) -> i32
                §B{~x:Nat} seed
                §ASSIGN x §W k
                  §K INT:1 → §W k
                    §K INT:2 → p
                    §K _ → seed
                  §K _ → seed
                §R x
            """);
        AssertDistinct(result, "Pos", "Nat", "a match arm of the assignment to 'x'");
    }

    [Fact]
    public void AssignMatch_SameRefinementOrBaseArms_IsAccepted()
    {
        AssertNoRefinementError(Compile("""
              §F{Same:pub} (Nat:n, Nat:seed, i32:k) -> i32
                §B{~x:Nat} seed
                §ASSIGN x §W k
                  §K INT:1 → n
                  §K _ → seed
                §R x
              §F{Base:pub} (Nat:seed, i32:k) -> i32
                §B{~x:Nat} seed
                §ASSIGN x §W k
                  §K INT:1 → (- k INT:1)
                  §K _ → k
                §R x
              §F{Plain:pub} (Pos:p, i32:k) -> i32
                §B{~y:i32} k
                §ASSIGN y §W k
                  §K INT:1 → p
                  §K _ → k
                §R y
            """));
    }

    [Fact]
    public void Runtime_AssignMatchOfBaseArms_IsStillGuarded()
    {
        var assembly = CompileAndLoad("""
              §F{Run:pub} (Nat:seed, i32:k) -> i32
                §B{~x:Nat} seed
                §ASSIGN x §W k
                  §K INT:1 → INT:7
                  §K _ → k
                §R x
            """);

        Assert.Equal(7, Invoke(assembly, "RtModule", "Run", 0, 1));
        Assert.Equal(4, Invoke(assembly, "RtModule", "Run", 0, 4));
        Assert.IsType<ArgumentOutOfRangeException>(Throws(assembly, "RtModule", "Run", 0, -3));
    }

    // ───────────────────────── 3. §TR bodies ─────────────────────────

    [Fact]
    public void Try_BindingAMatchOfADifferentRefinement_IsRejected()
    {
        var result = Compile("""
              §F{Run:pub} (Pos:p, i32:k) -> i32
                §TR
                  §B{x:Nat} §W k
                    §K INT:1 → p
                    §K _ → p
                  §R x
                §CA{Exception:e}
                  §R INT:0
                §R INT:0
            """);
        AssertDistinct(result, "Pos", "Nat", "a match arm");
    }

    [Fact]
    public void Try_ReturningAMatchOfADifferentRefinement_IsRejected()
    {
        var result = Compile("""
              §F{Run:pub} (Pos:p, i32:k) -> Nat
                §TR
                  §R §W k
                    §K INT:1 → p
                    §K _ → p
                §CA{Exception:e}
                  §R §C{Make} §A INT:0 §/C
                §R §C{Make} §A INT:0 §/C
              §F{Make:pub} (i32:v) -> Nat
                §R v
            """);
        AssertDistinct(result, "Pos", "Nat", "a match arm");
    }

    [Fact]
    public void Try_ForeachAndUsingBodies_ReportTheMatchArmMismatch()
    {
        var result = Compile("""
              §F{Run:pub} (Pos:p, i32:k, List<i32>:xs) -> i32
                §EACH{v:i32} xs
                  §B{x:Nat} §W k
                    §K INT:1 → p
                    §K _ → p
                §R INT:0
            """);
        AssertDistinct(result, "Pos", "Nat", "a match arm");
    }

    [Fact]
    public void Try_MatchOfTheSameRefinement_IsAccepted()
    {
        AssertNoRefinementError(Compile("""
              §F{Run:pub} (Nat:n, i32:k) -> i32
                §TR
                  §B{x:Nat} §W k
                    §K INT:1 → n
                    §K _ → n
                  §R x
                §CA{Exception:e}
                  §R INT:0
                §R INT:0
            """));
    }

    [Fact]
    public void Try_OrdinaryCheckerDiagnosticsAreStillLeftToLaterPasses()
    {
        // The try fix is narrow: only refinement-soundness errors survive the flow cache. An
        // ordinary checker error in a try body (here an i32 match arm bound as str) is still
        // dropped by the checker, exactly as before, while the same code outside a try reports.
        static IReadOnlyList<Calor.Compiler.Diagnostics.Diagnostic> Check(string body)
        {
            var source = Header + "\n" + body;
            var parseDiagnostics = new DiagnosticBag();
            var tokens = new Lexer(source, parseDiagnostics).TokenizeAll();
            var module = new Parser(tokens, parseDiagnostics).Parse();
            Assert.False(parseDiagnostics.HasErrors,
                string.Join("\n", parseDiagnostics.Errors.Select(d => d.Message)));
            var diagnostics = new DiagnosticBag();
            new TypeChecker(diagnostics).Check(module);
            return diagnostics.ToList();
        }

        const string Arms = """
                    §K INT:1 → k
                    §K _ → k
            """;
        var inTry = Check($$"""
              §F{Run:pub} (i32:k) -> void
                §TR
                  §B{s:str} §W k
            {{Arms}}
                §CA{Exception:e}
                  §P "caught"
            """);
        var outside = Check($$"""
              §F{Run:pub} (i32:k) -> void
                §B{s:str} §W k
            {{Arms}}
            """);

        Assert.DoesNotContain(inTry, d => d.Message.Contains("Match arm type i32 is not assignable to str"));
        Assert.Contains(outside, d => d.Message.Contains("Match arm type i32 is not assignable to str"));
    }

    [Fact]
    public void Runtime_TryBodyBaseArmBoundAsRefinement_IsStillGuarded()
    {
        var assembly = CompileAndLoad("""
              §F{Run:pub} (Nat:seed, i32:k) -> i32
                §TR
                  §B{~x:Nat} seed
                  §ASSIGN x §W k
                    §K INT:1 → INT:5
                    §K _ → k
                  §R x
                §FI
                  §B{done} INT:1
                §R INT:0
            """);

        Assert.Equal(5, Invoke(assembly, "RtModule", "Run", 0, 1));
        Assert.IsType<ArgumentOutOfRangeException>(Throws(assembly, "RtModule", "Run", 0, -2));
    }

    // ───────────────────────── 4. Class members ─────────────────────────

    [Fact]
    public void ClassMethod_BindCallReturnAndAssign_AreChecked()
    {
        var result = Compile("""
              §CL{Holder:pub}
                §FLD{Pos:_p:priv}
                §MT{Bind:pub} (Nat:n) -> i32
                  §B{y:Pos} n
                  §R y
                §MT{Call:pub} (Nat:n) -> i32
                  §R §C{TakePos} §A n §/C
                §MT{Ret:pub} (Nat:n) -> Pos
                  §R n
                §MT{Set:pub} (Nat:n) -> void
                  §ASSIGN _p n
            """);
        AssertDistinct(result, "Nat", "Pos", "the binding of 'y'");
        AssertDistinct(result, "Nat", "Pos", "argument 1 of 'TakePos'");
        AssertDistinct(result, "Nat", "Pos", "a return");
        AssertDistinct(result, "Nat", "Pos", "assignment to '_p'");
    }

    [Fact]
    public void ClassConstructorAccessorsAndOperators_AreChecked()
    {
        var ctor = Compile("""
              §CL{Holder:pub}
                §FLD{Pos:_p:priv}
                §CTOR{pub} (Nat:n)
                  §ASSIGN _p n
            """);
        AssertDistinct(ctor, "Nat", "Pos", "assignment to '_p'");

        var getter = Compile("""
              §CL{Holder:pub}
                §FLD{Pos:_p:priv}
                §PROP{P:Nat:pub}
                  §GET{pub}
                    §R _p
            """);
        AssertDistinct(getter, "Pos", "Nat", "a return");

        var setter = Compile("""
              §CL{Holder:pub}
                §FLD{Pos:_p:priv}
                §PROP{P:Nat:pub}
                  §GET{pub}
                    §R INT:0
                  §SET{pub}
                    §ASSIGN _p value
            """);
        // The emitter does not guard a setter's 'value', so the checker types it as its base
        // type (i32) and claims no refinement for it: writing it into a Pos field is a base write.
        AssertNoRefinementError(setter);

        var op = Compile("""
              §CL{Holder:pub}
                §OP{+:pub} (Holder:a, Nat:b) -> Pos
                  §R b
            """);
        AssertDistinct(op, "Nat", "Pos", "a return");
    }

    [Fact]
    public void ClassMember_MatchArmAndTryBody_AreChecked()
    {
        var result = Compile("""
              §CL{Holder:pub}
                §MT{Run:pub} (Pos:p, Nat:seed, i32:k) -> i32
                  §B{~x:Nat} seed
                  §TR
                    §ASSIGN x §W k
                      §K INT:1 → p
                      §K _ → p
                  §CA{Exception:e}
                    §R INT:0
                  §R x
            """);
        AssertDistinct(result, "Pos", "Nat", "a match arm of the assignment to 'x'");
    }

    [Fact]
    public void ClassMember_InferredLocalRebindChangingTheRefinement_IsRejected()
    {
        var result = Compile("""
              §CL{Holder:pub}
                §MT{Run:pub} (Pos:x, Nat:a) -> i32
                  §B{~x} a
                  §R x
            """);
        Assert.Contains(result.Diagnostics.Errors, d => d.Code == DiagnosticCode.TypeMismatch
            && d.Message.Contains("Cannot rebind 'x' of type Pos as Nat"));
    }

    [Fact]
    public void ClassMember_SameRefinementsFieldsAndUnmodeledNames_AreAccepted()
    {
        // Positive controls: the same refinement and base values pass; fields, properties and
        // the fields of a module base class are typed; a name the checker cannot see (a member
        // of an external base class) is left to Roslyn; a method shadows a same-named module
        // function, as it does in C#.
        AssertNoRefinementError(Compile("""
              §CL{BaseHolder:pub}
                §FLD{Nat:_base:prot}
              §CL{Holder:pub}
                §EXT{BaseHolder}
                §FLD{Pos:_p:priv}
                §PROP{P:Pos:pub}
                  §GET{pub}
                    §R _p
                  §SET{pub}
                    §ASSIGN _p value
                §CTOR{pub} (Pos:p)
                  §ASSIGN _p p
                §MT{TakePos:pub} (Nat:n) -> i32
                  §R n
                §MT{Use:pub} (Nat:n, i32:k) -> i32
                  §B{a:Nat} _base
                  §B{b:Pos} _p
                  §B{~c:Nat} n
                  §ASSIGN c (+ k INT:1)
                  §R §C{TakePos} §A n §/C
                §MT{Generic<T>:pub} (T:v) -> T
                  §R v
            """));
    }

    [Fact]
    public void ClassMember_UnknownNameIsLeftToRoslyn()
    {
        // The checker stays silent (it has no member table); Roslyn still rejects the name.
        var result = Compile("""
              §CL{Holder:pub}
                §MT{Run:pub} () -> i32
                  §R NoSuchMember
            """);
        Assert.DoesNotContain(result.Diagnostics.Errors, d => d.Code == DiagnosticCode.UndefinedReference);
        Assert.Contains(result.Diagnostics.Errors, d => d.Message.Contains("NoSuchMember"));
    }

    [Fact]
    public void Runtime_ClassMemberGuards_StillFire()
    {
        var assembly = CompileAndLoad("""
              §CL{Holder:pub}
                §FLD{Nat:_n:priv}
                §MT{Write:pub} (i32:v) -> i32
                  §ASSIGN _n v
                  §R _n
                §MT{Local:pub} (Nat:a, i32:v) -> i32
                  §B{~x} a
                  §ASSIGN x v
                  §R x
            """);

        Assert.Equal(3, Invoke(assembly, "Holder", "Write", 3));
        Assert.IsType<ArgumentOutOfRangeException>(Throws(assembly, "Holder", "Local", 1, -1));
        Assert.Equal(2, Invoke(assembly, "Holder", "Local", 1, 2));
    }

    [Fact]
    public void Runtime_MutableBindNamingAField_DeclaresANewLocal_NotARebind()
    {
        // The emitter declares `var _p = n;` (a local shadowing the field), so the rebind rules,
        // which reject changing a refinement, must not treat the Pos field as the target.
        var assembly = CompileAndLoad("""
              §CL{Holder:pub}
                §FLD{Pos:_p:priv}
                §MT{Run:pub} (Nat:n, i32:v) -> i32
                  §B{~_p} n
                  §ASSIGN _p v
                  §R _p
            """);

        Assert.Equal(0, Invoke(assembly, "Holder", "Run", 1, 0));
        Assert.IsType<ArgumentOutOfRangeException>(Throws(assembly, "Holder", "Run", 1, -1));
    }

    [Fact]
    public void Runtime_LocalInferredFromARefinedField_IsGuarded()
    {
        var assembly = CompileAndLoad("""
              §CL{Holder:pub}
                §FLD{Nat:_n:priv}
                §MT{Run:pub} (i32:v) -> i32
                  §B{~x} _n
                  §ASSIGN x v
                  §R x
            """);

        Assert.Equal(2, Invoke(assembly, "Holder", "Run", 2));
        Assert.IsType<ArgumentOutOfRangeException>(Throws(assembly, "Holder", "Run", -1));
    }

    // ───────────────────────── Review round 1 ─────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Print_MemberAccessTheCheckerCannotType_StillCompiles(bool withRefinements)
    {
        // §P keeps only refinement errors: the checker cannot type `s.Length`, Roslyn can.
        var header = withRefinements ? Header : "§M{Rt}";
        var result = Program.Compile(header + "\n" + """
              §F{Show:pub} (str:s) -> void
                §E{cw}
                §P s.Length
            """, "print-member.calr", new CompilationOptions { EnableTypeChecking = true });
        Assert.False(result.HasErrors, Errors(result));
    }

    [Fact]
    public void Try_OrdinaryArmMismatchBeforeARefinementArm_StillReportsTheRefinement()
    {
        var result = Compile("""
              §F{Run:pub} (Pos:p, i32:k) -> i32
                §TR
                  §B{x:Nat} §W k
                    §K INT:0 → k
                    §K _ → p
                  §R x
                §CA{Exception:e}
                  §R INT:0
                §R INT:0
            """);
        AssertDistinct(result, "Pos", "Nat", "a match arm");
    }

    [Fact]
    public void ClassMember_ThisQualifiedFieldWriteAndRead_AreChecked()
    {
        var write = Compile("""
              §CL{Holder:pub}
                §FLD{Pos:_p:priv}
                §MT{Set:pub} (Nat:n) -> void
                  §ASSIGN this._p n
            """);
        AssertDistinct(write, "Nat", "Pos", "assignment to 'this._p'");

        var read = Compile("""
              §CL{Holder:pub}
                §FLD{Pos:_p:priv}
                §MT{Get:pub} (Nat:_p) -> Nat
                  §R this._p
            """);
        AssertDistinct(read, "Pos", "Nat", "a return");
    }

    [Fact]
    public void ClassMember_CaseDistinctMembers_AreDistinctVariables()
    {
        const string Members = """
              §CL{Holder:pub}
                §FLD{Nat:n:priv}
                §PROP{N:Pos:pub:get,set}
            """;
        AssertNoRefinementError(Compile(Members + """

                §MT{Small:pub} () -> Nat
                  §R n
            """));
        AssertDistinct(Compile(Members + """

                §MT{Big:pub} () -> Nat
                  §R N
            """), "Pos", "Nat", "a return");
    }

    [Fact]
    public void ClassMember_NestedClassCallsTheEnclosingStaticMethod()
    {
        // C# resolves `Take` to the enclosing class's static method, not the module function.
        AssertNoRefinementError(Compile("""
              §F{Take:pub} (Pos:p) -> i32
                §R p
              §CL{Outer:pub}
                §MT{Take:pub:stat} (Nat:n) -> i32
                  §R n
                §CL{Inner:pub}
                  §MT{Use:pub} (Nat:n) -> i32
                    §R §C{Take} §A n §/C
            """));
    }

    [Fact]
    public void ClassMember_AmbiguousOrGenericBaseClass_ImportsNoMembers()
    {
        AssertNoRefinementError(Compile("""
              §CL{Base:pub}
                §FLD{Nat:v:prot}
              §CL{Base<T>:pub}
                §FLD{Pos:v:prot}
              §CL{Derived:pub}
                §EXT{Base<i32>}
                §MT{Get:pub} () -> Pos
                  §R v
            """));
    }

    // ───────────────────────── Review round 2 ─────────────────────────

    [Fact]
    public void Runtime_DerivedMethodHidingABaseMethod_DecidesTheInferredRefinement()
    {
        // C# calls Derived.Take (returns Nat); the checker must not pick Base.Take (returns Pos)
        // and give x a Pos guard that rejects a valid 0.
        var assembly = CompileAndLoad("""
              §CL{Base:pub}
                §MT{Take:pub} (Nat:n) -> Pos
                  §R §C{ToPos} §A INT:1 §/C
                §MT{ToPos:pub} (i32:v) -> Pos
                  §R v
              §CL{Derived:pub}
                §EXT{Base}
                §MT{Take:pub} (i32:n) -> Nat
                  §R §C{ToNat} §A INT:0 §/C
                §MT{ToNat:pub} (i32:v) -> Nat
                  §R v
                §MT{Get:pub} (Nat:n) -> i32
                  §B{~x} §C{Take} §A n §/C
                  §R x
            """);

        Assert.Equal(0, Invoke(assembly, "Derived", "Get", 0));
    }

    [Fact]
    public void ClassMember_NestedBaseClassOfTheSameName_ImportsNoMembers()
    {
        AssertNoRefinementError(Compile("""
              §CL{Base:pub}
                §FLD{Nat:v:prot}
              §CL{Outer:pub}
                §CL{Base:pub}
                  §FLD{Pos:v:prot}
                §CL{Derived:pub}
                  §EXT{Base}
                  §MT{Get:pub} () -> Pos
                    §R v
            """));
    }

    [Fact]
    public void ClassMember_CallThatMayBeAnInheritedMethod_IsNotCheckedAgainstAModuleFunction()
    {
        AssertNoRefinementError(Compile("""
              §F{Add:pub} (Pos:p) -> i32
                §R p
              §CL{Holder:pub}
                §EXT{System.Collections.Generic.List<i32>}
                §MT{Use:pub} (Nat:n) -> void
                  §C{Add} §A n §/C
            """));
    }

    [Fact]
    public void ExpressionLambdaReturningADifferentRefinement_IsRejectedInPrintAndInAClassMethod()
    {
        const string Factory = """
              §DEL{Factory}
                §O{Pos}
              §F{Apply:pub} (Factory:fn) -> i32
                §R §C{fn} §/C
            """;
        AssertDistinct(Compile(Factory + """

              §F{Run:pub} (Nat:n) -> void
                §P §C{Apply} §A §LAM{} n §/LAM §/C
            """), "Nat", "Pos", "a lambda return");
        AssertDistinct(Compile(Factory + """

              §CL{Holder:pub}
                §MT{Run:pub} (Nat:n) -> i32
                  §B{f:Factory} §LAM{} n §/LAM
                  §R §C{f} §/C
            """), "Nat", "Pos", "a lambda return");
    }

    [Fact]
    public void Runtime_LambdaInAClassMethod_IsGuarded()
    {
        var assembly = CompileAndLoad("""
              §DEL{Mapper}
                §I{Nat:p}
                §O{i32}
              §CL{Holder:pub}
                §MT{Run:pub} (Nat:a) -> i32
                  §B{fn:Mapper} §LAM{p}
                    §ASSIGN p (- p INT:10)
                    §R p
                  §/LAM
                  §R §C{fn} §A a §/C
            """);

        Assert.Equal(10, Invoke(assembly, "Holder", "Run", 20));
        Assert.IsType<ArgumentOutOfRangeException>(Throws(assembly, "Holder", "Run", 5));
    }
}
