using System;

namespace DomainProblems
{
    public static class UnitConverter
    {
        public static double InchesToCm(double inches) => inches * 2.54;
        public static double CmToInches(double cm) => cm / 2.54;
        public static double PoundsToKg(double pounds) => pounds * 0.453592;
        public static double KgToPounds(double kg) => kg / 0.453592;
        public static double GallonsToLiters(double gallons) => gallons * 3.78541;
    }
}
