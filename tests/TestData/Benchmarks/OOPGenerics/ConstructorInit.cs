using System;

namespace OOPGenerics
{
    public class Rectangle
    {
        public double Width { get; }
        public double Height { get; }

        public Rectangle(double width, double height)
        {
            if (width <= 0) throw new ArgumentException("Width must be positive");
            if (height <= 0) throw new ArgumentException("Height must be positive");
            Width = width; Height = height;
        }

        public double Area() => Width * Height;
        public double Perimeter() => 2 * (Width + Height);
    }
}
