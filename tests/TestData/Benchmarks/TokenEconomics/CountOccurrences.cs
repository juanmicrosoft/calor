using System;

namespace TokenEconomics
{
    public static class CountOccurrencesModule
    {
        public static int CountOccurrences(int[] arr, int target)
        {
            int count = 0;
            foreach (var item in arr)
            {
                if (item == target) count++;
            }
            return count;
        }
    }
}
