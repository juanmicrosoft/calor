using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for compiler gap fixes discovered during native interop sample conversion.
/// Covers: struct support, static fields, global namespace, explicit update replacements.
/// </summary>
public class CompilerGapFixTests
{
    #region Helpers

    private static string ParseAndEmit(string source)
    {
        var diagnostics = new DiagnosticBag();
        diagnostics.SetFilePath("test.calr");

        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();

        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        return emitter.Emit(module);
    }

    private static DiagnosticBag ParseWithDiagnostics(string source)
    {
        var diagnostics = new DiagnosticBag();
        diagnostics.SetFilePath("test.calr");

        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();

        var parser = new Parser(tokens, diagnostics);
        parser.Parse();

        return diagnostics;
    }

    #endregion

    #region Fix 1: Struct Support

    [Fact]
    public void Struct_BasicDeclaration()
    {
        var source = @"
§M{TestMod}
  §CL{MyPoint:pub:struct}
      §FLD{i32:X:pub}
      §FLD{i32:Y:pub}
";

        var result = ParseAndEmit(source);

        Assert.Contains("public struct MyPoint", result);
        Assert.DoesNotContain("public class MyPoint", result);
    }

    [Fact]
    public void Struct_MultiPositionModifiers()
    {
        // §CL{Name:pub:struct} — modifiers span _pos2 and _pos3
        var source = @"
§M{TestMod}
  §CL{MyPoint:pub:struct}
      §FLD{i32:X:pub}
";

        var result = ParseAndEmit(source);

        Assert.Contains("public struct MyPoint", result);
    }

    [Fact]
    public void Struct_BackwardCompat()
    {
        var source = @"
§M{TestMod}
  §CL{MyClass:pub}
";

        var result = ParseAndEmit(source);

        Assert.Contains("public class MyClass", result);
        Assert.DoesNotContain("struct", result);
    }

    [Fact]
    public void Struct_SealedStruct()
    {
        // C# structs are implicitly sealed, so "sealed" is correctly omitted from output
        var source = @"
§M{TestMod}
  §CL{MyVal:pub:struct seal}
";

        var result = ParseAndEmit(source);

        // Struct emitted correctly (sealed is implicit for structs in C#)
        Assert.Contains("public struct MyVal", result);
        Assert.DoesNotContain("sealed struct", result);
    }

    [Fact]
    public void Struct_AbstractStruct_ReportsDiagnostic()
    {
        var source = @"
§M{TestMod}
  §CL{Bad:struct abs}
";

        var diagnostics = ParseWithDiagnostics(source);

        // Should report an error about structs not being abstract
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.InvalidModifier);
    }

    #endregion

    #region Fix 2: Static Fields

    [Fact]
    public void StaticField_EmitsStaticKeyword()
    {
        var source = @"
§M{TestMod}
  §CL{Counter:pub}
      §FLD{i32:Count:pub:stat}
";

        var result = ParseAndEmit(source);

        Assert.Contains("public static int Count;", result);
    }

    [Fact]
    public void StaticField_BackwardCompat()
    {
        var source = @"
§M{TestMod}
  §CL{MyClass:pub}
      §FLD{str:_name:priv}
";

        var result = ParseAndEmit(source);

        Assert.Contains("private string _name;", result);
        Assert.DoesNotContain("static", result.Split('\n').First(l => l.Contains("_name")));
    }

    #endregion

    #region Fix 3: Global Namespace

    [Fact]
    public void GlobalNamespace_SuppressesWrapper()
    {
        var source = @"
§M{_global}
  §CL{MyClass:pub}
";

        var result = ParseAndEmit(source);

        Assert.DoesNotContain("namespace", result);
        Assert.Contains("public class MyClass", result);
    }

    [Fact]
    public void GlobalNamespace_BackwardCompat()
    {
        var source = @"
§M{MyApp}
  §CL{MyClass:pub}
";

        var result = ParseAndEmit(source);

        Assert.Contains("namespace MyApp", result);
    }

    #endregion

    #region Fix 4: Increment/Decrement Replacements

    [Fact]
    public void Increment_PrefixReplacement_UsesUpdatedValue()
    {
        var source = @"
§M{TestMod}
  §F{Main:pub} () -> i32
    §B{~x:i32} 0
    §ASSIGN x (+ x 1)
    §B{value:i32} x
    §R value
";

        var result = ParseAndEmit(source);

        Assert.Contains("x = checked(x + 1);", result);
        Assert.Contains("int value = x;", result);
        Assert.True(result.IndexOf("x = checked(x + 1);", StringComparison.Ordinal)
            < result.IndexOf("int value = x;", StringComparison.Ordinal));
        Assert.Contains("return value;", result);
    }

    [Fact]
    public void Increment_PostfixReplacement_CapturesPreviousValue()
    {
        var source = @"
§M{TestMod}
  §F{Main:pub} () -> i32
    §B{~x:i32} 0
    §B{previous:i32} x
    §ASSIGN x (+ x 1)
    §R previous
";

        var result = ParseAndEmit(source);

        Assert.Contains("x = checked(x + 1);", result);
        Assert.Contains("int previous = x;", result);
        Assert.True(result.IndexOf("int previous = x;", StringComparison.Ordinal)
            < result.IndexOf("x = checked(x + 1);", StringComparison.Ordinal));
        Assert.Contains("return previous;", result);
    }

    [Fact]
    public void Decrement_PrefixReplacement_UsesUpdatedValue()
    {
        var source = @"
§M{TestMod}
  §F{Main:pub} () -> i32
    §B{~x:i32} 0
    §ASSIGN x (- x 1)
    §B{value:i32} x
    §R value
";

        var result = ParseAndEmit(source);

        Assert.Contains("x = checked(x - 1);", result);
        Assert.Contains("int value = x;", result);
        Assert.True(result.IndexOf("x = checked(x - 1);", StringComparison.Ordinal)
            < result.IndexOf("int value = x;", StringComparison.Ordinal));
        Assert.Contains("return value;", result);
    }

    [Fact]
    public void Decrement_PostfixReplacement_CapturesPreviousValue()
    {
        var source = @"
§M{TestMod}
  §F{Main:pub} () -> i32
    §B{~x:i32} 0
    §B{previous:i32} x
    §ASSIGN x (- x 1)
    §R previous
";

        var result = ParseAndEmit(source);

        Assert.Contains("x = checked(x - 1);", result);
        Assert.Contains("int previous = x;", result);
        Assert.True(result.IndexOf("int previous = x;", StringComparison.Ordinal)
            < result.IndexOf("x = checked(x - 1);", StringComparison.Ordinal));
        Assert.Contains("return previous;", result);
    }

    #endregion

    #region Integration

    [Fact]
    public void Integration_AllFixes()
    {
        var source = @"
§M{_global}
  §CL{Vector2:pub:struct}
      §FLD{i32:X:pub}
      §FLD{i32:Y:pub}
      §FLD{i32:InstanceCount:pub:stat}
      §MT{Advance:pub}
          §O{void}
          §ASSIGN X (+ X 1)
          §ASSIGN InstanceCount (+ InstanceCount 1)
";

        var result = ParseAndEmit(source);

        // Global namespace: no namespace wrapper
        Assert.DoesNotContain("namespace", result);
        // Struct (via multi-position: pub in _pos2, struct in _pos3)
        Assert.Contains("public struct Vector2", result);
        // Static field
        Assert.Contains("public static int InstanceCount;", result);
        Assert.Contains("X = checked(X + 1);", result);
        Assert.Contains("InstanceCount = checked(InstanceCount + 1);", result);
    }

    #endregion
}
