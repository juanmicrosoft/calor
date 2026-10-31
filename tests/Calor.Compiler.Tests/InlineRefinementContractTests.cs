using System.Reflection;
using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification.Obligations;
using Calor.Compiler.Verification;
using Calor.Compiler.Verification.Z3;
using Calor.Compiler.Verification.Z3.Cache;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class InlineRefinementContractTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void EntryAndReassignmentGuards_SurviveProofAndElision(bool verify, bool reassign)
    {
        var source = $$"""
            §M{Refined}
              §F{Upper:pub} (i32:lo, i32:hi | (> # lo)) -> i32
                §E{}
                §S (> result lo)
                {{(reassign ? "§ASSIGN hi lo" : "")}}
                §R hi
            """;
        var assembly = Compile(source, verify);
        var method = assembly.GetType("Refined.RefinedModule")!.GetMethod("Upper")!;
        if (!reassign)
            Assert.Equal(2, method.Invoke(null, [1, 2]));
        var exception = Assert.Throws<TargetInvocationException>(
            () => method.Invoke(null, reassign ? [1, 2] : [2, 1]));
        Assert.IsType<ArgumentOutOfRangeException>(exception.InnerException);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InheritedRefinement_RenamesBothParametersAndKeepsRuntimeGuard(bool verify)
    {
        var assembly = Compile("""
            §M{Refined}
              §IFACE{IRange}
                §MT{Upper} (i32:lo, i32:hi | (> # lo)) -> i32
              §CL{Range:pub}
                §IMPL{IRange}
                §MT{Upper:pub} (i32:a, i32:b) -> i32
                  §E{}
                  §R b
            """, verify);
        var type = assembly.GetType("Refined.Range")!;
        var instance = Activator.CreateInstance(type);
        var method = type.GetMethod("Upper")!;
        Assert.Equal(2, method.Invoke(instance, [1, 2]));
        var exception = Assert.Throws<TargetInvocationException>(() => method.Invoke(instance, [2, 1]));
        Assert.Equal("ContractViolationException", exception.InnerException!.GetType().Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReassigningDependency_DoesNotLeaveAUsableStaleEntryFact(bool verify)
    {
        var assembly = Compile("""
            §M{Refined}
              §F{Upper:pub} (i32:lo, i32:hi | (> # lo)) -> i32
                §S (> result lo)
                §ASSIGN lo hi
                §R hi
            """, verify);
        var method = assembly.GetType("Refined.RefinedModule")!.GetMethod("Upper")!;
        var exception = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [1, 2]));
        Assert.Equal("ContractViolationException", exception.InnerException!.GetType().Name);
    }

    [Theory]
    [InlineData("§C{Upper} §A 1 §A 2 §/C", ObligationStatus.Discharged)]
    [InlineData("§C{Upper} §A 2 §A 1 §/C", ObligationStatus.Failed)]
    [InlineData("§C{Upper} §A hi §A lo §/C", ObligationStatus.Discharged)]
    [InlineData("§C{Upper} §A lo §A hi §/C", ObligationStatus.Failed)]
    public void CallerObligations_SubstituteArgumentsSimultaneously(string call, ObligationStatus expected)
    {
        var source = $$"""
            §M{Refined}
              §F{Upper:pub} (i32:lo, i32:hi | (> # lo)) -> i32
                §R hi
              §F{Caller:pub} (i32:lo | (> # hi), i32:hi) -> i32
                §R {{call}}
            """;
        var module = Parse(source);
        var tracker = Solve(module);
        var obligation = Assert.Single(tracker.Obligations.Where(o => o.Description.StartsWith("Call to")));
        Assert.Equal(expected, obligation.Status);
        var callNode = Assert.IsType<CallExpressionNode>(
            Assert.IsType<ReturnStatementNode>(module.Functions[1].Body[0]).Expression);
        Assert.Equal(callNode.Span, obligation.Span);
    }

    [Fact]
    public void EffectfulCallArgument_IsExplicitlyUnsupported()
    {
        var tracker = Solve(Parse("""
            §M{Refined}
              §F{Positive:pub} (i32:x | (> # 0)) -> i32
                §R x
              §F{Caller:pub} (i32:x) -> i32
                §R §C{Positive} §A §C{Get} §/C §/C
            """));
        var obligation = Assert.Single(tracker.Obligations.Where(o => o.Description.StartsWith("Call to")));
        Assert.Equal(ObligationStatus.Unsupported, obligation.Status);
        Assert.Contains("not modeled exactly", obligation.Outcome!.Reason);
    }

    [Fact]
    public void CallRequiringNumericConversion_IsNotProvenInTheWrongWidth()
    {
        var tracker = Solve(Parse("""
            §M{Refined}
              §F{Positive:pub} (i64:x | (> # 2147483647)) -> i64
                §R x
              §F{Caller:pub} (i32:x) -> i64
                §R §C{Positive} §A x §/C
            """));
        Assert.Equal(ObligationStatus.Unsupported,
            Assert.Single(tracker.Obligations.Where(o => o.Description.StartsWith("Call to"))).Status);
    }

    [Fact]
    public void CapturedCallerRefinement_DoesNotManufactureAnInconsistentFact()
    {
        var module = Parse("""
            §M{Refined}
              §F{Positive:pub} (i32:value | (> # 0)) -> i32
                §R value
              §F{Caller:pub} (i32:x | (exists ((x i32)) (== # x))) -> i32
                §R §C{Positive} §A -1 §/C
            """);
        var collector = new FactCollector();
        collector.CollectFromFunction(module.Functions[1]);
        Assert.DoesNotContain(collector.ScopedFacts, fact => fact.Fact is BoolLiteralNode { Value: false });
        Assert.Equal(ObligationStatus.Unsupported,
            Assert.Single(Solve(module).Obligations.Where(o => o.Description.StartsWith("Call to"))).Status);
    }

    [Fact]
    public void InlinePredicate_RoundTripsOnSignatureWithoutLegacyParameterTag()
    {
        var module = Parse("""
            §M{Refined}
              §F{Upper:pub} (i32:lo, i32:hi | (> # lo)) -> i32
                §R hi
            """);
        var emitted = new CalorEmitter().Emit(module);
        Assert.DoesNotContain("§I{", emitted);
        Assert.Contains("| (> # lo)", emitted);
        Assert.NotNull(Parse(emitted).Functions[0].Parameters[1].InlineRefinement);
    }

    [Theory]
    [InlineData("(> # missing)", DiagnosticCode.UndefinedReference)]
    [InlineData("(+ # 1)", DiagnosticCode.TypeMismatch)]
    public void InvalidRefinement_UsesPredicateSourceSpan(string predicate, string code)
    {
        var source = $$"""
            §M{Refined}
              §F{Upper:pub} (i32:x | {{predicate}}) -> i32
                §R x
            """;
        var result = Program.Compile(source, "invalid.calr");
        Assert.True(result.HasErrors);
        var diagnostic = Assert.Single(result.Diagnostics.Errors.Where(d => d.Code == code));
        Assert.Equal(Parse(source).Functions[0].Parameters[0].InlineRefinement!.Predicate.Span, diagnostic.Span);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("timeout")]
    [InlineData("unsupported")]
    [InlineData("unavailable")]
    public void NonProvenOutcome_KeepsEntryAndPostconditionGuards(string status)
    {
        var module = Parse("""
            §M{Refined}
              §F{Upper:pub} (i32:x | (> # 0)) -> i32
                §S (< result 0)
                §R x
            """);
        var outcome = Calor.Compiler.Verification.Z3.ContractVerificationResult.FromOutcome(
            ProofOutcome.Rehydrate(status, null, "control: no proof established"));
        var results = new ModuleVerificationResult(
            [new FunctionVerificationResult("f1", "Upper", [outcome], [outcome])]);
        var generated = new CSharpEmitter(ContractMode.Debug, results).Emit(module);
        Assert.DoesNotContain("// PROVEN", generated);
        var method = Load(generated).GetType("Refined.RefinedModule")!.GetMethod("Upper")!;
        var entryError = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [-1]));
        Assert.IsType<ArgumentOutOfRangeException>(entryError.InnerException);
        var postError = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [1]));
        Assert.Equal("ContractViolationException", postError.InnerException!.GetType().Name);
    }

    [Fact]
    public void PrivateRefinement_IsCheckedAtCallerNotUniversallyRefutedAtDeclaration()
    {
        var options = new CompilationOptions
        {
            VerifyRefinements = true,
            VerifyContracts = true,
            VerificationCacheOptions = new VerificationCacheOptions { Enabled = false }
        };
        var result = Program.Compile("""
            §M{Refined}
              §F{Positive:priv} (i32:x | (> # 0)) -> i32
                §R x
              §F{Caller:pub} () -> i32
                §R §C{Positive} §A 1 §/C
            """, "private.calr", options);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics.Errors));
        Assert.Equal(ObligationStatus.Discharged,
            Assert.Single(options.ObligationResults!.Obligations.Where(o => o.Description.StartsWith("Call to"))).Status);
        Assert.Equal(ObligationStatus.Boundary,
            Assert.Single(options.ObligationResults.Obligations.Where(o => o.Kind == ObligationKind.RefinementEntry)).Status);
        Assert.Contains("ArgumentOutOfRangeException", result.GeneratedCode);
    }

    private static ObligationTracker Solve(ModuleNode module)
    {
        var tracker = new ObligationTracker();
        new ObligationGenerator(tracker).Generate(module);
        using var context = Calor.Compiler.Verification.Z3.Z3ContextFactory.Create();
        using var solver = new ObligationSolver(context);
        solver.SolveAll(tracker, module);
        return tracker;
    }

    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        return module;
    }

    private static Assembly Compile(string source, bool verify)
    {
        var result = Program.Compile(source, "refined.calr", new CompilationOptions
        {
            EnableTypeChecking = true,
            VerifyContracts = verify,
            ElideProvenGuards = true,
            VerificationCacheOptions = new VerificationCacheOptions { Enabled = false },
            StatusWriter = TextWriter.Null
        });
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics.Errors));
        return Load(result.GeneratedCode);
    }

    private static Assembly Load(string code)
    {
        var compilation = CSharpCompilation.Create("Refined_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble + code)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emission = compilation.Emit(stream);
        Assert.True(emission.Success, string.Join("\n", emission.Diagnostics));
        return Assembly.Load(stream.ToArray());
    }
}
