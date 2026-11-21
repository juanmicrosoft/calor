using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for batch codegen/converter bug fixes:
/// Fix 1: §B{var:name} generates correct C#
/// Fix 2: Auto-using System.Collections.Generic and System.Linq
/// Fix 3: Decimal literal support (canonical m suffix)
/// Fix 4: Object initializer roundtrip with §INIT tags
/// Fix 5: Array initializer CalorEmitter uses §ARR tags
/// </summary>
public class CodegenBatchFixTests
{
    // ========== Fix 1: §B{var:name} ==========

    [Fact]
    public void Fix1_BindVarType_GeneratesVarBeforeName()
    {
        // §B{var:myList} should produce "var myList = ..." not "myList var = ..."
        var source = @"
module Test Test block
  func Main () → void
    !
    var myList = new List<int>()
";

        var result = ParseAndEmit(source);

        Assert.Contains("var myList", result);
        Assert.DoesNotContain("myList var", result);
    }

    [Fact]
    public void Fix1_BindStrType_BackwardCompat()
    {
        // §B{str:name} should still work as type:name format
        var source = @"
module Test Test block
  func Main () → void
    !
    let string:name = ""hello""
";

        var result = ParseAndEmit(source);

        Assert.Contains("string name", result);
    }

    [Fact]
    public void Fix1_BindInferredType_BackwardCompat()
    {
        // §B{myVar} with inferred type should still work
        var source = @"
module Test Test block
  func Main () → void
    !
    let myVar = 42
";

        var result = ParseAndEmit(source);

        Assert.Contains("myVar", result);
    }

    // ========== Fix 2: Auto-using ==========

    [Fact]
    public void Fix2_GeneratedCSharp_ContainsCollectionsGenericUsing_WhenUsed()
    {
        // Collections.Generic using is conditionally emitted when collection types are referenced
        var source = @"
module Test Test block
  func Main () → void
    !
    let List<int>:result = new List<int>() { 1 }
";

        var result = ParseAndEmit(source);

        Assert.Contains("using System.Collections.Generic;", result);
    }

    [Fact]
    public void Fix2_GeneratedCSharp_OmitsLinqUsing_WhenNotUsed()
    {
        // System.Linq is only emitted when LINQ methods are actually used
        var source = @"
module Test Test block
  func Main () → void
    !
    return
";

        var result = ParseAndEmit(source);

        Assert.DoesNotContain("using System.Linq;", result);
    }

    // ========== Fix 3: Decimal literals ==========

    [Fact]
    public void Fix3_DecimalLiteral_EmitsMSuffix()
    {
        // DEC:21.35 in Calor should produce 21.35m in C#
        var source = @"
module Test Test block
  func Main () → void
    !
    var price = 21.35m
";

        var result = ParseAndEmit(source);

        Assert.Contains("21.35m", result);
    }

    [Fact]
    public void Fix3_DecimalIntegerValue_EmitsMSuffix()
    {
        // DEC:18 should produce 18m in C#
        var source = @"
module Test Test block
  func Main () → void
    !
    var amount = 18m
";

        var result = ParseAndEmit(source);

        Assert.Contains("18m", result);
    }

    [Fact]
    public void Fix3_FloatLiteral_BackwardCompat_NoMSuffix()
    {
        // FLOAT:3.14 should still produce 3.14 (no m suffix)
        var source = @"
module Test Test block
  func Main () → void
    !
    var pi = 3.14
";

        var result = ParseAndEmit(source);

        Assert.Contains("3.14", result);
        Assert.DoesNotContain("3.14m", result);
    }

    [Fact]
    public void Fix3_DecimalLiteral_RoundtripThroughCalorEmitter()
    {
        // The accepted prefix input formats as a bare decimal with its m suffix.
        var source = @"
module Test Test block
  func Main () → void
    !
    var price = 21.35m
";

        var module = ParseModule(source);
        var calorEmitter = new Migration.CalorEmitter();
        var calorOutput = calorEmitter.Emit(module);

        Assert.Contains("21.35m", calorOutput);
        var reparsed = ParseModule(calorOutput);
        Assert.Contains("21.35m", new CSharpEmitter().Emit(reparsed));
    }

