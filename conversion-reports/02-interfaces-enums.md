# Challenge Report 02: Interfaces and Enums

## Feature: Interface Declaration

### Snippet 02-01: Simple Interface
```csharp
public interface IAnimal
{
    string Speak();
    string Name { get; }
}
```
**Expected:** §IFACE with §MT and §PROP tags.

### Snippet 02-02: Interface with Generic Type
```csharp
public interface IRepository<T> where T : class
{
    T GetById(int id);
    void Save(T entity);
    IEnumerable<T> GetAll();
}
```
**Expected:** Generic type parameter with §WHERE constraint.

## Feature: Enum Declaration

### Snippet 02-03: Simple Enum
```csharp
public enum Color
{
    Red,
    Green,
    Blue
}
```
**Expected:** §EN with enum members.

### Snippet 02-04: Enum with Values
```csharp
public enum HttpStatus
{
    Ok = 200,
    NotFound = 404,
    ServerError = 500
}
```
**Expected:** §EN with explicit integer values.
