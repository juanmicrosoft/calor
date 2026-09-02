using Calor.Compiler.Analysis;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects.Manifests;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.Effects;

/// <summary>
/// SCC-based interprocedural effect enforcement pass.
/// Uses Tarjan's algorithm to compute strongly connected components,
/// then processes them in reverse topological order to infer and verify effects.
/// </summary>
public sealed class EffectEnforcementPass
{
    private readonly DiagnosticBag _diagnostics;
    private readonly EffectResolver _resolver;
    private readonly UnknownCallPolicy _policy;
    private readonly bool _strictEffects;
    private readonly HashSet<string> _crossModuleFunctionNames;

    /// <summary>
    /// Default cap on the <see cref="ProcessScc"/> fixpoint: how many rounds over
    /// a recursive SCC the pass will run before it stops and reports
    /// Calor0406. Effect sets only grow and the effect universe is finite, so a
    /// converging SCC needs a bounded number of rounds — but the bound is set by
    /// the SCC's SIZE, not its effect count: a round processes members in
    /// Tarjan's order, so an effect travelling along a forward edge arrives in
    /// the same round while one travelling along a back edge costs a round per
    /// hop, and a doubly-linked chain of n functions sharing ONE effect needs
    /// about n rounds (review of #1114 measured n = 101 hitting a flat cap of
    /// 100). That is why the effective cap, when none is injected, is
    /// <c>max(this, scc.Count + 1)</c> — see <see cref="SccFixpointIterationCap"/>.
    /// </summary>
    public const int DefaultSccFixpointIterationCap = 100;

    /// <summary>
    /// Default cap on the <see cref="PropagateInstantiatedCharges"/> worklist:
    /// how many dequeues the pass will perform before it stops and reports
    /// Calor0406. Counts dequeues, not hops, so a fan-in of N callers on a chain
    /// of depth D costs about N × D.
    /// </summary>
    public const int DefaultInstantiatedChargeIterationCap = 10_000;

    /// <summary>
    /// How many function names a Calor0406 message lists before it says
    /// "and N more".
    /// </summary>
    private const int MaxNamedFunctionsInNonConvergenceMessage = 8;

    /// <summary>
    /// v0.16 W5 (gate 11) — the <see cref="ProcessScc"/> fixpoint cap, injectable
    /// so a pin can drive a three-hop fixture into the cap at 2 rather than
    /// building a hundred-function SCC. Must be at least 1.
    ///
    /// <para>An injected value is used <b>verbatim</b>, so the pins see exactly the
    /// cap they set. When nothing is injected the cap applied to an SCC is
    /// <c>max(DefaultSccFixpointIterationCap, scc.Count + 1)</c>: monotone growth
    /// over an SCC of n members needs at most n − 1 rounds of back-edge travel
    /// plus one confirming round, so with that floor Calor0406 at this site can
    /// only fire for an inference that is not monotone — a bug — never for a
    /// large but well-behaved recursive group.</para>
    /// </summary>
    internal int SccFixpointIterationCap
    {
        get => _sccFixpointIterationCap;
        init
        {
            _sccFixpointIterationCap = value >= 1
                ? value
                : throw new ArgumentOutOfRangeException(nameof(SccFixpointIterationCap), value, "The cap must be at least 1.");
            _sccCapInjected = true;
        }
    }

    /// <summary>
    /// v0.16 W5 (gate 11) — the <see cref="PropagateInstantiatedCharges"/> worklist
    /// cap, injectable for the same reason. Defaults to
    /// <see cref="DefaultInstantiatedChargeIterationCap"/>; must be at least 1.
    /// </summary>
    internal int InstantiatedChargeIterationCap
    {
        get => _instantiatedChargeIterationCap;
        init => _instantiatedChargeIterationCap = value >= 1
            ? value
            : throw new ArgumentOutOfRangeException(nameof(InstantiatedChargeIterationCap), value, "The cap must be at least 1.");
    }

    private readonly int _sccFixpointIterationCap = DefaultSccFixpointIterationCap;
    private readonly bool _sccCapInjected;
    private readonly int _instantiatedChargeIterationCap = DefaultInstantiatedChargeIterationCap;

    /// <summary>
    /// The cap <see cref="ProcessScc"/> applies to an SCC of <paramref name="sccSize"/>
    /// members: the injected value verbatim, else the default floored at
    /// <c>sccSize + 1</c> (see <see cref="SccFixpointIterationCap"/>).
    /// </summary>
    internal int EffectiveSccFixpointIterationCap(int sccSize) =>
        _sccCapInjected
            ? _sccFixpointIterationCap
            : Math.Max(DefaultSccFixpointIterationCap, sccSize + 1);

    // Delegated call graph analysis (populated by Enforce)
    private CallGraphAnalysis _callGraphAnalysis = null!;

    // Maps function ID to computed effects
    private readonly Dictionary<string, EffectSet> _computedEffects = new(StringComparer.Ordinal);

    // D-W2.3: per-function assumption provenance (function ID → reasons). A function
    // with entries here has effects that are ASSUMED, not verified (C# interop content,
    // an unrecognized construct, an uncheckable external base, or a call into any of
    // those). The marker propagates to callers after SCC processing and is surfaced
    // per function via Calor0419 — never silently pure.
    private readonly Dictionary<string, List<string>> _assumedEffects = new(StringComparer.Ordinal);
    private readonly HashSet<string> _onDemandInference = new(StringComparer.Ordinal);

    // v0.15 E5 (design-doc §8.5) — the per-declaration RESULT of this pass, keyed
    // the way the pass keys everything: by the declaration's structural id
    // (`f001`, `Cls.m001`), never by name. Filled once per Enforce, after phase 4.
    private readonly Dictionary<string, DeclarationEffectFact> _declarationFacts =
        new(StringComparer.Ordinal);

    // v0.15 E5 (review round 1, #2) — the VARIABLE part of what a body was
    // charged: the caller's own `eff` binders that reached it through an
    // invoked value's row (ChargeInvokedRow) or through a rank-1 instantiation's
    // residual (InstantiateAndCharge). `_computedEffects` is an EffectSet and
    // cannot carry `e`; without this record the inferred row of `Map<eff e>`
    // printed as [pure]. Bookkeeping only — no charge or diagnostic changes.
    private readonly Dictionary<string, SortedDictionary<int, string>> _chargedVariables =
        new(StringComparer.Ordinal);

    private void RecordVariableCharge(string functionId, int ordinal, string name)
    {
        if (!_chargedVariables.TryGetValue(functionId, out var variables))
        {
            variables = new SortedDictionary<int, string>();
            _chargedVariables[functionId] = variables;
        }
        variables.TryAdd(ordinal, name);
    }

    /// <summary>
    /// v0.15 E5, design-doc §8.5 ("one producer, two consumers") — what this pass
    /// concluded about ONE declaration: the row it declared, the row inference
    /// computed for its body, and the verdict between them together with the
    /// diagnostic code phase 4 reports for it. The project index's effects facet
    /// and <c>calor query effects</c> read this; nothing re-runs inference to
    /// answer them.
    ///
    /// <para><see cref="Verdict"/> is the pass's OWN verdict, not a second
    /// computation: <see cref="Binding.BoundTypes.EffectFit.DoesNotFit"/> exactly
    /// when phase 4 reports a forbidden effect, <see cref="Binding.BoundTypes.EffectFit.CannotTell"/>
    /// when the computed set is <c>Unknown</c> (phase 4 still reports Calor0410
    /// <c>'unknown'</c> there — the fail-closed answer of §3.5, which is why
    /// <see cref="DiagnosticCode"/> travels beside the verdict rather than being
    /// derived from it), and <see cref="Binding.BoundTypes.EffectFit.Fits"/>
    /// otherwise — with Calor0419 as the code when the fit rests on assumptions.</para>
    /// </summary>
    /// <param name="FunctionId">The pass's key: <c>FunctionNode.Id</c>, class-qualified for members.</param>
    /// <param name="Name">Display name, as the diagnostics spell it. Not a key.</param>
    /// <param name="Kind"><c>function</c>, <c>method</c>, <c>constructor</c> or <c>accessor</c>.</param>
    /// <param name="HasDeclaration">Whether a <c>§E</c> was written. Constructors and
    /// implicit accessors have no <c>§E</c> surface; their declared row is the
    /// intrinsic contract phase 4 checks against.</param>
    /// <param name="DeclaredRow">The declared row's concrete part (§3.5: an omitted
    /// row on a declaration is pure).</param>
    /// <param name="DeclaredVariables">The <c>eff</c> binders the declared row
    /// mentions, by ordinal within the declaration's own <c>eff</c> list (§7).</param>
    /// <param name="InferredRow">The body's computed effects as a row: Concrete,
    /// Assumed with the D-W2.3 reasons, or Unknown.</param>
    /// <param name="InferredVariables">The declaration's own <c>eff</c> binders the
    /// body was charged — through an invoked value's polymorphic row or a rank-1
    /// instantiation's residual — by ordinal and name. Empty for a monomorphic body.</param>
    /// <param name="Verdict">See the summary.</param>
    /// <param name="DiagnosticCode">The code phase 4 reports for this declaration, or null.</param>
    /// <param name="Forbidden">Surface codes the body uses and the declaration does not cover.</param>
    /// <param name="Span">The declaration's span.</param>
    /// <param name="EffectsSpan">The <c>§E</c> node's span, when one was written.</param>
    public sealed record DeclarationEffectFact(
        string FunctionId,
        string Name,
        string Kind,
        bool HasDeclaration,
        Binding.BoundTypes.EffectRow DeclaredRow,
        IReadOnlyList<KeyValuePair<int, string>> DeclaredVariables,
        Binding.BoundTypes.EffectRow InferredRow,
        IReadOnlyList<KeyValuePair<int, string>> InferredVariables,
        Binding.BoundTypes.EffectFit Verdict,
        string? DiagnosticCode,
        IReadOnlyList<string> Forbidden,
        TextSpan Span,
        TextSpan? EffectsSpan);

    /// <summary>
    /// v0.15 E5 — the per-declaration facts of the last <see cref="Enforce"/>,
    /// keyed by structural function id. Empty before the first call.
    /// </summary>
    public IReadOnlyDictionary<string, DeclarationEffectFact> DeclarationFacts => _declarationFacts;

    // Module-shape lookups for delegate detection (D-W2.1), static-receiver
    // call-site charging and declaration-local effect variance (D-W2.2).
    private Dictionary<string, ClassDefinitionNode> _classesByName = new(StringComparer.Ordinal);
    private Dictionary<string, InterfaceDefinitionNode> _interfacesByName = new(StringComparer.Ordinal);
    private HashSet<string> _delegateTypeNames = new(StringComparer.Ordinal);
    private Dictionary<string, ClassDefinitionNode> _ownerClassByFunctionId = new(StringComparer.Ordinal);
    private Dictionary<string, ConstructorDeclaration> _constructorsByFunctionId = new(StringComparer.Ordinal);
    private Dictionary<string, ImplicitAccessorDeclaration> _implicitAccessorsByFunctionId = new(StringComparer.Ordinal);

    private sealed record ConstructorDeclaration(
        string OwnerName,
        ClassDefinitionNode Owner,
        ConstructorNode Constructor);

    private sealed record ImplicitAccessorDeclaration(
        string OwnerName,
        string MemberName,
        string Kind,
        AstNode Declaration);

    public EffectEnforcementPass(
        DiagnosticBag diagnostics,
        UnknownCallPolicy policy = UnknownCallPolicy.Strict,
        EffectResolver? resolver = null,
        bool strictEffects = false,
        string? projectDirectory = null,
        string? solutionDirectory = null,
        IEnumerable<string>? crossModuleFunctionNames = null)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _policy = policy;
        _strictEffects = strictEffects;
        _invocations = new InvocationSink(this);
        // Bare public function names exported by OTHER modules in the same
        // multi-file compilation (unambiguous names only, from the driver's
        // pre-parse). Calls to these are legitimately bare per-module; the
        // cross-module pass charges their declared effects.
        _crossModuleFunctionNames = crossModuleFunctionNames != null
            ? new HashSet<string>(crossModuleFunctionNames, StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

        // Initialize the effect resolver with manifests
        _resolver = resolver ?? new EffectResolver();
        _resolver.Initialize(projectDirectory, solutionDirectory);
    }

    /// <summary>
    /// Enforces effect declarations across all functions and class methods in the module.
    /// </summary>
    public void Enforce(ModuleNode module)
    {
        _chargedVariables.Clear();

        // Phase 1: Build function map and call graph (includes functions and methods)
        _callGraphAnalysis = CallGraphAnalysis.Build(module);

        // Phase 1b: index module shape for delegate detection, static-receiver
        // resolution and variance checks. All enumerations include §PP-wrapped
        // types/members (W2 review C1).
        _classesByName = new Dictionary<string, ClassDefinitionNode>(StringComparer.Ordinal);
        foreach (var cls in CallGraphAnalysis.EnumerateClasses(module))
            _classesByName[cls.Name] = cls;
        _interfacesByName = new Dictionary<string, InterfaceDefinitionNode>(StringComparer.Ordinal);
        foreach (var iface in CallGraphAnalysis.EnumerateInterfaces(module))
            _interfacesByName[iface.Name] = iface;
        _delegateTypeNames = new HashSet<string>(
            CallGraphAnalysis.EnumerateDelegates(module).Select(d => d.Name), StringComparer.Ordinal);
        _ownerClassByFunctionId = new Dictionary<string, ClassDefinitionNode>(StringComparer.Ordinal);
        _constructorsByFunctionId = new Dictionary<string, ConstructorDeclaration>(StringComparer.Ordinal);
        _implicitAccessorsByFunctionId = new Dictionary<string, ImplicitAccessorDeclaration>(StringComparer.Ordinal);
        foreach (var cls in CallGraphAnalysis.EnumerateClasses(module))
        {
            foreach (var method in CallGraphAnalysis.EnumerateMethods(cls))
                _ownerClassByFunctionId[$"{cls.Name}.{method.Id}"] = cls;
            foreach (var ctor in CallGraphAnalysis.EnumerateConstructors(cls))
            {
                _ownerClassByFunctionId[$"{cls.Name}.{ctor.Id}"] = cls;
                _constructorsByFunctionId[$"{cls.Name}.{ctor.Id}"] =
                    new ConstructorDeclaration(cls.Name, cls, ctor);
            }
            foreach (var property in CallGraphAnalysis.EnumerateProperties(cls))
            {
                foreach (var accessor in new[] { property.Setter, property.Initer }.Where(a => a != null))
                {
                    var id = CallGraphAnalysis.GetPropertyAccessorFunctionId(cls.Name, property, accessor!);
                    _ownerClassByFunctionId[id] = cls;
                    _implicitAccessorsByFunctionId[id] = new ImplicitAccessorDeclaration(
                        cls.Name,
                        property.Name,
                        accessor!.Kind.ToString().ToLowerInvariant(),
                        accessor);
                }
            }
            foreach (var evt in CallGraphAnalysis.EnumerateEvents(cls))
            {
                if (evt.AddBody != null)
                {
                    var id = CallGraphAnalysis.GetEventAccessorFunctionId(cls.Name, evt, isAdd: true);
                    _ownerClassByFunctionId[id] = cls;
                    _implicitAccessorsByFunctionId[id] =
                        new ImplicitAccessorDeclaration(cls.Name, evt.Name, "add", evt);
                }
                if (evt.RemoveBody != null)
                {
                    var id = CallGraphAnalysis.GetEventAccessorFunctionId(cls.Name, evt, isAdd: false);
                    _ownerClassByFunctionId[id] = cls;
                    _implicitAccessorsByFunctionId[id] =
                        new ImplicitAccessorDeclaration(cls.Name, evt.Name, "remove", evt);
                }
            }
        }

        // Phase 2+3: Process SCCs in reverse topological order
        // (Tarjan produces them in reverse topological order already)
        foreach (var scc in _callGraphAnalysis.StronglyConnectedComponents)
        {
            ProcessScc(scc);
        }

        // Phase 3b (D-W2.2): declaration-local effect-variance checks
        // (override §E ⊆ base §E; implementation §E ⊆ interface §E). External
        // bases route their overrides to the Assumed channel below.
        CheckEffectVariance(module);

        // Phase 3c (D-W2.3): propagate assumption provenance to callers — a caller
        // of an assumed-effect function inherits the assumption transitively.
        PropagateAssumptions();

        // Phase 3d (v0.15 E3 slice a): effect-row compatibility at §6.2 sites 1-3
        // (assignment, argument, return). AFTER PropagateAssumptions, because a
        // method group's row is Assumed exactly when this pass could only assume
        // its declaration, and that fact is not settled until assumptions have
        // propagated through the call graph.
        CheckRowCompatibility();

        // Phase 3d' (v0.15 E3 slice b, review round 1 finding 1): site-6 charges
        // reach TRANSITIVE callers. Must run before phase 4, which is where
        // Calor0410 reads _computedEffects.
        PropagateInstantiatedCharges();

        // Phase 3e (v0.15 E3 slice b): design-doc §5 / P14 — every §LAM's
        // DECLARED row against its BODY's row. After inference, so ρ_body is the
        // converged answer and not an SCC iteration's.
        CheckLambdaDeclaredRows();

        // Phase 4: Check every executable body. Constructors and custom accessors
        // have no §E declaration surface, so they use an explicit fail-closed
        // contract: intrinsic initialization/accessor mutation is allowed; every
        // other effect is rejected and must be moved behind a declared method.
        foreach (var function in _callGraphAnalysis.Functions.Values)
        {
            if (_constructorsByFunctionId.TryGetValue(function.Id, out var constructor))
                CheckImplicitEffectBody(function, constructor.Constructor,
                    DiagnosticCode.ConstructorEffectContractUnavailable,
                    $"constructor '{constructor.OwnerName}.{constructor.Constructor.Id}'",
                    EffectSet.From("mut", "alloc"),
                    "intrinsic initialization mutation/allocation ('mut', 'alloc')");
            else if (_implicitAccessorsByFunctionId.TryGetValue(function.Id, out var accessor))
                CheckImplicitEffectBody(function, accessor.Declaration,
                    DiagnosticCode.AccessorEffectContractUnavailable,
                    $"{accessor.Kind} accessor '{accessor.OwnerName}.{accessor.MemberName}'",
                    EffectSet.From("mut"),
                    "intrinsic accessor mutation ('mut')");
            else
                CheckEffects(function);
        }

        // Phase 5 (v0.15 E5, design-doc §8.5): project the per-declaration
        // result. Read-only over what phases 2-4 left behind — this is a
        // projection of the pass's answer, not a second inference.
        ProjectDeclarationFacts();
    }

    /// <summary>
    /// Fills <see cref="DeclarationFacts"/> from <c>_computedEffects</c>,
    /// <c>_assumedEffects</c> and each declaration's <c>§E</c>, using the SAME
    /// conditions <see cref="CheckEffects"/> and <see cref="CheckImplicitEffectBody"/>
    /// report on, so a fact's verdict and code are what the CLI printed.
    /// </summary>
    private void ProjectDeclarationFacts()
    {
        _declarationFacts.Clear();
        foreach (var function in _callGraphAnalysis.Functions.Values)
        {
            string kind;
            string mismatchCode;
            EffectSet declaredSet;
            bool hasDeclaration;
            if (_constructorsByFunctionId.ContainsKey(function.Id))
            {
                kind = "constructor";
                mismatchCode = DiagnosticCode.ConstructorEffectContractUnavailable;
                declaredSet = EffectSet.From("mut", "alloc");
                hasDeclaration = false;
            }
            else if (_implicitAccessorsByFunctionId.ContainsKey(function.Id))
            {
                kind = "accessor";
                mismatchCode = DiagnosticCode.AccessorEffectContractUnavailable;
                declaredSet = EffectSet.From("mut");
                hasDeclaration = false;
            }
            else
            {
                // Member ids are class-qualified (`Cls.m001`, `Cls.p001.get`); a
                // getter with a body is a member too, though it is registered in
                // no owner map (it has no implicit contract to check).
                kind = _ownerClassByFunctionId.ContainsKey(function.Id) || function.Id.Contains('.')
                    ? "method"
                    : "function";
                mismatchCode = DiagnosticCode.ForbiddenEffect;
                declaredSet = GetDeclaredEffects(function);
                hasDeclaration = function.Effects != null;
            }

            // Except() is the difference CheckEffects reports (subtyping included);
            // it is empty exactly when the subset test there passes, so the
            // body-vs-declaration relation is asked once, in phase 4 (P16 counts
            // its call sites).
            var computed = _computedEffects.GetValueOrDefault(function.Id, EffectSet.Empty);
            var forbidden = computed.Except(declaredSet)
                .Select(effect => EffectSetExtensions.ToSurfaceCode(effect.Kind, effect.Value))
                .OrderBy(code => code, StringComparer.Ordinal)
                .ToList();
            var reasons = _assumedEffects.TryGetValue(function.Id, out var assumed) && assumed.Count > 0
                ? assumed
                : (IReadOnlyList<string>)Array.Empty<string>();

            var inferredRow = computed.IsUnknown
                ? Binding.BoundTypes.EffectRow.Unknown
                : reasons.Count > 0
                    ? Binding.BoundTypes.EffectRow.Assumed(computed.ToRow().Codes, reasons)
                    : computed.ToRow();
            var verdict = computed.IsUnknown
                ? Binding.BoundTypes.EffectFit.CannotTell
                : forbidden.Count > 0
                    ? Binding.BoundTypes.EffectFit.DoesNotFit
                    : Binding.BoundTypes.EffectFit.Fits;
            // Same order as phase 4: a forbidden effect is reported before (and
            // independently of) an assumption; constructors and accessors fold
            // both into their one contract-unavailable code.
            var code = forbidden.Count > 0
                ? mismatchCode
                : reasons.Count > 0
                    ? (hasDeclaration || kind is "function" or "method"
                        ? DiagnosticCode.AssumedEffects
                        : mismatchCode)
                    : null;

            var variables = new List<KeyValuePair<int, string>>();
            if (function.Effects != null)
            {
                for (var index = 0; index < function.Effects.EffectVariables.Count; index++)
                {
                    var ordinal = index < function.Effects.EffectVariableOrdinals.Count
                        ? function.Effects.EffectVariableOrdinals[index]
                        : -1;
                    variables.Add(new KeyValuePair<int, string>(ordinal, function.Effects.EffectVariables[index]));
                }
            }

            var inferredVariables = _chargedVariables.TryGetValue(function.Id, out var charged)
                ? charged.Select(pair => new KeyValuePair<int, string>(pair.Key, pair.Value)).ToList()
                : new List<KeyValuePair<int, string>>();

            _declarationFacts[function.Id] = new DeclarationEffectFact(
                function.Id,
                function.Name,
                kind,
                hasDeclaration,
                declaredSet.ToRow(),
                variables,
                inferredRow,
                inferredVariables,
                verdict,
                code,
                forbidden,
                function.Span,
                function.Effects?.Span);
        }
    }

    /// <summary>
    /// Resolves a call target string to an internal function ID.
    /// Thin wrapper that delegates to CallGraphAnalysis.
    /// </summary>
    private string? ResolveToInternalId(string callee)
    {
        return _callGraphAnalysis.ResolveToInternalId(callee);
    }

