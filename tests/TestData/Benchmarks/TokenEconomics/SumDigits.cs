using System;

namespace TokenEconomics
{
    public static class SumDigitsModule
    {
        public static int SumDigits(int n)
        {
            int sum = 0;
            int num = Math.Abs(n);
            while (num > 0)
            {
                sum += num % 10;
                num /= 10;
            }
            return sum;
        }
    }
}
