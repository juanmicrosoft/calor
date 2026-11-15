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
module TestMod TestMod block
  class MyPoint struct
    field int:X
    field int:Y
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
module TestMod TestMod block
  class MyPoint struct
    field int:X
";

        var result = ParseAndEmit(source);

        Assert.Contains("public struct MyPoint", result);
    }

    [Fact]
    public void Struct_BackwardCompat()
    {
        var source = @"
module TestMod TestMod block
  class MyClass
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
module TestMod TestMod block
  class MyVal sealed struct
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
module TestMod TestMod block
  class Bad struct abstract
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
module TestMod TestMod block
  class Counter
    field int:Count static
";

        var result = ParseAndEmit(source);

        Assert.Contains("public static int Count;", result);
    }

    [Fact]
    public void StaticField_BackwardCompat()
    {
        var source = @"
module TestMod TestMod block
  class MyClass
    field string:_name private
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
module _global
  class MyClass
";

        var result = ParseAndEmit(source);

        Assert.DoesNotContain("namespace", result);
        Assert.Contains("public class MyClass", result);
    }

    [Fact]
    public void GlobalNamespace_BackwardCompat()
    {
        var source = @"
module MyApp MyApp block
  class MyClass
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
module TestMod TestMod block
  func Main () → int
    !
    var int:x = 0
    set x (x + 1)
    let int:value = x
    return value
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
module TestMod TestMod block
  func Main () → int
    !
    var int:x = 0
    let int:previous = x
    set x (x + 1)
    return previous
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
module TestMod TestMod block
  func Main () → int
    !
    var int:x = 0
    set x (x - 1)
    let int:value = x
    return value
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
module TestMod TestMod block
  func Main () → int
    !
    var int:x = 0
    let int:previous = x
    set x (x - 1)
    return previous
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
module _global
  class Vector2 struct
    field int:X
    field int:Y
    field int:InstanceCount static
    func Advance () → void
      !
      set X (X + 1)
      set InstanceCount (InstanceCount + 1)
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
