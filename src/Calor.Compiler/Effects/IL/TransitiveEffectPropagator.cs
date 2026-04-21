namespace Calor.Compiler.Effects.IL;

/// <summary>
/// Propagates effects transitively through the IL call graph.
///
/// Phase 1 (Graph construction): Demand-driven BFS from entry points.
///   Stop expanding at manifest-resolved seeds or analysis boundaries.
/// Phase 2 (Propagation): Tarjan SCC + reverse-topological fixpoint.
///   effects(m) = Union(effects of all callees). Incomplete propagates up.
/// </summary>
public sealed class TransitiveEffectPropagator
{
    private readonly ILCallGraphBuilder _callGraphBuilder;
    private readonly AssemblyIndex _assemblyIndex;
    private readonly StateMachineResolver _stateMachineResolver;
    private readonly EffectResolver _manifestResolver;
    private readonly ILAnalysisOptions _options;

    public TransitiveEffectPropagator(
        ILCallGraphBuilder callGraphBuilder,
        AssemblyIndex assemblyIndex,
        StateMachineResolver stateMachineResolver,
        EffectResolver manifestResolver,
        ILAnalysisOptions? options = null)
    {
        _callGraphBuilder = callGraphBuilder;
        _assemblyIndex = assemblyIndex;
        _stateMachineResolver = stateMachineResolver;
        _manifestResolver = manifestResolver;
        _options = options ?? new ILAnalysisOptions();
    }

    /// <summary>
    /// Runs transitive effect propagation from the given entry points.
    /// Returns a map from MethodKey to (status, effects).
    /// </summary>
    public Dictionary<MethodKey, (ILResolutionStatus Status, EffectSet Effects)> Propagate(
        IEnumerable<MethodKey> entryPoints)
    {
        // Phase 1: Build call graph via demand-driven BFS
        var (forwardEdges, seeds, incomplete) = BuildCallGraph(entryPoints);

        // Phase 2: Tarjan SCC + fixpoint propagation
        return PropagateEffects(forwardEdges, seeds, incomplete);
    }

    private (Dictionary<MethodKey, List<CallEdge>> ForwardEdges,
             Dictionary<MethodKey, EffectSet> Seeds,
             HashSet<MethodKey> Incomplete) BuildCallGraph(IEnumerable<MethodKey> entryPoints)
    {
        var forwardEdges = new Dictionary<MethodKey, List<CallEdge>>();
        var seeds = new Dictionary<MethodKey, EffectSet>();
        var incomplete = new HashSet<MethodKey>();
        var visited = new HashSet<MethodKey>();

        // Sort entry points for determinism
        var sortedEntries = entryPoints.OrderBy(e => e.ToString()).ToList();
        var worklist = new Queue<MethodKey>(sortedEntries);

        while (worklist.Count > 0)
        {
            var method = worklist.Dequeue();

            if (visited.Contains(method))
                continue;

            if (visited.Count >= _options.MaxVisitedMethods)
            {
                incomplete.Add(method);
                continue;
            }

            visited.Add(method);

            // Check manifest first — seeds don't expand further
            var resolution = ResolveFromManifest(method);
            if (resolution.Status != EffectResolutionStatus.Unknown)
            {
                seeds[method] = resolution.Effects;
                continue;
            }

            // Find method in loaded assemblies
            var location = _assemblyIndex.FindMethod(method);
            if (location == null)
            {
                // Method not in any loaded assembly. If it's an external BCL/framework
                // method not covered by manifests, treat as pure leaf rather than Incomplete.
                // Rationale: marking every unresolvable BCL method as Incomplete would make
                // ALL chains Incomplete (every method eventually calls Object:.ctor, exception
                // constructors, etc.). Methods with effects should be covered by manifests.
                seeds[method] = EffectSet.Empty;
                continue;
            }

            if (!location.HasBody)
            {
                // Method exists but has no IL body (extern, P/Invoke, abstract).
                // Same reasoning: treat as pure if manifests didn't cover it.
                seeds[method] = EffectSet.Empty;
                continue;
            }

            // State machine redirection (async/iterator)
            var redirected = _stateMachineResolver.Redirect(location);
            if (redirected != null)
                location = redirected;

            // Extract call edges
            var result = _callGraphBuilder.ExtractCallEdges(location);
            if (result.HasIndirectCalls)
                incomplete.Add(method);

            // Sort edges for deterministic processing, filtering out skipped interfaces
            var sortedEdges = result.Edges.OrderBy(e => e.Callee.ToString()).ToList();
            var keptEdges = new List<CallEdge>();

            foreach (var edge in sortedEdges)
            {
                if (edge.IsVirtual)
                {
                    // Skip known-unresolvable interfaces — don't include in graph
                    if (_options.SkipInterfaces.Contains(edge.Callee.TypeName))
                        continue;
                    if (_options.UbiquitousInterfaces.Contains(edge.Callee.TypeName))
                        continue;

                    keptEdges.Add(edge);

                    // Always enqueue the virtual method itself — it may be seeded by manifests
                    // (e.g., DbCommand.ExecuteNonQuery is abstract but has a manifest entry)
                    worklist.Enqueue(edge.Callee);

                    var impls = _assemblyIndex.GetImplementations(edge.Callee);
                    if (impls.Count > _options.MaxVirtualImplementations)
                    {
                        incomplete.Add(edge.Callee);
                        continue;
                    }

                    foreach (var impl in impls)
                        worklist.Enqueue(impl);
                }
                else
                {
                    keptEdges.Add(edge);
                    worklist.Enqueue(edge.Callee);
                }
            }

            forwardEdges[method] = keptEdges;
        }

        return (forwardEdges, seeds, incomplete);
    }

