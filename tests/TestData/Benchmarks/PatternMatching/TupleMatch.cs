using System;
namespace PatternMatching
{
    public static class TupleMatch
    {
        public static string RockPaperScissors(int p1, int p2) => (p1, p2) switch
        {
            var (a, b) when a == b => "Draw",
            (0, 2) or (1, 0) or (2, 1) => "Player 1 wins",
            _ => "Player 2 wins"
        };
    }
}
