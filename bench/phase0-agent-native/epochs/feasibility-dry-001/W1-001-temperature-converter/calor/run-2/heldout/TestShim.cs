// Calor-arm shim (harness-provided, fixed, not agent-editable).
// Calor module M emits namespace M / static class MModule.
namespace TempConvert.HeldOut;

internal static class TestShim
{
    public static int CelsiusToFahrenheit(int celsius) => global::TempConvert.TempConvertModule.CelsiusToFahrenheit(celsius);
    public static int CelsiusToKelvin(int celsius) => global::TempConvert.TempConvertModule.CelsiusToKelvin(celsius);
    public static int FahrenheitToCelsius(int fahrenheit) => global::TempConvert.TempConvertModule.FahrenheitToCelsius(fahrenheit);
    public static int KelvinToCelsius(int kelvin) => global::TempConvert.TempConvertModule.KelvinToCelsius(kelvin);
}
