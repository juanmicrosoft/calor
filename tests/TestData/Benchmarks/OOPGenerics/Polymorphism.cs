using System;
namespace OOPGenerics
{
    public interface IShape { double Area(); }
    public class Circle : IShape { double r; public Circle(double r) { this.r = r; } public double Area() => Math.PI * r * r; }
    public class Rect : IShape { double w, h; public Rect(double w, double h) { this.w = w; this.h = h; } public double Area() => w * h; }
    public class Tri : IShape { double b, h; public Tri(double b, double h) { this.b = b; this.h = h; } public double Area() => b * h / 2; }
}
