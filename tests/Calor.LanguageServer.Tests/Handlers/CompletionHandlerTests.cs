using Calor.Compiler.Ast;
using Calor.LanguageServer.State;
using Calor.LanguageServer.Tests.Helpers;
using Xunit;

namespace Calor.LanguageServer.Tests.Handlers;

public class CompletionHandlerTests
{
    [Fact]
    public void GetAst_ValidModule_ReturnsAst()
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
        Assert.Single(ast.Functions);
        Assert.Equal("Add", ast.Functions[0].Name);
    }

    [Fact]
    public void GetAst_WithClass_HasClassMembers()
    {
        var source = """
            §M{m001:TestModule}
            §CL{c001:Person}
            §FLD{str:name}
            §FLD{i32:age}
            §MT{m001:GetName}
            §O{str}
            §R name
            §/MT{m001}
            §/CL{c001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);

        Assert.NotNull(ast);
        Assert.Single(ast.Classes);
        var cls = ast.Classes[0];
        Assert.Equal("Person", cls.Name);
        Assert.Equal(2, cls.Fields.Count);
        Assert.Single(cls.Methods);
    }

    [Fact]
    public void GetAst_WithEnum_HasEnumMembers()
    {
        var source = """
            §M{m001:TestModule}
            §EN{e001:Color}
            §EM{Red}
            §EM{Green}
            §EM{Blue}
            §/EN{e001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);

        Assert.NotNull(ast);
        Assert.Single(ast.Enums);
        var enumDef = ast.Enums[0];
        Assert.Equal("Color", enumDef.Name);
        Assert.Equal(3, enumDef.Members.Count);
    }

    [Fact]
    public void GetAst_WithInterface_HasMethods()
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
        Assert.Single(ast.Interfaces);
        var iface = ast.Interfaces[0];
        Assert.Equal("IShape", iface.Name);
        Assert.Single(iface.Methods);
    }

    [Fact]
    public void GetAst_WithDelegate_HasDelegate()
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
        Assert.Single(ast.Delegates);
        Assert.Equal("Callback", ast.Delegates[0].Name);
    }

    [Fact]
    public void Function_Parameters_AreExtracted()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Calculate}
            §I{i32:x}
            §I{i32:y}
            §I{str:label}
            §O{i32}
            §R x + y
            §/F{f001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);

        Assert.NotNull(ast);
        var func = ast.Functions[0];
        Assert.Equal(3, func.Parameters.Count);
        Assert.Equal("x", func.Parameters[0].Name);
        Assert.Equal("INT", func.Parameters[0].TypeName);
        Assert.Equal("y", func.Parameters[1].Name);
        Assert.Equal("label", func.Parameters[2].Name);
        Assert.Equal("STRING", func.Parameters[2].TypeName);
    }

    [Fact]
    public void Function_OutputType_IsExtracted()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:GetValue}
            §O{str}
            §R "hello"
            §/F{f001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);

        Assert.NotNull(ast);
        var func = ast.Functions[0];
        Assert.NotNull(func.Output);
        Assert.Equal("STRING", func.Output.TypeName);
    }

    [Fact]
    public void Function_VoidReturn_HasNoOutput()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:DoNothing}
            §R
            §/F{f001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);

        Assert.NotNull(ast);
        var func = ast.Functions[0];
        Assert.Null(func.Output);
    }

    [Fact]
    public void LocalBinding_IsRecognized()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Test}
            §B{x:i32} 42
            §R x
            §/F{f001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);

        Assert.NotNull(ast);
        var func = ast.Functions[0];
        Assert.NotEmpty(func.Body);
        var bind = func.Body[0] as BindStatementNode;
        Assert.NotNull(bind);
        Assert.Equal("x", bind.Name);
        Assert.Equal("INT", bind.TypeName);
    }

    [Fact]
    public void ForLoop_IsRecognized()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Test}
            §L{l001:i:0:10}
            §P i
            §/L{l001}
            §/F{f001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);

        Assert.NotNull(ast);
        var func = ast.Functions[0];
        Assert.NotEmpty(func.Body);
        var forLoop = func.Body[0] as ForStatementNode;
        Assert.NotNull(forLoop);
        Assert.Equal("i", forLoop.VariableName);
    }

    [Fact]
    public void WhileLoop_IsRecognized()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Test}
            §B{bool:running} true
            §WH{w001} running
            §B{bool:running} false
            §/WH{w001}
            §/F{f001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);

        Assert.NotNull(ast);
        var func = ast.Functions[0];
        Assert.True(func.Body.Count >= 2);
        var whileLoop = func.Body[1] as WhileStatementNode;
        Assert.NotNull(whileLoop);
    }

    [Fact]
    public void IfStatement_IsRecognized()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Test}
            §I{bool:condition}
            §O{i32}
            §IF{if001} condition
            §R 1
            §EL
            §R 0
            §/IF{if001}
            §/F{f001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);

        Assert.NotNull(ast);
        var func = ast.Functions[0];
        Assert.NotEmpty(func.Body);
        var ifStmt = func.Body[0] as IfStatementNode;
        Assert.NotNull(ifStmt);
        Assert.NotEmpty(ifStmt.ThenBody);
        Assert.NotNull(ifStmt.ElseBody);
    }

    [Fact]
    public void MemberCompletion_ClassField_ShowsFields()
    {
        var source = """
            §M{m001:TestModule}
            §CL{c001:Person}
            §FLD{str:name}
            §FLD{i32:age}
            §/CL{c001}
            §F{f001:Test}
            §B{p:Person} §NEW Person
            §R p.
            §/F{f001}
            §/M{m001}
            """;

        var completions = LspTestHarness.GetCompletions(source, "p.");

        Assert.NotEmpty(completions);
        Assert.Contains(completions, c => c.Label == "name");
        Assert.Contains(completions, c => c.Label == "age");
    }

    [Fact]
    public void MemberCompletion_ClassMethod_ShowsMethods()
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
            §F{f001:Test}
            §B{calc:Calculator} §NEW Calculator
            §R calc.
            §/F{f001}
            §/M{m001}
            """;

        var completions = LspTestHarness.GetCompletions(source, "calc.");

        Assert.NotEmpty(completions);
        Assert.Contains(completions, c => c.Label == "Add");
    }

    [Fact]
    public void MemberCompletion_EnumMembers_ShowsMembers()
    {
        var source = """
            §M{m001:TestModule}
            §EN{e001:Color}
            §EM{Red}
            §EM{Green}
            §EM{Blue}
            §/EN{e001}
            §F{f001:Test}
            §O{Color}
            §R Color.
            §/F{f001}
            §/M{m001}
            """;

        var completions = LspTestHarness.GetCompletions(source, "Color.");

        Assert.NotEmpty(completions);
        Assert.Contains(completions, c => c.Label == "Red");
        Assert.Contains(completions, c => c.Label == "Green");
        Assert.Contains(completions, c => c.Label == "Blue");
    }

    [Fact]
    public void MemberCompletion_StringType_ShowsStringMethods()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Test}
            §I{str:text}
            §O{str}
            §R text.
            §/F{f001}
            §/M{m001}
            """;

        var completions = LspTestHarness.GetCompletions(source, "text.");

        Assert.NotEmpty(completions);
        Assert.Contains(completions, c => c.Label == "Length");
        Assert.Contains(completions, c => c.Label == "ToUpper");
        Assert.Contains(completions, c => c.Label == "Contains");
    }

    [Fact]
    public void MemberCompletion_Parameter_ResolvesType()
    {
        var source = """
            §M{m001:TestModule}
            §CL{c001:Person}
            §FLD{str:name}
            §/CL{c001}
            §F{f001:Greet}
            §I{Person:person}
            §O{str}
            §R person.
            §/F{f001}
            §/M{m001}
            """;

        var completions = LspTestHarness.GetCompletions(source, "person.");

        Assert.NotEmpty(completions);
        Assert.Contains(completions, c => c.Label == "name");
    }
}
