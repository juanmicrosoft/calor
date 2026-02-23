using System;
namespace PatternMatching
{
    public static class RangeMatch
    {
        public static string AgeGroup(int age) => age switch
        {
            < 13 => "Child",
            < 20 => "Teenager",
            < 65 => "Adult",
            _ => "Senior"
        };
    }
}
