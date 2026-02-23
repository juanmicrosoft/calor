// C# equivalent - no static contract verification
public static class ListInvariant
{
    public static int AddLength(int len) => len + 1;
    public static int RemoveLength(int len) => len > 0 ? len - 1 : throw new InvalidOperationException();
    public static int ConcatLength(int lenA, int lenB) => lenA + lenB;
}
