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
module Test
  class Shape public abstract
    func Area public abstract () → double
      !
    func Name public abstract () → string
      !
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
module Test
  class Shape public abstract
    func Area public abstract () → double
      !
  class Circle public
    extends Shape
    field double:radius private
    func Area public override () → double
      !
      return 3.14159 * (radius * radius)
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
module Test
  class Animal public
    func Speak public virtual () → string
      !
      return ""...""
";

        var result = ParseAndEmit(source);

        Assert.Contains("public virtual string Speak()", result);
        Assert.Contains("return \"...\";", result);
    }

    [Fact]
    public void CodeGen_SealedOverride_GeneratesValidCSharp()
    {
        var source = @"
module Test
  class Base public
    func Compute public virtual () → int
      !
      return 0
  class Derived public
    extends Base
    func Compute public sealed override () → int
      !
      return 42
";

        var result = ParseAndEmit(source);

        Assert.Contains("public virtual int Compute()", result);
        Assert.Contains("public override sealed int Compute()", result);
    }

    [Fact]
    public void CodeGen_BaseMethodCall_GeneratesValidCSharp()
    {
        var source = @"
module Test
  class Base public
    func GetValue public virtual () → int
      !
      return 10
  class Derived public
    extends Base
    func GetValue public override () → int
      !
      return base.GetValue() + 5
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
module Test
  class Animal public
    field string:_name protected
    ctor (string:name)
      set this._name name
  class Dog public
    extends Animal
    field string:_breed private
    ctor (string:name, string:breed)
      base(name)
      set this._breed breed
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
module Test
  interface IMovable
    func Move () → void
      !
  interface IDrawable
    func Draw () → void
      !
  class GameObject public
    implements IMovable
    implements IDrawable
    func Move public () → void
      !
    func Draw public () → void
      !
";

        var result = ParseAndEmit(source);

        Assert.Contains("public class GameObject : IMovable, IDrawable", result);
    }

    [Fact]
    public void CodeGen_StaticMethod_GeneratesValidCSharp()
    {
        // Ordinary classes can contain static methods without becoming named modules.
        var source = @"
module Test
  class MathUtils public
    func Square public static (int:x) → int
      !
      return x * x
";

        var result = ParseAndEmit(source);

        Assert.Contains("public class MathUtils", result);
        Assert.Contains("public static int Square(int x)", result);
    }

    [Fact]
    public void CodeGen_PartialClass_GeneratesValidCSharp()
    {
        var source = @"
module Test
  class DataService public partial
    func GetData public () → string
      !
      return ""data""
";

        var result = ParseAndEmit(source);

        Assert.Contains("public partial class DataService", result);
    }

    [Fact]
    public void CodeGen_SealedClass_GeneratesValidCSharp()
    {
        var source = @"
module Test
  class FinalClass public sealed
    func DoWork public () → void
      !
";

        var result = ParseAndEmit(source);

        Assert.Contains("public sealed class FinalClass", result);
    }

    [Fact]
    public void CodeGen_GenericInterfaceImplementation_PreservesGenericSyntax()
    {
        var source = @"
module Test
  class MyOption<T> public
    implements IEquatable<MyOption<T>>
";

        var result = ParseAndEmit(source);

        Assert.Contains("IEquatable<MyOption<T>>", result);
    }

    [Fact]
    public void CodeGen_GenericBaseClass_PreservesGenericSyntax()
    {
        var source = @"
module Test
  class StringList public
    extends List<string>
";

        var result = ParseAndEmit(source);

        Assert.Contains("StringList : List<string>", result);
    }

    [Fact]
    public void CodeGen_InterfaceExtendsGenericInterface_PreservesGenericSyntax()
    {
        var source = @"
module Test
  interface IMyCollection<T>
    extends IEnumerable<T>
";

        var result = ParseAndEmit(source);

        Assert.Contains("IMyCollection<T> : IEnumerable<T>", result);
    }

    [Fact]
    public void CodeGen_DeeplyNestedGenericInheritance_PreservesGenericSyntax()
    {
        var source = @"
module Test
  class MyMapper<TKey,TValue> public
    implements IEquatable<Dictionary<TKey,List<TValue>>>
";

        var result = ParseAndEmit(source);

        Assert.Contains("IEquatable<Dictionary<TKey, List<TValue>>>", result);
    }

    [Fact]
    public void CodeGen_EmittedCode_ContainsNullableEnable()
    {
        var source = @"
module Test
  class Foo public
";

        var result = ParseAndEmit(source);

        Assert.Contains("#nullable enable", result);
    }

    [Fact]
    public void CodeGen_NullableEnable_AppearsBeforeUsings()
    {
        var source = @"
module Test
  class Foo public
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
module Test
  class AbstractBase abstract
    func Method public abstract () → void
      !
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
module Test
  class SealedClass sealed
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
module Test
  class Parent public
    field int:Dummy private
  class Child public
    extends Parent
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
module Test
  class Base public
    func VirtualMethod public virtual () → void
      !
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
module Test
  class Derived public
    func Method public override () → void
      !
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
module Test
  class Abstract abstract
    func Method public abstract () → void
      !
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
        // This is a parser-only modifier check, not valid C# modifier combinations.
        var source = @"
module Test
  class Base abstract sealed
    func Method public static virtual () → void
      !
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
module Test
  class NonAbstract public
    func AbstractMethod public abstract () → void
      !
";

        var module = Parse(source, out var diagnostics);

        // Parser should accept it (semantic analysis would catch the error)
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
    }

    [Fact]
    public void Parse_LegacyClassCloser_ReportsError()
    {
        // The native lexer rejects the retired closer, not an ID mismatch.
        var source = @"
module Test
  class MyClass public
§/CL
";

        var module = Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        var error = Assert.Single(diagnostics.Where(d => d.Code == "Calor0006"));
        Assert.Contains("§/CL", error.Message);
        Assert.DoesNotContain(diagnostics, d => d.Code == "Calor0101");
    }

    [Fact]
    public void Parse_InvalidVisibility_ReportsError()
    {
        var source = @"
module Test
  class MyClass invalid
";

        var module = Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.InvalidModifier);
        Assert.DoesNotContain(diagnostics, d => d.Code is "Calor0003" or "Calor0006" or "Calor0007");
    }

    #endregion

    #region Round-Trip Tests

    [Fact]
    public void RoundTrip_AbstractClass_FormatsAndReparses()
    {
        var source = @"
module Test
  class Shape abstract
    func Area public abstract () → double
      !
    func Perimeter public abstract () → double
      !
";

        // Parse original
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        // Format
        var formatter = new Formatting.CalorFormatter();
        var formatted = formatter.Format(module);

        // Verify the canonical modifier survives formatting.
        Assert.Contains("Shape", formatted);
        Assert.Contains("abstract", formatted);

        // Re-parse formatted output
        var module2 = Parse(formatted, out var diagnostics2);
        Assert.False(diagnostics2.HasErrors, $"Formatted output should parse:\n{formatted}\nErrors: {string.Join("\n", diagnostics2.Select(d => d.Message))}");

        // Verify AST matches
        var classNode = module2.Classes.FirstOrDefault(c => c.Name == "Shape");
        Assert.NotNull(classNode);
        Assert.True(classNode.IsAbstract);
    }

    [Fact]
    public void RoundTrip_InheritanceHierarchy_FormatsAndReparses()
    {
        var source = @"
module Test
  class Animal public
    func Speak public virtual () → string
      !
      return ""...""
  class Dog public
    extends Animal
    func Speak public override () → string
      !
      return ""Woof!""
";

        // Parse original
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        // Format
        var formatter = new Formatting.CalorFormatter();
        var formatted = formatter.Format(module);

        // Verify key elements are preserved
        Assert.Contains("extends Animal", formatted);

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
        var testDataPath = ActiveSyntaxInputs.ForCurrentCompiler(CliTestHarness.FindRepoRoot(),
            "tests/TestData/Benchmarks/DesignPatterns/TemplateMethod.calr");
        var source = File.ReadAllText(testDataPath);
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, $"TemplateMethod.calr should parse. Errors:\n{string.Join("\n", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message))}");
        Assert.True(module.Classes.Count >= 4, "Should have DataProcessor, CsvProcessor, JsonProcessor, and TemplateMethodDemo classes");
        // The active copy changes syntax, not the original parser-only fixture's invalid accessibility.
        Assert.Contains("private abstract string ReadData();", new CSharpEmitter().Emit(module));
    }

    [Fact]
    public void Benchmark_Visitor_ParsesSuccessfully()
    {
        var testDataPath = ActiveSyntaxInputs.ForCurrentCompiler(CliTestHarness.FindRepoRoot(),
            "tests/TestData/Benchmarks/DesignPatterns/Visitor.calr");
        var source = File.ReadAllText(testDataPath);
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, $"Visitor.calr should parse. Errors:\n{string.Join("\n", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message))}");
        Assert.True(module.Classes.Count >= 5, "Should have Expression, NumberExpression, AddExpression, MultiplyExpression, and visitors");
        Assert.True(module.Interfaces.Count >= 1, "Should have IExpressionVisitor interface");
        var emitted = new CSharpEmitter().Emit(module);
        Assert.Contains("public double Value;", emitted);
        Assert.Contains("private float Accept(", emitted);
        Assert.Contains("return;", emitted);
    }

    #endregion

    #region Documented Syntax Verification Tests

    /// <summary>
    /// Verifies the documented syntax for base method calls works.
    /// Native base calls use ordinary member-call punctuation.
    /// </summary>
    [Fact]
    public void DocSyntax_BaseMethodCall_UsesLowercase()
    {
        var source = @"
module Test
  class Base public
    func GetValue public virtual () → int
      !
      return 10
  class Derived public
    extends Base
    func GetValue public override () → int
      !
      return base.GetValue() + 5
";

        var result = ParseAndEmit(source);
        Assert.Contains("base.GetValue()", result);
    }

    /// <summary>
    /// Verifies the documented syntax for this method calls works.
    /// Native this calls use ordinary member-call punctuation.
    /// </summary>
    [Fact]
    public void DocSyntax_ThisMethodCall_UsesLowercase()
    {
        var source = @"
module Test
  class Service public
    func Helper private () → string
      return ""done""
    func Process public () → string
      !
      return this.Helper()
";

        var result = ParseAndEmit(source);
        Assert.Contains("this.Helper()", result);
    }

    /// <summary>
    /// Verifies the documented syntax for space-separated modifiers.
    /// Native modifiers use full words separated by spaces.
    /// </summary>
    [Fact]
    public void DocSyntax_CombinedModifiers_UseSpaceSeparation()
    {
        var source = @"
module Test
  class Base public
    func Compute public virtual () → int
      !
      return 0
  class Final public
    extends Base
    func Compute public sealed override () → int
      !
      return 42
";

        var result = ParseAndEmit(source);
        // Emitter outputs "override sealed" (override first)
        Assert.Contains("override sealed int Compute()", result);
    }

    /// <summary>
    /// Verifies native class modifiers follow the declaration name.
    /// </summary>
    [Fact]
    public void DocSyntax_ClassModifiers_Uses3Parts()
    {
        var source = @"
module Test
  class MyAbstract abstract
    func DoWork public abstract () → void
      !
  class MySealed sealed
";

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var absClass = module.Classes.First(c => c.Name == "MyAbstract");
        Assert.True(absClass.IsAbstract);

        var sealedClass = module.Classes.First(c => c.Name == "MySealed");
        Assert.True(sealedClass.IsSealed);
    }

    /// <summary>
    /// Verifies native method modifiers follow the declaration name.
    /// </summary>
    [Fact]
    public void DocSyntax_MethodModifiers_Uses4Parts()
    {
        var source = @"
module Test
  class Test public
    func VirtualMethod public virtual () → void
      !
    func StaticMethod public static () → void
      !
    func PrivateMethod private () → void
";

        var result = ParseAndEmit(source);
        Assert.Contains("public virtual void VirtualMethod()", result);
        Assert.Contains("public static void StaticMethod()", result);
        Assert.Contains("private void PrivateMethod()", result);
    }

    /// <summary>
    /// Verifies native object construction uses ordinary argument parentheses.
    /// </summary>
    [Fact]
    public void DocSyntax_NewExpression_NoEndTag()
    {
        var source = @"
module Test
  class Item public
    ctor (int:val)
  class Factory public
    func Create public static () → Item
      !
      return new Item(42)
";

        var result = ParseAndEmit(source);
        Assert.Contains("new Item(42)", result);
    }

    /// <summary>
    /// Verifies native field assignment uses set target value.
    /// </summary>
    [Fact]
    public void DocSyntax_Assignment_UsesAssign()
    {
        var source = @"
module Test
  class Person public
    field string:_name private
    ctor (string:name)
      set this._name name
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
        var tokens = lexer.TokenizeAllForParser();

        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

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

    #endregion
}
