using System;

namespace TokenEconomics
{
    public static class CapitalizeModule
    {
        public static string Capitalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (char.IsLower(s[0]))
                return char.ToUpper(s[0]) + s.Substring(1);
            return s;
        }
    }
}
