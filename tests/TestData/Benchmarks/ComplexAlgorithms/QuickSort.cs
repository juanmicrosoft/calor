using System;
using System.Collections.Generic;

namespace ComplexAlgorithms
{
    public static class QuickSort
    {
        public static void Sort(List<int> arr)
        {
            if (arr.Count > 1)
            {
                QuickSortRange(arr, 0, arr.Count - 1);
            }
        }

        private static void QuickSortRange(List<int> arr, int low, int high)
        {
            if (low < 0)
                throw new ArgumentException("Low index cannot be negative");
            if (high >= arr.Count)
                throw new ArgumentException("High index out of bounds");

            if (low < high)
            {
                var pivotIndex = Partition(arr, low, high);
                QuickSortRange(arr, low, pivotIndex - 1);
                QuickSortRange(arr, pivotIndex + 1, high);
            }
        }

        private static int Partition(List<int> arr, int low, int high)
        {
            var pivot = arr[high];
            var i = low - 1;

            for (int j = low; j < high; j++)
            {
                if (arr[j] <= pivot)
                {
                    i++;
                    Swap(arr, i, j);
                }
            }
            Swap(arr, i + 1, high);
            return i + 1;
        }

        private static void Swap(List<int> arr, int i, int j)
        {
            if (i < 0 || i >= arr.Count || j < 0 || j >= arr.Count)
                throw new ArgumentException("Index out of bounds");

            var temp = arr[i];
            arr[i] = arr[j];
            arr[j] = temp;
        }

        public static bool IsSorted(List<int> arr)
        {
            if (arr.Count <= 1)
                return true;

            for (int i = 1; i < arr.Count; i++)
            {
                if (arr[i - 1] > arr[i])
                    return false;
            }
            return true;
        }
    }
}
