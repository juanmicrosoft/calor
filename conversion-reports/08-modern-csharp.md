# Challenge Report 08: Modern C# Features

## Feature: Required Properties (C# 11+)

### Snippet 08-01: Required Properties
```csharp
public class UserDto
{
    public required string Name { get; set; }
    public required int Age { get; set; }
    public string? Email { get; set; }
}
```
**Expected:** Required modifier preserved as "req" in Calor.

## Feature: Tuple Literals

### Snippet 08-02: Tuple Return Type
```csharp
public class TupleDemo
{
    public (int, string) GetPair() => (42, "hello");
}
```
**Expected:** Tuple literal without ERR.

## Feature: Collection Expressions

### Snippet 08-03: Empty Collection Expression
```csharp
using System.Collections.Generic;

public class Container
{
    public List<string> Items { get; set; } = [];
}
```
**Expected:** §LIST instead of default.

## Feature: Null Coalescing

### Snippet 08-04: Null Coalescing Operator
```csharp
public class NullDemo
{
    public string GetValue(string? input)
    {
        return input ?? "default";
    }
}
```
**Expected:** Conditional expression (if (== input null) "default" input), NOT arithmetic addition.
