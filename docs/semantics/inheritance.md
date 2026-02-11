# Calor Inheritance Semantics

This document specifies the semantics of inheritance, abstract classes, and polymorphism in Calor.

---

## 1. Class Modifiers

Classes can have the following modifiers that affect their inheritance behavior:

### 1.1 Abstract Classes (`abs`)

```calor
§CL{c1:Shape:abs}
  §MT{mt1:Area:pub:abs}
    §O{double}
  §/MT{mt1}
§/CL{c1}
```

**Semantics:**
- Abstract classes cannot be instantiated directly
- May contain abstract methods (methods without implementation)
- May contain non-abstract methods with implementations
- Derived classes must implement all abstract methods or be abstract themselves

**Generated C#:**
```csharp
public abstract class Shape
{
    public abstract double Area();
}
```

### 1.2 Sealed Classes (`seal`)

```calor
§CL{c1:FinalClass:seal}
§/CL{c1}
```

**Semantics:**
- Sealed classes cannot be inherited from
- Useful for optimization and design intent
- Can implement interfaces but cannot be extended

**Generated C#:**
```csharp
public sealed class FinalClass { }
```

### 1.3 Static Classes (`stat`)

> **Note:** Static class modifier is not fully implemented in the parser. Use static methods within regular classes instead.

```calor
§CL{c1:Utilities:pub}
  §MT{mt1:Helper:pub:stat}
    §O{void}
  §/MT{mt1}
§/CL{c1}
```

**Semantics:**
- Static methods belong to the class, not instances
- All members should be static in utility classes
- Cannot be inherited from or inherit from other classes

**Generated C#:**
```csharp
public class Utilities
{
    public static void Helper() { }
}
```

### 1.4 Partial Classes (`partial`)

> **Note:** Partial class modifier is not fully implemented in the parser.

```calor
§CL{c1:DataService:partial}
§/CL{c1}
```

**Semantics:**
- Class definition can be split across multiple files
- All parts are combined at compile time
- All parts must use the `partial` modifier

**Generated C#:**
```csharp
public partial class DataService { }
```

---

## 2. Inheritance Declaration

### 2.1 Base Class (`§EXT`)

```calor
§CL{c1:Circle:pub}
  §EXT{Shape}
  // ...
§/CL{c1}
```

**Semantics:**
- Specifies single inheritance from a base class
- Only one `§EXT` declaration is allowed per class
- Derived class inherits all accessible members from base
- Base class must be accessible and not sealed

**Syntax:** `§EXT{BaseClassName}`

### 2.2 Interface Implementation (`§IMPL`)

```calor
§CL{c1:GameObject:pub}
  §IMPL{IMovable}
  §IMPL{IDrawable}
  // ...
§/CL{c1}
```

**Semantics:**
- Class commits to implementing all interface members
- Multiple interfaces can be implemented
- Interface members must be implemented publicly
- Explicit interface implementation is supported

**Syntax:** `§IMPL{InterfaceName}`

### 2.3 Combined Inheritance

```calor
§CL{c1:Dog:pub}
  §EXT{Animal}
  §IMPL{IPet}
  §IMPL{ITrainable}
  // ...
§/CL{c1}
```

**Generated C#:**
```csharp
public class Dog : Animal, IPet, ITrainable { ... }
```

---

## 3. Method Modifiers

### 3.1 Virtual Methods (`virt`)

```calor
§MT{mt1:Speak:pub:virt}
  §O{str}
  §R "..."
§/MT{mt1}
```

**Semantics:**
- Method can be overridden in derived classes
- Has a default implementation in the base class
- Polymorphic dispatch at runtime

**Generated C#:**
```csharp
public virtual string Speak()
{
    return "...";
}
```

### 3.2 Override Methods (`over`)

```calor
§MT{mt1:Speak:pub:over}
  §O{str}
  §R "Woof!"
§/MT{mt1}
```

**Semantics:**
- Provides new implementation for inherited virtual/abstract method
- Must match the signature of the base method
- Participates in polymorphic dispatch

**Generated C#:**
```csharp
public override string Speak()
{
    return "Woof!";
}
```

### 3.3 Abstract Methods (`abs`)

```calor
§MT{mt1:Area:pub:abs}
  §O{double}
§/MT{mt1}
```

**Semantics:**
- Declares method signature without implementation
- Must be in an abstract class
- Must be implemented by non-abstract derived classes
- No method body is allowed

**Generated C#:**
```csharp
public abstract double Area();
```

### 3.4 Sealed Override (`seal over`)

```calor
§MT{mt1:Compute:pub:seal over}
  §O{i32}
  §R 42
§/MT{mt1}
```

**Semantics:**
- Overrides a virtual method
- Prevents further overriding in derived classes
- Combines `sealed` and `override` modifiers (space-separated)

**Generated C#:**
```csharp
public override sealed int Compute()
{
    return 42;
}
```

### 3.5 Static Methods (`stat`)

```calor
§MT{mt1:Create:pub:stat}
  §O{MyClass}
  §R §NEW{MyClass}
§/MT{mt1}
```

**Semantics:**
- Method belongs to the class, not instances
- Cannot be virtual or override
- Called on the class type, not objects

---

## 4. Base Calls

### 4.1 Base Method Calls

To call a base class method, use `base.MethodName` as the call target:

```calor
§MT{mt1:GetValue:pub:over}
  §O{i32}
  §R (+ §C{base.GetValue} §/C 5)
§/MT{mt1}
```

