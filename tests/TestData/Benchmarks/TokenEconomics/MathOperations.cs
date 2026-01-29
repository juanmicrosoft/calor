using System;
using System.Diagnostics;

namespace MathOps
{
    public static class MathOpsModule
    {
        public static int Abs(int x)
        {
            int result;
            if (x < 0)
            {
                result = -x;
            }
            else
            {
                result = x;
            }

            Debug.Assert(result >= 0, "Result must be non-negative");
            return result;
        }

        public static int Min(int a, int b)
        {
            int result;
            if (a < b)
            {
                result = a;
            }
            else
            {
                result = b;
            }

            Debug.Assert(result <= a, "Result must be <= a");
            Debug.Assert(result <= b, "Result must be <= b");
            return result;
        }

        public static int Max(int a, int b)
        {
            int result;
            if (a > b)
            {
                result = a;
            }
            else
            {
                result = b;
            }

            Debug.Assert(result >= a, "Result must be >= a");
            Debug.Assert(result >= b, "Result must be >= b");
            return result;
        }
    }
}
