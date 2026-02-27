using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Formatting;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Comprehensive tests for compact syntax features:
/// 1. Compact auto-property shorthand (single-line accessor declarations)
/// 2. Optional IDs (omitting the ID positional in tags)
/// 3. Inline signatures (parenthesized params and arrow return types)
/// 4. Combined and integration tests
/// </summary>
public class CompactSyntaxTests
{
    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    /// <summary>
    /// Parse -> CalorFormatter.Format -> Re-parse, asserting no errors at each step.
    /// Returns (original AST, formatted string, reparsed AST).
    /// </summary>
    private static (ModuleNode original, string formatted, ModuleNode reparsed) FormatAndReparse(string source)
    {
        var original = Parse(source, out var diagnostics1);
        Assert.False(diagnostics1.HasErrors,
            $"Original source should parse without errors.\nErrors: {string.Join("\n", diagnostics1.Select(d => d.Message))}");

        var formatter = new CalorFormatter();
        var formatted = formatter.Format(original);

        var reparsed = Parse(formatted, out var diagnostics2);
        Assert.False(diagnostics2.HasErrors,
            $"Formatted output should re-parse without errors.\nFormatted:\n{formatted}\nErrors: {string.Join("\n", diagnostics2.Select(d => d.Message))}");

        return (original, formatted, reparsed);
    }

    #region 1. Compact Auto-Property Shorthand