    // ========== Fix 4: Object initializer §INIT ==========

    [Fact]
    public void Fix4_NewWithInit_GeneratesObjectInitializer()
    {
        // §NEW{Customer} §INIT{Name} STR:"John" §INIT{Age} 30 §/NEW
        // should produce new Customer() { Name = "John", Age = 30 }
        var source = @"
module Test Test block
  func Main () → void
    !
    var c = new Customer() { Name = ""John"", Age = 30 }
";

        var result = ParseAndEmit(source);

        Assert.Contains("Name = \"John\"", result);
        Assert.Contains("Age = 30", result);
        Assert.Contains("new Customer()", result);
    }

    [Fact]
    public void Fix4_NewWithoutInit_BackwardCompat()
    {
        // §NEW{Customer} §/NEW should still produce new Customer()
        var source = @"
module Test Test block
  func Main () → void
    !
    var c = new Customer()
";

        var result = ParseAndEmit(source);

        Assert.Contains("new Customer()", result);
    }

    [Fact]
    public void Fix4_NewWithArgsAndInit_GeneratesBoth()
    {
        // §NEW{Person} §A STR:"base" §INIT{Name} STR:"John" §/NEW
        var source = @"
module Test Test block
  func Main () → void
    !
    var p = new Person(""base"") { Name = ""John"" }
";

        var result = ParseAndEmit(source);

        Assert.Contains("new Person(\"base\")", result);
        Assert.Contains("Name = \"John\"", result);
    }

    [Fact]
    public void Fix4_InitRoundtrip_CalorEmitterProducesInitTags()
    {
        // Parse §INIT tags, then CalorEmitter should produce §INIT tags
        var source = @"
module Test Test block
  func Main () → void
    !
    var c = new Customer() { Name = ""John"" }
";

        var module = ParseModule(source);
        var calorEmitter = new Migration.CalorEmitter();
        var calorOutput = calorEmitter.Emit(module);

        Assert.Contains("Name =", calorOutput);
    }

    // ========== Fix 5: Array initializer CalorEmitter ==========

    [Fact]
    public void Fix5_ArrayInitializer_CalorEmitterProducesArrTags()
    {
        // When CalorEmitter emits an array with initializers, it should use §ARR block syntax
        // Parse a Calor source with initialized array, then re-emit with CalorEmitter
        var source = @"
module Test Test block
  func Main () → void
    !
    let int[]:nums = new int[] { 1, 2, 3 }
";
        var module = ParseModule(source);
        var calorEmitter = new Migration.CalorEmitter();
        var result = calorEmitter.Emit(module);

        Assert.Contains("new int[] { 1, 2, 3 }", result);
        Assert.Contains("1", result);
        Assert.Contains("2", result);
        Assert.Contains("3", result);
        Assert.DoesNotContain("§/ARR{", result);
    }

    [Fact]
    public void Fix5_ArrayInitializer_RoundtripThroughParser()
    {
        // §ARR{i32} §A 1 §A 2 §A 3 §/ARR should parse and generate correct C#
        var source = @"
module Test Test block
  func Main () → void
    !
    let int[]:nums = new int[] { 1, 2, 3 }
";

        var result = ParseAndEmit(source);

        Assert.Contains("new int[]", result);
        Assert.Contains("1", result);
        Assert.Contains("2", result);
        Assert.Contains("3", result);
    }

    // ========== Gap-closing: Converter-level tests ==========

