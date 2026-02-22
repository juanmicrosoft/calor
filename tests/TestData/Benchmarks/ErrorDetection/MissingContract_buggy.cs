using System;

namespace UnsafeDivide
{
    public static class UnsafeDivideModule
    {
        // Bug: Division without parameter validation
        public static int Divide(int a, int b)
        {
            return a / b;
        }
    }
}
