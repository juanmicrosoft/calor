// C# equivalent - no effect system
public static class UnsafeService
{
    public static void SilentLog(string message)
    {
        // Missing effect declaration for console write
        Console.WriteLine(message);
    }

    public static void UndeclaredWrite(string path, string content)
    {
        // Missing effect declaration for filesystem write
        File.WriteAllText(path, content);
    }

    public static async Task<string> HiddenNetwork(string url)
    {
        // Missing effect declaration for network access
        using var client = new HttpClient();
        return await client.GetStringAsync(url);
    }
}
