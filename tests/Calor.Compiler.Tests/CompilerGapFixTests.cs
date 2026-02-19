using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for compiler gap fixes discovered during native interop sample conversion.
/// Covers: struct support, static fields, global namespace, increment/decrement operators.
/// </summary>
public class CompilerGapFixTests
{
    #region Helpers

    private static string ParseAndEmit(string source)
    {
        var diagnostics = new DiagnosticBag();
        diagnostics.SetFilePath("test.calr");

        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();

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
        var tokens = lexer.TokenizeAll();

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
§M{m1:TestMod}
§CL{c1:MyPoint:struct}
  §FLD{i32:X:pub}
  §FLD{i32:Y:pub}
§/CL{c1}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("public struct MyPoint", result);
        Assert.DoesNotContain("public class MyPoint", result);
    }

    [Fact]
    public void Struct_MultiPositionModifiers()
    {
        // §CL{c1:Name:pub:struct} — modifiers span _pos2 and _pos3
        var source = @"
§M{m1:TestMod}
§CL{c1:MyPoint:pub:struct}
  §FLD{i32:X:pub}
§/CL{c1}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("public struct MyPoint", result);
    }

    [Fact]
    public void Struct_BackwardCompat()
    {
        var source = @"
§M{m1:TestMod}
§CL{c1:MyClass:pub}
§/CL{c1}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("public class MyClass", result);
        Assert.DoesNotContain("struct", result);
    }

    [Fact]
    public void Struct_SealedStruct()
    {
        var source = @"
§M{m1:TestMod}
§CL{c1:MyVal:struct seal}
§/CL{c1}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("sealed struct MyVal", result);
    }

    [Fact]
    public void Struct_AbstractStruct_ReportsDiagnostic()
    {
        var source = @"
§M{m1:TestMod}
§CL{c1:Bad:struct abs}
§/CL{c1}
§/M{m1}
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
§M{m1:TestMod}
§CL{c1:Counter:pub}
  §FLD{i32:Count:pub:stat}
§/CL{c1}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("public static int Count;", result);
    }

    [Fact]
    public void StaticField_BackwardCompat()
    {
        var source = @"
§M{m1:TestMod}
§CL{c1:MyClass:pub}
  §FLD{str:_name:pri}
§/CL{c1}
§/M{m1}
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
§M{m1:_global}
§CL{c1:MyClass:pub}
§/CL{c1}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.DoesNotContain("namespace", result);
        Assert.Contains("public class MyClass", result);
    }

    [Fact]
    public void GlobalNamespace_BackwardCompat()
    {
        var source = @"
§M{m1:MyApp}
§CL{c1:MyClass:pub}
§/CL{c1}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("namespace MyApp", result);
    }

    #endregion

    #region Fix 4: Increment/Decrement

    [Fact]
    public void Increment_Prefix()
    {
        var source = @"
§M{m1:TestMod}
§F{f001:Main:pub}
  §O{void}
  §B{~x:i32} 0
  (inc x)
§/F{f001}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("(++x);", result);
    }

    [Fact]
    public void Increment_PostFix()
    {
        var source = @"
§M{m1:TestMod}
§F{f001:Main:pub}
  §O{void}
  §B{~x:i32} 0
  (post-inc x)
§/F{f001}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("(x++);", result);
    }

    [Fact]
    public void Decrement_Prefix()
    {
        var source = @"
§M{m1:TestMod}
§F{f001:Main:pub}
  §O{void}
  §B{~x:i32} 0
  (dec x)
§/F{f001}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("(--x);", result);
    }

    [Fact]
    public void Decrement_PostFix()
    {
        var source = @"
§M{m1:TestMod}
§F{f001:Main:pub}
  §O{void}
  §B{~x:i32} 0
  (post-dec x)
§/F{f001}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("(x--);", result);
    }

    #endregion

    #region Integration

    [Fact]
    public void Integration_AllFixes()
    {
        var source = @"
§M{m1:_global}
§CL{c1:Vector2:pub:struct}
  §FLD{i32:X:pub}
  §FLD{i32:Y:pub}
  §FLD{i32:InstanceCount:pub:stat}
  §MT{m001:Advance:pub}
    §O{void}
    (inc X)
    (post-inc InstanceCount)
  §/MT{m001}
§/CL{c1}
§/M{m1}
";

        var result = ParseAndEmit(source);

        // Global namespace: no namespace wrapper
        Assert.DoesNotContain("namespace", result);
        // Struct (via multi-position: pub in _pos2, struct in _pos3)
        Assert.Contains("public struct Vector2", result);
        // Static field
        Assert.Contains("public static int InstanceCount;", result);
        // Increment operators as standalone statements
        Assert.Contains("(++X);", result);
        Assert.Contains("(InstanceCount++);", result);
    }

    #endregion
}
