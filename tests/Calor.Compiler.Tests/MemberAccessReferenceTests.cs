using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Migration;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for MemberAccessReference and enum value preservation in attribute conversion.
/// </summary>
public class MemberAccessReferenceTests
{
    private readonly CSharpToCalorConverter _converter = new();

    [Fact]
    public void MemberAccessReference_ToString_ReturnsExpression()
    {
        // Arrange
        var reference = new MemberAccessReference("AttributeTargets.Method");

        // Act
        var result = reference.ToString();

        // Assert
        Assert.Equal("AttributeTargets.Method", result);
    }

    [Fact]
    public void CalorAttributeArgument_GetFormattedValue_MemberAccess_ReturnsUnquoted()
    {
        // Arrange
        var reference = new MemberAccessReference("AttributeTargets.Method");
        var argument = new CalorAttributeArgument(reference);

        // Act
        var result = argument.GetFormattedValue();

        // Assert: Should NOT be quoted
        Assert.Equal("AttributeTargets.Method", result);
        Assert.DoesNotContain("\"", result);
    }

    [Fact]
    public void Convert_AttributeUsageWithEnumValue_PreservesEnumValue()
    {
        // Arrange
        var csharpSource = """
            [System.AttributeUsage(System.AttributeTargets.Method)]
            public class MyAttribute : System.Attribute { }
            """;

        // Act
        var result = _converter.Convert(csharpSource);

        // Assert
        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.Ast);

        var classNode = Assert.Single(result.Ast.Classes);
        var attribute = Assert.Single(classNode.CSharpAttributes);

        // The name may include namespace prefix
        Assert.Contains("AttributeUsage", attribute.Name);

        var argument = Assert.Single(attribute.Arguments);
        Assert.IsType<MemberAccessReference>(argument.Value);

