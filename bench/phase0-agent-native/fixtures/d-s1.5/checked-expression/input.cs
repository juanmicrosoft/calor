public static class CheckedFixture
{
    public static int Run()
    {
        int max = int.MaxValue;
        int big = 300;
        int caughtAdd = 0;
        try
        {
            caughtAdd = checked(max + 1);
        }
        catch (System.OverflowException)
        {
            caughtAdd = 1;
        }
        int caughtCast = 0;
        try
        {
            caughtCast = checked((byte)big);
        }
        catch (System.OverflowException)
        {
            caughtCast = 2;
        }
        int inner = checked(unchecked(max + 1) == int.MinValue ? 4 : 0);
        return caughtAdd * 100 + caughtCast * 10 + inner;
    }
}
