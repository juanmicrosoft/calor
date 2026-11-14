public static class UncheckedFixture
{
    public static int Run()
    {
        int max = int.MaxValue;
        int big = 300;
        int wrapped = checked(unchecked(max + 1) == int.MinValue ? 1 : 0);
        int truncated = checked(unchecked((byte)big) + 0);
        int outer = unchecked(max + 2) == int.MinValue + 1 ? 3 : 0;
        return wrapped * 1000 + truncated * 10 + outer;
    }
}
