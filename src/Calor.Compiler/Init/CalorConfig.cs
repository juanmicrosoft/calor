using System.Text.Json;
using System.Text.Json.Serialization;

namespace Calor.Compiler.Init;

/// <summary>
/// Represents the .calor/config.json project configuration.
/// </summary>
public sealed class CalorConfig
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("agents")]
    public List<AgentEntry> Agents { get; set; } = new();

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("o");

    public string Serialize() => JsonSerializer.Serialize(this, SerializerOptions);

    public static CalorConfig? Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<CalorConfig>(json, SerializerOptions);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// An agent entry with its name and the timestamp when it was added.
/// </summary>
public sealed class AgentEntry
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("addedAt")]
    public string AddedAt { get; set; } = DateTime.UtcNow.ToString("o");
}
