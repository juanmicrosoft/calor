using System;

public static class StringLib
{
    public static int Length(string s) => s.Length;
    public static bool IsEmpty(string s) => s.Length == 0;
    public static string Concat(string a, string b) => a + b;
}
