# Calor AST Construct Inventory

This document catalogs all AST constructs in the Calor language, providing a complete inventory for formal semantics specification.

## Overview

The Calor compiler defines **134** unique visitor methods in `IAstVisitor` (see `src/Calor.Compiler/Ast/AstNode.cs:24-159`). This inventory organizes constructs by category with syntax examples and file references.

---

## 1. Literals

Primitive value expressions representing constants.

| Construct | Node Type | Syntax Example | File Reference |
|-----------|-----------|----------------|----------------|
| Integer | `IntLiteralNode` | `INT:42` | `ExpressionNodes.cs:17-28` |
| Float | `FloatLiteralNode` | `FLOAT:3.14` | `ExpressionNodes.cs:90-101` |
| Boolean | `BoolLiteralNode` | `BOOL:true`, `BOOL:false` | `ExpressionNodes.cs:51-62` |
| String | `StringLiteralNode` | `STR:"hello"` | `ExpressionNodes.cs:34-45` |

---

## 2. Operators

### 2.1 Binary Operators

Defined in `ControlFlowNodes.cs:220-248`:

| Category | Operators | Syntax |
|----------|-----------|--------|
| Arithmetic | `Add`, `Subtract`, `Multiply`, `Divide`, `Modulo`, `Power` | `+`, `-`, `*`, `/`, `%`, `**` |
| Comparison | `Equal`, `NotEqual`, `LessThan`, `LessOrEqual`, `GreaterThan`, `GreaterOrEqual` | `==`, `!=`, `<`, `<=`, `>`, `>=` |
| Logical | `And`, `Or` | `&&`, `||` |
| Bitwise | `BitwiseAnd`, `BitwiseOr`, `BitwiseXor`, `LeftShift`, `RightShift` | `&`, `|`, `^`, `<<`, `>>` |

**Node:** `BinaryOperationNode` (`ControlFlowNodes.cs:195-215`)
```calor
§OP{kind=ADD} left right
§OP{kind=MUL} §REF{name=a} §REF{name=b}
```

### 2.2 Unary Operators

Defined in `ExpressionNodes.cs:141-146`:

| Operator | Symbol | Description |
|----------|--------|-------------|
| `Negate` | `-` | Arithmetic negation |
| `Not` | `!` | Logical negation |
| `BitwiseNot` | `~` | Bitwise complement |

**Node:** `UnaryOperationNode` (`ExpressionNodes.cs:122-136`)
```calor
§OP{kind=NEG} expression
§OP{kind=NOT} condition
```

---

## 3. Control Flow

### 3.1 Conditional Statements

| Construct | Node Type | File Reference |
|-----------|-----------|----------------|
| If/ElseIf/Else | `IfStatementNode` | `ControlFlowNodes.cs:105-134` |
| ElseIf Clause | `ElseIfClauseNode` | `ControlFlowNodes.cs:140-157` |

```calor
§IF{id001}
  §COND §OP{kind=GT} §REF{name=x} 0
  §THEN
    §R INT:1
  §ELSEIF
    §COND §OP{kind=LT} §REF{name=x} 0
    §THEN
      §R INT:-1
  §ELSE
    §R INT:0
§/IF{id001}
```

### 3.2 Loops

| Construct | Node Type | Syntax | File Reference |
|-----------|-----------|--------|----------------|
| For Loop | `ForStatementNode` | `§FOR{id}{var}{from}{to}{step}` | `ControlFlowNodes.cs:9-41` |
| While Loop | `WhileStatementNode` | `§WHILE{id}` | `ControlFlowNodes.cs:47-70` |
| Do-While Loop | `DoWhileStatementNode` | `§DO{id}...§/DO` | `ControlFlowNodes.cs:76-99` |
| Foreach Loop | `ForeachStatementNode` | `§EACH{id:var:type}` | `ArrayNodes.cs:115-164` |

