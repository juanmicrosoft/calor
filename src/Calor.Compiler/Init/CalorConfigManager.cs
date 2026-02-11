namespace Calor.Compiler.Init;

/// <summary>
/// Manages reading, writing, and discovering .calor/config.json files.
/// </summary>
public static class CalorConfigManager
{
    public const string ConfigDirectory = ".calor";
    public const string ConfigFileName = "config.json";

    /// <summary>
    /// Gets the full path to the config file in a given directory.
    /// </summary>
    public static string GetConfigPath(string directoryPath)
        => Path.Combine(directoryPath, ConfigDirectory, ConfigFileName);

    /// <summary>
    /// Reads the config from a directory. Returns null if not found, empty config if malformed.
    /// </summary>
    public static CalorConfig? Read(string directoryPath)
    {
        var configPath = GetConfigPath(directoryPath);
        if (!File.Exists(configPath))
            return null;

        try
        {
            var json = File.ReadAllText(configPath);
            var config = CalorConfig.Deserialize(json);
            if (config == null)
            {
                Console.Error.WriteLine($"Warning: .calor/config.json is malformed, treating as empty config.");
                return new CalorConfig();
            }
            return config;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: Failed to read .calor/config.json: {ex.Message}");
            return new CalorConfig();
        }
    }

    /// <summary>
    /// Writes the config to a directory, creating .calor/ if needed.
    /// </summary>
    public static void Write(string directoryPath, CalorConfig config)
    {
        var configDir = Path.Combine(directoryPath, ConfigDirectory);
        Directory.CreateDirectory(configDir);

        var configPath = Path.Combine(configDir, ConfigFileName);
        File.WriteAllText(configPath, config.Serialize());
    }

    /// <summary>
    /// Adds agents to the config. If force is true, replaces the agents list; otherwise appends (deduped).
    /// Creates .calor/config.json if it doesn't exist.
    /// Returns true if the file was created (vs updated).
    /// </summary>
    public static bool AddAgents(string directoryPath, IEnumerable<string> agentNames, bool force)
    {
        var existing = Read(directoryPath);
        var isNew = existing == null;
        var config = existing ?? new CalorConfig();

        var agents = agentNames.ToList();
        if (agents.Count == 0)
        {
            // No agents to add — just ensure the config exists
            if (isNew) Write(directoryPath, config);
            return isNew;
        }

        if (force)
        {
            // Replace entire agents list with the new ones
            config.Agents = agents.Select(name => new AgentEntry
            {
                Name = name.ToLowerInvariant(),
                AddedAt = DateTime.UtcNow.ToString("o")
            }).ToList();
        }
        else
        {
            // Append, deduplicating by name
            var existingNames = new HashSet<string>(
                config.Agents.Select(a => a.Name),
                StringComparer.OrdinalIgnoreCase);

            foreach (var name in agents)
            {
                var normalized = name.ToLowerInvariant();
                if (existingNames.Add(normalized))
                {
                    config.Agents.Add(new AgentEntry
                    {
                        Name = normalized,
                        AddedAt = DateTime.UtcNow.ToString("o")
                    });
                }
            }
        }

        Write(directoryPath, config);
        return isNew;
    }

    /// <summary>
    /// Ensures .calor/config.json exists in the directory. Creates it if missing.
    /// Returns true if the file was created.
    /// </summary>
    public static bool EnsureExists(string directoryPath)
    {
        var configPath = GetConfigPath(directoryPath);
        if (File.Exists(configPath))
            return false;

        Write(directoryPath, new CalorConfig());
        return true;
    }

    /// <summary>
    /// Walks up from startPath looking for .calor/config.json.
    /// Returns the config and its directory, or null if not found.
    /// </summary>
    public static (CalorConfig Config, string Directory)? Discover(string startPath)
    {
        try
        {
            var dir = File.Exists(startPath)
                ? Path.GetDirectoryName(Path.GetFullPath(startPath))
                : Path.GetFullPath(startPath);

            while (!string.IsNullOrEmpty(dir))
            {
                var configPath = GetConfigPath(dir);
                if (File.Exists(configPath))
                {
                    var config = Read(dir);
                    if (config != null)
                        return (config, dir);
                }

                var parent = Directory.GetParent(dir);
                if (parent == null) break;
                dir = parent.FullName;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets the comma-separated agent names from a config, or "none" if empty/null.
    /// </summary>
    public static string GetAgentString(CalorConfig? config)
    {
        if (config == null || config.Agents.Count == 0)
            return "none";

        return string.Join(",", config.Agents.Select(a => a.Name));
    }
}
