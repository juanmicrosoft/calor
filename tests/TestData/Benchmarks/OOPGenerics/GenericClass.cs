using System;
namespace OOPGenerics
{
    public class Box<T>
    {
        private T? value;
        private bool hasValue;
        public Box() { hasValue = false; }
        public Box(T val) { value = val; hasValue = true; }
        public bool HasValue => hasValue;
        public T Value => hasValue ? value! : throw new InvalidOperationException("No value");
        public T GetOrDefault(T def) => hasValue ? value! : def;
    }
}
