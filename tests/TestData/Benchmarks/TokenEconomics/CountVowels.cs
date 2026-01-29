using System;

namespace StringAnalysis
{
    public static class StringAnalysisModule
    {
        public static int CountVowels(string s)
        {
            int count = 0;
            string vowels = "aeiouAEIOU";

            for (int i = 0; i < s.Length; i++)
            {
                if (vowels.Contains(s[i]))
                {
                    count += 1;
                }
            }

            return count;
        }
    }
}
