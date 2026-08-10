using System.Reflection;
using System.Runtime.CompilerServices;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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

    [Fact]
    public void EveryConcreteAstNode_DispatchesExactlyOnceToMatchingVisitorMethod()
    {
        var nodeTypes = typeof(AstNode).Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract && typeof(AstNode).IsAssignableFrom(type))
            .OrderBy(type => type.Name)
            .ToList();

        var nonGenericVisitor = DispatchProxy.Create<IAstVisitor, RecordingVisitorProxy>();
        var nonGenericRecorder = (RecordingVisitorProxy)(object)nonGenericVisitor;
        var genericVisitor = DispatchProxy.Create<IAstVisitor<object?>, RecordingVisitorProxy>();
        var genericRecorder = (RecordingVisitorProxy)(object)genericVisitor;

        foreach (var nodeType in nodeTypes)
        {
            var node = (AstNode)RuntimeHelpers.GetUninitializedObject(nodeType);

            nonGenericRecorder.Reset();
            node.Accept(nonGenericVisitor);
            AssertSingleMatchingDispatch(nodeType, node, nonGenericRecorder.Calls);

            genericRecorder.Reset();
            node.Accept(genericVisitor);
            AssertSingleMatchingDispatch(nodeType, node, genericRecorder.Calls);
        }
    }

    [Fact]
    public void ParserOnlyKeywordArgument_IsNotAnAstNode()
    {
        Assert.False(typeof(AstNode).IsAssignableFrom(typeof(KeywordArgNode)));
    }

    [Fact]
    public void BinderRegistry_CoversEveryConcreteExpressionNode()
    {
        var concreteExpressions = typeof(ExpressionNode).Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract && typeof(ExpressionNode).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        var property = typeof(Calor.Compiler.Binding.Binder).GetProperty(
            "RegisteredExpressionNodeTypes",
            BindingFlags.Static | BindingFlags.NonPublic);
        var registeredExpressions = Assert.IsAssignableFrom<IEnumerable<Type>>(
                property?.GetValue(null))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(60, concreteExpressions.Length);
        Assert.Equal(concreteExpressions, registeredExpressions);
    }

    [Fact]
    public void BoundExpressionSwitches_AreUniversalOrExplicitlyAllowlisted()
    {
        var allowances = new Dictionary<string, (string Reason, string Marker)>(StringComparer.Ordinal)
        {
            ["Analysis/CallGraphAnalysis.cs:BuildResolved"] =
                ("Universal DescendantsAndSelf traversal; unmatched calls are explicit.",
                    "unresolved.Add"),
            ["Analysis/CallGraphAnalysis.cs:ResolveBoundCallSites"] =
                ("Universal DescendantsAndSelf traversal; unresolved/incompatible sites remain explicit.",
                    "boundCallSites.Add"),
            ["Effects/ExternalCallCollector.cs:IndexBoundCallReceivers"] =
                ("Universal ChildNodes traversal; unresolved receivers remain explicit raw calls.",
                    "_boundReceiverTypes"),
            ["Analysis/Dataflow/BoundNodeHelpers.cs:IsLiteralZero"] =
                ("Literal classifier, not traversal; non-literals explicitly return false.",
                    "_ => false"),
            ["Analysis/Security/TaintAnalysis.cs:GetExpressionName"] =
                ("Display-only classifier; unsupported expressions explicitly return null.",
                    "_ => null"),
            ["Analysis/BugPatterns/Patterns/OverflowChecker.cs:GetConstantValue"] =
                ("Constant classifier; unsupported expressions explicitly return null.",
                    "_ => null"),
            ["Analysis/BugPatterns/Patterns/DivisionByZeroChecker.cs:TranslateExpr"] =
                ("Semantic translator returns null, which callers propagate as an explicit inconclusive result.",
                    "_ => null"),
            ["Verification/Z3/KInduction/KInductionProver.cs:GetIntValue"] =
                ("Constant classifier; unsupported expressions explicitly return null.",
                    "_ => null"),
            ["Verification/Z3/KInduction/WhileConditionAnalyzer.cs:GetIntValue"] =
                ("Constant classifier; unsupported expressions explicitly return null.",
                    "_ => null"),
            ["Verification/Z3/KInduction/WhileConditionAnalyzer.cs:GetConditionString"] =
                ("Formatting extractor; unsupported expressions explicitly return null.",
                    "_ => null"),
        };

        var compilerRoot = Path.Combine(
            RepoRoot(),
            "src",
            "Calor.Compiler");
        var discovered = new HashSet<string>(StringComparer.Ordinal);
        var violations = new List<string>();
        var boundExpressionTypes = typeof(BoundExpression).Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract && typeof(BoundExpression).IsAssignableFrom(type))
            .Select(type => type.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(compilerRoot, "*.cs", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(compilerRoot, file).Replace('\\', '/');
            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot();
            foreach (var switchNode in root.DescendantNodes()
                         .Where(node => node is SwitchStatementSyntax or SwitchExpressionSyntax))
            {
                var switchedTypes = GetSwitchPatterns(switchNode)
                    .SelectMany(GetRootPatternTypeNames)
                    .Select(typeName => typeName.Split('.').Last())
                    .ToArray();
                if (!switchedTypes.Any(boundExpressionTypes.Contains))
                {
                    continue;
                }

                var method = switchNode.Ancestors()
                    .OfType<BaseMethodDeclarationSyntax>()
                    .FirstOrDefault();
                var localFunction = switchNode.Ancestors()
                    .OfType<LocalFunctionStatementSyntax>()
                    .FirstOrDefault();
                var methodName = localFunction?.Identifier.ValueText
                    ?? method switch
                    {
                        MethodDeclarationSyntax declaration => declaration.Identifier.ValueText,
                        ConstructorDeclarationSyntax constructor => constructor.Identifier.ValueText,
                        _ => "<unknown>",
                    };
                var key = $"{relativePath}:{methodName}";
                discovered.Add(key);

                if (!allowances.TryGetValue(key, out var allowance))
                {
                    violations.Add(
                        $"{key} contains a hand-maintained BoundExpression switch. " +
                        "Use ChildNodes/Children traversal or add an explicit incomplete-result allowance.");
                }
                else
                {
                    var containingBody = (SyntaxNode?)localFunction ?? method;
                    if (containingBody == null
                        || !containingBody.ToString().Contains(
                            allowance.Marker,
                            StringComparison.Ordinal))
                    {
                        violations.Add(
                            $"{key} is allowlisted but does not contain its required explicit " +
                            $"incomplete-result marker '{allowance.Marker}': {allowance.Reason}");
                    }
                }
            }
        }

        foreach (var staleAllowance in allowances.Keys.Except(discovered))
            violations.Add($"Stale BoundExpression switch allowance: {staleAllowance}");

        Assert.True(
            violations.Count == 0,
            string.Join(Environment.NewLine, violations));
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

    private static void AssertSingleMatchingDispatch(
        Type nodeType,
        AstNode node,
        IReadOnlyList<(MethodInfo Method, object? Argument)> calls)
    {
        var call = Assert.Single(calls);
        Assert.Equal("Visit", call.Method.Name);
        Assert.Equal(nodeType, Assert.Single(call.Method.GetParameters()).ParameterType);
        Assert.Same(node, call.Argument);
    }

    public class RecordingVisitorProxy : DispatchProxy
    {
        public List<(MethodInfo Method, object? Argument)> Calls { get; } = new();

        public void Reset() => Calls.Clear();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Assert.NotNull(targetMethod);
            Calls.Add((targetMethod, args is { Length: > 0 } ? args[0] : null));
            return null;
        }
    }

    private static string RepoRoot() =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));

    private static IEnumerable<PatternSyntax> GetSwitchPatterns(SyntaxNode switchNode)
    {
        if (switchNode is SwitchStatementSyntax statement)
        {
            return statement.Sections
                .SelectMany(section => section.Labels)
                .OfType<CasePatternSwitchLabelSyntax>()
                .Select(label => label.Pattern);
        }

        return ((SwitchExpressionSyntax)switchNode).Arms.Select(arm => arm.Pattern);
    }

    private static IEnumerable<string> GetRootPatternTypeNames(PatternSyntax pattern)
    {
        switch (pattern)
        {
            case DeclarationPatternSyntax declaration:
                yield return declaration.Type.ToString();
                break;
            case RecursivePatternSyntax { Type: not null } recursive:
                yield return recursive.Type.ToString();
                break;
            case TypePatternSyntax typePattern:
                yield return typePattern.Type.ToString();
                break;
            case BinaryPatternSyntax binary:
                foreach (var typeName in GetRootPatternTypeNames(binary.Left))
                    yield return typeName;
                foreach (var typeName in GetRootPatternTypeNames(binary.Right))
                    yield return typeName;
                break;
            case ParenthesizedPatternSyntax parenthesized:
                foreach (var typeName in GetRootPatternTypeNames(parenthesized.Pattern))
                    yield return typeName;
                break;
        }
    }
}
