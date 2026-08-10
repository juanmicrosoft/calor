using Xunit;

namespace Calor.Compiler.Tests.Mcp;

/// <summary>
/// #897: the MCP server tests fail on 2-core CI runners with ~31s timeouts under
/// parallel test collections while passing locally and in serial runs — the documented
/// mitigation is running them serialized. Remove only with #897's root cause fixed.
/// </summary>
[CollectionDefinition("McpSerial", DisableParallelization = true)]
public class McpSerialCollection
{
}
