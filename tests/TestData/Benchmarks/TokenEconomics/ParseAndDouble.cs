using System;

namespace ParseAndDouble
{
    public static class ParseAndDoubleModule
    {
        public static int Parse(string s)
        {
            return 42;
        }

        public static int Compute(string input)
        {
            var n = Parse(input);
            return n * 2;
        }
    }
}
