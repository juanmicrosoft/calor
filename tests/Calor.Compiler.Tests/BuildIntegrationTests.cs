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
§M{m001:Test}
§F{f001:Hello:pub}
  §O{void}
  §E{cw}
  §C{Console.WriteLine}
    §A ""Hello from test!""
  §/C
§/F{f001}
§/M{m001}
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
§M{m001:Test}
§F{f001:Hello:pub}
  §O{void}
    §INVALID_SYNTAX
§/F{f001}
§/M{m001}
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
§M{m001:Calculator}
§F{f001:Add:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §R (+ a b)
§/F{f001}
§/M{m001}
";

        // Act
        var result = Program.Compile(source, "calculator.calr");

        // Assert
        Assert.False(result.HasErrors);
        Assert.Contains("namespace Calculator", result.GeneratedCode);
        Assert.Contains("public static int Add(int a, int b)", result.GeneratedCode);
        Assert.Contains("return a + b", result.GeneratedCode);
    }

    [Fact]
    public void CompileCalor_Task_HandlesMultipleFunctions()
    {
        // Arrange
        var source = @"
§M{m001:Math}
§F{f001:Add:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §R (+ a b)
§/F{f001}

§F{f002:Subtract:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §R (- a b)
§/F{f002}
§/M{m001}
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
§M{m001:Safe}
§F{f001:Divide:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §Q (!= b INT:0)
  §R (/ a b)
§/F{f001}
§/M{m001}
";

        // Act
        var result = Program.Compile(source, "safe.calr");

        // Assert
        Assert.False(result.HasErrors);
        Assert.Contains("ContractViolationException", result.GeneratedCode);
        Assert.Contains("b != 0", result.GeneratedCode);
    }
}
