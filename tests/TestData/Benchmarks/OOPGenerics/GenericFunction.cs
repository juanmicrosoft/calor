using System;
namespace OOPGenerics
{
    public static class GenericFunction
    {
        public static T Identity<T>(T value) => value;
        public static bool AreEqual<T>(T a, T b) where T : IEquatable<T> => a.Equals(b);
        public static T DefaultValue<T>() => default!;
    }
}
