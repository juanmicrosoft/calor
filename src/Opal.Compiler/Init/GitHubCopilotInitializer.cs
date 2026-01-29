namespace Opal.Compiler.Init;

/// <summary>
/// Stub initializer for GitHub Copilot.
/// </summary>
public class GitHubCopilotInitializer : IAiInitializer
{
    public string AgentName => "GitHub Copilot";

    public Task<InitResult> InitializeAsync(string targetDirectory, bool force)
    {
        return Task.FromResult(InitResult.NotImplemented(AgentName));
    }
}
