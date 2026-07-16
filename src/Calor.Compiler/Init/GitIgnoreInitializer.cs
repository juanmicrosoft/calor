namespace Calor.Compiler.Init;

/// <summary>
/// Initializes .gitignore with entries for Calor build artifacts that should not
/// be committed (the incremental build-state file written by <c>calor --cache</c>,
/// <c>calor watch</c>, and the MSBuild task).
/// </summary>
public static class GitIgnoreInitializer
{
    private const string StateFileEntry = ".calor-build-state.json";

    private const string CalorSection = """
        # Calor incremental build state (calor --cache / calor watch / MSBuild task)
        .calor-build-state.json
        """;

    /// <summary>
    /// Creates or updates .gitignore with the Calor build-artifact entries.
    /// </summary>
    /// <param name="targetDirectory">The directory where .gitignore should be created/updated.</param>
    /// <returns>A tuple indicating whether the file was created or updated.</returns>
    public static async Task<(bool created, bool updated)> InitializeAsync(string targetDirectory)
    {
        var gitIgnorePath = Path.Combine(targetDirectory, ".gitignore");

        if (!File.Exists(gitIgnorePath))
        {
            await File.WriteAllTextAsync(gitIgnorePath, CalorSection + Environment.NewLine);
            return (created: true, updated: false);
        }

        var content = await File.ReadAllTextAsync(gitIgnorePath);
        if (content.Contains(StateFileEntry))
        {
            return (created: false, updated: false);
        }

        var newContent = content.TrimEnd() + Environment.NewLine + Environment.NewLine + CalorSection + Environment.NewLine;
        await File.WriteAllTextAsync(gitIgnorePath, newContent);
        return (created: false, updated: true);
    }
}
