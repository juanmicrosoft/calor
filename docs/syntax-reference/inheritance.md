---
layout: default
title: "Inheritance"
parent: Syntax Reference
nav_order: 7
---

# Inheritance

<!-- Generated from website/content/syntax-reference/inheritance.mdx by calor self-check docs --fix. Edit the source, not this file. -->

Calor supports abstract classes, virtual methods and interface implementation.
Use lowercase declaration keywords and indent class members two spaces below
the class header. Declaration IDs belong in `.calor/ids.json`, not in source.

## Class modifiers

Put visibility and modifiers after the class name. Visibility defaults to public.

| Declaration | Meaning |
|:------------|:--------|
| `class Shape abstract` | Cannot be instantiated; may declare abstract members |
| `class FinalService sealed` | Cannot be inherited from |
| `class Service partial` | Participates in a partial type declaration |
| `module MathUtils` inside a file module | Named static container; its functions are implicitly static |

Use a named `module` for a static container, not `class Name static`. The first
`module` line labels the file; a nested module declares the named container.
The following complete program returns `59`.

```calor
module ClassModifiers
  class FinalService sealed
    func Process () → int
      return 42

  class Service partial
    func Read () → int
      return 1

  module MathUtils
    func Square (int:x) → int
      return x * x

  func Main () → int
    ! alloc
    let FinalService:service = new FinalService()
    let Service:partialService = new Service()
    return service.Process() + partialService.Read() + MathUtils.Square(4)
```

## Extending a class

Put `extends BaseName` inside the derived class, before its members.
A variable declared with the base type still dispatches virtual calls to the
derived implementation. This program returns `"Woof!"`, not `"..."`.

```calor
module VirtualDispatch
  class Animal
    func Speak virtual () → string
      return "..."

  class Dog
    extends Animal
    func Speak override () → string
      return "Woof!"

  func Main () → string
    ! alloc
    let Animal:animal = new Dog()
    return animal.Speak()
```

The override emits an ordinary C# virtual override:

```csharp
public class Dog : Animal
{
    public override string Speak()
    {
        return "Woof!";
    }
}
```

## Implementing interfaces

Use one `implements InterfaceName` clause for each interface. A class can extend
one base class and implement multiple interfaces. Interface members declare
signatures without bodies.

The declarations below implement both interfaces and inherit `Animal.Speak`.
The same object can be viewed through its base class and each interface.
This program returns the tuple `("...", "Rex", 7)`.

```calor
module InterfaceInheritance
  interface IPet
    func Name () → string
      !

  interface ITrainable
    func Train () → int
      !

  class Animal
    func Speak virtual () → string
      return "..."

  class Dog
    extends Animal
    implements IPet
    implements ITrainable
    func Name () → string
      return "Rex"
    func Train () → int
      return 7

  func Main () → (string, string, int)
    ! alloc
    let Dog:dog = new Dog()
    let Animal:animal = dog
    let IPet:pet = dog
    let ITrainable:trainee = dog
    return (animal.Speak(), pet.Name(), trainee.Train())
```

### Calls through an interface

An interface call charges the selected declaration's effect row. An
implementation may use fewer effects, but cannot add effects outside that row.
Here `ReadThroughInterface` remains pure; `Main` declares allocation for creating
the object and returns `9`.

```calor
module InterfaceDispatch
  interface IQuiet
    func Read () → int
      !

  class Quiet
    implements IQuiet
    func Read () → int
      !
      return 9

  func ReadThroughInterface (IQuiet:value) → int
    !
    return value.Read()

  func Main () → int
    ! alloc
    return ReadThroughInterface(new Quiet())
```

## Method modifiers

Put method modifiers after the function name and before its parameter list.

| Modifier | Meaning |
|:---------|:--------|
| `virtual` | Supplies an implementation that derived classes may override |
| `override` | Replaces an inherited virtual or abstract implementation |
| `abstract` | Declares a signature without a body in an abstract class |
| `sealed override` | Overrides a method and prevents further overrides |
| `static` | Declares a class method that does not require an instance |

For example, `func Square static (int:x) → int` declares a static method inside
a regular class. Named modules do not need the `static` modifier.

