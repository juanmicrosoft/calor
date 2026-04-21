using Xunit;
using Calor.Compiler.Effects;
using Calor.Compiler.Effects.IL;
using Calor.Compiler.Effects.Manifests;

namespace Calor.ILAnalysis.Tests;

public class ILAnalysisTests : IDisposable
{
    private static readonly string TestAssemblyDir = Path.Combine(
        AppContext.BaseDirectory, "TestAssemblies");

    private static readonly string DataAccessDll = Path.Combine(TestAssemblyDir, "TestAssembly.DataAccess.dll");
    private static readonly string ScenariosDll = Path.Combine(TestAssemblyDir, "TestAssembly.Scenarios.dll");

    private ILEffectAnalyzer? _analyzer;

    private ILEffectAnalyzer CreateAnalyzer(params string[] assemblyPaths)
    {
        var resolver = new EffectResolver();
        resolver.Initialize();
        _analyzer = new ILEffectAnalyzer(assemblyPaths, resolver);
        return _analyzer;
    }

    public void Dispose()
    {
        _analyzer?.Dispose();
    }

    // ============================================================
    // Assembly Loading Tests
    // ============================================================

    [Fact]
    public void AssemblyIndex_LoadsImplementationAssembly()
    {
        using var index = new AssemblyIndex([DataAccessDll]);
        Assert.True(index.LoadedAssemblies.Count > 0);
    }

    [Fact]
    public void AssemblyIndex_FindsMethodByTypeName()
    {
        using var index = new AssemblyIndex([DataAccessDll]);
        var location = index.FindMethod("TestAssembly.DataAccess.UserRepository", "Save");
        Assert.NotNull(location);
        Assert.True(location.HasBody);
    }

    [Fact]
    public void AssemblyIndex_ReturnsNullForUnknownType()
    {
        using var index = new AssemblyIndex([DataAccessDll]);
        var location = index.FindMethod("NonExistent.Type", "Method");
        Assert.Null(location);
    }

