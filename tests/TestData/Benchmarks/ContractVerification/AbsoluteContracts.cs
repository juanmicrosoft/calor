// C# equivalent - no static contract verification
public static class AbsoluteContracts
{
    public static int AbsNonNeg(int x) => x >= 0 ? x : -x;
    public static int AbsIdempotent(int x) => x >= 0 ? x : throw new ArgumentException();
    public static bool TriangleInequality(int absA, int absB, int absSum) => absSum <= absA + absB;
}