```calor
§FOR{for1}{var=i}{from=0}{to=10}{step=1}
  §PRINT §REF{name=i}
§/FOR{for1}

§WHILE{while1}
  §COND §OP{kind=LT} §REF{name=i} 100
  ...
§/WHILE{while1}
```

### 3.3 Loop Control

| Construct | Node Type | Syntax | File Reference |
|-----------|-----------|--------|----------------|
| Break | `BreakStatementNode` | `§BREAK` | `ControlFlowNodes.cs:269-275` |
| Continue | `ContinueStatementNode` | `§CONTINUE` | `ControlFlowNodes.cs:257-263` |

---

## 4. Pattern Matching

### 4.1 Match Expressions

| Construct | Node Type | File Reference |
|-----------|-----------|----------------|
| Match Expression | `MatchExpressionNode` | `PatternNodes.cs:13-31` |
| Match Statement | `MatchStatementNode` | `PatternNodes.cs:36-54` |
| Match Case | `MatchCaseNode` | `PatternNodes.cs:59-75` |

```calor
§MATCH{m001} §REF{name=shape}
  §CASE §PATTERN{Some} §VAR{s}
    §BODY §R §REF{name=s}
  §/CASE
  §CASE §PATTERN{None}
    §BODY §R STR:"none"
  §/CASE
§/MATCH{m001}
```

### 4.2 Pattern Types

| Pattern | Node Type | Syntax | File Reference |
|---------|-----------|--------|----------------|
| Wildcard | `WildcardPatternNode` | `_` | `PatternNodes.cs:142-148` |
| Variable | `VariablePatternNode` | `name` | `PatternNodes.cs:153-164` |
| Literal | `LiteralPatternNode` | `42`, `"hello"` | `PatternNodes.cs:169-180` |
| Some | `SomePatternNode` | `Some(pattern)` | `PatternNodes.cs:185-196` |
| None | `NonePatternNode` | `None` | `PatternNodes.cs:201-207` |
| Ok | `OkPatternNode` | `Ok(pattern)` | `PatternNodes.cs:212-223` |
| Err | `ErrPatternNode` | `Err(pattern)` | `PatternNodes.cs:228-239` |
| Positional | `PositionalPatternNode` | `Point(x, y)` | `PatternNodes.cs:248-269` |
| Property | `PropertyPatternNode` | `{ Age: >= 18 }` | `PatternNodes.cs:276-297` |
| Relational | `RelationalPatternNode` | `>= 18`, `< 0` | `PatternNodes.cs:324-345` |
| List | `ListPatternNode` | `[first, ..rest]` | `PatternNodes.cs:352-373` |
| Var | `VarPatternNode` | `var x` | `PatternNodes.cs:380-395` |
| Constant | `ConstantPatternNode` | constant values | `PatternNodes.cs:400-412` |

---

## 5. Types

### 5.1 Type Definitions

| Construct | Node Type | Syntax | File Reference |
|-----------|-----------|--------|----------------|
| Record | `RecordDefinitionNode` | `§RECORD{id}{name}` | `TypeNodes.cs:30-47` |
| Union | `UnionTypeDefinitionNode` | `§TYPE{id}{name}` | `TypeNodes.cs:85-102` |
| Enum | `EnumDefinitionNode` | `§EN{id:Name}` | `TypeNodes.cs:317-344` |
| Enum Extension | `EnumExtensionNode` | `§EEXT{id:EnumName}` | `EnumExtensionNode.cs` |
| Interface | `InterfaceDefinitionNode` | `§IFACE{id:Name}` | `ClassNodes.cs:25-70` |
| Class | `ClassDefinitionNode` | `§CLASS{id:Name}` | `ClassNodes.cs:138-324` |
| Delegate | `DelegateDefinitionNode` | `§DEL{id:Name}` | `LambdaNodes.cs:77-100` |

### 5.2 Type Components