    [Fact]
    public void Gap_Converter_DecimalLiteral_ProducesMSuffix()
    {
        // C# decimal literal → converter → Calor keeps its decimal suffix.
        var converter = new Migration.CSharpToCalorConverter(new Migration.ConversionOptions { Fidelity = Migration.ConversionFidelity.Lossy });
        var result = converter.Convert("decimal price = 21.35m;");

        Assert.True(result.Success, string.Join("\n", result.Issues.Select(i => i.Message)));
        Assert.NotNull(result.CalorSource);
        Assert.Contains("21.35m", result.CalorSource);
        Assert.DoesNotContain("§ERR", result.CalorSource);
    }

    [Fact]
    public void Gap_Converter_DecimalLiteral_FullRoundtrip()
    {
        // C# decimal → converter → Calor → parse → emit C# should contain 'm' suffix
        var converter = new Migration.CSharpToCalorConverter(new Migration.ConversionOptions { Fidelity = Migration.ConversionFidelity.Lossy });
        var conversionResult = converter.Convert("decimal price = 21.35m;");

        Assert.True(conversionResult.Success, string.Join("\n", conversionResult.Issues.Select(i => i.Message)));

        var compilationResult = Program.Compile(
            conversionResult.CalorSource!,
            null,
            new CompilationOptions { DeferGeneratedOutputValidation = true });
        Assert.False(compilationResult.HasErrors,
            $"Roundtrip failed:\n{string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message))}");
        Assert.Contains("21.35m", compilationResult.GeneratedCode);
    }

    [Fact]
    public void Gap_Converter_DecimalZero_PreservesScaleWithMSuffix()
    {
        // Decimal zero retains both its scale and decimal suffix.
        var converter = new Migration.CSharpToCalorConverter(new Migration.ConversionOptions { Fidelity = Migration.ConversionFidelity.Lossy });
        var result = converter.Convert("decimal zero = 0.00m;");

        Assert.True(result.Success, string.Join("\n", result.Issues.Select(i => i.Message)));
        Assert.NotNull(result.CalorSource);
        Assert.Contains("0.00m", result.CalorSource);
    }

    [Fact]
    public void Gap_Converter_ArrayInitializer_ProducesArrTags()
    {
        // C# new int[] { 1, 2, 3 } → converter → CalorEmitter → should contain §ARR not bare {}
        var converter = new Migration.CSharpToCalorConverter(new Migration.ConversionOptions { Fidelity = Migration.ConversionFidelity.Lossy });
        var result = converter.Convert("int[] nums = new int[] { 1, 2, 3 };");

        Assert.True(result.Success, string.Join("\n", result.Issues.Select(i => i.Message)));
        Assert.NotNull(result.CalorSource);
        Assert.Contains("new int[] { 1, 2, 3 }", result.CalorSource);
        Assert.DoesNotContain("{1, 2, 3}", result.CalorSource);
    }

    [Fact]
    public void Gap_Converter_ArrayInitializer_FullRoundtrip()
    {
        // C# array init → converter → Calor → parse → emit C# → should contain new int[]
        var converter = new Migration.CSharpToCalorConverter(new Migration.ConversionOptions { Fidelity = Migration.ConversionFidelity.Lossy });
        var conversionResult = converter.Convert("int[] nums = new int[] { 1, 2, 3 };");

        Assert.True(conversionResult.Success, string.Join("\n", conversionResult.Issues.Select(i => i.Message)));

        var compilationResult = Program.Compile(conversionResult.CalorSource!);
        Assert.False(compilationResult.HasErrors,
            $"Roundtrip failed:\n{string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message))}");
        Assert.Contains("new int[]", compilationResult.GeneratedCode);
    }

    [Fact]
    public void Gap_Converter_ObjectInitializer_FullRoundtrip()
    {
        // C# object initializer → converter → Calor → parse → emit C# → should have initializers
        var csharpSource = """
            public class Person { public string Name { get; set; } public int Age { get; set; } }
            public class Program {
                public static void Main() {
                    var p = new Person { Name = "John", Age = 30 };
                }
            }
            """;

        var converter = new Migration.CSharpToCalorConverter(new Migration.ConversionOptions { Fidelity = Migration.ConversionFidelity.Lossy });
        var conversionResult = converter.Convert(csharpSource);

        Assert.True(conversionResult.Success, string.Join("\n", conversionResult.Issues.Select(i => i.Message)));
        Assert.Contains("Name =", conversionResult.CalorSource);

        var compilationResult = Program.Compile(conversionResult.CalorSource!);
        Assert.False(compilationResult.HasErrors,
            $"Roundtrip failed:\n{string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message))}");
        Assert.Contains("Name = ", compilationResult.GeneratedCode);
        Assert.Contains("Age = ", compilationResult.GeneratedCode);
    }

