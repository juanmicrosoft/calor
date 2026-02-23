// C# equivalent - no effect system
public static class PureComputation
{
    // All functions are pure (no effects)
    public static int Add(int a, int b) => a + b;
    public static int Multiply(int a, int b) => a * b;
    public static int Negate(int x) => -x;
    public static int Square(int x) => x * x;
}
