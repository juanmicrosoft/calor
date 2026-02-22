namespace SyntheticLib;

public class DateHelper
{
    public static bool IsLeapYear(int year)
    {
        if (year % 400 == 0)
            return true;
        if (year % 100 == 0)
            return false;
        if (year % 4 == 0)
            return true;
        return false;
    }

    public static int DaysInMonth(int year, int month)
    {
        if (month < 1 || month > 12)
            throw new ArgumentOutOfRangeException(nameof(month));
        if (month == 2)
            return IsLeapYear(year) ? 29 : 28;
        if (month == 4 || month == 6 || month == 9 || month == 11)
            return 30;
        return 31;
    }

    public static int DaysInYear(int year)
    {
        return IsLeapYear(year) ? 366 : 365;
    }

    public static string DayOfWeekName(int dayIndex)
    {
        if (dayIndex == 0) return "Sunday";
        if (dayIndex == 1) return "Monday";
        if (dayIndex == 2) return "Tuesday";
        if (dayIndex == 3) return "Wednesday";
        if (dayIndex == 4) return "Thursday";
        if (dayIndex == 5) return "Friday";
        if (dayIndex == 6) return "Saturday";
        throw new ArgumentOutOfRangeException(nameof(dayIndex));
    }

    public static bool IsWeekend(int dayIndex)
    {
        return dayIndex == 0 || dayIndex == 6;
    }
}
