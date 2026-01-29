namespace Opal.Compiler.Init;

/// <summary>
/// Stub initializer for OpenAI Codex.
/// </summary>
public class CodexInitializer : IAiInitializer
{
    public string AgentName => "OpenAI Codex";

    public Task<InitResult> InitializeAsync(string targetDirectory, bool force)
    {
        return Task.FromResult(InitResult.NotImplemented(AgentName));
    }
}
