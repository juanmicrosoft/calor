public static class LocalFunctionFixture
{
    public static int Add(int x) => x + 1000;

    public static int Run()
    {
        int shadowed = Add(2);
        if (shadowed != 3) return -1;

        int squared = Square(4);
        if (squared != 16) return -2;

        int factorial = Fact(5);
        if (factorial != 120) return -3;

        return shadowed * 10000 + squared * 100 + factorial % 100;

        int Add(int x) => x + 1;

        static int Square(int n)
        {
            return n * n;
        }

        int Fact(int n)
        {
            if (n <= 1) return 1;
            return n * Fact(n - 1);
        }
    }
}
