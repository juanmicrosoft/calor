using System;

namespace TokenEconomics
{
    public static class ContainsModule
    {
        public static bool Contains(int[] arr, int target)
        {
            foreach (var item in arr)
            {
                if (item == target) return true;
            }
            return false;
        }
    }
}
