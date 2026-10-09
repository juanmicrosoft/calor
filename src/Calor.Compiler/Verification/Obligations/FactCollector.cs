using Calor.Compiler.Ast;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.Verification.Obligations;

/// <summary>
/// A fact together with the source range it governs. Guard facts (if/while
/// conditions, loop bounds) only hold inside the body they guard, so the
/// solver must not assert them for obligations outside that range.
/// </summary>
public sealed record ScopedFact(ExpressionNode Fact, int ScopeStart, int ScopeEnd)
{
    public static ScopedFact FunctionWide(ExpressionNode fact)
        => new(fact, 0, int.MaxValue);

    public bool AppliesTo(TextSpan span)
        => span.Start >= ScopeStart && span.End <= ScopeEnd;
}

/// <summary>
/// Collects flow-sensitive facts from the AST that can be used as Z3 assumptions
/// when verifying obligations. Extracts loop bounds, if-guard conditions, and
/// inline refinement predicates.
///
/// Facts are scoped to the statement range they dominate: an if-condition holds
/// only inside the then-body, an elseif-condition only inside its own body, a
/// while-condition only inside the loop body. Facts whose variables are rebound
/// inside the governed range are dropped entirely (conservative assignment kill)
/// because the guard may no longer hold at the obligation site.
/// </summary>
public sealed class FactCollector
{
    /// <summary>
    /// Collected facts with the source ranges they govern.
    /// </summary>
    public List<ScopedFact> ScopedFacts { get; } = new();

    /// <summary>
    /// Convenience view of the collected fact expressions (scope-erased).
    /// </summary>
    public IReadOnlyList<ExpressionNode> Facts
        => ScopedFacts.Select(f => f.Fact).ToList();

    /// <summary>
    /// Adds a fact that holds for the whole function (e.g., an indexed-type
    /// constraint), subject to no assignment kill.
    /// </summary>
    public void AddFunctionWideFact(ExpressionNode fact)
    {
        if (!IsStaleAfterEntry(fact))
            ScopedFacts.Add(ScopedFact.FunctionWide(fact));
    }

    /// <summary>#1413: the names read by entry facts (parameter refinements) dropped as stale;
    /// an obligation reading one of them has an incompletely modeled entry state.</summary>
    public HashSet<string> DroppedFactNames { get; } = new(StringComparer.Ordinal);

    /// <summary>#1413 (S1 OBL-MUTATION-KILL): every name the body may rebind (assignments, bindings,
    /// loop variables, `ref`/`out` arguments); an entry fact about one may be stale.</summary>
    public IReadOnlySet<string> AssignedNames => _assignedNames;

    /// <summary>#1413: the body contains code the collector cannot see into; no fact is usable.</summary>
    public bool HasOpaqueCode { get; private set; }

    /// <summary>#1413: an entry predicate (parameter refinement) contains raw C#, which runs
    /// before the body and may rebind anything; no entry fact is then usable.</summary>
    public bool HasOpaqueEntry { get; private set; }

    /// <summary>#1413: the body may change heap state: a call (including a getter, indexer, or
    /// enumerator a member read or foreach may run), a collection mutation, a heap store, or new.</summary>
    public bool MutatesHeap { get; private set; }

    // A member or element read anywhere, a §PROOF condition included, may run a getter, indexer, or
    // enumerator.
    private static bool MayChangeHeap(IReadOnlyList<StatementNode> body)
        => body.SelectMany(DescendantsAndSelf).Any(node => IsHeapMutation(node) || IsHeapRead(node));

    /// <summary>For a §PROOF counterexample only: whether a fact may be stale at <paramref name="span"/>
    /// through code outside that span (the proof condition's own reads are its evaluation).</summary>
    public bool IsStaleBefore(ExpressionNode fact, TextSpan span, IReadOnlySet<string>? resolvedLocals = null)
        => HasOpaqueCode
           || ReferencedNames(fact).Any(name => resolvedLocals?.Contains(name) != true && _assignedNames.Contains(name))
           || ReadsHeap(fact)
            && (_heapWritten || _heapReads.Any(read => read.Start < span.Start || read.End > span.End));

    private static bool IsHeapMutation(AstNode node)
        => IsHeapStore(node) || node is CallExpressionNode or CallStatementNode or ExpressionCallNode
            or EventSubscribeNode or EventUnsubscribeNode or UsingStatementNode or NewExpressionNode
            or CollectionPushNode or DictionaryPutNode or CollectionRemoveNode or CollectionSetIndexNode
            or CollectionClearNode or CollectionInsertNode or ForeachStatementNode or DictionaryForeachNode;

    /// <summary>#1413: whether a fact true on entry or at a guard may be false later in the body.</summary>
    public bool IsStaleAfterEntry(ExpressionNode fact)
        => HasOpaqueCode
           || ReferencedNames(fact).Overlaps(_assignedNames)
           || (MutatesHeap && ReadsHeap(fact));

    /// <summary>Whether an expression reads an array element, a field, or a dotted path.</summary>
    public static bool ReadsHeap(ExpressionNode expression)
        => DescendantsAndSelf(expression).Any(IsHeapRead);

    private static bool IsHeapRead(AstNode node)
        => node is ArrayAccessNode or MultiDimArrayAccessNode or FieldAccessNode
            || node is ReferenceNode reference && reference.Name.Contains('.');

    private HashSet<string> _assignedNames = new(StringComparer.Ordinal);
    private IReadOnlyList<StatementNode> _body = [];
    private bool _refinementChangesState;
    private bool _heapWritten;
    private List<TextSpan> _heapReads = new();
    private readonly HashSet<string> _aliasWritten = new(StringComparer.Ordinal);
    private bool _bodyInitialized;
    private bool _hasJumps;
    private readonly List<(int Start, int End)> _exactStatements = new();

