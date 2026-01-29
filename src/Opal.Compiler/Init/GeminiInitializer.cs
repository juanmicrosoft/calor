namespace Opal.Compiler.Init;

/// <summary>
/// Stub initializer for Google Gemini.
/// </summary>
public class GeminiInitializer : IAiInitializer
{
    public string AgentName => "Google Gemini";

    public Task<InitResult> InitializeAsync(string targetDirectory, bool force)
    {
        return Task.FromResult(InitResult.NotImplemented(AgentName));
    }
}
