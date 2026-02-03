using System.Diagnostics;

namespace Calor.Compiler.Tests.Helpers;

/// <summary>
/// Represents a GitHub repository for integration testing.
/// </summary>
public record GitHubTestRepo(string Owner, string Repo, string Tag, int MinProjects)
{
    /// <summary>
    /// Gets the clone URL for this repository.
    /// </summary>
    public string CloneUrl => $"https://github.com/{Owner}/{Repo}.git";

    /// <summary>
    /// Gets a display name for test output.
    /// </summary>
    public string DisplayName => $"{Owner}/{Repo}@{Tag}";

    /// <summary>
    /// Clones the repository to a temporary directory.
    /// Uses shallow clone with specific tag for efficiency.
    /// </summary>
    /// <returns>Path to the cloned repository.</returns>
    public async Task<string> CloneAsync(CancellationToken cancellationToken = default)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"calor-test-{Repo}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"clone --depth 1 --branch {Tag} {CloneUrl} .",
                WorkingDirectory = tempDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            Directory.Delete(tempDir, recursive: true);
            throw new InvalidOperationException($"Failed to clone {DisplayName}: {error}");
        }

        return tempDir;
    }

    /// <summary>
    /// Clones the repository synchronously.
    /// </summary>
    public string CloneSync()
    {
        return CloneAsync().GetAwaiter().GetResult();
    }
}

/// <summary>
/// Manages test repository lifecycle.
/// </summary>
public sealed class TestRepoManager : IDisposable
{
    private readonly List<string> _tempDirectories = new();

    /// <summary>
    /// Clones a repository and registers it for cleanup.
    /// </summary>
    public async Task<string> CloneAsync(GitHubTestRepo repo, CancellationToken cancellationToken = default)
    {
        var path = await repo.CloneAsync(cancellationToken);
        _tempDirectories.Add(path);
        return path;
    }

    /// <summary>
    /// Cleans up all cloned repositories.
    /// </summary>
    public void Dispose()
    {
        foreach (var dir in _tempDirectories)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    // On Windows, git files can be locked, so retry with delay
                    for (int i = 0; i < 3; i++)
                    {
                        try
                        {
                            Directory.Delete(dir, recursive: true);
                            break;
                        }
                        catch (IOException) when (i < 2)
                        {
                            Thread.Sleep(100);
                        }
                    }
                }
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }
    }
}
