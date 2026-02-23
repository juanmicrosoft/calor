using System;

namespace TokenEconomics
{
    public static class CelsiusToKelvinModule
    {
        public static double ToKelvin(double celsius)
        {
            if (celsius < -273.15)
                throw new ArgumentException("Below absolute zero");
            return celsius + 273.15;
        }
    }
}
