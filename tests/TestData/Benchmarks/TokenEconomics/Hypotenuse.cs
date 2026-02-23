using System;

namespace TokenEconomics
{
    public static class HypotenuseModule
    {
        public static double Hypotenuse(double a, double b)
        {
            if (a <= 0 || b <= 0)
                throw new ArgumentException("Sides must be positive");
            return Math.Sqrt(a * a + b * b);
        }
    }
}
