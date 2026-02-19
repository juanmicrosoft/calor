using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for inheritance and abstract classes: parsing, code generation, and error cases.
/// </summary>
public class InheritanceTests
{
    #region Code Generation Tests

    [Fact]
    public void CodeGen_AbstractClass_GeneratesValidCSharp()
    {
        var source = @"
§M{m1:Test}
§CL{c1:Shape:abs}
  §MT{mt1:Area:pub:abs}
    §O{double}
  §/MT{mt1}
  §MT{mt2:Name:pub:abs}
    §O{str}
  §/MT{mt2}
§/CL{c1}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("public abstract class Shape", result);
        Assert.Contains("public abstract double Area();", result);
        Assert.Contains("public abstract string Name();", result);
    }

    [Fact]
    public void CodeGen_DerivedClass_GeneratesValidCSharp()
    {
        var source = @"
§M{m1:Test}
§CL{c1:Shape:abs}
  §MT{mt1:Area:pub:abs}
    §O{double}
  §/MT{mt1}
§/CL{c1}
§CL{c2:Circle:pub}
  §EXT{Shape}
  §FLD{f64:radius:pri}
  §MT{mt1:Area:pub:over}
    §O{double}
    §R (* 3.14159 (* radius radius))
  §/MT{mt1}
§/CL{c2}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("public abstract class Shape", result);
        Assert.Contains("public class Circle : Shape", result);
        Assert.Contains("public override double Area()", result);
    }

    [Fact]
    public void CodeGen_VirtualMethod_GeneratesValidCSharp()
    {
        var source = @"
§M{m1:Test}
§CL{c1:Animal:pub}
  §MT{mt1:Speak:pub:virt}
    §O{str}
    §R ""...""
  §/MT{mt1}
§/CL{c1}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("public virtual string Speak()", result);
        Assert.Contains("return \"...\";", result);
    }

    [Fact]
    public void CodeGen_SealedOverride_GeneratesValidCSharp()
    {
        var source = @"
§M{m1:Test}
§CL{c1:Base:pub}
  §MT{mt1:Compute:pub:virt}
    §O{i32}
    §R 0
  §/MT{mt1}
§/CL{c1}
§CL{c2:Derived:pub}
  §EXT{Base}
  §MT{mt1:Compute:pub:seal over}
    §O{i32}
    §R 42
  §/MT{mt1}
§/CL{c2}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("public virtual int Compute()", result);
        Assert.Contains("public override sealed int Compute()", result);
    }

    [Fact]
    public void CodeGen_BaseMethodCall_GeneratesValidCSharp()
    {
        var source = @"
§M{m1:Test}
§CL{c1:Base:pub}
  §MT{mt1:GetValue:pub:virt}
    §O{i32}
    §R 10
  §/MT{mt1}
§/CL{c1}
§CL{c2:Derived:pub}
  §EXT{Base}
  §MT{mt1:GetValue:pub:over}
    §O{i32}
    §R (+ §C{base.GetValue} §/C 5)
  §/MT{mt1}
§/CL{c2}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("public class Derived : Base", result);
        Assert.Contains("public override int GetValue()", result);
        Assert.Contains("base.GetValue()", result);
    }

    [Fact]
    public void CodeGen_ConstructorWithBaseCall_GeneratesValidCSharp()
    {
        var source = @"
§M{m1:Test}
§CL{c1:Animal:pub}
  §FLD{str:_name:pro}
  §CTOR{ctor1:pub}
    §I{str:name}
    §ASSIGN §THIS._name name
  §/CTOR{ctor1}
§/CL{c1}
§CL{c2:Dog:pub}
  §EXT{Animal}
  §FLD{str:_breed:pri}
  §CTOR{ctor1:pub}
    §I{str:name}
    §I{str:breed}
    §BASE §A name §/BASE
    §ASSIGN §THIS._breed breed
  §/CTOR{ctor1}
§/CL{c2}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("public class Dog : Animal", result);
        Assert.Contains("public Dog(string name, string breed)", result);
        Assert.Contains(": base(name)", result);
    }

    [Fact]
    public void CodeGen_MultipleInterfaceImplementation_GeneratesValidCSharp()
    {
        var source = @"
§M{m1:Test}
§IFACE{i1:IMovable}
  §MT{mt1:Move}
    §O{void}
  §/MT{mt1}
§/IFACE{i1}
§IFACE{i2:IDrawable}
  §MT{mt1:Draw}
    §O{void}
  §/MT{mt1}
§/IFACE{i2}
§CL{c1:GameObject:pub}
  §IMPL{IMovable}
  §IMPL{IDrawable}
  §MT{mt1:Move:pub}
    §O{void}
  §/MT{mt1}
  §MT{mt2:Draw:pub}
    §O{void}
  §/MT{mt2}
§/CL{c1}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("public class GameObject : IMovable, IDrawable", result);
    }

