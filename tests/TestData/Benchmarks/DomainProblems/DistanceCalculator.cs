using System;

namespace DomainProblems
{
    public static class DistanceCalculator
    {
        public static double EuclideanDist(double x1, double y1, double x2, double y2)
        {
            double dx = x2 - x1, dy = y2 - y1;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public static double ManhattanDist(double x1, double y1, double x2, double y2)
        {
            return Math.Abs(x2 - x1) + Math.Abs(y2 - y1);
        }
    }
}
