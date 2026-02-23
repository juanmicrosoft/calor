using System;

namespace TokenEconomics
{
    public static class SignModule
    {
        public static int Sign(int x)
        {
            if (x > 0) return 1;
            if (x < 0) return -1;
            return 0;
        }
    }
}
