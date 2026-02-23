using System;
namespace ErrorHandling
{
    public struct Option<T>
    {
        private T value;
        private bool hasValue;
        public static Option<T> Some(T val) => new() { value = val, hasValue = true };
        public static Option<T> None => new();
        public bool HasValue => hasValue;
        public T Unwrap() => hasValue ? value : throw new InvalidOperationException("None");
        public T GetOrElse(T fallback) => hasValue ? value : fallback;
    }
}
