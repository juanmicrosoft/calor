// Arm-shared held-out tests (never shown to the agent under test).
// Binds to the pinned public surface through the harness-provided TestShim
// (per-arm, fixed, not agent-editable): CelsiusToFahrenheit /
// CelsiusToKelvin / FahrenheitToCelsius / KelvinToCelsius.
using Xunit;

namespace TempConvert.HeldOut;

public sealed class TempConvertHeldOutTests
{
    // --- CelsiusToFahrenheit ---

    [Theory]
    [InlineData(-273, -459)] // absolute zero boundary
    [InlineData(0, 32)]
    [InlineData(37, 98)]     // truncating division: 333 / 5 = 66
    [InlineData(100, 212)]
    [InlineData(5000, 9032)] // ceiling boundary
    public void CelsiusToFahrenheit_ConvertsCorrectly(int celsius, int expected)
    {
        Assert.Equal(expected, TestShim.CelsiusToFahrenheit(celsius));
    }

    [Fact]
    public void CelsiusToFahrenheit_ClampsAboveCeiling()
    {
        Assert.Equal(9032, TestShim.CelsiusToFahrenheit(6000));
    }

    // --- CelsiusToKelvin ---

    [Theory]
    [InlineData(-273, 0)]    // absolute zero boundary
    [InlineData(0, 273)]
    [InlineData(5000, 5273)] // ceiling boundary
    public void CelsiusToKelvin_ConvertsCorrectly(int celsius, int expected)
    {
        Assert.Equal(expected, TestShim.CelsiusToKelvin(celsius));
    }

    [Fact]
    public void CelsiusToKelvin_ClampsAboveCeiling()
    {
        Assert.Equal(5273, TestShim.CelsiusToKelvin(9999));
    }

    // --- FahrenheitToCelsius ---

    [Theory]
    [InlineData(-459, -272)] // truncation toward zero: -2455 / 9 = -272
    [InlineData(32, 0)]
    [InlineData(98, 36)]     // truncating division: 330 / 9 = 36
    [InlineData(212, 100)]
    [InlineData(9032, 5000)] // ceiling boundary
    public void FahrenheitToCelsius_ConvertsCorrectly(int fahrenheit, int expected)
    {
        Assert.Equal(expected, TestShim.FahrenheitToCelsius(fahrenheit));
    }

    [Fact]
    public void FahrenheitToCelsius_ClampsAboveCeiling()
    {
        Assert.Equal(5000, TestShim.FahrenheitToCelsius(10000));
    }

    // --- KelvinToCelsius (new function) ---

    [Theory]
    [InlineData(0, -273)]    // absolute zero boundary
    [InlineData(273, 0)]
    [InlineData(310, 37)]
    [InlineData(5273, 5000)] // ceiling boundary
    public void KelvinToCelsius_ConvertsCorrectly(int kelvin, int expected)
    {
        Assert.Equal(expected, TestShim.KelvinToCelsius(kelvin));
    }

    [Fact]
    public void KelvinToCelsius_ClampsAboveCeiling()
    {
        Assert.Equal(5000, TestShim.KelvinToCelsius(6000));
    }

    // --- Round trips inside the shared domain ---

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(4000)]
    public void CelsiusKelvinRoundTrip_IsIdentity(int celsius)
    {
        Assert.Equal(celsius, TestShim.KelvinToCelsius(TestShim.CelsiusToKelvin(celsius)));
    }
}
