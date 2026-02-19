using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for code generation bug fixes identified during cyrillic-to-latin sample conversion.
/// </summary>
public class CodeGenBugFixTests
{
    [Fact]
    public void Bug1_IndexWithAttributeShorthand_GeneratesCorrectArrayAccess()
    {
        // §IDX{args} 1 should generate args[1], NOT new object[] { args }[1]
        var source = @"
§M{m1:Test}
§F{f001:Main:pub}
  §I{[str]:args}
  §O{void}
  §B{~result:str} §IDX{args} 1
§/F{f001}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("args[1]", result);
        Assert.DoesNotContain("new object[]", result);
    }

    [Fact]
    public void Bug2_NewWithAngleBracketGenerics_GeneratesCorrectType()
    {
        // §NEW{Dictionary<char,str>} should generate new Dictionary<char, string>()
        var source = @"
§M{m1:Test}
§F{f001:Main:pub}
  §O{void}
  §B{~dict} §NEW{Dictionary<char,str>}
§/F{f001}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("new Dictionary<char, string>()", result);
        Assert.DoesNotContain("Dictionarycharstr", result);
    }

    [Fact]
    public void Bug2_NewWithColonSeparatedGenerics_StillWorks()
    {
        // Backward compat: §NEW{Dictionary:char:str} should still work
        var source = @"
§M{m1:Test}
§F{f001:Main:pub}
  §O{void}
  §B{~dict} §NEW{Dictionary:char:str}
§/F{f001}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("new Dictionary<char, string>()", result);
    }

    [Fact]
    public void Bug3_BindWithPascalCaseType_CorrectTypeAndName()
    {
        // §B{ConsoleKeyInfo:keyPressed} should generate ConsoleKeyInfo keyPressed
        var source = @"
§M{m1:Test}
§F{f001:Main:pub}
  §O{void}
  §B{ConsoleKeyInfo:keyPressed} §C{Console.ReadKey} §/C
§/F{f001}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("ConsoleKeyInfo keyPressed", result);
        Assert.DoesNotContain("keyPressed ConsoleKeyInfo", result);
    }

    [Fact]
    public void Bug3_BindWithPrimitiveType_StillWorks()
    {
        // Backward compat: §B{~name:str} should still work as name:type format
        var source = @"
§M{m1:Test}
§F{f001:Main:pub}
  §O{void}
  §B{~name:str} ""hello""
§/F{f001}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("string name", result);
    }

    [Fact]
    public void Bug4_ArrayCreationWithTypeFirst_GeneratesCorrectType()
    {
        // §ARR{char:buf1:100} should generate new char[100], NOT new buf1[100]
        var source = @"
§M{m1:Test}
§F{f001:Main:pub}
  §O{void}
  §B{[char]:buf1} §ARR{char:buf1:100}
§/F{f001}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("new char[100]", result);
        Assert.DoesNotContain("new buf1[100]", result);
    }

    [Fact]
    public void Bug4_ArrayCreationWithIdFirst_StillWorks()
    {
        // Backward compat: §ARR{arr1:i32:10} should still work
        var source = @"
§M{m1:Test}
§F{f001:Main:pub}
  §O{void}
  §B{[i32]:arr1} §ARR{arr1:i32:10}
§/F{f001}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("new int[10]", result);
    }

    [Fact]
    public void Bug5_PropertyWithOverrideModifier_GeneratesOverrideKeyword()
    {
        // §PROP{p001:MaxCharCount:i32:pub:over} should generate public override int MaxCharCount
        var source = @"
§M{m1:Test}
§CL{c1:MyClass:pub}
  §EXT{BaseClass}
  §PROP{p001:MaxCharCount:i32:pub:over}
    §GET
    §/GET
    §SET
    §/SET
  §/PROP{p001}
§/CL{c1}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("public override int MaxCharCount", result);
    }

    [Fact]
    public void Bug5_PropertyWithVirtualModifier_GeneratesVirtualKeyword()
    {
        var source = @"
§M{m1:Test}
§CL{c1:MyClass:pub}
  §PROP{p001:Name:str:pub:virt}
    §GET
    §/GET
    §SET
    §/SET
  §/PROP{p001}
§/CL{c1}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("public virtual string Name", result);
    }

