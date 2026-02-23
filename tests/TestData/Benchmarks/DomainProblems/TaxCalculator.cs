using System;

namespace DomainProblems
{
    public static class TaxCalculator
    {
        public static double TaxRate(double income)
        {
            if (income <= 10000) return 0.1;
            if (income <= 40000) return 0.2;
            if (income <= 85000) return 0.3;
            return 0.37;
        }

        public static double SimpleTax(double income) => income * TaxRate(income);
        public static double AfterTax(double income) => income - SimpleTax(income);
    }
}
