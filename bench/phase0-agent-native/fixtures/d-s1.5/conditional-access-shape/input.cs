public static class ConditionalAccessFixture
{
    private static int ArgumentCalls;

    private static int Argument()
    {
        ArgumentCalls++;
        return 7;
    }

    public sealed class Holder
    {
        public int Calls;

        public int M(int value)
        {
            Calls++;
            return value;
        }

        public int M7 => 0;
    }

    public static int Run()
    {
        ArgumentCalls = 0;
        Holder target = null;
        int? skipped = target?.M(Argument());
        if (skipped != null || ArgumentCalls != 0) return -1;

        target = new Holder();
        int? selected = target?.M(Argument());
        if (selected != 7 || ArgumentCalls != 1 || target.Calls != 1) return -2;

        return ArgumentCalls * 100 + target.Calls * 10 + (selected ?? 0);
    }
}
