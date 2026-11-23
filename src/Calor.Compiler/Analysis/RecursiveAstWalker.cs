using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using Calor.Compiler.Ast;

namespace Calor.Compiler.Analysis;

/// <summary>
/// Reflection-based structural walker that enumerates the child
/// <see cref="AstNode"/>s of a node via its public properties — including
/// statement bodies and nested member/clause nodes — but EXCLUDING any child
/// whose (static or runtime) type is an <see cref="ExpressionNode"/>.
///
/// <para>Skipping expression subtrees is deliberate. It keeps consumers such as
/// <see cref="ReturnValidationPass"/> from descending into lambda bodies
/// (<see cref="LambdaExpressionNode"/>) and match-expression arms
/// (<see cref="MatchExpressionNode"/>), where a <c>§R</c> is a value rather than
/// a statement-position member return.</para>
///
/// <para>Because coverage of statement bodies is by <em>construction</em> — any
/// property typed <c>StatementNode</c> or <c>IEnumerable&lt;StatementNode&gt;</c>
/// is traversed — adding a new statement-bearing AST node is covered
/// automatically without editing this walker. The completeness meta-test
/// (<c>ReturnValidationCompletenessTests</c>) enforces that guarantee.</para>
/// </summary>
public static class RecursiveAstWalker
{
    /// <summary>
    /// A structural edge from a parent AST node to one child, retaining the
    /// public property that owns the child. Consumers that enforce context-
    /// sensitive rules (for example catch/finally yield legality) can therefore
    /// stay exhaustive without hand-maintaining a container switch.
    /// </summary>
    public readonly record struct ChildEdge(AstNode Node, PropertyInfo Property);

    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> NonExpressionCache = new();
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> AllChildrenCache = new();