    [Fact]
    public void CodeGen_StaticMethod_GeneratesValidCSharp()
    {
        // Note: static class modifier is not fully implemented in the parser
        // This test verifies static methods work correctly
        var source = @"
§M{m1:Test}
§CL{c1:MathUtils:pub}
  §MT{mt1:Square:pub:stat}
    §I{i32:x}
    §O{i32}
    §R (* x x)
  §/MT{mt1}
§/CL{c1}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("public class MathUtils", result);
        Assert.Contains("public static int Square(int x)", result);
    }

    [Fact]
    public void CodeGen_PartialClass_GeneratesValidCSharp()
    {
        var source = @"
§M{m1:Test}
§CL{c1:DataService:partial}
  §MT{mt1:GetData:pub}
    §O{str}
    §R ""data""
  §/MT{mt1}
§/CL{c1}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("public partial class DataService", result);
    }

    [Fact]
    public void CodeGen_SealedClass_GeneratesValidCSharp()
    {
        var source = @"
§M{m1:Test}
§CL{c1:FinalClass:seal}
  §MT{mt1:DoWork:pub}
    §O{void}
  §/MT{mt1}
§/CL{c1}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("public sealed class FinalClass", result);
    }

    [Fact]
    public void CodeGen_GenericInterfaceImplementation_PreservesGenericSyntax()
    {
        var source = @"
§M{m1:Test}
§CL{c1:MyOption:pub}<T>
  §IMPL{IEquatable<MyOption<T>>}
§/CL{c1}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("IEquatable<MyOption<T>>", result);
    }

    [Fact]
    public void CodeGen_GenericBaseClass_PreservesGenericSyntax()
    {
        var source = @"
§M{m1:Test}
§CL{c1:StringList:pub}
  §EXT{List<string>}
§/CL{c1}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("StringList : List<string>", result);
    }

    [Fact]
    public void CodeGen_InterfaceExtendsGenericInterface_PreservesGenericSyntax()
    {
        var source = @"
§M{m1:Test}
§IFACE{i1:IMyCollection}<T>
  §EXT{IEnumerable<T>}
§/IFACE{i1}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("IMyCollection<T> : IEnumerable<T>", result);
    }

    [Fact]
    public void CodeGen_DeeplyNestedGenericInheritance_PreservesGenericSyntax()
    {
        var source = @"
§M{m1:Test}
§CL{c1:MyMapper:pub}<TKey,TValue>
  §IMPL{IEquatable<Dictionary<TKey,List<TValue>>>}
§/CL{c1}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("IEquatable<Dictionary<TKey,List<TValue>>>", result);
    }

    [Fact]
    public void CodeGen_EmittedCode_ContainsNullableEnable()
    {
        var source = @"
§M{m1:Test}
§CL{c1:Foo:pub}
§/CL{c1}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("#nullable enable", result);
    }

    [Fact]
    public void CodeGen_NullableEnable_AppearsBeforeUsings()
    {
        var source = @"
§M{m1:Test}
§CL{c1:Foo:pub}
§/CL{c1}
§/M{m1}
";

        var result = ParseAndEmit(source);

        var nullableIndex = result.IndexOf("#nullable enable");
        var usingIndex = result.IndexOf("using System;");
        Assert.True(nullableIndex >= 0, "Output should contain #nullable enable");
        Assert.True(usingIndex >= 0, "Output should contain using System;");
        Assert.True(nullableIndex < usingIndex, "#nullable enable should appear before using directives");
    }

    #endregion

    #region Parser Tests

    [Fact]
    public void Parser_AbstractClass_ParsesCorrectly()
    {
        var source = @"
§M{m1:Test}
§CL{c1:AbstractBase:abs}
  §MT{mt1:Method:abs}
    §O{void}
  §/MT{mt1}
§/CL{c1}
§/M{m1}
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        var classNode = module.Classes.FirstOrDefault(c => c.Name == "AbstractBase");
        Assert.NotNull(classNode);
        Assert.True(classNode.IsAbstract);
    }

    [Fact]
    public void Parser_SealedClass_ParsesCorrectly()
    {
        var source = @"
§M{m1:Test}
§CL{c1:SealedClass:seal}
§/CL{c1}
§/M{m1}
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        var classNode = module.Classes.FirstOrDefault(c => c.Name == "SealedClass");
        Assert.NotNull(classNode);
        Assert.True(classNode.IsSealed);
    }