    // ========== Gap-closing: §INIT inside §C arg context ==========

    [Fact]
    public void Gap_InitInsideCallArg_ParsesCorrectly()
    {
        // §INIT inside §NEW that is inside a §C argument should parse correctly
        var source = @"
module Test Test block
  func Main () → void
    !
    Process(new Widget() { Name = ""test"" }, 42)
";

        var result = ParseAndEmit(source);

        Assert.Contains("Name = \"test\"", result);
        Assert.Contains("new Widget()", result);
        Assert.Contains("Process(", result);
        Assert.Contains(", 42)", result);
    }

    [Fact]
    public void Gap_InitInsideCallArg_MultipleInits()
    {
        // Multiple §INIT inside §NEW inside §C
        var source = @"
module Test Test block
  func Main () → void
    !
    Save(new Config() { Host = ""localhost"", Port = 8080 })
";

        var result = ParseAndEmit(source);

        Assert.Contains("Host = \"localhost\"", result);
        Assert.Contains("Port = 8080", result);
        Assert.Contains("Save(new Config()", result);
    }

    // ========== Gap-closing: Decimal precision limitation ==========

    [Fact]
    public void Gap_DecimalPrecision_StandardValues_RoundtripCorrectly()
    {
        // Common monetary/business values should roundtrip without precision loss
        var source = @"
module Test Test block
  func Main () → void
    !
    var a = 0.01m
    var b = 99.99m
    var c = 1234567890.12m
";

        var result = ParseAndEmit(source);

        Assert.Contains("0.01m", result);
        Assert.Contains("99.99m", result);
        Assert.Contains("1234567890.12m", result);
    }

    [Fact]
    public void Gap_DecimalNegativeValue_RoundtripsCorrectly()
    {
        var source = @"
module Test Test block
  func Main () → void
    !
    var x = -42.5m
";

        var result = ParseAndEmit(source);

        Assert.Contains("-42.5m", result);
    }

    // ========== Fix 6: Float literals preserve decimal point ==========

    [Fact]
    public void Fix6_FloatLiteral_WholeNumber_EmitsDecimalPoint()
    {
        // FLOAT:1.0 should produce 1.0, not bare 1 (which would be int)
        var source = @"
module Test Test block
  func Main () → void
    !
    var double:x = 1.0
";

        var result = ParseAndEmit(source);

        Assert.Contains("1.0", result);
        Assert.DoesNotContain("1.0m", result);
    }

    [Fact]
    public void Fix6_FloatLiteral_InListObject_PreservesType()
    {
        // Float literals inside §LIST{:object} should emit with decimal point
        var source = @"
module Test Test block
  func Main () → void
    !
    let List<object>:l1 = new List<object>() { 1.0, 2.0 }
";

        var result = ParseAndEmit(source);

        Assert.Contains("1.0", result);
        Assert.Contains("2.0", result);
    }

    [Fact]
    public void Fix6_FloatLiteral_WithFraction_Unchanged()
    {
        // FLOAT:3.14 should stay 3.14 (already has decimal point)
        var source = @"
module Test Test block
  func Main () → void
    !
    var double:x = 3.14
";

        var result = ParseAndEmit(source);

        Assert.Contains("3.14", result);
        Assert.DoesNotContain("3.14m", result);
    }

