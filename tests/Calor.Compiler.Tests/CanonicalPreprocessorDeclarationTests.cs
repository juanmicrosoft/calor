using System.Runtime.Loader;
using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class CanonicalPreprocessorDeclarationTests
{
    private const string Members = """
        module Conditional
          class Logger
            pp DEBUG && (!TRACE || true)
              func Read static () → int
                !
                return 1
            pp elif TRACE
              func Read static () → int
                !
                return 2
            pp else
              func Read static () → int
                !
                return 3
            func Sibling static () → int
              !
              return 4
        """;

    [Theory]
    [InlineData("", 3)]
    [InlineData("DEBUG", 1)]
    [InlineData("TRACE", 2)]
    [InlineData("DEBUG,TRACE", 1)]
    public void MemberChainsRetainAllBranchesAndActualClrSelection(string symbols, int expected)
    {
        var module = Parse(Members);
        var block = Assert.Single(module.Classes.Single().PreprocessorBlocks);
        Assert.Equal("DEBUG && (!TRACE || true)", block.Condition);
        Assert.Equal("TRACE", block.ElseBranch!.Condition);
        Assert.Equal("", block.ElseBranch.ElseBranch!.Condition);
        Assert.Single(module.Classes.Single().Methods);
        var emitted = new CalorEmitter().Emit(module);
        Assert.DoesNotContain('§', emitted);
        Assert.Contains("pp elif TRACE", emitted);
        Assert.Contains("pp else", emitted);
        foreach (var source in new[] { Members, emitted })
        {
            var result = Program.Compile(source);
            Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
            Assert.Equal(expected, Execute(result.GeneratedCode, symbols));
        }
    }

    [Theory]
    [InlineData("", 3)]
    [InlineData("DEBUG", 1)]
    [InlineData("TRACE", 2)]
    public void TypeChainsAndNestedMemberBlocksKeepTheirIndentationOwner(string symbols, int expected)
    {
        var source = """
            module Conditional
              pp DEBUG
                class Logger
                  pp true
                    func Read static () → int
                      !
                      return 1
              pp elif TRACE
                class Logger
                  func Read static () → int
                    !
                    return 2
              pp else
                class Logger
                  func Read static () → int
                    !
                    return 3
              class Sibling
            """;
        var module = Parse(source);
        Assert.Single(module.TypePreprocessorBlocks);
        Assert.Equal("Sibling", Assert.Single(module.Classes).Name);
        var emitted = new CalorEmitter().Emit(module);
        Assert.DoesNotContain('§', emitted);
        Assert.Equal(expected, Execute(new CSharpEmitter().Emit(Parse(emitted)), symbols));
    }

    [Fact]
    public void InterfaceSignaturesInsideConditionalBranchesRemainSignatures()
    {
        var module = Parse("""
            module Conditional
              interface IBag
                pp DEBUG
                  func Read () → int
                    !
                pp else
                  func Read () → int
                    !
            """);
        var block = Assert.Single(module.Interfaces.Single().PreprocessorBlocks);
        Assert.IsType<MethodSignatureNode>(Assert.Single(block.Items));
        Assert.IsType<MethodSignatureNode>(Assert.Single(block.ElseBranch!.Items));
        var emitted = new CalorEmitter().Emit(module);
        var result = Program.Compile(emitted);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        var compilation = CSharpCompilation.Create("ConditionalInterfaces",
            [CSharpSyntaxTree.ParseText(result.GeneratedCode)],
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(compilation.GetDiagnostics().Where(diagnostic =>
            diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
    }

    [Theory]
    [InlineData("", 3)]
    [InlineData("DEBUG", 1)]
    [InlineData("TRACE", 2)]
    public void RealConverterPreservesInactiveTextAndConditionalClrBranches(string symbols, int expected)
    {
        var source = """
            public class Logger
            {
            #if DEBUG
                public static int Read() { return 1; }
            #elif TRACE
                public static int Read() { return 2; }
            #else
                public static int Read() { return 3; }
            #endif
            }
            """;
        var converted = new CSharpToCalorConverter().Convert(source);
        Assert.True(converted.Success, string.Join("\n", converted.Context.Issues));
        Assert.DoesNotContain('§', converted.CalorSource!);
        Assert.DoesNotContain("post-validation-fallback", converted.CalorSource);
        var type = Assert.Single(Parse(converted.CalorSource!).Classes);
        Assert.Empty(type.InteropBlocks);
        var block = Assert.Single(type.PreprocessorBlocks);
        Assert.Equal("preprocessor-disabled", Assert.Single(block.InteropBlocks).FeatureName);
        Assert.Equal("preprocessor-disabled", Assert.Single(block.ElseBranch!.InteropBlocks).FeatureName);
        Assert.Equal("Read", Assert.Single(block.ElseBranch.ElseBranch!.Methods).Name);
        Assert.Equal(expected, Execute(source, symbols));
        var result = Program.Compile(converted.CalorSource!);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        Assert.Equal(expected, Execute(result.GeneratedCode, symbols));
    }

    [Fact]
    public void ConditionKeepsItsActualCSharpWhitespaceAndComment()
    {
        const string condition = "DEBUG\t&&  !TRACE // retained condition";
        var module = Parse($"module Conditional\n  pp {condition}\n    class Logger");
        Assert.Equal(condition, module.TypePreprocessorBlocks.Single().Condition);
        var emitted = new CalorEmitter().Emit(module);
        Assert.Contains("pp " + condition, emitted);
        Assert.Equal(condition, Parse(emitted).TypePreprocessorBlocks.Single().Condition);
    }

    [Fact]
    public void IndexersInsidePreprocessorBlocksAreNotLostFromTypedStorage()
    {
        var module = Parse("""
            module Conditional
              interface IBag
                pp DEBUG
                  this[int:i] → int
                    get
            """);
        var block = Assert.Single(module.Interfaces.Single().PreprocessorBlocks);
        Assert.Single(block.Indexers);
        Assert.IsType<IndexerNode>(Assert.Single(block.Items));
        var reparsed = Parse(new CalorEmitter().Emit(module));
        Assert.Single(reparsed.Interfaces.Single().PreprocessorBlocks.Single().Indexers);
    }

    [Theory]
    [InlineData("pp")]
    [InlineData("pp elif DEBUG")]
    [InlineData("pp else DEBUG")]
    [InlineData("pp DEBUG + TRACE")]
    [InlineData("pp DEBUG &&")]
    public void MalformedOrOrphanBranchesAreDiagnosed(string header)
    {
        var diagnostics = new DiagnosticBag();
        _ = new Parser(new Lexer($"module Bad\n  {header}\n    class C", diagnostics)
            .TokenizeAllForParser(), diagnostics).Parse();
        Assert.True(diagnostics.HasErrors);
    }

    private static int Execute(string source, string symbols)
    {
        var options = new CSharpParseOptions(preprocessorSymbols:
            symbols.Split(',', StringSplitOptions.RemoveEmptyEntries));
        var compilation = CSharpCompilation.Create("ConditionalDeclarations",
            [CSharpSyntaxTree.ParseText(source, options)],
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics) + "\n" + source);
        stream.Position = 0;
        var context = new AssemblyLoadContext("ConditionalDeclarations", isCollectible: true);
        try
        {
            return (int)context.LoadFromStream(stream).GetType("Logger")!.GetMethod("Read")!.Invoke(null, null)!;
        }
        finally { context.Unload(); }
    }

    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        return module;
    }
}