    [Fact]
    public void Bug5_PropertyWithNoModifier_NoExtraKeywords()
    {
        // Backward compat: property with no 5th position should work as before
        var source = @"
§M{m1:Test}
§CL{c1:MyClass:pub}
  §PROP{p001:Count:i32:pub}
    §GET
    §/GET
    §SET
    §/SET
  §/PROP{p001}
§/CL{c1}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("public int Count", result);
        Assert.DoesNotContain("override", result);
        Assert.DoesNotContain("virtual", result);
    }

    [Fact]
    public void Bug6_MultipleNewInsideCall_NoCrossNesting()
    {
        // §C{fn} §A §NEW{Type1} §A §NEW{Type2} §/C should generate fn(new Type1(), new Type2())
        var source = @"
§M{m1:Test}
§F{f001:Main:pub}
  §O{void}
  §C{DoWork}
    §A §NEW{StringBuilder}
    §A §NEW{List}
  §/C
§/F{f001}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("new StringBuilder()", result);
        Assert.Contains("new List()", result);
        // Should be two separate arguments to DoWork
        Assert.Contains("DoWork(new StringBuilder(), new List())", result);
    }

    [Fact]
    public void Bug7_CharLiteral_GeneratesCharSyntax()
    {
        // (char-lit "Y") should generate 'Y'
        var source = @"
§M{m1:Test}
§F{f001:Main:pub}
  §O{void}
  §B{~ch:char} (char-lit ""Y"")
§/F{f001}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("'Y'", result);
    }

    // --- Edge case tests ---

    [Fact]
    public void Bug6_NewWithArgsInsideCallArg_ArgsGoToCallNotNew()
    {
        // §NEW{Type} inside a §C arg context should NOT consume the next §A —
        // it belongs to the enclosing §C. To pass args to §NEW inside §C,
        // use the §/NEW closure pattern with a separate §B.
        var source = @"
§M{m1:Test}
§F{f001:Main:pub}
  §O{void}
  §C{Process}
    §A §NEW{Widget}
    §A ""extra""
  §/C
§/F{f001}
§/M{m1}
";

        var result = ParseAndEmit(source);

        // "extra" should be an arg to Process, not to Widget
        Assert.Contains("Process(new Widget(), \"extra\")", result);
    }

    [Fact]
    public void Bug6_NewWithExplicitEndTag_StillConsumesArgsOutsideCallContext()
    {
        // Outside of §C arg context, §NEW should still consume §A tokens normally
        var source = @"
§M{m1:Test}
§F{f001:Main:pub}
  §O{void}
  §B{~obj} §NEW{Widget} §A ""hello"" §A 42 §/NEW
§/F{f001}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("new Widget(\"hello\", 42)", result);
    }

