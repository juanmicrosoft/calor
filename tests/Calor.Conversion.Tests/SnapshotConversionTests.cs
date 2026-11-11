using Calor.Compiler.Migration;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using Xunit.Abstractions;

namespace Calor.Conversion.Tests;

/// <summary>
/// Tests that convert C# snippets from challenge reports and compare
/// against approved .calr snapshot files.
///
/// To update snapshots, set CALOR_UPDATE_SNAPSHOTS=1 and run tests.
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
        var originalProjection = TestHelpers.CompileCalorToCSharpWithDiagnostics(before);
        var currentProjection = TestHelpers.CompileCalorToCSharpWithDiagnostics(after);
        Assert.NotNull(originalProjection.GeneratedCode);
        Assert.NotNull(currentProjection.GeneratedCode);
        Assert.Empty(originalProjection.Diagnostics);
        Assert.Empty(currentProjection.Diagnostics);
        Assert.Equal(originalProjection.GeneratedCode, currentProjection.GeneratedCode);
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
        var snapshotName = $"{id}.approved.calr";
        var approved = TestHelpers.ReadSnapshot(snapshotName);

        var result = TestHelpers.ConvertCSharp(csharpSource, $"Test_{id.Replace("-", "_")}");

        Assert.True(result.Success,
            $"Conversion failed for [{id}] {description}: " +
            string.Join("; ", result.Issues.Select(i => i.Message)));
        Assert.NotNull(result.CalorSource);

        VerifySnapshot(
            snapshotName,
            approved,
            result.CalorSource!,
            Environment.GetEnvironmentVariable("CALOR_UPDATE_SNAPSHOTS") == "1",
            WriteSnapshot);
    }

    [Fact]
    public void Convert_AllSupportedSnippets_ProduceCalorWithModuleTag()
    {
        var failures = new List<string>();

        foreach (var snippet in ConversionCatalog.SupportedSnippets)
        {
            var result = TestHelpers.ConvertCSharp(snippet.CSharpSource, $"Test_{snippet.Id.Replace("-", "_")}");

            if (!result.Success || result.CalorSource == null)
            {
                failures.Add($"[{snippet.Id}] {snippet.Description}: conversion failed");
                continue;
            }

            if (!result.CalorSource.Contains("§M{"))
            {
                failures.Add($"[{snippet.Id}] {snippet.Description}: missing §M module tag");
            }
        }

        Assert.Empty(failures);
    }

    private static void WriteSnapshot(string snapshotName, string content)
    {
        // Write to the source directory (not the bin output)
        var projectDir = FindProjectDirectory();
        if (projectDir == null) return;

        var snapshotDir = Path.Combine(projectDir, "Snapshots");
        Directory.CreateDirectory(snapshotDir);
        var original = $"tests/Calor.Conversion.Tests/Snapshots/{snapshotName}";
        var activeInputs = Calor.Compiler.Tests.ActiveSyntaxInputs.OriginalPaths;
        var path = activeInputs.Contains(original)
            ? Calor.Compiler.Tests.ActiveSyntaxInputs.ForCurrentCompiler(
                Calor.Compiler.Tests.ActiveSyntaxInputs.RepositoryRoot, original)
            : Path.Combine(snapshotDir, snapshotName);
        File.WriteAllText(path, content);
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
            "Set CALOR_UPDATE_SNAPSHOTS=1 explicitly to create it.");
        Assert.Equal(
            approved!.Replace("\r\n", "\n").Trim(),
            actual.Replace("\r\n", "\n").Trim());
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

    private static string? FindProjectDirectory()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "Calor.Conversion.Tests.csproj")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }
}