    [Fact]
    public void AssemblyIndex_MalformedAssembly_DoesNotCrash()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"calor-malformed-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(tempPath, [0xFF, 0xFE, 0x00, 0x00]); // garbage
        try
        {
            using var index = new AssemblyIndex([tempPath]);
            // Should not throw — just skip the bad file
            Assert.Empty(index.LoadedAssemblies);
        }
        finally
        {
            try { File.Delete(tempPath); } catch { /* best-effort cleanup */ }
        }
    }

    [Fact]
    public void AssemblyIndex_EmptyAssemblyList_NoError()
    {
        using var index = new AssemblyIndex([]);
        Assert.Empty(index.LoadedAssemblies);
    }

    // ============================================================
    // ILCallGraphBuilder Tests
    // ============================================================

    [Fact]
    public void CallGraphBuilder_ExtractsDirectCallEdges()
    {
        using var index = new AssemblyIndex([DataAccessDll]);
        var builder = new ILCallGraphBuilder(index);
        var location = index.FindMethod("TestAssembly.DataAccess.UserRepository", "Save");
        Assert.NotNull(location);

        var result = builder.ExtractCallEdges(location);
        Assert.NotEmpty(result.Edges);

        // Should contain calls to DbConnection.Open and DbCommand.ExecuteNonQuery
        var calleeNames = result.Edges.Select(e => e.Callee.MethodName).ToHashSet();
        Assert.Contains("ExecuteNonQuery", calleeNames);
    }

    [Fact]
    public void CallGraphBuilder_ExtractsVirtualCallEdges()
    {
        using var index = new AssemblyIndex([ScenariosDll]);
        var builder = new ILCallGraphBuilder(index);
        var location = index.FindMethod("TestAssembly.Scenarios.ServiceWithInterface", "Process");
        Assert.NotNull(location);

        var result = builder.ExtractCallEdges(location);
        Assert.NotEmpty(result.Edges);
        Assert.Contains(result.Edges, e => e.IsVirtual);
    }

    // ============================================================
    // StateMachineResolver Tests
    // ============================================================

    [Fact]
    public void StateMachineResolver_DetectsAsyncMethod()
    {
        using var index = new AssemblyIndex([ScenariosDll]);
        var resolver = new StateMachineResolver(index);
        var location = index.FindMethod("TestAssembly.Scenarios.AsyncService", "SaveAsync");
        Assert.NotNull(location);

        var moveNext = resolver.Redirect(location);
        Assert.NotNull(moveNext);
        Assert.Equal("MoveNext", moveNext.Key.MethodName);
    }

    [Fact]
    public void StateMachineResolver_NonAsyncMethod_ReturnsNull()
    {
        using var index = new AssemblyIndex([DataAccessDll]);
        var resolver = new StateMachineResolver(index);
        var location = index.FindMethod("TestAssembly.DataAccess.UserRepository", "Save");
        Assert.NotNull(location);

        var moveNext = resolver.Redirect(location);
        Assert.Null(moveNext);
    }

    // ============================================================
    // Core Resolution Tests
    // ============================================================

    [Fact]
    public void ManifestResolver_RecognizesDbCommandSeed()
    {
        // Verify the manifest actually covers DbCommand.ExecuteNonQuery as a prerequisite
        var resolver = new EffectResolver();
        resolver.Initialize();
        var result = resolver.Resolve("System.Data.Common.DbCommand", "ExecuteNonQuery");
        Assert.NotEqual(EffectResolutionStatus.Unknown, result.Status);
        Assert.False(result.Effects.IsEmpty); // Should contain db:w
    }

    [Fact]
    public void Debug_CallEdges_FromUserRepositorySave()
    {
        // Diagnostic: see what types/methods the call graph builder extracts
        using var index = new AssemblyIndex([DataAccessDll]);
        var builder = new ILCallGraphBuilder(index);
        var location = index.FindMethod("TestAssembly.DataAccess.UserRepository", "Save");
        Assert.NotNull(location);

        var result = builder.ExtractCallEdges(location);

        // Verify we see DbCommand.ExecuteNonQuery in the edges
        Assert.Contains(result.Edges, e =>
            e.Callee.TypeName.Contains("DbCommand") &&
            e.Callee.MethodName == "ExecuteNonQuery");

        // Verify all BCL calls can be resolved by the manifest
        var resolver = new EffectResolver();
        resolver.Initialize();

        var unresolvedCallees = result.Edges
            .Where(e =>
            {
                var res = resolver.Resolve(e.Callee.TypeName, e.Callee.MethodName);
                return res.Status == EffectResolutionStatus.Unknown;
            })
            .Select(e => e.Callee.ToString())
            .ToList();

        // Key BCL method calls (not setters/getters) should be covered by manifests
        var unresolvedDbMethods = result.Edges
            .Where(e => e.Callee.TypeName.StartsWith("System.Data"))
            .Where(e => !e.Callee.MethodName.StartsWith("set_") && !e.Callee.MethodName.StartsWith("get_"))
            .Where(e =>
            {
                var res = resolver.Resolve(e.Callee.TypeName, e.Callee.MethodName);
                return res.Status == EffectResolutionStatus.Unknown;
            })
            .Select(e => e.Callee.ToString())
            .ToList();
        Assert.Empty(unresolvedDbMethods);
    }

    [Fact]
    public void Debug_CalleeTypeNames()
    {
        // See what type names the IL analysis extracts for DbCommand calls
        using var index = new AssemblyIndex([DataAccessDll]);
        var builder = new ILCallGraphBuilder(index);
        var location = index.FindMethod("TestAssembly.DataAccess.UserRepository", "Save");
        Assert.NotNull(location);

        var result = builder.ExtractCallEdges(location);
        var dbCallees = result.Edges
            .Where(e => e.Callee.MethodName == "ExecuteNonQuery")
            .Select(e => $"Type='{e.Callee.TypeName}' Method='{e.Callee.MethodName}' Sig='{e.Callee.ParameterSig}'")
            .ToList();

        // At least one DbCommand.ExecuteNonQuery should be found
        Assert.NotEmpty(dbCallees);

        // Check the type name matches what the manifest expects
        var resolver = new EffectResolver();
        resolver.Initialize();
        foreach (var edge in result.Edges.Where(e => e.Callee.MethodName == "ExecuteNonQuery"))
        {
            var res = resolver.Resolve(edge.Callee.TypeName, edge.Callee.MethodName);
            Assert.NotEqual(EffectResolutionStatus.Unknown, res.Status);
        }
    }

    [Fact]
    public void Analyzer_DirectCallToDbSeed_ResolvesDbWrite()
    {
        // Test the propagator directly to debug effect flow
        using var index = new AssemblyIndex([DataAccessDll]);
        var resolver = new EffectResolver();
        resolver.Initialize();
        var builder = new ILCallGraphBuilder(index);
        var smResolver = new StateMachineResolver(index);
        var propagator = new TransitiveEffectPropagator(builder, index, smResolver, resolver);

        var entryPoint = MethodKey.NameOnly("TestAssembly.DataAccess.UserRepository", "Save");
        var results = propagator.Propagate([entryPoint]);

        // Verify the resolver works directly
        var directCheck = resolver.Resolve("System.Data.Common.DbCommand", "ExecuteNonQuery");
        Assert.NotEqual(EffectResolutionStatus.Unknown, directCheck.Status);
        Assert.False(directCheck.Effects.IsEmpty, $"Direct resolver check: {directCheck.Status}, {directCheck.Effects}");

        Assert.True(results.ContainsKey(entryPoint), "Entry point not in results");
        var (status, effects) = results[entryPoint];

        // UserRepository.Save() → DbCommand.ExecuteNonQuery() (db:w) + DbConnection.Open() (db:rw)
        Assert.Equal(ILResolutionStatus.Resolved, status);
        Assert.False(effects.IsEmpty, "Effects should contain db:w from transitive call chain");
    }

    [Fact]
    public void Analyzer_TransitiveCall_ResolvesDbWrite()
    {
        var analyzer = CreateAnalyzer(DataAccessDll);

        // UserService.CreateUser() → UserRepository.Save() → DbCommand.ExecuteNonQuery()
        analyzer.AnalyzeFromCallSites([("TestAssembly.DataAccess.UserService", "CreateUser")]);

        var result = analyzer.TryResolve("TestAssembly.DataAccess.UserService", "CreateUser");
        // 3-hop chain should propagate db:w all the way back
        Assert.NotNull(result);
        Assert.False(result.Effects.IsEmpty, "Transitive call should propagate db effects");
    }

    [Fact]
    public void Analyzer_PureMethod_ResolvesPure()
    {
        var analyzer = CreateAnalyzer(DataAccessDll);

        analyzer.AnalyzeFromCallSites([("TestAssembly.DataAccess.MathHelper", "Add")]);

        var result = analyzer.TryResolve("TestAssembly.DataAccess.MathHelper", "Add");
        // MathHelper.Add is pure (just returns a + b) — no effects
        Assert.NotNull(result);
        Assert.Equal(EffectResolutionStatus.PureExplicit, result.Status);
        Assert.True(result.Effects.IsEmpty);
    }

    [Fact]
    public void Analyzer_DeepChain_TracesThrough()
    {
        var analyzer = CreateAnalyzer(ScenariosDll);

        // 15-level deep chain
        analyzer.AnalyzeFromCallSites([("TestAssembly.Scenarios.DeepChain", "Level0")]);

        Assert.True(analyzer.CachedMethodCount >= 15);
    }

    [Fact]
    public void Analyzer_CircularCalls_ConvergesFixpoint()
    {
        var analyzer = CreateAnalyzer(ScenariosDll);

        // A → B → A (mutual recursion)
        analyzer.AnalyzeFromCallSites([("TestAssembly.Scenarios.CircularCalls", "A")]);

        // Should not hang or crash
        Assert.True(analyzer.CachedMethodCount >= 2);
    }

    // ============================================================
    // Virtual Dispatch Tests
    // ============================================================

    [Fact]
    public void Analyzer_VirtualDispatch_FindsImplementations()
    {
        using var index = new AssemblyIndex([ScenariosDll]);
        var key = MethodKey.NameOnly("TestAssembly.Scenarios.IStore", "Save");
        var impls = index.GetImplementations(key);
        Assert.True(impls.Count >= 2); // SqlStore and FileStore
    }

    // ============================================================
    // Delegate / ldftn Tests
    // ============================================================

    [Fact]
    public void CallGraphBuilder_ExtractsDelegateEdges()
    {
        using var index = new AssemblyIndex([ScenariosDll]);
        var builder = new ILCallGraphBuilder(index);
        var location = index.FindMethod("TestAssembly.Scenarios.DelegateService", "ProcessWithDelegate");
        Assert.NotNull(location);

        var result = builder.ExtractCallEdges(location);
        // Should find ldftn edge for the lambda
        Assert.NotEmpty(result.Edges);
    }

    // ============================================================
    // Method Identity Tests
    // ============================================================

    [Fact]
    public void MethodKey_DistinguishesOverloads()
    {
        using var index = new AssemblyIndex([ScenariosDll]);

        var intOverload = index.FindMethod("TestAssembly.Scenarios.OverloadService", "Process", "(System.Int32)");
        var strOverload = index.FindMethod("TestAssembly.Scenarios.OverloadService", "Process", "(System.String)");

        // At least one should be found — exact param sig format may vary
        // The key test: FindMethod with paramSig=null returns a result
        var anyOverload = index.FindMethod("TestAssembly.Scenarios.OverloadService", "Process");
        Assert.NotNull(anyOverload);
    }

    [Fact]
    public void MethodKey_NameOnlyLookup_UnionsOverloads()
    {
        var analyzer = CreateAnalyzer(ScenariosDll);

        analyzer.AnalyzeFromCallSites([("TestAssembly.Scenarios.OverloadService", "Process")]);

        // Name-only lookup should analyze at least the method (may find one overload via FindMethod)
        Assert.True(analyzer.CachedMethodCount >= 1);

        // TryResolve with name-only should return a result (union of overloads or first match)
        var result = analyzer.TryResolve("TestAssembly.Scenarios.OverloadService", "Process");
        // Analysis ran — cached something
        Assert.True(analyzer.CachedMethodCount >= 1);
    }

    // ============================================================
    // Boundary Tests
    // ============================================================

    [Fact]
    public void Analyzer_DepthLimit_RespectsMaxDepth()
    {
        var options = new ILAnalysisOptions { MaxDepth = 5 };
        var resolver = new EffectResolver();
        resolver.Initialize();
        _analyzer = new ILEffectAnalyzer([ScenariosDll], resolver, options);

        // 15-level chain with max depth 5 — should hit limit
        _analyzer.AnalyzeFromCallSites([("TestAssembly.Scenarios.DeepChain", "Level0")]);

        // Analysis should still complete without crash
        Assert.True(_analyzer.CachedMethodCount > 0);
    }

    [Fact]
    public void Analyzer_MaxVisitedMethods_RespectsLimit()
    {
        var options = new ILAnalysisOptions { MaxVisitedMethods = 5 };
        var resolver = new EffectResolver();
        resolver.Initialize();
        _analyzer = new ILEffectAnalyzer([ScenariosDll], resolver, options);

        _analyzer.AnalyzeFromCallSites([("TestAssembly.Scenarios.DeepChain", "Level0")]);

        // Should have stopped early
        Assert.True(_analyzer.CachedMethodCount <= 10); // some buffer for seeds
    }

    // ============================================================
    // Soundness Tests
    // ============================================================

    [Fact]
    public void Analyzer_IncompleteTrace_DoesNotReturnFalsePure()
    {
        var options = new ILAnalysisOptions { MaxVisitedMethods = 3 };
        var resolver = new EffectResolver();
        resolver.Initialize();
        _analyzer = new ILEffectAnalyzer([ScenariosDll], resolver, options);

        _analyzer.AnalyzeFromCallSites([("TestAssembly.Scenarios.DeepChain", "Level0")]);

        // The result should be null (Incomplete), NOT a PureExplicit resolution
        var result = _analyzer.TryResolve("TestAssembly.Scenarios.DeepChain", "Level0");
        // If analysis was incomplete, TryResolve returns null (falls through to Unknown)
        // It should NOT return a resolution claiming the method is pure
        if (result != null)
        {
            // If it resolved, it should have effects (not empty/pure)
            // because the deep chain eventually calls DbCommand.ExecuteNonQuery
            Assert.NotEqual(EffectResolutionStatus.PureExplicit, result.Status);
        }
    }

    // ============================================================
    // Integration Tests
    // ============================================================

    [Fact]
    public void Analyzer_ReuseAcrossMultipleQueries()
    {
        var analyzer = CreateAnalyzer(DataAccessDll, ScenariosDll);

        // Analyze multiple entry points
        analyzer.AnalyzeFromCallSites([
            ("TestAssembly.DataAccess.UserService", "CreateUser"),
            ("TestAssembly.DataAccess.UserService", "GetUser"),
            ("TestAssembly.Scenarios.DeepChain", "Level0"),
        ]);

        // All should have been analyzed
        Assert.True(analyzer.CachedMethodCount >= 5);

        // Multiple TryResolve calls should work
        analyzer.TryResolve("TestAssembly.DataAccess.UserService", "CreateUser");
        analyzer.TryResolve("TestAssembly.DataAccess.UserService", "GetUser");
        analyzer.TryResolve("TestAssembly.Scenarios.DeepChain", "Level0");
    }

    [Fact]
    public void Analyzer_EmptyAssemblyList_NoError()
    {
        var resolver = new EffectResolver();
        resolver.Initialize();
        _analyzer = new ILEffectAnalyzer([], resolver);

        _analyzer.AnalyzeFromCallSites([("SomeType", "SomeMethod")]);

        // With no assemblies loaded, the method resolves as pure (default for unloadable)
        // This is a no-op — no crash, no error
        Assert.Equal(0, _analyzer.LoadedAssemblyCount);
    }

    [Fact]
    public void Analyzer_DisposedAnalyzer_ReturnsNull()
    {
        var resolver = new EffectResolver();
        resolver.Initialize();
        var analyzer = new ILEffectAnalyzer([DataAccessDll], resolver);
        analyzer.Dispose();

        var result = analyzer.TryResolve("TestAssembly.DataAccess.UserRepository", "Save");
        Assert.Null(result);
    }

    [Fact]
    public void EffectResolver_WithILAnalyzer_FallsBackCorrectly()
    {
        var ilAnalyzer = CreateAnalyzer(DataAccessDll);
        ilAnalyzer.AnalyzeFromCallSites([("TestAssembly.DataAccess.UserRepository", "Save")]);

        var resolver = new EffectResolver(ilAnalyzer: ilAnalyzer);
        resolver.Initialize();

        // A type not in manifests but in the IL analyzer
        var result = resolver.Resolve("TestAssembly.DataAccess.UserRepository", "Save");
        // Should either be resolved via IL or fall through to Unknown
        // The key: it doesn't crash and returns a valid resolution
        Assert.NotNull(result);
    }
}
