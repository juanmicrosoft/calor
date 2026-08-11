using Xunit;

namespace Calor.Compiler.Tests.Mcp;

/// <summary>
/// #897: the MCP server tests fail on 2-core CI runners with ~31s timeouts under
/// parallel test collections while passing locally and in serial runs — the documented
/// mitigation is running them serialized. Remove only with #897's root cause fixed.
/// </summary>
[CollectionDefinition("McpSerial", DisableParallelization = true)]
public class McpSerialCollection : ICollectionFixture<McpMemoryHeadroomFixture>
{
}

/// <summary>
/// #897 second failure mode (first seen on the B8 CI run): the MCP server's
/// memory-pressure breaker trips when the test HOST process is already holding the
/// rest of the suite's garbage (observed: 8.5 GB used vs the ~8 GB threshold — the
/// breaker measures the whole process, and thousands of preceding tests leave
/// collectable heap behind). Forcing a full compacting collection before the MCP
/// collection starts removes that inherited pressure deterministically instead of
/// hoping the GC ran recently.
/// </summary>
public sealed class McpMemoryHeadroomFixture
{
    public McpMemoryHeadroomFixture()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
    }
}
