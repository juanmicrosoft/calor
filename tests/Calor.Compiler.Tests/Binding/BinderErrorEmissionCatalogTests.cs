using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;
using CalorDiagnostic = Calor.Compiler.Diagnostics.Diagnostic;
using CalorDiagnosticSeverity = Calor.Compiler.Diagnostics.DiagnosticSeverity;

namespace Calor.Compiler.Tests;

public sealed class BinderErrorEmissionCatalogTests
{
    private const string UnknownCode = "<unknown-diagnostic-code>";
    private const string ForwardedBindingCompilationErrors = "<forwarded-binding-compilation-errors>";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [Fact]
    public void BinderErrorCapableEmissionSourceSites_MatchReviewedCatalogGolden()
    {
        var root = RepoRoot();
        var scanner = BinderErrorEmissionScanner.ForRepository(root);
        var routes = scanner.Scan().Select(ToGoldenRecord).ToArray();

        Assert.DoesNotContain(routes, route =>
            route.PossibleCodes.Contains(UnknownCode, StringComparer.Ordinal));
        Assert.Empty(routes.Where(route =>
            route.Disposition is not "CompilationError"
                and not "AnalysisOnly"
                and not "CompilationErrorForwarder"));

        var binderRoutes = routes.Where(route =>
            route.SiteId.StartsWith("src/Calor.Compiler/Binding/Binder.cs::", StringComparison.Ordinal)).ToArray();
        Assert.Equal(23, binderRoutes.Count(route =>
            route.Sink is "DiagnosticBag.ReportError" or "DiagnosticBag.ReportErrorWithFix"));
        Assert.Equal(4, binderRoutes.Count(route => route.Sink == "DiagnosticBag.Report"));
        Assert.Equal(3, binderRoutes
            .Where(route => route.Sink is "DiagnosticBag.ReportDuplicateDefinitionWithFix"
                or "DiagnosticBag.ReportNotAVariableWithFix")
            .Select(route => route.SiteId.Split("/leaf#", StringSplitOptions.None)[0])
            .Distinct(StringComparer.Ordinal)
            .Count());

        var actual = NormalizeJson(routes);
        var goldenPath = Path.Combine(root, "tests", "TestData", "Binding",
            "BinderErrorEmissionCatalog.golden.json");
        var expected = NormalizeJson(
            JsonSerializer.Deserialize<GoldenEmissionRecord[]>(
                File.ReadAllText(goldenPath), JsonOptions)
            ?? []);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void PrimitiveReporterContracts_RequireReviewWhenTheirImplementationChanges()
    {
        // The scanner summarizes these primitives instead of recursively expanding
        // their storage mechanics. Pin their complete syntax so a new hidden
        // emission/branch cannot evade the caller-site inventory.
        var syntax = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "Calor.Compiler", "Diagnostics", "DiagnosticBag.cs")));
        var contracts = syntax.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(method => method.Identifier.ValueText is
                "Report" or "ReportWithFix" or "ReportVerification" or "ReportError"
                or "ReportWarning" or "ReportInfo" or "ReportErrorWithFix" or "ReportWarningWithFix")
            .OrderBy(method => method.Identifier.ValueText, StringComparer.Ordinal)
            .Select(method => method.Identifier.ValueText + ":" + Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(method.WithoutTrivia().NormalizeWhitespace().ToFullString().ReplaceLineEndings("\n")))));
        var actual = string.Join("\n", contracts);
        Assert.Equal("""
            Report:16E5BE51B8E7BF102227AEF2CED0BD1089FF4DD56626816EEA15A86C33945699
            Report:27AFEF13F8096D3803AB0C737C33B26EDDD8B64599D7210AF02A98EC86DAEA22
            ReportError:5498EA0AF74AD9BFB32324C636DCC53FB48E57DBFB5AF7F50FC7974909AACBC0
            ReportErrorWithFix:091F375116B698F682EB6C7EA3098DB6CD2BCAEAA436F75E834382DA52AED070
            ReportInfo:B98739F1B24784CBFF1A92EACEC573C957ADB31574325CD98195B1EDF18DFACA
            ReportVerification:D3E4FD107681FA6CDE31DDC410D137F23A6BF6C00631D8664FDCCFB165D59D5B
            ReportWarning:68405272B4AD5BC6024B6BE51BEB1075F9263AF1B37F754A0B3755D2A22B28A9
            ReportWarningWithFix:DDC12647F68DEB5806F22DF141D697D36DA45A501D44A977A601719874402E35
            ReportWithFix:BFC5419BA1238B869E9DF7AC1F310D2DF11D82996C9AAD0A3529F52811748CA2
            """, actual);
    }

    [Fact]
    public void ScannerCanary_DoesNotCollapseRepeatedKnownCodeSites()
    {
        var scanner = BinderErrorEmissionScanner.ForSynthetic(
            ("src/Calor.Compiler/Binding/Canary.cs", """
                using Calor.Compiler.Diagnostics;
                using Calor.Compiler.Parsing;

                namespace Calor.Compiler.Binding;

                internal sealed class Canary
                {
                    private readonly DiagnosticBag _bag = new();

                    public void Probe(TextSpan span)
                    {
                        _bag.ReportError(span, DiagnosticCode.DuplicateDefinition, "first");
                        _bag.ReportError(span, DiagnosticCode.DuplicateDefinition, "second");
                    }
                }
                """));

        var routes = scanner.Scan();

        Assert.Equal(2, routes.Count);
        Assert.All(routes, route => Assert.Equal(DiagnosticCode.DuplicateDefinition, Assert.Single(route.PossibleCodes)));
        Assert.Equal(2, routes.Select(route => route.SiteId).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void ScannerCanary_ResolvesAliasNamedArgumentsDefaultsAndFixHelpers()
    {
        var scanner = BinderErrorEmissionScanner.ForSynthetic(
            ("src/Calor.Compiler/Binding/Canary.cs", """
                using Calor.Compiler.Diagnostics;
                using Calor.Compiler.Parsing;

                namespace Calor.Compiler.Binding;

                internal sealed class Canary
                {
                    public void Probe(DiagnosticBag bag, TextSpan span, SuggestedFix fix)
                    {
                        var alias = bag;
                        alias.Report(message: "named default", code: DiagnosticCode.DuplicateDefinition, span: span);
                        alias.ReportWithFix(fix: fix, message: "fix default", code: DiagnosticCode.UndefinedReference, span: span);
                    }
                }
                """));

        var routes = scanner.Scan().OrderBy(route => route.SiteId, StringComparer.Ordinal).ToArray();

        Assert.Collection(
            routes,
            route =>
            {
                Assert.Equal("DiagnosticBag.Report", route.Sink);
                Assert.Equal("Error", route.Severity);
                Assert.Equal(DiagnosticCode.DuplicateDefinition, Assert.Single(route.PossibleCodes));
            },
            route =>
            {
                Assert.Equal("DiagnosticBag.ReportWithFix", route.Sink);
                Assert.Equal("Error", route.Severity);
                Assert.Equal(DiagnosticCode.UndefinedReference, Assert.Single(route.PossibleCodes));
            });
    }

    [Fact]
    public void ScannerCanary_ExpandsDiagnosticBagHelpersAndDynamicSeverityBranches()
    {
        var scanner = BinderErrorEmissionScanner.ForSynthetic(
            ("src/Calor.Compiler/Binding/Canary.cs", """
                using Calor.Compiler.Diagnostics;
                using Calor.Compiler.Parsing;

                namespace Calor.Compiler.Binding;

                internal sealed class Canary
                {
                    public void Probe(DiagnosticBag bag, TextSpan span)
                    {
                        bag.ReportSyntheticConditional(span, chooseLeft: true);
                    }
                }
                """),
            ("src/Calor.Compiler/Diagnostics/DiagnosticBag.cs", """
                using Calor.Compiler.Parsing;

                namespace Calor.Compiler.Diagnostics;

                public sealed class DiagnosticBag
                {
                    public void ReportSyntheticConditional(TextSpan span, bool chooseLeft)
                    {
                        if (chooseLeft)
                        {
                            ReportError(span, DiagnosticCode.DuplicateDefinition, "left");
                        }
                        else
                        {
                            Report(span, DiagnosticCode.UndefinedReference, "right", Severity());
                        }
                    }

                    public void Report(TextSpan span, string code, string message, DiagnosticSeverity severity = DiagnosticSeverity.Error)
                    {
                    }

                    public void ReportError(TextSpan span, string code, string message)
                    {
                    }

                    private static DiagnosticSeverity Severity() => DiagnosticSeverity.Error;
                }
                """));

        var routes = scanner.Scan().OrderBy(route => route.SiteId, StringComparer.Ordinal).ToArray();

        Assert.Collection(
            routes,
            route =>
            {
                Assert.Equal("DiagnosticBag.ReportSyntheticConditional -> DiagnosticBag.ReportError", route.HelperPath);
                Assert.Equal("Error", route.Severity);
                Assert.Equal(DiagnosticCode.DuplicateDefinition, Assert.Single(route.PossibleCodes));
            },
            route =>
            {
                Assert.Equal("DiagnosticBag.ReportSyntheticConditional -> DiagnosticBag.Report", route.HelperPath);
                Assert.Equal("Dynamic", route.Severity);
                Assert.Equal(DiagnosticCode.UndefinedReference, Assert.Single(route.PossibleCodes));
            });
    }

    [Fact]
    public void ScannerCanary_FailsClosedForUnsupportedForwardingAndUnknownCodes()
    {
        var scanner = BinderErrorEmissionScanner.ForSynthetic(
            ("src/Calor.Compiler/Binding/Canary.cs", """
                using Calor.Compiler.Diagnostics;
                using Calor.Compiler.Parsing;

                namespace Calor.Compiler.Binding;

                internal sealed class Canary
                {
                    public void Probe(DiagnosticBag destination, Diagnostic diagnostic, TextSpan span, string dynamicCode)
                    {
                        destination.Add(diagnostic);
                        destination.ReportError(span, dynamicCode, "dynamic");
                    }
                }
                """));

        var routes = scanner.Scan().OrderBy(route => route.SiteId, StringComparer.Ordinal).ToArray();

        Assert.Collection(
            routes,
            route =>
            {
                Assert.Equal("UnsupportedForwarding", route.Disposition);
                Assert.Equal(ForwardedBindingCompilationErrors, Assert.Single(route.PossibleCodes));
            },
            route =>
            {
                Assert.Equal("Unclassified", route.Disposition);
                Assert.Equal(UnknownCode, Assert.Single(route.PossibleCodes));
            });
    }

    [Fact]
    public void ScannerCanary_CoversTargetTypedConstructionAndMixedNamedArguments()
    {
        var scanner = BinderErrorEmissionScanner.ForSynthetic(
            ("src/Calor.Compiler/Binding/Canary.cs", """
                using Calor.Compiler.Diagnostics;
                using Calor.Compiler.Parsing;
                namespace Calor.Compiler.Binding;
                internal class Canary
                {
                    public Diagnostic Probe(DiagnosticBag bag, TextSpan span)
                    {
                        bag.Report(span: span, DiagnosticCode.DuplicateDefinition, "error");
                        return new(DiagnosticCode.UndefinedReference, "error", span);
                    }
                    public DiagnosticWithFix Fix(TextSpan span) => new(DiagnosticCode.TypeMismatch, "error", span, null!);
                }
                """));
        var routes = scanner.Scan();
        Assert.Equal(3, routes.Count);
        Assert.All(routes, route => Assert.Equal("Candidate", route.Disposition));
        Assert.Contains(routes, route => route.PossibleCodes.Contains(DiagnosticCode.UndefinedReference));
    }

    [Fact]
    public void ScannerCanary_RejectsUnmodeledExternalDiagnosticHelper()
    {
        var scanner = BinderErrorEmissionScanner.ForSynthetic(
            ("src/Calor.Compiler/Binding/Canary.cs", """
                using Calor.Compiler.Diagnostics;
                namespace Calor.Compiler.Binding;
                internal class Canary
                {
                    public void Probe(DiagnosticBag bag) => Other.Reporter.Emit(bag);
                }
                """),
            ("Other.cs", """
                using Calor.Compiler.Diagnostics;
                namespace Other;
                internal static class Reporter
                {
                    public static void Emit(DiagnosticBag bag) => bag.ReportError(default, "Calor9999", "hidden");
                }
                """));
        Assert.Throws<InvalidOperationException>(() => scanner.Scan());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScannerCanary_RejectsEscapingReporterDelegatesAndInactiveBindingCode(bool inactiveCode)
    {
        var scanner = BinderErrorEmissionScanner.ForSynthetic(
            ("src/Calor.Compiler/Binding/Canary.cs", """
                using System;
                using Calor.Compiler.Diagnostics;
                using Calor.Compiler.Parsing;
                namespace Calor.Compiler.Binding;
                internal class Canary
                {
                    public void Probe(DiagnosticBag bag)
                    {
                        BODY
                    }
                }
                """.Replace("BODY", inactiveCode
                    ? "#if UNMEASURED\nbag.ReportError(default, \"Calor9999\", \"hidden\");\n#endif"
                    : "Action<TextSpan, string, string> emit = bag.ReportError;", StringComparison.Ordinal)));
        Assert.Throws<InvalidOperationException>(() => scanner.Scan());
    }

    [Fact]
    public void ScannerCanary_TreatsDefaultDiagnosticConstructorSeverityAsErrorCapable()
    {
        var scanner = BinderErrorEmissionScanner.ForSynthetic(
            ("src/Calor.Compiler/Binding/Canary.cs", """
                using Calor.Compiler.Diagnostics;
                using Calor.Compiler.Parsing;

                namespace Calor.Compiler.Binding;

                internal sealed class Canary
                {
                    public Diagnostic Probe(TextSpan span)
                    {
                        return new Diagnostic(DiagnosticCode.DuplicateDefinition, "message", span);
                    }
                }
                """));

        var route = Assert.Single(scanner.Scan());

        Assert.Equal("Diagnostic..ctor", route.Sink);
        Assert.Equal("Error", route.Severity);
        Assert.Equal(DiagnosticCode.DuplicateDefinition, Assert.Single(route.PossibleCodes));
    }

    private static GoldenEmissionRecord ToGoldenRecord(DiagnosticEmissionRoute route)
    {
        if (route.Disposition == "CompilationErrorForwarder")
        {
            return new GoldenEmissionRecord(
                route.SiteId,
                route.Member,
                route.Sink,
                route.HelperPath,
                [ForwardedBindingCompilationErrors],
                route.Severity,
                "CompilationErrorForwarder",
                1396,
                "User-facing propagation forwards only diagnostics accepted by BindingDiagnosticPolicy.IsCompilationError and de-duplicates them.");
        }

        if (route.Disposition != "Candidate")
        {
            return new GoldenEmissionRecord(
                route.SiteId,
                route.Member,
                route.Sink,
                route.HelperPath,
                route.PossibleCodes,
                route.Severity,
                route.Disposition,
                null,
                route.Reason);
        }

        var codes = route.PossibleCodes;
        if (codes.Length != 1
            || codes[0] == UnknownCode
            || !BindingDiagnosticPolicy.Catalog.TryGetValue(codes[0], out var policy))
        {
            return new GoldenEmissionRecord(
                route.SiteId,
                route.Member,
                route.Sink,
                route.HelperPath,
                codes,
                route.Severity,
                "Unclassified",
                null,
                "Error-capable binder diagnostic source site is not in BindingDiagnosticPolicy.Catalog.");
        }

        return new GoldenEmissionRecord(
            route.SiteId,
            route.Member,
            route.Sink,
            route.HelperPath,
            codes,
            route.Severity,
            policy.Disposition.ToString(),
            policy.OwningIssue,
            policy.Justification);
    }

    private static string NormalizeJson(IReadOnlyList<GoldenEmissionRecord> records) =>
        JsonSerializer.Serialize(records.OrderBy(record => record.SiteId, StringComparer.Ordinal), JsonOptions)
            .ReplaceLineEndings("\n")
        + "\n";

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "Directory.Build.props")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return dir!;
    }

    private sealed record GoldenEmissionRecord(
        string SiteId,
        string Member,
        string Sink,
        string HelperPath,
        string[] PossibleCodes,
        string Severity,
        string Disposition,
        int? OwningIssue,
        string Reason);

    private sealed record DiagnosticEmissionRoute(
        string SiteId,
        string Member,
        string Sink,
        string HelperPath,
        string[] PossibleCodes,
        string Severity,
        string Disposition,
        string Reason);

    private sealed class BinderErrorEmissionScanner
    {
        private static readonly SymbolDisplayFormat TypeFormat =
            new(typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces);

        private readonly ImmutableArray<SourceInput> _bindingSources;
        private readonly ImmutableArray<SourceInput> _helperSources;

        private BinderErrorEmissionScanner(IEnumerable<SourceInput> bindingSources, IEnumerable<SourceInput> helperSources)
        {
            _bindingSources = bindingSources.ToImmutableArray();
            _helperSources = helperSources.ToImmutableArray();
        }

        public static BinderErrorEmissionScanner ForRepository(string root)
        {
            var bindingRoot = Path.Combine(root, "src", "Calor.Compiler", "Binding");
            var bindingSources = Directory.EnumerateFiles(bindingRoot, "*.cs", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(path => new SourceInput(
                    Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'),
                    File.ReadAllText(path)));
            var helperSources = new[]
            {
                Path.Combine(root, "src", "Calor.Compiler", "Diagnostics", "DiagnosticBag.cs")
            }.Select(path => new SourceInput(
                Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'),
                File.ReadAllText(path)));

            return new BinderErrorEmissionScanner(bindingSources, helperSources);
        }

        public static BinderErrorEmissionScanner ForSynthetic(params (string Path, string Text)[] sources)
        {
            var bindingSources = sources
                .Where(source => source.Path.StartsWith("src/Calor.Compiler/Binding/", StringComparison.Ordinal))
                .Select(source => new SourceInput(source.Path, source.Text));
            var helperSources = sources
                .Where(source => !source.Path.StartsWith("src/Calor.Compiler/Binding/", StringComparison.Ordinal))
                .Select(source => new SourceInput(source.Path, source.Text));

            return new BinderErrorEmissionScanner(bindingSources, helperSources);
        }

        public IReadOnlyList<DiagnosticEmissionRoute> Scan()
        {
            var allSources = _bindingSources.Concat(_helperSources).ToArray();
            var trees = allSources
                .Select(source => CSharpSyntaxTree.ParseText(
                    source.Text,
                    new CSharpParseOptions(LanguageVersion.Preview),
                    source.Path))
                .Append(CSharpSyntaxTree.ParseText("""
                    global using System;
                    global using System.Collections.Generic;
                    global using System.IO;
                    global using System.Linq;
                    global using System.Net.Http;
                    global using System.Threading;
                    global using System.Threading.Tasks;
                    """, new CSharpParseOptions(LanguageVersion.Preview), path: "<implicit-usings>"))
                .ToArray();
            var sourceByTree = trees.Zip(allSources).ToDictionary(pair => pair.First, pair => pair.Second);
            var compilation = CSharpCompilation.Create(
                "BinderErrorEmissionCatalogScan",
                trees,
                References(),
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: NullableContextOptions.Enable));
            var helperIndex = new HelperIndex(compilation);
            var routes = new List<DiagnosticEmissionRoute>();
            var ordinals = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var tree in trees.Where(tree => _bindingSources.Any(source => source.Path == tree.FilePath)))
            {
                var source = sourceByTree[tree];
                var model = compilation.GetSemanticModel(tree);
                var root = tree.GetCompilationUnitRoot();
                if (root.DescendantTrivia(descendIntoTrivia: true).Any(trivia => trivia.IsKind(SyntaxKind.DisabledTextTrivia)))
                    throw new InvalidOperationException($"Inactive binding source requires an explicitly reviewed scan configuration: {source.Path}");
                foreach (var memberAccess in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
                {
                    if (model.GetSymbolInfo(memberAccess).Symbol is IMethodSymbol referenced
                        && IsDiagnosticBagMethod(referenced)
                        && memberAccess.Parent is not InvocationExpressionSyntax)
                        throw new InvalidOperationException($"Escaping DiagnosticBag method reference requires explicit analysis: {source.Path}: {memberAccess}");
                }
                var candidates = root.DescendantNodes()
                    .Where(node => node is InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax)
                    .OrderBy(node => node.SpanStart)
                    .ToArray();

                foreach (var candidate in candidates)
                {
                    var found = AnalyzeTopLevelCandidate(candidate, model, helperIndex);
                    if (found.Count == 0)
                        continue;

                    var member = GetMember(candidate, model);
                    var sink = found[0].TopLevelSink;
                    var ordinalKey = $"{source.Path}::{member}::{sink}";
                    ordinals.TryGetValue(ordinalKey, out var ordinal);
                    ordinal++;
                    ordinals[ordinalKey] = ordinal;
                    var baseSiteId = $"{source.Path}::{ShortMember(member)}::{sink}#{ordinal:00}";

                    for (var index = 0; index < found.Count; index++)
                    {
                        var leaf = found[index];
                        var siteId = found.Count == 1
                            ? baseSiteId
                            : $"{baseSiteId}/leaf#{index + 1:00}";
                        routes.Add(new DiagnosticEmissionRoute(
                            siteId,
                            member,
                            sink,
                            leaf.HelperPath,
                            leaf.PossibleCodes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                            leaf.Severity,
                            leaf.Disposition,
                            leaf.Reason));
                    }
                }
            }

            return routes.OrderBy(route => route.SiteId, StringComparer.Ordinal).ToArray();
        }

        private static IReadOnlyList<LeafEmission> AnalyzeTopLevelCandidate(
            SyntaxNode candidate,
            SemanticModel model,
            HelperIndex helperIndex)
        {
            var leaves = candidate switch
            {
                InvocationExpressionSyntax invocation => AnalyzeInvocation(
                    invocation,
                    model,
                    helperIndex,
                    ImmutableDictionary<IParameterSymbol, ExpressionWithModel>.Empty,
                    ImmutableArray<string>.Empty),
                BaseObjectCreationExpressionSyntax creation => AnalyzeDiagnosticConstructor(
                    creation,
                    model,
                    ImmutableDictionary<IParameterSymbol, ExpressionWithModel>.Empty,
                    topLevelSink: "Diagnostic..ctor",
                    helperPath: "Diagnostic..ctor"),
                _ => []
            };

            if (candidate is not InvocationExpressionSyntax topLevelInvocation)
                return leaves;

            var method = ResolveMethod(topLevelInvocation, model);
            if (method is null || !IsDiagnosticBagMethod(method))
                return leaves;

            var topLevelSink = $"DiagnosticBag.{method.Name}";
            return leaves.Select(leaf => leaf with { TopLevelSink = topLevelSink }).ToArray();
        }

        private static IReadOnlyList<LeafEmission> AnalyzeInvocation(
            InvocationExpressionSyntax invocation,
            SemanticModel model,
            HelperIndex helperIndex,
            ImmutableDictionary<IParameterSymbol, ExpressionWithModel> substitutions,
            ImmutableArray<string> helperPath)
        {
            var method = ResolveMethod(invocation, model);
            if (method is null)
            {
                if (invocation.Expression is MemberAccessExpressionSyntax access
                    && model.GetTypeInfo(access.Expression).Type?.ToDisplayString(TypeFormat)
                        == "Calor.Compiler.Diagnostics.DiagnosticBag")
                    throw new InvalidOperationException($"Unresolved DiagnosticBag invocation: {invocation}");
                return [];
            }
            if (!IsDiagnosticBagMethod(method))
            {
                if (!method.ContainingNamespace.ToDisplayString().StartsWith("Calor.Compiler.Binding", StringComparison.Ordinal)
                    && (method.Parameters.Any(parameter => parameter.Type.ToDisplayString(TypeFormat)
                            == "Calor.Compiler.Diagnostics.DiagnosticBag")
                        || method.ReturnType.ToDisplayString(TypeFormat) is
                            "Calor.Compiler.Diagnostics.Diagnostic" or "Calor.Compiler.Diagnostics.DiagnosticWithFix"))
                    throw new InvalidOperationException($"External diagnostic helper requires explicit analysis: {method}");
                return [];
            }

            var display = $"DiagnosticBag.{method.Name}";
            if (helperPath.Contains(display))
                throw new InvalidOperationException($"Recursive diagnostic helper requires explicit analysis: {display}");
            var path = helperPath.Add(display);
            if (method.Name is "Add" or "AddRange")
                return [AnalyzeForwarder(invocation, model, method, path)];

            if (TryAnalyzePrimitiveReport(invocation, model, method, substitutions, path, out var primitive))
                return primitive is null ? [] : [primitive];

            var declaration = helperIndex.Find(method);
            if (declaration is null)
            {
                return
                [
                    new LeafEmission(
                        display,
                        string.Join(" -> ", path),
                        [UnknownCode],
                        "Dynamic",
                        "UnsupportedDiagnosticBagHelper",
                        "DiagnosticBag helper source was not available to inspect.")
                ];
            }

            var helperModel = helperIndex.ModelFor(declaration);
            var helperSubstitutions = MapArguments(invocation, model, method, substitutions);
            var leaves = new List<LeafEmission>();
            foreach (var child in declaration.DescendantNodes()
                         .Where(node => node is InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax)
                         .OrderBy(child => child.SpanStart))
            {
                if (child is InvocationExpressionSyntax childInvocation)
                {
                    leaves.AddRange(AnalyzeInvocation(childInvocation, helperModel, helperIndex, helperSubstitutions, path));
                    continue;
                }

                if (child is BaseObjectCreationExpressionSyntax childCreation)
                {
                    leaves.AddRange(AnalyzeDiagnosticConstructor(
                        childCreation,
                        helperModel,
                        helperSubstitutions,
                        topLevelSink: "Diagnostic..ctor",
                        helperPath: string.Join(" -> ", path.Add("Diagnostic..ctor"))));
                }
            }

            return leaves;
        }

        private static bool TryAnalyzePrimitiveReport(
            InvocationExpressionSyntax invocation,
            SemanticModel model,
            IMethodSymbol method,
            ImmutableDictionary<IParameterSymbol, ExpressionWithModel> substitutions,
            ImmutableArray<string> helperPath,
            out LeafEmission? leaf)
        {
            leaf = null;
            var severity = method.Name switch
            {
                "ReportError" or "ReportErrorWithFix" => "Error",
                "ReportWarning" or "ReportWarningWithFix" => "Warning",
                "ReportInfo" => "Info",
                "Report" or "ReportWithFix" or "ReportVerification" =>
                    ResolveSeverity(invocation.ArgumentList.Arguments, method, model, substitutions),
                _ => null
            };

            if (severity is null)
                return false;
            if (severity is "Warning" or "Info")
                return true;

            var code = ResolveArgument(invocation.ArgumentList.Arguments, method, model, substitutions, "code", 1);
            var codes = code is null ? [UnknownCode] : ResolveStringValues(code.Value);
            leaf = new LeafEmission(
                $"DiagnosticBag.{method.Name}",
                string.Join(" -> ", helperPath),
                codes,
                severity,
                codes.Contains(UnknownCode, StringComparer.Ordinal) ? "Unclassified" : "Candidate",
                codes.Contains(UnknownCode, StringComparer.Ordinal)
                    ? "Error-capable diagnostic source site has a nonconstant diagnostic code."
                    : "");
            return true;
        }

        private static IReadOnlyList<LeafEmission> AnalyzeDiagnosticConstructor(
            BaseObjectCreationExpressionSyntax creation,
            SemanticModel model,
            ImmutableDictionary<IParameterSymbol, ExpressionWithModel> substitutions,
            string topLevelSink,
            string helperPath)
        {
            var constructor = ResolveConstructor(creation, model);
            if (constructor is null || constructor.ContainingType.ToDisplayString(TypeFormat)
                is not ("Calor.Compiler.Diagnostics.Diagnostic" or "Calor.Compiler.Diagnostics.DiagnosticWithFix"))
                return [];

            var severity = ResolveSeverity(creation.ArgumentList?.Arguments ?? [], constructor, model, substitutions);
            if (severity is "Warning" or "Info")
                return [];

            var code = ResolveArgument(creation.ArgumentList?.Arguments ?? [], constructor, model, substitutions, "code", 0);
            var codes = code is null ? [UnknownCode] : ResolveStringValues(code.Value);
            return
            [
                new LeafEmission(
                    topLevelSink,
                    helperPath,
                    codes,
                    severity,
                    codes.Contains(UnknownCode, StringComparer.Ordinal) ? "Unclassified" : "Candidate",
                    codes.Contains(UnknownCode, StringComparer.Ordinal)
                        ? "Error-capable diagnostic constructor has a nonconstant diagnostic code."
                        : "")
            ];
        }

        private static LeafEmission AnalyzeForwarder(
            InvocationExpressionSyntax invocation,
            SemanticModel model,
            IMethodSymbol method,
            ImmutableArray<string> helperPath)
        {
            var member = GetMember(invocation, model);
            if (member == "Calor.Compiler.Binding.BindingDiagnosticPolicy.PropagateCompilationErrors"
                && method.Name == "Add")
            {
                return new LeafEmission(
                    $"DiagnosticBag.{method.Name}",
                    string.Join(" -> ", helperPath.Prepend("BindingDiagnosticPolicy.PropagateCompilationErrors")),
                    [ForwardedBindingCompilationErrors],
                    "Forwarded",
                    "CompilationErrorForwarder",
                    "");
            }

            return new LeafEmission(
                $"DiagnosticBag.{method.Name}",
                string.Join(" -> ", helperPath),
                [ForwardedBindingCompilationErrors],
                "Forwarded",
                "UnsupportedForwarding",
                "Only BindingDiagnosticPolicy.PropagateCompilationErrors may forward pre-constructed diagnostics.");
        }

        private static ImmutableDictionary<IParameterSymbol, ExpressionWithModel> MapArguments(
            InvocationExpressionSyntax invocation,
            SemanticModel model,
            IMethodSymbol method,
            ImmutableDictionary<IParameterSymbol, ExpressionWithModel> callerSubstitutions)
        {
            var builder = ImmutableDictionary.CreateBuilder<IParameterSymbol, ExpressionWithModel>(SymbolEqualityComparer.Default);
            foreach (var parameter in method.Parameters)
            {
                var argument = FindArgument(invocation.ArgumentList.Arguments, method, parameter.Name, parameter.Ordinal);
                if (argument is not null)
                    builder[parameter] = ResolveExpression(argument.Expression, model, callerSubstitutions);
            }

            return builder.ToImmutable();
        }

        private static ExpressionWithModel? ResolveArgument(
            SeparatedSyntaxList<ArgumentSyntax> arguments,
            IMethodSymbol method,
            SemanticModel model,
            ImmutableDictionary<IParameterSymbol, ExpressionWithModel> substitutions,
            string parameterName,
            int fallbackOrdinal)
        {
            var parameter = method.Parameters.FirstOrDefault(candidate => candidate.Name == parameterName)
                ?? (fallbackOrdinal < method.Parameters.Length ? method.Parameters[fallbackOrdinal] : null);
            if (parameter is null)
                return null;

            var argument = FindArgument(arguments, method, parameter.Name, parameter.Ordinal);
            if (argument is not null)
                return ResolveExpression(argument.Expression, model, substitutions);

            if (substitutions.TryGetValue(parameter, out var substituted))
                return substituted;

            return null;
        }

        private static ArgumentSyntax? FindArgument(
            SeparatedSyntaxList<ArgumentSyntax> arguments,
            IMethodSymbol method,
            string parameterName,
            int ordinal)
        {
            foreach (var argument in arguments)
            {
                if (argument.NameColon?.Name.Identifier.ValueText == parameterName)
                    return argument;
            }

            return ordinal < arguments.Count && arguments[ordinal].NameColon is null ? arguments[ordinal] : null;
        }

        private static ExpressionWithModel ResolveExpression(
            ExpressionSyntax expression,
            SemanticModel model,
            ImmutableDictionary<IParameterSymbol, ExpressionWithModel> substitutions)
        {
            if (model.GetSymbolInfo(expression).Symbol is IParameterSymbol parameter
                && substitutions.TryGetValue(parameter, out var replacement))
                return replacement;

            return new ExpressionWithModel(expression, model);
        }

        private static string ResolveSeverity(
            SeparatedSyntaxList<ArgumentSyntax> arguments,
            IMethodSymbol method,
            SemanticModel model,
            ImmutableDictionary<IParameterSymbol, ExpressionWithModel> substitutions)
        {
            var severityParameter = method.Parameters.FirstOrDefault(parameter => parameter.Name == "severity");
            if (severityParameter is null)
                return "Error";

            var argument = FindArgument(arguments, method, severityParameter.Name, severityParameter.Ordinal);
            if (argument is not null)
                return ResolveSeverity(ResolveExpression(argument.Expression, model, substitutions));

            if (substitutions.TryGetValue(severityParameter, out var substituted))
                return ResolveSeverity(substituted);

            if (severityParameter.HasExplicitDefaultValue)
                return SeverityName(severityParameter.ExplicitDefaultValue);

            return "Dynamic";
        }

        private static string ResolveSeverity(ExpressionWithModel expression)
        {
            var symbol = expression.Model.GetSymbolInfo(expression.Expression).Symbol;
            if (symbol is IFieldSymbol { ContainingType.Name: "DiagnosticSeverity" } field)
                return field.Name;

            var constant = expression.Model.GetConstantValue(expression.Expression);
            return constant.HasValue ? SeverityName(constant.Value) : "Dynamic";
        }

        private static string SeverityName(object? value) =>
            value switch
            {
                0 => "Error",
                1 => "Warning",
                2 => "Info",
                CalorDiagnosticSeverity.Error => "Error",
                CalorDiagnosticSeverity.Warning => "Warning",
                CalorDiagnosticSeverity.Info => "Info",
                _ => "Dynamic"
            };

        private static string[] ResolveStringValues(ExpressionWithModel expression)
        {
            var constant = expression.Model.GetConstantValue(expression.Expression);
            if (constant is { HasValue: true, Value: string value })
                return [value];

            if (expression.Model.GetSymbolInfo(expression.Expression).Symbol is IFieldSymbol field
                && field.HasConstantValue
                && field.ConstantValue is string fieldValue)
                return [fieldValue];

            return [UnknownCode];
        }

        private static IMethodSymbol? ResolveMethod(InvocationExpressionSyntax invocation, SemanticModel model)
        {
            var info = model.GetSymbolInfo(invocation);
            return info.Symbol as IMethodSymbol
                ?? info.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
        }

        private static IMethodSymbol? ResolveConstructor(BaseObjectCreationExpressionSyntax creation, SemanticModel model)
        {
            var info = model.GetSymbolInfo(creation);
            return info.Symbol as IMethodSymbol
                ?? info.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
        }

        private static bool IsDiagnosticBagMethod(IMethodSymbol method) =>
            method.ContainingType.ToDisplayString(TypeFormat) == "Calor.Compiler.Diagnostics.DiagnosticBag";

        private static string GetMember(SyntaxNode node, SemanticModel model)
        {
            var symbol = model.GetEnclosingSymbol(node.SpanStart);
            if (symbol is IMethodSymbol method)
                return $"{method.ContainingType.ToDisplayString(TypeFormat)}.{method.Name}";

            return "<unknown-member>";
        }

        private static string ShortMember(string member)
        {
            const string prefix = "Calor.Compiler.Binding.";
            return member.StartsWith(prefix, StringComparison.Ordinal) ? member[prefix.Length..] : member;
        }

        private static MetadataReference[] References()
        {
            var trustedPlatformAssemblies =
                ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?.Split(Path.PathSeparator)
                ?? [];
            var references = trustedPlatformAssemblies
                .Select(path => MetadataReference.CreateFromFile(path))
                .Cast<MetadataReference>()
                .ToList();
            references.Add(MetadataReference.CreateFromFile(typeof(CalorDiagnostic).Assembly.Location));
            return references
                .GroupBy(reference => reference.Display ?? "", StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();
        }

        private sealed class HelperIndex
        {
            private readonly CSharpCompilation _compilation;
            private readonly Dictionary<string, MethodDeclarationSyntax> _methods = new(StringComparer.Ordinal);

            public HelperIndex(CSharpCompilation compilation)
            {
                _compilation = compilation;
                foreach (var tree in compilation.SyntaxTrees)
                {
                    var model = compilation.GetSemanticModel(tree);
                    foreach (var method in tree.GetCompilationUnitRoot()
                                 .DescendantNodes()
                                 .OfType<MethodDeclarationSyntax>())
                    {
                        if (model.GetDeclaredSymbol(method) is not IMethodSymbol symbol)
                            continue;

                        var key = MethodKey(symbol);
                        _methods.TryAdd(key, method);
                    }
                }
            }

            public MethodDeclarationSyntax? Find(IMethodSymbol method)
            {
                _methods.TryGetValue(MethodKey(method), out var declaration);
                return declaration;
            }

            public SemanticModel ModelFor(SyntaxNode node) =>
                _compilation.GetSemanticModel(node.SyntaxTree);

            private static string MethodKey(IMethodSymbol symbol) =>
                symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        }

        private sealed record SourceInput(string Path, string Text);

        private readonly record struct ExpressionWithModel(ExpressionSyntax Expression, SemanticModel Model);

        private sealed record LeafEmission(
            string TopLevelSink,
            string HelperPath,
            string[] PossibleCodes,
            string Severity,
            string Disposition,
            string Reason);
    }
}
