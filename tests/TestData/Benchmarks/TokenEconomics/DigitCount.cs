using System;

namespace TokenEconomics
{
    public static class DigitCountModule
    {
        public static int DigitCount(int n)
        {
            if (n == 0) return 1;
            int count = 0;
            int num = Math.Abs(n);
            while (num > 0)
            {
                num /= 10;
                count++;
            }
            return count;
        }
    }
}