> **Important:** Use lowercase `base.Method` inside `§C{...}`, not `§BASE.Method`. The `§BASE` token is only used for constructor initializers.

**Semantics:**
- Accesses base class implementation
- Used to extend rather than replace behavior
- Can only be used in derived classes

**Generated C#:**
```csharp
public override int GetValue()
{
    return base.GetValue() + 5;
}
```

### 4.2 Constructor Base Initializer

```calor
§CTOR{ctor1:pub}
  §I{str:name}
  §I{str:breed}
  §BASE §A name §/BASE
  §ASSIGN §THIS._breed breed
§/CTOR{ctor1}
```

**Semantics:**
- Calls base class constructor before derived constructor body
- Arguments must match a base class constructor signature
- Required when base class has no parameterless constructor

**Generated C#:**
```csharp
public Dog(string name, string breed)
    : base(name)
{
    this._breed = breed;
}
```

---

## 5. Contract Inheritance (LSP Rules)

### 5.1 Precondition Weakening

Derived classes may **weaken** (but not strengthen) preconditions:

```calor
// Base class
§MT{mt1:Process:pub:virt}
  §I{i32:value}
  §O{i32}
  §REQ (> value 0)    // Requires positive
  §R (* value 2)
§/MT{mt1}

// Derived class - OK: accepts wider range
§MT{mt1:Process:pub:over}
  §I{i32:value}
  §O{i32}
  §REQ (>= value 0)   // Also accepts zero
  §R (* value 2)
§/MT{mt1}
```

### 5.2 Postcondition Strengthening

Derived classes may **strengthen** (but not weaken) postconditions:

```calor
// Base class
§MT{mt1:GetValue:pub:virt}
  §O{i32}
  §ENS (>= result 0)   // Ensures non-negative
  §R 10
§/MT{mt1}

// Derived class - OK: guarantees stronger condition
§MT{mt1:GetValue:pub:over}
  §O{i32}
  §ENS (> result 0)    // Ensures strictly positive
  §R 42
§/MT{mt1}
```

### 5.3 Liskov Substitution Principle

These rules ensure that objects of derived classes can substitute for objects of base classes without breaking correctness:

1. **Preconditions**: Cannot demand more than base class
2. **Postconditions**: Cannot promise less than base class
3. **Invariants**: Must maintain all base class invariants

---

## 6. Visibility in Inheritance

### 6.1 Visibility Modifiers

| Modifier | Syntax | Inherited Access |
|----------|--------|------------------|
| Public | `pub` | Accessible everywhere |
| Protected | `pro` | Accessible in derived classes |
| Private | `pri` | Not accessible in derived classes |
| Internal | `int` | Accessible in same assembly |

### 6.2 Method Visibility Rules

**Overriding:**
- Override cannot reduce visibility
- `public` in base → must be `public` in derived
- `protected` in base → can be `protected` or `public` in derived

**Example:**
```calor
// Base class
§MT{mt1:Method:pro:virt}
  §O{void}
§/MT{mt1}

// Derived - OK: increasing visibility
§MT{mt1:Method:pub:over}
  §O{void}
§/MT{mt1}
```

---

## 7. Polymorphism Rules

### 7.1 Static vs Dynamic Dispatch

- **Static Dispatch**: Non-virtual methods resolved at compile time
- **Dynamic Dispatch**: Virtual/override methods resolved at runtime

### 7.2 Type Covariance

Return types support covariance:

```calor
// Base class returns Animal
§MT{mt1:Clone:pub:virt}
  §O{Animal}
§/MT{mt1}

// Derived can return Dog (more specific)
§MT{mt1:Clone:pub:over}
  §O{Dog}
§/MT{mt1}
```

---

## 8. Known Limitations

### 8.1 Class Modifiers

- **Static class modifier (`stat`)**: Not fully implemented in the parser. The `isStatic` flag is always `false`. Use static methods within regular classes as a workaround.

- **Partial class modifier (`partial`)**: Not fully implemented in the parser. The `isPartial` flag is always `false`.

### 8.2 Type Mapping

- **`f64` type**: Due to internal type expansion, `f64` may not correctly map to `double`. Use `double` directly for reliable results.

- **`f32` type**: Similarly, use `float` directly instead of `f32`.

### 8.3 Method Call Syntax

- **Base/This in call targets**: The `§BASE` and `§THIS` tokens cannot be used inside `§C{...}` call targets. Use lowercase `base.Method` and `this.Method` instead:
  ```calor
  // Correct:
  §C{base.GetValue} §/C
  §C{this.Process} §A data §/C

  // Incorrect (will not parse):
  §C{§BASE.GetValue} §/C
  §C{§THIS.Process} §A data §/C
  ```

### 8.4 Modifier Syntax

- **Multiple modifiers**: When combining modifiers (e.g., `seal` and `over`), use space separation in the modifiers field, not colon separation:
  ```calor
  // Correct:
  §MT{mt1:Method:pub:seal over}

  // Incorrect:
  §MT{mt1:Method:pub:seal:over}
  ```

---

## References

- AST Definitions: `src/Calor.Compiler/Ast/ClassNodes.cs`
- Parser Implementation: `src/Calor.Compiler/Parsing/Parser.cs:3799-4211`
- Code Generation: `src/Calor.Compiler/CodeGen/CSharpEmitter.cs:1473-1643`
- Test Suite: `tests/Calor.Compiler.Tests/InheritanceTests.cs`
