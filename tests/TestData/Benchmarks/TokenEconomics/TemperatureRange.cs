using System;

namespace TemperatureRange
{
    public static class TemperatureRangeModule
    {
        public static double RangeKelvin(double cMin, double cMax)
        {
            double kMin = cMin + 273.15;
            double kMax = cMax + 273.15;
            return kMax - kMin;
        }
    }
}
