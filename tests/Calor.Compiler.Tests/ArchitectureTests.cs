using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Calor.Compiler.Analysis;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
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
    public void AstSchema_CoversEveryNodeDispatchAndChildRelation()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(RepoRoot(), "eng", "ast-schema.json")));
        var schemaNodes = document.RootElement.GetProperty("nodes")
            .EnumerateArray()
            .Select(entry => (
                Name: entry.GetProperty("name").GetString()!,
                Source: entry.GetProperty("source").GetString()!))
            .OrderBy(entry => entry.Name, StringComparer.Ordinal)
            .ToArray();
        var concreteNodes = typeof(AstNode).Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract && typeof(AstNode).IsAssignableFrom(type))
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(concreteNodes.Select(type => type.Name), schemaNodes.Select(entry => entry.Name));
        Assert.Equal(
            concreteNodes,
            AstSchemaMetadata.NodeTypes.OrderBy(type => type.Name, StringComparer.Ordinal));

        var nonGenericVisits = typeof(IAstVisitor).GetMethods()
            .Select(method => Assert.Single(method.GetParameters()).ParameterType)
            .OrderBy(type => type.Name, StringComparer.Ordinal);
        var genericVisits = typeof(IAstVisitor<>).GetMethods()
            .Select(method => Assert.Single(method.GetParameters()).ParameterType)
            .OrderBy(type => type.Name, StringComparer.Ordinal);
        Assert.Equal(concreteNodes, nonGenericVisits);
        Assert.Equal(concreteNodes, genericVisits);

        foreach (var node in AstSchemaMetadata.Nodes)
        {
            Assert.Equal(
                RecursiveAstWalker.GetAllChildProperties(node.NodeType)
                    .Select(property => property.Name)
                    .OrderBy(name => name, StringComparer.Ordinal),
                node.ChildProperties);
            var sourcePath = Path.Combine(
                RepoRoot(),
                "src",
                "Calor.Compiler",
                "Ast",
                node.SourceFile);
            Assert.True(File.Exists(sourcePath), $"Missing schema source {node.SourceFile}");
        }

        var redundantDispatch = concreteNodes
            .SelectMany(type => type.GetMethods(
                BindingFlags.Public |
                BindingFlags.Instance |
                BindingFlags.DeclaredOnly))
            .Where(method => method.Name == nameof(AstNode.Accept))
            .Select(method => method.DeclaringType!.Name)
            .ToArray();
        Assert.Empty(redundantDispatch);
    }

    [Fact]
    public void ModuleUpdate_MirrorsEveryAggregateFieldAndPreservesMetadata()
    {
        var aggregateProperties = typeof(ModuleNode)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(property => property.Name != nameof(ModuleNode.HasExtendedMetadata))
            .Select(property => property.Name)
            .Append(nameof(AstNode.Span))
            .OrderBy(name => name, StringComparer.Ordinal);
        var updateProperties = typeof(ModuleNode.ModuleUpdate)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal);
        Assert.Equal(aggregateProperties, updateProperties);

        var original = EmptyModule("Original");
        original.NamespaceIdentity = "Example";
        original.NamespaceScopeId = "scope";
        original.FullyQualifiedSymbolIdentity = "Example.Original";
        original.DocComment = "docs";
        var copy = original.With(update => update.Name = "Copy");

        Assert.Equal("Copy", copy.Name);
        foreach (var property in typeof(ModuleNode)
                     .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(property => property.Name is not nameof(ModuleNode.Name)))
        {
            Assert.Equal(property.GetValue(original), property.GetValue(copy));
        }
        Assert.Equal(original.NamespaceIdentity, copy.NamespaceIdentity);
        Assert.Equal(original.NamespaceScopeId, copy.NamespaceScopeId);
        Assert.Equal(original.FullyQualifiedSymbolIdentity, copy.FullyQualifiedSymbolIdentity);
        Assert.Equal(original.DocComment, copy.DocComment);

        var astNodeSource = CSharpSyntaxTree.ParseText(
            File.ReadAllText(Path.Combine(
                RepoRoot(),
                "src",
                "Calor.Compiler",
                "Ast",
                "AstNode.cs"))).GetRoot();
        var copiedMetadata = astNodeSource.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "CopyMetadataTo")
            .DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Select(assignment => assignment.Left)
            .OfType<MemberAccessExpressionSyntax>()
            .Where(access => access.Expression.ToString() == "target")
            .Select(access => access.Name.Identifier.ValueText)
            .OrderBy(name => name, StringComparer.Ordinal);
        var mutableMetadata = typeof(AstNode)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(property => property.SetMethod != null)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal);
        Assert.Equal(mutableMetadata, copiedMetadata);
    }

    [Fact]
    public void Emitters_ReusedAcrossModules_MatchFreshInstances()
    {
        var first = ParseModule(
            """
            §M{m001:First}
              §F{f001:Run:pub} () -> void
                §E{cw}
                §L{loop:i:1:3:1}
                  §IF{branch} (== (% i 2) 0)
                    §P "even"
                  §EL
                    §P i
            """);
        var second = ParseModule(
            """
            §M{m002:Second}
              §F{f002:Run:pub} () -> int
                §E{}
                §B{value:int} 42
                §R value
            """);

        var reusedCSharp = new CSharpEmitter();
        _ = reusedCSharp.Emit(first);
        Assert.Equal(new CSharpEmitter().Emit(second), reusedCSharp.Emit(second));

        var reusedCalor = new CalorEmitter();
        _ = reusedCalor.Emit(first);
        Assert.Equal(new CalorEmitter().Emit(second), reusedCalor.Emit(second));
    }

    [Fact]
    public void ModuleConstruction_IsRestrictedToCreationBoundaries()
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "Migration/CSharpToCalorConverter.cs:ConvertWholeFileNamespaceInterop",
            "Migration/CSharpToCalorConverter.cs:MemberParsesCleanly",
            "Migration/RoslynSyntaxVisitor.cs:Convert",
            "Parsing/Parser.cs:ParseModule",
        };
        var actual = new HashSet<string>(StringComparer.Ordinal);
        var compilerRoot = Path.Combine(RepoRoot(), "src", "Calor.Compiler");

        foreach (var file in Directory.EnumerateFiles(
                     compilerRoot,
                     "*.cs",
                     SearchOption.AllDirectories))
        {
            if (file.EndsWith("Ast/ModuleNode.cs", StringComparison.Ordinal))
                continue;
            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot();
            foreach (var creation in root.DescendantNodes()
                         .OfType<ObjectCreationExpressionSyntax>()
                         .Where(node => node.Type.ToString().EndsWith(
                             nameof(ModuleNode),
                             StringComparison.Ordinal)))
            {
                var method = creation.Ancestors()
                    .OfType<MethodDeclarationSyntax>()
                    .FirstOrDefault();
                Assert.NotNull(method);
                actual.Add(
                    $"{Path.GetRelativePath(compilerRoot, file).Replace('\\', '/')}:" +
                    method.Identifier.ValueText);
            }
        }

        Assert.Equal(allowed.Order(), actual.Order());
    }

    [Fact]
    public void CompilerComponents_MatchDeclaredDependencyContract()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(
                RepoRoot(),
                "eng",
                "compiler-components.json")));
        var components = document.RootElement.GetProperty("components")
            .EnumerateArray()
            .Select(entry => new
            {
                Name = entry.GetProperty("name").GetString()!,
                Path = entry.GetProperty("path").GetString()!,
                Allowed = entry.GetProperty("allowedDependencies")
                    .EnumerateArray()
                    .Select(value => value.GetString()!)
                    .ToHashSet(StringComparer.Ordinal),
            })
            .ToArray();
        var knownCycles = document.RootElement.GetProperty("knownCycles")
            .EnumerateArray()
            .Select(cycle => string.Join(
                "|",
                cycle.EnumerateArray()
                    .Select(value => value.GetString()!)
                    .OrderBy(name => name, StringComparer.Ordinal)))
            .OrderBy(cycle => cycle, StringComparer.Ordinal)
            .ToArray();
        var compilerRoot = Path.Combine(RepoRoot(), "src", "Calor.Compiler");
        var graph = components.ToDictionary(
            component => component.Name,
            component => component.Allowed,
            StringComparer.Ordinal);

        foreach (var component in components)
        {
            var source = string.Join(
                Environment.NewLine,
                Directory.EnumerateFiles(
                        Path.Combine(compilerRoot, component.Path),
                        "*.cs",
                        SearchOption.AllDirectories)
                    .Select(File.ReadAllText));
            var actual = components
                .Where(candidate => candidate.Name != component.Name)
                .Where(candidate => source.Contains(
                    $"Calor.Compiler.{candidate.Name}",
                    StringComparison.Ordinal))
                .Select(candidate => candidate.Name)
                .ToHashSet(StringComparer.Ordinal);
            Assert.True(
                component.Allowed.SetEquals(actual),
                $"{component.Name} dependency contract drifted. " +
                $"Expected [{string.Join(", ", component.Allowed.Order())}], " +
                $"actual [{string.Join(", ", actual.Order())}].");
        }

        bool IsReachable(string start, string target)
        {
            var pending = new Stack<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            pending.Push(start);
            while (pending.Count > 0)
            {
                var current = pending.Pop();
                if (!seen.Add(current))
                    continue;
                foreach (var dependency in graph[current])
                {
                    if (dependency == target)
                        return true;
                    pending.Push(dependency);
                }
            }
            return false;
        }

        var actualCycles = components
            .Select(component => components
                .Where(candidate =>
                    candidate.Name != component.Name
                    && IsReachable(component.Name, candidate.Name)
                    && IsReachable(candidate.Name, component.Name))
                .Select(candidate => candidate.Name)
                .Append(component.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray())
            .Where(cycle => cycle.Length > 1)
            .Select(cycle => string.Join("|", cycle))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(cycle => cycle, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(knownCycles, actualCycles);
    }

    /// <summary>
    /// v0.15 E1 slice 2b: <c>Binding/</c> must not reference <c>Effects/</c>.
    /// The binder is upstream of effect enforcement — a call in the other
    /// direction makes the pass's string tables load-bearing inside binding and
    /// is what PR #1095 review round 1 finding 10 flagged.
    ///
    /// <para><see cref="CompilerComponents_MatchDeclaredDependencyContract"/>
    /// already declares Binding's allowed dependencies (Ast, CodeGen, Parsing —
    /// Effects is absent), but it matches the fully-qualified spelling
    /// <c>Calor.Compiler.Effects</c> only, so a namespace-relative
    /// <c>Effects.EffectEnforcementPass</c> slipped past it. This pin closes
    /// that by working on tokens: an <c>Effects</c> identifier that starts a
    /// qualified name, plus any using directive naming the namespace.</para>
    /// </summary>
    [Fact]
    public void BindingLayer_HasNoReferenceToEffectsNamespace()
    {
        var bindingRoot = Path.Combine(RepoRoot(), "src", "Calor.Compiler", "Binding");
        var offenders = new List<string>();
        var filesScanned = 0;

        foreach (var file in Directory.EnumerateFiles(
                     bindingRoot,
                     "*.cs",
                     SearchOption.AllDirectories))
        {
            filesScanned++;
            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot();
            var relative = Path.GetRelativePath(bindingRoot, file).Replace('\\', '/');

            foreach (var directive in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
            {
                if (directive.Name?.ToString().Contains("Effects", StringComparison.Ordinal) == true)
                    offenders.Add($"{relative}: using {directive.Name}");
            }

            // An "Effects" identifier that BEGINS a qualified reference — the
            // Effects.X shape. `effectsNode.Effects.Count` is preceded by a dot
            // and is a member on an AST node, not the namespace.
            foreach (var token in root.DescendantTokens())
            {
                if (!token.IsKind(SyntaxKind.IdentifierToken)
                    || token.ValueText != "Effects")
                {
                    continue;
                }
                var previous = token.GetPreviousToken();
                var next = token.GetNextToken();
                if (previous.IsKind(SyntaxKind.DotToken))
                    continue;
                if (!next.IsKind(SyntaxKind.DotToken))
                    continue;
                var line = token.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                offenders.Add($"{relative}:{line}: Effects.{next.GetNextToken().ValueText}");
            }
        }

        Assert.True(filesScanned >= 5, $"anti-vacuity: only {filesScanned} Binding/ files scanned");
        Assert.Equal(Array.Empty<string>(), offenders.Order().ToArray());
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
            ["Refactoring/ProjectSymbolIndex.cs:Populate"] =
                ("Universal ChildNodes traversal; the switch selects which nodes carry a " +
                    "renameable identifier, and the default arm states that a new " +
                    "identifier-bearing node would go unindexed. Rename correctness is " +
                    "established by the apply-recompile-and-run oracle, not by this switch.",
                    "No occurrence for this node kind"),
            ["Analysis/Dataflow/BoundNodeHelpers.cs:IsLiteralZero"] =
                ("Literal classifier, not traversal; non-literals explicitly return false.",
                    "_ => false"),
            ["Analysis/Security/TaintAnalysis.cs:GetExpressionName"] =
                ("Display-only classifier; unsupported expressions explicitly return null.",
                    "_ => null"),
            ["Analysis/Security/TaintAnalysis.cs:EvaluateExpression"] =
                ("Taint transfer dispatches exact call and access-path semantics; the default " +
                    "arm universally traverses expression children.",
                    "default:"),
            ["Analysis/Security/TaintAnalysis.cs:TryGetAccessPath"] =
                ("Access-path extractor; unsupported expressions explicitly report no trackable path.",
                    "return false"),
            ["Analysis/Security/TaintAnalysis.cs:GetResolvedCallees"] =
                ("Universal DescendantsAndSelf traversal; the switch selects the two node kinds " +
                    "that carry resolved function symbols, and unmatched nodes contribute no callees.",
                    "Array.Empty<FunctionSymbol>()"),
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

    private static ModuleNode EmptyModule(string name) =>
        new(
            Calor.Compiler.Parsing.TextSpan.Empty,
            $"m-{name}",
            name,
            Array.Empty<UsingDirectiveNode>(),
            Array.Empty<FunctionNode>(),
            new AttributeCollection());

    private static ModuleNode ParseModule(string source)
    {
        var diagnostics = new Calor.Compiler.Diagnostics.DiagnosticBag();
        var tokens = new Lexer(source, diagnostics).TokenizeAllForParser();
        Assert.Empty(diagnostics.Errors);
        var module = new Parser(tokens, diagnostics).Parse();
        Assert.Empty(diagnostics.Errors);
        return module;
    }

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

    /// <summary>
    /// v0.15 E1 slice 2c — roadmap §4.2 E1 **exit pin (c)**: "a structural pin
    /// that the string path is deleted, not bypassed — no
    /// <c>EffectResolver.Resolve(string, string, …)</c> overload remains".
    ///
    /// <para>Reflection, not grep, because the thing being pinned is the public
    /// SHAPE of the type: an overload can be re-added in any file, spelled any
    /// way, and this still catches it. Every public resolution entry point on
    /// <see cref="Calor.Compiler.Effects.EffectResolver"/> must take its
    /// subject as an <see cref="Calor.Compiler.Effects.EffectResolverKey"/>;
    /// a <c>string</c> parameter on any of them means a caller can once again
    /// name an external member by text and skip
    /// <see cref="Calor.Compiler.Effects.EffectResolverKey.FromStrings"/>,
    /// which is what makes the key ledger's bound-vs-string split
    /// meaningful.</para>
    ///
    /// <para>Deleting the check and re-adding
    /// <c>Resolve(string, string, params string[])</c> is the discriminating
    /// experiment: this test goes red, and nothing else does.</para>
    ///
    /// <para><b>Review round 1 (MAJOR 4) — the filter is on the RETURN TYPE,
    /// not the name.</b> The first version of this pin selected methods whose
    /// name started with <c>"Resolve"</c>, which meant a freshly added
    /// <c>public EffectResolution TryResolve(string, string, params string[])</c>
    /// walked straight past it: the string path would be back under a different
    /// verb and the pin would still be green. What the roadmap is actually
    /// asking is "can a caller obtain an <c>EffectResolution</c> by naming a
    /// member in text?", so the predicate is now exactly that — any public
    /// member returning an <c>EffectResolution</c> (or a nullable one) that
    /// accepts a <c>string</c>. Renaming the method no longer helps.</para>
    /// </summary>
    [Fact]
    public void EffectResolver_ExposesNoStringTypeNameResolveOverload()
    {
        var resolverType = typeof(Calor.Compiler.Effects.EffectResolver);
        var resolution = typeof(Calor.Compiler.Effects.EffectResolution);

        var offenders = resolverType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            // Any verb, not just "Resolve": what matters is that the member
            // HANDS BACK a resolution, since that is the capability exit pin (c)
            // says must not be reachable from a string.
            .Where(method => method.ReturnType == resolution
                || Nullable.GetUnderlyingType(method.ReturnType) == resolution)
            .Where(method => method.GetParameters().Any(p =>
                p.ParameterType == typeof(string)
                // Any string sequence, under any collection shape: string[],
                // List<string>, IReadOnlyList<string>, IEnumerable<string>, ...
                || typeof(System.Collections.Generic.IEnumerable<string>).IsAssignableFrom(p.ParameterType)
            .Select(method =>
                $"{method.Name}({string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name))})")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "roadmap §4.2 E1 exit pin (c): no public member of EffectResolver may return an "
            + "EffectResolution while accepting a string — under ANY name. Callers holding only "
            + "text build a key through EffectResolverKey.FromStrings, which marks "
            + "FromStringFallback so the key ledger can count it. Offending members: "
            + string.Join(", ", offenders));

        // Positive half: the keyed entry point actually exists, so the pin
        // cannot be satisfied by deleting resolution altogether.
        var keyed = resolverType.GetMethod(
            "Resolve",
            BindingFlags.Public | BindingFlags.Instance,
            [typeof(Calor.Compiler.Effects.EffectResolverKey)]);
        Assert.NotNull(keyed);
    }
}
