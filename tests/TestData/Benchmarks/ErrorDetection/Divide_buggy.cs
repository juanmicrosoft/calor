using System;

namespace UnsafeMath
{
    public static class UnsafeMathModule
    {
        // Bug: No validation for division by zero
        public static int Divide(int a, int b)
        {
            return a / b;
        }
    }
}
