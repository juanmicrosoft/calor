using System;

namespace TokenEconomics
{
    // C# expression-bodied members are extremely compact — one line each.
    // Calor requires §F/§I/§O/§R/§/F (5 lines) per function.
    // This is an adversarial benchmark where C# wins on token economy.
    public static class ExpressionBodied
    {
        public static int Double(int x) => x * 2;
        public static int Triple(int x) => x * 3;
        public static int Square(int x) => x * x;
        public static int Increment(int x) => x + 1;
        public static int Decrement(int x) => x - 1;
        public static bool IsPositive(int x) => x > 0;
        public static bool IsNegative(int x) => x < 0;
        public static int Negate(int x) => -x;
    }
}
