using System.Security.Cryptography;
using Calor.Compiler;
using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification;
using Calor.Compiler.Verification.Obligations;
using Calor.Compiler.Verification.Z3;
using Calor.Compiler.Verification.Z3.Cache;
using Microsoft.Z3;
using Z3ContractVerificationResult = Calor.Compiler.Verification.Z3.ContractVerificationResult;

namespace Calor.Verification.Tests.VerifierRuntimeDifferential;

internal static class DifferentialGate
{
    public const string PinnedWhitelistSha256 =
        "6dbdc9c0e1ec122ec1110013cb023ac51109ae5452b55ad00a0b782b471ec463";
    internal const string ProbeFieldType = "u8";
    internal const byte ProbeFieldWitness = byte.MaxValue;
    internal const string RuntimeCultureName = "en-US";

    private const int MaximumDepth = 3;
    private static readonly TextSpan Span = TextSpan.Empty;
    private static readonly AttributeCollection Attributes = new();

    public static DifferentialReport Run(string repositoryRoot)
    {
        VerifyWhitelistHash(repositoryRoot);

        var forms = DifferentialFormRegistry.Build();
        var cases = GenerateCases(forms);
        var module = BuildModule(cases);
        var diagnostics = new DiagnosticBag();

        var verification = new ContractVerificationPass(
                diagnostics,
                new VerificationOptions
                {
                    Verbose = false,
                    ElideProvenGuards = false,
                    TimeoutMs = VerificationOptions.DefaultTimeoutMs,
                    CacheOptions = new VerificationCacheOptions { Enabled = false }
                })
            .Verify(module);

        var obligations = new ObligationTracker();
        new ObligationGenerator(obligations).Generate(module);
        using (var context = Z3ContextFactory.Create())
        using (var solver = new ObligationSolver(context))
        {
            solver.SolveAll(obligations, module);
        }
        verification = ApplySelfRefHarnessBinding(cases, verification, obligations);

        var forcedCode = Emit(module, verification, obligations, elide: false, diagnostics);
        var elidedCode = Emit(module, verification, obligations, elide: true, diagnostics);
        ValidateGeneratedCode(forcedCode, "forced-guards");
        ValidateGeneratedCode(elidedCode, "elision-enabled");

        var forcedMethods = GeneratedMethodInspector.ExtractMethods(forcedCode);
        var elidedMethods = GeneratedMethodInspector.ExtractMethods(elidedCode);
        List<CaseResult> results;
        using (var runtime = GeneratedRuntime.Compile(
                   "CalorVerifierDifferentialMain",
                   forcedCode,
                   "VerifierDifferential"))
        {
            results = cases.Select(testCase => EvaluateCase(
                    testCase,
                    verification,
                    obligations,
                    forcedMethods,
                    elidedMethods,
                    runtime))
                .ToList();
        }

        var failSafeControls = RunFailSafeControls();
        return BuildReport(forms, results, failSafeControls);
    }

    private static IReadOnlyList<DifferentialCase> GenerateCases(
        IReadOnlyList<DifferentialForm> forms)
    {
        var cases = new List<DifferentialCase>();
        var sequence = 0;

        foreach (var form in forms.Where(candidate => candidate.MatrixApplicable))
        {
            foreach (var position in Enum.GetValues<ContractPosition>())
            {
                foreach (var depth in Enumerable.Range(1, MaximumDepth))
                {
                    foreach (var polarity in Enum.GetValues<CasePolarity>())
                    {
                        sequence++;
                        var built = form.Build(polarity);
                        if (!form.ContainsTarget(built.Condition))
                        {
                            throw new InvalidOperationException(
                                $"Generator for '{form.Id}' does not contain its registered target.");
                        }

                        var nested = DifferentialFormRegistry.ApplyIdentityNesting(
                            built.Condition,
                            depth);
                        var functionId = $"f{sequence:D6}";
                        var functionName = BuildFunctionName(sequence, form, position, depth, polarity);
                        var proofId = position == ContractPosition.Obligation
                            ? $"p{sequence:D6}"
                            : null;
                        var function = BuildFunction(
                            functionId,
                            functionName,
                            built.Parameters,
                            nested,
                            position,
                            proofId);
                        cases.Add(new DifferentialCase(
                            $"case-{sequence:D6}",
                            form.Id,
                            form.Category,
                            position,
                            depth,
                            polarity,
                            function,
                            proofId,
                            form.AllowedAssumptions));
                    }
                }
            }
        }

        return cases;
    }

