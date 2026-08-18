using System.Collections.Immutable;
using Calor.Compiler.Binding.Metadata;
using Microsoft.CodeAnalysis;
using Xunit;

// Flat test namespace mirrors BinderDispatchCompletenessTests — avoids
// shadowing Calor.Compiler.Binding.Metadata elsewhere.
namespace Calor.Compiler.Tests;

/// <summary>
/// v0.14 §3.1 S1: MetadataContext plumbing tests. Exercises D3 lookup +
/// D4 overload-resolution mechanism. The lifecycle suite is in
/// <see cref="MetadataContextLifecycleTests"/> (round-2 M3 mitigation).
/// </summary>
public class MetadataContextTests
{
    // Test (i) — D3 TryResolveType: baseline BCL type resolves.
    [Fact]
    public void TryResolveType_SystemConsole_Resolves()
    {
        using var ctx = MetadataContext.Create();
        var symbol = ctx.TryResolveType("System.Console");
        Assert.NotNull(symbol);
        Assert.Equal("Console", symbol!.Name);
        Assert.Equal("System.Console", symbol.ToDisplayString());
    }

    [Fact]
    public void TryResolveType_SystemString_Resolves()
    {
        using var ctx = MetadataContext.Create();
        var symbol = ctx.TryResolveType("System.String");
        Assert.NotNull(symbol);
        Assert.Equal("String", symbol!.Name);
    }

