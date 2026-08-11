using System.Text;
using System.Runtime.CompilerServices;
using Calor.Compiler.Ast;
using Calor.Compiler.Verification;
using Calor.Compiler.Verification.Z3;
using Microsoft.Z3;
using Xunit;

namespace Calor.Verification.Tests.VerifierRuntimeDifferential;

public sealed class VerifierRuntimeDifferentialTests
{
    private const string UpdateReportsVariable =
        "CALOR_UPDATE_VERIFIER_RUNTIME_DIFFERENTIAL_REPORTS";

    [Fact]
    public void CommittedReportsMatchGeneratedOracle()
    {
        Assert.True(
            Z3ContextFactory.IsAvailable,
            "F-4 differential gate cannot run: Z3 is unavailable. This is a blocking failure, not a skip.");

        var repositoryRoot = FindRepositoryRoot();
        var report = DifferentialGate.Run(repositoryRoot);

        Assert.True(report.Passed, ReportWriter.ToJson(report));
        Assert.Equal(65, report.Coverage.FormsWhitelisted);
        Assert.Equal(65, report.Coverage.FormsCovered);
        Assert.Equal(1_170, report.Coverage.MatrixCellsRegistered);
        Assert.Equal(1_170, report.Coverage.MatrixCellsApplicable);
        Assert.Equal(1_170, report.Coverage.MatrixCellsCovered);
        Assert.Equal(1_170, report.Coverage.CasesGenerated);
        Assert.Equal(0, report.Coverage.Mismatches);
        Assert.Equal(10, report.FailSafeControls.Count);
        Assert.All(report.FailSafeControls, control => Assert.True(control.Passed));
        var timeoutControls = report.FailSafeControls
            .Where(control => control.Scenario == "timeout")
            .ToList();
        var solverErrorControls = report.FailSafeControls
            .Where(control => control.Scenario == "solver-error")
            .ToList();
        Assert.Equal(2, timeoutControls.Count);
        Assert.Equal(2, solverErrorControls.Count);
        Assert.All(timeoutControls, control => Assert.Equal("timeout", control.Status));
        Assert.All(solverErrorControls, control => Assert.Equal("unknown", control.Status));
        Assert.All(
            report.Forms.Where(form => form.Applicable),
            form => Assert.True(form.SolverHandled, form.Id));

        var fieldAccess = report.Forms.Single(
            form => form.Id == "expression-kind:FieldAccessNode");
        Assert.Equal(18, fieldAccess.Cases);
        Assert.Equal(3, fieldAccess.Statuses["proven"]);
        Assert.Equal(6, fieldAccess.Statuses["assumed"]);
        Assert.Equal(9, fieldAccess.Statuses["refuted"]);
        Assert.False(fieldAccess.Statuses.ContainsKey("unsupported"));

        var reports = new[]
        {
            (
                Path.Combine(
                    repositoryRoot,
                    "bench",
                    "phase0-agent-native",
                    "verifier-runtime-differential.json"),
                ReportWriter.ToJson(report)),
            (
                Path.Combine(
                    repositoryRoot,
                    "bench",
                    "phase0-agent-native",
                    "verifier-runtime-differential.md"),
                ReportWriter.ToMarkdown(report))
        };

        foreach (var (path, generated) in reports)
        {
            var generatedBytes = Encoding.UTF8.GetBytes(generated);
            Assert.DoesNotContain((byte)'\r', generatedBytes);
            if (Environment.GetEnvironmentVariable(UpdateReportsVariable) == "1")
                File.WriteAllBytes(path, generatedBytes);

            Assert.True(File.Exists(path), $"Committed report is missing: {path}");
            var committedBytes = File.ReadAllBytes(path);
            Assert.DoesNotContain((byte)'\r', committedBytes);
            Assert.True(
                committedBytes.AsSpan().SequenceEqual(generatedBytes),
                $"Committed report is stale: {path}. Set {UpdateReportsVariable}=1 and rerun this test.");
        }
    }