    /// <summary>
    /// Whether a function calls itself directly. Tarjan reports a self-recursive function as a
    /// singleton SCC exactly as it reports a non-recursive one, so the self-edge has to be asked
    /// for separately — the distinction the two branches of <see cref="ProcessScc"/> turn on.
    /// </summary>
    private bool HasSelfEdge(string functionId)
    {
        foreach (var (calleeId, _, _) in _callGraphAnalysis.GetCallees(functionId))
        {
            if (string.Equals(calleeId, functionId, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private void ProcessScc(List<string> scc)
    {
        // For single-function SCCs with no self-recursion, compute effects directly.
        //
        // The self-edge test is load-bearing and was missing: this comment claimed it and the
        // code did not do it. A directly self-recursive function is its own singleton SCC, so it
        // took this branch with an EMPTY member set — the recursive call then failed the
        // `SccMembers.Contains` test, found no entry in `_computedEffects` (it is being computed),
        // and fell through to the unknown-call path. Result: `Calor0411 Unknown call target 'Fact'`
        // on a function defined ten lines above, then `Calor0410 uses effect 'Unknown:*'`, which
        // cannot be declared away — every directly self-recursive function failed to compile,
        // on `build`, `run`, `test` and the MCP tools alike. Mutual recursion was unaffected
        // because an SCC of size >= 2 populates the set.
        //
        // A self-recursive singleton goes through the fixpoint loop below, which seeds
        // `EffectSet.Empty` and iterates — the same treatment mutual recursion already got.
        if (scc.Count == 1 && !HasSelfEdge(scc[0]))
        {
            var functionId = scc[0];
            var function = _callGraphAnalysis.Functions[functionId];
            var effects = InferEffects(function, new HashSet<string>());
            _computedEffects[functionId] = effects;
            return;
        }

        // For recursive SCCs — mutual, or a single function calling itself — iterate to fixpoint
        var changed = true;
        var iterations = 0;
        var maxIterations = EffectiveSccFixpointIterationCap(scc.Count);

        // Initialize with empty effects
        foreach (var functionId in scc)
        {
            _computedEffects[functionId] = EffectSet.Empty;
        }

        while (changed && iterations < maxIterations)
        {
            changed = false;
            iterations++;

            foreach (var functionId in scc)
            {
                var function = _callGraphAnalysis.Functions[functionId];
                var newEffects = InferEffects(function, new HashSet<string>(scc));
                var oldEffects = _computedEffects[functionId];

                if (!newEffects.Equals(oldEffects))
                {
                    _computedEffects[functionId] = newEffects;
                    changed = true;
                }
            }
        }

        // v0.16 W5 (gate 11): the loop above exits either because a full round
        // changed nothing (converged) or because the cap was reached with the
        // last round still changing. The second case used to be reported as the
        // API-strictness code Calor0600 (a warning); it is now Calor0406, an
        // error, because an SCC whose sets were still growing may be missing
        // effects and any Calor0410 not reported for its members is unsound.
        if (changed)
        {
            ReportDidNotConverge(
                _callGraphAnalysis.Functions[scc[0]].Span,
                "SCC fixpoint",
                maxIterations,
                "rounds",
                scc.Select(id => _callGraphAnalysis.Functions[id].Name),
                "the effects computed for this recursive group may be incomplete");
        }
    }

    /// <summary>
    /// Calor0406 at one of the two capped sites. The message names the site, the
    /// cap, and the functions involved so the reader can tell which of the two
    /// loops stopped and what to split; the function list is truncated after
    /// <see cref="MaxNamedFunctionsInNonConvergenceMessage"/> names.
    /// </summary>
    private void ReportDidNotConverge(
        TextSpan span,
        string site,
        int cap,
        string unit,
        IEnumerable<string> functionNames,
        string consequence)
    {
        var names = functionNames.ToList();
        var shown = names.Take(MaxNamedFunctionsInNonConvergenceMessage)
            .Select(n => $"'{n}'");
        var list = string.Join(", ", shown);
        if (names.Count > MaxNamedFunctionsInNonConvergenceMessage)
            list += $", and {names.Count - MaxNamedFunctionsInNonConvergenceMessage} more";

        _diagnostics.Report(
            span,
            DiagnosticCode.EffectInferenceDidNotConverge,
            $"Effect inference did not converge: the {site} stopped at its cap of {cap} {unit} "
            + $"with the effect sets still changing; {consequence}. Functions involved: {list}.",
            DiagnosticSeverity.Error);
    }

    private EffectSet InferEffects(FunctionNode function, HashSet<string> sccMembers)
    {
        var assumptions = new List<string>();
        var context = new InferenceContext(
            _resolver, _computedEffects,
            _callGraphAnalysis.Functions,
            _callGraphAnalysis.FunctionNameToId,
            _callGraphAnalysis.MethodNameToIds,
            _callGraphAnalysis,
            sccMembers, _policy, _strictEffects, _diagnostics, function.Id,
            _crossModuleFunctionNames,
            _classesByName, _interfacesByName, _delegateTypeNames,
            _ownerClassByFunctionId.GetValueOrDefault(function.Id),
            _constructorsByFunctionId.GetValueOrDefault(function.Id),
            ResolveInternalEffectsOnDemand,
            assumptions)
        {
            RecordLambdaBody = (lambda, functionId, body, reasons) =>
                _lambdaBodyRows[lambda] = new LambdaBodyFact(functionId, body, reasons),
            LambdaBodyRow = LambdaBodyRow,
            Invocations = _invocations,
            AstResolution = AstResolution,
        };
        var inferrer = new EffectInferrer(context);
        var effects = inferrer.InferFromStatements(function.Body);
        if (context.CurrentConstructor is { Constructor.IsStatic: false }
            && context.CurrentConstructor.Constructor.Initializer != null)
        {
            effects = effects.Union(inferrer.InferFromConstructorInitializer(
                context.CurrentConstructor));
        }
        else if (context.CurrentConstructor is { Constructor.IsStatic: false }
                 && context.CurrentConstructor.Owner.BaseClass != null)
        {
            effects = effects.Union(inferrer.InferFromImplicitBaseConstructor(
                context.CurrentConstructor));
        }

        // Direct in-body assumption reasons REPLACE previous ones so SCC fixpoint
        // iterations don't accumulate duplicates. (Variance and propagation reasons
        // are added after all SCCs are processed.)
        if (assumptions.Count > 0)
            _assumedEffects[function.Id] = assumptions;
        else
            _assumedEffects.Remove(function.Id);

        return effects;
    }

    private EffectSet ResolveInternalEffectsOnDemand(string functionId)
    {
        if (_computedEffects.TryGetValue(functionId, out var computed))
            return computed;
        if (!_callGraphAnalysis.Functions.TryGetValue(functionId, out var function))
            return EffectSet.Unknown;
        if (!_onDemandInference.Add(functionId))
            return _computedEffects.GetValueOrDefault(functionId, EffectSet.Empty);

        try
        {
            var effects = InferEffects(function, [functionId]);
            _computedEffects[functionId] = effects;
            return effects;
        }
        finally
        {
            _onDemandInference.Remove(functionId);
        }
    }

    private void CheckImplicitEffectBody(
        FunctionNode function,
        AstNode declaration,
        string diagnosticCode,
        string displayName,
        EffectSet intrinsicEffects,
        string intrinsicDescription)
    {
        var computedEffects = _computedEffects.GetValueOrDefault(function.Id, EffectSet.Empty);
        var forbidden = computedEffects.Except(intrinsicEffects).ToList();
        if (forbidden.Count > 0)
        {
            var codes = string.Join(", ",
                forbidden.Select(effect => EffectCodes.ToCompact(effect.Kind, effect.Value)));
            _diagnostics.Report(
                declaration.Span,
                diagnosticCode,
                $"The {displayName} uses effect(s) '{codes}', but this declaration has no §E effect-contract surface. " +
                $"Only {intrinsicDescription} is permitted. " +
                "Move effectful work to a method with an explicit §E declaration.",
                DiagnosticSeverity.Error);
        }

        if (_assumedEffects.TryGetValue(function.Id, out var reasons) && reasons.Count > 0)
        {
            _diagnostics.Report(
                declaration.Span,
                diagnosticCode,
                $"The {displayName} has unverifiable effects: {string.Join("; ", reasons.Take(3))}. " +
                "Declarations without a §E surface fail closed.",
                DiagnosticSeverity.Error);
        }
    }

    private void CheckEffects(FunctionNode function)
    {
        var declaredEffects = GetDeclaredEffects(function);
        var computedEffects = _computedEffects.GetValueOrDefault(function.Id, EffectSet.Empty);

        // Check if computed effects are a subset of declared effects
        if (!computedEffects.IsSubsetOf(declaredEffects))
        {
            var forbidden = computedEffects.Except(declaredEffects).ToList();

            // In permissive mode, demote forbidden-effect errors to warnings
            var severity = _policy == UnknownCallPolicy.Permissive
                ? DiagnosticSeverity.Warning
                : DiagnosticSeverity.Error;

            // Compute the full correct effect set for the fix
            var correctEffects = declaredEffects.Union(computedEffects);
            // §E{} syntax uses comma-separated codes without spaces
            var correctEffectStr = correctEffects.ToDisplayString().Replace(", ", ",");
            var fixSpan = function.Effects?.Span ?? function.Span;
            var filePath = _diagnostics.CurrentFilePath ?? "unknown";

            // Generate fix: replace existing §E{...} line or insert new one
            SuggestedFix? fix = null;
            if (function.Effects != null)
            {
                // Replace the entire §E{...} line to avoid span-length issues
                // §E{...} always occupies its own line with leading whitespace
                var effectLine = function.Effects.Span.Line;
                fix = new SuggestedFix(
                    $"Update effect declaration to §E{{{correctEffectStr}}}",
                    TextEdit.Replace(filePath,
                        effectLine, 1,
                        effectLine + 1, 1,
                        $"  §E{{{correctEffectStr}}}\n"));
            }
            else
            {
                // Insert §E{...} after the last §O or §I line
                var insertLine = function.Span.Line + 1; // Default: after function declaration
                if (function.Output != null)
                    insertLine = function.Output.Span.Line + 1;
                else if (function.Parameters.Count > 0)
                    insertLine = function.Parameters[^1].Span.Line + 1;

                fix = new SuggestedFix(
                    $"Add effect declaration §E{{{correctEffectStr}}}",
                    TextEdit.Insert(filePath, insertLine, 1, $"  §E{{{correctEffectStr}}}\n"));
            }

            foreach (var (kind, value) in forbidden)
            {
                // Find the call chain that leads to this effect
                var chain = FindCallChain(function.Id, kind, value);
                var chainStr = chain.Count > 0 ? $"\n  Call chain: {string.Join(" → ", chain)}" : "";

                // v0.15 E3 slice b, design-doc §10.3 — when the effect arrived
                // through a rank-1 instantiation, say so. Today's Calor0410 can
                // only report that a call happened; with rows it can report that
                // the call was to something whose effect variable resolved to
                // this effect, and which argument decided that.
                var surface = EffectSetExtensions.ToSurfaceCode(kind, value);
                var rowStr =
                    _rank1Provenance.TryGetValue(function.Id, out var byCode)
                    && byCode.TryGetValue(surface, out var why)
                        ? $"\n  Effect row: {why}"
                        : "";

                var message = $"Function '{function.Name}' uses effect '{surface}' but does not declare it{rowStr}{chainStr}";

                if (fix != null)
                {
                    _diagnostics.ReportWithFix(
                        fixSpan,
                        DiagnosticCode.ForbiddenEffect,
                        message,
                        fix,
                        severity);
                    fix = null; // Only attach fix to the first forbidden effect diagnostic
                }
                else
                {
                    _diagnostics.Report(fixSpan, DiagnosticCode.ForbiddenEffect, message, severity);
                }
            }
        }

        // D-W2.3: surface assumption provenance. An assumed-effect function satisfies
        // its declaration only WITH the assumption named; strict mode fails loud.
        if (_assumedEffects.TryGetValue(function.Id, out var reasons) && reasons.Count > 0)
        {
            var assumedSeverity = _strictEffects
                ? DiagnosticSeverity.Error
                : DiagnosticSeverity.Warning;
            var shown = string.Join("; ", reasons.Take(3));
            if (reasons.Count > 3)
                shown += $"; and {reasons.Count - 3} more";
            _diagnostics.Report(
                function.Effects?.Span ?? function.Span,
                DiagnosticCode.AssumedEffects,
                $"Effects of '{function.Name}' are ASSUMED, not verified: {shown}. " +
                "The declared effect set is accepted as an assumption, not a proof; " +
                "narrow the interop surface or add manifest coverage to restore verification.",
                assumedSeverity);
        }
    }

    private EffectSet GetDeclaredEffects(FunctionNode function) => GetDeclaredEffects(function.Effects);

    /// <summary>
    /// Computes the declared effect set from an §E node. A missing declaration is the
    /// empty (pure) set — consistent with per-function enforcement, where an
    /// undeclared function may not exhibit any effect.
    /// </summary>
    internal static EffectSet GetDeclaredEffects(EffectsNode? effectsNode)
    {
        if (effectsNode == null || effectsNode.Effects.Count == 0)
        {
            return EffectSet.Empty;
        }

        // The EffectsNode.Effects dictionary is populated by InterpretEffectsAttributes/ExpandEffectCode
        // in the parser. Keys are categories ("io", "mutation", etc.) and values are internal names
        // ("console_write", "database_write") — potentially comma-separated for multiple effects
        // in the same category.
        //
        // We build (EffectKind, string) tuples directly to match the internal representation
        // used by the enforcement pass and manifest resolver.
        var effects = new List<(EffectKind Kind, string Value)>();
        foreach (var kv in effectsNode.Effects)
        {
            var kind = ParseEffectCategory(kv.Key);
            var values = kv.Value.Split(',');
            foreach (var value in values)
            {
                var trimmedValue = value.Trim();
                if (!string.IsNullOrEmpty(trimmedValue))
                {
                    effects.Add((kind, trimmedValue));
                }
            }
        }
        return EffectSet.FromInternal(effects);
    }

    /// <summary>
    /// D-W2.2 — declaration-local effect-variance checks (behavioral subtyping):
    /// an override may declare only effects covered by its base method's declared
    /// set (Calor0420), and an interface implementation only effects covered by the
    /// interface method's declared set (Calor0421). A missing §E declaration is the
    /// pure contract, consistent with per-function enforcement. Overrides whose base
    /// class is external C# (not in this module) cannot be checked and route to the
    /// Assumed channel (Calor0419). Implementations of external interfaces are not
    /// per-method attributable and are outside the declared-variance surface; calls
    /// through such receivers hit the unknown-call chain, which fails loud.
    /// </summary>
    private void CheckEffectVariance(ModuleNode module)
    {
        // v0.15 E3 slice a, design-doc §4.5 — the permissive DEMOTION is GONE.
        // A `DoesNotFit` verdict is never waived, at any of the six sites, by any
        // flag: `--permissive-effects` waives only "we cannot tell" (Calor0425).
        // These two sites are sites 4 and 5, so their verdict is an Error whatever
        // the policy is. (Priced before the change: no test asserted the demotion,
        // and no committed .calr depends on it — gate 5's corpus legs.)
        const DiagnosticSeverity varianceSeverity = DiagnosticSeverity.Error;

        foreach (var cls in CallGraphAnalysis.EnumerateClasses(module))
        {
            foreach (var method in CallGraphAnalysis.EnumerateMethods(cls))
            {
                if (!method.IsOverride)
                    continue;

                var (baseMethod, baseClassName) = FindBaseMethod(cls, method);
                if (baseMethod != null)
                {
                    var overrideDeclared = GetDeclaredEffects(method.Effects);
                    var baseDeclared = GetDeclaredEffects(baseMethod.Effects);
                    // Site 4 (§6.2). ONE relation across all three codes: the same
                    // EffectRow.Fits that answers Calor0424 at sites 1-3 answers
                    // here, and Calor0420 is the site-specific spelling of its
                    // DoesNotFit (§6.3). P16 pins that they move together.
                    // v0.15 E3 slice b — the rows carry their `eff` VARIABLES
                    // here, by ORDINAL, so an override's `eff f` is identified
                    // with its base's `eff e` (§7.5's R2) by the ORDINARY fits
                    // relation and not by a rank-1-specific branch. Slice a
                    // computed from EffectSet, where a variable contributes
                    // nothing, and therefore never compared them at all.
                    var overrideRow = PolyRow.FromDeclaration(method.Effects);
                    var baseRow = PolyRow.FromDeclaration(baseMethod.Effects);
                    var verdict = PolyRow.Fits(overrideRow, baseRow);
                    if (verdict == Binding.BoundTypes.EffectFit.DoesNotFit)
                    {
                        var extraVariables = overrideRow.ExtraVariables(baseRow).ToList();
                        var extra = overrideDeclared.Except(baseDeclared)
                            .Select(e => EffectSetExtensions.ToSurfaceCode(e.Kind, e.Value))
                            .Concat(extraVariables);
                        var positionNote = extraVariables.Count == 0
                            ? ""
                            : " Effect variables are matched BY POSITION in the declaration's "
                              + "'eff' list, not by name.";
                        _diagnostics.Report(
                            method.Effects?.Span ?? method.Span,
                            DiagnosticCode.OverrideEffectVariance,
                            $"Override '{cls.Name}.{method.Name}' declares effect(s) [{string.Join(", ", extra)}] " +
                            $"not declared by base method '{baseClassName}.{method.Name}' " +
                            $"(base declares: {baseDeclared.ToDisplayString()}). " +
                            $"Effect row {overrideRow.Display()} does not fit the base method's " +
                            $"row {baseRow.Display()}. " +
                            "An override may not broaden its base method's effect set — broader effects " +
                            "would launder through dynamic dispatch." + positionNote,
                            varianceSeverity);
                    }
                    else if (verdict == Binding.BoundTypes.EffectFit.CannotTell)
                    {
                        ReportRowUnknown(
                            method.Effects?.Span ?? method.Span,
                            $"Override '{cls.Name}.{method.Name}' has effect row "
                            + $"{overrideRow.Display()} and base method "
                            + $"'{baseClassName}.{method.Name}' has row "
                            + $"{baseRow.Display()}, so effect variance cannot be decided. "
                            + "State a row on both, or compile with --permissive-effects.");
                    }
                }
                else if (baseClassName != null)
                {
                    // v0.15 E3 slice b, design-doc §6.2 — the FIRST of the two
                    // external-base Calor0419s, retired in favour of Calor0425.
                    // Slice a took neither, because they must move together: this
                    // one was an AddAssumption whose reasons propagate through
                    // PropagateAssumptions into every caller's Calor0419, and
                    // converting only its interface sibling would make sites 4 and
                    // 5 disagree about what an unresolvable base means. The
                    // assumption channel carries REASONS, not effects (see
                    // AddAssumption), so retiring it removes Calor0419 provenance
                    // and cannot move a computed effect set or a Calor0410 —
                    // measured on the committed corpus, not assumed.
                    var overrideRow = PolyRow.FromDeclaration(method.Effects);
                    ReportRowUnknown(
                        method.Effects?.Span ?? method.Span,
                        $"Override '{cls.Name}.{method.Name}' overrides a member of external base "
                        + $"class '{baseClassName}', which is not visible in this module, so the base "
                        + $"method's effect row is Unknown. The override's declared row "
                        + $"{overrideRow.Display()} is assumed to fit here, not verified.");
                }
            }

            foreach (var iface in ResolveInterfaceChain(cls))
            {
                foreach (var sig in iface.Methods)
                {
                    // W2 review C3: the implementing member may be INHERITED —
                    // walk the in-module base chain, not just cls.Methods.
                    // Interface dispatch through an inherited implementation
                    // launders exactly like a direct one.
                    var (impl, implOwnerName, externalBaseName) =
                        FindImplementingMethod(cls, sig);

                    if (impl != null)
                    {
                        var implDeclared = GetDeclaredEffects(impl.Effects);
                        var ifaceDeclared = GetDeclaredEffects(sig.Effects);
                        // Site 5 (§6.2) — same relation, site-specific code.
                        // Site 5, alpha-equivalently (§7.5 R2): the ordinal is
                        // the variable's identity, so `eff e` on the interface
                        // member and `eff f` on the implementation unify.
                        var implRow = PolyRow.FromDeclaration(impl.Effects);
                        var ifaceRow = PolyRow.FromDeclaration(sig.Effects);
                        var implVerdict = PolyRow.Fits(implRow, ifaceRow);
                        if (implVerdict == Binding.BoundTypes.EffectFit.CannotTell)
                        {
                            ReportRowUnknown(
                                impl.Effects?.Span ?? impl.Span,
                                $"Implementation '{implOwnerName ?? cls.Name}.{impl.Name}' of interface method "
                                + $"'{iface.Name}.{sig.Name}' has effect row "
                                + $"{implRow.Display()} and the interface declares row "
                                + $"{ifaceRow.Display()}, so effect variance cannot be decided. "
                                + "State a row on both, or compile with --permissive-effects.");
                        }
                        else if (implVerdict == Binding.BoundTypes.EffectFit.DoesNotFit)
                        {
                            var extraVariables = implRow.ExtraVariables(ifaceRow).ToList();
                            var extra = implDeclared.Except(ifaceDeclared)
                                .Select(e => EffectSetExtensions.ToSurfaceCode(e.Kind, e.Value))
                                .Concat(extraVariables);
                            var positionNote = extraVariables.Count == 0
                                ? ""
                                : " Effect variables are matched BY POSITION in the declaration's "
                                  + "'eff' list, not by name.";
                            var inheritedNote = implOwnerName != null
                                && !implOwnerName.Equals(cls.Name, StringComparison.Ordinal)
                                ? $" (implementation inherited from base class '{implOwnerName}')"
                                : "";
                            // Point at the implementing member when it is local;
                            // at the implementing class declaration when inherited.
                            var span = inheritedNote.Length == 0
                                ? impl.Effects?.Span ?? impl.Span
                                : cls.Span;
                            _diagnostics.Report(
                                span,
                                DiagnosticCode.InterfaceEffectVariance,
                                $"Implementation '{implOwnerName ?? cls.Name}.{impl.Name}' of interface method " +
                                $"'{iface.Name}.{sig.Name}' on class '{cls.Name}'{inheritedNote} declares effect(s) " +
                                $"[{string.Join(", ", extra)}] not declared by the interface " +
                                $"(interface declares: {ifaceDeclared.ToDisplayString()}). " +
                                $"Effect row {implRow.Display()} does not fit the interface's " +
                                $"row {ifaceRow.Display()}. " +
                                "An implementation may not broaden the interface's declared effect set — interface " +
                                "dispatch launders effects identically to overrides." + positionNote,
                                varianceSeverity);
                        }
                    }
                    else if (externalBaseName != null)
                    {
                        // The §IMPL is satisfied (if at all) by a member inherited
                        // from an EXTERNAL base: variance cannot be checked, so the
                        // interface's declared effect set is only an assumption —
                        // surfaced like external-base overrides (Calor0419).
                        //
                        // §6.2 retires this Calor0419 and its override sibling in
                        // favour of Calor0425. Slice a does NOT take it, and the
                        // reason is that the two must move TOGETHER: the override
                        // arm (above) is an AddAssumption, not a report, and the
                        // assumption it registers propagates through the SCC pass
                        // into every caller's computed effect set. Converting only
                        // this one would make sites 4 and 5 disagree about what an
                        // unresolvable base means, for a message improvement.
                        // Owed by the slice that redesigns the assumption channel.
                        // The SECOND external-base Calor0419, retired with its
                        // override sibling above (§6.2). §6.4's third message
                        // sample, which RE-WORDS the old text rather than merely
                        // re-coding it: it names the row.
                        var assumedIfaceRow = PolyRow.FromDeclaration(sig.Effects);
                        ReportRowUnknown(
                            cls.Span,
                            $"Class '{cls.Name}' implements '{iface.Name}.{sig.Name}' through a member "
                            + $"not visible in this module (inherited from external base "
                            + $"'{externalBaseName}'), so its effect row is Unknown. The interface's "
                            + $"declared row {assumedIfaceRow.Display()} is assumed here, not verified.");
                    }
                }
            }
        }
    }

    /// <summary>
    /// v0.15 E3 slice a, design-doc §6.1/§4.5 — reports Calor0425
    /// <c>EffectRowUnknown</c>. The ONE thing <c>--permissive-effects</c> still
    /// does in 0.15 is waive this, and it waives it by SUPPRESSION, not by
    /// demotion: "we cannot tell" is the honest thing to silence.
    /// <c>--strict-effects</c> raises it to an error, exactly as it does for
    /// Calor0419. A <c>DoesNotFit</c> verdict never comes through here.
    /// </summary>
    private void ReportRowUnknown(TextSpan span, string message)
    {
        if (_policy == UnknownCallPolicy.Permissive)
            return;

        _diagnostics.Report(
            span,
            DiagnosticCode.EffectRowUnknown,
            message,
            _strictEffects ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning);
    }

    /// <summary>
    /// v0.15 E3 slice a — design-doc §6.2 sites <b>1 (assignment)</b>,
    /// <b>2 (argument)</b> and <b>3 (return)</b>, the three sites whose
    /// destination is a declared position rather than a declared MEMBER. Sites 4
    /// and 5 live in <see cref="CheckEffectVariance"/> because their destination
    /// is a base method / interface member; site 6 (rank-1 instantiation) is
    /// slice b's.
    ///
    /// <para><b>What makes a site.</b> A site exists when a function VALUE the
    /// pass can name — a <c>§LAM</c>, a function-typed local/parameter/field, or
    /// a method group naming an in-module callable — flows into a function-typed
    /// DESTINATION whose declaration this module contains. An external callee has
    /// no destination at all: the manifest schema gains no row field in 0.15
    /// (§8.4), so there is nothing to check against and the site does not exist,
    /// rather than existing with an Unknown destination that would put a
    /// Calor0425 on every BCL argument in the corpus.</para>
    ///
    /// <para><b>Effect-POLYMORPHIC positions are skipped, and that is the slice
    /// boundary.</b> A row that mentions an <c>eff</c> variable is site 6, whose
    /// verdict needs §7.4's instantiation and the alpha-equivalence that goes
    /// with it. Slice a is the FIVE MONOMORPHIC sites; treating a variable row as
    /// Unknown here would put a Calor0425 on every call in the four A3 combinator
    /// fixtures, whose frozen baseline (PP-E1 leg A's negative control) is zero
    /// effect-family diagnostics.</para>
    ///
    /// <para><b>§4.4's destination rule is applied at site 1</b>: a binding whose
    /// initializer fits while carrying assumption reasons takes
    /// <c>Assumed(declared, reasons)</c>, not <c>Concrete(declared)</c>, so
    /// passing it onward reports a SECOND Calor0425 rather than laundering the
    /// assumption away. One Calor0425 per hop, which is P10(b).</para>
    /// </summary>
    private void CheckRowCompatibility()
    {
        foreach (var function in _callGraphAnalysis.Functions.Values)
        {
            new RowSiteChecker(this, function).Check();
        }
    }

    /// <summary>
    /// v0.15 E3 slice b, design-doc §5 / P14 — a <c>§LAM</c>'s declared row is a
    /// CONTRACT, checked against the body exactly as a function's <c>§E</c> is.
    /// <c>DoesNotFit</c> is <b>Calor0410 at the <c>§E</c> span, per effect, in
    /// today's shape</c>; <c>CannotTell</c> is Calor0425.
    ///
    /// <para>Lambdas whose row is effect-POLYMORPHIC do not exist — §7.3 position
    /// 2 forbids a variable in a lambda row and the parser answers Calor0404 —
    /// so there is no variable case to handle here.</para>
    ///
    /// <para><b>Zero committed <c>.calr</c> is affected</b>: none of the corpus's
    /// nine <c>§LAM</c> occurrences carries a <c>§E</c> at all (§5), so this can
    /// only fire on code written after 0.15.</para>
    /// </summary>
    private void CheckLambdaDeclaredRows()
    {
        foreach (var (lambda, fact) in _lambdaBodyRows)
        {
            if (lambda.Effects == null)
                continue;

            var declared = PolyRow.FromDeclaration(lambda.Effects);
            var body = LambdaBodyRow(lambda);
            var owner = _callGraphAnalysis.Functions.TryGetValue(fact.FunctionId, out var function)
                ? function.Name
                : fact.FunctionId;

            switch (PolyRow.Fits(body, declared))
            {
                case Binding.BoundTypes.EffectFit.DoesNotFit:
                    foreach (var (kind, value) in body.Row.ToEffectSet().Except(declared.Row.ToEffectSet()))
                    {
                        _diagnostics.Report(
                            lambda.Effects.Span,
                            DiagnosticCode.ForbiddenEffect,
                            $"Lambda '{lambda.Id}' in '{owner}' uses effect "
                            + $"'{EffectSetExtensions.ToSurfaceCode(kind, value)}' but does not declare it",
                            DiagnosticSeverity.Error);
                    }
                    break;

                case Binding.BoundTypes.EffectFit.CannotTell:
                    ReportRowUnknown(
                        lambda.Effects.Span,
                        $"Lambda '{lambda.Id}' in '{owner}' has an inferred body row of "
                        + $"{body.Display()} against a declared row of {declared.Display()}, so it "
                        + "cannot be decided whether the body fits the declaration. Resolve the "
                        + "unknown call the body makes, or compile with --permissive-effects.");
                    break;
            }
        }
    }

    /// <summary>The row a declared <c>§E{…}</c> annotation denotes, in the
    /// <see cref="Binding.BoundTypes.EffectRow"/> lattice. A missing annotation is
    /// <see cref="Binding.BoundTypes.EffectRow.Unknown"/> at a binding POSITION
    /// (§3.5) — never pure, which is the laundering hole rows exist to close.</summary>
    private static Binding.BoundTypes.EffectRow PositionRow(EffectsNode? row) =>
        row == null
            ? Binding.BoundTypes.EffectRow.Unknown
            : GetDeclaredEffects(row).ToRow();

    /// <summary>True when a row is effect-POLYMORPHIC — it mentions an
    /// <c>eff</c> variable. Slice a declined every such position; slice b
    /// adjudicates it at site 6 (§7.4), so this survives only as a shape test.</summary>
    private static bool IsPolymorphicRow(EffectsNode? row) =>
        row is { EffectVariables.Count: > 0 };

    /// <summary>
    /// v0.15 E3 slice b, design-doc §5 — ρ_body per <c>§LAM</c>, recorded during
    /// inference and read afterwards. Reference identity: two structurally
    /// identical lambdas at different places are different lambdas.
    /// </summary>
    private readonly Dictionary<LambdaExpressionNode, LambdaBodyFact> _lambdaBodyRows =
        new(ReferenceEqualityComparer.Instance as IEqualityComparer<LambdaExpressionNode>);

    private readonly record struct LambdaBodyFact(
        string FunctionId,
        EffectSet Body,
        IReadOnlyList<string> Reasons);

    /// <summary>
    /// Design-doc §10.3's provenance clause, per function and per SURFACE effect
    /// code: why this function is charged that effect, when the answer is "a
    /// rank-1 effect variable instantiated to it at a call site". Read by
    /// <see cref="CheckEffects"/>, which is where Calor0410 names the effect.
    /// </summary>
    private readonly Dictionary<string, Dictionary<string, string>> _rank1Provenance =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Functions whose computed effect set GREW because of a rank-1 instantiation
    /// (§7.4). Seeds <see cref="PropagateInstantiatedCharges"/>; empty for every
    /// program that binds no <c>eff</c> variable, which is every committed
    /// <c>.calr</c>, so the propagation below is a no-op on the corpus by
    /// construction rather than by measurement.
    /// </summary>
    private readonly HashSet<string> _rank1Charged = new(StringComparer.Ordinal);

    /// <summary>
    /// v0.15 E3 slice b, review round 1 finding 1 — carry site-6 charges to
    /// TRANSITIVE callers, to a fixpoint.
    ///
    /// <para><b>The hole this closes.</b> The solve runs in phase 3d, after the
    /// SCC fixpoint has already computed everyone's effects. An in-module call
    /// charges its caller the callee's COMPUTED set
    /// (<c>EffectInferrer.InferFromInternalFunctions</c> and its sibling both read
    /// <c>ComputedEffects</c>), so a caller processed during phase 2+3 saw the
    /// callee's PRE-instantiation set. With a three-level chain — <c>Top</c> calls
    /// <c>Outer</c> calls a rank-1 <c>Run</c> — <c>Outer</c> gained the instantiated
    /// effect and <c>Top</c> did not, so <c>Top</c> could declare <c>§E{}</c> and
    /// compile. Calor0418 masked it in the default mode; under
    /// <c>--permissive-effects</c> the program printed "Compilation successful".
    /// That is exactly the laundering rows exist to close, so it is closed here
    /// rather than deferred to E4.</para>
    ///
    /// <para><b>Why a worklist and not one extra pass.</b> One pass fixes a
    /// two-level chain and leaves a three-level one broken. Effect sets only ever
    /// GROW here and the effect universe is finite, so the worklist terminates;
    /// the iteration cap mirrors <see cref="ProcessScc"/>'s and exists for the
    /// same reason — a cycle in the call graph must not spin. Since v0.16 (W5,
    /// gate 11) reaching the cap with work still queued is Calor0406, not a
    /// silent stop: the functions still in the queue may have callers of their
    /// own that were never charged.</para>
    ///
    /// <para>Recursion is handled by the growth test, not by an SCC walk: a member
    /// of a cycle re-enters the queue only while its set is still changing.</para>
    /// </summary>
    private void PropagateInstantiatedCharges()
    {
        if (_rank1Charged.Count == 0) return;

        var queue = new Queue<string>(_rank1Charged);
        var iterations = 0;
        var maxIterations = _instantiatedChargeIterationCap;

        while (queue.Count > 0 && iterations < maxIterations)
        {
            iterations++;
            var calleeId = queue.Dequeue();
            var calleeEffects = _computedEffects.GetValueOrDefault(calleeId, EffectSet.Empty);
            if (calleeEffects.IsEmpty) continue;

            var calleeName = _callGraphAnalysis.Functions.TryGetValue(calleeId, out var callee)
                ? callee.Name
                : calleeId;

            foreach (var callerId in _callGraphAnalysis.GetCallers(calleeId))
            {
                if (callerId.Equals(calleeId, StringComparison.Ordinal))
                    continue;

                var before = _computedEffects.GetValueOrDefault(callerId, EffectSet.Empty);
                var after = before.Union(calleeEffects);
                if (after.Equals(before))
                    continue;

                _computedEffects[callerId] = after;

                // §10.3's provenance, one hop on. The caller did not itself
                // instantiate anything; it inherited the charge, and the message
                // says which call brought it.
                if (!_rank1Provenance.TryGetValue(callerId, out var byCode))
                {
                    byCode = new Dictionary<string, string>(StringComparer.Ordinal);
                    _rank1Provenance[callerId] = byCode;
                }
                foreach (var (kind, value) in after.Except(before))
                {
                    byCode.TryAdd(
                        EffectSetExtensions.ToSurfaceCode(kind, value),
                        $"charged by calling '{calleeName}', whose effect row is instantiated at a "
                        + "rank-1 call site inside it");
                }

                queue.Enqueue(callerId);
            }
        }

        // v0.16 W5 (gate 11): work left in the queue means the cap stopped the
        // propagation, not the growth test. Reported at the declaration of the
        // first function still queued — the one whose callers were never
        // charged — and the message names every function still waiting (a
        // function can be queued more than once; it is named once). Every id
        // in the queue is a key of `Functions`: the seeds are the ids inference
        // charged (ChargeInstantiatedRow) and the rest come from `GetCallers`,
        // whose reverse graph is built from the same function map.
        if (queue.Count > 0)
        {
            var pending = queue.Distinct(StringComparer.Ordinal).ToList();
            ReportDidNotConverge(
                _callGraphAnalysis.Functions[pending[0]].Span,
                "instantiated-charge propagation",
                maxIterations,
                "steps",
                pending.Select(id => _callGraphAnalysis.Functions[id].Name),
                "the callers of these functions may not have been charged the effects instantiated in their callees");
        }
    }

    /// <summary>
    /// ρ_body for an un-annotated <c>§LAM</c> (§5). Unknown when the lambda was
    /// never reached by inference — which is the honest answer, and the same one
    /// slice a gave unconditionally.
    /// </summary>
    private PolyRow LambdaBodyRow(LambdaExpressionNode lambda)
    {
        if (!_lambdaBodyRows.TryGetValue(lambda, out var fact))
            return PolyRow.Unknown;

        var row = fact.Body.ToRow();
        return PolyRow.Concrete(
            fact.Reasons.Count > 0
                ? Binding.BoundTypes.EffectRow.Assumed(row.Codes, fact.Reasons)
                : row);
    }

    /// <summary>
    /// Site 6's charge (§7.4): the caller is charged the INSTANTIATED own-row of
    /// the callee, not its declared one. Applied to <c>_computedEffects</c> after
    /// SCC processing, and then carried to the caller's own callers by
    /// <see cref="PropagateInstantiatedCharges"/> — <b>without</b> which the charge
    /// would stop one level up and a two-hop program could launder an effect
    /// (review round 1, finding 1).
    /// </summary>
    private void ChargeInstantiatedRow(
        string functionId,
        Binding.BoundTypes.EffectRow row,
        IReadOnlyDictionary<string, string> provenance)
    {
        // v0.17 S1 / #1136 — an Unknown instantiated row used to RETURN HERE,
        // charging nothing. That is the second half of the escape: the call site
        // reported Calor0425 as a warning, this dropped the charge, and the
        // build exited 0 with a `§E{}` method invoking a `§E{cw}` member through
        // a row-polymorphic callee. Unknown is not "no effects" — it is "any
        // effects", so it is charged, and the caller reports Calor0410 unless it
        // declares it. `--permissive-effects` still demotes that to a warning,
        // which is the escape hatch conversion work relies on.
        var charged = row.ToEffectSet();
        var before = _computedEffects.GetValueOrDefault(functionId, EffectSet.Empty);
        var after = before.Union(charged);
        _computedEffects[functionId] = after;
        if (!after.Equals(before))
            _rank1Charged.Add(functionId);

        if (provenance.Count == 0) return;
        if (!_rank1Provenance.TryGetValue(functionId, out var byCode))
        {
            byCode = new Dictionary<string, string>(StringComparer.Ordinal);
            _rank1Provenance[functionId] = byCode;
        }
        foreach (var (code, reason) in provenance)
            byCode.TryAdd(code, reason);
    }

    /// <summary>
    /// v0.15 E4, design-doc §10.1 / roadmap §4.2 E4 — <b>Calor0418 is replaced by
    /// fits-at-invocation.</b> Invoking a function-typed VALUE (a parameter, a
    /// <c>§B</c>, a field, or the result of a call) charges the value's effect
    /// row to the invoking function, exactly as calling a named function charges
    /// its declared <c>§E</c>. The charge itself happens INSIDE inference
    /// (<see cref="EffectInferrer.ChargeInvokedRow"/>), so it flows through the
    /// SCC fixpoint, into a <c>§LAM</c>'s ρ_body when the invocation is inside a
    /// lambda, and into Calor0410 with no extra propagation step.
    ///
    /// <para>What is NOT a charge is a diagnostic, and diagnostics must be
    /// emitted once: a recursive SCC re-infers every member per iteration, and
    /// <see cref="ResolveInternalEffectsOnDemand"/> can infer a body before its
    /// SCC is reached. This sink de-duplicates by (function, span, message), so
    /// the inferrer can report from wherever it is without knowing whether it
    /// has been here before.</para>
    ///
    /// <para><b>The three verdicts, per §4.3 read at an invocation.</b>
    /// <c>Concrete(S)</c>: charge <c>S</c>, record §10.1's provenance clause so
    /// Calor0410 can say <i>charged by invoking …</i>. <c>Assumed(S, R)</c>:
    /// charge <c>S</c> and report ONE Calor0425 carrying <c>R</c> (§4.4 — an
    /// assumption is surfaced at every hop, and an invocation is where it is
    /// finally spent). <c>Unknown</c>: report Calor0425 and charge
    /// <c>EffectSet.Unknown</c> under the default policy — the same fail-closed
    /// answer an unknown external call gets today (Calor0411 + Calor0410
    /// <c>'unknown'</c>), which is what §3.5 means by "defaulting to pure
    /// re-opens the hole PR #968 closed" and what §6.4's second sample says in
    /// so many words ("Invoking a value whose row is Unknown charges Unknown to
    /// 'Run'"). Under <c>--permissive-effects</c> the Calor0425 is suppressed
    /// and nothing is charged, which is the flag's one remaining job (§4.5):
    /// waive <i>we cannot tell</i>. A row that does not fit cannot arise at an
    /// invocation at all — there is no destination row to fit into, so §6.2 has
    /// no <c>DoesNotFit</c> cell here and Calor0424 is never reported at an
    /// invocation; the body-vs-declaration check (Calor0410) is what catches an
    /// under-declared caller.</para>
    /// </summary>
    internal sealed class InvocationSink
    {
        private readonly EffectEnforcementPass _pass;
        private readonly HashSet<(string FunctionId, int Line, int Column, string Message)> _reported =
            new();

        public InvocationSink(EffectEnforcementPass pass) => _pass = pass;

        /// <summary>Calor0425 at an invocation, once per (function, span, message).</summary>
        public void ReportRowUnknown(string functionId, TextSpan span, string message)
        {
            if (_reported.Add((functionId, span.Line, span.Column, message)))
                _pass.ReportRowUnknown(span, message);
        }

        /// <summary>
        /// The residual Calor0418: the invoked value's type is PROVABLY not a
        /// function type (<c>i32</c>, <c>str</c>, an array …), so there is no row
        /// to read and the call is not a call. An error under every policy —
        /// this is "we know it is wrong", not "we cannot tell", so
        /// <c>--permissive-effects</c> does not demote it (§4.5).
        /// </summary>
        public void ReportNotAFunction(string functionId, TextSpan span, string message)
        {
            if (_reported.Add((functionId, span.Line, span.Column, message)))
                _pass._diagnostics.Report(span, DiagnosticCode.DelegateInvocation, message,
                    DiagnosticSeverity.Error);
        }

        /// <summary>A variable-mentioning row invoked by a function that does not
        /// bind the variable. Not reachable from source today (the parser's
        /// Calor0404 scope check keeps a parameter row inside its own
        /// declaration's binders); kept so the charge is total.</summary>
        public void ReportUndeclaredEffectVariable(string functionId, TextSpan span, string message)
        {
            if (_reported.Add((functionId, span.Line, span.Column, message)))
                _pass._diagnostics.Report(span, DiagnosticCode.ForbiddenEffect, message,
                    DiagnosticSeverity.Error);
        }

        /// <summary>v0.15 E5 — the variable part of an invoked row's charge, for
        /// <see cref="DeclarationFacts"/>. Bookkeeping, not a diagnostic.</summary>
        public void RecordVariableCharge(string functionId, int ordinal, string name)
            => _pass.RecordVariableCharge(functionId, ordinal, name);

        /// <summary>§10.1's provenance clause, keyed by function and surface code —
        /// the same table site 6 writes, read by <see cref="CheckEffects"/>.</summary>
        public void RecordProvenance(string functionId, string code, string why)
        {
            if (!_pass._rank1Provenance.TryGetValue(functionId, out var byCode))
            {
                byCode = new Dictionary<string, string>(StringComparer.Ordinal);
                _pass._rank1Provenance[functionId] = byCode;
            }
            byCode.TryAdd(code, why);
        }
    }

    private readonly InvocationSink _invocations;

    /// <summary>
    /// v0.15 E3 slice b, design-doc §8.2 — the declared <c>FunctionBoundType</c>
    /// of a named position, or <c>null</c>. This is the first PRODUCTION reader
    /// of <c>VariableSymbol.FunctionType</c> and
    /// <c>FunctionSymbol.ReturnFunctionType</c>: the row checker asks the BOUND
    /// answer before the string test, so a position whose type the string test
    /// cannot recognise — a <c>§CSHARP</c>-declared delegate, the A2 shape — is
    /// still a site.
    /// </summary>
    private Binding.BoundTypes.FunctionBoundType? BoundFunctionType(string functionId, string name)
        => _callGraphAnalysis.DeclaredFunctionTypes(functionId).GetValueOrDefault(name);

    private Binding.BoundTypes.FunctionBoundType? BoundReturnFunctionType(string functionId)
        => _callGraphAnalysis.DeclaredReturnFunctionType(functionId);

    private Binding.BoundTypes.FunctionBoundType? BoundFieldFunctionType(string className, string field)
        => _callGraphAnalysis.DeclaredFieldFunctionType(className, field);

    /// <summary>
    /// v0.15 E3 slice b, design-doc §7 — a row that may mention <c>eff</c>
    /// VARIABLES as well as concrete codes: <c>§E{cw, e}</c> denotes
    /// <c>Concrete({cw}) ⊔ e</c>.
    ///
    /// <para>The variables are carried as <b>ordinals</b> — the index of the
    /// binder in its declaration's <c>eff</c> list — because that is the
    /// identity two declarations' binders are compared on. An interface member's
    /// <c>eff e</c> and its implementation's <c>eff f</c> are both ordinal 0, so
    /// sites 4 and 5 identify them without a rank-1-specific branch, which is
    /// §7.5's R2 (<c>A3-middleware-alpha</c>). The map's VALUES are the author's
    /// spellings and are display only — they take no part in <see cref="Fits"/>,
    /// because a row's identity must not depend on what the author called the
    /// binder. Keeping them keyed BY ordinal rather than in a separate set is
    /// what lets a diagnostic say <c>f (binder #0)</c> when two rows disagree
    /// about position while agreeing about spelling (review round 1, finding 4).</para>
    ///
    /// <para><b>Ordinals are only comparable within one declaration's
    /// vocabulary.</b> A callee's row is in the CALLEE's vocabulary until
    /// <see cref="RowSiteChecker"/> instantiates it; every row that reaches
    /// <see cref="Fits"/> is in the caller's, because instantiation is what
    /// converts one to the other.</para>
    /// </summary>
    private readonly record struct PolyRow(
        Binding.BoundTypes.EffectRow Row,
        System.Collections.Immutable.ImmutableSortedDictionary<int, string> Variables)
    {
        private static readonly System.Collections.Immutable.ImmutableSortedDictionary<int, string>
            NoVariables = System.Collections.Immutable.ImmutableSortedDictionary<int, string>.Empty;

        public static readonly PolyRow Unknown =
            new(Binding.BoundTypes.EffectRow.Unknown, NoVariables);

        public static readonly PolyRow Pure =
            new(Binding.BoundTypes.EffectRow.Pure, NoVariables);

        public bool IsPolymorphic => Variables.Count > 0;

        public static PolyRow Concrete(Binding.BoundTypes.EffectRow row) =>
            new(row, NoVariables);

        /// <summary>The row a declared <c>§E{…}</c> denotes, variables included.</summary>
        public static PolyRow From(EffectsNode? row)
        {
            if (row == null) return Unknown;

            var variables = NoVariables;
            for (var index = 0; index < row.EffectVariables.Count; index++)
            {
                var ordinal = index < row.EffectVariableOrdinals.Count
                    ? row.EffectVariableOrdinals[index]
                    : -1;
                variables = variables.SetItem(ordinal, row.EffectVariables[index]);
            }

            return new PolyRow(GetDeclaredEffects(row).ToRow(), variables);
        }

        /// <summary>
        /// The row a DECLARATION denotes. Differs from <see cref="From"/> in
        /// exactly one cell and the difference is §3.5's: an omitted row on a
        /// declaration is <b>pure</b>, while an omitted row on a binding POSITION
        /// is Unknown.
        /// </summary>
        public static PolyRow FromDeclaration(EffectsNode? row) =>
            row == null ? Pure : From(row);

        /// <summary>§4.2's join, lifted over the variable part.</summary>
        public static PolyRow Join(PolyRow left, PolyRow right)
        {
            var variables = left.Variables;
            foreach (var (ordinal, name) in right.Variables)
            {
                if (!variables.ContainsKey(ordinal))
                    variables = variables.SetItem(ordinal, name);
            }

            return new PolyRow(
                Binding.BoundTypes.EffectRow.Join(left.Row, right.Row),
                variables);
        }

        /// <summary>
        /// §4.3's <c>fits</c>, lifted over the variable part: a source may
        /// mention only variables the destination also mentions. A destination
        /// with EXTRA variables is a widening and fits, exactly as a destination
        /// with extra concrete codes does.
        /// </summary>
        public static Binding.BoundTypes.EffectFit Fits(PolyRow source, PolyRow destination)
        {
            // v0.15 E4 (review round 1, F6): §4.3 — Unknown never yields Fits on
            // EITHER side, and it never yields DoesNotFit either. The ordinal
            // test below runs only between two rows that are both known; a
            // variable-mentioning source into a row-less (Unknown) destination
            // is CannotTell, not a Calor0424 reading "declared row: [unknown]".
            if (source.Row.IsUnknown || destination.Row.IsUnknown)
                return Binding.BoundTypes.EffectRow.Fits(source.Row, destination.Row);

            // Spelled as an explicit loop rather than the library containment
            // helper, whose NAME is the token P16's structural half counts under
            // `Effects/`. This is containment over ORDINALS, not the effect-set
            // relation that pin exists to keep out of the compatibility sites,
            // and it should not read as one.
            foreach (var (ordinal, _) in source.Variables)
            {
                if (!destination.Variables.ContainsKey(ordinal))
                    return Binding.BoundTypes.EffectFit.DoesNotFit;
            }

            return Binding.BoundTypes.EffectRow.Fits(source.Row, destination.Row);
        }

        /// <summary>
        /// §7.4's <c>⊖</c> — difference over the CONCRETE part only. The variable
        /// part is carried through untouched: a residual that still mentions a
        /// caller variable is what makes <c>A3-middleware</c>'s
        /// <c>§C{RunTwice} §A next §/C</c> instantiate to the caller's own
        /// variable rather than to Unknown.
        /// </summary>
        public PolyRow Except(PolyRow declared)
        {
            if (Row.IsUnknown) return new PolyRow(Row, Variables);
            if (declared.Row.IsUnknown) return this;

            var remaining = EffectSet
                .FromInternal(Row.ToEffectSet().Except(declared.Row.ToEffectSet()))
                .ToRow();
            var residual = Row.IsAssumed
                ? Binding.BoundTypes.EffectRow.Assumed(remaining.Codes, Row.Reasons)
                : remaining;
            return new PolyRow(residual, Variables);
        }

        /// <summary>
        /// The binders this row mentions that <paramref name="destination"/> does
        /// NOT, spelled <c>name (binder #k)</c>.
        ///
        /// <para>The ordinal is in the spelling deliberately (review round 1,
        /// finding 4). Two rows can disagree about POSITION while agreeing about
        /// SPELLING — an interface's <c>&lt;eff e, eff f&gt; §E{f}</c> against an
        /// implementation's <c>&lt;eff f, eff e&gt; §E{f}</c> — and a message that
        /// named only the spelling read as <i>"f does not fit f"</i>, or, when the
        /// extras were computed by name, as an empty list. Naming the position
        /// says what actually differs, because position is what <see cref="Fits"/>
        /// compares.</para>
        /// </summary>
        public IEnumerable<string> ExtraVariables(PolyRow destination)
        {
            foreach (var (ordinal, name) in Variables)
            {
                if (!destination.Variables.ContainsKey(ordinal))
                    yield return $"{name} (binder #{ordinal})";
            }
        }

        /// <summary>The compact spelling for diagnostics, variables included.</summary>
        public string Display()
        {
            var concrete = Row.ToCompactDisplayString();
            if (Variables.Count == 0) return concrete;
            var variables = string.Join(", ", Variables.Values);
            return Row.IsUnknown || (Row.IsConcrete && Row.Codes.Count == 0)
                ? variables
                : $"{concrete}, {variables}";
        }
    }

    /// <summary>
    /// Walks ONE callable body and adjudicates sites 1-3 inside it. A fresh
    /// instance per callable, so the in-scope function-valued names cannot leak
    /// between callables.
    /// </summary>
    private sealed class RowSiteChecker
    {
        private readonly EffectEnforcementPass _pass;
        private readonly FunctionNode _function;

        /// <summary>Function-valued names in scope — parameters, fields of the
        /// owning class, and <c>§B</c> bindings as they are met — mapped to the
        /// row they carry AT THIS POINT. Site 1 rewrites an entry through
        /// <c>EffectRow.AtDestination</c>, which is how §4.4's assumption
        /// survives to the next hop.</summary>
        private readonly Dictionary<string, PolyRow> _scope =
            new(StringComparer.Ordinal);

        public RowSiteChecker(EffectEnforcementPass pass, FunctionNode function)
        {
            _pass = pass;
            _function = function;
        }

        public void Check()
        {
            if (_pass._ownerClassByFunctionId.TryGetValue(_function.Id, out var owner))
            {
                foreach (var field in owner.Fields)
                {
                    if (IsFieldFunctionTyped(owner, field))
                        _scope[field.Name] = PolyRow.From(field.Row);
                }

                // v0.17 S1 / #1136 — properties, on the same footing as fields.
                // A `§PROP` could not carry a row until this release, so a read
                // of one was Unknown BY CONSTRUCTION and #1136 measured it
                // escaping. Parsing the row is only half the fix: the pass has to
                // resolve the read to it, or the author states a row the compiler
                // then ignores — which is worse than rejecting it, because the
                // declaration would look like it worked.
                foreach (var property in owner.Properties)
                {
                    if (IsFunctionTyped(property.TypeName))
                        _scope[property.Name] = PolyRow.From(property.Row);
                }
            }

            // Slice b keeps effect-POLYMORPHIC parameters in scope rather than
            // dropping them (slice a's `!IsPolymorphicRow` guard): `next §E{e}`
            // is the source whose row makes A3-middleware's `§C{RunTwice} §A next`
            // instantiate to the caller's own variable instead of to Unknown.
            foreach (var parameter in _function.Parameters)
            {
                if (IsParameterFunctionTyped(_function, parameter))
                    _scope[parameter.Name] = PolyRow.From(parameter.Row);
            }

            foreach (var statement in _function.Body)
                Walk(statement);
        }

        /// <summary>
        /// Guard on the structural walk. <see cref="RecursiveAstWalker"/> is
        /// reflection-driven and a converted corpus module can nest expressions
        /// far deeper than hand-written Calor does; a StackOverflowException is
        /// not catchable, so the depth is capped rather than trusted. 256 is well
        /// past anything the 886-file corpus reaches and well short of the frame
        /// budget. Declining below the cap loses sites, never invents them.
        /// </summary>
        private const int MaxWalkDepth = 256;

        /// <summary>
        /// Every node is walked AT MOST ONCE. <see cref="RecursiveAstWalker"/>
        /// enumerates a node's children by reflecting over its public properties,
        /// and several AST nodes expose the same child through more than one
        /// property — a receiver that is also the head of an argument list, a
        /// clause reachable both directly and through its container. Without
        /// this set the walk is exponential in the number of such aliases, which
        /// is invisible on hand-written Calor and fatal on a converted 1,400-line
        /// corpus module (measured: the effect pass never returned on
        /// <c>serilog/src/Serilog/Core/Logger.cs</c>). Reference identity, not
        /// equality: two structurally identical sub-expressions at different
        /// places are different sites.
        /// </summary>
        private readonly HashSet<AstNode> _walked =
            new(ReferenceEqualityComparer.Instance as IEqualityComparer<AstNode>);

        private void Walk(AstNode node) => Walk(node, depth: 0);

        private void Walk(AstNode node, int depth)
        {
            if (depth > MaxWalkDepth || !_walked.Add(node))
                return;

            // A §LAM's interior is the LAMBDA's body, not this callable's: its §R
            // is a lambda return (site 3 against the lambda's own row, which is
            // ρ_body and therefore slice b). The lambda itself is still adjudicated
            // where it appears, as a source, before we decline to descend.
            if (node is LambdaExpressionNode)
                return;

            switch (node)
            {
                case BindStatementNode bind:
                    CheckAssignmentSite(bind);
                    break;
                case AssignmentStatementNode assignment:
                    CheckReassignmentSite(assignment);
                    break;
                case ReturnStatementNode { Expression: { } returned }:
                    CheckReturnSite(returned);
                    break;
                case CallStatementNode call:
                    CheckArgumentSite(call.Target, call.Arguments, call.ArgumentNames, call.Span);
                    break;
                case CallExpressionNode call:
                    CheckArgumentSite(call.Target, call.Arguments, call.ArgumentNames, call.Span);
                    break;
            }

            foreach (var child in RecursiveAstWalker.GetAllChildren(node))
                Walk(child, depth + 1);
        }

        // ===== Site 1 — assignment (§B initializer) =====

        private void CheckAssignmentSite(BindStatementNode bind)
        {
            var declaresRow = bind.Row != null;
            var functionTyped = declaresRow || IsBindingFunctionTyped(bind);
            if (!functionTyped)
                return;

            var source = SourceRow(bind.Initializer);
            if (source == null)
            {
                // No nameable function value on the right. §3.5: a row-less §B
                // takes its initializer's row, and there is nothing to check.
                if (declaresRow)
                    _scope[bind.Name] = PolyRow.From(bind.Row);
                return;
            }

            var destination = PolyRow.From(bind.Row);

            // §3.5 — a §B with NO row of its own INFERS one from its initializer.
            // That is not a site: there is no declared destination to disagree
            // with. The inferred row is what the next hop sees.
            if (!declaresRow)
            {
                _scope[bind.Name] = source.Value.Row;
                return;
            }

            Adjudicate(
                bind.Row!.Span,
                source.Value.Row,
                destination,
                sourceDescription: $"Initializer of binding '{bind.Name}'",
                destinationDescription: $"binding '{bind.Name}'",
                destinationName: $"'{bind.Name}'",
                destinationIsPosition: true,
                positionDescription: $"Binding '{bind.Name}'",
                owner: _function.Name);

            // §4.4 — the row the value HAS at its destination. An Assumed source
            // that fits produces an Assumed destination, so the assumption is
            // reported again at the next hop instead of vanishing.
            _scope[bind.Name] = AtDestination(source.Value.Row, destination);
        }

        /// <summary>
        /// Site 1's SECOND half (§6.2 — "`§B` init, <b>and re-assignment to a
        /// function-typed mutable</b>"), added by review round 1 (F10). The
        /// destination is the row the mutable already carries at this point in
        /// the body, which after a `Fits` hop is <see cref="AtDestination"/>'s
        /// answer — so re-assigning through a mutable cannot launder a row that
        /// the original binding reported on.
        ///
        /// <para>Only a bare name is a site. A field or element target
        /// (<c>this.cb</c>, <c>xs[i]</c>) needs the receiver typed, which this AST
        /// walk does not do; declining is the honest answer and leaves it to the
        /// slice that types receivers.</para>
        /// </summary>
        private void CheckReassignmentSite(AssignmentStatementNode assignment)
        {
            if (assignment.Target is not ReferenceNode target) return;
            if (!_scope.TryGetValue(target.Name, out var destination)) return;

            var source = SourceRow(assignment.Value);
            if (source == null) return;

            Adjudicate(
                assignment.Value.Span,
                source.Value.Row,
                destination,
                sourceDescription: $"Value assigned to '{target.Name}'",
                destinationDescription: $"'{target.Name}'",
                destinationName: $"'{target.Name}'",
                destinationIsPosition: true,
                positionDescription: $"'{target.Name}'",
                owner: _function.Name);

            // The row the mutable carries AFTER the assignment (§4.4).
            _scope[target.Name] = AtDestination(source.Value.Row, destination);
        }

        /// <summary>§4.4's destination rule, lifted over the variable part: the
        /// destination's variables are the ones the value has once it has
        /// arrived, because the destination's declaration is the contract.</summary>
        private static PolyRow AtDestination(PolyRow source, PolyRow destination)
            => PolyRow.Fits(source, destination) == Binding.BoundTypes.EffectFit.Fits
                ? new PolyRow(
                    Binding.BoundTypes.EffectRow.AtDestination(source.Row, destination.Row),
                    destination.Variables)
                : destination;

        // ===== Site 2 — argument — and site 6 — rank-1 instantiation =====

        /// <summary>
        /// One pass over a call's arguments settles BOTH sites, because they are
        /// the same traversal asking two questions of each parameter.
        ///
        /// <para>A parameter whose declared row is MONOMORPHIC is site 2: the
        /// argument's row must fit it (Calor0424/0425), exactly as in slice a.</para>
        ///
        /// <para>A parameter whose declared row MENTIONS an <c>eff</c> variable is
        /// site 6's binding site: it contributes
        /// <c>ρ(argⱼ) ⊖ ρ_declⱼ</c> to that variable's solution (§7.4). There is
        /// no second <c>fits</c> check at such a position, and that is a
        /// consequence rather than an omission: the solution is DEFINED as the
        /// join of the residuals, so the substituted parameter row contains the
        /// argument's row by construction. What can still go wrong is that a
        /// contributor is not determinable at all, and that is exactly the
        /// Calor0425 <see cref="InstantiateAndCharge"/> reports.</para>
        ///
        /// <para><b>ONE solve, no fixpoint</b> — §7.5's R3. Every variable is
        /// settled by a single left-to-right sweep of this argument list; nothing
        /// is revisited, and no constraint set is built.</para>
        /// </summary>
        private void CheckArgumentSite(
            string target,
            IReadOnlyList<ExpressionNode> arguments,
            IReadOnlyList<string?>? argumentNames,
            TextSpan callSpan)
        {
            // Named arguments would need overload-accurate positional mapping,
            // which this AST walk does not have. Declining is the honest answer.
            if (argumentNames != null && argumentNames.Any(name => name != null))
                return;

            var callee = ResolveInModuleCallee(target);
            if (callee == null)
                return;

            var binderCount = callee.EffectParameters.Count;

            // The zero-argument early-out is AFTER the binder count, not before it
            // (review round 1, finding 3). A declaration can bind an `eff` variable
            // that no parameter mentions — §F{NoBinder:pub}<eff e> () -> i32 §E{e} —
            // and returning first made InstantiateAndCharge's "no parameter of 'X'
            // binds it" arm unreachable from source, so the variable silently
            // contributed nothing instead of reporting Calor0425.
            if (arguments.Count == 0 && binderCount == 0)
                return;

            var solutions = new PolyRow?[binderCount];
            var undetermined = new string?[binderCount];

            for (var index = 0; index < arguments.Count && index < callee.Parameters.Count; index++)
            {
                var parameter = callee.Parameters[index];
                if (!IsParameterFunctionTyped(callee, parameter))
                    continue;

                var declared = PolyRow.From(parameter.Row);
                var source = SourceRow(arguments[index]);

                if (declared.IsPolymorphic)
                {
                    var reason = source == null
                        ? $"argument {index + 1} of '{callee.Name}' is not a function value this "
                          + "pass can name"
                        : source.Value.Row.Row.IsUnknown
                            ? $"the row of argument {source.Value.Description} could not be determined"
                            : null;

                    if (reason != null)
                    {
                        foreach (var ordinal in declared.Variables.Keys)
                            MarkUndetermined(undetermined, ordinal, reason);
                        continue;
                    }

                    var residual = source!.Value.Row.Except(declared);
                    foreach (var ordinal in declared.Variables.Keys)
                    {
                        if (ordinal < 0 || ordinal >= binderCount) continue;
                        solutions[ordinal] = solutions[ordinal] is { } existing
                            ? PolyRow.Join(existing, residual)
                            : residual;
                    }
                    continue;
                }

                if (source == null)
                    continue;

                Adjudicate(
                    arguments[index].Span,
                    source.Value.Row,
                    declared,
                    sourceDescription: $"Argument {source.Value.Description}",
                    destinationDescription: $"parameter '{parameter.Name}' of '{callee.Name}'",
                    destinationName: $"'{parameter.Name}'",
                    destinationIsPosition: parameter.Row == null,
                    positionDescription: $"Parameter '{parameter.Name}' of '{callee.Name}'",
                    owner: callee.Name);
            }

            if (binderCount > 0)
                InstantiateAndCharge(callee, solutions, undetermined, callSpan);
        }

        private static void MarkUndetermined(string?[] undetermined, int ordinal, string reason)
        {
            if (ordinal < 0 || ordinal >= undetermined.Length) return;
            undetermined[ordinal] ??= reason;
        }

        /// <summary>
        /// Site 6's second half (§7.4): substitute the solved variables into the
        /// callee's OWN row and charge the caller with the result.
        ///
        /// <para>A variable the arguments could not determine makes the
        /// instantiated row Unknown, which is Calor0425 at the CALL SITE — §10.3's
        /// second message — and the caller is CHARGED Unknown for it.</para>
        ///
        /// <para>v0.17 S1 / #1136 reversed the earlier rule here. Through v0.16
        /// nothing was charged for such a variable (E3b's disclosed deviation
        /// from §10.3's sample, which E4 did not revisit), so an undetermined
        /// effect variable simply vanished and a `§E{}` method could invoke a
        /// `§E{cw}` member through a row-polymorphic callee with the build
        /// exiting 0. #1136 measured five argument spellings that reach this
        /// path. The pass now charges what it actually knows — Unknown — and the
        /// caller reports Calor0410 unless it declares the effect. See the
        /// comment on the fail-closed branch below for the full record.</para>
        /// </summary>
        private void InstantiateAndCharge(
            FunctionNode callee,
            PolyRow?[] solutions,
            string?[] undetermined,
            TextSpan callSpan)
        {
            var ownRow = PolyRow.FromDeclaration(callee.Effects);
            var instantiated = PolyRow.Concrete(ownRow.Row);
            var provenance = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var ordinal in ownRow.Variables.Keys)
            {
                var name = BinderName(callee, ordinal);
                if (ordinal < 0 || ordinal >= solutions.Length || solutions[ordinal] is not { } solved)
                {
                    var reason = ordinal >= 0 && ordinal < undetermined.Length
                        ? undetermined[ordinal]
                        : null;
                    // v0.17 S1 / #1136 — FAIL CLOSED. This used to report and then
                    // `continue`, charging NOTHING: an effect variable the
                    // arguments could not determine simply vanished, so a
                    // `§E{}` method could invoke a `§E{cw}` member through a
                    // row-polymorphic callee and the build exited 0. #1136
                    // measured five argument spellings that reach here — a field
                    // via `this.`, a `§PROP` by simple name, an alias of `this`,
                    // another instance's field, and an instance method group —
                    // and the held-out suites confirmed the effect escaping at
                    // runtime.
                    //
                    // Unknown is what the pass actually knows, so Unknown is what
                    // it charges. The caller then reports Calor0410 unless it
                    // declares the effect, which is the same treatment the
                    // unknown-call path has always given an unresolvable callee.
                    // `--permissive-effects` still relaxes it, so conversion work
                    // has the same escape hatch it had before.
                    _pass.ReportRowUnknown(
                        callSpan,
                        $"Effect variable '{name}' of '{callee.Name}' instantiates to Unknown at this "
                        + $"call site: {reason ?? $"no parameter of '{callee.Name}' binds it"}. "
                        + $"'{_function.Name}' is charged Unknown for it, because a row this pass "
                        + "cannot determine may carry any effect. State a row on the argument's "
                        + "declaration, or compile with --permissive-effects.");
                    instantiated = PolyRow.Join(instantiated, PolyRow.Unknown);
                    continue;
                }

                instantiated = PolyRow.Join(instantiated, solved);

                // §10.3's provenance clause, attributed PER EFFECT: the Calor0410
                // that follows names the effect, so the explanation has to be
                // reachable from the effect and not merely from the call.
                foreach (var effect in solved.Row.ToEffectSet().Effects)
                {
                    provenance.TryAdd(
                        EffectSetExtensions.ToSurfaceCode(effect.Kind, effect.Value),
                        $"effect variable '{name}' of '{callee.Name}' instantiated to "
                        + $"{solved.Display()} at this call site");
                }
            }

            // §4.4 — an Assumed source produces an Assumed destination, and every
            // hop that carries an assumption reports it ONCE. Site 6 was silent
            // here (review round 1, finding 2): a callback whose own effects this
            // pass could only assume flowed through the solve and the instantiated
            // row's reasons were charged but never surfaced, so the caller inherited
            // an assumption with no Calor0425 naming it. This mirrors Adjudicate's
            // Fits-carrying-reasons arm, deliberately in the same shape.
            if (instantiated.Row.IsAssumed && !instantiated.Row.Reasons.IsEmpty)
            {
                var reasons = instantiated.Row.Reasons;
                var shown = string.Join("; ", reasons.Take(3));
                if (reasons.Count > 3)
                    shown += $"; and {reasons.Count - 3} more";
                _pass.ReportRowUnknown(
                    callSpan,
                    $"The instantiated effect row of '{callee.Name}' at this call site rests on an "
                    + $"assumption: {shown}. '{_function.Name}' is charged "
                    + $"{instantiated.Display()} as an assumption, not a proof.");
            }

            // The concrete part is charged to the caller. The ordinary call-site
            // charge already added the callee's DECLARED concrete codes, so this
            // union adds exactly what instantiation contributed.
            _pass.ChargeInstantiatedRow(_function.Id, instantiated.Row, provenance);

            // A variable the instantiated row still mentions is one of the
            // CALLER's own binders (it arrived through a caller parameter's row).
            // The caller must declare it, exactly as it must declare a concrete
            // effect it uses.
            var callerOwn = PolyRow.FromDeclaration(_function.Effects);
            foreach (var ordinal in instantiated.Variables.Keys)
            {
                if (callerOwn.Variables.ContainsKey(ordinal))
                {
                    _pass.RecordVariableCharge(_function.Id, ordinal, BinderName(_function, ordinal));
                    continue;
                }
                var name = BinderName(_function, ordinal);
                _pass._diagnostics.Report(
                    _function.Effects?.Span ?? _function.Span,
                    DiagnosticCode.ForbiddenEffect,
                    $"Function '{_function.Name}' uses effect variable '{name}' but does not declare "
                    + $"it{Environment.NewLine}  Effect row: {callee.Name}'s row instantiates to "
                    + $"{instantiated.Display()} at a call site in '{_function.Name}'",
                    DiagnosticSeverity.Error);
            }
        }

        private static string BinderName(FunctionNode declaration, int ordinal) =>
            ordinal >= 0 && ordinal < declaration.EffectParameters.Count
                ? declaration.EffectParameters[ordinal].Name
                : $"#{ordinal}";

        // ===== Site 3 — return =====

        private void CheckReturnSite(ExpressionNode returned)
        {
            var output = _function.Output;
            if (output == null)
                return;
            if (output.Row == null && !IsReturnFunctionTyped(_function, output))
                return;

            var source = SourceRow(returned);
            if (source == null)
                return;

            Adjudicate(
                returned.Span,
                source.Value.Row,
                PolyRow.From(output.Row),
                sourceDescription: $"Returned value {source.Value.Description}",
                destinationDescription: $"the return of '{_function.Name}'",
                destinationName: "the return type",
                destinationIsPosition: output.Row == null,
                positionDescription: $"The return of '{_function.Name}'",
                owner: _function.Name);
        }

        // ===== The shared verdict =====

        private void Adjudicate(
            TextSpan span,
            PolyRow source,
            PolyRow destination,
            string sourceDescription,
            string destinationDescription,
            string destinationName,
            bool destinationIsPosition,
            string positionDescription,
            string owner)
        {
            switch (PolyRow.Fits(source, destination))
            {
                case Binding.BoundTypes.EffectFit.DoesNotFit:
                {
                    var extraCodes = source.Row.ToEffectSet().Except(destination.Row.ToEffectSet())
                        .Select(e => EffectSetExtensions.ToSurfaceCode(e.Kind, e.Value))
                        .Concat(source.ExtraVariables(destination))
                        .OrderBy(code => code, StringComparer.Ordinal);
                    var extra = string.Join(", ", extraCodes);
                    _pass._diagnostics.Report(
                        span,
                        DiagnosticCode.EffectRowMismatch,
                        $"{sourceDescription} has effect row {source.Display()}, which does "
                        + $"not fit {destinationDescription} (declared row: "
                        + $"{destination.Display()}). Extra effect(s): {extra}. Widen "
                        + $"{destinationName} to §E{{{extra}}}, or pass a function whose row fits. "
                        + "An effect row that does not fit is never waived.",
                        DiagnosticSeverity.Error);
                    break;
                }

                case Binding.BoundTypes.EffectFit.CannotTell when destinationIsPosition && destination.Row.IsUnknown:
                    // §6.4's second sample: the destination POSITION carries no row
                    // at all, so nothing is known about what may be passed.
                    _pass.ReportRowUnknown(
                        span,
                        $"{positionDescription} is function-typed with no effect row, so its effects are "
                        + "Unknown. Add §E{…} on the same line as the type to state what callers may pass, "
                        + "or compile with --permissive-effects. Invoking a value whose row is Unknown "
                        + $"charges Unknown to '{owner}'.");
                    break;

                case Binding.BoundTypes.EffectFit.CannotTell:
                    _pass.ReportRowUnknown(
                        span,
                        $"{sourceDescription} has effect row {source.Display()} and "
                        + $"{destinationDescription} declares row {destination.Display()}, "
                        + "so it cannot be decided whether the row fits. State a row on both sides, or "
                        + "compile with --permissive-effects.");
                    break;

                case Binding.BoundTypes.EffectFit.Fits:
                {
                    // §4.3 — Assumed fits like its underlying set and ALWAYS
                    // propagates a Calor0425, from whichever side the assumption
                    // came. Read through CarriedReasons, which is total; reading it
                    // off AtDestination would lose them on an Unknown destination.
                    var reasons = Binding.BoundTypes.EffectRow.CarriedReasons(source.Row, destination.Row);
                    if (reasons.IsEmpty)
                        break;

                    var shown = string.Join("; ", reasons.Take(3));
                    if (reasons.Count > 3)
                        shown += $"; and {reasons.Count - 3} more";
                    _pass.ReportRowUnknown(
                        span,
                        $"{sourceDescription} fits {destinationDescription} only under an assumption: "
                        + $"{shown}. The row is accepted as an assumption, not a proof.");
                    break;
                }
            }
        }

        // ===== Source rows =====

        private readonly record struct RowSource(
            PolyRow Row,
            string Description);

        /// <summary>
        /// The row of a function VALUE, or <c>null</c> when the expression is not
        /// a function value this slice can name — in which case there is no site.
        ///
        /// <para>P17, "no silent Unknown": an unresolved receiver, an
        /// unknown-typed name and a call result all yield
        /// <see cref="PolyRow.Unknown"/> or no site at all; none of them ever
        /// yields a <c>Concrete</c> row. A <c>Concrete</c> row here would mean the
        /// compiler claiming to know what a value does when it does not.</para>
        /// </summary>
        private RowSource? SourceRow(ExpressionNode? expression)
        {
            switch (expression)
            {
                case LambdaExpressionNode lambda:
                    // §5 — the §LAM's §E IS the lambda's declared row, and the
                    // DECLARATION BOUNDARY makes it Concrete. With no annotation
                    // the row is ρ_body, which slice b computes during inference.
                    return new RowSource(
                        lambda.Effects != null
                            ? PolyRow.From(lambda.Effects)
                            : _pass.LambdaBodyRow(lambda),
                        "lambda");

                case ReferenceNode reference:
                {
                    if (_scope.TryGetValue(reference.Name, out var inScope))
                        return new RowSource(inScope, $"'{reference.Name}'");

                    // A method group naming an in-module callable: its row is its
                    // DECLARED §E — and Assumed, with the reasons, when the effect
                    // pass could only assume that declaration (§4.5's composition:
                    // soundness is body ⊑ declaration ∧ declaration ⊑ destination).
                    var callee = ResolveInModuleCallee(reference.Name);
                    if (callee == null)
                        return null;

                    // A method group whose OWN declaration is effect-polymorphic
                    // carries a row in ITS vocabulary, not the caller's, and
                    // passing it as a value is rank-2 (§7.3, position 6). Unknown
                    // is the honest answer; it fits nothing.
                    var declaredRow = PolyRow.FromDeclaration(callee.Effects);
                    if (callee.EffectParameters.Count > 0 || declaredRow.IsPolymorphic)
                        return new RowSource(PolyRow.Unknown, $"'{callee.Name}'");

                    var declared = declaredRow.Row;
                    var row =
                        _pass._assumedEffects.TryGetValue(callee.Id, out var reasons) && reasons.Count > 0
                            ? Binding.BoundTypes.EffectRow.Assumed(declared.Codes, reasons)
                            : declared;
                    return new RowSource(PolyRow.Concrete(row), $"'{callee.Name}'");
                }

                default:
                    return null;
            }
        }

        private FunctionNode? ResolveInModuleCallee(string target)
        {
            var id = _pass.ResolveToInternalId(target);
            return id != null && _pass._callGraphAnalysis.Functions.TryGetValue(id, out var callee)
                ? callee
                : null;
        }

        private bool IsParameterFunctionTyped(FunctionNode owner, ParameterNode parameter) =>
            _pass.BoundFunctionType(owner.Id, parameter.Name) != null
            || IsFunctionTyped(parameter.TypeName);

        private bool IsFieldFunctionTyped(ClassDefinitionNode owner, ClassFieldNode field) =>
            _pass.BoundFieldFunctionType(owner.Name, field.Name) != null
            || IsFunctionTyped(field.TypeName);

        // v0.15 E4 (review round 1, F1): an UNTYPED `§B{~f} §LAM …` is
        // function-valued by construction, and the binder gives it a
        // FunctionBoundType (inherited from the initializer) that the
        // parameter-only DeclaredFunctionTypes map never carried. Without this,
        // site 1 never put the mutable in scope, so re-binding it to an impure
        // value was silently accepted while the invocation charged the
        // initializer's row.
        private bool IsBindingFunctionTyped(BindStatementNode bind) =>
            _pass.BoundFunctionType(_function.Id, bind.Name) != null
            || _pass._callGraphAnalysis.DeclaredLocalFunctionType(_function.Id, bind.Name, bind.Span) != null
            || bind.Initializer is LambdaExpressionNode
            || IsFunctionTyped(bind.TypeName);

        private bool IsReturnFunctionTyped(FunctionNode owner, OutputNode output) =>
            _pass.BoundReturnFunctionType(owner.Id) != null
            || IsFunctionTyped(output.TypeName);

        private bool IsFunctionTyped(string? typeName) =>
            TypeIdentity.IsFunctionTypeName(typeName, _pass._delegateTypeNames.Contains);
    }

    /// <summary>
    /// Finds the member implementing an interface method on a class: the class's
    /// own methods first (including §PP-wrapped ones), then the in-module
    /// base-class chain. Returns (method, definingClassName, null) when found;
    /// (null, null, externalBaseName) when the chain leaves the module without a
    /// match; (null, null, null) when there is no match and no external base.
    /// </summary>
    private (MethodNode? Method, string? OwnerName, string? ExternalBaseName) FindImplementingMethod(
        ClassDefinitionNode cls, MethodSignatureNode signature)
    {
        var current = cls;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (current != null && visited.Add(current.Name))
        {
            var matches = CallGraphAnalysis.EnumerateMethods(current)
                .Where(method => CallableSignatureMatches(method, signature))
                .Take(2)
                .ToArray();
            if (matches.Length == 1)
                return (matches[0], current.Name, null);
            if (matches.Length > 1)
                return (null, null, null);

            var baseName = StripGenericArguments(current.BaseClass);
            if (baseName == null)
                return (null, null, null);
            if (!_classesByName.TryGetValue(baseName, out var baseCls))
                return (null, null, baseName); // external base
            current = baseCls;
        }
        return (null, null, null);
    }

    /// <summary>
    /// Walks the in-module base-class chain looking for a method with the given name.
    /// Returns (method, definingClassName) when found; (null, firstUnresolvedBaseName)
    /// when the chain leaves the module (external C# base); (null, null) when there is
    /// no base class at all.
    /// </summary>
    private (MethodNode? Method, string? BaseClassName) FindBaseMethod(
        ClassDefinitionNode cls,
        MethodNode method)
    {
        var baseName = StripGenericArguments(cls.BaseClass);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (baseName != null && visited.Add(baseName))
        {
            if (!_classesByName.TryGetValue(baseName, out var baseCls))
                return (null, baseName); // external base class
            var matches = CallGraphAnalysis.EnumerateMethods(baseCls)
                .Where(candidate => CallableSignatureMatches(candidate, method))
                .Take(2)
                .ToArray();
            if (matches.Length == 1)
                return (matches[0], baseName);
            if (matches.Length > 1)
                return (null, null);
            baseName = StripGenericArguments(baseCls.BaseClass);
        }
        return (null, null);
    }

    private static bool CallableSignatureMatches(MethodNode method, MethodSignatureNode signature) =>
        method.Name.Equals(signature.Name, StringComparison.Ordinal)
        && method.TypeParameters.Count == signature.TypeParameters.Count
        && ParametersMatch(
            method.Parameters,
            method.TypeParameters,
            signature.Parameters,
            signature.TypeParameters);

    private static bool CallableSignatureMatches(MethodNode candidate, MethodNode method) =>
        candidate.Name.Equals(method.Name, StringComparison.Ordinal)
        && candidate.TypeParameters.Count == method.TypeParameters.Count
        && ParametersMatch(
            candidate.Parameters,
            candidate.TypeParameters,
            method.Parameters,
            method.TypeParameters);

    private static bool ParametersMatch(
        IReadOnlyList<ParameterNode> left,
        IReadOnlyList<TypeParameterNode> leftTypeParameters,
        IReadOnlyList<ParameterNode> right,
        IReadOnlyList<TypeParameterNode> rightTypeParameters)
    {
        if (left.Count != right.Count)
            return false;

        for (var index = 0; index < left.Count; index++)
        {
            if (left[index].Modifier != right[index].Modifier
                || !string.Equals(
                    TypeIdentity.CanonicalizeSignature(
                        left[index].TypeName,
                        leftTypeParameters.Select(parameter => parameter.Name).ToArray()),
                    TypeIdentity.CanonicalizeSignature(
                        right[index].TypeName,
                        rightTypeParameters.Select(parameter => parameter.Name).ToArray()),
                    StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Resolves the transitive set of in-module interfaces a class implements
    /// (declared interfaces plus their base-interface chains). External interface
    /// names are skipped: they carry no §E declarations to check against.
    /// </summary>
    private List<InterfaceDefinitionNode> ResolveInterfaceChain(ClassDefinitionNode cls)
    {
        var result = new List<InterfaceDefinitionNode>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        foreach (var name in cls.ImplementedInterfaces)
        {
            var stripped = StripGenericArguments(name);
            if (stripped != null)
                queue.Enqueue(stripped);
        }
        while (queue.Count > 0)
        {
            var name = queue.Dequeue();
            if (!visited.Add(name))
                continue;
            if (!_interfacesByName.TryGetValue(name, out var iface))
                continue;
            result.Add(iface);
            foreach (var baseName in iface.BaseInterfaces)
            {
                var stripped = StripGenericArguments(baseName);
                if (stripped != null)
                    queue.Enqueue(stripped);
            }
        }
        return result;
    }

    internal static string? StripGenericArguments(string? typeName)
    {
        if (typeName == null)
            return null;
        var trimmed = typeName.Trim().TrimEnd('?');
        var idx = trimmed.IndexOf('<');
        return idx > 0 ? trimmed[..idx] : trimmed;
    }

    private void AddAssumption(string functionId, string reason)
    {
        if (!_assumedEffects.TryGetValue(functionId, out var list))
        {
            list = new List<string>();
            _assumedEffects[functionId] = list;
        }
        if (!list.Contains(reason))
            list.Add(reason);
    }

    /// <summary>
    /// D-W2.3 — transitively marks every caller of an assumed-effect function as
    /// itself assumed (via the reverse call graph), so the assumption is inherited
    /// rather than laundered at the first call boundary.
    /// </summary>
    private void PropagateAssumptions()
    {
        var queue = new Queue<string>(_assumedEffects.Where(kv => kv.Value.Count > 0).Select(kv => kv.Key));
        var enqueued = new HashSet<string>(queue, StringComparer.Ordinal);
        while (queue.Count > 0)
        {
            var calleeId = queue.Dequeue();
            var calleeName = _callGraphAnalysis.Functions.TryGetValue(calleeId, out var callee)
                ? callee.Name
                : calleeId;
            foreach (var callerId in _callGraphAnalysis.GetCallers(calleeId))
            {
                if (callerId.Equals(calleeId, StringComparison.Ordinal))
                    continue;
                AddAssumption(callerId, $"calls '{calleeName}', whose effects are assumed");
                if (enqueued.Add(callerId))
                    queue.Enqueue(callerId);
            }
        }
    }

    internal static EffectKind ParseEffectCategory(string category)
        => EffectCodes.ParseKind(category);

    private static (string TypeName, string MethodName) ParseCallTargetForChain(string target)
    {
        var lastDot = target.LastIndexOf('.');
        if (lastDot <= 0)
            return ("", "");

        var methodName = target[(lastDot + 1)..];
        var typePart = target[..lastDot];

        if (!typePart.Contains('.'))
        {
            typePart = MapShortTypeNameToFullName(typePart);
        }

        return (typePart, methodName);
    }

    /// <summary>
    /// Maps common short type names to fully-qualified names for manifest resolution.
    /// The table itself lives in <see cref="Binding.TypeIdentity"/> (E1 slice 2b):
    /// the binder needs the same expansion, and <c>Binding/</c> must not reference
    /// <c>Effects/</c>. This forwarder keeps the pass's ~20 call sites and the
    /// existing external callers on their current name.
    /// </summary>
    internal static string MapShortTypeNameToFullName(string shortName) =>
        TypeIdentity.MapShortTypeNameToFullName(shortName);

    /// <summary>
    /// v0.15 E1 slice 2b — "is this a function type?" answered from the BOUND
    /// TYPE instead of a prefix test on a type name.
    ///
    /// <para>Two structural answers, both produced by the binder:
    /// <see cref="FunctionBoundType"/> (what a <c>§LAM</c> binds to since this
    /// slice) and a <see cref="NominalBoundType"/> whose
    /// <see cref="NominalBoundType.Declaration"/> is a <c>§DEL</c> type
    /// (<c>TypeSymbol.IsDelegate</c>). Neither can be spelled around: an alias,
    /// a metadata return, or a type parameter that resolves to a function type
    /// answers true here and answers false to every string test.</para>
    ///
    /// <para>It is deliberately NOT a superset of the string tests. Where the
    /// binder hands over only a type string — a declared <c>Func&lt;i32&gt;</c>
    /// parameter, whose BoundType is a bare <c>NominalBoundType("Func&lt;i32&gt;")</c>
    /// with no <c>Declaration</c> — this returns false and the caller's string
    /// test still decides. That kept Calor0418's behaviour byte-stable through E1,
    /// and keeps the E4 invocation charge's notion of "function-typed" the same
    /// as the string test's where no bound answer exists, while making the
    /// structural answer the one that is asked first.</para>
    /// </summary>
    // Fully qualified rather than a file-level `using`: adding one shifts every
    // line in this file, and the effect-rows spike transcripts pin
    // EffectEnforcementPass.cs:377/:533/:571 by line number (facts.py).
    internal static bool IsFunctionBoundType(Binding.BoundTypes.BoundType? type) =>
        type is Binding.BoundTypes.FunctionBoundType
        || (type is Binding.BoundTypes.NominalBoundType nominal
            && nominal.Declaration is { IsDelegate: true });

    private List<string> FindCallChain(string startFunctionId, EffectKind targetKind, string targetValue)
    {
        // BFS to find shortest path to the effect
        var queue = new Queue<(string FunctionId, List<string> Path)>();
        var visited = new HashSet<string>();

        queue.Enqueue((startFunctionId, new List<string> { _callGraphAnalysis.Functions[startFunctionId].Name }));
        visited.Add(startFunctionId);

        while (queue.Count > 0)
        {
            var (currentId, path) = queue.Dequeue();

            // Check direct effects from this function's body
            if (_callGraphAnalysis.ForwardGraph.TryGetValue(currentId, out var calls))
            {
                foreach (var (calleeName, span) in calls)
                {
                    // Resolve callee name to ID for internal calls (handles cross-class method calls)
                    var calleeIds = _callGraphAnalysis.ResolveCallSites(
                        currentId,
                        calleeName,
                        span);

                    // Check external calls via manifest resolver
                    if (calleeIds.Count == 0)
                    {
                        var (typeName, methodName) = ParseCallTargetForChain(calleeName);
                        if (!string.IsNullOrEmpty(typeName) && !string.IsNullOrEmpty(methodName))
                        {
                            // v0.15 E1 slice 2c — the call-chain BFS walks the
                            // legacy name graph, which holds target STRINGS and
                            // no receiver identity at all. String fallback,
                            // counted.
                            var resolution = _resolver.Resolve(
                                EffectResolverKey.FromStrings(typeName, methodName));
                            if (resolution.Status != EffectResolutionStatus.Unknown &&
                                resolution.Effects.Contains(targetKind, targetValue))
                            {
                                var result = new List<string>(path) { calleeName };
                                return result;
                            }
                        }
                    }
                    // Check internal calls
                    else
                    {
                        foreach (var calleeId in calleeIds)
                        {
                            if (!visited.Add(calleeId))
                                continue;

                            var newPath = new List<string>(path)
                            {
                                _callGraphAnalysis.Functions[calleeId].Name,
                            };
                            queue.Enqueue((calleeId, newPath));
                        }
                    }
                }
            }
        }

        return new List<string>();
    }

    /// <summary>
    /// #1104 (v0.16 W3(c)) — the bound on the nested <see cref="EffectInferrer"/>'s
    /// AST-side local-type resolution, and the counters that make it observable.
    ///
    /// <para><see cref="EffectInferrer.ResolveLocalValueTypeFromAst"/> answers
    /// "what is the declared type of the value called <c>name</c>" by reading
    /// the name's <c>§B</c> initializer, and a call initializer is typed by
    /// resolving its receiver and its arguments — each of which is again a
    /// name. A converted module whose initializers refer back to the name being
    /// resolved (Serilog's <c>out var</c> hoisting produces
    /// <c>§B{~x} §C{f.M} §A x §/C</c>) therefore recursed without limit, and a
    /// StackOverflowException is a fail-fast: the host dies, nothing catches it.</para>
    ///
    /// <para><b>Who reaches this unbound.</b> Not only the in-process ledgers:
    /// <c>EditPreviewTool.CheckEffects</c> runs this pass on a lex/parse-only
    /// <c>ParseResult</c> with no binder anywhere on the path, and TWO MCP
    /// tools call it — <c>edit_preview</c> (<c>EditPreviewTool</c>) and
    /// <c>file_write</c> (<c>FileWriteTool</c>, which calls the same helper
    /// for its effect check). <c>calor mcp</c> therefore died on any parsed
    /// <c>.calr</c> of this shape, through either tool. Those are shipping
    /// surfaces, and they are why a cycle stop stays SILENT here: the
    /// author of the edited file cannot act on a converter artifact, the call
    /// the cycle sits on is already reported (Calor0411), and edit_preview
    /// would otherwise grow a diagnostic about the compiler's own search.
    /// The CLI binds first and returns on binding errors, which is why it
    /// never saw any of this.</para>
    ///
    /// <para>Two guards, the way <see cref="RowSiteChecker"/> bounds its
    /// structural walk. (1) A VISITED SET keyed on the name in progress: asking
    /// for a name whose resolution is already on the stack is, by construction,
    /// a cycle — the search is deterministic and context-free within one
    /// function, so the inner ask could only repeat the outer one — and it
    /// answers <see cref="EffectInferrer.UnknownLocalTypeSentinel"/> ("known
    /// value, unknown type"). This guard alone guarantees termination: depth is
    /// bounded by the number of distinct names in the function. It cannot fire
    /// on acyclic code, so no resolution answer there changes (gate 6 / §9).
    /// (2) A DEPTH CAP as a backstop for a very long ACYCLIC chain of distinct
    /// temporaries, which terminates but could still exhaust a 1 MB thread; it
    /// also answers the sentinel. <see cref="DefaultDepthCap"/> is an order of
    /// magnitude past what the corpus reaches — measured over the 364 corpus
    /// modules enforced without binding: deepest acyclic nesting 6, and the
    /// only cycles are the two Serilog modules from the issue — and well inside
    /// the frame budget (each nested ask costs roughly 2.5 KB of stack in
    /// Debug; the CLI's main thread is 1 MB on Windows).</para>
    ///
    /// <para>Injectable so the boundary can be pinned at a small cap; the
    /// counters distinguish a cycle stop from a cap stop so a test can say
    /// which guard fired, and <see cref="MaxObservedDepth"/> lets a test pin
    /// that an acyclic chain resolved all the way down rather than stopping.</para>
    /// </summary>
    internal sealed class AstResolutionBound
    {
        /// <summary>
        /// Frames of the AST-side search allowed on the stack at once — name
        /// asks and the structural walk's levels alike (review round 1, C1).
        /// The (cap + 1)-th frame stops with the sentinel.
        ///
        /// <para><b>Chosen empirically, not by taste.</b> With frame-accurate
        /// counting the worst shape is a chain whose links nest one <c>§IF</c>
        /// deeper each time, which costs frames quadratically: 30 such links
        /// reach 525 frames, 50 reach 1375. Driving the 200-link version on a
        /// <b>1 MB</b> thread (what a test host gives a test, and the CLI's
        /// main thread on Windows) at rising caps: 448 completes, 480
        /// overflows. The default is half the largest surviving cap, so the
        /// budget is doubled against a deeper stack above this pass or a
        /// costlier frame in a future build. The corpus needs 13 frames at its
        /// worst — measured over all 364 corpus modules enforced without
        /// binding, with zero cap stops (pinned by
        /// <c>EffectInferrerCorpusDepthTests</c>; several modules reach 13, so
        /// no one file is "the deepest") — so ordinary code sits an order of
        /// magnitude below the cap and never reaches it.</para>
        /// </summary>
        public const int DefaultDepthCap = 224;

        public int DepthCap { get; init; } = DefaultDepthCap;

        /// <summary>Asks refused because the name was already being resolved.</summary>
        public int CycleStops { get; private set; }

        /// <summary>Asks refused because <see cref="DepthCap"/> was reached.</summary>
        public int DepthCapStops { get; private set; }

        /// <summary>Deepest nesting any resolution reached (0 = never asked).</summary>
        public int MaxObservedDepth { get; private set; }

        private readonly List<string> _stoppedNames = new();

        /// <summary>The names whose asks were refused, in order, by either guard.</summary>
        public IReadOnlyList<string> StoppedNames => _stoppedNames;

        internal void RecordCycleStop(string name)
        {
            CycleStops++;
            _stoppedNames.Add(name);
        }

        internal void RecordDepthCapStop(string name)
        {
            DepthCapStops++;
            _stoppedNames.Add(name);
        }

        internal void RecordDepth(int depth)
        {
            if (depth > MaxObservedDepth)
                MaxObservedDepth = depth;
        }
    }

    /// <summary>
    /// #1104 — the AST-resolution bound this pass runs under. Shared by every
    /// per-function inferrer the pass creates, so the counters are per Enforce.
    /// </summary>
    internal AstResolutionBound AstResolution { get; init; } = new();

    /// <summary>
    /// Context for effect inference.
    /// </summary>
    private sealed class InferenceContext
    {
        public EffectResolver Resolver { get; }
        public Dictionary<string, EffectSet> ComputedEffects { get; }
        public Dictionary<string, FunctionNode> Functions { get; }
        public Dictionary<string, string> FunctionNameToId { get; }
        public Dictionary<string, List<string>> MethodNameToIds { get; }
        public CallGraphAnalysis CallGraph { get; }
        public HashSet<string> SccMembers { get; }
        public UnknownCallPolicy Policy { get; }
        public bool StrictEffects { get; }
        public DiagnosticBag Diagnostics { get; }
        public string CurrentFunctionId { get; }
        public IReadOnlyCollection<string> CrossModuleFunctionNames { get; }
        public IReadOnlyDictionary<string, ClassDefinitionNode> ClassesByName { get; }
        public IReadOnlyDictionary<string, InterfaceDefinitionNode> InterfacesByName { get; }
        public IReadOnlyCollection<string> DelegateTypeNames { get; }
        public ClassDefinitionNode? OwnerClass { get; }
        public ConstructorDeclaration? CurrentConstructor { get; }
        public Func<string, EffectSet> ResolveInternalEffects { get; }

        /// <summary>
        /// Sink for D-W2.3 assumption reasons collected while inferring the current
        /// function (interop content, unrecognized constructs, expression-target calls).
        /// </summary>
        public List<string> Assumptions { get; }

        /// <summary>
        /// v0.15 E3 slice b, design-doc §5 — receives ρ_body for every
        /// <c>§LAM</c> the inferrer walks: the lambda, the callable it appears
        /// in, the body's effect set, and the assumption reasons the body itself
        /// added. Last write wins, so an SCC's fixpoint leaves the CONVERGED
        /// row behind rather than the first iteration's.
        /// </summary>
        public Action<LambdaExpressionNode, string, EffectSet, IReadOnlyList<string>>?
            RecordLambdaBody { get; init; }

        /// <summary>
        /// v0.15 E4 — ρ_body of a <c>§LAM</c> the inferrer has already walked,
        /// for a lambda-bound local that is then INVOKED (§3.5: a row-less
        /// <c>§B</c> takes its initializer's row). Unknown when the lambda was
        /// never reached.
        /// </summary>
        public Func<LambdaExpressionNode, PolyRow>? LambdaBodyRow { get; init; }

        /// <summary>
        /// v0.15 E4 — the invocation verdicts that are DIAGNOSTICS rather than
        /// charges, reported through the pass so an SCC fixpoint iteration or an
        /// on-demand re-inference cannot emit the same one twice.
        /// </summary>
        public InvocationSink? Invocations { get; init; }

        /// <summary>
        /// #1104 — the bound on AST-side local-type resolution (cap and
        /// counters). Defaults to a fresh bound so a context built without one
        /// is still guarded.
        /// </summary>
        public AstResolutionBound AstResolution { get; init; } = new();

        public InferenceContext(
            EffectResolver resolver,
            Dictionary<string, EffectSet> computedEffects,
            Dictionary<string, FunctionNode> functions,
            Dictionary<string, string> functionNameToId,
            Dictionary<string, List<string>> methodNameToIds,
            CallGraphAnalysis callGraph,
            HashSet<string> sccMembers,
            UnknownCallPolicy policy,
            bool strictEffects,
            DiagnosticBag diagnostics,
            string currentFunctionId,
            IReadOnlyCollection<string> crossModuleFunctionNames,
            IReadOnlyDictionary<string, ClassDefinitionNode> classesByName,
            IReadOnlyDictionary<string, InterfaceDefinitionNode> interfacesByName,
            IReadOnlyCollection<string> delegateTypeNames,
            ClassDefinitionNode? ownerClass,
            ConstructorDeclaration? currentConstructor,
            Func<string, EffectSet> resolveInternalEffects,
            List<string> assumptions)
        {
            Resolver = resolver;
            ComputedEffects = computedEffects;
            Functions = functions;
            FunctionNameToId = functionNameToId;
            MethodNameToIds = methodNameToIds;
            CallGraph = callGraph;
            SccMembers = sccMembers;
            Policy = policy;
            StrictEffects = strictEffects;
            Diagnostics = diagnostics;
            CurrentFunctionId = currentFunctionId;
            CrossModuleFunctionNames = crossModuleFunctionNames;
            ClassesByName = classesByName;
            InterfacesByName = interfacesByName;
            DelegateTypeNames = delegateTypeNames;
            OwnerClass = ownerClass;
            CurrentConstructor = currentConstructor;
            ResolveInternalEffects = resolveInternalEffects;
            Assumptions = assumptions;
        }
    }

    /// <summary>
    /// Infers effects from AST nodes.
    /// </summary>
    private sealed class EffectInferrer
    {
        private readonly InferenceContext _context;

        public EffectInferrer(InferenceContext context)
        {
            _context = context;
        }

        public EffectSet InferFromStatements(IEnumerable<StatementNode> statements)
        {
            var effects = EffectSet.Empty;
            foreach (var statement in statements)
            {
                effects = effects.Union(InferFromStatement(statement));
            }
            return effects;
        }

        private EffectSet InferFromStatement(StatementNode statement)
        {
            // D-W2.6: this switch is exhaustive over the statement kinds the pass
            // understands. Anything else falls into the final arm, which routes to
            // the Assumed channel (Calor0419) — an unknown construct is an
            // assumption, never silently pure.
            return statement switch
            {
                PrintStatementNode print => EffectSet.From("cw").Union(InferFromExpression(print.Expression)),
                CallStatementNode call => InferFromCallStatement(call),
                IfStatementNode ifStmt => InferFromIf(ifStmt),
                ForStatementNode forStmt => InferFromFor(forStmt),
                WhileStatementNode whileStmt => InferFromExpression(whileStmt.Condition).Union(InferFromStatements(whileStmt.Body)),
                DoWhileStatementNode doWhile => InferFromExpression(doWhile.Condition).Union(InferFromStatements(doWhile.Body)),
                ForeachStatementNode foreach_ => InferFromExpression(foreach_.Collection).Union(InferFromStatements(foreach_.Body)),
                MatchStatementNode matchStmt => InferFromMatch(matchStmt),
                TryStatementNode tryStmt => InferFromTry(tryStmt),
                ThrowStatementNode throwStmt => EffectSet.From("throw")
                    .Union(throwStmt.Exception != null ? InferFromExpression(throwStmt.Exception) : EffectSet.Empty),
                RethrowStatementNode => EffectSet.From("throw"),
                ReturnStatementNode ret => ret.Expression != null ? InferFromExpression(ret.Expression) : EffectSet.Empty,
                BindStatementNode bind => bind.Initializer != null ? InferFromExpression(bind.Initializer) : EffectSet.Empty,
                AssignmentStatementNode assign => InferFromAssignment(assign),
                CompoundAssignmentStatementNode compound => InferFromCompoundAssignment(compound),
                ExpressionStatementNode exprStmt => InferFromExpression(exprStmt.Expression),
                YieldReturnStatementNode yield => yield.Expression != null ? InferFromExpression(yield.Expression) : EffectSet.Empty,
                UsingStatementNode usingStmt => InferFromUsing(usingStmt),
                SyncBlockNode sync => InferFromExpression(sync.LockExpression).Union(InferFromStatements(sync.Body)),
                UnsafeBlockNode unsafeBlock => EffectSet.From("unsafe").Union(InferFromStatements(unsafeBlock.Body)),
                FixedStatementNode fixedStmt => EffectSet.From("unsafe")
                    .Union(InferFromExpression(fixedStmt.Initializer))
                    .Union(InferFromStatements(fixedStmt.Body)),
                EventSubscribeNode sub => InferFromEventAccessor(sub.Event, sub.Handler, isAdd: true, sub.Span),
                EventUnsubscribeNode unsub => InferFromEventAccessor(unsub.Event, unsub.Handler, isAdd: false, unsub.Span),
                // Collection mutations (child expressions charged too)
                CollectionPushNode push => EffectSet.From("mut").Union(InferFromExpression(push.Value)),
                DictionaryPutNode put => EffectSet.From("mut")
                    .Union(InferFromExpression(put.Key))
                    .Union(InferFromExpression(put.Value)),
                CollectionRemoveNode remove => EffectSet.From("mut").Union(InferFromExpression(remove.KeyOrValue)),
                CollectionSetIndexNode setIndex => EffectSet.From("mut")
                    .Union(InferFromExpression(setIndex.Index))
                    .Union(InferFromExpression(setIndex.Value)),
                CollectionClearNode => EffectSet.From("mut"),
                CollectionInsertNode insert => EffectSet.From("mut")
                    .Union(InferFromExpression(insert.Index))
                    .Union(InferFromExpression(insert.Value)),
                DictionaryForeachNode dictForeach => InferFromExpression(dictForeach.Dictionary).Union(InferFromStatements(dictForeach.Body)),
                // §PP conditional-compilation blocks: any branch may be the active
                // one, so charge the UNION of all branches (W2 review C1 — a §PP
                // body must not be silently pure).
                PreprocessorDirectiveNode pp => InferFromStatements(pp.Body)
                    .Union(pp.ElseBody != null ? InferFromStatements(pp.ElseBody) : EffectSet.Empty),
                // No-effect control-flow / declaration-only constructs
                BreakStatementNode or ContinueStatementNode or GotoStatementNode or LabelStatementNode
                    or YieldBreakStatementNode => EffectSet.Empty,
                ProofObligationNode => InferFromStructuralChildren(statement),
                // D-W2.3: interop content — effects are assumed, not silently pure
                RawCSharpNode => InferFromStructuralChildren(statement)
                    .Union(RecordAssumption("contains a raw C# interop statement (§CSHARP)")),
                FallbackCommentNode => InferFromStructuralChildren(statement)
                    .Union(RecordAssumption("contains an unconverted C# fallback statement")),
                // D-W2.6: fail-loud catch-all
                _ => InferFromStructuralChildren(statement)
                    .Union(RecordAssumption(
                        $"contains an unrecognized statement construct '{statement.GetType().Name}' whose effects cannot be inferred"))
            };
        }

        private EffectSet InferFromCompoundAssignment(CompoundAssignmentStatementNode compound)
        {
            var effects = InferFromExpression(compound.Value);
            if (compound.Target is FieldAccessNode)
                effects = effects.Union(EffectSet.From("mut"));
            return effects;
        }

        /// <summary>
        /// Records a D-W2.3 assumption reason for the current function and returns the
        /// empty effect set: the construct contributes an assumption marker (surfaced
        /// as Calor0419 and propagated to callers), not silent purity.
        /// </summary>
        private EffectSet RecordAssumption(string reason)
        {
            if (!_context.Assumptions.Contains(reason))
                _context.Assumptions.Add(reason);
            return EffectSet.Empty;
        }

        private EffectSet InferFromCallStatement(CallStatementNode call)
        {
            var effects = InferFromCallTarget(call.Target, call.Span, call.Arguments);
            return effects.Union(InferFromCallArguments(call.Target, call.Arguments));
        }

        /// <summary>
        /// Charges call arguments (W2 review C4). Beyond the argument
        /// expressions themselves:
        /// - a bare-name argument that resolves to an internal function/method is
        ///   a METHOD GROUP — its declared effects are charged at the passing
        ///   site (conservative and sound: handing an impure callable to anything
        ///   charges its effects, closing the ConvertAll/Select laundering path);
        /// - a function-typed VALUE argument passed to an external receiver call is
        ///   surfaced as a Calor0419 assumption — the callee may invoke it invisibly.
        /// </summary>
        private EffectSet InferFromCallArguments(string callTarget, IEnumerable<ExpressionNode> arguments)
        {
            var effects = EffectSet.Empty;
            foreach (var arg in arguments)
            {
                effects = effects.Union(InferFromExpression(arg));

                if (arg is not ReferenceNode reference || reference.Name.Contains('.'))
                    continue;

                var valueType = ResolveLocalValueType(reference.Name);
                if (valueType == null)
                {
                    var internalFunc = FindInternalFunctionByName(reference.Name);
                    if (internalFunc != null)
                    {
                        // Method-group argument: charge the referenced internal
                        // function's declared effects.
                        effects = effects.Union(GetDeclaredEffects(internalFunc.Effects));
                    }
                }
                else if (IsFunctionValued(reference.Name, valueType)
                         && callTarget.Contains('.'))
                {
                    RecordAssumption(
                        $"passes function-typed value '{reference.Name}' to '{callTarget}', " +
                        "which may invoke it with unverifiable effects");
                }
            }
            return effects;
        }

        private EffectSet InferFromCallTarget(
            string target,
            TextSpan span,
            IReadOnlyList<ExpressionNode>? arguments = null,
            TextSpan? referenceSpan = null)
        {
            var exactInternalIds = _context.CallGraph.ResolveCallSites(
                _context.CurrentFunctionId,
                target,
                span);
            if (exactInternalIds.Count > 0
                && target.Contains('.')
                && !_context.CallGraph.IsBinderResolvedCallSite(
                    _context.CurrentFunctionId,
                    target,
                    span)
                && !IsProvenInternalDottedTarget(target))
                exactInternalIds = Array.Empty<string>();
            if (exactInternalIds.Count > 0)
            {
                var effects = EffectSet.Empty;
                foreach (var exactInternalId in exactInternalIds)
                {
                    if (!_context.Functions.TryGetValue(exactInternalId, out var function))
                        continue;

                    if (_context.ComputedEffects.TryGetValue(exactInternalId, out var exactEffects))
                    {
                        effects = effects.Union(exactEffects);
                    }
                    else if (_context.SccMembers.Contains(exactInternalId))
                    {
                        effects = effects.Union(_context.ComputedEffects.GetValueOrDefault(
                            exactInternalId,
                            EffectSet.Empty));
                    }
                    else
                    {
                        effects = effects.Union(GetDeclaredEffects(function.Effects));
                    }
                }

                return effects;
            }
            // Bare (no-dot) targets: either a value invocation (delegate — D-W2.1),
            // an internal function/method, or an unresolvable free name.
            // Value resolution runs FIRST, mirroring C# scoping: a parameter,
            // binding, or field SHADOWS a same-named function, and emission
            // resolves the call to the value — so the effect pass must too
            // (review C2: a decoy-named delegate parameter must not charge the
            // shadowed pure function's effects).
            if (!target.Contains('.'))
            {
                // InferFromBareNameTarget re-runs the value lookup itself and
                // branches on it there; both arms of the old `if` called it
                // unconditionally, so the test was dead. Collapsed in E1 slice 2b
                // (review round 2, nit 5) — behaviour unchanged.
                return InferFromBareNameTarget(target, span, referenceSpan ?? span);
            }

            // D-W2.2 (call-site leg): a call through a receiver whose static type is an
            // in-module interface or class charges the static type's DECLARED §E. Sound
            // for dispatch because the declaration-local variance checks (Calor0420/0421)
            // pin every override/implementation to a subset of that declared set.
            var argumentTypes = (arguments ?? Array.Empty<ExpressionNode>())
                .Select(InferExpressionType)
                .ToArray();
            var staticCharge = TryChargeStaticReceiverType(target);
            if (staticCharge != null)
            {
                return staticCharge;
            }

            // Try to resolve using the EffectResolver (manifest-based)
            var (typeName, methodName) = ParseCallTarget(target);
            if (!string.IsNullOrEmpty(typeName) && !string.IsNullOrEmpty(methodName))
            {
                // Step 1 keys on the receiver EXACTLY AS WRITTEN — "System.IO.File"
                // in `File`-qualified source, "r" in `r.Next`. That is a
                // string-fallback key by construction: at this point the pass
                // has not asked anything about the receiver's type yet.
                var resolution = _context.Resolver.Resolve(
                    EffectResolverKey.FromStrings(typeName, methodName, argumentTypes));
                if (resolution.Status != EffectResolutionStatus.Unknown)
                {
                    return resolution.Effects;
                }

                // If type didn't resolve, try variable type resolution:
                // "r.Next" where "r" is a variable declared as "new Random()".
                // v0.15 E1 slice 2c — THIS is the symbol-identity site: the
                // receiver's type has just been asked of the bound tree, so the
                // key is built from the receiver's BoundType whenever the binder
                // typed it, and from ResolveVariableType's string otherwise.
                var resolvedVarType = ResolveVariableType(typeName);
                if (resolvedVarType != null && resolvedVarType != typeName)
                {
                    resolution = _context.Resolver.Resolve(
                        ResolverKey(typeName, resolvedVarType, methodName, argumentTypes));
                    if (resolution.Status != EffectResolutionStatus.Unknown)
                    {
                        return resolution.Effects;
                    }

                    resolution = _context.Resolver.Resolve(ResolverKey(
                        typeName, resolvedVarType, methodName, argumentTypes,
                        EffectMemberKind.Extension));
                    if (resolution.Status != EffectResolutionStatus.Unknown)
                        return resolution.Effects;
                }

                var (chainedReceiverType, chainedEffects) = ResolveReceiverChain(typeName);
                if (chainedReceiverType != null)
                {
                    resolution = _context.Resolver.Resolve(
                        ResolverKey(typeName, chainedReceiverType, methodName, argumentTypes));
                    if (resolution.Status == EffectResolutionStatus.Unknown)
                    {
                        resolution = _context.Resolver.Resolve(ResolverKey(
                            typeName, chainedReceiverType, methodName, argumentTypes,
                            EffectMemberKind.Extension));
                    }
                    if (resolution.Status != EffectResolutionStatus.Unknown)
                        return chainedEffects.Union(resolution.Effects);
                }
            }

            // A MODULE-QUALIFIED call into another module of the same multi-file
            // compilation (#925). The bare-name path already accepts these; this
            // path did not, so naming the module explicitly — the clearer thing
            // to write — produced a WORSE result than leaving it bare: the call
            // fell through to "unknown external", which then forbade it inside a
            // pure function even when the callee was itself pure.
            //
            // The driver's map holds "Module.Function" keys for every module and
            // the bare name only when unambiguous, so membership here is already
            // the exactly-one-match rule the rest of resolution uses. As in the
            // bare case, this pass contributes nothing: the cross-module pass
            // charges the callee's declared effects.
            if (_context.CrossModuleFunctionNames.Contains(target))
            {
                return EffectSet.Empty;
            }

            // Permissive mode: assume pure for unknown calls (no diagnostic)
            if (_context.Policy == UnknownCallPolicy.Permissive)
            {
                return EffectSet.Empty;
            }

            // Unknown external call - report diagnostic based on policy
            ReportUnknownCall(target, span);
            return EffectSet.Unknown;
        }

        /// <summary>
        /// D-W2.1 detection rule for bare-name call targets that are NOT internal
        /// functions/methods:
        /// (a) the name resolves lexically to a parameter of the current function, a
        ///     §B binding in its body, or a field of the enclosing class — a VALUE is
        ///     being invoked. v0.15 E4: its effect ROW is charged to this function
        ///     (<see cref="ChargeInvokedRow"/>) — Concrete silently, Assumed with one
        ///     Calor0425, Unknown as Calor0425 plus the fail-closed Unknown charge
        ///     (nothing under --permissive-effects). Pre-E4 this arm was an
        ///     unconditional Calor0418; that code survives only for a value whose
        ///     type is provably not a function type.
        /// (b) the name is a free name the pass cannot see (e.g. an inherited member
        ///     of an external C# base) — routed through the unknown-call chain, which
        ///     fails loud under the strict policy (Calor0411 + unknown effects)
        ///     instead of the pre-W2 behavior of silently assuming purity.
        /// </summary>
        /// <summary>
        /// The AST search's answer for "there is a value with this name, and I
        /// cannot type it" (<see cref="FindLocalDeclarationType"/>, and
        /// <c>§USING</c> without a declared type). It is a real answer on the
        /// property-read paths — <c>InferFromReference</c> and
        /// <c>InferSetterEffects</c> use it to report an unknown operation
        /// rather than silently charging nothing — which is why it is named
        /// here instead of deleted.
        /// </summary>
        private const string UnknownLocalTypeSentinel = "?";

        private EffectSet InferFromBareNameTarget(string target, TextSpan span, TextSpan referenceSpan)
        {
            var valueType = ResolveLocalValueType(target);

            // v0.15 E1 slice 2c — the sentinel is NOT a type, and this is the
            // one site where treating it as one is a decision rather than a
            // formatting detail. `§C{u}` on a `§B{u}` the pass cannot type used
            // to take the delegate-invocation arm and report Calor0418
            // "declared type '?'", charging EffectSet.Empty — a guess that
            // launders the call. It now falls through to the unknown-call chain
            // and fails closed as Calor0411, which is what the SAME fixture with
            // a receiver use already did through AskBoundTree's veto.
            //
            // Slice 2b recorded this as debt and refused to do it as a
            // drive-by, because it SUBSUMES that veto: with the guard here, the
            // veto's fixture reaches Calor0411 by this path too, so
            // E1Slice2b_ReportedUnresolvedReceiver_VetoesTheAstSentinel no
            // longer fails when the veto branch is deleted. The veto is kept
            // anyway — it is the fail-closed rule stated at the layer that owns
            // it, and E2 needs it there when chains become typed — but design
            // doc §8.1 now records plainly that it has no discriminating pin.
            if (valueType != null && valueType != UnknownLocalTypeSentinel)
            {
                // v0.15 E4 — a VALUE is being invoked. Its effect row is read
                // from the declaration that put it in scope (the same row node
                // the binder's FunctionBoundType.Row was built from — read here
                // as the node rather than the bound projection, because the
                // bound Row collapses a variable-mentioning row to Unknown and
                // `f §E{e}` invoked inside a rank-1 `Map` must charge `e`, not
                // Unknown) and charged to this function. Calor0418 survives for
                // exactly one shape: a value whose type is PROVABLY not a
                // function type, where there is no row to read.
                var invoked = ResolveInvokedValueRow(target, valueType, referenceSpan);
                if (invoked == null)
                {
                    // F10: quote the SOURCE spelling of the type (`i32`), not the
                    // binder's (`INT`), when the AST has one.
                    var spelled = ResolveLocalValueTypeFromAst(target) is { } ast
                                  && ast != UnknownLocalTypeSentinel
                        ? ast
                        : valueType;
                    _context.Invocations?.ReportNotAFunction(
                        _context.CurrentFunctionId,
                        span,
                        $"'{target}' has type '{spelled}', which is not a function type, so "
                        + "invoking it cannot be charged to any effect row. Calor0418 is reported "
                        + "only for a value that is provably not a function; a function-typed value "
                        + "is charged through its effect row.");
                    return EffectSet.Empty;
                }

                return ChargeInvokedRow($"'{target}'", invoked.Value, span);
            }

            // A bare public function exported by another module of the same
            // multi-file compilation (unambiguous, from the driver's pre-parse):
            // legitimately bare here — the cross-module pass charges its declared
            // effects, so the per-module pass contributes nothing.
            if (_context.CrossModuleFunctionNames.Contains(target))
            {
                return EffectSet.Empty;
            }

            // Unresolvable free name: fail closed through the unknown-call chain.
            if (_context.Policy == UnknownCallPolicy.Permissive)
            {
                return EffectSet.Empty;
            }

            var unknownSeverity = _context.StrictEffects
                ? DiagnosticSeverity.Error
                : DiagnosticSeverity.Warning;
            if (_context.Policy == UnknownCallPolicy.Strict || _context.StrictEffects)
            {
                _context.Diagnostics.Report(
                    span,
                    DiagnosticCode.UnknownExternalCall,
                    $"Unknown call target '{target}': not an internal function, parameter, binding, or field " +
                    "visible to the effect pass. If this is a delegate-typed member, declare it where this " +
                    "module can see it with an effect row (§E{…} on the same line as its type); otherwise " +
                    "add the callee to a .calor-effects.json manifest.",
                    unknownSeverity);
            }
            else if (_context.Policy == UnknownCallPolicy.Warn)
            {
                _context.Diagnostics.Report(
                    span,
                    DiagnosticCode.UnknownExternalCall,
                    $"Unknown call target '{target}' - assuming worst-case effects. Consider adding to manifest.",
                    DiagnosticSeverity.Warning);
            }
            return EffectSet.Unknown;
        }

        /// <summary>
        /// v0.15 E4 — the row of a VALUE about to be invoked, with the sentence
        /// a Calor0425 would quote if that row is Unknown; or <c>null</c> when the
        /// value's type is PROVABLY not a function type (the residual Calor0418).
        ///
        /// <para>Resolution order is the declaration that put the name in scope —
        /// the same lexical order <see cref="ResolveLocalValueTypeFromAst"/> uses,
        /// so the row and the type come from the same declaration:</para>
        /// <list type="number">
        /// <item>a parameter of the current callable — its <c>§E</c> suffix row,
        /// variables included (§3.3 positions 4/5);</item>
        /// <item>a <c>§B</c> in the body — its own row, else the initializer's:
        /// a <c>§LAM</c>'s declared row or ρ_body (§3.5, §5), a call to an
        /// in-module function's declared RETURN row (position 6), another
        /// in-scope value's row (aliasing);</item>
        /// <item>a field of the owning class — its row (position 8);</item>
        /// <item>anything else the type string alone can vouch for.</item>
        /// </list>
        /// <para>A position that IS function-typed but carries no row is
        /// <c>Unknown</c> (§3.5's fourth row), never pure. Function-typedness is
        /// asked of the bound <c>FunctionBoundType</c> first and the type string
        /// second, the same order <c>RowSiteChecker.IsParameterFunctionTyped</c>
        /// uses — so a <c>§CSHARP</c>-declared delegate (the A2 shape) is a
        /// function value here exactly when it is a site there. A position whose
        /// type is not provably EITHER way — an external nominal type the binder
        /// does not know — is <c>Unknown</c> too: "cannot tell" is the honest
        /// answer, and fails closed through the same Calor0425.</para>
        /// </summary>
        private (PolyRow Row, string Why)? ResolveInvokedValueRow(string name, string valueType, TextSpan reference)
        {
            _context.Functions.TryGetValue(_context.CurrentFunctionId, out var function);
            var ownerName = function?.Name ?? _context.CurrentFunctionId;

            if (function != null)
            {
                // v0.15 E4 (review round 1, F2): the reference is resolved to its
                // BOUND declaration, not to the first `§B` of that name. Two
                // sibling branches can each bind `f`; the binder knows which one
                // this `§C{f}` names (`BoundCallStatement.ReceiverSymbol` /
                // `BoundVariableExpression.Variable`, via `_scope.LookupAll`), and
                // CallGraphAnalysis records that declaration's span per reference.
                var declaration = _context.CallGraph.BoundValueDeclaration(_context.CurrentFunctionId, reference);
                var bindings = CollectLocalBindings(name, function.Body);
                var parameter = function.Parameters.FirstOrDefault(
                    p => p.Name.Equals(name, StringComparison.Ordinal));

                BindStatementNode? bind = null;
                var ambiguous = false;
                if (declaration is { } declared)
                {
                    bind = bindings.FirstOrDefault(b => b.Span.Start <= declared.Start && declared.Start <= b.Span.End)
                        ?? bindings.FirstOrDefault(b => b.Span.Start == declared.Start);
                    // A resolved declaration that is not any `§B` is the parameter
                    // (its span is in the signature) or a field; fall through.
                }
                else
                {
                    // Binding threw, or the reference was not recorded: the
                    // name-keyed answer is used only where it is UNAMBIGUOUS.
                    // More than one candidate — two same-named `§B`s, or a `§B`
                    // shadowing a parameter — fails CLOSED as Unknown.
                    var candidates = bindings.Count + (parameter != null ? 1 : 0);
                    if (candidates > 1)
                        ambiguous = true;
                    else if (bindings.Count == 1)
                        bind = bindings[0];
                }

                if (ambiguous)
                {
                    return (PolyRow.Unknown,
                        $"more than one declaration named '{name}' is visible in '{ownerName}' and the "
                        + "binder did not resolve this reference to one of them");
                }

                if (bind != null)
                {
                    if (bind.Row != null)
                        return (PolyRow.From(bind.Row), string.Empty);

                    if (InitializerRow(bind, ownerName) is { } fromInitializer)
                        return fromInitializer;

                    var functionTyped =
                        _context.CallGraph.DeclaredLocalFunctionType(_context.CurrentFunctionId, name, bind.Span) != null
                        || IsFunctionTypeName(bind.TypeName ?? valueType);
                    if (!functionTyped && TypeIdentity.IsProvablyNonFunctionType(bind.TypeName ?? valueType))
                        return null;

                    return (PolyRow.Unknown,
                        $"binding '{name}' in '{ownerName}' carries no effect row and its "
                        + "initializer's row cannot be determined");
                }

                if (parameter != null)
                {
                    if (parameter.Row != null)
                        return (PolyRow.From(parameter.Row), string.Empty);

                    var functionTyped =
                        _context.CallGraph.DeclaredFunctionTypes(_context.CurrentFunctionId)
                            .GetValueOrDefault(name) != null
                        || IsFunctionTypeName(parameter.TypeName);
                    if (!functionTyped && TypeIdentity.IsProvablyNonFunctionType(parameter.TypeName))
                        return null;

                    return (PolyRow.Unknown,
                        $"parameter '{name}' of '{ownerName}' (type '{parameter.TypeName}') carries "
                        + "no effect row");
                }
            }

            var field = _context.OwnerClass?.Fields.FirstOrDefault(
                f => f.Name.Equals(name, StringComparison.Ordinal));
            if (field != null)
            {
                if (field.Row != null)
                    return (PolyRow.From(field.Row), string.Empty);

                var functionTyped =
                    _context.CallGraph.DeclaredFieldFunctionType(_context.OwnerClass!.Name, name) != null
                    || IsFunctionTypeName(field.TypeName);
                if (!functionTyped && TypeIdentity.IsProvablyNonFunctionType(field.TypeName))
                    return null;

                return (PolyRow.Unknown,
                    $"field '{name}' of '{_context.OwnerClass.Name}' (type '{field.TypeName}') "
                    + "carries no effect row");
            }

            // A §FE variable, a §USING binding, or a value the side channel typed:
            // no row can be written at these positions in 0.15, so a function
            // type here is Unknown and a provable non-function is the residual.
            if (!IsFunctionValued(name, valueType) && TypeIdentity.IsProvablyNonFunctionType(valueType))
                return null;

            return (PolyRow.Unknown,
                $"'{name}' in '{ownerName}' (type '{valueType}') is declared at a position that "
                + "carries no effect row");
        }

        /// <summary>The row a row-less <c>§B</c> INHERITS from its initializer
        /// (§3.5), or <c>null</c> when the initializer is not something this pass
        /// can read a row off.</summary>
        private (PolyRow Row, string Why)? InitializerRow(BindStatementNode bind, string ownerName)
        {
            switch (bind.Initializer)
            {
                case LambdaExpressionNode lambda:
                    // §5 — the §LAM's own §E is its row; with none, ρ_body, which
                    // this same walk recorded when it inferred the initializer.
                    return lambda.Effects != null
                        ? (PolyRow.From(lambda.Effects), string.Empty)
                        : (_context.LambdaBodyRow?.Invoke(lambda) ?? PolyRow.Unknown,
                            $"the lambda bound to '{bind.Name}' in '{ownerName}' has no "
                            + "determinable body row");

                case CallExpressionNode call:
                    return ReturnedValueRow(call.Target, call.Span, bind.Name, ownerName);

                case ReferenceNode reference when !reference.Name.Contains('.')
                                                   && !reference.Name.Equals(bind.Name, StringComparison.Ordinal):
                {
                    // `§B{g} f` — an alias of another value in scope. Bounded to
                    // one hop: a chain of aliases is rare and a second hop is
                    // Unknown rather than a loop.
                    var aliasType = ResolveLocalValueType(reference.Name);
                    if (aliasType != null && aliasType != UnknownLocalTypeSentinel)
                    {
                        var aliased = ResolveInvokedValueRowDirect(reference.Name, reference.Span);
                        if (aliased != null)
                            return aliased;
                    }

                    var internalFunc = FindInternalFunctionByName(reference.Name);
                    if (internalFunc != null)
                    {
                        // A method group. Its row is its declared §E; effect-
                        // polymorphic declarations are rank-2 as values (§7.3) and
                        // are Unknown here, as they are at the six sites.
                        var declared = PolyRow.FromDeclaration(internalFunc.Effects);
                        return internalFunc.EffectParameters.Count > 0 || declared.IsPolymorphic
                            ? (PolyRow.Unknown,
                                $"'{internalFunc.Name}' is effect-polymorphic and its row cannot be "
                                + "read as a value")
                            : (declared, string.Empty);
                    }

                    return null;
                }

                default:
                    return null;
            }
        }

        /// <summary>One alias hop of <see cref="ResolveInvokedValueRow"/> — the
        /// same lookup, minus the alias arm, so `§B{g} f` cannot recurse.</summary>
        private (PolyRow Row, string Why)? ResolveInvokedValueRowDirect(string name, TextSpan reference)
        {
            _context.Functions.TryGetValue(_context.CurrentFunctionId, out var function);
            if (function != null)
            {
                var bindings = CollectLocalBindings(name, function.Body);
                var declaration = _context.CallGraph.BoundValueDeclaration(_context.CurrentFunctionId, reference);
                var bind = declaration is { } declared
                    ? bindings.FirstOrDefault(b => b.Span.Start <= declared.Start && declared.Start <= b.Span.End)
                    : bindings.Count == 1 ? bindings[0] : null;

                if (bind == null)
                {
                    foreach (var parameter in function.Parameters)
                    {
                        if (parameter.Name.Equals(name, StringComparison.Ordinal))
                            return parameter.Row != null
                                ? (PolyRow.From(parameter.Row), string.Empty)
                                : null;
                    }
                }

                if (bind?.Row != null)
                    return (PolyRow.From(bind.Row), string.Empty);
                if (bind?.Initializer is LambdaExpressionNode lambda)
                    return lambda.Effects != null
                        ? (PolyRow.From(lambda.Effects), string.Empty)
                        : (_context.LambdaBodyRow?.Invoke(lambda) ?? PolyRow.Unknown, string.Empty);
            }

            var field = _context.OwnerClass?.Fields.FirstOrDefault(
                f => f.Name.Equals(name, StringComparison.Ordinal));
            return field?.Row != null ? (PolyRow.From(field.Row), string.Empty) : null;
        }

        /// <summary>
        /// The row of the value a CALL returns, for `§B{f} §C{GetF} §/C` and for
        /// the immediate `§C §C{GetF} §/C §/C` spelling: an in-module callee's
        /// declared return row (§3.3 position 6), Unknown when the return
        /// carries none or the callee is not visible to this pass.
        /// </summary>
        private (PolyRow Row, string Why) ReturnedValueRow(
            string target, TextSpan span, string? boundName, string ownerName)
        {
            var subject = boundName != null
                ? $"binding '{boundName}' in '{ownerName}' takes its row from '{target}', which "
                : $"'{target}' ";

            var ids = _context.CallGraph.ResolveCallSites(_context.CurrentFunctionId, target, span);
            FunctionNode? callee = null;
            if (ids.Count == 1 && _context.Functions.TryGetValue(ids[0], out var resolved))
                callee = resolved;
            callee ??= target.Contains('.') ? null : FindInternalFunctionByName(target);

            if (callee == null)
                return (PolyRow.Unknown, $"{subject}is not visible to the effect pass");

            if (callee.Output?.Row != null)
                return (PolyRow.From(callee.Output.Row), string.Empty);

            return (PolyRow.Unknown, $"{subject}returns a function type with no effect row");
        }

        /// <summary>EVERY <c>§B</c> of that name in lexical order, not descending
        /// into a <c>§LAM</c> (its bindings are the lambda's). The caller picks
        /// the one the binder resolved a reference to, or fails closed when it
        /// cannot tell (F2).</summary>
        private static List<BindStatementNode> CollectLocalBindings(string name, IEnumerable<StatementNode> statements)
        {
            var found = new List<BindStatementNode>();
            foreach (var statement in statements)
                CollectLocalBindings(name, statement, found);
            return found;
        }

        private static void CollectLocalBindings(string name, AstNode node, List<BindStatementNode> found)
        {
            if (node is LambdaExpressionNode)
                return;
            if (node is BindStatementNode bind && bind.Name.Equals(name, StringComparison.Ordinal))
                found.Add(bind);

            foreach (var child in RecursiveAstWalker.GetAllChildren(node))
                CollectLocalBindings(name, child, found);
        }

        /// <summary>
        /// v0.15 E4 — the verdict at an invocation, read off §4.3 with no
        /// destination: the invoked value's row IS what the caller performs.
        /// Returns what is charged; reports through
        /// <see cref="InvocationSink"/> what is not. See that class for the
        /// three cells.
        /// </summary>
        private EffectSet ChargeInvokedRow(string description, (PolyRow Row, string Why) invoked, TextSpan span)
        {
            var functionId = _context.CurrentFunctionId;
            _context.Functions.TryGetValue(functionId, out var function);
            var owner = function?.Name ?? functionId;
            var (row, why) = invoked;

            // A variable the row mentions is the caller's own binder or nothing:
            // a parameter row can only name its declaration's `eff` list
            // (Calor0404 at the parser), so this is a totality guard, not a path
            // a program reaches.
            if (row.IsPolymorphic && function != null)
            {
                foreach (var (ordinal, variable) in row.Variables)
                {
                    if (ordinal >= 0 && ordinal < function.EffectParameters.Count)
                    {
                        _context.Invocations?.RecordVariableCharge(functionId, ordinal, variable);
                        continue;
                    }
                    _context.Invocations?.ReportUndeclaredEffectVariable(
                        functionId,
                        function.Effects?.Span ?? function.Span,
                        $"Function '{owner}' uses effect variable '{variable}' but does not declare "
                        + $"it\n  Effect row: charged by invoking {description} (row: {row.Display()})");
                }
            }

            if (row.Row.IsUnknown)
            {
                _context.Invocations?.ReportRowUnknown(
                    functionId,
                    span,
                    $"Invocation of {description} in '{owner}' cannot be charged: its effect row is "
                    + $"Unknown ({why}). Add §E{{…}} on the same line as the type to state what "
                    + $"{description} may do, or compile with --permissive-effects. '{owner}' is "
                    + "charged Unknown.");
                return _context.Policy == UnknownCallPolicy.Permissive
                    ? EffectSet.Empty
                    : EffectSet.Unknown;
            }

            var charged = row.Row.ToEffectSet();
            foreach (var (kind, value) in charged.Effects)
            {
                _context.Invocations?.RecordProvenance(
                    functionId,
                    EffectSetExtensions.ToSurfaceCode(kind, value),
                    $"charged by invoking {description} (row: {row.Display()})");
            }

            if (row.Row.IsAssumed && !row.Row.Reasons.IsEmpty)
            {
                var reasons = row.Row.Reasons;
                var shown = string.Join("; ", reasons.Take(3));
                if (reasons.Count > 3)
                    shown += $"; and {reasons.Count - 3} more";
                _context.Invocations?.ReportRowUnknown(
                    functionId,
                    span,
                    $"Invocation of {description} in '{owner}' is charged {row.Display()} under an "
                    + $"assumption: {shown}. The row is charged as an assumption, not a proof.");
            }

            return charged;
        }

        private bool IsProvenInternalDottedTarget(string target)
        {
            var lastDot = target.LastIndexOf('.');
            if (lastDot <= 0)
                return false;

            var receiver = target[..lastDot];
            var headDot = receiver.IndexOf('.');
            var head = headDot > 0 ? receiver[..headDot] : receiver;
            if (head is "this" or "self")
                return _context.OwnerClass != null;

            if (_context.ClassesByName.ContainsKey(head)
                || _context.InterfacesByName.ContainsKey(head))
            {
                return true;
            }

            var headType = ResolveLocalValueType(head);
            if (headType == null || headType == "?")
                return false;

            var typeName = StripGenericArguments(headType);
            return typeName != null
                && (_context.ClassesByName.ContainsKey(typeName)
                    || _context.InterfacesByName.ContainsKey(typeName));
        }

        /// <summary>
        /// D-W2.2 call-site resolution: if the receiver of "recv.Method" is a
        /// parameter/binding/field whose declared static type is an in-module
        /// interface or class, charge that static type's declared §E for the method.
        /// Returns null when the receiver's static type is unknown, external, or the
        /// method is not found on the static type (e.g. extension methods) — callers
        /// fall through to the existing resolution chain.
        /// </summary>
        private EffectSet? TryChargeStaticReceiverType(string target)
        {
            var lastDot = target.LastIndexOf('.');
            if (lastDot <= 0)
                return null;
            var receiver = target[..lastDot];
            var methodName = target[(lastDot + 1)..];
            if (receiver.Contains('.'))
                return null; // chained receivers not modeled — fall through

            var receiverType = ResolveLocalValueType(receiver);
            if (receiverType == null)
                return null;
            var typeName = StripGenericArguments(receiverType);
            if (typeName == null)
                return null;

            if (_context.InterfacesByName.TryGetValue(typeName, out var iface))
            {
                var sig = FindInterfaceMethodSignature(iface, methodName);
                if (sig != null)
                {
                    // Absent §E on the interface method is the declared-pure contract.
                    return GetDeclaredEffects(sig.Effects);
                }
                return null;
            }

            if (_context.ClassesByName.TryGetValue(typeName, out var cls))
            {
                var resolved = FindClassMethod(cls, methodName);
                if (resolved != null)
                {
                    var (ownerName, method) = resolved.Value;
                    // Prefer the declared contract (dispatch-safe under Calor0420);
                    // fall back to computed effects when no declaration exists.
                    if (method.Effects != null)
                        return GetDeclaredEffects(method.Effects);
                    var id = $"{ownerName}.{method.Id}";
                    if (_context.ComputedEffects.TryGetValue(id, out var computed))
                        return computed;
                    if (_context.SccMembers.Contains(id))
                        return _context.ComputedEffects.GetValueOrDefault(id, EffectSet.Empty);
                }
                return null;
            }

            return null;
        }

        private MethodSignatureNode? FindInterfaceMethodSignature(InterfaceDefinitionNode iface, string methodName)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<InterfaceDefinitionNode>();
            queue.Enqueue(iface);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!visited.Add(current.Name))
                    continue;
                var signatures = current.Methods
                    .Where(m => m.Name.Equals(methodName, StringComparison.Ordinal))
                    .Take(2)
                    .ToArray();
                if (signatures.Length == 1)
                    return signatures[0];
                if (signatures.Length > 1)
                    return null;
                foreach (var baseName in current.BaseInterfaces)
                {
                    var stripped = StripGenericArguments(baseName);
                    if (stripped != null && _context.InterfacesByName.TryGetValue(stripped, out var baseIface))
                        queue.Enqueue(baseIface);
                }
            }
            return null;
        }

        private (string OwnerName, MethodNode Method)? FindClassMethod(ClassDefinitionNode cls, string methodName)
        {
            var current = cls;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (current != null && visited.Add(current.Name))
            {
                var methods = CallGraphAnalysis.EnumerateMethods(current)
                    .Where(m => m.Name.Equals(methodName, StringComparison.Ordinal))
                    .Take(2)
                    .ToArray();
                if (methods.Length == 1)
                    return (current.Name, methods[0]);
                if (methods.Length > 1)
                    return null;
                var baseName = StripGenericArguments(current.BaseClass);
                current = baseName != null && _context.ClassesByName.TryGetValue(baseName, out var baseCls)
                    ? baseCls
                    : null;
            }
            return null;
        }

        private (string OwnerName, PropertyNode Property)? FindClassProperty(
            ClassDefinitionNode cls,
            string propertyName)
        {
            var current = cls;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (current != null && visited.Add(current.Name))
            {
                var property = CallGraphAnalysis.EnumerateProperties(current)
                    .FirstOrDefault(candidate =>
                        candidate.Name.Equals(propertyName, StringComparison.Ordinal));
                if (property != null)
                    return (current.Name, property);
                current = ResolveBaseClass(current);
            }
            return null;
        }

        private (string OwnerName, EventDefinitionNode Event)? FindClassEvent(
            ClassDefinitionNode cls,
            string eventName)
        {
            var current = cls;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (current != null && visited.Add(current.Name))
            {
                var evt = CallGraphAnalysis.EnumerateEvents(current)
                    .FirstOrDefault(candidate =>
                        candidate.Name.Equals(eventName, StringComparison.Ordinal));
                if (evt != null)
                    return (current.Name, evt);
                current = ResolveBaseClass(current);
            }
            return null;
        }

        private ClassDefinitionNode? ResolveBaseClass(ClassDefinitionNode cls)
        {
            var baseName = StripGenericArguments(cls.BaseClass);
            return baseName != null
                && _context.ClassesByName.TryGetValue(baseName, out var baseClass)
                    ? baseClass
                    : null;
        }

        private bool TryResolveClass(string typeName, out ClassDefinitionNode cls)
        {
            var shortType = StripGenericArguments(typeName);
            if (shortType != null && _context.ClassesByName.TryGetValue(shortType, out cls!))
                return true;

            var lastDot = shortType?.LastIndexOf('.') ?? -1;
            if (lastDot >= 0
                && _context.ClassesByName.TryGetValue(shortType![(lastDot + 1)..], out cls!))
            {
                return true;
            }

            cls = null!;
            return false;
        }

        /// <summary>
        /// v0.15 E1 slice 2b — the binder's RECEIVER types for the current
        /// function, fetched once from the side channel
        /// (<see cref="CallGraphAnalysis.BoundValueTypes"/>), keyed by the
        /// receiver path as the call target spells it. Empty when binding threw,
        /// in which case every resolver below behaves exactly as it did before
        /// this slice.
        ///
        /// <para>Receivers only, in the sense of what is COLLECTED: a name that
        /// is never a receiver anywhere in this function is absent here and
        /// resolves through the AST as before — the string this pass gets back
        /// is quoted verbatim in the invocation diagnostics' messages (E4's
        /// Calor0425 and the residual Calor0418), so the source spelling has
        /// to survive.</para>
        ///
        /// <para><b>But the map is keyed by name, not by position</b> (review
        /// round 1, finding 6): a name used as a receiver ONCE answers from here
        /// at EVERY occurrence in the function, receiver or not. The ambiguity
        /// rule in <see cref="CallGraphAnalysis.BoundValueTypes"/> is what makes
        /// that sound — a name the binder types two ways is dropped. The spread
        /// is also load-bearing rather than merely tolerated: it is how the
        /// Unresolved branch below is reachable at a BARE call target, which is
        /// what <c>E1Slice2b_ReportedUnresolvedReceiver_VetoesTheAstSentinel</c>
        /// pins. Keying by position would need a position argument threaded
        /// through eleven call sites AND that veto re-established at the
        /// bare-target position; deferred.</para>
        /// </summary>
        private IReadOnlyDictionary<string, Binding.BoundTypes.BoundType>? _boundValueTypes;

        private IReadOnlyDictionary<string, Binding.BoundTypes.BoundType> BoundValueTypes =>
            _boundValueTypes ??= _context.CallGraph.BoundValueTypes(_context.CurrentFunctionId);

        /// <summary>What the bound tree said about a name.</summary>
        private enum BoundValueAnswerKind
        {
            /// <summary>The binder has nothing for this name — use the AST strings.</summary>
            NoAnswer,

            /// <summary>
            /// <c>UnresolvedBoundType</c>: the binder LOOKED and could not name
            /// the type. Fail closed — the AST strings must not supply a guess in
            /// its place, because the whole point of §D6's exit ramp is that
            /// "unresolved" stops being spelled like a type.
            /// </summary>
            Unresolved,

            /// <summary>A type the binder can name.</summary>
            Typed,
        }

        /// <summary>
        /// v0.15 E1 slice 2b — asks the bound tree for a value's type before any
        /// AST string is consulted.
        ///
        /// <para>The answer is normalized into the vocabulary the rest of this
        /// pass already speaks, so that consulting the binder first cannot move
        /// a diagnostic's TEXT: a function type answers with the pass's existing
        /// <c>"Func&lt;&gt;"</c> marker — the same string
        /// <see cref="FindLocalDeclarationType"/> produces for a lambda-initialized
        /// <c>§B</c> — and every other kind answers with its
        /// <c>DisplayString</c>, which for a bound variable IS the symbol's type
        /// string, i.e. the same text the AST search would have found.</para>
        ///
        /// <para><c>OBJECT</c> and <c>?</c> are treated as NO answer rather than
        /// as a type: they are the binder's non-answers, and returning them as
        /// types would let a placeholder be keyed as a manifest type. A receiver
        /// the binder actually looked at and could not name arrives as
        /// <c>UnresolvedBoundType</c> instead, since slice 2a.</para>
        /// </summary>
        private BoundValueAnswerKind AskBoundTree(string name, out string typeName)
        {
            typeName = "";
            if (!BoundValueTypes.TryGetValue(name, out var type))
                return BoundValueAnswerKind.NoAnswer;

            // Authoritative only when the binder REPORTED it (Calor0270). An
            // unreported UnresolvedBoundType is a binder limitation — a member
            // chain, or a converter-synthesized _chainNNN temporary — and
            // suppressing the AST fallback for those deletes resolution the
            // fallback still performs. Measured: 05-02/05-03.approved.calr go
            // from clean to Calor0411 + Calor0410 on '_chainWhere005.ToList'.
            //
            // ───────────────────────────────────────────────────────────────
            // v0.15 E1 slice 2c, review round 1 (MAJOR 2) — READ THIS BEFORE
            // TRUSTING ANY OLDER COMMENT HERE. Everything slice 2b wrote about
            // this branch being pinned is now FALSE, and the previous text said
            // otherwise. Corrected in place rather than deleted, because "this
            // is retained without a pin" is exactly the kind of claim that has
            // to survive in the source.
            //
            // THIS BRANCH HAS NO DISCRIMINATING PIN. Delete it and the whole
            // Enforcement suite stays green (381/381 at slice 2c; the number will drift, the greenness is the claim). Slice 2b's
            // E1Slice2b_ReportedUnresolvedReceiver_VetoesTheAstSentinel is
            // RETAINED as a behavioural pin — it asserts the fixture's outcome
            // — but it no longer fails when this branch goes, and its control
            // was renamed (see
            // E1Slice2c_BareCallOnUnknownTypedBinding_IsCalor0411WithOrWithoutAReceiverUse).
            //
            // Why it stopped discriminating: slice 2c guards the AST's "?"
            // sentinel at InferFromBareNameTarget (UnknownLocalTypeSentinel), so
            // the sentinel is no longer mistaken for a type there. Slice 2b's
            // comment said "InferFromBareNameTarget tests != null, not the
            // sentinel" — that is no longer true, and it was the mechanism the
            // whole 0418-vs-0411 argument rested on. The outer guard now answers
            // the same question this branch answers, so the fixture reaches
            // Calor0411 either way. Round 1 probed four more shapes looking for
            // one where the two layers disagree and found none.
            //
            // WHY IT IS KEPT ANYWAY. It states the fail-closed rule at the layer
            // that OWNS it: "the binder looked, told the author it could not name
            // the type (Calor0270), and the AST does not get to guess in its
            // place". The outer guard is a sentinel check in one consumer; this
            // is the rule. E2 needs the rule here the moment chains carry types,
            // because then AskBoundTree starts answering Typed for dotted paths
            // and every consumer — not just the bare-target one — depends on
            // Unresolved meaning Unresolved.
            //
            // WHAT E2 OWES: a pin that fails when this branch is deleted, i.e. a
            // shape where a Reported UnresolvedBoundType reaches a consumer whose
            // AST fallback returns a REAL type rather than the sentinel. That
            // shape does not exist today; when chain typing lands it will.
            //
            // The reachability path is the NAME-KEYED side channel (see
            // CallGraphAnalysis.BoundValueTypes). A name used as a receiver
            // ANYWHERE in the function answers from here at EVERY occurrence,
            // including positions the channel never collects:
            //
            //     §B{u} §C{Mystery.Make} §/C
            //     §C{u.Run} §/C     <- receiver use: puts u's Reported
            //                          UnresolvedBoundType into the channel
            //     §C{u} §/C         <- bare target: reads it back
            //
            // CORPUS claim, and only that: over all 886 committed .calr files
            // every unresolved receiver arriving here is Reported=false — 32
            // sites, all _chainNNN or member chains, zero Reported=true. (Slice
            // 2b's comment said 301; that was the count of a narrower sweep and
            // is corrected here to the demand ledger's denominator.) So the
            // branch changes nothing on the committed corpus, which is why the
            // ledgers and transcripts are unmoved; it is NOT evidence about
            // observability either way. Reproduce the sweep by tracing
            // (name, Reported, ResolveLocalValueTypeFromAst(name)) here.
            // ───────────────────────────────────────────────────────────────
            if (type is Binding.BoundTypes.UnresolvedBoundType unresolved)
            {
                return unresolved.Reported
                    ? BoundValueAnswerKind.Unresolved
                    : BoundValueAnswerKind.NoAnswer;
            }

            var display = type.DisplayString;
            if (IsFunctionBoundType(type)
                || display.StartsWith("LAMBDA(", StringComparison.Ordinal)
                || display.StartsWith("ASYNC_LAMBDA(", StringComparison.Ordinal))
            {
                // The pass's existing marker for "function-typed by construction".
                // The LAMBDA( spellings appear on a §B whose TypeName the binder
                // inferred from a lambda's DisplayString (Binder.cs:1320), which
                // is a NominalBoundType carrying that text, not a function type.
                typeName = "Func<>";
                return BoundValueAnswerKind.Typed;
            }

            if (string.IsNullOrWhiteSpace(display)
                || display is "?" or "OBJECT")
            {
                return BoundValueAnswerKind.NoAnswer;
            }

            typeName = display;
            return BoundValueAnswerKind.Typed;
        }

        /// <summary>
        /// v0.15 E1 slice 2c — the BOUND receiver behind a receiver path, when
        /// the binder typed it well enough to key a manifest lookup on.
        ///
        /// <para>The accepted set is exactly <see cref="AskBoundTree"/>'s
        /// <c>Typed</c> answer MINUS function types. That is not an arbitrary
        /// narrowing: <c>ResolveVariableType</c> — the string path this key
        /// replaces — returns null for <c>"Func&lt;&gt;"</c> and <c>"?"</c>, so
        /// accepting them here would resolve members the pre-slice compiler
        /// never resolved, and the effects of a function value are E2/E4's
        /// business, not a manifest lookup's.</para>
        ///
        /// <para>A <c>Reported</c> <c>UnresolvedBoundType</c> is excluded for
        /// the same fail-closed reason slice 2b gives, and an unreported one is
        /// excluded because there is nothing to key on either way — both fall
        /// through to the caller's AST-derived string.</para>
        /// </summary>
        private Binding.BoundTypes.BoundType? KeyableBoundReceiver(string? receiverPath)
        {
            if (string.IsNullOrEmpty(receiverPath))
                return null;
            if (!BoundValueTypes.TryGetValue(receiverPath, out var type))
                return null;
            if (type is Binding.BoundTypes.UnresolvedBoundType)
                return null;
            if (IsFunctionBoundType(type))
                return null;

            var display = type.DisplayString;
            if (string.IsNullOrWhiteSpace(display)
                || display is "?" or "OBJECT"
                || display.StartsWith("LAMBDA(", StringComparison.Ordinal)
                || display.StartsWith("ASYNC_LAMBDA(", StringComparison.Ordinal))
            {
                return null;
            }

            return type;
        }

        /// <summary>
        /// v0.15 E1 slice 2c — the resolver key for a member reached through
        /// <paramref name="receiverPath"/>.
        ///
        /// <para>When the binder typed that receiver the key is built from its
        /// <c>BoundType</c> — symbol identity, which is the whole point of E1
        /// exit pin (c). Otherwise it is built from
        /// <paramref name="declaringType"/>, the manifest-ready string the AST
        /// fallbacks produce, through the single
        /// <see cref="EffectResolverKey.FromStrings"/> factory so the fallback
        /// is COUNTED rather than invisible.</para>
        ///
        /// <para>The two keys name the same type by construction:
        /// <see cref="EffectResolverKey.FromBoundReceiver"/> applies the same
        /// <c>MapShortTypeNameToFullName</c> that
        /// <see cref="ResolveVariableType"/> applies to the bound tree's own
        /// answer. Re-keying therefore moves no diagnostic — which is what the
        /// unchanged D-A demand ledger, Calor0270 ledger and
        /// <c>LosslessFormattingTests</c> observe.</para>
        /// </summary>
        private EffectResolverKey ResolverKey(
            string? receiverPath,
            string declaringType,
            string memberName,
            IReadOnlyList<string>? parameterTypes = null,
            EffectMemberKind kind = EffectMemberKind.Method)
        {
            var bound = KeyableBoundReceiver(receiverPath);
            return bound != null
                ? EffectResolverKey.FromBoundReceiver(bound, memberName, parameterTypes, kind)
                : EffectResolverKey.FromStrings(declaringType, memberName, parameterTypes, kind);
        }

        /// <summary>
        /// Resolves a bare name to the declared type of the value it denotes.
        ///
        /// <para>v0.15 E1 slice 2b — the BOUND TREE answers first
        /// (<see cref="AskBoundTree"/>). An <c>UnresolvedBoundType</c> ends the
        /// lookup with null: the binder looked and could not name the type, and
        /// the AST strings do not get to guess one in its place (fail-closed,
        /// design doc §8.1 / P17).</para>
        ///
        /// <para>The AST search below is the FALLBACK, for every shape the
        /// receiver side channel does not carry:</para>
        /// <list type="bullet">
        ///   <item><description><c>function.Parameters[].TypeName</c> — the name
        ///   is not used as a call receiver anywhere in this function (it is a
        ///   bare call target, a method-group argument, or simply read), so no
        ///   bound receiver exists to read a type off.</description></item>
        ///   <item><description><c>FindLocalDeclarationType</c> /
        ///   <c>FindForeachVariableType</c> — a <c>§B</c> or <c>§FE</c> variable
        ///   in a statement shape the binder does not bind (interop content,
        ///   unsupported constructs), a module whose binding threw, and a
        ///   receiver path whose bound answers disagreed between two call sites
        ///   and were dropped as ambiguous.</description></item>
        ///   <item><description><c>OwnerClass.Fields[].TypeName</c> — a field of
        ///   the enclosing class used somewhere other than a receiver
        ///   position.</description></item>
        /// </list>
        /// <para>A lambda-initialized binding without an explicit type reports the
        /// marker type "Func&lt;&gt;" on both paths.</para>
        /// </summary>
        private string? ResolveLocalValueType(string name)
        {
            switch (AskBoundTree(name, out var boundTypeName))
            {
                case BoundValueAnswerKind.Unresolved:
                    return null;
                case BoundValueAnswerKind.Typed:
                    return boundTypeName;
            }

            return ResolveLocalValueTypeFromAst(name);
        }

        /// <summary>
        /// #1104 — the names whose AST-side resolution is on the stack right
        /// now. Every cycle through the local-type search re-enters
        /// <see cref="ResolveLocalValueTypeFromAst"/> with a name (a call
        /// initializer is typed by resolving its receiver and its arguments,
        /// and <c>FindLocalDeclarationType</c> / <c>FindForeachVariableType</c>
        /// are reached only from here), so this ONE choke point sees every
        /// cycle. Per inferrer, i.e. per function: names are function-scoped
        /// and an on-demand inference of another function gets its own.
        ///
        /// <para>Review round 1 (m3) — <see cref="FindForeachVariableType"/>
        /// also calls <see cref="FindLocalDeclarationType"/> for a DIFFERENT
        /// name (a §FE collection's), without going back through the ask. That
        /// hop cannot make a cycle invisible: it reaches no ask, so it cannot
        /// re-enter this set, and every frame it costs is still charged to the
        /// shared depth counter. It is named here so the "one choke point"
        /// claim is read as being about ASKS, not about frames — frames are
        /// bounded separately by <see cref="TryEnterAstFrame"/>.</para>
        /// </summary>
        private readonly HashSet<string> _astResolutionInProgress = new(StringComparer.Ordinal);

        /// <summary>#1104 — nesting depth of the AST-side resolution (backstop cap).</summary>
        private int _astResolutionDepth;

        /// <summary>
        /// The pre-slice-2b AST search, extracted so the resolver order above
        /// reads as "bound first, then this" and so the fallback can be probed
        /// on its own.
        ///
        /// <para>#1104 — bounded (see <see cref="AstResolutionBound"/>): a name
        /// already being resolved is a cycle and answers
        /// <see cref="UnknownLocalTypeSentinel"/>; past the depth cap every
        /// ask answers the sentinel. Neither guard can fire on an acyclic chain
        /// shorter than the cap, so nothing else changes.</para>
        /// </summary>
        private string? ResolveLocalValueTypeFromAst(string name)
        {
            var bound = _context.AstResolution;
            if (!_astResolutionInProgress.Add(name))
            {
                bound.RecordCycleStop(name);
                return UnknownLocalTypeSentinel;
            }

            // The cap check below is TryEnterAstFrame's, inlined: this one has
            // to leave the visited set on the way out and answer the sentinel
            // rather than a bool, which the shared helper cannot do. The two
            // are equivalent today (same comparison, same counters) and must be
            // changed together — if this method ever stops needing the sentinel
            // return, delete the copy and call TryEnterAstFrame.
            if (_astResolutionDepth >= bound.DepthCap)
            {
                _astResolutionInProgress.Remove(name);
                bound.RecordDepthCapStop(name);
                return UnknownLocalTypeSentinel;
            }

            _astResolutionDepth++;
            bound.RecordDepth(_astResolutionDepth);
            try
            {
                return ResolveLocalValueTypeFromAstUnbounded(name);
            }
            finally
            {
                _astResolutionDepth--;
                _astResolutionInProgress.Remove(name);
            }
        }

        /// <summary>
        /// #1104, review round 1 (CRITICAL C1) — enters one FRAME of the
        /// AST-side search and reports whether the cap admits it.
        ///
        /// <para>The first cut of this guard counted <i>name asks</i> only, and
        /// that does not bound the stack: <see cref="FindLocalDeclarationType"/>
        /// and <see cref="FindForeachVariableType"/> recurse STRUCTURALLY into
        /// every nested statement body, and that walk runs inside each ask. A
        /// chain whose links sit progressively deeper inside <c>§IF</c> blocks
        /// therefore costs frames in proportion to links × nesting while an
        /// ask-counter saw only <c>links</c>. Measured on the ask-counting
        /// version at cap 64: 40 such links, no cycle, all names distinct,
        /// <c>DepthCapStops == 0</c> — and the stack overflowed on a 1 MB
        /// thread (the size a test host actually gives a test). Every recursion
        /// inside the search now shares this one counter, so the cap bounds the
        /// real frame cost rather than a proxy for it.</para>
        /// </summary>
        private bool TryEnterAstFrame(string name)
        {
            var bound = _context.AstResolution;
            if (_astResolutionDepth >= bound.DepthCap)
            {
                bound.RecordDepthCapStop(name);
                return false;
            }

            _astResolutionDepth++;
            bound.RecordDepth(_astResolutionDepth);
            return true;
        }

        private void ExitAstFrame() => _astResolutionDepth--;

        private string? ResolveLocalValueTypeFromAstUnbounded(string name)
        {
            if (!_context.Functions.TryGetValue(_context.CurrentFunctionId, out var function))
                return null;

            // FALLBACK: declared parameter type string.
            foreach (var parameter in function.Parameters)
            {
                if (parameter.Name.Equals(name, StringComparison.Ordinal))
                    return parameter.TypeName;
            }

            // FALLBACK: §B declaration / §FE variable type string, found lexically.
            var declaredType = FindLocalDeclarationType(name, function.Body);
            if (declaredType != null)
                return declaredType;

            var foreachType = FindForeachVariableType(name, function.Body);
            if (foreachType != null)
                return foreachType;

            // FALLBACK: enclosing-class field type string.
            var field = _context.OwnerClass?.Fields.FirstOrDefault(
                f => f.Name.Equals(name, StringComparison.Ordinal));
            if (field != null)
                return field.TypeName;

            return null;
        }

        private string? FindForeachVariableType(
            string name,
            IReadOnlyList<StatementNode> rootStatements)
        {
            // #1104 C1 — one frame per structural level, sharing the ask
            // counter (see TryEnterAstFrame). Declining at the cap loses a
            // §FE variable's type, never invents one.
            string? Search(IEnumerable<StatementNode> statements)
            {
                if (!TryEnterAstFrame(name))
                    return null;
                try
                {
                    return SearchCore(statements);
                }
                finally
                {
                    ExitAstFrame();
                }
            }

            string? SearchCore(IEnumerable<StatementNode> statements)
            {
                foreach (var statement in statements)
                {
                    if (statement is ForeachStatementNode foreachStatement)
                    {
                        if (foreachStatement.VariableName.Equals(name, StringComparison.Ordinal))
                        {
                            if (!string.IsNullOrWhiteSpace(foreachStatement.VariableType)
                                && foreachStatement.VariableType is not "var" and not "OBJECT")
                            {
                                return foreachStatement.VariableType;
                            }

                            if (foreachStatement.Collection is ReferenceNode collectionReference)
                            {
                                var collectionType = FindLocalDeclarationType(
                                    collectionReference.Name,
                                    rootStatements);
                                var elementType = TryGetElementType(collectionType);
                                if (elementType != null)
                                    return elementType;
                            }
                        }
                        if (foreachStatement.IndexVariableName?.Equals(
                                name,
                                StringComparison.Ordinal) == true)
                        {
                            return "INT";
                        }

                        var nested = Search(foreachStatement.Body);
                        if (nested != null)
                            return nested;
                    }
                    else
                    {
                        var nestedStatements = statement switch
                        {
                            IfStatementNode ifStatement => ifStatement.ThenBody
                                .Concat(ifStatement.ElseIfClauses.SelectMany(clause => clause.Body))
                                .Concat(ifStatement.ElseBody ?? []),
                            ForStatementNode forStatement => forStatement.Body,
                            WhileStatementNode whileStatement => whileStatement.Body,
                            DoWhileStatementNode doWhileStatement => doWhileStatement.Body,
                            TryStatementNode tryStatement => tryStatement.TryBody
                                .Concat(tryStatement.CatchClauses.SelectMany(clause => clause.Body))
                                .Concat(tryStatement.FinallyBody ?? []),
                            UsingStatementNode usingStatement => usingStatement.Body,
                            SyncBlockNode sync => sync.Body,
                            UnsafeBlockNode unsafeBlock => unsafeBlock.Body,
                            FixedStatementNode fixedStatement => fixedStatement.Body,
                            _ => null
                        };
                        if (nestedStatements != null && Search(nestedStatements) is { } nested)
                            return nested;
                    }
                }
                return null;
            }

            return Search(rootStatements);
        }

        private static string? TryGetElementType(string? collectionType)
        {
            if (string.IsNullOrWhiteSpace(collectionType))
                return null;
            if (collectionType.StartsWith("ARRAY[element=", StringComparison.Ordinal)
                && collectionType.EndsWith(']'))
            {
                return collectionType["ARRAY[element=".Length..^1];
            }
            if (collectionType.StartsWith('[') && collectionType.EndsWith(']'))
                return collectionType[1..^1];
            var genericStart = collectionType.IndexOf('<');
            return genericStart > 0 && collectionType.EndsWith('>')
                ? collectionType[(genericStart + 1)..^1].Split(',')[0].Trim()
                : null;
        }

        /// <summary>
        /// #1104 C1 — the structural half of the search, and the half that
        /// actually multiplies frames: it recurses into every nested statement
        /// body, once per level, INSIDE each name ask. It therefore takes a
        /// frame from the same counter the ask does
        /// (<see cref="TryEnterAstFrame"/>). Declining at the cap returns "not
        /// declared in this body", which is what an unsearched body honestly
        /// says; the ask above it then falls through to its remaining
        /// fallbacks. It never invents a declaration.
        /// </summary>
        private string? FindLocalDeclarationType(string name, IEnumerable<StatementNode> statements)
        {
            if (!TryEnterAstFrame(name))
                return null;
            try
            {
                return FindLocalDeclarationTypeCore(name, statements);
            }
            finally
            {
                ExitAstFrame();
            }
        }

        private string? FindLocalDeclarationTypeCore(
            string name,
            IEnumerable<StatementNode> statements)
        {
            foreach (var statement in statements)
            {
                switch (statement)
                {
                    case BindStatementNode bind when bind.Name.Equals(name, StringComparison.Ordinal):
                        if (bind.TypeName != null)
                            return bind.TypeName;
                        if (bind.Initializer is NewExpressionNode newExpr)
                            return newExpr.TypeName;
                        if (bind.Initializer is LambdaExpressionNode)
                            return "Func<>";
                        if (bind.Initializer is CallExpressionNode call
                            && InferKnownCallResultType(call) is { } callResultType)
                        {
                            return callResultType;
                        }
                        // Known value, unknown type. v0.15 E1 slice 2c: this
                        // stays a sentinel rather than becoming null, because
                        // null here means "no such value" and the two are
                        // consumed differently — InferFromReference /
                        // InferSetterEffects report an unknown operation for the
                        // first and charge nothing for the second. Collapsing
                        // them was implemented and rejected: it turns an
                        // untyped receiver's property read from a reported
                        // unknown operation into EffectSet.Empty, which is a
                        // fail-OPEN change and the opposite of what this slice
                        // is for. The one site where the sentinel was a guess —
                        // a bare call target — guards it explicitly
                        // (InferFromBareNameTarget, UnknownLocalTypeSentinel).
                        return UnknownLocalTypeSentinel;
                    case IfStatementNode ifStmt:
                        var inIf = FindLocalDeclarationType(name, ifStmt.ThenBody)
                            ?? ifStmt.ElseIfClauses.Select(c => FindLocalDeclarationType(name, c.Body)).FirstOrDefault(t => t != null)
                            ?? (ifStmt.ElseBody != null ? FindLocalDeclarationType(name, ifStmt.ElseBody) : null);
                        if (inIf != null) return inIf;
                        break;
                    case ForStatementNode forStmt:
                        if (FindLocalDeclarationType(name, forStmt.Body) is { } inFor) return inFor;
                        break;
                    case WhileStatementNode whileStmt:
                        if (FindLocalDeclarationType(name, whileStmt.Body) is { } inWhile) return inWhile;
                        break;
                    case DoWhileStatementNode doWhile:
                        if (FindLocalDeclarationType(name, doWhile.Body) is { } inDoWhile) return inDoWhile;
                        break;
                    case ForeachStatementNode foreachStmt:
                        if (FindLocalDeclarationType(name, foreachStmt.Body) is { } inForeach) return inForeach;
                        break;
                    case MatchStatementNode matchStmt:
                        var inMatch = matchStmt.Cases.Select(c => FindLocalDeclarationType(name, c.Body)).FirstOrDefault(t => t != null);
                        if (inMatch != null) return inMatch;
                        break;
                    case TryStatementNode tryStmt:
                        var inTry = FindLocalDeclarationType(name, tryStmt.TryBody)
                            ?? tryStmt.CatchClauses.Select(c => FindLocalDeclarationType(name, c.Body)).FirstOrDefault(t => t != null)
                            ?? (tryStmt.FinallyBody != null ? FindLocalDeclarationType(name, tryStmt.FinallyBody) : null);
                        if (inTry != null) return inTry;
                        break;
                    case UsingStatementNode usingStmt:
                        // §USING{Type:name} declares a typed resource variable.
                        if (name.Equals(usingStmt.VariableName, StringComparison.Ordinal))
                            return usingStmt.VariableType ?? "?";
                        if (FindLocalDeclarationType(name, usingStmt.Body) is { } inUsing) return inUsing;
                        break;
                    case SyncBlockNode sync:
                        if (FindLocalDeclarationType(name, sync.Body) is { } inSync) return inSync;
                        break;
                    case UnsafeBlockNode unsafeBlock:
                        if (FindLocalDeclarationType(name, unsafeBlock.Body) is { } inUnsafe) return inUnsafe;
                        break;
                    case FixedStatementNode fixedStmt:
                        if (FindLocalDeclarationType(name, fixedStmt.Body) is { } inFixed) return inFixed;
                        break;
                }
            }
            return null;
        }

        private string? InferKnownCallResultType(CallExpressionNode call)
        {
            var target = call.Target;
            var lastDot = target.LastIndexOf('.');
            if (lastDot <= 0)
                return null;
            var receiver = target[..lastDot];
            var methodName = lastDot >= 0 ? target[(lastDot + 1)..] : target;
            var receiverType = ResolveVariableType(receiver);
            if (receiverType == null)
                return null;
            var argumentTypes = call.Arguments.Select(InferExpressionType).ToArray();
            var resolution = _context.Resolver.Resolve(
                ResolverKey(receiver, receiverType, methodName, argumentTypes));
            if (resolution.Status == EffectResolutionStatus.Unknown)
            {
                resolution = _context.Resolver.Resolve(ResolverKey(
                    receiver, receiverType, methodName, argumentTypes,
                    EffectMemberKind.Extension));
            }
            if (resolution.Status == EffectResolutionStatus.Unknown)
                return null;

            return methodName switch
            {
                "Where" or "Select" or "SelectMany"
                    or "OrderBy" or "OrderByDescending"
                    or "ThenBy" or "ThenByDescending"
                    or "Distinct" or "DistinctBy"
                    or "Skip" or "Take" or "SkipWhile" or "TakeWhile"
                    or "Concat" or "Append" or "Prepend"
                    => "System.Collections.Generic.IEnumerable`1",
                "ToList" => "System.Collections.Generic.List`1",
                "ToArray" => "System.Array",
                _ => null
            };
        }

        /// <summary>
        /// v0.15 E1 slice 2b — "is the value called <paramref name="name"/> a
        /// function value?", asked of the BOUND TYPE first
        /// (<see cref="EffectEnforcementPass.IsFunctionBoundType"/>: a
        /// <c>FunctionBoundType</c>, or a nominal type whose declaration is a
        /// <c>§DEL</c>) and only then of the type string.
        ///
        /// <para>SURVIVING FALLBACK — <see cref="IsFunctionTypeName"/>, for the
        /// shapes whose function-typedness exists only as text: a declared
        /// <c>Func&lt;…&gt;</c>/<c>Action</c>/<c>Predicate&lt;…&gt;</c>
        /// parameter, binding or field (the binder builds a plain nominal type
        /// from the type string, with no declaration attached), and a
        /// module-level <c>§DEL</c> name reached through
        /// <c>_context.DelegateTypeNames</c>.</para>
        /// </summary>
        /// <para>The structural half answers for a receiver the side channel
        /// carries (<c>f.Invoke</c> where <c>f</c> is a lambda or a <c>§DEL</c>
        /// value); a bare call target is not a receiver, so that site reduces to
        /// the string test today — E4's invocation charge asks the declared
        /// <c>FunctionBoundType</c> (<see cref="CallGraphAnalysis.DeclaredFunctionTypes"/>)
        /// alongside it, so a §CSHARP-declared delegate is a function value there.
        /// </para>
        private bool IsFunctionValued(string name, string typeName) =>
            IsFunctionBoundType(BoundValueTypes.GetValueOrDefault(name))
            || IsFunctionTypeName(typeName);

        // v0.15 E2 slice b — the list moved to Binding/TypeIdentity so the
        // binder's Calor0405 check (pin P6) and this pass share ONE predicate.
        // Forwarder kept so this pass's call sites are unchanged.
        private bool IsFunctionTypeName(string typeName) =>
            TypeIdentity.IsFunctionTypeName(
                typeName,
                name => _context.DelegateTypeNames.Contains(name));

        private void ReportUnknownCall(string target, TextSpan span)
        {
            // Calor0411: Unknown external call
            var severity = _context.StrictEffects
                ? DiagnosticSeverity.Error
                : DiagnosticSeverity.Warning;

            if (_context.Policy == UnknownCallPolicy.Strict || _context.StrictEffects)
            {
                _context.Diagnostics.Report(
                    span,
                    DiagnosticCode.UnknownExternalCall,
                    $"Unknown external call to '{target}'. Add effect declaration in a .calor-effects.json manifest.",
                    severity);
            }
            else if (_context.Policy == UnknownCallPolicy.Warn)
            {
                _context.Diagnostics.Report(
                    span,
                    DiagnosticCode.UnknownExternalCall,
                    $"Unknown external call to '{target}' - assuming worst-case effects. Consider adding to manifest.",
                    DiagnosticSeverity.Warning);
            }
        }

        private static (string TypeName, string MethodName) ParseCallTarget(string target)
        {
            // Handle patterns like "Console.WriteLine", "File.ReadAllText", "System.IO.File.ReadAllText"
            var lastDot = target.LastIndexOf('.');
            if (lastDot <= 0)
                return ("", "");

            var methodName = target[(lastDot + 1)..];
            var typePart = target[..lastDot];

            // If type part doesn't contain a dot, try common namespaces
            if (!typePart.Contains('.'))
            {
                // Map common short names to full types
                typePart = MapShortTypeNameToFullName(typePart);
            }

            return (typePart, methodName);
        }

        private FunctionNode? FindInternalFunctionByName(string name)
        {
            var matches = _context.Functions
                .Where(pair => pair.Value.Name.Equals(name, StringComparison.Ordinal))
                .Where(pair => _context.ComputedEffects.ContainsKey(pair.Key)
                    || _context.SccMembers.Contains(pair.Key))
                .Select(pair => pair.Value)
                .Take(2)
                .ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }

        /// <summary>
        /// Resolves a variable name to its manifest-ready declared type: parameters,
        /// §B bindings (explicit type or §NEW initializer, anywhere in the body),
        /// §USING resource variables, and enclosing-class fields.
        /// E.g., "§B{client} §NEW{HttpClient}" → "client" resolves to "System.Net.Http.HttpClient".
        /// </summary>
        private string? ResolveVariableType(string variableName)
        {
            var declared = ResolveLocalValueType(variableName);
            if (declared == null || declared == "?" || declared == "Func<>")
                return null;
            return MapShortTypeNameToFullName(declared);
        }

        /// <summary>
        /// Resolves a dotted receiver path (<c>a.b</c> in <c>a.b.M</c>) to a
        /// manifest-ready type, charging any property getters walked on the way.
        ///
        /// <para>v0.15 E1 slice 2b — the BOUND TREE answers first: the whole
        /// path is a receiver the binder attached to the call node, so if it can
        /// name that receiver's type there is nothing to walk.</para>
        ///
        /// <para>An <c>UnresolvedBoundType</c> here does NOT end the lookup, and
        /// that is deliberate. Slice 2a types EVERY member chain
        /// <c>UnresolvedBoundType</c> — PR #1095 records the shape as "binder
        /// limitation, unactionable", which is why it is marked but never
        /// reported as Calor0270. Treating a binder limitation as an
        /// authoritative "not a type" would delete resolution the property walk
        /// below still performs. The fail-closed rule applies where slice 2a's
        /// exit ramp is an actual DECISION: a bare receiver head, handled in
        /// <see cref="ResolveLocalValueType"/>.</para>
        ///
        /// <para>SURVIVING FALLBACK: the member-by-member property/field walk,
        /// for chains the binder does not type.</para>
        /// </summary>
        private (string? Type, EffectSet Effects) ResolveReceiverChain(string receiverPath)
        {
            var parts = receiverPath.Split('.');
            if (parts.Length < 2)
                return (null, EffectSet.Empty);

            // FIXME(E2): this branch returns EffectSet.Empty, discarding the
            // property-getter effects the member walk below charges as it steps
            // through `a.b.c`. Knowing the END type is not the same as knowing
            // that reaching it ran a getter with effects. Dead today — slice 2a
            // types every member chain UnresolvedBoundType, so AskBoundTree
            // never answers Typed for a dotted path — but it goes live the
            // moment E2 types chains, and would then silently under-charge.
            //
            // Guarded rather than left as a comment: the shortcut is taken only
            // when the walk could not have charged anything anyway, i.e. when no
            // segment after the head resolves to a property getter with effects.
            // Otherwise fall through and let the walk charge them.
            if (AskBoundTree(receiverPath, out var boundPathType) == BoundValueAnswerKind.Typed
                && boundPathType != "Func<>"
                && !ChainWalkCouldChargeEffects(parts))
            {
                return (MapShortTypeNameToFullName(boundPathType), EffectSet.Empty);
            }

            var currentType = ResolveVariableType(parts[0]);
            if (currentType == null)
                return (null, EffectSet.Empty);

            var effects = EffectSet.Empty;
            for (var i = 1; i < parts.Length; i++)
            {
                var member = parts[i];
                // Only the FIRST step has a receiver the binder may have typed
                // (the chain head). Every later step's receiver is a member type
                // this walk derived itself, so it can only be keyed on text.
                var getter = _context.Resolver.Resolve(ResolverKey(
                    i == 1 ? parts[0] : null, currentType, member, null, EffectMemberKind.Getter));
                if (getter.Status == EffectResolutionStatus.Unknown)
                    return (null, effects);
                effects = effects.Union(getter.Effects);

                var nextType = ResolveKnownMemberType(currentType, member);
                if (nextType == null)
                    return (null, effects);
                currentType = nextType;
            }

            return (currentType, effects);
        }

        /// <summary>
        /// Whether stepping through <paramref name="parts"/> the way
        /// <see cref="ResolveReceiverChain"/>'s member walk does could charge
        /// any effect. Used to decide whether the bound-type shortcut above is
        /// safe: if no getter on the way contributes effects, skipping the walk
        /// loses nothing.
        ///
        /// <para><b>FIXME(E2) — this method is UNTESTED and needs a pin the
        /// moment E2 types chains.</b> Nothing observes it today, and nothing
        /// can: its only caller is the bound-type shortcut in
        /// <see cref="ResolveReceiverChain"/>, and that shortcut is unreachable
        /// because slice 2a types EVERY member chain
        /// <c>UnresolvedBoundType</c>, so <see cref="AskBoundTree"/> never
        /// answers <c>Typed</c> for a dotted path. Deleting this method's body
        /// and returning a constant would therefore fail no test — which is
        /// exactly the condition that makes it dangerous, because the day E2
        /// gives chain expressions real types the shortcut goes live and a
        /// wrong answer here silently UNDER-CHARGES: it would skip a member
        /// walk that runs property getters with effects. E2 must land a pin
        /// that (a) drives a chain the binder types, (b) puts an effectful
        /// getter partway along it, and (c) asserts the effect is charged —
        /// before, not after, chain typing merges. Remove the method entirely
        /// once chain types carry rows and the walk's effects can be read off
        /// the type instead of re-derived.</para>
        /// </summary>
        private bool ChainWalkCouldChargeEffects(string[] parts)
        {
            var currentType = ResolveVariableType(parts[0]);
            if (currentType == null)
                return false;

            for (var i = 1; i < parts.Length; i++)
            {
                var getter = _context.Resolver.Resolve(ResolverKey(
                    i == 1 ? parts[0] : null, currentType, parts[i], null, EffectMemberKind.Getter));
                if (getter.Status == EffectResolutionStatus.Unknown)
                    return false;
                if (!getter.Effects.IsEmpty)
                    return true;

                var nextType = ResolveKnownMemberType(currentType, parts[i]);
                if (nextType == null)
                    return false;
                currentType = nextType;
            }

            return false;
        }

        private string? ResolveKnownMemberType(string receiverType, string memberName)
        {
            var shortType = StripGenericArguments(receiverType);
            if (TryResolveClass(receiverType, out var cls))
            {
                var property = CallGraphAnalysis.EnumerateProperties(cls)
                    .FirstOrDefault(candidate =>
                        candidate.Name.Equals(memberName, StringComparison.Ordinal));
                if (property != null)
                    return MapShortTypeNameToFullName(property.TypeName);
                var field = cls.Fields.FirstOrDefault(candidate =>
                    candidate.Name.Equals(memberName, StringComparison.Ordinal));
                if (field != null)
                    return MapShortTypeNameToFullName(field.TypeName);
            }

            return (receiverType, memberName) switch
            {
                ("System.String", "Length") => "System.Int32",
                ("System.Array", "Length") => "System.Int32",
                _ when receiverType.EndsWith("[]", StringComparison.Ordinal)
                    && memberName == "Length" => "System.Int32",
                _ => null
            };
        }

        private EffectSet InferFromUsing(UsingStatementNode usingStatement)
        {
            var effects = InferFromExpression(usingStatement.Resource)
                .Union(InferFromStatements(usingStatement.Body));
            var resourceType = usingStatement.VariableType;
            if (string.IsNullOrWhiteSpace(resourceType))
            {
                resourceType = usingStatement.Resource switch
                {
                    NewExpressionNode creation => creation.TypeName,
                    ReferenceNode reference => ResolveLocalValueType(reference.Name),
                    _ => null
                };
            }

            if (string.IsNullOrWhiteSpace(resourceType) || resourceType == "?")
            {
                return effects.Union(UnknownResolvedOperation(
                    "<using-resource>.Dispose",
                    usingStatement.Span));
            }

            if (TryResolveClass(resourceType, out var cls))
            {
                var match = FindClassMethod(cls, "Dispose");
                if (match is { } resolved && resolved.Method.Parameters.Count == 0)
                {
                    return effects.Union(InferFromInternalFunctions(
                        [$"{resolved.OwnerName}.{resolved.Method.Id}"]));
                }

                return effects.Union(UnknownResolvedOperation(
                    $"{cls.Name}.Dispose",
                    usingStatement.Span));
            }

            var manifestType = MapShortTypeNameToFullName(resourceType);
            // §USING's resource: bound when the resource is a plain reference the
            // binder typed, text when it is a §NEW or an explicit §USING{Type:name}.
            var resolution = _context.Resolver.Resolve(ResolverKey(
                (usingStatement.Resource as ReferenceNode)?.Name,
                manifestType,
                "Dispose"));
            return effects.Union(
                resolution.Status == EffectResolutionStatus.Unknown
                    ? UnknownResolvedOperation($"{manifestType}.Dispose", usingStatement.Span)
                    : resolution.Effects);
        }

        private EffectSet InferFromEventAccessor(
            ExpressionNode eventExpression,
            ExpressionNode handler,
            bool isAdd,
            TextSpan span)
        {
            var effects = EffectSet.From("mut")
                .Union(InferFromExpression(eventExpression))
                .Union(InferFromExpression(handler));
            var eventPath = GetReferencePath(eventExpression);
            if (string.IsNullOrWhiteSpace(eventPath))
            {
                return effects.Union(UnknownResolvedOperation(
                    isAdd ? "<event>.add" : "<event>.remove",
                    span));
            }

            string? receiverType;
            string eventName;
            var lastDot = eventPath.LastIndexOf('.');
            if (lastDot < 0)
            {
                receiverType = _context.OwnerClass?.Name;
                eventName = eventPath;
            }
            else
            {
                var receiver = eventPath[..lastDot];
                eventName = eventPath[(lastDot + 1)..];
                receiverType = receiver is "this" or "self"
                    ? _context.OwnerClass?.Name
                    : ResolveLocalValueType(receiver);
            }

            if (string.IsNullOrWhiteSpace(receiverType) || receiverType == "?")
            {
                return effects.Union(UnknownResolvedOperation(
                    $"{eventPath}.{(isAdd ? "add" : "remove")}",
                    span));
            }

            if (TryResolveClass(receiverType, out var cls))
            {
                var resolved = FindClassEvent(cls, eventName);
                var evt = resolved?.Event;
                if (evt == null)
                {
                    return effects.Union(UnknownResolvedOperation(
                        $"{cls.Name}.{eventName}.{(isAdd ? "add" : "remove")}",
                        span));
                }

                var body = isAdd ? evt.AddBody : evt.RemoveBody;
                if (body == null)
                    return effects;

                var id = CallGraphAnalysis.GetEventAccessorFunctionId(
                    resolved!.Value.OwnerName,
                    evt,
                    isAdd);
                return effects.Union(InferFromInternalFunctions([id]));
            }

            var manifestType = MapShortTypeNameToFullName(receiverType);
            var handlerType = InferExpressionType(handler);
            var resolution = _context.Resolver.Resolve(ResolverKey(
                lastDot < 0 ? null : eventPath[..lastDot],
                manifestType,
                $"{(isAdd ? "add" : "remove")}_{eventName}",
                [handlerType]));
            return effects.Union(
                resolution.Status == EffectResolutionStatus.Unknown
                    ? UnknownResolvedOperation(
                        $"{manifestType}.{(isAdd ? "add" : "remove")}_{eventName}",
                        span)
                    : resolution.Effects);
        }

        private static string? GetReferencePath(ExpressionNode expression)
        {
            return expression switch
            {
                ReferenceNode reference => reference.Name,
                ThisExpressionNode => "this",
                FieldAccessNode field when GetReferencePath(field.Target) is { } target =>
                    $"{target}.{field.FieldName}",
                _ => null
            };
        }

        private string InferExpressionType(ExpressionNode expression)
        {
            return expression switch
            {
                StringLiteralNode => "String",
                IntLiteralNode => "Int32",
                BoolLiteralNode => "Boolean",
                FloatLiteralNode => "Double",
                DecimalLiteralNode => "Decimal",
                ReferenceNode reference => EffectResolver.NormalizeParameterType(
                    ResolveLocalValueType(reference.Name) ?? "?"),
                NewExpressionNode creation => EffectResolver.NormalizeParameterType(
                    GetConstructedTypeName(creation)),
                ThisExpressionNode => EffectResolver.NormalizeParameterType(
                    _context.OwnerClass?.Name ?? "?"),
                LambdaExpressionNode => "Func",
                BinaryOperationNode binary => CommonType(
                    InferExpressionType(binary.Left),
                    InferExpressionType(binary.Right)),
                ConditionalExpressionNode conditional => CommonType(
                    InferExpressionType(conditional.WhenTrue),
                    InferExpressionType(conditional.WhenFalse)),
                CallExpressionNode call => InferCallReturnType(call),
                FieldAccessNode field => InferFieldAccessType(field),
                _ => "?"
            };
        }

        private string InferCallReturnType(CallExpressionNode call)
        {
            var ids = _context.CallGraph.ResolveCallSites(
                _context.CurrentFunctionId,
                call.Target,
                call.Span);
            if (ids.Count != 1 || !_context.Functions.TryGetValue(ids[0], out var function))
                return "?";
            return EffectResolver.NormalizeParameterType(function.Output?.TypeName ?? "void");
        }

        private string InferFieldAccessType(FieldAccessNode field)
        {
            var targetType = InferExpressionType(field.Target);
            var shortType = StripGenericArguments(targetType);
            if (shortType == null || !_context.ClassesByName.TryGetValue(shortType, out var cls))
                return "?";

            var property = FindClassProperty(cls, field.FieldName)?.Property;
            if (property != null)
                return EffectResolver.NormalizeParameterType(property.TypeName);

            var classField = cls.Fields.FirstOrDefault(candidate =>
                candidate.Name.Equals(field.FieldName, StringComparison.Ordinal));
            return classField == null
                ? "?"
                : EffectResolver.NormalizeParameterType(classField.TypeName);
        }

        private EffectSet InferFromFieldAccess(FieldAccessNode field)
        {
            var effects = InferFromExpression(field.Target);
            var targetType = InferExpressionType(field.Target);
            var shortType = StripGenericArguments(targetType);
            if (shortType != null && _context.ClassesByName.TryGetValue(shortType, out var cls))
            {
                var resolved = FindClassProperty(cls, field.FieldName);
                var getter = resolved?.Property.Getter;
                if (getter == null)
                    return effects;

                var id = CallGraphAnalysis.GetPropertyAccessorFunctionId(
                    resolved!.Value.OwnerName,
                    resolved.Value.Property,
                    getter);
                return effects.Union(InferFromInternalFunctions([id]));
            }

            var manifestType = MapShortTypeNameToFullName(targetType);
            var resolution = _context.Resolver.Resolve(ResolverKey(
                GetReferencePath(field.Target),
                manifestType,
                field.FieldName,
                null,
                EffectMemberKind.Getter));
            return resolution.Status == EffectResolutionStatus.Unknown
                ? effects.Union(UnknownResolvedOperation(
                    $"{manifestType}.get_{field.FieldName}",
                    field.Span))
                : effects.Union(resolution.Effects);
        }

        private EffectSet InferSetterEffects(FieldAccessNode field)
        {
            var targetType = InferExpressionType(field.Target);
            var shortType = StripGenericArguments(targetType);
            if (shortType != null && _context.ClassesByName.TryGetValue(shortType, out var cls))
            {
                var resolved = FindClassProperty(cls, field.FieldName);
                if (resolved == null)
                    return EffectSet.Empty;

                var setter = resolved.Value.Property.Setter ?? resolved.Value.Property.Initer;
                if (setter == null)
                {
                    return UnknownResolvedOperation(
                        $"{resolved.Value.OwnerName}.set_{field.FieldName}",
                        field.Span);
                }

                var id = CallGraphAnalysis.GetPropertyAccessorFunctionId(
                    resolved.Value.OwnerName,
                    resolved.Value.Property,
                    setter);
                return InferFromInternalFunctions([id]);
            }

            var manifestType = MapShortTypeNameToFullName(targetType);
            var resolution = _context.Resolver.Resolve(ResolverKey(
                GetReferencePath(field.Target),
                manifestType,
                field.FieldName,
                null,
                EffectMemberKind.Setter));
            return resolution.Status == EffectResolutionStatus.Unknown
                ? UnknownResolvedOperation(
                    $"{manifestType}.set_{field.FieldName}",
                    field.Span)
                : resolution.Effects;
        }

        private EffectSet InferFromReference(ReferenceNode reference)
        {
            if (!TrySplitMemberReference(reference.Name, out var receiver, out var member))
                return EffectSet.Empty;

            var receiverType = ResolveLocalValueType(receiver);
            return receiverType == null
                ? EffectSet.Empty
                : InferGetterEffects(receiverType, member, reference.Span, receiver);
        }

        private EffectSet InferGetterEffects(
            string receiverType,
            string member,
            TextSpan span,
            string? receiverPath = null)
        {
            if (TryResolveClass(receiverType, out var cls))
            {
                var resolved = FindClassProperty(cls, member);
                var getter = resolved?.Property.Getter;
                if (getter == null)
                    return EffectSet.Empty;
                var id = CallGraphAnalysis.GetPropertyAccessorFunctionId(
                    resolved!.Value.OwnerName,
                    resolved.Value.Property,
                    getter);
                return InferFromInternalFunctions([id]);
            }

            var moduleSeparator = receiverType.IndexOf('.');
            if (moduleSeparator > 0
                && _context.CrossModuleFunctionNames.Any(name =>
                    name.StartsWith(
                        receiverType[..moduleSeparator] + ".",
                        StringComparison.Ordinal)))
            {
                RecordAssumption(
                    $"reads cross-module member '{receiverType}.{member}', whose field/property accessor effects are not available in this module");
                return EffectSet.Empty;
            }

            var manifestType = MapShortTypeNameToFullName(receiverType);
            var resolution = _context.Resolver.Resolve(ResolverKey(
                receiverPath, manifestType, member, null, EffectMemberKind.Getter));
            return resolution.Status == EffectResolutionStatus.Unknown
                ? UnknownResolvedOperation($"{manifestType}.get_{member}", span)
                : resolution.Effects;
        }

        private EffectSet InferSetterEffects(
            string receiver,
            string member,
            TextSpan span)
        {
            var receiverType = ResolveLocalValueType(receiver);
            if (receiverType == null)
                return EffectSet.Empty;

            var shortType = StripGenericArguments(receiverType);
            if (shortType != null && _context.ClassesByName.TryGetValue(shortType, out var cls))
            {
                var resolved = FindClassProperty(cls, member);
                if (resolved == null)
                    return EffectSet.Empty;
                var setter = resolved.Value.Property.Setter ?? resolved.Value.Property.Initer;
                if (setter == null)
                    return UnknownResolvedOperation(
                        $"{resolved.Value.OwnerName}.set_{member}",
                        span);
                var id = CallGraphAnalysis.GetPropertyAccessorFunctionId(
                    resolved.Value.OwnerName,
                    resolved.Value.Property,
                    setter);
                return InferFromInternalFunctions([id]);
            }

            var manifestType = MapShortTypeNameToFullName(receiverType);
            var resolution = _context.Resolver.Resolve(ResolverKey(
                receiver, manifestType, member, null, EffectMemberKind.Setter));
            return resolution.Status == EffectResolutionStatus.Unknown
                ? UnknownResolvedOperation($"{manifestType}.set_{member}", span)
                : resolution.Effects;
        }

        private static bool TrySplitMemberReference(
            string name,
            out string receiver,
            out string member)
        {
            var dot = name.LastIndexOf('.');
            if (dot <= 0 || dot == name.Length - 1)
            {
                receiver = "";
                member = "";
                return false;
            }
            receiver = name[..dot];
            member = name[(dot + 1)..];
            return !receiver.Contains('.');
        }

        private static string CommonType(string left, string right)
            => left == right ? left : "?";

        private static string GetConstructedTypeName(NewExpressionNode creation)
            => creation.TypeArguments.Count == 0
                ? creation.TypeName
                : $"{creation.TypeName}<{string.Join(",", creation.TypeArguments)}>";

        private EffectSet InferFromIf(IfStatementNode ifStmt)
        {
            var effects = InferFromExpression(ifStmt.Condition);
            effects = effects.Union(InferFromStatements(ifStmt.ThenBody));

            foreach (var elseIf in ifStmt.ElseIfClauses)
            {
                effects = effects.Union(InferFromExpression(elseIf.Condition));
                effects = effects.Union(InferFromStatements(elseIf.Body));
            }

            if (ifStmt.ElseBody != null)
            {
                effects = effects.Union(InferFromStatements(ifStmt.ElseBody));
            }

            return effects;
        }

        private EffectSet InferFromFor(ForStatementNode forStmt)
        {
            var effects = InferFromExpression(forStmt.From);
            effects = effects.Union(InferFromExpression(forStmt.To));
            if (forStmt.Step != null)
            {
                effects = effects.Union(InferFromExpression(forStmt.Step));
            }
            effects = effects.Union(InferFromStatements(forStmt.Body));
            return effects;
        }

        private EffectSet InferFromMatch(MatchStatementNode matchStmt)
        {
            var effects = InferFromExpression(matchStmt.Target);
            foreach (var matchCase in matchStmt.Cases)
            {
                effects = effects.Union(InferFromStatements(matchCase.Body));
            }
            return effects;
        }

        private EffectSet InferFromTry(TryStatementNode tryStmt)
        {
            var effects = InferFromStatements(tryStmt.TryBody);

            foreach (var catchClause in tryStmt.CatchClauses)
            {
                effects = effects.Union(InferFromStatements(catchClause.Body));
            }

            if (tryStmt.FinallyBody != null)
            {
                effects = effects.Union(InferFromStatements(tryStmt.FinallyBody));
            }

            return effects;
        }

        private EffectSet InferFromAssignment(AssignmentStatementNode assign)
        {
            var effects = InferFromExpression(assign.Value);

            // Check if this is a mutation (writing to non-local object)
            if (assign.Target is FieldAccessNode)
            {
                effects = effects.Union(EffectSet.From("mut"));
                effects = effects.Union(InferSetterEffects((FieldAccessNode)assign.Target));
            }
            else if (assign.Target is ReferenceNode reference
                     && TrySplitMemberReference(reference.Name, out var receiver, out var member))
            {
                effects = effects.Union(EffectSet.From("mut"));
                effects = effects.Union(InferSetterEffects(receiver, member, reference.Span));
            }

            return effects;
        }

        private EffectSet InferFromExpression(ExpressionNode expr)
        {
            // D-W2.6: exhaustive over the expression kinds the pass understands;
            // the final arm routes unrecognized constructs to the Assumed channel
            // (Calor0419) — never silently pure.
            return expr switch
            {
                CallExpressionNode call => InferFromCallExpression(call),
                ExpressionCallNode exprCall => InferFromExpressionCall(exprCall),
                MatchExpressionNode match => InferFromMatchExpression(match),
                BinaryOperationNode binOp => InferFromExpression(binOp.Left).Union(InferFromExpression(binOp.Right)),
                UnaryOperationNode unOp => InferFromExpression(unOp.Operand),
                ConditionalExpressionNode cond => InferFromExpression(cond.Condition)
                    .Union(InferFromExpression(cond.WhenTrue))
                    .Union(InferFromExpression(cond.WhenFalse)),
                SomeExpressionNode some => InferFromExpression(some.Value),
                OkExpressionNode ok => InferFromExpression(ok.Value),
                ErrExpressionNode err => InferFromExpression(err.Error),
                NewExpressionNode newExpr => InferFromNewExpression(newExpr),
                FieldAccessNode field => InferFromFieldAccess(field),
                ArrayAccessNode array => InferFromExpression(array.Array).Union(InferFromExpression(array.Index)),
                LambdaExpressionNode lambda => InferFromLambda(lambda),
                AwaitExpressionNode await_ => InferFromExpression(await_.Awaited),
                ThrowExpressionNode throwExpr => EffectSet.From("throw").Union(InferFromExpression(throwExpr.Exception)),
                InterpolatedStringNode interp => InferFromInterpolatedString(interp),
                NullCoalesceNode coalesce => InferFromExpression(coalesce.Left).Union(InferFromExpression(coalesce.Right)),
                NullConditionalNode nullCond => InferFromExpression(nullCond.Target),
                RangeExpressionNode range =>
                    (range.Start != null ? InferFromExpression(range.Start) : EffectSet.Empty)
                    .Union(range.End != null ? InferFromExpression(range.End) : EffectSet.Empty),
                IndexFromEndNode indexFromEnd => InferFromExpression(indexFromEnd.Offset),
                TypeOperationNode typeOp => InferFromExpression(typeOp.Operand),
                IsPatternNode isPattern => InferFromExpression(isPattern.Operand),
                WithExpressionNode with => with.Assignments.Aggregate(
                    InferFromExpression(with.Target),
                    (acc, a) => acc.Union(InferFromExpression(a.Value))),
                ArrayCreationNode arrayCreation => EffectSet.From("alloc")
                    .Union(arrayCreation.Size != null
                        ? InferFromExpression(arrayCreation.Size)
                        : EffectSet.Empty)
                    .Union(InferFromMany(arrayCreation.Initializer)),
                MultiDimArrayCreationNode multiDim => EffectSet.From("alloc")
                    .Union(InferFromMany(multiDim.DimensionSizes)),
                MultiDimArrayAccessNode multiDimAccess =>
                    InferFromExpression(multiDimAccess.Array).Union(InferFromMany(multiDimAccess.Indices)),
                ArrayLengthNode arrayLength => InferFromExpression(arrayLength.Array),
                ListCreationNode listCreation => EffectSet.From("alloc")
                    .Union(InferFromMany(listCreation.Elements)),
                SetCreationNode setCreation => EffectSet.From("alloc")
                    .Union(InferFromMany(setCreation.Elements)),
                DictionaryCreationNode dictCreation => dictCreation.Entries.Aggregate(
                    EffectSet.From("alloc"),
                    (acc, e) => acc.Union(InferFromExpression(e.Key)).Union(InferFromExpression(e.Value))),
                CollectionContainsNode contains => InferFromExpression(contains.KeyOrValue),
                CollectionCountNode count => InferFromExpression(count.Collection),
                TupleLiteralNode tuple => InferFromMany(tuple.Elements),
                RecordCreationNode record => record.Fields.Aggregate(
                    EffectSet.Empty, (acc, f) => acc.Union(InferFromExpression(f.Value))),
                AnonymousObjectCreationNode anonymous => anonymous.Initializers.Aggregate(
                    EffectSet.Empty, (acc, i) => acc.Union(InferFromExpression(i.Value))),
                StringOperationNode stringOp => InferFromMany(stringOp.Arguments),
                CharOperationNode charOp => InferFromMany(charOp.Arguments),
                // Native §SB modification ops mutate the builder (heap write);
                // creation/query ops are pure.
                StringBuilderOperationNode sbOp => (sbOp.Operation switch
                {
                    StringBuilderOp.Append or StringBuilderOp.AppendLine or StringBuilderOp.Insert
                        or StringBuilderOp.Remove or StringBuilderOp.Clear => EffectSet.From("mut"),
                    _ => EffectSet.Empty
                }).Union(InferFromMany(sbOp.Arguments)),
                StackAllocNode stackAlloc => EffectSet.From("alloc")
                    .Union(stackAlloc.Size != null
                        ? InferFromExpression(stackAlloc.Size)
                        : EffectSet.Empty)
                    .Union(InferFromMany(stackAlloc.Initializer)),
                AddressOfNode addressOf => InferFromExpression(addressOf.Operand),
                PointerDereferenceNode deref => InferFromExpression(deref.Operand),
                // No-effect leaves
                ReferenceNode reference => InferFromReference(reference),
                IntLiteralNode or StringLiteralNode or BoolLiteralNode or FloatLiteralNode
                    or DecimalLiteralNode or NoneExpressionNode
                    or ThisExpressionNode or BaseExpressionNode or SelfRefNode
                    or GenericTypeNode or TypeOfExpressionNode or NameOfExpressionNode
                    or SizeOfNode => EffectSet.Empty,
                // Contract-form wrappers are pure themselves, but their retained
                // predicates still need traversal so nested calls cannot disappear.
                ForallExpressionNode or ExistsExpressionNode or ImplicationExpressionNode
                    => InferFromStructuralChildren(expr),
                // D-W2.3: interop / unconverted content — assumed, not silently pure
                RawCSharpExpressionNode => InferFromStructuralChildren(expr)
                    .Union(RecordAssumption("contains a raw C# interop expression (§CS)")),
                FallbackExpressionNode fallback => InferFromStructuralChildren(expr)
                    .Union(RecordAssumption(
                        $"contains an unconverted C# fallback expression ('{fallback.FeatureName}')")),
                // D-W2.6: fail-loud catch-all
                _ => InferFromStructuralChildren(expr)
                    .Union(RecordAssumption(
                        $"contains an unrecognized expression construct '{expr.GetType().Name}' whose effects cannot be inferred"))
            };
        }

        private EffectSet InferFromStructuralChildren(AstNode node)
        {
            var effects = EffectSet.Empty;
            foreach (var child in Calor.Compiler.Analysis.RecursiveAstWalker.GetAllChildren(node))
            {
                effects = child switch
                {
                    ExpressionNode expression => effects.Union(InferFromExpression(expression)),
                    StatementNode statement => effects.Union(InferFromStatement(statement)),
                    _ => effects.Union(InferFromStructuralChildren(child)),
                };
            }
            return effects;
        }

        private EffectSet InferFromMany(IEnumerable<ExpressionNode> expressions)
        {
            var effects = EffectSet.Empty;
            foreach (var expression in expressions)
            {
                effects = effects.Union(InferFromExpression(expression));
            }
            return effects;
        }

        private EffectSet InferFromInterpolatedString(InterpolatedStringNode interpolated)
        {
            var effects = EffectSet.Empty;
            foreach (var part in interpolated.Parts)
            {
                if (part is InterpolatedStringExpressionNode exprPart)
                {
                    effects = effects.Union(InferFromExpression(exprPart.Expression));
                }
            }
            return effects;
        }

        private EffectSet InferFromExpressionCall(ExpressionCallNode call)
        {
            var argEffects = EffectSet.Empty;
            foreach (var arg in call.Arguments)
            {
                argEffects = argEffects.Union(InferFromExpression(arg));
            }

            // W2 review M1: the expression-call spelling must not demote the
            // delegate rule. Classify the callee expression:
            switch (call.TargetExpression)
            {
                case ReferenceNode reference:
                    // `§C f §A x §/C` is the same call as `§C{f} §A x §/C` —
                    // route through the full named-target resolution (value →
                    // its row is charged, E4; internal function → its effects;
                    // free name → unknown chain).
                    return InferFromCallTarget(reference.Name, call.Span, call.Arguments, reference.Span)
                        .Union(argEffects);

                case LambdaExpressionNode lambda:
                    // Immediately-invoked lambda literal: the body IS the callee,
                    // so its effects are fully charged — no delegate opacity.
                    return InferFromLambda(lambda).Union(argEffects);

                case CallExpressionNode inner:
                {
                    // v0.15 E4 — invoking the RESULT of a call (`GetF()()`): the
                    // invoked thing is a function value whose row is the
                    // callee's declared RETURN row (§3.3 position 6), Unknown
                    // when the return carries none or the callee is not visible.
                    // The producing call is charged as before; the invocation is
                    // charged through the row, the same rule as a named value.
                    _context.Functions.TryGetValue(_context.CurrentFunctionId, out var owner);
                    var returned = ReturnedValueRow(
                        inner.Target, inner.Span, boundName: null,
                        owner?.Name ?? _context.CurrentFunctionId);
                    return InferFromExpression(call.TargetExpression)
                        .Union(ChargeInvokedRow(
                            $"the value returned by '{inner.Target}'", returned, call.Span))
                        .Union(argEffects);
                }

                case ExpressionCallNode:
                    // `§C §C f §/C §/C` — the result of an expression-call whose
                    // own target is not a named function; nothing declares the
                    // returned value's row, so it is Unknown (P17: never
                    // Concrete by default).
                    return InferFromExpression(call.TargetExpression)
                        .Union(ChargeInvokedRow(
                            "the value returned by an expression call",
                            (PolyRow.Unknown,
                                "the callee is an expression, not a declaration this pass can read a "
                                + "return row from"),
                            call.Span))
                        .Union(argEffects);

                default:
                    // Other expression-valued targets (e.g. method call on a new
                    // object) cannot be resolved to a callee — surface the
                    // assumption instead of silently charging nothing.
                    return InferFromExpression(call.TargetExpression)
                        .Union(argEffects)
                        .Union(RecordAssumption(
                            "calls through an expression-valued target whose callee cannot be resolved"));
            }
        }

        private EffectSet InferFromCallExpression(CallExpressionNode call)
        {
            var effects = InferFromCallTarget(call.Target, call.Span, call.Arguments);
            return effects.Union(InferFromCallArguments(call.Target, call.Arguments));
        }

        private EffectSet InferFromMatchExpression(MatchExpressionNode match)
        {
            var effects = InferFromExpression(match.Target);
            foreach (var matchCase in match.Cases)
            {
                effects = effects.Union(InferFromStatements(matchCase.Body));
            }
            return effects;
        }

        private EffectSet InferFromNewExpression(NewExpressionNode newExpr)
        {
            var effects = EffectSet.From("alloc").Union(InferFromMany(newExpr.Arguments));
            var constructorTarget = $"{newExpr.TypeName}..ctor";
            var resolvedInternalIds = _context.CallGraph.ResolveCallSites(
                _context.CurrentFunctionId,
                constructorTarget,
                newExpr.Span);

            if (resolvedInternalIds.Count > 0)
            {
                effects = effects.Union(InferFromInternalFunctions(resolvedInternalIds));
            }
            else
            {
                effects = effects.Union(ResolveConstructorEffects(
                    newExpr.TypeName,
                    newExpr.Arguments,
                    newExpr.TypeNameSpan));
            }

            foreach (var initializer in newExpr.Initializers)
            {
                effects = effects.Union(InferFromExpression(initializer.Value));
                effects = effects.Union(EffectSet.From("mut"));
                effects = effects.Union(ResolveInitializerSetterEffects(
                    newExpr.TypeName,
                    initializer));
            }

            return effects;
        }

        public EffectSet InferFromConstructorInitializer(ConstructorDeclaration declaration)
        {
            var initializer = declaration.Constructor.Initializer!;
            var effects = InferFromMany(initializer.Arguments);
            var targetType = initializer.IsBaseCall
                ? declaration.Owner.BaseClass
                : declaration.OwnerName;

            if (string.IsNullOrWhiteSpace(targetType))
            {
                return initializer.IsBaseCall
                    ? effects
                    : effects.Union(UnknownResolvedOperation(
                        $"{declaration.OwnerName}..ctor",
                        initializer.Span));
            }

            return effects.Union(ResolveConstructorEffects(
                targetType,
                initializer.Arguments,
                initializer.Span,
                declaration.Constructor));
        }

        public EffectSet InferFromImplicitBaseConstructor(ConstructorDeclaration declaration)
            => ResolveConstructorEffects(
                declaration.Owner.BaseClass!,
                [],
                declaration.Constructor.Span);

        private EffectSet ResolveConstructorEffects(
            string typeName,
            IReadOnlyList<ExpressionNode> arguments,
            TextSpan span,
            ConstructorNode? currentConstructor = null)
        {
            var argumentTypes = arguments.Select(InferExpressionType).ToArray();
            if (arguments.Count == 0 && HasNewConstraint(typeName))
                return EffectSet.Empty;

            var shortType = StripGenericArguments(typeName);
            if (shortType != null && _context.ClassesByName.TryGetValue(shortType, out var cls))
            {
                var constructors = CallGraphAnalysis.EnumerateConstructors(cls)
                    .Where(ctor => !ctor.IsStatic && !ReferenceEquals(ctor, currentConstructor))
                    .Where(ctor => ConstructorParametersMatch(ctor, argumentTypes))
                    .Take(2)
                    .ToArray();

                if (constructors.Length == 1)
                {
                    var id = $"{cls.Name}.{constructors[0].Id}";
                    return InferFromInternalFunctions([id]);
                }

                if (constructors.Length == 0
                    && !CallGraphAnalysis.EnumerateConstructors(cls).Any(ctor => !ctor.IsStatic)
                    && arguments.Count == 0)
                {
                    var baseType = cls.BaseClass;
                    return string.IsNullOrWhiteSpace(baseType)
                        ? EffectSet.Empty
                        : ResolveConstructorEffects(baseType, [], span);
                }

                return UnknownResolvedOperation(
                    $"{cls.Name}..ctor({string.Join(",", argumentTypes)})",
                    span);
            }

            var manifestType = MapShortTypeNameToFullName(typeName);
            // A constructor has no receiver — the type is named outright, so this
            // key is text by construction, not by a missing binder answer.
            var resolution = _context.Resolver.Resolve(EffectResolverKey.FromStrings(
                manifestType, ".ctor", argumentTypes, EffectMemberKind.Constructor));
            return resolution.Status == EffectResolutionStatus.Unknown
                ? UnknownResolvedOperation(
                    $"{manifestType}..ctor({string.Join(",", argumentTypes)})",
                    span)
                : resolution.Effects;
        }

        private bool HasNewConstraint(string typeName)
        {
            static bool Matches(TypeParameterNode parameter, string name)
                => parameter.Name.Equals(name, StringComparison.Ordinal)
                    && parameter.Constraints.Any(constraint =>
                        constraint.Kind == TypeConstraintKind.New);

            if (_context.Functions.TryGetValue(_context.CurrentFunctionId, out var function)
                && function.TypeParameters.Any(parameter => Matches(parameter, typeName)))
            {
                return true;
            }

            return _context.OwnerClass?.TypeParameters.Any(
                parameter => Matches(parameter, typeName)) == true;
        }

        private static bool ConstructorParametersMatch(
            ConstructorNode constructor,
            IReadOnlyList<string> argumentTypes)
        {
            if (constructor.Parameters.Count != argumentTypes.Count)
                return false;

            for (var i = 0; i < argumentTypes.Count; i++)
            {
                if (argumentTypes[i] == "?")
                    continue;
                var parameterType = EffectResolver.NormalizeParameterType(
                    constructor.Parameters[i].TypeName);
                if (!parameterType.Equals(argumentTypes[i], StringComparison.Ordinal))
                    return false;
            }

            return true;
        }

        private EffectSet ResolveInitializerSetterEffects(
            string typeName,
            ObjectInitializerAssignment initializer)
        {
            var shortType = StripGenericArguments(typeName);
            if (shortType != null && _context.ClassesByName.TryGetValue(shortType, out var cls))
            {
                var resolved = FindClassProperty(cls, initializer.PropertyName);
                var property = resolved?.Property;
                var accessor = property?.Initer ?? property?.Setter;
                if (property == null || accessor == null)
                {
                    return UnknownResolvedOperation(
                        $"{cls.Name}.set_{initializer.PropertyName}",
                        initializer.Value.Span);
                }

                var id = CallGraphAnalysis.GetPropertyAccessorFunctionId(
                    resolved!.Value.OwnerName,
                    property,
                    accessor);
                return InferFromInternalFunctions([id]);
            }

            var manifestType = MapShortTypeNameToFullName(typeName);
            // An object-initializer member sits on a type just named by §NEW;
            // there is no receiver path to ask the binder about.
            var resolution = initializer.PropertyName.StartsWith("_item", StringComparison.Ordinal)
                ? _context.Resolver.Resolve(EffectResolverKey.FromStrings(
                    manifestType,
                    "Add",
                    [InferExpressionType(initializer.Value)]))
                : _context.Resolver.Resolve(EffectResolverKey.FromStrings(
                    manifestType, initializer.PropertyName, kind: EffectMemberKind.Setter));
            return resolution.Status == EffectResolutionStatus.Unknown
                ? UnknownResolvedOperation(
                    initializer.PropertyName.StartsWith("_item", StringComparison.Ordinal)
                        ? $"{manifestType}.Add"
                        : $"{manifestType}.set_{initializer.PropertyName}",
                    initializer.Value.Span)
                : resolution.Effects;
        }

        private EffectSet InferFromInternalFunctions(IEnumerable<string> functionIds)
        {
            var effects = EffectSet.Empty;
            foreach (var functionId in functionIds)
            {
                if (_context.ComputedEffects.TryGetValue(functionId, out var computed))
                    effects = effects.Union(computed);
                else if (_context.SccMembers.Contains(functionId))
                    effects = effects.Union(
                        _context.ComputedEffects.GetValueOrDefault(functionId, EffectSet.Empty));
                else if (_context.Functions.ContainsKey(functionId))
                    effects = effects.Union(_context.ResolveInternalEffects(functionId));
                else
                    effects = effects.Union(EffectSet.Unknown);
            }
            return effects;
        }

        private EffectSet UnknownResolvedOperation(string target, TextSpan span)
        {
            if (_context.Policy == UnknownCallPolicy.Permissive)
                return EffectSet.Empty;

            ReportUnknownCall(target, span);
            return EffectSet.Unknown;
        }

        private EffectSet InferFromLambda(LambdaExpressionNode lambda)
        {
            // The body's effects still contribute to the ENCLOSING callable,
            // exactly as before: whether creating a lambda should charge its
            // creator is the invocation-charging question, and that is E4's.
            // What slice b adds is that ρ_body is RECORDED (§5), so §5's two
            // consumers exist — the declared-row check below, and an
            // un-annotated lambda's type row at the six binding sites.
            var before = _context.Assumptions.Count;

            var body = lambda.ExpressionBody != null
                ? InferFromExpression(lambda.ExpressionBody)
                : lambda.StatementBody != null
                    ? InferFromStatements(lambda.StatementBody)
                    : EffectSet.Empty;

            _context.RecordLambdaBody?.Invoke(
                lambda,
                _context.CurrentFunctionId,
                body,
                _context.Assumptions.Skip(before).ToList());

            return body;
        }
    }
}

/// <summary>
/// Policy for handling unknown external calls.
/// </summary>
public enum UnknownCallPolicy
{
    /// <summary>
    /// Unknown calls are errors (v1 default).
    /// </summary>
    Strict,

    /// <summary>
    /// Unknown calls produce warnings, assume worst-case effects.
    /// </summary>
    Warn,

    /// <summary>
    /// Unknown calls are errors unless stubbed.
    /// </summary>
    StubRequired,

    /// <summary>
    /// Unknown calls are silently assumed pure.
    /// Forbidden-effect checks (Calor0410) are demoted to warnings.
    /// Designed for converted code that lacks effect annotations.
    /// </summary>
    Permissive
}

/// <summary>
/// Extension methods for EffectSet display.
/// </summary>
internal static class EffectSetExtensions
{
    public static string ToSurfaceCode(EffectKind kind, string value)
        => EffectCodes.ToCompact(kind, value);
}
