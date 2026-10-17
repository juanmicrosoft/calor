using System;

public static class InterpolationFormattable
{
    public static int Kind(FormattableString f)
    {
        return 1;
    }

    public static int Kind(object o)
    {
        return -1;
    }

    public static int Verify()
    {
        int x = 42;
        string s = $"k={x},{x + 1}";
        return Kind($"k={x},{x + 1}") * 100 + s.Length;
    }
}
