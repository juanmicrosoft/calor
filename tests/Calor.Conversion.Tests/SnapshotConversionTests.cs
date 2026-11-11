using Calor.Compiler.Migration;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;
using Xunit.Abstractions;

namespace Calor.Conversion.Tests;

/// <summary>
/// Tests current conversion output separately from faithful historical snapshot inputs.
/// </summary>
public class SnapshotConversionTests
{
    private readonly ITestOutputHelper _output;

    public SnapshotConversionTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public static IEnumerable<object[]> SupportedSnippetData()
    {
        foreach (var snippet in ConversionCatalog.SupportedSnippets)
            yield return new object[] { snippet.Id, snippet.Description, snippet.CSharpSource };
    }

    public static IEnumerable<object[]> StaticModuleSnippetData() =>
        SupportedSnippetData().Where(row => row[0] is "01-04" or "07-04" or "10-01" or "10-02" or "12-05" or "13-04");

    public static IEnumerable<object[]> CurrentSnapshotData() =>
        Calor.Compiler.Tests.ActiveSyntaxInputs.OriginalPaths
            .Where(path => path.StartsWith("tests/Calor.Conversion.Tests/Snapshots/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal).Select(path => new object[] { path });

    [Theory]
    [InlineData("09-03")]
    [InlineData("10-01")]
    [InlineData("11-05")]
    [InlineData("12-01")]
    public void NativeProducer_RoundTripsWithoutInterop(string id)
    {
        var snippet = ConversionCatalog.SupportedSnippets.Single(snippet => snippet.Id == id);
        var result = TestHelpers.ConvertCSharp(snippet.CSharpSource, $"Test_{id.Replace("-", "_")}");
        Assert.True(result.Success, string.Join("\n", result.Issues.Select(issue => issue.Message)));
        Assert.Equal(0, result.InteropPreservationCount);
        Assert.Equal(0, result.LossySubstitutionCount);
        Assert.Equal(0, result.DropCount);
        Assert.NotNull(result.CalorSource);
        Assert.DoesNotContain("§", result.CalorSource);

        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(result.CalorSource, diagnostics).TokenizeAllForParser();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var module = new Parser(tokens, diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var roundTrip = new CalorEmitter().Emit(module);
        var secondDiagnostics = new DiagnosticBag();
        var secondTokens = new Lexer(roundTrip, secondDiagnostics).TokenizeAllForParser();
        Assert.False(secondDiagnostics.HasErrors, string.Join("\n", secondDiagnostics));
        var second = new Parser(secondTokens, secondDiagnostics).Parse();
        Assert.False(secondDiagnostics.HasErrors, string.Join("\n", secondDiagnostics));
        var generated = new CSharpEmitter().Emit(module);
        Assert.Equal(generated, new CSharpEmitter().Emit(second));
        var validation = TestHelpers.RoslynCompile(generated);
        if (id == "09-03")
        {
            // The original broad slice arm already makes [1, 2, ..] unreachable.
            var originalError = Assert.Single(TestHelpers.RoslynCompile(snippet.CSharpSource).CompilationErrors);
            var generatedError = Assert.Single(validation.CompilationErrors);
            Assert.Equal("CS8510", originalError.Id);
            Assert.Equal(originalError.Id, generatedError.Id);
            Assert.Equal("[1, 2, ..]", ErrorPattern(originalError));
            Assert.Equal(ErrorPattern(originalError), ErrorPattern(generatedError));
        }
        else
        {
            Assert.True(validation.CompilationSuccess, string.Join("\n", validation.FormattedCompilationErrors));
        }

        static string ErrorPattern(Microsoft.CodeAnalysis.Diagnostic diagnostic) =>
            diagnostic.Location.SourceTree!.GetRoot().FindNode(diagnostic.Location.SourceSpan)
                .WithoutTrivia().NormalizeWhitespace().ToFullString();
    }

    [Fact]
    public void NativeProducer_RuntimePreservesEvaluationAndSliceBindings()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            namespace NativeProducerRuntime;
            public static class NativeOutputs
            {
                private static int trace;
                private static int Mark(int value) { trace = trace * 10 + value; return value; }
                private static int[] Items() { Mark(5); return new int[] { 6, 7, 8 }; }
                private static int Index() { Mark(6); return 1; }
                private static int @sizeof(int value) => value + 1;
                public static string Run()
                {
                    trace = 0;
                    var values = new Dictionary<int, int>
                    {
                        { Mark(1), Mark(2) },
                        { Mark(3), Mark(4) }
                    };
                    var text = $"{Items()[Index()]}";
                    return $"{values[1]}:{values[3]}:{text}:{trace}:{sizeof(int)}:{@sizeof(7)}";
                }
                public static int Duplicate()
                {
                    trace = 0;
                    try
                    {
                        var values = new Dictionary<int, int>
                        {
                            { Mark(1), Mark(2) },
                            { Mark(1), Mark(3) },
                            { Mark(4), Mark(5) }
                        };
                    }
                    catch (ArgumentException) { return trace; }
                    return -1;
                }
                public static int Middle(int[] values) => values switch
                {
                    [] => -1,
                    [var single] => single,
                    [var first, .. var middle, var last] => first + middle.Length * 10 + last,
                    _ => 0
                };
                public static int Prefix(int[] values) => values switch
                {
                    [.. var prefix, var last] => prefix.Length * 10 + last,
                    _ => -1
                };
                public static int Suffix(int[] values) => values switch
                {
                    [var first, .. var suffix] => first * 10 + suffix.Length,
                    _ => -1
                };
                public static int Nested(int[][] values) => values switch
                {
                    [[var first], ..] => first,
                    _ => -1
                };
                public static int Discard(int[] values)
                {
                    int _ = 7;
                    return values switch { [..] => _, _ => 0 };
                }
            }
            """;
        AssertRuntime(source);
        var conversion = TestHelpers.ConvertCSharp(source, "NativeProducerRuntime");
        _output.WriteLine(conversion.CalorSource);
        foreach (var issue in conversion.Issues)
            _output.WriteLine(issue.Message);
        Assert.True(conversion.Success, string.Join("\n", conversion.Issues.Select(issue => issue.Message)));
        Assert.Equal(0, conversion.InteropPreservationCount);
        Assert.Equal(0, conversion.LossySubstitutionCount);
        Assert.Equal(0, conversion.DropCount);
        Assert.NotNull(conversion.CalorSource);
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(conversion.CalorSource, diagnostics).TokenizeAllForParser();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var module = new Parser(tokens, diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        foreach (var native in new[] { conversion.CalorSource, new CalorEmitter().Emit(module) })
        {
            foreach (var typeChecking in new[] { false, true })
            {
                var compiled = Calor.Compiler.Program.Compile(native, "native-producer.calr",
                    options: new Calor.Compiler.CompilationOptions { EnableTypeChecking = typeChecking });
                Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics) + "\n" + native);
                Assert.NotNull(compiled.GeneratedCode);
                AssertRuntime(compiled.GeneratedCode);
            }
        }

        static void AssertRuntime(string csharp)
        {
            var compilation = CSharpCompilation.Create("NativeProducer_" + Guid.NewGuid().ToString("N"),
                [CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble),
                    CSharpSyntaxTree.ParseText(csharp)],
                GeneratedCSharpCompiler.References,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var stream = new MemoryStream();
            var emitted = compilation.Emit(stream);
            Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
            var context = new System.Runtime.Loader.AssemblyLoadContext(compilation.AssemblyName, isCollectible: true);
            try
            {
                stream.Position = 0;
                var type = context.LoadFromStream(stream).GetType("NativeProducerRuntime.NativeOutputs");
                Assert.NotNull(type);
                Assert.Equal("2:4:7:123456:4:8", type.GetMethod("Run")!.Invoke(null, null));
                Assert.Equal(1213, type.GetMethod("Duplicate")!.Invoke(null, null));
                foreach (var (values, middle, prefix, suffix) in new[]
                {
                    (Array.Empty<int>(), -1, -1, -1),
                    (new[] { 9 }, 9, 9, 90),
                    (new[] { 2, 3 }, 5, 13, 21),
                    (new[] { 2, 4, 5, 3 }, 25, 33, 23)
                })
                {
                    Assert.Equal(middle, type.GetMethod("Middle")!.Invoke(null, [values]));
                    Assert.Equal(prefix, type.GetMethod("Prefix")!.Invoke(null, [values]));
                    Assert.Equal(suffix, type.GetMethod("Suffix")!.Invoke(null, [values]));
                }
                Assert.Equal(7, type.GetMethod("Nested")!.Invoke(null,
                    [new[] { new[] { 7 }, new[] { 2, 3 } }]));
                Assert.Equal(-1, type.GetMethod("Nested")!.Invoke(null,
                    [new[] { new[] { 7, 8 } }]));
                Assert.Equal(7, type.GetMethod("Discard")!.Invoke(null, [Array.Empty<int>()]));
            }
            finally
            {
                context.Unload();
            }
        }
    }

    [Theory]
    [InlineData("List")]
    [InlineData("HashSet")]
    public void NativeProducer_CollectionSwitchArmsStayLazyAndSingleValued(string collection)
    {
        var source = $$"""
            using System;
            using System.Collections.Generic;
            public static class ArmProbe
            {
                public static int Trace;
                private static int Next(int value) { Trace = Trace * 10 + value; return value; }
                public static {{collection}}<int> Run(int category)
                {
                    Trace = 0;
                    return Next(category) switch
                    {
                        1 => new {{collection}}<int> { Next(2), Next(3) },
                        2 => new {{collection}}<int> { Next(4), Next(5) },
                        _ => new {{collection}}<int>()
                    };
                }
            }
            """;
        const string moduleName = "CollectionSwitch";
        var conversion = TestHelpers.ConvertCSharp(source, moduleName);
        Assert.True(conversion.Success, string.Join("; ", conversion.Issues.Select(issue => issue.Message)));
        Assert.Empty(conversion.Losses);
        Assert.NotNull(conversion.CalorSource);
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(conversion.CalorSource, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.Empty(diagnostics);
        foreach (var native in new[] { conversion.CalorSource, new CalorEmitter().Emit(module) })
        {
            foreach (var typeChecking in new[] { false, true })
            {
                var compiled = Calor.Compiler.Program.Compile(native, "collection-switch.calr",
                    new Calor.Compiler.CompilationOptions { EnableTypeChecking = typeChecking });
                Assert.False(compiled.HasErrors, string.Join("; ", compiled.Diagnostics.Errors));
                foreach (var (category, expected) in new[] { (0, "0:"), (1, "123:2,3"), (2, "245:4,5"), (-1, "-1:") })
                {
                    var driver = $"var values = $N$ArmProbe.Run({category}); return $N$ArmProbe.Trace + \":\" + System.String.Join(\",\", values);";
                    Assert.Equal(expected, RunSnapshotProbe(source, driver, ""));
                    Assert.Equal(expected, RunSnapshotProbe(compiled.GeneratedCode, driver, moduleName + "."));
                }
            }
        }
    }

    private static string RunSnapshotProbe(string source, string driver, string ownerPrefix, string[]? symbols = null)
    {
        var probe = "public static class CurrentSnapshotProbe { public static string Run() { "
            + driver.Replace("$N$", ownerPrefix, StringComparison.Ordinal) + " } }";
        var compilation = CSharpCompilation.Create("SnapshotRuntime_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble),
                CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(preprocessorSymbols: symbols)),
                CSharpSyntaxTree.ParseText(probe)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        using var stream = new MemoryStream();
        var emission = compilation.Emit(stream);
        Assert.True(emission.Success, string.Join("; ", emission.Diagnostics));
        var context = new System.Runtime.Loader.AssemblyLoadContext(compilation.AssemblyName, isCollectible: true);
        try
        {
            stream.Position = 0;
            var type = context.LoadFromStream(stream).GetType("CurrentSnapshotProbe");
            Assert.NotNull(type);
            return Assert.IsType<string>(type.GetMethod("Run")!.Invoke(null, null));
        }
        finally
        {
            context.Unload();
        }
    }

    [Fact]
    public void NativeProducer_RejectsDuplicateListSlicesAtTheirSourceSpan()
    {
        const string source = """
            module DuplicateSlices
              func Test (int[]:values) → int
                return match values
                  case [.., ..] → 1
                  case _ → 0
            """;
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, diagnostics).TokenizeAllForParser();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        new Parser(tokens, diagnostics).Parse();
        var error = Assert.Single(diagnostics.Errors);
        Assert.Equal(DiagnosticCode.UnexpectedToken, error.Code);
        Assert.Equal(source.LastIndexOf("..", StringComparison.Ordinal), error.Span.Start);
        Assert.Equal(2, error.Span.Length);
    }

    [Theory]
    [InlineData("[.. var slice]", true)]
    [InlineData("[..]", false)]
    public void NativeProducer_ArraySliceAllocationRequiresItsEffectRow(string pattern, bool allocates)
    {
        var source = $$"""
            module SliceEffects
              func Read (int[]:values) → int
                !
                return match values
                  case {{pattern}} → 1
                  case _ → 0
            """;
        var compiled = Calor.Compiler.Program.Compile(source);
        Assert.DoesNotContain(compiled.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
        if (allocates)
        {
            Assert.True(compiled.HasErrors);
            var error = Assert.Single(compiled.Diagnostics.Errors);
            Assert.Equal(DiagnosticCode.ForbiddenEffect, error.Code);
            Assert.Contains("'alloc'", error.Message);
        }
        else
        {
            Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        }
    }

    [Fact]
    public void NativeProducer_NonArrayListPatternDoesNotAcquireArrayAuthority()
    {
        const string source = """
            module NonArrayPattern
              func Read (string:value) → int
                !
                return match value
                  case [..] → 1
                  case _ → 0
            """;
        var compiled = Calor.Compiler.Program.Compile(source);
        Assert.True(compiled.HasErrors);
        Assert.Contains(compiled.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall
            && diagnostic.Message.Contains("list-pattern members", StringComparison.Ordinal));
        Assert.Contains(compiled.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
            && diagnostic.Message.Contains("'unknown'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("07-04", 7)]
    [InlineData("13-03", 7)]
    public void CallbackContracts_RetainNativeMethodsAndConversionCounts(string id, int nativeCount)
    {
        var snippet = ConversionCatalog.SupportedSnippets.Single(snippet => snippet.Id == id);
        var result = TestHelpers.ConvertCSharp(snippet.CSharpSource, $"Test_{id.Replace("-", "_")}");
        Assert.True(result.Success);
        Assert.Equal(nativeCount, result.NativeConversionCount);
        Assert.Equal(0, result.InteropPreservationCount);
        Assert.Equal(0, result.LossySubstitutionCount);
        Assert.Equal(0, result.DropCount);
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(result.CalorSource!, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var methods = Assert.Single(module.Classes).Methods;
        Assert.Equal(2, methods.Count);
        if (id == "07-04")
            Assert.Null(methods.Single(method => method.Name == "Handle").Parameters[1].Row);
        else
            Assert.Null(methods.Single(method => method.Name == "GetComparer").Output!.Row);
    }

    [Theory]
    [MemberData(nameof(CurrentSnapshotData))]
    public void CurrentSnapshot_PreservesOriginalCurrentCompilerProjection(string original)
    {
        var root = Calor.Compiler.Tests.ActiveSyntaxInputs.RepositoryRoot;
        var originalPath = Path.Combine(root, original);
        var currentPath = Calor.Compiler.Tests.ActiveSyntaxInputs.ForCurrentCompiler(root, original);
        Assert.NotEqual(originalPath, currentPath);
        var before = File.ReadAllText(originalPath);
        var after = File.ReadAllText(currentPath);
        Assert.NotEqual(before, after);
        // Captured from unmodified producer 2c4b6b45b; today's parser rejects the frozen syntax.
        var fixturePath = Path.Combine(root, "tests/TestData/Conversion/snapshot-historical-projections.json");
        var expectations = JsonSerializer.Deserialize<Dictionary<string, SnapshotProjectionExpectation>>(
            File.ReadAllText(fixturePath))!;
        Assert.Equal(CurrentSnapshotData().Select(row => (string)row[0]), expectations.Keys.Order(StringComparer.Ordinal));
        var expected = expectations[original];
        Assert.Equal(expected.OriginalSource, before);

        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(after, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.Empty(diagnostics);
        foreach (var (mode, historical, escaped) in new[]
        {
            (EmitContractMode.Debug, expected.HistoricalDebugCSharp, expected.EscapedDebugCSharp),
            (EmitContractMode.Off, expected.HistoricalOffCSharp, expected.EscapedOffCSharp)
        })
        {
            var actual = new CSharpEmitter(mode).Emit(module);
            if (original.EndsWith("/13-02.approved.calr", StringComparison.Ordinal))
            {
                Assert.Contains(CSharpSyntaxTree.ParseText(historical).GetDiagnostics(),
                    diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
                Assert.NotNull(escaped);
                Assert.Equal(escaped, actual);
            }
            else
            {
                Assert.Null(escaped);
                var optionalConstructorParentheses = original.EndsWith("/13-08.approved.calr", StringComparison.Ordinal);
                var liftedRawExpression = optionalConstructorParentheses
                    || original.EndsWith("/12-01.approved.calr", StringComparison.Ordinal);
                foreach (var symbols in new string[][] { [], ["NET6_0_OR_GREATER"] })
                    Assert.Equal(
                        SnapshotDeclarations(historical, optionalConstructorParentheses, liftedRawExpression, symbols),
                        SnapshotDeclarations(actual, optionalConstructorParentheses, liftedRawExpression, symbols));
            }
        }

        var production = Calor.Compiler.Program.Compile(after, currentPath);
        Assert.Equal(expected.NativeErrors,
            production.Diagnostics.Where(diagnostic => diagnostic.IsError)
                .Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
    }

    private sealed record SnapshotProjectionExpectation(
        string OriginalSource,
        string HistoricalDebugCSharp,
        string HistoricalOffCSharp,
        string? EscapedDebugCSharp,
        string? EscapedOffCSharp,
        string[] NativeErrors);

    private static string[] SnapshotDeclarations(
        string source, bool optionalConstructorParentheses, bool liftedRawExpression, string[] symbols)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(preprocessorSymbols: symbols));
        var root = tree.GetCompilationUnitRoot();
        var unusedImports = Array.Empty<Microsoft.CodeAnalysis.Text.TextSpan>();
        if (liftedRawExpression)
        {
            var compilation = CSharpCompilation.Create("SnapshotRawLifting", [tree],
                GeneratedCSharpCompiler.References, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var diagnostics = compilation.GetDiagnostics();
            Assert.DoesNotContain(diagnostics,
                diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
            // Retired raw expressions requested extra backend imports; remove only Roslyn-proven unused ones.
            unusedImports = diagnostics.Where(diagnostic => diagnostic.Id == "CS8019")
                .Select(diagnostic => diagnostic.Location.SourceSpan).ToArray();
        }
        var declarations = new List<string>();
        foreach (var directive in root.DescendantTrivia(descendIntoTrivia: true)
            .Select(trivia => trivia.GetStructure()).OfType<DirectiveTriviaSyntax>()
            .Where(directive => directive is IfDirectiveTriviaSyntax or ElifDirectiveTriviaSyntax
                or ElseDirectiveTriviaSyntax or EndIfDirectiveTriviaSyntax))
            declarations.Add(Project(directive));
        AddMembers(root.Members, root.Usings, "");
        return declarations.ToArray();

        void AddMembers(SyntaxList<MemberDeclarationSyntax> members, SyntaxList<UsingDirectiveSyntax> usings, string owner)
        {
            declarations.AddRange(usings.Where(@using => !unusedImports.Any(span => span.IntersectsWith(@using.Span)))
                .Select(@using => owner + ":" + Project(@using)));
            foreach (var member in members)
            {
                if (member is BaseNamespaceDeclarationSyntax ns)
                    AddMembers(ns.Members, ns.Usings, string.IsNullOrEmpty(owner) ? ns.Name.ToString() : owner + "." + ns.Name);
                else
                    declarations.Add(owner + ":" + Project(member));
            }
        }

        string Project(SyntaxNode syntax)
        {
            var result = new StringBuilder();
            Append(syntax);
            return result.ToString();

            void Append(SyntaxNodeOrToken element)
            {
                if (element.AsNode() is not { } node)
                {
                    var token = element.AsToken();
                    result.Append('T').Append(token.RawKind).Append(token.IsMissing ? 'M' : 'P').Append(':')
                        .Append(token.Text.Length).Append(':').Append(token.Text);
                    return;
                }
                if (node is ParenthesizedExpressionSyntax parenthesized)
                {
                    Append(parenthesized.Expression);
                    return;
                }
                if (optionalConstructorParentheses && node is ObjectCreationExpressionSyntax { ArgumentList: null } creation)
                    node = creation.WithArgumentList(SyntaxFactory.ArgumentList());
                result.Append('N').Append(node.RawKind).Append('[');
                foreach (var child in node.ChildNodesAndTokens())
                    Append(child);
                result.Append(']');
            }
        }
    }

    [Fact]
    public void SnapshotProjection_PreservesGroupingAndConstructorArguments()
    {
        string Project(string source, bool optionalConstructorParentheses = false) =>
            Assert.Single(SnapshotDeclarations(source, optionalConstructorParentheses, false, []));

        Assert.Equal(
            Project("class C { int M(int x) => x + 1; }"),
            Project("class C { int M(int x) => ((x + 1)); }"));
        Assert.NotEqual(
            Project("class C { int M(int a, int b, int c) => (a + b) * c; }"),
            Project("class C { int M(int a, int b, int c) => a + (b * c); }"));

        const string absent = "class C { C M() => new C {}; }";
        const string empty = "class C { C M() => new C() {}; }";
        Assert.NotEqual(Project(absent), Project(empty));
        Assert.Equal(Project(absent, true), Project(empty, true));
        Assert.NotEqual(
            Project("class C { C(int x) {} C M() => new C(1); }", true),
            Project("class C { C(int x) {} C M() => new C(2); }", true));
    }

    [Theory]
    [MemberData(nameof(StaticModuleSnippetData))]
    public void StaticModuleSnapshot_PreservesCSharpDeclarationSignatures(string id, string description, string csharpSource)
    {
        var diagnostics = new DiagnosticBag();
        var approved = TestHelpers.ReadSnapshot($"{id}.approved.calr")!;
        var module = new Parser(new Lexer(approved, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, $"{description}: " + string.Join("\n", diagnostics.Errors));
        var generated = new CSharpEmitter().Emit(module);
        // Snapshot conversion explicitly assigns this namespace to namespace-free snippets.
        var originalInSnapshotNamespace = $"namespace Test_{id.Replace("-", "_")} {{\n{csharpSource}\n}}";
        Assert.Equal(Signatures(originalInSnapshotNamespace), Signatures(generated));
    }

    private static string[] Signatures(string source)
    {
        var compilation = CSharpCompilation.Create("SignatureControl",
            [CSharpSyntaxTree.ParseText(source)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var signatures = new List<string>();
        Collect(compilation.Assembly.GlobalNamespace);
        return signatures.Order(StringComparer.Ordinal).ToArray();

        void Collect(INamespaceOrTypeSymbol owner)
        {
            foreach (var member in owner.GetMembers())
            {
                if (member is INamespaceSymbol ns)
                    Collect(ns);
                else if (member is INamedTypeSymbol type)
                {
                    signatures.Add($"{type.ToDisplayString()}:{type.DeclaredAccessibility}:{type.IsStatic}:{type.IsAbstract}:{type.IsSealed}");
                    Collect(type);
                }
                else if (!member.IsImplicitlyDeclared)
                {
                    var resultType = member switch
                    {
                        IMethodSymbol method => method.ReturnType.ToDisplayString(),
                        IFieldSymbol field => field.Type.ToDisplayString(),
                        IPropertySymbol property => property.Type.ToDisplayString(),
                        _ => ""
                    };
                    signatures.Add($"{member.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)}:{member.DeclaredAccessibility}:{member.IsStatic}:{resultType}");
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(SupportedSnippetData))]
    public void Convert_ProducesCalorOutput(string id, string description, string csharpSource)
    {
        var result = TestHelpers.ConvertCSharp(csharpSource, $"Test_{id.Replace("-", "_")}");

        _output.WriteLine($"[{id}] {description}");
        if (result.CalorSource != null)
            _output.WriteLine(result.CalorSource);

        Assert.True(result.Success,
            $"Conversion failed for [{id}] {description}: " +
            string.Join("; ", result.Issues.Select(i => i.Message)));
        Assert.NotNull(result.CalorSource);
        Assert.NotEmpty(result.CalorSource!);
    }

    [Theory]
    [MemberData(nameof(SupportedSnippetData))]
    public void Convert_MatchesApprovedSnapshot(string id, string description, string csharpSource)
    {
        var fixturePath = Path.Combine(Calor.Compiler.Tests.ActiveSyntaxInputs.RepositoryRoot,
            "tests/TestData/Conversion/snapshot-current-output.json");
        var expectations = JsonSerializer.Deserialize<Dictionary<string, CurrentSnapshotExpectation>>(
            File.ReadAllText(fixturePath))!;
        Assert.Equal(SupportedSnippetData().Select(row => (string)row[0]), expectations.Keys);
        var expected = expectations[id];
        Assert.Equal(expected.CSharpSource, csharpSource);
        var moduleName = $"Test_{id.Replace("-", "_")}";
        var result = TestHelpers.ConvertCSharp(csharpSource, moduleName);

        Assert.True(result.Success,
            $"Conversion failed for [{id}] {description}: " +
            string.Join("; ", result.Issues.Select(i => i.Message)));
        Assert.NotNull(result.CalorSource);

        VerifySnapshot($"{id}.current", expected.CalorSource, result.CalorSource!, updateMode: false,
            (_, _) => throw new InvalidOperationException("Current-output fixtures require independent review."));
        Assert.Equal(expected.NativeConversionCount, result.NativeConversionCount);
        Assert.Equal(expected.InteropPreservationCount, result.InteropPreservationCount);
        Assert.Equal(expected.LossySubstitutionCount, result.LossySubstitutionCount);
        Assert.Equal(expected.DropCount, result.DropCount);
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(result.CalorSource!, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.Empty(diagnostics);
        var generated = new CSharpEmitter().Emit(module);
        var reemittedDiagnostics = new DiagnosticBag();
        var reemitted = new CalorEmitter().Emit(module);
        var reparsed = new Parser(new Lexer(reemitted, reemittedDiagnostics).TokenizeAllForParser(),
            reemittedDiagnostics).Parse();
        Assert.Empty(reemittedDiagnostics);
        Assert.Equal(generated, new CSharpEmitter().Emit(reparsed));

        foreach (var symbols in new string[][] { [], ["NET6_0_OR_GREATER"] })
        {
            var before = SnapshotCompilation(csharpSource, symbols);
            var after = SnapshotCompilation(generated, symbols);
            Assert.Equal(SnapshotErrors(before), SnapshotErrors(after));
            Assert.Equal(SnapshotApi(before, moduleName), SnapshotApi(after, moduleName));
            Assert.Equal(SnapshotCallTargets(before, moduleName), SnapshotCallTargets(after, moduleName));
        }

        var production = Calor.Compiler.Program.Compile(result.CalorSource!, $"{id}.calr");
        Assert.Equal(expected.NativeErrors, production.Diagnostics.Errors.Select(error => $"{error.Code}: {error.Message}"));
        if (expected.NativeErrors.Length == 0)
        {
            Assert.Contains(expected.ProbeKind, new[] { "behavior", "completion", "metadata" });
            Assert.NotNull(expected.RuntimeDriver);
            Assert.Equal(
                RunSnapshotProbe(csharpSource, expected.RuntimeDriver, "", expected.RuntimeSymbols),
                RunSnapshotProbe(production.GeneratedCode, expected.RuntimeDriver, moduleName + ".", expected.RuntimeSymbols));
        }
        else
        {
            Assert.Equal("rejected", expected.ProbeKind);
            Assert.Null(expected.RuntimeDriver);
            Assert.True(production.HasErrors);
            // Generated-code validation retains the rejected C# for diagnostics; never execute it.
            if (expected.NativeErrors.All(error => error.StartsWith("Calor1002:", StringComparison.Ordinal)))
                Assert.Equal(SnapshotDeclarations(generated, false, false, []),
                    SnapshotDeclarations(production.GeneratedCode, false, false, []));
            else
                Assert.Empty(production.GeneratedCode);
        }
    }

    private sealed record CurrentSnapshotExpectation(
        string CSharpSource, string CalorSource, int NativeConversionCount, int InteropPreservationCount,
        int LossySubstitutionCount, int DropCount, string[] NativeErrors, string ProbeKind,
        string? RuntimeDriver, string[] RuntimeSymbols);

    private static CSharpCompilation SnapshotCompilation(string source, string[] symbols) =>
        CSharpCompilation.Create("SnapshotContract_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(preprocessorSymbols: symbols))],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

    private static string[] SnapshotErrors(CSharpCompilation compilation) =>
        compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Select(diagnostic => $"{diagnostic.Id}: {diagnostic.GetMessage()}: "
                + diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan)).ToArray();

    private static string[] SnapshotApi(CSharpCompilation compilation, string moduleName)
    {
        var signatures = new List<string>();
        Collect(compilation.Assembly.GlobalNamespace);
        return signatures.Order(StringComparer.Ordinal).ToArray();

        string Name(ISymbol? symbol) =>
            (symbol?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) ?? "<null>")
                .Replace(moduleName + ".", "", StringComparison.Ordinal);
        string Parameters(IMethodSymbol method) => string.Join(",", method.Parameters.Select(parameter =>
            $"{parameter.RefKind}:{Name(parameter.Type)}:{parameter.NullableAnnotation}:{parameter.Name}:{parameter.IsParams}:{parameter.IsOptional}:"
                + (parameter.HasExplicitDefaultValue ? parameter.ExplicitDefaultValue?.ToString() ?? "null" : "-")));
        string Constraints(ITypeParameterSymbol parameter) =>
            $"{parameter.Name}:{parameter.Variance}:{parameter.HasReferenceTypeConstraint}:{parameter.ReferenceTypeConstraintNullableAnnotation}:"
            + $"{parameter.HasNotNullConstraint}:{parameter.HasValueTypeConstraint}:{parameter.HasUnmanagedTypeConstraint}:{parameter.HasConstructorConstraint}:"
            + string.Join(",", parameter.ConstraintTypes.Select(Name));
        void Collect(INamespaceOrTypeSymbol owner)
        {
            foreach (var symbol in owner.GetMembers())
            {
                if (symbol is INamespaceSymbol ns)
                    Collect(ns);
                else if (symbol is INamedTypeSymbol type)
                {
                    signatures.Add($"type:{Name(type)}:{type.TypeKind}:{type.DeclaredAccessibility}:{type.IsStatic}:{type.IsAbstract}:"
                        + $"{type.IsSealed}:{type.IsReadOnly}:{Name(type.BaseType)}:{string.Join(",", type.Interfaces.Select(Name))}:"
                        + $"{string.Join("|", type.TypeParameters.Select(Constraints))}:{string.Join(",", type.GetAttributes().Select(attribute => Name(attribute.AttributeClass)))}");
                    Collect(type);
                }
                else if (!symbol.IsImplicitlyDeclared)
                {
                    var head = $"{Name(symbol)}:{symbol.DeclaredAccessibility}:{symbol.IsStatic}";
                    switch (symbol)
                    {
                        case IMethodSymbol method when method.MethodKind is MethodKind.Ordinary or MethodKind.Constructor
                            or MethodKind.StaticConstructor or MethodKind.UserDefinedOperator or MethodKind.Conversion:
                            signatures.Add($"method:{head}:{method.MethodKind}:{Name(method.ReturnType)}:{method.ReturnNullableAnnotation}:"
                                + $"{method.RefKind}:{method.IsAbstract}:{method.IsVirtual}:{method.IsOverride}:{Parameters(method)}:"
                                + string.Join("|", method.TypeParameters.Select(Constraints)));
                            break;
                        case IPropertySymbol property:
                            signatures.Add($"property:{head}:{Name(property.Type)}:{property.NullableAnnotation}:{property.IsRequired}:"
                                + $"{property.GetMethod?.DeclaredAccessibility}:{property.SetMethod?.DeclaredAccessibility}:{property.SetMethod?.IsInitOnly}");
                            break;
                        case IFieldSymbol field:
                            signatures.Add($"field:{head}:{Name(field.Type)}:{field.NullableAnnotation}:{field.IsConst}:{field.ConstantValue}:{field.IsReadOnly}");
                            break;
                        case IEventSymbol @event:
                            signatures.Add($"event:{head}:{Name(@event.Type)}");
                            break;
                    }
                }
            }
        }
    }

    private static string[] SnapshotCallTargets(CSharpCompilation compilation, string moduleName)
    {
        var targets = new List<string>();
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        foreach (var node in tree.GetRoot().DescendantNodes().Where(node =>
            node is InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax))
        {
            var owner = node.Ancestors().FirstOrDefault(parent => parent is BaseMethodDeclarationSyntax
                or PropertyDeclarationSyntax || parent is VariableDeclaratorSyntax variable
                && variable.Parent?.Parent is FieldDeclarationSyntax);
            var ownerSymbol = owner == null ? null : model.GetDeclaredSymbol(owner);
            var binding = model.GetSymbolInfo(node);
            var kind = node is BaseObjectCreationExpressionSyntax ? "constructor" : "invocation";
            targets.Add($"{Name(ownerSymbol)}:{kind}:{Name(binding.Symbol)}:{binding.CandidateReason}:"
                + string.Join(",", binding.CandidateSymbols.Select(Name)));
        }
        return targets.Order(StringComparer.Ordinal).ToArray();

        string Name(ISymbol? symbol) =>
            (symbol?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) ?? "<null>")
                .Replace(moduleName + ".", "", StringComparison.Ordinal);
    }

    [Fact]
    public void Convert_AllSupportedSnippets_ProduceCalorWithModuleTag()
    {
        var failures = new List<string>();

        foreach (var snippet in ConversionCatalog.SupportedSnippets)
        {
            var moduleName = $"Test_{snippet.Id.Replace("-", "_")}";
            var result = TestHelpers.ConvertCSharp(snippet.CSharpSource, moduleName);

            if (!result.Success || result.CalorSource == null)
            {
                failures.Add($"[{snippet.Id}] {snippet.Description}: conversion failed");
                continue;
            }

            if (!result.CalorSource.StartsWith($"module {moduleName} ", StringComparison.Ordinal))
            {
                failures.Add($"[{snippet.Id}] {snippet.Description}: missing native module declaration");
                continue;
            }

            var diagnostics = new DiagnosticBag();
            var module = new Parser(new Lexer(result.CalorSource, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
            if (diagnostics.HasErrors)
                failures.Add($"[{snippet.Id}] {snippet.Description}: " + string.Join("; ", diagnostics.Errors));
            else if (module.Name != moduleName)
                failures.Add($"[{snippet.Id}] {snippet.Description}: module name changed to '{module.Name}'");
        }

        Assert.Empty(failures);
    }

    internal static void VerifySnapshot(
        string snapshotName,
        string? approved,
        string actual,
        bool updateMode,
        Action<string, string> writeSnapshot)
    {
        if (updateMode)
        {
            writeSnapshot(snapshotName, actual);
            return;
        }

        Assert.True(
            approved != null,
            $"Missing approved snapshot '{snapshotName}'. " +
            "Add an independently reviewed current-output fixture.");
        Assert.Equal(Normalize(approved!), Normalize(actual));

        static string Normalize(string source) => RawCSharpFrame.RewriteOutsideFrames(
            RawCSharpFrame.NormalizeLineEndings(source), envelope => envelope.Trim());
    }

    [Fact]
    public void SnapshotComparison_NormalizesOnlyTheEnvelope()
    {
        const string payload = "class C {}\r\n";
        var marker = new RawCSharpFrame(payload, "snapshot-comparison", "internal fidelity fixture").ReasonMarker;
        var header = $"module Snapshot\n  raw {marker} {Encoding.UTF8.GetByteCount(payload)}\n";
        var approved = header + payload;
        VerifySnapshot("raw", approved, header.Replace("\n", "\r\n", StringComparison.Ordinal) + payload,
            updateMode: false, (_, _) => throw new InvalidOperationException("Must not write."));

        const string differentPayload = "class C {}\n ";
        Assert.Equal(Encoding.UTF8.GetByteCount(payload), Encoding.UTF8.GetByteCount(differentPayload));
        Assert.NotNull(Record.Exception(() => VerifySnapshot("raw", approved, header + differentPayload,
            updateMode: false, (_, _) => throw new InvalidOperationException("Must not write."))));
    }

    [Fact]
    public void MissingSnapshot_FailsWithoutWriting()
    {
        var writeAttempted = false;

        var exception = Record.Exception(() => VerifySnapshot(
            "missing.approved.calr",
            approved: null,
            actual: "§M{Missing}",
            updateMode: false,
            (_, _) => writeAttempted = true));

        Assert.NotNull(exception);
        Assert.False(writeAttempted);
    }

}
