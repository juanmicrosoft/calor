#if FEATURE
public class ConditionalClass { public static int Value() => 1; }
public record ConditionalRecord(int Value);
public delegate int ConditionalDelegate(int value);
public partial class ConditionalPartial { public static int PartialValue() => 10; }
#else
public class ConditionalClass { public static int Value() => 2; }
public sealed class ConditionalRecord
{
    public ConditionalRecord(int value) { Value = value; }
    public int Value { get; }
}
public delegate int ConditionalDelegate(int value);
public partial class ConditionalPartial { public static int PartialValue() => 20; }
#endif
public partial class ConditionalPartial { public static int Shared() => 100; }
public static class ConditionalHarness
{
    public static int Get()
    {
        ConditionalDelegate d = value => value + 1;
        return ConditionalClass.Value()
            + new ConditionalRecord(3).Value
            + d(4)
            + ConditionalPartial.PartialValue()
            + ConditionalPartial.Shared();
    }
}
