using Calor.LanguageServer.Handlers;
using Calor.LanguageServer.Tests.Helpers;
using Xunit;

namespace Calor.LanguageServer.Tests.Handlers;

public class ReferencesHandlerTests
{
    [Fact]
    public void ReferenceCollector_FindsVariableReferences()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Test:pub}
            §O{i32}
            §B{x} 10
            §B{y} x
            §R (+ x y)
            §/F{f001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var collector = new ReferenceCollector("x", includeDeclaration: true);
        collector.Visit(ast);

        // Should find: declaration (§B{x}), use in y = x, and use in return
        Assert.True(collector.References.Count >= 2);
    }

    [Fact]
    public void ReferenceCollector_FindsFunctionReferences()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Add:pub}
            §I{i32:a}
            §I{i32:b}
            §O{i32}
            §R (+ a b)
            §/F{f001}
            §F{f002:Test:pub}
            §O{i32}
            §R §C{Add} §A 1 §A 2 §/C
            §/F{f002}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var collector = new ReferenceCollector("Add", includeDeclaration: true);
        collector.Visit(ast);

        // Should find at least: declaration and call
        Assert.True(collector.References.Count >= 1);
    }

    [Fact]
    public void ReferenceCollector_FindsParameterReferences()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Double:pub}
            §I{i32:value}
            §O{i32}
            §R (* value 2)
            §/F{f001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var collector = new ReferenceCollector("value", includeDeclaration: true);
        collector.Visit(ast);

        // Should find: parameter declaration and use in return
        Assert.True(collector.References.Count >= 1);
    }

    [Fact]
    public void ReferenceCollector_ExcludesDeclaration_WhenFlagIsFalse()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Test:pub}
            §O{i32}
            §B{x} 10
            §R x
            §/F{f001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var collectorWithDecl = new ReferenceCollector("x", includeDeclaration: true);
        collectorWithDecl.Visit(ast);

        var collectorWithoutDecl = new ReferenceCollector("x", includeDeclaration: false);
        collectorWithoutDecl.Visit(ast);

        // Without declaration should have fewer or equal references
        Assert.True(collectorWithoutDecl.References.Count <= collectorWithDecl.References.Count);
    }

    [Fact]
    public void ReferenceCollector_FindsClassReferences()
    {
        var source = """
            §M{m001:TestModule}
            §CL{c001:Person}
            §FLD{str:name}
            §/CL{c001}
            §F{f001:Test:pub}
            §O{Person}
            §R §NEW{Person} §/NEW
            §/F{f001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var collector = new ReferenceCollector("Person", includeDeclaration: true);
        collector.Visit(ast);

        // Should find: class declaration and new expression
        Assert.True(collector.References.Count >= 1);
    }

    [Fact]
    public void ReferenceCollector_FindsFieldReferences()
    {
        var source = """
            §M{m001:TestModule}
            §CL{c001:Counter}
            §FLD{i32:count:priv}
            §MT{m001:Increment:pub}
            §O{void}
            §ASSIGN count (+ count 1)
            §/MT{m001}
            §/CL{c001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var collector = new ReferenceCollector("count", includeDeclaration: true);
        collector.Visit(ast);

        // Should find: field declaration and usages
        Assert.True(collector.References.Count >= 1);
    }

    [Fact]
    public void ReferenceCollector_FindsEnumReferences()
    {
        var source = """
            §M{m001:TestModule}
            §EN{e001:Color}
            Red
            Green
            Blue
            §/EN{e001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var collector = new ReferenceCollector("Color", includeDeclaration: true);
        collector.Visit(ast);

        // Should find: enum declaration
        Assert.True(collector.References.Count >= 1);
    }

    [Fact]
    public void ReferenceCollector_FindsLoopVariableReferences()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Sum:pub}
            §O{i32}
            §B{total} 0
            §L{for1:i:0:10:1}
            §ASSIGN total (+ total i)
            §/L{for1}
            §R total
            §/F{f001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var collector = new ReferenceCollector("i", includeDeclaration: true);
        collector.Visit(ast);

        // Should find: loop variable and usage inside loop
        Assert.True(collector.References.Count >= 1);
    }

    [Fact]
    public void ReferenceCollector_FindsLambdaParameterReferences()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Test:pub}
            §O{i32}
            §B{fn} §LAM
            §I{i32:x}
            §R (* x 2)
            §/LAM
            §R 0
            §/F{f001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var collector = new ReferenceCollector("x", includeDeclaration: true);
        collector.Visit(ast);

        // Should find references inside lambda
        Assert.True(collector.References.Count >= 1);
    }

    [Fact]
    public void ReferenceCollector_NoReferences_ForUnusedSymbol()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Test:pub}
            §O{i32}
            §R 42
            §/F{f001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var collector = new ReferenceCollector("nonexistent", includeDeclaration: true);
        collector.Visit(ast);

        Assert.Empty(collector.References);
    }
}
