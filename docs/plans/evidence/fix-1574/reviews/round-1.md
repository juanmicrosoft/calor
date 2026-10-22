# #1574 Codex review — round 1

- Command: `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"` (codex-cli 0.163.0), prompt asking for
  silent behavior differences in the uncommitted #1574 diff.
- Reviewed: the working tree before the first commit (base `24bc55533`).
- Verdict: **CHANGES-REQUESTED** (6 MAJOR, 0 BLOCKING).

## Findings (verbatim)

Read-only review; no files changed, builds, or tests run.

1. **MAJOR — Literal reuse changes user-defined conversion selection.** [RoslynSyntaxVisitor.cs:11280](src/Calor.Compiler/Migration/RoslynSyntaxVisitor.cs:11280)

   An assignment expression is not a constant expression. Returning its literal RHS makes constant-expression conversions available again:

   ```csharp
   class W {
       public string Kind;
       public static implicit operator W(byte v) => new W { Kind = "byte" };
       public static implicit operator W(long v) => new W { Kind = "long" };
   }
   static W F() { int x; return x = 1; }
   ```

   Original C# selects `W(long)`. The lowering produces the equivalent of `x = 1; return 1;`, selecting `W(byte)`. The literal needs a typed temp even though its type equals the assignment target’s type. See the [conversion selection rules](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/conversions#1054-user-defined-implicit-conversions).

2. **MAJOR — Collection initializers discard the temp’s declared type, repeating conversions.** [RoslynSyntaxVisitor.cs:11287](src/Calor.Compiler/Migration/RoslynSyntaxVisitor.cs:11287), [CalorEmitter.cs:1911](src/Calor.Compiler/Migration/CalorEmitter.cs:1911)

   ```csharp
   class W {
       public static int Count;
       public static implicit operator W(List<int> xs) {
           Count++;
           return new W();
       }
   }
   static string Run() {
       W a, b;
       a = b = new List<int> { 1 };
       return W.Count + ":" + ReferenceEquals(a, b);
   }
   ```

   Original result: `1:True`. The new temp has AST type `W`, but `CalorEmitter.Visit(BindStatementNode)` renders a `ListCreationNode` as `§LIST`, ignoring that type. The resulting temp is a `List<int>`; assigning it separately to `b` and `a` invokes the conversion twice and produces different objects (`2:False`).

3. **MAJOR — Compound-assignment RHS hoists run before the original target read.** [RoslynSyntaxVisitor.cs:11304](src/Calor.Compiler/Migration/RoslynSyntaxVisitor.cs:11304)

   ```csharp
   class C { public int N() => 2; }
   static int x = 1;
   static C F() { x = 10; return new C(); }
   static int Run() { return x += F().N(); }
   ```

   Original C# reads `x == 1` before evaluating `F()`, so it returns `3`. Converting `F().N()` hoists `F()` into `_pendingStatements` before the compound statement. The generated compound assignment therefore reads `10` and returns `12`. A simple target and an approved statement site do not establish safety when RHS conversion itself produces pending statements.

4. **MAJOR — Re-reading a nullable target loses the type of a `??=` expression.** [RoslynSyntaxVisitor.cs:11307](src/Calor.Compiler/Migration/RoslynSyntaxVisitor.cs:11307)

   ```csharp
   static int F() {
       int? n = null;
       return n ??= 1;
   }
   ```

   This is valid C#: the expression `n ??= 1` has type `int`. The permitted lowering becomes `n ??= 1; return n;`, returning an `int?` and failing compilation. Capture the expression’s result rather than re-reading the target, or preserve this case. The [language proposal explicitly specifies the underlying result type](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-8.0/null-coalescing-assignment.md#detailed-design).

5. **MAJOR — Direct block membership does not guarantee pending statements are flushed under `#if`.** [RoslynSyntaxVisitor.cs:11334](src/Calor.Compiler/Migration/RoslynSyntaxVisitor.cs:11334), [RoslynSyntaxVisitor.cs:6754](src/Calor.Compiler/Migration/RoslynSyntaxVisitor.cs:6754)

   ```csharp
   static int x;
   static int Set() {
   #if true
       return x = 5;
   #endif
   }
   static int Run() { Set(); return x; }
   ```

   The site check accepts the return because its parent is a block. However, the active preprocessor-region loop calls `ConvertStatement` without flushing `_pendingStatements`. It retains `return 5` and loses `x = 5`, changing `Run()` from `5` to `0`. A nonliteral RHS additionally leaves references to undeclared `_asg` temps.

6. **MAJOR — The expressibility check admits types containing anonymous types.** [RoslynSyntaxVisitor.cs:11383](src/Calor.Compiler/Migration/RoslynSyntaxVisitor.cs:11383)

   ```csharp
   static bool Run() {
       var a = new { X = 1 };
       var xs = System.Linq.Enumerable.Repeat(a, 1);
       var ys = xs = xs;
       return ReferenceEquals(xs, ys);
   }
   ```

   `xs` has a named constructed type, `IEnumerable<anonymous-type>`, so the top-level checks accept it. `CalorTypeName` then inserts an anonymous-type display string into the temp’s binding header, which cannot represent that C# type. This introduces invalid output for an assignment previously lowered using inferred bindings. Expressibility must be checked recursively through generic arguments and array elements.

Verdict line: CHANGES-REQUESTED


## Disposition

| # | Finding | Fix | Witness |
|---|---|---|---|
| 1 | Literal reuse re-enables constant conversions | Removed literal reuse; the right-hand side always goes into a typed temp | `R1-literal-conversion-selection` (native, `long`) |
| 2 | Collection/array initializers keep their own type | Escalate when the converted value is a collection or array creation and the Roslyn conversion is not identity | `R1-collection-conversion` (preserved, `1:True`) |
| 3 | Compound right-hand side hoists run before the target read | Escalate when converting the compound right-hand side adds pending statements | `R1-compound-rhs-hoist` |
| 4 | `n ??= v` re-read changes the type | `??=` in expression context escalates | `R1-null-coalescing-assignment` (preserved) |
| 5 | Pending statements dropped in an active `#if` region | `ConvertBlock` region loop clears, converts, flushes and orders hoisted statements under their statement | `R1-preprocessor-region` |
| 6 | Anonymous types inside type arguments | Recursive `IsExpressibleType` | `R1-anonymous-type-argument` |
