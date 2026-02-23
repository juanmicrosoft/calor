using System;
namespace OOPGenerics
{
    // C# doesn't have true type aliases for value semantics
    public readonly struct UserId
    {
        public int Value { get; }
        public UserId(int value) { if (value <= 0) throw new ArgumentException(); Value = value; }
        public static implicit operator int(UserId id) => id.Value;
        public UserId Next() => new UserId(Value + 1);
    }
}