    [Fact]
    public void Bug7_CharLiteral_SingleQuote_ProperlyEscaped()
    {
        // (char-lit "'") should produce '\'' not '''
        var source = @"
§M{m1:Test}
§F{f001:Main:pub}
  §O{void}
  §B{~ch:char} (char-lit ""'"")
§/F{f001}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains(@"'\''", result);
        Assert.DoesNotContain("'''", result);
    }

    [Fact]
    public void Bug7_CharLiteral_Backslash_ProperlyEscaped()
    {
        // (char-lit "\\") should produce '\\'
        var source = """
            §M{m1:Test}
            §F{f001:Main:pub}
              §O{void}
              §B{~ch:char} (char-lit "\\")
            §/F{f001}
            §/M{m1}
            """;

        var result = ParseAndEmit(source);

        Assert.Contains(@"'\\'", result);
    }

    [Fact]
    public void Bug3_BindWithBothPascalCase_FallsToNameTypeFormat()
    {
        // When both positions are PascalCase (e.g., §B{HttpClient:MyClient}),
        // both pass IsLikelyType, so the condition !IsLikelyType(pos1) is false,
        // and we fall through to {name:type} format.
        // This means: HttpClient is the name, MyClient is the type.
        var source = @"
§M{m1:Test}
§F{f001:Main:pub}
  §O{void}
  §B{HttpClient:MyClient} §NEW{MyClient}
§/F{f001}
§/M{m1}
";

        var result = ParseAndEmit(source);

        // Falls through to {name:type} — HttpClient is the variable name, MyClient is the type
        Assert.Contains("MyClient HttpClient", result);
    }

    [Fact]
    public void Bug3_BindWithSameTypeName_FallsToNameTypeFormat()
    {
        // §B{StringBuilder:StringBuilder} — both are PascalCase and identical
        // Falls through to {name:type} format, which works correctly
        var source = @"
§M{m1:Test}
§F{f001:Main:pub}
  §O{void}
  §B{StringBuilder:StringBuilder} §NEW{StringBuilder}
§/F{f001}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("StringBuilder StringBuilder", result);
    }

    [Fact]
    public void Bug4_ArrayCreationBothPascalCase_FallsToIdTypeFormat()
    {
        // §ARR{MyArray:Buffer:10} — both PascalCase
        // Both pass IsLikelyType, so !IsLikelyType(pos1) is false,
        // falls to {id:type:size} format: MyArray is id, Buffer is type
        var source = @"
§M{m1:Test}
§F{f001:Main:pub}
  §O{void}
  §B{Buffer:MyArray} §ARR{MyArray:Buffer:10}
§/F{f001}
§/M{m1}
";

        var result = ParseAndEmit(source);

        // Falls through to {id:type} — MyArray is id, Buffer is type
        Assert.Contains("new Buffer[10]", result);
    }

    // --- Integration test: realistic program exercising all 7 bug patterns ---

    [Fact]
    public void Integration_CyrillicToLatinStyle_AllBugPatternsInOneProgram()
    {
        // Simulates a transliteration program that exercises all 7 bug patterns:
        // Bug 1: §IDX{args} shorthand
        // Bug 2: §NEW{Dictionary<char,str>} angle-bracket generics
        // Bug 3: §B{ConsoleKeyInfo:keyPressed} PascalCase type binding
        // Bug 4: §ARR{char:buf:256} type-first array creation
        // Bug 5: §PROP{...:over} override modifier on property
        // Bug 6: §C{fn} §A §NEW{X} §A §NEW{Y} §/C multiple NEWs in call
        // Bug 7: (char-lit "Y") char literal
        var source = @"
§M{m1:Transliterator}

§U{System}
§U{System.Collections.Generic}
§U{System.Text}

§CL{c0:TransliteratorBase:pub:abs}
  §PROP{p001:Name:str:pub:abs}
    §GET
    §/GET
  §/PROP{p001}
§/CL{c0}

§CL{c1:CyrillicToLatin:pub}
  §EXT{TransliteratorBase}

  §FLD{str:_mappings:pri}

  §PROP{p001:Name:str:pub:over}
    §GET
    §/GET
  §/PROP{p001}

  §CTOR{ctor1:pub}
    §I{str:name}
    §ASSIGN §THIS._mappings name
  §/CTOR{ctor1}

  §MT{mt1:Transliterate:pub}
    §I{str:input}
    §O{str}
    §E{cw,cr}

    §B{~dict} §NEW{Dictionary<char,str>}
    §B{[char]:buf} §ARR{char:buf:256}
    §B{~sb} §NEW{StringBuilder}
    §B{ConsoleKeyInfo:keyPressed} §C{Console.ReadKey} §/C

    §L{l1:i:0:(len input):1}
      §B{char:ch} §IDX{input} i
      §IF{if1} (== ch (char-lit ""Y""))
        §C{sb.Append}
          §A ""Y""
        §/C
      §EL
        §C{sb.Append}
          §A ch
        §/C
      §/I{if1}
    §/L{l1}

    §C{Console.WriteLine}
      §A §NEW{StringBuilder}
      §A §NEW{StringBuilder}
    §/C

    §R §C{sb.ToString} §/C
  §/MT{mt1}

§/CL{c1}

§/M{m1}
";

        var result = ParseAndEmit(source);

        // Bug 1: §IDX{input} i → input[i]
        Assert.Contains("input[i]", result);
        Assert.DoesNotContain("new object[]", result);

        // Bug 2: §NEW{Dictionary<char,str>} → new Dictionary<char, string>()
        Assert.Contains("new Dictionary<char, string>()", result);
        Assert.DoesNotContain("Dictionarycharstr", result);

        // Bug 3: §B{ConsoleKeyInfo:keyPressed} → ConsoleKeyInfo keyPressed
        Assert.Contains("ConsoleKeyInfo keyPressed", result);

        // Bug 4: §ARR{char:buf:256} → new char[256]
        Assert.Contains("new char[256]", result);
        Assert.DoesNotContain("new buf[256]", result);

        // Bug 5: §PROP{...:over} → public override string Name
        Assert.Contains("public override string Name", result);
        // Also check the abstract property on the base class
        Assert.Contains("public abstract string Name", result);

        // Bug 6: §C{...} §A §NEW{StringBuilder} §A §NEW{StringBuilder} §/C
        // Both NEWs should be separate args, not nested
        Assert.Contains("new StringBuilder(), new StringBuilder()", result);

        // Bug 7: (char-lit "Y") → 'Y'
        Assert.Contains("'Y'", result);

        // Overall: should parse without errors and produce valid structure
        Assert.Contains("class CyrillicToLatin : TransliteratorBase", result);
        Assert.Contains("namespace Transliterator", result);
    }

    [Fact]
    public void Integration_CalrFile_CompilesAndVerifiesAllBugFixes()
    {
        // Compile the actual .calr file from the E2E scenarios directory
        var calrPath = FindCalrFile("09_codegen_bugfixes", "input.calr");
        var source = File.ReadAllText(calrPath);

        var compilationResult = Program.Compile(source, calrPath);

        Assert.False(compilationResult.HasErrors,
            "Compilation failed:\n" + string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message)));

