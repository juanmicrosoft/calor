namespace SyntheticLib;

public class FizzBuzz
{
    public static string Evaluate(int n)
    {
        if (n % 15 == 0)
            return "FizzBuzz";
        if (n % 3 == 0)
            return "Fizz";
        if (n % 5 == 0)
            return "Buzz";
        return n.ToString();
    }

    public static string[] Range(int start, int end)
    {
        if (start > end)
            throw new ArgumentException("Start must be less than or equal to end");
        int count = end - start + 1;
        string[] results = new string[count];
        for (int i = 0; i < count; i++)
        {
            results[i] = Evaluate(start + i);
        }
        return results;
    }

    public static int CountFizzes(int start, int end)
    {
        int count = 0;
        for (int i = start; i <= end; i++)
        {
            if (Evaluate(i) == "Fizz")
                count++;
        }
        return count;
    }

    public static int CountBuzzes(int start, int end)
    {
        int count = 0;
        for (int i = start; i <= end; i++)
        {
            if (Evaluate(i) == "Buzz")
                count++;
        }
        return count;
    }
}
