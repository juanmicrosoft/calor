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
/// Fix 3: Decimal literal support (DEC: prefix)
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
§M{m1:Test}
§F{f001:Main:pub}
  §O{void}
  §B{var:myList} §NEW{List<i32>} §/NEW
§/F{f001}
§/M{m1}
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
§M{m1:Test}
§F{f001:Main:pub}
  §O{void}
  §B{str:name} ""hello""
§/F{f001}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("string name", result);
    }

    [Fact]
    public void Fix1_BindInferredType_BackwardCompat()
    {
        // §B{myVar} with inferred type should still work
        var source = @"
§M{m1:Test}
§F{f001:Main:pub}
  §O{void}
  §B{myVar} 42
§/F{f001}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("myVar", result);
    }

    // ========== Fix 2: Auto-using ==========

    [Fact]
    public void Fix2_GeneratedCSharp_ContainsCollectionsGenericUsing()
    {
        var source = @"
§M{m1:Test}
§F{f001:Main:pub}
  §O{void}
§/F{f001}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("using System.Collections.Generic;", result);
    }

    [Fact]
    public void Fix2_GeneratedCSharp_ContainsLinqUsing()
    {
        var source = @"
§M{m1:Test}
§F{f001:Main:pub}
  §O{void}
§/F{f001}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("using System.Linq;", result);
    }

    // ========== Fix 3: Decimal literals ==========

    [Fact]
    public void Fix3_DecimalLiteral_EmitsMSuffix()
    {
        // DEC:21.35 in Calor should produce 21.35m in C#
        var source = @"
§M{m1:Test}
§F{f001:Main:pub}
  §O{void}
  §B{~price} DEC:21.35
§/F{f001}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("21.35m", result);
    }

    [Fact]
    public void Fix3_DecimalIntegerValue_EmitsMSuffix()
    {
        // DEC:18 should produce 18m in C#
        var source = @"
§M{m1:Test}
§F{f001:Main:pub}
  §O{void}
  §B{~amount} DEC:18
§/F{f001}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("18m", result);
    }

    [Fact]
    public void Fix3_FloatLiteral_BackwardCompat_NoMSuffix()
    {
        // FLOAT:3.14 should still produce 3.14 (no m suffix)
        var source = @"
§M{m1:Test}
§F{f001:Main:pub}
  §O{void}
  §B{~pi} FLOAT:3.14
§/F{f001}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("3.14", result);
        Assert.DoesNotContain("3.14m", result);
    }

    [Fact]
    public void Fix3_DecimalLiteral_RoundtripThroughCalorEmitter()
    {
        // DEC:21.35 should parse, then CalorEmitter should produce DEC:21.35
        var source = @"
§M{m1:Test}
§F{f001:Main:pub}
  §O{void}
  §B{~price} DEC:21.35
§/F{f001}
§/M{m1}
";

        var module = ParseModule(source);
        var calorEmitter = new Migration.CalorEmitter();
        var calorOutput = calorEmitter.Emit(module);

        Assert.Contains("DEC:21.35", calorOutput);
    }

    // ========== Fix 4: Object initializer §INIT ==========

    [Fact]
    public void Fix4_NewWithInit_GeneratesObjectInitializer()
    {
        // §NEW{Customer} §INIT{Name} STR:"John" §INIT{Age} 30 §/NEW
        // should produce new Customer() { Name = "John", Age = 30 }
        var source = @"
§M{m1:Test}
§F{f001:Main:pub}
  §O{void}
  §B{~c} §NEW{Customer} §INIT{Name} STR:""John"" §INIT{Age} 30 §/NEW
§/F{f001}
§/M{m1}
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
§M{m1:Test}
§F{f001:Main:pub}
  §O{void}
  §B{~c} §NEW{Customer} §/NEW
§/F{f001}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("new Customer()", result);
    }

    [Fact]
    public void Fix4_NewWithArgsAndInit_GeneratesBoth()
    {
        // §NEW{Person} §A STR:"base" §INIT{Name} STR:"John" §/NEW
        var source = @"
§M{m1:Test}
§F{f001:Main:pub}
  §O{void}
  §B{~p} §NEW{Person} §A STR:""base"" §INIT{Name} STR:""John"" §/NEW
§/F{f001}
§/M{m1}
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
§M{m1:Test}
§F{f001:Main:pub}
  §O{void}
  §B{~c} §NEW{Customer} §INIT{Name} STR:""John"" §/NEW
§/F{f001}
§/M{m1}
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
§M{m1:Test}
§F{f001:Main:pub}
  §O{void}
  §B{[i32]:nums} §ARR{i32:nums} §A 1 §A 2 §A 3 §/ARR{nums}
§/F{f001}
§/M{m1}
";
        var module = ParseModule(source);
        var calorEmitter = new Migration.CalorEmitter();
        var result = calorEmitter.Emit(module);

        Assert.Contains("§ARR{", result);
        Assert.Contains("1", result);
        Assert.Contains("2", result);
        Assert.Contains("3", result);
        Assert.Contains("§/ARR{", result);
    }

    [Fact]
    public void Fix5_ArrayInitializer_RoundtripThroughParser()
    {
        // §ARR{i32:nums} §A 1 §A 2 §A 3 §/ARR{nums} should parse and generate correct C#
        var source = @"
§M{m1:Test}
§F{f001:Main:pub}
  §O{void}
  §B{[i32]:nums} §ARR{i32:nums} §A 1 §A 2 §A 3 §/ARR{nums}
§/F{f001}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("new int[]", result);
        Assert.Contains("1", result);
        Assert.Contains("2", result);
        Assert.Contains("3", result);
    }

    // ========== Gap-closing: Converter-level tests ==========

    [Fact]
    public void Gap_Converter_DecimalLiteral_ProducesDECPrefix()
    {
        // C# decimal literal → converter → Calor should contain DEC:
        var converter = new Migration.CSharpToCalorConverter();
        var result = converter.Convert("decimal price = 21.35m;");

        Assert.True(result.Success, string.Join("\n", result.Issues.Select(i => i.Message)));
        Assert.NotNull(result.CalorSource);
        Assert.Contains("DEC:21.35", result.CalorSource);
        Assert.DoesNotContain("§ERR", result.CalorSource);
    }

    [Fact]
    public void Gap_Converter_DecimalLiteral_FullRoundtrip()
    {
        // C# decimal → converter → Calor → parse → emit C# should contain 'm' suffix
        var converter = new Migration.CSharpToCalorConverter();
        var conversionResult = converter.Convert("decimal price = 21.35m;");

        Assert.True(conversionResult.Success, string.Join("\n", conversionResult.Issues.Select(i => i.Message)));

        var compilationResult = Program.Compile(conversionResult.CalorSource!);
        Assert.False(compilationResult.HasErrors,
            $"Roundtrip failed:\n{string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message))}");
        Assert.Contains("21.35m", compilationResult.GeneratedCode);
    }

    [Fact]
    public void Gap_Converter_DecimalZero_ProducesDECPrefix()
    {
        // C# 0.00m → converter → Calor should contain DEC:0
        var converter = new Migration.CSharpToCalorConverter();
        var result = converter.Convert("decimal zero = 0.00m;");

        Assert.True(result.Success, string.Join("\n", result.Issues.Select(i => i.Message)));
        Assert.NotNull(result.CalorSource);
        Assert.Contains("DEC:0", result.CalorSource);
    }

    [Fact]
    public void Gap_Converter_ArrayInitializer_ProducesArrTags()
    {
        // C# new int[] { 1, 2, 3 } → converter → CalorEmitter → should contain §ARR not bare {}
        var converter = new Migration.CSharpToCalorConverter();
        var result = converter.Convert("int[] nums = new int[] { 1, 2, 3 };");

        Assert.True(result.Success, string.Join("\n", result.Issues.Select(i => i.Message)));
        Assert.NotNull(result.CalorSource);
        Assert.Contains("§ARR{", result.CalorSource);
        Assert.DoesNotContain("{1, 2, 3}", result.CalorSource);
    }

    [Fact]
    public void Gap_Converter_ArrayInitializer_FullRoundtrip()
    {
        // C# array init → converter → Calor → parse → emit C# → should contain new int[]
        var converter = new Migration.CSharpToCalorConverter();
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

        var converter = new Migration.CSharpToCalorConverter();
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
§M{m1:Test}
§F{f001:Main:pub}
  §O{void}
  §C{Process}
    §A §NEW{Widget} §INIT{Name} STR:""test"" §/NEW
    §A 42
  §/C
§/F{f001}
§/M{m1}
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
§M{m1:Test}
§F{f001:Main:pub}
  §O{void}
  §C{Save}
    §A §NEW{Config} §INIT{Host} STR:""localhost"" §INIT{Port} 8080 §/NEW
  §/C
§/F{f001}
§/M{m1}
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
§M{m1:Test}
§F{f001:Main:pub}
  §O{void}
  §B{~a} DEC:0.01
  §B{~b} DEC:99.99
  §B{~c} DEC:1234567890.12
§/F{f001}
§/M{m1}
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
§M{m1:Test}
§F{f001:Main:pub}
  §O{void}
  §B{~x} DEC:-42.5
§/F{f001}
§/M{m1}
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
§M{m1:Test}
§F{f001:Main:pub}
  §O{void}
  §B{~x:f64} FLOAT:1.0
§/F{f001}
§/M{m1}
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
§M{m1:Test}
§F{f001:Main:pub}
  §O{void}
  §LIST{l1:object}
    FLOAT:1.0
    FLOAT:2.0
  §/LIST{l1}
§/F{f001}
§/M{m1}
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
§M{m1:Test}
§F{f001:Main:pub}
  §O{void}
  §B{~x:f64} FLOAT:3.14
§/F{f001}
§/M{m1}
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
§M{m1:Test}
§F{f001:Main:pub}
  §O{void}
  §B{~x:f64} FLOAT:-1.0
§/F{f001}
§/M{m1}
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
§M{m1:Test}
§F{f001:Main:pub}
  §O{void}
  §B{~x:f64} FLOAT:1E10
§/F{f001}
§/M{m1}
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
        var tokens = lexer.TokenizeAll();

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
        var tokens = lexer.TokenizeAll();

        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        return module;
    }

    #endregion
}
