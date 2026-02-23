using System;
using System.Collections.Generic;
using System.Linq;
namespace CollectionsLINQ
{
    public static class GroupBy
    {
        public static Dictionary<int, List<int>> ByRemainder(List<int> list, int divisor) =>
            list.GroupBy(x => x % divisor).ToDictionary(g => g.Key, g => g.ToList());
        public static Dictionary<int, List<int>> ByBucket(List<int> list, int bucketSize) =>
            list.GroupBy(x => x / bucketSize).ToDictionary(g => g.Key, g => g.ToList());
    }
}
