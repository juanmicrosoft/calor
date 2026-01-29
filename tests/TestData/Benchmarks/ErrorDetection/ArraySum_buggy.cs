using System;

namespace ArrayOps
{
    public static class ArrayOpsModule
    {
        // Bug: Loop goes to arr.Length instead of arr.Length - 1
        public static int Sum(int[] arr)
        {
            int total = 0;
            for (int i = 0; i <= arr.Length; i++)  // Off-by-one error
            {
                total += arr[i];
            }
            return total;
        }
    }
}
