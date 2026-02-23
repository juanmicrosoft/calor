// C# equivalent - no static contract verification
public static class BitContracts
{
    public static bool PowerOfTwo(int n) => n > 0 && (n & (n - 1)) == 0;
    public static bool IsAligned(int value, int alignment) => value % alignment == 0;
    public static int ClearLowest(int n) => n & (n - 1);
}
