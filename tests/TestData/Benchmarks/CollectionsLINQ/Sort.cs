using System;
using System.Collections.Generic;
using System.Linq;
namespace CollectionsLINQ
{
    public static class Sort
    {
        public static List<int> Ascending(List<int> list) => list.OrderBy(x => x).ToList();
        public static List<int> Descending(List<int> list) => list.OrderByDescending(x => x).ToList();
        public static bool IsSorted(List<int> list) { for (int i = 1; i < list.Count; i++) if (list[i-1] > list[i]) return false; return true; }
    }
}
