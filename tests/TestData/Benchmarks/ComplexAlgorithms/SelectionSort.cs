using System;

namespace ComplexAlgorithms
{
    public static class SelectionSort
    {
        public static void Sort(int[] arr)
        {
            for (int i = 0; i < arr.Length - 1; i++)
            {
                int minIdx = i;
                for (int j = i + 1; j < arr.Length; j++)
                {
                    if (arr[j] < arr[minIdx]) minIdx = j;
                }
                int temp = arr[minIdx];
                arr[minIdx] = arr[i];
                arr[i] = temp;
            }
        }
    }
}