    [Fact]
    public void FieldAccessProbeDistinguishesU8FromI8AndI32Mutations()
    {
        Assert.True(
            Z3ContextFactory.IsAvailable,
            "Field-access regression cannot run: Z3 is unavailable.");

        var form = DifferentialFormRegistry.Build().Single(
            candidate => candidate.Id == "expression-kind:FieldAccessNode");
        var predicate = Assert.IsType<BinaryOperationNode>(
            form.Build(CasePolarity.Provable).Condition);
        Assert.Equal(BinaryOperator.And, predicate.Operator);
        Assert.Equal(
            BinaryOperator.GreaterOrEqual,
            Assert.IsType<BinaryOperationNode>(predicate.Left).Operator);
        Assert.Equal(
            BinaryOperator.LessOrEqual,
            Assert.IsType<BinaryOperationNode>(predicate.Right).Operator);
        Assert.Equal(byte.MaxValue, DifferentialGate.ProbeFieldWitness);

        using var context = Z3ContextFactory.Create();
        var registered = TranslateFieldBound(
            context,
            predicate,
            DifferentialGate.ProbeFieldType);
        var signedMutation = TranslateFieldBound(context, predicate, "i8");
        var widthMutation = TranslateFieldBound(context, predicate, "i32");

        Assert.Equal(8u, registered.Width);
        Assert.Equal(Status.UNSATISFIABLE, registered.NegatedBoundStatus);
        Assert.Equal(8u, signedMutation.Width);
        Assert.Equal(Status.SATISFIABLE, signedMutation.NegatedBoundStatus);
        Assert.Equal(32u, widthMutation.Width);
        Assert.Equal(Status.SATISFIABLE, widthMutation.NegatedBoundStatus);
    }

    [Fact]
    public void ArrayElementProbeDistinguishesU8FromI8AndI32Mutations()
    {
        Assert.True(
            Z3ContextFactory.IsAvailable,
            "Array-element regression cannot run: Z3 is unavailable.");

        var form = DifferentialFormRegistry.Build().Single(
            candidate => candidate.Id == "array-element-type:u8");
        var predicate = form.Build(CasePolarity.Provable).Condition;

        using var context = Z3ContextFactory.Create();
        var registered = TranslateArrayBound(context, predicate, "u8");
        var signedMutation = TranslateArrayBound(context, predicate, "i8");
        var widthMutation = TranslateArrayBound(context, predicate, "i32");

        Assert.Equal(8u, registered.Width);
        Assert.Equal(Status.UNSATISFIABLE, registered.NegatedBoundStatus);
        Assert.Equal(8u, signedMutation.Width);
        Assert.Equal(Status.SATISFIABLE, signedMutation.NegatedBoundStatus);
        Assert.Equal(32u, widthMutation.Width);
        Assert.Equal(Status.SATISFIABLE, widthMutation.NegatedBoundStatus);
    }

