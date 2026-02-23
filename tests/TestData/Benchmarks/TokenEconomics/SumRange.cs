using System;

namespace TokenEconomics
{
    public static class SumRangeModule
    {
        public static int SumRange(int n)
        {
            if (n <= 0) throw new ArgumentException("n must be positive");
            int sum = 0;
            for (int i = 1; i <= n; i++)
            {
                sum += i;
            }
            return sum;
        }
    }
}