An override must retain the inherited signature. A sealed override remains
callable through the base type. Here `Final` inherits the sealed implementation,
and `Main` returns `42`.

```calor
module SealedOverride
  class Base
    func Compute virtual () → int
      return 1

  class Derived
    extends Base
    func Compute sealed override () → int
      return 42

  class Final
    extends Derived
    func Label () → string
      return "final"

  func Main () → int
    ! alloc
    let Base:value = new Final()
    return value.Compute()
```

An abstract method has no body. A concrete subclass must implement it; see
`Shape.Area` in the complete example below.

## Calling a base implementation

Use ordinary `base.Method(arguments)` syntax inside an instance method.
`this.Method(arguments)` calls a method on the current instance.

This override adds `5` to the base result, so the program returns `15`.

```calor
module BaseMethods
  class Base
    func GetValue virtual () → int
      return 10

  class Derived
    extends Base
    func GetValue override () → int
      return base.GetValue() + 5

  func Main () → int
    ! alloc
    let Base:value = new Derived()
    return value.GetValue()
```

## Base constructors

Write `base(arguments)` as the first clause in a constructor body. It emits a
C# base-constructor initializer, not an ordinary method call. A `this(arguments)`
initializer delegates to another constructor of the same class instead.

The following program returns `"Rex:Collie"`. The base constructor initializes
the name before the derived constructor initializes the breed. The caller
declares `alloc` for construction and `mut` for the field initialization.

```calor
module BaseConstructors
  class Animal
    field string:_name protected
    ctor protected (string:name)
      set this._name name
    func GetName () → string
      return this._name

  class Dog
    extends Animal
    field string:_breed private
    ctor public (string:name, string:breed)
      base(name)
      set this._breed breed
    func Describe () → string
      return "${this.GetName()}:${this._breed}"

  func Main () → string
    ! alloc, mut
    let Dog:dog = new Dog("Rex", "Collie")
    return dog.Describe()
```

The derived constructor emits:

```csharp
public Dog(string name, string breed)
    : base(name)
{
    this._breed = breed;
}
```

## Complete shape hierarchy

`Shape` has an abstract `Area` method and a virtual `Describe` method.
`Circle` supplies the area calculation and passes `"Circle"` to the protected
base constructor. `Demo` calls `Describe` through a `Shape` reference.

The abstract `Area` declaration has a pure effect row (`!`). Calls through
that declaration charge its contract, and an override cannot add effects.
For radius `5.0`, `Demo` returns `("Circle", 78.53975)`. The tuple carries the
name and numeric area without formatting the number as text.

```calor
module ShapeHierarchy
  class Shape abstract
    field string:_name protected
    ctor protected (string:name)
      set this._name name
    func Area abstract () → double
      !
    func Describe virtual () → (string, double)
      return (this._name, this.Area())

  class Circle
    extends Shape
    field double:_radius private
    ctor public (double:radius)
      base("Circle")
      set this._radius radius
    func Area override () → double
      return 3.14159 * (this._radius * this._radius)

  func Demo () → (string, double)
    ! alloc, mut
    let Shape:shape = new Circle(5.0)
    return shape.Describe()
```

## Quick reference

| Task | Native form |
|:-----|:------------|
| Abstract class | `class Shape abstract` |
| Sealed class | `class Final sealed` |
| Explicit public visibility | `class Service public` |
| Extend a class | `extends Shape` |
| Implement an interface | `implements IDrawable` |
| Virtual method | `func Speak virtual () → string` |
| Override method | `func Speak override () → string` |
| Abstract method | `func Area abstract () → double` |
| Sealed override | `func Get sealed override () → int` |
| Static class method | `func Create static () → int` |
| Call a base method | `base.GetValue()` |
| Call an instance method | `this.Process()` |
| Initialize the base class | `base(name)` as the first constructor clause |

## Next

- [Control Flow](https://calor.dev/docs/syntax-reference/control-flow/) - Conditionals and loops
- [Contracts](https://calor.dev/docs/syntax-reference/contracts/) - Preconditions and postconditions
