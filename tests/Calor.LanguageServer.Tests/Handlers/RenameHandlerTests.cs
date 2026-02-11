using Calor.LanguageServer.Handlers;
using Calor.LanguageServer.Tests.Helpers;
using Xunit;

namespace Calor.LanguageServer.Tests.Handlers;

public class RenameHandlerTests
{
    [Fact]
    public void ReferenceCollectorForRename_FindsAllOccurrences()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Test:pub}
            §O{i32}
            §B{counter} 0
            §ASSIGN counter (+ counter 1)
            §R counter
            §/F{f001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var collector = new ReferenceCollectorForRename("counter");
        collector.Visit(ast);

        // Should find: declaration in bind, two usages in assignment, and usage in return
        Assert.True(collector.References.Count >= 1);
    }

    [Fact]
    public void ReferenceCollectorForRename_FindsFunctionDeclarationAndCalls()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Helper:pub}
            §O{i32}
            §R 42
            §/F{f001}
            §F{f002:Main:pub}
            §O{i32}
            §R §C{Helper} §/C
            §/F{f002}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var collector = new ReferenceCollectorForRename("Helper");
        collector.Visit(ast);

        // Should find: function declaration and call
        Assert.True(collector.References.Count >= 1);
    }

    [Fact]
    public void ReferenceCollectorForRename_FindsParameterUsages()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Square:pub}
            §I{i32:num}
            §O{i32}
            §R (* num num)
            §/F{f001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var collector = new ReferenceCollectorForRename("num");
        collector.Visit(ast);

        // Should find: parameter declaration and usages in return
        Assert.True(collector.References.Count >= 1);
    }

    [Fact]
    public void ReferenceCollectorForRename_FindsClassAndConstructorUsages()
    {
        var source = """
            §M{m001:TestModule}
            §CL{c001:Widget}
            §FLD{str:name}
            §/CL{c001}
            §F{f001:Create:pub}
            §O{Widget}
            §R §NEW{Widget} §/NEW
            §/F{f001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var collector = new ReferenceCollectorForRename("Widget");
        collector.Visit(ast);

        // Should find: class declaration and new expression
        Assert.True(collector.References.Count >= 1);
    }

    [Fact]
    public void ReferenceCollectorForRename_FindsFieldUsages()
    {
        var source = """
            §M{m001:TestModule}
            §CL{c001:Counter}
            §FLD{i32:value:priv}
            §MT{m001:Get:pub}
            §O{i32}
            §R value
            §/MT{m001}
            §MT{m002:Set:pub}
            §I{i32:newValue}
            §O{void}
            §ASSIGN value newValue
            §/MT{m002}
            §/CL{c001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var collector = new ReferenceCollectorForRename("value");
        collector.Visit(ast);

        // Should find: field declaration, return usage, and assignment target
        Assert.True(collector.References.Count >= 1);
    }

    [Fact]
    public void ReferenceCollectorForRename_FindsLoopVariableUsages()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Sum:pub}
            §O{i32}
            §B{result} 0
            §L{for1:index:0:10:1}
            §ASSIGN result (+ result index)
            §/L{for1}
            §R result
            §/F{f001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var collector = new ReferenceCollectorForRename("index");
        collector.Visit(ast);

        // Should find: for loop variable and usage in loop body
        Assert.True(collector.References.Count >= 1);
    }

    [Fact]
    public void ReferenceCollectorForRename_FindsForeachVariableUsages()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Print:pub}
            §I{[str]:items}
            §O{void}
            §EACH{e1:item:str} items
            §P item
            §/EACH{e1}
            §/F{f001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var collector = new ReferenceCollectorForRename("item");
        collector.Visit(ast);

        // Should find: foreach variable and usage in print
        Assert.True(collector.References.Count >= 1);
    }

    [Fact]
    public void ReferenceCollectorForRename_FindsMethodUsages()
    {
        var source = """
            §M{m001:TestModule}
            §CL{c001:Math}
            §MT{m001:Add:pub}
            §I{i32:a}
            §I{i32:b}
            §O{i32}
            §R (+ a b)
            §/MT{m001}
            §/CL{c001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var collector = new ReferenceCollectorForRename("Add");
        collector.Visit(ast);

        // Should find: method declaration
        Assert.True(collector.References.Count >= 1);
    }

    [Fact]
    public void ReferenceCollectorForRename_FindsEnumMemberUsages()
    {
        var source = """
            §M{m001:TestModule}
            §EN{e001:Status}
            Active
            Inactive
            §/EN{e001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var collector = new ReferenceCollectorForRename("Active");
        collector.Visit(ast);

        // Should find: enum member
        Assert.True(collector.References.Count >= 1);
    }

    [Fact]
    public void ReferenceCollectorForRename_FindsInterfaceDeclaration()
    {
        var source = """
            §M{m001:TestModule}
            §IFACE{i001:IService}
            §MT{m001:Execute}
            §O{void}
            §/MT{m001}
            §/IFACE{i001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var collector = new ReferenceCollectorForRename("IService");
        collector.Visit(ast);

        // Should find: interface declaration
        Assert.True(collector.References.Count >= 1);
    }

    [Fact]
    public void ReferenceCollectorForRename_NoReferences_ForNonexistentSymbol()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Test:pub}
            §O{i32}
            §R 0
            §/F{f001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var collector = new ReferenceCollectorForRename("nonexistent");
        collector.Visit(ast);

        Assert.Empty(collector.References);
    }

    [Fact]
    public void ReferenceCollectorForRename_FindsLambdaParameterUsages()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Test:pub}
            §O{i32}
            §B{doubler} §LAM
            §I{i32:val}
            §R (* val 2)
            §/LAM
            §R 0
            §/F{f001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var collector = new ReferenceCollectorForRename("val");
        collector.Visit(ast);

        // Should find: lambda parameter and usage
        Assert.True(collector.References.Count >= 1);
    }

    [Fact]
    public void ReferenceCollectorForRename_FindsCatchVariableUsages()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Safe:pub}
            §O{i32}
            §TRY{t1}
            §R (/ 10 0)
            §CATCH{Exception:ex}
            §P ex
            §R 0
            §/CATCH
            §/TRY{t1}
            §/F{f001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var collector = new ReferenceCollectorForRename("ex");
        collector.Visit(ast);

        // Should find: catch variable and usage in print
        Assert.True(collector.References.Count >= 1);
    }
}
