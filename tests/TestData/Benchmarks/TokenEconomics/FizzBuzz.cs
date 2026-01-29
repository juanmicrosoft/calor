using System;

namespace FizzBuzz
{
    public static class FizzBuzzModule
    {
        public static string Solve(int n)
        {
            if (n % 15 == 0)
            {
                return "FizzBuzz";
            }
            else if (n % 3 == 0)
            {
                return "Fizz";
            }
            else if (n % 5 == 0)
            {
                return "Buzz";
            }
            else
            {
                return n.ToString();
            }
        }

        public static void Run(int max)
        {
            for (int i = 1; i <= max; i++)
            {
                Console.WriteLine(Solve(i));
            }
        }
    }
}