    /// <summary>
    /// Resolves a method from manifests, handling property accessors and constructors.
    /// IL generates set_PropertyName/get_PropertyName for property access,
    /// and .ctor for constructors — these need to be routed to the right resolver method.
    /// </summary>
    private EffectResolution ResolveFromManifest(MethodKey method)
    {
        // Try standard method resolution first
        var result = _manifestResolver.Resolve(method.TypeName, method.MethodName);
        if (result.Status != EffectResolutionStatus.Unknown)
            return result;

        // Property setter: set_PropertyName → ResolveSetter(type, PropertyName)
        if (method.MethodName.StartsWith("set_") && method.MethodName.Length > 4)
        {
            var propertyName = method.MethodName[4..];
            result = _manifestResolver.ResolveSetter(method.TypeName, propertyName);
            if (result.Status != EffectResolutionStatus.Unknown)
                return result;
        }

        // Property getter: get_PropertyName → ResolveGetter(type, PropertyName)
        if (method.MethodName.StartsWith("get_") && method.MethodName.Length > 4)
        {
            var propertyName = method.MethodName[4..];
            result = _manifestResolver.ResolveGetter(method.TypeName, propertyName);
            if (result.Status != EffectResolutionStatus.Unknown)
                return result;
        }

        // Constructor: .ctor → ResolveConstructor(type)
        if (method.MethodName == ".ctor")
        {
            result = _manifestResolver.ResolveConstructor(method.TypeName);
            if (result.Status != EffectResolutionStatus.Unknown)
                return result;
        }

        return result;
    }

    private Dictionary<MethodKey, (ILResolutionStatus, EffectSet)> PropagateEffects(
        Dictionary<MethodKey, List<CallEdge>> forwardEdges,
        Dictionary<MethodKey, EffectSet> seeds,
        HashSet<MethodKey> incomplete)
    {
        // Build the set of all known methods
        var allMethods = new HashSet<MethodKey>();
        foreach (var kvp in forwardEdges)
        {
            allMethods.Add(kvp.Key);
            foreach (var edge in kvp.Value)
                allMethods.Add(edge.Callee);
        }
        foreach (var seed in seeds.Keys) allMethods.Add(seed);
        foreach (var inc in incomplete) allMethods.Add(inc);

        // Compute SCCs via Tarjan's algorithm
        var sccs = ComputeSccs(allMethods, forwardEdges);

        // Process SCCs in reverse topological order (leaves first — Tarjan gives this)
        var computed = new Dictionary<MethodKey, (ILResolutionStatus Status, EffectSet Effects)>();

        // Seed entries
        foreach (var (method, effects) in seeds)
        {
            var status = effects.IsEmpty ? ILResolutionStatus.ResolvedPure : ILResolutionStatus.Resolved;
            computed[method] = (status, effects);
        }

        // Incomplete entries
        foreach (var method in incomplete)
        {
            computed.TryAdd(method, (ILResolutionStatus.Incomplete, EffectSet.Unknown));
        }

        foreach (var scc in sccs)
        {
            if (scc.Count == 1)
            {
                var method = scc[0];
                if (computed.ContainsKey(method)) continue; // Already a seed or incomplete

                var (effects, isComplete) = ComputeMethodEffects(method, forwardEdges, computed, incomplete);
                var status = !isComplete ? ILResolutionStatus.Incomplete
                    : effects.IsEmpty ? ILResolutionStatus.ResolvedPure
                    : ILResolutionStatus.Resolved;
                computed[method] = (status, effects);
            }
            else
            {
                // Multi-method SCC — fixpoint iteration
                ProcessSccFixpoint(scc, forwardEdges, computed, seeds, incomplete);
            }
        }

        return computed;
    }

    private static (EffectSet Effects, bool IsComplete) ComputeMethodEffects(
        MethodKey method,
        Dictionary<MethodKey, List<CallEdge>> forwardEdges,
        Dictionary<MethodKey, (ILResolutionStatus Status, EffectSet Effects)> computed,
        HashSet<MethodKey> incomplete)
    {
        var effects = EffectSet.Empty;
        var isComplete = true;

        if (!forwardEdges.TryGetValue(method, out var edges))
            return (effects, !incomplete.Contains(method));

        foreach (var edge in edges)
        {
            if (computed.TryGetValue(edge.Callee, out var calleeResult))
            {
                if (calleeResult.Status == ILResolutionStatus.Incomplete)
                    isComplete = false;
                effects = effects.Union(calleeResult.Effects);
            }
            else if (incomplete.Contains(edge.Callee))
            {
                isComplete = false;
            }
            // Callee not in computed and not incomplete — it wasn't visited (unreachable from seeds)
            // Treat as incomplete
            else
            {
                isComplete = false;
            }
        }

        return (effects, isComplete);
    }

