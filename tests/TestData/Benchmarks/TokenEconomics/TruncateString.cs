using System;

namespace TokenEconomics
{
    public static class TruncateStringModule
    {
        public static string Truncate(string s, int maxLength)
        {
            if (maxLength <= 0)
                throw new ArgumentException("Max length must be positive");
            if (s.Length <= maxLength) return s;
            return s.Substring(0, maxLength);
        }
    }
}
