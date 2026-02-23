using System;

namespace OOPGenerics
{
    public interface IShape { double Area(); string Name { get; } }

    public class Circle : IShape
    {
        public double Radius { get; }
        public Circle(double radius) { Radius = radius; }
        public double Area() => Math.PI * Radius * Radius;
        public string Name => "Circle";
    }

    public class Square : IShape
    {
        public double Side { get; }
        public Square(double side) { Side = side; }
        public double Area() => Side * Side;
        public string Name => "Square";
    }

    public class Triangle : IShape
    {
        public double Base { get; }
        public double Height { get; }
        public Triangle(double b, double h) { Base = b; Height = h; }
        public double Area() => Base * Height / 2;
        public string Name => "Triangle";
    }
}
