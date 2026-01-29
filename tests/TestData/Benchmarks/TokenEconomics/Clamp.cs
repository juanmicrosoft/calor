using System;
using System.Diagnostics;

namespace ValueOps
{
    public static class ValueOpsModule
    {
        public static int Clamp(int value, int min, int max)
        {
            Debug.Assert(min <= max, "min must be <= max");

            int result;
            if (value < min)
            {
                result = min;
            }
            else if (value > max)
            {
                result = max;
            }
            else
            {
                result = value;
            }

            Debug.Assert(result >= min, "Result must be >= min");
            Debug.Assert(result <= max, "Result must be <= max");

            return result;
        }
    }
}
