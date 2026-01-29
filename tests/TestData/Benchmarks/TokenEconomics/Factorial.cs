using System;

namespace Factorial
{
    public static class FactorialModule
    {
        public static int Calculate(int n)
        {
            if (n <= 1)
            {
                return 1;
            }
            else
            {
                return n * Calculate(n - 1);
            }
        }
    }
}