| Construct | Node Type | File Reference |
|-----------|-----------|----------------|
| Field Definition | `FieldDefinitionNode` | `TypeNodes.cs:53-76` |
| Variant Definition | `VariantDefinitionNode` | `TypeNodes.cs:108-128` |
| Enum Member | `EnumMemberNode` | `TypeNodes.cs:294-308` |
| Type Reference | `TypeReferenceNode` | `TypeNodes.cs:134-155` |

### 5.3 Algebraic Types

| Construct | Node Type | Syntax | File Reference |
|-----------|-----------|--------|----------------|
| Option.Some | `SomeExpressionNode` | `§SOME expr` | `TypeNodes.cs:223-235` |
| Option.None | `NoneExpressionNode` | `§NONE{type}` | `TypeNodes.cs:241-253` |
| Result.Ok | `OkExpressionNode` | `§OK expr` | `TypeNodes.cs:259-271` |
| Result.Err | `ErrExpressionNode` | `§ERR expr` | `TypeNodes.cs:277-289` |

---

## 6. Functions and Methods

### 6.1 Functions

**Node:** `FunctionNode` (`FunctionNode.cs:52-221`)

```calor
§F{f001:add:pub}
  §I{i32:a} §I{i32:b}
  §O{i32}
  §E{}
  §REQUIRES §OP{kind=GTE} §REF{name=a} 0
  §ENSURES §OP{kind=GT} result 0
  §R §OP{kind=ADD} §REF{name=a} §REF{name=b}
§/F{f001}
```

### 6.2 Parameters

| Construct | Node Type | Syntax | File Reference |
|-----------|-----------|--------|----------------|
| Parameter | `ParameterNode` | `§I{type:name}` | `FunctionNode.cs:227-263` |
| Output | `OutputNode` | `§O{type}` | `FunctionNode.cs:19-30` |
| Effects | `EffectsNode` | `§E{io=cw,fs:r}` | `FunctionNode.cs:35-46` |

### 6.3 Methods (OOP)

| Construct | Node Type | File Reference |
|-----------|-----------|----------------|
| Method | `MethodNode` | `ClassNodes.cs:383-463` |
| Method Signature | `MethodSignatureNode` | `ClassNodes.cs:76-128` |
| Constructor | `ConstructorNode` | `PropertyNodes.cs` |

**Method Modifiers** (`ClassNodes.cs:9-17`):
- `Virtual`, `Override`, `Abstract`, `Sealed`, `Static`

---

## 7. Statements

### 7.1 Basic Statements

| Construct | Node Type | Syntax | File Reference |
|-----------|-----------|--------|----------------|
| Return | `ReturnStatementNode` | `§R expr` | `StatementNodes.cs` |
| Call | `CallStatementNode` | `§CALL target args` | `StatementNodes.cs` |
| Print | `PrintStatementNode` | `§PRINT expr` | `StatementNodes.cs` |
| Bind | `BindStatementNode` | `§BIND{name}{type}` | `ControlFlowNodes.cs:163-189` |
| Assignment | `AssignmentStatementNode` | `§SET target value` | `StatementNodes.cs` |
| Compound Assignment | `CompoundAssignmentStatementNode` | `§SET{+=} target value` | `StatementNodes.cs` |

### 7.2 Using Statements

| Construct | Node Type | File Reference |
|-----------|-----------|----------------|
| Using Directive | `UsingDirectiveNode` | `UsingDirectiveNode.cs` |
| Using Statement | `UsingStatementNode` | `StatementNodes.cs` |

---

## 8. Exception Handling

**File:** `ExceptionNodes.cs`

| Construct | Node Type | Syntax | File Reference |
|-----------|-----------|--------|----------------|
| Try Statement | `TryStatementNode` | `§TRY{id}...§/TRY` | `ExceptionNodes.cs:17-43` |
| Catch Clause | `CatchClauseNode` | `§CATCH{Type:var}` | `ExceptionNodes.cs:50-92` |
| Throw | `ThrowStatementNode` | `§THROW expr` | `ExceptionNodes.cs:98-113` |
| Rethrow | `RethrowStatementNode` | `§RETHROW` | `ExceptionNodes.cs:119-125` |