    private void ProcessSccFixpoint(
        List<MethodKey> scc,
        Dictionary<MethodKey, List<CallEdge>> forwardEdges,
        Dictionary<MethodKey, (ILResolutionStatus Status, EffectSet Effects)> computed,
        Dictionary<MethodKey, EffectSet> seeds,
        HashSet<MethodKey> incomplete)
    {
        // Initialize SCC members not already computed
        foreach (var method in scc)
        {
            computed.TryAdd(method, (ILResolutionStatus.ResolvedPure, EffectSet.Empty));
        }

        // Convergence bound: O(|SCC| × |lattice height|)
        // EffectSet lattice height is bounded by the total number of distinct effects (~30)
        var maxIterations = scc.Count * 30;
        var sccSet = new HashSet<MethodKey>(scc);

        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            var changed = false;

            foreach (var method in scc)
            {
                if (seeds.ContainsKey(method)) continue; // Seeds are fixed

                var (newEffects, isComplete) = ComputeMethodEffects(method, forwardEdges, computed, incomplete);

                // Also union effects from SCC peers
                foreach (var peer in scc)
                {
                    if (peer.Equals(method)) continue;
                    if (computed.TryGetValue(peer, out var peerResult))
                        newEffects = newEffects.Union(peerResult.Effects);
                }

                var newStatus = !isComplete ? ILResolutionStatus.Incomplete
                    : newEffects.IsEmpty ? ILResolutionStatus.ResolvedPure
                    : ILResolutionStatus.Resolved;

                var current = computed[method];
                if (current.Status != newStatus || !current.Effects.Equals(newEffects))
                {
                    computed[method] = (newStatus, newEffects);
                    changed = true;
                }
            }

            if (!changed) break; // Fixpoint reached

            // Assert convergence — if we exhaust iterations, something is non-monotone
            if (iteration == maxIterations - 1)
            {
                // Mark entire SCC as Incomplete rather than producing incorrect results
                foreach (var method in scc)
                {
                    if (!seeds.ContainsKey(method))
                        computed[method] = (ILResolutionStatus.Incomplete, EffectSet.Unknown);
                }
            }
        }
    }

    /// <summary>
    /// Tarjan's SCC algorithm. Returns SCCs in reverse topological order.
    /// </summary>
    private static List<List<MethodKey>> ComputeSccs(
        HashSet<MethodKey> allMethods,
        Dictionary<MethodKey, List<CallEdge>> forwardEdges)
    {
        var index = 0;
        var stack = new Stack<MethodKey>();
        var onStack = new HashSet<MethodKey>();
        var indices = new Dictionary<MethodKey, int>();
        var lowlinks = new Dictionary<MethodKey, int>();
        var sccs = new List<List<MethodKey>>();

        // Sort methods for deterministic SCC ordering
        var sortedMethods = allMethods.OrderBy(m => m.ToString()).ToList();

        foreach (var method in sortedMethods)
        {
            if (!indices.ContainsKey(method))
                StrongConnect(method, ref index, stack, onStack, indices, lowlinks, forwardEdges, sccs);
        }

        return sccs;
    }

    private static void StrongConnect(
        MethodKey v, ref int index,
        Stack<MethodKey> stack, HashSet<MethodKey> onStack,
        Dictionary<MethodKey, int> indices, Dictionary<MethodKey, int> lowlinks,
        Dictionary<MethodKey, List<CallEdge>> forwardEdges,
        List<List<MethodKey>> sccs)
    {
        indices[v] = index;
        lowlinks[v] = index;
        index++;
        stack.Push(v);
        onStack.Add(v);

        if (forwardEdges.TryGetValue(v, out var edges))
        {
            foreach (var edge in edges)
            {
                var w = edge.Callee;
                if (!indices.ContainsKey(w))
                {
                    StrongConnect(w, ref index, stack, onStack, indices, lowlinks, forwardEdges, sccs);
                    lowlinks[v] = Math.Min(lowlinks[v], lowlinks[w]);
                }
                else if (onStack.Contains(w))
                {
                    lowlinks[v] = Math.Min(lowlinks[v], indices[w]);
                }
            }
        }

        if (lowlinks[v] == indices[v])
        {
            var scc = new List<MethodKey>();
            MethodKey w;
            do
            {
                w = stack.Pop();
                onStack.Remove(w);
                scc.Add(w);
            } while (!w.Equals(v));

            scc.Sort(); // Deterministic order within SCC
            sccs.Add(scc);
        }
    }
}
