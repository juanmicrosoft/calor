// C# equivalent - no static contract verification available
public static class BuggyMath
{
    public static int BadAbs(int x)
    {
        // BUG: Returns x directly, should ensure result >= 0
        return x;
    }

    public static int BadMax(int a, int b)
    {
        // BUG: Always returns a, should return larger value
        return a;
    }

    public static int BadDivide(int a, int b)
    {
        // BUG: Always returns 0, should return a/b
        return 0;
    }
}
