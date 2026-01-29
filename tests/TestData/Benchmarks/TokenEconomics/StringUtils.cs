using System;

namespace StringUtils
{
    public static class StringUtilsModule
    {
        public static string ToUpper(string s)
        {
            return s.ToUpper();
        }

        public static string ToLower(string s)
        {
            return s.ToLower();
        }

        public static int Length(string s)
        {
            return s.Length;
        }

        public static string Concat(string a, string b)
        {
            return a + b;
        }
    }
}
