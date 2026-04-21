public static class TemperatureConverter
{
    public static double CelsiusToFahrenheit(double celsius)
    {
        return celsius * 9.0 / 5.0 + 32.0;
    }

    public static double FahrenheitToCelsius(double fahrenheit)
    {
        return (fahrenheit - 32.0) * 5.0 / 9.0;
    }
}
