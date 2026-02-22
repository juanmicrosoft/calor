using SyntheticLib;
using Xunit;

namespace SyntheticLib.Tests;

public class TemperatureTests
{
    [Fact]
    public void Celsius_ToCelsius_ReturnsSame()
    {
        var t = new Temperature(25, TemperatureScale.Celsius);
        Assert.Equal(25, t.ToCelsius());
    }

    [Fact]
    public void Fahrenheit_ToCelsius_Converts()
    {
        var t = new Temperature(212, TemperatureScale.Fahrenheit);
        Assert.Equal(100, t.ToCelsius(), 1);
    }

    [Fact]
    public void Kelvin_ToCelsius_Converts()
    {
        var t = new Temperature(273.15, TemperatureScale.Kelvin);
        Assert.Equal(0, t.ToCelsius(), 1);
    }

    [Fact]
    public void FreezingPoint_IsDetected()
    {
        var freezing = new Temperature(0, TemperatureScale.Celsius);
        Assert.True(freezing.IsFreezingPoint());

        var warm = new Temperature(25, TemperatureScale.Celsius);
        Assert.False(warm.IsFreezingPoint());
    }

    [Fact]
    public void BoilingPoint_IsDetected()
    {
        var boiling = new Temperature(100, TemperatureScale.Celsius);
        Assert.True(boiling.IsBoilingPoint());
    }

    [Fact]
    public void GetDescription_ReturnsCorrectDescriptions()
    {
        Assert.Equal("Extreme cold", Temperature.GetDescription(-50));
        Assert.Equal("Freezing", Temperature.GetDescription(-10));
        Assert.Equal("Cold", Temperature.GetDescription(5));
        Assert.Equal("Cool", Temperature.GetDescription(15));
        Assert.Equal("Warm", Temperature.GetDescription(25));
        Assert.Equal("Hot", Temperature.GetDescription(35));
        Assert.Equal("Extreme heat", Temperature.GetDescription(45));
    }
}
