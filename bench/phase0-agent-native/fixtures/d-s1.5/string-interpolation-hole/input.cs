public static class InterpolationHole
{
    public static int Verify()
    {
        int[] items = { 3, 5, 8 };
        int i = 1;
        string s = $"[{items[i]}|{items[i + 1]}]";
        return s == "[5|8]" ? 58 : -1;
    }
}
