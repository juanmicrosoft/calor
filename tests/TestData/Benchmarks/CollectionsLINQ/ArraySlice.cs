using System;
namespace CollectionsLINQ
{
    public static class ArraySlice
    {
        public static T[] Slice<T>(T[] arr, int start, int end)
        {
            if (start < 0 || end > arr.Length || start > end)
                throw new ArgumentOutOfRangeException();
            var result = new T[end - start];
            Array.Copy(arr, start, result, 0, result.Length);
            return result;
        }
    }
}
