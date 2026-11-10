# Calor Normal Form (CNF) Specification

Design revision: 1.0.0

This document specifies the Calor Normal Form (CNF), an intermediate representation that makes evaluation semantics explicit.

**Status: unintegrated design.** The production compiler calls
`CSharpEmitter.Emit(ast)` directly after analyses; it does not lower through
CNF. The pipeline and guarantees below describe design goals, not guarantees
established by the shipped compiler.

---

## 1. Overview

### 1.1 The Problem: Backend-Dependent Semantics

When compiling directly to a backend language (like C#), semantic decisions can become implicit:

```text
result = f(a(), b()) + c()
```

Direct compilation to C# might produce:
```csharp
var result = f(a(), b()) + c();
```

C# specifies left-to-right argument and operand evaluation, so direct emission
can rely on that rule. A different backend would need its own conforming
implementation; a hypothetical change is not evidence that C# order is
unspecified.

**CNF prevents this by making every semantic decision explicit in the IR.**

### 1.2 Purpose

CNF is an intermediate representation (IR) between the Calor AST and backend code generation. Its purpose is to:

1. **Make evaluation order explicit** - Temporaries enforce left-to-right evaluation
2. **Introduce explicit temporaries** - All intermediate values have names
3. **Linearize control flow** - Branch/label/goto instead of structured control
4. **Remove implicit conversions** - All conversions are explicit nodes

The design aims to enforce the modeled rules before backend emission. That
requires complete, validated lowering and backend integration; neither follows
merely from having a CNF representation.

### 1.3 Proposed Pipeline Position (Not the Production Path)

```
Production: Source → Lexer/Parser → AST → Binder/Checks/Analysis → C# Emitter → C#
Prototype:  AST → CnfLowering → CNF → CnfValidator / CnfPrettyPrinter
```

### 1.4 Design Principles

- **Explicit over implicit**: Every semantic operation is a visible node
- **Flat structure**: No deeply nested expressions
- **Backend agnostic**: Same CNF serves all backends
- **Verifiable**: CNF can be validated against semantics spec

---

## 2. CNF Node Types

### 2.1 Expressions (Atomic)

The design requires operation operands to be atomic: literals or references
to previous results. Binary, unary and call nodes are operations, not themselves
atomic operands. The C# shapes below are schematic design excerpts, not exact
constructor/API definitions; see the implementation files for those signatures.

The retained prototype skips some unsupported statements and substitutes
placeholders for some unsupported expressions. `ExpressionCallNode` explicitly
throws `NotSupportedException`. This prototype is not authority for accepting
arbitrary native source or claiming equivalent executable output.

#### CnfLiteral

Represents a constant value.

```csharp
public sealed class CnfLiteral : CnfExpression
{
    public object Value { get; }           // The literal value
    public CnfType Type { get; }           // INT, FLOAT, BOOL, STRING
}
```

**Examples:**
- `CnfLiteral(42, INT)`
- `CnfLiteral(3.14, FLOAT)`
- `CnfLiteral(true, BOOL)`
- `CnfLiteral("hello", STRING)`

#### CnfVariableRef

References a variable by name.

```csharp
public sealed class CnfVariableRef : CnfExpression
{
    public string Name { get; }
    public CnfType Type { get; }
}
```

#### CnfBinaryOp

Binary operation on two atomic operands.

```csharp
public sealed class CnfBinaryOp : CnfExpression
{
    public CnfBinaryOperator Operator { get; }
    public CnfExpression Left { get; }     // Must be atomic
    public CnfExpression Right { get; }    // Must be atomic
    public CnfType ResultType { get; }
}
```

**Operators:** Add, Subtract, Multiply, Divide, Modulo, Power, Equal, NotEqual, LessThan, LessOrEqual, GreaterThan, GreaterOrEqual, BitwiseAnd, BitwiseOr, BitwiseXor, LeftShift, RightShift

**Note:** `And` and `Or` are NOT binary ops in CNF - they are lowered to control flow.

#### CnfUnaryOp

Unary operation on an atomic operand.

```csharp
public sealed class CnfUnaryOp : CnfExpression
{
    public CnfUnaryOperator Operator { get; }
    public CnfExpression Operand { get; }  // Must be atomic
    public CnfType ResultType { get; }
}
```

**Operators:** Negate, Not, BitwiseNot

#### CnfCall

Function call with atomic arguments.

```csharp
public sealed class CnfCall : CnfExpression
{
    public string FunctionName { get; }
    public IReadOnlyList<CnfExpression> Arguments { get; }  // All must be atomic
    public CnfType ReturnType { get; }
}
```

#### CnfConversion

Explicit type conversion.

```csharp
public sealed class CnfConversion : CnfExpression
{
    public CnfExpression Operand { get; }  // Must be atomic
    public CnfType FromType { get; }
    public CnfType ToType { get; }
    public ConversionKind Kind { get; }    // Implicit, Explicit, Checked
}
```

---

### 2.2 Statements

#### CnfAssign

Assigns a value to a variable (introduces temporaries).

```csharp
public sealed class CnfAssign : CnfStatement
{
    public string Target { get; }          // Variable name
    public CnfExpression Value { get; }    // Expression to assign
}
```

#### CnfSequence

A sequence of statements executed in order.

```csharp
public sealed class CnfSequence : CnfStatement
{
    public IReadOnlyList<CnfStatement> Statements { get; }
}
```

#### CnfBranch

Conditional branch.

```csharp
public sealed class CnfBranch : CnfStatement
{
    public CnfExpression Condition { get; }  // Must be atomic BOOL
    public string TrueLabel { get; }
    public string FalseLabel { get; }
}
```

#### CnfLoop (Design Only)

Proposed unconditional loop construct. The retained implementation uses
branches and labels instead and has no `CnfLoop` node.

```csharp
public sealed class CnfLoop : CnfStatement
{
    public string HeaderLabel { get; }
    public string BodyLabel { get; }
    public string ExitLabel { get; }
}
```

#### CnfReturn

Returns a value from a function.

```csharp
public sealed class CnfReturn : CnfStatement
{
    public CnfExpression? Value { get; }   // Null for void functions
}
```

#### CnfThrow

Throws an exception.

```csharp
public sealed class CnfThrow : CnfStatement
{
    public CnfExpression Exception { get; }
}
```

#### CnfLabel

A labeled point in the code.

```csharp
public sealed class CnfLabel : CnfStatement
{
    public string Name { get; }
}
```

#### CnfGoto

Unconditional jump to a label.

```csharp
public sealed class CnfGoto : CnfStatement
{
    public string Target { get; }
}
```

#### CnfTry

Try/catch/finally block.

```csharp
public sealed class CnfTry : CnfStatement
{
    public CnfSequence TryBody { get; }
    public IReadOnlyList<CnfCatchClause> CatchClauses { get; }
    public CnfSequence? FinallyBody { get; }
}

public sealed class CnfCatchClause
{
    public string? ExceptionType { get; }
    public string? VariableName { get; }
    public CnfSequence Body { get; }
}
```

---

## 3. Lowering Rules

### 3.1 Expression Lowering

Every complex expression is decomposed into a sequence of assignments to temporaries.

#### Nested Expressions

**Source:**
```calor
module NormalBinary
  func Value (int:a, int:b, int:c) → int
    !
    return a * b + c
```

**CNF:**
```
t1 = CnfBinaryOp(Multiply, a, b, INT)
t2 = CnfBinaryOp(Add, t1, c, INT)
return t2
```

#### Function Call Arguments

**Source:**
```calor
module NormalCalls
  func Mark (int:value) → int
    ! cw
    System.Console.Write(value)
    return value
  func Combine (int:a, int:b, int:c) → int
    !
    return a * 100 + b * 10 + c
  func Main () → int
    ! cw
    return Combine(Mark(1), Mark(2), Mark(3))
```

**CNF:**
```
t1 = CnfCall("a", [])
t2 = CnfCall("b", [])
t3 = CnfCall("c", [])
t4 = CnfCall("f", [t1, t2, t3])
```

The diagram names the arguments schematically. For `NormalCalls`, those
temporaries hold `Mark(1)`, `Mark(2)` and `Mark(3)`, respectively, before
`Combine` runs. On the production path, `Main()` writes `123` once and returns
`123`. Running that source does not establish complete CNF lowering coverage.

### 3.2 Short-Circuit Lowering

Logical operators are lowered to control flow.

#### Logical AND

**Source:**
```calor
module NormalAnd
  func Both (bool:left, bool:right) → bool
    !
    return left && right
```

**CNF:**
```
    t_result = false
    branch A -> then_block, end_block
then_block:
    t_result = B
    goto end_block
end_block:
    // t_result contains result
```

**Expanded (with labels):**
```
CnfAssign("t_result", CnfLiteral(false, BOOL))
CnfBranch(CnfVariableRef("A"), "then_and", "end_and")
CnfLabel("then_and")
CnfAssign("t_result", CnfVariableRef("B"))
CnfGoto("end_and")
CnfLabel("end_and")
// Result in t_result
```

#### Logical OR

**Source:**
```calor
module NormalOr
  func Either (bool:left, bool:right) → bool
    !
    return left || right
```

**CNF:**
```
    t_result = true
    branch A -> end_block, else_block
else_block:
    t_result = B
    goto end_block
end_block:
    // t_result contains result
```

### 3.3 Conditional Expression Lowering

**Source:**
```calor-fragment
condition ? whenTrue : whenFalse
```

**CNF:**
```
    branch condition -> true_block, false_block
true_block:
    t_result = whenTrue
    goto end_block
false_block:
    t_result = whenFalse
    goto end_block
end_block:
    // Result in t_result
```

### 3.4 Control Flow Lowering

#### If Statement

**Source:**
```calor-fragment
if condition
  then_body
else
  else_body
```

**CNF:**
```
    branch condition -> then_label, else_label
then_label:
    [then_body lowered]
    goto end_label
else_label:
    [else_body lowered]
    goto end_label
end_label:
```

#### While Loop

**Source:**
```calor-fragment
while condition
  body
```

**CNF:**
```
loop_header:
    branch condition -> loop_body, loop_exit
loop_body:
    [body lowered]
    goto loop_header
loop_exit:
```

#### For Loop

**Source:**
```calor-fragment
for i = 0; i < 10; i = i + 1
  body
```

**CNF:**
```
    i = 0
loop_header:
    t_cond = i < 10
    branch t_cond -> loop_body, loop_exit
loop_body:
    [body lowered]
    i = i + 1
    goto loop_header
loop_exit:
```

This is the reevaluated semicolon form. The separate
`for i from start to end step stepValue` form snapshots bounds and step once,
includes its endpoint, rejects a zero step, and ends on checked range overflow.
The simple diagram above is not a lowering specification for that form.

### 3.5 Match Expression Lowering

**Source:**
```calor-fragment
return match target
  case pattern1 → body1
  case pattern2 → body2
  case _ → fallback
```

**CNF:**
```
    t_target = [target lowered]
    t_match1 = [pattern1 match check]
    branch t_match1 -> case1, try_case2
case1:
    [body1 lowered]
    goto match_end
try_case2:
    t_match2 = [pattern2 match check]
    branch t_match2 -> case2, default_case
case2:
    [body2 lowered]
    goto match_end
default_case:
    [default lowered]
    goto match_end
match_end:
```

---

## 4. Contract Lowering

### 4.1 Preconditions

**Source:**
```calor
module NormalRequires
  func Positive (int:x) → int
    !
    requires x > 0
    return x
```

**CNF:**
```
    t_pre = x > 0
    branch t_pre -> precond_ok, precond_fail
precond_fail:
    throw ContractViolationException("f1", "x must be positive", "x > 0")
precond_ok:
    [body lowered]
```

### 4.2 Postconditions

**Source:**
```calor
module NormalEnsures
  func Positive (int:value) → int
    !
    => result > 0
    return value
```

**CNF:**
```
    result = [expr lowered]
    t_post = result > 0
    branch t_post -> postcond_ok, postcond_fail
postcond_fail:
    throw ContractViolationException("f1", "result positive", "result > 0")
postcond_ok:
    return result
```

---

## 5. Temporary Naming Convention

Temporaries follow a predictable naming scheme:

| Pattern | Usage |
|---------|-------|
| `t{n}` | General temporaries (t1, t2, ...) |
| `t_cond` | Condition result |
| `t_result` | Accumulator for expression result |
| `t_pre` | Precondition check |
| `t_post` | Postcondition check |
| `t_match{n}` | Match pattern check |
| `t_target` | Match target |

---

## 6. Type System in CNF

### 6.1 CNF Types

```csharp
public enum CnfType
{
    Void,
    Bool,
    Int,     // native int
    Long,    // native long
    Float,   // native float
    Double,  // native double
    String,
    Object,  // Reference types
    Array,   // Array types (with element type)
}
```

### 6.2 Type Annotations

All CNF nodes carry explicit type information:

```csharp
t1 = CnfBinaryOp(Add, a, b, INT)
//                          ^^^-- Result type explicitly stated
```

---

## 7. Validation Rules

CNF must satisfy these invariants:

### 7.1 Atomicity

All operands of `CnfBinaryOp`, `CnfUnaryOp`, `CnfCall`, `CnfConversion` must be atomic:
- `CnfLiteral`
- `CnfVariableRef`

### 7.2 Label Integrity

- Every `CnfGoto` target must have a corresponding `CnfLabel`
- Every `CnfBranch` true/false label must have corresponding `CnfLabel`s

### 7.3 Type Consistency

- Binary operator operand types must be compatible
- Assignment target type must match value type
- Return value type must match function return type

### 7.4 Definite Assignment

Variables must be assigned before use. This is a design obligation, not a
property fully established by the current validator. Its traversal records
definitions and checks atomic binary/unary/call operands and label targets;
it is not path-sensitive definite-assignment or type-consistency analysis.
Use a fresh validator per function because its errors persist across calls.

---

## 8. CNF Visitor Interface

```csharp
public interface ICnfVisitor<T>
{
    // Expressions
    T Visit(CnfLiteral node);
    T Visit(CnfVariableRef node);
    T Visit(CnfBinaryOp node);
    T Visit(CnfUnaryOp node);
    T Visit(CnfCall node);
    T Visit(CnfConversion node);

    // Statements
    T Visit(CnfAssign node);
    T Visit(CnfSequence node);
    T Visit(CnfBranch node);
    T Visit(CnfReturn node);
    T Visit(CnfThrow node);
    T Visit(CnfLabel node);
    T Visit(CnfGoto node);
    T Visit(CnfTry node);
}
```

---

## 9. Example: Complete Lowering

### Source

```calor
module NormalSum
  func SumIfPositive (int:a, int:b) → int
    !
    requires a > 0 && b > 0
    return a + b
```

### CNF

This is an explanatory target shape, not a production lowering transcript.
The prototype does not implement full postcondition or checked-overflow
semantics; the ordinary compiler enforces its supported runtime contracts
and native arithmetic policy independently.

```
sumIfPositive:
    // Precondition: (a > 0) && (b > 0)
    t1 = CnfBinaryOp(GreaterThan, a, 0, BOOL)
    t_pre_result = false
    branch t1 -> and_then, and_end
and_then:
    t2 = CnfBinaryOp(GreaterThan, b, 0, BOOL)
    t_pre_result = t2
    goto and_end
and_end:
    branch t_pre_result -> precond_ok, precond_fail
precond_fail:
    throw ContractViolationException("f1", null, "(a > 0) && (b > 0)")
precond_ok:
    // Body: return a + b
    t3 = CnfBinaryOp(Add, a, b, INT)
    return t3
```

---

## 10. Implementation Files

| File | Purpose |
|------|---------|
| `src/Calor.Compiler/IR/CnfNodes.cs` | CNF node type definitions |
| `src/Calor.Compiler/IR/CnfLowering.cs` | AST → CNF lowering pass |
| `src/Calor.Compiler/IR/CnfVisitor.cs` | Visitor interface for CNF |

---

## References

- Core Semantics: `docs/semantics/core.md`
- Backend Specification: `docs/semantics/dotnet-backend.md`
- AST Inventory: `docs/semantics/inventory.md`
