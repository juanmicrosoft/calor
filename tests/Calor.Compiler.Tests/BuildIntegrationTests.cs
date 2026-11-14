using Xunit;

namespace Calor.Compiler.Tests;

public class BuildIntegrationTests
{
    [Fact]
    public void CompileCalor_Task_CompilesSourceFile()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"calor_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var sourceFile = Path.Combine(tempDir, "test.calr");
            var source = @"
module Test Test block
  func Hello () → void
    ! cw
    Console.WriteLine(""Hello from test!"")
";
            File.WriteAllText(sourceFile, source);

            // Act - Use the compiler directly (same as task would)
            var result = Program.Compile(source, sourceFile);

            // Assert
            Assert.False(result.HasErrors);
            Assert.NotEmpty(result.GeneratedCode);
            Assert.Contains("class TestModule", result.GeneratedCode);
            Assert.Contains("void Hello()", result.GeneratedCode);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void CompileCalor_Task_ReportsErrors()
    {
        // Arrange - Invalid Calor source
        var source = @"
§M{Test}
  §F{Hello:pub}
      §O{void}
        §INVALID_SYNTAX
";

        // Act
        var result = Program.Compile(source, "test.calr");

        // Assert
        Assert.True(result.HasErrors);
    }

    [Fact]
    public void CompileCalor_Task_GeneratesValidCSharp()
    {
        // Arrange
        var source = @"
module Calculator Calculator block
  func Add (int:a, int:b) → int
    !
    return (a + b)
";

        // Act
        var result = Program.Compile(source, "calculator.calr");

        // Assert
        Assert.False(result.HasErrors);
        Assert.Contains("namespace Calculator", result.GeneratedCode);
        Assert.Contains("public static int Add(int a, int b)", result.GeneratedCode);
        Assert.Contains("return checked(a + b)", result.GeneratedCode);
    }

    [Fact]
    public void CompileCalor_Task_HandlesMultipleFunctions()
    {
        // Arrange
        var source = @"
module Math Math block
  func Add (int:a, int:b) → int
    !
    return (a + b)

  func Subtract (int:a, int:b) → int
    !
    return (a - b)
";

        // Act
        var result = Program.Compile(source, "math.calr");

        // Assert
        Assert.False(result.HasErrors);
        Assert.Contains("public static int Add(int a, int b)", result.GeneratedCode);
        Assert.Contains("public static int Subtract(int a, int b)", result.GeneratedCode);
    }

    [Fact]
    public void CompileCalor_Task_PreservesContracts()
    {
        // Arrange
        var source = @"
module Safe Safe block
  func Divide (int:a, int:b) → int
    !
    requires (b != 0)
    return (a / b)
";

        // Act
        var result = Program.Compile(source, "safe.calr");

        // Assert
        Assert.False(result.HasErrors);
        Assert.Contains("ContractViolationException", result.GeneratedCode);
        Assert.Contains("b != 0", result.GeneratedCode);
    }
}
