namespace Opal.Compiler.Init;

/// <summary>
/// Interface for AI-specific initialization of OPAL development environments.
/// </summary>
public interface IAiInitializer
{
    /// <summary>
    /// Gets the name of the AI agent this initializer supports.
    /// </summary>
    string AgentName { get; }

    /// <summary>
    /// Initializes the current directory for use with this AI agent.
    /// </summary>
    /// <param name="targetDirectory">The directory to initialize.</param>
    /// <param name="force">If true, overwrite existing files without prompting.</param>
    /// <returns>A result indicating success or failure with messages.</returns>
    Task<InitResult> InitializeAsync(string targetDirectory, bool force);
}

/// <summary>
/// Result of an initialization operation.
/// </summary>
public class InitResult
{
    public bool Success { get; init; }
    public List<string> Messages { get; init; } = new();
    public List<string> Warnings { get; init; } = new();
    public List<string> CreatedFiles { get; init; } = new();
    public List<string> UpdatedFiles { get; init; } = new();

    public static InitResult Succeeded(IEnumerable<string>? createdFiles = null, IEnumerable<string>? messages = null)
    {
        return new InitResult
        {
            Success = true,
            CreatedFiles = createdFiles?.ToList() ?? new(),
            Messages = messages?.ToList() ?? new()
        };
    }

    public static InitResult Failed(string message)
    {
        return new InitResult
        {
            Success = false,
            Messages = new List<string> { message }
        };
    }

    public static InitResult NotImplemented(string agentName)
    {
        return new InitResult
        {
            Success = false,
            Messages = new List<string> { $"Support for {agentName} is not yet implemented." }
        };
    }
}
