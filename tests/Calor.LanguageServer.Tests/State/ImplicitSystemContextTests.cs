using Calor.Compiler.CodeGen;
using Calor.Compiler.Migration;
using Calor.LanguageServer.State;
using OmniSharp.Extensions.LanguageServer.Protocol;
using Xunit;

namespace Calor.LanguageServer.Tests.State;

public sealed class ImplicitSystemContextTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EditorDiagnostics_ValidatePreservedSourceInSelectedContext(bool implicitSystem)
    {
        const string csharp = """
            namespace Host
            {
                using System.Threading.Tasks;
                public class Api { public Task Work() { Console.WriteLine("work"); return Task.CompletedTask; } }
            }
            """;
        var source = new CSharpToCalorConverter().Convert(csharp, "host.cs").CalorSource!;
        var workspace = new WorkspaceState();
        workspace.ConfigureCompilationContext(new GeneratedCSharpCompilationContext
        {
            IncludeImplicitSystemUsing = implicitSystem
        });
        var uri = DocumentUri.FromFileSystemPath(Path.Combine(Path.GetTempPath(), $"editor-{Guid.NewGuid():N}.calr"));
        workspace.GetOrCreate(uri, source, 1);
        var snapshot = workspace.CaptureSnapshot(refreshClosedDocuments: false);
        var document = Assert.Single(snapshot.Documents);
        Assert.False(document.Analysis.HasErrors, string.Join("\n", document.Analysis.Diagnostics));
        var diagnostics = workspace.GetDiagnostics(snapshot, document);
        Assert.Equal(!implicitSystem, diagnostics.Any(diagnostic => diagnostic.IsError));
        if (!implicitSystem)
            Assert.Contains(diagnostics, diagnostic => diagnostic.Message.Contains("CS0103"));
        Assert.Equal(source, document.Analysis.Source);
    }

    [Fact]
    public void EditorContext_RetainsProjectSourcesSymbolsAndNullablePolicy()
    {
        const string csharp = """
            #if FEATURE
            public class Host { public Callback Value => null; }
            #else
            #error missing selected project symbol
            #endif
            """;
        var converted = new CSharpToCalorConverter(new ConversionOptions
        {
            DefinedSymbols = ["FEATURE"]
        }).Convert(csharp, "host.cs").CalorSource!;
        var workspace = new WorkspaceState();
        workspace.ConfigureCompilationContext(new GeneratedCSharpCompilationContext
        {
            IncludeImplicitSystemUsing = true,
            PreprocessorSymbols = ["FEATURE"],
            NullableContextOptions = Microsoft.CodeAnalysis.NullableContextOptions.Enable,
            TreatWarningsAsErrors = true,
            AdditionalSources = [new("global using Callback = System.Action;", "project-usings.g.cs")]
        });
        var uri = DocumentUri.FromFileSystemPath(Path.Combine(Path.GetTempPath(), $"editor-{Guid.NewGuid():N}.calr"));
        workspace.GetOrCreate(uri, converted, 1);
        var snapshot = workspace.CaptureSnapshot(refreshClosedDocuments: false);
        var diagnostics = workspace.GetDiagnostics(snapshot, Assert.Single(snapshot.Documents));
        Assert.Contains(diagnostics, diagnostic => diagnostic.Message.Contains("CS8603"));
        Assert.DoesNotContain(diagnostics, diagnostic =>
            diagnostic.Message.Contains("CS0246") || diagnostic.Message.Contains("CS1029"));
    }

    [Fact]
    public void EditorDefault_ReportsAmbiguity_AndAcceptsAnAliasEscape()
    {
        const string ambiguous = """
            using Rival;
            namespace Rival { public class Action {} }
            public class Host { public Action Value { get; set; } }
            """;
        foreach (var alias in new[] { "", "using Action = System.Action;\n" })
        {
            var source = new CSharpToCalorConverter().Convert(alias + ambiguous).CalorSource!;
            var workspace = new WorkspaceState();
            var uri = DocumentUri.FromFileSystemPath(Path.Combine(Path.GetTempPath(), $"editor-{Guid.NewGuid():N}.calr"));
            workspace.GetOrCreate(uri, source, 1);
            var snapshot = workspace.CaptureSnapshot(refreshClosedDocuments: false);
            var diagnostics = workspace.GetDiagnostics(snapshot, Assert.Single(snapshot.Documents));
            Assert.Equal(alias.Length == 0, diagnostics.Any(diagnostic => diagnostic.Message.Contains("CS0104")));
        }
    }
}
