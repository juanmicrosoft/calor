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
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> Cache = new();

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
                if (single is not ExpressionNode)
                {
                    yield return single;
                }
            }
            else if (value is IEnumerable seq)
            {
                foreach (var item in seq)
                {
                    if (item is AstNode child and not ExpressionNode)
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
        Cache.GetOrAdd(type, static t =>
            t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetIndexParameters().Length == 0 && CanHoldChildAstNode(p.PropertyType))
                .OrderBy(p => p.MetadataToken)
                .ThenBy(p => p.Name, StringComparer.Ordinal)
                .ToArray());

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
