namespace UnsafeCode
{
    using System;

    public static class UnsafeExamples
    {
        public static unsafe int SumWithPointers(int[] array)
        {
            int sum = 0;
            fixed (int* ptr = array)
            {
                for (int i = 0; i < array.Length; i++)
                {
                    sum += *(ptr + i);
                }
            }
            return sum;
        }

        public static int GetSizeOfInt()
        {
            return sizeof(int);
        }

        public static unsafe void SwapValues(int* a, int* b)
        {
            int temp = *a;
            *a = *b;
            *b = temp;
        }
    }
}
