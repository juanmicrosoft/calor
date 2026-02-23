using System;

namespace DomainProblems
{
    public static class DateDiff
    {
        public static int DaysBetween(DateTime a, DateTime b) => Math.Abs((b - a).Days);
        public static int WeeksBetween(DateTime a, DateTime b) => DaysBetween(a, b) / 7;
        public static int RemainingDays(DateTime a, DateTime b) => DaysBetween(a, b) % 7;
    }
}
