using Calor.Compiler.Migration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SyntaxCapabilityClassifier =
    Calor.Compiler.Migration.FeatureSupport.SyntaxCapabilityClassifier;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Integration tests for the feature-check command functionality.
/// Tests the FeatureSupport registry for completeness and correctness.
/// </summary>
public class FeatureCheckCommandTests
{
    #region Feature Lookup

    [Theory]
    [InlineData("class", SupportLevel.Full)]
    [InlineData("async-await", SupportLevel.Full)]
    [InlineData("lambda", SupportLevel.Full)]
    [InlineData("generics", SupportLevel.Full)]
    [InlineData("operator-overload", SupportLevel.Full)]
    [InlineData("implicit-conversion", SupportLevel.Full)]
    [InlineData("explicit-conversion", SupportLevel.Full)]
    [InlineData("equals-operator", SupportLevel.Full)]
    [InlineData("linq-method", SupportLevel.Full)]
    [InlineData("goto", SupportLevel.Full)]
    [InlineData("labeled-statement", SupportLevel.Full)]
    [InlineData("postfix-operator", SupportLevel.Full)]
    [InlineData("is-type-pattern", SupportLevel.Full)]
    [InlineData("declaration-pattern", SupportLevel.Full)]
    [InlineData("throw-expression", SupportLevel.Full)]
    [InlineData("nested-generic-type", SupportLevel.Full)]
    [InlineData("binary pattern (and/or)", SupportLevel.Full)]
    [InlineData("unary pattern (not)", SupportLevel.Full)]
    [InlineData("record", SupportLevel.Full)]
    public void FeatureCheck_FullySupported_ReturnsFullLevel(string feature, SupportLevel expected)
    {
        var info = FeatureSupport.GetFeatureInfo(feature);

        Assert.NotNull(info);
        Assert.Equal(expected, info.Support);
        Assert.True(FeatureSupport.IsFullySupported(feature));
        Assert.True(FeatureSupport.IsSupported(feature));
    }

    [Theory]
    [InlineData("ref-parameter", SupportLevel.Partial)]
    [InlineData("dynamic", SupportLevel.Partial)]
    [InlineData("interface", SupportLevel.Partial)]
    [InlineData("linq-query", SupportLevel.Partial)]
    [InlineData("local-function", SupportLevel.Partial)] // 0.25 F3 (#847): the non-capturing slice
    public void FeatureCheck_PartiallySupported_ReturnsPartialLevel(string feature, SupportLevel expected)
    {
        var info = FeatureSupport.GetFeatureInfo(feature);

        Assert.NotNull(info);
        Assert.Equal(expected, info.Support);
        Assert.False(FeatureSupport.IsFullySupported(feature));
        Assert.True(FeatureSupport.IsSupported(feature));
    }

    [Theory]
    [InlineData("await-foreach", SupportLevel.NotSupported)]
    [InlineData("file-scoped-type", SupportLevel.NotSupported)]
    [InlineData("scoped-parameter", SupportLevel.NotSupported)]
    [InlineData("using-declaration", SupportLevel.NotSupported)]
    [InlineData("standalone-block", SupportLevel.NotSupported)]
    public void FeatureCheck_NotSupported_ReturnsNotSupportedLevel(string feature, SupportLevel expected)
    {
        var info = FeatureSupport.GetFeatureInfo(feature);

        Assert.NotNull(info);
        Assert.Equal(expected, info.Support);
        Assert.False(FeatureSupport.IsFullySupported(feature));
        Assert.False(FeatureSupport.IsSupported(feature));
    }

    [Theory]
    [InlineData("yield-return", SupportLevel.Full)]
    [InlineData("extension-method", SupportLevel.Full)]
    public void FeatureCheck_NewlyFullySupported_ReturnsFullLevel(string feature, SupportLevel expected)
    {
        var info = FeatureSupport.GetFeatureInfo(feature);

        Assert.NotNull(info);
        Assert.Equal(expected, info.Support);
        Assert.True(FeatureSupport.IsFullySupported(feature));
        Assert.True(FeatureSupport.IsSupported(feature));
    }

    #endregion

    #region Unknown Features

    [Fact]
    public void FeatureCheck_UnknownFeature_ReturnsNull()
    {
        var info = FeatureSupport.GetFeatureInfo("some-made-up-feature");

        Assert.Null(info);
    }

