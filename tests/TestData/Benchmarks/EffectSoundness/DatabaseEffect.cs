// C# equivalent - no effect system
public static class DbService
{
    // Effect: database (not tracked in C#)
    public static int QueryCount(string table) => 0; // placeholder
    public static void Insert(string table, string data) { /* db write */ }

    // Pure function - no effects
    public static string PureTransform(string data) => data.Trim();
}
