using Calor.LanguageServer.Tests.Helpers;
using Calor.LanguageServer.Utilities;
using Xunit;

namespace Calor.LanguageServer.Tests.Handlers;

public class DefinitionHandlerTests
{
    [Fact]
    public void FindDefinition_Function_ReturnsFunction()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Add}
            §I{i32:a}
            §I{i32:b}
            §O{i32}
            §R a + b
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
            §FLD{str:name}
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
            §MT{m001:GetArea}
            §O{f64}
            §/MT{m001}
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
            §EM{Green}
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
    public void FindDefinition_NonExistent_ReturnsNull()
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

        var def = SymbolFinder.FindDefinition(ast, "NotFound");

        Assert.Null(def);
    }

    [Fact]
    public void FindFunction_ExistingFunction_ReturnsFunction()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Calculate}
            §I{i32:x}
            §I{i32:y}
            §O{i32}
            §R x + y
            §/F{f001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var func = SymbolFinder.FindFunction(ast, "Calculate");

        Assert.NotNull(func);
        Assert.Equal("Calculate", func.Name);
        Assert.Equal(2, func.Parameters.Count);
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
    public void FindDefinition_MultipleFunctions_FindsCorrectOne()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:First}
            §R 1
            §/F{f001}
            §F{f002:Second}
            §R 2
            §/F{f002}
            §F{f003:Third}
            §R 3
            §/F{f003}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var def = SymbolFinder.FindDefinition(ast, "Second");

        Assert.NotNull(def);
        Assert.IsType<Calor.Compiler.Ast.FunctionNode>(def);
        var func = (Calor.Compiler.Ast.FunctionNode)def;
        Assert.Equal("Second", func.Name);
    }

    [Fact]
    public void FindDefinition_NestedClassInOtherClass_FindsClass()
    {
        var source = """
            §M{m001:TestModule}
            §CL{c001:Outer}
            §FLD{i32:x}
            §/CL{c001}
            §CL{c002:Inner}
            §FLD{str:name}
            §/CL{c002}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var def = SymbolFinder.FindDefinition(ast, "Inner");

        Assert.NotNull(def);
        Assert.IsType<Calor.Compiler.Ast.ClassDefinitionNode>(def);
    }
}
