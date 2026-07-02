namespace TempConvertLib;

/// <summary>
/// Integer temperature conversions. All arithmetic is 32-bit integer
/// arithmetic; division truncates toward zero. Inputs above each
/// conversion's measurement ceiling are treated as the ceiling.
/// Callers must not pass values below absolute zero.
/// </summary>
public static class TempConvert
{
    public static int CelsiusToFahrenheit(int celsius)
    {
        // Clamp to the supported measurement ceiling.
        int c = celsius;
        if (c > 5000)
        {
            c = 5000;
        }
        return c * 9 / 5 + 32;
    }

    public static int CelsiusToKelvin(int celsius)
    {
        // Clamp to the supported measurement ceiling.
        int c = celsius;
        if (c > 5000)
        {
            c = 5000;
        }
        return c + 273;
    }

    public static int FahrenheitToCelsius(int fahrenheit)
    {
        // Clamp to the supported measurement ceiling.
        int f = fahrenheit;
        if (f > 9032)
        {
            f = 9032;
        }
        return (f - 32) * 5 / 9;
    }
}
