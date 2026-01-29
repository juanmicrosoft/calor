using System;

namespace MathUtils
{
    public static class MathUtilsModule
    {
        public static int GCD(int a, int b)
        {
            if (b == 0)
            {
                return a;
            }
            else
            {
                return GCD(b, a % b);
            }
        }
    }
}
