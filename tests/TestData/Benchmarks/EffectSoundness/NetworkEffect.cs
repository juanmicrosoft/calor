// C# equivalent - no effect system
public static class NetService
{
    // Effect: network (not tracked in C#)
    public static string FetchUrl(string url) => ""; // placeholder
    public static int PostData(string url, string body) => 200; // placeholder

    // Pure function
    public static string BuildUrl(string baseUrl, string path) => $"{baseUrl}/{path}";
}
