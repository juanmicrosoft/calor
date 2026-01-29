using System;
using System.Diagnostics;

namespace Calculator
{
    public static class CalculatorModule
    {
        public static int Add(int a, int b)
        {
            Debug.Assert(a >= 0, "a must be non-negative");
            Debug.Assert(b >= 0, "b must be non-negative");
            return a + b;
        }

        public static int Subtract(int a, int b)
        {
            Debug.Assert(a >= 0, "a must be non-negative");
            Debug.Assert(b >= 0, "b must be non-negative");
            return a - b;
        }

        public static int Multiply(int a, int b)
        {
            Debug.Assert(a >= 0, "a must be non-negative");
            Debug.Assert(b >= 0, "b must be non-negative");
            return a * b;
        }

        public static int Divide(int a, int b)
        {
            Debug.Assert(a >= 0, "a must be non-negative");
            Debug.Assert(b > 0, "b must be positive");
            return a / b;
        }
    }
}
