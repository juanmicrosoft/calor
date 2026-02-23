using System;

namespace DomainProblems
{
    public static class BMICalculator
    {
        public static double CalculateBMI(double weightKg, double heightM) => weightKg / (heightM * heightM);

        public static string Category(double bmi)
        {
            if (bmi < 18.5) return "Underweight";
            if (bmi < 25.0) return "Normal";
            if (bmi < 30.0) return "Overweight";
            return "Obese";
        }

        public static bool IsHealthy(double bmi) => bmi >= 18.5 && bmi < 25.0;
    }
}
