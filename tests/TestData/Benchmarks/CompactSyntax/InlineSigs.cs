using System;
namespace CompactSyntax
{
    public static class InlineSigs
    {
        public static int Add(int a, int b) => a + b;
        public static int Multiply(int a, int b) => a * b;
        public static int Negate(int x) => -x;
        public static bool IsZero(int x) => x == 0;
    }
}
