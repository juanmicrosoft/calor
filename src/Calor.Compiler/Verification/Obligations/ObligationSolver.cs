using System.Diagnostics;
using Calor.Compiler.Ast;
using Calor.Compiler.Verification.Z3;
using Microsoft.Z3;

namespace Calor.Compiler.Verification.Obligations;

/// <summary>
/// Solves obligations using Z3 with the assume-negate-check pattern.
/// Follows the same pattern as Z3Verifier.VerifyPostcondition().
/// </summary>
public sealed class ObligationSolver : IDisposable
{
    private readonly Context _ctx;
    private readonly uint _timeoutMs;
    private bool _disposed;

    public ObligationSolver(Context ctx, uint timeoutMs = VerificationOptions.DefaultTimeoutMs)
    {
        _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        _timeoutMs = timeoutMs;
    }

    /// <summary>
    /// Solve all pending obligations in the tracker.
    /// </summary>
    public void SolveAll(
        ObligationTracker tracker,
        ModuleNode module)
    {
        // Build a lookup of function info for parameter declarations
        var functionInfo = BuildFunctionInfo(module);
        var userTypeRegistry = ContractTranslator.BuildUserTypeRegistry(module);

        foreach (var obligation in tracker.Obligations)
        {
            if (obligation.Status != ObligationStatus.Pending)
                continue;

            if (functionInfo.TryGetValue(obligation.FunctionId, out var info))
            {
                SolveObligation(obligation, info, userTypeRegistry);
            }
            else
            {
                obligation.ApplyOutcome(ProofOutcome.Assign(ProofEvidence.Unsupported(
                    $"Function '{obligation.FunctionId}' not found")));
            }
        }
    }

