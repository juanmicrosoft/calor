# Challenge Report 04: Generics and Inheritance

## Feature: Generic Classes

### Snippet 04-01: Generic Class with Constraint
```csharp
public class Box<T> where T : notnull
{
    public T Value { get; set; }
    public Box(T value) { Value = value; }
}
```
**Expected:** Generic type parameter with notnull constraint, §WHERE.

### Snippet 04-02: Generic Method
```csharp
public class Converter
{
    public T Identity<T>(T value) where T : class => value;
}
```
**Expected:** Generic method with class constraint.

## Feature: Inheritance

### Snippet 04-03: Class Inheritance
```csharp
public class Shape
{
    public virtual double Area() => 0;
}

public class Circle : Shape
{
    public double Radius { get; }
    public Circle(double radius) { Radius = radius; }
    public override double Area() => Math.PI * Radius * Radius;
}
```
**Expected:** Class hierarchy with base class, virtual/override methods.

## Feature: Variance

### Snippet 04-04: Covariant and Contravariant Interfaces
```csharp
public interface IProducer<out T>
{
    T Produce();
}
public interface IConsumer<in T>
{
    void Consume(T item);
}
```
**Expected:** out/in variance preserved in Calor output.
