namespace Calor.Compiler.Init;

/// <summary>
/// Factory for creating AI-specific initializers.
/// </summary>
public static class AiInitializerFactory
{
    /// <summary>
    /// Supported AI agent types.
    /// </summary>
    public static readonly string[] SupportedAgents = { "claude", "codex", "gemini", "github" };

    /// <summary>
    /// Creates an initializer for the specified AI agent.
    /// </summary>
    /// <param name="agentType">The AI agent type (e.g., "claude", "codex", "gemini", "github").</param>
    /// <returns>An initializer for the specified agent.</returns>
    /// <exception cref="ArgumentException">If the agent type is not recognized.</exception>
    public static IAiInitializer Create(string agentType)
    {
        return agentType.ToLowerInvariant() switch
        {
            "claude" => new ClaudeInitializer(),
            "codex" => new CodexInitializer(),
            "gemini" => new GeminiInitializer(),
            "github" => new GitHubCopilotInitializer(),
            _ => throw new ArgumentException(
                $"Unknown AI agent type: '{agentType}'. Supported types: {string.Join(", ", SupportedAgents)}",
                nameof(agentType))
        };
    }

    /// <summary>
    /// Checks if the specified agent type is supported.
    /// </summary>
    public static bool IsSupported(string agentType)
    {
        return SupportedAgents.Contains(agentType.ToLowerInvariant());
    }
}
