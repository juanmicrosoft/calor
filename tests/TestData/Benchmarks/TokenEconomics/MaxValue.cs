using System;
using System.Diagnostics;

namespace ArrayOps
{
    public static class MaxValueModule
    {
        public static int Max(int[] arr)
        {
            Debug.Assert(arr.Length > 0, "Array must not be empty");

            int max = arr[0];
            for (int i = 1; i < arr.Length; i++)
            {
                if (arr[i] > max)
                {
                    max = arr[i];
                }
            }
            return max;
        }
    }
}
