using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;
using Xunit;

namespace Calor.Compiler.Tests;

public class NullableCastTests
{
    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    private static string WrapInFunction(string body)
    {
        return $$"""
            module Test
              func Main public (object:value) → object
                !
                {{body}}
            """;
    }

    [Fact]
    public void ParseNativeCast_NullableValueType_ParsesCorrectly()
    {
        var source = WrapInFunction("return (int?)value");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];
        var returnStmt = func.Body[0] as ReturnStatementNode;
        Assert.NotNull(returnStmt);
        var typeOp = returnStmt!.Expression as TypeOperationNode;
        Assert.NotNull(typeOp);
        Assert.Equal(TypeOp.Cast, typeOp!.Operation);
        Assert.Equal("int?", typeOp.TargetType);
    }

    [Fact]
    public void ParseNativeCast_NullableGeneric_ParsesCorrectly()
    {
        var source = WrapInFunction("return (List<int>?)value");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];
        var returnStmt = func.Body[0] as ReturnStatementNode;
        Assert.NotNull(returnStmt);
        var typeOp = returnStmt!.Expression as TypeOperationNode;
        Assert.NotNull(typeOp);
        Assert.Equal(TypeOp.Cast, typeOp!.Operation);
        Assert.Equal("List<int>?", typeOp.TargetType);
    }

    [Fact]
    public void ParseNativeCast_GroupedNullableValueType_ParsesCorrectly()
    {
        var source = WrapInFunction("return ((int?)value)");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];
        var returnStmt = func.Body[0] as ReturnStatementNode;
        Assert.NotNull(returnStmt);
        var typeOp = returnStmt!.Expression as TypeOperationNode;
        Assert.NotNull(typeOp);
        Assert.Equal(TypeOp.Cast, typeOp!.Operation);
        Assert.Equal("int?", typeOp.TargetType);
    }

    [Theory]
    [InlineData("(cast ?i32 value)")]
    [InlineData("(cast ?List<int> value)")]
    [InlineData("(cast i32? value)")]
    public void ParseLegacyCast_IsRejectedInsideNativeFunction(string expression)
    {
        Parse(WrapInFunction("return " + expression), out var diagnostics);
        Assert.True(diagnostics.HasErrors);
    }

    [Fact]
    public void CSharpRoundTrip_NullableCast_ProducesPostfixNotation()
    {
        var csharp = @"
public class Foo
{
    public int? Bar(object value)
    {
        return (int?)value;
    }
}";
        var converter = new CSharpToCalorConverter(new ConversionOptions());
        var result = converter.Convert(csharp, "Test.cs");
        Assert.True(result.Success, "Conversion should succeed");
        Assert.NotNull(result.CalorSource);
        Assert.True(result.CalorSource!.Length > 0);
        Assert.Contains("(int?)value", result.CalorSource);
        var compiled = Program.Compile(result.CalorSource, "nullable-cast.calr");
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics.Errors));
        Assert.Contains("(int?)value", compiled.GeneratedCode);
        var compilation = CSharpCompilation.Create(
            $"NullableCast_{Guid.NewGuid():N}",
            [CSharpSyntaxTree.ParseText(compiled.GeneratedCode!)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var image = new MemoryStream();
        var emission = compilation.Emit(image);
        Assert.True(emission.Success, string.Join("\n", emission.Diagnostics));
        var type = Assembly.Load(image.ToArray()).GetTypes().Single(t => t.Name == "Foo");
        var method = type.GetMethod("Bar")!;
        Assert.Equal(typeof(int?), method.ReturnType);
        var instance = Activator.CreateInstance(type);
        Assert.Null(method.Invoke(instance, [null]));
        Assert.Equal(42, method.Invoke(instance, [42]));
        var invalid = Assert.Throws<TargetInvocationException>(() => method.Invoke(instance, [42d]));
        Assert.IsType<InvalidCastException>(invalid.InnerException);
    }

    [Theory]
    [InlineData("int", "int", false)]
    [InlineData("int", "long", false)]
    [InlineData("System.Guid", "System.Guid", false)]
    [InlineData("int", "int", true)]
    public void BuiltInNullableUnwrapping_ChargesTheSelectedValueGetter(
        string operandType, string targetType, bool guarded)
    {
        var tree = CSharpSyntaxTree.ParseText(
            $"class Probe {{ static {targetType} Read({operandType}? value) => ({targetType})value; }}");
        var compilation = CSharpCompilation.Create("NullableSelection", [tree],
            GeneratedCSharpCompiler.References, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.DoesNotContain(compilation.GetDiagnostics(), diagnostic =>
            diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        var cast = Assert.Single(tree.GetRoot().DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.CastExpressionSyntax>());
        var conversion = Assert.IsAssignableFrom<Microsoft.CodeAnalysis.Operations.IConversionOperation>(
            compilation.GetSemanticModel(tree).GetOperation(cast));
        Assert.Null(conversion.OperatorMethod);
        Assert.True(conversion.GetConversion().IsNullable);
        var nullable = Assert.IsAssignableFrom<INamedTypeSymbol>(conversion.Operand.Type);
        Assert.Equal(SpecialType.System_Nullable_T, nullable.OriginalDefinition.SpecialType);
        Assert.Equal(typeof(int?).Assembly.FullName, nullable.ContainingAssembly.Identity.ToString());
        var property = Assert.IsAssignableFrom<IPropertySymbol>(Assert.Single(nullable.GetMembers("Value")));
        var getter = Assert.IsAssignableFrom<IMethodSymbol>(property.GetMethod);
        Assert.Equal(MethodKind.PropertyGet, getter.MethodKind);
        Assert.DoesNotContain(getter.Locations, location => location.IsInSource);
        var key = Calor.Compiler.Effects.EffectResolverKey.FromBoundReceiver(
            Calor.Compiler.Binding.Metadata.MetadataBinderResult.ToBoundType(nullable),
            property.MetadataName, null, Calor.Compiler.Effects.EffectMemberKind.Getter);
        // The restored 0.29 Nullable<T> default contract makes the selected getter pure.
        Assert.Equal(Calor.Compiler.Effects.EffectResolutionStatus.PureExplicit,
            new Calor.Compiler.Effects.EffectResolver().Resolve(key).Status);

        var source = $$"""
            module NullableLowering
              func Unwrap public ({{operandType}}?:value) → {{targetType}}
                !
                {{(guarded ? $"if value.HasValue\n      return ({targetType})value\n    return 0" : $"return ({targetType})value")}}
            """;
        foreach (var typed in new[] { false, true })
        {
            var result = Program.Compile(source, null, NullableLoweringOptions(typed));
            Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
            Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
        }
    }

    [Theory]
    [InlineData("int", "int?", "(int?)value")]
    [InlineData("int?", "long?", "(long?)value")]
    [InlineData("int", "long", "(long)value")]
    [InlineData("object", "int?", "(int?)value")]
    [InlineData("int?", "int", "value.GetValueOrDefault()")]
    [InlineData("int?", "int", "value.GetValueOrDefault(7)")]
    [InlineData("int?", "bool", "value.HasValue")]
    public void NullableLowering_DoesNotWithdrawWrappingOrExactCopyContracts(
        string operandType, string targetType, string expression)
    {
        var source = $$"""
            module NullableCopies
              func Copy public ({{operandType}}:value) → {{targetType}}
                !
                return {{expression}}
            """;
        foreach (var typed in new[] { false, true })
        {
            var result = Program.Compile(source, null, NullableLoweringOptions(typed));
            Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        }
    }

    // Unwrapping charges the Nullable<T>.Value getter, which the restored 0.29 contract
    // makes pure, so every row is charged exactly the operand's 'cw'.
    [Theory]
    [InlineData("(int)Next(value)", "int")]
    [InlineData("(long?)Next(value)", "long?")]
    [InlineData("(int?)Next(value)", "int?")]
    public void NullableLowering_RetainsOperandEvaluationEffects(
        string expression, string targetType)
    {
        var source = $$"""
            module NullableEvaluation
              func Next (int?:value) → int?
                ! cw
                System.Console.WriteLine("operand")
                return value
              func Read (int?:value) → {{targetType}}
                !
                return {{expression}}
            """;
        source = RawCSharpFrame.NormalizeLineEndings(source);
        foreach (var typed in new[] { false, true })
        {
            var result = Program.Compile(source, null, NullableLoweringOptions(typed));
            Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains("'Read'", StringComparison.Ordinal)
                && diagnostic.Message.Contains("'cw'", StringComparison.Ordinal));
            var declared = Program.Compile(source.Replace("    !\n", "    ! cw\n", StringComparison.Ordinal),
                null, NullableLoweringOptions(typed));
            Assert.False(declared.HasErrors, string.Join("\n", declared.Diagnostics));
        }
    }

    [Theory]
    [InlineData("field int:Value public", "")]
    [InlineData("field int:Value public static", "")]
    [InlineData("property int:Value", "\n      get public")]
    [InlineData("property int:Value static", "\n      get public")]
    public void NullableLowering_SourceInitializersChargeUnwrappingWithoutForcingInitialization(
        string declaration, string accessor)
    {
        var source = $$"""
            module NullableInitialization
              class Holder
                {{declaration}} = (int)(int?)null{{accessor}}
              func Make () → Holder
                ! alloc
                return new Holder()
            """;
        foreach (var typed in new[] { false, true })
        {
            // The initializer's Nullable<T>.Value read resolves to the restored 0.29
            // Nullable<T> contract (pure), so `! alloc` on Make suffices.
            var result = Program.Compile(source, null, NullableLoweringOptions(typed));
            Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
            Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
        }
    }

    [Fact]
    public void NullableLowering_DoesNotReplaceSelectedSourceConversionAuthority()
    {
        var source = """
            module SourceConversion
              class Number struct
                op explicit (Number:value) → int
                  ! cw
                  System.Console.WriteLine("conversion")
                  return 7
              func Read (Number:value) → int
                !
                return (int)value
            """;
        source = RawCSharpFrame.NormalizeLineEndings(source);
        foreach (var typed in new[] { false, true })
        {
            var result = Program.Compile(source, null, NullableLoweringOptions(typed));
            Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains("'Read'", StringComparison.Ordinal)
                && diagnostic.Message.Contains("'cw'", StringComparison.Ordinal));
            var declared = Program.Compile(source.Replace("    !\n", "    ! cw\n", StringComparison.Ordinal),
                null, NullableLoweringOptions(typed));
            Assert.False(declared.HasErrors, string.Join("\n", declared.Diagnostics));
        }
    }

    private static CompilationOptions NullableLoweringOptions(bool typed) => new()
    {
        EnableTypeChecking = typed,
        StrictApi = true,
        StrictEffects = true,
        VerifyContracts = false,
        StatusWriter = TextWriter.Null
    };
}
