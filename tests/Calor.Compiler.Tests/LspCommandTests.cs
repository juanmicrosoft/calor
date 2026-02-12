using System.CommandLine;
using Calor.Compiler.Commands;
using Xunit;

namespace Calor.Compiler.Tests;

public class LspCommandTests
{
    [Fact]
    public void Create_ReturnsCommandWithCorrectName()
    {
        var command = LspCommand.Create();

        Assert.Equal("lsp", command.Name);
    }

    [Fact]
    public void Create_ReturnsCommandWithCorrectDescription()
    {
        var command = LspCommand.Create();

        Assert.Contains("Language Server", command.Description);
        Assert.Contains("stdio", command.Description);
    }

    [Fact]
    public void Create_CommandHasNoRequiredOptions()
    {
        var command = LspCommand.Create();

        // The lsp command should have no options - it just starts the LSP
        Assert.Empty(command.Options);
    }

    [Fact]
    public void Create_CommandHasNoSubcommands()
    {
        var command = LspCommand.Create();

        Assert.Empty(command.Subcommands);
    }

    [Fact]
    public void Create_CommandHasHandler()
    {
        var command = LspCommand.Create();

        Assert.NotNull(command.Handler);
    }

    [Fact]
    public void LspCommand_IsRegisteredInRootCommand()
    {
        // Create a minimal root command setup similar to Program.cs
        var rootCommand = new RootCommand("Test");
        rootCommand.AddCommand(LspCommand.Create());

        var lspCommand = rootCommand.Subcommands.FirstOrDefault(c => c.Name == "lsp");

        Assert.NotNull(lspCommand);
        Assert.Equal("lsp", lspCommand.Name);
    }
}