    private static FunctionNode BuildFunction(
        string functionId,
        string functionName,
        IReadOnlyList<ParameterNode> parameters,
        ExpressionNode condition,
        ContractPosition position,
        string? proofId)
    {
        var preconditions = position == ContractPosition.Precondition
            ? new[] { new RequiresNode(Span, condition, null, Attributes) }
            : Array.Empty<RequiresNode>();
        var postconditions = position == ContractPosition.Postcondition
            ? new[] { new EnsuresNode(Span, condition, null, Attributes) }
            : Array.Empty<EnsuresNode>();
        var body = position == ContractPosition.Obligation
            ? new StatementNode[]
            {
                new ProofObligationNode(
                    Span,
                    proofId!,
                    "F-4 differential",
                    condition,
                    Attributes)
            }
            : Array.Empty<StatementNode>();

        return new FunctionNode(
            Span,
            functionId,
            functionName,
            Visibility.Internal,
            parameters,
            output: null,
            effects: null,
            preconditions,
            postconditions,
            body,
            Attributes);
    }

    private static ModuleNode BuildModule(IReadOnlyList<DifferentialCase> cases)
    {
        var probe = new ClassDefinitionNode(
            Span,
            "c000001",
            "Probe",
            isAbstract: false,
            isSealed: false,
            baseClass: null,
            implementedInterfaces: Array.Empty<string>(),
            typeParameters: Array.Empty<TypeParameterNode>(),
            fields:
            [
                new ClassFieldNode(
                    Span,
                    "Value",
                    ProbeFieldType,
                    Visibility.Public,
                    new IntLiteralNode(Span, ProbeFieldWitness),
                    Attributes)
            ],
            methods: Array.Empty<MethodNode>(),
            Attributes);

        return new ModuleNode(
            Span,
            "m000001",
            "VerifierDifferential",
            Array.Empty<UsingDirectiveNode>(),
            Array.Empty<InterfaceDefinitionNode>(),
            [probe],
            cases.Select(testCase => testCase.Function).ToList(),
            Attributes);
    }

    private static string Emit(
        ModuleNode module,
        ModuleVerificationResult verification,
        ObligationTracker obligations,
        bool elide,
        DiagnosticBag diagnostics)
    {
        return new CSharpEmitter(
            ContractMode.Debug,
            verification,
            inheritanceResult: null,
            obligations,
            diagnostics)
        {
            ElideProvenGuards = elide
        }.Emit(module);
    }

    private static void ValidateGeneratedCode(string code, string label)
    {
        var validation = GeneratedCSharpCompiler.Validate(code);
        var errors = validation.SyntaxErrors.Concat(validation.CompilationErrors).ToList();
        if (errors.Count == 0)
            return;

        throw new InvalidOperationException(
            $"The {label} generated C# failed validation:{Environment.NewLine}" +
            string.Join(Environment.NewLine, errors));
    }

