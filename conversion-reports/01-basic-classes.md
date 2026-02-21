# Challenge Report 01: Basic Classes

## Feature: Class Declaration and Methods

### Snippet 01-01: Simple Class with Methods
```csharp
public class Calculator
{
    public int Add(int a, int b) => a + b;
    public int Subtract(int a, int b) => a - b;
}
```
**Expected:** Full conversion with §CL, §MT, §I, §O tags.

### Snippet 01-02: Class with Properties
```csharp
public class Person
{
    public string Name { get; set; }
    public int Age { get; }
    public string Email { get; set; } = "";
}
```
**Expected:** §PROP with §GET/§SET, getter-only property emits only §GET.

### Snippet 01-03: Class with Constructor
```csharp
public class Account
{
    private readonly string _id;
    private decimal _balance;

    public Account(string id, decimal initialBalance)
    {
        _id = id;
        _balance = initialBalance;
    }

    public decimal GetBalance() => _balance;
}
```
**Expected:** §CTOR with §ASSIGN statements, §FLD declarations.

### Snippet 01-04: Static Class with Static Methods
```csharp
public static class MathUtils
{
    public static double Square(double x) => x * x;
    public static double Cube(double x) => x * x * x;
}
```
**Expected:** Static modifier preserved on class and methods.
