## C# Refactoring Reference

This is a C# project. Write code in `.cs` files.

### Signature Changes
When changing a method signature:
1. **Add parameter**: Add new parameter with optional default value
2. **Remove parameter**: Remove from signature (may break callers)
3. **Change return type**: Update return type and all return statements
4. **Reorder parameters**: Change parameter order

### Updating Call Sites
After signature changes, update all callers to match:
- Provide arguments in new order
- Add/remove arguments as needed
- May need to update contract comments

### Return Type Changes
When changing return type to nullable/optional:
```csharp
// Before: public int TryParse(string input)
// After:  public int? TryParse(string input)
```

### Contract Comments
Update when signature changes:
```csharp
// Precondition: newParam >= 0  // Add for new parameters
// Postcondition: result != null  // Update if return type changes
```

### Code Conventions
- Use C# 14 features where appropriate
- Consider adding default parameter values for backwards compatibility
- Update XML documentation if present