```calor
§TRY{try1}
  §C{RiskyOperation} §/C
§CA{IOException:ex}
  §PRINT §REF{name=ex}
§CA
  §RETHROW
§FI
  §C{Cleanup} §/C
§/TRY{try1}
```

---

## 9. Contracts

**File:** `ContractNodes.cs`

| Construct | Node Type | Syntax | Description | File Reference |
|-----------|-----------|--------|-------------|----------------|
| Requires | `RequiresNode` | `§REQUIRES{message} expr` | Precondition | `ContractNodes.cs:9-29` |
| Ensures | `EnsuresNode` | `§ENSURES{message} expr` | Postcondition | `ContractNodes.cs:36-56` |
| Invariant | `InvariantNode` | `§INVARIANT{message} expr` | Type invariant | `ContractNodes.cs:62-82` |

---

## 10. Generics

**File:** `GenericNodes.cs`

| Construct | Node Type | File Reference |
|-----------|-----------|----------------|
| Type Parameter | `TypeParameterNode` | `GenericNodes.cs` |
| Type Constraint | `TypeConstraintNode` | `GenericNodes.cs` |
| Generic Type | `GenericTypeNode` | `GenericNodes.cs` |

---

## 11. Arrays and Collections

**File:** `ArrayNodes.cs`

| Construct | Node Type | Syntax | File Reference |
|-----------|-----------|--------|----------------|
| Array Creation | `ArrayCreationNode` | `§ARR{id:type:size}` | `ArrayNodes.cs:10-59` |
| Array Access | `ArrayAccessNode` | `§IDX array index` | `ArrayNodes.cs:65-86` |
| Array Length | `ArrayLengthNode` | `§LEN array` | `ArrayNodes.cs:92-107` |

---

## 12. Object-Oriented Programming

### 12.1 Classes

| Construct | Node Type | File Reference |
|-----------|-----------|----------------|
| Class Definition | `ClassDefinitionNode` | `ClassNodes.cs:138-324` |
| Class Field | `ClassFieldNode` | `ClassNodes.cs:330-374` |
| Interface Definition | `InterfaceDefinitionNode` | `ClassNodes.cs:25-70` |
| Method | `MethodNode` | `ClassNodes.cs:383-463` |
| Method Signature | `MethodSignatureNode` | `ClassNodes.cs:76-128` |

### 12.2 Inheritance

| Construct | Syntax | Description | File Reference |
|-----------|--------|-------------|----------------|
| Base Class | `§EXT{ClassName}` | Single inheritance | `Parser.cs:3868-3872` |
| Interface Implementation | `§IMPL{InterfaceName}` | Implement interface | `Parser.cs:3874-3883` |
| Base Constructor Call | `§BASE §A arg §/BASE` | Call base constructor | `PropertyNodes.cs` |
| Base Method Call | `§C{§BASE.Method} §/C` | Call base method | `ClassNodes.cs:559-565` |

### 12.3 Class Modifiers

| Modifier | Syntax | Description | Status |
|----------|--------|-------------|--------|
| Abstract | `abs` | Cannot be instantiated, may have abstract members | ✓ Implemented |
| Sealed | `seal` | Cannot be inherited from | ✓ Implemented |
| Static | `stat` | All members must be static | ⚠ Not implemented |
| Partial | `partial` | Definition split across files | ⚠ Not implemented |

**Syntax:** `§CL{id:Name:modifiers}` (3 positional parts)

> **Note:** Static and partial class modifiers are not fully implemented in the parser.

**Example:**
```calor
§CL{c1:Shape:abs}         // Abstract class
§CL{c2:Final:seal}        // Sealed class
§CL{c3:Service:pub}       // Public class
```

### 12.4 Method Modifiers