    [Fact]
    public void FeatureCheck_UnknownFeature_DefaultsToNotSupported()
    {
        // Unknown features default to NotSupported to prevent silent suppression of blockers
        var level = FeatureSupport.GetSupportLevel("unknown-feature");

        Assert.Equal(SupportLevel.NotSupported, level);
    }

    #endregion

    #region Case Insensitivity

    [Theory]
    [InlineData("ASYNC-AWAIT")]
    [InlineData("Async-Await")]
    [InlineData("async-AWAIT")]
    public void FeatureCheck_CaseInsensitive_FindsFeature(string feature)
    {
        var info = FeatureSupport.GetFeatureInfo(feature);

        Assert.NotNull(info);
        Assert.Equal("async-await", info.Name);
    }

    #endregion

    #region Workarounds

    [Theory]
    [InlineData("await-foreach")]
    public void FeatureCheck_UnsupportedFeature_HasWorkaround(string feature)
    {
        var info = FeatureSupport.GetFeatureInfo(feature);

        Assert.NotNull(info);
        Assert.NotNull(info.Workaround);
        Assert.NotEmpty(info.Workaround);
    }

    [Fact]
    public void FeatureCheck_GetWorkaround_ReturnsWorkaroundText()
    {
        var workaround = FeatureSupport.GetWorkaround("await-foreach");

        Assert.Equal("Keep the containing member as exact C# interop", workaround);
    }

    [Fact]
    public void FeatureCheck_GetWorkaround_DefaultDoesNotRecommendHandwrittenInterop()
    {
        var feature = new FeatureInfo
        {
            Name = "unsupported-without-workaround",
            Support = SupportLevel.NotSupported
        };

        var workaround = FeatureSupport.GetWorkaroundText(feature);

        Assert.NotNull(workaround);
        Assert.DoesNotContain("explicit interop", workaround);
        Assert.Contains("calor convert", workaround);
        Assert.Contains("original C#", workaround);
        Assert.Contains("converter-marked fallback", workaround);
        Assert.Contains("do not handwrite passthrough", workaround);
    }

    [Theory]
    [InlineData(SupportLevel.Full)]
    [InlineData(SupportLevel.Partial)]
    [InlineData(SupportLevel.ManualRequired)]
    public void FeatureCheck_GetWorkaround_NoDefaultForOtherSupportLevels(SupportLevel support)
    {
        var feature = new FeatureInfo { Name = "without-workaround", Support = support };

        Assert.Null(FeatureSupport.GetWorkaroundText(feature));
    }

    [Fact]
    public void FeatureCheck_GetWorkaround_PreservesExplicitWorkarounds()
    {
        foreach (var feature in FeatureSupport.GetAllFeatures().Where(feature => feature.Workaround != null))
        {
            Assert.Equal(feature.Workaround, FeatureSupport.GetWorkaround(feature.Name));
        }

        Assert.Null(FeatureSupport.GetWorkaround("unknown-feature"));
    }

    #endregion

    #region Feature Listing

    [Fact]
    public void FeatureCheck_GetAllFeatures_ReturnsNonEmpty()
    {
        var features = FeatureSupport.GetAllFeatures().ToList();

        Assert.NotEmpty(features);
        Assert.True(features.Count > 50, $"Expected > 50 features, got {features.Count}");
    }

    [Fact]
    public void FeatureCheck_GetFeaturesBySupport_FiltersCorrectly()
    {
        var fullFeatures = FeatureSupport.GetFeaturesBySupport(SupportLevel.Full).ToList();
        var notSupportedFeatures = FeatureSupport.GetFeaturesBySupport(SupportLevel.NotSupported).ToList();

        Assert.NotEmpty(fullFeatures);
        Assert.NotEmpty(notSupportedFeatures);
        Assert.All(fullFeatures, f => Assert.Equal(SupportLevel.Full, f.Support));
        Assert.All(notSupportedFeatures, f => Assert.Equal(SupportLevel.NotSupported, f.Support));
    }

