using System;
namespace PatternMatching
{
    public static class TypeMatch
    {
        public static string Describe(object obj) => obj switch
        {
            int i => $"Integer: {i}",
            string s => $"String: {s}",
            double d => $"Double: {d}",
            _ => "Unknown type"
        };
    }
}
