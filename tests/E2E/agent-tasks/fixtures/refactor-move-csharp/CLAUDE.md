## C# Refactoring Reference

This is a C# project with multiple classes. Files are in `src/`.

### Moving Methods Between Classes
When moving a method to another class:
1. Move the entire method including its contract comments
2. Update any callers to reference the new class
3. Consider whether the method should be static or instance
4. Update using statements if moving to a different namespace

### Contract Comments
Move contract comments with the method:
```csharp
// Precondition: b != 0
// Postcondition: result >= 0
```

### Cross-Class Calls
After moving, callers need to reference the new class:
```csharp
var utils = new UtilsModule();
utils.MethodName(arg);
// or if static:
UtilsModule.MethodName(arg);
```

### Code Conventions
- Use C# 14 features where appropriate
- Follow Microsoft naming conventions
