using Xunit;

namespace Opal.Compiler.Tests;

public class BuildIntegrationTests
{
    [Fact]
    public void CompileOpal_Task_CompilesSourceFile()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"opal_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var sourceFile = Path.Combine(tempDir, "test.opal");
            var source = @"
§MODULE[id=m001][name=Test]
§FUNC[id=f001][name=Hello][visibility=public]
  §OUT[type=VOID]
  §BODY
    §CALL[target=Console.WriteLine][fallible=false]
      §ARG STR:""Hello from test!""
    §END_CALL
  §END_BODY
§END_FUNC[id=f001]
§END_MODULE[id=m001]
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
    public void CompileOpal_Task_ReportsErrors()
    {
        // Arrange - Invalid OPAL source
        var source = @"
§MODULE[id=m001][name=Test]
§FUNC[id=f001][name=Hello][visibility=public]
  §OUT[type=VOID]
  §BODY
    §INVALID_SYNTAX
  §END_BODY
§END_FUNC[id=f001]
§END_MODULE[id=m001]
";

        // Act
        var result = Program.Compile(source, "test.opal");

        // Assert
        Assert.True(result.HasErrors);
    }

    [Fact]
    public void CompileOpal_Task_GeneratesValidCSharp()
    {
        // Arrange
        var source = @"
§MODULE[id=m001][name=Calculator]
§FUNC[id=f001][name=Add][visibility=public]
  §IN[name=a][type=INT]
  §IN[name=b][type=INT]
  §OUT[type=INT]
  §BODY
    §RETURN (+ a b)
  §END_BODY
§END_FUNC[id=f001]
§END_MODULE[id=m001]
";

        // Act
        var result = Program.Compile(source, "calculator.opal");

        // Assert
        Assert.False(result.HasErrors);
        Assert.Contains("namespace Calculator", result.GeneratedCode);
        Assert.Contains("public static int Add(int a, int b)", result.GeneratedCode);
        Assert.Contains("return (a + b)", result.GeneratedCode);
    }

    [Fact]
    public void CompileOpal_Task_HandlesMultipleFunctions()
    {
        // Arrange
        var source = @"
§MODULE[id=m001][name=Math]
§FUNC[id=f001][name=Add][visibility=public]
  §IN[name=a][type=INT]
  §IN[name=b][type=INT]
  §OUT[type=INT]
  §BODY
    §RETURN (+ a b)
  §END_BODY
§END_FUNC[id=f001]

§FUNC[id=f002][name=Subtract][visibility=public]
  §IN[name=a][type=INT]
  §IN[name=b][type=INT]
  §OUT[type=INT]
  §BODY
    §RETURN (- a b)
  §END_BODY
§END_FUNC[id=f002]
§END_MODULE[id=m001]
";

        // Act
        var result = Program.Compile(source, "math.opal");

        // Assert
        Assert.False(result.HasErrors);
        Assert.Contains("public static int Add(int a, int b)", result.GeneratedCode);
        Assert.Contains("public static int Subtract(int a, int b)", result.GeneratedCode);
    }

    [Fact]
    public void CompileOpal_Task_PreservesContracts()
    {
        // Arrange
        var source = @"
§MODULE[id=m001][name=Safe]
§FUNC[id=f001][name=Divide][visibility=public]
  §IN[name=a][type=INT]
  §IN[name=b][type=INT]
  §OUT[type=INT]
  §REQUIRES (!= b INT:0)
  §BODY
    §RETURN (/ a b)
  §END_BODY
§END_FUNC[id=f001]
§END_MODULE[id=m001]
";

        // Act
        var result = Program.Compile(source, "safe.opal");

        // Assert
        Assert.False(result.HasErrors);
        Assert.Contains("ArgumentException", result.GeneratedCode);
        Assert.Contains("b != 0", result.GeneratedCode);
    }
}