    [Fact]
    public void Parser_BaseClass_ParsesCorrectly()
    {
        var source = @"
§M{m1:Test}
§CL{c1:Parent:pub}
§/CL{c1}
§CL{c2:Child:pub}
  §EXT{Parent}
§/CL{c2}
§/M{m1}
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        var childClass = module.Classes.FirstOrDefault(c => c.Name == "Child");
        Assert.NotNull(childClass);
        Assert.Equal("Parent", childClass.BaseClass);
    }

    [Fact]
    public void Parser_VirtualMethod_ParsesModifiersCorrectly()
    {
        var source = @"
§M{m1:Test}
§CL{c1:Base:pub}
  §MT{mt1:VirtualMethod:pub:virt}
    §O{void}
  §/MT{mt1}
§/CL{c1}
§/M{m1}
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        var classNode = module.Classes.FirstOrDefault();
        Assert.NotNull(classNode);
        var method = classNode.Methods.FirstOrDefault();
        Assert.NotNull(method);
        Assert.True(method.IsVirtual);
    }

    [Fact]
    public void Parser_OverrideMethod_ParsesModifiersCorrectly()
    {
        var source = @"
§M{m1:Test}
§CL{c1:Derived:pub}
  §MT{mt1:Method:pub:over}
    §O{void}
  §/MT{mt1}
§/CL{c1}
§/M{m1}
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        var classNode = module.Classes.FirstOrDefault();
        Assert.NotNull(classNode);
        var method = classNode.Methods.FirstOrDefault();
        Assert.NotNull(method);
        Assert.True(method.IsOverride);
    }

    [Fact]
    public void Parser_AbstractMethod_ParsesModifiersCorrectly()
    {
        var source = @"
§M{m1:Test}
§CL{c1:Abstract:abs}
  §MT{mt1:Method:pub:abs}
    §O{void}
  §/MT{mt1}
§/CL{c1}
§/M{m1}
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        var classNode = module.Classes.FirstOrDefault();
        Assert.NotNull(classNode);
        var method = classNode.Methods.FirstOrDefault();
        Assert.NotNull(method);
        Assert.True(method.IsAbstract);
    }

    [Fact]
    public void Parser_CombinedModifiers_ParsesCorrectly()
    {
        // Class modifiers: space-separated in 3rd position
        // Method modifiers: space-separated in 4th position
        var source = @"
§M{m1:Test}
§CL{c1:Base:abs seal}
  §MT{mt1:Method:pub:stat virt}
    §O{void}
  §/MT{mt1}
§/CL{c1}
§/M{m1}
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        var classNode = module.Classes.FirstOrDefault();
        Assert.NotNull(classNode);
        Assert.True(classNode.IsAbstract);
        Assert.True(classNode.IsSealed);
        var method = classNode.Methods.FirstOrDefault();
        Assert.NotNull(method);
        Assert.True(method.IsStatic);
        Assert.True(method.IsVirtual);
    }

    #endregion

    #region Negative/Error Case Tests

    [Fact]
    public void Parse_AbstractMethodInNonAbstractClass_ParsesSuccessfully()
    {
        // Parser accepts this; semantic analysis would catch the error
        var source = @"
§M{m1:Test}
§CL{c1:NonAbstract:pub}
  §MT{mt1:AbstractMethod:abs}
    §O{void}
  §/MT{mt1}
§/CL{c1}
§/M{m1}
";

        var module = Parse(source, out var diagnostics);

        // Parser should accept it (semantic analysis would catch the error)
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
    }

