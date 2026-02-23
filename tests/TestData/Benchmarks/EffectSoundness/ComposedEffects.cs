// C# equivalent - no effect system
public static class ComposedService
{
    // Effects: network + console write
    public static string FetchAndLog(string url)
    {
        Console.WriteLine($"Fetching {url}");
        return ""; // placeholder for network fetch
    }

    // Effects: file read + database
    public static void ReadAndStore(string path)
    {
        string data = System.IO.File.ReadAllText(path);
        // Store to database...
    }

    // Pure function
    public static int PureHelper(string data) => data.Length;
}
