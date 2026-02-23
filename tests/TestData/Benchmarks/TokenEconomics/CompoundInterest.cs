using System;

namespace TokenEconomics
{
    public static class CompoundInterestModule
    {
        public static double SimpleInterest(double principal, double rate, int years)
        {
            if (principal <= 0) throw new ArgumentException("Principal must be positive");
            if (rate < 0) throw new ArgumentException("Rate cannot be negative");
            if (years <= 0) throw new ArgumentException("Years must be positive");
            return principal * (1 + rate * years);
        }

        public static double YearlyGrowth(double amount, double rate)
        {
            return amount * (1 + rate);
        }
    }
}