        var code = compilationResult.GeneratedCode;

        // Verify all 7 bug patterns in the compiled .calr file
        Assert.Contains("input[i]", code);                                     // Bug 1
        Assert.Contains("new Dictionary<char, string>()", code);               // Bug 2
        Assert.Contains("ConsoleKeyInfo keyPressed", code);                    // Bug 3
        Assert.Contains("new char[256]", code);                                // Bug 4
        Assert.Contains("public override string Name", code);                  // Bug 5
        Assert.Contains("new StringBuilder(), new StringBuilder()", code);     // Bug 6
        Assert.Contains("'Y'", code);                                          // Bug 7
    }

    private static string FindCalrFile(string scenarioName, string fileName)
    {
        // Walk up from assembly location to find tests/E2E/scenarios
        var assemblyDir = Path.GetDirectoryName(typeof(CodeGenBugFixTests).Assembly.Location)!;
        var current = new DirectoryInfo(assemblyDir);
        while (current != null)
        {
            var filePath = Path.Combine(current.FullName, "tests", "E2E", "scenarios", scenarioName, fileName);
            if (File.Exists(filePath))
                return filePath;
            current = current.Parent;
        }
        throw new FileNotFoundException($"Could not find tests/E2E/scenarios/{scenarioName}/{fileName}");
    }

    // --- Parametric/fuzz tests for attribute heuristics ---

    [Theory]
    [InlineData("i32", true)]
    [InlineData("i64", true)]
    [InlineData("u8", true)]
    [InlineData("f32", true)]
    [InlineData("str", true)]
    [InlineData("string", true)]
    [InlineData("bool", true)]
    [InlineData("char", true)]
    [InlineData("void", true)]
    [InlineData("int", true)]
    [InlineData("float", true)]
    // Nullable/Result/Generic types
    [InlineData("?i32", true)]
    [InlineData("str!Exception", true)]
    [InlineData("List<i32>", true)]
    [InlineData("Dictionary<str,i32>", true)]
    // Array types
    [InlineData("[i32]", true)]
    [InlineData("[str]", true)]
    // PascalCase .NET types
    [InlineData("StringBuilder", true)]
    [InlineData("ConsoleKeyInfo", true)]
    [InlineData("HttpClient", true)]
    [InlineData("Encoding", true)]
    [InlineData("IO", false)]        // All caps, 2 chars — no lowercase
    [InlineData("A", false)]         // Single char
    // camelCase identifiers — should NOT be types
    [InlineData("myVar", false)]
    [InlineData("keyPressed", false)]
    [InlineData("buf1", false)]
    [InlineData("count", false)]
    [InlineData("result", false)]
    [InlineData("sb", false)]
    // Edge cases
    [InlineData("", false)]
    [InlineData("x", false)]
    [InlineData("X", false)]         // Single uppercase — too short for PascalCase heuristic
    [InlineData("ABC", false)]       // All caps, no lowercase
    [InlineData("URL", false)]       // All caps abbreviation
    [InlineData("Id", true)]         // Short but PascalCase with lowercase
    public void IsLikelyType_ClassifiesCorrectly(string value, bool expected)
    {
        var actual = AttributeHelper.IsLikelyType(value);
        Assert.Equal(expected, actual);
    }

    [Theory]
    // {type:name} format — type-first when type is recognized and name isn't
    [InlineData("ConsoleKeyInfo", "keyPressed", "keyPressed", "ConsoleKeyInfo")]  // Bug 3 pattern
    [InlineData("i32", "count", "count", "INT")]                                  // Primitive type first
    [InlineData("str", "name", "name", "STRING")]                                 // str type first
    [InlineData("StringBuilder", "sb", "sb", "StringBuilder")]                    // PascalCase type
    [InlineData("?i32", "result", "result", "OPTION[inner=INT]")]                 // Nullable type
    // {name:type} format — fallback when pos0 isn't a type
    [InlineData("myVar", "i32", "myVar", "INT")]                                  // Original format
    [InlineData("count", "u64", "count", "INT[bits=64][signed=false]")]           // name:type
    // Both PascalCase — ambiguous, falls to {name:type}
    [InlineData("HttpClient", "MyService", "HttpClient", "MyService")]            // Both PascalCase
    [InlineData("StringBuilder", "StringBuilder", "StringBuilder", "StringBuilder")] // Same name
    public void InterpretBindAttributes_DetectsCorrectFormat(
        string pos0, string pos1, string expectedName, string expectedType)
    {
        var attrs = new AttributeCollection();
        attrs.Add("_pos0", pos0);
        attrs.Add("_pos1", pos1);
        attrs.Add("_posCount", "2");

        var (name, mutable, typeName) = AttributeHelper.InterpretBindAttributes(attrs);

        Assert.Equal(expectedName, name);
        Assert.NotNull(typeName);
        Assert.Equal(expectedType, typeName);
        Assert.False(mutable);
    }

    [Theory]
    // {type:id:size} format — type-first when type is recognized and id isn't
    [InlineData("char", "buf1", "char")]     // Bug 4 pattern: char is type, buf1 is id
    [InlineData("i32", "arr1", "int")]       // Primitive type first
    [InlineData("str", "names", "string")]   // str type first → maps to string
    // {id:type:size} format — fallback when pos0 isn't a type
    [InlineData("arr1", "i32", "int")]       // Original format: arr1 is id, i32 is type
    [InlineData("buf", "char", "char")]      // name first, type second
    public void ArrayCreation_DetectsCorrectTypeIdOrder(
        string pos0, string pos1, string expectedCSharpType)
    {
        // Parse a §ARR with these attributes and check the resulting array type
        var source = $@"
§M{{m1:Test}}
§F{{f001:Main:pub}}
  §O{{void}}
  §B{{~x}} §ARR{{{pos0}:{pos1}:10}}
§/F{{f001}}
§/M{{m1}}
";

        var result = ParseAndEmit(source);

        // The generated code should contain the correct C# type
        Assert.Contains($"new {expectedCSharpType}[10]", result);
    }

    [Theory]
    // Various char literals
    [InlineData("Y", "'Y'")]
    [InlineData("A", "'A'")]
    [InlineData("0", "'0'")]
    [InlineData(" ", "' '")]
    public void CharLiteral_VariousChars_ProducesCorrectOutput(string ch, string expected)
    {
        var source = $@"
§M{{m1:Test}}
§F{{f001:Main:pub}}
  §O{{void}}
  §B{{~c:char}} (char-lit ""{ch}"")
§/F{{f001}}
§/M{{m1}}
";

        var result = ParseAndEmit(source);

        Assert.Contains(expected, result);
    }

    [Theory]
    // Property modifiers
    [InlineData("", "public int")]            // No modifier
    [InlineData("over", "public override int")]
    [InlineData("virt", "public virtual int")]
    [InlineData("abs", "public abstract int")]
    [InlineData("stat", "public static int")]
    public void PropertyModifiers_VariousValues_EmitCorrectKeywords(string modifier, string expectedPrefix)
    {
        var classModifier = modifier == "abs" ? "abs" : "pub";
        var source = $@"
§M{{m1:Test}}
§CL{{c1:MyClass:{classModifier}}}
  {(modifier == "over" ? "§EXT{BaseClass}" : "")}
  §PROP{{p001:Count:i32:pub:{modifier}}}
    §GET
    §/GET
    §SET
    §/SET
  §/PROP{{p001}}
§/CL{{c1}}
§/M{{m1}}
";

        var result = ParseAndEmit(source);

        Assert.Contains($"{expectedPrefix} Count", result);
    }

    // --- Validation tests for char-lit ---

    [Fact]
    public void Bug7_CharLiteral_MultiChar_ReportsError()
    {
        // (char-lit "AB") should report an error — char-lit requires a single character
        var source = @"
§M{m1:Test}
§F{f001:Main:pub}
  §O{void}
  §B{~ch:char} (char-lit ""AB"")
§/F{f001}
§/M{m1}
";

        var diagnostics = ParseWithDiagnostics(source);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, d => d.Message.Contains("char-lit requires a single character"));
    }

    [Fact]
    public void Bug7_CharLiteral_EmptyString_ReportsError()
    {
        // (char-lit "") should report an error — empty string has 0 characters
        var source = @"
§M{m1:Test}
§F{f001:Main:pub}
  §O{void}
  §B{~ch:char} (char-lit """")
§/F{f001}
§/M{m1}
";

        var diagnostics = ParseWithDiagnostics(source);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, d => d.Message.Contains("char-lit requires a single character"));
    }

    [Fact]
    public void Bug7_CharLiteral_EscapeSequence_IsValid()
    {
        // (char-lit "\n") should be valid — escape sequences are single characters
        var source = @"
§M{m1:Test}
§F{f001:Main:pub}
  §O{void}
  §B{~ch:char} (char-lit ""\n"")
§/F{f001}
§/M{m1}
";

        // Should parse without errors
        var result = ParseAndEmit(source);
        Assert.Contains("'\\n'", result);
    }

    #region Bind :const Backward Compatibility

    [Fact]
    public void InterpretBindAttributes_NameConst_ParsesAsImmutableNoType()
    {
        // Legacy format: {name:const}
        var attrs = new AttributeCollection();
        attrs.Add("_pos0", "count");
        attrs.Add("_pos1", "const");
        attrs.Add("_posCount", "2");

        var (name, mutable, typeName) = AttributeHelper.InterpretBindAttributes(attrs);

        Assert.Equal("count", name);
        Assert.False(mutable);
        Assert.Null(typeName);
    }

    [Fact]
    public void InterpretBindAttributes_TypeNameConst_ParsesAsImmutableWithType()
    {
        // Legacy format: {type:name:const}
        var attrs = new AttributeCollection();
        attrs.Add("_pos0", "i32");
        attrs.Add("_pos1", "count");
        attrs.Add("_pos2", "const");
        attrs.Add("_posCount", "3");

        var (name, mutable, typeName) = AttributeHelper.InterpretBindAttributes(attrs);

        Assert.Equal("count", name);
        Assert.False(mutable);
        Assert.NotNull(typeName);
        Assert.Equal("INT", typeName);
    }

    #endregion

    #region CalorEmitter Bind Format

    private static TextSpan DummySpan => new(0, 1, 1, 1);

    private static ModuleNode MakeModuleWithBinds(params BindStatementNode[] binds)
    {
        var func = new FunctionNode(
            DummySpan, "f001", "Test", Visibility.Public,
            Array.Empty<ParameterNode>(),
            new OutputNode(DummySpan, "VOID"),
            null,
            binds,
            new AttributeCollection());
        return new ModuleNode(
            DummySpan, "m001", "Test",
            Array.Empty<UsingDirectiveNode>(),
            new[] { func },
            new AttributeCollection());
    }

    [Fact]
    public void CalorEmitter_UntypedImmutableBind_EmitsNameOnly()
    {
        var bind = new BindStatementNode(DummySpan, "count", null, false,
            new IntLiteralNode(DummySpan, 42), new AttributeCollection());
        var module = MakeModuleWithBinds(bind);

        var emitter = new CalorEmitter();
        var output = emitter.Emit(module);

        Assert.Contains("§B{count} 42", output);
        Assert.DoesNotContain(":const", output);
    }

    [Fact]
    public void CalorEmitter_UntypedMutableBind_EmitsTildePrefix()
    {
        var bind = new BindStatementNode(DummySpan, "counter", null, true,
            new IntLiteralNode(DummySpan, 0), new AttributeCollection());
        var module = MakeModuleWithBinds(bind);

        var emitter = new CalorEmitter();
        var output = emitter.Emit(module);

        Assert.Contains("§B{~counter} 0", output);
    }

    [Fact]
    public void CalorEmitter_TypedImmutableBind_EmitsTypeFirst()
    {
        // TypeName in the emitter is a C# type; TypeMapper.CSharpToCalor maps it
        var bind = new BindStatementNode(DummySpan, "count", "int", false,
            new IntLiteralNode(DummySpan, 42), new AttributeCollection());
        var module = MakeModuleWithBinds(bind);

        var emitter = new CalorEmitter();
        var output = emitter.Emit(module);

        // Immutable typed: {type:name} format
        Assert.Contains("§B{i32:count} 42", output);
        Assert.DoesNotContain(":const", output);
    }

    [Fact]
    public void CalorEmitter_TypedMutableBind_EmitsTildePrefixWithType()
    {
        var bind = new BindStatementNode(DummySpan, "total", "int", true,
            new IntLiteralNode(DummySpan, 0), new AttributeCollection());
        var module = MakeModuleWithBinds(bind);

        var emitter = new CalorEmitter();
        var output = emitter.Emit(module);

        // Mutable typed: {~name:type} format
        Assert.Contains("§B{~total:i32} 0", output);
    }

    [Fact]
    public void CalorEmitter_BindOutput_ReParsesCorrectly()
    {
        // Construct AST with all 4 bind variants
        var binds = new[]
        {
            new BindStatementNode(DummySpan, "a", null, false,
                new IntLiteralNode(DummySpan, 1), new AttributeCollection()),
            new BindStatementNode(DummySpan, "b", null, true,
                new IntLiteralNode(DummySpan, 2), new AttributeCollection()),
            new BindStatementNode(DummySpan, "c", "int", false,
                new IntLiteralNode(DummySpan, 3), new AttributeCollection()),
            new BindStatementNode(DummySpan, "d", "int", true,
                new IntLiteralNode(DummySpan, 4), new AttributeCollection()),
        };
        var module = MakeModuleWithBinds(binds);

        // Emit via CalorEmitter
        var emitter = new CalorEmitter();
        var calor = emitter.Emit(module);

        // Re-parse the emitted output
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calor, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        var reparsed = parser.Parse();

        Assert.False(diagnostics.HasErrors,
            $"Emitter output should re-parse without errors.\nOutput:\n{calor}\nErrors: {string.Join("\n", diagnostics.Select(d => d.Message))}");

        var func = Assert.Single(reparsed.Functions);
        Assert.Equal(4, func.Body.Count);

        // Immutable, no type
        var bindA = (BindStatementNode)func.Body[0];
        Assert.Equal("a", bindA.Name);
        Assert.False(bindA.IsMutable);
        Assert.Null(bindA.TypeName);

        // Mutable, no type
        var bindB = (BindStatementNode)func.Body[1];
        Assert.Equal("b", bindB.Name);
        Assert.True(bindB.IsMutable);
        Assert.Null(bindB.TypeName);

        // Immutable, with type
        var bindC = (BindStatementNode)func.Body[2];
        Assert.Equal("c", bindC.Name);
        Assert.False(bindC.IsMutable);
        Assert.NotNull(bindC.TypeName);

        // Mutable, with type
        var bindD = (BindStatementNode)func.Body[3];
        Assert.Equal("d", bindD.Name);
        Assert.True(bindD.IsMutable);
        Assert.NotNull(bindD.TypeName);
    }

    #endregion

    #region Array Initialized Tests

    [Fact]
    public void Array_BareElementInitialized_ParsesCorrectly()
    {
        // §ARR{nums:i32} 1 2 3 §/ARR{nums} → 3 elements, no size
        var source = @"
§M{m1:Test}
§F{f001:Main:pub}
  §O{void}
  §ARR{nums:i32} 1 2 3 §/ARR{nums}
§/F{f001}
§/M{m1}
";
        var module = ParseModule(source);
        var func = Assert.Single(module.Functions);
        var bind = (BindStatementNode)func.Body[0];
        Assert.Equal("nums", bind.Name);
        var arr = (ArrayCreationNode)bind.Initializer!;
        Assert.Equal(3, arr.Initializer.Count);
        Assert.Null(arr.Size);
        Assert.Equal("i32", arr.ElementType);
    }

    [Fact]
    public void Array_StandaloneStatement_ParsesWithoutError()
    {
        // §ARR at statement level (no §B wrapper)
        var source = @"
§M{m1:Test}
§F{f001:Main:pub}
  §O{void}
  §ARR{nums:i32} 1 2 3 §/ARR{nums}
§/F{f001}
§/M{m1}
";
        var diagnostics = ParseWithDiagnostics(source);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
    }

    [Fact]
    public void Array_ArgPrefixedElements_BackwardCompat()
    {
        // §A-prefixed elements still work
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

    [Fact]
    public void Array_BareElements_EmitsCorrectCSharp()
    {
        // Bare element syntax → correct C# generation
        var source = @"
§M{m1:Test}
§F{f001:Main:pub}
  §O{void}
  §ARR{nums:i32} 1 2 3 §/ARR{nums}
§/F{f001}
§/M{m1}
";
        var result = ParseAndEmit(source);
        Assert.Contains("new int[]", result);
        Assert.Contains("1", result);
        Assert.Contains("2", result);
        Assert.Contains("3", result);
    }

    [Fact]
    public void Array_SizedArray_StillWorks()
    {
        // §ARR{a1:i32:10} unchanged behavior
        var source = @"
§M{m1:Test}
§F{f001:Main:pub}
  §O{void}
  §B{[i32]:arr1} §ARR{i32:arr1:10}
§/F{f001}
§/M{m1}
";
        var result = ParseAndEmit(source);
        Assert.Contains("new int[10]", result);
    }

    [Fact]
    public void Array_CSharpInit_EmitterRoundTrip()
    {
        // C# array init → converter → Calor → parse → emit C#
        var converter = new CSharpToCalorConverter();
        var conversionResult = converter.Convert("int[] nums = new int[] { 1, 2, 3 };");

        Assert.True(conversionResult.Success,
            string.Join("\n", conversionResult.Issues.Select(i => i.Message)));

        var compilationResult = Program.Compile(conversionResult.CalorSource!);
        Assert.False(compilationResult.HasErrors,
            $"Roundtrip failed:\nCalor: {conversionResult.CalorSource}\n{string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message))}");
        Assert.Contains("new int[]", compilationResult.GeneratedCode);
        Assert.Contains("1", compilationResult.GeneratedCode);
        Assert.Contains("2", compilationResult.GeneratedCode);
        Assert.Contains("3", compilationResult.GeneratedCode);
    }

    [Fact]
    public void Array_InitializedInReturnExprContext_CalorEmitterInline()
    {
        // Initialized array in return (expression context) should emit inline §ARR, not break
        var source = @"
§M{m1:Test}
§F{f001:GetNums:pub}
  §O{[i32]}
  §R §ARR{nums:i32} 1 2 3 §/ARR{nums}
§/F{f001}
§/M{m1}
";
        var module = ParseModule(source);
        var calorEmitter = new Migration.CalorEmitter();
        var result = calorEmitter.Emit(module);

        // The §R and §ARR should be on the same line (inline), not split
        Assert.Contains("§R §ARR{", result);
        Assert.Contains("§/ARR{nums}", result);
    }

    [Fact]
    public void Array_InitializedInReturnExprContext_RoundTrip()
    {
        // return new int[] { 1, 2, 3 } → parse → emit C# should produce correct code
        var source = @"
§M{m1:Test}
§F{f001:GetNums:pub}
  §O{[i32]}
  §R §ARR{nums:i32} 1 2 3 §/ARR{nums}
§/F{f001}
§/M{m1}
";
        var result = ParseAndEmit(source);
        Assert.Contains("return new int[]", result);
        Assert.Contains("1", result);
        Assert.Contains("2", result);
        Assert.Contains("3", result);
    }

    #endregion

    #region Helper

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
}
