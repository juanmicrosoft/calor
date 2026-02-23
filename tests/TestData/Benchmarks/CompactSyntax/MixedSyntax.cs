using System;
namespace CompactSyntax
{
    public static class MixedSyntax
    {
        public static int Compact(int x) => x * 2;
        public static int Verbose(int x)
        {
            if (x <= 0) throw new ArgumentException("Must be positive");
            return x * 3;
        }
        public static int CompactWithContract(int a, int b)
        {
            if (b == 0) throw new DivideByZeroException();
            return a / b;
        }
    }
}
