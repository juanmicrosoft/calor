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
    [InlineData("System.SByte", "int")]
    [InlineData("System.Byte", "int")]
    [InlineData("System.Int16", "int")]
    [InlineData("System.UInt16", "int")]
    [InlineData("System.Int32", "int")]
    [InlineData("System.UInt32", "uint")]
    [InlineData("System.Int64", "long")]
    [InlineData("System.UInt64", "ulong")]
    [InlineData("System.Single", "float")]
    [InlineData("System.Double", "double")]
    [InlineData("System.Decimal", "decimal")]
    public void ClrNumericAliases_RetainArithmeticTypingAndRealParameterIdentity(string alias, string output)
    {
        var authored = $$"""
            module Numbers
              func Divide ({{alias}}:left, {{alias}}:right) → {{output}}
                let quotient = left / right
                return quotient
            """;
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        {
            var diagnostics = new DiagnosticBag();
            new TypeChecking.TypeChecker(diagnostics).Check(Parse(source));
            Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Errors));
            Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UndefinedReference);
            var result = Compile(source);
            Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
            var method = CompileAssembly(result.GeneratedCode).GetTypes().SelectMany(owner => owner.GetMethods())
                .Single(method => method.Name == "Divide");
            var parameterType = Type.GetType(alias, throwOnError: true)!;
            Assert.All(method.GetParameters(), parameter => Assert.Equal(parameterType, parameter.ParameterType));
            var arguments = new[] { Convert.ChangeType(6, parameterType), Convert.ChangeType(3, parameterType) };
            Assert.Equal(Convert.ChangeType(2, method.ReturnType), method.Invoke(null, arguments));
        }
    }

    [Theory]
    [InlineData("System.Boolean")]
    [InlineData("System.String")]
    public void ClrNonNumericAliases_DoNotAcquireArithmeticOperations(string alias)
    {
        var source = $"module Numbers\n  func Divide ({alias}:left, {alias}:right) → int\n    let quotient = left / right\n    return quotient";
        var diagnostics = new DiagnosticBag();
        new TypeChecking.TypeChecker(diagnostics).Check(Parse(source));
        Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
        Assert.True(Compile(source).HasErrors);
    }

    [Theory]
    [InlineData("Example.Int32")]
    [InlineData("System.int32")]
    [InlineData("System.Int32Thing")]
    public void ClrNumericAliases_DoNotResolveLookalikeTypeNames(string name)
        => Assert.Null(TypeChecking.PrimitiveType.FromName(name));

    [Theory]
    [InlineData("byte[]", 3)]
    [InlineData("int[]", 3)]
    [InlineData("int[,]", 6)]
    [InlineData("string", 3)]
    public void NativeLength_HasIntegerTypeInOrdinaryLoopConditions(string type, int expected)
    {
        var authored = $$"""
            module Lengths
              func Count ({{type}}:values) → int
                ! mut
                var int:i = 0
                while i < (values).Length
                  set i i + 1
                return i
            """;
        object input = type switch
        {
            "byte[]" => new byte[] { 1, 2, 3 },
            "int[]" => new[] { 1, 2, 3 },
            "int[,]" => new int[2, 3],
            "string" => "Ωab",
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        {
            var diagnostics = new DiagnosticBag();
            new TypeChecking.TypeChecker(diagnostics).Check(Parse(source));
            Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Errors));
            var result = Compile(source);
            Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
            var method = CompileAssembly(result.GeneratedCode).GetTypes().SelectMany(owner => owner.GetMethods())
                .Single(method => method.Name == "Count");
            Assert.Equal(expected, method.Invoke(null, [input]));
        }
    }

    [Theory]
    [InlineData("byte[]", "Count")]
    [InlineData("string", "length")]
    [InlineData("int", "Length")]
    public void NativeLength_DoesNotPermitUnmodeledMembersOrReceivers(string type, string member)
    {
        var source = $"module Lengths\n  func Count ({type}:values) → int\n    while (values).{member} > 0\n      return 1\n    return 0";
        var diagnostics = new DiagnosticBag();
        var module = Parse(source);
        Assert.Contains(Assert.Single(module.Functions).Body, statement => statement is WhileStatementNode);
        new TypeChecking.TypeChecker(diagnostics).Check(module);
        Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
        Assert.True(Compile(source).HasErrors);
    }

    [Theory]
    [InlineData("byte[]")]
    [InlineData("string")]
    public void NativeLength_DoesNotBecomeABooleanOrAnUnknownType(string type)
    {
        var source = $"module Lengths\n  func Count ({type}:values) → int\n    while (values).Length\n      return 1\n    return 0";
        var diagnostics = new DiagnosticBag();
        var module = Parse(source);
        Assert.Contains(Assert.Single(module.Functions).Body, statement => statement is WhileStatementNode);
        new TypeChecking.TypeChecker(diagnostics).Check(module);
        Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
        Assert.True(Compile(source).HasErrors);
    }

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
