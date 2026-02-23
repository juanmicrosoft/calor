using System;

namespace DomainProblems
{
    public static class Calendar
    {
        public static bool IsLeapYear(int year)
        {
            return year % 400 == 0 || (year % 4 == 0 && year % 100 != 0);
        }

        public static int DaysInMonth(int month, bool isLeap)
        {
            return month switch
            {
                2 => isLeap ? 29 : 28,
                4 or 6 or 9 or 11 => 30,
                _ => 31
            };
        }

        public static int DaysInYear(bool isLeap) => isLeap ? 366 : 365;
    }
}