    /// <summary>
    /// Enumerates the direct child <see cref="AstNode"/>s of
    /// <paramref name="node"/> that are not expressions, in a deterministic
    /// order (declaration order, then property name).
    /// </summary>
    public static IEnumerable<AstNode> GetChildren(AstNode node)
    {
        if (node is null)
        {
            yield break;
        }

        var yielded = new HashSet<AstNode>(ReferenceEqualityComparer.Instance);
        foreach (var prop in GetChildProperties(node.GetType()))
        {
            object? value;
            try
            {
                value = prop.GetValue(node);
            }
            catch
            {
                // A property getter should never throw on a parsed AST, but be
                // defensive so an unexpected node can't crash compilation.
                continue;
            }

            if (value is null)
            {
                continue;
            }

            if (value is AstNode single)
            {
                if (single is not ExpressionNode && yielded.Add(single))
                {
                    yield return single;
                }
            }
            else if (value is IEnumerable seq)
            {
                foreach (var item in seq)
                {
                    if (item is AstNode child
                        && child is not ExpressionNode
                        && yielded.Add(child))
                    {
                        yield return child;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Returns the cached set of properties on <paramref name="type"/> that can
    /// hold non-expression child <see cref="AstNode"/>s (single or enumerable),
    /// in a deterministic order.
    /// </summary>
    public static PropertyInfo[] GetChildProperties(Type type) =>
        NonExpressionCache.GetOrAdd(type, static t => IsSeparateCallable(t) ? [] :
            t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetIndexParameters().Length == 0 && CanHoldChildAstNode(p.PropertyType))
                .OrderBy(p => p.MetadataToken)
                .ThenBy(p => p.Name, StringComparer.Ordinal)
                .ToArray());

    /// <summary>
    /// Enumerates every direct AST child, including expression subtrees. This is
    /// the universal traversal surface for analyses that must not hide nested
    /// calls or diagnostic seeds inside newly added expression wrappers.
    /// </summary>
    /// <remarks>
    /// The same children, in the same order, as the nodes of
    /// <see cref="GetAllChildEdges"/>. Both unwrap a property value's first level
    /// in place (a node, an initializer, a refinement, or a sequence of those) and
    /// fall back to <see cref="UnwrapAstChildren"/> only for a nested sequence, so
    /// a visit no longer allocates an iterator per child (#1738).
    /// </remarks>
    public static IEnumerable<AstNode> GetAllChildren(AstNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var yielded = new YieldedChildren();

        foreach (var prop in GetAllChildProperties(node.GetType()))
        {
            // Universal analyses must not silently hide a subtree if an AST accessor fails.
            var value = prop.GetValue(node);
            if (value is IEnumerable sequence and not string and not AstNode)
            {
                foreach (var item in sequence)
                {
                    if (TryUnwrapDirect(item, out var direct))
                    {
                        if (yielded.Add(direct))
                            yield return direct;
                    }
                    else if (item is IEnumerable and not string)
                    {
                        foreach (var child in UnwrapAstChildren(item))
                            if (yielded.Add(child))
                                yield return child;
                    }
                }
            }
            else if (TryUnwrapDirect(value, out var direct) && yielded.Add(direct))
                yield return direct;
        }
    }

    /// <summary>
    /// Enumerates every direct AST child, including expression subtrees, together
    /// with the property that contains it.
    /// </summary>
    public static IEnumerable<ChildEdge> GetAllChildEdges(AstNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var yielded = new YieldedChildren();

        foreach (var prop in GetAllChildProperties(node.GetType()))
        {
            // Universal analyses must not silently hide a subtree if an AST accessor fails.
            var value = prop.GetValue(node);
            if (value is IEnumerable sequence and not string and not AstNode)
            {
                foreach (var item in sequence)
                {
                    if (TryUnwrapDirect(item, out var direct))
                    {
                        if (yielded.Add(direct))
                            yield return new ChildEdge(direct, prop);
                    }
                    else if (item is IEnumerable and not string)
                    {
                        foreach (var child in UnwrapAstChildren(item))
                            if (yielded.Add(child))
                                yield return new ChildEdge(child, prop);
                    }
                }
            }
            else if (TryUnwrapDirect(value, out var direct) && yielded.Add(direct))
                yield return new ChildEdge(direct, prop);
        }
    }

    // The non-sequence cases of UnwrapAstChildren, which yield exactly one child.
    private static bool TryUnwrapDirect(object? value, out AstNode child)
    {
        switch (value)
        {
            case AstNode node:
                child = node;
                return true;
            case ObjectInitializerAssignment initializer:
                child = initializer.Value;
                return true;
            case InlineRefinementInfo refinement:
                child = refinement.Predicate;
                return true;
            default:
                child = null!;
                return false;
        }
    }

    /// <summary>
    /// The children already yielded for one node, compared by reference: the same
    /// answers as a <see cref="ReferenceEqualityComparer"/> hash set. Most nodes have
    /// a handful of children, so they are compared by a scan (the first without any
    /// storage); a node with more switches to a hash set. Hashing every child of every
    /// node was a large part of a project index build (#1738). A mutable struct held in
    /// an iterator's local is a field of the iterator, so each Add updates it in place.
    /// </summary>
    private struct YieldedChildren
    {
        private const int ScanLimit = 16;
        private int _count;
        private AstNode? _first;
        private AstNode?[]? _rest;
        private HashSet<AstNode?>? _hashed;

        public bool Add(AstNode? child)
        {
            if (_hashed != null)
                return _hashed.Add(child);
            if (_count > 0)
            {
                if (ReferenceEquals(_first, child))
                    return false;
                for (var index = 0; index < _count - 1; index++)
                {
                    if (ReferenceEquals(_rest![index], child))
                        return false;
                }
            }
            if (_count == ScanLimit)
            {
                _hashed = new HashSet<AstNode?>(ReferenceEqualityComparer.Instance) { _first };
                for (var index = 0; index < _count - 1; index++)
                    _hashed.Add(_rest![index]);
                _hashed.Add(child);
                _rest = null;
                return true;
            }
            if (_count == 0)
                _first = child;
            else
            {
                _rest ??= new AstNode?[4];
                if (_count - 1 == _rest.Length)
                    Array.Resize(ref _rest, _rest.Length * 2);
                _rest[_count - 1] = child;
            }
            _count++;
            return true;
        }
    }

    private static IEnumerable<AstNode> UnwrapAstChildren(object? value)
    {
        switch (value)
        {
            case AstNode node:
                yield return node;
                break;
            case ObjectInitializerAssignment initializer:
                yield return initializer.Value;
                break;
            case InlineRefinementInfo refinement:
                yield return refinement.Predicate;
                break;
            case IEnumerable sequence when value is not string:
                foreach (var item in sequence)
                    foreach (var child in UnwrapAstChildren(item))
                        yield return child;
                break;
        }
    }

    /// <summary>
    /// Enumerates statement roots and every nested statement container while
    /// deliberately not crossing expression boundaries such as lambdas. This is
    /// the shared statement traversal surface for iterator classification,
    /// structural return lowering preflights, legality checks, and future
    /// statement transforms.
    /// </summary>
    public static IEnumerable<StatementNode> EnumerateStatements(
        IEnumerable<StatementNode> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);

        foreach (var root in roots)
        {
            foreach (var node in DescendantsAndSelf(root))
            {
                if (node is StatementNode statement)
                    yield return statement;
            }
        }
    }

    /// <summary>
    /// Enumerates a node and all structural descendants without crossing into
    /// expression subtrees.
    /// </summary>
    public static IEnumerable<AstNode> DescendantsAndSelf(AstNode root)
    {
        ArgumentNullException.ThrowIfNull(root);

        yield return root;
        foreach (var child in GetChildren(root))
        {
            foreach (var descendant in DescendantsAndSelf(child))
                yield return descendant;
        }
    }

    public static PropertyInfo[] GetAllChildProperties(Type type) =>
        AllChildrenCache.GetOrAdd(type, static t => IsSeparateCallable(t) ? [] :
            t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetIndexParameters().Length == 0 && CanHoldAnyAstNode(p.PropertyType))
                .OrderBy(p => p.MetadataToken)
                .ThenBy(p => p.Name, StringComparer.Ordinal)
                .ToArray());

    /// <summary>0.25 F3 (#847): a local function is a leaf; its body is another
    /// callable, so nothing in it belongs to the enclosing body.</summary>
    public static bool IsSeparateCallable(Type type) => type == typeof(LocalFunctionStatementNode);

    private static bool CanHoldChildAstNode(Type propertyType)
    {
        if (typeof(AstNode).IsAssignableFrom(propertyType))
        {
            // A property statically typed as an expression subtree is skipped
            // wholesale. Base-typed AstNode properties are still visited; the
            // runtime `is not ExpressionNode` filter in GetChildren guards them.
            return !typeof(ExpressionNode).IsAssignableFrom(propertyType);
        }

        var element = GetEnumerableElementType(propertyType);
        if (element is null || !typeof(AstNode).IsAssignableFrom(element))
        {
            return false;
        }

        return !typeof(ExpressionNode).IsAssignableFrom(element);
    }

    private static bool CanHoldAnyAstNode(Type propertyType)
    {
        if (typeof(AstNode).IsAssignableFrom(propertyType)
            || propertyType == typeof(ObjectInitializerAssignment)
            || propertyType == typeof(InlineRefinementInfo))
            return true;

        var element = GetEnumerableElementType(propertyType);
        return element != null && CanHoldAnyAstNode(element);
    }

    private static Type? GetEnumerableElementType(Type type)
    {
        if (type == typeof(string))
        {
            return null;
        }

        foreach (var candidate in Prepend(type, type.GetInterfaces()))
        {
            if (candidate.IsGenericType &&
                candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                return candidate.GetGenericArguments()[0];
            }
        }

        return null;
    }

    private static IEnumerable<Type> Prepend(Type first, Type[] rest)
    {
        yield return first;
        foreach (var t in rest)
        {
            yield return t;
        }
    }
}
