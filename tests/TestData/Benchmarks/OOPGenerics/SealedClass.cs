using System;
namespace OOPGenerics
{
    public sealed class ImmutablePoint
    {
        public int X { get; }
        public int Y { get; }
        public ImmutablePoint(int x, int y) { X = x; Y = y; }
        public double DistanceTo(ImmutablePoint other) =>
            Math.Sqrt(Math.Pow(X - other.X, 2) + Math.Pow(Y - other.Y, 2));
    }
}