    private static CaseResult EvaluateCase(
        DifferentialCase testCase,
        ModuleVerificationResult verification,
        ObligationTracker obligations,
        IReadOnlyDictionary<string, string> forcedMethods,
        IReadOnlyDictionary<string, string> elidedMethods,
        GeneratedRuntime runtime)
    {
        var outcome = GetOutcome(testCase, verification, obligations);
        var forcedMethod = forcedMethods[testCase.Function.Name];
        var elidedMethod = elidedMethods[testCase.Function.Name];
        var guardForced = GeneratedMethodInspector.HasGuard(forcedMethod, testCase.Position);
        var guardWithElision = GeneratedMethodInspector.HasGuard(elidedMethod, testCase.Position);
        var elisionEligible = testCase.Position != ContractPosition.Precondition
            && outcome.Status == ProofStatus.Proven
            && !outcome.IsVacuous;
        var elidedWhenEnabled = !guardWithElision;

        var runtimeVerdict = runtime.Invoke(
            testCase.Function.Name,
            testCase.Function.Parameters.Select(parameter => parameter.TypeName).ToList(),
            out var runtimeDetail);
        var expectedRuntime = testCase.Polarity == CasePolarity.Provable
            ? RuntimeVerdict.Completed
            : RuntimeVerdict.GuardFailed;
        var solverHandled = IsExpectedSolverOutcome(testCase, outcome)
            && !outcome.IsVacuous;

        var detail = new List<string>();
        if (!solverHandled)
        {
            var expected = testCase.Polarity == CasePolarity.Refutable
                ? "refuted"
                : testCase.AllowedAssumptions.Count == 0
                    ? "proven"
                    : "proven or explicitly allowed assumed";
            detail.Add(
                $"solver expected {expected} but observed {outcome.StatusName}");
        }
        if (!guardForced)
            detail.Add("forced-guard emission omitted the runtime guard");
        if (elidedWhenEnabled != elisionEligible)
        {
            detail.Add(
                elisionEligible
                    ? "eligible Proven/Discharged guard was not elided on the opt-in path"
                    : "fail-safe or precondition guard was elided on the opt-in path");
        }
        if (outcome.IsVacuous)
            detail.Add("generated case produced a vacuous proof");
        if (runtimeVerdict != expectedRuntime)
        {
            detail.Add(
                $"runtime expected {expectedRuntime} but observed {runtimeVerdict}" +
                (runtimeDetail == null ? "" : $" ({runtimeDetail})"));
        }
        if (outcome.Status == ProofStatus.Proven
            && testCase.Polarity == CasePolarity.Refutable)
        {
            detail.Add("solver proved a generated refutable case");
        }
        if (outcome.Status == ProofStatus.Refuted
            && testCase.Polarity == CasePolarity.Provable)
        {
            detail.Add("solver refuted a generated provable case");
        }

        return new CaseResult(
            testCase.Id,
            testCase.FormId,
            testCase.FormCategory,
            ToWireName(testCase.Position),
            testCase.NestingDepth,
            testCase.Polarity.ToString().ToLowerInvariant(),
            outcome.StatusName,
            runtimeVerdict.ToString().ToLowerInvariant(),
            guardForced,
            elidedWhenEnabled,
            solverHandled,
            detail.Count > 0,
            detail.Count == 0 ? null : string.Join("; ", detail));
    }

    private static bool IsExpectedSolverOutcome(
        DifferentialCase testCase,
        ProofOutcome outcome)
    {
        if (testCase.Polarity == CasePolarity.Refutable)
            return outcome.Status == ProofStatus.Refuted;

        if (outcome.Status == ProofStatus.Proven)
            return true;

        return outcome.Status == ProofStatus.Assumed
            && outcome.Assumptions.SequenceEqual(
                testCase.AllowedAssumptions.OrderBy(
                    assumption => assumption,
                    StringComparer.Ordinal));
    }

    private static ProofOutcome GetOutcome(
        DifferentialCase testCase,
        ModuleVerificationResult verification,
        ObligationTracker obligations)
    {
        if (testCase.Position == ContractPosition.Obligation)
        {
            return obligations.Obligations
                .Single(obligation => obligation.SourceProofId == testCase.ProofId)
                .Outcome
                ?? throw new InvalidOperationException(
                    $"Obligation '{testCase.ProofId}' has no typed outcome.");
        }

        var function = verification.GetFunctionResult(testCase.Function.Id)
            ?? throw new InvalidOperationException(
                $"Function '{testCase.Function.Id}' has no verification result.");
        var result = testCase.Position == ContractPosition.Precondition
            ? function.PreconditionResults.Single()
            : function.PostconditionResults.Single();
        return result.EffectiveOutcome;
    }

