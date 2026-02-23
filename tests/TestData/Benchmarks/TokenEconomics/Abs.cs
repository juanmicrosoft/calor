using System;

namespace TokenEconomics
{
    public static class AbsModule
    {
        public static int Abs(int x)
        {
            return x >= 0 ? x : -x;
        }
    }
}
