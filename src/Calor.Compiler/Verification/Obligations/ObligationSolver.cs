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
    private HashSet<string> _propertyNames = new(StringComparer.Ordinal);
    private bool _checkIntegerOverflow = true;
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
        _checkIntegerOverflow = module.ShouldCheckIntegerOverflow();
        _propertyNames = FactCollector.PropertyNames(module);
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

        // #1413 review: a parameter named `result` would share the return value's solver
        // constant, turning the parameter's refinement into a "proof" of the return's.
        if (obligation.Kind == ObligationKind.RefinementReturn && info.Parameters.Any(p => p.Name == "result"))
        {
            obligation.ApplyOutcome(ProofOutcome.Assign(ProofEvidence.Unsupported(
                "a parameter is named 'result', which collides with the return value. Runtime check kept.")));
            return;
        }

        // #1413 review: raw C# in an entry predicate runs before the body and may change any state.
        if (info.Facts.HasOpaqueEntry || info.Preconditions.Any(pre => FactCollector.IsOpaque(pre.Condition)))
        {
            obligation.ApplyOutcome(ProofOutcome.Assign(ProofEvidence.Unsupported(
                "an entry predicate contains raw C#, whose effects are not modeled. Runtime check kept.")));
            return;
        }

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

        IsolatedSolver? solver = null;
        try
        {
            solver = new IsolatedSolver(_ctx, _timeoutMs);

            // #1413 (S1 OBL-MUTATION-KILL): a satisfying model is a counterexample only
            // when the asserted state is exactly the program's state at the obligation.
            // Every reason it is not is collected here; see the SAT handling below.
            var inexact = new List<string>();
            var isEntry = obligation.Kind == ObligationKind.RefinementEntry;

            // ASSUME: Assert all translatable preconditions. A precondition describes the
            // ENTRY state: it is dropped for any obligation after entry when the body may
            // rebind a name it reads — asserting `§Q (> x -1)` after `§ASSIGN x -5`
            // discharged `§PROOF (> x -1)` and deleted its guard (a false proof).
            var preconditionExprs = new List<BoolExpr>();
            foreach (var pre in info.Preconditions)
            {
                if (!isEntry && info.Facts.IsStaleAfterEntry(pre.Condition))
                {
                    inexact.Add("a precondition reads a variable or heap state the body may change");
                    continue;
                }
                var preExpr = translator.TranslateBoolExpr(pre.Condition);
                if (preExpr != null)
                {
                    preconditionExprs.Add(preExpr);
                    solver.Assert(preExpr);
                }
                else
                {
                    inexact.Add("a precondition could not be modeled");
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
                    else
                    {
                        inexact.Add("a path fact could not be modeled");
                    }
                }
            }

            // CONSISTENCY PRE-CHECK: an UNSAT assumption set would vacuously
            // discharge every obligation ("assume False, prove anything"). If the
            // assumptions are inconsistent, retry with preconditions only; if the
            // preconditions themselves are inconsistent, refuse to discharge.
            if (solver.Check() == Status.UNSATISFIABLE)
            {
                inexact.Add("the path facts at this obligation are inconsistent (it may be unreachable)");
                solver.Dispose();
                solver = new IsolatedSolver(_ctx, _timeoutMs);
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

            var arithmeticSafety = _checkIntegerOverflow
                ? translator.GetCheckedArithmeticSafety(obligation.Condition) : _ctx.MkTrue();
            if (arithmeticSafety == null)
            {
                obligation.ApplyOutcome(ProofOutcome.Assign(ProofEvidence.Unsupported(
                    "Obligation checked-arithmetic safety could not be modeled. Runtime check kept.")));
                return;
            }
            var arithmeticAssumed = !Z3Verifier.ArithmeticSafetyEntailed(_ctx, solver, [arithmeticSafety]);
            solver.Assert(arithmeticSafety);
            if (solver.Check() == Status.UNSATISFIABLE)
            {
                obligation.ApplyOutcome(ProofOutcome.Assign(ProofEvidence.Unsupported(
                    "No overflow-free state satisfies the modeled obligation assumptions. Runtime check kept.")));
                return;
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
                && (translator.TouchedStringTheory || translator.TouchedNullableReferenceSort || arithmeticAssumed))
            {
                // Name the divergence that ACTUALLY carried the proof. An earlier revision
                // parameterized the assumption list but left the reason hardcoded to the string
                // wording, so an array- or user-type-carried obligation reported "the string
                // model" — on a condition containing no string at all.
                var assumptions = new List<string>();
                var reasons = new List<string>();
                if (arithmeticAssumed)
                {
                    assumptions.Add(Z3Verifier.CheckedArithmeticAssumption);
                    reasons.Add("checked arithmetic completing without overflow");
                }
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

            if (status == Status.SATISFIABLE && !isEntry)
            {
                if (obligation.Kind == ObligationKind.RefinementReturn)
                    inexact.Add("the returned value is not modeled (the solver's `result` is unconstrained)");
                if (!info.Facts.IsExact(obligation.Span))
                    inexact.Add("the path to the obligation is not fully modeled (an enclosing guard, loop, try, or earlier exit is not asserted)");
                if (obligation.Kind == ObligationKind.ProofObligation
                        ? info.Facts.IsStaleBefore(obligation.Condition, obligation.Span)
                        : info.Facts.IsStaleAfterEntry(obligation.Condition))
                    inexact.Add("the obligation reads a variable or heap state the body may change, whose current value is not modeled");
                if (info.Preconditions.Select(pre => pre.Condition)
                        .Concat(info.CollectedFacts.Where(f => f.AppliesTo(obligation.Span)).Select(f => f.Fact))
                        .Append(obligation.Condition)
                        .Any(e => FactCollector.ReferencedNames(e).Overlaps(info.Facts.DroppedFactNames)))
                    inexact.Add("a dropped entry refinement constrains a variable this query reads");
                if (info.Facts.ThrowsEarlierInStatement(obligation.Span))
                    inexact.Add("an operand evaluated before the obligation may throw");
            }
            // #1413 (D-OBL-PROOF-GETTER): entry obligations included.
            if (status == Status.SATISFIABLE && FactCollector.ReadsProperty(obligation.Condition, _propertyNames))
                inexact.Add("the obligation reads a property, whose getter is not modeled");
            if (status == Status.SATISFIABLE && inexact.Count > 0)
            {
                // Not a refutation: the model may describe a state the program never
                // reaches at this obligation (S1 OBL-MUTATION-KILL/-BRANCH-FACTS/
                // -REFINEMENT-RETURN spurious refutations). Report Unsupported — visible,
                // guard kept, no compile error built on a fabricated counterexample.
                obligation.ApplyOutcome(ProofOutcome.Assign(ProofEvidence.Unsupported(
                    "No counterexample established: " + string.Join("; ", inexact.Distinct())
                    + ". Runtime check kept.")));
                return;
            }

            obligation.ApplyOutcome(ProofOutcome.Assign(ProofEvidence.SolverVerdict(
                status, solver.CheckedSolver, solver.TranslateVariables(translator.Variables), SatPolarity.SatIsRefutation)));
        }
        catch (Z3Exception ex)
        {
            obligation.ApplyOutcome(ProofOutcome.Assign(ProofEvidence.SolverError(ex)));
            obligation.SolverDuration = sw.Elapsed;
        }
        finally
        {
            solver?.Dispose();
            if (obligation.Kind is ObligationKind.RefinementEntry or ObligationKind.RefinementReturn
                && obligation.ParameterName != null)
                translator.PopSelfVariable();
        }
    }

    private static Dictionary<string, FunctionInfo> BuildFunctionInfo(ModuleNode module)
    {
        var overloads = FactCollector.OverloadsOperators(module);
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
        var refinementNodes = new Dictionary<string, RefinementTypeNode>(StringComparer.Ordinal);
        foreach (var type in module.RefinementTypes)
            refinementNodes[type.Name] = type;
        var refinementPredicates = refinementNodes.ToDictionary(
            pair => pair.Key,
            pair => ObligationGenerator.EffectiveRefinementPredicate(pair.Value, refinementNodes),
            StringComparer.Ordinal);

        foreach (var func in module.Functions)
        {
            var parameters = func.Parameters
                .Select(p => (p.Name, p.TypeName))
                .ToList();

            // Collect flow-sensitive facts (loop bounds, etc.)
            var factCollector = new FactCollector { OperatorsMayBeOverloaded = overloads };
            factCollector.CollectFromFunction(func, refinementPredicates);

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
                    if (factCollector.AssignedNames.Contains(param.Name))
                        factCollector.MarkAssigned(itype.SizeParam);
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
                factCollector,
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
                var factCollector = new FactCollector { OperatorsMayBeOverloaded = overloads };
                factCollector.CollectFromFunction(method, refinementPredicates);
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
                        if (factCollector.AssignedNames.Contains(param.Name))
                            factCollector.MarkAssigned(indexedType.SizeParam);
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
                    factCollector,
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
                var factCollector = new FactCollector { OperatorsMayBeOverloaded = overloads };
                factCollector.CollectFromCallable(constructor.Parameters, constructor.Body, refinementPredicates);
                result[constructor.Id] = new FunctionInfo(
                    parameters,
                    constructor.Preconditions,
                    null,
                    factCollector,
                    new List<(string, string)>(),
                    refinementTypes);
            }

            foreach (var method in cls.Methods)
            {
                var parameters = method.Parameters
                    .Select(p => (p.Name, p.TypeName))
                    .ToList();
                var factCollector = new FactCollector { OperatorsMayBeOverloaded = overloads };
                factCollector.CollectFromMethod(method, refinementPredicates);
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
                        if (factCollector.AssignedNames.Contains(param.Name))
                            factCollector.MarkAssigned(indexedType.SizeParam);
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
                    factCollector,
                    extraVars,
                    refinementTypes);
            }

            foreach (var operatorOverload in cls.OperatorOverloads)
            {
                var parameters = operatorOverload.Parameters
                    .Select(p => (p.Name, p.TypeName))
                    .ToList();
                var factCollector = new FactCollector { OperatorsMayBeOverloaded = overloads };
                factCollector.CollectFromCallable(operatorOverload.Parameters, operatorOverload.Body, refinementPredicates);
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
                        if (factCollector.AssignedNames.Contains(param.Name))
                            factCollector.MarkAssigned(indexedType.SizeParam);
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
                    factCollector,
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
        FactCollector Facts,
        List<(string Name, string TypeName)> ExtraVariables,
        IReadOnlyDictionary<string, string> RefinementTypes)
    {
        public List<ScopedFact> CollectedFacts => Facts.ScopedFacts;
    }

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
