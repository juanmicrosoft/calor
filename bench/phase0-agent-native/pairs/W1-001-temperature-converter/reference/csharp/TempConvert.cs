namespace TempConvertLib;

/// <summary>
/// Integer temperature conversions. All arithmetic is 32-bit integer
/// arithmetic; division truncates toward zero. Inputs above each
/// conversion's measurement ceiling are treated as the ceiling.
/// Callers must not pass values below absolute zero.
/// </summary>
public static class TempConvert
{
    private static int ClampToCeiling(int value, int ceiling)
        => value > ceiling ? ceiling : value;

    public static int CelsiusToFahrenheit(int celsius)
    {
        int c = ClampToCeiling(celsius, 5000);
        return c * 9 / 5 + 32;
    }

    public static int CelsiusToKelvin(int celsius)
    {
        int c = ClampToCeiling(celsius, 5000);
        return c + 273;
    }

    public static int FahrenheitToCelsius(int fahrenheit)
    {
        int f = ClampToCeiling(fahrenheit, 9032);
        return (f - 32) * 5 / 9;
    }

    public static int KelvinToCelsius(int kelvin)
    {
        int k = ClampToCeiling(kelvin, 5273);
        return k - 273;
    }
}
