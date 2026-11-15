using Calor.LanguageServer.Handlers;
using Calor.LanguageServer.State;
using Calor.LanguageServer.Tests.Helpers;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Xunit;

namespace Calor.LanguageServer.Tests.Handlers;

public class WorkspaceSymbolHandlerTests
{
    [Fact]
    public async Task Handle_EmptyQuery_ReturnsAllSymbolsAsync()
    {
        var source = """
            module TestModule TestModule block
              func Add ()
                !
                return 0

              func Subtract ()
                !
                return 0
            """;

        var workspace = CreateWorkspace(source);
        var handler = new WorkspaceSymbolHandler(workspace);

        var result = await handler.Handle(new WorkspaceSymbolParams { Query = "" }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result.Count() >= 2);
    }

    [Fact]
    public async Task Handle_SpecificQuery_FiltersResultsAsync()
    {
        var source = """
            module TestModule TestModule block
              func Calculate ()
                !
                return 0

              func Process ()
                !
                return 0
            """;

        var workspace = CreateWorkspace(source);
        var handler = new WorkspaceSymbolHandler(workspace);

        var result = await handler.Handle(new WorkspaceSymbolParams { Query = "Calc" }, CancellationToken.None);

        Assert.NotNull(result);
        var symbols = result.ToList();
        Assert.All(symbols, s => Assert.Contains("Calc", s.Name, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Handle_ClassQuery_ReturnsClassesAsync()
    {
        var source = """
            module TestModule TestModule block
              class PersonService internal
                field int:dummy private

              class OrderService internal
                field int:dummy private
            """;

        var workspace = CreateWorkspace(source);
        var handler = new WorkspaceSymbolHandler(workspace);

        var result = await handler.Handle(new WorkspaceSymbolParams { Query = "Service" }, CancellationToken.None);

        Assert.NotNull(result);
        var symbols = result.ToList();
        Assert.Equal(2, symbols.Count);
        Assert.All(symbols, s => Assert.Equal(SymbolKind.Class, s.Kind));
    }

    [Fact]
    public async Task Handle_MethodQuery_ReturnsMethodsAsync()
    {
        var source = """
            module TestModule TestModule block
              class Calculator internal
                func Add () → int
                  !
                  return 0

                func Multiply () → int
                  !
                  return 0
            """;

        var workspace = CreateWorkspace(source);
        var handler = new WorkspaceSymbolHandler(workspace);

        var result = await handler.Handle(new WorkspaceSymbolParams { Query = "Add" }, CancellationToken.None);

        Assert.NotNull(result);
        var symbols = result.ToList();
        Assert.Contains(symbols, s => s.Name == "Add");
    }

    [Fact]
    public async Task Handle_EnumQuery_ReturnsEnumsAsync()
    {
        var source = """
            module TestModule TestModule block
              enum StatusCode
                Ok = 200
                NotFound = 404
            """;

        var workspace = CreateWorkspace(source);
        var handler = new WorkspaceSymbolHandler(workspace);

        var result = await handler.Handle(new WorkspaceSymbolParams { Query = "Status" }, CancellationToken.None);

        Assert.NotNull(result);
        var symbols = result.ToList();
        Assert.Contains(symbols, s => s.Name == "StatusCode" && s.Kind == SymbolKind.Enum);
    }

    [Fact]
    public async Task Handle_EnumMemberQuery_ReturnsEnumMembersAsync()
    {
        var source = """
            module TestModule TestModule block
              enum Color
                Red
                Green
                Blue
            """;

        var workspace = CreateWorkspace(source);
        var handler = new WorkspaceSymbolHandler(workspace);

        var result = await handler.Handle(new WorkspaceSymbolParams { Query = "Red" }, CancellationToken.None);

        Assert.NotNull(result);
        var symbols = result.ToList();
        Assert.Contains(symbols, s => s.Name == "Red" && s.Kind == SymbolKind.EnumMember);
    }

    [Fact]
    public async Task Handle_InterfaceQuery_ReturnsInterfacesAsync()
    {
        var source = """
            module TestModule TestModule block
              interface IRepository
            """;

        var workspace = CreateWorkspace(source);
        var handler = new WorkspaceSymbolHandler(workspace);

        var result = await handler.Handle(new WorkspaceSymbolParams { Query = "Repository" }, CancellationToken.None);

        Assert.NotNull(result);
        var symbols = result.ToList();
        Assert.Contains(symbols, s => s.Name == "IRepository" && s.Kind == SymbolKind.Interface);
    }

    [Fact]
    public async Task Handle_FieldQuery_ReturnsFieldsAsync()
    {
        var source = """
            module TestModule TestModule block
              class Person internal
                field string:firstName private
                field string:lastName private
            """;

        var workspace = CreateWorkspace(source);
        var handler = new WorkspaceSymbolHandler(workspace);

        var result = await handler.Handle(new WorkspaceSymbolParams { Query = "Name" }, CancellationToken.None);

        Assert.NotNull(result);
        var symbols = result.ToList();
        Assert.True(symbols.Count >= 2);
        Assert.All(symbols.Where(s => s.Kind == SymbolKind.Field), s => Assert.Contains("Name", s.Name));
    }

    [Fact]
    public async Task Handle_NoMatches_ReturnsEmptyAsync()
    {
        var source = """
            module TestModule TestModule block
              func Test ()
                !
                return 0
            """;

        var workspace = CreateWorkspace(source);
        var handler = new WorkspaceSymbolHandler(workspace);

        var result = await handler.Handle(new WorkspaceSymbolParams { Query = "NonexistentSymbol" }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task Handle_CaseInsensitiveSearchAsync()
    {
        var source = """
            module TestModule TestModule block
              func CalculateTotal ()
                !
                return 0
            """;

        var workspace = CreateWorkspace(source);
        var handler = new WorkspaceSymbolHandler(workspace);

        var result = await handler.Handle(new WorkspaceSymbolParams { Query = "calculate" }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains(result, s => s.Name == "CalculateTotal");
    }

    [Fact]
    public async Task Handle_SymbolsIncludeContainerNameAsync()
    {
        var source = """
            module TestModule TestModule block
              class Calculator internal
                func Add () → int
                  !
                  return 0
            """;

        var workspace = CreateWorkspace(source);
        var handler = new WorkspaceSymbolHandler(workspace);

        var result = await handler.Handle(new WorkspaceSymbolParams { Query = "Add" }, CancellationToken.None);

        Assert.NotNull(result);
        var addSymbol = result.FirstOrDefault(s => s.Name == "Add");
        Assert.NotNull(addSymbol);
        Assert.NotNull(addSymbol.ContainerName);
        Assert.Contains("Calculator", addSymbol.ContainerName);
    }

    private static WorkspaceState CreateWorkspace(string source)
    {
        var workspace = new WorkspaceState();
        var uri = DocumentUri.From("file:///test.calr");
        workspace.GetOrCreate(uri, source);
        return workspace;
    }
}
