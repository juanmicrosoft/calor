using System;
using System.Collections.Generic;
using System.Linq;
namespace CollectionsLINQ
{
    public static class Reduce
    {
        public static int Sum(List<int> list) => list.Aggregate(0, (acc, x) => acc + x);
        public static int Product(List<int> list) => list.Aggregate(1, (acc, x) => acc * x);
        public static int Max(List<int> list) => list.Aggregate(int.MinValue, (acc, x) => x > acc ? x : acc);
    }
}
