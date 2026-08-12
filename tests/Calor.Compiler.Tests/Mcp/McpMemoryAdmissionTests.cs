using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Calor.Compiler.Mcp;
using Xunit;

namespace Calor.Compiler.Tests.Mcp;

/// <summary>
/// Pins for #897 — six MCP tests that failed only on CI, each after ~31s, with
/// "Server under memory pressure".
///
/// The cause was not flakiness. The heavy-tool admission gate measures
/// <c>Process.WorkingSet64</c>, which is the whole process, and it ran
/// unconditionally. Under `calor mcp` that is fair — the server owns its process.
/// Inside the xUnit host it was not: 6,810 tests, Roslyn and Z3 in memory, over the
/// 50%-of-RAM threshold on a Linux runner and under it on a bigger dev machine. The
/// gate then charged the test host's heap to the next compile, waited its full 30s,
/// and refused.
///
/// So these pin all three halves of the rule, because any one alone is passable by a
/// wrong fix: embedded does not gate, server does gate, and the gate still refuses
/// when it is enabled and the ceiling is genuinely exceeded.
/// </summary>
public class McpMemoryAdmissionTests
{
    // A compile request — CompileTool is one of the four heavy tools the gate covers.
    private static JsonRpcRequest CompileRequest() => new()
    {
        Id = JsonDocument.Parse("1").RootElement,
        Method = "tools/call",
        Params = JsonDocument.Parse("""
            {
                "name": "calor_compile",
                "arguments": {
                    "source": "§M{m001:Test}\n§F{f001:Add:pub}\n§I{i32:a}\n§I{i32:b}\n§O{i32}\n§R (+ a b)\n\n"
                }
            }
            """).RootElement
    };

    private static string ResultText(JsonRpcResponse response) =>
        JsonSerializer.Serialize(response.Result, McpJsonOptions.Default);

    [Fact]
    public async Task EmbeddedHandler_DoesNotGateHeavyToolsOnProcessMemory()
    {
        // The public constructor is the embedded path — the one the six #897 tests
        // use. A threshold of one byte is exceeded by any process that exists, so if
        // the gate applied here at all, this call would wait and then be refused.
        var handler = new McpMessageHandler();

        var stopwatch = Stopwatch.StartNew();
        var response = await handler.HandleRequestAsync(CompileRequest());
        stopwatch.Stop();

        Assert.NotNull(response);
        var text = ResultText(response!);
        Assert.DoesNotContain("under memory pressure", text);
        Assert.Contains("success", text);

        // The ~31s signature of #897, asserted directly: a gated call spends the full
        // wait budget before refusing, an ungated one cannot.
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(10),
            $"compile took {stopwatch.Elapsed.TotalSeconds:F1}s — the #897 signature is a "
                + "call that spends the memory-pressure wait budget before returning");

        // Asserted last so the behaviour above is what discriminates, not this.
        Assert.False(handler.MemoryAdmission.Enabled);
    }

    [Fact]
    public void StdioServer_DoesGateHeavyToolsOnProcessMemory()
    {
        // Without this, "disable the gate everywhere" would pass the test above and
        // silently drop the protection `calor mcp` actually relies on.
        using var output = new MemoryStream();
        var server = new McpServer(new StringReader(string.Empty), output);

        Assert.True(server.Handler.MemoryAdmission.Enabled);
        Assert.Equal(TimeSpan.FromSeconds(30), server.Handler.MemoryAdmission.MaxWait);
    }

    [Fact]
    public async Task EnabledPolicy_StillRefusesHeavyToolsOverTheCeiling()
    {
        // And without this, "make the policy a no-op" would pass both of the above.
        // One byte is under any real working set, so the ceiling is genuinely
        // exceeded; the wait is shortened so the pin costs a fraction of a second.
        var policy = new McpMemoryAdmissionPolicy(
            Enabled: true,
            ThresholdBytes: 1,
            MaxWait: TimeSpan.FromMilliseconds(200),
            PollInterval: TimeSpan.FromMilliseconds(50));
        var handler = new McpMessageHandler(policy);

        var response = await handler.HandleRequestAsync(CompileRequest());

        Assert.NotNull(response);
        Assert.Contains("under memory pressure", ResultText(response!));
    }

    [Fact]
    public void EnvironmentPolicy_HonoursAnExplicitCeiling()
    {
        // The override advertised in the refusal message has to work, and reading it
        // per-construction rather than once per process is what makes it testable.
        var previous = Environment.GetEnvironmentVariable("CALOR_MCP_MAX_MEMORY_MB");
        try
        {
            Environment.SetEnvironmentVariable("CALOR_MCP_MAX_MEMORY_MB", "1234");
            var policy = McpMemoryAdmissionPolicy.FromEnvironment();

            Assert.True(policy.Enabled);
            Assert.Equal(1234L * 1024 * 1024, policy.ThresholdBytes);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CALOR_MCP_MAX_MEMORY_MB", previous);
        }
    }
}
