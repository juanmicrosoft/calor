using System;
namespace CompactSyntax
{
    public static class CompactClass
    {
        public static void Greet(string name) => Console.WriteLine($"Hello, {name}!");
        public static int Square(int x) => x * x;
        public static int Max(int a, int b) => a > b ? a : b;
    }
}
