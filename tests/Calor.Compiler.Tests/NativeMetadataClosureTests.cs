using System.Reflection;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using Binder = Calor.Compiler.Binding.Binder;

namespace Calor.Compiler.Tests;

public sealed class NativeMetadataClosureTests
{
    [Theory]
    [InlineData("let List<string>:lines = File.ReadAllLines(path)\n    return lines.Count", "int")]
    [InlineData("return File.ReadAllLines(path)", "List<string>")]
    [InlineData("let List<string>:lines = System.IO.File.ReadAllLines(path)\n    return lines.Count", "int")]
    public void NativeArrayResults_RetainTheCalorLayerCollectionDiagnostic(string body, string output)
    {
        var source = $"module Files\n  func Get (string:path) → {output}\n    ! fs:r\n    {body}";
        var result = Compile(source);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.BindArrayToConcreteCollection);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.CodeGenCompilationError);
        var module = Parse(source);
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(module);
        var statement = Assert.Single(bound.Functions).Body[0];
        var call = Assert.IsType<BoundCallExpression>(statement is BoundBindStatement binding
            ? binding.Initializer : Assert.IsType<BoundReturnStatement>(statement).Expression);
        Assert.Equal("System.IO.File", call.ResolvedMetadataMethod!.ContainingType.ToDisplayString());
        Assert.IsAssignableFrom<IArrayTypeSymbol>(call.ResolvedMetadataMethod.ReturnType);
        Assert.NotNull(call.MetadataArrayResultType);
    }

    [Fact]
    public void NativeCharacterContract_RetainsTheUnicodePredicateAndActualMetadataTarget()
    {
        const string authored = """
            module AgentReference41
              func DigitValue (char:c) → int
                !
                requires char.IsDigit(c)
                return c - '0'
            """;
        var module = Parse(authored);
        var predicate = Assert.Single(Assert.Single(module.Functions).Preconditions).Condition.Accept(new CalorEmitter());
        foreach (var source in new[] { authored, authored.Replace("char.IsDigit(c)", predicate, StringComparison.Ordinal) })
        {
            var result = Compile(source);
            Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
            Assert.Contains("char.IsDigit", result.GeneratedCode);
            var method = CompileAssembly(result.GeneratedCode).GetTypes().SelectMany(type => type.GetMethods())
                .Single(method => method.Name == "DigitValue");
            Assert.Equal(5, method.Invoke(null, ['5']));
            Assert.Equal('\u0665' - '0', method.Invoke(null, ['\u0665']));
        }
    }

    [Fact]
    public void StringIndexReceiver_UsesTheSelectedCharToStringMethodWithoutUnknownEffects()
    {
        const string authored = """
            module M
              func Test private (string:text) → string
                return text[0].ToString()
            """;
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(Parse(authored));
        var call = Assert.IsType<BoundCallExpression>(
            Assert.IsType<BoundReturnStatement>(Assert.Single(bound.Functions).Body[0]).Expression);
        Assert.Equal(SpecialType.System_Char, call.ResolvedMetadataMethod!.ContainingType.SpecialType);
        Assert.Equal("ToString", call.ResolvedMetadataMethod.Name);
        var result = Compile(authored);
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
        var method = CompileAssembly(result.GeneratedCode).GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .Single(method => method.Name == "Test");
        Assert.Equal("Ω", method.Invoke(null, ["Ωvalue"]));
    }

    [Theory]
    [InlineData("char")]
    [InlineData("int")]
    public void MissingEscapedPrimitiveReceiver_DoesNotBecomeAStaticMetadataType(string name)
    {
        var source = $"module M\n  func Test () → string\n    return @{name}.ToString()";
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(Parse(source));
        var call = Assert.IsType<BoundCallExpression>(
            Assert.IsType<BoundReturnStatement>(Assert.Single(bound.Functions).Body[0]).Expression);
        Assert.IsNotType<BoundTypeReferenceExpression>(call.Receiver);
        Assert.Null(call.ResolvedMetadataMethod);
        Assert.True(Compile(source).HasErrors);
    }

    [Fact]
    public void EscapedPrimitiveValueReceiver_RetainsItsValueIdentity()
    {
        const string source = """
            module M
              func Test (char:@char) → string
                return @char.ToString()
            """;
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(Parse(source));
        var call = Assert.IsType<BoundCallExpression>(
            Assert.IsType<BoundReturnStatement>(Assert.Single(bound.Functions).Body[0]).Expression);
        Assert.IsType<BoundVariableExpression>(call.Receiver);
        Assert.Equal(SpecialType.System_Char, call.ResolvedMetadataMethod!.ContainingType.SpecialType);
        var result = Compile(source);
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
        Assert.Contains("@char.ToString()", result.GeneratedCode);
    }

    [Fact]
    public void DeclaredGenericList_DoesNotAcquireTheBclCollectionIdentity()
    {
        const string source = """
            module M
              class List<T>
                func Read (string:path) → List<string>
                  ! fs:r
                  return File.ReadAllLines(path)
            """;
        var diagnostics = new DiagnosticBag();
        new Binder(diagnostics).Bind(Parse(source));
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.BindArrayToConcreteCollection);
    }

    private static CompilationResult Compile(string source) => Program.Compile(source, "native-metadata.calr",
        new CompilationOptions { EnableTypeChecking = true, EnforceEffects = true, StatusWriter = TextWriter.Null });

    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Errors));
        return module;
    }

    private static Assembly CompileAssembly(string source)
    {
        var compilation = CSharpCompilation.Create("NativeMetadata_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source)], GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, source + Environment.NewLine + string.Join(Environment.NewLine, result.Diagnostics));
        return Assembly.Load(stream.ToArray());
    }
}