    private static ModuleVerificationResult ApplySelfRefHarnessBinding(
        IReadOnlyList<DifferentialCase> cases,
        ModuleVerificationResult verification,
        ObligationTracker obligations)
    {
        const string selfRefForm = "expression-kind:SelfRefNode";
        var selfCases = cases.Where(testCase => testCase.FormId == selfRefForm).ToList();
        if (selfCases.Count == 0)
            return verification;

        var outcomes = new Dictionary<string, ProofOutcome>(StringComparer.Ordinal);
        using var context = Z3ContextFactory.Create();
        foreach (var testCase in selfCases)
        {
            var condition = testCase.Position switch
            {
                ContractPosition.Precondition =>
                    testCase.Function.Preconditions.Single().Condition,
                ContractPosition.Postcondition =>
                    testCase.Function.Postconditions.Single().Condition,
                ContractPosition.Obligation =>
                    ((ProofObligationNode)testCase.Function.Body.Single()).Condition,
                _ => throw new ArgumentOutOfRangeException()
            };

            var translator = new ContractTranslator(context);
            if (!translator.DeclareVariable("__self__", "i32"))
                throw new InvalidOperationException("Could not declare the F-4 SelfRef binding.");
            translator.PushSelfVariable("__self__");
            var translated = translator.TranslateBoolExpr(condition)
                ?? throw new InvalidOperationException(
                    $"SelfRef differential case '{testCase.Id}' did not translate.");

            using var solver = context.MkSolver();
            solver.Set("timeout", VerificationOptions.DefaultTimeoutMs);
            var polarity = testCase.Position == ContractPosition.Precondition
                ? SatPolarity.SatIsProof
                : SatPolarity.SatIsRefutation;
            solver.Assert(
                testCase.Position == ContractPosition.Precondition
                    ? translated
                    : context.MkNot(translated));
            outcomes[testCase.Function.Id] = ProofOutcome.Assign(
                ProofEvidence.SolverVerdict(
                    solver.Check(),
                    solver,
                    translator.Variables,
                    polarity,
                    unsatNote: testCase.Position == ContractPosition.Precondition
                        ? "Precondition is never satisfiable"
                        : null));
            translator.PopSelfVariable();
        }

        foreach (var testCase in selfCases.Where(
                     candidate => candidate.Position == ContractPosition.Obligation))
        {
            var obligation = obligations.Obligations
                .Single(candidate => candidate.SourceProofId == testCase.ProofId);
            ApplySyntheticOutcome(obligation, outcomes[testCase.Function.Id]);
        }

        var rewritten = verification.Functions.Select(function =>
        {
            if (!outcomes.TryGetValue(function.FunctionId, out var outcome))
                return function;

            var testCase = selfCases.Single(candidate =>
                candidate.Function.Id == function.FunctionId);
            return testCase.Position switch
            {
                ContractPosition.Precondition => function with
                {
                    PreconditionResults = [Z3ContractVerificationResult.FromOutcome(outcome)]
                },
                ContractPosition.Postcondition => function with
                {
                    PostconditionResults = [Z3ContractVerificationResult.FromOutcome(outcome)]
                },
                _ => function
            };
        }).ToList();
        return new ModuleVerificationResult(rewritten);
    }

    private static IReadOnlyList<FailSafeControl> RunFailSafeControls()
    {
        var statuses = new (string Scenario, ProofOutcome Outcome)[]
        {
            ("unsupported",
                ProofOutcome.Assign(ProofEvidence.Unsupported("synthetic unsupported control"))),
            ("timeout",
                ProofOutcome.ClassifySolverStatus(
                    Status.UNKNOWN,
                    SatPolarity.SatIsRefutation,
                    reasonUnknown: "timeout: synthetic deterministic control")),
            ("solver-error",
                ProofOutcome.ClassifySolverException(
                    new InvalidOperationException("synthetic deterministic control"))),
            ("unavailable",
                ProofOutcome.Assign(ProofEvidence.SolverUnavailable("synthetic unavailable control"))),
            ("assumed",
                ProofOutcome.Assign(ProofEvidence.AssumedProof(
                    "synthetic assumed control",
                    ["differential-control"])))
        };

        var functions = new List<FunctionNode>();
        var verificationFunctions = new List<FunctionVerificationResult>();
        var tracker = new ObligationTracker();
        var controls = new List<(
            string Scenario,
            string Channel,
            ProofOutcome Outcome,
            FunctionNode Function,
            string? ProofId)>();
        var sequence = 0;

        foreach (var (scenario, outcome) in statuses)
        {
            sequence++;
            var postFunction = BuildFunction(
                $"fc{sequence:D3}",
                $"FailSafe_Post_{scenario.Replace('-', '_')}",
                Array.Empty<ParameterNode>(),
                new BoolLiteralNode(Span, false),
                ContractPosition.Postcondition,
                proofId: null);
            functions.Add(postFunction);
            verificationFunctions.Add(new FunctionVerificationResult(
                postFunction.Id,
                postFunction.Name,
                Array.Empty<Z3ContractVerificationResult>(),
                [Z3ContractVerificationResult.FromOutcome(outcome)]));
            controls.Add((scenario, "postcondition", outcome, postFunction, null));

            sequence++;
            var proofId = $"pc{sequence:D3}";
            var obligationFunction = BuildFunction(
                $"fc{sequence:D3}",
                $"FailSafe_Obligation_{scenario.Replace('-', '_')}",
                Array.Empty<ParameterNode>(),
                new BoolLiteralNode(Span, false),
                ContractPosition.Obligation,
                proofId);
            functions.Add(obligationFunction);
            controls.Add((scenario, "obligation", outcome, obligationFunction, proofId));
        }

        var module = new ModuleNode(
            Span,
            "mc0001",
            "VerifierFailSafe",
            Array.Empty<UsingDirectiveNode>(),
            functions,
            Attributes);
        new ObligationGenerator(tracker).Generate(module);
        foreach (var control in controls.Where(control => control.ProofId != null))
        {
            var obligation = tracker.Obligations.Single(
                candidate => candidate.SourceProofId == control.ProofId);
            ApplySyntheticOutcome(obligation, control.Outcome);
        }

        var code = new CSharpEmitter(
            ContractMode.Debug,
            new ModuleVerificationResult(verificationFunctions),
            inheritanceResult: null,
            tracker)
        {
            ElideProvenGuards = true
        }.Emit(module);
        ValidateGeneratedCode(code, "fail-safe-control");
        var methods = GeneratedMethodInspector.ExtractMethods(code);
        using var runtime = GeneratedRuntime.Compile(
            "CalorVerifierDifferentialFailSafe",
            code,
            "VerifierFailSafe");

        return controls.Select(control =>
        {
            var position = control.Channel == "postcondition"
                ? ContractPosition.Postcondition
                : ContractPosition.Obligation;
            var method = methods[control.Function.Name];
            var guardRetained = GeneratedMethodInspector.HasGuard(method, position);
            var runtimeVerdict = runtime.Invoke(
                control.Function.Name,
                Array.Empty<string>(),
                out _);
            return new FailSafeControl(
                control.Scenario,
                control.Channel,
                control.Outcome.StatusName,
                guardRetained,
                runtimeVerdict.ToString().ToLowerInvariant(),
                guardRetained && runtimeVerdict == RuntimeVerdict.GuardFailed);
        }).ToList();
    }

