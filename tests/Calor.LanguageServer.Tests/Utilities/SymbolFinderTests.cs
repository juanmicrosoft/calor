using Calor.LanguageServer.Tests.Helpers;
using Calor.LanguageServer.Utilities;
using Xunit;

namespace Calor.LanguageServer.Tests.Utilities;

public class SymbolFinderTests
{
    [Fact]
    public void FindDefinition_Function_ReturnsFunction()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Add}
            §R 0
            §/F{f001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var def = SymbolFinder.FindDefinition(ast, "Add");

        Assert.NotNull(def);
        Assert.IsType<Calor.Compiler.Ast.FunctionNode>(def);
    }

    [Fact]
    public void FindDefinition_Class_ReturnsClass()
    {
        var source = """
            §M{m001:TestModule}
            §CL{c001:Person}
            §/CL{c001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var def = SymbolFinder.FindDefinition(ast, "Person");

        Assert.NotNull(def);
        Assert.IsType<Calor.Compiler.Ast.ClassDefinitionNode>(def);
    }

    [Fact]
    public void FindDefinition_Interface_ReturnsInterface()
    {
        var source = """
            §M{m001:TestModule}
            §IFACE{i001:IShape}
            §/IFACE{i001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var def = SymbolFinder.FindDefinition(ast, "IShape");

        Assert.NotNull(def);
        Assert.IsType<Calor.Compiler.Ast.InterfaceDefinitionNode>(def);
    }

    [Fact]
    public void FindDefinition_Enum_ReturnsEnum()
    {
        var source = """
            §M{m001:TestModule}
            §EN{e001:Color}
            §EM{Red}
            §/EN{e001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var def = SymbolFinder.FindDefinition(ast, "Color");

        Assert.NotNull(def);
        Assert.IsType<Calor.Compiler.Ast.EnumDefinitionNode>(def);
    }

    [Fact]
    public void FindDefinition_Delegate_ReturnsDelegate()
    {
        var source = """
            §M{m001:TestModule}
            §DEL{d001:Callback}
            §I{i32:value}
            §O{void}
            §/DEL{d001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var def = SymbolFinder.FindDefinition(ast, "Callback");

        Assert.NotNull(def);
        Assert.IsType<Calor.Compiler.Ast.DelegateDefinitionNode>(def);
    }

    [Fact]
    public void FindDefinition_Nonexistent_ReturnsNull()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Test}
            §R 0
            §/F{f001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var def = SymbolFinder.FindDefinition(ast, "NonExistent");

        Assert.Null(def);
    }

    [Fact]
    public void FindFunction_ExistingFunction_ReturnsFunction()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Calculate}
            §I{i32:x}
            §O{i32}
            §R x
            §/F{f001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var func = SymbolFinder.FindFunction(ast, "Calculate");

        Assert.NotNull(func);
        Assert.Equal("Calculate", func.Name);
        Assert.Single(func.Parameters);
    }

    [Fact]
    public void FindFunction_NonExistent_ReturnsNull()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Test}
            §R 0
            §/F{f001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var func = SymbolFinder.FindFunction(ast, "Missing");

        Assert.Null(func);
    }

    [Fact]
    public void FindMethod_ExistingMethod_ReturnsMethod()
    {
        var source = """
            §M{m001:TestModule}
            §CL{c001:Calculator}
            §MT{m001:Add}
            §I{i32:a}
            §I{i32:b}
            §O{i32}
            §R a + b
            §/MT{m001}
            §/CL{c001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var cls = ast.Classes.FirstOrDefault(c => c.Name == "Calculator");
        Assert.NotNull(cls);

        var method = SymbolFinder.FindMethod(cls, "Add");

        Assert.NotNull(method);
        Assert.Equal("Add", method.Name);
        Assert.Equal(2, method.Parameters.Count);
    }

    [Fact]
    public void FindMethod_NonExistent_ReturnsNull()
    {
        var source = """
            §M{m001:TestModule}
            §CL{c001:Calculator}
            §/CL{c001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var cls = ast.Classes.FirstOrDefault(c => c.Name == "Calculator");
        Assert.NotNull(cls);

        var method = SymbolFinder.FindMethod(cls, "Missing");

        Assert.Null(method);
    }

    // Position-based tests using the marker approach
    [Fact]
    public void FindSymbol_AtModuleName_ReturnsModule()
    {
        var (source, line, column) = LspTestHarness.FindMarker("""
            §M{m001:/*cursor*/TestModule}
            §F{f001:Test}
            §R 0
            §/F{f001}
            §/M{m001}
            """);

        var result = LspTestHarness.FindSymbol(source, line, column);

        Assert.NotNull(result);
        Assert.Equal("TestModule", result.Name);
        Assert.Equal("module", result.Kind);
    }

    [Fact]
    public void FindSymbol_AtFunctionName_ReturnsFunction()
    {
        var (source, line, column) = LspTestHarness.FindMarker("""
            §M{m001:TestModule}
            §F{f001:/*cursor*/Add}
            §I{i32:a}
            §O{i32}
            §R a
            §/F{f001}
            §/M{m001}
            """);

        var result = LspTestHarness.FindSymbol(source, line, column);

        Assert.NotNull(result);
        Assert.Equal("Add", result.Name);
        Assert.Equal("function", result.Kind);
    }

    [Fact]
    public void FindSymbol_AtParameterName_ReturnsParameter()
    {
        var (source, line, column) = LspTestHarness.FindMarker("""
            §M{m001:TestModule}
            §F{f001:Add}
            §I{i32:/*cursor*/myParam}
            §O{i32}
            §R myParam
            §/F{f001}
            §/M{m001}
            """);

        var result = LspTestHarness.FindSymbol(source, line, column);

        Assert.NotNull(result);
        Assert.Equal("myParam", result.Name);
        Assert.Equal("parameter", result.Kind);
        Assert.Equal("INT", result.Type);
    }

    [Fact]
    public void FindSymbol_AtTypeName_ReturnsType()
    {
        var (source, line, column) = LspTestHarness.FindMarker("""
            §M{m001:TestModule}
            §F{f001:Test}
            §I{/*cursor*/i32:x}
            §O{i32}
            §R x
            §/F{f001}
            §/M{m001}
            """);

        var result = LspTestHarness.FindSymbol(source, line, column);

        Assert.NotNull(result);
        Assert.Equal("i32", result.Name);
        Assert.Equal("type", result.Kind);
    }

    [Fact]
    public void FindSymbol_AtLocalVariable_ReturnsVariable()
    {
        var (source, line, column) = LspTestHarness.FindMarker("""
            §M{m001:TestModule}
            §F{f001:Test}
            §B{/*cursor*/myVar:i32} 42
            §R myVar
            §/F{f001}
            §/M{m001}
            """);

        var result = LspTestHarness.FindSymbol(source, line, column);

        Assert.NotNull(result);
        Assert.Equal("myVar", result.Name);
        Assert.Contains("variable", result.Kind);
    }

    [Fact]
    public void FindSymbol_AtVariableReference_ReturnsReference()
    {
        var (source, line, column) = LspTestHarness.FindMarker("""
            §M{m001:TestModule}
            §F{f001:Test}
            §I{i32:n}
            §O{i32}
            §R /*cursor*/n
            §/F{f001}
            §/M{m001}
            """);

        var result = LspTestHarness.FindSymbol(source, line, column);

        Assert.NotNull(result);
        Assert.Equal("n", result.Name);
        // It's either parameter or variable reference
        Assert.True(result.Kind.Contains("parameter") || result.Kind.Contains("reference"));
    }

    [Fact]
    public void FindSymbol_AtClassName_ReturnsClass()
    {
        var (source, line, column) = LspTestHarness.FindMarker("""
            §M{m001:TestModule}
            §CL{c001:/*cursor*/Person}
            §FLD{str:name}
            §/CL{c001}
            §/M{m001}
            """);

        var result = LspTestHarness.FindSymbol(source, line, column);

        Assert.NotNull(result);
        Assert.Equal("Person", result.Name);
        Assert.Equal("class", result.Kind);
    }

    [Fact]
    public void FindSymbol_AtFieldName_ReturnsField()
    {
        var (source, line, column) = LspTestHarness.FindMarker("""
            §M{m001:TestModule}
            §CL{c001:Person}
            §FLD{str:/*cursor*/name}
            §/CL{c001}
            §/M{m001}
            """);

        var result = LspTestHarness.FindSymbol(source, line, column);

        Assert.NotNull(result);
        Assert.Equal("name", result.Name);
        Assert.Equal("field", result.Kind);
    }

    [Fact]
    public void FindSymbol_AtEnumName_ReturnsEnum()
    {
        var (source, line, column) = LspTestHarness.FindMarker("""
            §M{m001:TestModule}
            §EN{e001:/*cursor*/Color}
            §EM{Red}
            §/EN{e001}
            §/M{m001}
            """);

        var result = LspTestHarness.FindSymbol(source, line, column);

        Assert.NotNull(result);
        Assert.Equal("Color", result.Name);
        Assert.Equal("enum", result.Kind);
    }

    [Fact]
    public void FindSymbol_AtEnumMember_ReturnsEnumMember()
    {
        var (source, line, column) = LspTestHarness.FindMarker("""
            §M{m001:TestModule}
            §EN{e001:Color}
            §EM{/*cursor*/Red}
            §/EN{e001}
            §/M{m001}
            """);

        var result = LspTestHarness.FindSymbol(source, line, column);

        Assert.NotNull(result);
        Assert.Equal("Red", result.Name);
        Assert.Equal("enum member", result.Kind);
    }

    [Fact]
    public void FindSymbol_AtIntegerLiteral_ReturnsLiteral()
    {
        var (source, line, column) = LspTestHarness.FindMarker("""
            §M{m001:TestModule}
            §F{f001:Test}
            §O{i32}
            §R /*cursor*/42
            §/F{f001}
            §/M{m001}
            """);

        var result = LspTestHarness.FindSymbol(source, line, column);

        Assert.NotNull(result);
        Assert.Equal("42", result.Name);
        Assert.Equal("integer literal", result.Kind);
    }

    [Fact]
    public void FindSymbol_OutsideModule_ReturnsNull()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Test}
            §R 0
            §/F{f001}
            §/M{m001}
            """;

        // Position outside the module - far past the end
        var result = LspTestHarness.FindSymbol(source, 100, 1);

        Assert.Null(result);
    }

    [Fact]
    public void FindSymbol_AtMethodName_ReturnsMethod()
    {
        var (source, line, column) = LspTestHarness.FindMarker("""
            §M{m001:TestModule}
            §CL{c001:Calculator}
            §MT{m001:/*cursor*/Add}
            §I{i32:a}
            §I{i32:b}
            §O{i32}
            §R a + b
            §/MT{m001}
            §/CL{c001}
            §/M{m001}
            """);

        var result = LspTestHarness.FindSymbol(source, line, column);

        Assert.NotNull(result);
        Assert.Equal("Add", result.Name);
        Assert.Equal("method", result.Kind);
    }

    [Fact]
    public void Ast_ContainsMultipleFunctions()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:First}
            §R 1
            §/F{f001}
            §F{f002:Second}
            §R 2
            §/F{f002}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);

        Assert.NotNull(ast);
        Assert.Equal(2, ast.Functions.Count);
        Assert.Contains(ast.Functions, f => f.Name == "First");
        Assert.Contains(ast.Functions, f => f.Name == "Second");
    }

    [Fact]
    public void Ast_ContainsMultipleClasses()
    {
        var source = """
            §M{m001:TestModule}
            §CL{c001:Person}
            §FLD{str:name}
            §/CL{c001}
            §CL{c002:Employee}
            §FLD{i32:id}
            §/CL{c002}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);

        Assert.NotNull(ast);
        Assert.Equal(2, ast.Classes.Count);
        Assert.Contains(ast.Classes, c => c.Name == "Person");
        Assert.Contains(ast.Classes, c => c.Name == "Employee");
    }
}
