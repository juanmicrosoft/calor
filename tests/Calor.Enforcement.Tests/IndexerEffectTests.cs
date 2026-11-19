using Calor.Compiler;
using Calor.Compiler.Diagnostics;
using Xunit;

namespace Calor.Enforcement.Tests;

/// <summary>
/// 0.30: an element access on anything but an array runs an indexer, and is charged that
/// indexer's accessor: the manifest contract of a selected metadata indexer, a source
/// class's own accessor, or <c>unknown</c> when nothing resolves. Before 0.30 no element
/// access charged its indexer, so <c>config["k"]</c> and a logging source indexer were pure.
/// </summary>
public class IndexerEffectTests
{
    private static CompilationResult Compile(string source, IReadOnlyList<string>? references = null) =>
        TestHarness.CompileWithEffects(source, enforceEffects: true,
            policy: Calor.Compiler.Effects.UnknownCallPolicy.Strict, references: references);

    private static string Describe(CompilationResult result) =>
        string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}"));

    private static string ConfigRead(string effects) => $$"""
        module Test Test block
          using Microsoft.Extensions.Configuration
          func Read (IConfiguration:config) → string?
            ! {{effects}}
            return config["k"]
        """;

    [Fact]
    public void ConfigurationIndexer_WithReference_IsEnvRead()
    {
        var declared = Compile(ConfigRead("env:r"), TestHarness.FrameworkReferences);
        Assert.False(declared.HasErrors, Describe(declared));
        Assert.DoesNotContain(declared.Diagnostics, d => d.Code == DiagnosticCode.UnknownExternalCall);

        var undeclared = Compile(ConfigRead(""), TestHarness.FrameworkReferences);
        var forbidden = Assert.Single(undeclared.Diagnostics.Errors, d => d.Code == DiagnosticCode.ForbiddenEffect);
        Assert.Contains("'env:r'", forbidden.Message);
    }

    [Fact]
    public void ConfigurationIndexer_WithoutReference_IsUnknown()
    {
        var result = Compile(ConfigRead("env:r"));
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.UnknownExternalCall);
        Assert.Contains(result.Diagnostics.Errors,
            d => d.Code == DiagnosticCode.ForbiddenEffect && d.Message.Contains("'unknown'"));
    }

    [Fact]
    public void ConfigurationIndexerStore_ChargesTheSetter()
    {
        // IConfiguration declares only a getter contract; the setter has none, so a store is unknown.
        var result = Compile("""
            module Test Test block
              using Microsoft.Extensions.Configuration
              func Write (IConfiguration:config) → void
                ! env:r, mut
                set config["k"] "v"
            """, TestHarness.FrameworkReferences);
        Assert.Contains(result.Diagnostics.Errors,
            d => d.Code == DiagnosticCode.ForbiddenEffect && d.Message.Contains("'unknown'"));
    }

    private static string SourceIndexer(string declared) => $$"""
        module Test Test block
          class Journal
            this[int:i] → int
              get
                ! cw
                Console.WriteLine("read")
                return i
          func Read (Journal:journal) → int
            ! {{declared}}
            return journal[1]
        """;

    [Fact]
    public void SourceIndexer_ChargesItsGetterRow()
    {
        var declared = Compile(SourceIndexer("cw"));
        Assert.False(declared.HasErrors, Describe(declared));

        var undeclared = Compile(SourceIndexer(""));
        Assert.Contains(undeclared.Diagnostics.Errors,
            d => d.Code == DiagnosticCode.ForbiddenEffect && d.Message.Contains("'cw'"));
    }

    [Fact]
    public void UnresolvedReceiver_IsUnknown()
    {
        var result = Compile("""
            module Test Test block
              func Read (Mystery:m) → int
                ! cw
                return m[0]
            """);
        Assert.Contains(result.Diagnostics.Errors,
            d => d.Code == DiagnosticCode.ForbiddenEffect && d.Message.Contains("'unknown'"));
    }

    [Fact]
    public void ArrayListDictionaryAndStringIndexers_KeepTheirContracts()
    {
        var result = Compile("""
            module Test Test block
              using System.Collections.Generic
              func Read (int[]:xs, List<int>:ys, Dictionary<string, int>:zs, string:s) → int
                ! mut
                set xs[0] 1
                set ys[0] 2
                set zs["a"] 3
                let char:c = s[0]
                return xs[0] + ys[0] + zs["a"]
            """);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.UnknownExternalCall);
        Assert.False(result.HasErrors, Describe(result));
    }
}