    private static void ApplySyntheticOutcome(Obligation obligation, ProofOutcome outcome)
    {
        var method = typeof(Obligation).GetMethod(
            "ApplyOutcome",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Obligation.ApplyOutcome was not found.");
        method.Invoke(obligation, [outcome]);
    }

    private static DifferentialReport BuildReport(
        IReadOnlyList<DifferentialForm> forms,
        IReadOnlyList<CaseResult> results,
        IReadOnlyList<FailSafeControl> failSafeControls)
    {
        var formCoverage = forms.Select(form =>
        {
            var cases = results.Where(result => result.FormId == form.Id).ToList();
            return new FormCoverage(
                form.Id,
                form.Category,
                form.MatrixApplicable,
                form.ExclusionReason,
                form.AllowedAssumptions,
                cases.Count == ExpectedCasesPerApplicableForm
                    && cases.All(testCase => testCase.SolverHandled),
                cases.Count,
                cases.Count(testCase => testCase.Polarity == "provable"),
                cases.Count(testCase => testCase.Polarity == "refutable"),
                cases.Count(testCase => testCase.Position == "precondition"),
                cases.Count(testCase => testCase.Position == "postcondition"),
                cases.Count(testCase => testCase.Position == "obligation"),
                cases.Any(testCase => testCase.ElidedWhenEnabled),
                cases.Count(testCase => testCase.Mismatch),
                cases.GroupBy(testCase => testCase.SolverStatus, StringComparer.Ordinal)
                    .OrderBy(group => group.Key, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal));
        }).ToList();

        var formsWhitelisted = forms.Count;
        var formsApplicable = forms.Count(form => form.MatrixApplicable);
        var formsCovered = formCoverage.Count(form => form.Applicable && form.SolverHandled);
        var formsEliding = formCoverage.Count(form => form.Elides);
        var matrixRegistered = formsWhitelisted
            * Enum.GetValues<ContractPosition>().Length
            * MaximumDepth
            * Enum.GetValues<CasePolarity>().Length;
        var matrixApplicable = formsApplicable * ExpectedCasesPerApplicableForm;
        var mismatches = results.Count(result => result.Mismatch);
        var metrics = new CoverageMetrics(
            formsWhitelisted,
            formsApplicable,
            formsCovered,
            formsEliding,
            Fraction(formsCovered, formsWhitelisted),
            Fraction(formsEliding, formsWhitelisted),
            matrixRegistered,
            matrixApplicable,
            results.Count(result => result.SolverHandled),
            Fraction(results.Count(result => result.SolverHandled), matrixApplicable),
            results.Count,
            mismatches);

        return new DifferentialReport(
            "1.1",
            "F-4",
            PinnedWhitelistSha256,
            MaximumDepth,
            Enum.GetValues<ContractPosition>().Select(ToWireName).ToList(),
            Enum.GetValues<CasePolarity>()
                .Select(polarity => polarity.ToString().ToLowerInvariant())
                .ToList(),
            metrics,
            results.GroupBy(result => result.SolverStatus, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["expression-kind:SelfRefNode"] =
                    "The harness binds '#' to an i32 parameter named '__self__' before translation; " +
                    "the emitter's existing SelfRef lowering targets the same generated parameter. " +
                    "This exercises the modeled refinement meaning without claiming ordinary source-level " +
                    "§Q/§S/§PROOF acceptance of an unbound '#'.",
                ["expression-kind:FieldAccessNode"] =
                    "The production contract pass and obligation solver derive field types from module " +
                    "class declarations. Probe.Value is a u8 accessor checked against both bounds 0..255 " +
                    "and executed at 255; mapping it as i8 violates the lower bound and mapping it as i32 " +
                    "violates both finite bounds. Proofs remain explicitly Assumed under the nullable-" +
                    "reference model.",
                ["scalar-type:i64"] =
                    "The translator models integer literals only through signed i32. The i64 row therefore " +
                    "uses signedness at -1 plus an i32-overflow boundary witness (2 * Int32.MaxValue) rather " +
                    "than claiming coverage of Int64.MinValue/MaxValue literals.",
                ["scalar-type:u32"] =
                    "The u32 row combines non-negativity with the wrap boundary of " +
                    "3 * Int32.MaxValue; this distinguishes 32-bit unsigned arithmetic from u64 without " +
                    "requiring an out-of-model UInt32.MaxValue literal.",
                ["scalar-type:u64"] =
                    "The u64 row combines non-negativity with the non-wrapping result of " +
                    "3 * Int32.MaxValue; it does not claim direct UInt64.MaxValue literal coverage.",
                ["array-element-types"] =
                    "Integer array rows apply the same per-type boundary predicates to values[0]. Runtime " +
                    "uses a non-null one-element array with the matching deterministic witness; proofs are " +
                    "therefore conditional only on the production nullable-reference-model assumption.",
                ["scalar-type:str"] =
                    "The string row proves non-negative length using the non-null ASCII runtime witness " +
                    "'ascii'. The solver result remains explicitly conditional on the production string-" +
                    "model assumption; this does not claim nullable or non-ASCII equivalence.",
                ["string-comparison-mode:Ordinal"] =
                    "The ordinal row uses the zero-width-joiner witness " +
                    "'abc'.StartsWith('\\u200dabc'): false under Ordinal but true under en-US " +
                    "CurrentCulture. Provable and refutable cells use opposite polarities of that same " +
                    "predicate. Generated runtime is executed under en-US with ambient culture restored."
            },
            formCoverage,
            failSafeControls,
            results.Where(result => result.Mismatch).ToList());
    }

    private static string BuildFunctionName(
        int sequence,
        DifferentialForm form,
        ContractPosition position,
        int depth,
        CasePolarity polarity)
    {
        var formName = new string(form.Id.Select(character =>
            char.IsLetterOrDigit(character) ? character : '_').ToArray());
        return $"D{sequence:D4}_{formName}_{position}_N{depth}_{polarity}";
    }

    private static void VerifyWhitelistHash(string repositoryRoot)
    {
        var path = Path.Combine(repositoryRoot, "docs", "verification-modeled-forms.md");
        var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
            .ToLowerInvariant();
        if (!string.Equals(actual, PinnedWhitelistSha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"F-4 whitelist hash mismatch: expected {PinnedWhitelistSha256}, actual {actual}. " +
                "Update the freeze registration before changing the denominator.");
        }
    }

    private static string ToWireName(ContractPosition position) => position switch
    {
        ContractPosition.Precondition => "precondition",
        ContractPosition.Postcondition => "postcondition",
        ContractPosition.Obligation => "obligation",
        _ => throw new ArgumentOutOfRangeException(nameof(position))
    };

    private static double Fraction(int numerator, int denominator) =>
        denominator == 0 ? 0 : Math.Round((double)numerator / denominator, 6);

    private const int ExpectedCasesPerApplicableForm =
        3 * MaximumDepth * 2;
}
