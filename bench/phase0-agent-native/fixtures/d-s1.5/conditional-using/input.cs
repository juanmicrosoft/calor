#if FEATURE
using Number = System.Int32;

public static class ConditionalValue
{
    public static Number Get() => 41;
}
#else
using Number = System.Int32;

public static class ConditionalValue
{
    public static Number Get() => 42;
}
#endif
