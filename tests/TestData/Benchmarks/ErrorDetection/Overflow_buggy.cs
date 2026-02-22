using System;

namespace UnsafeAdd
{
    public static class UnsafeAddModule
    {
        // Bug: No overflow protection for large int addition
        public static int AddLarge(int a, int b)
        {
            return a + b;
        }
    }
}
