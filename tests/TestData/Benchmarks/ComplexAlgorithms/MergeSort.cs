using System;
using System.Collections.Generic;

namespace ComplexAlgorithms
{
    public static class MergeSort
    {
        public static void Sort(int[] arr)
        {
            if (arr.Length <= 1) return;
            MergeSortRange(arr, 0, arr.Length - 1);
        }

        private static void MergeSortRange(int[] arr, int low, int high)
        {
            if (low >= high) return;
            int mid = low + (high - low) / 2;
            MergeSortRange(arr, low, mid);
            MergeSortRange(arr, mid + 1, high);
            Merge(arr, low, mid, high);
        }

        private static void Merge(int[] arr, int low, int mid, int high)
        {
            var temp = new int[high - low + 1];
            int i = low, j = mid + 1, k = 0;
            while (i <= mid && j <= high)
            {
                if (arr[i] <= arr[j]) temp[k++] = arr[i++];
                else temp[k++] = arr[j++];
            }
            while (i <= mid) temp[k++] = arr[i++];
            while (j <= high) temp[k++] = arr[j++];
            Array.Copy(temp, 0, arr, low, temp.Length);
        }
    }
}
