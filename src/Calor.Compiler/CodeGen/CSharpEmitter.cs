using System.Text;
using Calor.Compiler.Analysis.Dataflow;
using Calor.Compiler.Ast;
using Calor.Compiler.Migration;
using Calor.Compiler.Verification;
using Calor.Compiler.Verification.Z3;

// Import ContractMode from Program.cs
using ContractMode = Calor.Compiler.ContractMode;

namespace Calor.Compiler.CodeGen;

/// <summary>
/// Contract enforcement mode for code generation.
/// </summary>
public enum EmitContractMode
{
    /// <summary>
    /// No contract checks emitted.
    /// </summary>
    Off,

    /// <summary>
    /// Full contract checks with detailed messages.
    /// </summary>
    Debug,

    /// <summary>
    /// Lean contract checks with minimal messages.
    /// </summary>
    Release
}

/// <summary>
/// Emits C# source code from an Calor AST.
/// </summary>
public sealed class CSharpEmitter : IAstVisitor<string>
{
    private sealed class EmissionContext
    {
        public EmissionContext(int indentLevel = 0)
        {
            IndentLevel = indentLevel;
        }

        public StringBuilder Writer { get; } = new();
        public int IndentLevel { get; set; }
    }

    private EmissionContext _emissionContext = new();
    private HashSet<string> _reservedGeneratedIdentifiers = new(StringComparer.Ordinal);
    private string? _currentClassName;
    private string _currentModuleName = "";
    private HashSet<string> _currentModuleFunctionNames = new(StringComparer.Ordinal);
    private HashSet<string> _currentClassMemberNames = new(StringComparer.Ordinal);
    private readonly Stack<(HashSet<string> Members, bool Suppress)> _classMemberScopes = new();
    private bool _suppressCrossModuleQualification;

    /// <summary>
    /// Bare public function name → defining module name, for qualifying
    /// cross-module calls at emission (G3/#809). Without qualification a
    /// bare-name call into another module emits as-is and fails csc with
    /// CS0103 (each module emits into its own namespace/static class). Only
    /// unambiguous names appear here; ambiguous bare names stay bare (and are
    /// likewise skipped by cross-module effect resolution). Null for
    /// single-module compiles.
    /// </summary>
    public IReadOnlyDictionary<string, string>? CrossModuleFunctionModules { get; set; }
    private string? _currentFunctionId;
    private string? _currentFilePath;
    private string _currentNamespace = "";
    private string? _currentInlineReturnRefinement;
    private string? _currentYieldRefinement;
    private int _inlineReturnGuardCounter;
    private ReturnLoweringContext? _currentReturnLowering;
    private string? _postconditionResultIdentifier;
    private int _postconditionResultShadowDepth;
    private int _returnLoweringCounter;
    private readonly EmitContractMode _contractMode;
    private readonly ModuleVerificationResult? _verificationResults;
    private readonly ModuleInheritanceResult? _inheritanceResult;
    private readonly Verification.Obligations.ObligationTracker? _obligationTracker;
    private readonly Verification.Obligations.ObligationPolicy _obligationPolicy;
    private readonly Diagnostics.DiagnosticBag? _diagnostics;

    // Track current indices for contract emission
    private int _currentPostconditionIndex;

    // Track declared variables in current function scope for reassignment detection
    // Scope-aware declared-variable tracking (#732): a stack of block scopes, reset to
    // a single base scope at each function/method/accessor/ctor. A mutable §B rebind
    // emits a reassignment (`x = …`) only when the name is visible in a live (enclosing)
    // scope; a rebind whose earlier declaration lives in a now-closed sibling block
    // re-declares (`var x = …`) instead — matching C#, which forbids reassigning an
    // out-of-scope local (CS0103). Push on entering a control-flow block, pop on leaving.
    // Starts with a base scope so isolated statement/expression emission (tests, or any
    // path not entered through a function/method ResetDeclScopes) always has a live scope.
    private readonly List<HashSet<string>> _declScopes = new() { new(StringComparer.Ordinal) };
    private sealed record RefinementConstraint(
        ExpressionNode Predicate,
        string Description,
        string TypeName);
    private sealed record CallableReturnShape(
        string DeclarationType,
        string? ValueType)
    {
        public bool HasValue => ValueType != null;
    }
    private sealed record PostconditionEmission(
        EnsuresNode Contract,
        string? InheritedFrom = null);
    private sealed record ReturnLoweringContext(
        string ExitLabel,
        string? ResultIdentifier);
    private readonly List<Dictionary<string, RefinementConstraint?>> _refinementDeclScopes =
        new() { new(StringComparer.Ordinal) };
    private readonly List<Dictionary<string, string?>> _indexedBoundScopes =
        new() { new(StringComparer.Ordinal) };
    private readonly HashSet<string> _outParameterNames = new(StringComparer.Ordinal);
    private int _indexGuardCounter;
    private int _mutationGuardCounter;

    private void ResetDeclScopes(IReadOnlyList<ParameterNode>? parameters = null)
    {
        _declScopes.Clear();
        _declScopes.Add(new HashSet<string>(StringComparer.Ordinal));
        _refinementDeclScopes.Clear();
        _refinementDeclScopes.Add(
            new Dictionary<string, RefinementConstraint?>(StringComparer.Ordinal));
        _indexedBoundScopes.Clear();
        _indexedBoundScopes.Add(new Dictionary<string, string?>(StringComparer.Ordinal));
        _outParameterNames.Clear();
        _indexGuardCounter = 0;
        _mutationGuardCounter = 0;
        if (parameters != null)
        {
            // Parameters live in the base scope so a mutable §B{~param} rebind is a
            // reassignment (`param = …`, valid), not a re-declaration that shadows the
            // parameter (CS0136) — matching BindValidationPass, which seeds parameters
            // into its base scope too (#732).
            foreach (var p in parameters)
            {
                var name = SanitizeIdentifier(p.Name);
                _declScopes[0].Add(name);
                if (p.Modifier.HasFlag(ParameterModifier.Out))
                {
                    _outParameterNames.Add(name);
                }
                var hasNamedRefinement =
                    _refinementTypes.TryGetValue(p.TypeName, out var refinementType);
                if (p.InlineRefinement?.Predicate is { } inlinePredicate
                    && hasNamedRefinement)
                {
                    _refinementDeclScopes[0][name] = new RefinementConstraint(
                        new BinaryOperationNode(
                            p.Span,
                            BinaryOperator.And,
                            GetEffectiveRefinementPredicate(refinementType!),
                            inlinePredicate),
                        $"refinement type '{refinementType!.Name}' and inline refinement for parameter '{p.Name}'",
                        ResolveRefinementBaseType(refinementType));
                }
                else if (p.InlineRefinement?.Predicate is { } inlineOnlyPredicate)
                {
                    _refinementDeclScopes[0][name] = new RefinementConstraint(
                        inlineOnlyPredicate,
                        $"inline refinement for parameter '{p.Name}'",
                        p.TypeName);
                }
                else if (hasNamedRefinement)
                {
                    _refinementDeclScopes[0][name] = new RefinementConstraint(
                        GetEffectiveRefinementPredicate(refinementType!),
                        $"refinement type '{refinementType!.Name}'",
                        ResolveRefinementBaseType(refinementType));
                }

                var indexedTypeName = p.TypeName;
                var genericIndex = indexedTypeName.IndexOf('<');
                if (genericIndex > 0)
                    indexedTypeName = indexedTypeName[..genericIndex];
                if (_indexedTypes.TryGetValue(indexedTypeName, out var indexedType))
                {
                    var hasRuntimeWitness = parameters.Any(candidate =>
                        string.Equals(
                            SanitizeIdentifier(candidate.Name),
                            SanitizeIdentifier(indexedType.SizeParam),
                            StringComparison.Ordinal));
                    _indexedBoundScopes[0][name] = hasRuntimeWitness
                        ? indexedType.SizeParam
                        : $"missing:{indexedType.SizeParam}";
                }
            }
        }
    }

    private void PushDeclScope()
    {
        _declScopes.Add(new HashSet<string>(StringComparer.Ordinal));
        _refinementDeclScopes.Add(
            new Dictionary<string, RefinementConstraint?>(StringComparer.Ordinal));
        _indexedBoundScopes.Add(new Dictionary<string, string?>(StringComparer.Ordinal));
    }

    private void PopDeclScope()
    {
        // Popping should never reach the base scope — that means an unbalanced
        // Push/Pop site. Assert in Debug/tests; guard in Release so a stray imbalance
        // can't crash real codegen.
        System.Diagnostics.Debug.Assert(_declScopes.Count > 1, "unbalanced PushDeclScope/PopDeclScope");
        if (_declScopes.Count > 1)
        {
            _declScopes.RemoveAt(_declScopes.Count - 1);
            _refinementDeclScopes.RemoveAt(_refinementDeclScopes.Count - 1);
            _indexedBoundScopes.RemoveAt(_indexedBoundScopes.Count - 1);
        }
    }

    private bool IsVarDeclaredInScope(string name)
    {
        var sanitizedName = SanitizeIdentifier(name);
        for (var i = _declScopes.Count - 1; i >= 0; i--)
        {
            if (_declScopes[i].Contains(name) ||
                _declScopes[i].Contains(sanitizedName))
            {
                return true;
            }
        }

        return false;
    }

    private void DeclareVarInScope(string name)
    {
        System.Diagnostics.Debug.Assert(_declScopes.Count > 0, "DeclareVarInScope with no active scope");
        if (_declScopes.Count == 0)
        {
            ResetDeclScopes();
        }

        _declScopes[^1].Add(name);
    }

    private void DeclareRefinementInScope(string name, RefinementConstraint? constraint)
    {
        System.Diagnostics.Debug.Assert(
            _refinementDeclScopes.Count > 0,
            "DeclareRefinementInScope with no active scope");
        _refinementDeclScopes[^1][SanitizeIdentifier(name)] = constraint;
    }

    private void SetRefinementForExistingVariable(
        string name,
        RefinementConstraint? constraint)
    {
        var sanitizedName = SanitizeIdentifier(name);
        for (var i = _refinementDeclScopes.Count - 1; i >= 0; i--)
        {
            if (_declScopes[i].Contains(sanitizedName))
            {
                _refinementDeclScopes[i][sanitizedName] = constraint;
                return;
            }
        }

        DeclareRefinementInScope(sanitizedName, constraint);
    }

    private bool TryGetRefinementConstraint(
        string name,
        out RefinementConstraint constraint)
    {
        var sanitizedName = SanitizeIdentifier(name);
        for (var i = _refinementDeclScopes.Count - 1; i >= 0; i--)
        {
            if (_refinementDeclScopes[i].TryGetValue(sanitizedName, out var candidate))
            {
                constraint = candidate!;
                return candidate is not null;
            }
        }

        constraint = null!;
        return false;
    }

    private void DeclareIndexedBoundInScope(string name, string? sizeParameter)
        => _indexedBoundScopes[^1][SanitizeIdentifier(name)] = sizeParameter;

    private void SetIndexedBoundForExistingVariable(
        string name,
        string? sizeParameter)
    {
        var sanitizedName = SanitizeIdentifier(name);
        for (var i = _indexedBoundScopes.Count - 1; i >= 0; i--)
        {
            if (_declScopes[i].Contains(sanitizedName))
            {
                _indexedBoundScopes[i][sanitizedName] = sizeParameter;
                return;
            }
        }

        DeclareIndexedBoundInScope(sanitizedName, sizeParameter);
    }

    private bool TryGetIndexedBound(string name, out string sizeParameter)
    {
        var sanitizedName = SanitizeIdentifier(name);
        for (var i = _indexedBoundScopes.Count - 1; i >= 0; i--)
        {
            if (_indexedBoundScopes[i].TryGetValue(sanitizedName, out var candidate))
            {
                sizeParameter = candidate!;
                return candidate is not null;
            }
        }

        sizeParameter = "";
        return false;
    }

    private string? GetIndexedBoundForBinding(BindStatementNode node)
    {
        if (node.TypeName != null)
        {
            var typeName = node.TypeName;
            var genericIndex = typeName.IndexOf('<');
            if (genericIndex > 0)
                typeName = typeName[..genericIndex];
            if (_indexedTypes.TryGetValue(typeName, out var indexedType))
            {
                return IsVarDeclaredInScope(indexedType.SizeParam)
                    ? indexedType.SizeParam
                    : $"missing:{indexedType.SizeParam}";
            }
        }

        return node.Initializer is ReferenceNode reference
            && TryGetIndexedBound(reference.Name, out var aliasedBound)
            ? aliasedBound
            : null;
    }

    private static bool IsMissingIndexedWitness(
        string bound,
        out string witnessName)
    {
        const string prefix = "missing:";
        if (bound.StartsWith(prefix, StringComparison.Ordinal))
        {
            witnessName = bound[prefix.Length..];
            return true;
        }

        witnessName = "";
        return false;
    }


    // AST-level namespace usage tracking for conditional using emission
    private bool _usesCalorRuntime;
    private bool _usesSystemLinq;
    private bool _usesCollectionsGeneric;

    // Indexed type name → base type for erasure (populated during Visit(ModuleNode))
    private readonly Dictionary<string, string> _indexedTypeErasure = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IndexedTypeNode> _indexedTypes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RefinementTypeNode> _refinementTypes =
        new(StringComparer.Ordinal);

    public CSharpEmitter() : this(EmitContractMode.Debug)
    {
    }

    public CSharpEmitter(ContractMode contractMode) : this(contractMode, null, null)
    {
    }

    public CSharpEmitter(ContractMode contractMode, ModuleVerificationResult? verificationResults)
        : this(contractMode, verificationResults, null)
    {
    }

    /// <summary>
    /// Opt-in to deleting runtime guards on Proven/Discharged verdicts (roadmap v0.13
    /// §2.1). Default false: verification verdicts are diagnostic; every guard stays.
    /// </summary>
    public bool ElideProvenGuards { get; set; }

    private bool ShouldEmitObligationGuard(
        Verification.Obligations.ObligationKind kind,
        Parsing.TextSpan? span = null,
        string? parameterName = null)
    {
        if (_contractMode == EmitContractMode.Off)
            return true;
        if (_obligationTracker is null || _currentFunctionId is null)
            return true;

        var matching = _obligationTracker.Obligations.Where(obligation =>
                obligation.FunctionId == _currentFunctionId
                && obligation.Kind == kind
                && (parameterName is null
                    || string.Equals(
                        obligation.ParameterName,
                        parameterName,
                        StringComparison.Ordinal))
                && (span is null || obligation.Span.Start == span.Value.Start))
            .ToArray();
        if (matching.Length == 0)
            return true;

        return matching.Any(obligation =>
        {
            var action = _obligationPolicy.GetAction(obligation.Status);
            return Verification.Obligations.ObligationPolicy.RequiresGuard(action)
                || obligation.Status
                    != Verification.Obligations.ObligationStatus.Discharged
                || !ElideProvenGuards;
        });
    }

    public CSharpEmitter(ContractMode contractMode, ModuleVerificationResult? verificationResults, ModuleInheritanceResult? inheritanceResult,
        Verification.Obligations.ObligationTracker? obligationTracker = null,
        Diagnostics.DiagnosticBag? diagnostics = null,
        Verification.Obligations.ObligationPolicy? obligationPolicy = null)
    {
        _contractMode = contractMode switch
        {
            ContractMode.Off => EmitContractMode.Off,
            ContractMode.Release => EmitContractMode.Release,
            _ => EmitContractMode.Debug
        };
        _verificationResults = verificationResults;
        _inheritanceResult = inheritanceResult;
        _obligationTracker = obligationTracker;
        _diagnostics = diagnostics;
        _obligationPolicy = obligationPolicy ?? Verification.Obligations.ObligationPolicy.Default;
    }

    public CSharpEmitter(EmitContractMode contractMode)
    {
        _contractMode = contractMode;
        _obligationPolicy = Verification.Obligations.ObligationPolicy.Default;
    }

    /// <summary>
    /// When set, the emitter writes <c>#line</c> directives before each
    /// statement-level construct so Roslyn diagnostics, debugger sessions and
    /// stack traces map back to the original <c>.calr</c> source instead of the
    /// generated <c>.g.cs</c> file. Generated-only regions (headers, contract
    /// checks, closing braces) are reset with <c>#line default</c> so they
    /// attribute honestly to the generated file. Null (the default) disables
    /// source mapping entirely.
    /// </summary>
    public string? LineDirectiveFilePath { get; set; }

    // Escaped form of LineDirectiveFilePath, computed once per Emit call.
    private string? _lineDirectiveFile;

    public string Emit(ModuleNode module, string? filePath = null)
    {
        _emissionContext = new EmissionContext();
        _reservedGeneratedIdentifiers = CollectReservedModuleIdentifiers(module);
        _currentFilePath = filePath;
        _lineDirectiveFile = string.IsNullOrEmpty(LineDirectiveFilePath)
            ? null
            : EscapeString(LineDirectiveFilePath);

        var result = Visit(module);
        return result;
    }

    public string Emit(ModuleNode module)
    {
        return Emit(module, null);
    }

    private static IEnumerable<StatementNode> TraverseStatements(
        IReadOnlyList<StatementNode> body) =>
        Analysis.RecursiveAstWalker.EnumerateStatements(body);

    private static bool ContainsOpaqueCSharp(IReadOnlyList<StatementNode> body) =>
        TraverseStatements(body).Any(statement => statement is RawCSharpNode);

    private static IEnumerable<AstNode> DescendantsAndSelf(AstNode node)
    {
        yield return node;
        foreach (var child in Analysis.RecursiveAstWalker.GetAllChildren(node))
        {
            foreach (var descendant in DescendantsAndSelf(child))
            {
                yield return descendant;
            }
        }
    }

    private static HashSet<string> CollectReservedCallableIdentifiers(
        IReadOnlyList<StatementNode> body,
        IReadOnlyList<ParameterNode> parameters,
        IReadOnlyList<TypeParameterNode> typeParameters,
        IReadOnlyList<PostconditionEmission> postconditions)
    {
        var reserved = parameters
            .Select(parameter => SanitizeIdentifier(parameter.Name))
            .ToHashSet(StringComparer.Ordinal);
        reserved.UnionWith(typeParameters.Select(parameter =>
            SanitizeIdentifier(parameter.Name)));

        foreach (var node in body.SelectMany(DescendantsAndSelf))
        {
            AddReservedIdentifiers(reserved, node);
        }

        foreach (var node in postconditions
                     .SelectMany(postcondition =>
                         DescendantsAndSelf(
                             postcondition.Contract.Condition)))
        {
            AddReservedIdentifiers(reserved, node);
        }

        return reserved;
    }

