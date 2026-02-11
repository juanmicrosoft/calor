using System.Reflection;
using Calor.Compiler.Ast;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests that verify architectural invariants of the codebase.
/// These tests help catch common mistakes when extending the compiler.
/// </summary>
public class ArchitectureTests
{
    /// <summary>
    /// Verifies that all IAstVisitor implementations have Visit methods for all visitable AST node types.
    /// A node type is considered "visitable" if it has an Accept(IAstVisitor) method.
    /// This prevents the common bug where a new AST node is added but some visitor implementations
    /// are missed, causing runtime failures.
    /// </summary>
    [Fact]
    public void AllVisitors_ImplementVisitMethodsForAllNodeTypes()
    {
        var assembly = typeof(IAstVisitor).Assembly;

        // Find all concrete AST node types that are defined in the visitor interface
        // The visitor interface is the source of truth for which nodes need visitor methods
        var visitableNodeTypes = typeof(IAstVisitor)
            .GetMethods()
            .Where(m => m.Name == "Visit" && m.GetParameters().Length == 1)
            .Select(m => m.GetParameters()[0].ParameterType)
            .ToHashSet();

        var nodeTypes = assembly
            .GetTypes()
            .Where(t => !t.IsAbstract && typeof(AstNode).IsAssignableFrom(t))
            .Where(t => t != typeof(AstNode)) // Exclude base class
            .Where(t => visitableNodeTypes.Contains(t)) // Only nodes defined in visitor interface
            .ToList();

        // Find all types that implement IAstVisitor (non-generic)
        var nonGenericVisitors = assembly
            .GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface)
            .Where(t => typeof(IAstVisitor).IsAssignableFrom(t))
            .ToList();

        // Find all types that implement IAstVisitor<T>
        var genericVisitors = assembly
            .GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface)
            .Where(t => t.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IAstVisitor<>)))
            .ToList();

        var allVisitors = nonGenericVisitors.Union(genericVisitors).Distinct().ToList();

        var missingMethods = new List<string>();

        foreach (var visitorType in allVisitors)
        {
            foreach (var nodeType in nodeTypes)
            {
                // Check if the visitor has a Visit method that takes this node type
                var hasVisitMethod = visitorType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Any(m => m.Name == "Visit" &&
                              m.GetParameters().Length == 1 &&
                              m.GetParameters()[0].ParameterType == nodeType);

                if (!hasVisitMethod)
                {
                    missingMethods.Add($"{visitorType.Name} is missing Visit({nodeType.Name})");
                }
            }
        }

        Assert.True(missingMethods.Count == 0,
            $"The following visitor methods are missing:\n{string.Join("\n", missingMethods)}");
    }

    /// <summary>
    /// Verifies that IAstVisitor and IAstVisitor&lt;T&gt; interfaces have the same Visit method signatures.
    /// This ensures consistency between the two visitor interfaces.
    /// </summary>
    [Fact]
    public void VisitorInterfaces_HaveConsistentVisitMethods()
    {
        var nonGenericMethods = typeof(IAstVisitor)
            .GetMethods()
            .Where(m => m.Name == "Visit")
            .Select(m => m.GetParameters()[0].ParameterType.Name)
            .OrderBy(n => n)
            .ToList();

        var genericMethods = typeof(IAstVisitor<>)
            .GetMethods()
            .Where(m => m.Name == "Visit")
            .Select(m => m.GetParameters()[0].ParameterType.Name)
            .OrderBy(n => n)
            .ToList();

        var onlyInNonGeneric = nonGenericMethods.Except(genericMethods).ToList();
        var onlyInGeneric = genericMethods.Except(nonGenericMethods).ToList();

        var mismatches = new List<string>();

        if (onlyInNonGeneric.Any())
        {
            mismatches.Add($"Only in IAstVisitor: {string.Join(", ", onlyInNonGeneric)}");
        }

        if (onlyInGeneric.Any())
        {
            mismatches.Add($"Only in IAstVisitor<T>: {string.Join(", ", onlyInGeneric)}");
        }

        Assert.True(mismatches.Count == 0,
            $"IAstVisitor and IAstVisitor<T> have inconsistent Visit methods:\n{string.Join("\n", mismatches)}");
    }

    /// <summary>
    /// Lists all current visitor implementations for documentation purposes.
    /// If this test fails, update this list and ensure all visitors are properly documented.
    /// </summary>
    [Fact]
    public void DocumentedVisitors_MatchActualImplementations()
    {
        var assembly = typeof(IAstVisitor).Assembly;

        // Known visitor implementations - update this list when adding new visitors
        var expectedVisitors = new HashSet<string>
        {
            "CSharpEmitter",
            "CalorEmitter",
            "IdScanner",
            "ExpressionSimplifier",
        };

        var actualVisitors = assembly
            .GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface)
            .Where(t => typeof(IAstVisitor).IsAssignableFrom(t) ||
                        t.GetInterfaces().Any(i =>
                            i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IAstVisitor<>)))
            .Select(t => t.Name)
            .ToHashSet();

        var undocumented = actualVisitors.Except(expectedVisitors).ToList();
        var removed = expectedVisitors.Except(actualVisitors).ToList();

        var issues = new List<string>();

        if (undocumented.Any())
        {
            issues.Add($"New visitors found (add to expectedVisitors list): {string.Join(", ", undocumented)}");
        }

        if (removed.Any())
        {
            issues.Add($"Visitors removed (remove from expectedVisitors list): {string.Join(", ", removed)}");
        }

        Assert.True(issues.Count == 0,
            $"Visitor documentation is out of sync:\n{string.Join("\n", issues)}\n\n" +
            $"Current visitors: {string.Join(", ", actualVisitors.OrderBy(x => x))}");
    }

}
