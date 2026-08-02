using Calor.Compiler.Effects;
using Calor.Compiler.Effects.Manifests;
using Xunit;

namespace Calor.ILAnalysis.Tests;

/// <summary>
/// D-W3.1/D-W3.2 (wedge plan v0.11): the calor-import engine over a real
/// assembly. Pins the import-honesty invariants that M-T1 formalizes:
/// every emitted entry carries provenance, derived entries are 'inferred',
/// synthesized contracts are 'assumed', nothing is ever 'verified', and
/// unresolved members never enter the manifest (an empty effect list would
/// mean pure — under-approximation).
/// </summary>
public class PackageManifestGeneratorTests
{
    private static readonly string TestAssemblyDir = Path.Combine(
        AppContext.BaseDirectory, "TestAssemblies");

    private static readonly string DataAccessDll = Path.Combine(TestAssemblyDir, "TestAssembly.DataAccess.dll");
    private static readonly string ScenariosDll = Path.Combine(TestAssemblyDir, "TestAssembly.Scenarios.dll");

    private static PackageImportReport GenerateReport(string dll, bool contracts = true)
    {
        var resolver = new EffectResolver();
        resolver.Initialize();
        return PackageManifestGenerator.Generate(
            [dll], [], resolver,
            library: Path.GetFileNameWithoutExtension(dll),
            synthesizeContracts: contracts);
    }

    // ------------------------------------------------------------------
    // Tier A: derived entries
    // ------------------------------------------------------------------

    [Fact]
    public void DataAccess_RepositorySave_DerivedWithDbWrite()
    {
        var report = GenerateReport(DataAccessDll);

        var save = report.Derived.FirstOrDefault(e =>
            e.Type == "TestAssembly.DataAccess.UserRepository" && e.Member == "Save");
        Assert.NotNull(save);
        Assert.Equal("il-analysis", save.Source);
        Assert.Contains("db:w", save.Effects);
    }

    [Fact]
    public void DataAccess_ServiceLayer_TransitiveEffectsDerived()
    {
        var report = GenerateReport(DataAccessDll);

        var createUser = report.Derived.FirstOrDefault(e =>
            e.Type == "TestAssembly.DataAccess.UserService" && e.Member == "CreateUser");
        Assert.NotNull(createUser);
        Assert.Contains("db:w", createUser.Effects);
    }

    [Fact]
    public void Manifest_ContainsOnlyDerivedEntries_WithInferredProvenance()
    {
        var report = GenerateReport(DataAccessDll);
        var manifest = report.Manifest;

        Assert.Equal("calor import", manifest.GeneratedBy);
        Assert.Equal(PackageManifestGenerator.DerivedConfidence, manifest.Confidence);
        Assert.NotNull(manifest.GeneratedAt);

        // Nothing calor import emits is ever 'verified' (PP-T1 / M-T1).
        Assert.NotEqual("verified", manifest.Confidence);

        // Every manifest member corresponds to a derived entry — unresolved
        // and curated members never enter the emitted manifest.
        var derivedKeys = report.Derived
            .Select(d => (d.Type, d.Member, d.Kind))
            .ToHashSet();
        foreach (var mapping in manifest.Mappings)
        {
            foreach (var method in mapping.Methods?.Keys ?? Enumerable.Empty<string>())
                Assert.Contains((mapping.Type, method, "method"), derivedKeys);
            foreach (var getter in mapping.Getters?.Keys ?? Enumerable.Empty<string>())
                Assert.Contains((mapping.Type, getter, "getter"), derivedKeys);
            foreach (var setter in mapping.Setters?.Keys ?? Enumerable.Empty<string>())
                Assert.Contains((mapping.Type, setter, "setter"), derivedKeys);
            if (mapping.Constructors is { Count: > 0 })
                Assert.Contains(derivedKeys, k => k.Type == mapping.Type && k.Kind == "constructor");
        }
    }

