# Calor Inheritance Semantics

This document specifies the semantics of inheritance, abstract classes, and polymorphism in Calor.

---

## 1. Class Modifiers

Classes can have the following modifiers that affect their inheritance behavior:

### 1.1 Abstract Classes (`abstract`)

```calor
module AbstractShapes
  class Shape abstract
    func Area abstract () → double
      !
  class Circle
    extends Shape
    func Area override () → double
      !
      return 3.14159 * 5.0 * 5.0
  func Main () → double
    ! alloc
    let Shape:value = new Circle()
    return value.Area()
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

### 1.2 Sealed Classes (`sealed`)

```calor
module SealedClasses
  class FinalClass sealed
    func Read () → int
      !
      return 42
  func Main () → int
    ! alloc
    return new FinalClass().Read()
```

**Semantics:**
- Sealed classes cannot be inherited from
- Useful for optimization and design intent
- Can implement interfaces but cannot be extended

**Generated C#:**
```csharp
public sealed class FinalClass { }
```

### 1.3 Static Containers and Methods

Use a nested named `module` for a static container. The first module labels
the file; it does not introduce that container. A regular class can instead
declare individual static methods.

```calor
module StaticUtilities
  module Utilities
    func Helper () → int
      !
      return 21
  class Service
    func Helper static () → int
      !
      return 21
  func Main () → int
    !
    return Utilities.Helper() + Service.Helper()
```

**Semantics:**
- Static methods belong to the class, not instances
- Named modules emit static containers and cannot participate in inheritance
- A regular class containing a static method is still a regular class

**Generated C#:**
```csharp
public class Utilities
{
    public static void Helper() { }
}
```

### 1.4 Partial Classes (`partial`)

The native parser retains the partial modifier. Combining declarations still
requires the ordinary C# rules and compatible compilation context.

```calor
module PartialService
  class DataService partial
    func Read () → int
      !
      return 42
  func Main () → int
    ! alloc
    return new DataService().Read()
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

### 2.1 Base Class (`extends`)

```calor-fragment
class Circle
  extends Shape
  // ...
```

**Semantics:**
- Specifies single inheritance from a base class
- A class has one base class
- Derived class inherits all accessible members from base
- Base class must be accessible and not sealed

**Syntax:** `extends BaseClassName`

### 2.2 Interface Implementation (`implements`)

```calor-fragment
class GameObject
  implements IMovable
  implements IDrawable
  // ...
```

**Semantics:**
- Class commits to implementing all interface members
- Multiple interfaces can be implemented
- Interface members must be implemented publicly
- Explicit interface implementation is supported

**Syntax:** `implements InterfaceName`

### 2.3 Combined Inheritance

```calor
module CombinedInheritance
  interface IPet
    func Name () → string
      !
  interface ITrainable
    func Train () → int
      !
  class Animal
    func Speak virtual () → string
      !
      return "..."
  class Dog
    extends Animal
    implements IPet
    implements ITrainable
    func Name () → string
      !
      return "Rex"
    func Train () → int
      !
      return 7
  func Main () → string
    ! alloc
    let Dog:dog = new Dog()
    let Animal:animal = dog
    let IPet:pet = dog
    let ITrainable:trainee = dog
    return "${animal.Speak()}:${pet.Name()}:${trainee.Train()}"
```

**Generated C#:**
```csharp
public class Dog : Animal, IPet, ITrainable { ... }
```

---

## 3. Method Modifiers

### 3.1 Virtual Methods (`virtual`)

```calor-fragment
func Speak virtual () → string
  return "..."
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

### 3.2 Override Methods (`override`)

```calor
module MethodDispatch
  class Animal
    func Speak virtual () → string
      !
      return "..."
  class Dog
    extends Animal
    func Speak override () → string
      !
      return "Woof!"
  func Main () → string
    ! alloc
    let Animal:animal = new Dog()
    return animal.Speak()
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

### 3.3 Abstract Methods (`abstract`)

```calor-fragment
func Area abstract () → double
  !
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

### 3.4 Sealed Override (`sealed override`)

```calor
module SealedOverrides
  class Base
    func Compute virtual () → int
      !
      return 1
  class Derived
    extends Base
    func Compute sealed override () → int
      !
      return 42
  class Final
    extends Derived
  func Main () → int
    ! alloc
    let Base:value = new Final()
    return value.Compute()
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

### 3.5 Static Methods (`static`)

```calor-fragment
func Create static () → MyClass
  ! alloc
  return new MyClass()
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
module BaseMethods
  class Base
    func GetValue virtual () → int
      !
      return 10
  class Derived
    extends Base
    func GetValue override () → int
      !
      return base.GetValue() + 5
  func Main () → int
    ! alloc
    let Base:value = new Derived()
    return value.GetValue()
```

