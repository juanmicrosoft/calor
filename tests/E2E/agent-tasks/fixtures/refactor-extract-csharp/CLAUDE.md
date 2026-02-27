## C# Refactoring Reference

This is a C# project. Write code in `.cs` files.

### Method Extraction Guidelines
- Extract methods should be `private` unless specified otherwise
- Preserve parameter validation/preconditions in both methods
- Document preconditions and postconditions as comments
- Use guard clauses for validation

### Code Conventions
- Use C# 14 features where appropriate
- Follow Microsoft naming conventions (PascalCase for methods, camelCase for parameters)
- Keep methods focused on a single responsibility

### Contract Comments
Use comments to document contracts:
```csharp
// Precondition: index >= 0
// Precondition: index < length
// Postcondition: result >= 0
```

### Side Effects
When extracting code with side effects (Console.WriteLine, file access, etc.):
- Consider whether the effect should stay in the original or move to the extracted method
- Document side effects in method comments
