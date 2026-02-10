// C# equivalent - no effect system
public static class LogService
{
    public static void LogMessage(string message)
    {
        // Effect: console write (not tracked in C#)
        Console.WriteLine(message);
    }

    public static int ComputeSum(int a, int b)
    {
        // Pure function (no effects)
        return a + b;
    }

    public static int LogAndCompute(string label, int x, int y)
    {
        // Effect: console write (not tracked in C#)
        LogMessage(label);
        var result = x + y;
        return result;
    }
}