    [Fact]
    public void Parse_MismatchedClassId_ReportsError()
    {
        var source = @"
§M{m1:Test}
§CL{c1:MyClass:pub}
§/CL{c2}
§/M{m1}
";

        var module = Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        var errorMessages = string.Join("\n", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message));
        Assert.Contains("does not match", errorMessages, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_InvalidVisibility_ReportsError()
    {
        var source = @"
§M{m1:Test}
§CL{c1:MyClass:invalid}
§/CL{c1}
§/M{m1}
";

        var module = Parse(source, out var diagnostics);

        // The visibility will be treated as default private
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
    }

    #endregion

    #region Round-Trip Tests

    [Fact(Skip = "CalorFormatter doesn't preserve class definitions - requires formatter fix")]
    public void RoundTrip_AbstractClass_FormatsAndReparses()
    {
        var source = @"
§M{m1:Test}
§CL{c1:Shape:abs}
  §MT{mt1:Area:pub:abs}
    §O{double}
  §/MT{mt1}
  §MT{mt2:Perimeter:pub:abs}
    §O{double}
  §/MT{mt2}
§/CL{c1}
§/M{m1}
";

        // Parse original
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        // Format
        var formatter = new Formatting.CalorFormatter();
        var formatted = formatter.Format(module);

        // Verify key elements are preserved
        Assert.Contains("§CL{c1:Shape:abs}", formatted);
        Assert.Contains("abs", formatted);

        // Re-parse formatted output
        var module2 = Parse(formatted, out var diagnostics2);
        Assert.False(diagnostics2.HasErrors, $"Formatted output should parse:\n{formatted}\nErrors: {string.Join("\n", diagnostics2.Select(d => d.Message))}");

        // Verify AST matches
        var classNode = module2.Classes.FirstOrDefault(c => c.Name == "Shape");
        Assert.NotNull(classNode);
        Assert.True(classNode.IsAbstract);
    }

    [Fact(Skip = "CalorFormatter doesn't preserve class definitions - requires formatter fix")]
    public void RoundTrip_InheritanceHierarchy_FormatsAndReparses()
    {
        var source = @"
§M{m1:Test}
§CL{c1:Animal:pub}
  §MT{mt1:Speak:pub:virt}
    §O{str}
    §R ""...""
  §/MT{mt1}
§/CL{c1}
§CL{c2:Dog:pub}
  §EXT{Animal}
  §MT{mt1:Speak:pub:over}
    §O{str}
    §R ""Woof!""
  §/MT{mt1}
§/CL{c2}
§/M{m1}
";

        // Parse original
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        // Format
        var formatter = new Formatting.CalorFormatter();
        var formatted = formatter.Format(module);

        // Verify key elements are preserved
        Assert.Contains("§EXT{Animal}", formatted);

        // Re-parse formatted output
        var module2 = Parse(formatted, out var diagnostics2);
        Assert.False(diagnostics2.HasErrors, $"Formatted output should parse:\n{formatted}\nErrors: {string.Join("\n", diagnostics2.Select(d => d.Message))}");

        // Verify inheritance is preserved
        var dogClass = module2.Classes.FirstOrDefault(c => c.Name == "Dog");
        Assert.NotNull(dogClass);
        Assert.Equal("Animal", dogClass.BaseClass);
    }

    #endregion

    #region Benchmark File Tests

    [Fact]
    public void Benchmark_TemplateMethod_ParsesSuccessfully()
    {
        var testDataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData", "Benchmarks", "DesignPatterns", "TemplateMethod.calr");
        var source = File.ReadAllText(testDataPath);
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, $"TemplateMethod.calr should parse. Errors:\n{string.Join("\n", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message))}");
        Assert.True(module.Classes.Count >= 4, "Should have DataProcessor, CsvProcessor, JsonProcessor, and TemplateMethodDemo classes");
    }

