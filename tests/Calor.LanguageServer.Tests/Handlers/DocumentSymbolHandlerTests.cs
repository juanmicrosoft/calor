using Calor.LanguageServer.Tests.Helpers;
using Xunit;

namespace Calor.LanguageServer.Tests.Handlers;

public class DocumentSymbolHandlerTests
{
    [Fact]
    public void DocumentSymbols_FunctionsListed()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Add}
            §R 0
            §/F{f001}
            §F{f002:Subtract}
            §R 0
            §/F{f002}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);

        Assert.NotNull(ast);
        Assert.Equal(2, ast.Functions.Count);
        Assert.Contains(ast.Functions, f => f.Name == "Add");
        Assert.Contains(ast.Functions, f => f.Name == "Subtract");
    }

    [Fact]
    public void DocumentSymbols_ClassesListed()
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

    [Fact]
    public void DocumentSymbols_ClassWithMembersListed()
    {
        var source = """
            §M{m001:TestModule}
            §CL{c001:Calculator}
            §FLD{i32:value}
            §MT{m001:Add}
            §I{i32:x}
            §O{i32}
            §R x
            §/MT{m001}
            §MT{m002:Subtract}
            §I{i32:x}
            §O{i32}
            §R x
            §/MT{m002}
            §/CL{c001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);

        Assert.NotNull(ast);
        Assert.Single(ast.Classes);
        var cls = ast.Classes[0];
        Assert.Equal("Calculator", cls.Name);
        Assert.Single(cls.Fields);
        Assert.Equal(2, cls.Methods.Count);
        Assert.Contains(cls.Methods, m => m.Name == "Add");
        Assert.Contains(cls.Methods, m => m.Name == "Subtract");
    }

    [Fact]
    public void DocumentSymbols_InterfacesListed()
    {
        var source = """
            §M{m001:TestModule}
            §IFACE{i001:IReadable}
            §MT{m001:Read}
            §O{str}
            §/MT{m001}
            §/IFACE{i001}
            §IFACE{i002:IWritable}
            §MT{m001:Write}
            §I{str:data}
            §O{void}
            §/MT{m001}
            §/IFACE{i002}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);

        Assert.NotNull(ast);
        Assert.Equal(2, ast.Interfaces.Count);
        Assert.Contains(ast.Interfaces, i => i.Name == "IReadable");
        Assert.Contains(ast.Interfaces, i => i.Name == "IWritable");
    }

    [Fact]
    public void DocumentSymbols_EnumsListed()
    {
        var source = """
            §M{m001:TestModule}
            §EN{e001:Color}
            §EM{Red}
            §EM{Green}
            §EM{Blue}
            §/EN{e001}
            §EN{e002:Size}
            §EM{Small}
            §EM{Medium}
            §EM{Large}
            §/EN{e002}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);

        Assert.NotNull(ast);
        Assert.Equal(2, ast.Enums.Count);
        Assert.Contains(ast.Enums, e => e.Name == "Color");
        Assert.Contains(ast.Enums, e => e.Name == "Size");
    }

    [Fact]
    public void DocumentSymbols_EnumMembersListed()
    {
        var source = """
            §M{m001:TestModule}
            §EN{e001:Status}
            §EM{Pending}
            §EM{Active}
            §EM{Complete}
            §/EN{e001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);

        Assert.NotNull(ast);
        Assert.Single(ast.Enums);
        var enumDef = ast.Enums[0];
        Assert.Equal("Status", enumDef.Name);
        Assert.Equal(3, enumDef.Members.Count);
        Assert.Contains(enumDef.Members, m => m.Name == "Pending");
        Assert.Contains(enumDef.Members, m => m.Name == "Active");
        Assert.Contains(enumDef.Members, m => m.Name == "Complete");
    }

    [Fact]
    public void DocumentSymbols_DelegatesListed()
    {
        var source = """
            §M{m001:TestModule}
            §DEL{d001:Action}
            §O{void}
            §/DEL{d001}
            §DEL{d002:Func}
            §I{i32:x}
            §O{i32}
            §/DEL{d002}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);

        Assert.NotNull(ast);
        Assert.Equal(2, ast.Delegates.Count);
        Assert.Contains(ast.Delegates, d => d.Name == "Action");
        Assert.Contains(ast.Delegates, d => d.Name == "Func");
    }

    [Fact]
    public void DocumentSymbols_MixedTypesListed()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Helper}
            §R 0
            §/F{f001}
            §CL{c001:MyClass}
            §/CL{c001}
            §IFACE{i001:IMyInterface}
            §/IFACE{i001}
            §EN{e001:MyEnum}
            §EM{Value}
            §/EN{e001}
            §DEL{d001:MyDelegate}
            §O{void}
            §/DEL{d001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);

        Assert.NotNull(ast);
        Assert.Single(ast.Functions);
        Assert.Single(ast.Classes);
        Assert.Single(ast.Interfaces);
        Assert.Single(ast.Enums);
        Assert.Single(ast.Delegates);
    }

    [Fact]
    public void DocumentSymbols_FunctionParameters_Counted()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Calculate}
            §I{i32:a}
            §I{i32:b}
            §I{str:label}
            §O{i32}
            §R a + b
            §/F{f001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);

        Assert.NotNull(ast);
        var func = ast.Functions[0];
        Assert.Equal("Calculate", func.Name);
        Assert.Equal(3, func.Parameters.Count);
    }

    [Fact]
    public void DocumentSymbols_ClassProperties_Listed()
    {
        var source = """
            §M{m001:TestModule}
            §CL{c001:Person}
            §PROP{p001:Name:str:pub}
            §GET
            §R ""
            §/GET
            §/PROP{p001}
            §PROP{p002:Age:i32:pub}
            §GET
            §R 0
            §/GET
            §SET
            §/SET
            §/PROP{p002}
            §/CL{c001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);

        Assert.NotNull(ast);
        var cls = ast.Classes[0];
        Assert.Equal(2, cls.Properties.Count);
        Assert.Contains(cls.Properties, p => p.Name == "Name");
        Assert.Contains(cls.Properties, p => p.Name == "Age");
    }
}
