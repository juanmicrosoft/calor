using System;
using System.Collections.Generic;
using System.Linq;
namespace CollectionsLINQ
{
    public static class Zip
    {
        public static List<int> Add(List<int> a, List<int> b) => a.Zip(b, (x, y) => x + y).ToList();
        public static List<int> Multiply(List<int> a, List<int> b) => a.Zip(b, (x, y) => x * y).ToList();
        public static int ZipLength(List<int> a, List<int> b) => Math.Min(a.Count, b.Count);
    }
}
