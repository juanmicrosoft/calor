// C# equivalent - no static contract verification
public static class ModuloContracts
{
    public static int ModBounded(int a, int b) => a % b;
    public static int ModEvenOdd(int n) => Math.Abs(n % 2);
    public static int WrapAround(int index, int size) => index % size;
}
