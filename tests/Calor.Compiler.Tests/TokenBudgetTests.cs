using System.Text.Json;
using Calor.Compiler.Init;
using Calor.Compiler.Mcp;
using Microsoft.ML.Tokenizers;
using Xunit;
using Xunit.Abstractions;

namespace Calor.Compiler.Tests;

/// <summary>
/// Offline proxy tokenizer for the agent-surface token budgets (#1603, #1613, #1614).
///
/// <para>This is o200k_base (<c>TiktokenTokenizer.CreateForModel("gpt-4o")</c> from
/// Microsoft.ML.Tokenizers), a <b>proxy</b>. The #1597 measurements used Anthropic's
/// <c>count_tokens</c> API, which CI cannot call (it needs network access and a key).
/// o200k counts are not Claude counts; they move together closely enough to catch growth,
/// which is what these budgets are for. Every budget below is in o200k tokens.</para>
/// </summary>
internal static class ProxyTokenizer
{
    private static readonly Lazy<Tokenizer> Instance =
        new(() => TiktokenTokenizer.CreateForModel("gpt-4o"));

    public static int Count(string text) => Instance.Value.CountTokens(text);
}

/// <summary>
/// Token budgets for the always-loaded and session-start agent surfaces.
///
/// <para>Budgets were set on 2026-11-20 from o200k measurements (see
/// <see cref="ProxyTokenizer"/>). To change one, edit its constant deliberately and say why
/// in the comment next to it (and in the PR): a budget that moves silently is not a budget.
/// Ratchets (full reference, MCP surface) were set to the next multiple of 250 above the
/// measurement; lower them when a surface shrinks.</para>
/// </summary>
public sealed class TokenBudgetTests
{
    private static readonly string[] InitTemplates =
        ["CLAUDE.md.template", "AGENTS.md.template", "GEMINI.md.template", "copilot-instructions.md.template"];

    /// <summary>
    /// #1603: each <c>calor init</c> template plus the MCP server instructions, which an agent
    /// with the MCP server loads together on every turn, must fit 600 tokens. 2026-11-20:
    /// 511 + 442 = 953 before trimming; after trimming both to a pointer plus the rules that
    /// prevent the most common errors, 317 + 275 = 592 for each of the four templates.
    /// </summary>
    private const int TemplatePlusInstructionsBudget = 600;

    /// <summary>
    /// #1603: the primer (<c>calor://primer</c>) must stay between 800 and 1,600 tokens.
    /// 2026-11-20: 1,728 before trimming, 1,569 after.
    /// </summary>
    private const int PrimerMinimum = 800;
    private const int PrimerBudget = 1600;

    /// <summary>
    /// #1603: no-growth ratchet on the full generated reference
    /// (<c>calor://reference</c>). 2026-11-20: 16,750, so the ratchet is 17,000.
    /// #1597 estimated a 6–7K target; it is <b>not met</b>. Reaching it needs the
    /// conversion-limit material moved out of the reference into <c>calor_help</c>
    /// <c>action=features</c> (follow-up). Lower this ratchet as the reference shrinks.
    /// </summary>
    private const int FullReferenceRatchet = 17000;

    /// <summary>
    /// #1614: always-on MCP surface = the serialized <c>tools/list</c> result (names,
    /// descriptions, input schemas, annotations) plus the server instructions.
    /// 2026-11-20: 7,900 (19 tools: tools/list 7,625 + instructions 275; the instructions were
    /// 442 before the #1603 trim), so the ratchet is 8,000.
    /// </summary>
    private const int McpSurfaceRatchet = 8000;

    /// <summary>
    /// #1614: per-tool ceiling on one tool description. 2026-11-20: the longest description
    /// (calor_batch) is 167 tokens, so the ceiling is 200. The next longest are calor_file_write
    /// (116) and calor_query (113).
    /// </summary>
    private const int ToolDescriptionCeiling = 200;

    private readonly ITestOutputHelper _output;

