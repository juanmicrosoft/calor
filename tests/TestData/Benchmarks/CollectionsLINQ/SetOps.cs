using System;
using System.Collections.Generic;
using System.Linq;
namespace CollectionsLINQ
{
    public static class SetOps
    {
        public static HashSet<T> Union<T>(HashSet<T> a, HashSet<T> b) { var r = new HashSet<T>(a); r.UnionWith(b); return r; }
        public static HashSet<T> Intersect<T>(HashSet<T> a, HashSet<T> b) { var r = new HashSet<T>(a); r.IntersectWith(b); return r; }
        public static HashSet<T> Difference<T>(HashSet<T> a, HashSet<T> b) { var r = new HashSet<T>(a); r.ExceptWith(b); return r; }
    }
}
