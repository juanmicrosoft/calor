using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public class ClassVisibilityTests
{
    private readonly CSharpToCalorConverter _converter = new();

    #region Converter visibility tests

    [Fact]
    public void Converter_InternalClass_PreservesVisibility()
    {
        var csharp = """
            internal class Foo
            {
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        Assert.Equal(Visibility.Internal, cls.Visibility);
    }

    [Fact]
    public void Converter_PublicClass_PreservesVisibility()
    {
        var csharp = """
            public class Bar
            {
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        Assert.Equal(Visibility.Public, cls.Visibility);
    }

    [Fact]
    public void Converter_DefaultClass_IsInternal()
    {
        var csharp = """
            class Baz
            {
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        Assert.Equal(Visibility.Internal, cls.Visibility);
    }

    #endregion

    #region CSharpEmitter tests

    [Fact]
    public void CSharpEmitter_InternalClass_EmitsInternal()
    {
        var cls = new ClassDefinitionNode(
            new TextSpan(0, 0, 0, 0),
            "c1", "Foo",
            isAbstract: false, isSealed: false, isPartial: false, isStatic: false,
            baseClass: null,
            implementedInterfaces: Array.Empty<string>(),
            typeParameters: Array.Empty<TypeParameterNode>(),
            fields: Array.Empty<ClassFieldNode>(),
            properties: Array.Empty<PropertyNode>(),
            constructors: Array.Empty<ConstructorNode>(),
            methods: Array.Empty<MethodNode>(),
            events: Array.Empty<EventDefinitionNode>(),
            attributes: new AttributeCollection(),
            csharpAttributes: Array.Empty<CalorAttributeNode>(),
            visibility: Visibility.Internal);

        var module = new ModuleNode(
            new TextSpan(0, 0, 0, 0), "m1", "Test",
            Array.Empty<UsingDirectiveNode>(),
            Array.Empty<InterfaceDefinitionNode>(),
            new[] { cls },
            Array.Empty<EnumDefinitionNode>(),
            Array.Empty<DelegateDefinitionNode>(),
            Array.Empty<FunctionNode>(),
            new AttributeCollection(),
            Array.Empty<IssueNode>(),
            Array.Empty<AssumeNode>(),
            Array.Empty<InvariantNode>(),
            Array.Empty<DecisionNode>(),
            null);

        var emitter = new CSharpEmitter();
        var output = emitter.Emit(module);

        Assert.Contains("internal class Foo", output);
        Assert.DoesNotContain("public class Foo", output);
    }

    [Fact]
    public void CSharpEmitter_ProtectedMember_EmitsProtected()
    {
        var method = new MethodNode(
            new TextSpan(0, 0, 0, 0),
            "m1", "DoStuff",
            Visibility.Protected,
            MethodModifiers.Virtual,
            Array.Empty<TypeParameterNode>(),
            Array.Empty<ParameterNode>(),
            output: null,
            effects: null,
            preconditions: Array.Empty<RequiresNode>(),
            postconditions: Array.Empty<EnsuresNode>(),
            body: Array.Empty<StatementNode>(),
            attributes: new AttributeCollection(),
            csharpAttributes: Array.Empty<CalorAttributeNode>());

        var cls = new ClassDefinitionNode(
            new TextSpan(0, 0, 0, 0),
            "c1", "Base",
            isAbstract: false, isSealed: false, isPartial: false, isStatic: false,
            baseClass: null,
            implementedInterfaces: Array.Empty<string>(),
            typeParameters: Array.Empty<TypeParameterNode>(),
            fields: Array.Empty<ClassFieldNode>(),
            properties: Array.Empty<PropertyNode>(),
            constructors: Array.Empty<ConstructorNode>(),
            methods: new[] { method },
            events: Array.Empty<EventDefinitionNode>(),
            attributes: new AttributeCollection(),
            csharpAttributes: Array.Empty<CalorAttributeNode>(),
            visibility: Visibility.Public);

        var module = new ModuleNode(
            new TextSpan(0, 0, 0, 0), "m1", "Test",
            Array.Empty<UsingDirectiveNode>(),
            Array.Empty<InterfaceDefinitionNode>(),
            new[] { cls },
            Array.Empty<EnumDefinitionNode>(),
            Array.Empty<DelegateDefinitionNode>(),
            Array.Empty<FunctionNode>(),
            new AttributeCollection(),
            Array.Empty<IssueNode>(),
            Array.Empty<AssumeNode>(),
            Array.Empty<InvariantNode>(),
            Array.Empty<DecisionNode>(),
            null);

        var emitter = new CSharpEmitter();
        var output = emitter.Emit(module);

        Assert.Contains("protected virtual void DoStuff", output);
    }

    #endregion

    #region Roundtrip tests

    [Fact]
    public void Roundtrip_InternalClass_Preserved()
    {
        var csharp = """
            internal class Program
            {
                public void Run()
                {
                }
            }
            """;

        // C# → Calor
        var conversionResult = _converter.Convert(csharp);
        Assert.True(conversionResult.Success, GetErrorMessage(conversionResult));
        Assert.Contains(":int", conversionResult.CalorSource!);

        // Calor → C#
        var compilationResult = Program.Compile(conversionResult.CalorSource!);
        Assert.False(compilationResult.HasErrors,
            "Roundtrip failed:\n" +
            string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message)));

        Assert.Contains("internal class Program", compilationResult.GeneratedCode);
        Assert.DoesNotContain("public class Program", compilationResult.GeneratedCode);
    }

    #endregion

    #region Parser tests

    [Fact]
    public void Parser_ClassWithVisibility_Parsed()
    {
        var calorSource = """
            §M{m1:Test}
              §CL{c1:Foo:int:static}
              §/CL{c1}
            §/M{m1}
            """;

        var compilationResult = Program.Compile(calorSource);
        Assert.False(compilationResult.HasErrors,
            string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message)));

        var cls = Assert.Single(compilationResult.Ast!.Classes);
        Assert.Equal(Visibility.Internal, cls.Visibility);
        Assert.True(cls.IsStatic);
    }

    [Fact]
    public void Parser_PrivateClass_PreservesVisibility()
    {
        // Private visibility can appear in hand-written Calor (e.g., nested class scenarios)
        var calorSource = """
            §M{m1:Test}
              §CL{c1:Inner:pri}
              §/CL{c1}
            §/M{m1}
            """;

        var compilationResult = Program.Compile(calorSource);
        Assert.False(compilationResult.HasErrors,
            string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message)));

        var cls = Assert.Single(compilationResult.Ast!.Classes);
        Assert.Equal(Visibility.Private, cls.Visibility);
        Assert.Contains("private class Inner", compilationResult.GeneratedCode);
    }

    #endregion

    #region CalorEmitter tests

    [Fact]
    public void CalorEmitter_InternalClass_EmitsVis()
    {
        var cls = new ClassDefinitionNode(
            new TextSpan(0, 0, 0, 0),
            "c1", "Program",
            isAbstract: false, isSealed: false, isPartial: false, isStatic: false,
            baseClass: null,
            implementedInterfaces: Array.Empty<string>(),
            typeParameters: Array.Empty<TypeParameterNode>(),
            fields: Array.Empty<ClassFieldNode>(),
            properties: Array.Empty<PropertyNode>(),
            constructors: Array.Empty<ConstructorNode>(),
            methods: Array.Empty<MethodNode>(),
            events: Array.Empty<EventDefinitionNode>(),
            attributes: new AttributeCollection(),
            csharpAttributes: Array.Empty<CalorAttributeNode>(),
            visibility: Visibility.Internal);

        var module = new ModuleNode(
            new TextSpan(0, 0, 0, 0), "m1", "Test",
            Array.Empty<UsingDirectiveNode>(),
            Array.Empty<InterfaceDefinitionNode>(),
            new[] { cls },
            Array.Empty<EnumDefinitionNode>(),
            Array.Empty<DelegateDefinitionNode>(),
            Array.Empty<FunctionNode>(),
            new AttributeCollection(),
            Array.Empty<IssueNode>(),
            Array.Empty<AssumeNode>(),
            Array.Empty<InvariantNode>(),
            Array.Empty<DecisionNode>(),
            null);

        var emitter = new CalorEmitter();
        var output = emitter.Emit(module);

        Assert.Contains("§CL{c1:Program:int}", output);
    }

    #endregion

    #region Helpers

    private static string GetErrorMessage(ConversionResult result)
    {
        if (result.Success) return string.Empty;
        return string.Join("\n", result.Issues.Select(i => i.ToString()));
    }

    #endregion
}
