using System;
using System.Diagnostics;

namespace SafeMath
{
    public static class SafeMathModule
    {
        public static int Divide(int a, int b)
        {
            Debug.Assert(b != 0, "Divisor cannot be zero");

            int result = a / b;

            Debug.Assert(result == a / b, "Result must equal quotient");

            return result;
        }
    }
}
