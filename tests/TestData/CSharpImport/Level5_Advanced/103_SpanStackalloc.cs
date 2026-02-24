namespace SpanStackalloc
{
    using System;

    public static class SpanExamples
    {
        public static int SumStackAlloc()
        {
            Span<int> span = stackalloc int[10];
            for (int i = 0; i < span.Length; i++)
            {
                span[i] = i;
            }

            int sum = 0;
            for (int i = 0; i < span.Length; i++)
            {
                sum += span[i];
            }
            return sum;
        }

        public static int SumStackAllocInitializer()
        {
            Span<int> span = stackalloc int[] { 1, 2, 3, 4, 5 };
            int sum = 0;
            for (int i = 0; i < span.Length; i++)
            {
                sum += span[i];
            }
            return sum;
        }

        public static ReadOnlySpan<char> GetSlice(string text)
        {
            return text.AsSpan(0, 5);
        }
    }
}
