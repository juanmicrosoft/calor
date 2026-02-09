# Calor Normal Form (CNF) Specification

Version: 1.0.0

This document specifies the Calor Normal Form (CNF), an intermediate representation that makes evaluation semantics explicit.

---

## 1. Overview

### 1.1 The Problem: Backend-Dependent Semantics

When compiling directly to a backend language (like C#), semantic decisions can become implicit:

```calor
result = f(a(), b()) + c()
```

Direct compilation to C# might produce:
```csharp
var result = f(a(), b()) + c();
```

This **delegates** evaluation order to C#. If C# changes its evaluation order (or if a different backend has different rules), the code's behavior changes silently.

**CNF prevents this by making every semantic decision explicit in the IR.**

### 1.2 Purpose

CNF is an intermediate representation (IR) between the Calor AST and backend code generation. Its purpose is to:

1. **Make evaluation order explicit** - Temporaries enforce left-to-right evaluation
2. **Introduce explicit temporaries** - All intermediate values have names
3. **Linearize control flow** - Branch/label/goto instead of structured control
4. **Remove implicit conversions** - All conversions are explicit nodes

By lowering to CNF before emitting backend code, we guarantee that **Calor semantics are enforced regardless of backend**.

### 1.2 Pipeline Position

```
Source → Parser → AST → TypeChecker → Binder → [CNF Lowering] → CNF → [Backend] → Output
                                       ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
                                       NEW: This specification
```

### 1.3 Design Principles

- **Explicit over implicit**: Every semantic operation is a visible node
- **Flat structure**: No deeply nested expressions
- **Backend agnostic**: Same CNF serves all backends
- **Verifiable**: CNF can be validated against semantics spec

---

## 2. CNF Node Types

### 2.1 Expressions (Atomic)

CNF expressions are always atomic - they reference either literals, variables, or the results of previous operations.

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

#### CnfLoop

Unconditional loop construct.

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
§R §OP{kind=ADD} §OP{kind=MUL} §REF{name=a} §REF{name=b} §REF{name=c}
// return (a * b) + c
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
§C{f} §C{a} §/C §C{b} §/C §C{c} §/C §/C
// f(a(), b(), c())
```

**CNF:**
```
t1 = CnfCall("a", [])
t2 = CnfCall("b", [])
t3 = CnfCall("c", [])
t4 = CnfCall("f", [t1, t2, t3])
```

This enforces left-to-right evaluation order.

### 3.2 Short-Circuit Lowering

Logical operators are lowered to control flow.

#### Logical AND

**Source:**
```calor
A && B
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
A || B
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
```calor
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
```calor
§IF{if1}
  §COND condition
  §THEN then_body
  §ELSE else_body
§/IF{if1}
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
```calor
§WHILE{w1}
  §COND condition
  body
§/WHILE{w1}
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
```calor
§FOR{for1}{var=i}{from=0}{to=10}{step=1}
  body
§/FOR{for1}
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

### 3.5 Match Expression Lowering

**Source:**
```calor
§MATCH{m1} target
  §CASE pattern1 => body1
  §CASE pattern2 => body2
  §CASE _ => default
§/MATCH{m1}
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
§F{f1:myFunc}
  §REQUIRES{message="x must be positive"} §OP{kind=GT} x 0
  body
§/F{f1}
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
§F{f1:myFunc}
  §ENSURES{message="result positive"} §OP{kind=GT} result 0
  §R expr
§/F{f1}
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
    Int,     // i32
    Long,    // i64
    Float,   // f32
    Double,  // f64
    String,
    Object,  // Reference types
    Array,   // Array types (with element type)
    Option,  // Option<T>
    Result,  // Result<T,E>
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

Variables must be assigned before use.

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
    T Visit(CnfLoop node);
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
§F{f1:sumIfPositive:pub}
  §I{i32:a} §I{i32:b}
  §O{i32}
  §REQUIRES §OP{kind=AND} §OP{kind=GT} a 0 §OP{kind=GT} b 0
  §R §OP{kind=ADD} a b
§/F{f1}
```

### CNF

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