    [Fact]
    public void FeatureCheck_AllLevelsHaveFeatures()
    {
        // At least the main levels (Full, Partial, NotSupported) should have features
        var fullFeatures = FeatureSupport.GetFeaturesBySupport(SupportLevel.Full).ToList();
        var partialFeatures = FeatureSupport.GetFeaturesBySupport(SupportLevel.Partial).ToList();
        var notSupportedFeatures = FeatureSupport.GetFeaturesBySupport(SupportLevel.NotSupported).ToList();

        Assert.NotEmpty(fullFeatures);
        Assert.NotEmpty(partialFeatures);
        Assert.NotEmpty(notSupportedFeatures);
    }

    #endregion

    #region Blocker Name Consistency

    [Theory]
    [InlineData("relational-pattern")]
    [InlineData("compound-pattern")]
    [InlineData("range-expression")]
    [InlineData("index-from-end")]
    [InlineData("target-typed-new")]
    [InlineData("null-conditional-method")]
    [InlineData("named-argument")]
    [InlineData("declaration-pattern")]
    [InlineData("throw-expression")]
    [InlineData("nested-generic-type")]
    [InlineData("out-var")]
    [InlineData("in-parameter")]
    [InlineData("checked-block")]
    [InlineData("with-expression")]
    [InlineData("init-accessor")]
    [InlineData("required-member")]
    [InlineData("list-pattern")]
    [InlineData("static-abstract-member")]
    [InlineData("ref-struct")]
    [InlineData("lock-statement")]
    [InlineData("await-foreach")]
    [InlineData("await-using")]
    [InlineData("using-declaration")]
    [InlineData("standalone-block")]
    [InlineData("scoped-parameter")]
    [InlineData("collection-expression")]
    [InlineData("readonly-struct")]
    [InlineData("default-lambda-parameter")]
    [InlineData("file-scoped-type")]
    [InlineData("utf8-string-literal")]
    [InlineData("generic-attribute")]
    [InlineData("using-type-alias")]
    public void FeatureCheck_BlockerName_ExistsInRegistry(string blockerName)
    {
        // All blocker names from MigrationAnalyzer should exist in FeatureSupport
        var info = FeatureSupport.GetFeatureInfo(blockerName);

        Assert.NotNull(info);
        Assert.Equal(blockerName, info.Name);
    }

    [Fact]
    public void FeatureCheck_AllBlockerNames_UseKebabCase()
    {
        var features = FeatureSupport.GetAllFeatures();

        foreach (var feature in features)
        {
            // All names should be lowercase kebab-case
            Assert.Equal(feature.Name.ToLowerInvariant(), feature.Name);
            Assert.DoesNotContain("_", feature.Name);
            Assert.DoesNotMatch(@"[A-Z]", feature.Name);
        }
    }

