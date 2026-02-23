using System;
namespace OOPGenerics
{
    public class Engine { public int Horsepower { get; } public Engine(int hp) { Horsepower = hp; } }
    public class Wheels { public int Count { get; } = 4; public int Size { get; } public Wheels(int size) { Size = size; } }
    public class Car
    {
        public Engine Engine { get; }
        public Wheels Wheels { get; }
        public Car(Engine e, Wheels w) { Engine = e; Wheels = w; }
        public double SpeedRatio(double weight) => Engine.Horsepower / weight;
    }
}