    /// <summary>
    /// Marks an additional name as possibly rebound (e.g. the size variable of an
    /// indexed-type parameter that the body reassigns).
    /// </summary>
    public void MarkAssigned(string name) => _assignedNames.Add(name);

    /// <summary>#1413 (S1 OBL-MUTATION-KILL, OBL-BRANCH-FACTS): the span is a simple statement reached
    /// only through bodies whose guards are all asserted, after no statement that can exit or jump,
    /// so a model reaches it. Residual: an earlier statement that throws is not tracked.</summary>
    public bool IsExact(TextSpan span)
        => !HasOpaqueCode
           && _exactStatements.Any(statement =>
               span.Start >= statement.Start && span.End <= statement.End);

    /// <summary>
    /// Collects facts from a function's body and parameter refinements that are relevant
    /// to proving obligations within the given statements.
    /// Inline refinements are added as facts for non-RefinementEntry obligations
    /// (e.g., IndexBounds can use parameter refinements as assumptions).
    /// </summary>
    public void CollectFromFunction(
        FunctionNode func,
        IReadOnlyDictionary<string, ExpressionNode>? refinementPredicates = null)
        => CollectFromCallable(func.Parameters, func.Body, refinementPredicates);

    /// <summary>
    /// Collects facts from a class method using the same rules as module functions.
    /// </summary>
    public void CollectFromMethod(
        MethodNode method,
        IReadOnlyDictionary<string, ExpressionNode>? refinementPredicates = null)
        => CollectFromCallable(method.Parameters, method.Body, refinementPredicates);

    internal void CollectFromCallable(
        IReadOnlyList<ParameterNode> parameters,
        IReadOnlyList<StatementNode> body,
        IReadOnlyDictionary<string, ExpressionNode>? refinementPredicates)
    {
        InitializeBody(body);

        // #1413: two ref/out parameters may alias one variable, so a write through either
        // rebinds both.
        // #1413 (D-OBL-THROWING-PREDECESSOR): a binding or assignment to a refined name runs a
        // compiler-inserted refinement guard, which throws.
        _refinedTypes.UnionWith(refinementPredicates?.Keys ?? []);
        // #1516 review rounds 2-3: a refinement guard runs its predicate after a binding or assignment
        // (and on entry), outside the body this collector scans. Unless every predicate is plain
        // (literals, operators, `#`, simple names), it may change state: an increment, a call, or a
        // member read that runs a getter.
        _refinementChangesState = (refinementPredicates?.Values ?? [])
            .Concat(parameters.Select(p => p.InlineRefinement?.Predicate).OfType<ExpressionNode>())
            .Any(predicate => !DescendantsAndSelf(predicate).All(node => node is SelfRefNode || IsPlainDefinitionNode(node)));
        _parameterNames.UnionWith(parameters.Select(p => p.Name));
        _refinedNames.UnionWith(parameters
            .Where(p => p.InlineRefinement != null || _refinedTypes.Contains(p.TypeName))
            .Select(p => p.Name));
        _refinedNames.UnionWith(body.SelectMany(DescendantsAndSelf).OfType<BindStatementNode>()
            .Where(bind => bind.TypeName is { } refined && _refinedTypes.Contains(refined))
            .Select(bind => bind.Name));
        // Expression-level only: a statement's own guard (a refined binding or assignment) is the
        // statement's obligation itself, not something evaluated before it.
        _throwingSpans = body.SelectMany(DescendantsAndSelf).Where(node => node is not StatementNode && IsThrowingNode(node))
            .Select(node => node.Span).Where(span => span.Length > 0).ToList();

        var byReference = parameters
            .Where(p => (p.Modifier & (ParameterModifier.Ref | ParameterModifier.Out | ParameterModifier.In)) != 0)
            .Select(p => p.Name)
            .ToArray();
        if (byReference.Length > 1 && byReference.Any(_assignedNames.Contains)
            || byReference.Length > 0 && MutatesHeap)
        {
            _assignedNames.UnionWith(byReference);
            _aliasWritten.UnionWith(byReference);
        }

        // Parameter refinements, inline or named (#1413 S1 OBL-SELFREF, OBL-SUBTYPE), hold on
        // entry; dropped when the body may rebind them, and for out parameters.
        foreach (var param in parameters)
        {
            if (param.Modifier == ParameterModifier.Out)
                continue;
            var predicate = param.InlineRefinement?.Predicate;
            if (refinementPredicates != null && refinementPredicates.TryGetValue(param.TypeName, out var named))
                predicate = predicate == null ? named : new BinaryOperationNode(named.Span, BinaryOperator.And, predicate, named);
            if (predicate == null)
                continue;
            HasOpaqueEntry |= IsOpaque(predicate);
            EntryMayThrow |= MayThrow(predicate);
            var fact = SubstituteSelfRefStatic(predicate, param.Name);
            if (IsStaleAfterEntry(fact))
                DroppedFactNames.UnionWith(ReferencedNames(fact));
            else
                ScopedFacts.Add(ScopedFact.FunctionWide(fact));
        }

        Walk(body, exact: true);
    }

    /// <summary>
    /// Collects facts from a list of statements.
    /// </summary>
    public void CollectFromStatements(IReadOnlyList<StatementNode> statements)
    {
        InitializeBody(statements);
        Walk(statements, exact: true);
    }

    private void InitializeBody(IReadOnlyList<StatementNode> body)
    {
        if (_bodyInitialized)
            return;
        _bodyInitialized = true;
        _body = body;
        _assignedNames = CollectAssignedNames(body);
        var nodes = body.SelectMany(DescendantsAndSelf).ToArray();
        HasOpaqueCode = nodes.Any(node => IsOpaque(node));
        MutatesHeap = MayChangeHeap(body);
        _heapWritten = nodes.Any(IsHeapMutation);
        _heapReads = nodes.Where(IsHeapRead).Select(node => node.Span).ToList();
        _hasJumps = nodes.Any(node => node is GotoStatementNode or LabelStatementNode);
    }