    // ------------------------------------------------------------------
    // Tier C: unresolved surfaced loudly, never silently pure
    // ------------------------------------------------------------------

    [Fact]
    public void InterfaceMethods_AreUnresolved_NotEmittedAsPure()
    {
        var report = GenerateReport(ScenariosDll);

        // IStore's methods have no body and no curated manifest entry —
        // they must land in the unresolved bucket with a reason.
        var storeMembers = report.Unresolved
            .Where(u => u.Type == "TestAssembly.Scenarios.IStore")
            .ToList();
        Assert.NotEmpty(storeMembers);
        Assert.All(storeMembers, u => Assert.False(string.IsNullOrWhiteSpace(u.Reason)));

        // And they never appear in the emitted manifest.
        Assert.DoesNotContain(report.Manifest.Mappings, m => m.Type == "TestAssembly.Scenarios.IStore");
    }

    [Fact]
    public void ClassificationBuckets_AreDisjointAndCoverSurface()
    {
        var report = GenerateReport(DataAccessDll);

        Assert.True(report.PublicMethodCount > 0);
        Assert.Equal(report.PublicMethodCount,
            report.Derived.Count + report.Curated.Count + report.Unresolved.Count);
    }

    // ------------------------------------------------------------------
    // D-W3.2: mechanical contract synthesis — assumed provenance only
    // ------------------------------------------------------------------

    [Fact]
    public void Synthesis_NonNullableReferenceParameter_YieldsAssumedNotNullFact()
    {
        var report = GenerateReport(DataAccessDll);

        // UserService.CreateUser(string name) under <Nullable>enable</Nullable>
        var fact = report.SynthesizedContracts.FirstOrDefault(c =>
            c.Type == "TestAssembly.DataAccess.UserService"
            && c.Method == "CreateUser"
            && c.Parameter == "name");
        Assert.NotNull(fact);
        Assert.Equal("requires-not-null", fact.Kind);
        Assert.Equal("(!= name null)", fact.Expression);
        Assert.Equal(PackageManifestGenerator.AssumedProvenance, fact.Provenance);
        Assert.Equal("nrt-metadata", fact.Source);
    }

    [Fact]
    public void Synthesis_NullableReferenceParameter_YieldsNoFact()
    {
        var report = GenerateReport(DataAccessDll);

        // No non-null fact may be synthesized for a T? parameter anywhere.
        Assert.DoesNotContain(report.SynthesizedContracts, c =>
            c.Kind == "requires-not-null" && c.Type.EndsWith("User") && c.Parameter == "id");
    }

    [Fact]
    public void Synthesis_EveryFact_CarriesAssumedProvenance_NeverVerifiedOrProven()
    {
        foreach (var dll in new[] { DataAccessDll, ScenariosDll })
        {
            var report = GenerateReport(dll);
            Assert.All(report.SynthesizedContracts, c =>
            {
                Assert.Equal(PackageManifestGenerator.AssumedProvenance, c.Provenance);
                Assert.NotEqual("verified", c.Provenance);
                Assert.NotEqual("proven", c.Provenance);
            });
        }
    }

    [Fact]
    public void Synthesis_Disabled_ProducesNoContracts()
    {
        var report = GenerateReport(DataAccessDll, contracts: false);
        Assert.Empty(report.SynthesizedContracts);
    }

    // ------------------------------------------------------------------
    // Missing input handled loudly
    // ------------------------------------------------------------------

    [Fact]
    public void MissingAssembly_ReportsLoadError()
    {
        var resolver = new EffectResolver();
        resolver.Initialize();
        var report = PackageManifestGenerator.Generate(
            [Path.Combine(TestAssemblyDir, "does-not-exist.dll")], [], resolver);

        Assert.NotEmpty(report.LoadErrors);
        Assert.Equal(0, report.PublicMethodCount);
        Assert.Empty(report.Manifest.Mappings);
    }
}
