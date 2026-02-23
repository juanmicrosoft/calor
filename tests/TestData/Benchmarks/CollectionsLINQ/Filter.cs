using System;
using System.Collections.Generic;
using System.Linq;
namespace CollectionsLINQ
{
    public static class Filter
    {
        public static List<int> Positives(List<int> list) => list.Where(x => x > 0).ToList();
        public static List<int> Evens(List<int> list) => list.Where(x => x % 2 == 0).ToList();
        public static List<int> InRange(List<int> list, int min, int max) => list.Where(x => x >= min && x <= max).ToList();
    }
}
