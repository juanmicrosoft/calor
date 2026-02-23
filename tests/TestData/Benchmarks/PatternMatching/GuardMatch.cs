using System;
namespace PatternMatching
{
    public static class GuardMatch
    {
        public static string Classify(int x, int y) => (x, y) switch
        {
            (0, 0) => "Origin",
            var (a, b) when a == b => "Diagonal",
            var (a, b) when a == -b => "Anti-diagonal",
            _ => "Other"
        };
    }
}
