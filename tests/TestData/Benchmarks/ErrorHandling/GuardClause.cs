using System;
namespace ErrorHandling
{
    public static class GuardClause
    {
        public static void ValidatePositive(int value)
        {
            if (value <= 0) throw new ArgumentException("Must be positive");
        }
        public static void ValidateRange(int value, int min, int max)
        {
            if (value < min || value > max) throw new ArgumentOutOfRangeException();
        }
        public static void ValidateNotEmpty(string s)
        {
            if (string.IsNullOrEmpty(s)) throw new ArgumentException("Must not be empty");
        }
    }
}
