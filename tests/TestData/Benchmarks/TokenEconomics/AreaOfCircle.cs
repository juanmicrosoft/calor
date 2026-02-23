using System;

namespace TokenEconomics
{
    public static class AreaOfCircleModule
    {
        public static double Area(double radius)
        {
            if (radius <= 0)
                throw new ArgumentException("Radius must be positive");
            return Math.PI * radius * radius;
        }
    }
}