    [Fact]
    public void TryResolveType_ListOfInt_ConstructsAndResolves()
    {
        using var ctx = MetadataContext.Create();
        var listUnbound = ctx.TryResolveType("System.Collections.Generic.List`1");
        Assert.NotNull(listUnbound);
        var intType = ctx.TryResolveType("System.Int32");
        Assert.NotNull(intType);
        var listOfInt = listUnbound!.Construct(intType!);
        Assert.Equal("List<int>",
            listOfInt.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
    }

    // Test (ii) — determinism across two independent contexts.
    [Fact]
    public void TryResolveType_Deterministic_AcrossTwoContexts()
    {
        using var ctx1 = MetadataContext.Create();
        using var ctx2 = MetadataContext.Create();
        var s1 = ctx1.TryResolveType("System.Console");
        var s2 = ctx2.TryResolveType("System.Console");
        Assert.NotNull(s1);
        Assert.NotNull(s2);
        Assert.Equal(s1!.ToDisplayString(), s2!.ToDisplayString());
    }

    // Test (iii) — D4 overload smoke probe: WriteLine(int) picks int overload.
    [Fact]
    public void TryResolveOverload_ConsoleWriteLine_Int_PicksIntOverload()
    {
        using var ctx = MetadataContext.Create();
        var console = ctx.TryResolveType("System.Console");
        Assert.NotNull(console);
        var intType = ctx.TryResolveType("System.Int32");
        Assert.NotNull(intType);

        var resolved = ctx.TryResolveOverload(
            console!, "WriteLine",
            new[] { new MetadataArgument(intType!) },
            out var reason);

        Assert.NotNull(resolved);
        Assert.Null(reason);
        Assert.Single(resolved!.Parameters);
        Assert.Equal("int", resolved.Parameters[0].Type.ToDisplayString());
    }

    [Fact]
    public void TryResolveOverload_ConsoleWriteLine_String_PicksStringOverload()
    {
        using var ctx = MetadataContext.Create();
        var console = ctx.TryResolveType("System.Console");
        Assert.NotNull(console);
        var stringType = ctx.TryResolveType("System.String");
        Assert.NotNull(stringType);

        var resolved = ctx.TryResolveOverload(
            console!, "WriteLine",
            new[] { new MetadataArgument(stringType!) }, out var reason);

        Assert.NotNull(resolved);
        Assert.Null(reason);
        Assert.Single(resolved!.Parameters);
        // .NET 10 annotates WriteLine's string parameter as string? (nullable).
        Assert.Equal("string?",
            resolved.Parameters[0].Type.ToDisplayString(
                SymbolDisplayFormat.MinimallyQualifiedFormat));
    }

    // Test (iv) — F-2 manifest drift is a hard failure with a specific message.
    // The discriminating pin: a manifest with a fake assembly must throw with
    // a message that names the missing assembly (never silent null).
    [Fact]
    public void CreateWithManifest_MissingAssembly_HardFailsWithSpecificMessage()
    {
        var badManifest = new MetadataReferenceManifest(
            SdkVersionRange: "10.0.0 - 10.9.999",
            GeneratedFrom: null,
            GeneratedAt: null,
            Assemblies: ImmutableArray.Create(
                new ManifestAssemblyEntry(
                    AssemblyName: "System.Fake.DoesNotExist",
                    Version: "10.0.0",
                    Sha256: new string('0', 64))));

        var ex = Assert.Throws<InvalidOperationException>(
            () => MetadataContext.CreateWithManifest(badManifest));
        Assert.Contains("Metadata manifest drift", ex.Message);
        Assert.Contains("System.Fake.DoesNotExist", ex.Message);
        Assert.Contains("SDK version", ex.Message);
    }

    // Test (iv cont'd) — SHA drift inside sdkVersionRange is intentionally
    // NOT a hard failure (scoping doc §D3: inside-range SHA drift is the
    // Info/auto-regen path; only outside-range framework version is fatal).
    // This test pins the leniency so a future revision that reintroduces
    // hard-fail-on-drift breaks CI on every SDK patch — which is exactly
    // the round-2-M4 rollForward-fragility bug this design mitigates.
    [Fact]
    public void CreateWithManifest_ShaDriftInsideRange_DoesNotThrow()
    {
        var driftedManifest = new MetadataReferenceManifest(
            SdkVersionRange: "10.0.0 - 10.9.999",
            GeneratedFrom: null,
            GeneratedAt: null,
            Assemblies: ImmutableArray.Create(
                new ManifestAssemblyEntry(
                    AssemblyName: "System.Runtime",
                    Version: "10.0.0",
                    Sha256: "deadbeef00000000000000000000000000000000000000000000000000000000")));

        using var ctx = MetadataContext.CreateWithManifest(driftedManifest);
        Assert.NotNull(ctx);
    }

    // Discriminating pin for §S1 F-2: reverting the mutation (using the real
    // manifest via .Create()) resolves cleanly — the pin fires only under
    // drift. Assert both directions in one test to make the pin bidirectional.
    [Fact]
    public void Create_WithRealManifest_ConstructsSuccessfully()
    {
        // No throw = success. The Create() call verifies every manifest entry
        // against TPA; if this ever throws, either the SDK moved or the
        // manifest is out of date — regenerate.
        using var ctx = MetadataContext.Create();
        Assert.NotNull(ctx);
        // Sanity: the context actually resolves a familiar type.
        Assert.NotNull(ctx.TryResolveType("System.Object"));
    }

    // Discriminating pin for D4 mechanism: arg-type projection must matter.
    // If we projected all args as System.Object, WriteLine(42) would pick
    // WriteLine(object) instead of WriteLine(int). This test asserts the
    // two overloads resolve distinctly.
    [Fact]
    public void TryResolveOverload_ArgTypeProjection_DistinguishesOverloads()
    {
        using var ctx = MetadataContext.Create();
        var console = ctx.TryResolveType("System.Console");
        var intType = ctx.TryResolveType("System.Int32");
        var objectType = ctx.TryResolveType("System.Object");
        Assert.NotNull(console);
        Assert.NotNull(intType);
        Assert.NotNull(objectType);

        var withInt = ctx.TryResolveOverload(
            console!, "WriteLine",
            new[] { new MetadataArgument(intType!) }, out _);
        var withObject = ctx.TryResolveOverload(
            console!, "WriteLine",
            new[] { new MetadataArgument(objectType!) }, out _);

        Assert.NotNull(withInt);
        Assert.NotNull(withObject);
        Assert.NotEqual(withInt!.ToDisplayString(), withObject!.ToDisplayString());
        Assert.Contains("int",
            withInt.Parameters[0].Type.ToDisplayString(
                SymbolDisplayFormat.MinimallyQualifiedFormat));
        Assert.Contains("object",
            withObject.Parameters[0].Type.ToDisplayString(
                SymbolDisplayFormat.MinimallyQualifiedFormat));
    }

    // Unresolved surface exercised: a nonsense method name gives a
    // non-null unresolvedReason and a null symbol.
    [Fact]
    public void TryResolveOverload_UnknownMethod_ReturnsNullWithReason()
    {
        using var ctx = MetadataContext.Create();
        var console = ctx.TryResolveType("System.Console");
        Assert.NotNull(console);

        var resolved = ctx.TryResolveOverload(
            console!, "NoSuchMethod_ZZZ",
            Array.Empty<MetadataArgument>(), out var reason);

        Assert.Null(resolved);
        Assert.NotNull(reason);
        Assert.Contains("NoSuchMethod_ZZZ", reason!);
    }

    [Fact]
    public void Dispose_MakesSubsequentCallsThrow()
    {
        var ctx = MetadataContext.Create();
        ctx.Dispose();
        Assert.Throws<ObjectDisposedException>(() => ctx.TryResolveType("System.Object"));
    }

    // Round-2 C1: SDK version range enforcement — outside range → hard failure.
    [Fact]
    public void CreateWithManifest_FrameworkOutsideRange_HardFails()
    {
        // Manifest declaring a range that cannot possibly include current .NET 10.x.
        var outOfRangeManifest = new MetadataReferenceManifest(
            SdkVersionRange: "6.0.0 - 6.0.999",
            GeneratedFrom: null,
            GeneratedAt: null,
            Assemblies: ImmutableArray.Create(
                new ManifestAssemblyEntry("System.Runtime", "6.0.0", new string('0', 64))));

        var ex = Assert.Throws<InvalidOperationException>(
            () => MetadataContext.CreateWithManifest(outOfRangeManifest));
        Assert.Contains("outside the manifest's sdkVersionRange", ex.Message);
    }

    // Round-2 C2: static-vs-instance dispatch — int.TryParse is a static method
    // on a value type. Without the C2 fix this would produce an instance-call
    // synthetic tree that Roslyn resolves incorrectly.
    [Fact]
    public void TryResolveOverload_IntTryParse_ResolvesStaticOnValueType()
    {
        using var ctx = MetadataContext.Create();
        var intType = ctx.TryResolveType("System.Int32");
        var stringType = ctx.TryResolveType("System.String");
        var boolType = ctx.TryResolveType("System.Boolean");
        Assert.NotNull(intType);
        Assert.NotNull(stringType);

        var resolved = ctx.TryResolveOverload(
            intType!,
            "TryParse",
            new[]
            {
                new MetadataArgument(stringType!),
                new MetadataArgument(intType!, RefKind.Out),
            },
            out var reason);

        Assert.NotNull(resolved);
        Assert.Null(reason);
        Assert.True(resolved!.IsStatic, "int.TryParse must resolve as a static method.");
        Assert.Equal("TryParse", resolved.Name);
    }

    // Round-2 M1: in-code allow-list backstops the manifest whitelist.
    // A tampered manifest listing a compiler-internal assembly must fail
    // even if the assembly is genuinely in TPA with a matching SHA.
    [Fact]
    public void CreateWithManifest_TamperedWithCompilerInternal_HardFails()
    {
        // Microsoft.CodeAnalysis IS in TPA; a name-only check would pass.
        // The in-code allow-list must reject it.
        var tamperedManifest = new MetadataReferenceManifest(
            SdkVersionRange: "10.0.0 - 10.0.999",
            GeneratedFrom: null,
            GeneratedAt: null,
            Assemblies: ImmutableArray.Create(
                new ManifestAssemblyEntry("Microsoft.CodeAnalysis", "5.3.0", new string('0', 64))));

        var ex = Assert.Throws<InvalidOperationException>(
            () => MetadataContext.CreateWithManifest(tamperedManifest));
        Assert.Contains("tampering detected", ex.Message);
        Assert.Contains("Microsoft.CodeAnalysis", ex.Message);
    }

    // Round-2 M3 (corrected): F-2's true discriminating pin — a manifest that
    // OMITS System.Runtime results in System.Object failing to resolve
    // through the filtered reference set. This is the "delete one manifest
    // entry, watch a real type fail" pin the scoping doc §F-2 calls for.
    [Fact]
    public void CreateWithManifest_MissingCoreEntry_DownstreamResolutionFails()
    {
        // Manifest with a minimal set that omits System.Runtime and System.Private.CoreLib.
        // System.Object lives in the runtime and cannot be resolved without them.
        var minimalManifest = new MetadataReferenceManifest(
            SdkVersionRange: "10.0.0 - 10.0.999",
            GeneratedFrom: null,
            GeneratedAt: null,
            Assemblies: ImmutableArray.Create(
                // Grab a real assembly (Microsoft.CSharp) with a real SHA so
                // the drift check passes, but omit System.Runtime.
                new ManifestAssemblyEntry(
                    "Microsoft.CSharp",
                    "10.0.0",
                    ManifestAssemblyEntry.ComputeSha256(
                        Calor.Compiler.CodeGen.GeneratedCSharpCompiler.References
                            .OfType<PortableExecutableReference>()
                            .First(r => Path.GetFileNameWithoutExtension(r.FilePath ?? "") == "Microsoft.CSharp")
                            .FilePath!))));

        using var ctx = MetadataContext.CreateWithManifest(minimalManifest);
        // Without System.Runtime, System.Object doesn't resolve.
        var objSymbol = ctx.TryResolveType("System.Object");
        Assert.Null(objSymbol);
    }

    // Round-2 M2: strict JSON validation — a manifest with case-typo property
    // name should fail during load (not silently default).
    [Fact]
    public void LoadFrom_MalformedManifest_TypoInPropertyName_Throws()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"bad-manifest-{Guid.NewGuid()}.json");
        try
        {
            // "sdkVersionRnge" (missing 'a') — should fail case-sensitively.
            File.WriteAllText(tempPath, "{ \"sdkVersionRnge\": \"10.0.0 - 10.0.999\", \"assemblies\": [] }");
            Assert.ThrowsAny<Exception>(() => MetadataReferenceManifest.LoadFrom(tempPath));
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    // Round-2 M2: empty assemblies array is a load-time failure with a clear message.
    [Fact]
    public void LoadFrom_ManifestWithEmptyAssemblies_ThrowsWithClearMessage()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"empty-manifest-{Guid.NewGuid()}.json");
        try
        {
            File.WriteAllText(tempPath, "{ \"sdkVersionRange\": \"10.0.0 - 10.0.999\", \"assemblies\": [] }");
            var ex = Assert.Throws<InvalidOperationException>(
                () => MetadataReferenceManifest.LoadFrom(tempPath));
            Assert.Contains("no 'assemblies' entries", ex.Message);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }
}