    private static HashSet<string> CollectReservedModuleIdentifiers(ModuleNode module)
    {
        var reserved = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in DescendantsAndSelf(module))
        {
            AddReservedIdentifiers(reserved, node);
        }
        return reserved;
    }

    private static void AddReservedIdentifiers(
        HashSet<string> reserved,
        AstNode node)
    {
        switch (node)
        {
            case RawCSharpNode raw:
                AddRawCSharpIdentifiers(reserved, raw.CSharpCode);
                break;
            case RawCSharpExpressionNode raw:
                AddRawCSharpIdentifiers(reserved, raw.CSharpCode);
                break;
            case CSharpInteropBlockNode interop:
                AddRawCSharpIdentifiers(reserved, interop.CSharpCode);
                break;
        }

        var name = node switch
        {
            BindStatementNode bind => bind.Name,
            ForStatementNode loop => loop.VariableName,
            ForeachStatementNode loop => loop.VariableName,
            DictionaryForeachNode loop => loop.KeyName,
            UsingStatementNode usingStatement => usingStatement.VariableName,
            CatchClauseNode catchClause => catchClause.VariableName,
            FixedStatementNode fixedStatement => fixedStatement.PointerName,
            LabelStatementNode label => label.Label,
            LambdaParameterNode parameter => parameter.Name,
            ParameterNode parameter => parameter.Name,
            TypeParameterNode parameter => parameter.Name,
            QuantifierVariableNode variable => variable.Name,
            IsPatternNode pattern => pattern.VariableName,
            VariablePatternNode pattern => pattern.Name,
            VarPatternNode pattern => pattern.Name,
            TypePatternNode pattern => pattern.BindingName,
            ReferenceNode reference when !reference.Name.Contains('.') =>
                reference.Name,
            _ => null
        };
        if (!string.IsNullOrEmpty(name))
        {
            reserved.Add(SanitizeIdentifier(name));
        }

        if (node is ForeachStatementNode { IndexVariableName: { } indexName })
        {
            reserved.Add(SanitizeIdentifier(indexName));
        }
        else if (node is DictionaryForeachNode dictionaryLoop)
        {
            reserved.Add(SanitizeIdentifier(dictionaryLoop.ValueName));
        }
    }

    private static void AddRawCSharpIdentifiers(
        HashSet<string> reserved,
        string source)
    {
        var pending = new Stack<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        pending.Push(source);

        while (pending.Count > 0)
        {
            var fragment = pending.Pop();
            if (string.IsNullOrEmpty(fragment) || !visited.Add(fragment))
                continue;

            var root = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree
                .ParseText(fragment)
                .GetRoot();
            foreach (var token in root.DescendantTokens())
            {
                if (token.RawKind
                    != (int)Microsoft.CodeAnalysis.CSharp.SyntaxKind.IdentifierToken)
                {
                    continue;
                }

                var value = token.ValueText;
                if (Microsoft.CodeAnalysis.CSharp.SyntaxFacts.GetKeywordKind(value)
                        != Microsoft.CodeAnalysis.CSharp.SyntaxKind.None
                    || Microsoft.CodeAnalysis.CSharp.SyntaxFacts
                        .GetContextualKeywordKind(value)
                        != Microsoft.CodeAnalysis.CSharp.SyntaxKind.None)
                {
                    continue;
                }

                reserved.Add(SanitizeIdentifier(value));
            }

            foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: true))
            {
                if (trivia.RawKind
                        is (int)Microsoft.CodeAnalysis.CSharp.SyntaxKind.DisabledTextTrivia
                        or (int)Microsoft.CodeAnalysis.CSharp.SyntaxKind.SkippedTokensTrivia)
                {
                    pending.Push(trivia.ToFullString());
                }
            }
        }
    }

    private static string ReserveUniqueIdentifier(
        HashSet<string> reserved,
        string baseName)
    {
        var candidate = baseName;
        var suffix = 1;
        while (!reserved.Add(candidate))
        {
            candidate = $"{baseName}_{suffix++}";
        }
        return candidate;
    }

    private static bool PatternBindsName(
        PatternNode pattern,
        string name) =>
        DescendantsAndSelf(pattern).Any(node => node switch
        {
            VariablePatternNode variable =>
                variable.Name.Equals(name, StringComparison.Ordinal),
            VarPatternNode variable =>
                variable.Name.Equals(name, StringComparison.Ordinal),
            TypePatternNode { BindingName: { } bindingName } =>
                bindingName.Equals(name, StringComparison.Ordinal),
            _ => false
        });

    private static bool ExpressionBindsPatternName(
        ExpressionNode expression,
        string name,
        bool whenTruth = true)
    {
        var outcomes = AnalyzeBindingOutcomes(expression, name);
        return outcomes.Any(outcome => outcome.Truth == whenTruth)
            && outcomes
                .Where(outcome => outcome.Truth == whenTruth)
                .All(outcome => outcome.Bound);
    }

    private static IReadOnlySet<(bool Truth, bool Bound)> AnalyzeBindingOutcomes(
        ExpressionNode expression,
        string name)
    {
        if (expression is IsPatternNode pattern)
        {
            return pattern.VariableName?.Equals(
                    name,
                    StringComparison.Ordinal) == true
                ? new HashSet<(bool, bool)> { (true, true), (false, false) }
                : UnknownBindingOutcomes();
        }
        if (expression is BoolLiteralNode boolean)
        {
            return new HashSet<(bool, bool)> { (boolean.Value, false) };
        }
        if (expression is UnaryOperationNode
            {
                Operator: UnaryOperator.Not
            } unary)
        {
            return AnalyzeBindingOutcomes(unary.Operand, name)
                .Select(outcome => (!outcome.Truth, outcome.Bound))
                .ToHashSet();
        }
        if (expression is BinaryOperationNode binary
            && binary.Operator is BinaryOperator.And or BinaryOperator.Or)
        {
            return CombineBindingOutcomes(
                AnalyzeBindingOutcomes(binary.Left, name),
                AnalyzeBindingOutcomes(binary.Right, name),
                binary.Operator == BinaryOperator.And);
        }
        if (expression is ImplicationExpressionNode implication)
        {
            var negatedAntecedent = AnalyzeBindingOutcomes(
                    implication.Antecedent,
                    name)
                .Select(outcome => (!outcome.Truth, outcome.Bound))
                .ToHashSet();
            return CombineBindingOutcomes(
                negatedAntecedent,
                AnalyzeBindingOutcomes(implication.Consequent, name),
                isAnd: false);
        }
        if (expression is ConditionalExpressionNode conditional)
        {
            var outcomes = new HashSet<(bool, bool)>();
            foreach (var condition in AnalyzeBindingOutcomes(
                         conditional.Condition,
                         name))
            {
                var branch = condition.Truth
                    ? conditional.WhenTrue
                    : conditional.WhenFalse;
                foreach (var branchOutcome in AnalyzeBindingOutcomes(
                             branch,
                             name))
                {
                    outcomes.Add((
                        branchOutcome.Truth,
                        condition.Bound || branchOutcome.Bound));
                }
            }
            return outcomes;
        }
        return UnknownBindingOutcomes();
    }

    private static IReadOnlySet<(bool Truth, bool Bound)> CombineBindingOutcomes(
        IReadOnlySet<(bool Truth, bool Bound)> leftOutcomes,
        IReadOnlySet<(bool Truth, bool Bound)> rightOutcomes,
        bool isAnd)
    {
        var outcomes = new HashSet<(bool, bool)>();
        foreach (var left in leftOutcomes)
        {
            var shortCircuits = isAnd ? !left.Truth : left.Truth;
            if (shortCircuits)
            {
                outcomes.Add((left.Truth, left.Bound));
                continue;
            }
            foreach (var right in rightOutcomes)
            {
                outcomes.Add((
                    right.Truth,
                    left.Bound || right.Bound));
            }
        }
        return outcomes;
    }

    private static IReadOnlySet<(bool Truth, bool Bound)>
        UnknownBindingOutcomes() =>
        new HashSet<(bool, bool)> { (true, false), (false, false) };

    private void ReportUnsupportedPostconditionLowering(
        string declarationName,
        Parsing.TextSpan span,
        string reason,
        string diagnosticCode)
    {
        var message =
            $"Postconditions on '{declarationName}' are unsupported: {reason}";
        if (_diagnostics is null)
        {
            throw new InvalidOperationException(message);
        }

        _diagnostics.Add(new Diagnostics.Diagnostic(
            diagnosticCode,
            message,
            span,
            Diagnostics.DiagnosticSeverity.Error));
    }

    private CallableReturnShape GetCallableReturnShape(
        string returnType,
        bool isAsync,
        bool isIterator)
    {
        var mappedType = MapTypeName(returnType);
        if (isIterator)
        {
            return new CallableReturnShape(
                WrapInIEnumerable(mappedType),
                mappedType.Equals("void", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : mappedType);
        }

        if (!isAsync)
        {
            return new CallableReturnShape(
                mappedType,
                mappedType.Equals("void", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : mappedType);
        }

        if (TryUnwrapAsyncValueType(mappedType, out var asyncValueType))
        {
            return new CallableReturnShape(mappedType, asyncValueType);
        }

        if (mappedType is "Task" or "ValueTask")
        {
            return new CallableReturnShape(mappedType, null);
        }

        return mappedType.Equals("void", StringComparison.OrdinalIgnoreCase)
            ? new CallableReturnShape("Task", null)
            : new CallableReturnShape(WrapInTask(mappedType), mappedType);
    }

    private static bool TryUnwrapAsyncValueType(
        string mappedType,
        out string valueType)
    {
        var genericStart = mappedType.IndexOf('<');
        if (genericStart > 0
            && mappedType.EndsWith('>')
            && (mappedType[..genericStart].EndsWith(
                    "Task",
                    StringComparison.Ordinal)
                || mappedType[..genericStart].EndsWith(
                    "ValueTask",
                    StringComparison.Ordinal)))
        {
            valueType = mappedType[(genericStart + 1)..^1];
            return true;
        }

        valueType = "";
        return false;
    }

    private void EmitCallableBody(
        IReadOnlyList<StatementNode> body,
        IReadOnlyList<ParameterNode> parameters,
        IReadOnlyList<TypeParameterNode> typeParameters,
        IReadOnlyList<PostconditionEmission> postconditions,
        string declarationName,
        Parsing.TextSpan declarationSpan,
        CallableReturnShape returnShape,
        string returnRefinementType,
        bool isIterator)
    {
        var emitsPostconditions =
            postconditions.Count > 0
            && _contractMode != EmitContractMode.Off;
        if (emitsPostconditions && isIterator)
        {
            ReportUnsupportedPostconditionLowering(
                declarationName,
                declarationSpan,
                "iterator postcondition semantics are not defined; remove the postcondition or the yield statements",
                Diagnostics.DiagnosticCode.IteratorPostconditionUnsupported);
            foreach (var statement in body)
            {
                EmitStatement(statement, _emissionContext);
            }
            return;
        }

        if (emitsPostconditions && ContainsOpaqueCSharp(body))
        {
            ReportUnsupportedPostconditionLowering(
                declarationName,
                declarationSpan,
                "its body contains raw C# whose returns cannot be identified structurally",
                Diagnostics.DiagnosticCode.PostconditionCheckNotLowered);
            foreach (var statement in body)
            {
                EmitStatement(statement, _emissionContext);
            }
            return;
        }

        var hasReturnRefinement =
            _refinementTypes.ContainsKey(returnRefinementType);
        if (!emitsPostconditions)
        {
            if (!hasReturnRefinement || isIterator)
            {
                foreach (var statement in body)
                {
                    EmitStatement(statement, _emissionContext);
                }
                return;
            }
        }

        var loweringIndex = _returnLoweringCounter++;
        var reservedIdentifiers =
            CollectReservedCallableIdentifiers(
                body,
                parameters,
                typeParameters,
                postconditions);
        var exitLabel = ReserveUniqueIdentifier(
            reservedIdentifiers,
            $"__calorPostconditionExit{loweringIndex}");
        var resultIdentifier = returnShape.HasValue
            ? ReserveUniqueIdentifier(
                reservedIdentifiers,
                $"__calorPostconditionResult{loweringIndex}")
            : null;
        if (resultIdentifier != null)
        {
            AppendLine($"{returnShape.ValueType} {resultIdentifier};");
        }

        var previousLowering = _currentReturnLowering;
        _currentReturnLowering = new ReturnLoweringContext(
            exitLabel,
            resultIdentifier);
        try
        {
            foreach (var statement in body)
            {
                EmitStatement(statement, _emissionContext);
            }
        }
        finally
        {
            _currentReturnLowering = previousLowering;
        }

        AppendLine($"{exitLabel}:");
        EmitPostconditionChecks(postconditions, resultIdentifier);
        if (resultIdentifier != null)
        {
            EmitReturnRefinementGuard(
                returnRefinementType,
                resultIdentifier);
            AppendLine($"return {resultIdentifier};");
        }
        else
        {
            AppendLine("return;");
        }
    }

    private void EmitPostconditionChecks(
        IReadOnlyList<PostconditionEmission> postconditions,
        string? resultIdentifier)
    {
        var previousResultIdentifier = _postconditionResultIdentifier;
        var previousPostconditionIndex = _currentPostconditionIndex;
        _postconditionResultIdentifier = resultIdentifier;
        _currentPostconditionIndex = 0;
        try
        {
            foreach (var postcondition in postconditions)
            {
                if (postcondition.InheritedFrom != null)
                {
                    AppendLine(
                        $"// Inherited from {postcondition.InheritedFrom}");
                }

                var check = Visit(postcondition.Contract);
                if (!string.IsNullOrEmpty(check))
                {
                    AppendLine(check);
                }
            }
        }
        finally
        {
            _postconditionResultIdentifier = previousResultIdentifier;
            _currentPostconditionIndex = previousPostconditionIndex;
        }
    }

    private void ReportConstructorRefinementInitializerNotLowered(ConstructorNode node)
    {
        const string message =
            "A constructor has refinement entry guards and an explicit zero-argument initializer. C# executes the initializer before the constructor body and provides no initializer argument in which to enforce those guards. Add an initializer argument that can carry validation, remove the explicit initializer, or remove the parameter refinement.";
        if (_diagnostics is null)
        {
            throw new InvalidOperationException(
                $"Constructor '{node.Id}' cannot be emitted safely. {message}");
        }

        _diagnostics.Add(new Diagnostics.Diagnostic(
            Diagnostics.DiagnosticCode.ConstructorRefinementInitializerNotLowered,
            $"Constructor '{node.Id}' cannot be emitted safely. {message}",
            node.Initializer?.Span ?? node.Span,
            Diagnostics.DiagnosticSeverity.Error));
    }

    private void EmitStatement(
        StatementNode statement,
        EmissionContext context,
        bool skipEmptyLine = false)
    {
        var previousContext = _emissionContext;
        _emissionContext = context;
        try
        {
            var mapped = TryBeginLineMapping(statement);
            var isMutableRebind = statement is BindStatementNode bindStatement
                && bindStatement.IsMutable
                && IsVarDeclaredInScope(SanitizeIdentifier(bindStatement.Name));
            var code = statement.Accept(this);
            if (!skipEmptyLine || !string.IsNullOrEmpty(code))
            {
                AppendLine(code);
                if (statement is BindStatementNode bind
                    && !isMutableRebind
                    && TryGetRefinementConstraint(bind.Name, out var bindConstraint))
                {
                    if (ShouldEmitObligationGuard(
                        Verification.Obligations.ObligationKind.Subtype,
                        bind.Span,
                        bind.Name))
                    {
                        EmitRefinementValueGuard(
                            bindConstraint,
                            SanitizeIdentifier(bind.Name));
                    }
                }
            }
            EndLineMapping(mapped);
        }
        finally
        {
            _emissionContext = previousContext;
        }
    }

    /// <summary>
    /// Writes a <c>#line</c> directive mapping the next output line to the
    /// node's source location. Returns true if a directive was written (caller
    /// must then close the region with <see cref="EndLineMapping"/>).
    /// </summary>
    private bool TryBeginLineMapping(AstNode node)
    {
        if (_lineDirectiveFile == null || node.Span.Line <= 0)
        {
            return false;
        }

        AppendLine($"#line {node.Span.Line} \"{_lineDirectiveFile}\"");
        return true;
    }

    private void EndLineMapping(bool mapped)
    {
        if (mapped)
        {
            AppendLine("#line default");
        }
    }

    private void AppendLine(string line = "")
    {
        if (string.IsNullOrEmpty(line))
        {
            _emissionContext.Writer.AppendLine();
        }
        else
        {
            _emissionContext.Writer.Append(
                new string(' ', _emissionContext.IndentLevel * 4));
            _emissionContext.Writer.AppendLine(line);
        }
    }

    private void Indent() => _emissionContext.IndentLevel++;
    private void Dedent() => _emissionContext.IndentLevel--;

    public string Visit(ModuleNode node)
    {
        AppendLine("// <auto-generated>");
        AppendLine("// This code was generated by the Calor compiler.");
        AppendLine("// Do not modify this file directly.");
        AppendLine("// </auto-generated>");
        AppendLine();
        AppendLine("#nullable enable");
        AppendLine();

        // Placeholder for using directives — replaced after body emission
        const string usingPlaceholder = "/* __CALOR_USINGS_PLACEHOLDER__ */";
        AppendLine(usingPlaceholder);
        AppendLine();

        // Collect user-defined usings for dedup
        var userUsings = new List<string>();
        foreach (var usingDirective in node.Usings)
        {
            var usingCode = Visit(usingDirective);
            if (!string.IsNullOrEmpty(usingCode))
                userUsings.Add(usingDirective.Namespace);
        }

        // Register indexed type erasure mappings (name → base type)
        foreach (var itype in node.IndexedTypes)
        {
            _indexedTypeErasure[itype.Name] = itype.BaseTypeName;
            _indexedTypes[itype.Name] = itype;
        }
        foreach (var refinementType in node.RefinementTypes)
        {
            _refinementTypes[refinementType.Name] = refinementType;
        }

        var isGlobalNamespace = node.Name == "_global" || string.IsNullOrEmpty(node.Name);
        var namespaceName = isGlobalNamespace ? "" : SanitizeNamespace(node.Name);
        _currentNamespace = namespaceName;
        _currentModuleName = node.Name;
        _currentModuleFunctionNames = node.Functions.Select(f => f.Name).ToHashSet(StringComparer.Ordinal);

        // Emit module-level extended metadata as file-level comments
        if (node.Context != null)
        {
            var contextComment = Visit(node.Context);
            foreach (var line in contextComment.Split('\n'))
            {
                AppendLine(line);
            }
            AppendLine();
        }
        foreach (var decision in node.Decisions)
        {
            var decisionComment = Visit(decision);
            foreach (var line in decisionComment.Split('\n'))
            {
                AppendLine(line);
            }
            AppendLine();
        }

        if (!isGlobalNamespace)
        {
            AppendLine($"namespace {namespaceName}");
            AppendLine("{");
            Indent();
        }

        // Emit module-level issues as comments
        foreach (var issue in node.Issues)
        {
            AppendLine(Visit(issue));
        }
        // Emit module-level assumptions
        foreach (var assume in node.Assumptions)
        {
            AppendLine(Visit(assume));
        }
        // Emit module-level invariants as comments
        foreach (var invariant in node.Invariants)
        {
            var invCode = Visit(invariant);
            AppendLine($"// INVARIANT: {invCode}");
        }
        if (node.Issues.Count > 0 || node.Assumptions.Count > 0 || node.Invariants.Count > 0)
        {
            AppendLine();
        }

        // Emit interfaces
        foreach (var iface in node.Interfaces)
        {
            Visit(iface);
            AppendLine();
        }

        // Emit enums
        foreach (var enumDef in node.Enums)
        {
            Visit(enumDef);
            AppendLine();
        }

        // Emit enum extensions
        foreach (var enumExt in node.EnumExtensions)
        {
            Visit(enumExt);
            AppendLine();
        }

        // Emit delegates
        foreach (var del in node.Delegates)
        {
            Visit(del);
            AppendLine();
        }

        // Emit classes
        foreach (var cls in node.Classes)
        {
            Visit(cls);
            AppendLine();
        }

        // Emit C# interop blocks
        foreach (var interop in node.InteropBlocks)
        {
            Visit(interop);
            AppendLine();
        }

        // Emit type-level preprocessor blocks
        foreach (var tpp in node.TypePreprocessorBlocks)
        {
            Visit(tpp);
            AppendLine();
        }

        // Emit module-level functions in a static class
        if (node.Functions.Count > 0)
        {
            var moduleClassName = isGlobalNamespace
                ? "GlobalModule"
                : SanitizeIdentifier(node.Name.Contains('.')
                    ? node.Name.Split('.').Last()
                    : node.Name) + "Module";
            AppendLine($"public static class {moduleClassName}");
            AppendLine("{");
            Indent();

            foreach (var function in node.Functions)
            {
                Visit(function);
                AppendLine();
            }

            Dedent();
            AppendLine("}");
        }

        if (!isGlobalNamespace)
        {
            Dedent();
            AppendLine("}");
        }

        // Replace using placeholder with only the namespaces actually referenced.
        // Uses AST-level flags set during emission, with string-scanning fallback
        // for raw C# passthrough (§CS{}, §RAW) that may reference these namespaces.
        var output = _emissionContext.Writer.ToString();
        var usingBlock = new StringBuilder();
        var emittedUsings = new HashSet<string>(StringComparer.Ordinal);

        // System is always needed
        usingBlock.AppendLine("using System;");
        emittedUsings.Add("System");

        // Calor.Runtime only if Option/Result/Contract types were emitted
        if (_usesCalorRuntime || output.Contains("Calor.Runtime."))
        {
            usingBlock.AppendLine("using Calor.Runtime;");
            emittedUsings.Add("Calor.Runtime");
        }

        // System.Collections.Generic if collection creation nodes were emitted
        if (_usesCollectionsGeneric || output.Contains("List<") || output.Contains("Dictionary<") || output.Contains("HashSet<"))
        {
            usingBlock.AppendLine("using System.Collections.Generic;");
            emittedUsings.Add("System.Collections.Generic");
        }

        // System.Linq only if quantifier nodes were emitted, or raw C# uses LINQ methods
        if (_usesSystemLinq || output.Contains("Enumerable.")
            || output.Contains(".Select(") || output.Contains(".Where(")
            || output.Contains(".Any(") || output.Contains(".All(")
            || output.Contains(".First(") || output.Contains(".OrderBy(")
            || output.Contains(".GroupBy(") || output.Contains(".ToList(")
            || output.Contains(".ToArray("))
        {
            usingBlock.AppendLine("using System.Linq;");
            emittedUsings.Add("System.Linq");
        }

        // Add user-defined usings
        foreach (var ns in userUsings)
        {
            if (!emittedUsings.Contains(ns))
            {
                usingBlock.AppendLine($"using {ns};");
                emittedUsings.Add(ns);
            }
        }

        return output.Replace(usingPlaceholder + Environment.NewLine, usingBlock.ToString())
                     .Replace(usingPlaceholder + "\n", usingBlock.ToString())
                     .Replace(usingPlaceholder, usingBlock.ToString());
    }

    public string Visit(UsingDirectiveNode node)
    {
        if (node.IsStatic)
        {
            return $"using static {node.Namespace};";
        }
        else if (node.Alias != null)
        {
            return $"using {node.Alias} = {node.Namespace};";
        }
        else
        {
            return $"using {node.Namespace};";
        }
    }

    public string Visit(FunctionNode node)
    {
        // Track current function ID for contract emission
        _currentFunctionId = node.Id;

        // Reset contract indices for this function
        _currentPostconditionIndex = 0;

        // Clear declared variables tracking for new function scope
        ResetDeclScopes(node.Parameters);

        // Emit extended metadata as documentation comments
        foreach (var issue in node.Issues)
        {
            AppendLine(Visit(issue));
        }
        if (node.Uses != null)
        {
            AppendLine(Visit(node.Uses));
        }
        if (node.UsedBy != null)
        {
            AppendLine(Visit(node.UsedBy));
        }
        foreach (var assume in node.Assumptions)
        {
            AppendLine(Visit(assume));
        }
        if (node.Complexity != null)
        {
            AppendLine(Visit(node.Complexity));
        }
        if (node.Since != null)
        {
            AppendLine(Visit(node.Since));
        }
        foreach (var breaking in node.BreakingChanges)
        {
            AppendLine(Visit(breaking));
        }
        foreach (var prop in node.Properties)
        {
            AppendLine(Visit(prop));
        }
        if (node.Lock != null)
        {
            AppendLine(Visit(node.Lock));
        }
        if (node.Author != null)
        {
            AppendLine(Visit(node.Author));
        }
        if (node.TaskRef != null)
        {
            AppendLine(Visit(node.TaskRef));
        }

        // Emit [Obsolete] attribute if deprecated
        if (node.Deprecated != null)
        {
            AppendLine(Visit(node.Deprecated));
        }

        var visibility = node.Visibility switch
        {
            Visibility.Public => "public",
            Visibility.ProtectedInternal => "protected internal",
            Visibility.Internal => "internal",
            Visibility.Protected => "protected",
            Visibility.Private => "private",
            _ => "private"
        };

        var returnType = node.Output?.TypeName ?? "void";
        var isIterator = ContainsYieldStatements(node.Body);
        var returnShape = GetCallableReturnShape(
            returnType,
            node.IsAsync,
            isIterator);
        var hasReturnRefinement = _refinementTypes.ContainsKey(returnType);
        var previousYieldRefinement = _currentYieldRefinement;
        _currentYieldRefinement = hasReturnRefinement && isIterator
            ? returnType
            : null;
        _inlineReturnGuardCounter = 0;

        var parameters = string.Join(", ", node.Parameters.Select(p => Visit(p)));

        var callableName = node.Name;
        var genericStart = callableName.LastIndexOf('<');
        var embeddedTypeParams = "";
        if (genericStart > 0 && callableName.EndsWith('>'))
        {
            embeddedTypeParams = callableName[genericStart..];
            callableName = callableName[..genericStart];
        }
        var methodName = SanitizeIdentifier(callableName);

        // Build type parameters if present
        var typeParams = "";
        var whereClause = "";
        if (node.TypeParameters.Count > 0)
        {
            typeParams = "<" + string.Join(", ", node.TypeParameters.Select(tp => tp.Accept(this))) + ">";

            // Build where clauses
            var whereClauses = new List<string>();
            foreach (var tp in node.TypeParameters)
            {
                if (tp.Constraints.Count > 0)
                {
                    var constraints = string.Join(", ", tp.Constraints.Select(c => EmitConstraint(c)));
                    whereClauses.Add($"where {tp.Name} : {constraints}");
                }
            }
            if (whereClauses.Count > 0)
            {
                whereClause = " " + string.Join(" ", whereClauses);
            }
        }
        else
        {
            typeParams = embeddedTypeParams;
        }

        // Check if this is the entry point
        var isMain = node.Name.Equals("Main", StringComparison.OrdinalIgnoreCase);
        var staticKeyword = "static "; // All module functions are static
        var asyncKeyword = node.IsAsync ? "async " : "";

        AppendLine($"{visibility} {staticKeyword}{asyncKeyword}{returnShape.DeclarationType} {methodName}{typeParams}({parameters}){whereClause}");
        AppendLine("{");
        Indent();

        // Emit inline examples as Debug.Assert
        foreach (var example in node.Examples)
        {
            AppendLine(Visit(example));
        }

        // Emit preconditions (REQUIRES)
        foreach (var requires in node.Preconditions)
        {
            var check = Visit(requires);
            AppendLine(check);
        }
        EmitRefinementParameterGuards(node.Parameters);

        EmitCallableBody(
            node.Body,
            node.Parameters,
            node.TypeParameters,
            node.Postconditions
                .Select(postcondition => new PostconditionEmission(postcondition))
                .ToArray(),
            node.Name,
            node.Span,
            returnShape,
            returnType,
            isIterator);

        Dedent();
        AppendLine("}");
        _currentYieldRefinement = previousYieldRefinement;

        return "";
    }

    public string Visit(OutputNode node) => MapTypeName(node.TypeName);

    public string Visit(EffectsNode node) => "";

    public string Visit(ParameterNode node)
    {
        var attrPrefix = "";
        if (node.CSharpAttributes.Count > 0)
        {
            var attrs = node.CSharpAttributes.Select(a =>
            {
                var args = a.Arguments.Count > 0 ? $"({string.Join(", ", a.Arguments)})" : "";
                return $"[{a.Name}{args}]";
            });
            attrPrefix = string.Join(" ", attrs) + " ";
        }
        var prefix = "";
        if (node.Modifier.HasFlag(ParameterModifier.This)) prefix += "this ";
        if (node.Modifier.HasFlag(ParameterModifier.Ref)) prefix += "ref ";
        if (node.Modifier.HasFlag(ParameterModifier.Out)) prefix += "out ";
        if (node.Modifier.HasFlag(ParameterModifier.In)) prefix += "in ";
        if (node.Modifier.HasFlag(ParameterModifier.Params)) prefix += "params ";
        var result = $"{attrPrefix}{prefix}{MapTypeName(node.TypeName)} {SanitizeIdentifier(node.Name)}";
        if (node.DefaultValue != null)
        {
            result += $" = {node.DefaultValue.Accept(this)}";
        }
        return result;
    }

    /// <summary>
    /// Qualifies a bare-name call target that resolves to another module's public
    /// function (G3/#809): `SaveSnapshot` → `global::Store.StoreModule.SaveSnapshot`,
    /// mirroring the module emission naming (namespace = sanitized module name,
    /// class = sanitized last segment + "Module"). Self-module names, dotted or
    /// generic targets, and names absent from the map pass through untouched.
    /// Skip order matters (#823 review C1/C2): locals and parameters in scope
    /// (the emitter tracks them in _declScopes), the enclosing class's own
    /// members, and the module's own functions all shadow other modules'
    /// names — qualifying past any of them silently runs the wrong code.
    /// </summary>
    private string QualifyCrossModuleTarget(string target)
    {
        if (CrossModuleFunctionModules != null && target.Contains('.'))
        {
            var separator = target.LastIndexOf('.');
            var module = target[..separator];
            var function = target[(separator + 1)..];
            if (CrossModuleFunctionModules.TryGetValue(target, out var explicitModule) &&
                explicitModule == module &&
                explicitModule != _currentModuleName)
            {
                var explicitNamespace = SanitizeNamespace(explicitModule);
                var explicitClass = SanitizeIdentifier(explicitModule.Split('.').Last()) + "Module";
                return $"global::{explicitNamespace}.{explicitClass}.{function}";
            }
        }

        if (CrossModuleFunctionModules == null
            || _suppressCrossModuleQualification
            || target.Length == 0
            || target.Contains('.')
            || target.Contains('<')
            || IsVarDeclaredInScope(target)
            || _currentClassMemberNames.Contains(target)
            // ENCLOSING classes' members are bare-visible from nested types too
            // (#823 re-review NEW-1: a nested class calling an enclosing static
            // was mis-qualified to another module — silent wrong code). Union
            // over the scope stack errs toward under-qualification, the
            // accepted failure direction.
            || _classMemberScopes.Any(scope => scope.Members.Contains(target))
            || _currentModuleFunctionNames.Contains(target)
            || !CrossModuleFunctionModules.TryGetValue(target, out var calleeModule)
            || calleeModule == _currentModuleName)
        {
            return target;
        }

        var calleeNamespace = SanitizeNamespace(calleeModule);
        var calleeClass = SanitizeIdentifier(calleeModule.Contains('.')
            ? calleeModule.Split('.').Last()
            : calleeModule) + "Module";
        return $"global::{calleeNamespace}.{calleeClass}.{target}";
    }

    public string Visit(CallStatementNode node)
    {
        var target = QualifyCrossModuleTarget(node.Target);
        if (node.TypeArguments is { Count: > 0 })
        {
            var typeArgs = string.Join(", ", node.TypeArguments.Select(MapTypeName));
            target += $"<{typeArgs}>";
        }
        var argStrings = new List<string>();
        for (int i = 0; i < node.Arguments.Count; i++)
        {
            var argStr = node.Arguments[i].Accept(this);
            if (node.ArgumentModifiers != null && i < node.ArgumentModifiers.Count && node.ArgumentModifiers[i] != null)
            {
                argStr = $"{node.ArgumentModifiers[i]} {argStr}";
            }
            if (node.ArgumentNames != null && i < node.ArgumentNames.Count && node.ArgumentNames[i] != null)
            {
                argStr = $"{node.ArgumentNames[i]}: {argStr}";
            }
            argStrings.Add(argStr);
        }
        var args = string.Join(", ", argStrings);

        return $"{target}({args});";
    }

    public string Visit(ReturnStatementNode node)
    {
        if (_currentReturnLowering is { } lowering)
        {
            if (node.Expression == null)
            {
                return $"goto {lowering.ExitLabel};";
            }

            var loweredExpression = node.Expression.Accept(this);
            if (lowering.ResultIdentifier == null)
            {
                return $"return {loweredExpression};";
            }

            var continuationIndent = Environment.NewLine
                + new string(' ', _emissionContext.IndentLevel * 4);
            return $"{lowering.ResultIdentifier} = {loweredExpression};"
                + continuationIndent
                + $"goto {lowering.ExitLabel};";
        }

        if (node.Expression == null)
        {
            return "return;";
        }

        var expr = node.Expression.Accept(this);
        if (_currentInlineReturnRefinement != null
            && _refinementTypes.TryGetValue(
                _currentInlineReturnRefinement,
                out var refinementType)
            && ShouldEmitObligationGuard(
                Verification.Obligations.ObligationKind.RefinementReturn))
        {
            var resultName = $"__refinedReturn{_inlineReturnGuardCounter++}";
            var condition = EmitRefinementCondition(
                GetEffectiveRefinementPredicate(refinementType),
                resultName);
            var continuationIndent = Environment.NewLine
                + new string(' ', _emissionContext.IndentLevel * 4);
            return $"var {resultName} = {expr};"
                + continuationIndent
                + $"if (!({condition})) throw new InvalidOperationException("
                + $"\"Return value violates refinement type '{refinementType.Name}'\");"
                + continuationIndent
                + $"return {resultName};";
        }

        return $"return {expr};";
    }

    public string Visit(IntLiteralNode node)
    {
        var sb = new System.Text.StringBuilder();

        if (node.IsUnsigned)
        {
            if (node.IsHex)
                sb.Append($"0x{node.UnsignedValue:X}");
            else
                sb.Append(node.UnsignedValue);

            // #774: defer to the carried 64-bit marker; fall back to magnitude only
            // when no explicit width was preserved. A `uint` (IsLong false) that fits
            // 32 bits stays `U`, never silently promoted to `UL`.
            if (node.IsLong || node.UnsignedValue > uint.MaxValue)
                sb.Append("UL");
            else
                sb.Append("U");
        }
        else
        {
            if (node.IsHex)
                sb.Append($"0x{node.Value:X}");
            else
                sb.Append(node.Value);

            // #774: an explicit `long` (IsLong) keeps its `L` even when the value
            // fits an int; otherwise the width is derived from magnitude as before.
            if (node.IsLong || node.Value is > int.MaxValue or < int.MinValue)
                sb.Append("L");
        }

        return sb.ToString();
    }

    public string Visit(StringLiteralNode node)
    {
        // Check if this is an interpolated string (contains ${expr})
        // Uses brace-depth matching to support complex expressions like ${arr[0]}, ${obj.Method()}, etc.
        // Content starting with a digit (e.g., ${0}) is treated as a format placeholder, not interpolation
        var (converted, hasInterpolation) = ConvertInlineInterpolation(node.Value);
        if (hasInterpolation)
        {
            // Escape for C# string literal (but not the interpolation braces)
            var escaped = converted
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");

            return $"$\"{escaped}\"";
        }

        // Multiline strings (from triple-quote """ ... """) emit as C# verbatim strings
        if (node.IsMultiline && node.Value.Contains('\n'))
        {
            var verbatimValue = node.Value.Replace("\"", "\"\"");
            var multilineSuffix = node.IsUtf8 ? "u8" : "";
            return $"@\"{verbatimValue}\"{multilineSuffix}";
        }

        // Escape the string for C#
        var escapedValue = node.Value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");

        var suffix = node.IsUtf8 ? "u8" : "";
        return $"\"{escapedValue}\"{suffix}";
    }

    /// <summary>
    /// Converts inline Calor interpolation syntax ${expr} to C# interpolation {expr}.
    /// Uses brace-depth tracking to support complex expressions (indexers, method calls, etc.).
    /// Content starting with a digit (e.g., ${0}) is treated as a format placeholder, not interpolation.
    /// Limitation: quoted strings containing braces inside interpolation (e.g., ${map["key"]})
    /// will miscount brace depth because the Lexer terminates the string at the inner quote.
    /// Supporting that would require Lexer-level interpolation parsing.
    /// </summary>
    internal static (string converted, bool hasInterpolation) ConvertInlineInterpolation(string value)
    {
        // Fast path: skip StringBuilder allocation if there's no interpolation marker
        if (!value.Contains("${"))
            return (value, false);

        var sb = new System.Text.StringBuilder(value.Length);
        bool foundInterpolation = false;
        int i = 0;

        while (i < value.Length)
        {
            if (i + 1 < value.Length && value[i] == '$' && value[i + 1] == '{')
            {
                // Found ${, now find matching } using brace-depth tracking
                int exprStart = i + 2;
                int depth = 1;
                int j = exprStart;

                while (j < value.Length && depth > 0)
                {
                    if (value[j] == '{')
                        depth++;
                    else if (value[j] == '}')
                        depth--;
                    if (depth > 0)
                        j++;
                }

                if (depth == 0)
                {
                    // Found matching } at position j
                    string expr = value.Substring(exprStart, j - exprStart);

                    // Check if this is a format placeholder (starts with digit)
                    if (expr.Length > 0 && char.IsDigit(expr[0]))
                    {
                        // Keep as literal text
                        sb.Append("${");
                        sb.Append(expr);
                        sb.Append('}');
                    }
                    else
                    {
                        // Convert to C# interpolation, converting prefix notation to infix
                        sb.Append('{');
                        sb.Append(ConvertPrefixToInfix(expr));
                        sb.Append('}');
                        foundInterpolation = true;
                    }

                    i = j + 1; // Skip past the closing }
                }
                else
                {
                    // Unmatched ${ — no closing }, treat as literal text
                    sb.Append(value[i]);
                    i++;
                }
            }
            else
            {
                sb.Append(value[i]);
                i++;
            }
        }

        return (sb.ToString(), foundInterpolation);
    }

    /// <summary>
    /// Returns the C# operator precedence level (higher = tighter binding).
    /// </summary>
    internal static int GetPrecedence(string op) => op switch
    {
        "*" or "/" or "%" => 13,
        "+" or "-" => 12,
        "<<" or ">>" => 11,
        "<" or ">" or "<=" or ">=" => 10,
        "==" or "!=" => 9,
        "&" => 8,
        "^" => 7,
        "|" => 6,
        "&&" => 5,
        "||" => 4,
        "??" => 3,
        _ => 0
    };

    /// <summary>
    /// Converts Calor prefix notation in interpolation expressions to C# infix.
    /// Handles (op left right) → left op right, with nesting support and correct parenthesization.
    /// </summary>
    internal static string ConvertPrefixToInfix(string expr)
    {
        expr = expr.Trim();

        // Must start with ( and end with )
        if (expr.Length < 5 || expr[0] != '(' || expr[^1] != ')')
            return expr;

        var inner = expr[1..^1].Trim();

        // Extract operator (first token)
        var spaceIdx = inner.IndexOf(' ');
        if (spaceIdx <= 0)
            return expr;

        var op = inner[..spaceIdx];
        var rest = inner[(spaceIdx + 1)..].Trim();

        // Map Calor operators to C# operators
        var csharpOp = op switch
        {
            "+" or "-" or "*" or "/" or "%" => op,
            "==" or "!=" or "<" or ">" or "<=" or ">=" => op,
            "&&" or "||" or "&" or "|" or "^" => op,
            "<<" or ">>" => op,
            "??" => op,
            "!" => op, // unary
            "~" => op, // unary bitwise not
            _ => null
        };

        if (csharpOp == null)
            return expr; // Not a known operator, pass through

        // Unary operators: wrap operand in parens if it's a compound expression
        if (csharpOp is "!" or "~")
        {
            var operand = ConvertPrefixToInfix(rest);
            if (operand.Contains(' '))
                operand = $"({operand})";
            return $"{csharpOp}{operand}";
        }

        // Split into left and right operands (respecting nesting)
        var (left, right) = SplitOperands(rest);
        if (left == null || right == null)
            return expr; // Can't split, pass through

        var leftConverted = ConvertPrefixToInfix(left);
        var rightConverted = ConvertPrefixToInfix(right);

        var parentPrec = GetPrecedence(csharpOp);

        // Wrap left child if it was a prefix expression and has lower precedence
        if (left.StartsWith('(') && left.EndsWith(')'))
        {
            var leftPrec = GetChildPrecedence(left);
            if (leftPrec > 0 && leftPrec < parentPrec)
                leftConverted = $"({leftConverted})";
        }

        // Wrap right child if it was a prefix expression and has lower or equal precedence
        if (right.StartsWith('(') && right.EndsWith(')'))
        {
            var rightPrec = GetChildPrecedence(right);
            if (rightPrec > 0 && rightPrec < parentPrec)
                rightConverted = $"({rightConverted})";
        }

        return $"{leftConverted} {csharpOp} {rightConverted}";
    }

    /// <summary>
    /// Extracts the precedence of the top-level operator from a prefix expression.
    /// Returns 0 if not a recognized binary prefix expression.
    /// </summary>
    private static int GetChildPrecedence(string prefixExpr)
    {
        var trimmed = prefixExpr.Trim();
        if (trimmed.Length < 5 || trimmed[0] != '(' || trimmed[^1] != ')')
            return 0;
        var inner = trimmed[1..^1].Trim();
        var spaceIdx = inner.IndexOf(' ');
        if (spaceIdx <= 0)
            return 0;
        var op = inner[..spaceIdx];
        if (op is "!" or "~")
            return 0; // unary, not binary
        return GetPrecedence(op);
    }

    /// <summary>
    /// Splits a string into two operands, respecting parentheses nesting.
    /// </summary>
    private static (string? left, string? right) SplitOperands(string text)
    {
        text = text.Trim();
        int depth = 0;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (c == ' ' && depth == 0)
            {
                var left = text[..i].Trim();
                var right = text[(i + 1)..].Trim();
                if (left.Length > 0 && right.Length > 0)
                    return (left, right);
            }
        }

        return (null, null);
    }

    public string Visit(BoolLiteralNode node)
    {
        return node.Value ? "true" : "false";
    }

    public string Visit(ConditionalExpressionNode node)
    {
        var condition = node.Condition.Accept(this);
        var previousShadowDepth = _postconditionResultShadowDepth;
        string whenTrue;
        try
        {
            if (ExpressionBindsPatternName(node.Condition, "result"))
            {
                _postconditionResultShadowDepth++;
            }
            whenTrue = node.WhenTrue.Accept(this);
        }
        finally
        {
            _postconditionResultShadowDepth = previousShadowDepth;
        }
        string whenFalse;
        try
        {
            if (ExpressionBindsPatternName(
                    node.Condition,
                    "result",
                    whenTruth: false))
            {
                _postconditionResultShadowDepth++;
            }
            whenFalse = node.WhenFalse.Accept(this);
        }
        finally
        {
            _postconditionResultShadowDepth = previousShadowDepth;
        }
        return $"({condition} ? {whenTrue} : {whenFalse})";
    }

    public string Visit(FloatLiteralNode node)
    {
        // #774: a single-precision literal recovers its exact float value (double→
        // float→string is the shortest round-trippable form, e.g. 3.14f → "3.14")
        // and re-emits the `f` suffix — never the widened 17-digit double expansion.
        if (node.IsSingle)
        {
            var single = ((float)node.Value).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return single + "f";
        }

        var str = node.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        // Ensure float literals always contain a decimal point so they aren't
        // reinterpreted as integers in the generated C# code.
        if (!node.IsDecimal && !str.Contains('.') && !str.Contains('E') && !str.Contains('e'))
        {
            str += ".0";
        }
        return node.IsDecimal ? str + "m" : str;
    }

    public string Visit(DecimalLiteralNode node)
    {
        return node.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "m";
    }

    public string Visit(ReferenceNode node)
    {
        if (_postconditionResultIdentifier != null
            && _postconditionResultShadowDepth == 0
            && node.Name.Equals("result", StringComparison.Ordinal))
        {
            return _postconditionResultIdentifier;
        }

        // Handle C# keywords that are used as literals (not identifiers)
        if (node.Name is "null" or "true" or "false")
        {
            return node.Name;
        }
        if (node.Name == "default")
        {
            return IsVarDeclaredInScope(node.Name) ||
                _currentClassMemberNames.Contains(node.Name) ||
                _classMemberScopes.Any(scope => scope.Members.Contains(node.Name))
                ? SanitizeIdentifier(node.Name)
                : node.Name;
        }

        // Handle member access like "args.Length" - preserve the dot notation
        if (node.Name.Contains('.'))
        {
            var parts = node.Name.Split('.');
            var sanitizedParts = parts.Select(SanitizeIdentifier);
            return string.Join(".", sanitizedParts);
        }
        return SanitizeIdentifier(node.Name);
    }

    // Phase 2: Control Flow

    public string Visit(ForStatementNode node)
    {
        var varName = SanitizeIdentifier(node.VariableName);
        var from = node.From.Accept(this);
        var to = node.To.Accept(this);
        var hasExplicitStep = node.Step != null;
        var step = node.Step?.Accept(this);
        var fromTemp = ReserveUniqueIdentifier(
            _reservedGeneratedIdentifiers,
            "__calorForFrom");
        var toTemp = ReserveUniqueIdentifier(
            _reservedGeneratedIdentifiers,
            "__calorForTo");
        var stepTemp = hasExplicitStep
            ? ReserveUniqueIdentifier(
                _reservedGeneratedIdentifiers,
                "__calorForStep")
            : null;
        var ascendingTemp = hasExplicitStep
            ? ReserveUniqueIdentifier(
                _reservedGeneratedIdentifiers,
                "__calorForAscending")
            : null;
        var firstTemp = ReserveUniqueIdentifier(
            _reservedGeneratedIdentifiers,
            "__calorForFirst");

        AppendLine("{");
        Indent();
        AppendLine($"var {fromTemp} = {from};");
        AppendLine($"var {toTemp} = {to};");
        if (hasExplicitStep)
        {
            AppendLine($"var {stepTemp} = {step};");
            AppendLine(
                $"if ({stepTemp} == 0) throw new ArgumentOutOfRangeException(" +
                $"nameof({stepTemp}), \"Calor for-loop step must not be zero\");");
            AppendLine($"var {ascendingTemp} = {stepTemp} > 0;");
        }
        AppendLine($"var {firstTemp} = true;");
        AppendLine($"var {varName} = {fromTemp};");
        AppendLine("while (true)");
        AppendLine("{");
        Indent();

        AppendLine($"if (!{firstTemp})");
        AppendLine("{");
        Indent();
        AppendLine("try");
        AppendLine("{");
        Indent();
        AppendLine("checked");
        AppendLine("{");
        Indent();
        AppendLine(
            hasExplicitStep
                ? $"{varName} += {stepTemp};"
                : $"{varName}++;");
        Dedent();
        AppendLine("}");
        Dedent();
        AppendLine("}");
        AppendLine("catch (OverflowException)");
        AppendLine("{");
        Indent();
        AppendLine("break;");
        Dedent();
        AppendLine("}");
        Dedent();
        AppendLine("}");
        AppendLine("else");
        AppendLine("{");
        Indent();
        AppendLine($"{firstTemp} = false;");
        Dedent();
        AppendLine("}");
        AppendLine(hasExplicitStep
            ? $"if (!({ascendingTemp} ? {varName} <= {toTemp} : " +
                $"{varName} >= {toTemp})) break;"
            : $"if (!({varName} <= {toTemp})) break;");

        PushDeclScope();
        DeclareVarInScope(varName); // the loop variable is in scope for the body (#732)
        foreach (var stmt in node.Body)
        {
            EmitStatement(stmt, _emissionContext);
        }
        PopDeclScope();

        Dedent();
        AppendLine("}");
        Dedent();
        AppendLine("}");

        return "";
    }

    public string Visit(WhileStatementNode node)
    {
        var condition = node.Condition.Accept(this);

        AppendLine($"while ({condition})");
        AppendLine("{");
        Indent();

        PushDeclScope();
        foreach (var stmt in node.Body)
        {
            EmitStatement(stmt, _emissionContext);
        }
        PopDeclScope();

        Dedent();
        AppendLine("}");

        return "";
    }

    public string Visit(DoWhileStatementNode node)
    {
        var condition = node.Condition.Accept(this);

        AppendLine("do");
        AppendLine("{");
        Indent();

        PushDeclScope();
        foreach (var stmt in node.Body)
        {
            EmitStatement(stmt, _emissionContext);
        }
        PopDeclScope();

        Dedent();
        AppendLine($"}} while ({condition});");

        return "";
    }

    public string Visit(IfStatementNode node)
    {
        var condition = node.Condition.Accept(this);

        AppendLine($"if ({condition})");
        AppendLine("{");
        Indent();

        PushDeclScope();
        foreach (var stmt in node.ThenBody)
        {
            EmitStatement(stmt, _emissionContext);
        }
        PopDeclScope();

        Dedent();
        AppendLine("}");

        // Emit ELSEIF clauses
        foreach (var elseIf in node.ElseIfClauses)
        {
            var elseIfCondition = elseIf.Condition.Accept(this);
            AppendLine($"else if ({elseIfCondition})");
            AppendLine("{");
            Indent();

            PushDeclScope();
            foreach (var stmt in elseIf.Body)
            {
                EmitStatement(stmt, _emissionContext);
            }
            PopDeclScope();

            Dedent();
            AppendLine("}");
        }

        // Emit ELSE clause
        if (node.ElseBody != null)
        {
            AppendLine("else");
            AppendLine("{");
            Indent();

            PushDeclScope();
            foreach (var stmt in node.ElseBody)
            {
                EmitStatement(stmt, _emissionContext);
            }
            PopDeclScope();

            Dedent();
            AppendLine("}");
        }

        return "";
    }

    public string Visit(ElseIfClauseNode node)
    {
        var condition = node.Condition.Accept(this);
        AppendLine($"else if ({condition})");
        AppendLine("{");
        Indent();
        PushDeclScope();
        foreach (var stmt in node.Body)
        {
            EmitStatement(stmt, _emissionContext);
        }
        PopDeclScope();
        Dedent();
        AppendLine("}");
        return "";
    }

    public string Visit(BindStatementNode node)
    {
        // Discard-await (`§B{_} §AWAIT ...`, produced by the C# converter for a
        // bare `await X();` statement): emit a plain await statement. The old
        // `var _ = await X();` form is invalid C# (CS0815) when the awaited
        // task is non-generic, and the discard is a no-op for value-returning
        // awaits too (exposed by the #771 compile gate).
        if (node.Name == "_" && node.TypeName == null && node.Initializer is AwaitExpressionNode awaitInit)
        {
            return $"{awaitInit.Accept(this)};";
        }

        var varName = SanitizeIdentifier(node.Name);
        var typeName = node.TypeName != null ? MapTypeName(node.TypeName) : "var";

        // Only emit assignment (not declaration) if:
        // 1. The variable is marked as mutable (§B{~name:type})
        // 2. AND it was already declared in this scope
        // This preserves Calor's shadowing semantics (S5-S6): immutable binds in inner
        // scopes create new shadowing variables, while mutable binds reassign.
        if (node.IsMutable && IsVarDeclaredInScope(varName))
        {
            TryGetRefinementConstraint(node.Name, out var existingConstraint);
            var hasExistingIndexedBound = TryGetIndexedBound(node.Name, out _);
            RefinementConstraint? reboundConstraint = null;
            if (node.TypeName != null
                && _refinementTypes.TryGetValue(node.TypeName, out var reboundRefinement))
            {
                reboundConstraint = new RefinementConstraint(
                    GetEffectiveRefinementPredicate(reboundRefinement),
                    $"refinement type '{reboundRefinement.Name}'",
                    ResolveRefinementBaseType(reboundRefinement));
            }
            var reboundIndexedBound = GetIndexedBoundForBinding(node);
            // Emit the initializer against the old variable contract. It may
            // itself mutate the target, so installing the rebound metadata
            // first would validate nested writes against the wrong invariant.
            var initExpr = node.Initializer?.Accept(this);
            if (existingConstraint is null && reboundConstraint != null)
            {
                SetRefinementForExistingVariable(varName, reboundConstraint);
            }
            if (!hasExistingIndexedBound && reboundIndexedBound != null)
            {
                SetIndexedBoundForExistingVariable(varName, reboundIndexedBound);
            }

            // Mutable rebind - emit assignment only
            if (initExpr != null)
            {
                var constraints = new[] { existingConstraint, reboundConstraint }
                    .Where(constraint => constraint is not null)
                    .Cast<RefinementConstraint>()
                    .Distinct()
                    .ToArray();
                var changesEstablishedConstraint = existingConstraint != null
                    && reboundConstraint != null
                    && !ReferenceEquals(
                        existingConstraint.Predicate,
                        reboundConstraint.Predicate);
                if (constraints.Length > 0
                    && (changesEstablishedConstraint
                        || ShouldEmitObligationGuard(
                            Verification.Obligations.ObligationKind.Subtype,
                            node.Span,
                            node.Name)))
                {
                    return EmitCheckedRefinedAssignment(
                        varName,
                        initExpr,
                        constraints[0],
                        restoreTargetOnFailure:
                            !_outParameterNames.Contains(varName)
                            || ExpressionReferencesName(
                                node.Initializer!,
                                node.Name),
                        additionalConstraints: constraints.Skip(1).ToArray());
                }
                return $"{varName} = {initExpr};";
            }
            return "";
        }

        DeclareVarInScope(varName);
        if (node.TypeName != null
            && _refinementTypes.TryGetValue(node.TypeName, out var refinementType))
        {
            DeclareRefinementInScope(
                varName,
                new RefinementConstraint(
                    GetEffectiveRefinementPredicate(refinementType),
                    $"refinement type '{refinementType.Name}'",
                    ResolveRefinementBaseType(refinementType)));
        }
        else
        {
            DeclareRefinementInScope(varName, null);
        }
        DeclareIndexedBoundInScope(varName, GetIndexedBoundForBinding(node));

        if (node.Initializer != null)
        {
            var initExpr = node.Initializer.Accept(this);
            return $"{typeName} {varName} = {initExpr};";
        }

        // No initializer - need explicit type
        if (node.TypeName == null)
        {
            typeName = "int"; // Default to int
        }
        return $"{typeName} {varName} = default;";
    }

    private static bool ExpressionReferencesName(
        ExpressionNode expression,
        string name)
    {
        if (expression is ReferenceNode reference
            && string.Equals(reference.Name, name, StringComparison.Ordinal))
        {
            return true;
        }

        return Calor.Compiler.Analysis.RecursiveAstWalker
            .GetAllChildren(expression)
            .OfType<ReferenceNode>()
            .Any(reference =>
                string.Equals(reference.Name, name, StringComparison.Ordinal));
    }

    public string Visit(BinaryOperationNode node)
    {
        var left = node.Left.Accept(this);
        var previousShadowDepth = _postconditionResultShadowDepth;
        if (node.Operator is BinaryOperator.And or BinaryOperator.Or
            && ExpressionBindsPatternName(
                node.Left,
                "result",
                whenTruth: node.Operator == BinaryOperator.And))
        {
            _postconditionResultShadowDepth++;
        }

        string right;
        try
        {
            right = node.Right.Accept(this);
        }
        finally
        {
            _postconditionResultShadowDepth = previousShadowDepth;
        }

        // Special handling for Power operator (use Math.Pow)
        if (node.Operator == BinaryOperator.Power)
        {
            return $"Math.Pow({left}, {right})";
        }

        var op = node.Operator.ToCSharpOperator();
        var parentPrecedence = GetPrecedence(node.Operator);

        // Only wrap children when their precedence is lower than parent's
        if (node.Left is BinaryOperationNode leftBin && GetPrecedence(leftBin.Operator) < parentPrecedence)
            left = $"({left})";
        if (node.Right is BinaryOperationNode rightBin && GetPrecedence(rightBin.Operator) <= parentPrecedence)
            right = $"({right})";

        return $"{left} {op} {right}";
    }

    public string Visit(UnaryOperationNode node)
    {
        if (node.Operator is UnaryOperator.PreIncrement
                or UnaryOperator.PreDecrement
                or UnaryOperator.PostIncrement
                or UnaryOperator.PostDecrement
            && node.Operand is ReferenceNode refinedTarget
            && TryGetRefinementConstraint(
                refinedTarget.Name,
                out var refinementConstraint))
        {
            var valueName = SanitizeIdentifier(refinedTarget.Name);
            var operation = node.Operator is UnaryOperator.PostIncrement
                    or UnaryOperator.PostDecrement
                ? $"{valueName}{node.Operator.ToCSharpOperator()}"
                : $"{node.Operator.ToCSharpOperator()}{valueName}";
            if (!ShouldEmitObligationGuard(
                Verification.Obligations.ObligationKind.Subtype,
                node.Span,
                refinedTarget.Name))
            {
                return operation;
            }

            var mutationId = _mutationGuardCounter++;
            var candidateName = $"__mutationCandidate{mutationId}";
            var operationResultName = $"__mutationOperation{mutationId}";
            var condition = EmitRefinementCondition(
                refinementConstraint.Predicate,
                candidateName);
            var candidateOperation =
                node.Operator is UnaryOperator.PostIncrement or UnaryOperator.PostDecrement
                    ? $"{candidateName}{node.Operator.ToCSharpOperator()}"
                    : $"{node.Operator.ToCSharpOperator()}{candidateName}";
            var candidateCapture = $"{valueName} is var {candidateName}"
                + $" && ({candidateOperation}) is var {operationResultName}"
                + $" && ({condition})";
            if (node.Operator is UnaryOperator.PostIncrement or UnaryOperator.PostDecrement)
            {
                var originalName = $"__mutationOriginal{mutationId}";
                return $"({valueName} is var {originalName}"
                    + $" && {candidateCapture}"
                    + $" ? (({valueName} = {candidateName}), {originalName}).Item2"
                    + " : throw new ArgumentOutOfRangeException("
                    + $"nameof({valueName}), \"Value violates {refinementConstraint.Description}\"))";
            }

            return $"({candidateCapture}"
                + $" ? {valueName} = {candidateName}"
                + " : throw new ArgumentOutOfRangeException("
                + $"nameof({valueName}), \"Value violates {refinementConstraint.Description}\"))";
        }

        if (node.Operator is UnaryOperator.PreIncrement
                or UnaryOperator.PreDecrement
                or UnaryOperator.PostIncrement
                or UnaryOperator.PostDecrement
            && node.Operand is ArrayAccessNode indexedOperand
            && indexedOperand.Array is ReferenceNode reference
            && TryGetIndexedBound(reference.Name, out var logicalLength))
        {
            var array = indexedOperand.Array.Accept(this);
            var index = indexedOperand.Index.Accept(this);
            var indexedOp = node.Operator.ToCSharpOperator();
            string Apply(string guardedIndex) =>
                node.Operator is UnaryOperator.PostIncrement or UnaryOperator.PostDecrement
                    ? $"{array}[{guardedIndex}]{indexedOp}"
                    : $"{indexedOp}{array}[{guardedIndex}]";

            if (IsMissingIndexedWitness(logicalLength, out var missingWitness))
            {
                return $"(false ? {Apply(index)} : throw new InvalidOperationException("
                    + $"\"Indexed-type size witness '{missingWitness}' is unavailable\"))";
            }
            if (!ShouldEmitObligationGuard(
                Verification.Obligations.ObligationKind.IndexBounds,
                indexedOperand.Span,
                reference.Name))
            {
                return Apply(index);
            }

            var guardedIndex = $"__calorIndex{_indexGuardCounter++}";
            return $"(({index}) is var {guardedIndex}"
                + $" && {guardedIndex} >= 0"
                + $" && {guardedIndex} < {SanitizeIdentifier(logicalLength)}"
                + $" ? {Apply(guardedIndex)}"
                + " : throw new IndexOutOfRangeException("
                + "\"Indexed-type bound violated\"))";
        }

        var operand = node.Operand.Accept(this);
        var op = node.Operator.ToCSharpOperator();
        // Only parenthesize when operand is a binary expression (lower precedence than unary)
        var needsParens = node.Operand is BinaryOperationNode or IsPatternNode;
        if (node.Operator is UnaryOperator.PostIncrement or UnaryOperator.PostDecrement)
            return needsParens ? $"({operand}){op}" : $"{operand}{op}";
        return needsParens ? $"{op}({operand})" : $"{op}{operand}";
    }

    /// <summary>
    /// Returns C# operator precedence (higher = binds tighter).
    /// Based on C# specification operator precedence table.
    /// </summary>
    private static int GetPrecedence(BinaryOperator op)
    {
        return op switch
        {
            BinaryOperator.Power => 13,
            BinaryOperator.Multiply or BinaryOperator.Divide or BinaryOperator.Modulo => 12,
            BinaryOperator.Add or BinaryOperator.Subtract => 11,
            BinaryOperator.LeftShift or BinaryOperator.RightShift => 10,
            BinaryOperator.LessThan or BinaryOperator.LessOrEqual
                or BinaryOperator.GreaterThan or BinaryOperator.GreaterOrEqual => 9,
            BinaryOperator.Equal or BinaryOperator.NotEqual => 8,
            BinaryOperator.BitwiseAnd => 7,
            BinaryOperator.BitwiseXor => 6,
            BinaryOperator.BitwiseOr => 5,
            BinaryOperator.And => 4,
            BinaryOperator.Or => 3,
            _ => 1
        };
    }

    public string Visit(PrintStatementNode node)
    {
        var expr = node.Expression.Accept(this);
        var method = node.IsWriteLine ? "Console.WriteLine" : "Console.Write";
        return $"{method}({expr});";
    }

    public string Visit(ContinueStatementNode node)
    {
        return "continue;";
    }

    public string Visit(BreakStatementNode node)
    {
        return "break;";
    }

    public string Visit(GotoStatementNode node)
    {
        if (node.CaseLabel != null)
            return $"goto case {node.CaseLabel.Accept(this)};";
        if (node.IsDefault)
            return "goto default;";
        return $"goto {node.Label};";
    }

    public string Visit(LabelStatementNode node)
    {
        return $"{node.Label}:";
    }

    public string Visit(YieldReturnStatementNode node)
    {
        if (node.Expression == null)
        {
            throw new InvalidOperationException(
                "A valueless §YIELD is invalid. Use §YBRK to emit 'yield break;'.");
        }
        var expr = node.Expression.Accept(this);
        if (_currentYieldRefinement != null
            && _refinementTypes.TryGetValue(
                _currentYieldRefinement,
                out var refinementType)
            && ShouldEmitObligationGuard(
                Verification.Obligations.ObligationKind.RefinementReturn))
        {
            var resultName = $"__refinedYield{_inlineReturnGuardCounter++}";
            var condition = EmitRefinementCondition(
                GetEffectiveRefinementPredicate(refinementType),
                resultName);
            var continuationIndent = Environment.NewLine
                + new string(' ', _emissionContext.IndentLevel * 4);
            return $"var {resultName} = {expr};"
                + continuationIndent
                + $"if (!({condition})) throw new InvalidOperationException("
                + $"\"Yielded value violates refinement type '{refinementType.Name}'\");"
                + continuationIndent
                + $"yield return {resultName};";
        }

        return $"yield return {expr};";
    }

    public string Visit(YieldBreakStatementNode node)
    {
        return "yield break;";
    }

    // Phase 3: Type System

    public string Visit(RecordDefinitionNode node)
    {
        AppendLine($"public record {SanitizeIdentifier(node.Name)}(");
        Indent();

        for (int i = 0; i < node.Fields.Count; i++)
        {
            var field = node.Fields[i];
            var typeName = MapTypeName(field.TypeName);
            var fieldName = SanitizeIdentifier(field.Name);
            var comma = i < node.Fields.Count - 1 ? "," : "";
            AppendLine($"{typeName} {fieldName}{comma}");
        }

        Dedent();
        AppendLine(");");

        return "";
    }

    public string Visit(FieldDefinitionNode node)
    {
        var defaultValue = node.DefaultValue is null ? "" : $" = {node.DefaultValue.Accept(this)}";
        return $"{MapTypeName(node.TypeName)} {SanitizeIdentifier(node.Name)}{defaultValue}";
    }

    public string Visit(UnionTypeDefinitionNode node)
    {
        // Generate as abstract base class with derived classes for each variant
        var typeName = SanitizeIdentifier(node.Name);

        AppendLine($"public abstract record {typeName};");
        AppendLine();

        foreach (var variant in node.Variants)
        {
            var variantName = SanitizeIdentifier(variant.Name);
            if (variant.Fields.Count == 0)
            {
                AppendLine($"public sealed record {variantName}() : {typeName};");
            }
            else
            {
                var fields = string.Join(", ", variant.Fields.Select(f =>
                    $"{MapTypeName(f.TypeName)} {SanitizeIdentifier(f.Name)}"));
                AppendLine($"public sealed record {variantName}({fields}) : {typeName};");
            }
        }

        return "";
    }

    public string Visit(VariantDefinitionNode node)
    {
        var fields = string.Join(", ", node.Fields.Select(Visit));
        return $"{SanitizeIdentifier(node.Name)}({fields})";
    }

    public string Visit(TypeReferenceNode node)
    {
        var typeName = MapTypeName(node.Name);
        return node.TypeArguments.Count == 0
            ? typeName
            : $"{typeName}<{string.Join(", ", node.TypeArguments.Select(Visit))}>";
    }

    public string Visit(EnumDefinitionNode node)
    {
        EmitCSharpAttributes(node.CSharpAttributes);

        // Generate C# enum with optional underlying type
        var typeName = SanitizeIdentifier(node.Name);
        var baseType = node.UnderlyingType != null
            ? $" : {MapTypeName(node.UnderlyingType)}"
            : "";

        var visibility = node.Visibility switch
        {
            Visibility.Public => "public",
            Visibility.ProtectedInternal => "protected internal",
            Visibility.Internal => "internal",
            Visibility.Protected => "protected",
            Visibility.Private => "private",
            _ => "internal"
        };

        AppendLine($"{visibility} enum {typeName}{baseType}");
        AppendLine("{");
        Indent();

        foreach (var member in node.Members)
        {
            Visit(member);
        }

        Dedent();
        AppendLine("}");

        return "";
    }

    public string Visit(EnumMemberNode node)
    {
        EmitCSharpAttributes(node.CSharpAttributes);
        var memberName = SanitizeIdentifier(node.Name);
        var value = node.Value != null ? $" = {node.Value}" : "";
        AppendLine($"{memberName}{value},");
        return "";
    }

    public string Visit(EnumExtensionNode node)
    {
        // Generate a static extension class for the enum
        var enumName = SanitizeIdentifier(node.EnumName);
        var className = $"{enumName}Extensions";

        AppendLine($"public static class {className}");
        AppendLine("{");
        Indent();

        foreach (var method in node.Methods)
        {
            EmitExtensionMethod(method, enumName);
            AppendLine();
        }

        Dedent();
        AppendLine("}");

        return "";
    }

    /// <summary>
    /// Emits an extension method for an enum.
    /// The first parameter with the enum type becomes the 'this' parameter.
    /// </summary>
    private void EmitExtensionMethod(FunctionNode method, string enumName)
    {
        // Track current function ID for contract emission
        _currentFunctionId = method.Id;
        _currentPostconditionIndex = 0;
        ResetDeclScopes(method.Parameters);

        // Emit extended metadata as documentation comments
        foreach (var issue in method.Issues)
        {
            AppendLine(Visit(issue));
        }
        if (method.Deprecated != null)
        {
            AppendLine(Visit(method.Deprecated));
        }

        var visibility = method.Visibility switch
        {
            Visibility.Public => "public",
            Visibility.ProtectedInternal => "protected internal",
            Visibility.Internal => "internal",
            Visibility.Protected => "protected",
            Visibility.Private => "private",
            _ => "public"
        };

        var returnType = method.Output?.TypeName ?? "void";
        var isIterator = ContainsYieldStatements(method.Body);
        var returnShape = GetCallableReturnShape(
            returnType,
            method.IsAsync,
            isIterator);
        var hasReturnRefinement = _refinementTypes.ContainsKey(returnType);
        var previousYieldRefinement = _currentYieldRefinement;
        _currentYieldRefinement = hasReturnRefinement && isIterator
            ? returnType
            : null;
        _inlineReturnGuardCounter = 0;

        // Find the 'self' parameter (the one with the enum type) and make it the 'this' parameter
        var selfParam = method.Parameters.FirstOrDefault(p =>
            p.TypeName.Equals(enumName, StringComparison.OrdinalIgnoreCase) ||
            p.Name.Equals("self", StringComparison.OrdinalIgnoreCase));

        var parameters = new List<string>();
        var hasThisParam = false;

        foreach (var p in method.Parameters)
        {
            var paramType = MapTypeName(p.TypeName);
            var paramName = SanitizeIdentifier(p.Name);

            if (p == selfParam && !hasThisParam)
            {
                // This is the extension method 'this' parameter
                parameters.Add($"this {paramType} {paramName}");
                hasThisParam = true;
            }
            else
            {
                parameters.Add($"{paramType} {paramName}");
            }
        }

        var paramString = string.Join(", ", parameters);
        var methodName = SanitizeIdentifier(method.Name);

        // Build type parameters if present
        var typeParams = "";
        var whereClause = "";
        if (method.TypeParameters.Count > 0)
        {
            typeParams = "<" + string.Join(", ", method.TypeParameters.Select(tp => tp.Accept(this))) + ">";

            var whereClauses = new List<string>();
            foreach (var tp in method.TypeParameters)
            {
                if (tp.Constraints.Count > 0)
                {
                    var constraints = string.Join(", ", tp.Constraints.Select(c => EmitConstraint(c)));
                    whereClauses.Add($"where {tp.Name} : {constraints}");
                }
            }
            if (whereClauses.Count > 0)
            {
                whereClause = " " + string.Join(" ", whereClauses);
            }
        }

        var asyncKeyword = method.IsAsync ? "async " : "";

        AppendLine($"{visibility} static {asyncKeyword}{returnShape.DeclarationType} {methodName}{typeParams}({paramString}){whereClause}");
        AppendLine("{");
        Indent();

        // Emit preconditions
        foreach (var requires in method.Preconditions)
        {
            var check = Visit(requires);
            AppendLine(check);
        }
        EmitRefinementParameterGuards(method.Parameters);

        EmitCallableBody(
            method.Body,
            method.Parameters,
            method.TypeParameters,
            method.Postconditions
                .Select(postcondition => new PostconditionEmission(postcondition))
                .ToArray(),
            method.Name,
            method.Span,
            returnShape,
            returnType,
            isIterator);

        Dedent();
        AppendLine("}");
        _currentYieldRefinement = previousYieldRefinement;
    }

    public string Visit(RecordCreationNode node)
    {
        var typeName = MapTypeName(node.TypeName);
        var fields = string.Join(", ", node.Fields.Select(f => f.Value.Accept(this)));
        return $"new {typeName}({fields})";
    }

    public string Visit(FieldAssignmentNode node) => node.Value.Accept(this);

    public string Visit(FieldAccessNode node)
    {
        var target = node.Target.Accept(this);
        var fieldName = SanitizeIdentifier(node.FieldName);
        return $"{target}.{fieldName}";
    }

    public string Visit(SomeExpressionNode node)
    {
        _usesCalorRuntime = true;
        var value = node.Value.Accept(this);
        return $"Calor.Runtime.Option.Some({value})";
    }

    public string Visit(NoneExpressionNode node)
    {
        _usesCalorRuntime = true;
        if (node.TypeName != null)
        {
            var typeName = MapTypeName(node.TypeName);
            return $"Calor.Runtime.Option<{typeName}>.None()";
        }
        return "Calor.Runtime.Option.None<object>()";
    }

    public string Visit(OkExpressionNode node)
    {
        _usesCalorRuntime = true;
        var value = node.Value.Accept(this);
        return $"Calor.Runtime.Result.Ok<{GetInferredTypeName(node.Value)}, string>({value})";
    }

    public string Visit(ErrExpressionNode node)
    {
        _usesCalorRuntime = true;
        var error = node.Error.Accept(this);
        return $"Calor.Runtime.Result.Err<object, {GetInferredTypeName(node.Error)}>({error})";
    }

    public string Visit(MatchExpressionNode node)
    {
        // Generate as switch expression
        var target = node.Target.Accept(this);
        var sb = new System.Text.StringBuilder();
        sb.Append($"({target}) switch {{ ");

        for (int i = 0; i < node.Cases.Count; i++)
        {
            var matchCase = node.Cases[i];
            var pattern = EmitPattern(matchCase.Pattern);
            var previousShadowDepth = _postconditionResultShadowDepth;
            if (PatternBindsName(matchCase.Pattern, "result"))
            {
                _postconditionResultShadowDepth++;
            }

            try
            {
                // Emit guard clause if present
                var guard = matchCase.Guard != null ? $" when {matchCase.Guard.Accept(this)}" : "";

                // For expression match, the body should yield a value
                // Take the last statement if it's a return, otherwise default
                var body = "default";
                if (matchCase.Body.Count > 0)
                {
                    var lastStmt = matchCase.Body[^1];
                    if (lastStmt is ReturnStatementNode ret && ret.Expression != null)
                    {
                        body = ret.Expression.Accept(this);
                    }
                }
                sb.Append($"{pattern}{guard} => {body}");
                if (i < node.Cases.Count - 1) sb.Append(", ");
            }
            finally
            {
                _postconditionResultShadowDepth = previousShadowDepth;
            }
        }

        sb.Append(" }");
        return sb.ToString();
    }

    public string Visit(MatchStatementNode node)
    {
        var target = node.Target.Accept(this);

        AppendLine($"switch ({target})");
        AppendLine("{");
        Indent();

        foreach (var matchCase in node.Cases)
        {
            var previousShadowDepth = _postconditionResultShadowDepth;
            if (PatternBindsName(matchCase.Pattern, "result"))
            {
                _postconditionResultShadowDepth++;
            }

            try
            {
                var guard = matchCase.Guard != null
                    ? $" when {matchCase.Guard.Accept(this)}"
                    : "";
                if (matchCase.Pattern is WildcardPatternNode
                    && matchCase.Guard == null)
                {
                    AppendLine("default:");
                }
                else
                {
                    var pattern = EmitPattern(matchCase.Pattern);
                    AppendLine($"case {pattern}{guard}:");
                }
                Indent();

                PushDeclScope();
                foreach (var stmt in matchCase.Body)
                {
                    EmitStatement(stmt, _emissionContext);
                }
                PopDeclScope();

                AppendLine("break;");
                Dedent();
            }
            finally
            {
                _postconditionResultShadowDepth = previousShadowDepth;
            }
        }

        Dedent();
        AppendLine("}");

        return "";
    }

    private string EmitPattern(PatternNode pattern)
    {
        return pattern switch
        {
            WildcardPatternNode => "_",
            VariablePatternNode vp => vp.Name.Contains('.')
                ? SanitizeIdentifier(vp.Name)
                : $"var {SanitizeIdentifier(vp.Name)}",
            VarPatternNode varP => $"var {SanitizeIdentifier(varP.Name)}",
            TypePatternNode tp => Visit(tp),
            LiteralPatternNode lp => lp.Literal.Accept(this),
            RelationalPatternNode rp => Visit(rp),
            PropertyPatternNode pp => Visit(pp),
            PositionalPatternNode pos => Visit(pos),
            ConstantPatternNode cp => cp.Value.Accept(this),
            SomePatternNode sp => $"{{ IsSome: true, Value: {EmitPattern(sp.InnerPattern)} }}",
            NonePatternNode => "{ IsNone: true }",
            OkPatternNode op => $"{{ IsOk: true, Value: {EmitPattern(op.InnerPattern)} }}",
            ErrPatternNode ep => $"{{ IsErr: true, Error: {EmitPattern(ep.InnerPattern)} }}",
            ListPatternNode lp => Visit(lp),
            NegatedPatternNode np => $"not {EmitPattern(np.Inner)}",
            OrPatternNode orp => $"{EmitPattern(orp.Left)} or {EmitPattern(orp.Right)}",
            AndPatternNode andp => $"{EmitPattern(andp.Left)} and {EmitPattern(andp.Right)}",
            // #774: no silent wildcard fallback — an unhandled pattern node would
            // broaden the arm to match everything. Fail loud instead.
            _ => throw new ArgumentOutOfRangeException(nameof(pattern),
                $"Unhandled pattern node in C# emitter: {pattern.GetType().Name}")
        };
    }

    public string Visit(MatchCaseNode node)
    {
        var pattern = EmitPattern(node.Pattern);
        // For match statement context, case is emitted as part of switch
        return pattern;
    }

    public string Visit(WildcardPatternNode node) => "_";

    public string Visit(VariablePatternNode node) => node.Name.Contains('.')
        ? SanitizeIdentifier(node.Name)
        : $"var {SanitizeIdentifier(node.Name)}";

    public string Visit(LiteralPatternNode node) => node.Literal.Accept(this);

    public string Visit(SomePatternNode node)
        => $"{{ IsSome: true, Value: {node.InnerPattern.Accept(this)} }}";

    public string Visit(NonePatternNode node) => "{ IsNone: true }";

    public string Visit(TypePatternNode node)
        => node.BindingName is { } name
            ? $"{node.TypeName} {SanitizeIdentifier(name)}"
            : node.TypeName;

    public string Visit(OkPatternNode node)
        => $"{{ IsOk: true, Value: {node.InnerPattern.Accept(this)} }}";

    public string Visit(ErrPatternNode node)
        => $"{{ IsErr: true, Error: {node.InnerPattern.Accept(this)} }}";

    // Phase 4: Contracts

    public string Visit(RequiresNode node)
    {
        // Off mode: no contract checks
        if (_contractMode == EmitContractMode.Off)
        {
            return ""; // No check emitted
        }

        _usesCalorRuntime = true;
        var condition = node.Condition.Accept(this);
        var functionId = _currentFunctionId ?? "unknown";

        // Precondition guards are NEVER elided on verification results (#755, guarantees
        // plan D-G1.2): the verifier's precondition "Proven" is a satisfiability result
        // (∃ an input meeting it), not validity (∀ inputs meet it) — a caller can still
        // violate the precondition, so the runtime check is the contract. Only a genuine
        // ∀-proof could justify elision, and preconditions have none by construction.

        // Release mode: lean exception
        if (_contractMode == EmitContractMode.Release)
        {
            return $"if (!({condition})) throw new Calor.Runtime.ContractViolationException(\"{EscapeString(functionId)}\", Calor.Runtime.ContractKind.Requires);";
        }

        // Debug mode: full details
        var message = node.Message != null
            ? EscapeString(node.Message)
            : $"Precondition failed: {EscapeString(condition)}";
        var sourceFile = _currentFilePath != null ? $"\"{EscapeString(_currentFilePath)}\"" : "null";

        return $"if (!({condition})) throw new Calor.Runtime.ContractViolationException(" +
               $"\"{message}\", " +
               $"\"{EscapeString(functionId)}\", " +
               $"Calor.Runtime.ContractKind.Requires, " +
               $"startOffset: {node.Span.Start}, " +
               $"length: {node.Span.Length}, " +
               $"sourceFile: {sourceFile}, " +
               $"line: {node.Span.Line}, " +
               $"column: {node.Span.Column}, " +
               $"condition: \"{EscapeString(condition)}\");";
    }

    public string Visit(EnsuresNode node)
    {
        // Off mode: no contract checks
        if (_contractMode == EmitContractMode.Off)
        {
            _currentPostconditionIndex++;
            return ""; // No check emitted
        }

        _usesCalorRuntime = true;
        var condition = node.Condition.Accept(this);
        var functionId = _currentFunctionId ?? "unknown";

        // Check verification status if available
        var verificationResult = GetPostconditionVerificationResult();
        _currentPostconditionIndex++;

        // Proven postconditions elide the runtime check ONLY when the caller opted in
        // (roadmap v0.13 §2.1: elision is opt-in; verification is diagnostic by
        // default). A Proven verdict is a genuine ∀-proof (UNSAT on negation); a
        // VACUOUS proof never qualifies (guarantees plan D-G1.3): it holds only
        // because the precondition set is unsatisfiable, so the check is kept.
        if (ElideProvenGuards
            && verificationResult is { Status: ContractVerificationStatus.Proven }
            && !verificationResult.EffectiveOutcome.IsVacuous)
        {
            return $"// PROVEN: Postcondition statically verified: {condition}";
        }

        // Release mode: lean exception
        if (_contractMode == EmitContractMode.Release)
        {
            return $"if (!({condition})) throw new Calor.Runtime.ContractViolationException(\"{EscapeString(functionId)}\", Calor.Runtime.ContractKind.Ensures);";
        }

        // Debug mode: full details
        var message = node.Message != null
            ? EscapeString(node.Message)
            : $"Postcondition failed: {EscapeString(condition)}";
        var sourceFile = _currentFilePath != null ? $"\"{EscapeString(_currentFilePath)}\"" : "null";

        return $"if (!({condition})) throw new Calor.Runtime.ContractViolationException(" +
               $"\"{message}\", " +
               $"\"{EscapeString(functionId)}\", " +
               $"Calor.Runtime.ContractKind.Ensures, " +
               $"startOffset: {node.Span.Start}, " +
               $"length: {node.Span.Length}, " +
               $"sourceFile: {sourceFile}, " +
               $"line: {node.Span.Line}, " +
               $"column: {node.Span.Column}, " +
               $"condition: \"{EscapeString(condition)}\");";
    }

    public string Visit(InvariantNode node)
    {
        // Off mode: no contract checks
        if (_contractMode == EmitContractMode.Off)
        {
            return ""; // No check emitted
        }

        _usesCalorRuntime = true;
        var condition = node.Condition.Accept(this);
        var functionId = _currentFunctionId ?? "unknown";

        // Release mode: lean exception
        if (_contractMode == EmitContractMode.Release)
        {
            return $"if (!({condition})) throw new Calor.Runtime.ContractViolationException(\"{EscapeString(functionId)}\", Calor.Runtime.ContractKind.Invariant);";
        }

        // Debug mode: full details
        var message = node.Message != null
            ? EscapeString(node.Message)
            : $"Invariant violated: {EscapeString(condition)}";
        var sourceFile = _currentFilePath != null ? $"\"{EscapeString(_currentFilePath)}\"" : "null";

        return $"if (!({condition})) throw new Calor.Runtime.ContractViolationException(" +
               $"\"{message}\", " +
               $"\"{EscapeString(functionId)}\", " +
               $"Calor.Runtime.ContractKind.Invariant, " +
               $"startOffset: {node.Span.Start}, " +
               $"length: {node.Span.Length}, " +
               $"sourceFile: {sourceFile}, " +
               $"line: {node.Span.Line}, " +
               $"column: {node.Span.Column}, " +
               $"condition: \"{EscapeString(condition)}\");";
    }

    private static string EscapeString(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
    }

    private Verification.Z3.ContractVerificationResult? GetPostconditionVerificationResult()
    {
        if (_verificationResults == null || _currentFunctionId == null)
            return null;

        var funcResult = _verificationResults.GetFunctionResult(_currentFunctionId);
        if (funcResult == null || _currentPostconditionIndex >= funcResult.PostconditionResults.Count)
            return null;

        return funcResult.PostconditionResults[_currentPostconditionIndex];
    }

    // Phase 6: Arrays and Collections

    public string Visit(ArrayCreationNode node)
    {
        var elementType = MapTypeName(node.ElementType);

        if (node.Size != null)
        {
            // Sized array expression: new int[10] or new int[n]
            var size = node.Size.Accept(this);
            return $"new {elementType}[{size}]";
        }
        else if (node.Initializer.Count > 0)
        {
            // Initialized array expression: new[] { 1, 2, 3 }
            var elements = string.Join(", ", node.Initializer.Select(e => e.Accept(this)));
            return $"new {elementType}[] {{ {elements} }}";
        }
        else
        {
            // Empty array expression
            return $"Array.Empty<{elementType}>()";
        }
    }

    public string Visit(ArrayAccessNode node)
    {
        var array = node.Array.Accept(this);
        var index = node.Index.Accept(this);
        if (node.Array is ReferenceNode reference
            && TryGetIndexedBound(reference.Name, out var logicalLength))
        {
            if (IsMissingIndexedWitness(logicalLength, out var missingWitness))
            {
                return $"(false ? {array}[{index}] : throw new InvalidOperationException("
                    + $"\"Indexed-type size witness '{missingWitness}' is unavailable\"))";
            }
            if (!ShouldEmitObligationGuard(
                Verification.Obligations.ObligationKind.IndexBounds,
                node.Span,
                reference.Name))
            {
                return $"{array}[{index}]";
            }

            var guardedIndex = $"__calorIndex{_indexGuardCounter++}";
            return $"(({index}) is var {guardedIndex}"
                + $" && {guardedIndex} >= 0"
                + $" && {guardedIndex} < {SanitizeIdentifier(logicalLength)}"
                + $" ? {array}[{guardedIndex}]"
                + " : throw new IndexOutOfRangeException("
                + "\"Indexed-type bound violated\"))";
        }

        return $"{array}[{index}]";
    }

    public string Visit(ArrayLengthNode node)
    {
        var array = node.Array.Accept(this);
        return $"{array}.Length";
    }

    public string Visit(ForeachStatementNode node)
    {
        var varType = MapTypeName(node.VariableType);
        var varName = SanitizeIdentifier(node.VariableName);
        var collection = node.Collection.Accept(this);

        if (node.IndexVariableName != null)
        {
            // Index starts at -1 and increments at the top of each iteration so that
            // `continue` statements in the body don't skip the increment — matching
            // the semantics of C#'s Select((item, index) => ...).
            var indexName = SanitizeIdentifier(node.IndexVariableName);
            AppendLine($"var {indexName} = -1;");
            AppendLine($"foreach ({varType} {varName} in {collection})");
            AppendLine("{");
            Indent();
            AppendLine($"{indexName}++;");

            PushDeclScope();
            // Iteration/index variables are in scope for the body (#732). Note: a §B
            // rebind of a foreach variable is not valid C# (CS1656) regardless — see #738.
            DeclareVarInScope(varName);
            DeclareVarInScope(indexName);
            foreach (var stmt in node.Body)
            {
                EmitStatement(stmt, _emissionContext);
            }
            PopDeclScope();

            Dedent();
            AppendLine("}");
        }
        else
        {
            AppendLine($"foreach ({varType} {varName} in {collection})");
            AppendLine("{");
            Indent();

            PushDeclScope();
            DeclareVarInScope(varName);
            foreach (var stmt in node.Body)
            {
                EmitStatement(stmt, _emissionContext);
            }
            PopDeclScope();

            Dedent();
            AppendLine("}");
        }

        return "";
    }

    // Phase 6 Extended: Collections (List, Dictionary, HashSet)

    public string Visit(ListCreationNode node)
    {
        _usesCollectionsGeneric = true;
        var elementType = MapTypeName(node.ElementType);

        if (node.Elements.Count > 0)
        {
            var elements = string.Join(", ", node.Elements.Select(e => e.Accept(this)));
            return $"new List<{elementType}>() {{ {elements} }}";
        }
        else
        {
            return $"new List<{elementType}>()";
        }
    }

    public string Visit(DictionaryCreationNode node)
    {
        _usesCollectionsGeneric = true;
        var keyType = MapTypeName(node.KeyType);
        var valueType = MapTypeName(node.ValueType);

        if (node.Entries.Count > 0)
        {
            var entries = string.Join(", ", node.Entries.Select(e =>
            {
                var key = e.Key.Accept(this);
                var value = e.Value.Accept(this);
                return $"{{ {key}, {value} }}";
            }));
            return $"new Dictionary<{keyType}, {valueType}>() {{ {entries} }}";
        }
        else
        {
            return $"new Dictionary<{keyType}, {valueType}>()";
        }
    }

    public string Visit(KeyValuePairNode node)
    {
        var key = node.Key.Accept(this);
        var value = node.Value.Accept(this);
        return $"{{ {key}, {value} }}";
    }

    public string Visit(SetCreationNode node)
    {
        _usesCollectionsGeneric = true;
        var elementType = MapTypeName(node.ElementType);

        if (node.Elements.Count > 0)
        {
            var elements = string.Join(", ", node.Elements.Select(e => e.Accept(this)));
            return $"new HashSet<{elementType}>() {{ {elements} }}";
        }
        else
        {
            return $"new HashSet<{elementType}>()";
        }
    }

    public string Visit(CollectionPushNode node)
    {
        var collectionName = SanitizeIdentifier(node.CollectionName);
        var value = node.Value.Accept(this);
        return $"{collectionName}.Add({value});";
    }

    public string Visit(DictionaryPutNode node)
    {
        var dictionaryName = SanitizeIdentifier(node.DictionaryName);
        var key = node.Key.Accept(this);
        var value = node.Value.Accept(this);
        return $"{dictionaryName}[{key}] = {value};";
    }

    public string Visit(CollectionRemoveNode node)
    {
        var collectionName = SanitizeIdentifier(node.CollectionName);
        var keyOrValue = node.KeyOrValue.Accept(this);
        return $"{collectionName}.Remove({keyOrValue});";
    }

    public string Visit(CollectionSetIndexNode node)
    {
        var collectionName = SanitizeIdentifier(node.CollectionName);
        var index = node.Index.Accept(this);
        var value = node.Value.Accept(this);
        if (TryGetIndexedBound(collectionName, out var logicalLength))
        {
            if (IsMissingIndexedWitness(logicalLength, out var missingWitness))
            {
                return "throw new InvalidOperationException("
                    + $"\"Indexed-type size witness '{missingWitness}' is unavailable\");";
            }
            if (!ShouldEmitObligationGuard(
                Verification.Obligations.ObligationKind.IndexBounds,
                node.Span,
                node.CollectionName))
            {
                return $"{collectionName}[{index}] = {value};";
            }

            var guardedIndex = $"__calorIndex{_indexGuardCounter++}";
            var continuationIndent = Environment.NewLine
                + new string(' ', _emissionContext.IndentLevel * 4);
            return $"var {guardedIndex} = {index};"
                + continuationIndent
                + $"if ({guardedIndex} < 0"
                + $" || {guardedIndex} >= {SanitizeIdentifier(logicalLength)})"
                + " throw new IndexOutOfRangeException("
                + "\"Indexed-type bound violated\");"
                + continuationIndent
                + $"{collectionName}[{guardedIndex}] = {value};";
        }

        return $"{collectionName}[{index}] = {value};";
    }

    public string Visit(CollectionClearNode node)
    {
        var collectionName = SanitizeIdentifier(node.CollectionName);
        return $"{collectionName}.Clear();";
    }

    public string Visit(CollectionInsertNode node)
    {
        var collectionName = SanitizeIdentifier(node.CollectionName);
        var index = node.Index.Accept(this);
        var value = node.Value.Accept(this);
        return $"{collectionName}.Insert({index}, {value});";
    }

    public string Visit(CollectionContainsNode node)
    {
        var collectionName = SanitizeIdentifier(node.CollectionName);
        var keyOrValue = node.KeyOrValue.Accept(this);

        return node.Mode switch
        {
            ContainsMode.Key => $"{collectionName}.ContainsKey({keyOrValue})",
            ContainsMode.DictValue => $"{collectionName}.ContainsValue({keyOrValue})",
            ContainsMode.Value => $"{collectionName}.Contains({keyOrValue})",
            _ => $"{collectionName}.Contains({keyOrValue})"
        };
    }

    public string Visit(DictionaryForeachNode node)
    {
        var keyName = SanitizeIdentifier(node.KeyName);
        var valueName = SanitizeIdentifier(node.ValueName);
        var dictionary = node.Dictionary.Accept(this);

        AppendLine($"foreach (var ({keyName}, {valueName}) in {dictionary})");
        AppendLine("{");
        Indent();

        PushDeclScope();
        DeclareVarInScope(keyName);
        DeclareVarInScope(valueName);
        foreach (var stmt in node.Body)
        {
            EmitStatement(stmt, _emissionContext);
        }
        PopDeclScope();

        Dedent();
        AppendLine("}");

        return "";
    }

    public string Visit(CollectionCountNode node)
    {
        var collection = node.Collection.Accept(this);
        return $"{collection}.Count";
    }

    // Phase 7: Generics

    public string Visit(TypeParameterNode node)
    {
        // Type parameters are handled in function/method signature emission
        var variance = node.Variance switch
        {
            VarianceKind.In => "in ",
            VarianceKind.Out => "out ",
            _ => ""
        };
        return $"{variance}{node.Name}";
    }

    public string Visit(TypeConstraintNode node)
    {
        return EmitConstraint(node);
    }

    public string Visit(GenericTypeNode node)
    {
        var typeName = MapTypeName(node.TypeName);
        if (node.TypeArguments.Count == 0)
        {
            return typeName;
        }

        var typeArgs = string.Join(", ", node.TypeArguments.Select(MapTypeName));
        return $"{typeName}<{typeArgs}>";
    }

    private string EmitConstraint(TypeConstraintNode constraint)
    {
        return constraint.Kind switch
        {
            TypeConstraintKind.Class => "class",
            TypeConstraintKind.Struct => "struct",
            TypeConstraintKind.New => "new()",
            TypeConstraintKind.Interface => constraint.TypeName ?? "object",
            TypeConstraintKind.BaseClass => constraint.TypeName ?? "object",
            TypeConstraintKind.TypeName => MapTypeName(constraint.TypeName ?? "object"),
            TypeConstraintKind.NotNull => "notnull",
            _ => "object"
        };
    }

    // Phase 8: Classes, Interfaces, Inheritance

    public string Visit(InterfaceDefinitionNode node)
    {
        EmitCSharpAttributes(node.CSharpAttributes);

        var name = SanitizeIdentifier(node.Name);

        // Build type parameters
        var typeParams = "";
        var whereClause = "";
        if (node.TypeParameters.Count > 0)
        {
            typeParams = "<" + string.Join(", ", node.TypeParameters.Select(tp => tp.Accept(this))) + ">";

            // Build where clauses
            var whereClauses = new List<string>();
            foreach (var tp in node.TypeParameters)
            {
                if (tp.Constraints.Count > 0)
                {
                    var constraints = string.Join(", ", tp.Constraints.Select(c => EmitConstraint(c)));
                    whereClauses.Add($"where {tp.Name} : {constraints}");
                }
            }
            if (whereClauses.Count > 0)
            {
                whereClause = " " + string.Join(" ", whereClauses);
            }
        }

        var baseList = node.BaseInterfaces.Count > 0
            ? " : " + string.Join(", ", node.BaseInterfaces)
            : "";

        AppendLine($"public interface {name}{typeParams}{baseList}{whereClause}");
        AppendLine("{");
        Indent();

        foreach (var prop in node.Properties)
        {
            Visit(prop);
            AppendLine();
        }

        foreach (var indexer in node.Indexers)
        {
            Visit(indexer);
            AppendLine();
        }

        foreach (var method in node.Methods)
        {
            Visit(method);
        }

        Dedent();
        AppendLine("}");

        return "";
    }

    public string Visit(MethodSignatureNode node)
    {
        // Emit XML comments for contracts
        if (node.HasContracts)
        {
            AppendLine("/// <summary>");
            AppendLine($"/// Interface method with contracts.");
            AppendLine("/// </summary>");

            foreach (var requires in node.Preconditions)
            {
                var condition = requires.Condition.Accept(this);
                AppendLine($"/// <remarks>Requires: {condition}</remarks>");
            }

            foreach (var ensures in node.Postconditions)
            {
                var condition = ensures.Condition.Accept(this);
                AppendLine($"/// <remarks>Ensures: {condition}</remarks>");
            }
        }

        EmitCSharpAttributes(node.CSharpAttributes);

        var returnType = node.Output?.TypeName ?? "void";
        var mappedReturnType = MapTypeName(returnType);
        var methodName = SanitizeIdentifier(node.Name);

        var typeParams = "";
        var whereClause = "";
        if (node.TypeParameters.Count > 0)
        {
            typeParams = "<" + string.Join(", ", node.TypeParameters.Select(tp => tp.Accept(this))) + ">";

            // Build where clauses
            var whereClauses = new List<string>();
            foreach (var tp in node.TypeParameters)
            {
                if (tp.Constraints.Count > 0)
                {
                    var constraints = string.Join(", ", tp.Constraints.Select(c => EmitConstraint(c)));
                    whereClauses.Add($"where {tp.Name} : {constraints}");
                }
            }
            if (whereClauses.Count > 0)
            {
                whereClause = " " + string.Join(" ", whereClauses);
            }
        }

        var parameters = string.Join(", ", node.Parameters.Select(p => Visit(p)));

        AppendLine($"{mappedReturnType} {methodName}{typeParams}({parameters}){whereClause};");

        return "";
    }

    public string Visit(ClassDefinitionNode node)
    {
        EmitCSharpAttributes(node.CSharpAttributes);

        var name = SanitizeIdentifier(node.Name);

        var modifiers = node.Visibility switch
        {
            Visibility.Public => "public",
            Visibility.ProtectedInternal => "protected internal",
            Visibility.Internal => "internal",
            Visibility.Protected => "protected",
            Visibility.Private => "private",
            _ => "internal"
        };
        if (node.IsAbstract) modifiers += " abstract";
        if (!node.IsStruct && node.IsSealed) modifiers += " sealed";
        if (node.IsStatic) modifiers += " static";
        if (node.IsReadOnly) modifiers += " readonly";
        if (node.IsPartial) modifiers += " partial";

        var keyword = node.IsStruct ? "struct" : "class";

        // Build type parameters
        var typeParams = "";
        var whereClause = "";
        if (node.TypeParameters.Count > 0)
        {
            typeParams = "<" + string.Join(", ", node.TypeParameters.Select(tp => tp.Accept(this))) + ">";

            var whereClauses = new List<string>();
            foreach (var tp in node.TypeParameters)
            {
                if (tp.Constraints.Count > 0)
                {
                    var constraints = string.Join(", ", tp.Constraints.Select(c => EmitConstraint(c)));
                    whereClauses.Add($"where {tp.Name} : {constraints}");
                }
            }
            if (whereClauses.Count > 0)
            {
                whereClause = " " + string.Join(" ", whereClauses);
            }
        }

        // Build inheritance list
        var baseList = new List<string>();
        if (!string.IsNullOrEmpty(node.BaseClass))
        {
            baseList.Add(node.BaseClass);
        }
        baseList.AddRange(node.ImplementedInterfaces);
        var inheritance = baseList.Count > 0 ? " : " + string.Join(", ", baseList) : "";

        AppendLine($"{modifiers} {keyword} {name}{typeParams}{inheritance}{whereClause}");
        AppendLine("{");
        Indent();

        // Set current class name for constructor emission
        _currentClassName = name;
        // Bare-name calls inside a class resolve to the class's OWN members first;
        // cross-module qualification must never override them (#823 review C1 —
        // mis-qualifying a sibling method call silently ran another module's
        // code). Nested classes push/pop so the outer class's set survives
        // (#823 re-review NEW-1). Classes with a base type suppress
        // qualification entirely: INHERITED members are not enumerable here
        // (the base may be C#), and mis-qualifying one silently runs another
        // module's code (#823 re-review NEW-2) — under-qualification (CS0103)
        // is the acceptable failure direction, silent wrong code is not.
        _classMemberScopes.Push((_currentClassMemberNames, _suppressCrossModuleQualification));
        _currentClassMemberNames = node.Methods.Select(m => m.Name)
            .Concat(node.Fields.Select(f => f.Name))
            .Concat(node.Properties.Select(pr => pr.Name))
            .ToHashSet(StringComparer.Ordinal);
        // Inherited (OR'd with the enclosing class's flag): a nested type inside a
        // derived class also sees the enclosing base's statics bare (#823
        // re-review NEW-1 adjacent).
        _suppressCrossModuleQualification = _suppressCrossModuleQualification
            || !string.IsNullOrEmpty(node.BaseClass);

        // Emit fields
        foreach (var field in node.Fields)
        {
            Visit(field);
        }

        // Emit properties
        foreach (var prop in node.Properties)
        {
            Visit(prop);
            AppendLine();
        }

        // Emit indexers
        foreach (var indexer in node.Indexers)
        {
            Visit(indexer);
            AppendLine();
        }

        // Emit constructors
        foreach (var ctor in node.Constructors)
        {
            Visit(ctor);
            AppendLine();
        }

        // Emit methods
        foreach (var method in node.Methods)
        {
            Visit(method);
            AppendLine();
        }

        // Emit operator overloads
        foreach (var op in node.OperatorOverloads)
        {
            Visit(op);
            AppendLine();
        }

        // Emit events
        foreach (var evt in node.Events)
        {
            Visit(evt);
        }

        // Emit C# interop blocks
        foreach (var interop in node.InteropBlocks)
        {
            Visit(interop);
            AppendLine();
        }

        // Emit preprocessor blocks
        foreach (var ppBlock in node.PreprocessorBlocks)
        {
            Visit(ppBlock);
            AppendLine();
        }

        // Emit nested types
        foreach (var nestedClass in node.NestedClasses)
        {
            Visit(nestedClass);
            AppendLine();
        }
        foreach (var nestedIface in node.NestedInterfaces)
        {
            Visit(nestedIface);
            AppendLine();
        }
        foreach (var nestedEnum in node.NestedEnums)
        {
            Visit(nestedEnum);
            AppendLine();
        }
        foreach (var nestedDelegate in node.NestedDelegates)
        {
            Visit(nestedDelegate);
            AppendLine();
        }

        _currentClassName = null;
        (_currentClassMemberNames, _suppressCrossModuleQualification) = _classMemberScopes.Pop();

        Dedent();
        AppendLine("}");

        return "";
    }

    public string Visit(ClassFieldNode node)
    {
        EmitCSharpAttributes(node.CSharpAttributes);

        var visibility = node.Visibility switch
        {
            Visibility.Public => "public",
            Visibility.ProtectedInternal => "protected internal",
            Visibility.Internal => "internal",
            Visibility.Protected => "protected",
            Visibility.Private => "private",
            _ => "private"
        };

        var parts = new List<string>();
        if (node.IsRequired) parts.Add("required");
        parts.Add(visibility);
        if (node.Modifiers.HasFlag(MethodModifiers.Const)) parts.Add("const");
        else if (node.IsStatic) parts.Add("static");
        if (node.Modifiers.HasFlag(MethodModifiers.Readonly)) parts.Add("readonly");
        if (node.IsVolatile) parts.Add("volatile");
        var fullModifiers = string.Join(" ", parts);

        var typeName = MapTypeName(node.TypeName);
        var fieldName = SanitizeIdentifier(node.Name);

        if (node.DefaultValue != null)
        {
            var defaultVal = node.DefaultValue.Accept(this);
            AppendLine($"{fullModifiers} {typeName} {fieldName} = {defaultVal};");
        }
        else
        {
            AppendLine($"{fullModifiers} {typeName} {fieldName};");
        }

        return "";
    }

    public string Visit(MethodNode node)
    {
        // #879: the postcondition-elision key is a mutable cursor
        // (_currentFunctionId, _currentPostconditionIndex) that every emission path
        // visiting contracts MUST maintain. Before this was set here, class-method
        // postconditions never elided even when Proven, §MT contract violations
        // reported "unknown" (or a foreign id) as the failing function, and — had
        // §EEXT contracts ever been verified — a method's check could have been
        // deleted on an unrelated function's proof.
        _currentFunctionId = node.Id;
        _currentPostconditionIndex = 0;

        // Clear declared variables tracking for new method scope
        ResetDeclScopes(node.Parameters);

        EmitCSharpAttributes(node.CSharpAttributes);

        var visibility = node.Visibility switch
        {
            Visibility.Public => "public",
            Visibility.ProtectedInternal => "protected internal",
            Visibility.Internal => "internal",
            Visibility.Protected => "protected",
            Visibility.Private => "private",
            _ => "private"
        };

        var modifiers = new List<string> { visibility };
        if (node.IsStatic) modifiers.Add("static");
        if (node.IsUnsafe) modifiers.Add("unsafe");
        if (node.IsExtern) modifiers.Add("extern");
        if (node.IsPartial) modifiers.Add("partial");
        if (node.IsAsync) modifiers.Add("async");
        if (node.IsAbstract) modifiers.Add("abstract");
        else if (node.IsVirtual) modifiers.Add("virtual");
        if (node.IsOverride) modifiers.Add("override");
        if (node.IsSealed && node.IsOverride) modifiers.Add("sealed");

        var returnType = node.Output?.TypeName ?? "void";
        var isIterator = ContainsYieldStatements(node.Body);
        var returnShape = GetCallableReturnShape(
            returnType,
            node.IsAsync,
            isIterator);
        var hasReturnRefinement = _refinementTypes.ContainsKey(returnType);
        var methodName = SanitizeIdentifier(node.Name);

        // Build type parameters
        var typeParams = "";
        var whereClause = "";
        if (node.TypeParameters.Count > 0)
        {
            typeParams = "<" + string.Join(", ", node.TypeParameters.Select(tp => tp.Accept(this))) + ">";

            var whereClauses = new List<string>();
            foreach (var tp in node.TypeParameters)
            {
                if (tp.Constraints.Count > 0)
                {
                    var constraints = string.Join(", ", tp.Constraints.Select(c => EmitConstraint(c)));
                    whereClauses.Add($"where {tp.Name} : {constraints}");
                }
            }
            if (whereClauses.Count > 0)
            {
                whereClause = " " + string.Join(" ", whereClauses);
            }
        }

        var parameters = string.Join(", ", node.Parameters.Select(p => Visit(p)));

        // Operator overload detection: op_ prefix methods emit C# operator syntax
        if (node.Name.StartsWith("op_"))
        {
            return EmitOperatorMethod(
                node,
                modifiers,
                returnShape,
                parameters,
                isIterator);
        }

        // Abstract methods, extern methods, and partial method stubs have no body
        if (node.IsAbstract || node.IsExtern || (node.IsPartial && node.Body.Count == 0))
        {
            AppendLine($"{string.Join(" ", modifiers)} {returnShape.DeclarationType} {methodName}{typeParams}({parameters}){whereClause};");
            return "";
        }

        AppendLine($"{string.Join(" ", modifiers)} {returnShape.DeclarationType} {methodName}{typeParams}({parameters}){whereClause}");
        AppendLine("{");
        Indent();

        // Check for inherited contracts
        var inheritedContracts = _currentClassName != null && _inheritanceResult != null
            ? _inheritanceResult.GetInheritedContracts(_currentClassName, node)
            : null;

        // Emit explicit preconditions
        foreach (var requires in node.Preconditions)
        {
            var check = Visit(requires);
            AppendLine(check);
        }
        EmitRefinementParameterGuards(node.Parameters);

        // Emit inherited preconditions (only if method has no explicit contracts)
        if (!node.HasContracts && inheritedContracts != null)
        {
            foreach (var requires in inheritedContracts.Preconditions)
            {
                AppendLine($"// Inherited from {inheritedContracts.SourceDisplayName}");
                var check = Visit(requires);
                AppendLine(check);
            }
        }

        // Calculate effective postconditions
        var hasInheritedPostconditions = !node.HasContracts
            && inheritedContracts != null
            && inheritedContracts.Postconditions.Count > 0;
        var effectivePostconditions = node.Postconditions.Count > 0
            ? node.Postconditions
            : hasInheritedPostconditions
                ? inheritedContracts!.Postconditions
                : Array.Empty<EnsuresNode>();
        var previousYieldRefinement = _currentYieldRefinement;
        _currentYieldRefinement = hasReturnRefinement && isIterator
            ? returnType
            : null;
        _inlineReturnGuardCounter = 0;

        var postconditions = effectivePostconditions
            .Select(postcondition => new PostconditionEmission(
                postcondition,
                hasInheritedPostconditions
                    ? inheritedContracts!.SourceDisplayName
                    : null))
            .ToArray();
        EmitCallableBody(
            node.Body,
            node.Parameters,
            node.TypeParameters,
            postconditions,
            node.Name,
            node.Span,
            returnShape,
            returnType,
            isIterator);

        Dedent();
        AppendLine("}");
        _currentYieldRefinement = previousYieldRefinement;

        return "";
    }

    private void EmitRefinementParameterGuards(IReadOnlyList<ParameterNode> parameters)
    {
        foreach (var parameter in parameters)
        {
            // An out parameter has no readable entry value. Its refinement is
            // enforced after each assignment through refinement-scope tracking.
            if (parameter.Modifier == ParameterModifier.Out)
                continue;
            if (!ShouldEmitObligationGuard(
                Verification.Obligations.ObligationKind.RefinementEntry,
                parameter.Span,
                parameter.Name))
            {
                continue;
            }

            if (!TryGetRefinementConstraint(
                    parameter.Name,
                    out var constraint))
                continue;

            var condition = EmitRefinementCondition(
                constraint.Predicate,
                parameter.Name);
            AppendLine($"if (!({condition})) throw new ArgumentOutOfRangeException(" +
                $"nameof({SanitizeIdentifier(parameter.Name)}), \"Violation of {constraint.Description}\");");
        }
    }

    private void EmitReturnRefinementGuard(string returnType, string resultName)
    {
        if (!_refinementTypes.TryGetValue(returnType, out var refinementType))
            return;
        if (!ShouldEmitObligationGuard(
            Verification.Obligations.ObligationKind.RefinementReturn))
        {
            return;
        }

        var condition = EmitRefinementCondition(
            GetEffectiveRefinementPredicate(refinementType),
            resultName);
        AppendLine($"if (!({condition})) throw new InvalidOperationException(" +
            $"\"Return value violates refinement type '{refinementType.Name}'\");");
    }

    private void EmitRefinementValueGuard(
        RefinementConstraint constraint,
        string valueName)
    {
        var condition = EmitRefinementCondition(constraint.Predicate, valueName);
        AppendLine($"if (!({condition})) throw new ArgumentOutOfRangeException(" +
            $"nameof({valueName}), \"Value violates {constraint.Description}\");");
    }

    private string EmitRefinementCondition(ExpressionNode predicate, string valueName)
    {
        var sanitizedValueName = SanitizeIdentifier(valueName);
        if (HasQuantifierBindingName(predicate, sanitizedValueName))
            return "false";

        var condition = predicate.Accept(this);
        if (condition.Contains("STATIC ONLY:", StringComparison.Ordinal))
            return "false";

        return System.Text.RegularExpressions.Regex.Replace(
            condition,
            @"\b__self__\b",
            sanitizedValueName);
    }

    private static bool HasQuantifierBindingName(
        ExpressionNode predicate,
        string valueName)
    {
        if (predicate is ForallExpressionNode forall
            && forall.BoundVariables.Any(variable =>
                string.Equals(
                    SanitizeIdentifier(variable.Name),
                    valueName,
                    StringComparison.Ordinal)))
        {
            return true;
        }
        if (predicate is ExistsExpressionNode exists
            && exists.BoundVariables.Any(variable =>
                string.Equals(
                    SanitizeIdentifier(variable.Name),
                    valueName,
                    StringComparison.Ordinal)))
        {
            return true;
        }

        return Calor.Compiler.Analysis.RecursiveAstWalker
            .GetAllChildren(predicate)
            .OfType<ExpressionNode>()
            .Any(child => HasQuantifierBindingName(child, valueName));
    }

    private static readonly Dictionary<string, string> CilNameToOperator = new()
    {
        ["op_Addition"] = "+",
        ["op_Subtraction"] = "-",
        ["op_Multiply"] = "*",
        ["op_Division"] = "/",
        ["op_Modulus"] = "%",
        ["op_Equality"] = "==",
        ["op_Inequality"] = "!=",
        ["op_LessThan"] = "<",
        ["op_GreaterThan"] = ">",
        ["op_LessThanOrEqual"] = "<=",
        ["op_GreaterThanOrEqual"] = ">=",
        ["op_UnaryNegation"] = "-",
        ["op_UnaryPlus"] = "+",
        ["op_LogicalNot"] = "!",
        ["op_BitwiseAnd"] = "&",
        ["op_BitwiseOr"] = "|",
        ["op_ExclusiveOr"] = "^",
    };

    private string EmitOperatorMethod(
        MethodNode node,
        List<string> modifiers,
        CallableReturnShape returnShape,
        string parameters,
        bool isIterator)
    {
        var modStr = string.Join(" ", modifiers);

        if (node.Name == "op_Implicit")
        {
            AppendLine($"{modStr} implicit operator {returnShape.DeclarationType}({parameters})");
        }
        else if (node.Name == "op_Explicit")
        {
            AppendLine($"{modStr} explicit operator {returnShape.DeclarationType}({parameters})");
        }
        else if (CilNameToOperator.TryGetValue(node.Name, out var op))
        {
            AppendLine($"{modStr} {returnShape.DeclarationType} operator {op}({parameters})");
        }
        else
        {
            // Unknown operator — fall back to regular method
            AppendLine($"{modStr} {returnShape.DeclarationType} {SanitizeIdentifier(node.Name)}({parameters})");
        }

        AppendLine("{");
        Indent();

        // Emit explicit preconditions
        foreach (var requires in node.Preconditions)
        {
            var check = Visit(requires);
            AppendLine(check);
        }
        EmitRefinementParameterGuards(node.Parameters);

        var returnType = node.Output?.TypeName ?? "void";
        var hasReturnRefinement = _refinementTypes.ContainsKey(returnType);
        var previousYieldRefinement = _currentYieldRefinement;
        _currentYieldRefinement = hasReturnRefinement && isIterator
            ? returnType
            : null;
        _inlineReturnGuardCounter = 0;

        EmitCallableBody(
            node.Body,
            node.Parameters,
            node.TypeParameters,
            node.Postconditions
                .Select(postcondition => new PostconditionEmission(postcondition))
                .ToArray(),
            node.Name,
            node.Span,
            returnShape,
            returnType,
            isIterator);

        Dedent();
        AppendLine("}");
        _currentYieldRefinement = previousYieldRefinement;

        return "";
    }

    public string Visit(NewExpressionNode node)
    {
        var typeName = MapTypeName(node.TypeName);
        if (node.TypeArguments.Count > 0)
        {
            typeName += "<" + string.Join(", ", node.TypeArguments.Select(MapTypeName)) + ">";
        }

        var args = string.Join(", ", node.Arguments.Select(a => a.Accept(this)));
        var result = $"new {typeName}({args})";

        if (node.Initializers.Count > 0)
        {
            var inits = node.Initializers.Select(i =>
                $"{SanitizeIdentifier(i.PropertyName)} = {i.Value.Accept(this)}");
            result += $" {{ {string.Join(", ", inits)} }}";
        }

        return result;
    }

    public string Visit(AnonymousObjectCreationNode node)
    {
        var props = node.Initializers.Select(i =>
            $"{SanitizeIdentifier(i.PropertyName)} = {i.Value.Accept(this)}");
        return $"new {{ {string.Join(", ", props)} }}";
    }

    public string Visit(CallExpressionNode node)
    {
        // Unescape braces that were escaped for Calor syntax: \{ -> { and \} -> }
        var target = UnescapeBraces(node.Target);
        // A leading dot (e.g., §C{.Method}) means implicit this — prepend "this"
        if (target.StartsWith("."))
            target = "this" + target;
        else
            target = QualifyCrossModuleTarget(target);

        // Append explicit generic type arguments: target<T1, T2>(args)
        if (node.TypeArguments is { Count: > 0 })
        {
            var typeArgs = string.Join(", ", node.TypeArguments.Select(MapTypeName));
            target += $"<{typeArgs}>";
        }

        var argStrings = new List<string>();
        for (int i = 0; i < node.Arguments.Count; i++)
        {
            var argStr = node.Arguments[i].Accept(this);
            if (node.ArgumentModifiers != null && i < node.ArgumentModifiers.Count && node.ArgumentModifiers[i] != null)
            {
                argStr = $"{node.ArgumentModifiers[i]} {argStr}";
            }
            if (node.ArgumentNames != null && i < node.ArgumentNames.Count && node.ArgumentNames[i] != null)
            {
                argStr = $"{node.ArgumentNames[i]}: {argStr}";
            }
            argStrings.Add(argStr);
        }
        var args = string.Join(", ", argStrings);
        return $"{target}({args})";
    }

    /// <summary>
    /// Unescapes braces that were escaped for Calor syntax.
    /// \{ becomes { and \} becomes }
    /// </summary>
    private static string UnescapeBraces(string input)
    {
        if (!input.Contains('\\'))
            return input;

        return input.Replace("\\{", "{").Replace("\\}", "}");
    }

    public string Visit(ThisExpressionNode node)
    {
        return "this";
    }

    public string Visit(BaseExpressionNode node)
    {
        return "base";
    }

    public string Visit(TupleLiteralNode node)
    {
        var elements = string.Join(", ", node.Elements.Select(e => e.Accept(this)));
        return $"({elements})";
    }

    // Phase 9: Properties and Constructors

    public string Visit(PropertyNode node)
    {
        EmitCSharpAttributes(node.CSharpAttributes);

        var visibility = node.Visibility switch
        {
            Visibility.Public => "public",
            Visibility.ProtectedInternal => "protected internal",
            Visibility.Internal => "internal",
            Visibility.Protected => "protected",
            Visibility.Private => "private",
            _ => "public"
        };

        var modifiers = new List<string>();
        if (node.IsRequired) modifiers.Add("required");
        modifiers.Add(visibility);
        if (node.IsStatic) modifiers.Add("static");
        if (node.IsAbstract) modifiers.Add("abstract");
        else if (node.IsVirtual) modifiers.Add("virtual");
        if (node.IsOverride) modifiers.Add("override");
        if (node.IsSealed && node.IsOverride) modifiers.Add("sealed");

        var modifierStr = string.Join(" ", modifiers);
        var typeName = MapTypeName(node.TypeName);
        var propName = SanitizeIdentifier(node.Name);

        // Auto-property with default value
        if (node.IsAutoProperty)
        {
            var getVis = FormatAccessorVisibility(node.Getter?.Visibility);
            var accessors = "get;";
            if (node.Setter != null)
            {
                var setVis = FormatAccessorVisibility(node.Setter.Visibility);
                accessors = $"{getVis}get; {setVis}set;";
            }
            else if (node.Initer != null)
            {
                var initVis = FormatAccessorVisibility(node.Initer.Visibility);
                accessors = $"{getVis}get; {initVis}init;";
            }
            if (node.DefaultValue != null)
            {
                var defaultVal = node.DefaultValue.Accept(this);
                AppendLine($"{modifierStr} {typeName} {propName} {{ {accessors} }} = {defaultVal};");
            }
            else
            {
                AppendLine($"{modifierStr} {typeName} {propName} {{ {accessors} }}");
            }
            return "";
        }

        // Property with accessors
        AppendLine($"{modifierStr} {typeName} {propName}");
        AppendLine("{");
        Indent();

        if (node.Getter != null)
        {
            Visit(node.Getter);
        }

        if (node.Setter != null)
        {
            Visit(node.Setter);
        }

        if (node.Initer != null)
        {
            Visit(node.Initer);
        }

        Dedent();
        AppendLine("}");

        return "";
    }

    public string Visit(IndexerNode node)
    {
        EmitCSharpAttributes(node.CSharpAttributes);

        var visibility = node.Visibility switch
        {
            Visibility.Public => "public",
            Visibility.ProtectedInternal => "protected internal",
            Visibility.Internal => "internal",
            Visibility.Protected => "protected",
            Visibility.Private => "private",
            _ => "public"
        };

        var modifiers = new List<string>();
        modifiers.Add(visibility);
        if (node.IsAbstract) modifiers.Add("abstract");
        else if (node.IsVirtual) modifiers.Add("virtual");
        if (node.IsOverride) modifiers.Add("override");
        if (node.IsSealed && node.IsOverride) modifiers.Add("sealed");

        var modifierStr = string.Join(" ", modifiers);
        var typeName = MapTypeName(node.TypeName);

        // Build parameter list
        var paramList = string.Join(", ", node.Parameters.Select(p => p.Accept(this)));

        // Auto-indexer
        if (node.IsAutoIndexer)
        {
            var getVis = FormatAccessorVisibility(node.Getter?.Visibility);
            var accessors = "get;";
            if (node.Setter != null)
            {
                var setVis = FormatAccessorVisibility(node.Setter.Visibility);
                accessors = $"{getVis}get; {setVis}set;";
            }
            else if (node.Initer != null)
            {
                var initVis = FormatAccessorVisibility(node.Initer.Visibility);
                accessors = $"{getVis}get; {initVis}init;";
            }
            AppendLine($"{modifierStr} {typeName} this[{paramList}] {{ {accessors} }}");
            return "";
        }

        // Indexer with accessor bodies
        AppendLine($"{modifierStr} {typeName} this[{paramList}]");
        AppendLine("{");
        Indent();

        if (node.Getter != null)
        {
            Visit(node.Getter);
        }

        if (node.Setter != null)
        {
            Visit(node.Setter);
        }

        if (node.Initer != null)
        {
            Visit(node.Initer);
        }

        Dedent();
        AppendLine("}");

        return "";
    }

    private static string FormatAccessorVisibility(Visibility? visibility) => visibility switch
    {
        Visibility.Private => "private ",
        Visibility.ProtectedInternal => "protected internal ",
        Visibility.Internal => "internal ",
        Visibility.Protected => "protected ",
        _ => ""
    };

    public string Visit(PropertyAccessorNode node)
    {
        // #879: property accessors are never verified (EnumerateContractBearers covers
        // functions and class methods only), and this node carries no id. A stale cursor
        // here would attribute an accessor precondition failure to a foreign method — or,
        // worse, elide on a foreign proof. Null is the honest key: lookup misses, guard
        // kept, id reported as "unknown".
        _currentFunctionId = null;
        _currentPostconditionIndex = 0;

        var accessorKeyword = node.Kind switch
        {
            PropertyAccessorNode.AccessorKind.Get => "get",
            PropertyAccessorNode.AccessorKind.Set => "set",
            PropertyAccessorNode.AccessorKind.Init => "init",
            _ => "get"
        };

        var visibilityPrefix = node.Visibility switch
        {
            Visibility.Private => "private ",
            Visibility.ProtectedInternal => "protected internal ",
            Visibility.Internal => "internal ",
            Visibility.Protected => "protected ",
            _ => ""
        };

        if (node.IsAutoImplemented)
        {
            AppendLine($"{visibilityPrefix}{accessorKeyword};");
        }
        else
        {
            // Clear declared variables tracking for new accessor scope
            ResetDeclScopes();

            AppendLine($"{visibilityPrefix}{accessorKeyword}");
            AppendLine("{");
            Indent();

            foreach (var pre in node.Preconditions)
            {
                AppendLine(Visit(pre));
            }

            foreach (var stmt in node.Body)
            {
                EmitStatement(stmt, _emissionContext);
            }

            Dedent();
            AppendLine("}");
        }

        return "";
    }

    public string Visit(ConstructorNode node)
    {
        // #879: constructors are never verified, so their own id is the honest cursor
        // key — the results lookup misses (guard kept) and violations report this
        // constructor, not whatever method the cursor last pointed at.
        _currentFunctionId = node.Id;
        _currentPostconditionIndex = 0;

        // Clear declared variables tracking for new constructor scope
        ResetDeclScopes(node.Parameters);

        EmitCSharpAttributes(node.CSharpAttributes);

        // Constructor name is the class name
        var ctorName = _currentClassName ?? "UnknownClass";

        if (node.IsStatic)
        {
            AppendLine($"static {ctorName}()");
        }
        else
        {
            var visibility = node.Visibility switch
            {
                Visibility.Public => "public",
                Visibility.ProtectedInternal => "protected internal",
                Visibility.Internal => "internal",
                Visibility.Protected => "protected",
                Visibility.Private => "private",
                _ => "public"
            };

            var parameters = string.Join(", ", node.Parameters.Select(p => Visit(p)));

            var initializerStr = "";
            if (node.Initializer != null)
            {
                var initArgs = node.Initializer.Arguments
                    .Select(argument => argument.Accept(this))
                    .ToArray();
                var entryChecks = node.Parameters
                    .Where(parameter => parameter.Modifier != ParameterModifier.Out)
                    .Select(parameter =>
                    {
                        if (!TryGetRefinementConstraint(
                                parameter.Name,
                                out var constraint)
                            || !ShouldEmitObligationGuard(
                                Verification.Obligations.ObligationKind.RefinementEntry,
                                parameter.Span,
                                parameter.Name))
                        {
                            return null;
                        }

                        return EmitRefinementCondition(
                            constraint.Predicate,
                            parameter.Name);
                    })
                    .Where(check => check is not null)
                    .ToArray();
                if (initArgs.Length > 0)
                {
                    if (entryChecks.Length > 0)
                    {
                        initArgs[0] = $"({string.Join(" && ", entryChecks)}"
                            + $" ? {initArgs[0]}"
                            + " : throw new ArgumentOutOfRangeException("
                            + "\"constructor parameter\", "
                            + "\"Constructor refinement violated\"))";
                    }
                }
                else if (entryChecks.Length > 0)
                {
                    ReportConstructorRefinementInitializerNotLowered(node);
                }
                var renderedInitArgs = string.Join(", ", initArgs);
                initializerStr = node.Initializer.IsBaseCall
                    ? $" : base({renderedInitArgs})"
                    : $" : this({renderedInitArgs})";
            }

            AppendLine($"{visibility} {ctorName}({parameters}){initializerStr}");
        }
        AppendLine("{");
        Indent();

        foreach (var pre in node.Preconditions)
        {
            AppendLine(Visit(pre));
        }
        EmitRefinementParameterGuards(node.Parameters);

        foreach (var stmt in node.Body)
        {
            EmitStatement(stmt, _emissionContext);
        }

        Dedent();
        AppendLine("}");

        return "";
    }

    public string Visit(OperatorOverloadNode node)
    {
        _currentFunctionId = node.Id;
        _currentPostconditionIndex = 0;

        ResetDeclScopes(node.Parameters);

        EmitCSharpAttributes(node.CSharpAttributes);

        var returnTypeName = node.Output?.TypeName ?? "void";
        var isIterator = ContainsYieldStatements(node.Body);
        var returnShape = GetCallableReturnShape(
            returnTypeName,
            isAsync: false,
            isIterator);
        var hasReturnRefinement =
            _refinementTypes.ContainsKey(returnTypeName);
        var previousYieldRefinement = _currentYieldRefinement;
        _currentYieldRefinement = hasReturnRefinement && isIterator
            ? returnTypeName
            : null;
        _inlineReturnGuardCounter = 0;
        var parameters = string.Join(", ", node.Parameters.Select(p => Visit(p)));

        if (node.IsConversion)
        {
            // implicit/explicit operator: public static implicit operator TargetType(SourceType value)
            AppendLine($"public static {node.OperatorToken} operator {returnShape.DeclarationType}({parameters})");
        }
        else
        {
            AppendLine($"public static {returnShape.DeclarationType} operator {node.OperatorToken}({parameters})");
        }

        AppendLine("{");
        Indent();

        foreach (var pre in node.Preconditions)
        {
            AppendLine(Visit(pre));
        }
        EmitRefinementParameterGuards(node.Parameters);

        EmitCallableBody(
            node.Body,
            node.Parameters,
            Array.Empty<TypeParameterNode>(),
            node.Postconditions
                .Select(postcondition => new PostconditionEmission(postcondition))
                .ToArray(),
            $"operator {node.OperatorToken}",
            node.Span,
            returnShape,
            returnTypeName,
            isIterator);

        Dedent();
        AppendLine("}");
        _currentYieldRefinement = previousYieldRefinement;

        return "";
    }

    public string Visit(ConstructorInitializerNode node)
    {
        var args = string.Join(", ", node.Arguments.Select(a => a.Accept(this)));
        return node.IsBaseCall ? $"base({args})" : $"this({args})";
    }

    public string Visit(AssignmentStatementNode node)
    {
        if (node.Target is ArrayAccessNode indexedTarget
            && TryEmitIndexedAssignment(
                indexedTarget,
                "=",
                node.Value,
                out var indexedAssignment))
        {
            return indexedAssignment;
        }

        var target = node.Target.Accept(this);
        var value = node.Value.Accept(this);
        if (node.Target is ReferenceNode reference
            && TryGetRefinementConstraint(
                reference.Name,
                out var refinementConstraint)
            && ShouldEmitObligationGuard(
                Verification.Obligations.ObligationKind.Subtype,
                node.Span,
                reference.Name))
        {
            return EmitCheckedRefinedAssignment(
                target,
                value,
                refinementConstraint);
        }
        return $"{target} = {value};";
    }

    public string Visit(CompoundAssignmentStatementNode node)
    {
        var op = node.Operator switch
        {
            CompoundAssignmentOperator.Add => "+=",
            CompoundAssignmentOperator.Subtract => "-=",
            CompoundAssignmentOperator.Multiply => "*=",
            CompoundAssignmentOperator.Divide => "/=",
            CompoundAssignmentOperator.Modulo => "%=",
            CompoundAssignmentOperator.BitwiseAnd => "&=",
            CompoundAssignmentOperator.BitwiseOr => "|=",
            CompoundAssignmentOperator.BitwiseXor => "^=",
            CompoundAssignmentOperator.LeftShift => "<<=",
            CompoundAssignmentOperator.RightShift => ">>=",
            CompoundAssignmentOperator.NullCoalesce => "??=",
            // #774: no silent fallback to "+=" — an unmapped compound operator
            // would change the arithmetic. This switch is exhaustive over the enum.
            _ => throw new ArgumentOutOfRangeException(nameof(node),
                $"Unhandled compound assignment operator: {node.Operator}")
        };
        if (node.Target is ArrayAccessNode indexedTarget
            && TryEmitIndexedAssignment(
                indexedTarget,
                op,
                node.Value,
                out var indexedAssignment))
        {
            return indexedAssignment;
        }

        var target = node.Target.Accept(this);
        var value = node.Value.Accept(this);
        if (node.Target is ReferenceNode reference
            && TryGetRefinementConstraint(
                reference.Name,
                out var refinementConstraint)
            && ShouldEmitObligationGuard(
                Verification.Obligations.ObligationKind.Subtype,
                node.Span,
                reference.Name))
        {
            return EmitCheckedRefinedAssignment(
                target,
                value,
                refinementConstraint,
                op);
        }
        return $"{target} {op} {value};";
    }

    private string EmitCheckedRefinedAssignment(
        string target,
        string value,
        RefinementConstraint constraint,
        string? compoundOperator = null,
        bool restoreTargetOnFailure = false,
        IReadOnlyList<RefinementConstraint>? additionalConstraints = null)
    {
        var candidateName = $"__refinementCandidate{_mutationGuardCounter++}";
        var continuationIndent = Environment.NewLine
            + new string(' ', _emissionContext.IndentLevel * 4);
        var initializeCandidate = compoundOperator is null
            ? $"{MapTypeName(constraint.TypeName)} {candidateName} = {value};"
            : $"var {candidateName} = {target};"
                + continuationIndent
                + $"{candidateName} {compoundOperator} {value};";
        var constraints = new[] { constraint }
            .Concat(additionalConstraints ?? [])
            .Distinct()
            .ToArray();
        var condition = string.Join(
            " && ",
            constraints.Select(item =>
                $"({EmitRefinementCondition(item.Predicate, candidateName)})"));
        var description = string.Join(
            " and ",
            constraints.Select(item => item.Description));
        var checkedAssignment = initializeCandidate
            + continuationIndent
            + $"if (!({condition})) throw new ArgumentOutOfRangeException("
            + $"nameof({target}), \"Value violates {description}\");"
            + continuationIndent
            + $"{target} = {candidateName};";
        if (!restoreTargetOnFailure)
        {
            return checkedAssignment;
        }

        var snapshotName = $"__refinementSnapshot{_mutationGuardCounter++}";
        var nestedIndent = continuationIndent + "    ";
        var nestedAssignment = checkedAssignment.Replace(
            continuationIndent,
            nestedIndent,
            StringComparison.Ordinal);
        return $"var {snapshotName} = {target};"
            + continuationIndent
            + "try"
            + continuationIndent
            + "{"
            + nestedIndent
            + nestedAssignment
            + continuationIndent
            + "}"
            + continuationIndent
            + "catch"
            + continuationIndent
            + "{"
            + nestedIndent
            + $"{target} = {snapshotName};"
            + nestedIndent
            + "throw;"
            + continuationIndent
            + "}";
    }

    private bool TryEmitIndexedAssignment(
        ArrayAccessNode target,
        string assignmentOperator,
        ExpressionNode value,
        out string code)
    {
        if (target.Array is not ReferenceNode reference
            || !TryGetIndexedBound(reference.Name, out var logicalLength))
        {
            code = "";
            return false;
        }

        var array = target.Array.Accept(this);
        var index = target.Index.Accept(this);
        var emittedValue = value.Accept(this);
        if (IsMissingIndexedWitness(logicalLength, out var missingWitness))
        {
            code = "throw new InvalidOperationException("
                + $"\"Indexed-type size witness '{missingWitness}' is unavailable\");";
            return true;
        }
        if (!ShouldEmitObligationGuard(
            Verification.Obligations.ObligationKind.IndexBounds,
            target.Span,
            reference.Name))
        {
            code = $"{array}[{index}] {assignmentOperator} {emittedValue};";
            return true;
        }

        var guardedIndex = $"__calorIndex{_indexGuardCounter++}";
        var continuationIndent = Environment.NewLine
            + new string(' ', _emissionContext.IndentLevel * 4);
        code = $"var {guardedIndex} = {index};"
            + continuationIndent
            + $"if ({guardedIndex} < 0"
            + $" || {guardedIndex} >= {SanitizeIdentifier(logicalLength)})"
            + " throw new IndexOutOfRangeException("
            + "\"Indexed-type bound violated\");"
            + continuationIndent
            + $"{array}[{guardedIndex}] {assignmentOperator} {emittedValue};";
        return true;
    }

    public string Visit(UsingStatementNode node)
    {
        var typePart = node.VariableType != null ? MapTypeName(node.VariableType) : "var";
        var namePart = node.VariableName != null ? SanitizeIdentifier(node.VariableName) : "_";
        var resource = node.Resource.Accept(this);

        AppendLine($"using ({typePart} {namePart} = {resource})");
        AppendLine("{");
        Indent();

        PushDeclScope();
        if (node.VariableName != null)
        {
            DeclareVarInScope(namePart); // using resource binding is in scope for the body
        }
        foreach (var stmt in node.Body)
        {
            EmitStatement(stmt, _emissionContext);
        }
        PopDeclScope();

        Dedent();
        AppendLine("}");

        return "";
    }

    // Phase 10: Try/Catch/Finally

    public string Visit(TryStatementNode node)
    {
        AppendLine("try");
        AppendLine("{");
        Indent();

        PushDeclScope();
        foreach (var stmt in node.TryBody)
        {
            EmitStatement(stmt, _emissionContext);
        }
        PopDeclScope();

        Dedent();
        AppendLine("}");

        foreach (var catchClause in node.CatchClauses)
        {
            Visit(catchClause);
        }

        if (node.FinallyBody != null)
        {
            AppendLine("finally");
            AppendLine("{");
            Indent();

            PushDeclScope();
            foreach (var stmt in node.FinallyBody)
            {
                EmitStatement(stmt, _emissionContext);
            }
            PopDeclScope();

            Dedent();
            AppendLine("}");
        }

        return "";
    }

    public string Visit(CatchClauseNode node)
    {
        var catchPart = "catch";
        if (node.ExceptionType != null)
        {
            var exType = MapTypeName(node.ExceptionType);
            if (node.VariableName != null)
            {
                var varName = SanitizeIdentifier(node.VariableName);
                catchPart = $"catch ({exType} {varName})";
            }
            else
            {
                catchPart = $"catch ({exType})";
            }
        }

        if (node.Filter != null)
        {
            var filter = node.Filter.Accept(this);
            catchPart += $" when ({filter})";
        }

        AppendLine(catchPart);
        AppendLine("{");
        Indent();

        PushDeclScope();
        if (node.VariableName != null)
        {
            DeclareVarInScope(SanitizeIdentifier(node.VariableName)); // exception variable is in scope
        }
        foreach (var stmt in node.Body)
        {
            EmitStatement(stmt, _emissionContext);
        }
        PopDeclScope();

        Dedent();
        AppendLine("}");

        return "";
    }

    public string Visit(ThrowStatementNode node)
    {
        if (node.Exception != null)
        {
            var exception = node.Exception.Accept(this);
            if (node.Exception is StringLiteralNode or InterpolatedStringNode)
            {
                return $"throw new System.Exception({exception});";
            }
            if (node.Exception is IntLiteralNode or BoolLiteralNode or FloatLiteralNode or DecimalLiteralNode or CharOperationNode)
            {
                return $"throw new System.Exception({exception}.ToString());";
            }
            return $"throw {exception};";
        }
        return "throw;";
    }

    public string Visit(ThrowExpressionNode node)
    {
        var exception = node.Exception.Accept(this);
        if (node.Exception is StringLiteralNode or InterpolatedStringNode)
        {
            return $"throw new System.Exception({exception})";
        }
        if (node.Exception is IntLiteralNode or BoolLiteralNode or FloatLiteralNode or DecimalLiteralNode or CharOperationNode)
        {
            return $"throw new System.Exception({exception}.ToString())";
        }
        return $"throw {exception}";
    }

    public string Visit(RethrowStatementNode node)
    {
        return "throw;";
    }

    // Phase 11: Lambdas, Delegates, Events

    public string Visit(LambdaParameterNode node)
    {
        if (node.TypeName != null)
        {
            var mappedType = MapTypeName(node.TypeName);
            // A bare "?" with no base type (e.g., from unresolved nullable) produces invalid C#.
            // Drop the type annotation and let C# infer it.
            if (!string.IsNullOrEmpty(mappedType) && mappedType != "?")
            {
                return $"{mappedType} {SanitizeIdentifier(node.Name)}";
            }
        }
        return SanitizeIdentifier(node.Name);
    }

    public string Visit(LambdaExpressionNode node)
    {
        var staticMod = node.IsStatic ? "static " : "";
        var async = node.IsAsync ? "async " : "";
        var parameters = node.Parameters.Count switch
        {
            0 => "()",
            1 when node.Parameters[0].TypeName == null => SanitizeIdentifier(node.Parameters[0].Name),
            _ => "(" + string.Join(", ", node.Parameters.Select(p => Visit(p))) + ")"
        };
        var previousInlineReturnRefinement = _currentInlineReturnRefinement;
        var previousYieldRefinement = _currentYieldRefinement;
        var previousReturnLowering = _currentReturnLowering;
        var previousPostconditionResultShadowDepth =
            _postconditionResultShadowDepth;
        _currentInlineReturnRefinement = null;
        _currentYieldRefinement = null;
        _currentReturnLowering = null;
        if (node.Parameters.Any(parameter =>
                parameter.Name.Equals("result", StringComparison.Ordinal)))
        {
            _postconditionResultShadowDepth++;
        }

        try
        {
            if (node.IsExpressionLambda && node.ExpressionBody != null)
            {
                PushDeclScope();
                RegisterLambdaParameters(node.Parameters);
                string body;
                try
                {
                    body = node.ExpressionBody.Accept(this);
                }
                finally
                {
                    PopDeclScope();
                }
                return $"{staticMod}{async}{parameters} => {body}";
            }
            else if (node.StatementBody != null && node.StatementBody.Count > 0)
            {
                var lambdaContext = new EmissionContext(indentLevel: 1);
                PushDeclScope();
                RegisterLambdaParameters(node.Parameters);

                string body;
                try
                {
                    foreach (var stmt in node.StatementBody)
                    {
                        EmitStatement(stmt, lambdaContext);
                    }
                    body = lambdaContext.Writer.ToString().TrimEnd();
                }
                finally
                {
                    PopDeclScope();
                }

                return $"{staticMod}{async}{parameters} => {{\n{body}\n}}";
            }

            return $"{staticMod}{async}{parameters} => default";
        }
        finally
        {
            _currentInlineReturnRefinement = previousInlineReturnRefinement;
            _currentYieldRefinement = previousYieldRefinement;
            _currentReturnLowering = previousReturnLowering;
            _postconditionResultShadowDepth =
                previousPostconditionResultShadowDepth;
        }
    }

    private void RegisterLambdaParameters(
        IReadOnlyList<LambdaParameterNode> parameters)
    {
        foreach (var parameter in parameters)
        {
            var name = SanitizeIdentifier(parameter.Name);
            DeclareVarInScope(name);
            DeclareRefinementInScope(name, null);
            DeclareIndexedBoundInScope(name, null);
        }

        foreach (var parameter in parameters)
        {
            if (parameter.TypeName is null)
                continue;

            var name = SanitizeIdentifier(parameter.Name);
            if (_refinementTypes.TryGetValue(parameter.TypeName, out var refinementType))
            {
                DeclareRefinementInScope(
                    name,
                    new RefinementConstraint(
                        GetEffectiveRefinementPredicate(refinementType),
                        $"refinement type '{refinementType.Name}'",
                        ResolveRefinementBaseType(refinementType)));
            }

            var indexedTypeName = parameter.TypeName;
            var genericIndex = indexedTypeName.IndexOf('<');
            if (genericIndex > 0)
                indexedTypeName = indexedTypeName[..genericIndex];
            if (_indexedTypes.TryGetValue(indexedTypeName, out var indexedType))
            {
                var hasWitness = parameters.Any(candidate =>
                    string.Equals(
                        SanitizeIdentifier(candidate.Name),
                        SanitizeIdentifier(indexedType.SizeParam),
                        StringComparison.Ordinal));
                DeclareIndexedBoundInScope(
                    name,
                    hasWitness
                        ? indexedType.SizeParam
                        : $"missing:{indexedType.SizeParam}");
            }
        }
    }

    public string Visit(DelegateDefinitionNode node)
    {
        var name = SanitizeIdentifier(node.Name);
        var returnType = node.Output?.TypeName ?? "void";
        var mappedReturnType = MapTypeName(returnType);
        var parameters = string.Join(", ", node.Parameters.Select(p => Visit(p)));

        AppendLine($"public delegate {mappedReturnType} {name}({parameters});");
        return "";
    }

    public string Visit(EventDefinitionNode node)
    {
        var visibility = node.Visibility switch
        {
            Visibility.Public => "public",
            Visibility.ProtectedInternal => "protected internal",
            Visibility.Internal => "internal",
            Visibility.Protected => "protected",
            Visibility.Private => "private",
            _ => "public"
        };

        var eventName = SanitizeIdentifier(node.Name);
        var delegateType = MapTypeName(node.DelegateType);

        if (node.HasAccessors)
        {
            AppendLine($"{visibility} event {delegateType} {eventName}");
            AppendLine("{");
            Indent();

            if (node.AddBody != null)
            {
                ResetDeclScopes();
                AppendLine("add");
                AppendLine("{");
                Indent();
                foreach (var stmt in node.AddBody)
                {
                    EmitStatement(stmt, _emissionContext);
                }
                Dedent();
                AppendLine("}");
            }

            if (node.RemoveBody != null)
            {
                ResetDeclScopes();
                AppendLine("remove");
                AppendLine("{");
                Indent();
                foreach (var stmt in node.RemoveBody)
                {
                    EmitStatement(stmt, _emissionContext);
                }
                Dedent();
                AppendLine("}");
            }

            Dedent();
            AppendLine("}");
        }
        else
        {
            AppendLine($"{visibility} event {delegateType} {eventName};");
        }

        return "";
    }

    public string Visit(EventSubscribeNode node)
    {
        var @event = node.Event.Accept(this);
        var handler = node.Handler.Accept(this);
        return $"{@event} += {handler};";
    }

    public string Visit(EventUnsubscribeNode node)
    {
        var @event = node.Event.Accept(this);
        var handler = node.Handler.Accept(this);
        return $"{@event} -= {handler};";
    }

    // Phase 12: Async/Await

    public string Visit(AwaitExpressionNode node)
    {
        var awaited = node.Awaited.Accept(this);

        // Handle ConfigureAwait if specified
        if (node.ConfigureAwait.HasValue)
        {
            var configValue = node.ConfigureAwait.Value ? "true" : "false";
            return $"await {awaited}.ConfigureAwait({configValue})";
        }

        return $"await {awaited}";
    }

    // Phase 9: String Interpolation and Modern Operators

    public string Visit(InterpolatedStringNode node)
    {
        var sb = new StringBuilder();
        sb.Append("$\"");

        foreach (var part in node.Parts)
        {
            if (part is InterpolatedStringTextNode textPart)
            {
                // Escape quotes and braces in literal text
                var escaped = textPart.Text
                    .Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("{", "{{")
                    .Replace("}", "}}");
                sb.Append(escaped);
            }
            else if (part is InterpolatedStringExpressionNode exprPart)
            {
                sb.Append("{");
                sb.Append(exprPart.Expression.Accept(this));
                if (!string.IsNullOrEmpty(exprPart.AlignmentClause))
                {
                    sb.Append(",");
                    sb.Append(exprPart.AlignmentClause);
                }
                if (!string.IsNullOrEmpty(exprPart.FormatSpecifier))
                {
                    sb.Append(":");
                    sb.Append(exprPart.FormatSpecifier);
                }
                sb.Append("}");
            }
        }

        sb.Append("\"");
        return sb.ToString();
    }

    public string Visit(InterpolatedStringTextNode node)
    {
        // This is typically only called standalone, not as part of interpolation
        return node.Text;
    }

    public string Visit(InterpolatedStringExpressionNode node)
    {
        // This is typically only called standalone, not as part of interpolation
        var alignment = !string.IsNullOrEmpty(node.AlignmentClause) ? $",{node.AlignmentClause}" : "";
        var format = !string.IsNullOrEmpty(node.FormatSpecifier) ? $":{node.FormatSpecifier}" : "";
        return $"{node.Expression.Accept(this)}{alignment}{format}";
    }

    public string Visit(NullCoalesceNode node)
    {
        var left = node.Left.Accept(this);
        var right = node.Right.Accept(this);
        return $"{left} ?? {right}";
    }

    public string Visit(NullConditionalNode node)
    {
        var target = node.Target.Accept(this);
        return $"{target}?.{SanitizeIdentifier(node.MemberName)}";
    }

    public string Visit(RangeExpressionNode node)
    {
        var start = node.Start?.Accept(this) ?? "";
        var end = node.End?.Accept(this) ?? "";
        return $"{start}..{end}";
    }

    public string Visit(IndexFromEndNode node)
    {
        var offset = node.Offset.Accept(this);
        return $"^{offset}";
    }

    // Phase 10: Advanced Patterns

    public string Visit(WithExpressionNode node)
    {
        var target = node.Target.Accept(this);
        var assignments = string.Join(", ", node.Assignments.Select(a =>
            $"{SanitizeIdentifier(a.PropertyName)} = {a.Value.Accept(this)}"));
        return $"{target} with {{ {assignments} }}";
    }

    public string Visit(WithPropertyAssignmentNode node)
    {
        var value = node.Value.Accept(this);
        return $"{SanitizeIdentifier(node.PropertyName)} = {value}";
    }

    public string Visit(PositionalPatternNode node)
    {
        var patterns = string.Join(", ", node.Patterns.Select(p => p.Accept(this)));
        return $"{MapTypeName(node.TypeName)}({patterns})";
    }

    public string Visit(PropertyPatternNode node)
    {
        var matches = string.Join(", ", node.Matches.Select(m => m.Accept(this)));
        var typePart = string.IsNullOrEmpty(node.TypeName) ? "" : MapTypeName(node.TypeName) + " ";
        return $"{typePart}{{ {matches} }}";
    }

    public string Visit(PropertyMatchNode node)
    {
        var pattern = node.Pattern.Accept(this);
        return $"{SanitizeIdentifier(node.PropertyName)}: {pattern}";
    }

    public string Visit(RelationalPatternNode node)
    {
        var value = node.Value.Accept(this);
        var op = node.Operator.ToLowerInvariant() switch
        {
            "lt" => "<",
            "lte" => "<=",
            "gt" => ">",
            "gte" => ">=",
            "eq" => "",
            _ => node.Operator
        };
        return string.IsNullOrEmpty(op) ? value : $"{op} {value}";
    }

    public string Visit(ListPatternNode node)
    {
        var parts = new List<string>();
        var sliceText = node.SlicePattern == null ? null
            : node.SlicePattern.Name == "_" ? ".."
            : $"..{node.SlicePattern.Accept(this)}";

        for (int i = 0; i < node.Patterns.Count; i++)
        {
            if (sliceText != null && i == node.SliceIndex)
            {
                parts.Add(sliceText);
            }
            parts.Add(node.Patterns[i].Accept(this));
        }
        // Slice at end (or no non-slice patterns before it)
        if (sliceText != null && node.SliceIndex >= node.Patterns.Count)
        {
            parts.Add(sliceText);
        }
        return $"[{string.Join(", ", parts)}]";
    }

    public string Visit(VarPatternNode node)
    {
        return $"var {SanitizeIdentifier(node.Name)}";
    }

    public string Visit(ConstantPatternNode node)
    {
        return node.Value.Accept(this);
    }

    public string Visit(NegatedPatternNode node)
    {
        return $"not {EmitPattern(node.Inner)}";
    }

    public string Visit(OrPatternNode node)
    {
        return $"{EmitPattern(node.Left)} or {EmitPattern(node.Right)}";
    }

    public string Visit(AndPatternNode node)
    {
        return $"{EmitPattern(node.Left)} and {EmitPattern(node.Right)}";
    }

    #region Extended Features Visit Methods

    /// <summary>
    /// Emits Debug.Assert for an inline example/test.
    /// </summary>
    public string Visit(ExampleNode node)
    {
        var expr = node.Expression.Accept(this);
        var expected = node.Expected.Accept(this);
        var message = node.Message ?? $"Example {node.Id ?? ""}";
        return $"System.Diagnostics.Debug.Assert(object.Equals({expr}, {expected}), \"{EscapeString(message)}\");";
    }

    /// <summary>
    /// Emits a structured comment for issues (TODO, FIXME, HACK).
    /// </summary>
    public string Visit(IssueNode node)
    {
        var idPart = node.Id != null ? $"[{node.Id}]" : "";
        var categoryPart = node.Category != null ? $"({node.Category})" : "";
        var priorityPart = node.Priority != IssuePriority.Medium ? $" [{node.Priority}]" : "";
        return $"// {node.Kind.ToString().ToUpperInvariant()}{idPart}{categoryPart}{priorityPart}: {node.Description}";
    }

    /// <summary>
    /// Emits nothing for dependency nodes (used as part of Uses/UsedBy).
    /// </summary>
    public string Visit(DependencyNode node)
    {
        var versionPart = node.Version != null ? $"@{node.Version}" : "";
        var optionalPart = node.IsOptional ? "?" : "";
        return $"{node.Target}{versionPart}{optionalPart}";
    }

    /// <summary>
    /// Emits a comment for USES declarations.
    /// </summary>
    public string Visit(UsesNode node)
    {
        if (node.Dependencies.Count == 0)
            return "";

        var deps = string.Join(", ", node.Dependencies.Select(d => d.Accept(this)));
        return $"// USES: {deps}";
    }

    /// <summary>
    /// Emits a comment for USEDBY declarations.
    /// </summary>
    public string Visit(UsedByNode node)
    {
        if (node.Dependents.Count == 0 && !node.HasUnknownCallers)
            return "";

        var deps = string.Join(", ", node.Dependents.Select(d => d.Accept(this)));
        var unknownPart = node.HasUnknownCallers ? (deps.Length > 0 ? ", [external]" : "[external]") : "";
        return $"// USEDBY: {deps}{unknownPart}";
    }

    /// <summary>
    /// Emits a comment for ASSUME declarations.
    /// </summary>
    public string Visit(AssumeNode node)
    {
        var categoryPart = node.Category.HasValue ? $"[{node.Category.Value.ToString().ToLowerInvariant()}]" : "";
        return $"// ASSUME{categoryPart}: {node.Description}";
    }

    /// <summary>
    /// Emits a comment for COMPLEXITY declarations.
    /// </summary>
    public string Visit(ComplexityNode node)
    {
        var parts = new List<string>();
        var prefix = node.IsWorstCase ? "Worst-case " : "";

        if (node.TimeComplexity.HasValue)
            parts.Add($"time: {FormatComplexity(node.TimeComplexity.Value)}");
        if (node.SpaceComplexity.HasValue)
            parts.Add($"space: {FormatComplexity(node.SpaceComplexity.Value)}");
        if (node.CustomExpression != null)
            parts.Add(node.CustomExpression);

        if (parts.Count == 0)
            return "";

        return $"// COMPLEXITY: {prefix}{string.Join(", ", parts)}";
    }

    private static string FormatComplexity(ComplexityClass c)
    {
        return c switch
        {
            ComplexityClass.O1 => "O(1)",
            ComplexityClass.OLogN => "O(log n)",
            ComplexityClass.ON => "O(n)",
            ComplexityClass.ONLogN => "O(n log n)",
            ComplexityClass.ON2 => "O(n²)",
            ComplexityClass.ON3 => "O(n³)",
            ComplexityClass.O2N => "O(2ⁿ)",
            ComplexityClass.ONFact => "O(n!)",
            _ => c.ToString()
        };
    }

    /// <summary>
    /// Emits a comment for SINCE declarations.
    /// </summary>
    public string Visit(SinceNode node)
    {
        return $"// SINCE: {node.Version}";
    }

    /// <summary>
    /// Emits an [Obsolete] attribute for DEPRECATED declarations.
    /// </summary>
    public string Visit(DeprecatedNode node)
    {
        var parts = new List<string>();
        parts.Add($"Deprecated since {node.SinceVersion}");

        if (node.Replacement != null)
            parts.Add($"Use {node.Replacement} instead");
        if (node.Reason != null)
            parts.Add(node.Reason);
        if (node.RemovedInVersion != null)
            parts.Add($"Will be removed in {node.RemovedInVersion}");

        var message = string.Join(". ", parts);
        return $"[System.Obsolete(\"{EscapeString(message)}\")]";
    }

    /// <summary>
    /// Emits a comment for BREAKING declarations.
    /// </summary>
    public string Visit(BreakingChangeNode node)
    {
        return $"// BREAKING CHANGE ({node.Version}): {node.Description}";
    }

    /// <summary>
    /// Emits documentation comment for a DECISION record.
    /// </summary>
    public string Visit(DecisionNode node)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"// DECISION[{node.Id}]: {node.Title}");
        sb.AppendLine($"// Chosen: {node.ChosenOption}");
        foreach (var reason in node.ChosenReasons)
        {
            sb.AppendLine($"//   Reason: {reason}");
        }
        foreach (var rejected in node.RejectedOptions)
        {
            var rejectedText = Visit(rejected);
            foreach (var line in rejectedText.Split('\n'))
            {
                sb.AppendLine(line);
            }
        }
        if (node.Context != null)
            sb.AppendLine($"// Context: {node.Context}");
        if (node.Date.HasValue)
            sb.AppendLine($"// Date: {node.Date.Value:yyyy-MM-dd}");
        if (node.Author != null)
            sb.AppendLine($"// Author: {node.Author}");

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Emits documentation for a rejected option in a decision record.
    /// </summary>
    public string Visit(RejectedOptionNode node)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"// Rejected: {node.Name}");
        foreach (var reason in node.Reasons)
        {
            sb.AppendLine($"//   Reason: {reason}");
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Emits a comment for CONTEXT partial view markers.
    /// </summary>
    public string Visit(ContextNode node)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"// CONTEXT{(node.IsPartial ? " (partial)" : "")}");

        if (node.VisibleFiles.Count > 0)
        {
            sb.AppendLine("// Visible:");
            foreach (var file in node.VisibleFiles)
            {
                sb.AppendLine($"//   - {file.FilePath}{(file.Description != null ? $" ({file.Description})" : "")}");
            }
        }

        if (node.HiddenFiles.Count > 0)
        {
            sb.AppendLine("// Hidden:");
            foreach (var file in node.HiddenFiles)
            {
                sb.AppendLine($"//   - {file.FilePath}{(file.Description != null ? $" ({file.Description})" : "")}");
            }
        }

        if (node.FocusTarget != null)
            sb.AppendLine($"// Focus: {node.FocusTarget}");

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Emits a comment for FILE references.
    /// </summary>
    public string Visit(FileRefNode node)
    {
        var descPart = node.Description != null ? $" ({node.Description})" : "";
        return $"// FILE: {node.FilePath}{descPart}";
    }

    /// <summary>
    /// Emits a property-based test method stub.
    /// </summary>
    public string Visit(PropertyTestNode node)
    {
        var quantifiers = node.Quantifiers.Count > 0
            ? $"∀{string.Join(",", node.Quantifiers)}: "
            : "";
        var predicate = node.Predicate.Accept(this);
        return $"// PROPERTY: {quantifiers}{predicate}";
    }

    /// <summary>
    /// Emits a comment for LOCK declarations.
    /// </summary>
    public string Visit(LockNode node)
    {
        var parts = new List<string> { $"agent={node.AgentId}" };
        if (node.Acquired.HasValue)
            parts.Add($"acquired={node.Acquired.Value:O}");
        if (node.Expires.HasValue)
            parts.Add($"expires={node.Expires.Value:O}");

        return $"// LOCK: {string.Join(", ", parts)}";
    }

    /// <summary>
    /// Emits a comment for AUTHOR declarations.
    /// </summary>
    public string Visit(AuthorNode node)
    {
        var parts = new List<string> { $"agent={node.AgentId}", $"date={node.Date:yyyy-MM-dd}" };
        if (node.TaskId != null)
            parts.Add($"task={node.TaskId}");

        return $"// AUTHOR: {string.Join(", ", parts)}";
    }

    /// <summary>
    /// Emits a comment for TASK references.
    /// </summary>
    public string Visit(TaskRefNode node)
    {
        return $"// TASK[{node.TaskId}]: {node.Description}";
    }

    #endregion

    private string GetInferredTypeName(ExpressionNode expr)
    {
        return expr switch
        {
            IntLiteralNode => "int",
            FloatLiteralNode => "double",
            BoolLiteralNode => "bool",
            StringLiteralNode => "string",
            _ => "object"
        };
    }

    private string MapTypeName(string calorType)
    {
        // Check indexed type erasure: SizedList → List, NonEmptyArr → int[], etc.
        var baseTypeName = calorType;
        var genericIdx = calorType.IndexOf('<');
        var lookupName = genericIdx > 0 ? calorType.Substring(0, genericIdx) : calorType;
        if (_indexedTypeErasure.TryGetValue(lookupName, out var erasedBase))
        {
            // Preserve generic arguments: SizedList<i32> → List<int>
            baseTypeName = genericIdx > 0
                ? erasedBase + calorType.Substring(genericIdx)
                : erasedBase;
        }
        else if (_refinementTypes.TryGetValue(lookupName, out var refinementType))
        {
            baseTypeName = ResolveRefinementBaseType(refinementType);
        }

        // Use the centralized TypeMapper for bidirectional type mapping
        return TypeMapper.CalorToCSharp(baseTypeName);
    }

    private string ResolveRefinementBaseType(RefinementTypeNode refinementType)
    {
        var baseTypeName = refinementType.BaseTypeName;
        var visited = new HashSet<string>(StringComparer.Ordinal)
        {
            refinementType.Name
        };
        while (_refinementTypes.TryGetValue(baseTypeName, out var baseRefinement)
               && visited.Add(baseRefinement.Name))
        {
            baseTypeName = baseRefinement.BaseTypeName;
        }
        return baseTypeName;
    }

    private ExpressionNode GetEffectiveRefinementPredicate(
        RefinementTypeNode refinementType)
    {
        var predicates = new Stack<ExpressionNode>();
        var current = refinementType;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (visited.Add(current.Name))
        {
            predicates.Push(current.Predicate);
            if (!_refinementTypes.TryGetValue(
                    current.BaseTypeName,
                    out current))
            {
                break;
            }
        }

        var effective = predicates.Pop();
        while (predicates.Count > 0)
        {
            effective = new BinaryOperationNode(
                refinementType.Span,
                BinaryOperator.And,
                effective,
                predicates.Pop());
        }
        return effective;
    }

    /// <summary>
    /// Wraps a return type in Task for async methods.
    /// </summary>
    private static string WrapInTask(string returnType)
    {
        // Don't double-wrap types that are already Task/ValueTask
        if (returnType.StartsWith("Task<", StringComparison.Ordinal) ||
            returnType == "Task" ||
            returnType.StartsWith("ValueTask<", StringComparison.Ordinal) ||
            returnType == "ValueTask")
        {
            return returnType;
        }

        // void -> Task, T -> Task<T>
        return returnType == "void" ? "Task" : $"Task<{returnType}>";
    }

    /// <summary>
    /// Wraps a return type in IEnumerable for iterator methods.
    /// </summary>
    private static string WrapInIEnumerable(string returnType)
    {
        // Don't wrap types that are already IEnumerable/IEnumerator
        if (returnType.StartsWith("IEnumerable<", StringComparison.Ordinal) ||
            returnType == "IEnumerable" ||
            returnType.StartsWith("IEnumerator<", StringComparison.Ordinal) ||
            returnType == "IEnumerator")
        {
            return returnType;
        }

        // void -> IEnumerable, T -> IEnumerable<T>
        return returnType == "void" ? "IEnumerable" : $"IEnumerable<{returnType}>";
    }

    /// <summary>
    /// Checks whether a statement list contains any yield statements.
    /// </summary>
    private static bool ContainsYieldStatements(
        IReadOnlyList<StatementNode> statements) =>
        TraverseStatements(statements).Any(statement =>
            statement is YieldReturnStatementNode or YieldBreakStatementNode);

    private static string SanitizeIdentifier(string name)
    {
        // Handle qualified names (e.g., TimeUnit.Day) by sanitizing each part
        if (name.Contains('.'))
        {
            var parts = name.Split('.');
            return string.Join(".", parts.Select(SanitizeSingleIdentifier));
        }
        return SanitizeSingleIdentifier(name);
    }

    private static string SanitizeSingleIdentifier(string name)
    {
        // Replace any characters that aren't valid in C# identifiers
        var sb = new StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                sb.Append(c);
            }
        }

        var result = sb.ToString();

        // Ensure identifier doesn't start with a digit
        if (result.Length > 0 && char.IsDigit(result[0]))
        {
            result = "_" + result;
        }

        // Handle reserved words — prefix with @ to make valid C# identifiers.
        // Covers C# reserved keywords (ECMA-334 §6.4.4), excluding type alias
        // keywords (object, byte, char, etc.) since SanitizeIdentifier is also
        // called on type names in some contexts (NewExpression, CatchClause, etc.).
        // Exclude this/base/null/true/false — they have special meaning.
        return result switch
        {
            "abstract" or "as" or "bool" or "break" or
            "case" or "catch" or "checked" or
            "class" or "const" or "continue" or "default" or
            "delegate" or "do" or "double" or "else" or "enum" or
            "event" or "explicit" or "extern" or "finally" or "fixed" or
            "float" or "for" or "foreach" or "goto" or "if" or
            "implicit" or "in" or "int" or "interface" or "internal" or
            "is" or "lock" or "namespace" or "new" or
            "operator" or "out" or "override" or "params" or
            "private" or "protected" or "public" or "readonly" or "ref" or
            "return" or "sealed" or "sizeof" or
            "stackalloc" or "static" or "string" or "struct" or "switch" or
            "throw" or "try" or "typeof" or
            "unchecked" or "unsafe" or "using" or "virtual" or
            "void" or "volatile" or "while" or
            // Contextual keywords that conflict when used as identifiers
            "var" or "dynamic" or "yield" or "async" or "await" or
            "nameof" or "when"
            => "@" + result,
            _ => result
        };
    }

    private static string SanitizeNamespace(string name)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsLetterOrDigit(c) || c == '_' || c == '.')
            {
                sb.Append(c);
            }
        }

        var result = sb.ToString();
        if (result.Length > 0 && char.IsDigit(result[0]))
        {
            result = "_" + result;
        }
        return result;
    }

    /// <summary>
    /// Emits C#-style attributes.
    /// </summary>
    private void EmitCSharpAttributes(IReadOnlyList<CalorAttributeNode> attributes)
    {
        foreach (var attr in attributes)
        {
            AppendLine(Visit(attr));
        }
    }

    public string Visit(CalorAttributeNode node)
    {
        var targetPrefix = node.Target != null ? $"{node.Target}: " : "";
        if (node.Arguments.Count == 0)
        {
            return $"[{targetPrefix}{node.Name}]";
        }

        var args = string.Join(", ", node.Arguments.Select(FormatCSharpAttributeArgument));
        return $"[{targetPrefix}{node.Name}({args})]";
    }

    private static string FormatCSharpAttributeArgument(CalorAttributeArgument arg)
    {
        var value = arg.GetFormattedValue();

        if (arg.IsNamed)
        {
            return $"{arg.Name} = {value}";
        }
        return value;
    }

    // Quantified Contracts

    public string Visit(QuantifierVariableNode node)
    {
        // Variable nodes are handled internally by quantifier translation
        return $"{node.Name}: {MapTypeName(node.TypeName)}";
    }

    public string Visit(ForallExpressionNode node)
    {
        var previousShadowDepth = _postconditionResultShadowDepth;
        if (node.BoundVariables.Any(variable =>
                variable.Name.Equals("result", StringComparison.Ordinal)))
        {
            _postconditionResultShadowDepth++;
        }

        try
        {
            return EmitForallExpression(node);
        }
        finally
        {
            _postconditionResultShadowDepth = previousShadowDepth;
        }
    }

    private string EmitForallExpression(ForallExpressionNode node)
    {
        _usesSystemLinq = true;
        // Try to extract finite range from the pattern:
        // (forall ((i type)) (-> (&& (>= i 0) (< i n)) body))
        var range = TryExtractFiniteRange(node);
        if (range != null)
        {
            // Generate loop-based check - support multiple bound variables
            if (range.AllRanges.Count > 1)
            {
                // Generate nested Enumerable.Range().All() calls for multiple variables
                // e.g., Enumerable.Range(0, n).All(i => Enumerable.Range(0, m).All(j => body))
                var body = range.Body.Accept(this);
                var result = body;

                // Build from innermost to outermost
                for (int i = range.AllRanges.Count - 1; i >= 0; i--)
                {
                    var varRange = range.AllRanges[i];
                    var varName = SanitizeIdentifier(varRange.Name);
                    result = $"Enumerable.Range({varRange.Start}, {varRange.End} - {varRange.Start}).All({varName} => ({result}))";
                }
                return result;
            }
            else
            {
                // Single variable - original behavior
                var varName = SanitizeIdentifier(node.BoundVariables[0].Name);
                var body = range.Body.Accept(this);
                return $"Enumerable.Range({range.Start}, {range.End} - {range.Start}).All({varName} => ({body}))";
            }
        }

        // For infinite domains or unrecognized patterns, emit static-only verification
        // Supported pattern: (forall ((var type)) (-> (&& (>= var lower) (< var upper)) body))
        var boundVarsStr = string.Join(", ", node.BoundVariables.Select(bv => $"{bv.Name}: {bv.TypeName}"));
        var bodyStr = node.Body.Accept(this);
        var hint = node.Body is not ImplicationExpressionNode
            ? " [Hint: Use implication pattern (-> bounds body) for runtime checking]"
            : " [Hint: Could not extract finite bounds from antecedent]";
        return $"true /* STATIC ONLY: forall (({boundVarsStr})) - verified by Z3.{hint} */";
    }

    public string Visit(ExistsExpressionNode node)
    {
        var previousShadowDepth = _postconditionResultShadowDepth;
        if (node.BoundVariables.Any(variable =>
                variable.Name.Equals("result", StringComparison.Ordinal)))
        {
            _postconditionResultShadowDepth++;
        }

        try
        {
            return EmitExistsExpression(node);
        }
        finally
        {
            _postconditionResultShadowDepth = previousShadowDepth;
        }
    }

    private string EmitExistsExpression(ExistsExpressionNode node)
    {
        _usesSystemLinq = true;
        // Try to extract finite range from the pattern:
        // (exists ((i type)) (&& (>= i 0) (< i n) body))
        var range = TryExtractFiniteRangeForExists(node);
        if (range != null)
        {
            // Generate Any-based check - support multiple bound variables
            if (range.AllRanges.Count > 1)
            {
                // Generate nested Enumerable.Range().Any() calls for multiple variables
                // e.g., Enumerable.Range(0, n).Any(i => Enumerable.Range(0, m).Any(j => body))
                var body = range.Body.Accept(this);
                var result = body;

                // Build from innermost to outermost
                for (int i = range.AllRanges.Count - 1; i >= 0; i--)
                {
                    var varRange = range.AllRanges[i];
                    var varName = SanitizeIdentifier(varRange.Name);
                    result = $"Enumerable.Range({varRange.Start}, {varRange.End} - {varRange.Start}).Any({varName} => ({result}))";
                }
                return result;
            }
            else
            {
                // Single variable - original behavior
                var varName = SanitizeIdentifier(node.BoundVariables[0].Name);
                var body = range.Body.Accept(this);
                return $"Enumerable.Range({range.Start}, {range.End} - {range.Start}).Any({varName} => ({body}))";
            }
        }

        // For infinite domains or unrecognized patterns, emit static-only verification
        // Supported pattern: (exists ((var type)) (&& (>= var lower) (< var upper) body))
        var boundVarsStr = string.Join(", ", node.BoundVariables.Select(bv => $"{bv.Name}: {bv.TypeName}"));
        var hint = " [Hint: Use conjunction pattern (&& bounds body) for runtime checking]";
        return $"false /* STATIC ONLY: exists (({boundVarsStr})) - verified by Z3.{hint} */";
    }

    public string Visit(ImplicationExpressionNode node)
    {
        // p -> q is equivalent to !p || q
        var ante = node.Antecedent.Accept(this);
        var previousShadowDepth = _postconditionResultShadowDepth;
        if (ExpressionBindsPatternName(node.Antecedent, "result"))
        {
            _postconditionResultShadowDepth++;
        }

        try
        {
            var cons = node.Consequent.Accept(this);
            return $"(!({ante}) || ({cons}))";
        }
        finally
        {
            _postconditionResultShadowDepth = previousShadowDepth;
        }
    }

    // Native String Operations

    public string Visit(StringOperationNode node)
    {
        var args = node.Arguments.Select(a => a.Accept(this)).ToList();
        var compMode = node.ComparisonMode?.ToCSharpName();

        return node.Operation switch
        {
            // Instance methods - Query operations (with optional comparison mode)
            StringOp.Length => $"{args[0]}.Length",
            StringOp.Contains when compMode != null => $"{args[0]}.Contains({args[1]}, {compMode})",
            StringOp.Contains => $"{args[0]}.Contains({args[1]})",
            StringOp.StartsWith when compMode != null => $"{args[0]}.StartsWith({args[1]}, {compMode})",
            StringOp.StartsWith => $"{args[0]}.StartsWith({args[1]})",
            StringOp.EndsWith when compMode != null => $"{args[0]}.EndsWith({args[1]}, {compMode})",
            StringOp.EndsWith => $"{args[0]}.EndsWith({args[1]})",
            StringOp.IndexOf when compMode != null => $"{args[0]}.IndexOf({args[1]}, {compMode})",
            StringOp.IndexOf => $"{args[0]}.IndexOf({args[1]})",
            StringOp.Equals when compMode != null => $"{args[0]}.Equals({args[1]}, {compMode})",
            StringOp.Equals => $"{args[0]}.Equals({args[1]})",

            // Instance methods - Transform operations
            StringOp.Substring => $"{args[0]}.Substring({args[1]}, {args[2]})",
            StringOp.SubstringFrom => $"{args[0]}.Substring({args[1]})",
            StringOp.Replace => $"{args[0]}.Replace({args[1]}, {args[2]})",
            StringOp.ToUpper => $"{args[0]}.ToUpper()",
            StringOp.ToLower => $"{args[0]}.ToLower()",
            StringOp.Trim => $"{args[0]}.Trim()",
            StringOp.TrimStart => $"{args[0]}.TrimStart()",
            StringOp.TrimEnd => $"{args[0]}.TrimEnd()",
            StringOp.PadLeft when args.Count == 2 => $"{args[0]}.PadLeft({args[1]})",
            StringOp.PadLeft => $"{args[0]}.PadLeft({args[1]}, {args[2]})",
            StringOp.PadRight when args.Count == 2 => $"{args[0]}.PadRight({args[1]})",
            StringOp.PadRight => $"{args[0]}.PadRight({args[1]}, {args[2]})",
            StringOp.Split => $"{args[0]}.Split({args[1]})",
            StringOp.ToString => $"{args[0]}.ToString()",

            // Static methods
            StringOp.Join => $"string.Join({args[0]}, {args[1]})",
            StringOp.Format => $"string.Format({string.Join(", ", args)})",
            StringOp.Concat => $"string.Concat({string.Join(", ", args)})",
            StringOp.IsNullOrEmpty => $"string.IsNullOrEmpty({args[0]})",
            StringOp.IsNullOrWhiteSpace => $"string.IsNullOrWhiteSpace({args[0]})",

            // Regex operations
            StringOp.RegexTest => $"System.Text.RegularExpressions.Regex.IsMatch({args[0]}, {args[1]})",
            StringOp.RegexMatch => $"System.Text.RegularExpressions.Regex.Match({args[0]}, {args[1]})",
            StringOp.RegexReplace => $"System.Text.RegularExpressions.Regex.Replace({args[0]}, {args[1]}, {args[2]})",
            StringOp.RegexSplit => $"System.Text.RegularExpressions.Regex.Split({args[0]}, {args[1]})",

            _ => throw new NotSupportedException($"Unknown string operation: {node.Operation}")
        };
    }

    // Native Char Operations

    public string Visit(CharOperationNode node)
    {
        var args = node.Arguments.Select(a => a.Accept(this)).ToList();

        return node.Operation switch
        {
            // Literal
            CharOp.CharLiteral => EmitCharLiteral(args[0]),

            // Extraction
            CharOp.CharAt => $"{args[0]}[{args[1]}]",
            CharOp.CharCode => $"(int){args[0]}",
            CharOp.CharFromCode => $"(char){args[0]}",

            // Classification
            CharOp.IsLetter => $"char.IsLetter({args[0]})",
            CharOp.IsDigit => $"char.IsDigit({args[0]})",
            CharOp.IsWhiteSpace => $"char.IsWhiteSpace({args[0]})",
            CharOp.IsUpper => $"char.IsUpper({args[0]})",
            CharOp.IsLower => $"char.IsLower({args[0]})",

            // Transformation
            CharOp.ToUpperChar => $"char.ToUpper({args[0]})",
            CharOp.ToLowerChar => $"char.ToLower({args[0]})",

            _ => throw new NotSupportedException($"Unknown char operation: {node.Operation}")
        };
    }

    /// <summary>
    /// Emits a C# char literal from a string literal argument.
    /// Input is the emitted C# string (e.g., "\"Y\""), output is a char literal (e.g., "'Y'").
    /// Handles special characters that need different escaping in char vs string context.
    /// </summary>
    private static string EmitCharLiteral(string stringArg)
    {
        // Strip surrounding double quotes from the emitted string literal
        var inner = stringArg;
        if (inner.StartsWith('"') && inner.EndsWith('"'))
        {
            inner = inner[1..^1];
        }

        // Handle characters that need escaping in a char literal
        return inner switch
        {
            "'" => @"'\''",        // single quote needs escaping in char context
            "\\\\" => @"'\\'",     // already-escaped backslash stays as-is
            "\\n" => "'\\n'",      // newline
            "\\r" => "'\\r'",      // carriage return
            "\\t" => "'\\t'",      // tab
            "\\0" => "'\\0'",      // null
            _ => $"'{inner}'"      // normal character
        };
    }

    // Native Type Operations

    public string Visit(TypeOperationNode node)
    {
        var operand = node.Operand.Accept(this);
        var csharpType = MapTypeName(node.TargetType);
        return node.Operation switch
        {
            TypeOp.Cast => $"({csharpType}){operand}",
            TypeOp.Is => $"{operand} is {csharpType}",
            TypeOp.As => $"{operand} as {csharpType}",
            _ => throw new NotSupportedException($"Unknown type operation: {node.Operation}")
        };
    }

    public string Visit(IsPatternNode node)
    {
        var operand = node.Operand.Accept(this);
        var csharpType = MapTypeName(node.TargetType);
        return node.VariableName != null
            ? $"{operand} is {csharpType} {node.VariableName}"
            : $"{operand} is {csharpType}";
    }

    // Native StringBuilder Operations

    public string Visit(StringBuilderOperationNode node)
    {
        var args = node.Arguments.Select(a => a.Accept(this)).ToList();

        return node.Operation switch
        {
            // Creation
            StringBuilderOp.New when args.Count == 0 => "new System.Text.StringBuilder()",
            StringBuilderOp.New => $"new System.Text.StringBuilder({args[0]})",

            // Modification
            StringBuilderOp.Append => $"{args[0]}.Append({args[1]})",
            StringBuilderOp.AppendLine => $"{args[0]}.AppendLine({args[1]})",
            StringBuilderOp.Insert => $"{args[0]}.Insert({args[1]}, {args[2]})",
            StringBuilderOp.Remove => $"{args[0]}.Remove({args[1]}, {args[2]})",
            StringBuilderOp.Clear => $"{args[0]}.Clear()",

            // Query
            StringBuilderOp.ToString => $"{args[0]}.ToString()",
            StringBuilderOp.Length => $"{args[0]}.Length",

            _ => throw new NotSupportedException($"Unknown StringBuilder operation: {node.Operation}")
        };
    }

    // Fallback nodes for unsupported C# constructs (from C# to Calor conversion)

    public string Visit(FallbackExpressionNode node)
    {
        // Emit the original C# code with a TODO comment
        return $"/* TODO: {node.FeatureName} */ {node.OriginalCSharp}";
    }

    public string Visit(ExpressionStatementNode node)
    {
        var expr = node.Expression.Accept(this);
        if (node.Expression is UnaryOperationNode
            {
                Operator: UnaryOperator.PreIncrement
                    or UnaryOperator.PreDecrement
                    or UnaryOperator.PostIncrement
                    or UnaryOperator.PostDecrement
            })
        {
            return $"_ = {expr};";
        }

        AppendLine($"{expr};");
        return "";
    }

    public string Visit(FallbackCommentNode node)
    {
        // Emit as a comment block
        var escapedCode = node.OriginalCSharp.Replace("*/", "* /");
        AppendLine($"/* TODO: Manual conversion needed [{node.FeatureName}]");
        AppendLine($"   C#: {escapedCode}");
        if (!string.IsNullOrEmpty(node.Suggestion))
        {
            AppendLine($"   Suggestion: {node.Suggestion}");
        }
        AppendLine("*/");
        return "";
    }

    public string Visit(TypeOfExpressionNode node)
    {
        return $"typeof({MapTypeName(node.TypeName)})";
    }

    public string Visit(NameOfExpressionNode node)
    {
        return $"nameof({node.Name})";
    }

    public string Visit(ExpressionCallNode node)
    {
        var target = node.TargetExpression.Accept(this);
        var args = string.Join(", ", node.Arguments.Select(a => a.Accept(this)));
        return $"{target}({args})";
    }

    public string Visit(RawCSharpNode node)
    {
        // Emit raw C# content verbatim without applying scope indentation,
        // since the raw content has its own formatting.
        foreach (var line in node.CSharpCode.Split('\n'))
        {
            _emissionContext.Writer.AppendLine(line.TrimEnd('\r'));
        }
        return "";
    }

    public string Visit(RawCSharpExpressionNode node) => node.CSharpCode;

    public string Visit(PreprocessorDirectiveNode node)
    {
        AppendLine($"#if {node.Condition}");
        foreach (var stmt in node.Body)
        {
            EmitStatement(stmt, _emissionContext, skipEmptyLine: true);
        }
        if (node.ElseBody != null && node.ElseBody.Count > 0)
        {
            AppendLine("#else");
            foreach (var stmt in node.ElseBody)
            {
                EmitStatement(stmt, _emissionContext, skipEmptyLine: true);
            }
        }
        AppendLine("#endif");
        return "";
    }

    public string Visit(MemberPreprocessorBlockNode node)
    {
        EmitMemberPreprocessorBlock(node, isFirst: true);
        return "";
    }

    private void EmitMemberPreprocessorBlock(MemberPreprocessorBlockNode node, bool isFirst)
    {
        AppendLine(isFirst ? $"#if {node.Condition}" : $"#elif {node.Condition}");

        foreach (var field in node.Fields)
            Visit(field);
        foreach (var prop in node.Properties)
        {
            Visit(prop);
            AppendLine();
        }
        foreach (var indexer in node.Indexers)
        {
            Visit(indexer);
            AppendLine();
        }
        foreach (var ctor in node.Constructors)
        {
            Visit(ctor);
            AppendLine();
        }
        foreach (var method in node.Methods)
        {
            Visit(method);
            AppendLine();
        }
        foreach (var op in node.OperatorOverloads)
        {
            Visit(op);
            AppendLine();
        }
        foreach (var evt in node.Events)
            Visit(evt);

        if (node.ElseBranch != null)
        {
            if (!string.IsNullOrEmpty(node.ElseBranch.Condition))
            {
                EmitMemberPreprocessorBlock(node.ElseBranch, isFirst: false);
                return; // #endif already emitted by recursive call
            }
            else
            {
                AppendLine("#else");
                foreach (var field in node.ElseBranch.Fields)
                    Visit(field);
                foreach (var prop in node.ElseBranch.Properties)
                {
                    Visit(prop);
                    AppendLine();
                }
                foreach (var ctor in node.ElseBranch.Constructors)
                {
                    Visit(ctor);
                    AppendLine();
                }
                foreach (var method in node.ElseBranch.Methods)
                {
                    Visit(method);
                    AppendLine();
                }
                foreach (var op in node.ElseBranch.OperatorOverloads)
                {
                    Visit(op);
                    AppendLine();
                }
                foreach (var evt in node.ElseBranch.Events)
                    Visit(evt);
            }
        }

        AppendLine("#endif");
    }

    public string Visit(TypePreprocessorBlockNode node)
    {
        EmitTypePreprocessorBlock(node, isFirst: true);
        return "";
    }

    private void EmitTypePreprocessorBlock(TypePreprocessorBlockNode node, bool isFirst)
    {
        AppendLine(isFirst ? $"#if {node.Condition}" : $"#elif {node.Condition}");

        foreach (var u in node.Usings)
            AppendLine(Visit(u));
        foreach (var cls in node.Classes)
        {
            Visit(cls);
            AppendLine();
        }
        foreach (var iface in node.Interfaces)
        {
            Visit(iface);
            AppendLine();
        }
        foreach (var en in node.Enums)
        {
            Visit(en);
            AppendLine();
        }
        foreach (var del in node.Delegates)
        {
            Visit(del);
            AppendLine();
        }

        if (node.ElseBranch != null)
        {
            if (!string.IsNullOrEmpty(node.ElseBranch.Condition))
            {
                EmitTypePreprocessorBlock(node.ElseBranch, isFirst: false);
                return; // #endif already emitted by recursive call
            }
            else
            {
                AppendLine("#else");
                foreach (var u in node.ElseBranch.Usings)
                    AppendLine(Visit(u));
                foreach (var cls in node.ElseBranch.Classes)
                {
                    Visit(cls);
                    AppendLine();
                }
                foreach (var iface in node.ElseBranch.Interfaces)
                {
                    Visit(iface);
                    AppendLine();
                }
                foreach (var en in node.ElseBranch.Enums)
                {
                    Visit(en);
                    AppendLine();
                }
                foreach (var del in node.ElseBranch.Delegates)
                {
                    Visit(del);
                    AppendLine();
                }
            }
        }

        AppendLine("#endif");
    }

    public string Visit(CSharpInteropBlockNode node)
    {
        var code = node.CSharpCode;

        // Strip namespace declarations that match the current module namespace
        // to avoid doubled namespace wrappers when raw C# is emitted inside a module.
        if (!string.IsNullOrEmpty(_currentNamespace))
        {
            code = StripMatchingNamespace(code, _currentNamespace);
        }

        foreach (var line in code.Split('\n'))
        {
            _emissionContext.Writer.AppendLine(line.TrimEnd('\r'));
        }
        return "";
    }

    private static string StripMatchingNamespace(string code, string namespaceName)
    {
        var lines = code.Split('\n');

        // Try file-scoped namespace: "namespace Foo;"
        var fileScopedPattern = $"namespace {namespaceName};";
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimEnd('\r').Trim() == fileScopedPattern)
            {
                var result = new List<string>(lines.Length);
                for (int j = 0; j < lines.Length; j++)
                {
                    if (j != i)
                        result.Add(lines[j]);
                }
                return string.Join("\n", result);
            }
        }

        // Try block-scoped namespace: "namespace Foo {" ... "}"
        var blockPattern = $"namespace {namespaceName}";
        for (int i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimEnd('\r').Trim();
            // Match "namespace Foo {", "namespace Foo{", or "namespace Foo" (brace on next line)
            // Also handle trailing comments: "namespace Foo { // comment"
            if (!trimmed.StartsWith(blockPattern, StringComparison.Ordinal))
                continue;

            var afterPattern = trimmed.Substring(blockPattern.Length).TrimStart();
            bool hasBraceOnThisLine = afterPattern.StartsWith("{");
            if (afterPattern.Length > 0 && !hasBraceOnThisLine)
                continue; // Not a match — extra text that isn't a brace

            int openBraceLineIndex = hasBraceOnThisLine ? i : -1;
            if (openBraceLineIndex == -1)
            {
                for (int j = i + 1; j < lines.Length; j++)
                {
                    var nextTrimmed = lines[j].TrimEnd('\r').Trim();
                    if (nextTrimmed.StartsWith("{"))
                    {
                        openBraceLineIndex = j;
                        break;
                    }
                    if (!string.IsNullOrWhiteSpace(lines[j]))
                        break;
                }
            }
            if (openBraceLineIndex == -1)
                break;

            // Find the matching closing brace, skipping braces inside strings and comments
            int closeBraceLineIndex = -1;
            int braceDepth = 0;
            for (int j = openBraceLineIndex; j < lines.Length; j++)
            {
                CountBracesInLine(lines[j], ref braceDepth);
                if (braceDepth == 0)
                {
                    closeBraceLineIndex = j;
                    break;
                }
            }
            if (closeBraceLineIndex == -1)
                break;

            // Detect indentation level of inner content
            var indentSize = DetectIndentSize(lines, openBraceLineIndex + 1, closeBraceLineIndex);

            // Unwrap: keep inner content, dedented
            var result = new List<string>();
            for (int j = 0; j < i; j++)
                result.Add(lines[j]);
            for (int j = openBraceLineIndex + 1; j < closeBraceLineIndex; j++)
            {
                var line = lines[j].TrimEnd('\r');
                line = Dedent(line, indentSize);
                result.Add(line);
            }
            for (int j = closeBraceLineIndex + 1; j < lines.Length; j++)
                result.Add(lines[j]);
            return string.Join("\n", result);
        }

        return code;
    }

    /// <summary>
    /// Counts net braces in a line, skipping braces inside string literals, char literals,
    /// verbatim strings, and comments.
    /// </summary>
    private static void CountBracesInLine(string line, ref int depth)
    {
        bool inLineComment = false;
        bool inString = false;
        bool inVerbatimString = false;
        bool inChar = false;

        for (int i = 0; i < line.Length; i++)
        {
            var ch = line[i];

            if (inLineComment)
                break; // rest of line is comment

            if (inVerbatimString)
            {
                if (ch == '"')
                {
                    // "" is an escaped quote inside verbatim string
                    if (i + 1 < line.Length && line[i + 1] == '"')
                        i++;
                    else
                        inVerbatimString = false;
                }
                continue;
            }

            if (inString)
            {
                if (ch == '\\') { i++; continue; } // skip escaped char
                if (ch == '"') inString = false;
                continue;
            }

            if (inChar)
            {
                if (ch == '\\') { i++; continue; }
                if (ch == '\'') inChar = false;
                continue;
            }

            // Check for comment start
            if (ch == '/' && i + 1 < line.Length)
            {
                if (line[i + 1] == '/')
                    break; // line comment — done with this line
                // Note: block comments (/* */) spanning multiple lines are rare in
                // namespace-level code and not handled here — acceptable limitation.
            }

            if (ch == '@' && i + 1 < line.Length && line[i + 1] == '"')
            {
                inVerbatimString = true;
                i++; // skip the '"'
                continue;
            }

            if (ch == '"') { inString = true; continue; }
            if (ch == '\'') { inChar = true; continue; }

            if (ch == '{') depth++;
            else if (ch == '}') depth--;
        }
    }

    /// <summary>
    /// Detects the indentation size of the first non-empty line in a range.
    /// </summary>
    private static int DetectIndentSize(string[] lines, int start, int end)
    {
        for (int i = start; i < end; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
                continue;

            int spaces = 0;
            foreach (var ch in line)
            {
                if (ch == ' ') spaces++;
                else if (ch == '\t') spaces += 4; // treat tab as 4 spaces
                else break;
            }
            if (spaces > 0)
                return spaces;
        }
        return 4; // default
    }

    /// <summary>
    /// Removes leading indentation from a line.
    /// </summary>
    private static string Dedent(string line, int spaces)
    {
        if (string.IsNullOrWhiteSpace(line))
            return line;

        int removed = 0;
        int charIndex = 0;
        while (charIndex < line.Length && removed < spaces)
        {
            if (line[charIndex] == ' ')
            {
                removed++;
                charIndex++;
            }
            else if (line[charIndex] == '\t')
            {
                removed += 4;
                charIndex++;
            }
            else
            {
                break;
            }
        }
        return line.Substring(charIndex);
    }

    public string Visit(StackAllocNode node)
    {
        var elementType = MapTypeName(node.ElementType);
        if (node.Size != null)
        {
            var size = node.Size.Accept(this);
            return $"stackalloc {elementType}[{size}]";
        }
        else if (node.Initializer.Count > 0)
        {
            var elements = string.Join(", ", node.Initializer.Select(e => e.Accept(this)));
            return $"stackalloc {elementType}[] {{ {elements} }}";
        }
        return $"stackalloc {elementType}[0]";
    }

    public string Visit(UnsafeBlockNode node)
    {
        AppendLine("unsafe");
        AppendLine("{");
        Indent();
        PushDeclScope();
        foreach (var stmt in node.Body)
        {
            EmitStatement(stmt, _emissionContext);
        }
        PopDeclScope();
        Dedent();
        AppendLine("}");
        return "";
    }

    public string Visit(SyncBlockNode node)
    {
        var lockExpr = node.LockExpression.Accept(this);
        AppendLine($"lock ({lockExpr})");
        AppendLine("{");
        Indent();
        PushDeclScope();
        foreach (var stmt in node.Body)
        {
            EmitStatement(stmt, _emissionContext);
        }
        PopDeclScope();
        Dedent();
        AppendLine("}");
        return "";
    }

    public string Visit(FixedStatementNode node)
    {
        var pointerType = MapTypeName(node.PointerType);
        var init = node.Initializer.Accept(this);
        AppendLine($"fixed ({pointerType} {node.PointerName} = {init})");
        AppendLine("{");
        Indent();
        foreach (var stmt in node.Body)
        {
            EmitStatement(stmt, _emissionContext);
        }
        Dedent();
        AppendLine("}");
        return "";
    }

    public string Visit(AddressOfNode node)
    {
        var operand = node.Operand.Accept(this);
        return $"&{operand}";
    }

    public string Visit(PointerDereferenceNode node)
    {
        var operand = node.Operand.Accept(this);
        return $"*{operand}";
    }

    public string Visit(SizeOfNode node)
    {
        var typeName = MapTypeName(node.TypeName);
        return $"sizeof({typeName})";
    }

    public string Visit(MultiDimArrayCreationNode node)
    {
        var elementType = MapTypeName(node.ElementType);

        if (node.DimensionSizes.Count > 0)
        {
            var dims = string.Join(", ", node.DimensionSizes.Select(d => d.Accept(this)));
            return $"new {elementType}[{dims}]";
        }
        else if (node.Initializer.Count > 0)
        {
            var commas = new string(',', Math.Max(node.Rank - 1, 0));
            var rows = node.Initializer.Select(row =>
                "{ " + string.Join(", ", row.Select(e => e.Accept(this))) + " }");
            return $"new {elementType}[{commas}] {{ {string.Join(", ", rows)} }}";
        }
        else
        {
            var zeros = string.Join(", ", Enumerable.Repeat("0", node.Rank));
            return $"new {elementType}[{zeros}]";
        }
    }

    public string Visit(MultiDimArrayAccessNode node)
    {
        var array = node.Array.Accept(this);
        var indices = string.Join(", ", node.Indices.Select(i => i.Accept(this)));
        return $"{array}[{indices}]";
    }

    /// <summary>
    /// Represents a single variable's finite range.
    /// </summary>
    private sealed class VariableRange
    {
        public string Name { get; }
        public string Start { get; }
        public string End { get; }

        public VariableRange(string name, string start, string end)
        {
            Name = name;
            Start = start;
            End = end;
        }
    }

    /// <summary>
    /// Represents a finite range extracted from a quantifier expression.
    /// Supports single or multiple bound variables.
    /// </summary>
    private sealed class FiniteRange
    {
        public string Start { get; }
        public string End { get; }
        public ExpressionNode Body { get; }
        public IReadOnlyList<VariableRange> AllRanges { get; }

        public FiniteRange(string start, string end, ExpressionNode body)
            : this(start, end, body, Array.Empty<VariableRange>())
        {
        }

        public FiniteRange(string start, string end, ExpressionNode body, IReadOnlyList<VariableRange> allRanges)
        {
            Start = start;
            End = end;
            Body = body;
            AllRanges = allRanges;
        }
    }

    /// <summary>
    /// Attempts to extract finite ranges from a forall expression.
    /// Supports single and multiple bound variables.
    /// Pattern: (forall ((i type)) (-> (&& (>= i start) (< i end)) body))
    /// Pattern: (forall ((i type) (j type)) (-> (&& (>= i 0) (< i n) (>= j 0) (< j m)) body))
    /// </summary>
    private FiniteRange? TryExtractFiniteRange(ForallExpressionNode node)
    {
        // Must have at least one bound variable
        if (node.BoundVariables.Count == 0)
            return null;

        // Body should be an implication
        if (node.Body is not ImplicationExpressionNode impl)
            return null;

        // Try to extract bounds for all bound variables
        var allRanges = new List<VariableRange>();
        foreach (var boundVar in node.BoundVariables)
        {
            if (!TryExtractBounds(impl.Antecedent, boundVar.Name, out var start, out var end))
                return null;
            allRanges.Add(new VariableRange(boundVar.Name, start, end));
        }

        // The runtime body is the WHOLE implication, not just the consequent.
        //
        // The bounds mined out of the antecedent constrain the Range; they do NOT replace the
        // antecedent. Emitting only `impl.Consequent` made the runtime check a strictly STRONGER
        // proposition than the one Z3 proved: every antecedent conjunct that is not a bound on the
        // loop variable — and every bound after the first, since ExtractBound uses `??=` — was
        // silently dropped. Z3 proved `∀i. (bounds ∧ G) → P(i)`; the emitter then checked
        // `∀i ∈ [lo,hi). P(i)`. `Proven && !IsVacuous` deleted that stronger check, so a program
        // that throws under `calor run` printed cleanly under `calor run --verify`.
        //
        // Keeping the full implication is sound in both directions: inside the range the bound
        // conjuncts hold, so it reduces to the consequent; for any value a too-wide range admits
        // but the antecedent excludes, the implication is vacuously true. A too-wide range costs
        // iterations, never soundness — and `??=` can only ever widen.
        //
        // This is the seventh false-Proven-elide vector of the v0.12 cycle and the first on the
        // EMITTER side: the sort-demotion mechanism is structurally blind to it, because nothing
        // here mints a Z3 sort at all. It also mis-lowered §Q preconditions, which never elide —
        // there it was a pure false alarm, and it is fixed by the same change.
        var firstRange = allRanges[0];
        return new FiniteRange(firstRange.Start, firstRange.End, impl, allRanges);
    }

    /// <summary>
    /// Attempts to extract a finite range from an exists expression.
    /// Pattern: (exists ((i type)) (&& (>= i start) (< i end) body))
    /// Pattern: (exists ((i type) (j type)) (&& (>= i 0) (< i n) (>= j 0) (< j m) body))
    /// </summary>
    private FiniteRange? TryExtractFiniteRangeForExists(ExistsExpressionNode node)
    {
        // Must have at least one bound variable
        if (node.BoundVariables.Count == 0)
            return null;

        // Collect all conjuncts from the body
        var conjuncts = new List<ExpressionNode>();
        FlattenConjunction(node.Body, conjuncts);

        // Try to extract bounds for all bound variables
        var allRanges = new List<VariableRange>();
        var boundVarNames = node.BoundVariables.Select(bv => bv.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var boundVar in node.BoundVariables)
        {
            string? lower = null;
            string? upper = null;

            foreach (var conjunct in conjuncts)
            {
                if (conjunct is BinaryOperationNode cmp && cmp.Operator != BinaryOperator.And)
                {
                    ExtractBound(cmp, boundVar.Name, ref lower, ref upper);
                }
            }

            if (lower == null || upper == null)
                return null;

            allRanges.Add(new VariableRange(boundVar.Name, lower, upper));
        }

        // Find the body expression (conjuncts that aren't bound constraints)
        ExpressionNode? bodyExpr = null;
        foreach (var conjunct in conjuncts)
        {
            if (conjunct is BinaryOperationNode cmp && cmp.Operator != BinaryOperator.And)
            {
                // Check if this conjunct is a bound constraint for any variable
                bool isBoundConstraint = false;
                foreach (var boundVar in node.BoundVariables)
                {
                    string? testLower = null;
                    string? testUpper = null;
                    ExtractBound(cmp, boundVar.Name, ref testLower, ref testUpper);
                    if (testLower != null || testUpper != null)
                    {
                        isBoundConstraint = true;
                        break;
                    }
                }

                if (!isBoundConstraint)
                {
                    bodyExpr = conjunct;
                    break;
                }
            }
            else if (conjunct is not BinaryOperationNode)
            {
                bodyExpr = conjunct;
                break;
            }
        }

        if (bodyExpr == null)
            return null;

        var firstRange = allRanges[0];
        return new FiniteRange(firstRange.Start, firstRange.End, bodyExpr, allRanges);
    }

    /// <summary>
    /// Tries to extract bounds from a conjunction like (&& (>= i 0) (< i n))
    /// Handles arbitrarily nested ANDs.
    /// </summary>
    private bool TryExtractBounds(ExpressionNode expr, string varName, out string start, out string end)
    {
        start = "0";
        end = "0";

        string? lowerBound = null;
        string? upperBound = null;

        // Recursively collect all bound expressions from the conjunction
        ExtractBoundsRecursive(expr, varName, ref lowerBound, ref upperBound);

        if (lowerBound != null && upperBound != null)
        {
            start = lowerBound;
            end = upperBound;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Recursively extracts bounds from nested AND expressions.
    /// </summary>
    private void ExtractBoundsRecursive(ExpressionNode expr, string varName, ref string? lowerBound, ref string? upperBound)
    {
        if (expr is BinaryOperationNode binOp)
        {
            if (binOp.Operator == BinaryOperator.And)
            {
                // Recursively process both sides of AND
                ExtractBoundsRecursive(binOp.Left, varName, ref lowerBound, ref upperBound);
                ExtractBoundsRecursive(binOp.Right, varName, ref lowerBound, ref upperBound);
            }
            else
            {
                // Try to extract a bound from this comparison
                ExtractBound(binOp, varName, ref lowerBound, ref upperBound);
            }
        }
    }

    /// <summary>
    /// Tries to extract a single bound from a comparison expression like (>= i 0) or (< i n)
    /// </summary>
    private void ExtractBound(BinaryOperationNode cmp, string varName, ref string? lowerBound, ref string? upperBound)
    {
        // Check if one operand is the variable
        bool isVarLeft = cmp.Left is ReferenceNode leftRef && leftRef.Name == varName;
        bool isVarRight = cmp.Right is ReferenceNode rightRef && rightRef.Name == varName;

        if (!isVarLeft && !isVarRight)
            return;

        var otherOperand = isVarLeft ? cmp.Right : cmp.Left;
        var otherStr = otherOperand.Accept(this);

        // Determine bound type based on operator and variable position
        switch (cmp.Operator)
        {
            case BinaryOperator.GreaterOrEqual when isVarLeft:
            case BinaryOperator.LessOrEqual when isVarRight:
                // i >= start => lower bound
                lowerBound ??= otherStr;
                break;
            case BinaryOperator.GreaterThan when isVarLeft:
            case BinaryOperator.LessThan when isVarRight:
                // i > start => lower bound is start + 1
                lowerBound ??= $"({otherStr} + 1)";
                break;
            case BinaryOperator.LessThan when isVarLeft:
            case BinaryOperator.GreaterThan when isVarRight:
                // i < end => upper bound
                upperBound ??= otherStr;
                break;
            case BinaryOperator.LessOrEqual when isVarLeft:
            case BinaryOperator.GreaterOrEqual when isVarRight:
                // i <= end => upper bound is end + 1
                upperBound ??= $"({otherStr} + 1)";
                break;
        }
    }

    /// <summary>
    /// Tries to extract bounds and body from an exists conjunction like (&& (&& (>= i 0) (< i n)) body)
    /// The body is the rightmost non-bound expression in the conjunction.
    /// </summary>
    private bool TryExtractBoundsAndBody(ExpressionNode expr, string varName, out string start, out string end, out ExpressionNode? body)
    {
        start = "0";
        end = "0";
        body = null;

        // Collect all conjuncts
        var conjuncts = new List<ExpressionNode>();
        FlattenConjunction(expr, conjuncts);

        if (conjuncts.Count < 3) // Need at least lower bound, upper bound, and body
            return false;

        string? lowerBound = null;
        string? upperBound = null;
        ExpressionNode? bodyExpr = null;

        // Try to extract bounds from each conjunct
        foreach (var conjunct in conjuncts)
        {
            if (conjunct is BinaryOperationNode cmp && cmp.Operator != BinaryOperator.And)
            {
                var prevLower = lowerBound;
                var prevUpper = upperBound;
                ExtractBound(cmp, varName, ref lowerBound, ref upperBound);

                // If this conjunct didn't contribute a new bound, it's the body
                if (prevLower == lowerBound && prevUpper == upperBound)
                {
                    bodyExpr = conjunct;
                }
            }
            else if (conjunct is not BinaryOperationNode)
            {
                // Non-binary expression - this is the body
                bodyExpr = conjunct;
            }
        }

        if (lowerBound != null && upperBound != null && bodyExpr != null)
        {
            start = lowerBound;
            end = upperBound;
            body = bodyExpr;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Flattens nested AND expressions into a list of conjuncts.
    /// </summary>
    private void FlattenConjunction(ExpressionNode expr, List<ExpressionNode> conjuncts)
    {
        if (expr is BinaryOperationNode binOp && binOp.Operator == BinaryOperator.And)
        {
            FlattenConjunction(binOp.Left, conjuncts);
            FlattenConjunction(binOp.Right, conjuncts);
        }
        else
        {
            conjuncts.Add(expr);
        }
    }

    // Dependent Types: Refinement Types and Proof Obligations

    public string Visit(RefinementTypeNode node)
    {
        // Refinement types are erased in C# emission — emit nothing
        return "";
    }

    public string Visit(IndexedTypeNode node)
    {
        // Indexed types are erased in C# emission — emit nothing
        return "";
    }

    public string Visit(SelfRefNode node)
    {
        // Self-reference placeholder; only reachable inside emitted runtime checks (M1+)
        return "__self__";
    }

    public string Visit(ProofObligationNode node)
    {
        var desc = node.Description != null ? $": {node.Description}" : "";

        // Consult obligation tracker for status if available
        if (_obligationTracker != null)
        {
            var matching = _obligationTracker.Obligations
                .FirstOrDefault(o => o.Kind == Verification.Obligations.ObligationKind.ProofObligation
                    && o.SourceProofId == node.Id);

            if (matching != null)
            {
                var action = _obligationPolicy.GetAction(matching.Status);
                var requiresGuard = Verification.Obligations.ObligationPolicy.RequiresGuard(action);
                switch (matching.Status)
                {
                    // Elision is opt-in (roadmap v0.13 §2.1): a Discharged obligation
                    // drops its guard only when the caller asked for it; otherwise the
                    // verdict is diagnostic and the check stays.
                    case Verification.Obligations.ObligationStatus.Discharged
                        when ElideProvenGuards && !requiresGuard:
                        AppendLine($"// PROVEN: proof obligation [{node.Id}{desc}]");
                        return "";

                    case Verification.Obligations.ObligationStatus.Discharged:
                    case Verification.Obligations.ObligationStatus.Boundary:
                    case Verification.Obligations.ObligationStatus.Failed:
                    case Verification.Obligations.ObligationStatus.Timeout:
                    // #879 ride-along: Unsupported and Pending keep their runtime guards
                    // by design. Previously both fell through to the no-guard TODO
                    // comment — and Assumed's guard survived only because
                    // ToObligationStatus maps it onto Timeout. Pending-with-tracker is
                    // reachable (--verify-refinements with Z3 unavailable attaches the
                    // tracker without solving). An obligation not proven must keep its
                    // check (#779 posture: guards stay until verification proves); the
                    // no-guard TODO now means exactly "no tracker ran".
                    case Verification.Obligations.ObligationStatus.Unsupported:
                    case Verification.Obligations.ObligationStatus.Pending:
                        // Emit runtime guard
                        var condition = node.Condition.Accept(this);
                        AppendLine($"if (!({condition})) throw new InvalidOperationException(" +
                            $"\"Proof obligation [{node.Id}{desc}] violated\");");
                        return "";
                }
            }
        }

        // No tracker means no proof was established. Preserve executable protection.
        var fallbackCondition = node.Condition.Accept(this);
        AppendLine($"if (!({fallbackCondition})) throw new InvalidOperationException(" +
            $"\"Proof obligation [{node.Id}{desc}] violated\");");
        return "";
    }
}

/// <summary>
/// Result of validating generated C# with a full Roslyn compilation.
/// Splits pure syntax validity from semantic (compilation) validity so callers
/// can report <c>CSharpSyntaxSuccess</c> and <c>CSharpCompilationSuccess</c>
/// separately (#771).
/// </summary>
public sealed class GeneratedCSharpValidation
{
    public GeneratedCSharpValidation(
        IReadOnlyList<Microsoft.CodeAnalysis.Diagnostic> syntaxErrors,
        IReadOnlyList<Microsoft.CodeAnalysis.Diagnostic> compilationErrors)
    {
        SyntaxErrors = syntaxErrors;
        CompilationErrors = compilationErrors;
    }

    /// <summary>Parse-level error diagnostics (syntax only).</summary>
    public IReadOnlyList<Microsoft.CodeAnalysis.Diagnostic> SyntaxErrors { get; }

    /// <summary>
    /// Error diagnostics from the full semantic compilation (superset of syntax
    /// errors — a syntax-broken input is also compilation-broken).
    /// </summary>
    public IReadOnlyList<Microsoft.CodeAnalysis.Diagnostic> CompilationErrors { get; }

    /// <summary>True when the source parses with zero syntax errors.</summary>
    public bool SyntaxSuccess => SyntaxErrors.Count == 0;

    /// <summary>True when the source compiles with zero Roslyn errors.</summary>
    public bool CompilationSuccess => CompilationErrors.Count == 0;

    /// <summary>Compilation errors formatted as "CSxxxx: message (line N)".</summary>
    public IReadOnlyList<string> FormattedCompilationErrors => CompilationErrors
        .Select(FormatDiagnostic)
        .ToList();

    internal static string FormatDiagnostic(Microsoft.CodeAnalysis.Diagnostic d)
    {
        var line = d.Location.GetLineSpan().StartLinePosition.Line + 1;
        return $"{d.Id}: {d.GetMessage()} (line {line})";
    }
}

/// <summary>A generated C# source, its generated path, and its originating Calor path.</summary>
public sealed record GeneratedCSharpSource(string Text, string Path, string? SourcePath = null);

/// <summary>Project compilation inputs required to validate generated C# faithfully.</summary>
public sealed record GeneratedCSharpCompilationContext
{
    public IEnumerable<string>? ReferencePaths { get; init; }
    public IEnumerable<GeneratedCSharpSource>? AdditionalSources { get; init; }
    public bool AllowUnsafe { get; init; } = true;
    public Microsoft.CodeAnalysis.OutputKind OutputKind { get; init; } =
        Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary;
    public Microsoft.CodeAnalysis.CSharp.LanguageVersion LanguageVersion { get; init; } =
        Microsoft.CodeAnalysis.CSharp.LanguageVersion.Default;
    public IEnumerable<string>? PreprocessorSymbols { get; init; }
    public bool IncludeImplicitGlobalUsings { get; init; } = true;
    public IEnumerable<string>? AnalyzerPaths { get; init; }
    public IEnumerable<string>? AdditionalFilePaths { get; init; }
    public Microsoft.CodeAnalysis.NullableContextOptions NullableContextOptions { get; init; }
    public bool TreatWarningsAsErrors { get; init; }
    public string? AnalyzerConfigPath { get; init; }
}

/// <summary>
/// Shared Roslyn compilation helper for validating generated C# (#771/#761).
///
/// <para>Resolves the full trusted-platform-assembly reference set plus
/// <c>Calor.Runtime</c>, and supplies the implicit global usings the Calor SDK
/// provides to generated code — the same environment the production self-check
/// exemplar compile uses (<see cref="SelfCheck.ExemplarCompileChecker"/> delegates
/// here). Test helpers must use this instead of hand-rolled minimal reference
/// sets, whose failures are unassertable (missing-reference noise drowns real
/// emitter defects).</para>
/// </summary>
public static class GeneratedCSharpCompiler
{
    // Built once — enumerating the trusted-platform-assembly set is not free and
    // the reference set is constant for the process lifetime. Lazy<T> makes the
    // one-time build thread-safe under xUnit's parallel test classes.
    private static readonly Lazy<IReadOnlyList<Microsoft.CodeAnalysis.MetadataReference>> _references =
        new(BuildReferences);

    /// <summary>The reference set: all TPA assemblies plus Calor.Runtime.</summary>
    public static IReadOnlyList<Microsoft.CodeAnalysis.MetadataReference> References => _references.Value;

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("SingleFile", "IL3000",
        Justification = "Assembly.Location is checked for empty string; the reference is skipped in single-file mode.")]
    private static IReadOnlyList<Microsoft.CodeAnalysis.MetadataReference> BuildReferences()
    {
        var tpa = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "";
        var references = tpa
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(p => (Microsoft.CodeAnalysis.MetadataReference)
                Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(p))
            .ToList();

        // Belt-and-suspenders: generated C# for contract-bearing programs
        // references Calor.Runtime; ensure it is present even if TPA omitted it.
        var runtime = typeof(Calor.Runtime.ContractKind).Assembly.Location;
        if (!string.IsNullOrEmpty(runtime) &&
            !references.Any(r => (r as Microsoft.CodeAnalysis.PortableExecutableReference)?.FilePath == runtime))
        {
            references.Add(Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(runtime));
        }

        return references;
    }

    /// <summary>
    /// The implicit global usings the Calor SDK provides to generated code —
    /// mirrors Microsoft.NET.Sdk's ImplicitUsings set as of .NET 10. The emitter
    /// relies on these (it emits <c>File.ReadAllLines(...)</c> with no
    /// <c>using System.IO;</c>), so generated C# only compiles standalone when
    /// they are supplied here too.
    /// </summary>
    public const string GlobalUsingsPreamble =
        "global using System;\n" +
        "global using System.Collections.Generic;\n" +
        "global using System.IO;\n" +
        "global using System.Linq;\n" +
        "global using System.Net.Http;\n" +
        "global using System.Threading;\n" +
        "global using System.Threading.Tasks;\n";

    /// <summary>
    /// Parses and compiles one or more generated C# sources together (so
    /// cross-file references resolve) and returns split syntax/compilation
    /// error diagnostics. Warnings are ignored.
    /// </summary>
    public static GeneratedCSharpValidation Validate(params string[] csharpSources)
        => Validate(
            csharpSources.Select((source, index) =>
                new GeneratedCSharpSource(source, $"generated-{index}.g.cs")));

    /// <summary>
    /// Parses and compiles generated sources together using the process framework
    /// references plus project references supplied by the invoking surface.
    /// </summary>
    public static GeneratedCSharpValidation Validate(
        IEnumerable<GeneratedCSharpSource> csharpSources,
        IEnumerable<string>? projectReferencePaths = null)
        => Validate(
            csharpSources,
            new GeneratedCSharpCompilationContext
            {
                ReferencePaths = projectReferencePaths
            });

    /// <summary>
    /// Parses and compiles generated sources with the invoking project's source,
    /// reference, language, and unsafe-code settings.
    /// </summary>
    public static GeneratedCSharpValidation Validate(
        IEnumerable<GeneratedCSharpSource> csharpSources,
        GeneratedCSharpCompilationContext context)
    {
        var parseOptions = new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions(
            context.LanguageVersion,
            preprocessorSymbols: context.PreprocessorSymbols ?? []);

        var sourceTrees = csharpSources
            .Concat(context.AdditionalSources ?? [])
            .Select(source => Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(
                source.Text, parseOptions, source.Path))
            .ToList();

        var syntaxErrors = sourceTrees
            .SelectMany(t => t.GetDiagnostics())
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .ToList();

        var trees = new List<Microsoft.CodeAnalysis.SyntaxTree>();
        if (context.IncludeImplicitGlobalUsings)
        {
            trees.Add(Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(
                GlobalUsingsPreamble, parseOptions));
        }
        trees.AddRange(sourceTrees);

        Microsoft.CodeAnalysis.Compilation compilation =
            Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
            "GeneratedCSharpValidation",
            trees,
            BuildProjectReferences(context.ReferencePaths),
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(
                context.OutputKind,
                allowUnsafe: context.AllowUnsafe,
                nullableContextOptions: context.NullableContextOptions,
                generalDiagnosticOption: context.TreatWarningsAsErrors
                    ? Microsoft.CodeAnalysis.ReportDiagnostic.Error
                    : Microsoft.CodeAnalysis.ReportDiagnostic.Default));

        var generatorDiagnostics = RunSourceGenerators(
            ref compilation,
            parseOptions,
            context.AnalyzerPaths,
            context.AdditionalFilePaths,
            context.AnalyzerConfigPath);
        var compilationErrors = generatorDiagnostics
            .Concat(compilation.GetDiagnostics())
            .Where(d =>
                d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error ||
                context.TreatWarningsAsErrors &&
                d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .ToList();

        return new GeneratedCSharpValidation(syntaxErrors, compilationErrors);
    }

    private static IReadOnlyList<Microsoft.CodeAnalysis.Diagnostic> RunSourceGenerators(
        ref Microsoft.CodeAnalysis.Compilation compilation,
        Microsoft.CodeAnalysis.CSharp.CSharpParseOptions parseOptions,
        IEnumerable<string>? analyzerPaths,
        IEnumerable<string>? additionalFilePaths,
        string? analyzerConfigPath)
    {
        var paths = (analyzerPaths ?? [])
            .Where(File.Exists)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (paths.Count == 0)
            return [];

        var loader = new ValidationAnalyzerAssemblyLoader();
        foreach (var path in paths)
            loader.AddDependencyLocation(path);
        var generators = paths
            .Select(path => new Microsoft.CodeAnalysis.Diagnostics.AnalyzerFileReference(
                path, loader))
            .SelectMany(reference => reference.GetGenerators(
                Microsoft.CodeAnalysis.LanguageNames.CSharp))
            .ToList();
        if (generators.Count == 0)
            return [];

        var additionalTexts = (additionalFilePaths ?? [])
            .Where(File.Exists)
            .Select(path => (Microsoft.CodeAnalysis.AdditionalText)
                new FileAdditionalText(Path.GetFullPath(path)))
            .ToList();
        var driver = Microsoft.CodeAnalysis.CSharp.CSharpGeneratorDriver.Create(
            generators,
            additionalTexts,
            parseOptions,
            optionsProvider: CreateAnalyzerConfigOptionsProvider(analyzerConfigPath));
        driver.RunGeneratorsAndUpdateCompilation(
            compilation, out var updatedCompilation, out var diagnostics);
        compilation = updatedCompilation;
        return diagnostics;
    }

    private sealed class ValidationAnalyzerAssemblyLoader
        : Microsoft.CodeAnalysis.IAnalyzerAssemblyLoader
    {
        public void AddDependencyLocation(string fullPath)
        {
        }

        public System.Reflection.Assembly LoadFromPath(string fullPath)
            => System.Reflection.Assembly.LoadFrom(fullPath);
    }

    private sealed class FileAdditionalText(string path) : Microsoft.CodeAnalysis.AdditionalText
    {
        public override string Path { get; } = path;

        public override Microsoft.CodeAnalysis.Text.SourceText? GetText(
            CancellationToken cancellationToken = default)
            => Microsoft.CodeAnalysis.Text.SourceText.From(
                File.ReadAllText(Path), System.Text.Encoding.UTF8);
    }

    private static Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptionsProvider
        CreateAnalyzerConfigOptionsProvider(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return new ValidationAnalyzerConfigOptionsProvider(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), []);

        var globalValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sections = new List<AnalyzerConfigSection>();
        Dictionary<string, string>? currentValues = null;
        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
                continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                currentValues = new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);
                sections.Add(new AnalyzerConfigSection(
                    line[1..^1].Replace('\\', '/'), currentValues));
                continue;
            }

            var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || parts[0].Equals(
                    "is_global", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            (currentValues ?? globalValues)[parts[0]] = parts[1];
        }
        return new ValidationAnalyzerConfigOptionsProvider(globalValues, sections);
    }

    private sealed class ValidationAnalyzerConfigOptionsProvider(
        IReadOnlyDictionary<string, string> globalValues,
        IReadOnlyList<AnalyzerConfigSection> sections)
        : Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptionsProvider
    {
        private readonly Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions _global =
            new ValidationAnalyzerConfigOptions(globalValues);
        private static readonly Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions Empty =
            new ValidationAnalyzerConfigOptions(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        public override Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions GlobalOptions
            => _global;

        public override Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions GetOptions(
            Microsoft.CodeAnalysis.SyntaxTree tree)
            => GetPathOptions(tree.FilePath);

        public override Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions GetOptions(
            Microsoft.CodeAnalysis.AdditionalText textFile)
            => GetPathOptions(textFile.Path);

        private Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions GetPathOptions(
            string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return Empty;

            var normalizedPath = Path.GetFullPath(path).Replace('\\', '/');
            Dictionary<string, string>? values = null;
            foreach (var section in sections)
            {
                if (!Matches(section.Pattern, normalizedPath))
                    continue;
                values ??= new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);
                foreach (var pair in section.Values)
                    values[pair.Key] = pair.Value;
            }
            return values == null ? Empty : new ValidationAnalyzerConfigOptions(values);
        }

        private static bool Matches(string pattern, string normalizedPath)
        {
            var normalizedPattern = pattern.Replace('\\', '/');
            if (string.Equals(
                    normalizedPattern, normalizedPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(
                    normalizedPattern, normalizedPath, ignoreCase: true) ||
                System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(
                    normalizedPattern, Path.GetFileName(normalizedPath), ignoreCase: true);
        }
    }

    private sealed record AnalyzerConfigSection(
        string Pattern,
        IReadOnlyDictionary<string, string> Values);

    private sealed class ValidationAnalyzerConfigOptions(
        IReadOnlyDictionary<string, string> values)
        : Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value)
            => values.TryGetValue(key, out value!);
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "SingleFile",
        "IL3000",
        Justification = "Assembly.Location is checked for empty string; the embedded runtime is skipped in single-file mode.")]
    private static IReadOnlyList<Microsoft.CodeAnalysis.MetadataReference> BuildProjectReferences(
        IEnumerable<string>? projectReferencePaths)
    {
        var explicitPaths = (projectReferencePaths ?? [])
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (explicitPaths.Count == 0)
            return References;

        var explicitNames = explicitPaths
            .Select(TryGetAssemblyName)
            .Where(name => name != null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (explicitNames.Contains("System.Runtime"))
        {
            var projectReferences = explicitPaths
                .Select(path => Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(path))
                .ToList();
            var calorRuntime = typeof(Calor.Runtime.ContractKind).Assembly.Location;
            if (!string.IsNullOrEmpty(calorRuntime) &&
                !explicitNames.Contains(
                    System.Reflection.AssemblyName.GetAssemblyName(calorRuntime).Name))
            {
                projectReferences.Add(
                    Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(calorRuntime));
            }
            return projectReferences;
        }

        var references = References
            .Where(reference =>
            {
                var path = (reference as Microsoft.CodeAnalysis.PortableExecutableReference)?.FilePath;
                var name = path == null ? null : TryGetAssemblyName(path);
                return name == null || !explicitNames.Contains(name);
            })
            .ToList();
        references.AddRange(explicitPaths.Select(path =>
            Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(path)));
        return references;
    }

    private static string? TryGetAssemblyName(string path)
    {
        try
        {
            return System.Reflection.AssemblyName.GetAssemblyName(path).Name;
        }
        catch (BadImageFormatException)
        {
            return null;
        }
        catch (FileLoadException)
        {
            return null;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }
}
