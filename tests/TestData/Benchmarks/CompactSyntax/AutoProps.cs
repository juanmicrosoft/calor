using System;
namespace CompactSyntax
{
    public class Point
    {
        public int X { get; set; }
        public int Y { get; set; }
        public Point(int x, int y) { X = x; Y = y; }
        public int Sum() => X + Y;
    }
}
