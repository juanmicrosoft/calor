using System;

namespace TokenEconomics
{
    public static class ReverseArrayModule
    {
        public static void Reverse(int[] arr)
        {
            int left = 0, right = arr.Length - 1;
            while (left < right)
            {
                int temp = arr[left];
                arr[left] = arr[right];
                arr[right] = temp;
                left++;
                right--;
            }
        }
    }
}
