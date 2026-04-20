using Calor.Compiler.Analysis;
using Calor.Compiler.Ast;
using Calor.Compiler.Commands;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Effects.Manifests;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Enforcement.Tests;

/// <summary>
/// Tests for the calor effects suggest command logic:
/// ExternalCallCollector, internal call filtering, and manifest generation.
/// </summary>
public class EffectsSuggestTests
{
    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    // ========================================================================
    // ExternalCallCollector
    // ========================================================================

    [Fact]
    public void Collector_FindsExternalCalls()
    {
        var source = @"
§M{m001:Test}
§F{f001:DoWork:pub}
  §O{void}
  §C{Console.WriteLine} §A STR:""test"" §/C
  §C{MyService.Process} §/C
§/F{f001}
§/M{m001}
";
        var module = Parse(source);
        var calls = ExternalCallCollector.Collect(module);

        Assert.Contains(calls, c => c.TypeName == "System.Console" && c.MethodName == "WriteLine");
        Assert.Contains(calls, c => c.MethodName == "Process");
    }

    [Fact]
    public void Collector_DeduplicatesCalls()
    {
        var source = @"
§M{m001:Test}
§F{f001:DoWork:pub}
  §O{void}
  §C{Console.WriteLine} §A STR:""a"" §/C
  §C{Console.WriteLine} §A STR:""b"" §/C
  §C{Console.WriteLine} §A STR:""c"" §/C
§/F{f001}
§/M{m001}
";
        var module = Parse(source);
        var calls = ExternalCallCollector.Collect(module);

        Assert.Single(calls, c => c.TypeName == "System.Console" && c.MethodName == "WriteLine");
    }

    [Fact]
    public void Collector_ExpandsShortNames()
    {
        var source = @"
§M{m001:Test}
§F{f001:DoWork:pub}
  §O{void}
  §C{File.ReadAllText} §A STR:""test.txt"" §/C
  §C{HttpClient.GetAsync} §A STR:""url"" §/C
§/F{f001}
§/M{m001}
";
        var module = Parse(source);
        var calls = ExternalCallCollector.Collect(module);

        Assert.Contains(calls, c => c.TypeName == "System.IO.File" && c.MethodName == "ReadAllText");
        Assert.Contains(calls, c => c.TypeName == "System.Net.Http.HttpClient" && c.MethodName == "GetAsync");
    }

    [Fact]
    public void Collector_WalksClassMethods()
    {
        var source = @"
§M{m001:Test}
§CL{c001:MyClass:pub}
  §MT{mt001:DoWork:pub}
    §O{void}
    §C{ExternalService.Call} §/C
  §/MT{mt001}
§/CL{c001}
§/M{m001}
";
        var module = Parse(source);
        var calls = ExternalCallCollector.Collect(module);

        Assert.Contains(calls, c => c.MethodName == "Call");
    }

    [Fact]
    public void Collector_ResolvesVariableTypes()
    {
        var source = @"
§M{m001:Test}
§F{f001:DoWork:pub}
  §O{void}
  §B{r} §NEW{Random} §/NEW
  §C{r.Next} §/C
§/F{f001}
§/M{m001}
";
        var module = Parse(source);
        var calls = ExternalCallCollector.Collect(module);

        // r.Next should resolve to System.Random.Next via variable type map
        Assert.Contains(calls, c => c.TypeName == "System.Random" && c.MethodName == "Next");
    }

    [Fact]
    public void Collector_TagsConstructors()
    {
        var source = @"
§M{m001:Test}
§F{f001:DoWork:pub}
  §O{void}
  §B{x} §NEW{HttpClient} §/NEW
§/F{f001}
§/M{m001}
";
        var module = Parse(source);
        var calls = ExternalCallCollector.Collect(module);

        Assert.Contains(calls, c => c.TypeName == "System.Net.Http.HttpClient" && c.Kind == CallKind.Constructor);
    }

    // ========================================================================
    // Internal call filtering
    // ========================================================================

    [Fact]
    public void InternalFunction_NotIncludedWhenFiltered()
    {
        var source = @"
§M{m001:Test}
§F{f001:Helper:pub}
  §O{void}
  §C{Console.WriteLine} §A STR:""internal"" §/C
§/F{f001}
§F{f002:Main:pub}
  §O{void}
  §C{Helper}
  §/C
  §C{ExternalService.DoWork} §/C
§/F{f002}
§/M{m001}
";
        var module = Parse(source);
        var callGraph = CallGraphAnalysis.Build(module);
        var allCalls = ExternalCallCollector.Collect(module);

        var resolver = new EffectResolver();
        resolver.Initialize();

        var unresolvedExternal = allCalls
            .Where(c => !callGraph.FunctionNameToId.ContainsKey(c.MethodName))
            .Where(c => resolver.Resolve(c.TypeName, c.MethodName).Status == EffectResolutionStatus.Unknown)
            .ToList();

        // Console.WriteLine resolves from manifests → not in unresolved
        Assert.DoesNotContain(unresolvedExternal, c => c.TypeName == "System.Console");
        // ExternalService.DoWork is unknown → should be in unresolved
        Assert.Contains(unresolvedExternal, c => c.MethodName == "DoWork");
        // Helper is an internal function → should not be in unresolved
        Assert.DoesNotContain(unresolvedExternal, c => c.MethodName == "Helper");
    }

    // ========================================================================
    // Edge cases
    // ========================================================================