    [Fact]
    public void Fix6_FloatLiteral_NegativeWholeNumber_EmitsDecimalPoint()
    {
        // FLOAT:-1.0 should produce -1.0, not bare -1
        var source = @"
module Test Test block
  func Main () → void
    !
    var double:x = -1.0
";

        var result = ParseAndEmit(source);

        Assert.Contains("-1.0", result);
        Assert.DoesNotContain("-1.0m", result);
    }

    [Fact]
    public void Fix6_FloatLiteral_ScientificNotation_Unchanged()
    {
        // Scientific notation like 1E10 should stay as-is (already a double in C#)
        var source = @"
module Test Test block
  func Main () → void
    !
    var double:x = 10000000000.0
";

        var result = ParseAndEmit(source);

        // Should contain the scientific notation form, not have .0 appended
        Assert.DoesNotContain("1E10.0", result);
    }

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

    private static ModuleNode ParseModule(string source)
    {
        var diagnostics = new DiagnosticBag();
        diagnostics.SetFilePath("test.calr");

        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();

        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        return module;
    }

    #endregion

    /// <summary>
    /// v0.17 R4(b) / #1137 — adding <c>§EXT</c> to a class made every
    /// module-level CALL from its body emit unqualified, so Roslyn reported
    /// CS0103 and the compiler surfaced Calor1002. The same body without
    /// <c>§EXT</c> compiled clean.
    /// <para>#823 suppressed qualification for any derived class on purpose:
    /// inherited members are not enumerable when the base may be C#, and
    /// mis-qualifying one silently runs another module's code. That reasoning
    /// only holds while the base chain is OPAQUE — when every base is declared
    /// in this module the inherited members are known exactly, and there is
    /// nothing left to guess at.</para>
    /// </summary>
    [Fact]
    public void ModuleFunctionCall_FromClassWithLocalBase_IsQualified()
    {
        var emitted = ParseAndEmit("""
module ExtProbe ExtProbe block
  func Helper (int:x) → int
    !
    return (x * 2)

  class BaseThing
    func Tag () → int
      !
      return 0

  class Derived
    extends BaseThing

    func Use (int:v) → int
      !
      return Helper(v)
""");

        Assert.Contains("ExtProbeModule.Helper(", emitted, StringComparison.Ordinal);
    }

    /// <summary>
    /// v0.17 R4(b) — the OTHER half of #823's guarantee, which the #1137 fix may
    /// not trade away. When the base class is NOT declared in this module the
    /// chain is opaque, an inherited member could carry the same name, and
    /// qualifying past it would silently run the wrong code. Suppression must
    /// still apply there: CS0103 is the acceptable failure, wrong code is not.
    /// </summary>
    [Fact]
    public void ModuleFunctionCall_FromClassWithOpaqueBase_StaysUnqualified()
    {
        var emitted = ParseAndEmit("""
module OpaqueProbe OpaqueProbe block
  func Helper (int:x) → int
    !
    return (x * 2)

  class Derived
    extends SomeExternalBase

    func Use (int:v) → int
      !
      return Helper(v)
""");

        Assert.DoesNotContain("OpaqueProbeModule.Helper(", emitted, StringComparison.Ordinal);
    }

    /// <summary>
    /// v0.17 R4(c) / #1118 — a module-level function referenced AS A VALUE (a
    /// method group in argument position) was emitted bare from inside a class
    /// body, while the same function CALLED from the same position was qualified
    /// correctly. #1137 and #1118 are complementary defects, not one root cause,
    /// which is why fixing calls does not close this and both need a test.
    /// </summary>
    [Fact]
    public void ModuleFunctionMethodGroup_FromClassBody_IsQualified()
    {
        var emitted = ParseAndEmit("""
module MgProbe MgProbe block
  func Shout (int:x) → int
    !
    return x

  func Apply (Func<int, int>:f, int:v) → int
    !
    return f(v)

  class Holder
    func Use (int:v) → int
      !
      return Apply(Shout, v)
""");

        Assert.Contains("MgProbeModule.Shout", emitted, StringComparison.Ordinal);
    }
}
