using System;

namespace DesignPatterns
{
    public abstract class Shape
    {
        public abstract string Type { get; }
        public abstract double CalculateArea();
        public abstract double CalculatePerimeter();
    }

    public class Circle : Shape
    {
        public double Radius { get; }
        public override string Type => "circle";

        public Circle(double radius)
        {
            if (radius <= 0)
                throw new ArgumentException("Radius must be positive");
            Radius = radius;
        }

        public override double CalculateArea()
        {
            return 3.14159 * Radius * Radius;
        }

        public override double CalculatePerimeter()
        {
            return 2.0 * 3.14159 * Radius;
        }
    }

    public class Rectangle : Shape
    {
        public double Width { get; }
        public double Height { get; }
        public override string Type => "rectangle";

        public Rectangle(double width, double height)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentException("Dimensions must be positive");
            Width = width;
            Height = height;
        }

        public override double CalculateArea()
        {
            return Width * Height;
        }

        public override double CalculatePerimeter()
        {
            return 2.0 * (Width + Height);
        }
    }

    public static class ShapeFactory
    {
        public static Shape CreateCircle(double radius)
        {
            return new Circle(radius);
        }

        public static Shape CreateRectangle(double width, double height)
        {
            return new Rectangle(width, height);
        }

        public static Shape CreateSquare(double side)
        {
            return new Rectangle(side, side);
        }

        public static double CalculateArea(Shape s)
        {
            return s.CalculateArea();
        }

        public static double CalculatePerimeter(Shape s)
        {
            return s.CalculatePerimeter();
        }
    }
}
