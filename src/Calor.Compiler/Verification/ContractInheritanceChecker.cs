using System.Runtime.CompilerServices;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification.Z3;

namespace Calor.Compiler.Verification;

/// <summary>
/// Checks contract inheritance from interfaces to implementing classes.
/// Enforces LSP (Liskov Substitution Principle):
/// - Preconditions: implementer must be weaker or equal (cannot strengthen)
/// - Postconditions: implementer must be stronger or equal (cannot weaken)
/// </summary>
public sealed class ContractInheritanceChecker : IDisposable
{
    private readonly DiagnosticBag _diagnostics;
    private readonly Z3ImplicationProver? _z3Prover;
    private readonly bool _useZ3;
    private readonly uint _timeoutMs;
    private bool _z3UnavailableReported;
    private bool _disposed;

    public ContractInheritanceChecker(
        DiagnosticBag diagnostics,
        bool useZ3 = true,
        uint timeoutMs = VerificationOptions.DefaultTimeoutMs)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _useZ3 = useZ3 && Z3ContextFactory.IsAvailable;
        _timeoutMs = timeoutMs;

        if (_useZ3)
        {
            _z3Prover = CreateZ3Prover(_timeoutMs);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Z3ImplicationProver? CreateZ3Prover(uint timeoutMs)
    {
        try
        {
            var ctx = Z3ContextFactory.Create();
            return new Z3ImplicationProver(ctx, timeoutMs);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Checks contract inheritance for all classes in a module.
    /// </summary>
    public ModuleInheritanceResult Check(ModuleNode module)
    {
        var results = new List<ClassInheritanceResult>();
        var inheritedContracts =
            new Dictionary<MethodContractKey, InheritedContractInfo>();

        var interfaces = module.Interfaces.ToArray();
        var classes = module.Classes.ToArray();

        foreach (var classNode in module.Classes)
        {
            var classResult = CheckClass(
                classNode,
                interfaces,
                classes,
                inheritedContracts);
            results.Add(classResult);
        }

        return new ModuleInheritanceResult(results, inheritedContracts);
    }

    private ClassInheritanceResult CheckClass(
        ClassDefinitionNode classNode,
        IReadOnlyList<InterfaceDefinitionNode> interfaces,
        IReadOnlyList<ClassDefinitionNode> classes,
        Dictionary<MethodContractKey, InheritedContractInfo> inheritedContracts)
    {
        var methodResults = new List<MethodInheritanceResult>();
        var interfaceMethods = CollectInterfaceMethods(classNode, interfaces);

        foreach (var interfaceSource in interfaceMethods)
        {
            if (!classNode.Methods.Any(method =>
                    InterfaceMethodMatches(method, interfaceSource))
                && interfaceSource.Method.HasContracts)
            {
                _diagnostics.ReportError(
                    classNode.Span,
                    DiagnosticCode.InterfaceMethodNotFound,
                    $"Class '{classNode.Name}' does not implement "
                    + $"'{interfaceSource.Interface.Name}."
                    + $"{interfaceSource.Method.Name}' which has contracts");
            }
        }

        foreach (var implementingMethod in classNode.Methods)
        {
            var sources = new List<ContractSource>();
            sources.AddRange(interfaceMethods
                .Where(source => InterfaceMethodMatches(
                    implementingMethod,
                    source))
                .Select(pair => new ContractSource(
                    pair.Interface.Name,
                    pair.Method.Name,
                    pair.Method.Id,
                    pair.Method.Parameters,
                    pair.Method.Preconditions,
                    pair.Method.Postconditions,
                    pair.Method.TypeParameters
                        .Select(parameter => parameter.Name)
                        .ToArray(),
                    pair.TypeSubstitutions)));
            sources.AddRange(CollectBaseMethods(
                classNode,
                implementingMethod,
                interfaces,
                classes));

            if (sources.Count == 0)
                continue;

            methodResults.Add(CheckMethodContracts(
                classNode,
                implementingMethod,
                sources,
                inheritedContracts));
        }

        return new ClassInheritanceResult(classNode.Id, classNode.Name, methodResults);
    }

    private MethodInheritanceResult CheckMethodContracts(
        ClassDefinitionNode classNode,
        MethodNode implementingMethod,
        IReadOnlyList<ContractSource> sources,
        Dictionary<MethodContractKey, InheritedContractInfo> inheritedContracts)
    {
        var violations = new List<ContractViolation>();
        var key = MethodContractKey.Create(classNode.Name, implementingMethod);
        var orderedSources = sources
            .Select(source => RebindSourceParameters(
                source,
                implementingMethod))
            .OrderBy(source => source.TypeName, StringComparer.Ordinal)
            .ThenBy(source => source.MethodName, StringComparer.Ordinal)
            .ThenBy(source => source.MethodId, StringComparer.Ordinal)
            .ToArray();

        if (!_useZ3 && !_z3UnavailableReported)
        {
            _z3UnavailableReported = true;
            _diagnostics.ReportInfo(
                classNode.Span,
                DiagnosticCode.Z3UnavailableForInheritance,
                "Z3 SMT solver unavailable, using heuristic checking only for contract inheritance");
        }

        var combinedInherited = orderedSources.Any(source => source.HasContracts)
            ? CreateInheritedContractInfo(implementingMethod, orderedSources)
            : null;
        if (combinedInherited != null)
        {
            AddInheritedConflictViolation(
                classNode,
                implementingMethod,
                combinedInherited,
                violations);
        }

        if (!implementingMethod.HasContracts)
        {
            if (!orderedSources.Any(source => source.HasContracts))
            {
                return new MethodInheritanceResult(
                    implementingMethod.Id,
                    implementingMethod.Name,
                    ContractInheritanceStatus.NoContracts,
                    violations);
            }

            var inherited = combinedInherited!;
            inheritedContracts[key] = inherited;

            _diagnostics.ReportInfo(
                implementingMethod.Span,
                DiagnosticCode.InheritedContracts,
                $"Method '{classNode.Name}.{implementingMethod.Name}' inherits contracts from "
                + inherited.SourceDisplayName);

            return new MethodInheritanceResult(
                implementingMethod.Id,
                implementingMethod.Name,
                violations.Count == 0
                    ? ContractInheritanceStatus.Inherited
                    : ContractInheritanceStatus.Violation,
                violations);
        }

        var parameters = GetParameterList(implementingMethod.Parameters);
        var implementerPrecondition = Conjoin(
            implementingMethod.Preconditions.Select(contract => contract.Condition),
            implementingMethod.Span);
        var implementerPostcondition = Conjoin(
            implementingMethod.Postconditions.Select(contract => contract.Condition),
            implementingMethod.Span);
        var outputType = implementingMethod.Output?.TypeName;

        foreach (var source in orderedSources)
        {
            var sourcePrecondition = Conjoin(
                source.Preconditions.Select(contract => contract.Condition),
                implementingMethod.Span);
            var preconditionViolation = CheckPreconditionImplication(
                parameters,
                sourcePrecondition,
                implementerPrecondition,
                classNode,
                implementingMethod,
                source.TypeName,
                source.MethodName,
                implementingMethod.Span);
            if (preconditionViolation != null)
                violations.Add(preconditionViolation);

            var sourcePostcondition = Conjoin(
                source.Postconditions.Select(contract => contract.Condition),
                implementingMethod.Span);
            sourcePostcondition = QualifyPostcondition(
                sourcePrecondition,
                sourcePostcondition,
                implementingMethod.Span);
            var postconditionViolation = CheckPostconditionImplication(
                parameters,
                outputType,
                sourcePostcondition,
                implementerPostcondition,
                classNode,
                implementingMethod,
                source.TypeName,
                source.MethodName,
                implementingMethod.Span);
            if (postconditionViolation != null)
                violations.Add(postconditionViolation);
        }

        var status = violations.Count > 0
            ? ContractInheritanceStatus.Violation
            : ContractInheritanceStatus.Valid;
        if (status == ContractInheritanceStatus.Valid)
        {
            _diagnostics.ReportInfo(
                implementingMethod.Span,
                DiagnosticCode.ContractInheritanceValid,
                $"Contract inheritance valid for "
                + $"'{classNode.Name}.{implementingMethod.Name}'");
        }

        return new MethodInheritanceResult(
            implementingMethod.Id,
            implementingMethod.Name,
            status,
            violations);
    }

    private static IReadOnlyList<InterfaceMethodSource> CollectInterfaceMethods(
        ClassDefinitionNode classNode,
        IReadOnlyList<InterfaceDefinitionNode> interfaces)
    {
        var result = new List<InterfaceMethodSource>();
        var visited = new HashSet<string>(StringComparer.Ordinal);

        void Visit(
            string interfaceReference,
            IReadOnlyDictionary<string, string> outerSubstitutions)
        {
            var substitutedReference = SubstituteTypeName(
                interfaceReference,
                outerSubstitutions);
            if (!visited.Add(substitutedReference))
                return;

            var reference = ParseTypeReference(substitutedReference);
            var interfaceNode = interfaces.FirstOrDefault(candidate =>
                candidate.Name.Equals(reference.Name, StringComparison.Ordinal)
                && candidate.TypeParameters.Count == reference.Arguments.Count);
            if (interfaceNode == null)
            {
                return;
            }
            var substitutions = interfaceNode.TypeParameters
                .Select(parameter => parameter.Name)
                .Zip(reference.Arguments, KeyValuePair.Create)
                .ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.Ordinal);

            foreach (var baseInterface in interfaceNode.BaseInterfaces
                         .OrderBy(name => name, StringComparer.Ordinal))
            {
                Visit(baseInterface, substitutions);
            }
            foreach (var method in interfaceNode.Methods
                         .OrderBy(method => method.Name, StringComparer.Ordinal)
                         .ThenBy(method => method.Id, StringComparer.Ordinal))
            {
                result.Add(new InterfaceMethodSource(
                    interfaceNode,
                    method,
                    substitutions));
            }
        }

        foreach (var interfaceName in classNode.ImplementedInterfaces
                     .OrderBy(name => name, StringComparer.Ordinal))
        {
            Visit(
                interfaceName,
                new Dictionary<string, string>(StringComparer.Ordinal));
        }
        return result;
    }

    private static IEnumerable<ContractSource> CollectBaseMethods(
        ClassDefinitionNode classNode,
        MethodNode implementingMethod,
        IReadOnlyList<InterfaceDefinitionNode> interfaces,
        IReadOnlyList<ClassDefinitionNode> classes)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var baseClassReference = classNode.BaseClass;
        var substitutions =
            new Dictionary<string, string>(StringComparer.Ordinal);
        while (baseClassReference != null)
        {
            var substitutedReference = SubstituteTypeName(
                baseClassReference,
                substitutions);
            if (!visited.Add(substitutedReference))
                yield break;
            var reference = ParseTypeReference(substitutedReference);
            var baseClass = classes.FirstOrDefault(candidate =>
                candidate.Name.Equals(reference.Name, StringComparison.Ordinal)
                && candidate.TypeParameters.Count == reference.Arguments.Count);
            if (baseClass == null)
                yield break;
            substitutions = baseClass.TypeParameters
                .Select(parameter => parameter.Name)
                .Zip(reference.Arguments, KeyValuePair.Create)
                .ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.Ordinal);
            var baseMethod = baseClass.Methods.FirstOrDefault(method =>
                MethodsMatch(implementingMethod, method, substitutions));
            if (baseMethod != null)
            {
                if (baseMethod.HasContracts)
                {
                    yield return new ContractSource(
                        baseClass.Name,
                        baseMethod.Name,
                        baseMethod.Id,
                        baseMethod.Parameters,
                        baseMethod.Preconditions,
                        baseMethod.Postconditions,
                        baseMethod.TypeParameters
                            .Select(parameter => parameter.Name)
                            .ToArray(),
                        new Dictionary<string, string>(
                            substitutions,
                            StringComparer.Ordinal));
                }
                else
                {
                    foreach (var pair in CollectInterfaceMethods(
                                 baseClass,
                                 interfaces)
                                 .Where(pair => MethodsMatch(
                                     baseMethod,
                                     pair.Method,
                                     pair.TypeSubstitutions)))
                    {
                        yield return new ContractSource(
                            pair.Interface.Name,
                            pair.Method.Name,
                            pair.Method.Id,
                            pair.Method.Parameters,
                            pair.Method.Preconditions,
                            pair.Method.Postconditions,
                            pair.Method.TypeParameters
                                .Select(parameter => parameter.Name)
                                .ToArray(),
                            pair.TypeSubstitutions.ToDictionary(
                                substitution => substitution.Key,
                                substitution => SubstituteTypeName(
                                    substitution.Value,
                                    substitutions),
                                StringComparer.Ordinal));
                    }
                }
            }
            baseClassReference = baseClass.BaseClass;
        }
    }