    private void SolveObligation(
        Obligation obligation,
        FunctionInfo info,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> userTypeRegistry)
    {
        var sw = Stopwatch.StartNew();

        var translator = new ContractTranslator(_ctx);
        translator.SetUserTypeRegistry(userTypeRegistry);

        // Declare all function parameters
        foreach (var (name, type) in info.Parameters)
        {
            var solverType = ResolveRefinementBaseType(type, info.RefinementTypes);
            if (!translator.DeclareVariable(name, solverType))
            {
                // For IndexBounds obligations, skip undeclarable parameters
                // (e.g., indexed type names like SizedList that aren't Z3-translatable).
                // The obligation condition only references the index and size variables.
                if (obligation.Kind == ObligationKind.IndexBounds)
                    continue;

                obligation.ApplyOutcome(ProofOutcome.Assign(ProofEvidence.Unsupported(
                    ContractTranslator.DiagnoseUnsupportedType(solverType))));
                obligation.SolverDuration = sw.Elapsed;
                return;
            }
        }

        // Declare extra variables (e.g., indexed type size parameters)
        foreach (var (name, type) in info.ExtraVariables)
        {
            // Only declare if not already declared (could overlap with a parameter name)
            if (!translator.Variables.ContainsKey(name))
            {
                translator.DeclareVariable(name, type);
            }
        }

        if (obligation.Kind == ObligationKind.RefinementReturn
            && info.OutputType is not null
            && !translator.DeclareVariable(
                "result",
                ResolveRefinementBaseType(info.OutputType, info.RefinementTypes)))
        {
            obligation.ApplyOutcome(ProofOutcome.Assign(ProofEvidence.Unsupported(
                ContractTranslator.DiagnoseUnsupportedType(info.OutputType))));
            obligation.SolverDuration = sw.Elapsed;
            return;
        }

        // Refinement predicates use # for the constrained entry or return value.
        // so # in the predicate resolves to the parameter being checked
        if (obligation.Kind is ObligationKind.RefinementEntry
                or ObligationKind.RefinementReturn
                or ObligationKind.Subtype
            && obligation.ParameterName != null)
        {
            translator.PushSelfVariable(obligation.ParameterName);
        }

        // Translate the obligation condition
        var conditionExpr = translator.TranslateBoolExpr(obligation.Condition);
        if (conditionExpr == null)
        {
            obligation.ApplyOutcome(ProofOutcome.Assign(ProofEvidence.Unsupported(
                translator.DiagnoseBoolExprFailure(obligation.Condition)
                ?? translator.DiagnoseTranslationFailure(obligation.Condition)
                ?? "Obligation condition could not be translated to Z3")));
            obligation.SolverDuration = sw.Elapsed;

            if (obligation.Kind is ObligationKind.RefinementEntry
                    or ObligationKind.RefinementReturn
                    or ObligationKind.Subtype
                && obligation.ParameterName != null)
                translator.PopSelfVariable();
            return;
        }

        try
        {
            var solver = _ctx.MkSolver();
            solver.Set("timeout", _timeoutMs);

            // ASSUME: Assert all translatable preconditions
            var preconditionExprs = new List<BoolExpr>();
            foreach (var pre in info.Preconditions)
            {
                var preExpr = translator.TranslateBoolExpr(pre.Condition);
                if (preExpr != null)
                {
                    preconditionExprs.Add(preExpr);
                    solver.Assert(preExpr);
                }
            }

            // ASSUME: Assert collected flow-sensitive facts (loop bounds, parameter
            // refinements, etc.) whose governed source range contains the obligation —
            // a guard fact must not leak into sibling branches or past its body.
            // For RefinementEntry obligations, skip collected facts to avoid circular
            // reasoning (the obligation IS the refinement, not an assumption for it).
            if (obligation.Kind != ObligationKind.RefinementEntry)
            {
                foreach (var fact in info.CollectedFacts)
                {
                    if (!fact.AppliesTo(obligation.Span))
                        continue;

                    var factExpr = translator.TranslateBoolExpr(fact.Fact);
                    if (factExpr != null)
                    {
                        solver.Assert(factExpr);
                    }
                }
            }

            // CONSISTENCY PRE-CHECK: an UNSAT assumption set would vacuously
            // discharge every obligation ("assume False, prove anything"). If the
            // assumptions are inconsistent, retry with preconditions only; if the
            // preconditions themselves are inconsistent, refuse to discharge.
            if (solver.Check() == Status.UNSATISFIABLE)
            {
                solver = _ctx.MkSolver();
                solver.Set("timeout", _timeoutMs);
                foreach (var preExpr in preconditionExprs)
                {
                    solver.Assert(preExpr);
                }

                if (solver.Check() == Status.UNSATISFIABLE)
                {
                    obligation.ApplyOutcome(ProofOutcome.Assign(ProofEvidence.Unsupported(
                        "Assumption set is inconsistent (unsatisfiable preconditions); " +
                        "vacuous discharge prevented")));
                    obligation.SolverDuration = sw.Elapsed;
                    return;
                }
            }

            // NEGATE: Assert NOT(obligation condition)
            // If UNSAT -> obligation always holds under preconditions -> Discharged
            solver.Assert(_ctx.MkNot(conditionExpr));

            // CHECK
            var status = solver.Check();

            obligation.SolverDuration = sw.Elapsed;

            // D3/D12: this is the SECOND channel where a proof deletes a runtime guard —
            // `Discharged` makes CSharpEmitter drop the `if (!(cond)) throw` for a refinement
            // obligation, exactly as `Proven` elides a postcondition. It shares the string theory
            // with the contract path, so it needs the same demotion: a refinement predicate like
            // `(> (len #) INT:0)` is carried by a null-blind, byte-counted model. Assumed maps
            // away from Discharged (ProofOutcome.ToObligationStatus), so the guard survives.
            if (status == Status.UNSATISFIABLE
                && (translator.TouchedStringTheory || translator.TouchedNullableReferenceSort))
            {
                // Name the divergence that ACTUALLY carried the proof. An earlier revision
                // parameterized the assumption list but left the reason hardcoded to the string
                // wording, so an array- or user-type-carried obligation reported "the string
                // model" — on a condition containing no string at all.
                var assumptions = new List<string>();
                var reasons = new List<string>();
                if (translator.TouchedStringTheory)
                {
                    assumptions.Add(Z3Verifier.StringModelAssumption);
                    reasons.Add("the solver's string theory, whose strings are non-null and " +
                                "byte-counted while .NET's are nullable and UTF-16-code-unit-counted (D3/D12)");
                }
                if (translator.TouchedNullableReferenceSort)
                {
                    assumptions.Add(Z3Verifier.NullableReferenceModelAssumption);
                    reasons.Add("the solver's array and user-type sorts, which are total and " +
                                "non-null while .NET's are nullable references (D14)");
                }

                obligation.ApplyOutcome(ProofOutcome.Assign(ProofEvidence.AssumedProof(
                    $"Proof is conditional on {string.Join("; and on ", reasons)}. Runtime check kept.",
                    assumptions)));
                return;
            }

            obligation.ApplyOutcome(ProofOutcome.Assign(ProofEvidence.SolverVerdict(
                status, solver, translator.Variables, SatPolarity.SatIsRefutation)));
        }
        catch (Z3Exception ex)
        {
            obligation.ApplyOutcome(ProofOutcome.Assign(ProofEvidence.SolverError(ex)));
            obligation.SolverDuration = sw.Elapsed;
        }
        finally
        {
            if (obligation.Kind is ObligationKind.RefinementEntry or ObligationKind.RefinementReturn
                && obligation.ParameterName != null)
                translator.PopSelfVariable();
        }
    }

