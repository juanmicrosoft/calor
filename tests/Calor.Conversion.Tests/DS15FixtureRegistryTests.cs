using System.Text.Json;
using System.Text.Json.Nodes;
using Calor.Compiler;
using Calor.Compiler.Analysis;
using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;
using DiagnosticBag = Calor.Compiler.Diagnostics.DiagnosticBag;

namespace Calor.Conversion.Tests;

/// <summary>
/// PP-S4's fixture registry (A-1.5.7; roadmap v0.13 §2.5 gate 6). Every entry under
/// bench/phase0-agent-native/fixtures/d-s1.5/ must be structurally complete, its C# input
/// must convert to exactly the expected Calor, and both Calor files must compile.
/// Explicit current project projections compare ordered outputs and compile their C# together.
/// Current producer observations keep the file label distinct from the original CLR namespace.
/// Indeterminate (a fixture that will not build) counts as FAILING, per the registration —
/// which these tests implement by failing on any defect rather than skipping.
/// The registry is legitimately empty until the first SupportLevel promotion; the
/// enumeration test pins the location exists either way.
/// </summary>
public class DS15FixtureRegistryTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static readonly HashSet<string> CurrentExpectationFixtures =
        ["local-function", "conditional-expression-hoisting", "conditional-declaration", "string-interpolation-hole",
            "namespace-topology"];

    private static readonly HashSet<string> CurrentProducerFixtures =
        ["conditional-access-shape", "conditional-declaration", "string-interpolation-hole",
            "local-function", "conditional-expression-hoisting", "conditional-using", "char-literal-surrogate",
            "initializer-interop", "string-interpolation-formattable", "string-interpolation-to-string"];

    private static string CurrentManifestPath(string repoRoot, string name, string original) =>
        name == "conditional-declaration"
            ? Path.Combine(repoRoot, "tests/TestData/Records/conditional-declaration-current.json")
            : Calor.Compiler.Tests.ActiveSyntaxInputs.ForCurrentTypedInput(repoRoot, original,
                Calor.Compiler.Tests.ActiveSyntaxInputs.InputKind.Structured);

    private static string RegistryPath()
    {
        // Walk up from the test bin dir to the repo root (marker: Directory.Build.props).
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "Directory.Build.props")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return Path.Combine(dir!, "bench", "phase0-agent-native", "fixtures", "d-s1.5");
    }

    [Fact]
    public void Registry_ExistsAtRegisteredLocation()
    {
        var root = RegistryPath();
        Assert.True(Directory.Exists(root),
            $"A-1.5.7 registry location missing: {root}");
        Assert.True(File.Exists(Path.Combine(root, "README.md")),
            "Registry schema doc (README.md) missing");
    }

    [Theory]
    [InlineData("local-function")]
    [InlineData("conditional-expression-hoisting")]
    [InlineData("conditional-declaration")]
    public void CurrentExpectation_ChangesOnlyApprovedClassificationFields(string name)
    {
        var repoRoot = Path.GetFullPath(Path.Combine(RegistryPath(), "../../../.."));
        var original = $"bench/phase0-agent-native/fixtures/d-s1.5/{name}/fixture.json";
        var resolved = CurrentManifestPath(repoRoot, name, original);
        var historical = JsonNode.Parse(File.ReadAllText(Path.Combine(repoRoot, original)))!.AsObject();
        var current = JsonNode.Parse(File.ReadAllText(resolved))!.AsObject();
        Assert.False(historical.ContainsKey("currentObservation"));
        Assert.NotNull(current["currentObservation"]);
        foreach (var field in new[]
        {
            "lossKindCertifiedAbsent", "expectedLossKinds", "allowedLossKinds", "currentObservation"
        })
        {
            historical.Remove(field);
            current.Remove(field);
        }
        Assert.True(JsonNode.DeepEquals(historical, current),
            "Only explicit current classification fields may differ; fixture identity, runtime results and symbols stay frozen.");
    }

    [Fact]
    public void ConditionalFixture_PreservesOnlyTheSevenLazyUpdateSites()
    {
        var inputPath = Path.Combine(RegistryPath(), "conditional-expression-hoisting", "input.cs");
        Assert.True(File.Exists(inputPath), "Required original conditional fixture missing.");
        var source = File.ReadAllText(inputPath);
        var method = CSharpSyntaxTree.ParseText(source).GetRoot()
            .DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var updates = method.DescendantNodes().Where(node =>
            node.IsKind(SyntaxKind.PostIncrementExpression)
            || node.IsKind(SyntaxKind.PreIncrementExpression)).ToArray();
        Assert.Equal(new[] { 7, 11, 14, 18, 22, 22, 26 },
            updates.Select(node => node.GetLocation().GetLineSpan().StartLinePosition.Line + 1));
        Assert.Equal(new[] { "i++", "i++", "++i", "++i", "i++", "++i", "++i" },
            updates.Select(node => node.ToString()));
        var converted = new CSharpToCalorConverter(new ConversionOptions
        {
            Fidelity = ConversionFidelity.Lossy,
            ModuleName = "Fixture",
            GracefulFallback = true,
            AutoGenerateIds = true,
            PreprocessorMode = PreprocessorConversionMode.PreserveAllBranches
        }).Convert(source, "conditional-expression-hoisting.cs");
        Assert.True(converted.Success, string.Join("; ", converted.Issues.Select(issue => issue.Message)));
        var losses = converted.Losses.ToArray();
        output.WriteLine(JsonSerializer.Serialize(losses.Select(loss => new
        {
            loss.Feature, loss.Description, loss.Line
        })));
        // The retired seven-expression representation is now one complete member,
        // not seven native sites or a partially hoisted native method.
        var loss = Assert.Single(losses);
        Assert.Equal(ConversionLossKind.InteropPreserved, loss.Kind);
        Assert.Equal("conditional-expression-hoisting", loss.Feature);
        Assert.Equal(3, loss.Line);
        Assert.Equal("Method preserved as raw C#: public static int Run()\n"
            + "    {\n        int i = 0;\n        bool gate = false;\n ...", loss.Description);
        Assert.Equal(4, converted.NativeConversionCount);
        Assert.Empty(converted.Ast!.Functions);
        var containingClass = Assert.Single(converted.Ast.Classes);
        Assert.Empty(containingClass.Methods);
        var block = Assert.Single(containingClass.InteropBlocks);
        Assert.Equal(method.ToFullString(), block.CSharpCode);
        Assert.Equal("conditional-expression-hoisting", block.FeatureName);
        Assert.Equal("Unsupported Method: public static int Run()\n"
            + "    {\n        int i = 0;\n        bool gate = false;\n ...", block.Reason);
        Assert.Single(Calor.Compiler.Tests.RawFrameAssertions.GetFrames(converted.CalorSource));
        Assert.DoesNotContain("_arrayOperand", converted.CalorSource);
        var compiled = Program.Compile(converted.CalorSource!, "conditional-expression-hoisting.calr",
            new Calor.Compiler.CompilationOptions { EnforceEffects = false });
        Assert.False(compiled.HasErrors, string.Join("; ", compiled.Diagnostics.Errors));
        AssertValueTestPasses("conditional-expression-hoisting", compiled.GeneratedCode, "Run", 329,
            expectedOwner: "ConditionalExpressionFixture");
    }

    [Fact]
    public void InterpolationHole_CurrentObservationIsIndependentlyNative()
    {
        CurrentExpectation_ChangesOnlyApprovedClassificationFields("string-interpolation-hole");
        ValidateFixture(Path.Combine(RegistryPath(), "string-interpolation-hole"), "string-interpolation-hole");
        var repoRoot = Path.GetFullPath(Path.Combine(RegistryPath(), "../../../.."));
        var original = Path.Combine(RegistryPath(), "string-interpolation-hole", "test.calr");
        var current = File.ReadAllText(Calor.Compiler.Tests.ActiveSyntaxInputs.ForCurrentCompiler(repoRoot, original));
        Calor.Compiler.Tests.RawFrameAssertions.AssertFramed(current, present: false);
        var result = Program.Compile(current, "string-interpolation-hole-test.calr",
            new Calor.Compiler.CompilationOptions
            {
                EnforceEffects = false,
                UnknownCallPolicy = Calor.Compiler.Effects.UnknownCallPolicy.Permissive
            });
        Assert.False(result.HasErrors, string.Join("; ", result.Diagnostics.Errors));
        AssertNative(result.Ast!);
        var arrays = CSharpSyntaxTree.ParseText(result.GeneratedCode).GetRoot()
            .DescendantNodes().OfType<ElementAccessExpressionSyntax>().ToArray();
        Assert.Equal(2, arrays.Length);
        Assert.All(arrays, access => Assert.Equal("items", access.Expression.ToString()));
        Assert.Equal("i", arrays[0].ArgumentList.Arguments.Single().Expression.ToString());
        var index = arrays[1].ArgumentList.Arguments.Single().Expression;
        var addition = Assert.Single(index.DescendantNodesAndSelf().OfType<BinaryExpressionSyntax>());
        Assert.True(addition.IsKind(SyntaxKind.AddExpression));
        Assert.Equal("i", addition.Left.ToString());
        Assert.Equal("1", addition.Right.ToString());
        Assert.All(index.DescendantNodesAndSelf().OfType<CheckedExpressionSyntax>(),
            expression => Assert.True(expression.IsKind(SyntaxKind.UncheckedExpression)));
        AssertValueTestPasses("string-interpolation-hole", result.GeneratedCode, "Verify", 58,
            expectedOwner: "InterpolationHoleTest.InterpolationHoleTestModule");
    }

    [Fact]
    public void LocalFunctionValueFixture_RetainsForwardCallsRecursionAndContract()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(RegistryPath(), "../../../.."));
        var original = Path.Combine(RegistryPath(), "local-function", "test.calr");
        var current = File.ReadAllText(Calor.Compiler.Tests.ActiveSyntaxInputs.ForCurrentCompiler(repoRoot, original));
        var frame = Assert.IsType<RawCSharpFrame>(
            Assert.Single(Calor.Compiler.Tests.RawFrameAssertions.GetFrames(current)).Value);
        Assert.Equal("method", frame.FeatureName);
        // This is the original value fixture's emitted Run member from db19e25
        // (line directives disabled), preserved with its contract, not native credit.
        var method = Assert.IsType<MethodDeclarationSyntax>(SyntaxFactory.ParseMemberDeclaration(frame.Code));
        var locals = method.DescendantNodes().OfType<LocalFunctionStatementSyntax>().ToArray();
        Assert.Equal(new[] { "Add", "Square", "Fact" }, locals.Select(local => local.Identifier.ValueText));
        foreach (var local in locals)
        {
            var firstCall = method.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .First(call => call.Expression.ToString() == local.Identifier.ValueText);
            Assert.True(firstCall.SpanStart < local.SpanStart, "Local calls must still precede their declarations.");
        }
        Assert.Contains(locals[2].DescendantNodes().OfType<InvocationExpressionSyntax>(),
            call => call.Expression.ToString() == "Fact");
        var compiled = Program.Compile(current, "local-function-test.calr",
            new Calor.Compiler.CompilationOptions
            {
                EnforceEffects = false,
                UnknownCallPolicy = Calor.Compiler.Effects.UnknownCallPolicy.Permissive
            });
        Assert.False(compiled.HasErrors, string.Join("; ", compiled.Diagnostics.Errors));
        AssertValueTestPasses("local-function", compiled.GeneratedCode, "Run", 31620,
            expectedOwner: "Fixture.LocalFunctionFixture");
        var brokenContract = compiled.GeneratedCode.Replace(
            "__calorPostconditionResult0 == 31620", "__calorPostconditionResult0 == 31621",
            StringComparison.Ordinal);
        Assert.NotEqual(compiled.GeneratedCode, brokenContract);
        var failure = Assert.Throws<System.Reflection.TargetInvocationException>(() =>
            AssertValueTestPasses("local-function-contract", brokenContract, "Run", 31620));
        Assert.IsType<Calor.Runtime.ContractViolationException>(failure.InnerException);
    }

    [Fact]
    public void ExactComparison_DoesNotNormalizeCountedPayloadBytes()
    {
        const string payload = "public static int Run() => 329;\r\n";
        var frame = new RawCSharpFrame(payload, "conditional-expression-hoisting", "comparison control");
        var header = $"module Fixture\n  raw {frame.ReasonMarker} {System.Text.Encoding.UTF8.GetByteCount(payload)}\n";
        Assert.Equal(Normalize(header + payload), Normalize(header.Replace("\n", "\r\n", StringComparison.Ordinal) + payload));
        Assert.NotEqual(Normalize(header + payload),
            Normalize(header + payload.Replace("\r\n", "\n ", StringComparison.Ordinal)));
    }

    [Fact]
    public void NamespaceTopology_CurrentProjectPartsRetainBothClrOwners()
        => ValidateFixture(Path.Combine(RegistryPath(), "namespace-topology"), "namespace-topology");

    private void ValidateNamespaceTopology(string repoRoot, string inputPath, CSharpToCalorConverter converter)
    {
        const string name = "namespace-topology";
        var original = $"bench/phase0-agent-native/fixtures/d-s1.5/{name}/fixture.json";
        using var doc = JsonDocument.Parse(File.ReadAllText(CurrentManifestPath(repoRoot, name, original)));
        var historicalManifest = JsonNode.Parse(File.ReadAllText(Path.Combine(repoRoot, original)));
        var currentManifest = JsonNode.Parse(doc.RootElement.GetRawText())!.AsObject();
        Assert.True(currentManifest.Remove("currentObservation"));
        Assert.True(JsonNode.DeepEquals(historicalManifest, currentManifest),
            "Topology projection must retain every original field, including zero-Interop certification.");
        var observation = doc.RootElement.GetProperty("currentObservation");
        Assert.Equal("current-compiler-active-syntax", observation.GetProperty("inputContract").GetString());
        Assert.Equal("lossless-project", observation.GetProperty("conversionMode").GetString());
        Assert.False(string.IsNullOrWhiteSpace(observation.GetProperty("reason").GetString()));
        var parts = observation.GetProperty("outputs").EnumerateArray().ToArray();
        Assert.Equal(new[] { "namespace-topology.namespace1.calr", "namespace-topology.namespace2.calr" },
            parts.Select(part => part.GetProperty("fileName").GetString()));
        Assert.Equal(new[] { "Alpha", "Beta" }, parts.Select(part => part.GetProperty("namespace").GetString()));
        Assert.Equal(new[] { "Alpha.Marker", "Beta.Value" }, parts.Select(part => part.GetProperty("clrType").GetString()));
        var entryPoint = doc.RootElement.GetProperty("runtimeEntryPoint").GetString()!;
        var expected = doc.RootElement.GetProperty("runtimeExpectedInt").GetInt32();
        var source = File.ReadAllText(inputPath);
        var project = converter.ConvertProject(source, $"{name}.cs");
        Assert.True(project.Success,
            string.Join("; ", project.Outputs.SelectMany(part => part.Conversion.Issues).Select(issue => issue.Message)));
        Assert.True(project.IsSplit);
        Assert.Equal(parts.Select(part => part.GetProperty("fileName").GetString()),
            project.Outputs.Select(part => part.FileName));
        Assert.True(observation.GetProperty("fullyNative").GetBoolean());
        Assert.Equal(observation.GetProperty("nativeConversionCount").GetInt32(),
            project.Outputs.Sum(part => part.Conversion.NativeConversionCount));
        foreach (var (part, index) in project.Outputs.Select((part, index) => (part, index)))
        {
            var conversion = part.Conversion;
            Assert.Empty(conversion.Losses);
            Assert.Equal(0, conversion.InteropPreservationCount);
            Assert.Equal(ConversionRoundTrip.Verified, conversion.Context.RoundTrip);
            Assert.Equal(parts[index].GetProperty("nativeConversionCount").GetInt32(), conversion.NativeConversionCount);
            var issue = Assert.Single(conversion.Issues);
            Assert.Equal("namespace-topology", issue.Feature);
            Assert.Equal(observation.GetProperty("requiredIssueMessage").GetString(), issue.Message);
            Assert.NotNull(conversion.CalorSource);
            Assert.NotNull(conversion.Ast);
            AssertNative(conversion.Ast);
        }
        output.WriteLine(JsonSerializer.Serialize(new
        {
            fixture = name, conversionMode = "lossless-project", project.IsSplit,
            outputs = project.Outputs.Select(part => new
            {
                part.FileName, part.Conversion.NativeConversionCount,
                losses = part.Conversion.Losses,
                issues = part.Conversion.Issues.Select(issue => new { issue.Feature, issue.Message })
            })
        }));
        var originalAssembly = AssertValueTestPasses(name, new[] { source },
            entryPoint, expected, expectedOwner: "Beta.Value");
        foreach (var projection in new[] { "converted", "expectedPath", "testPath" })
        {
            var generated = new List<string>();
            for (var index = 0; index < parts.Length; index++)
            {
                var part = parts[index];
                var path = projection == "converted"
                    ? project.Outputs[index].FileName
                    : Calor.Compiler.Tests.ActiveSyntaxInputs.ForCurrentCompiler(repoRoot,
                        part.GetProperty(projection).GetString()!);
                var calor = projection == "converted"
                    ? project.Outputs[index].Conversion.CalorSource!
                    : File.ReadAllText(path);
                if (projection == "expectedPath")
                    Assert.Equal(Normalize(calor), Normalize(project.Outputs[index].Conversion.CalorSource!));
                Calor.Compiler.Tests.RawFrameAssertions.AssertFramed(calor, present: false);
                var result = Program.Compile(calor, Path.GetFileName(path),
                    new Calor.Compiler.CompilationOptions
                    {
                        EnforceEffects = false,
                        UnknownCallPolicy = Calor.Compiler.Effects.UnknownCallPolicy.Permissive
                    });
                Assert.False(result.HasErrors, string.Join("; ", result.Diagnostics.Errors));
                AssertNative(result.Ast!);
                Assert.Equal(part.GetProperty("namespace").GetString(),
                    Assert.Single(result.Ast!.NamespaceScopes).FullName);
                var scope = Assert.Single(CSharpSyntaxTree.ParseText(result.GeneratedCode).GetRoot()
                    .DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>());
                Assert.Equal(part.GetProperty("namespace").GetString(), scope.Name.ToString());
                generated.Add(result.GeneratedCode);
            }
            var assembly = AssertValueTestPasses(name, generated, entryPoint, expected, expectedOwner: "Beta.Value");
            Assert.Equal(Shape(originalAssembly), Shape(assembly));
        }

        static string[] Shape(System.Reflection.Assembly assembly) => assembly.GetTypes()
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .Select(type => $"{type.FullName}|{type.Attributes}|"
                + string.Join(";", type.GetMembers(System.Reflection.BindingFlags.DeclaredOnly
                    | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance)
                    .Select(member => $"{member.MemberType}:{member}").Order(StringComparer.Ordinal)))
            .ToArray();
    }

    [Fact]
    public void AllFixtures_AreCompleteConvertExactlyAndCompile()
    {
        // A single fact rather than a MemberData theory: the registry is legitimately
        // empty until the first SupportLevel promotion, and xUnit fails a theory with
        // zero data rows. Errors are COLLECTED across all fixtures (not fail-fast), so
        // one bad fixture does not hide the others' defects.
        var root = RegistryPath();
        if (!Directory.Exists(root)) return;

        var failures = new List<string>();
        foreach (var dir in Directory.GetDirectories(root))
        {
            try
            {
                ValidateFixture(dir, Path.GetFileName(dir));
            }
            catch (Exception ex)
            {
                failures.Add($"{Path.GetFileName(dir)}: {ex.Message}");
            }
        }
        Assert.True(failures.Count == 0,
            "Fixture defects (indeterminate = failing):\n" + string.Join("\n", failures));
    }

    private void ValidateFixture(string dir, string name)
    {

        // 1. Structure: all four schema components present.
        var manifestPath = Path.Combine(dir, "fixture.json");
        Assert.True(File.Exists(manifestPath), $"{name}: fixture.json missing");
        var inputPath = Path.Combine(dir, "input.cs");
        Assert.True(File.Exists(inputPath), $"{name}: input.cs missing");
        var originalExpectedPath = Path.Combine(dir, "expected.calr");
        Assert.True(File.Exists(originalExpectedPath), $"{name}: expected.calr missing");
        var originalTestPath = Path.Combine(dir, "test.calr");
        Assert.True(File.Exists(originalTestPath), $"{name}: test.calr missing");
        var repoRoot = Path.GetFullPath(Path.Combine(RegistryPath(), "../../../.."));
        var expectedPath = Calor.Compiler.Tests.ActiveSyntaxInputs.ForCurrentCompiler(repoRoot, originalExpectedPath);
        if (name == "conditional-declaration")
            expectedPath = Path.Combine(repoRoot, "tests/TestData/Records/conditional-declaration-current.calr.txt");
        if (CurrentProducerFixtures.Contains(name))
        {
            var historical = Normalize(File.ReadAllText(expectedPath));
            expectedPath = Path.Combine(repoRoot, "tests/TestData/DS15", $"{name}-current.calr.txt");
            var current = Normalize(File.ReadAllText(expectedPath));
            const string historicalHeader = "module Fixture Fixture overflow unchecked\n";
            const string currentHeader = "module Fixture overflow unchecked\n";
            Assert.StartsWith(historicalHeader, historical);
            Assert.StartsWith(currentHeader, current);
            Assert.Equal(historical[historicalHeader.Length..], current[currentHeader.Length..]);
        }
        var testPath = Calor.Compiler.Tests.ActiveSyntaxInputs.ForCurrentCompiler(repoRoot, originalTestPath);

        // 2. Manifest: featureKey named; the DIRECTORY is the sanitized form of the key
        // (keys may contain spaces/parens/slashes — see README) and must match it.
        var relativeManifest = Path.GetRelativePath(repoRoot, manifestPath).Replace('\\', '/');
        var resolvedManifest = CurrentExpectationFixtures.Contains(name)
            ? CurrentManifestPath(repoRoot, name, relativeManifest)
            : manifestPath;
        using var doc = JsonDocument.Parse(File.ReadAllText(resolvedManifest));
        output.WriteLine(JsonSerializer.Serialize(new
        {
            fixture = name,
            inputContract = "current-compiler-active-syntax",
            originalExpectationPath = relativeManifest,
            resolvedExpectationPath = Path.GetRelativePath(repoRoot, resolvedManifest).Replace('\\', '/'),
            csharpPath = Path.GetRelativePath(repoRoot, inputPath).Replace('\\', '/'),
            originalCalorPath = Path.GetRelativePath(repoRoot, originalExpectedPath).Replace('\\', '/'),
            resolvedCalorPath = Path.GetRelativePath(repoRoot, expectedPath).Replace('\\', '/'),
            originalTestPath = Path.GetRelativePath(repoRoot, originalTestPath).Replace('\\', '/'),
            resolvedTestPath = Path.GetRelativePath(repoRoot, testPath).Replace('\\', '/')
        }));
        var featureKey = doc.RootElement.GetProperty("featureKey").GetString();
        Assert.False(string.IsNullOrWhiteSpace(featureKey), $"{name}: featureKey empty");
        Assert.Equal(SanitizeKey(featureKey!), name);
        var certifiedAbsent = doc.RootElement
            .GetProperty("lossKindCertifiedAbsent");
        var runtimeEntryPoint = doc.RootElement
            .GetProperty("runtimeEntryPoint")
            .GetString();
        Assert.False(
            string.IsNullOrWhiteSpace(runtimeEntryPoint),
            $"{name}: runtimeEntryPoint empty");
        var runtimeExpectedInt = doc.RootElement
            .GetProperty("runtimeExpectedInt")
            .GetInt32();
        var support = FeatureSupport.GetSupportLevel(featureKey!);
        var declaredSupport = doc.RootElement.TryGetProperty(
                "supportLevel",
                out var supportElement)
            ? Enum.Parse<SupportLevel>(
                supportElement.GetString()!,
                ignoreCase: true)
            : support;
        Assert.Equal(support, declaredSupport);
        var expectedLosses = ReadLossKinds(
            doc.RootElement,
            "expectedLossKinds");
        var allowedLosses = ReadLossKinds(
            doc.RootElement,
            "allowedLossKinds");

        // 3. Compare the single output or the explicit current project projection.
        var converter = new CSharpToCalorConverter(new ConversionOptions
        {
            Fidelity = name == "namespace-topology" ? ConversionFidelity.Lossless : ConversionFidelity.Lossy,
            ModuleName = "Fixture",
            GracefulFallback = true,
            AutoGenerateIds = true,
            PreprocessorMode =
                PreprocessorConversionMode.PreserveAllBranches
        });
        if (name == "namespace-topology")
        {
            ValidateNamespaceTopology(repoRoot, inputPath, converter);
            return;
        }
        var converted = converter.Convert(File.ReadAllText(inputPath), $"{name}.cs");
        Assert.True(
            converted.Success,
            $"{name}: conversion failed: "
            + string.Join("; ", converted.Issues.Select(issue => issue.Message)));
        Assert.NotNull(converted.CalorSource);
        if (CurrentExpectationFixtures.Contains(name))
        {
            var observation = doc.RootElement.GetProperty("currentObservation");
            Assert.Equal("current-compiler-active-syntax", observation.GetProperty("inputContract").GetString());
            Assert.False(string.IsNullOrWhiteSpace(observation.GetProperty("reason").GetString()));
            Assert.Equal(observation.GetProperty("nativeConversionCount").GetInt32(), converted.NativeConversionCount);
            Assert.Equal(observation.GetProperty("fullyNative").GetBoolean(), converted.Losses.Count == 0);
            var expectedInterop = observation.GetProperty("interopFeatureCounts").EnumerateObject()
                .ToDictionary(property => property.Name, property => property.Value.GetInt32());
            var actualInterop = converted.Losses.Where(loss => loss.Kind == ConversionLossKind.InteropPreserved)
                .GroupBy(loss => loss.Feature).ToDictionary(group => group.Key, group => group.Count());
            Assert.Equal(expectedInterop.OrderBy(pair => pair.Key), actualInterop.OrderBy(pair => pair.Key));
            output.WriteLine(JsonSerializer.Serialize(new
            {
                fixture = name,
                fullyNative = converted.Losses.Count == 0,
                converted.NativeConversionCount,
                interopFeatureCounts = actualInterop,
                issues = converted.Issues.Select(issue => new { issue.Feature, issue.Message })
            }));
            Assert.Equal(observation.GetProperty("requiredIssueFeatures").EnumerateArray()
                .Select(feature => feature.GetString()).Order(),
                converted.Issues.Select(issue => issue.Feature).Distinct().Order());
            if (name == "string-interpolation-hole")
            {
                var issue = Assert.Single(converted.Issues);
                Assert.Equal("nullable-oblivious-nested-shape", issue.Feature);
                Assert.Equal("Nested oblivious reference semantics for 'int[]' are not modeled; preserving syntax-only type mapping.",
                    issue.Message);
            }
        }
        var originalOwner = CurrentProducerFixtures.Contains(name) ? ExpectedOwner(name, valueFixture: false) : null;
        if (name == "conditional-declaration")
        {
            AssertValueTestPasses(name, File.ReadAllText(inputPath), runtimeEntryPoint!,
                doc.RootElement.GetProperty("convertedExpectedUndefinedInt").GetInt32(), [], originalOwner);
            AssertValueTestPasses(name, File.ReadAllText(inputPath), runtimeEntryPoint!,
                doc.RootElement.GetProperty("convertedExpectedDefinedInt").GetInt32(), ["FEATURE"], originalOwner);
        }
        else
        {
            AssertValueTestPasses(name, File.ReadAllText(inputPath), runtimeEntryPoint!, runtimeExpectedInt,
                expectedOwner: originalOwner);
            if (name == "conditional-using")
                AssertValueTestPasses(name, File.ReadAllText(inputPath), runtimeEntryPoint!, 41, ["FEATURE"], originalOwner);
        }
        if (certifiedAbsent.ValueKind != JsonValueKind.Null)
        {
            var lossName = certifiedAbsent.GetString();
            Assert.True(
                Enum.TryParse<ConversionLossKind>(lossName, out var lossKind),
                $"{name}: unknown loss kind '{lossName}'");
            Assert.DoesNotContain(
                converted.Losses,
                loss => loss.Kind == lossKind);
        }
        var actualLosses = converted.Losses
            .Select(loss => loss.Kind)
            .Distinct()
            .ToHashSet();
        if (support == SupportLevel.Full)
        {
            Assert.Empty(
                converted.Losses);
        }
        else
        {
            if (expectedLosses.Count == 0
                && allowedLosses.Count == 0)
            {
                Assert.NotEqual(
                    JsonValueKind.Null,
                    certifiedAbsent.ValueKind);
                Assert.Empty(converted.Losses);
            }
            else
            {
                Assert.NotEmpty(
                    expectedLosses);
                Assert.NotEmpty(
                    allowedLosses);
                Assert.Subset(
                    allowedLosses,
                    actualLosses);
                Assert.Subset(
                    actualLosses,
                    expectedLosses);
            }
            Assert.DoesNotContain(
                converted.Losses,
                loss => loss.Kind is
                    ConversionLossKind.Dropped
                    or ConversionLossKind.FallbackTodo
                    or ConversionLossKind.PreprocessorStripped
                    or ConversionLossKind.DirectiveRemoved);
        }
        Assert.Equal(
            Normalize(File.ReadAllText(expectedPath)),
            Normalize(converted.CalorSource!));
        if (doc.RootElement.TryGetProperty(
                "convertedRuntimeEntryPoint",
                out var convertedEntry)
            && doc.RootElement.TryGetProperty(
                "convertedExpectedUndefinedInt",
                out var undefinedExpected)
            && doc.RootElement.TryGetProperty(
                "convertedExpectedDefinedInt",
                out var definedExpected))
        {
            var compilation = Program.Compile(
                converted.CalorSource!,
                $"{name}.calr",
                new Calor.Compiler.CompilationOptions
                {
                    DeferGeneratedOutputValidation = true,
                    EnforceEffects = false
                });
            Assert.False(
                compilation.HasErrors,
                string.Join("; ", compilation.Diagnostics
                    .Where(diagnostic => diagnostic.IsError)
                    .Select(diagnostic => diagnostic.Message)));
            var symbols = doc.RootElement.TryGetProperty(
                    "definedSymbols",
                    out var symbolArray)
                ? symbolArray.EnumerateArray()
                    .Select(symbol => symbol.GetString()!)
                    .ToArray()
                : Array.Empty<string>();
            AssertValueTestPasses(
                name,
                compilation.GeneratedCode,
                convertedEntry.GetString()!,
                undefinedExpected.GetInt32(),
                [],
                ExpectedOwner(name, valueFixture: false));
            AssertValueTestPasses(
                name,
                compilation.GeneratedCode,
                convertedEntry.GetString()!,
                definedExpected.GetInt32(),
                symbols,
                ExpectedOwner(name, valueFixture: false));
        }

        // 4. Both Calor files compile with zero errors (indeterminate = failing).
        foreach (var calr in new[] { expectedPath, testPath })
        {
            var result = Program.Compile(File.ReadAllText(calr),
                Path.GetFileName(calr),
                new Calor.Compiler.CompilationOptions
                {
                    EnforceEffects = false,
                    UnknownCallPolicy =
                        Calor.Compiler.Effects.UnknownCallPolicy.Permissive
                });
            Assert.False(result.HasErrors,
                $"{name}/{Path.GetFileName(calr)} does not compile: " +
                string.Join("; ", result.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));
            if (calr == expectedPath && converted.Losses.Count == 0)
                AssertNative(result.Ast!);
            if (calr == testPath)
            {
                if (name != "local-function")
                    AssertNative(result.Ast!);
                AssertValueTestPasses(
                    name,
                    result.GeneratedCode,
                    runtimeEntryPoint!,
                    runtimeExpectedInt,
                    expectedOwner: ExpectedOwner(name, valueFixture: true));
                if (name == "conditional-declaration")
                    AssertValueTestPasses(name, result.GeneratedCode, runtimeEntryPoint!, runtimeExpectedInt,
                        ["FEATURE"], ExpectedOwner(name, valueFixture: true));
            }
            else if (doc.RootElement.TryGetProperty("convertedExpectedUndefinedInt", out var expectedValue))
            {
                AssertValueTestPasses(name, result.GeneratedCode, runtimeEntryPoint!, expectedValue.GetInt32(),
                    expectedOwner: ExpectedOwner(name, valueFixture: false));
                var symbols = doc.RootElement.TryGetProperty("definedSymbols", out var symbolArray)
                    ? symbolArray.EnumerateArray().Select(symbol => symbol.GetString()!).ToArray() : [];
                AssertValueTestPasses(name, result.GeneratedCode, runtimeEntryPoint!,
                    doc.RootElement.GetProperty("convertedExpectedDefinedInt").GetInt32(), symbols,
                    ExpectedOwner(name, valueFixture: false));
            }
            else
            {
                AssertValueTestPasses(name, result.GeneratedCode, runtimeEntryPoint!, runtimeExpectedInt,
                    expectedOwner: ExpectedOwner(name, valueFixture: false));
            }
            if (name == "conditional-using")
                AssertValueTestPasses(name, result.GeneratedCode, runtimeEntryPoint!, 41, ["FEATURE"],
                    ExpectedOwner(name, valueFixture: calr == testPath));
        }
    }

    private static void AssertNative(AstNode node)
    {
        Assert.False(node is CSharpInteropBlockNode or RawCSharpNode or RawCSharpExpressionNode
            or FallbackExpressionNode or FallbackCommentNode, $"Non-native value fixture node: {node.GetType().Name}");
        foreach (var child in RecursiveAstWalker.GetAllChildren(node))
            AssertNative(child);
    }

    private static string ExpectedOwner(string name, bool valueFixture) => name switch
    {
        "namespace-single-scope" => "SingleScope.Value",
        "namespace-topology" => "Beta.Value",
        "char-literal-surrogate" => valueFixture
            ? "SurrogateValueTest.SurrogateValueTestModule" : "SurrogateValues",
        "conditional-access-shape" => valueFixture
            ? "Fixture.ConditionalAccessFixture" : "ConditionalAccessFixture",
        "conditional-declaration" => valueFixture
            ? "ConditionalDeclarationFixture.FixtureOracle" : "ConditionalHarness",
        "conditional-expression-hoisting" => valueFixture
            ? "Fixture.ConditionalExpressionFixture" : "ConditionalExpressionFixture",
        "conditional-using" => valueFixture ? "FixtureTest.ConditionalValue" : "ConditionalValue",
        "initializer-interop" => valueFixture
            ? "InitializerInteropTest.InitializerInteropTest" : "InitializerInterop",
        "local-function" => valueFixture ? "Fixture.LocalFunctionFixture" : "LocalFunctionFixture",
        "string-interpolation-formattable" => valueFixture
            ? "InterpolationFormattableTest.FormattableProbe" : "InterpolationFormattable",
        "string-interpolation-hole" => valueFixture
            ? "InterpolationHoleTest.InterpolationHoleTestModule" : "InterpolationHole",
        "string-interpolation-to-string" => valueFixture
            ? "InterpolationToStringTest.InterpolationToStringTestModule" : "InterpolationToString",
        "record" => valueFixture ? "RecordTest.RecordFixture" : "RecordFixture",
        "checked-expression" => valueFixture ? "CheckedExpressionTest.CheckedFixture" : "CheckedFixture",
        "unchecked-expression" => valueFixture ? "UncheckedExpressionTest.UncheckedFixture" : "UncheckedFixture",
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unregistered fixture owner.")
    };

    private static void AssertValueTestPasses(
        string name,
        string generatedCode,
        string entryPoint,
        int expected,
        IReadOnlyList<string>? symbols = null,
        string? expectedOwner = null)
        => AssertValueTestPasses(name, new[] { generatedCode }, entryPoint, expected, symbols, expectedOwner);

    private static System.Reflection.Assembly AssertValueTestPasses(
        string name,
        IReadOnlyList<string> generatedCode,
        string entryPoint,
        int expected,
        IReadOnlyList<string>? symbols = null,
        string? expectedOwner = null)
    {
        var trees = generatedCode.Select(code => CSharpSyntaxTree.ParseText(
            code,
            new CSharpParseOptions(
                LanguageVersion.Latest,
                preprocessorSymbols: symbols ?? []))).ToArray();
        var compilation = CSharpCompilation.Create(
            $"DS15_{name}_{Guid.NewGuid():N}",
            trees,
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(
            emit.Success,
            $"{name}/test.calr generated C# failed: "
            + string.Join("; ", emit.Diagnostics
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Select(diagnostic => diagnostic.ToString())));

        var assembly = System.Reflection.Assembly.Load(stream.ToArray());
        var methods = assembly.GetTypes()
            .SelectMany(type => type.GetMethods(
                System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Static))
            .Where(method =>
                method.GetParameters().Length == 0
                && !method.IsSpecialName)
            .ToArray();
        foreach (var method in methods)
            _ = method.Invoke(null, null);
        var entry = Assert.Single(methods.Where(method =>
            method.Name == entryPoint && method.ReturnType == typeof(int)));
        if (expectedOwner != null)
            Assert.Equal(expectedOwner, entry.DeclaringType!.FullName);
        Assert.Equal(expected, Assert.IsType<int>(entry.Invoke(null, null)));
        return assembly;
    }

    private static HashSet<ConversionLossKind> ReadLossKinds(
        JsonElement manifest,
        string property)
    {
        if (!manifest.TryGetProperty(
                property,
                out var values))
            return [];
        return values.EnumerateArray()
            .Select(value =>
            {
                Assert.True(
                    Enum.TryParse<ConversionLossKind>(
                        value.GetString(),
                        out var parsed),
                    $"Unknown conversion loss kind "
                    + $"'{value.GetString()}'.");
                return parsed;
            })
            .ToHashSet();
    }

    private static string Normalize(string s)
    {
        var normalized = RawCSharpFrame.NormalizeLineEndings(s);
        var diagnostics = new DiagnosticBag();
        var payloadEnd = new Lexer(normalized, diagnostics).TokenizeAll()
            .Where(token => token.Value is RawCSharpFrame)
            .Select(token => token.Span.End).DefaultIfEmpty().Max();
        Assert.Empty(diagnostics.Errors);
        return normalized[..payloadEnd] + normalized[payloadEnd..].TrimEnd('\n');
    }

    /// <summary>README rule: every char outside [a-z0-9-] becomes '-', runs collapsed.</summary>
    private static string SanitizeKey(string key)
    {
        var chars = key.ToLowerInvariant()
            .Select(c => (char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-') ? c : '-');
        var collapsed = new System.Text.StringBuilder();
        foreach (var c in chars)
        {
            if (c == '-' && collapsed.Length > 0 && collapsed[^1] == '-') continue;
            collapsed.Append(c);
        }
        return collapsed.ToString().Trim('-');
    }
}
