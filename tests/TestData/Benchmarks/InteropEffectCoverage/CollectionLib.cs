using System;
using System.Collections.Generic;

public static class CollectionLib
{
    public static int ListLength<T>(List<T> list) => list.Count;
    public static bool IsInBounds<T>(List<T> list, int index) => index >= 0 && index < list.Count;
    public static int AfterAdd<T>(List<T> list, T item) { list.Add(item); return list.Count; }
}