    private static Dictionary<string, FunctionInfo> BuildFunctionInfo(ModuleNode module)
    {
        var result = new Dictionary<string, FunctionInfo>(StringComparer.Ordinal);

        // Build indexed type lookup for size parameter injection
        var indexedTypes = new Dictionary<string, IndexedTypeNode>(StringComparer.Ordinal);
        foreach (var itype in module.IndexedTypes)
        {
            indexedTypes[itype.Name] = itype;
        }
        var refinementTypes = module.RefinementTypes.ToDictionary(
            type => type.Name,
            type => type.BaseTypeName,
            StringComparer.Ordinal);

        foreach (var func in module.Functions)
        {
            var parameters = func.Parameters
                .Select(p => (p.Name, p.TypeName))
                .ToList();

            // Collect flow-sensitive facts (loop bounds, etc.)
            var factCollector = new FactCollector();
            factCollector.CollectFromFunction(func);

            // Add size parameter variables for indexed-typed parameters
            var extraVars = new List<(string Name, string TypeName)>();
            foreach (var param in func.Parameters)
            {
                var baseTypeName = param.TypeName;
                var genericIdx = baseTypeName.IndexOf('<');
                if (genericIdx > 0)
                    baseTypeName = baseTypeName.Substring(0, genericIdx);

                if (indexedTypes.TryGetValue(baseTypeName, out var itype))
                {
                    // Add the size parameter as an integer variable
                    extraVars.Add((itype.SizeParam, "i32"));

                    // If the indexed type has a constraint, add it as a fact
                    if (itype.Constraint != null)
                    {
                        factCollector.AddFunctionWideFact(
                            FactCollector.SubstituteSelfRefStatic(itype.Constraint, itype.SizeParam));
                    }
                }
            }

            result[func.Id] = new FunctionInfo(
                parameters,
                func.Preconditions,
                func.Output?.TypeName,
                factCollector.ScopedFacts,
                extraVars,
                refinementTypes);
        }

        foreach (var enumExtension in module.EnumExtensions)
        {
            foreach (var method in enumExtension.Methods)
            {
                var parameters = method.Parameters
                    .Select(p => (p.Name, p.TypeName))
                    .ToList();
                var factCollector = new FactCollector();
                factCollector.CollectFromFunction(method);
                var extraVars = new List<(string Name, string TypeName)>();
                foreach (var param in method.Parameters)
                {
                    var baseTypeName = param.TypeName;
                    var genericIdx = baseTypeName.IndexOf('<');
                    if (genericIdx > 0)
                        baseTypeName = baseTypeName[..genericIdx];

                    if (indexedTypes.TryGetValue(baseTypeName, out var indexedType))
                    {
                        extraVars.Add((indexedType.SizeParam, "i32"));
                        if (indexedType.Constraint != null)
                        {
                            factCollector.AddFunctionWideFact(
                                FactCollector.SubstituteSelfRefStatic(
                                    indexedType.Constraint,
                                    indexedType.SizeParam));
                        }
                    }
                }

                result[method.Id] = new FunctionInfo(
                    parameters,
                    method.Preconditions,
                    method.Output?.TypeName,
                    factCollector.ScopedFacts,
                    extraVars,
                    refinementTypes);
            }
        }

        foreach (var cls in module.Classes)
        {
            foreach (var constructor in cls.Constructors)
            {
                var parameters = constructor.Parameters
                    .Select(p => (p.Name, p.TypeName))
                    .ToList();
                var factCollector = new FactCollector();
                factCollector.CollectFromStatements(constructor.Body);
                result[constructor.Id] = new FunctionInfo(
                    parameters,
                    constructor.Preconditions,
                    null,
                    factCollector.ScopedFacts,
                    new List<(string, string)>(),
                    refinementTypes);
            }

            foreach (var method in cls.Methods)
            {
                var parameters = method.Parameters
                    .Select(p => (p.Name, p.TypeName))
                    .ToList();
                var factCollector = new FactCollector();
                factCollector.CollectFromMethod(method);
                var extraVars = new List<(string Name, string TypeName)>();
                foreach (var param in method.Parameters)
                {
                    var baseTypeName = param.TypeName;
                    var genericIdx = baseTypeName.IndexOf('<');
                    if (genericIdx > 0)
                        baseTypeName = baseTypeName[..genericIdx];

                    if (indexedTypes.TryGetValue(baseTypeName, out var indexedType))
                    {
                        extraVars.Add((indexedType.SizeParam, "i32"));
                        if (indexedType.Constraint != null)
                        {
                            factCollector.AddFunctionWideFact(
                                FactCollector.SubstituteSelfRefStatic(
                                    indexedType.Constraint,
                                    indexedType.SizeParam));
                        }
                    }
                }

                result[method.Id] = new FunctionInfo(
                    parameters,
                    method.Preconditions,
                    method.Output?.TypeName,
                    factCollector.ScopedFacts,
                    extraVars,
                    refinementTypes);
            }

            foreach (var operatorOverload in cls.OperatorOverloads)
            {
                var parameters = operatorOverload.Parameters
                    .Select(p => (p.Name, p.TypeName))
                    .ToList();
                var factCollector = new FactCollector();
                factCollector.CollectFromStatements(operatorOverload.Body);
                var extraVars = new List<(string Name, string TypeName)>();
                foreach (var param in operatorOverload.Parameters)
                {
                    var baseTypeName = param.TypeName;
                    var genericIdx = baseTypeName.IndexOf('<');
                    if (genericIdx > 0)
                        baseTypeName = baseTypeName[..genericIdx];

                    if (indexedTypes.TryGetValue(baseTypeName, out var indexedType))
                    {
                        extraVars.Add((indexedType.SizeParam, "i32"));
                        if (indexedType.Constraint != null)
                        {
                            factCollector.AddFunctionWideFact(
                                FactCollector.SubstituteSelfRefStatic(
                                    indexedType.Constraint,
                                    indexedType.SizeParam));
                        }
                    }
                }

                result[operatorOverload.Id] = new FunctionInfo(
                    parameters,
                    operatorOverload.Preconditions,
                    operatorOverload.Output?.TypeName,
                    factCollector.ScopedFacts,
                    extraVars,
                    refinementTypes);
            }
        }

        return result;
    }

    private sealed record FunctionInfo(
        List<(string Name, string TypeName)> Parameters,
        IReadOnlyList<RequiresNode> Preconditions,
        string? OutputType,
        List<ScopedFact> CollectedFacts,
        List<(string Name, string TypeName)> ExtraVariables,
        IReadOnlyDictionary<string, string> RefinementTypes);

    private static string ResolveRefinementBaseType(
        string typeName,
        IReadOnlyDictionary<string, string> refinementTypes)
    {
        var resolvedType = typeName;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (visited.Add(resolvedType)
               && refinementTypes.TryGetValue(
                   resolvedType,
                   out var baseType))
        {
            resolvedType = baseType;
        }
        return resolvedType;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _ctx.Dispose();
        }
    }
}
