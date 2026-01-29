using System;

namespace Fibonacci
{
    public static class FibonacciModule
    {
        public static int Calculate(int n)
        {
            if (n <= 1)
            {
                return n;
            }
            else
            {
                return Calculate(n - 1) + Calculate(n - 2);
            }
        }
    }
}
