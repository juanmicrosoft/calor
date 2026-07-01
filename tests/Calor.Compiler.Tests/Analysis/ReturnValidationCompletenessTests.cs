using System.Collections;
using System.Reflection;
using Calor.Compiler.Analysis;
using Calor.Compiler.Ast;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Completeness guarantee for <see cref="ReturnValidationPass"/>: it must be
/// impossible for a value <c>§R</c> to hide, unchecked, inside a no-value owner.
///
/// <para>The pass relies on <see cref="RecursiveAstWalker"/> traversing every
/// statement-bearing property of every AST node. This meta-test enforces that BY
/// CONSTRUCTION: for each concrete <see cref="AstNode"/> type, every property that
/// can hold a <see cref="StatementNode"/> (directly or via an enumerable) must be
/// visited by the walker — UNLESS the declaring node is itself an
/// <see cref="ExpressionNode"/>, in which case the whole subtree is deliberately
/// skipped (a <c>§R</c> there is a value, e.g. a lambda body or match arm).</para>
///
/// <para>Adding a new statement-bearing node with a public statement property will
/// pass automatically; adding one whose statement property is somehow NOT visited
/// (e.g. non-public) fails here, flagging the coverage hole before it can ship.</para>
/// </summary>
public class ReturnValidationCompletenessTests
{
    /// <summary>
    /// ExpressionNode subtypes whose statement-bearing subtrees are intentionally
    /// NOT walked (a return inside them is a value expression, not a member
    /// return). Documented here so the exemption is explicit and reviewable.
    /// </summary>
    private static readonly HashSet<string> DocumentedExpressionExemptions = new(StringComparer.Ordinal)
    {
        nameof(LambdaExpressionNode),
        nameof(MatchExpressionNode),
    };

    [Fact]
    public void EveryStatementBearingProperty_IsWalked_OrIsAnExpressionSubtree()
    {
        var assembly = typeof(AstNode).Assembly;
        var astTypes = assembly.GetTypes()
            .Where(t => typeof(AstNode).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
            .OrderBy(t => t.FullName, StringComparer.Ordinal);

        var holes = new List<string>();
        var exemptedExpressionTypes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var type in astTypes)
        {
            var isExpression = typeof(ExpressionNode).IsAssignableFrom(type);
            var walkedProperties = RecursiveAstWalker.GetChildProperties(type)
                .Select(p => p.Name)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.GetIndexParameters().Length != 0)
                {
                    continue;
                }

                if (!CanHoldStatement(prop.PropertyType))
                {
                    continue;
                }

                if (isExpression)
                {
                    // The whole expression subtree is skipped by design; record it
                    // so we can assert the exemption set stays documented.
                    exemptedExpressionTypes.Add(type.Name);
                    continue;
                }

                if (!walkedProperties.Contains(prop.Name))
                {
                    holes.Add($"{type.FullName}.{prop.Name} ({prop.PropertyType.Name})");
                }
            }
        }

        Assert.True(
            holes.Count == 0,
            "Statement-bearing properties NOT visited by RecursiveAstWalker (a §R could " +
            "hide unchecked here):\n  " + string.Join("\n  ", holes));

        // Any ExpressionNode type that carries statements must be a documented
        // exemption — otherwise we've silently grown a new unchecked hiding place.
        var undocumented = exemptedExpressionTypes
            .Where(name => !DocumentedExpressionExemptions.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            undocumented.Count == 0,
            "Undocumented ExpressionNode subtypes carrying statement subtrees " +
            "(add to DocumentedExpressionExemptions after confirming a §R there is a " +
            "value, not a member return):\n  " + string.Join("\n  ", undocumented));
    }

    private static bool CanHoldStatement(Type propertyType)
    {
        if (typeof(AstNode).IsAssignableFrom(propertyType))
        {
            return typeof(StatementNode).IsAssignableFrom(propertyType);
        }

        var element = GetEnumerableElementType(propertyType);
        return element is not null && typeof(StatementNode).IsAssignableFrom(element);
    }

    private static Type? GetEnumerableElementType(Type type)
    {
        if (type == typeof(string))
        {
            return null;
        }

        var candidates = new List<Type> { type };
        candidates.AddRange(type.GetInterfaces());

        foreach (var candidate in candidates)
        {
            if (candidate.IsGenericType &&
                candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                return candidate.GetGenericArguments()[0];
            }
        }

        return null;
    }
}
