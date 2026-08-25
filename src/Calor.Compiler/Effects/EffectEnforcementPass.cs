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
        const int maxIterations = 100;

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

        if (changed)
        {
            _diagnostics.ReportWarning(
                _callGraphAnalysis.Functions[scc[0]].Span,
                "Calor0600",
                $"Effect fixpoint iteration did not converge after {maxIterations} iterations for mutually recursive functions. Effects may be incomplete.");
        }
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
            assumptions);
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

                var message = $"Function '{function.Name}' uses effect '{EffectSetExtensions.ToSurfaceCode(kind, value)}' but does not declare it{chainStr}";

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
            // EMITTER SPIKE: the reserved rank-1 effect-variable key is not an
            // EffectKind category. It is carried by the ROW, not by the concrete
            // effect set, and is resolved at the instantiation site (§7.4).
            if (kv.Key == EffectsNode.EffectVariableCategory) continue;

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
    /// EMITTER SPIKE (effect-rows design doc §3.5, §8.2). The row a DECLARATION
    /// carries: its concrete effect set plus the binder indices of any rank-1
    /// effect variables it mentions. An omitted <c>§E</c> on a declaration is
    /// PURE, unchanged from today — the asymmetry with an omitted row at a
    /// parameter or field (which is Unknown) is deliberate: a declaration makes
    /// a promise, an omitted annotation states nothing.
    /// </summary>
    internal static EffectRow GetDeclaredRow(EffectsNode? effectsNode)
        => effectsNode == null
            ? EffectRow.Pure
            : EffectRow.Concrete(GetDeclaredEffects(effectsNode), effectsNode.EffectVariableIndices);

    /// <summary>
    /// EMITTER SPIKE. The row an ANNOTATION carries at a parameter or field
    /// (positions 4/5/8). Returns null when the source omits the row, which the
    /// caller reads as Unknown (§3.5) — never as pure.
    /// </summary>
    internal static EffectRow? GetAnnotationRow(EffectsNode? row)
        => row == null
            ? null
            : EffectRow.Concrete(GetDeclaredEffects(row), row.EffectVariableIndices);

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
        var varianceSeverity = _policy == UnknownCallPolicy.Permissive
            ? DiagnosticSeverity.Warning
            : DiagnosticSeverity.Error;

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
                    // EMITTER SPIKE (§6.3): the subset test becomes a call to the
                    // SHARED EffectRow.Fits relation. No rank-1-specific branch
                    // lives here — alpha-equivalence of effect variables is a
                    // property of Fits, because a row carries binder INDICES.
                    if (EffectRow.Fits(
                            GetDeclaredRow(method.Effects),
                            GetDeclaredRow(baseMethod.Effects)) == RowFit.DoesNotFit)
                    {
                        var extra = overrideDeclared.Except(baseDeclared)
                            .Select(e => EffectSetExtensions.ToSurfaceCode(e.Kind, e.Value));
                        _diagnostics.Report(
                            method.Effects?.Span ?? method.Span,
                            DiagnosticCode.OverrideEffectVariance,
                            $"Override '{cls.Name}.{method.Name}' declares effect(s) [{string.Join(", ", extra)}] " +
                            $"not declared by base method '{baseClassName}.{method.Name}' " +
                            $"(base declares: {baseDeclared.ToDisplayString()}). " +
                            "An override may not broaden its base method's effect set — broader effects " +
                            "would launder through dynamic dispatch.",
                            varianceSeverity);
                    }
                }
                else if (baseClassName != null)
                {
                    // External C# base class — variance cannot be checked declaration-locally.
                    AddAssumption($"{cls.Name}.{method.Id}",
                        $"overrides a method of external base class '{baseClassName}', so effect variance cannot be checked");
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
                        // EMITTER SPIKE (§6.3, R2): same shared relation as the
                        // override site, so a change to Fits moves both together.
                        if (EffectRow.Fits(
                                GetDeclaredRow(impl.Effects),
                                GetDeclaredRow(sig.Effects)) == RowFit.DoesNotFit)
                        {
                            var extra = implDeclared.Except(ifaceDeclared)
                                .Select(e => EffectSetExtensions.ToSurfaceCode(e.Kind, e.Value));
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
                                "An implementation may not broaden the interface's declared effect set — interface " +
                                "dispatch launders effects identically to overrides.",
                                varianceSeverity);
                        }
                    }
                    else if (externalBaseName != null)
                    {
                        // The §IMPL is satisfied (if at all) by a member inherited
                        // from an EXTERNAL base: variance cannot be checked, so the
                        // interface's declared effect set is only an assumption —
                        // surfaced like external-base overrides (Calor0419).
                        var assumedSeverity = _strictEffects
                            ? DiagnosticSeverity.Error
                            : DiagnosticSeverity.Warning;
                        _diagnostics.Report(
                            cls.Span,
                            DiagnosticCode.AssumedEffects,
                            $"Class '{cls.Name}' implements '{iface.Name}.{sig.Name}' through a member that is " +
                            $"not visible in this module (inherited from external base '{externalBaseName}'). " +
                            "The interface's declared effect set is ASSUMED for this implementation, not verified.",
                            assumedSeverity);
                    }
                }
            }
        }
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
    /// Used by both ParseCallTarget (in EffectInferrer) and ParseCallTargetForChain.
    /// </summary>
    internal static string MapShortTypeNameToFullName(string shortName)
    {
        // Calor surface syntax for the runtime's Option/Result types:
        // ?T is Option<T>; T!E is Result<T,E>. Their combinators are
        // manifest-entered as pure-modulo-arguments (delegate arguments are
        // charged at the lambda definition site by the effect pass).
        if (shortName.StartsWith('?') || shortName.StartsWith("Option<"))
            return "Calor.Runtime.Option`1";
        if (shortName.StartsWith("Result<"))
            return "Calor.Runtime.Result`2";
        if (shortName.Contains('!') && !shortName.Contains('.') && !shortName.Contains('('))
            return "Calor.Runtime.Result`2";

        // Normalize declared generic collection types ("List<i32>") to their
        // manifest identities so typed receivers resolve to the correct entries
        // (e.g. List`1.Add = mut) instead of hitting the unknown-call chain.
        var genericIdx = shortName.IndexOf('<');
        if (genericIdx > 0 && shortName.EndsWith('>'))
        {
            var baseName = shortName[..genericIdx];
            var mapped = baseName switch
            {
                "List" or "System.Collections.Generic.List" =>
                    "System.Collections.Generic.List`1",
                "Dictionary" or "Dict" or "System.Collections.Generic.Dictionary" =>
                    "System.Collections.Generic.Dictionary`2",
                "HashSet" or "Set" or "System.Collections.Generic.HashSet" =>
                    "System.Collections.Generic.HashSet`1",
                "Task" or "System.Threading.Tasks.Task" =>
                    "System.Threading.Tasks.Task`1",
                _ => null
            };
            if (mapped != null)
                return mapped;
        }

        return MapKnownShortTypeName(shortName);
    }

    private static string MapKnownShortTypeName(string shortName) => shortName switch
    {
        // Calor runtime static helper classes
        "Option" => "Calor.Runtime.Option",
        "Result" => "Calor.Runtime.Result",
        // BCL types
        "Console" => "System.Console",
        // Generic collections referenced by bare name (e.g. §NEW{List<i32>}
        // stores TypeName "List" with separate type arguments)
        "List" => "System.Collections.Generic.List`1",
        "System.Collections.Generic.List" => "System.Collections.Generic.List`1",
        "Dictionary" => "System.Collections.Generic.Dictionary`2",
        "Dict" => "System.Collections.Generic.Dictionary`2",
        "System.Collections.Generic.Dictionary" => "System.Collections.Generic.Dictionary`2",
        "HashSet" => "System.Collections.Generic.HashSet`1",
        "Set" => "System.Collections.Generic.HashSet`1",
        "System.Collections.Generic.HashSet" => "System.Collections.Generic.HashSet`1",
        "File" => "System.IO.File",
        "Directory" => "System.IO.Directory",
        "Path" => "System.IO.Path",
        "StreamReader" => "System.IO.StreamReader",
        "StreamWriter" => "System.IO.StreamWriter",
        "FileStream" => "System.IO.FileStream",
        "BinaryReader" => "System.IO.BinaryReader",
        "BinaryWriter" => "System.IO.BinaryWriter",
        "Random" => "System.Random",
        "DateTime" => "System.DateTime",
        "Environment" => "System.Environment",
        "Process" => "System.Diagnostics.Process",
        "HttpClient" => "System.Net.Http.HttpClient",
        "Math" => "System.Math",
        "Guid" => "System.Guid",
        "Enumerable" => "System.Linq.Enumerable",
        "str" => "System.String",
        "string" => "System.String",
        "STRING" => "System.String",
        "String" => "System.String",
        "i32" => "System.Int32",
        "int" => "System.Int32",
        "INT" => "System.Int32",
        "Int32" => "System.Int32",
        "i64" => "System.Int64",
        "long" => "System.Int64",
        "Int64" => "System.Int64",
        "f64" => "System.Double",
        "double" => "System.Double",
        "FLOAT" => "System.Double",
        "Double" => "System.Double",
        "bool" => "System.Boolean",
        "BOOL" => "System.Boolean",
        "Boolean" => "System.Boolean",
        "Convert" => "System.Convert",
        "Array" => "System.Array",
        "StringBuilder" => "System.Text.StringBuilder",
        "Stopwatch" => "System.Diagnostics.Stopwatch",
        "Debug" => "System.Diagnostics.Debug",
        "Trace" => "System.Diagnostics.Trace",
        "Thread" => "System.Threading.Thread",
        "Task" => "System.Threading.Tasks.Task",
        "JsonSerializer" => "System.Text.Json.JsonSerializer",
        "JsonDocument" => "System.Text.Json.JsonDocument",
        "Regex" => "System.Text.RegularExpressions.Regex",
        "Exception" => "System.Exception",
        "ArgumentException" => "System.ArgumentException",
        "ArgumentNullException" => "System.ArgumentNullException",
        "ArgumentOutOfRangeException" => "System.ArgumentOutOfRangeException",
        "InvalidOperationException" => "System.InvalidOperationException",
        "NotSupportedException" => "System.NotSupportedException",
        "NotImplementedException" => "System.NotImplementedException",
        "IndexOutOfRangeException" => "System.IndexOutOfRangeException",
        "FormatException" => "System.FormatException",
        "ObjectDisposedException" => "System.ObjectDisposedException",
        // Microsoft.Extensions.Logging
        "ILogger" => "Microsoft.Extensions.Logging.ILogger",
        "LoggerExtensions" => "Microsoft.Extensions.Logging.LoggerExtensions",
        "ILoggerFactory" => "Microsoft.Extensions.Logging.ILoggerFactory",
        // Microsoft.Extensions.Configuration
        "IConfiguration" => "Microsoft.Extensions.Configuration.IConfiguration",
        "IConfigurationRoot" => "Microsoft.Extensions.Configuration.IConfigurationRoot",
        "IConfigurationSection" => "Microsoft.Extensions.Configuration.IConfigurationSection",
        "ConfigurationExtensions" => "Microsoft.Extensions.Configuration.ConfigurationExtensions",
        // Microsoft.Extensions.DependencyInjection
        "IServiceProvider" => "Microsoft.Extensions.DependencyInjection.IServiceProvider",
        "IServiceCollection" => "Microsoft.Extensions.DependencyInjection.IServiceCollection",
        "IServiceScopeFactory" => "Microsoft.Extensions.DependencyInjection.IServiceScopeFactory",
        // Microsoft.Extensions.Options
        "IOptions" => "Microsoft.Extensions.Options.IOptions`1",
        "IOptionsSnapshot" => "Microsoft.Extensions.Options.IOptionsSnapshot`1",
        "IOptionsMonitor" => "Microsoft.Extensions.Options.IOptionsMonitor`1",
        // Microsoft.Extensions.Hosting
        "IHost" => "Microsoft.Extensions.Hosting.IHost",
        "IHostBuilder" => "Microsoft.Extensions.Hosting.IHostBuilder",
        "IHostedService" => "Microsoft.Extensions.Hosting.IHostedService",
        // Microsoft.EntityFrameworkCore
        "DbContext" => "Microsoft.EntityFrameworkCore.DbContext",
        "DbSet" => "Microsoft.EntityFrameworkCore.DbSet`1",
        "DatabaseFacade" => "Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade",
        // Microsoft.AspNetCore
        "HttpContext" => "Microsoft.AspNetCore.Http.HttpContext",
        "HttpRequest" => "Microsoft.AspNetCore.Http.HttpRequest",
        "HttpResponse" => "Microsoft.AspNetCore.Http.HttpResponse",
        "ControllerBase" => "Microsoft.AspNetCore.Mvc.ControllerBase",
        "Results" => "Microsoft.AspNetCore.Http.Results",
        "TypedResults" => "Microsoft.AspNetCore.Http.TypedResults",
        // Serilog
        "Log" => "Serilog.Log",
        "SerilogLog" => "Serilog.Log",
        // Newtonsoft.Json
        "JsonConvert" => "Newtonsoft.Json.JsonConvert",
        "JObject" => "Newtonsoft.Json.Linq.JObject",
        "JArray" => "Newtonsoft.Json.Linq.JArray",
        "JToken" => "Newtonsoft.Json.Linq.JToken",
        // Dapper
        "SqlMapper" => "Dapper.SqlMapper",
        // MediatR
        "IMediator" => "MediatR.IMediator",
        "ISender" => "MediatR.ISender",
        "Mediator" => "MediatR.Mediator",
        // AutoMapper
        "IMapper" => "AutoMapper.IMapper",
        "Mapper" => "AutoMapper.Mapper",
        // FluentValidation
        "IValidator" => "FluentValidation.IValidator",
        // Polly
        "Policy" => "Polly.Policy",
        "ResiliencePipeline" => "Polly.ResiliencePipeline",
        _ => shortName
    };

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
                            var resolution = _resolver.Resolve(typeName, methodName);
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

            // EMITTER SPIKE — §6.2 site 2 (argument) and site 6 (rank-1 generic
            // instantiation). Both engage only when the CALLEE declares a row on
            // the parameter in question, so a row-less call is untouched.
            var argumentList = arguments as IReadOnlyList<ExpressionNode> ?? arguments.ToList();
            effects = effects.Union(CheckRowedArguments(callTarget, argumentList));

            foreach (var arg in argumentList)
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
                else if (IsFunctionTypeName(valueType) && callTarget.Contains('.'))
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
            IReadOnlyList<ExpressionNode>? arguments = null)
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
                if (ResolveLocalValueType(target) != null)
                    return InferFromBareNameTarget(target, span);

                return InferFromBareNameTarget(target, span);
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
                var resolution = _context.Resolver.Resolve(typeName, methodName, argumentTypes);
                if (resolution.Status != EffectResolutionStatus.Unknown)
                {
                    return resolution.Effects;
                }

                // If type didn't resolve, try variable type resolution:
                // "r.Next" where "r" is a variable declared as "new Random()"
                var resolvedVarType = ResolveVariableType(typeName);
                if (resolvedVarType != null && resolvedVarType != typeName)
                {
                    resolution = _context.Resolver.Resolve(resolvedVarType, methodName, argumentTypes);
                    if (resolution.Status != EffectResolutionStatus.Unknown)
                    {
                        return resolution.Effects;
                    }

                    resolution = _context.Resolver.ResolveExtension(
                        resolvedVarType,
                        methodName,
                        argumentTypes);
                    if (resolution.Status != EffectResolutionStatus.Unknown)
                        return resolution.Effects;
                }

                var (chainedReceiverType, chainedEffects) = ResolveReceiverChain(typeName);
                if (chainedReceiverType != null)
                {
                    resolution = _context.Resolver.Resolve(
                        chainedReceiverType,
                        methodName,
                        argumentTypes);
                    if (resolution.Status == EffectResolutionStatus.Unknown)
                    {
                        resolution = _context.Resolver.ResolveExtension(
                            chainedReceiverType,
                            methodName,
                            argumentTypes);
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
        ///     being invoked, which is by construction a delegate/function-typed
        ///     invocation → unconditional Calor0418 error under enforcement (demoted
        ///     to a warning only under --permissive-effects, the explicit waiver).
        ///     There is no annotation escape hatch: effect-annotated function types
        ///     are a Phase 3 design.
        /// (b) the name is a free name the pass cannot see (e.g. an inherited member
        ///     of an external C# base) — routed through the unknown-call chain, which
        ///     fails loud under the strict policy (Calor0411 + unknown effects)
        ///     instead of the pre-W2 behavior of silently assuming purity.
        /// </summary>
        private EffectSet InferFromBareNameTarget(string target, TextSpan span)
        {
            // EMITTER SPIKE (§3.6 E-1, §7.4). A function-typed value that CARRIES
            // A ROW can be charged, so Calor0418 — "carries no effect contract" —
            // is simply not true of it any more. This branch is strictly
            // ADDITIVE: it engages only when a row is present, so every row-less
            // program (and therefore every committed test, transcript and ledger)
            // keeps today's behaviour exactly.
            var declaredRow = ResolveLocalValueRow(target);
            if (declaredRow != null)
            {
                return ChargeInvokedRow(target, declaredRow, span);
            }

            var valueType = ResolveLocalValueType(target);
            if (valueType != null)
            {
                var severity = _context.Policy == UnknownCallPolicy.Permissive
                    ? DiagnosticSeverity.Warning
                    : DiagnosticSeverity.Error;
                var typeDescription = IsFunctionTypeName(valueType)
                    ? $"function-typed value '{target}' (type '{valueType}')"
                    : $"value '{target}' (declared type '{valueType}')";
                _context.Diagnostics.Report(
                    span,
                    DiagnosticCode.DelegateInvocation,
                    $"Invocation of {typeDescription} is an error under effect enforcement: " +
                    "function-typed values carry no effect contract, so the call cannot be charged. " +
                    "Wrap the call in §CSHARP interop (surfaced as an assumption via Calor0419) or " +
                    "compile with --permissive-effects (an explicit waiver).",
                    severity);
                return EffectSet.Empty;
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
                    "visible to the effect pass. If this is a delegate-typed member, delegate invocation is " +
                    "disallowed under enforcement (Calor0418); otherwise add the callee to a " +
                    ".calor-effects.json manifest.",
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
        /// Resolves a bare name to the declared type of the value it denotes:
        /// current-function parameter, §B binding (anywhere in the body, including
        /// nested blocks), or enclosing-class field. Returns null for free names.
        /// A lambda-initialized binding without an explicit type reports the marker
        /// type "Func&lt;&gt;" (function-typed by construction).
        /// </summary>
        private string? ResolveLocalValueType(string name)
        {
            if (!_context.Functions.TryGetValue(_context.CurrentFunctionId, out var function))
                return null;

            foreach (var parameter in function.Parameters)
            {
                if (parameter.Name.Equals(name, StringComparison.Ordinal))
                    return parameter.TypeName;
            }

            var declaredType = FindLocalDeclarationType(name, function.Body);
            if (declaredType != null)
                return declaredType;

            var foreachType = FindForeachVariableType(name, function.Body);
            if (foreachType != null)
                return foreachType;

            var field = _context.OwnerClass?.Fields.FirstOrDefault(
                f => f.Name.Equals(name, StringComparison.Ordinal));
            if (field != null)
                return field.TypeName;

            return null;
        }

        /// <summary>
        /// EMITTER SPIKE (§3.3 positions 4/5/8). The row annotating the bare name
        /// <paramref name="name"/>: a parameter of the current function, or a
        /// field of the enclosing class. Returns null when the name has no row —
        /// which keeps every row-less program on today's code path.
        /// </summary>
        private EffectRow? ResolveLocalValueRow(string name)
        {
            if (!_context.Functions.TryGetValue(_context.CurrentFunctionId, out var function))
                return null;

            foreach (var parameter in function.Parameters)
            {
                if (parameter.Name.Equals(name, StringComparison.Ordinal))
                    return GetAnnotationRow(parameter.Row);
            }

            var field = _context.OwnerClass?.Fields.FirstOrDefault(
                f => f.Name.Equals(name, StringComparison.Ordinal));
            return field == null ? null : GetAnnotationRow(field.Row);
        }

        /// <summary>
        /// EMITTER SPIKE. Charges the row of an invoked function-typed value.
        /// The CONCRETE part flows into the ordinary effect set, where Calor0410
        /// checks it against the enclosing declaration exactly as for any other
        /// charge. A rank-1 VARIABLE has no concrete content, so it is checked
        /// structurally instead: invoking a value whose row mentions variable #k
        /// requires the enclosing declaration's own row to mention #k too —
        /// otherwise the declaration would be hiding a caller-supplied effect,
        /// which is the same defect Calor0410 reports for a concrete code.
        /// </summary>
        private EffectSet ChargeInvokedRow(string target, EffectRow row, TextSpan span)
        {
            if (row.Kind == EffectRow.RowKind.Unknown)
            {
                ReportRowUnknown(
                    span,
                    $"The effect row of '{target}' is Unknown, so invoking it charges Unknown effects.");
                return EffectSet.Unknown;
            }

            var enclosing = CurrentDeclaredRow();
            foreach (var variable in row.Variables)
            {
                if (enclosing.Variables.Contains(variable)) continue;

                _context.Diagnostics.Report(
                    span,
                    DiagnosticCode.ForbiddenEffect,
                    $"Function '{CurrentDeclarationName()}' invokes '{target}', whose effect row mentions "
                    + "an effect variable this declaration does not declare. Add the variable to the "
                    + "declaration's own §E{…} row.",
                    DiagnosticSeverity.Error);
            }

            return row.Effects;
        }

        /// <summary>
        /// EMITTER SPIKE. Calor0425 — a "cannot tell" verdict. Warning by
        /// default, error under --strict-effects, waived by
        /// --permissive-effects (§4.5: the waiver keeps exactly one job).
        /// </summary>
        private void ReportRowUnknown(TextSpan span, string message)
        {
            if (_context.Policy == UnknownCallPolicy.Permissive) return;

            _context.Diagnostics.Report(
                span,
                DiagnosticCode.EffectRowUnknown,
                message,
                _context.StrictEffects ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning);
        }

        /// <summary>EMITTER SPIKE. The row declared by the function being walked.</summary>
        private EffectRow CurrentDeclaredRow()
            => _context.Functions.TryGetValue(_context.CurrentFunctionId, out var function)
                ? GetDeclaredRow(function.Effects)
                : EffectRow.Pure;

        /// <summary>EMITTER SPIKE. The name of the function being walked, for diagnostics.</summary>
        private string CurrentDeclarationName()
            => _context.Functions.TryGetValue(_context.CurrentFunctionId, out var function)
                ? function.Name
                : _context.CurrentFunctionId;

        private string? FindForeachVariableType(
            string name,
            IReadOnlyList<StatementNode> rootStatements)
        {
            string? Search(IEnumerable<StatementNode> statements)
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

        private string? FindLocalDeclarationType(string name, IEnumerable<StatementNode> statements)
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
                        return "?"; // known value, unknown type
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
                receiverType,
                methodName,
                argumentTypes);
            if (resolution.Status == EffectResolutionStatus.Unknown)
            {
                resolution = _context.Resolver.ResolveExtension(
                    receiverType,
                    methodName,
                    argumentTypes);
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

        private bool IsFunctionTypeName(string typeName)
        {
            var t = typeName.Trim().TrimEnd('?');
            var stripped = StripGenericArguments(t);
            if (stripped != null && _context.DelegateTypeNames.Contains(stripped))
                return true;
            return t.Equals("Action", StringComparison.Ordinal)
                || t.StartsWith("Action<", StringComparison.Ordinal)
                || t.StartsWith("Func<", StringComparison.Ordinal)
                || t.StartsWith("Predicate<", StringComparison.Ordinal)
                || t.StartsWith("Comparison<", StringComparison.Ordinal)
                || t.StartsWith("Converter<", StringComparison.Ordinal)
                || t.Equals("Delegate", StringComparison.Ordinal)
                || t.Equals("MulticastDelegate", StringComparison.Ordinal)
                || t.Equals("EventHandler", StringComparison.Ordinal)
                || t.StartsWith("EventHandler<", StringComparison.Ordinal);
        }

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

        /// <summary>
        /// EMITTER SPIKE — §6.2 site 2 (argument) and site 6 (rank-1 generic
        /// instantiation), implemented together because they read the same two
        /// facts: the parameter's declared row and the argument's row.
        ///
        /// For each parameter the callee annotates with a row:
        ///   • if the parameter's row is MONOMORPHIC, the argument's row must fit
        ///     it — <c>DoesNotFit</c> is Calor0424 (never waived, §4.5),
        ///     <c>CannotTell</c> is Calor0425;
        ///   • if the parameter's row mentions effect variables, the argument
        ///     contributes to their solution by §7.4's one-line rule
        ///     <c>e := ⊔ { ρ(argⱼ) ⊖ ρ_declⱼ }</c>. One variable, one solution,
        ///     computed in a single pass over the argument list — no constraint
        ///     set, no fixpoint. That is the whole of R3.
        ///
        /// The solved effects are returned so the caller is charged them, and any
        /// CALLER-side variable that flows into the solution is checked against
        /// the enclosing declaration's own row.
        /// </summary>
        private EffectSet CheckRowedArguments(
            string callTarget,
            IReadOnlyList<ExpressionNode> arguments)
        {
            var callee = FindInternalFunctionByName(callTarget);
            if (callee == null) return EffectSet.Empty;

            var parameters = callee.Parameters;
            if (parameters.Count == 0 || parameters.All(p => p.Row == null))
                return EffectSet.Empty;

            var solvedEffects = EffectSet.Empty;
            var solvedVariables = new List<int>();

            for (var index = 0; index < arguments.Count && index < parameters.Count; index++)
            {
                var parameterRow = GetAnnotationRow(parameters[index].Row);
                if (parameterRow == null) continue;

                var argument = arguments[index];
                var argumentRow = ResolveArgumentRow(argument);
                var argumentLabel = argument is ReferenceNode reference ? reference.Name : "argument";

                if (argumentRow == null || argumentRow.Kind == EffectRow.RowKind.Unknown)
                {
                    ReportRowUnknown(
                        argument.Span,
                        $"Argument '{argumentLabel}' of '{callTarget}' has no determinable effect row, so "
                        + $"parameter '{parameters[index].Name}' is instantiated to Unknown here.");
                    if (parameterRow.IsPolymorphic) return EffectSet.Unknown;
                    continue;
                }

                if (!parameterRow.IsPolymorphic)
                {
                    // Site 2 — a monomorphic destination.
                    if (EffectRow.Fits(argumentRow, parameterRow) == RowFit.DoesNotFit)
                    {
                        _context.Diagnostics.Report(
                            argument.Span,
                            DiagnosticCode.EffectRowMismatch,
                            $"Argument '{argumentLabel}' has effect row {argumentRow.ToDisplayString()}, which "
                            + $"does not fit parameter '{parameters[index].Name}' of '{callTarget}' "
                            + $"(declared row: {parameterRow.ToDisplayString()}). "
                            + $"Extra effect(s): {EffectRow.ExtraEffects(argumentRow, parameterRow)}. "
                            + $"Widen '{parameters[index].Name}', or pass a function whose row fits. "
                            + "An effect row that does not fit is never waived.",
                            DiagnosticSeverity.Error);
                    }
                    continue;
                }

                // Site 6 — the one-line solve. `⊖` is difference over the
                // concrete part: whatever the argument brings beyond what the
                // parameter's own row already promises.
                foreach (var effect in argumentRow.Effects.Except(parameterRow.Effects))
                {
                    solvedEffects = solvedEffects.Union(
                        EffectSet.FromInternal(new[] { effect }));
                }

                solvedVariables.AddRange(argumentRow.Variables);
            }

            // A caller-side variable that reaches the solution must be declared by
            // the enclosing declaration's own row, exactly as for a direct
            // invocation (see ChargeInvokedRow).
            var enclosing = CurrentDeclaredRow();
            foreach (var variable in solvedVariables.Distinct().OrderBy(v => v))
            {
                if (enclosing.Variables.Contains(variable)) continue;

                _context.Diagnostics.Report(
                    arguments.Count > 0 ? arguments[0].Span : TextSpan.Empty,
                    DiagnosticCode.ForbiddenEffect,
                    $"Function '{CurrentDeclarationName()}' instantiates an effect variable of "
                    + $"'{callTarget}' from an argument whose row mentions an effect variable this "
                    + "declaration does not declare.",
                    DiagnosticSeverity.Error);
            }

            return solvedEffects;
        }

        /// <summary>
        /// EMITTER SPIKE. The row of an argument expression: a rowed local value
        /// (parameter or field), or a method group, whose row is its declared
        /// effect set. Returns null when the spike cannot determine one.
        /// </summary>
        private EffectRow? ResolveArgumentRow(ExpressionNode argument)
        {
            if (argument is not ReferenceNode reference || reference.Name.Contains('.'))
                return null;

            var localRow = ResolveLocalValueRow(reference.Name);
            if (localRow != null) return localRow;

            var internalFunction = FindInternalFunctionByName(reference.Name);
            return internalFunction == null ? null : GetDeclaredRow(internalFunction.Effects);
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

        private (string? Type, EffectSet Effects) ResolveReceiverChain(string receiverPath)
        {
            var parts = receiverPath.Split('.');
            if (parts.Length < 2)
                return (null, EffectSet.Empty);

            var currentType = ResolveVariableType(parts[0]);
            if (currentType == null)
                return (null, EffectSet.Empty);

            var effects = EffectSet.Empty;
            for (var i = 1; i < parts.Length; i++)
            {
                var member = parts[i];
                var getter = _context.Resolver.ResolveGetter(currentType, member);
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
            var resolution = _context.Resolver.Resolve(manifestType, "Dispose");
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
            var resolution = _context.Resolver.Resolve(
                manifestType,
                $"{(isAdd ? "add" : "remove")}_{eventName}",
                handlerType);
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
            var resolution = _context.Resolver.ResolveGetter(manifestType, field.FieldName);
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
            var resolution = _context.Resolver.ResolveSetter(manifestType, field.FieldName);
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
                : InferGetterEffects(receiverType, member, reference.Span);
        }

        private EffectSet InferGetterEffects(string receiverType, string member, TextSpan span)
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
            var resolution = _context.Resolver.ResolveGetter(manifestType, member);
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
            var resolution = _context.Resolver.ResolveSetter(manifestType, member);
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
                    // Calor0418; internal function → its effects; free name →
                    // unknown chain).
                    return InferFromCallTarget(reference.Name, call.Span, call.Arguments).Union(argEffects);

                case LambdaExpressionNode lambda:
                    // Immediately-invoked lambda literal: the body IS the callee,
                    // so its effects are fully charged — no delegate opacity.
                    return InferFromLambda(lambda).Union(argEffects);

                case CallExpressionNode or ExpressionCallNode:
                {
                    // Invoking the RESULT of a call (`GetF()()`): the invoked
                    // thing is a delegate value — Calor0418, same rule as a
                    // named delegate invocation (waived to a warning only under
                    // --permissive-effects).
                    var severity = _context.Policy == UnknownCallPolicy.Permissive
                        ? DiagnosticSeverity.Warning
                        : DiagnosticSeverity.Error;
                    _context.Diagnostics.Report(
                        call.Span,
                        DiagnosticCode.DelegateInvocation,
                        "Invocation of a returned delegate value is an error under effect enforcement: " +
                        "function-typed values carry no effect contract, so the call cannot be charged. " +
                        "Wrap the call in §CSHARP interop (surfaced as an assumption via Calor0419) or " +
                        "compile with --permissive-effects (an explicit waiver).",
                        severity);
                    return InferFromExpression(call.TargetExpression).Union(argEffects);
                }

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
            var resolution = _context.Resolver.ResolveConstructor(manifestType, argumentTypes);
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
            var resolution = initializer.PropertyName.StartsWith("_item", StringComparison.Ordinal)
                ? _context.Resolver.Resolve(
                    manifestType,
                    "Add",
                    InferExpressionType(initializer.Value))
                : _context.Resolver.ResolveSetter(manifestType, initializer.PropertyName);
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
            // Lambda body contributes effects to enclosing function
            if (lambda.ExpressionBody != null)
            {
                return InferFromExpression(lambda.ExpressionBody);
            }
            if (lambda.StatementBody != null)
            {
                return InferFromStatements(lambda.StatementBody);
            }
            return EffectSet.Empty;
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
