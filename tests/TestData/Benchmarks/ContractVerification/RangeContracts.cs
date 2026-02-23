// C# equivalent - no static contract verification
public static class RangeContracts
{
    public static int ClampByte(int value)
    {
        if (value < 0) return 0;
        if (value > 255) return 255;
        return value;
    }

    public static int Percentage(int part, int whole)
    {
        // Precondition: whole > 0, 0 <= part <= whole
        // Postcondition: 0 <= result <= 100
        return part * 100 / whole;
    }

    public static int MapRange(int value, int inMin, int inMax, int outMin, int outMax)
    {
        return outMin + (value - inMin) * (outMax - outMin) / (inMax - inMin);
    }
}
