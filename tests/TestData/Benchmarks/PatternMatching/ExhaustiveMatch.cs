using System;
namespace PatternMatching
{
    public enum Suit { Spades, Hearts, Diamonds, Clubs }
    public static class ExhaustiveMatch
    {
        public static string Name(Suit s) => s switch
        {
            Suit.Spades => "Spades", Suit.Hearts => "Hearts",
            Suit.Diamonds => "Diamonds", Suit.Clubs => "Clubs",
            _ => throw new ArgumentException()
        };
        public static bool IsRed(Suit s) => s is Suit.Hearts or Suit.Diamonds;
    }
}
