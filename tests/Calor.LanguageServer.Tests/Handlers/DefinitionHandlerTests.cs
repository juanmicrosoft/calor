using Calor.LanguageServer.Handlers;
using Calor.LanguageServer.State;
using Calor.LanguageServer.Tests.Helpers;
using Calor.LanguageServer.Utilities;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
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
              §F{f002:Second}
                §R 2
              §F{f003:Third}
                §R 3
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
              §CL{c002:Inner}
                §FLD{str:name}
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var def = SymbolFinder.FindDefinition(ast, "Inner");

        Assert.NotNull(def);
        Assert.IsType<Calor.Compiler.Ast.ClassDefinitionNode>(def);
    }

    [Fact]
    public async Task DefinitionUsesSymbolIdAcrossClosedWorkspaceFilesAsync()
    {
        var root = CreateWorkspaceDirectory();
        try
        {
            const string definition = """
                §M{defs:Collision}
                  §CL{worker1:Worker:pub:partial}
                    §MT{pickInt:Pick:pub} (i32:value) -> i32
                      §R value
                    §MT{pickText:Pick:pub} (str:value) -> i32
                      §R INT:2
                """;
            const string use = """
                §M{use:Collision}
                  §CL{worker2:Worker:pub:partial}
                    §MT{call:Call:pub} () -> i32
                      §R §C{Pick} §A INT:1 §/C
                """;
            var definitionPath = Path.Combine(root, "definition.calr");
            var usePath = Path.Combine(root, "use.calr");
            File.WriteAllText(definitionPath, definition);
            File.WriteAllText(usePath, use);
            var workspace = new WorkspaceState(root);
            var useUri = DocumentUri.FromFileSystemPath(usePath);
            workspace.GetOrCreate(useUri, use, version: 2);
            var offset = use.IndexOf("Pick", StringComparison.Ordinal);
            var (line, column) = LspTestHarness.GetLineColumn(use, offset);

            var result = await new DefinitionHandler(workspace).Handle(
                new DefinitionParams
                {
                    TextDocument = new TextDocumentIdentifier(useUri),
                    Position = new Position(line - 1, column - 1),
                },
                CancellationToken.None);

            Assert.NotNull(result);
            var link = Assert.Single(result);
            Assert.NotNull(link.Location);
            var location = link.Location!;
            Assert.Equal(definitionPath, location.Uri.ToUri().LocalPath);
            Assert.Equal(
                definition.IndexOf("Pick:pub", StringComparison.Ordinal),
                PositionConverter.ToOffset(location.Range.Start, definition));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DefinitionDoesNotResolveWhitespaceOrEndAdjacentCursorAsync()
    {
        const string source = """
            §M{m001:TestModule}
              §F{f001:Compute:pub} () -> i32
                §R INT:1
            """;
        var uri = DocumentUri.From("file:///definition-cursor.calr");
        var workspace = new WorkspaceState();
        workspace.GetOrCreate(uri, source);
        var identifier = source.IndexOf("Compute", StringComparison.Ordinal);
        var handler = new DefinitionHandler(workspace);

        foreach (var offset in new[] { identifier - 1, identifier + "Compute".Length })
        {
            var (line, column) = LspTestHarness.GetLineColumn(source, offset);
            var result = await handler.Handle(
                new DefinitionParams
                {
                    TextDocument = new TextDocumentIdentifier(uri),
                    Position = new Position(line - 1, column - 1),
                },
                CancellationToken.None);
            Assert.Null(result);
        }
    }

    private static string CreateWorkspaceDirectory()
    {
        var directory = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(directory, "Calor.sln")))
            directory = Directory.GetParent(directory)!.FullName;

        var root = Path.Combine(
            directory,
            "artifacts",
            "lsp-refactoring-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
