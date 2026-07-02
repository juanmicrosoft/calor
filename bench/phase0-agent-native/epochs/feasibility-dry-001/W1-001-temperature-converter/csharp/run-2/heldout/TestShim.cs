// C#-arm shim (harness-provided, fixed, not agent-editable).
namespace TempConvert.HeldOut;

internal static class TestShim
{
    public static int CelsiusToFahrenheit(int celsius) => TempConvertLib.TempConvert.CelsiusToFahrenheit(celsius);
    public static int CelsiusToKelvin(int celsius) => TempConvertLib.TempConvert.CelsiusToKelvin(celsius);
    public static int FahrenheitToCelsius(int fahrenheit) => TempConvertLib.TempConvert.FahrenheitToCelsius(fahrenheit);
    public static int KelvinToCelsius(int kelvin) => TempConvertLib.TempConvert.KelvinToCelsius(kelvin);
}
