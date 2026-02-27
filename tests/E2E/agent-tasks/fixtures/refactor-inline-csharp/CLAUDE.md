## C# Refactoring Reference

This is a C# project. Write code in `.cs` files.

### Inlining Guidelines
When inlining a method call:
1. Replace the call with the method body
2. Substitute parameters with actual arguments
3. Consider validation/contracts:
   - Validation from inlined method becomes validation at call site
   - Update contract comments to reflect the change
4. You may delete the inlined method if it's no longer called elsewhere

### Contract Comments
```csharp
// Precondition: min <= max
// Postcondition: result >= 0
```

### Code Conventions
- Use C# 14 features where appropriate
- Keep code readable - don't inline if it makes code harder to understand
