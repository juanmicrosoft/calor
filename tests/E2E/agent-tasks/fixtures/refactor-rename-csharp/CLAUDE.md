## C# Refactoring Reference

This is a C# project. Write code in `.cs` files.

### Renaming Guidelines
- When renaming a parameter, update ALL references:
  - In the method body
  - In validation/guard clauses
  - In `nameof()` expressions
  - In XML documentation if present
  - In contract comments

### Code Conventions
- Use C# 14 features where appropriate
- Follow Microsoft naming conventions (PascalCase for methods, camelCase for parameters)

### Contract Comments
Update comments when renaming:
```csharp
// Precondition: newName >= 0  // Update parameter name in comments
// Postcondition: result == newName * 2
```
