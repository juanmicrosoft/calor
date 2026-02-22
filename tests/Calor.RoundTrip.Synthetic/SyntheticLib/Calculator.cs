namespace SyntheticLib;

public class Calculator
{
    public static int Add(int a, int b)
    {
        return a + b;
    }

    public static int Subtract(int a, int b)
    {
        return a - b;
    }

    public static int Multiply(int a, int b)
    {
        return a * b;
    }

    public static double Divide(double a, double b)
    {
        if (b == 0)
            throw new DivideByZeroException("Cannot divide by zero");
        return a / b;
    }

    public static int Factorial(int n)
    {
        if (n < 0)
            throw new ArgumentException("Factorial not defined for negative numbers");
        if (n <= 1)
            return 1;
        int result = 1;
        for (int i = 2; i <= n; i++)
        {
            result = result * i;
        }
        return result;
    }

    public static bool IsEven(int n)
    {
        return n % 2 == 0;
    }

    public static int Abs(int n)
    {
        if (n < 0)
            return -n;
        return n;
    }

    public static int Max(int a, int b)
    {
        if (a >= b)
            return a;
        return b;
    }

    public static int Min(int a, int b)
    {
        if (a <= b)
            return a;
        return b;
    }

    public static int Clamp(int value, int min, int max)
    {
        if (value < min)
            return min;
        if (value > max)
            return max;
        return value;
    }
}
