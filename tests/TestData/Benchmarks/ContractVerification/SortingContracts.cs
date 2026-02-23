// C# equivalent - no static contract verification
public static class SortingContracts
{
    public static bool IsSorted(int a, int b) => a <= b;
    public static bool IsPartitioned(int left, int pivot, int right) => left <= pivot && right >= pivot;
    public static int MergePreserves(int lenA, int lenB) => lenA + lenB;
}