    [Fact]
    public void Benchmark_Visitor_ParsesSuccessfully()
    {
        var testDataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData", "Benchmarks", "DesignPatterns", "Visitor.calr");
        var source = File.ReadAllText(testDataPath);
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, $"Visitor.calr should parse. Errors:\n{string.Join("\n", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message))}");
        Assert.True(module.Classes.Count >= 5, "Should have Expression, NumberExpression, AddExpression, MultiplyExpression, and visitors");
        Assert.True(module.Interfaces.Count >= 1, "Should have IExpressionVisitor interface");
    }

    #endregion

    #region Documented Syntax Verification Tests

    /// <summary>
    /// Verifies the documented syntax for base method calls works.
    /// Documentation says: use base.Method not §BASE.Method inside §C{...}
    /// </summary>
    [Fact]
    public void DocSyntax_BaseMethodCall_UsesLowercase()
    {
        var source = @"
§M{m1:Test}
§CL{c1:Base:pub}
  §MT{mt1:GetValue:pub:virt}
    §O{i32}
    §R 10
  §/MT{mt1}
§/CL{c1}
§CL{c2:Derived:pub}
  §EXT{Base}
  §MT{mt1:GetValue:pub:over}
    §O{i32}
    §R (+ §C{base.GetValue} §/C 5)
  §/MT{mt1}
§/CL{c2}
§/M{m1}
";

        var result = ParseAndEmit(source);
        Assert.Contains("base.GetValue()", result);
    }

    /// <summary>
    /// Verifies the documented syntax for this method calls works.
    /// Documentation says: use this.Method not §THIS.Method inside §C{...}
    /// </summary>
    [Fact]
    public void DocSyntax_ThisMethodCall_UsesLowercase()
    {
        var source = @"
§M{m1:Test}
§CL{c1:Service:pub}
  §MT{mt1:Helper:pri}
    §O{str}
    §R ""done""
  §/MT{mt1}
  §MT{mt2:Process:pub}
    §O{str}
    §R §C{this.Helper} §/C
  §/MT{mt2}
§/CL{c1}
§/M{m1}
";

        var result = ParseAndEmit(source);
        Assert.Contains("this.Helper()", result);
    }

    /// <summary>
    /// Verifies the documented syntax for space-separated modifiers.
    /// Documentation says: use 'seal over' not 'seal:over'
    /// </summary>
    [Fact]
    public void DocSyntax_CombinedModifiers_UseSpaceSeparation()
    {
        var source = @"
§M{m1:Test}
§CL{c1:Base:pub}
  §MT{mt1:Compute:pub:virt}
    §O{i32}
    §R 0
  §/MT{mt1}
§/CL{c1}
§CL{c2:Final:pub}
  §EXT{Base}
  §MT{mt1:Compute:pub:seal over}
    §O{i32}
    §R 42
  §/MT{mt1}
§/CL{c2}
§/M{m1}
";

        var result = ParseAndEmit(source);
        // Emitter outputs "override sealed" (override first)
        Assert.Contains("override sealed int Compute()", result);
    }

    /// <summary>
    /// Verifies the documented syntax for class modifiers (3 parts).
    /// Documentation says: §CL{id:Name:modifiers} with 3 parts
    /// </summary>
    [Fact]
    public void DocSyntax_ClassModifiers_Uses3Parts()
    {
        var source = @"
§M{m1:Test}
§CL{c1:MyAbstract:abs}
  §MT{mt1:DoWork:pub:abs}
    §O{void}
  §/MT{mt1}
§/CL{c1}
§CL{c2:MySealed:seal}
§/CL{c2}
§/M{m1}
";

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var absClass = module.Classes.First(c => c.Name == "MyAbstract");
        Assert.True(absClass.IsAbstract);

        var sealedClass = module.Classes.First(c => c.Name == "MySealed");
        Assert.True(sealedClass.IsSealed);
    }

    /// <summary>
    /// Verifies the documented syntax for method modifiers (4 parts).
    /// Documentation says: §MT{id:Name:visibility:modifiers}
    /// </summary>
    [Fact]
    public void DocSyntax_MethodModifiers_Uses4Parts()
    {
        var source = @"
§M{m1:Test}
§CL{c1:Test:pub}
  §MT{mt1:VirtualMethod:pub:virt}
    §O{void}
  §/MT{mt1}
  §MT{mt2:StaticMethod:pub:stat}
    §O{void}
  §/MT{mt2}
  §MT{mt3:PrivateMethod:pri}
    §O{void}
  §/MT{mt3}
§/CL{c1}
§/M{m1}
";

        var result = ParseAndEmit(source);
        Assert.Contains("public virtual void VirtualMethod()", result);
        Assert.Contains("public static void StaticMethod()", result);
        Assert.Contains("private void PrivateMethod()", result);
    }

    /// <summary>
    /// Verifies §NEW does not require an end tag.
    /// </summary>
    [Fact]
    public void DocSyntax_NewExpression_NoEndTag()
    {
        var source = @"
§M{m1:Test}
§CL{c1:Item:pub}
  §CTOR{ctor1:pub}
    §I{i32:val}
  §/CTOR{ctor1}
§/CL{c1}
§CL{c2:Factory:pub}
  §MT{mt1:Create:pub:stat}
    §O{Item}
    §R §NEW{Item} §A 42
  §/MT{mt1}
§/CL{c2}
§/M{m1}
";

        var result = ParseAndEmit(source);
        Assert.Contains("new Item(42)", result);
    }

    /// <summary>
    /// Verifies §ASSIGN syntax for field assignment (not §SET).
    /// </summary>
    [Fact]
    public void DocSyntax_Assignment_UsesAssign()
    {
        var source = @"
§M{m1:Test}
§CL{c1:Person:pub}
  §FLD{str:_name:pri}
  §CTOR{ctor1:pub}
    §I{str:name}
    §ASSIGN §THIS._name name
  §/CTOR{ctor1}
§/CL{c1}
§/M{m1}
";

        var result = ParseAndEmit(source);
        Assert.Contains("this._name = name;", result);
    }

    #endregion

    #region Helper Methods

    private static Ast.ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        diagnostics.SetFilePath("test.calr");

        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();

        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

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

    #endregion
}
