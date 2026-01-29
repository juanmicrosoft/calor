using System;

namespace StringCheck
{
    public static class StringCheckModule
    {
        public static bool IsPalindrome(string s)
        {
            int left = 0;
            int right = s.Length - 1;

            while (left < right)
            {
                if (s[left] != s[right])
                {
                    return false;
                }
                left += 1;
                right -= 1;
            }

            return true;
        }
    }
}
