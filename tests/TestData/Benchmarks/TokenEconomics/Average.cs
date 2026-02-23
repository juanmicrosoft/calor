using System;

namespace TokenEconomics
{
    public static class AverageModule
    {
        public static double Average(int[] numbers)
        {
            if (numbers.Length == 0)
                throw new ArgumentException("Array cannot be empty");
            int sum = 0;
            foreach (var n in numbers) sum += n;
            return (double)sum / numbers.Length;
        }
    }
}
