using System;
using System.Collections.Generic;
using System.Linq;
namespace CollectionsLINQ
{
    public static class ListOps
    {
        public static int Sum(List<int> list) => list.Sum();
        public static int Count(List<int> list) => list.Count;
        public static int FirstOrDefault(List<int> list, int def) => list.Count > 0 ? list[0] : def;
    }
}
