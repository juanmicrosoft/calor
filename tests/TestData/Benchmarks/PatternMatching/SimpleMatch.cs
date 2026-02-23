using System;
namespace PatternMatching
{
    public static class SimpleMatch
    {
        public static string DayName(int day) => day switch
        {
            1 => "Monday", 2 => "Tuesday", 3 => "Wednesday",
            4 => "Thursday", 5 => "Friday", 6 => "Saturday",
            7 => "Sunday", _ => "Unknown"
        };
    }
}
