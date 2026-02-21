# Challenge Report 03: Control Flow

## Feature: If/Else Statements

### Snippet 03-01: Simple If/Else
```csharp
public class Guard
{
    public string Classify(int value)
    {
        if (value > 0)
        {
            return "positive";
        }
        else if (value < 0)
        {
            return "negative";
        }
        else
        {
            return "zero";
        }
    }
}
```
**Expected:** §IF/§EI/§EL/§/IF structure.

## Feature: For/While Loops

### Snippet 03-02: For Loop
```csharp
public class Loops
{
    public int Sum(int n)
    {
        int total = 0;
        for (int i = 0; i < n; i++)
        {
            total += i;
        }
        return total;
    }
}
```
**Expected:** §L loop with proper bounds.

### Snippet 03-03: While Loop
```csharp
public class Loops
{
    public int CountDown(int start)
    {
        int count = 0;
        while (start > 0)
        {
            start--;
            count++;
        }
        return count;
    }
}
```
**Expected:** §WH loop structure.

## Feature: Switch Statement

### Snippet 03-04: Switch Statement
```csharp
public class Router
{
    public string Route(int code)
    {
        switch (code)
        {
            case 200: return "OK";
            case 404: return "Not Found";
            case 500: return "Server Error";
            default: return "Unknown";
        }
    }
}
```
**Expected:** §W/§K/§/W structure with default:.
