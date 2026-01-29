using System;
using System.Diagnostics;

namespace PrimeCheck
{
    public static class PrimeCheckModule
    {
        public static bool IsPrime(int n)
        {
            Debug.Assert(n > 0, "Input must be positive");

            if (n <= 1)
            {
                return false;
            }
            if (n <= 3)
            {
                return true;
            }
            if (n % 2 == 0)
            {
                return false;
            }

            int i = 3;
            while (i * i <= n)
            {
                if (n % i == 0)
                {
                    return false;
                }
                i += 2;
            }
            return true;
        }
    }
}
