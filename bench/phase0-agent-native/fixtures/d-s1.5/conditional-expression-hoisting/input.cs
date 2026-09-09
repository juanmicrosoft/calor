public static class ConditionalExpressionFixture
{
    public static int Run()
    {
        int i = 0;
        bool gate = false;
        bool ignored = gate && i++ > 0;
        if (ignored || i != 0) return -1;

        gate = true;
        bool selectedAnd = gate && i++ == 0;
        if (!selectedAnd || i != 1) return -2;

        bool skippedOr = gate || ++i > 0;
        if (!skippedOr || i != 1) return -3;

        gate = false;
        bool selectedOr = gate || ++i == 2;
        if (!selectedOr || i != 2) return -4;

        gate = true;
        int selected = gate ? i++ : ++i;
        if (selected != 2 || i != 3) return -5;

        gate = false;
        int alternate = gate ? ++i : 9;
        if (alternate != 9 || i != 3) return -6;

        return i * 100 + selected * 10 + alternate;
    }
}