    /// <summary>
    /// Walks a statement list collecting scoped facts. <paramref name="exact"/> says
    /// whether reaching this list is fully described by the facts collected so far.
    /// </summary>
    private void Walk(IReadOnlyList<StatementNode> statements, bool exact)
    {
        var diverted = false;
        foreach (var stmt in statements)
        {
            var reachedExactly = exact && !diverted && !_hasJumps;
            switch (stmt)
            {
                case ForStatementNode forStmt:
                    var bounded = CollectFromForLoop(forStmt) && !MayThrow(forStmt.From) && !MayThrow(forStmt.To);
                    Walk(forStmt.Body, reachedExactly && bounded);
                    break;

                case WhileStatementNode whileStmt:
                    // The while condition holds on entry to each iteration, but a
                    // body that reassigns its variables invalidates it mid-body.
                    var guarded = AddGuardFact(whileStmt.Condition, whileStmt.Body);
                    Walk(whileStmt.Body, reachedExactly && guarded && !MayThrow(whileStmt.Condition));
                    break;

                case IfStatementNode ifStmt:
                    CollectFromIf(ifStmt, reachedExactly);
                    break;

                case DoWhileStatementNode doWhile:
                    // The first iteration runs unconditionally.
                    Walk(doWhile.Body, reachedExactly);
                    break;

                case ForeachStatementNode foreach_:
                    Walk(foreach_.Body, exact: false);
                    break;

                case TryStatementNode tryStmt:
                    Walk(tryStmt.TryBody, exact: false);
                    break;

                default:
                    if (reachedExactly && !ContainsNestedStatement(stmt))
                        _exactStatements.Add((stmt.Span.Start, stmt.Span.End));
                    break;
            }

            // #1413 (discovery D-OBL-THROWING-PREDECESSOR, amendment 1.3.0): a statement that may
            // throw stops the paths on which it throws, which the solver does not model.
            if (CanDivert(stmt) || MayThrow(stmt))
                diverted = true;
        }
    }

    /// <summary>
    /// #1413 (D-OBL-THROWING-PREDECESSOR): whether evaluating the node may throw. Only names,
    /// literals, comparisons, logic, bitwise operators and shifts, conditionals, bindings, and
    /// assignments to plain names count as non-throwing. Any other form may throw, including
    /// checked or dividing arithmetic, calls, member and element reads, casts, and a retained
    /// proof guard.
    /// </summary>
    internal bool MayThrow(AstNode node) => DescendantsAndSelf(node).Any(IsThrowingNode);

    /// <summary>Whether the module declares operator overloads (then no operator is assumed not to throw).</summary>
    public bool OperatorsMayBeOverloaded { get; init; }

    /// <summary>#1413 (review round 2): a parameter refinement (entry guard) may throw.</summary>
    public bool EntryMayThrow { get; set; }

    private readonly HashSet<string> _refinedTypes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _refinedNames = new(StringComparer.Ordinal);
    private readonly HashSet<string> _parameterNames = new(StringComparer.Ordinal);

    /// <summary>A name, literal, or `#` whose value the solver models (never null in a model).</summary>
    private bool IsModeledOperand(ExpressionNode operand)
        => operand is IntLiteralNode or BoolLiteralNode or StringLiteralNode or FloatLiteralNode or DecimalLiteralNode or SelfRefNode
            || operand is ReferenceNode reference && !reference.Name.Contains('.') && _parameterNames.Contains(reference.Name);
    private List<TextSpan> _throwingSpans = new();

    private bool IsThrowingNode(AstNode node) => node switch
    {
        // With operator overloads or conversions in the module, any non-literal may run user code.
        IntLiteralNode or BoolLiteralNode or StringLiteralNode or FloatLiteralNode or DecimalLiteralNode => false,
        IfStatementNode or ElseIfClauseNode or SelfRefNode => false,
        _ when OperatorsMayBeOverloaded => true,
        ReferenceNode reference => reference.Name.Contains('.'),
        BinaryOperationNode binary => binary.Operator is BinaryOperator.Add
            or BinaryOperator.Subtract or BinaryOperator.Multiply or BinaryOperator.Divide or BinaryOperator.Modulo
            or BinaryOperator.Power,
        UnaryOperationNode unary => unary.Operator is not (UnaryOperator.Not or UnaryOperator.BitwiseNot),
        // A (re)binding of a refined name, annotated or not, runs the refinement guard.
        BindStatementNode bind => bind.TypeName is { } refined && _refinedTypes.Contains(refined) || _refinedNames.Contains(bind.Name),
        AssignmentStatementNode assignment => assignment.Target is not ReferenceNode target || _refinedNames.Contains(target.Name),
        ConditionalExpressionNode => false,
        // Over modeled operands (parameters, literals, `#`, never null in a model) these do not
        // throw; over locals (possibly default null) or other forms they may.
        ArrayLengthNode length => !IsModeledOperand(length.Array),
        StringOperationNode text => text.Operation is not (StringOp.Length or StringOp.Contains or StringOp.StartsWith
            or StringOp.EndsWith or StringOp.IsNullOrEmpty or StringOp.IsNullOrWhiteSpace
            or StringOp.Equals or StringOp.Concat or StringOp.ToUpper or StringOp.ToLower or StringOp.Trim
            or StringOp.TrimStart or StringOp.TrimEnd)
            || !text.Arguments.All(IsModeledOperand),
        _ => true,
    };

