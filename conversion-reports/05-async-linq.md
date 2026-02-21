# Challenge Report 05: Async/Await and LINQ

## Feature: Async Methods

### Snippet 05-01: Simple Async Method
```csharp
using System.Threading.Tasks;

public class DataService
{
    public async Task<string> FetchDataAsync()
    {
        await Task.Delay(100);
        return "data";
    }
}
```
**Expected:** Async method with §ASYNC tag or equivalent.

## Feature: LINQ

### Snippet 05-02: LINQ Method Syntax
```csharp
using System.Linq;
using System.Collections.Generic;

public class Filter
{
    public List<int> GetPositive(List<int> items)
    {
        return items.Where(x => x > 0).ToList();
    }
}
```
**Expected:** Lambda with §LAM, method chain decomposed.

### Snippet 05-03: LINQ with Select and OrderBy
```csharp
using System.Linq;
using System.Collections.Generic;

public class Transform
{
    public List<string> GetNames(List<string> items)
    {
        return items.Where(s => s.Length > 0)
                    .Select(s => s.ToUpper())
                    .OrderBy(s => s)
                    .ToList();
    }
}
```
**Expected:** Chained LINQ calls decomposed into temp bindings.