    [Fact]
    public void CompactProp_GetOnly_ParsesSingleLine()
    {
        var source = """
            §M{m001:Test}
            §CL{c001:Person}
            §PROP{p001:Name:str:pub:get}
            §/CL{c001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var cls = Assert.Single(module.Classes);
        var prop = Assert.Single(cls.Properties);
        Assert.Equal("Name", prop.Name);
        Assert.Equal("str", prop.TypeName);
        Assert.NotNull(prop.Getter);
        Assert.Null(prop.Setter);
        Assert.Null(prop.Initer);
        Assert.True(prop.IsAutoProperty);
    }

    [Fact]
    public void CompactProp_GetSet_ParsesSingleLine()
    {
        var source = """
            §M{m001:Test}
            §CL{c001:Person}
            §PROP{p001:Age:i32:pub:get,set}
            §/CL{c001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var cls = Assert.Single(module.Classes);
        var prop = Assert.Single(cls.Properties);
        Assert.Equal("Age", prop.Name);
        Assert.NotNull(prop.Getter);
        Assert.NotNull(prop.Setter);
        Assert.Null(prop.Initer);
        Assert.True(prop.IsAutoProperty);
    }

    [Fact]
    public void CompactProp_GetInit_ParsesSingleLine()
    {
        var source = """
            §M{m001:Test}
            §CL{c001:Record}
            §PROP{p001:Id:i32:pub:get,init}
            §/CL{c001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var cls = Assert.Single(module.Classes);
        var prop = Assert.Single(cls.Properties);
        Assert.Equal("Id", prop.Name);
        Assert.NotNull(prop.Getter);
        Assert.Null(prop.Setter);
        Assert.NotNull(prop.Initer);
        Assert.True(prop.IsAutoProperty);
    }

    [Fact]
    public void CompactProp_GetPrivateSet_ParsesSingleLine()
    {
        var source = """
            §M{m001:Test}
            §CL{c001:Item}
            §PROP{p001:Count:i32:pub:get,priset}
            §/CL{c001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var cls = Assert.Single(module.Classes);
        var prop = Assert.Single(cls.Properties);
        Assert.Equal("Count", prop.Name);
        Assert.NotNull(prop.Getter);
        Assert.NotNull(prop.Setter);
        Assert.Equal(Visibility.Private, prop.Setter!.Visibility);
        Assert.True(prop.IsAutoProperty);
    }

    [Fact]
    public void CompactProp_WithDefaultValue_ParsesValueExpression()
    {
        var source = """
            §M{m001:Test}
            §CL{c001:Config}
            §PROP{p001:MaxRetries:i32:pub:get,set} = 3
            §/CL{c001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var cls = Assert.Single(module.Classes);
        var prop = Assert.Single(cls.Properties);
        Assert.Equal("MaxRetries", prop.Name);
        Assert.NotNull(prop.DefaultValue);
        Assert.IsType<IntLiteralNode>(prop.DefaultValue);
        Assert.Equal(3, ((IntLiteralNode)prop.DefaultValue).Value);
    }

    [Fact]
    public void CompactProp_NoClosingTagNeeded_SingleLine()
    {
        // Compact accessor shorthand means no §/PROP closing tag is required
        var source = """
            §M{m001:Test}
            §CL{c001:Data}
            §PROP{p001:Value:str:pub:get,set}
            §PROP{p002:Label:str:pub:get}
            §/CL{c001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var cls = Assert.Single(module.Classes);
        Assert.Equal(2, cls.Properties.Count);
        Assert.Equal("Value", cls.Properties[0].Name);
        Assert.Equal("Label", cls.Properties[1].Name);
    }

    [Fact]
    public void CompactProp_RoundTrip_OldVerboseToCompactFormat()
    {
        // Old verbose format with separate §GET/§SET tags
        var source = """
            §M{m001:Test}
            §CL{c001:Person}
            §PROP{p001:Name:str:pub}
            §GET
            §/GET
            §SET{pri}
            §/SET
            §/PROP{p001}
            §/CL{c001}
            §/M{m001}
            """;

        var (original, formatted, reparsed) = FormatAndReparse(source);

        // Formatter should produce compact auto-property shorthand
        Assert.Contains("§PROP{", formatted);
        // The compact format should contain get and priset accessors
        Assert.Contains("get", formatted);
        Assert.Contains("priset", formatted);
        // Should NOT contain multi-line §GET/§SET tags
        Assert.DoesNotContain("§/GET", formatted);
        Assert.DoesNotContain("§/SET", formatted);

        // Reparsed structure should match
        var cls = Assert.Single(reparsed.Classes);
        var prop = Assert.Single(cls.Properties);
        Assert.Equal("Name", prop.Name);
        Assert.NotNull(prop.Getter);
        Assert.NotNull(prop.Setter);
    }

    [Fact]
    public void CompactProp_Static_ParsesWithModifier()
    {
        var source = """
            §M{m001:Test}
            §CL{c001:Counter}
            §PROP{p001:Count:i32:pub:static:get,set}
            §/CL{c001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var cls = Assert.Single(module.Classes);
        var prop = Assert.Single(cls.Properties);
        Assert.Equal("Count", prop.Name);
        Assert.True(prop.IsStatic);
        Assert.NotNull(prop.Getter);
        Assert.NotNull(prop.Setter);
    }

    #endregion

    #region 2. Optional IDs

    [Fact]
    public void OptionalId_Function_TwoPartNameVis()
    {
        // §F{name:vis} instead of §F{id:name:vis}
        var source = """
            §M{m001:Test}
            §F{Main:pub}
            §O{void}
            §R
            §/F
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var func = Assert.Single(module.Functions);
        Assert.Equal("Main", func.Name);
        Assert.Equal(Visibility.Public, func.Visibility);
        // ID should be auto-generated
        Assert.Contains("_auto", func.Id);
    }

    [Fact]
    public void OptionalId_Function_ThreePartOldFormatPreserved()
    {
        // Old format §F{id:name:vis} should still work
        var source = """
            §M{m001:Test}
            §F{f001:Add:pub}
            §I{i32:a}
            §I{i32:b}
            §O{i32}
            §R (+ a b)
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var func = Assert.Single(module.Functions);
        Assert.Equal("f001", func.Id);
        Assert.Equal("Add", func.Name);
        Assert.Equal(Visibility.Public, func.Visibility);
    }

    [Fact]
    public void OptionalId_ClosingTagWithoutId_NoError()
    {
        // When open tag uses auto-generated ID, closing tag can omit the ID
        var source = """
            §M{m001:Test}
            §F{Hello:pub}
            §O{void}
            §R
            §/F
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var func = Assert.Single(module.Functions);
        Assert.Equal("Hello", func.Name);
    }

    [Fact]
    public void OptionalId_Class_NameOnly()
    {
        // §CL{Person} instead of §CL{c001:Person}
        var source = """
            §M{m001:Test}
            §CL{Person}
            §FLD{str:Name:pub}
            §/CL
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var cls = Assert.Single(module.Classes);
        Assert.Equal("Person", cls.Name);
        Assert.Contains("_auto", cls.Id);
    }

    [Fact]
    public void OptionalId_Interface_NameOnly()
    {
        // §IFACE{IShape} instead of §IFACE{i001:IShape}
        var source = """
            §M{m001:Test}
            §IFACE{IShape}
            §MT{m001:Area}
            §O{f64}
            §/MT{m001}
            §/IFACE
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var iface = Assert.Single(module.Interfaces);
        Assert.Equal("IShape", iface.Name);
        Assert.Contains("_auto", iface.Id);
    }

    [Fact]
    public void OptionalId_Interface_WithBaseInterface()
    {
        // §IFACE{IChild:IParent} — no explicit ID, with base interface
        var source = """
            §M{m001:Test}
            §IFACE{IChild:IParent}
            §MT{m001:DoWork}
            §O{void}
            §/MT{m001}
            §/IFACE
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var iface = Assert.Single(module.Interfaces);
        Assert.Equal("IChild", iface.Name);
        Assert.Contains("_auto", iface.Id);
        Assert.Single(iface.BaseInterfaces);
        Assert.Equal("IParent", iface.BaseInterfaces[0]);

        var emitter = new CSharpEmitter();
        var csharp = emitter.Emit(module);
        Assert.Contains(": IParent", csharp);
    }

    [Fact]
    public void Interface_BaseInterface_FromThirdAttribute()
    {
        var source = """
            §M{m001:Test}
            §IFACE{i001:IChild:IParent}
            §MT{m001:DoWork}
            §O{void}
            §/MT{m001}
            §/IFACE{i001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var iface = Assert.Single(module.Interfaces);
        Assert.Equal("IChild", iface.Name);
        Assert.Single(iface.BaseInterfaces);
        Assert.Equal("IParent", iface.BaseInterfaces[0]);

        // Verify C# emission includes base interface
        var emitter = new CSharpEmitter();
        var csharp = emitter.Emit(module);
        Assert.Contains(": IParent", csharp);
    }

    [Fact]
    public void Interface_MultipleBaseInterfaces_FromThirdAttribute()
    {
        var source = """
            §M{m001:Test}
            §IFACE{i001:IChild:IParent,IDisposable}
            §MT{m001:DoWork}
            §O{void}
            §/MT{m001}
            §/IFACE{i001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var iface = Assert.Single(module.Interfaces);
        Assert.Equal("IChild", iface.Name);
        Assert.Equal(2, iface.BaseInterfaces.Count);
        Assert.Equal("IParent", iface.BaseInterfaces[0]);
        Assert.Equal("IDisposable", iface.BaseInterfaces[1]);

        var emitter = new CSharpEmitter();
        var csharp = emitter.Emit(module);
        Assert.Contains(": IParent, IDisposable", csharp);
    }

    [Fact]
    public void Interface_BaseInterface_RoundTrip()
    {
        var source = """
            §M{m001:Test}
            §IFACE{i001:IChild:IParent}
            §MT{m001:DoWork}
            §O{void}
            §/MT{m001}
            §/IFACE{i001}
            §/M{m001}
            """;

        // Parse → Calor emit → Re-parse → verify base interface preserved
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var calorEmitter = new CalorEmitter();
        var calorCode = calorEmitter.Emit(module);

        var module2 = Parse(calorCode, out var diagnostics2);
        Assert.False(diagnostics2.HasErrors, string.Join("\n", diagnostics2.Select(d => d.Message)));

        var iface = Assert.Single(module2.Interfaces);
        Assert.Equal("IChild", iface.Name);
        Assert.Single(iface.BaseInterfaces);
        Assert.Equal("IParent", iface.BaseInterfaces[0]);
    }

    [Fact]
    public void OptionalId_Enum_NameOnly()
    {
        // §EN{Color} instead of §EN{e001:Color}
        var source = """
            §M{m001:Test}
            §EN{Color}
            Red = 0
            Green = 1
            Blue = 2
            §/EN
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var en = Assert.Single(module.Enums);
        Assert.Equal("Color", en.Name);
        Assert.Contains("_auto", en.Id);
        Assert.Equal(3, en.Members.Count);
    }

    [Fact]
    public void OptionalId_Constructor_VisibilityOnly()
    {
        // §CTOR{pub} instead of §CTOR{ctor1:pub}
        var source = """
            §M{m001:Test}
            §CL{c001:Widget}
            §CTOR{pub}
            §I{str:name}
            §/CTOR
            §/CL{c001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var cls = Assert.Single(module.Classes);
        var ctor = Assert.Single(cls.Constructors);
        Assert.Equal(Visibility.Public, ctor.Visibility);
        Assert.Contains("_auto", ctor.Id);
        Assert.Single(ctor.Parameters);
    }

    [Fact]
    public void OptionalId_Method_WithVisibilityKeywordDetection()
    {
        // §MT{DoWork:pub} instead of §MT{m001:DoWork:pub}
        // The parser detects "pub" as a visibility keyword and shifts
        var source = """
            §M{m001:Test}
            §CL{c001:Worker}
            §MT{DoWork:pub}
            §O{void}
            §R
            §/MT
            §/CL{c001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var cls = Assert.Single(module.Classes);
        var method = Assert.Single(cls.Methods);
        Assert.Equal("DoWork", method.Name);
        Assert.Equal(Visibility.Public, method.Visibility);
        Assert.Contains("_auto", method.Id);
    }

    [Fact]
    public void OptionalId_MissingCloseTag_StillReportsError()
    {
        // Missing closing tag should still report an error, regardless of optional IDs
        var source = """
            §M{m001:Test}
            §F{Hello:pub}
            §O{void}
            §R
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        // Should have errors because §/F is missing
        Assert.True(diagnostics.HasErrors, "Missing §/F closing tag should report an error");
    }

    #endregion

    #region 3. Inline Signatures

    [Fact]
    public void InlineSig_Function_SingleParam()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Square:pub} (i32:x) -> i32
            §R (* x x)
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var func = Assert.Single(module.Functions);
        Assert.Equal("Square", func.Name);
        Assert.Single(func.Parameters);
        Assert.Equal("i32", func.Parameters[0].TypeName);
        Assert.Equal("x", func.Parameters[0].Name);
        Assert.NotNull(func.Output);
        Assert.Equal("i32", func.Output!.TypeName);
    }

    [Fact]
    public void InlineSig_Function_MultipleParams()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Add:pub} (i32:a, i32:b) -> i32
            §R (+ a b)
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var func = Assert.Single(module.Functions);
        Assert.Equal(2, func.Parameters.Count);
        Assert.Equal("a", func.Parameters[0].Name);
        Assert.Equal("b", func.Parameters[1].Name);
        Assert.NotNull(func.Output);
        Assert.Equal("i32", func.Output!.TypeName);
    }

    [Fact]
    public void InlineSig_Function_GenericParam()
    {
        var source = """
            §M{m001:Test}
            §F{f001:First:pub} (List<i32>:items) -> i32
            §R 0
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var func = Assert.Single(module.Functions);
        Assert.Single(func.Parameters);
        Assert.Equal("List<i32>", func.Parameters[0].TypeName);
        Assert.Equal("items", func.Parameters[0].Name);
    }

    [Fact]
    public void InlineSig_Function_NullableParam()
    {
        var source = """
            §M{m001:Test}
            §F{f001:OrDefault:pub} (i32?:value) -> i32
            §R 0
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var func = Assert.Single(module.Functions);
        Assert.Single(func.Parameters);
        // Parser may represent nullable as "?i32" or "i32?" depending on parsing order
        var paramType = func.Parameters[0].TypeName;
        Assert.True(paramType.Contains("i32") && paramType.Contains("?"),
            $"Expected nullable i32 type, got: {paramType}");
    }

    [Fact]
    public void InlineSig_Function_ArrayParam()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Sum:pub} ([i32]:values) -> i32
            §R 0
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var func = Assert.Single(module.Functions);
        Assert.Single(func.Parameters);
        Assert.Contains("i32", func.Parameters[0].TypeName);
    }

    [Fact]
    public void InlineSig_Function_NoReturnType()
    {
        var source = """
            §M{m001:Test}
            §F{f001:PrintHello:pub} ()
            §E{cw}
            §C{Console.WriteLine} "hello"
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var func = Assert.Single(module.Functions);
        Assert.Equal("PrintHello", func.Name);
        Assert.Empty(func.Parameters);
        // No return type specified via inline signature, may use §O or be null
        // The function should still parse successfully
    }

    [Fact]
    public void InlineSig_Method_InClassContext()
    {
        var source = """
            §M{m001:Test}
            §CL{c001:Calculator}
            §MT{m001:Add:pub} (i32:a, i32:b) -> i32
            §R (+ a b)
            §/MT{m001}
            §/CL{c001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var cls = Assert.Single(module.Classes);
        var method = Assert.Single(cls.Methods);
        Assert.Equal("Add", method.Name);
        Assert.Equal(2, method.Parameters.Count);
        Assert.NotNull(method.Output);
        Assert.Equal("i32", method.Output!.TypeName);
    }

    [Fact]
    public void InlineSig_Constructor_WithParams()
    {
        var source = """
            §M{m001:Test}
            §CL{c001:Person}
            §FLD{str:_name:pri}
            §CTOR{ctor1:pub} (str:name)
            §C{Console.WriteLine} name
            §/CTOR{ctor1}
            §/CL{c001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var cls = Assert.Single(module.Classes);
        var ctor = Assert.Single(cls.Constructors);
        Assert.Single(ctor.Parameters);
        Assert.Equal("str", ctor.Parameters[0].TypeName);
        Assert.Equal("name", ctor.Parameters[0].Name);
    }

    [Fact]
    public void InlineSig_EmptyParamsWithReturn()
    {
        var source = """
            §M{m001:Test}
            §F{f001:GetZero:pub} () -> i32
            §R 0
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var func = Assert.Single(module.Functions);
        Assert.Empty(func.Parameters);
        Assert.NotNull(func.Output);
        Assert.Equal("i32", func.Output!.TypeName);
    }

    [Fact]
    public void InlineSig_WithContracts()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Divide:pub} (i32:a, i32:b) -> i32
            §Q (!= b 0)
            §S (>= result 0)
            §R (/ a b)
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var func = Assert.Single(module.Functions);
        Assert.Equal(2, func.Parameters.Count);
        Assert.NotNull(func.Output);
        Assert.Single(func.Preconditions);
        Assert.Single(func.Postconditions);
    }

    [Fact]
    public void InlineSig_RoundTrip_Function()
    {
        // Verbose form with §I/§O tags
        var source = """
            §M{m001:Test}
            §F{f001:Add:pub}
            §I{i32:a}
            §I{i32:b}
            §O{i32}
            §R (+ a b)
            §/F{f001}
            §/M{m001}
            """;

        var (original, formatted, reparsed) = FormatAndReparse(source);

        // Formatter should produce inline signature
        Assert.Contains("(", formatted);
        Assert.Contains("->", formatted);
        // Should NOT contain §I or §O tags
        Assert.DoesNotContain("§I{", formatted);
        Assert.DoesNotContain("§O{", formatted);

        // Reparsed structure should match original
        var origFunc = Assert.Single(original.Functions);
        var reparsedFunc = Assert.Single(reparsed.Functions);
        Assert.Equal(origFunc.Name, reparsedFunc.Name);
        Assert.Equal(origFunc.Parameters.Count, reparsedFunc.Parameters.Count);
        // Output type may differ in representation (INT vs i32) due to
        // ExpandType normalization on §O{} vs raw inline type after ->
        Assert.NotNull(origFunc.Output);
        Assert.NotNull(reparsedFunc.Output);
    }

    [Fact]
    public void InlineSig_RoundTrip_Constructor()
    {
        var source = """
            §M{m001:Test}
            §CL{c001:Widget}
            §FLD{str:_name:pri}
            §CTOR{ctor1:pub}
            §I{str:name}
            §I{i32:count}
            §C{Console.WriteLine} name
            §/CTOR{ctor1}
            §/CL{c001}
            §/M{m001}
            """;

        var (original, formatted, reparsed) = FormatAndReparse(source);

        // Formatter should produce inline constructor signature
        Assert.Contains("§CTOR{", formatted);
        Assert.Contains("(", formatted);

        // Reparsed constructor should have same parameters
        var origCtor = Assert.Single(original.Classes[0].Constructors);
        var reparsedCtor = Assert.Single(reparsed.Classes[0].Constructors);
        Assert.Equal(origCtor.Parameters.Count, reparsedCtor.Parameters.Count);
    }

    #endregion

    #region 4. Combined and Integration Tests

    [Fact]
    public void Combined_AllThreeFeatures_ParsesSuccessfully()
    {
        // Combines: optional ID, inline signature, compact auto-property
        var source = """
            §M{m001:Test}
            §CL{MyClass}
            §PROP{Name:str:pub:get,priset}
            §PROP{Count:i32:pub:get,set} = 0
            §CTOR{pub} (str:name, i32:count)
            §C{Console.WriteLine} name
            §/CTOR
            §MT{GetInfo:pub} () -> str
            §R "info"
            §/MT
            §/CL
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var cls = Assert.Single(module.Classes);
        Assert.Equal("MyClass", cls.Name);
        Assert.Contains("_auto", cls.Id);

        // Properties with compact shorthand
        Assert.Equal(2, cls.Properties.Count);
        Assert.Equal("Name", cls.Properties[0].Name);
        Assert.NotNull(cls.Properties[0].Getter);
        Assert.NotNull(cls.Properties[0].Setter);
        Assert.Equal(Visibility.Private, cls.Properties[0].Setter!.Visibility);

        Assert.Equal("Count", cls.Properties[1].Name);
        Assert.NotNull(cls.Properties[1].DefaultValue);

        // Constructor with optional ID and inline sig
        var ctor = Assert.Single(cls.Constructors);
        Assert.Contains("_auto", ctor.Id);
        Assert.Equal(2, ctor.Parameters.Count);

        // Method with optional ID and inline sig
        var method = Assert.Single(cls.Methods);
        Assert.Equal("GetInfo", method.Name);
        Assert.Contains("_auto", method.Id);
        Assert.NotNull(method.Output);
        Assert.Equal("str", method.Output!.TypeName);
    }

    [Fact]
    public void Combined_FullRoundTrip_VerboseToCompactToReparseVerifyStructure()
    {
        // Verbose old-format source
        var source = """
            §M{m001:Test}
            §CL{c001:DataHolder}
            §FLD{str:_label:pri}
            §PROP{p001:Value:i32:pub}
            §GET
            §/GET
            §SET
            §/SET
            §/PROP{p001}
            §CTOR{ctor1:pub}
            §I{str:label}
            §I{i32:value}
            §C{Console.WriteLine} label
            §/CTOR{ctor1}
            §MT{m001:GetLabel:pub}
            §O{str}
            §R "label"
            §/MT{m001}
            §/CL{c001}
            §/M{m001}
            """;

        var (original, formatted, reparsed) = FormatAndReparse(source);

        // Verify structure is preserved through round-trip
        var origCls = Assert.Single(original.Classes);
        var reparsedCls = Assert.Single(reparsed.Classes);

        Assert.Equal(origCls.Name, reparsedCls.Name);
        Assert.Equal(origCls.Fields.Count, reparsedCls.Fields.Count);
        Assert.Equal(origCls.Properties.Count, reparsedCls.Properties.Count);
        Assert.Equal(origCls.Constructors.Count, reparsedCls.Constructors.Count);
        Assert.Equal(origCls.Methods.Count, reparsedCls.Methods.Count);

        // Verify property accessors survived round-trip
        Assert.NotNull(reparsedCls.Properties[0].Getter);
        Assert.NotNull(reparsedCls.Properties[0].Setter);

        // Verify constructor params survived
        Assert.Equal(
            origCls.Constructors[0].Parameters.Count,
            reparsedCls.Constructors[0].Parameters.Count);

        // Verify method output survived
        Assert.NotNull(reparsedCls.Methods[0].Output);
    }

    [Fact]
    public void Combined_CompactnessCheck_FormattedShorterThanOriginal()
    {
        // Verbose source with lots of redundancy
        var source = """
            §M{m001:Test}
            §CL{c001:Item}
            §PROP{p001:Name:string:public}
            §GET
            §/GET
            §SET{private}
            §/SET
            §/PROP{p001}
            §PROP{p002:Value:int:public}
            §GET
            §/GET
            §SET
            §/SET
            §/PROP{p002}
            §/CL{c001}
            §F{f001:Main:public}
            §I{string:name}
            §I{int:count}
            §O{void}
            §E{cw}
            §C{Console.WriteLine} name
            §/F{f001}
            §/M{m001}
            """;

        var (_, formatted, _) = FormatAndReparse(source);

        // Compact format should be shorter than the original verbose format
        Assert.True(formatted.Length < source.Length,
            $"Compact format ({formatted.Length} chars) should be shorter than verbose ({source.Length} chars).\nFormatted:\n{formatted}");
    }

    [Fact]
    public void Integration_CSharpToCalorToParserToCodeGen_DataClass()
    {
        var csharpSource = """
            public class Person
            {
                public string Name { get; set; }
                public int Age { get; set; }
            }
            """;

        // Step 1: C# -> Calor AST
        var converter = new CSharpToCalorConverter();
        var convResult = converter.Convert(csharpSource);
        Assert.True(convResult.Success,
            $"C# to Calor conversion failed: {string.Join("\n", convResult.Issues.Select(i => i.Message))}");
        Assert.NotNull(convResult.Ast);

        // Step 2: Calor AST -> Calor text via CalorEmitter
        var calorEmitter = new CalorEmitter();
        var calorText = calorEmitter.Emit(convResult.Ast!);

        // Step 3: Calor text -> Parse back to AST
        var parsed = Parse(calorText, out var parseDiags);
        Assert.False(parseDiags.HasErrors,
            $"Calor re-parse failed.\nCalor text:\n{calorText}\nErrors: {string.Join("\n", parseDiags.Select(d => d.Message))}");

        // Step 4: AST -> C# via CSharpEmitter
        var csharpEmitter = new CSharpEmitter();
        var generatedCSharp = csharpEmitter.Emit(parsed);

        // Verify the generated C# contains expected elements
        Assert.Contains("Person", generatedCSharp);
        Assert.Contains("Name", generatedCSharp);
        Assert.Contains("Age", generatedCSharp);
    }

    [Fact]
    public void Integration_CSharpToCalorToParserToCodeGen_Interface()
    {
        var csharpSource = """
            public interface IGreeter
            {
                string Greet(string name);
            }
            """;

        // Step 1: C# -> Calor AST
        var converter = new CSharpToCalorConverter();
        var convResult = converter.Convert(csharpSource);
        Assert.True(convResult.Success,
            $"C# to Calor conversion failed: {string.Join("\n", convResult.Issues.Select(i => i.Message))}");
        Assert.NotNull(convResult.Ast);

        // Step 2: Calor AST -> Calor text
        var calorEmitter = new CalorEmitter();
        var calorText = calorEmitter.Emit(convResult.Ast!);

        // Step 3: Parse Calor text
        var parsed = Parse(calorText, out var parseDiags);
        Assert.False(parseDiags.HasErrors,
            $"Calor re-parse failed.\nCalor text:\n{calorText}\nErrors: {string.Join("\n", parseDiags.Select(d => d.Message))}");

        // Step 4: Generate C#
        var csharpEmitter = new CSharpEmitter();
        var generatedCSharp = csharpEmitter.Emit(parsed);

        Assert.Contains("IGreeter", generatedCSharp);
        Assert.Contains("Greet", generatedCSharp);
    }

    [Fact]
    public void Integration_CSharpToCalorToParserToCodeGen_DefaultParamValue()
    {
        var csharpSource = """
            public class Util
            {
                public static int Clamp(int value, int min = 0, int max = 100)
                {
                    if (value < min) return min;
                    if (value > max) return max;
                    return value;
                }
            }
            """;

        // Step 1: C# -> Calor AST
        var converter = new CSharpToCalorConverter();
        var convResult = converter.Convert(csharpSource);
        Assert.True(convResult.Success,
            $"C# to Calor conversion failed: {string.Join("\n", convResult.Issues.Select(i => i.Message))}");
        Assert.NotNull(convResult.Ast);

        // Step 2: Calor AST -> Calor text
        var calorEmitter = new CalorEmitter();
        var calorText = calorEmitter.Emit(convResult.Ast!);

        // Step 3: Parse Calor text
        var parsed = Parse(calorText, out var parseDiags);
        Assert.False(parseDiags.HasErrors,
            $"Calor re-parse failed.\nCalor text:\n{calorText}\nErrors: {string.Join("\n", parseDiags.Select(d => d.Message))}");

        // Step 4: Generate C#
        var csharpEmitter = new CSharpEmitter();
        var generatedCSharp = csharpEmitter.Emit(parsed);

        Assert.Contains("Clamp", generatedCSharp);
        Assert.Contains("value", generatedCSharp);
    }

    #endregion
}
