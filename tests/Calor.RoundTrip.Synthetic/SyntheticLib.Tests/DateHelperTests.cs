using SyntheticLib;
using Xunit;

namespace SyntheticLib.Tests;

public class DateHelperTests
{
    [Fact]
    public void IsLeapYear_StandardLeapYear()
    {
        Assert.True(DateHelper.IsLeapYear(2024));
    }

    [Fact]
    public void IsLeapYear_CenturyNonLeap()
    {
        Assert.False(DateHelper.IsLeapYear(1900));
    }

    [Fact]
    public void IsLeapYear_QuadCentury()
    {
        Assert.True(DateHelper.IsLeapYear(2000));
    }

    [Fact]
    public void IsLeapYear_NonLeap()
    {
        Assert.False(DateHelper.IsLeapYear(2023));
    }

    [Fact]
    public void DaysInMonth_February_LeapYear()
    {
        Assert.Equal(29, DateHelper.DaysInMonth(2024, 2));
    }

    [Fact]
    public void DaysInMonth_February_NonLeapYear()
    {
        Assert.Equal(28, DateHelper.DaysInMonth(2023, 2));
    }

    [Fact]
    public void DaysInMonth_ThirtyDayMonth()
    {
        Assert.Equal(30, DateHelper.DaysInMonth(2024, 4));
        Assert.Equal(30, DateHelper.DaysInMonth(2024, 6));
    }

    [Fact]
    public void DaysInMonth_ThirtyOneDayMonth()
    {
        Assert.Equal(31, DateHelper.DaysInMonth(2024, 1));
        Assert.Equal(31, DateHelper.DaysInMonth(2024, 7));
    }

    [Fact]
    public void DaysInMonth_Invalid_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DateHelper.DaysInMonth(2024, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => DateHelper.DaysInMonth(2024, 13));
    }

    [Fact]
    public void DaysInYear_LeapYear()
    {
        Assert.Equal(366, DateHelper.DaysInYear(2024));
    }

    [Fact]
    public void DaysInYear_NonLeapYear()
    {
        Assert.Equal(365, DateHelper.DaysInYear(2023));
    }

    [Fact]
    public void DayOfWeekName_Valid()
    {
        Assert.Equal("Sunday", DateHelper.DayOfWeekName(0));
        Assert.Equal("Saturday", DateHelper.DayOfWeekName(6));
    }

    [Fact]
    public void DayOfWeekName_Invalid_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DateHelper.DayOfWeekName(7));
    }

    [Fact]
    public void IsWeekend_Works()
    {
        Assert.True(DateHelper.IsWeekend(0));
        Assert.True(DateHelper.IsWeekend(6));
        Assert.False(DateHelper.IsWeekend(1));
        Assert.False(DateHelper.IsWeekend(5));
    }
}
