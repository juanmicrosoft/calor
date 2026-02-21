# Challenge Report 09: Known Converter Gaps

These C# constructs are NOT fully supported. The converter should produce
clear diagnostics or graceful fallbacks — never crash.

## Gap: Range Expressions (C# 8+)
```csharp
public class RangeDemo
{
    public int[] Slice(int[] array)
    {
        return array[0..5];
    }
}
```
**Expected:** Graceful fallback or diagnostic, no crash.

## Gap: Index From End (C# 8+)
```csharp
public class IndexDemo
{
    public int GetLast(int[] items)
    {
        return items[^1];
    }
}
```
**Expected:** Graceful fallback or diagnostic, no crash.

## Gap: List Patterns (C# 11+)
```csharp
public class ListPatternDemo
{
    public bool IsFirstAndLast(int[] list)
    {
        return list is [var first, .., var last];
    }
}
```
**Expected:** Graceful fallback or diagnostic, no crash.

## Gap: Raw String Literals (C# 11+)
```csharp
public class RawStringDemo
{
    public string GetJson()
    {
        return \"\"\"\n            {\n                \"name\": \"test\"\n            }\n            \"\"\";
    }
}
```
**Expected:** Graceful fallback or diagnostic, no crash.

## Gap: Collection Spread (C# 12+)
```csharp
using System.Collections.Generic;
using System.Linq;

public class SpreadDemo
{
    public int[] Combine(int[] first, int[] second)
    {
        return [..first, ..second];
    }
}
```
**Expected:** Graceful fallback or diagnostic, no crash.

## Gap: Throw Expression (C# 7+)
```csharp
using System;

public class ThrowExprDemo
{
    private string _name;
    public string Name
    {
        get => _name;
        set => _name = value ?? throw new ArgumentNullException(nameof(value));
    }
}
```
**Expected:** Graceful fallback or diagnostic, no crash.
