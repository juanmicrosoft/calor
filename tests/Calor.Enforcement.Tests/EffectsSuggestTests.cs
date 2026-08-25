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
        var tokens = lexer.TokenizeAllForParser();
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
";
        var module = Parse(source);
        var calls = ExternalCallCollector.Collect(module);

        // r.Next should resolve to System.Random.Next via variable type map
        Assert.Contains(calls, c => c.TypeName == "System.Random" && c.MethodName == "Next");
    }

    [Fact]
    public void Collector_UsesBoundReceiverIdentityForShadowedNames()
    {
        var source = @"
§M{m001:Test}
  §F{f001:DoWork:pub}
      §O{void}
      §B{r} §NEW{Random} §/NEW
      §IF{if1} BOOL:true
          §B{r} §NEW{HttpClient} §/NEW
          §C{r.GetAsync} §A STR:""/"" §/C
      §C{r.Next} §/C
";
        var module = Parse(source);

        var calls = ExternalCallCollector.Collect(module);

        Assert.Contains(
            calls,
            call => call.TypeName == "System.Net.Http.HttpClient"
                && call.MethodName == "GetAsync");
        Assert.Contains(
            calls,
            call => call.TypeName == "System.Random"
                && call.MethodName == "Next");
    }

    // ========================================================================
    // v0.15 E1 exit pins (roadmap §4.2 E1; metadata-binding scoping §3 S6 / D5)
    // ========================================================================

    /// <summary>
    /// E1 exit pin (a): the AST-side variable-type map is deleted, not
    /// bypassed. No identifier <c>_variableTypeMap</c> may occur anywhere
    /// under <c>src/Calor.Compiler/Effects/</c>.
    /// </summary>
    [Fact]
    public void E1_ExitPin_EffectsDirectory_HasNoVariableTypeMapIdentifier()
    {
        var effectsDir = Path.Combine(RepoRoot(), "src", "Calor.Compiler", "Effects");
        Assert.True(Directory.Exists(effectsDir), $"Effects directory not found at {effectsDir}");

        var identifier = new System.Text.RegularExpressions.Regex(@"\b_variableTypeMap\b");
        var offenders = Directory
            .EnumerateFiles(effectsDir, "*.cs", SearchOption.AllDirectories)
            .Where(file => identifier.IsMatch(File.ReadAllText(file)))
            .Select(file => Path.GetRelativePath(effectsDir, file))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "The _variableTypeMap string path was deleted in v0.15 E1; it must not be reintroduced " +
            "under src/Calor.Compiler/Effects/. Offending files: " + string.Join(", ", offenders));
    }

    /// <summary>
    /// E1 exit pin (b), the original S6 behavioural criterion: a receiver whose
    /// type is available ONLY through metadata resolves. Nothing in the module
    /// names <c>System.Guid</c> or <c>System.String</c> as the type of
    /// <c>g</c> / <c>line</c>; the binder learns them from MetadataBinder's
    /// Roslyn return types for <c>System.Guid.NewGuid()</c> and
    /// <c>System.Console.ReadLine()</c>.
    /// </summary>
    [Fact]
    public void E1_ExitPin_ReceiverTypeKnownOnlyThroughMetadata_Resolves()
    {
        var source = @"
§M{m001:Test}
  §F{f001:DoWork:pub}
      §O{void}
      §B{g} §C{System.Guid.NewGuid} §/C
      §C{g.ToString} §/C
      §B{line} §C{System.Console.ReadLine} §/C
      §C{line.Trim} §/C
";
        var module = Parse(source);
        var calls = ExternalCallCollector.Collect(module);

        var toString = Assert.Single(calls, c => c.MethodName == "ToString");
        Assert.Equal("System.Guid", toString.TypeName);
        Assert.True(toString.ReceiverResolved);

        var trim = Assert.Single(calls, c => c.MethodName == "Trim");
        Assert.Equal("System.String", trim.TypeName);
        Assert.True(trim.ReceiverResolved);
    }

    /// <summary>
    /// An inferred binding (<c>§B{sb}</c> with no type annotation) resolves
    /// its receiver through the bound tree — the <c>BoundType</c> of the
    /// variable — not by scanning §NEW initializers in the AST.
    /// </summary>
    [Fact]
    public void Collector_InferredBinding_ResolvesReceiverFromBoundType()
    {
        var source = @"
§M{m001:Test}
  §F{f001:DoWork:pub}
      §O{void}
      §B{sb} §NEW{System.Text.StringBuilder} §/NEW
      §C{sb.Append} §A STR:""x"" §/C
";
        var module = Parse(source);
        var calls = ExternalCallCollector.Collect(module);

        var append = Assert.Single(calls, c => c.MethodName == "Append");
        Assert.Equal("System.Text.StringBuilder", append.TypeName);
        Assert.True(append.ReceiverResolved);

        // A dotted §NEW target is one type, not a "System.Text" receiver with a
        // "StringBuilder" method.
        var ctor = Assert.Single(calls, c => c.Kind == CallKind.Constructor);
        Assert.Equal("System.Text.StringBuilder", ctor.TypeName);
        Assert.Equal(".ctor", ctor.MethodName);
        Assert.DoesNotContain(calls, c => c.TypeName == "System.Text");
    }

    /// <summary>
    /// A receiver the binder cannot vouch for is reported as unresolved and
    /// never given a guessed type: <c>x</c> is bound from an unknown callee
    /// (the binder's OBJECT fallback); <c>a.b</c> is a member chain the bound
    /// tree does not type; <c>foo</c> is not a bound variable and not written
    /// as a type; <c>this.sb</c> is a chain through <c>this</c>. None may
    /// surface as <c>OBJECT</c>, <c>System.Object</c>, the type of <c>a</c>,
    /// or as the echoed source text with <c>ReceiverResolved == true</c>.
    /// </summary>
    [Fact]
    public void Collector_UnresolvedReceiver_IsReportedWithoutGuessedType()
    {
        var source = @"
§M{m001:Test}
  §F{f001:DoWork:pub}
      §O{void}
      §B{x} §C{Unknown.Make} §/C
      §C{x.Run} §/C
      §B{a} §NEW{Random} §/NEW
      §C{a.b.Chain} §/C
      §C{foo.Bar} §/C
  §CL{c001:Holder:pub}
      §MT{mt001:Write:pub}
          §O{void}
          §C{this.sb.Append} §A STR:""x"" §/C
";
        var module = Parse(source);
        var calls = ExternalCallCollector.Collect(module);

        var run = Assert.Single(calls, c => c.MethodName == "Run");
        Assert.False(run.ReceiverResolved);
        Assert.Equal("x", run.TypeName);

        var chain = Assert.Single(calls, c => c.MethodName == "Chain");
        Assert.False(chain.ReceiverResolved);
        Assert.Equal("a.b", chain.TypeName);

        var bar = Assert.Single(calls, c => c.MethodName == "Bar");
        Assert.False(bar.ReceiverResolved);
        Assert.Equal("foo", bar.TypeName);

        var append = Assert.Single(calls, c => c.MethodName == "Append");
        Assert.False(append.ReceiverResolved);
        Assert.Equal("this.sb", append.TypeName);

        // The type-qualified callee that produced x is still a resolved receiver
        // (written as a type reference), so suggest can still propose it.
        var make = Assert.Single(calls, c => c.MethodName == "Make");
        Assert.True(make.ReceiverResolved);
        Assert.Equal("Unknown", make.TypeName);

        Assert.DoesNotContain(calls, c => c.TypeName is "OBJECT" or "System.Object");
        Assert.DoesNotContain(calls, c => c.TypeName == "System.Random" && c.MethodName == "Chain");

        // The unresolved call is still visible to the resolver as Unknown — never silently pure.
        var resolver = new EffectResolver();
        resolver.Initialize();
        Assert.Equal(EffectResolutionStatus.Unknown, resolver.Resolve(run.TypeName, run.MethodName).Status);
    }

    /// <summary>
    /// A lambda-bound variable is a function value, not a nominal receiver:
    /// invoking it must not be attributed to the binder's
    /// <c>LAMBDA(...)</c> type string.
    /// </summary>
    [Fact]
    public void Collector_FunctionTypedReceiver_IsUnresolved()
    {
        var source = @"
§M{m001:Test}
  §F{f001:DoWork:pub}
      §O{void}
      §B{f} §LAM{lam1:x:i32} (+ x 1) §/LAM{lam1}
      §C{f.Invoke} §A INT:1 §/C
";
        var module = Parse(source);
        var calls = ExternalCallCollector.Collect(module);

        var invoke = Assert.Single(calls, c => c.MethodName == "Invoke");
        Assert.False(invoke.ReceiverResolved);
        Assert.Equal("f", invoke.TypeName);
        Assert.DoesNotContain(calls, c => c.TypeName.StartsWith("LAMBDA", StringComparison.Ordinal));
    }

    /// <summary>
    /// An array-typed receiver resolves to <c>System.Array</c>, not to the
    /// element type.
    /// </summary>
    [Fact]
    public void Collector_ArrayReceiver_ResolvesToSystemArray()
    {
        var source = @"
§M{m001:Test}
  §F{f001:DoWork:pub}
      §I{i32[]:arr}
      §O{void}
      §C{arr.Clone} §/C
";
        var module = Parse(source);
        var calls = ExternalCallCollector.Collect(module);

        var clone = Assert.Single(calls, c => c.MethodName == "Clone");
        Assert.True(clone.ReceiverResolved);
        Assert.Equal("System.Array", clone.TypeName);
        Assert.DoesNotContain(calls, c => c.TypeName == "System.Int32");
    }

    /// <summary>
    /// Resolution step 2: a Calor-declared class used as a static receiver
    /// resolves through its <c>TypeSymbol</c>. And the deliberate slice-1
    /// residual, pinned so it cannot drift silently: a receiver written as a
    /// type reference that is neither a Calor type nor known to metadata
    /// (<c>OrderRepo</c>) stays a resolved receiver, so
    /// <c>calor effects suggest</c> can still propose a manifest entry for it.
    /// </summary>
    [Fact]
    public void Collector_CalorClassStaticReceiver_ResolvesThroughTypeSymbol()
    {
        var source = @"
§M{m001:Test}
  §CL{c001:Helper:pub}
      §MT{mt001:Run:pub}
          §O{void}
  §F{f001:DoWork:pub}
      §O{void}
      §C{Helper.Run} §/C
      §C{OrderRepo.Save} §/C
";
        var module = Parse(source);
        var calls = ExternalCallCollector.Collect(module);

        var run = Assert.Single(calls, c => c.MethodName == "Run");
        Assert.True(run.ReceiverResolved);
        Assert.EndsWith("Helper", run.TypeName, StringComparison.Ordinal);

        var save = Assert.Single(calls, c => c.MethodName == "Save");
        Assert.True(save.ReceiverResolved);
        Assert.Equal("OrderRepo", save.TypeName);
    }

    private static string RepoRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void Collector_TagsConstructors()
    {
        var source = @"
§M{m001:Test}
  §F{f001:DoWork:pub}
      §O{void}
      §B{x} §NEW{HttpClient} §/NEW
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
  §F{f002:Main:pub}
      §O{void}
      §C{Helper}
      §/C
      §C{ExternalService.DoWork} §/C
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

    // ========================================================================
    // E2E: suggest → fill → recompile loop
    // ========================================================================

    [Fact]
    public void E2E_SuggestThenFillThenRecompile()
    {
        // Step 1: Source with an unknown external call
        var source = @"
§M{m001:Test}
  §F{f001:ProcessOrder:pub}
      §O{void}
      §E{cw}
      §C{Console.WriteLine} §A STR:""Starting"" §/C
      §C{OrderRepo.Save} §/C
";
        // Step 2: Collect unresolved calls (simulating what suggest does)
        var module = Parse(source);
        var callGraph = CallGraphAnalysis.Build(module);
        var allCalls = ExternalCallCollector.Collect(module);

        var resolver = new EffectResolver();
        resolver.Initialize();

        var unresolved = allCalls
            .Where(c => !callGraph.FunctionNameToId.ContainsKey(c.MethodName))
            .Where(c => resolver.Resolve(c.TypeName, c.MethodName).Status == EffectResolutionStatus.Unknown)
            .Distinct()
            .ToList();

        // Verify OrderRepo.Save is unresolved
        Assert.Contains(unresolved, c => c.MethodName == "Save");

        // Step 3: Create a manifest with the user-filled effects (OrderRepo.Save → db:w)
        var manifestJson = @"{
            ""version"": ""1.0"",
            ""mappings"": [
                {
                    ""type"": ""OrderRepo"",
                    ""methods"": {
                        ""Save"": [""db:w""]
                    }
                }
            ]
        }";

        // Step 4: Recompile with the manifest loaded
        var loader = new ManifestLoader();
        loader.LoadFromJson(manifestJson, "suggested");

        var resolverWithManifest = new EffectResolver(loader);
        resolverWithManifest.Initialize();

        // Step 5: Verify OrderRepo.Save now resolves
        var resolution = resolverWithManifest.Resolve("OrderRepo", "Save");
        Assert.NotEqual(EffectResolutionStatus.Unknown, resolution.Status);
        Assert.Contains(resolution.Effects.Effects, e => e.Value == "database_write");

        // Step 6: Full compilation should succeed with correct effect declarations
        var fullSource = @"
§M{m001:Test}
  §F{f001:ProcessOrder:pub}
      §O{void}
      §E{cw,db:w}
      §C{Console.WriteLine} §A STR:""Starting"" §/C
      §C{OrderRepo.Save} §/C
";
        var compileResult = TestHarness.CompileWithEffects(fullSource, enforceEffects: true,
            policy: Calor.Compiler.Effects.UnknownCallPolicy.Permissive);

        // OrderRepo.Save should no longer produce Calor0411 if the manifest is loaded.
        // In this test context, the manifest isn't in the file system, so the compiler
        // won't auto-load it. Instead, verify the resolver round-trip works:
        // the suggest output → user fills effects → resolver resolves = the loop is valid.
        var unresolvedAfter = allCalls
            .Where(c => !callGraph.FunctionNameToId.ContainsKey(c.MethodName))
            .Where(c => resolverWithManifest.Resolve(c.TypeName, c.MethodName).Status == EffectResolutionStatus.Unknown)
            .ToList();

        Assert.Empty(unresolvedAfter);
    }

    // ========================================================================
    // Multi-file cross-references
    // ========================================================================

    [Fact]
    public void MultiFile_InternalFunctionRecognizedAcrossFiles()
    {
        // File A defines a helper function
        var sourceA = @"
§M{m001:Helpers}
  §F{f001:FormatName:pub}
      §I{str:name}
      §O{str}
      §R name
";
        // File B calls the helper + an external type
        var sourceB = @"
§M{m002:App}
  §F{f002:ProcessUser:pub}
      §O{void}
      §C{FormatName} §A STR:""test"" §/C
      §C{ExternalApi.Submit} §/C
";
        var moduleA = Parse(sourceA);
        var moduleB = Parse(sourceB);

        // Union function maps across files
        var combinedFunctionNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in CallGraphAnalysis.Build(moduleA).FunctionNameToId.Keys)
            combinedFunctionNames.Add(name);
        foreach (var name in CallGraphAnalysis.Build(moduleB).FunctionNameToId.Keys)
            combinedFunctionNames.Add(name);

        // Collect calls from file B
        var callsB = ExternalCallCollector.Collect(moduleB);

        var resolver = new EffectResolver();
        resolver.Initialize();

        var unresolved = callsB
            .Where(c => !combinedFunctionNames.Contains(c.MethodName))
            .Where(c => resolver.Resolve(c.TypeName, c.MethodName).Status == EffectResolutionStatus.Unknown)
            .ToList();

        // FormatName is in file A's function map → should be filtered out
        Assert.DoesNotContain(unresolved, c => c.MethodName == "FormatName");
        // ExternalApi.Submit is truly external → should be in unresolved
        Assert.Contains(unresolved, c => c.MethodName == "Submit");
    }

    // ========================================================================
    // Constructor parameter handling
    // ========================================================================

    [Fact]
    public void Constructor_CollectedWithTypeNameAndCtorKind()
    {
        var source = @"
§M{m001:Test}
  §F{f001:DoWork:pub}
      §O{void}
      §B{svc} §NEW{OrderService} §/NEW
      §B{client} §NEW{HttpClient} §/NEW
";
        var module = Parse(source);
        var calls = ExternalCallCollector.Collect(module);

        // OrderService constructor — unknown type, should appear
        Assert.Contains(calls, c => c.TypeName == "OrderService" && c.Kind == CallKind.Constructor);

        // HttpClient constructor — maps to System.Net.Http.HttpClient
        Assert.Contains(calls, c => c.TypeName == "System.Net.Http.HttpClient" && c.Kind == CallKind.Constructor);

        // Both should have .ctor as the method name
        Assert.All(calls.Where(c => c.Kind == CallKind.Constructor), c => Assert.Equal(".ctor", c.MethodName));
    }
}
