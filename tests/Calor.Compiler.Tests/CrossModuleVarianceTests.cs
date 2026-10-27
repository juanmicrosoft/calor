using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class CrossModuleVarianceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "calor-variance-" + Guid.NewGuid().ToString("N"));

    public CrossModuleVarianceTests() => Directory.CreateDirectory(_directory);

    [Theory]
    [InlineData("", true)]
    [InlineData("cw", false)]
    public void OverrideUsesBaseDeclarationFromAnotherFile(string baseEffects, bool violation)
    {
        Write("base", Base(baseEffects));
        Write("derived", Derived);

        var (_, diagnostics) = Compile();

        Assert.Equal(violation,
            diagnostics.Any(d => d.Code == DiagnosticCode.OverrideEffectVariance));
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.EffectRowUnknown);
        Assert.Equal(violation, diagnostics.HasErrors);
    }

    [Fact]
    public void UnrelatedBareNameDoesNotSupplyAnExternalBasesContract()
    {
        Write("base", Base("cw").Replace("Shared", "Unrelated"));
        Write("derived", Derived);

        var (_, diagnostics) = Compile();

        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.EffectRowUnknown);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.OverrideEffectVariance);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InterfaceVarianceChecksDirectAndInheritedImplementations(bool inherited)
    {
        Write("interface", """
            §M{m1:Shared}
              §IFACE{i1:IQuiet}
                §MT{mt1:Render} () -> void
                  §E{}
            """);
        Write("implementation", """
            §M{m2:Shared}
              §CL{c1:Writer:pub}
                §MT{mt2:Render:pub} () -> void
                  §E{cw}
            """ + (inherited ? "" : "\n    §IMPL{IQuiet}\n"));
        if (inherited)
        {
            Write("derived", """
                §M{m3:Shared}
                  §CL{c2:Derived:Writer:pub}
                    §IMPL{IQuiet}
                """);
        }

        var (_, diagnostics) = Compile();

        var diagnostic = Assert.Single(diagnostics,
            d => d.Code == DiagnosticCode.InterfaceEffectVariance);
        Assert.Equal(Path.Combine(_directory, inherited ? "derived.calr" : "implementation.calr"),
            diagnostic.FilePath);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.EffectRowUnknown);
    }

    [Fact]
    public void WarmBuildRechecksVarianceAfterBaseContractChanges()
    {
        Write("base", Base("cw"));
        Write("derived", Derived);
        var (cold, coldDiagnostics) = Compile(cache: true);
        Assert.False(coldDiagnostics.HasErrors);
        Assert.Equal(2, cold.Compiled.Count);

        var (warm, warmDiagnostics) = Compile(cache: true);
        Assert.False(warmDiagnostics.HasErrors);
        Assert.Equal(2, warm.Skipped.Count);

        Write("base", Base(""));
        var (_, changedDiagnostics) = Compile(cache: true);
        Assert.Contains(changedDiagnostics, d => d.Code == DiagnosticCode.OverrideEffectVariance);
        Assert.DoesNotContain(changedDiagnostics, d => d.Code == DiagnosticCode.EffectRowUnknown);
    }

    [Theory]
    [InlineData("Contracts.Base", "")]
    [InlineData("global::Contracts.Base", "")]
    [InlineData("Base", "  §U{Contracts}\n")]
    [InlineData("Chosen.Base", "  §U{Chosen:Contracts}\n")]
    public void QualifiedImportedAndAliasedBasesDoNotUseUnrelatedNames(
        string reference, string imports)
    {
        Write("base", Base("").Replace("Shared", "Contracts"));
        Write("decoy", Base("cw").Replace("Shared", "Unrelated"));
        Write("derived", Derived.Replace("§M{m2:Shared}\n", "§M{m2:Consumer}\n" + imports)
            .Replace(":Derived:Base:pub", ":Derived:" + reference + ":pub"));

        var (_, diagnostics) = Compile();

        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.OverrideEffectVariance);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.EffectRowUnknown);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AmbiguousTypesRemainUnknown(bool duplicateIdentity)
    {
        Write("base", Base(""));
        Write("other", duplicateIdentity ? Base("cw") : Base("cw").Replace("Shared", "Other"));
        Write("derived", duplicateIdentity ? Derived
            : Derived.Replace("§M{m2:Shared}\n",
                "§M{m2:Consumer}\n  §U{Shared}\n  §U{Other}\n"));

        var (_, diagnostics) = Compile();

        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.EffectRowUnknown);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.OverrideEffectVariance);
    }

    [Fact]
    public void GenericBaseArgumentsAreSubstitutedBeforeMatchingMethods()
    {
        Write("base", """
            §M{m1:Shared}
              §CL{c1:Base:pub}<T>
                §MT{mt1:Render:pub:virt} (T:value) -> void
                  §E{}
            """);
        Write("derived", """
            §M{m2:Shared}
              §CL{c2:Derived:Base<i32>:pub}
                §MT{mt2:Render:pub:over} (i32:value) -> void
                  §E{cw}
            """);

        var (_, diagnostics) = Compile();

        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.OverrideEffectVariance);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.EffectRowUnknown);
    }

    [Fact]
    public void PartialBaseDeclarationsShareTheirMethods()
    {
        Write("base", Base("").Replace(":Base:pub}", ":Base:pub:partial}"));
        Write("other", """
            §M{m3:Shared}
              §CL{c3:Base:pub:partial}
            """);
        Write("derived", Derived);

        var (_, diagnostics) = Compile();

        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.OverrideEffectVariance);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.EffectRowUnknown);
    }

    [Fact]
    public void GenericInterfaceAndImplementationChainsPreserveTypeArguments()
    {
        Write("parent", """
            §M{m1:Shared}
              §IFACE{i1:IQuiet}<T>
                §MT{mt1:Render} (T:value) -> void
                  §E{}
            """);
        Write("child", """
            §M{m2:Shared}
              §IFACE{i2:IChild}<U>
                §EXT{IQuiet<U>}
            """);
        Write("base", """
            §M{m3:Shared}
              §CL{c1:Writer:pub}<V>
                §MT{mt2:Render:pub} (V:value) -> void
                  §E{cw}
            """);
        Write("derived", """
            §M{m4:Shared}
              §CL{c2:Derived:Writer<i32>:pub}
                §IMPL{IChild<i32>}
            """);

        var (_, diagnostics) = Compile();

        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.InterfaceEffectVariance);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.EffectRowUnknown);
    }

    [Fact]
    public void NamespaceImportsDoNotLeakBetweenSiblingDeclarations()
    {
        Write("base", Base("").Replace("Shared", "Contracts"));
        Write("other", Base("cw").Replace("Shared", "Other"));
        Write("derived", """
            §M{m2:Consumer}
              §NS{ns1:Consumer}
                §U{Contracts}
                §CL{c2:First:Base:pub}
                  §MT{mt2:Render:pub:over} () -> void
                    §E{cw}
              §NS{ns2:Consumer}
                §U{Other}
                §CL{c3:Second:Base:pub}
                  §MT{mt3:Render:pub:over} () -> void
                    §E{cw}
            """);

        var (_, diagnostics) = Compile();

        var violation = Assert.Single(diagnostics,
            d => d.Code == DiagnosticCode.OverrideEffectVariance);
        Assert.Contains("First.Render", violation.Message);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.EffectRowUnknown);
    }

    [Fact]
    public void ClosedGenericTypeAliasCarriesItsArguments()
    {
        Write("base", """
            §M{m1:Shared}
              §CL{c1:Base:pub}<T>
                §MT{mt1:Render:pub:virt} (T:value) -> void
                  §E{}
            """);
        Write("derived", """
            §M{m2:Consumer}
              §U{Chosen:Shared.Base<i32>}
              §CL{c2:Derived:Chosen:pub}
                §MT{mt2:Render:pub:over} (i32:value) -> void
                  §E{cw}
            """);

        var (_, diagnostics) = Compile();

        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.OverrideEffectVariance);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.EffectRowUnknown);
    }

    [Fact]
    public void SourceChangedAfterDeclarationCollectionFailsClosed()
    {
        Write("base", Base("cw"));
        Write("derived", Derived);

        Assert.Throws<IOException>(() => Compile(beforeCompile: file =>
        {
            if (file.Name == "base.calr")
                Write("base", Base(""));
        }));
        Assert.Empty(Directory.GetFiles(_directory, "*.g.cs"));
    }

    [Theory]
    [InlineData("e", false)]
    [InlineData("", true)]
    public void CrossFileEffectVariablesAreComparedByOrdinal(string baseRow, bool violation)
    {
        Write("base", $$"""
            §M{m1:Shared}
              §CL{c1:Base:pub}
                §MT{mt1:Render:pub:virt}<eff e> () -> void
                  §E{ {{baseRow}} }
            """);
        Write("derived", """
            §M{m2:Shared}
              §CL{c2:Derived:Base:pub}
                §MT{mt2:Render:pub:over}<eff renamed> () -> void
                  §E{renamed}
            """);

        var (_, diagnostics) = Compile();

        Assert.Equal(violation,
            diagnostics.Any(d => d.Code == DiagnosticCode.OverrideEffectVariance));
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.EffectRowUnknown);
        Assert.Equal(violation, diagnostics.HasErrors);
    }

    [Fact]
    public void AmbiguousInterfaceContractsRemainUnknown()
    {
        const string contract = """
            §M{m1:Shared}
              §IFACE{i1:IQuiet}
                §MT{mt1:Render} () -> void
                  §E{}
            """;
        Write("first", contract);
        Write("second", contract);
        Write("implementation", """
            §M{m2:Shared}
              §CL{c1:Writer:pub}
                §IMPL{IQuiet}
                §MT{mt2:Render:pub} () -> void
                  §E{cw}
            """);

        var (_, diagnostics) = Compile();

        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.EffectRowUnknown);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.InterfaceEffectVariance);
    }

    [Fact]
    public void NominalParameterTypesAreNotMatchedByTheirBareNames()
    {
        Write("base", Base("").Replace("() ->", "(Thing:value) ->")
            + "\n  §CL{c3:Thing:pub}\n");
        Write("derived", Derived.Replace("§M{m2:Shared}", "§M{m2:Other}")
            .Replace(":Derived:Base:pub", ":Derived:Shared.Base:pub")
            .Replace("() ->", "(Thing:value) ->") + "\n  §CL{c4:Thing:pub}\n");

        var (_, diagnostics) = Compile();

        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.EffectRowUnknown);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.OverrideEffectVariance);
    }

    [Fact]
    public void BodyOnlyEditsDoNotInvalidateUnchangedDerivedContracts()
    {
        Write("base", Base("cw"));
        Write("derived", Derived);
        Assert.False(Compile(cache: true).Diagnostics.HasErrors);
        Write("base", Base("cw") + "\n      §P \"body only\"\n");

        var (result, diagnostics) = Compile(cache: true);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal("base.calr", Assert.Single(result.Compiled).File.Name);
        Assert.Equal("derived.calr", Assert.Single(result.Skipped).Name);
    }

    private static string Base(string effects) => $$"""
        §M{m1:Shared}
          §CL{c1:Base:pub}
            §MT{mt1:Render:pub:virt} () -> void
              §E{ {{effects}} }
        """;

    private const string Derived = """
        §M{m2:Shared}
          §CL{c2:Derived:Base:pub}
            §MT{mt2:Render:pub:over} () -> void
              §E{cw}
        """;

    private void Write(string name, string source) =>
        File.WriteAllText(Path.Combine(_directory, name + ".calr"), source);

    private (CompilationDriver.DriverResult Result, DiagnosticBag Diagnostics) Compile(
        bool cache = false, Action<FileInfo>? beforeCompile = null)
    {
        var diagnostics = new DiagnosticBag();
        var sources = Directory.GetFiles(_directory, "*.calr")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new FileInfo(path)).ToList();
        var result = CompilationDriver.CompileAll(
            sources,
            file =>
            {
                beforeCompile?.Invoke(file);
                return new CompilationOptions
                {
                    EnforceEffects = true,
                    UnknownCallPolicy = UnknownCallPolicy.Strict,
                    UnsafeTranspileOnly = !cache
                };
            },
            crossModuleEnforcement: true,
            crossModulePolicy: UnknownCallPolicy.Strict,
            onCompiled: (file, compiled) =>
                File.WriteAllText(Path.ChangeExtension(file.FullName, ".g.cs"), compiled.GeneratedCode),
            diagnosticSink: diagnostics,
            cache: cache ? new CompilationDriver.DriverCacheSettings(
                _directory, "variance", false,
                file => Path.ChangeExtension(file.FullName, ".g.cs")) : null);
        return (result, diagnostics);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