| Modifier | Syntax | Description | File Reference |
|----------|--------|-------------|----------------|
| Virtual | `virt` | Can be overridden | `ClassNodes.cs:9-17` |
| Override | `over` | Overrides virtual/abstract | `ClassNodes.cs:9-17` |
| Abstract | `abs` | No implementation | `ClassNodes.cs:9-17` |
| Sealed | `seal` | Prevents further override | `ClassNodes.cs:9-17` |
| Static | `stat` | Belongs to class | `ClassNodes.cs:9-17` |

**Syntax:** `§MT{id:Name:visibility:modifiers}`

**Example:**
```calor
§MT{mt1:Speak:pub:virt}   // Virtual method
§MT{mt2:Speak:pub:over}   // Override method
§MT{mt3:Area:pub:abs}     // Abstract method
```

### 12.5 Object Creation

| Construct | Node Type | Syntax | File Reference |
|-----------|-----------|--------|----------------|
| New Expression | `NewExpressionNode` | `§NEW{Type} args` | `ClassNodes.cs:470-506` |
| Object Initializer | `ObjectInitializerAssignment` | `{ Prop: value }` | `ClassNodes.cs:511-521` |
| Record Creation | `RecordCreationNode` | `§RECORD{type}` | `TypeNodes.cs:161-178` |

### 12.6 Member Access

| Construct | Node Type | Syntax | File Reference |
|-----------|-----------|--------|----------------|
| Field Access | `FieldAccessNode` | `target.field` | `TypeNodes.cs:203-217` |
| Call Expression | `CallExpressionNode` | `§C{target} args §/C` | `ClassNodes.cs:527-541` |
| This Expression | `ThisExpressionNode` | `§THIS` | `ClassNodes.cs:547-553` |
| Base Expression | `BaseExpressionNode` | `§BASE` | `ClassNodes.cs:559-565` |

### 12.7 Properties

| Construct | Node Type | File Reference |
|-----------|-----------|----------------|
| Property | `PropertyNode` | `PropertyNodes.cs` |
| Property Accessor | `PropertyAccessorNode` | `PropertyNodes.cs` |

### 12.8 Events

| Construct | Node Type | File Reference |
|-----------|-----------|----------------|
| Event Definition | `EventDefinitionNode` | `LambdaNodes.cs:106-132` |
| Event Subscribe | `EventSubscribeNode` | `LambdaNodes.cs:138-152` |
| Event Unsubscribe | `EventUnsubscribeNode` | `LambdaNodes.cs:158-172` |

---

## 13. Lambdas and Delegates

**File:** `LambdaNodes.cs`

| Construct | Node Type | Syntax | File Reference |
|-----------|-----------|--------|----------------|
| Lambda Expression | `LambdaExpressionNode` | `§LAM{id:params} body §/LAM` | `LambdaNodes.cs:29-69` |
| Lambda Parameter | `LambdaParameterNode` | param definition | `LambdaNodes.cs:8-22` |

---

## 14. Async/Await

**File:** `AsyncNodes.cs`

| Construct | Node Type | Syntax | File Reference |
|-----------|-----------|--------|----------------|
| Await Expression | `AwaitExpressionNode` | `§AWAIT expr` | `AsyncNodes.cs:10-31` |

---

## 15. Modern Operators

**File:** `ModernOperatorNodes.cs`

| Construct | Node Type | Syntax | Generated C# | File Reference |
|-----------|-----------|--------|--------------|----------------|
| Interpolated String | `InterpolatedStringNode` | `§INTERP{...}` | `$"..."` | `ModernOperatorNodes.cs:10-25` |
| Null Coalesce | `NullCoalesceNode` | `§?? left right` | `left ?? right` | `ModernOperatorNodes.cs:74-95` |
| Null Conditional | `NullConditionalNode` | `§?. target member` | `target?.member` | `ModernOperatorNodes.cs:102-123` |
| Range | `RangeExpressionNode` | `§RANGE start end` | `start..end` | `ModernOperatorNodes.cs:130-151` |
| Index From End | `IndexFromEndNode` | `§^ offset` | `^offset` | `ModernOperatorNodes.cs:158-173` |