    [Fact]
    public void AllResolved_ProducesEmptyList()
    {
        var source = @"
§M{m001:Test}
§F{f001:DoWork:pub}
  §O{void}
  §C{Console.WriteLine} §A STR:""test"" §/C
  §C{Math.Abs} §A INT:42 §/C
§/F{f001}
§/M{m001}
";
        var module = Parse(source);
        var allCalls = ExternalCallCollector.Collect(module);

        var resolver = new EffectResolver();
        resolver.Initialize();

        var unresolved = allCalls
            .Where(c => resolver.Resolve(c.TypeName, c.MethodName).Status == EffectResolutionStatus.Unknown)
            .ToList();

        Assert.Empty(unresolved);
    }

    [Fact]
    public void NoExternalCalls_ProducesEmptyList()
    {
        var source = @"
§M{m001:Test}
§F{f001:Add:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §R (+ a b)
§/F{f001}
§/M{m001}
";
        var module = Parse(source);
        var calls = ExternalCallCollector.Collect(module);

        Assert.Empty(calls);
    }

    // ========================================================================
    // Merge mode
    // ========================================================================

    [Fact]
    public void Merge_PreservesExistingEffects()
    {
        // Existing manifest has OrderService.Save with db:w
        var existingJson = @"{
            ""version"": ""1.0"",
            ""description"": ""Production manifest"",
            ""confidence"": ""verified"",
            ""mappings"": [
                {
                    ""type"": ""OrderService"",
                    ""methods"": {
                        ""Save"": [""db:w""],
                        ""Delete"": [""db:w""]
                    }
                }
            ],
            ""namespaceDefaults"": {
                ""MyApp.Data"": [""db:rw""]
            }
        }";

        // Write to temp file
        var tempPath = Path.Combine(Path.GetTempPath(), $"calor-merge-test-{Guid.NewGuid()}.json");
        try
        {
            File.WriteAllText(tempPath, existingJson);

            // Build a suggested manifest that adds a new method to OrderService + a new type
            var suggested = new EffectManifest
            {
                Version = "1.0",
                Description = "Suggested",
                Confidence = "inferred",
                Mappings = new List<TypeMapping>
                {
                    new TypeMapping { Type = "OrderService", Methods = new Dictionary<string, List<string>> { ["Cancel"] = new() } },
                    new TypeMapping { Type = "CacheClient", Methods = new Dictionary<string, List<string>> { ["Get"] = new() } }
                }
            };

            var result = EffectsCommand.MergeManifest(tempPath, suggested);

            // Existing effects preserved
            var orderMapping = result.Mappings.First(m => m.Type == "OrderService");
            Assert.Equal(new List<string> { "db:w" }, orderMapping.Methods!["Save"]);
            Assert.Equal(new List<string> { "db:w" }, orderMapping.Methods!["Delete"]);

            // New method added
            Assert.True(orderMapping.Methods!.ContainsKey("Cancel"));
            Assert.Empty(orderMapping.Methods!["Cancel"]);

            // New type added
            Assert.Contains(result.Mappings, m => m.Type == "CacheClient");

            // Metadata preserved
            Assert.Equal("Production manifest", result.Description);
            Assert.Equal("verified", result.Confidence);

            // NamespaceDefaults preserved
            Assert.NotNull(result.NamespaceDefaults);
            Assert.True(result.NamespaceDefaults.ContainsKey("MyApp.Data"));
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void Merge_ExistingEmptyEffectsNotOverwritten()
    {
        // A method with explicitly empty [] (marked pure) should NOT be overwritten
        var existingJson = @"{
            ""version"": ""1.0"",
            ""mappings"": [
                {
                    ""type"": ""MyService"",
                    ""methods"": {
                        ""PureMethod"": [],
                        ""EffectfulMethod"": [""db:w""]
                    }
                }
            ]
        }";

        var tempPath = Path.Combine(Path.GetTempPath(), $"calor-merge-test-{Guid.NewGuid()}.json");
        try
        {
            File.WriteAllText(tempPath, existingJson);

            var suggested = new EffectManifest
            {
                Version = "1.0",
                Mappings = new List<TypeMapping>
                {
                    new TypeMapping { Type = "MyService", Methods = new Dictionary<string, List<string>>
                    {
                        ["PureMethod"] = new(),         // would overwrite if not careful
                        ["EffectfulMethod"] = new(),    // would overwrite if not careful
                        ["NewMethod"] = new()           // genuinely new
                    }}
                }
            };

            var result = EffectsCommand.MergeManifest(tempPath, suggested);

            var mapping = result.Mappings.First(m => m.Type == "MyService");

            // Existing empty [] preserved (not overwritten)
            Assert.Empty(mapping.Methods!["PureMethod"]);

            // Existing filled effects preserved
            Assert.Equal(new List<string> { "db:w" }, mapping.Methods!["EffectfulMethod"]);

            // New method added
            Assert.True(mapping.Methods!.ContainsKey("NewMethod"));
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void MergeDictionary_OnlyAddsNewKeys()
    {
        var existing = new Dictionary<string, List<string>>
        {
            ["Save"] = new List<string> { "db:w" },
            ["Pure"] = new List<string>()
        };

        var suggested = new Dictionary<string, List<string>>
        {
            ["Save"] = new List<string>(),       // existing — should NOT overwrite
            ["Pure"] = new List<string>(),        // existing — should NOT overwrite
            ["NewMethod"] = new List<string>()    // new — should add
        };

        EffectsCommand.MergeDictionary(existing, suggested);

        // Existing entries untouched
        Assert.Equal(new List<string> { "db:w" }, existing["Save"]);
        Assert.Empty(existing["Pure"]);

        // New entry added
        Assert.True(existing.ContainsKey("NewMethod"));
        Assert.Empty(existing["NewMethod"]);
    }
}
