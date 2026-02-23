using System;

namespace ComplexAlgorithms
{
    public static class InterpolationSearch
    {
        public static int Search(int[] arr, int target)
        {
            int low = 0, high = arr.Length - 1;
            while (low <= high && target >= arr[low] && target <= arr[high])
            {
                if (low == high)
                {
                    if (arr[low] == target) return low;
                    return -1;
                }
                int pos = low + ((target - arr[low]) * (high - low)) / (arr[high] - arr[low]);
                if (arr[pos] == target) return pos;
                if (arr[pos] < target) low = pos + 1;
                else high = pos - 1;
            }
            return -1;
        }
    }
}
