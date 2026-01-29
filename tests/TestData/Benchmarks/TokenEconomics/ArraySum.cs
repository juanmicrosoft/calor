using System;

namespace ArrayOps
{
    public static class ArrayOpsModule
    {
        public static int Sum(int[] arr)
        {
            int total = 0;
            for (int i = 0; i < arr.Length; i++)
            {
                total += arr[i];
            }
            return total;
        }
    }
}
