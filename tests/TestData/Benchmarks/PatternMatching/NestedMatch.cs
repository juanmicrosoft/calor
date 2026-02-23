using System;
namespace PatternMatching
{
    public static class NestedMatch
    {
        public static string ClassifyPair(int a, int b) => (a > 0, b > 0) switch
        {
            (true, true) => "Both positive",
            (true, false) => "Only first positive",
            (false, true) => "Only second positive",
            (false, false) => "Both negative or zero"
        };
    }
}