    private static ContractSource RebindSourceParameters(
        ContractSource source,
        MethodNode implementation)
    {
        var implementationParameters = implementation.Parameters;
        var replacements = source.Parameters
            .Zip(
                implementationParameters,
                (sourceParameter, implementationParameter) =>
                    KeyValuePair.Create(
                        sourceParameter.Name,
                        implementationParameter.Name))
            .Where(pair =>
                !pair.Key.Equals(pair.Value, StringComparison.Ordinal))
            .ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.Ordinal);
        var typeReplacements = new Dictionary<string, string>(
            source.TypeSubstitutions,
            StringComparer.Ordinal);
        foreach (var pair in source.MethodTypeParameters
                     .Zip(
                         implementation.TypeParameters,
                         (sourceParameter, implementationParameter) =>
                             KeyValuePair.Create(
                                 sourceParameter,
                                 implementationParameter.Name)))
        {
            typeReplacements[pair.Key] = pair.Value;
        }

        return source with
        {
            Parameters = implementationParameters,
            Preconditions = source.Preconditions
                .Select(contract => new RequiresNode(
                    contract.Span,
                    RewriteReferences(
                        AvoidPatternCapture(
                            contract.Condition,
                            replacements),
                        replacements,
                        typeReplacements),
                    contract.Message,
                    contract.Attributes))
                .ToArray(),
            Postconditions = source.Postconditions
                .Select(contract => new EnsuresNode(
                    contract.Span,
                    RewriteReferences(
                        AvoidPatternCapture(
                            contract.Condition,
                            replacements),
                        replacements,
                        typeReplacements),
                    contract.Message,
                    contract.Attributes))
                .ToArray()
        };
    }

    private static ExpressionNode RewriteReferences(
        ExpressionNode expression,
        IReadOnlyDictionary<string, string> replacements,
        IReadOnlyDictionary<string, string>? typeReplacements = null)
    {
        if (expression is ReferenceNode reference)
        {
            var rewritten = RewriteReferenceName(reference.Name, replacements);
            return rewritten.Equals(reference.Name, StringComparison.Ordinal)
                ? reference
                : new ReferenceNode(reference.Span, rewritten);
        }
        if (expression is BinaryOperationNode binary)
        {
            return new BinaryOperationNode(
                binary.Span,
                binary.Operator,
                RewriteReferences(binary.Left, replacements, typeReplacements),
                RewriteReferences(binary.Right, replacements, typeReplacements));
        }
        if (expression is UnaryOperationNode unary)
        {
            return new UnaryOperationNode(
                unary.Span,
                unary.Operator,
                RewriteReferences(unary.Operand, replacements, typeReplacements));
        }
        if (expression is ConditionalExpressionNode conditional)
        {
            return new ConditionalExpressionNode(
                conditional.Span,
                RewriteReferences(conditional.Condition, replacements, typeReplacements),
                RewriteReferences(conditional.WhenTrue, replacements, typeReplacements),
                RewriteReferences(conditional.WhenFalse, replacements, typeReplacements));
        }
        if (expression is ArrayAccessNode arrayAccess)
        {
            return new ArrayAccessNode(
                arrayAccess.Span,
                RewriteReferences(arrayAccess.Array, replacements, typeReplacements),
                RewriteReferences(arrayAccess.Index, replacements, typeReplacements));
        }
        if (expression is ArrayLengthNode arrayLength)
        {
            return new ArrayLengthNode(
                arrayLength.Span,
                RewriteReferences(arrayLength.Array, replacements, typeReplacements));
        }
        if (expression is FieldAccessNode fieldAccess)
        {
            return new FieldAccessNode(
                fieldAccess.Span,
                RewriteReferences(fieldAccess.Target, replacements, typeReplacements),
                fieldAccess.FieldName,
                fieldAccess.FieldNameSpan);
        }
        if (expression is StringOperationNode stringOperation)
        {
            return new StringOperationNode(
                stringOperation.Span,
                stringOperation.Operation,
                stringOperation.Arguments
                    .Select(argument => RewriteReferences(
                        argument,
                        replacements,
                        typeReplacements))
                    .ToArray(),
                stringOperation.ComparisonMode);
        }
        if (expression is ImplicationExpressionNode implication)
        {
            return new ImplicationExpressionNode(
                implication.Span,
                RewriteReferences(implication.Antecedent, replacements, typeReplacements),
                RewriteReferences(implication.Consequent, replacements, typeReplacements));
        }
        if (expression is NullCoalesceNode coalesce)
        {
            return new NullCoalesceNode(
                coalesce.Span,
                RewriteReferences(coalesce.Left, replacements, typeReplacements),
                RewriteReferences(coalesce.Right, replacements, typeReplacements));
        }
        if (expression is NullConditionalNode conditionalAccess)
        {
            return new NullConditionalNode(
                conditionalAccess.Span,
                RewriteReferences(
                    conditionalAccess.Target,
                    replacements,
                    typeReplacements),
                conditionalAccess.MemberName);
        }
        if (expression is RangeExpressionNode range)
        {
            return new RangeExpressionNode(
                range.Span,
                range.Start == null
                    ? null
                    : RewriteReferences(range.Start, replacements, typeReplacements),
                range.End == null
                    ? null
                    : RewriteReferences(range.End, replacements, typeReplacements));
        }
        if (expression is IndexFromEndNode indexFromEnd)
        {
            return new IndexFromEndNode(
                indexFromEnd.Span,
                RewriteReferences(
                    indexFromEnd.Offset,
                    replacements,
                    typeReplacements));
        }
        if (expression is TypeOperationNode typeOperation)
        {
            return new TypeOperationNode(
                typeOperation.Span,
                typeOperation.Operation,
                RewriteReferences(
                    typeOperation.Operand,
                    replacements,
                    typeReplacements),
                RewriteTypeName(typeOperation.TargetType, typeReplacements),
                typeOperation.TargetTypeSpan);
        }
        if (expression is IsPatternNode isPattern)
        {
            return new IsPatternNode(
                isPattern.Span,
                RewriteReferences(
                    isPattern.Operand,
                    replacements,
                    typeReplacements),
                RewriteTypeName(isPattern.TargetType, typeReplacements),
                isPattern.VariableName == null
                    ? null
                    : RewriteReferenceName(
                        isPattern.VariableName,
                        replacements),
                isPattern.TargetTypeSpan);
        }
        if (expression is InterpolatedStringNode interpolated)
        {
            return new InterpolatedStringNode(
                interpolated.Span,
                interpolated.Parts.Select(part =>
                    part is InterpolatedStringExpressionNode expressionPart
                        ? new InterpolatedStringExpressionNode(
                            expressionPart.Span,
                            RewriteReferences(
                                expressionPart.Expression,
                                replacements,
                                typeReplacements),
                            expressionPart.FormatSpecifier,
                            expressionPart.AlignmentClause)
                        : part).ToArray());
        }
        if (expression is TypeOfExpressionNode typeOf)
        {
            return new TypeOfExpressionNode(
                typeOf.Span,
                RewriteTypeName(typeOf.TypeName, typeReplacements),
                typeOf.TypeNameSpan);
        }
        if (expression is SizeOfNode sizeOf)
        {
            return new SizeOfNode(
                sizeOf.Span,
                RewriteTypeName(sizeOf.TypeName, typeReplacements),
                sizeOf.TypeNameSpan);
        }
        if (expression is NameOfExpressionNode nameOf)
        {
            return new NameOfExpressionNode(
                nameOf.Span,
                RewriteReferenceName(nameOf.Name, replacements));
        }
        if (expression is SomeExpressionNode some)
        {
            return new SomeExpressionNode(
                some.Span,
                RewriteReferences(some.Value, replacements, typeReplacements));
        }
        if (expression is OkExpressionNode ok)
        {
            return new OkExpressionNode(
                ok.Span,
                RewriteReferences(ok.Value, replacements, typeReplacements));
        }
        if (expression is ErrExpressionNode err)
        {
            return new ErrExpressionNode(
                err.Span,
                RewriteReferences(err.Error, replacements, typeReplacements));
        }
        if (expression is AwaitExpressionNode awaitExpression)
        {
            return new AwaitExpressionNode(
                awaitExpression.Span,
                RewriteReferences(
                    awaitExpression.Awaited,
                    replacements,
                    typeReplacements),
                awaitExpression.ConfigureAwait);
        }
        if (expression is ThrowExpressionNode throwExpression)
        {
            return new ThrowExpressionNode(
                throwExpression.Span,
                RewriteReferences(
                    throwExpression.Exception,
                    replacements,
                    typeReplacements));
        }
        if (expression is AddressOfNode addressOf)
        {
            return new AddressOfNode(
                addressOf.Span,
                RewriteReferences(addressOf.Operand, replacements, typeReplacements));
        }
        if (expression is PointerDereferenceNode dereference)
        {
            return new PointerDereferenceNode(
                dereference.Span,
                RewriteReferences(dereference.Operand, replacements, typeReplacements));
        }
        if (expression is CollectionContainsNode contains)
        {
            return new CollectionContainsNode(
                contains.Span,
                RewriteReferenceName(contains.CollectionName, replacements),
                RewriteReferences(
                    contains.KeyOrValue,
                    replacements,
                    typeReplacements),
                contains.Mode);
        }
        if (expression is CollectionCountNode count)
        {
            return new CollectionCountNode(
                count.Span,
                RewriteReferences(count.Collection, replacements, typeReplacements));
        }
        if (expression is CharOperationNode charOperation)
        {
            return new CharOperationNode(
                charOperation.Span,
                charOperation.Operation,
                charOperation.Arguments
                    .Select(argument => RewriteReferences(
                        argument,
                        replacements,
                        typeReplacements))
                    .ToArray());
        }
        if (expression is StringBuilderOperationNode stringBuilder)
        {
            return new StringBuilderOperationNode(
                stringBuilder.Span,
                stringBuilder.Operation,
                stringBuilder.Arguments
                    .Select(argument => RewriteReferences(
                        argument,
                        replacements,
                        typeReplacements))
                    .ToArray());
        }
        if (expression is CallExpressionNode call)
        {
            return new CallExpressionNode(
                call.Span,
                RewriteReferenceName(call.Target, replacements),
                call.Arguments
                    .Select(argument => RewriteReferences(
                        argument,
                        replacements,
                        typeReplacements))
                    .ToArray(),
                call.ArgumentNames,
                call.ArgumentModifiers,
                call.TypeArguments?
                    .Select(typeArgument => RewriteTypeName(
                        typeArgument,
                        typeReplacements))
                    .ToArray(),
                call.CalleeSpan,
                call.ReceiverSpan);
        }
        if (expression is NewExpressionNode newExpression)
        {
            return new NewExpressionNode(
                newExpression.Span,
                RewriteTypeName(newExpression.TypeName, typeReplacements),
                newExpression.TypeArguments
                    .Select(typeArgument => RewriteTypeName(
                        typeArgument,
                        typeReplacements))
                    .ToArray(),
                newExpression.Arguments
                    .Select(argument => RewriteReferences(
                        argument,
                        replacements,
                        typeReplacements))
                    .ToArray(),
                newExpression.Initializers
                    .Select(initializer => new ObjectInitializerAssignment(
                        initializer.PropertyName,
                        RewriteReferences(
                            initializer.Value,
                            replacements,
                            typeReplacements)))
                    .ToArray(),
                newExpression.TypeNameSpan);
        }
        if (expression is AnonymousObjectCreationNode anonymousObject)
        {
            return new AnonymousObjectCreationNode(
                anonymousObject.Span,
                anonymousObject.Initializers
                    .Select(initializer => new ObjectInitializerAssignment(
                        initializer.PropertyName,
                        RewriteReferences(
                            initializer.Value,
                            replacements,
                            typeReplacements)))
                    .ToArray());
        }
        if (expression is ExpressionCallNode expressionCall)
        {
            return new ExpressionCallNode(
                expressionCall.Span,
                RewriteReferences(
                    expressionCall.TargetExpression,
                    replacements,
                    typeReplacements),
                expressionCall.Arguments
                    .Select(argument => RewriteReferences(
                        argument,
                        replacements,
                        typeReplacements))
                    .ToArray());
        }
        if (expression is ForallExpressionNode forall)
        {
            var (boundVariables, body) = AvoidReplacementCapture(
                forall.BoundVariables,
                forall.Body,
                replacements);
            var nested = replacements
                .Where(pair => !boundVariables.Any(variable =>
                    variable.Name.Equals(pair.Key, StringComparison.Ordinal)))
                .ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.Ordinal);
            return new ForallExpressionNode(
                forall.Span,
                RewriteQuantifierTypes(boundVariables, typeReplacements),
                RewriteReferences(body, nested, typeReplacements));
        }
        if (expression is ExistsExpressionNode exists)
        {
            var (boundVariables, body) = AvoidReplacementCapture(
                exists.BoundVariables,
                exists.Body,
                replacements);
            var nested = replacements
                .Where(pair => !boundVariables.Any(variable =>
                    variable.Name.Equals(pair.Key, StringComparison.Ordinal)))
                .ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.Ordinal);
            return new ExistsExpressionNode(
                exists.Span,
                RewriteQuantifierTypes(boundVariables, typeReplacements),
                RewriteReferences(body, nested, typeReplacements));
        }
        return expression;
    }

    private static string RewriteReferenceName(
        string name,
        IReadOnlyDictionary<string, string> replacements)
    {
        foreach (var (oldName, newName) in replacements)
        {
            if (name.Equals(oldName, StringComparison.Ordinal))
                return newName;
            if (name.StartsWith(oldName + ".", StringComparison.Ordinal))
                return newName + name[oldName.Length..];
        }
        return name;
    }

    private static ExpressionNode AvoidPatternCapture(
        ExpressionNode expression,
        IReadOnlyDictionary<string, string> replacements)
    {
        var replacementTargets = replacements.Values
            .ToHashSet(StringComparer.Ordinal);
        var patternNames = EnumerateDescendantsAndSelf(expression)
            .OfType<IsPatternNode>()
            .Select(pattern => pattern.VariableName)
            .OfType<string>()
            .Where(replacementTargets.Contains)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (patternNames.Length == 0)
            return expression;

        var usedNames = replacements.Keys
            .Concat(replacements.Values)
            .Concat(patternNames)
            .ToHashSet(StringComparer.Ordinal);
        var renames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var patternName in patternNames)
        {
            var suffix = 0;
            string freshName;
            do
            {
                freshName = $"__contract_{patternName}_{suffix++}";
            }
            while (!usedNames.Add(freshName));
            renames.Add(patternName, freshName);
        }
        return RewriteReferences(expression, renames);
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

    private static string RewriteTypeName(
        string typeName,
        IReadOnlyDictionary<string, string>? replacements) =>
        replacements == null
            ? typeName
            : SubstituteTypeName(typeName, replacements);

    private static IReadOnlyList<QuantifierVariableNode> RewriteQuantifierTypes(
        IReadOnlyList<QuantifierVariableNode> variables,
        IReadOnlyDictionary<string, string>? replacements) =>
        replacements == null || replacements.Count == 0
            ? variables
            : variables.Select(variable => new QuantifierVariableNode(
                variable.Span,
                variable.Name,
                RewriteTypeName(variable.TypeName, replacements),
                variable.IdentifierSpan,
                variable.TypeNameSpan)).ToArray();

    private static (
        IReadOnlyList<QuantifierVariableNode> Variables,
        ExpressionNode Body) AvoidReplacementCapture(
            IReadOnlyList<QuantifierVariableNode> variables,
            ExpressionNode body,
            IReadOnlyDictionary<string, string> replacements)
    {
        var replacementTargets = replacements.Values
            .ToHashSet(StringComparer.Ordinal);
        var usedNames = replacements.Keys
            .Concat(replacements.Values)
            .Concat(variables.Select(variable => variable.Name))
            .ToHashSet(StringComparer.Ordinal);
        var renames = new Dictionary<string, string>(StringComparer.Ordinal);
        var rewrittenVariables = variables
            .Select(variable =>
            {
                if (!replacementTargets.Contains(variable.Name))
                    return variable;
                var suffix = 0;
                string freshName;
                do
                {
                    freshName = $"__contract_{variable.Name}_{suffix++}";
                }
                while (!usedNames.Add(freshName));
                renames.Add(variable.Name, freshName);
                return new QuantifierVariableNode(
                    variable.Span,
                    freshName,
                    variable.TypeName,
                    variable.IdentifierSpan,
                    variable.TypeNameSpan);
            })
            .ToArray();
        return renames.Count == 0
            ? (variables, body)
            : (rewrittenVariables, RewriteReferences(body, renames));
    }

    private InheritedContractInfo CreateInheritedContractInfo(
        MethodNode implementingMethod,
        IReadOnlyList<ContractSource> sources)
    {
        var sourcePreconditions = sources
            .Select(source => Conjoin(
                source.Preconditions.Select(contract => contract.Condition),
                implementingMethod.Span))
            .ToArray();
        var effectivePrecondition = Disjoin(
            sourcePreconditions,
            implementingMethod.Span);
        var preconditions = effectivePrecondition is BoolLiteralNode { Value: true }
            ? Array.Empty<RequiresNode>()
            : new[]
            {
                new RequiresNode(
                    implementingMethod.Span,
                    effectivePrecondition,
                    null,
                    new AttributeCollection())
            };
        var postconditions = sources
            .Where(source => source.Postconditions.Count > 0)
            .Select(source =>
            {
                var sourcePrecondition = Conjoin(
                    source.Preconditions.Select(contract => contract.Condition),
                    implementingMethod.Span);
                var sourcePostcondition = Conjoin(
                    source.Postconditions.Select(contract => contract.Condition),
                    implementingMethod.Span);
                return new EnsuresNode(
                    implementingMethod.Span,
                    QualifyPostcondition(
                        sourcePrecondition,
                        sourcePostcondition,
                        implementingMethod.Span),
                    null,
                    new AttributeCollection());
            })
            .ToArray();
        return new InheritedContractInfo(
            sources.Select(source => new InheritedContractSource(
                source.TypeName,
                source.MethodName,
                source.MethodId)).ToArray(),
            preconditions,
            postconditions);
    }

    private void AddInheritedConflictViolation(
        ClassDefinitionNode classNode,
        MethodNode implementingMethod,
        InheritedContractInfo inherited,
        List<ContractViolation> violations)
    {
        if (_z3Prover == null || inherited.Postconditions.Count < 2)
            return;

        var combined = Conjoin(
            inherited.Postconditions.Select(contract => contract.Condition),
            implementingMethod.Span);
        var impossible = _z3Prover.CheckPostconditionStrengthening(
            GetParameterList(implementingMethod.Parameters),
            implementingMethod.Output?.TypeName,
            new BoolLiteralNode(implementingMethod.Span, false),
            combined);
        if (impossible.Status != ImplicationStatus.Proven)
            return;

        var violation = new ContractViolation(
            ContractViolationType.IncompatibleInheritedContracts,
            inherited.SourceDisplayName,
            implementingMethod.Name,
            "Inherited postconditions are mutually incompatible");
        violations.Add(violation);
        _diagnostics.ReportError(
            implementingMethod.Span,
            DiagnosticCode.IncompatibleInheritedContracts,
            $"Inherited contracts for '{classNode.Name}.{implementingMethod.Name}' "
            + $"are incompatible: {inherited.SourceDisplayName}");
    }

    private static ExpressionNode Conjoin(
        IEnumerable<ExpressionNode> expressions,
        TextSpan span) =>
        Combine(expressions, BinaryOperator.And, true, span);

    private static ExpressionNode Disjoin(
        IEnumerable<ExpressionNode> expressions,
        TextSpan span) =>
        Combine(expressions, BinaryOperator.Or, false, span);

    private static ExpressionNode QualifyPostcondition(
        ExpressionNode precondition,
        ExpressionNode postcondition,
        TextSpan span) =>
        precondition is BoolLiteralNode { Value: true }
            ? postcondition
            : new ImplicationExpressionNode(
                span,
                precondition,
                postcondition);

    private static ExpressionNode Combine(
        IEnumerable<ExpressionNode> expressions,
        BinaryOperator operation,
        bool identity,
        TextSpan span)
    {
        using var enumerator = expressions.GetEnumerator();
        if (!enumerator.MoveNext())
            return new BoolLiteralNode(span, identity);

        var combined = enumerator.Current;
        while (enumerator.MoveNext())
        {
            combined = new BinaryOperationNode(
                span,
                operation,
                combined,
                enumerator.Current);
        }
        return combined;
    }

    private sealed record ContractSource(
        string TypeName,
        string MethodName,
        string MethodId,
        IReadOnlyList<ParameterNode> Parameters,
        IReadOnlyList<RequiresNode> Preconditions,
        IReadOnlyList<EnsuresNode> Postconditions,
        IReadOnlyList<string> MethodTypeParameters,
        IReadOnlyDictionary<string, string> TypeSubstitutions)
    {
        public bool HasContracts =>
            Preconditions.Count > 0 || Postconditions.Count > 0;
    }

    private sealed record InterfaceMethodSource(
        InterfaceDefinitionNode Interface,
        MethodSignatureNode Method,
        IReadOnlyDictionary<string, string> TypeSubstitutions);

    private sealed record TypeReference(
        string Name,
        IReadOnlyList<string> Arguments);

    private static IReadOnlyList<(string Name, string Type)> GetParameterList(IReadOnlyList<ParameterNode> parameters)
    {
        return parameters.Select(p => (p.Name, p.TypeName)).ToList();
    }

    /// <summary>
    /// Checks if interface precondition implies implementer precondition.
    /// Returns a violation if the implication fails (implementer is stronger).
    /// </summary>
    private ContractViolation? CheckPreconditionImplication(
        IReadOnlyList<(string Name, string Type)> parameters,
        ExpressionNode interfacePrecondition,
        ExpressionNode implementerPrecondition,
        ClassDefinitionNode classNode,
        MethodNode implementingMethod,
        string sourceTypeName,
        string sourceMethodName,
        TextSpan implSpan)
    {
        // Try Z3 first if available
        if (_z3Prover != null)
        {
            var z3Result = _z3Prover.CheckPreconditionWeakening(
                parameters,
                interfacePrecondition,
                implementerPrecondition);

            switch (z3Result.Status)
            {
                case ImplicationStatus.Proven:
                    // Implication proven - no violation
                    _diagnostics.ReportInfo(
                        implSpan,
                        DiagnosticCode.ImplicationProvenByZ3,
                        $"Precondition weakening proven by Z3 for '{classNode.Name}.{implementingMethod.Name}'");
                    return null;

                case ImplicationStatus.Disproven:
                    // Implication disproven - this is a violation
                    var violation = new ContractViolation(
                        ContractViolationType.StrongerPrecondition,
                        sourceTypeName,
                        sourceMethodName,
                        $"Precondition is stronger than interface contract (LSP violation). {z3Result.CounterexampleDescription}");

                    _diagnostics.ReportError(
                        implSpan,
                        DiagnosticCode.StrongerPrecondition,
                        $"LSP violation: Precondition in '{classNode.Name}.{implementingMethod.Name}' is stronger than '{sourceTypeName}.{sourceMethodName}'. {z3Result.CounterexampleDescription}");
                    return violation;

                case ImplicationStatus.Unknown:
                    // Could not determine - fall back to heuristics
                    _diagnostics.ReportWarning(
                        implSpan,
                        DiagnosticCode.ImplicationUnknown,
                        $"Could not determine if precondition weakening is valid for '{classNode.Name}.{implementingMethod.Name}', using heuristics");
                    break;

                case ImplicationStatus.Unsupported:
                    // Unsupported constructs - fall back to heuristics silently
                    break;
            }
        }

        // Fall back to heuristic checking
        return CheckPreconditionHeuristic(
            interfacePrecondition,
            implementerPrecondition,
            classNode,
            implementingMethod,
            sourceTypeName,
            sourceMethodName,
            implSpan);
    }

    /// <summary>
    /// Checks if implementer postcondition implies interface postcondition.
    /// Returns a violation if the implication fails (implementer is weaker).
    /// </summary>
    private ContractViolation? CheckPostconditionImplication(
        IReadOnlyList<(string Name, string Type)> parameters,
        string? outputType,
        ExpressionNode interfacePostcondition,
        ExpressionNode implementerPostcondition,
        ClassDefinitionNode classNode,
        MethodNode implementingMethod,
        string sourceTypeName,
        string sourceMethodName,
        TextSpan implSpan)
    {
        // Try Z3 first if available
        if (_z3Prover != null)
        {
            var z3Result = _z3Prover.CheckPostconditionStrengthening(
                parameters,
                outputType,
                interfacePostcondition,
                implementerPostcondition);

            switch (z3Result.Status)
            {
                case ImplicationStatus.Proven:
                    // Implication proven - no violation
                    _diagnostics.ReportInfo(
                        implSpan,
                        DiagnosticCode.ImplicationProvenByZ3,
                        $"Postcondition strengthening proven by Z3 for '{classNode.Name}.{implementingMethod.Name}'");
                    return null;

                case ImplicationStatus.Disproven:
                    // Implication disproven - this is a violation
                    var violation = new ContractViolation(
                        ContractViolationType.WeakerPostcondition,
                        sourceTypeName,
                        sourceMethodName,
                        $"Postcondition is weaker than interface contract (LSP violation). {z3Result.CounterexampleDescription}");

                    _diagnostics.ReportError(
                        implSpan,
                        DiagnosticCode.WeakerPostcondition,
                        $"LSP violation: Postcondition in '{classNode.Name}.{implementingMethod.Name}' is weaker than '{sourceTypeName}.{sourceMethodName}'. {z3Result.CounterexampleDescription}");
                    return violation;

                case ImplicationStatus.Unknown:
                    // Could not determine - fall back to heuristics
                    _diagnostics.ReportWarning(
                        implSpan,
                        DiagnosticCode.ImplicationUnknown,
                        $"Could not determine if postcondition strengthening is valid for '{classNode.Name}.{implementingMethod.Name}', using heuristics");
                    break;

                case ImplicationStatus.Unsupported:
                    // Unsupported constructs - fall back to heuristics silently
                    break;
            }
        }

        // Fall back to heuristic checking
        return CheckPostconditionHeuristic(
            interfacePostcondition,
            implementerPostcondition,
            classNode,
            implementingMethod,
            sourceTypeName,
            sourceMethodName,
            implSpan);
    }

    /// <summary>
    /// Heuristic check for precondition weakening.
    /// </summary>
    private ContractViolation? CheckPreconditionHeuristic(
        ExpressionNode interfacePrecondition,
        ExpressionNode implementerPrecondition,
        ClassDefinitionNode classNode,
        MethodNode implementingMethod,
        string sourceTypeName,
        string sourceMethodName,
        TextSpan implSpan)
    {
        // Check if implementer precondition is weaker or equal (valid)
        if (IsWeakerOrEqual(implementerPrecondition, interfacePrecondition))
        {
            return null;
        }

        // Check if implementer precondition is strictly stronger (violation)
        if (IsStronger(implementerPrecondition, interfacePrecondition))
        {
            var violation = new ContractViolation(
                ContractViolationType.StrongerPrecondition,
                sourceTypeName,
                sourceMethodName,
                "Precondition is stronger than interface contract (LSP violation)");

            _diagnostics.ReportError(
                implSpan,
                DiagnosticCode.StrongerPrecondition,
                $"LSP violation: Precondition in '{classNode.Name}.{implementingMethod.Name}' is stronger than '{sourceTypeName}.{sourceMethodName}'");
            return violation;
        }

        var unknownViolation = new ContractViolation(
            ContractViolationType.StrongerPrecondition,
            sourceTypeName,
            sourceMethodName,
            "Precondition weakening could not be proven");
        _diagnostics.ReportError(
            implSpan,
            DiagnosticCode.StrongerPrecondition,
            $"LSP violation: Could not prove that precondition in "
            + $"'{classNode.Name}.{implementingMethod.Name}' is no stronger than "
            + $"'{sourceTypeName}.{sourceMethodName}'");
        return unknownViolation;
    }

    /// <summary>
    /// Heuristic check for postcondition strengthening.
    /// </summary>
    private ContractViolation? CheckPostconditionHeuristic(
        ExpressionNode interfacePostcondition,
        ExpressionNode implementerPostcondition,
        ClassDefinitionNode classNode,
        MethodNode implementingMethod,
        string sourceTypeName,
        string sourceMethodName,
        TextSpan implSpan)
    {
        // Check if implementer postcondition is stronger or equal (valid)
        if (IsStrongerOrEqual(implementerPostcondition, interfacePostcondition))
        {
            return null;
        }
        if (interfacePostcondition is ImplicationExpressionNode implication
            && IsStrongerOrEqual(
                implementerPostcondition,
                implication.Consequent))
        {
            return null;
        }

        // Check if implementer postcondition is strictly weaker (violation)
        if (IsWeaker(implementerPostcondition, interfacePostcondition))
        {
            var violation = new ContractViolation(
                ContractViolationType.WeakerPostcondition,
                sourceTypeName,
                sourceMethodName,
                "Postcondition is weaker than interface contract (LSP violation)");

            _diagnostics.ReportError(
                implSpan,
                DiagnosticCode.WeakerPostcondition,
                $"LSP violation: Postcondition in '{classNode.Name}.{implementingMethod.Name}' is weaker than '{sourceTypeName}.{sourceMethodName}'");
            return violation;
        }

        var unknownViolation = new ContractViolation(
            ContractViolationType.WeakerPostcondition,
            sourceTypeName,
            sourceMethodName,
            "Postcondition strengthening could not be proven");
        _diagnostics.ReportError(
            implSpan,
            DiagnosticCode.WeakerPostcondition,
            $"LSP violation: Could not prove that postcondition in "
            + $"'{classNode.Name}.{implementingMethod.Name}' is no weaker than "
            + $"'{sourceTypeName}.{sourceMethodName}'");
        return unknownViolation;
    }

    private static bool ParametersMatch(
        IReadOnlyList<ParameterNode> impl,
        IReadOnlyList<ParameterNode> contract,
        IReadOnlyList<TypeParameterNode> implementationTypeParameters,
        IReadOnlyList<TypeParameterNode> contractTypeParameters,
        IReadOnlyDictionary<string, string>? typeSubstitutions = null)
    {
        if (impl.Count != contract.Count)
            return false;

        for (int i = 0; i < impl.Count; i++)
        {
            var contractType = typeSubstitutions == null
                || contractTypeParameters.Any(parameter =>
                    parameter.Name.Equals(
                        contract[i].TypeName,
                        StringComparison.Ordinal))
                ? contract[i].TypeName
                : SubstituteTypeName(
                    contract[i].TypeName,
                    typeSubstitutions);
            var implementationSignature = TypeIdentity.CanonicalizeSignature(
                impl[i].TypeName,
                implementationTypeParameters.Select(parameter => parameter.Name).ToArray());
            var contractSignature = TypeIdentity.CanonicalizeSignature(
                contractType,
                contractTypeParameters.Select(parameter => parameter.Name).ToArray());
            if (impl[i].Modifier != contract[i].Modifier
                || !implementationSignature.Equals(
                    contractSignature,
                    StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static bool MethodsMatch(
        MethodNode implementation,
        MethodSignatureNode contract,
        IReadOnlyDictionary<string, string>? typeSubstitutions = null) =>
        implementation.Name.Equals(contract.Name, StringComparison.Ordinal)
        && implementation.TypeParameters.Count == contract.TypeParameters.Count
        && ParametersMatch(
            implementation.Parameters,
            contract.Parameters,
            implementation.TypeParameters,
            contract.TypeParameters,
            typeSubstitutions);

    private static bool InterfaceMethodMatches(
        MethodNode implementation,
        InterfaceMethodSource source) =>
        (implementation.Name.Equals(
             source.Method.Name,
             StringComparison.Ordinal)
         || implementation.Name.Equals(
             $"{source.Interface.Name}.{source.Method.Name}",
             StringComparison.Ordinal))
        && implementation.TypeParameters.Count
            == source.Method.TypeParameters.Count
        && ParametersMatch(
            implementation.Parameters,
            source.Method.Parameters,
            implementation.TypeParameters,
            source.Method.TypeParameters,
            source.TypeSubstitutions);

    private static bool MethodsMatch(
        MethodNode implementation,
        MethodNode contract,
        IReadOnlyDictionary<string, string>? typeSubstitutions = null) =>
        implementation.Name.Equals(contract.Name, StringComparison.Ordinal)
        && implementation.TypeParameters.Count == contract.TypeParameters.Count
        && ParametersMatch(
            implementation.Parameters,
            contract.Parameters,
            implementation.TypeParameters,
            contract.TypeParameters,
            typeSubstitutions);

    private static string SubstituteTypeName(
        string typeName,
        IReadOnlyDictionary<string, string> substitutions)
    {
        if (substitutions.TryGetValue(typeName, out var replacement))
            return replacement;

        var reference = ParseTypeReference(typeName);
        if (reference.Arguments.Count == 0)
            return typeName;
        return reference.Name
            + "<"
            + string.Join(
                ",",
                reference.Arguments.Select(argument =>
                    SubstituteTypeName(argument, substitutions)))
            + ">";
    }

    private static TypeReference ParseTypeReference(string typeName)
    {
        var genericStart = typeName.IndexOf('<');
        if (genericStart < 0 || !typeName.EndsWith('>'))
            return new TypeReference(typeName, Array.Empty<string>());

        var arguments = new List<string>();
        var argumentStart = genericStart + 1;
        var depth = 0;
        for (var index = argumentStart; index < typeName.Length - 1; index++)
        {
            switch (typeName[index])
            {
                case '<':
                    depth++;
                    break;
                case '>':
                    depth--;
                    break;
                case ',' when depth == 0:
                    arguments.Add(typeName[argumentStart..index].Trim());
                    argumentStart = index + 1;
                    break;
            }
        }
        arguments.Add(typeName[argumentStart..^1].Trim());
        return new TypeReference(
            typeName[..genericStart],
            arguments);
    }

    /// <summary>
    /// Checks if condition1 is structurally equal to condition2.
    /// </summary>
    private static bool AreStructurallyEqual(ExpressionNode expr1, ExpressionNode expr2)
    {
        if (expr1.GetType() != expr2.GetType())
            return false;

        return (expr1, expr2) switch
        {
            (BinaryOperationNode b1, BinaryOperationNode b2) =>
                b1.Operator == b2.Operator &&
                AreStructurallyEqual(b1.Left, b2.Left) &&
                AreStructurallyEqual(b1.Right, b2.Right),

            (ReferenceNode r1, ReferenceNode r2) =>
                r1.Name.Equals(r2.Name, StringComparison.Ordinal),

            (IntLiteralNode i1, IntLiteralNode i2) =>
                i1.Value == i2.Value,

            (FloatLiteralNode f1, FloatLiteralNode f2) =>
                Math.Abs(f1.Value - f2.Value) < 0.0001,

            (BoolLiteralNode b1, BoolLiteralNode b2) =>
                b1.Value == b2.Value,

            (StringLiteralNode s1, StringLiteralNode s2) =>
                s1.Value.Equals(s2.Value, StringComparison.Ordinal),

            (NoneExpressionNode, NoneExpressionNode) => true,

            _ => false
        };
    }

    /// <summary>
    /// Checks if condition1 is weaker than or equal to condition2.
    /// Without Z3, we use structural equality and simple heuristics.
    /// </summary>
    private static bool IsWeakerOrEqual(ExpressionNode condition1, ExpressionNode condition2)
    {
        // Structural equality means equal strength
        if (AreStructurallyEqual(condition1, condition2))
            return true;

        // Heuristics for common weakening patterns
        // (> x 0) weaker than (>= x 0) is FALSE - we want the opposite
        // (>= x 0) is weaker than (> x 0) because it allows more values
        if (condition1 is BinaryOperationNode b1 && condition2 is BinaryOperationNode b2)
        {
            // Check if same operands but weaker operator
            if (AreStructurallyEqual(b1.Left, b2.Left) && AreStructurallyEqual(b1.Right, b2.Right))
            {
                return IsWeakerOperator(b1.Operator, b2.Operator);
            }
        }

        // Without full SMT solving, we conservatively return false
        // (i.e., assume not weaker unless we can prove it)
        return false;
    }

    /// <summary>
    /// Checks if condition1 is stronger than or equal to condition2.
    /// </summary>
    private static bool IsStrongerOrEqual(ExpressionNode condition1, ExpressionNode condition2)
    {
        if (AreStructurallyEqual(condition1, condition2))
            return true;

        if (condition1 is BinaryOperationNode b1 && condition2 is BinaryOperationNode b2)
        {
            if (AreStructurallyEqual(b1.Left, b2.Left) && AreStructurallyEqual(b1.Right, b2.Right))
            {
                return IsStrongerOperator(b1.Operator, b2.Operator);
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if condition1 is strictly stronger than condition2.
    /// </summary>
    private static bool IsStronger(ExpressionNode condition1, ExpressionNode condition2)
    {
        if (AreStructurallyEqual(condition1, condition2))
            return false; // Equal, not stronger

        if (condition1 is BinaryOperationNode b1 && condition2 is BinaryOperationNode b2)
        {
            if (AreStructurallyEqual(b1.Left, b2.Left) && AreStructurallyEqual(b1.Right, b2.Right))
            {
                return IsStrongerOperator(b1.Operator, b2.Operator);
            }
        }

        // Without SMT, we can't determine - conservatively return false
        return false;
    }

    /// <summary>
    /// Checks if condition1 is strictly weaker than condition2.
    /// </summary>
    private static bool IsWeaker(ExpressionNode condition1, ExpressionNode condition2)
    {
        if (AreStructurallyEqual(condition1, condition2))
            return false; // Equal, not weaker

        if (condition1 is BinaryOperationNode b1 && condition2 is BinaryOperationNode b2)
        {
            if (AreStructurallyEqual(b1.Left, b2.Left) && AreStructurallyEqual(b1.Right, b2.Right))
            {
                return IsWeakerOperator(b1.Operator, b2.Operator);
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if op1 is a weaker comparison operator than op2.
    /// Weaker means it allows more values to pass.
    /// </summary>
    private static bool IsWeakerOperator(BinaryOperator op1, BinaryOperator op2)
    {
        // >= is weaker than > (allows equal values)
        // <= is weaker than < (allows equal values)
        // != is weaker than == (allows more values)
        return (op1, op2) switch
        {
            (BinaryOperator.GreaterOrEqual, BinaryOperator.GreaterThan) => true,
            (BinaryOperator.LessOrEqual, BinaryOperator.LessThan) => true,
            (BinaryOperator.NotEqual, BinaryOperator.Equal) => true,
            _ => false
        };
    }

    /// <summary>
    /// Checks if op1 is a stronger comparison operator than op2.
    /// Stronger means it allows fewer values to pass.
    /// </summary>
    private static bool IsStrongerOperator(BinaryOperator op1, BinaryOperator op2)
    {
        return (op1, op2) switch
        {
            (BinaryOperator.GreaterThan, BinaryOperator.GreaterOrEqual) => true,
            (BinaryOperator.LessThan, BinaryOperator.LessOrEqual) => true,
            (BinaryOperator.Equal, BinaryOperator.NotEqual) => true,
            _ => false
        };
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _z3Prover?.Dispose();
            _disposed = true;
        }
    }
}

/// <summary>
/// Status of contract inheritance checking for a method.
/// </summary>
public enum ContractInheritanceStatus
{
    /// <summary>
    /// No contracts involved.
    /// </summary>
    NoContracts,

    /// <summary>
    /// Contracts inherited from interface (method has no explicit contracts).
    /// </summary>
    Inherited,

    /// <summary>
    /// Contract inheritance is valid (LSP compliant).
    /// </summary>
    Valid,

    /// <summary>
    /// Contract inheritance violates LSP.
    /// </summary>
    Violation
}

/// <summary>
/// Type of contract violation.
/// </summary>
public enum ContractViolationType
{
    /// <summary>
    /// Implementer has a stronger precondition than the interface.
    /// </summary>
    StrongerPrecondition,

    /// <summary>
    /// Implementer has a weaker postcondition than the interface.
    /// </summary>
    WeakerPostcondition,

    /// <summary>
    /// Multiple inherited guarantees cannot all hold.
    /// </summary>
    IncompatibleInheritedContracts
}

/// <summary>
/// Represents a contract violation.
/// </summary>
public sealed class ContractViolation
{
    public ContractViolationType Type { get; }
    public string InterfaceName { get; }
    public string MethodName { get; }
    public string Description { get; }

    public ContractViolation(
        ContractViolationType type,
        string interfaceName,
        string methodName,
        string description)
    {
        Type = type;
        InterfaceName = interfaceName;
        MethodName = methodName;
        Description = description;
    }
}

/// <summary>
/// Information about contracts inherited from an interface.
/// </summary>
public sealed class InheritedContractInfo
{
    public IReadOnlyList<InheritedContractSource> Sources { get; }
    public string InterfaceName => Sources[0].TypeName;
    public string MethodName => Sources[0].MethodName;
    public string SourceDisplayName => string.Join(
        ", ",
        Sources.Select(source => $"{source.TypeName}.{source.MethodName}"));
    public IReadOnlyList<RequiresNode> Preconditions { get; }
    public IReadOnlyList<EnsuresNode> Postconditions { get; }

    public InheritedContractInfo(
        IReadOnlyList<InheritedContractSource> sources,
        IReadOnlyList<RequiresNode> preconditions,
        IReadOnlyList<EnsuresNode> postconditions)
    {
        if (sources == null || sources.Count == 0)
            throw new ArgumentException(
                "At least one inherited contract source is required",
                nameof(sources));
        Sources = sources;
        Preconditions = preconditions;
        Postconditions = postconditions;
    }
}

public sealed record InheritedContractSource(
    string TypeName,
    string MethodName,
    string MethodId);

public sealed record MethodContractKey(
    string ClassName,
    string MethodName,
    int GenericArity,
    string ParameterSignature)
{
    public static MethodContractKey Create(
        string className,
        MethodNode method) =>
        new(
            className,
            method.Name,
            method.TypeParameters.Count,
            string.Join(
                "\u001f",
                method.Parameters.Select(parameter =>
                    $"{(int)parameter.Modifier}:{parameter.TypeName}")));
}

/// <summary>
/// Result of checking method contract inheritance.
/// </summary>
public sealed class MethodInheritanceResult
{
    public string MethodId { get; }
    public string MethodName { get; }
    public ContractInheritanceStatus Status { get; }
    public IReadOnlyList<ContractViolation> Violations { get; }

    public MethodInheritanceResult(
        string methodId,
        string methodName,
        ContractInheritanceStatus status,
        IReadOnlyList<ContractViolation> violations)
    {
        MethodId = methodId;
        MethodName = methodName;
        Status = status;
        Violations = violations;
    }
}

/// <summary>
/// Result of checking class contract inheritance.
/// </summary>
public sealed class ClassInheritanceResult
{
    public string ClassId { get; }
    public string ClassName { get; }
    public IReadOnlyList<MethodInheritanceResult> Methods { get; }

    public ClassInheritanceResult(
        string classId,
        string className,
        IReadOnlyList<MethodInheritanceResult> methods)
    {
        ClassId = classId;
        ClassName = className;
        Methods = methods;
    }

    public bool HasViolations => Methods.Any(m => m.Status == ContractInheritanceStatus.Violation);
}

/// <summary>
/// Result of checking module contract inheritance.
/// </summary>
public sealed class ModuleInheritanceResult
{
    public IReadOnlyList<ClassInheritanceResult> Classes { get; }

    /// <summary>
    /// Mapping of resolved class method signatures to inherited contracts.
    /// Used by the emitter to emit inherited contract checks.
    /// </summary>
    public IReadOnlyDictionary<MethodContractKey, InheritedContractInfo> InheritedContracts { get; }

    public ModuleInheritanceResult(
        IReadOnlyList<ClassInheritanceResult> classes,
        IReadOnlyDictionary<MethodContractKey, InheritedContractInfo> inheritedContracts)
    {
        Classes = classes;
        InheritedContracts = inheritedContracts;
    }

    public bool HasViolations => Classes.Any(c => c.HasViolations);

    /// <summary>
    /// Gets inherited contracts for a specific method, if any.
    /// </summary>
    public InheritedContractInfo? GetInheritedContracts(string className, string methodName)
    {
        var matches = InheritedContracts
            .Where(pair =>
                pair.Key.ClassName.Equals(className, StringComparison.Ordinal)
                && pair.Key.MethodName.Equals(methodName, StringComparison.Ordinal))
            .Select(pair => pair.Value)
            .Take(2)
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    public InheritedContractInfo? GetInheritedContracts(
        string className,
        MethodNode method) =>
        InheritedContracts.TryGetValue(
            MethodContractKey.Create(className, method),
            out var info)
            ? info
            : null;
}
