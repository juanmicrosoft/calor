# Challenge Report 06: Pattern Matching and Expressions

## Feature: Switch Expressions (Supported)

### Snippet 06-01: Simple Switch Expression
```csharp
public class Grader
{
    public string GetGrade(int score) => score switch
    {
        >= 90 => "A",
        >= 80 => "B",
        >= 70 => "C",
        _ => "F"
    };
}
```
**Expected:** MatchExpressionNode with relational patterns.

### Snippet 06-02: Type Pattern Switch
```csharp
public class TypeSwitch
{
    public string Describe(object obj) => obj switch
    {
        string s => $"string: {s}",
        int n => $"int: {n}",
        _ => "unknown"
    };
}
```
**Expected:** Type patterns with variable binding.

## Feature: Declaration Pattern

### Snippet 06-03: Is Pattern with Variable Binding
```csharp
public class PatternDemo
{
    public int GetLength(object value)
    {
        if (value is string text)
        {
            return text.Length;
        }
        return 0;
    }
}
```
**Expected:** (is value str) check with §B for cast.

## Feature: Pattern Combinators

### Snippet 06-04: Not/Or/And Patterns
```csharp
public class Combinators
{
    public string Classify(object obj)
    {
        if (obj is not null)
            return "has value";
        return "null";
    }

    public string Range(int x) => x switch
    {
        > 0 and < 100 => "in range",
        1 or 2 or 3 => "low",
        _ => "other"
    };
}
```
**Expected:** Pattern combinators preserved in Calor syntax.
