using System;
namespace OOPGenerics
{
    public static class GenericConstraints
    {
        public static int Compare<T>(T a, T b) where T : IComparable<T> => a.CompareTo(b);
        public static T MaxOf<T>(T a, T b) where T : IComparable<T> => a.CompareTo(b) >= 0 ? a : b;
        public static T MinOf<T>(T a, T b) where T : IComparable<T> => a.CompareTo(b) <= 0 ? a : b;
    }
}
