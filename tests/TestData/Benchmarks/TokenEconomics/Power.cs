using System;
using System.Diagnostics;

namespace MathPower
{
    public static class MathPowerModule
    {
        public static int Power(int baseNum, int exp)
        {
            Debug.Assert(exp >= 0, "Exponent must be non-negative");

            if (exp == 0)
            {
                return 1;
            }
            else if (exp == 1)
            {
                return baseNum;
            }
            else
            {
                return baseNum * Power(baseNum, exp - 1);
            }
        }
    }
}