Use ordinary `base.Method(arguments)` and `this.Method(arguments)` calls.
The separate `base(arguments)` clause is a constructor initializer.

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
module BaseConstructors
  class Animal
    field string:_name protected
    ctor protected (string:name)
      set this._name name
    func GetName () → string
      !
      return this._name
  class Dog
    extends Animal
    field string:_breed private
    ctor public (string:name, string:breed)
      base(name)
      set this._breed breed
    func Describe () → string
      !
      return "${this.GetName()}:${this._breed}"
  func Main () → string
    ! alloc, mut
    return new Dog("Rex", "Collie").Describe()
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

The substitution rule is that derived methods may **weaken**, but not
strengthen, their inherited preconditions:

```calor
module WeakerPreconditions
  class Base
    func Process virtual (int:value) → int
      !
      requires value > 0
      return value * 2
  class Derived
    extends Base
    func Process override (int:value) → int
      !
      requires value >= 0
      return value * 2
  func Main () → int
    ! alloc
    let Base:value = new Derived()
    return value.Process(2)
```

### 5.2 Postcondition Strengthening

Derived classes may **strengthen** (but not weaken) postconditions:

```calor
module StrongerPostconditions
  class Base
    func GetValue virtual () → int
      !
      => result >= 0
      return 10
  class Derived
    extends Base
    func GetValue override () → int
      !
      => result > 0
      return 42
  func Main () → int
    ! alloc
    let Base:value = new Derived()
    return value.GetValue()
```

### 5.3 Liskov Substitution Principle

These are substitution obligations, not a proof of every object lifecycle:

1. **Preconditions**: Cannot demand more than base class
2. **Postconditions**: Cannot promise less than base class
3. **Invariants**: Must maintain all base class invariants

The contract-inheritance checker resolves supported source relationships and
checks the corresponding implications. Unsupported or assumption-dependent
verification is not established validity. Supported effective inherited
conditions participate in runtime checking and effect accounting. An
implementation with no own conditions can still execute inherited guards.
This does not make arbitrary source predicates statically provable.
Module invariants currently emit diagnostic comments, not automatic checks
at every method entry and exit.

---

## 6. Visibility in Inheritance

### 6.1 Visibility Modifiers

| Modifier | Syntax | Inherited Access |
|----------|--------|------------------|
| Public | `public` | Accessible everywhere |
| Protected internal | `protected internal` | Accessible in derived classes or the same assembly |
| Internal | `internal` | Accessible in same assembly |
| Protected | `protected` | Accessible in derived classes |
| Private protected | `private protected` | Accessible in derived classes in the same assembly |
| Private | `private` | Not accessible in derived classes |

### 6.2 Method Visibility Rules

**Overriding:**
- Overrides must obey C# accessibility rules; they cannot arbitrarily change visibility
- `public` in base → must be `public` in derived
- `protected` in base → `protected` in derived, not `public`

**Example:**
```calor-fragment
// Base class
func Method protected virtual () → void
  return

// Derived - same accessibility
func Method protected override () → void
  return
```

---

## 7. Polymorphism Rules

### 7.1 Static vs Dynamic Dispatch

- **Static Dispatch**: Non-virtual methods resolved at compile time
- **Dynamic Dispatch**: Virtual/override methods resolved at runtime

### 7.2 Type Covariance

Return types support covariance:

```calor
module CovariantReturns
  class Animal
    func Clone virtual () → Animal
      !
      return this
  class Dog
    extends Animal
    func Clone override () → Dog
      !
      return this
  func Main () → bool
    ! alloc
    let Animal:animal = new Dog()
    return animal.Clone() is Dog
```

---

## 8. Known Limitations

### 8.1 Class Modifiers

- **Static containers**: Use named modules, not a static class modifier.
- **Partial classes**: Supported syntax does not supply missing source files or
  make independently compiled declarations one assembly.

### 8.2 Type Mapping

Use C# type spellings, including `double` and `float`. Historical internal
aliases are not current source guidance.

### 8.3 Method Call Syntax

- **Base/this calls**: Use ordinary native postfix syntax:
  ```calor-fragment
  base.GetValue()
  this.Process(data)
  ```

### 8.4 Modifier Syntax

- **Multiple modifiers**: Separate full keywords by spaces before the signature:
  ```calor-fragment
  func Method public sealed override () → int
    return 42
  ```

---

## References

- AST Definitions: `src/Calor.Compiler/Ast/ClassNodes.cs`
- Parser Implementation: `src/Calor.Compiler/Parsing/Parser.cs`
- Code Generation: `src/Calor.Compiler/CodeGen/CSharpEmitter.cs`
- Contract inheritance: `src/Calor.Compiler/Verification/ContractInheritanceChecker.cs`
- Test Suite: `tests/Calor.Compiler.Tests/InheritanceTests.cs`
