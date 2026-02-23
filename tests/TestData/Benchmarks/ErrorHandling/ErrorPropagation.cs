using System;
namespace ErrorHandling
{
    public static class ErrorPropagation
    {
        public static int Layer1(int input)
        {
            if (input <= 0) throw new ArgumentException("Layer1: positive required");
            return input * 2;
        }
        public static int Layer2(int input) => Layer1(input) + 10;
        public static int Pipeline(int input) => Layer2(input);
    }
}
