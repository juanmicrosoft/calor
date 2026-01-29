using System;

namespace StringOps
{
    public static class StringOpsModule
    {
        public static string Reverse(string s)
        {
            string result = "";
            int i = s.Length - 1;

            while (i >= 0)
            {
                result += s[i];
                i -= 1;
            }

            return result;
        }
    }
}