public class MetadataContextLifecycleTests
{
    // Round-2 M4 (corrected): 100 sequential construct/dispose cycles must
    // leave GC-managed memory within 20% of a warm baseline. Switched from
    // Environment.WorkingSet (a coarse OS-level number subject to noisy
    // parallel-test scheduling) to GC.GetTotalMemory(forceFullCollection:
    // true), which is deterministic and captures the actual .NET managed-
    // heap footprint. This is the exit-criterion threshold from the scoping
    // doc §S1 (v), not a relaxed one.
    [Fact]
    public void Create_100SequentialConstructions_ManagedMemoryStable()
    {
        // Warm the shared TPA reference set + one context worth of Roslyn state.
        using (var _ = MetadataContext.Create())
        {
            /* GC-visible warm */
        }
        var baseline = GC.GetTotalMemory(forceFullCollection: true);

        for (int i = 0; i < 100; i++)
        {
            using var ctx = MetadataContext.Create();
            _ = ctx.TryResolveType("System.String");
        }

        var after = GC.GetTotalMemory(forceFullCollection: true);
        var growth = (double)(after - baseline) / baseline;
        Assert.True(growth < 0.20,
            $"Managed memory grew {growth:P0} from baseline {baseline:N0} to {after:N0}. " +
            "MetadataContext lifecycle may be retaining references — inspect the " +
            "shared GeneratedCSharpCompiler.References cache and Compilation state.");
    }

    // Round-2 M3 mitigation: two concurrent constructions must not race on
    // GeneratedCSharpCompiler.References (which is Lazy<T> — the .NET Lazy
    // pattern is thread-safe by default, but the test guards against a
    // future change that would break that invariant).
    [Fact]
    public async Task Create_TwoConcurrentConstructions_DoNotRace()
    {
        var t1 = Task.Run(() =>
        {
            using var ctx = MetadataContext.Create();
            return ctx.TryResolveType("System.Console")?.ToDisplayString();
        });
        var t2 = Task.Run(() =>
        {
            using var ctx = MetadataContext.Create();
            return ctx.TryResolveType("System.Console")?.ToDisplayString();
        });
        var results = await Task.WhenAll(t1, t2);
        Assert.Equal("System.Console", results[0]);
        Assert.Equal("System.Console", results[1]);
    }
}
