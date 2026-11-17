using System.Runtime.Loader;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class CanonicalMultiFileFunctionTests
{
    [Theory]
    [InlineData("", "GlobalModule")]
    [InlineData(" Acme.Services", "Acme.Services.ServicesModule")]
    [InlineData(" Acme.Services block", "Acme.Services.ServicesModule")]
    public void ActualProjectFunctionsLinkAcrossFilesWithoutFileLabelContainers(
        string namespaceHeader, string owner)
    {
        var callee = $"module OrderService{namespaceHeader}\n  func SaveOrder () → int\n    !\n    return 41";
        var caller = $"module Handler{namespaceHeader}\n  func HandleRequest () → int\n    !\n    return SaveOrder() + 1";
        var generated = CompileProject(false, callee, caller);
        Assert.All(generated, code => Assert.Contains("public static partial class", code));
        var compilation = Group(generated);
        using var assembly = new MemoryStream();
        var emitted = compilation.Emit(assembly);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        assembly.Position = 0;
        var context = new AssemblyLoadContext(Guid.NewGuid().ToString("N"), isCollectible: true);
        try
        {
            var loaded = context.LoadFromStream(assembly);
            Assert.Null(loaded.GetType("OrderService"));
            Assert.Null(loaded.GetType("Handler"));
            var module = Assert.IsAssignableFrom<Type>(loaded.GetType(owner));
            Assert.Equal(42, module.GetMethod("HandleRequest")!.Invoke(null, null));
            Assert.Equal(41, module.GetMethod("SaveOrder")!.Invoke(null, null));
        }
        finally
        {
            context.Unload();
        }
    }

    [Fact]
    public void DuplicateClrFunctionSignaturesRemainErrors()
    {
        CompileProject(true,
            "module First\n  func Value () → int\n    !\n    return 1",
            "module Second\n  func Value () → int\n    !\n    return 2");
    }

    [Fact]
    public void MissingCrossModuleEffectRowsSuggestOnlyTheNativeSyntax()
    {
        // A public function without a row no longer compiles (Calor0124), so the registry's
        // Calor0417 path is reached only from a parsed-but-rejected module (editor, index).
        // Its suggestion must still teach the native row.
        var parseDiagnostics = new DiagnosticBag();
        var module = new Calor.Compiler.Parsing.Parser(
            new Calor.Compiler.Parsing.Lexer("module Orders\n  func SaveOrder () → int\n    return 41", parseDiagnostics)
                .TokenizeAllForParser(),
            parseDiagnostics).Parse();
        Assert.Contains(parseDiagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.MissingBoundaryEffectRow);
        var registry = CrossModuleEffectRegistry.Build([(module, "Orders.calr")]);
        var diagnostic = Assert.Single(registry.BuildDiagnostics,
            diagnostic => diagnostic.Code == DiagnosticCode.UndeclaredPublicFunction);
        Assert.Contains("indented ! row", diagnostic.Message);
        Assert.DoesNotContain('§', diagnostic.Message);
    }

    private static IReadOnlyList<string> CompileProject(bool expectDuplicate, params string[] sources)
    {
        var root = Path.Combine(Path.GetTempPath(), "calor-multifile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var files = sources.Select((source, index) =>
            {
                var path = Path.Combine(root, index + ".calr");
                File.WriteAllText(path, source);
                return new FileInfo(path);
            }).ToList();
            var diagnostics = new DiagnosticBag();
            var generated = new List<string>();
            CompilationDriver.CompileAll(files,
                _ => new CompilationOptions { EnforceEffects = true, UnknownCallPolicy = UnknownCallPolicy.Strict },
                crossModuleEnforcement: true, crossModulePolicy: UnknownCallPolicy.Strict,
                onCompiled: (_, result) => generated.Add(result.GeneratedCode), diagnosticSink: diagnostics);
            if (expectDuplicate)
                Assert.Contains(diagnostics, diagnostic =>
                    diagnostic.Severity == Calor.Compiler.Diagnostics.DiagnosticSeverity.Error
                    && diagnostic.Message.Contains("CS0111", StringComparison.Ordinal));
            else
                Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
            Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
            Assert.Equal(expectDuplicate ? 0 : sources.Length, generated.Count);
            return generated;
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static CSharpCompilation Group(IReadOnlyList<string> sources) =>
        CSharpCompilation.Create("MultiFileFunctions",
            sources.Select(source => CSharpSyntaxTree.ParseText(source)),
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
}
