using Calor.Compiler.CodeGen;
using Calor.Compiler.Migration;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for namespace dot preservation in C# code generation.
/// </summary>
public class NamespaceEmissionTests
{
    [Fact]
    public void Compile_DottedNamespace_PreservesDots()
    {
        // Arrange: Calor source with dotted namespace
        var source = "§M{m1:Calor.Runtime.Tests}\n§F{f1:Test:pub}\n§O{void}\n§/F{f1}\n§/M{m1}";

        // Act
        var result = Program.Compile(source);

        // Assert: Should contain "namespace Calor.Runtime.Tests" (not "Calor_Runtime_Tests")
        Assert.False(result.HasErrors, GetErrors(result));
        Assert.NotNull(result.GeneratedCode);
        Assert.Contains("namespace Calor.Runtime.Tests", result.GeneratedCode);
        Assert.DoesNotContain("Calor_Runtime_Tests", result.GeneratedCode);
    }

    [Fact]
    public void Compile_DottedNamespace_ModuleClassUsesLastSegment()
    {
        // Arrange: Calor source with dotted namespace and a function
        var source = "§M{m1:Calor.Runtime.Tests}\n§F{f1:TestFunc:pub}\n§O{void}\n§/F{f1}\n§/M{m1}";

        // Act
        var result = Program.Compile(source);

        // Assert: Module class should be "TestsModule" not "Calor.Runtime.TestsModule"
        Assert.False(result.HasErrors, GetErrors(result));
        Assert.NotNull(result.GeneratedCode);
        Assert.Contains("public static class TestsModule", result.GeneratedCode);
        Assert.DoesNotContain("Calor.Runtime.TestsModule", result.GeneratedCode);
        Assert.DoesNotContain("Calor_Runtime_TestsModule", result.GeneratedCode);
    }

    [Fact]
    public void Compile_SimpleNamespace_NoDotsToPreserve()
    {
        // Arrange
        var source = "§M{m1:MyNamespace}\n§F{f1:Test:pub}\n§O{void}\n§/F{f1}\n§/M{m1}";

        // Act
        var result = Program.Compile(source);

        // Assert
        Assert.False(result.HasErrors, GetErrors(result));
        Assert.NotNull(result.GeneratedCode);
        Assert.Contains("namespace MyNamespace", result.GeneratedCode);
    }

    [Fact]
    public void Compile_DeepDottedNamespace_PreservesAllDots()
    {
        // Arrange
        var source = "§M{m1:Company.Product.Feature.SubFeature.Tests}\n§F{f1:Test:pub}\n§O{void}\n§/F{f1}\n§/M{m1}";

        // Act
        var result = Program.Compile(source);

        // Assert
        Assert.False(result.HasErrors, GetErrors(result));
        Assert.NotNull(result.GeneratedCode);
        Assert.Contains("namespace Company.Product.Feature.SubFeature.Tests", result.GeneratedCode);
    }

    [Fact]
    public void Convert_CSharpWithDottedNamespace_PreservesDots_Roundtrip()
    {
        // Arrange
        var csharpSource = """
            namespace MyCompany.MyProduct.MyFeature
            {
                public class TestClass
                {
                    public void Test() { }
                }
            }
            """;

        // Act: Convert C# to Calor
        var converter = new CSharpToCalorConverter();
        var conversionResult = converter.Convert(csharpSource);
        Assert.True(conversionResult.Success, GetConversionErrors(conversionResult));

        // Act: Compile Calor back to C#
        var compilationResult = Program.Compile(conversionResult.CalorSource!);

        // Assert
        Assert.False(compilationResult.HasErrors, GetErrors(compilationResult));
        Assert.NotNull(compilationResult.GeneratedCode);
        Assert.Contains("namespace MyCompany.MyProduct.MyFeature", compilationResult.GeneratedCode);
        Assert.DoesNotContain("MyCompany_MyProduct_MyFeature", compilationResult.GeneratedCode);
    }

    private static string GetErrors(CompilationResult result)
    {
        return string.Join("\n", result.Diagnostics.Where(d => d.IsError).Select(d => d.Message));
    }

    private static string GetConversionErrors(ConversionResult result)
    {
        return string.Join("\n", result.Issues.Select(i => i.Message));
    }
}