    public TokenBudgetTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void InitTemplatePlusServerInstructions_FitsBudget()
    {
        var instructions = new McpMessageHandler(verbose: false).GetServerInstructionsForTest();
        var instructionTokens = ProxyTokenizer.Count(instructions);
        var over = new List<string>();
        foreach (var name in InitTemplates)
        {
            var templateTokens = ProxyTokenizer.Count(EmbeddedResourceHelper.ReadTemplate(name));
            var total = templateTokens + instructionTokens;
            _output.WriteLine($"{name}: {templateTokens} + instructions {instructionTokens} = {total}");
            if (total > TemplatePlusInstructionsBudget)
                over.Add($"{name} ({templateTokens}) + server instructions ({instructionTokens}) = {total}");
        }

        Assert.True(over.Count == 0,
            $"Template plus MCP server instructions exceeds the #1603 budget of {TemplatePlusInstructionsBudget} " +
            $"o200k tokens: {string.Join("; ", over)}. Trim agentInstructions in " +
            "src/Calor.Compiler/Resources/Teaching/reference.json and run 'calor self-check docs --fix'.");
    }

    [Fact]
    public void Primer_StaysWithinItsBand()
    {
        var tokens = ProxyTokenizer.Count(McpMessageHandler.GetPrimerContentPublic());
        _output.WriteLine($"primer: {tokens}");

        Assert.True(tokens is >= PrimerMinimum and <= PrimerBudget,
            $"calor://primer measures {tokens} o200k tokens; #1603 requires {PrimerMinimum}..{PrimerBudget}. " +
            "Edit the primer section of src/Calor.Compiler/Resources/Teaching/reference.json.");
    }

    [Fact]
    public void FullReference_DoesNotGrow()
    {
        var tokens = ProxyTokenizer.Count(
            EmbeddedResourceHelper.ReadResource("Calor.Compiler.Resources.current-language-reference.md"));
        _output.WriteLine($"full reference: {tokens}");

        Assert.True(tokens <= FullReferenceRatchet,
            $"The full language reference measures {tokens} o200k tokens; the #1603 no-growth ratchet is " +
            $"{FullReferenceRatchet}. Move material to calor_help instead of growing the reference.");
    }

    [Fact]
    public async Task AlwaysOnMcpSurface_FitsRatchet()
    {
        var handler = new McpMessageHandler(verbose: false);
        var toolsJson = await ToolsListJsonAsync(handler);
        var toolTokens = ProxyTokenizer.Count(toolsJson);
        var instructionTokens = ProxyTokenizer.Count(handler.GetServerInstructionsForTest());
        var total = toolTokens + instructionTokens;
        _output.WriteLine($"tools/list: {toolTokens}, instructions: {instructionTokens}, total: {total}");

        Assert.True(total <= McpSurfaceRatchet,
            $"The always-on MCP surface measures {total} o200k tokens (tools/list {toolTokens} + " +
            $"server instructions {instructionTokens}); the #1614 ratchet is {McpSurfaceRatchet}. " +
            "Merge or shorten tools rather than raising the ratchet.");
    }

    [Fact]
    public async Task EveryToolDescription_FitsPerToolCeiling()
    {
        using var tools = JsonDocument.Parse(await ToolsListJsonAsync(new McpMessageHandler(verbose: false)));
        var over = new List<string>();
        var count = 0;
        foreach (var tool in tools.RootElement.GetProperty("tools").EnumerateArray())
        {
            count++;
            var name = tool.GetProperty("name").GetString()!;
            var tokens = ProxyTokenizer.Count(tool.GetProperty("description").GetString() ?? "");
            _output.WriteLine($"{name}: {tokens}");
            if (tokens > ToolDescriptionCeiling)
                over.Add($"{name} ({tokens})");
        }

        Assert.True(count > 0, "tools/list returned no tools.");
        Assert.True(over.Count == 0,
            $"Tool descriptions over the #1614 per-tool ceiling of {ToolDescriptionCeiling} o200k tokens: " +
            string.Join(", ", over));
    }

    /// <summary>The exact <c>tools/list</c> result a client receives, serialized as the server writes it.</summary>
    private static async Task<string> ToolsListJsonAsync(McpMessageHandler handler)
    {
        var response = await handler.HandleRequestAsync(new JsonRpcRequest
        {
            Id = JsonSerializer.SerializeToElement(1),
            Method = "tools/list"
        });
        Assert.NotNull(response);
        Assert.Null(response.Error);
        return JsonSerializer.Serialize(response.Result, McpJsonOptions.Default);
    }
}
