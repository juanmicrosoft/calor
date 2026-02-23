using System;
using System.Collections.Generic;
using System.Linq;
namespace CollectionsLINQ
{
    public static class Map
    {
        public static List<int> Double(List<int> list) => list.Select(x => x * 2).ToList();
        public static List<int> Square(List<int> list) => list.Select(x => x * x).ToList();
        public static List<int> Negate(List<int> list) => list.Select(x => -x).ToList();
    }
}