    /// <summary>
    /// #1413 (D-OBL-THROWING-PREDECESSOR, review rounds 1-2): whether the simple statement that holds
    /// <paramref name="span"/> contains a node that may throw outside the obligation's own span.
    /// Source spans do not give evaluation order (a folded multi-operand operation spans the whole
    /// expression), so any such node counts, including an enclosing one evaluated afterwards.
    /// </summary>
    public bool ThrowsElsewhereInStatement(TextSpan span)
        => _exactStatements.Where(statement => span.Start >= statement.Start && span.End <= statement.End)
            .Any(statement => _throwingSpans.Any(t => t.Start >= statement.Start && t.End <= statement.End
                && (t.Start < span.Start || t.End > span.End)));

    /// <summary>All property names a module declares, nested types included.</summary>
    internal static HashSet<string> PropertyNames(ModuleNode module)
        => DescendantsAndSelf(module).OfType<PropertyNode>().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);

    /// <summary>Operator or conversion declarations, including migrated op_* methods.</summary>
    internal static bool OverloadsOperators(ModuleNode module)
        => DescendantsAndSelf(module).Any(node => node is OperatorOverloadNode
            || node is MethodNode method && method.Name.StartsWith("op_", StringComparison.Ordinal))
           || HasOpaqueMembers(module);

    /// <summary>#1413 (review round 3): raw C# type members (operators, properties, conversions) the
    /// verifier cannot see.</summary>
    internal static bool HasOpaqueMembers(ModuleNode module)
        => DescendantsAndSelf(module).Any(node => node is ClassDefinitionNode type && type.InteropBlocks.Count > 0
            || node is InterfaceDefinitionNode iface && iface.InteropBlocks.Count > 0);

    /// <summary>
    /// #1413 (discovery D-OBL-PROOF-GETTER, amendment 1.3.0): whether an expression reads a member
    /// that a declared property has (a getter the solver does not model, or a property that hides
    /// an inherited field the solver models instead).
    /// </summary>
    internal static bool ReadsProperty(ExpressionNode expression, IReadOnlySet<string> propertyNames, bool anyMember = false)
        => DescendantsAndSelf(expression).Any(n =>
            n is FieldAccessNode field && (anyMember || propertyNames.Contains(field.FieldName))
            || n is ReferenceNode reference && reference.Name.Contains('.')
               && (anyMember || reference.Name.Split('.').Skip(1).Any(propertyNames.Contains))
            // §LEN emits `.Length`, which a declared (or raw) Length property may implement.
            || n is ArrayLengthNode && (anyMember || propertyNames.Contains("Length")));

    /// <summary>
    /// #1516: resolves the §B locals a §PROOF condition reads to their defining expressions, appending
    /// them to <paramref name="locals"/> (dependencies first). A local resolves only when its value at the
    /// proof is exactly its definition evaluated there: it is bound once, immutably, never rebound, by a
    /// binding that precedes the proof in an enclosing statement list (so every path to the proof runs
    /// it), and its definition is literals, operators, unchanged parameters, and other such locals.
    /// Names with no binding are left to the translator. Returns why a local cannot be resolved, or null.
    /// </summary>
    public string? ResolveProofLocals(
        ExpressionNode condition,
        IReadOnlySet<string> declared,
        List<(string Name, string? TypeName, ExpressionNode Definition)> locals)
    {
        var proof = _body.SelectMany(DescendantsAndSelf).OfType<ProofObligationNode>()
            .FirstOrDefault(node => ReferenceEquals(node.Condition, condition));
        if (proof == null)
            return null;
        var binds = _body.SelectMany(DescendantsAndSelf).OfType<BindStatementNode>()
            .GroupBy(bind => bind.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        var rebound = CollectAssignedNames(_body, includeBinds: false);
        // #1516 review round 3: a variable passed by reference with any modifier, `in` included,
        // exposes its storage to the callee (an `in` reference can be written through unsafe code).
        foreach (var node in _body.SelectMany(DescendantsAndSelf))
        {
            if (node.GetType().GetProperty("ArgumentModifiers")?.GetValue(node) is IReadOnlyList<string?> modifiers
                && node.GetType().GetProperty("Arguments")?.GetValue(node) is IReadOnlyList<ExpressionNode> arguments)
            {
                for (var index = 0; index < arguments.Count && index < modifiers.Count; index++)
                    if (modifiers[index] != null && RootName(arguments[index]) is { } root)
                        rebound.Add(root);
            }
        }
        var resolved = new Dictionary<string, BindStatementNode>(StringComparer.Ordinal);
        var resolving = new HashSet<string>(StringComparer.Ordinal);
        // The emitter sanitizes names (`a-b` and `ab` both become `ab`), so two Calor names can be
        // one C# variable; a name sharing its C# identifier with another is never resolved.
        var emittedNames = _body.SelectMany(DescendantsAndSelf).OfType<ReferenceNode>()
            .Select(reference => reference.Name.Split('.')[0])
            .Concat(binds.Keys).Concat(rebound).Concat(_assignedNames).Concat(_parameterNames).Concat(declared)
            .Distinct(StringComparer.Ordinal)
            .GroupBy(Calor.Compiler.CodeGen.CSharpEmitter.SanitizeIdentifier, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        bool SharesEmittedName(string name)
            => emittedNames.GetValueOrDefault(Calor.Compiler.CodeGen.CSharpEmitter.SanitizeIdentifier(name)) > 1;
        return Resolve(condition, proof);

        string? Resolve(ExpressionNode expression, AstNode site)
        {
            foreach (var name in ReferencedNames(expression))
            {
                if (!binds.TryGetValue(name, out var candidates))
                    continue;
                // The solver already holds this name for a parameter or size variable, which the
                // local shadows; the proof would read the wrong variable.
                if (declared.Contains(name))
                    return $"local '{name}' shares its name with a parameter or solver variable, so the proof's reference is ambiguous";
                var reason = ResolveLocal(name, candidates, site);
                if (reason != null)
                    return $"local '{name}' {reason}";
            }
            return null;
        }

        string? ResolveLocal(string name, List<BindStatementNode> candidates, AstNode site)
        {
            if (HasOpaqueCode)
                return "is in a body with raw C#, unsafe code, or a lambda, which the verifier cannot see and which may change it";
            // An operator in a definition or refinement predicate may run a user-defined overload.
            if (OperatorsMayBeOverloaded)
                return "is in a module that declares operator overloads or raw C# members, whose effects are not modeled";
            if (_refinementChangesState)
                return "is in a function whose refinement guards may change state (a refinement predicate is not plain), which the verifier does not model";
            if (_hasJumps)
                return "may be skipped by a goto, so its binding is not known to run before the proof";
            if (candidates.Count > 1)
                return "is bound more than once, so its value at the proof is not known";
            var bind = candidates[0];
            if (SharesEmittedName(name))
                return "shares its C# name with another variable, so its value at the proof is not known";
            if (bind.IsMutable)
                return "is mutable (§B{~...}), so its value at the proof is not modeled";
            if (rebound.Contains(name) || _parameterNames.Contains(name))
                return "is reassigned, passed by reference, or shadows another variable, so its value at the proof is not modeled";
            if (bind.Initializer is not { } definition)
                return "has no initializer";
            if (!Dominates(bind, site))
                return "is not bound on every path to the proof (it is bound in a branch or loop body that does not enclose the proof, or after it)";
            if (resolved.TryGetValue(name, out _))
                return null;
            if (!resolving.Add(name))
                return "is defined in terms of itself";
            if (!DescendantsAndSelf(definition).All(IsPlainDefinitionNode))
                return "has a defining expression the verifier does not model for locals (only literals, operators, parameters, and other immutable locals)";
            foreach (var read in ReferencedNames(definition))
            {
                if (binds.ContainsKey(read))
                {
                    var reason = ResolveLocal(read, binds[read], bind);
                    if (reason != null)
                        return $"is defined from local '{read}', which {reason}";
                }
                else if (!_parameterNames.Contains(read) || _assignedNames.Contains(read) || rebound.Contains(read) || !declared.Contains(read)
                         || SharesEmittedName(read))
                {
                    return $"is defined from '{read}', whose value at the proof is not modeled";
                }
            }
            resolving.Remove(name);
            resolved[name] = bind;
            locals.Add((name, bind.TypeName, definition));
            return null;
        }
    }

    // #1516: the forms a resolvable local's definition may use: never a call, member or element read,
    // increment, or anything else that reads or changes state outside the function's parameters.
    private static bool IsPlainDefinitionNode(AstNode node)
        => node is IntLiteralNode or BoolLiteralNode or BinaryOperationNode or ConditionalExpressionNode
            || node is ReferenceNode reference && !reference.Name.Contains('.')
            || node is UnaryOperationNode unary && !IsIncrement(unary);

    /// <summary>#1516: whether <paramref name="bind"/> runs on every path to <paramref name="site"/>: it
    /// precedes, in one sequential statement list, the statement that contains the site.</summary>
    private bool Dominates(StatementNode bind, AstNode site)
    {
        foreach (var list in StatementLists())
        {
            var index = -1;
            for (var i = 0; i < list.Count && index < 0; i++)
                if (ReferenceEquals(list[i], bind))
                    index = i;
            if (index >= 0)
                return list.Skip(index + 1).Any(statement =>
                    DescendantsAndSelf(statement).Any(node => ReferenceEquals(node, site)));
        }
        return false;
    }

    // Sequential statement lists only; a binding in any other container never resolves.
    private IEnumerable<IReadOnlyList<StatementNode>> StatementLists()
    {
        yield return _body;
        foreach (var node in _body.SelectMany(DescendantsAndSelf))
        {
            IReadOnlyList<StatementNode>?[] lists = node switch
            {
                IfStatementNode n => [n.ThenBody, n.ElseBody],
                ElseIfClauseNode n => [n.Body],
                ForStatementNode n => [n.Body],
                WhileStatementNode n => [n.Body],
                DoWhileStatementNode n => [n.Body],
                ForeachStatementNode n => [n.Body],
                DictionaryForeachNode n => [n.Body],
                TryStatementNode n => [n.TryBody, n.FinallyBody],
                CatchClauseNode n => [n.Body],
                MatchCaseNode n => [n.Body],
                UsingStatementNode n => [n.Body],
                SyncBlockNode n => [n.Body],
                _ => []
            };
            foreach (var list in lists)
                if (list != null)
                    yield return list;
        }
    }

    /// <summary>Each condition is a fact only within the body it guards; #1413 (S1 OBL-BRANCH-FACTS):
    /// elseif and else bodies also get the negations of the earlier conditions.</summary>
    private void CollectFromIf(IfStatementNode ifStmt, bool exact)
    {
        var conditions = new List<ExpressionNode> { ifStmt.Condition };
        conditions.AddRange(ifStmt.ElseIfClauses.Select(clause => clause.Condition));
        // A condition that itself changes state (an increment, a ref/out argument, or a call)
        // is evaluated against a changing state: neither it nor its negation is a stable fact.
        var negationsUsable = !conditions.Any(ChangesState);

        // #1413 (D-OBL-THROWING-PREDECESSOR): a body is reached only when every condition evaluated
        // before it completed; a later condition is never evaluated on that path.
        var thenGuarded = negationsUsable && AddGuardFact(ifStmt.Condition, ifStmt.ThenBody);
        Walk(ifStmt.ThenBody, exact && thenGuarded && !MayThrow(ifStmt.Condition));
        for (var index = 0; index < ifStmt.ElseIfClauses.Count; index++)
        {
            var clause = ifStmt.ElseIfClauses[index];
            var fact = negationsUsable
                ? Conjoin(conditions.Take(index + 1).Select(Negate).Append(clause.Condition), clause.Condition.Span)
                : clause.Condition;
            var clauseGuarded = negationsUsable && AddGuardFact(fact, clause.Body);
            Walk(clause.Body, exact && clauseGuarded && !conditions.Take(index + 2).Any(MayThrow));
        }
        if (ifStmt.ElseBody != null)
        {
            var added = negationsUsable && AddGuardFact(
                Conjoin(conditions.Select(Negate), ifStmt.Span),
                ifStmt.ElseBody);
            Walk(ifStmt.ElseBody, exact && added && !conditions.Any(MayThrow));
        }
    }

    private static ExpressionNode Negate(ExpressionNode condition)
        => new UnaryOperationNode(condition.Span, UnaryOperator.Not, condition);

    private static ExpressionNode Conjoin(IEnumerable<ExpressionNode> conjuncts, TextSpan span)
        => conjuncts.Aggregate((left, right) =>
            new BinaryOperationNode(span, BinaryOperator.And, left, right));

    /// <summary>#1413 review: code the collector cannot follow: raw C# (statement or §CS
    /// expression), unsafe/pointer code, and lambdas (deferred bodies). It makes the body opaque:
    /// no entry or guard facts, no exact state.</summary>
    internal static bool IsOpaque(AstNode root)
        => DescendantsAndSelf(root).Any(node => node is RawCSharpNode or RawCSharpExpressionNode
            or CompilerDirectiveNode or UnsafeBlockNode or FixedStatementNode or AddressOfNode
            or PointerDereferenceNode or LambdaExpressionNode);

    /// <summary>A store whose target is not a plain local or parameter (element, field, dotted path).</summary>
    private static bool IsHeapStore(AstNode node)
    {
        var target = node switch
        {
            AssignmentStatementNode assignment => assignment.Target,
            CompoundAssignmentStatementNode compound => compound.Target,
            UnaryOperationNode unary when IsIncrement(unary) => unary.Operand,
            _ => null
        };
        return target != null && (target is not ReferenceNode reference || reference.Name.Contains('.'));
    }

    private static bool ChangesState(ExpressionNode condition)
        => DescendantsAndSelf(condition).Any(node => IsIncrement(node)
            || node is CallExpressionNode or NewExpressionNode);

    private static bool IsIncrement(AstNode node)
        => node is UnaryOperationNode
        {
            Operator: UnaryOperator.PreIncrement
                or UnaryOperator.PreDecrement
                or UnaryOperator.PostIncrement
                or UnaryOperator.PostDecrement
        };

    private static bool ContainsNestedStatement(StatementNode statement)
        => DescendantsAndSelf(statement).Skip(1).Any(node => node is StatementNode);

    /// <summary>Whether control may leave the statement other than by falling through.</summary>
    private static bool CanDivert(StatementNode statement)
        => DescendantsAndSelf(statement).Any(node => node
            is ReturnStatementNode
            or ThrowStatementNode
            or RethrowStatementNode
            or BreakStatementNode
            or ContinueStatementNode
            or GotoStatementNode
            or YieldBreakStatementNode
            // A statement after a loop runs only once the loop condition is false, a path
            // condition the solver does not assert.
            or WhileStatementNode or ForStatementNode or DoWhileStatementNode
            or ForeachStatementNode or DictionaryForeachNode);

    /// <summary>
    /// Extracts loop bounds from a for-loop.
    /// §L{id:i:from:to:step} uses inclusive bounds, matching C# emission.
    /// </summary>
    private bool CollectFromForLoop(ForStatementNode forStmt)
    {
        var dummySpan = new TextSpan(0, 0, 1, 1);
        var loopVar = new ReferenceNode(dummySpan, forStmt.VariableName);

        var isPositiveStep = forStmt.Step is IntLiteralNode { Value: > 0 }
            or UnaryOperationNode
            {
                Operator: UnaryOperator.Negate,
                Operand: IntLiteralNode { Value: < 0 }
            };
        var isNegativeStep = forStmt.Step is IntLiteralNode { Value: < 0 }
            or UnaryOperationNode
            {
                Operator: UnaryOperator.Negate,
                Operand: IntLiteralNode { Value: > 0 }
            };
        // Both bounds must be facts for the body's state to be exact; the body
        // itself is walked by the caller.
        if (isPositiveStep)
        {
            var lower = AddGuardFact(
                new BinaryOperationNode(
                    dummySpan,
                    BinaryOperator.GreaterOrEqual,
                    loopVar,
                    forStmt.From),
                forStmt.Body);
            var upper = AddGuardFact(
                new BinaryOperationNode(
                    dummySpan,
                    BinaryOperator.LessOrEqual,
                    loopVar,
                    forStmt.To),
                forStmt.Body);
            return lower && upper;
        }
        if (isNegativeStep)
        {
            var upper = AddGuardFact(
                new BinaryOperationNode(
                    dummySpan,
                    BinaryOperator.LessOrEqual,
                    loopVar,
                    forStmt.From),
                forStmt.Body);
            var lower = AddGuardFact(
                new BinaryOperationNode(
                    dummySpan,
                    BinaryOperator.GreaterOrEqual,
                    loopVar,
                    forStmt.To),
                forStmt.Body);
            return lower && upper;
        }
        return false;
    }

    /// <summary>
    /// Records a guard fact scoped to the body it governs, unless the body
    /// rebinds a variable the fact mentions (conservative assignment kill).
    /// </summary>
    private bool AddGuardFact(ExpressionNode fact, IReadOnlyList<StatementNode> body)
    {
        if (body.Count == 0 || HasOpaqueCode)
            return false;

        var referenced = new HashSet<string>(StringComparer.Ordinal);
        CollectReferencedNames(fact, referenced);

        var assigned = CollectAssignedNames(body);
        if (referenced.Overlaps(assigned) || referenced.Overlaps(_aliasWritten))
            return false;
        // #1413: a guard over heap state is stale once the body can change the heap.
        if (ReadsHeap(fact) && MayChangeHeap(body))
            return false;

        var scopeStart = body.Min(s => s.Span.Start);
        var scopeEnd = body.Max(s => s.Span.End);
        ScopedFacts.Add(new ScopedFact(fact, scopeStart, scopeEnd));
        return true;
    }

    /// <summary>The names an expression reads.</summary>
    public static HashSet<string> ReferencedNames(ExpressionNode expr)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        CollectReferencedNames(expr, names);
        return names;
    }

    private static void CollectReferencedNames(ExpressionNode expr, HashSet<string> names)
    {
        foreach (var node in DescendantsAndSelf(expr))
        {
            if (node is ReferenceNode reference)
                names.Add(reference.Name);
        }
    }

    private static IEnumerable<AstNode> DescendantsAndSelf(AstNode node)
    {
        yield return node;
        foreach (var child in Calor.Compiler.Analysis.RecursiveAstWalker.GetAllChildren(node))
        {
            foreach (var descendant in DescendantsAndSelf(child))
                yield return descendant;
        }
    }

    private static HashSet<string> CollectAssignedNames(IReadOnlyList<StatementNode> statements, bool includeBinds = true)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in statements.SelectMany(DescendantsAndSelf))
        {
            switch (node)
            {
                case BindStatementNode bind:
                    if (includeBinds)
                        names.Add(bind.Name);
                    break;
                case AssignmentStatementNode { Target: ReferenceNode target }:
                    names.Add(target.Name);
                    names.Add(target.Name.Split('.')[0]);
                    break;
                case CompoundAssignmentStatementNode { Target: ReferenceNode compoundTarget }:
                    names.Add(compoundTarget.Name);
                    break;
                case AssignmentStatementNode assignment when RootName(assignment.Target) is { } root:
                    names.Add(root);
                    break;
                case CompoundAssignmentStatementNode compound when RootName(compound.Target) is { } compoundRoot:
                    names.Add(compoundRoot);
                    break;
                case CollectionPushNode push:
                    names.Add(push.CollectionName);
                    break;
                case DictionaryPutNode put:
                    names.Add(put.DictionaryName);
                    break;
                case CollectionRemoveNode remove:
                    names.Add(remove.CollectionName);
                    break;
                case CollectionSetIndexNode setIndex:
                    names.Add(setIndex.CollectionName);
                    break;
                case CollectionClearNode clear:
                    names.Add(clear.CollectionName);
                    break;
                case CollectionInsertNode insert:
                    names.Add(insert.CollectionName);
                    break;
                case UnaryOperationNode unary when IsIncrement(unary) && RootName(unary.Operand) is { } unaryRoot:
                    names.Add(unaryRoot);
                    break;
                case VariablePatternNode or VarPatternNode or LambdaParameterNode or ArrayCreationNode
                        or MultiDimArrayCreationNode or ListCreationNode or DictionaryCreationNode or SetCreationNode:
                    if (node.GetType().GetProperty("Name")?.GetValue(node) is string declared)
                        names.Add(declared);
                    break;
                case ForStatementNode forStmt:
                    names.Add(forStmt.VariableName);
                    break;
                case ForeachStatementNode foreachStmt:
                    names.Add(foreachStmt.VariableName);
                    break;
                case CallExpressionNode call:
                    AddByReferenceArguments(call.Arguments, call.ArgumentModifiers, names);
                    break;
                case CallStatementNode callStatement:
                    AddByReferenceArguments(
                        callStatement.Arguments, callStatement.ArgumentModifiers, names);
                    break;
            }
            // Any other binder (catch variable, foreach key/value, using, is-pattern).
            foreach (var binder in new[] { "VariableName", "KeyName", "ValueName", "BindingName", "IndexVariableName" })
            {
                if (node.GetType().GetProperty(binder)?.GetValue(node) is string bound)
                    names.Add(bound);
            }
        }

        return names;
    }

    /// <summary>The variable at the root of an element or field store target (a[i], o.f).</summary>
    private static string? RootName(ExpressionNode target) => target switch
    {
        ReferenceNode reference => reference.Name.Split('.')[0],
        ArrayAccessNode access => RootName(access.Array),
        MultiDimArrayAccessNode multi => RootName(multi.Array),
        FieldAccessNode field => RootName(field.Target),
        _ => null
    };

    /// <summary>#1413: a variable passed as `ref` or `out` may be rebound by the callee.</summary>
    private static void AddByReferenceArguments(
        IReadOnlyList<ExpressionNode> arguments,
        IReadOnlyList<string?>? modifiers,
        HashSet<string> names)
    {
        if (modifiers == null)
            return;
        for (var index = 0; index < arguments.Count && index < modifiers.Count; index++)
        {
            if (modifiers[index] is "ref" or "out"
                && arguments[index] is ReferenceNode reference)
            {
                names.Add(reference.Name);
            }
        }
    }

    /// <summary>
    /// Substitutes SelfRefNode (#) with a ReferenceNode for the given variable name.
    /// Returns a new expression tree with substitutions applied.
    /// </summary>
    public static ExpressionNode SubstituteSelfRefStatic(ExpressionNode expr, string variableName)
        => SubstituteSelfRefStatic(
            expr,
            new ReferenceNode(expr.Span, variableName));

    /// <summary>
    /// Substitutes SelfRefNode (#) with an arbitrary expression.
    /// </summary>
    public static ExpressionNode SubstituteSelfRefStatic(
        ExpressionNode expr,
        ExpressionNode replacement)
    {
        var substituted = SubstituteSelfRef(expr, replacement);
        return ContainsSelfRef(substituted)
            ? new BoolLiteralNode(expr.Span, false)
            : substituted;
    }

    private static ExpressionNode SubstituteSelfRef(
        ExpressionNode expr,
        ExpressionNode replacement)
    {
        if (expr is SelfRefNode)
        {
            return replacement;
        }

        if (expr is BinaryOperationNode binOp)
        {
            var left = SubstituteSelfRef(binOp.Left, replacement);
            var right = SubstituteSelfRef(binOp.Right, replacement);
            if (!ReferenceEquals(left, binOp.Left) || !ReferenceEquals(right, binOp.Right))
                return new BinaryOperationNode(binOp.Span, binOp.Operator, left, right);
            return binOp;
        }

        if (expr is UnaryOperationNode unOp)
        {
            var operand = SubstituteSelfRef(unOp.Operand, replacement);
            if (operand != unOp.Operand)
                return new UnaryOperationNode(unOp.Span, unOp.Operator, operand);
            return unOp;
        }

        if (expr is ConditionalExpressionNode conditional)
        {
            var condition = SubstituteSelfRef(
                conditional.Condition,
                replacement);
            var whenTrue = SubstituteSelfRef(
                conditional.WhenTrue,
                replacement);
            var whenFalse = SubstituteSelfRef(
                conditional.WhenFalse,
                replacement);
            return ReferenceEquals(condition, conditional.Condition)
                    && ReferenceEquals(whenTrue, conditional.WhenTrue)
                    && ReferenceEquals(whenFalse, conditional.WhenFalse)
                ? conditional
                : new ConditionalExpressionNode(
                    conditional.Span,
                    condition,
                    whenTrue,
                    whenFalse);
        }

        if (expr is ArrayAccessNode arrayAccess)
        {
            var array = SubstituteSelfRef(arrayAccess.Array, replacement);
            var index = SubstituteSelfRef(arrayAccess.Index, replacement);
            return ReferenceEquals(array, arrayAccess.Array)
                    && ReferenceEquals(index, arrayAccess.Index)
                ? arrayAccess
                : new ArrayAccessNode(arrayAccess.Span, array, index);
        }

        if (expr is ArrayLengthNode arrayLength)
        {
            var array = SubstituteSelfRef(arrayLength.Array, replacement);
            return ReferenceEquals(array, arrayLength.Array)
                ? arrayLength
                : new ArrayLengthNode(arrayLength.Span, array);
        }

        if (expr is FieldAccessNode fieldAccess)
        {
            var target = SubstituteSelfRef(fieldAccess.Target, replacement);
            return ReferenceEquals(target, fieldAccess.Target)
                ? fieldAccess
                : new FieldAccessNode(
                    fieldAccess.Span,
                    target,
                    fieldAccess.FieldName);
        }

        if (expr is StringOperationNode stringOperation)
        {
            var arguments = stringOperation.Arguments
                .Select(argument => SubstituteSelfRef(argument, replacement))
                .ToArray();
            return arguments
                .Zip(
                    stringOperation.Arguments,
                    ReferenceEquals)
                .All(unchanged => unchanged)
                ? stringOperation
                : new StringOperationNode(
                    stringOperation.Span,
                    stringOperation.Operation,
                    arguments,
                    stringOperation.ComparisonMode);
        }

        if (expr is ImplicationExpressionNode implication)
        {
            var antecedent = SubstituteSelfRef(
                implication.Antecedent,
                replacement);
            var consequent = SubstituteSelfRef(
                implication.Consequent,
                replacement);
            return ReferenceEquals(antecedent, implication.Antecedent)
                    && ReferenceEquals(consequent, implication.Consequent)
                ? implication
                : new ImplicationExpressionNode(
                    implication.Span,
                    antecedent,
                    consequent);
        }

        if (expr is ForallExpressionNode forall)
        {
            if (ReplacementCouldBeCaptured(
                    replacement,
                    forall.BoundVariables))
            {
                return forall;
            }
            var body = SubstituteSelfRef(forall.Body, replacement);
            return ReferenceEquals(body, forall.Body)
                ? forall
                : new ForallExpressionNode(
                    forall.Span,
                    forall.BoundVariables,
                    body);
        }

        if (expr is ExistsExpressionNode exists)
        {
            if (ReplacementCouldBeCaptured(
                    replacement,
                    exists.BoundVariables))
            {
                return exists;
            }
            var body = SubstituteSelfRef(exists.Body, replacement);
            return ReferenceEquals(body, exists.Body)
                ? exists
                : new ExistsExpressionNode(
                    exists.Span,
                    exists.BoundVariables,
                    body);
        }

        return expr;
    }

    private static bool ReplacementCouldBeCaptured(
        ExpressionNode replacement,
        IReadOnlyList<QuantifierVariableNode> boundVariables)
    {
        var boundNames = boundVariables
            .Select(variable => variable.Name)
            .ToHashSet(StringComparer.Ordinal);
        return EnumerateDescendantsAndSelf(replacement)
            .OfType<ReferenceNode>()
            .Any(reference => boundNames.Contains(reference.Name));
    }

    private static bool ContainsSelfRef(ExpressionNode expression)
    {
        if (expression is SelfRefNode)
            return true;

        return Calor.Compiler.Analysis.RecursiveAstWalker
            .GetAllChildren(expression)
            .OfType<ExpressionNode>()
            .Any(ContainsSelfRef);
    }

    private static IEnumerable<AstNode> EnumerateDescendantsAndSelf(
        AstNode node)
    {
        yield return node;
        foreach (var child in Calor.Compiler.Analysis.RecursiveAstWalker
                     .GetAllChildren(node))
        {
            foreach (var descendant in EnumerateDescendantsAndSelf(child))
                yield return descendant;
        }
    }
}
