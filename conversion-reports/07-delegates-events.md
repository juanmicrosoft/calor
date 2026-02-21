# Challenge Report 07: Delegates and Lambdas

## Feature: Delegate Declarations

### Snippet 07-01: Delegate Types
```csharp
public delegate void MyHandler(int x);
public delegate bool Predicate<T>(T item);
```
**Expected:** §DEL tags with parameter and return types.

## Feature: Lambda Expressions

### Snippet 07-02: Lambda Expressions
```csharp
using System;

public class LambdaDemo
{
    public void Test()
    {
        Func<int, int> doubler = x => x * 2;
        Action<string> printer = msg => Console.WriteLine(msg);
    }
}
```
**Expected:** §LAM syntax with proper parameter types.

### Snippet 07-03: Static Lambda
```csharp
using System;

public class StaticLambdaDemo
{
    public void Test()
    {
        Func<int, int> doubler = static (int x) => x * 2;
    }
}
```
**Expected:** Static lambda preserves static keyword in round-trip.