    [Theory]
    [InlineData("await foreach (var item in items) { }", "await-foreach")]
    [InlineData("await using (resource) { }", "await-using")]
    [InlineData("await using var resource = value;", "await-using")]
    [InlineData("using var resource = value;", "using-declaration")]
    [InlineData("public ref struct Buffer { }", "ref-struct")]
    [InlineData("file class LocalType { }", "file-scoped-type")]
    [InlineData("public void M(scoped ref int value) { }", "scoped-parameter")]
    [InlineData("public interface I { void M() { } }", "interface-method-semantics")]
    [InlineData("public interface I { int P { get => 1; } }", "interface-property-semantics")]
    [InlineData("public interface I { int this[int i] { get => i; } }", "interface-indexer-semantics")]
    [InlineData("public interface I { event System.Action Changed; }", "interface-member")]
    [InlineData("public void M() { { int x = 1; } }", "standalone-block")]
    [InlineData("public delegate T Factory<T>(T value);", "delegate-semantics")]
    [InlineData("[System.Obsolete] public delegate void Legacy();", "delegate-semantics")]
    public void SyntaxCapabilityClassifier_DetectsRequiredUnsupportedFeature(
        string source,
        string expectedFeature)
    {
        var root = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview)).GetRoot();

        var detection = Assert.Single(
            SyntaxCapabilityClassifier.Detect(root)
                .Where(item => item.FeatureName == expectedFeature));

        Assert.Equal(expectedFeature, detection.FeatureName);
        Assert.True(detection.Line > 0);
        Assert.True(detection.Column > 0);
        Assert.True(detection.SpanLength > 0);
        Assert.Contains(expectedFeature, SyntaxCapabilityClassifier.RequiredUnsupportedFeatures);
    }

    [Fact]
    public void SyntaxCapabilityClassifier_InternalInterfaceConvertsNativelyWithItsVisibility()
    {
        const string source = "internal interface I { void M(); }";
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        Assert.Empty(SyntaxCapabilityClassifier.Detect(root));

        var converted = new CSharpToCalorConverter(new ConversionOptions
        {
            ValidateRoundTripCSharp = false
        }).Convert(source);
        Assert.True(converted.Success, string.Join("\n", converted.Issues));
        var definition = Assert.Single(converted.Ast!.Interfaces);
        Assert.Equal(Calor.Compiler.Ast.Visibility.Internal, definition.Visibility);
        Assert.Empty(converted.Ast.InteropBlocks);
        Assert.Empty(converted.Losses);

        var compiled = Program.Compile(converted.CalorSource!);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        Assert.Contains("internal interface I", compiled.GeneratedCode);
    }

    [Theory]
    [InlineData("public interface I { void M() { } }", "interface-method-semantics", SyntaxKind.InterfaceDeclaration)]
    [InlineData("public interface I { int P { get => 1; } }", "interface-property-semantics", SyntaxKind.InterfaceDeclaration)]
    [InlineData("public interface I { int this[int i] { get => i; } }", "interface-indexer-semantics", SyntaxKind.InterfaceDeclaration)]
    [InlineData("public interface I { event System.Action E; }", "interface-member", SyntaxKind.InterfaceDeclaration)]
    [InlineData("public partial interface I { void M(); }", "interface-semantics", SyntaxKind.InterfaceDeclaration)]
    [InlineData("public delegate T Factory<T>(T value);", "delegate-semantics", SyntaxKind.DelegateDeclaration)]
    [InlineData("public ref struct S { }", "ref-struct", SyntaxKind.StructDeclaration)]
    [InlineData("file class C { }", "file-scoped-type", SyntaxKind.ClassDeclaration)]
    [InlineData("public class C { public void M(scoped ref int x) { } }", "scoped-parameter", SyntaxKind.MethodDeclaration)]
    [InlineData("public class C { public void M() { { int x = 1; } } }", "standalone-block", SyntaxKind.MethodDeclaration)]
    [InlineData("public class C { public void M(System.IDisposable d) { using var item = d; } }", "using-declaration", SyntaxKind.MethodDeclaration)]
    [InlineData("public class C { public async System.Threading.Tasks.Task M() { await foreach (var x in xs) { } } }", "await-foreach", SyntaxKind.MethodDeclaration)]
    [InlineData("public class C { public async System.Threading.Tasks.Task M() { await using (resource) { } } }", "await-using", SyntaxKind.MethodDeclaration)]
    [InlineData("public record struct R(int X);", "record-struct", SyntaxKind.RecordStructDeclaration)]
    [InlineData("public partial record R(int X);", "record-partial", SyntaxKind.RecordDeclaration)]
    [InlineData("public unsafe record R(int X);", "unsafe", SyntaxKind.RecordDeclaration)]
    [InlineData("public record R([System.Obsolete] int X);", "record-positional-parameter-metadata", SyntaxKind.RecordDeclaration)]
    [InlineData("public class C { public void M() { for (int i = 0; i < 1; (i, i) = (1, 2)) { } } }", "tuple-deconstruction", SyntaxKind.ForStatement)]
    [InlineData("public class C { public void M() { for (int i = 0; int.TryParse(\"0\", out var n); i++) { } } }", "for", SyntaxKind.ForStatement)]
    public void PreservationAuthority_ExistingGuardRequiresExactOriginalBoundary(
        string source, string feature, SyntaxKind guardKind)
    {
        var root = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.Preview)).GetRoot();
        var original = Assert.Single(root.DescendantNodes().Where(node => node.IsKind(guardKind)));

        var decision = FeatureSupport.AssessPreservation(feature, original, original);

        Assert.Equal(FeatureSupport.PreservationDecisionKind.StillRequired, decision.Kind);
        Assert.Equal(feature, decision.RequiredFeature);
        var converted = new CSharpToCalorConverter(new ConversionOptions
        {
            ValidateRoundTripCSharp = false
        }).Convert(source);
        Assert.True(converted.Success, string.Join("\n", converted.Issues));
        Assert.Contains(converted.Losses, loss =>
            loss.Kind == ConversionLossKind.InteropPreserved && loss.Feature == feature);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("invented-feature")]
    [InlineData("RECORD-STRUCT")]
    [InlineData("record-member-InventedSyntaxKind")]
    [InlineData("record-member-SimpleAssignmentExpression")]
    [InlineData("record-member-0")]
    [InlineData("binary pattern (xor)")]
    public void PreservationAuthority_UnknownReasonCannotBecomeApplicable(string? feature)
    {
        var original = CSharpSyntaxTree.ParseText("public record struct R;")
            .GetRoot().DescendantNodes().OfType<RecordDeclarationSyntax>().Single();

        var decision = FeatureSupport.AssessPreservation(feature, original, original);

        Assert.Equal(FeatureSupport.PreservationDecisionKind.UnknownOrMismatch, decision.Kind);
        Assert.Contains("calor convert", decision.Explanation);
        Assert.Null(decision.RequiredFeature);
    }

    [Theory]
    [InlineData("record-struct", "public record R;", SyntaxKind.RecordDeclaration)]
    [InlineData("record-partial", "public record R;", SyntaxKind.RecordDeclaration)]
    [InlineData("file-scoped-type", "public class C { }", SyntaxKind.ClassDeclaration)]
    [InlineData("interface-semantics", "internal interface I { void M(); }", SyntaxKind.InterfaceDeclaration)]
    [InlineData("await-using", "public class C { public void M() { } }", SyntaxKind.MethodDeclaration)]
    [InlineData("ref-struct", "file struct S { }", SyntaxKind.StructDeclaration)]
    public void PreservationAuthority_MissingOrDifferentGuardIsNotNativeProof(
        string feature, string source, SyntaxKind guardKind)
    {
        var original = CSharpSyntaxTree.ParseText(source,
                new CSharpParseOptions(LanguageVersion.Preview)).GetRoot()
            .DescendantNodes().Single(node => node.IsKind(guardKind));

        var decision = FeatureSupport.AssessPreservation(feature, original, original);

        Assert.Equal(FeatureSupport.PreservationDecisionKind.UnknownOrMismatch, decision.Kind);
        Assert.Contains("native support is not proven", decision.Explanation);
    }

    [Theory]
    [InlineData("await-foreach")]
    [InlineData("for")]
    [InlineData("record-struct")]
    [InlineData("preprocessor-disabled")]
    [InlineData("conversion-error")]
    [InlineData("default-expression")]
    [InlineData("whole-file-preservation")]
    public void PreservationAuthority_MissingOriginalContextIsExplicit(string feature)
    {
        var original = CSharpSyntaxTree.ParseText("public record struct R;")
            .GetRoot().DescendantNodes().OfType<RecordDeclarationSyntax>().Single();

        foreach (var (node, boundary) in new (SyntaxNode?, SyntaxNode?)[]
        {
            (null, null), (original, null), (null, original)
        })
        {
            var decision = FeatureSupport.AssessPreservation(feature, node, boundary);
            Assert.Equal(FeatureSupport.PreservationDecisionKind.InsufficientContext, decision.Kind);
            Assert.Contains("calor convert", decision.Explanation);
        }
    }

    [Theory]
    [InlineData("checked-block")]
    [InlineData("conversion-error")]
    [InlineData("post-validation-fallback")]
    [InlineData("whole-file-preservation")]
    [InlineData("async-return-signature")]
    [InlineData("alias-qualified-value")]
    [InlineData("default-expression")]
    [InlineData("null-forgiving-expression")]
    [InlineData("dictionary-initializer")]
    [InlineData("explicit-interface")]
    [InlineData("record-unresolved-base-kind")]
    [InlineData("preprocessor-disabled")]
    [InlineData("preprocessor-member")]
    [InlineData("preprocessor-ownership-fallback")]
    [InlineData("preprocessor-unparsed-remainder")]
    [InlineData("iterator-accessor")]
    [InlineData("partial-property")]
    [InlineData("compound-assignment-order")]
    [InlineData("binary pattern (and)")]
    [InlineData("record-member-EventFieldDeclaration")]
    [InlineData("method")]
    [InlineData("localfunction")]
    public void PreservationAuthority_ContextualAndDynamicProducersRemainUnresolved(string feature)
    {
        var original = CSharpSyntaxTree.ParseText("public class C { }")
            .GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single();

        Assert.True(FeatureSupport.IsKnownPreservationReason(feature));
        var decision = FeatureSupport.AssessPreservation(feature, original, original);

        Assert.Equal(FeatureSupport.PreservationDecisionKind.InsufficientContext, decision.Kind);
        Assert.Contains("context", decision.Explanation);
    }

    [Fact]
    public void PreservationAuthority_SourceTreeAndBoundaryIdentityCannotBeReconstructedFromPayload()
    {
        const string source = "public record struct R;";
        var original = CSharpSyntaxTree.ParseText(source).GetRoot()
            .DescendantNodes().OfType<RecordDeclarationSyntax>().Single();
        var copy = CSharpSyntaxTree.ParseText(source).GetRoot()
            .DescendantNodes().OfType<RecordDeclarationSyntax>().Single();
        var detached = SyntaxFactory.ParseMemberDeclaration(source)!;

        foreach (var (node, boundary) in new[]
        {
            (original, copy), (detached, detached)
        })
        {
            var decision = FeatureSupport.AssessPreservation("record-struct", node, boundary);
            Assert.Equal(FeatureSupport.PreservationDecisionKind.InsufficientContext, decision.Kind);
        }
    }

    [Fact]
    public void PreservationAuthority_ChangedBoundaryIsNotAValidReasonOrNativeProof()
    {
        var root = CSharpSyntaxTree.ParseText(
            "public class C { public void M(System.IDisposable d) { using var item = d; } }").GetRoot();
        var guard = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var oldBoundary = root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single();

        var decision = FeatureSupport.AssessPreservation("using-declaration", guard, oldBoundary);

        Assert.Equal(FeatureSupport.PreservationDecisionKind.ChangedBoundary, decision.Kind);
        Assert.Equal("using-declaration", decision.RequiredFeature);
        Assert.Contains("calor convert", decision.Explanation);
    }

    [Theory]
    [InlineData("#if OFF\nnot C#\n#endif\npublic record struct R;")]
    [InlineData("[assembly: System.CLSCompliant(true)] public record struct R;")]
    [InlineData("namespace A { public record struct R; } namespace B { class C { } }")]
    [InlineData("public record struct R(")]
    public void PreservationAuthority_ConditionalMalformedAndUnitContextsStayUnresolved(string source)
    {
        var original = CSharpSyntaxTree.ParseText(source,
                new CSharpParseOptions(LanguageVersion.Preview)).GetRoot()
            .DescendantNodes().OfType<RecordDeclarationSyntax>().Single();

        var decision = FeatureSupport.AssessPreservation("record-struct", original, original);

        Assert.Equal(FeatureSupport.PreservationDecisionKind.InsufficientContext, decision.Kind);
    }

    [Fact]
    public void PreservationAuthority_LocalFunctionPreflightNeedsItsActualConversionDecision()
    {
        var root = CSharpSyntaxTree.ParseText(
            "public class C { public void M(System.IDisposable d) { using var item = d; void Local() { } } }")
            .GetRoot();
        var original = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();

        var decision = FeatureSupport.AssessPreservation("using-declaration", original, original);

        Assert.Equal(FeatureSupport.PreservationDecisionKind.InsufficientContext, decision.Kind);
    }

    [Theory]
    [InlineData("public class C { public void M(ref int n) { for (ref int i = ref n; i < 1; i++) { } } }", "for", SyntaxKind.ForStatement)]
    [InlineData("public unsafe class C { public void M(System.IDisposable d) { using var item = d; } }", "using-declaration", SyntaxKind.MethodDeclaration)]
    [InlineData("public record C { public void M(System.IDisposable d) { using var item = d; } }", "using-declaration", SyntaxKind.MethodDeclaration)]
    [InlineData("public interface I { void M(); }", "interface-member", SyntaxKind.InterfaceDeclaration)]
    [InlineData("public class C { public record struct R; }", "record-struct", SyntaxKind.RecordStructDeclaration)]
    [InlineData("public class C { public void M() { System.Action run = () => { for (int i = 0; int.TryParse(\"0\", out var n); i++) { } }; } }", "for", SyntaxKind.ForStatement)]
    public void PreservationAuthority_EnclosingPreflightAndRescueCannotBeAssumed(
        string source, string feature, SyntaxKind guardKind)
    {
        var original = CSharpSyntaxTree.ParseText(source,
                new CSharpParseOptions(LanguageVersion.Preview)).GetRoot()
            .DescendantNodes().Single(node => node.IsKind(guardKind));

        var decision = FeatureSupport.AssessPreservation(feature, original, original);

        Assert.Equal(FeatureSupport.PreservationDecisionKind.InsufficientContext, decision.Kind);
    }

    [Fact]
    public void PreservationAuthority_EnclosingUnsupportedOwnerChangesTheBoundary()
    {
        var original = CSharpSyntaxTree.ParseText(
                "file class C { public void M(System.IDisposable d) { using var item = d; } }")
            .GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();

        var decision = FeatureSupport.AssessPreservation("using-declaration", original, original);

        Assert.Equal(FeatureSupport.PreservationDecisionKind.ChangedBoundary, decision.Kind);
        Assert.Equal("file-scoped-type", decision.RequiredFeature);
    }

    [Fact]
    public void PreservationAuthority_RefLocalPreflightChangesLoopToMemberBoundary()
    {
        var original = CSharpSyntaxTree.ParseText(
                "public class C { private int n; public void M() { for (ref int i = ref n; i < 1; i++) { } } }")
            .GetRoot().DescendantNodes().OfType<ForStatementSyntax>().Single();

        var decision = FeatureSupport.AssessPreservation("for", original, original);

        Assert.Equal(FeatureSupport.PreservationDecisionKind.ChangedBoundary, decision.Kind);
        Assert.Equal("ref-parameter", decision.RequiredFeature);
    }

    [Fact]
    public void PreservationAuthority_FullRegistrySupportNeverProvesNativeConversion()
    {
        foreach (var feature in new[] { "class", "record", "postfix-operator", "explicit-interface" })
        {
            Assert.Equal(SupportLevel.Full, FeatureSupport.GetSupportLevel(feature));
            var original = CSharpSyntaxTree.ParseText("public class C { }").GetRoot()
                .DescendantNodes().OfType<ClassDeclarationSyntax>().Single();

            var decision = FeatureSupport.AssessPreservation(feature, original, original);

            Assert.Equal(FeatureSupport.PreservationDecisionKind.InsufficientContext, decision.Kind);
        }
    }

    [Theory]
    [InlineData("whole-file-preservation")]
    [InlineData("namespace-topology")]
    [InlineData("post-validation-fallback")]
    public void PreservationAuthority_UnitScopeDoesNotSupplyMissingPipelineContext(string feature)
    {
        var original = CSharpSyntaxTree.ParseText("public class C { }").GetCompilationUnitRoot();

        var decision = FeatureSupport.AssessPreservation(feature, original, original);

        Assert.Equal(FeatureSupport.PreservationDecisionKind.InsufficientContext, decision.Kind);
    }

    #endregion

    #region Description Quality

    [Fact]
    public void FeatureCheck_AllFeatures_HaveDescription()
    {
        var features = FeatureSupport.GetAllFeatures();

        foreach (var feature in features)
        {
            Assert.NotNull(feature.Description);
            Assert.NotEmpty(feature.Description);
        }
    }

    [Fact]
    public void FeatureCheck_UnsupportedFeatures_HaveWorkaround()
    {
        var unsupportedFeatures = FeatureSupport.GetFeaturesBySupport(SupportLevel.NotSupported);

        foreach (var feature in unsupportedFeatures)
        {
            Assert.NotNull(feature.Workaround);
            Assert.NotEmpty(feature.Workaround);
        }
    }

    [Fact]
    public void FeatureCheck_ManualRequiredFeatures_HaveWorkaround()
    {
        var manualFeatures = FeatureSupport.GetFeaturesBySupport(SupportLevel.ManualRequired);

        foreach (var feature in manualFeatures)
        {
            Assert.NotNull(feature.Workaround);
            Assert.NotEmpty(feature.Workaround);
        }
    }

    #endregion
}