    [Fact]
    public void SolverClassificationSeamDistinguishesTimeoutFromSolverError()
    {
        var timeout = ProofOutcome.ClassifySolverStatus(
            Status.UNKNOWN,
            SatPolarity.SatIsRefutation,
            reasonUnknown: "timeout: deterministic regression");
        var solverError = ProofOutcome.ClassifySolverException(
            new InvalidOperationException("deterministic regression"));

        Assert.Equal(ProofStatus.Timeout, timeout.Status);
        Assert.Equal(ProofStatus.Unknown, solverError.Status);
        Assert.Contains("Z3 solver error", solverError.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void RepositoryRootDiscoveryAcceptsGitFileWorktrees()
    {
        const string candidate = "/worktree";
        Assert.True(IsRepositoryRootCandidate(
            candidate,
            path => path == Path.Combine(candidate, "Directory.Build.props")
                || path == Path.Combine(candidate, ".git"),
            _ => false));
    }

    [Fact]
    public void GeneratedAssembliesAreCollectibleAndUnload()
    {
        var loadContext = CompileInvokeAndUnload();

        for (var attempt = 0; attempt < 10 && loadContext.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.False(loadContext.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CompileInvokeAndUnload()
    {
        const string code = """
            namespace CollectibleProbe
            {
                internal static class CollectibleProbeModule
                {
                    internal static void Execute()
                    {
                    }
                }
            }
            """;
        using var runtime = GeneratedRuntime.Compile(
            "CalorVerifierDifferentialCollectible",
            code,
            "CollectibleProbe");
        Assert.True(runtime.IsCollectible);
        Assert.Equal(
            RuntimeVerdict.Completed,
            runtime.Invoke("Execute", Array.Empty<string>(), out _));
        return runtime.LoadContextReference;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (IsRepositoryRootCandidate(directory.FullName, File.Exists, Directory.Exists))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the Calor repository root.");
    }

    private static bool IsRepositoryRootCandidate(
        string directory,
        Func<string, bool> fileExists,
        Func<string, bool> directoryExists)
    {
        return fileExists(Path.Combine(directory, "Directory.Build.props"))
            && (directoryExists(Path.Combine(directory, ".git"))
                || fileExists(Path.Combine(directory, ".git")));
    }

    private static (uint Width, Status NegatedBoundStatus) TranslateFieldBound(
        Context context,
        BinaryOperationNode bound,
        string fieldType)
    {
        var translator = new ContractTranslator(context);
        translator.SetUserTypeRegistry(
            new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["probe"] = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["Value"] = fieldType
                }
            });
        Assert.True(translator.DeclareVariable("probe", "Probe"));

        var field = FindFieldAccess(bound);
        var translatedField = translator.TranslateBitVecExpr(field);
        Assert.NotNull(translatedField);

        var translatedBound = translator.TranslateBoolExpr(bound);
        Assert.NotNull(translatedBound);

        using var solver = context.MkSolver();
        solver.Assert(context.MkNot(translatedBound));
        return (translatedField.SortSize, solver.Check());
    }

    private static FieldAccessNode FindFieldAccess(ExpressionNode expression)
    {
        return expression switch
        {
            FieldAccessNode field => field,
            BinaryOperationNode binary => FindFieldAccess(binary.Left),
            UnaryOperationNode unary => FindFieldAccess(unary.Operand),
            _ => throw new Xunit.Sdk.XunitException(
                $"No field access found in {expression.GetType().Name}.")
        };
    }

    private static (uint Width, Status NegatedBoundStatus) TranslateArrayBound(
        Context context,
        ExpressionNode predicate,
        string elementType)
    {
        var translator = new ContractTranslator(context);
        Assert.True(translator.DeclareVariable("values", $"{elementType}[]"));

        var access = FindArrayAccess(predicate);
        var translatedAccess = translator.TranslateBitVecExpr(access);
        Assert.NotNull(translatedAccess);
        var translatedPredicate = translator.TranslateBoolExpr(predicate);
        Assert.NotNull(translatedPredicate);

        using var solver = context.MkSolver();
        solver.Assert(context.MkNot(translatedPredicate));
        return (translatedAccess.SortSize, solver.Check());
    }

    private static ArrayAccessNode FindArrayAccess(ExpressionNode expression)
    {
        return FindArrayAccessOrNull(expression)
            ?? throw new Xunit.Sdk.XunitException(
                $"No array access found in {expression.GetType().Name}.");
    }

    private static ArrayAccessNode? FindArrayAccessOrNull(ExpressionNode expression)
    {
        return expression switch
        {
            ArrayAccessNode access => access,
            BinaryOperationNode binary => FindArrayAccessOrNull(binary.Left)
                ?? FindArrayAccessOrNull(binary.Right),
            UnaryOperationNode unary => FindArrayAccessOrNull(unary.Operand),
            ImplicationExpressionNode implication => FindArrayAccessOrNull(implication.Antecedent)
                ?? FindArrayAccessOrNull(implication.Consequent),
            _ => null
        };
    }
}
