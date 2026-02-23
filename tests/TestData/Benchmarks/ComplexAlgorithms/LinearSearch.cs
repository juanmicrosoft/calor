using System;

namespace ComplexAlgorithms
{
    public static class LinearSearch
    {
        public static int Search(int[] arr, int target)
        {
            for (int i = 0; i < arr.Length; i++)
            {
                if (arr[i] == target) return i;
            }
            return -1;
        }
    }
}
