using System;

namespace AggregateStats
{
    public static class AggregateStatsModule
    {
        public static int Mean3(int a, int b, int c)
        {
            int sum = a + b + c;
            return sum / 3;
        }
    }
}
