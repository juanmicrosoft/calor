using System;

namespace DomainProblems
{
    public static class CurrencyConverter
    {
        public static double Convert(double amount, double rate) => amount * rate;
        public static double RoundToTwo(double value) => Math.Round(value, 2);
        public static double InverseRate(double rate) => 1.0 / rate;
    }
}
