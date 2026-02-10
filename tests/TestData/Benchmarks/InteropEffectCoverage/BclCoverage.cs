// C# equivalent - no effect manifest system
public static class BclUsage
{
    private static readonly HttpClient _httpClient = new();

    public static void ReadAndLog(string path)
    {
        // Effects: filesystem read, console write
        var content = File.ReadAllText(path);
        Console.WriteLine(content);
    }

    public static void WriteFile(string path, string content)
    {
        // Effect: filesystem write
        File.WriteAllText(path, content);
    }

    public static DateTime GetCurrentTime()
    {
        // Effect: nondeterminism (time)
        return DateTime.Now;
    }

    public static string GenerateId()
    {
        // Effect: nondeterminism (random)
        return Guid.NewGuid().ToString();
    }

    public static async Task<string> FetchData(string url)
    {
        // Effect: network read
        return await _httpClient.GetStringAsync(url);
    }

    public static string? GetEnvironmentVar(string name)
    {
        // Effect: environment read
        return Environment.GetEnvironmentVariable(name);
    }
}