        var memberAccess = (MemberAccessReference)argument.Value;
        Assert.Contains("AttributeTargets.Method", memberAccess.Expression);
    }

    [Fact]
    public void Convert_AttributeUsageWithEnumValue_DirectEmit_PreservesValue()
    {
        // Arrange: Convert C# to Calor AST (not Calor source string)
        var csharpSource = """
            [System.AttributeUsage(System.AttributeTargets.Method)]
            public class MyAttribute : System.Attribute { }
            """;

        // Act: Convert C# to Calor AST
        var toCalorResult = _converter.Convert(csharpSource);
        Assert.True(toCalorResult.Success, GetErrorMessage(toCalorResult));

        // Emit the AST directly back to C# (bypassing Calor source serialization)
        var emitter = new CSharpEmitter();
        var emittedCSharp = emitter.Emit(toCalorResult.Ast!);

        // Assert: Should contain unquoted enum value
        Assert.Contains("AttributeTargets.Method", emittedCSharp);
        // Should NOT contain the pattern where the enum value is quoted as a string literal
        // (i.e., should not have ("...AttributeTargets.Method..."))
        Assert.DoesNotMatch(@"\(""[^""]*AttributeTargets\.Method[^""]*""\)", emittedCSharp);
    }

    [Fact]
    public void Convert_AttributeUsageWithMultipleFlags_PreservesOrExpression()
    {
        // Arrange
        var csharpSource = """
            [System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Class)]
            public class MyAttribute : System.Attribute { }
            """;

        // Act
        var result = _converter.Convert(csharpSource);

        // Assert
        Assert.True(result.Success, GetErrorMessage(result));

        var classNode = Assert.Single(result.Ast!.Classes);
        var attribute = Assert.Single(classNode.CSharpAttributes);

        // The binary expression should be captured
        var argument = Assert.Single(attribute.Arguments);
        var formatted = argument.GetFormattedValue();

        // Should preserve the | expression without quoting
        Assert.Contains("AttributeTargets.Method", formatted);
        Assert.Contains("AttributeTargets.Class", formatted);
        // Should not have the pattern where enum values are quoted as string literals
        Assert.DoesNotContain("\"AttributeTargets", formatted);
    }

    [Fact]
    public void Convert_JsonPropertyWithTypeofArg_PreservesTypeOf()
    {
        // Arrange
        var csharpSource = """
            using System.Text.Json.Serialization;
            public class Foo
            {
                [JsonConverter(typeof(JsonStringEnumConverter))]
                public int Day { get; set; }
            }
            """;

        // Act
        var result = _converter.Convert(csharpSource);

        // Assert
        Assert.True(result.Success, GetErrorMessage(result));

        var classNode = Assert.Single(result.Ast!.Classes);
        var prop = Assert.Single(classNode.Properties);
        var attribute = Assert.Single(prop.CSharpAttributes);

        var argument = Assert.Single(attribute.Arguments);
        Assert.IsType<TypeOfReference>(argument.Value);
    }

    [Fact]
    public void CalorAttributeArgument_String_GetsQuoted()
    {
        // Arrange: A regular string should still be quoted
        var argument = new CalorAttributeArgument("hello");

        // Act
        var result = argument.GetFormattedValue();

        // Assert
        Assert.Equal("\"hello\"", result);
    }

    [Fact]
    public void CalorAttributeArgument_TypeOfReference_FormatsCorrectly()
    {
        // Arrange
        var typeRef = new TypeOfReference("string");
        var argument = new CalorAttributeArgument(typeRef);

        // Act
        var result = argument.GetFormattedValue();

        // Assert
        Assert.Equal("typeof(string)", result);
    }

    [Fact]
    public void Convert_SimpleEnumAccess_WrappedInMemberAccessReference()
    {
        // Arrange
        var csharpSource = """
            public class Test
            {
                [System.Obsolete]
                [System.ComponentModel.DefaultValue(System.DayOfWeek.Monday)]
                public int Day { get; set; }
            }
            """;

        // Act
        var result = _converter.Convert(csharpSource);

        // Assert
        Assert.True(result.Success, GetErrorMessage(result));
        var classNode = Assert.Single(result.Ast!.Classes);
        var prop = Assert.Single(classNode.Properties);

        // Find the DefaultValue attribute
        var defaultValueAttr = prop.CSharpAttributes.FirstOrDefault(a => a.Name.Contains("DefaultValue"));
        Assert.NotNull(defaultValueAttr);

        var argument = Assert.Single(defaultValueAttr.Arguments);
        Assert.IsType<MemberAccessReference>(argument.Value);
    }

    [Fact]
    public void Convert_EnumValueInAttribute_EmitFromAst_PreservesValue()
    {
        // This test verifies that when we have a MemberAccessReference in the AST,
        // the CSharpEmitter correctly emits it without quotes.
        // Note: Full roundtrip through Calor source is a separate concern.

        // Arrange
        var csharpSource = """
            [System.AttributeUsage(System.AttributeTargets.Method)]
            public class MyAttribute : System.Attribute { }
            """;

        // Act: Convert C# to Calor AST
        var conversionResult = _converter.Convert(csharpSource);
        Assert.True(conversionResult.Success, GetErrorMessage(conversionResult));

        // Emit AST directly to C#
        var emitter = new CSharpEmitter();
        var generatedCode = emitter.Emit(conversionResult.Ast!);

        // Assert
        Assert.NotNull(generatedCode);
        // The generated code should have unquoted enum value
        Assert.Contains("AttributeTargets.Method", generatedCode);
        // Should not have the pattern where the enum value is quoted as a string literal argument
        Assert.DoesNotMatch(@"\(""[^""]*AttributeTargets\.Method[^""]*""\)", generatedCode);
    }

    private static string GetErrorMessage(ConversionResult result)
    {
        if (result.Issues.Count > 0)
        {
            return string.Join("\n", result.Issues.Select(i => i.Message));
        }
        return "Conversion failed with no specific error message";
    }
}
