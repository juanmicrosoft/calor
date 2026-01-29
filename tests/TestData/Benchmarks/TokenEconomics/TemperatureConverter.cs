using System;

namespace TempConvert
{
    public static class TempConvertModule
    {
        public static double CelsiusToFahrenheit(double c)
        {
            return c * 1.8 + 32;
        }

        public static double FahrenheitToCelsius(double f)
        {
            return (f - 32) / 1.8;
        }

        public static double CelsiusToKelvin(double c)
        {
            return c + 273.15;
        }
    }
}
