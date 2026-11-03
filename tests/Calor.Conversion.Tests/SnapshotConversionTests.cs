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
        File.WriteAllText(Path.Combine(snapshotDir, snapshotName), content);
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
