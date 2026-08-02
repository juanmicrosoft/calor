using Calor.Compiler.CodeGen;
using Calor.Compiler.Effects;
using Calor.Compiler.Effects.Manifests;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.ILAnalysis.Tests;

/// <summary>
/// Pins for the adversarial-review C1 finding (PR #841): Tier A must never
/// emit derived (implicitly pure or effect-underapproximated) entries for
/// members whose call chains rest on unverified assumptions — callees missing
/// from the loaded assemblies, bodiless declarations (interface/abstract),
/// or delegate invocations. Such members are Tier-C unresolved with a reason
/// naming the assumption, and become derivable when the missing assemblies
/// are supplied via references.
/// </summary>
public class ImportHonestyC1Tests : IDisposable
{
    private readonly string _tempDir;

    public ImportHonestyC1Tests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "calor-c1-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { }
    }

    // ------------------------------------------------------------------
    // Fixture compilation
    // ------------------------------------------------------------------

    private string CompileFixture(string assemblyName, string source, params string[] referencePaths)
    {
        var references = new List<MetadataReference>(GeneratedCSharpCompiler.References);
        references.AddRange(referencePaths.Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)));

        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview))],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var path = Path.Combine(_tempDir, assemblyName + ".dll");
        var emit = compilation.Emit(path);
        Assert.True(emit.Success, string.Join("\n",
            emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return path;
    }

    private static PackageImportReport Generate(string dll, params string[] references)
    {
        var resolver = new EffectResolver();
        resolver.Initialize();
        return PackageManifestGenerator.Generate(
            [dll], references, resolver,
            library: Path.GetFileNameWithoutExtension(dll),
            synthesizeContracts: false);
    }

    // ------------------------------------------------------------------
    // Repro 1: caller of an interface method must not emit derived-pure
    // ------------------------------------------------------------------

    [Fact]
    public void InterfaceDispatchCaller_IsUnresolved_NotDerivedPure()
    {
        var dll = CompileFixture("C1.Widgets", @"
namespace Widgets
{
    public interface IWidget { void Ping(); }
    public static class Caller
    {
        public static void Poke(IWidget w) => w.Ping();
    }
}");
        var report = Generate(dll);

        var poke = report.Unresolved.FirstOrDefault(u =>
            u.Type == "Widgets.Caller" && u.Member == "Poke");
        Assert.NotNull(poke);
        Assert.Contains("assum", poke.Reason, StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(report.Derived, d => d.Type == "Widgets.Caller" && d.Member == "Poke");
        Assert.DoesNotContain(report.Manifest.Mappings, m => m.Type == "Widgets.Caller");
    }

    // ------------------------------------------------------------------
    // Repro 2: missing reference assembly → unresolved; with -r → derived
    // ------------------------------------------------------------------

    [Fact]
    public void MissingReference_IsUnresolved_AndDerivesWithReference()
    {
        var depB = CompileFixture("C1.DepB", @"
namespace DepB
{
    public static class Sink
    {
        public static void WriteLog(string s) => System.IO.File.AppendAllText(""log.txt"", s);
    }
}");
        var depA = CompileFixture("C1.DepA", @"
namespace DepA
{
    public static class Record
    {
        public static void Save(string s) => DepB.Sink.WriteLog(s);
    }
}", depB);

        // Without Dep.B loaded: Save must be unresolved and the reason must
        // name the assumed leaf — never an empty (pure) derived entry.
        var without = Generate(depA);
        var save = without.Unresolved.FirstOrDefault(u => u.Type == "DepA.Record" && u.Member == "Save");
        Assert.NotNull(save);
        Assert.Contains("DepB.Sink.WriteLog", save.Reason);
        Assert.DoesNotContain(without.Derived, d => d.Type == "DepA.Record" && d.Member == "Save");

        // With Dep.B supplied, the chain resolves to the manifest seed fs:w.
        var with = Generate(depA, depB);
        var derived = with.Derived.FirstOrDefault(d => d.Type == "DepA.Record" && d.Member == "Save");
        Assert.NotNull(derived);
        Assert.Contains("fs:w", derived.Effects);
    }

    // ------------------------------------------------------------------
    // Repro 3: delegate invocation is never derived-pure
    // ------------------------------------------------------------------

    [Fact]
    public void DelegateInvokingMethod_IsUnresolved_WithDelegateReason()
    {
        var dll = CompileFixture("C1.Runners", @"
namespace Runners
{
    public static class Runner
    {
        public static int Run(System.Func<int> f) => f();
    }
}");
        var report = Generate(dll);

        var run = report.Unresolved.FirstOrDefault(u => u.Type == "Runners.Runner" && u.Member == "Run");
        Assert.NotNull(run);
        Assert.Contains("delegate invocation", run.Reason);
        Assert.DoesNotContain(report.Derived, d => d.Type == "Runners.Runner" && d.Member == "Run");
    }

    // ------------------------------------------------------------------
    // Clean chains still derive (the fix must not nuke Tier A)
    // ------------------------------------------------------------------

    [Fact]
    public void FullyResolvedChain_StillDerives_WithEffects()
    {
        var dll = CompileFixture("C1.Clean", @"
namespace Clean
{
    public class Greeter
    {
        public void Greet(string name) => System.Console.WriteLine(name);
        public int Add(int a, int b) => a + b;
    }
}");
        var report = Generate(dll);

        var greet = report.Derived.FirstOrDefault(d => d.Type == "Clean.Greeter" && d.Member == "Greet");
        Assert.NotNull(greet);
        Assert.Contains("cw", greet.Effects);

        var add = report.Derived.FirstOrDefault(d => d.Type == "Clean.Greeter" && d.Member == "Add");
        Assert.NotNull(add);
        Assert.Empty(add.Effects);

        // The instance constructor chains only through the manifest-covered
        // System.Object..ctor — clean, so it derives.
        Assert.Contains(report.Derived, d => d.Type == "Clean.Greeter" && d.Kind == "constructor");
    }
}
