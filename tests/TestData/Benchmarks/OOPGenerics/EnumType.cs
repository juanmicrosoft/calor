using System;
namespace OOPGenerics
{
    public enum Season { Spring, Summer, Autumn, Winter }
    public static class SeasonHelper
    {
        public static int DaysInSeason(Season s) => s switch { Season.Spring => 92, Season.Summer => 92, Season.Autumn => 91, _ => 90 };
        public static bool IsWarm(Season s) => s == Season.Summer || s == Season.Spring;
    }
}