---

## 16. With Expressions

**File:** `PatternNodes.cs:77-127`

| Construct | Node Type | Syntax | File Reference |
|-----------|-----------|--------|----------------|
| With Expression | `WithExpressionNode` | `§WITH target assignments §/WITH` | `PatternNodes.cs:86-107` |
| Property Assignment | `WithPropertyAssignmentNode` | `§SET{prop} value` | `PatternNodes.cs:113-127` |

---

## 17. Extended Metadata

**File:** `MetadataNodes.cs`

| Construct | Node Type | Description |
|-----------|-----------|-------------|
| Example | `ExampleNode` | Inline test cases |
| Issue | `IssueNode` | Tracked issues |
| Dependency | `DependencyNode` | External dependencies |
| Uses | `UsesNode` | Function dependencies |
| UsedBy | `UsedByNode` | Reverse dependencies |
| Assume | `AssumeNode` | Assumptions |
| Complexity | `ComplexityNode` | Complexity annotations |
| Since | `SinceNode` | Version introduced |
| Deprecated | `DeprecatedNode` | Deprecation info |
| BreakingChange | `BreakingChangeNode` | Breaking change markers |
| Decision | `DecisionNode` | Design decisions |
| RejectedOption | `RejectedOptionNode` | Rejected alternatives |
| Context | `ContextNode` | Contextual info |
| FileRef | `FileRefNode` | File references |
| PropertyTest | `PropertyTestNode` | Property-based tests |
| Lock | `LockNode` | Multi-agent locking |
| Author | `AuthorNode` | Authorship |
| TaskRef | `TaskRefNode` | Task references |

---

## 18. Attributes

**File:** `AttributeNodes.cs`

| Construct | Node Type | Syntax | File Reference |
|-----------|-----------|--------|----------------|
| Calor Attribute | `CalorAttributeNode` | `{@Attribute(args)}` | `AttributeNodes.cs` |

---

## Summary Statistics

| Category | Count |
|----------|-------|
| Literal Types | 4 |
| Binary Operators | 18 |
| Unary Operators | 3 |
| Control Flow Constructs | 8 |
| Pattern Types | 12 |
| Type Definitions | 6 |
| Function/Method Constructs | 8 |
| Statement Types | 10 |
| Exception Handling | 4 |
| Contract Types | 3 |
| OOP Constructs | 24 |
| Inheritance Constructs | 9 |
| Modern Operators | 5 |
| Extended Metadata | 18 |
| **Total Unique Constructs** | **~132** |

---

## Known Limitations and Syntax Notes

### Class Syntax
- Classes use **3 positional parts**: `§CL{id:Name:modifiers}`
- Visibility defaults to public for classes
- Static (`stat`) and partial (`partial`) class modifiers are not implemented

### Method Syntax
- Methods use **4 positional parts**: `§MT{id:Name:visibility:modifiers}`
- Multiple modifiers are **space-separated**: `§MT{mt1:Method:pub:seal over}`

### Method Call Syntax
- Inside `§C{...}` call targets, use **lowercase** `base.Method` and `this.Method`
- The `§BASE` and `§THIS` tokens are **not supported** inside call expressions
- Correct: `§C{base.GetValue} §/C`
- Incorrect: `§C{§BASE.GetValue} §/C` (will not parse)

### Type Mapping
- `f64` and `f32` may not map correctly; use `double` and `float` directly

### Object Creation
- `§NEW{Type}` does not require an end tag
- Arguments follow the `§A arg` pattern: `§NEW{Point} §A x §A y`

---

## References

- AST Node Base: `src/Calor.Compiler/Ast/AstNode.cs`
- IAstVisitor: `src/Calor.Compiler/Ast/AstNode.cs:24-159`
- IAstVisitor<T>: `src/Calor.Compiler/Ast/AstNode.cs:164-299`
- Inheritance Semantics: `docs/semantics/inheritance.md`
