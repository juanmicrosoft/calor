// C# equivalent - no static contract verification
public static class SearchContracts
{
    public static int MidIndex(int low, int high)
    {
        // Postcondition: low <= result <= high
        return low + (high - low) / 2;
    }

    public static int NarrowLeft(int mid, int high) => mid + 1;
    public static int NarrowRight(int low, int mid) => mid - 1;
}
