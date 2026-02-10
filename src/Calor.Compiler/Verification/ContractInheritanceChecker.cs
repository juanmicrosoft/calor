using System.Runtime.CompilerServices;
using Calor.Compiler.Ast;
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
    private bool _z3UnavailableReported;
    private bool _disposed;

    public ContractInheritanceChecker(DiagnosticBag diagnostics, bool useZ3 = true)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _useZ3 = useZ3 && Z3ContextFactory.IsAvailable;

        if (_useZ3)
        {
            _z3Prover = CreateZ3Prover();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Z3ImplicationProver? CreateZ3Prover()
    {
        try
        {
            var ctx = Z3ContextFactory.Create();
            return new Z3ImplicationProver(ctx);
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
        var inheritedContracts = new Dictionary<(string ClassName, string MethodName), InheritedContractInfo>();

        // Build a lookup of interfaces by name
        var interfaces = module.Interfaces.ToDictionary(i => i.Name, StringComparer.Ordinal);

        foreach (var classNode in module.Classes)
        {
            var classResult = CheckClass(classNode, interfaces, inheritedContracts);
            results.Add(classResult);
        }

        return new ModuleInheritanceResult(results, inheritedContracts);
    }

    private ClassInheritanceResult CheckClass(
        ClassDefinitionNode classNode,
        Dictionary<string, InterfaceDefinitionNode> interfaces,
        Dictionary<(string ClassName, string MethodName), InheritedContractInfo> inheritedContracts)
    {
        var methodResults = new List<MethodInheritanceResult>();

        foreach (var interfaceName in classNode.ImplementedInterfaces)
        {
            if (!interfaces.TryGetValue(interfaceName, out var interfaceNode))
            {
                // Interface not defined in this module - skip (could be external)
                continue;
            }

            foreach (var interfaceMethod in interfaceNode.Methods)
            {
                // Find the implementing method
                var implementingMethod = classNode.Methods.FirstOrDefault(m =>
                    m.Name.Equals(interfaceMethod.Name, StringComparison.Ordinal) &&
                    ParametersMatch(m.Parameters, interfaceMethod.Parameters));

                if (implementingMethod == null)
                {
                    // Method not implemented - warn if interface has contracts
                    if (interfaceMethod.HasContracts)
                    {
                        _diagnostics.ReportWarning(
                            classNode.Span,
                            DiagnosticCode.InterfaceMethodNotFound,
                            $"Class '{classNode.Name}' does not implement '{interfaceName}.{interfaceMethod.Name}' which has contracts");
                    }
                    continue;
                }

                var result = CheckMethodContracts(
                    classNode, implementingMethod, interfaceNode, interfaceMethod, inheritedContracts);
                methodResults.Add(result);
            }
        }

        return new ClassInheritanceResult(classNode.Id, classNode.Name, methodResults);
    }

    private MethodInheritanceResult CheckMethodContracts(
        ClassDefinitionNode classNode,
        MethodNode implementingMethod,
        InterfaceDefinitionNode interfaceNode,
        MethodSignatureNode interfaceMethod,
        Dictionary<(string ClassName, string MethodName), InheritedContractInfo> inheritedContracts)
    {
        var violations = new List<ContractViolation>();
        var key = (classNode.Name, implementingMethod.Name);

        // Report Z3 unavailable once per check session
        if (!_useZ3 && !_z3UnavailableReported)
        {
            _z3UnavailableReported = true;
            _diagnostics.ReportInfo(
                classNode.Span,
                DiagnosticCode.Z3UnavailableForInheritance,
                "Z3 SMT solver unavailable, using heuristic checking only for contract inheritance");
        }

        // Case 1: Interface has contracts, implementer has none → inherit
        if (interfaceMethod.HasContracts && !implementingMethod.HasContracts)
        {
            inheritedContracts[key] = new InheritedContractInfo(
                interfaceNode.Name,
                interfaceMethod.Name,
                interfaceMethod.Preconditions,
                interfaceMethod.Postconditions);

            _diagnostics.ReportInfo(
                implementingMethod.Span,
                DiagnosticCode.InheritedContracts,
                $"Method '{classNode.Name}.{implementingMethod.Name}' inherits contracts from '{interfaceNode.Name}.{interfaceMethod.Name}'");

            return new MethodInheritanceResult(
                implementingMethod.Id,
                implementingMethod.Name,
                ContractInheritanceStatus.Inherited,
                violations);
        }

        // Case 2: Both have contracts → check LSP compliance
        if (interfaceMethod.HasContracts && implementingMethod.HasContracts)
        {
            var parameters = GetParameterList(implementingMethod.Parameters);

            // Check preconditions: interface precondition must imply implementer precondition
            // (implementer must accept at least what interface accepts)
            // For each interface precondition, check if ANY implementer precondition satisfies it
            foreach (var interfacePrecondition in interfaceMethod.Preconditions)
            {
                bool hasValidMatch = false;
                ContractViolation? lastViolation = null;

                foreach (var implPrecondition in implementingMethod.Preconditions)
                {
                    var result = CheckPreconditionImplication(
                        parameters,
                        interfacePrecondition.Condition,
                        implPrecondition.Condition,
                        classNode,
                        implementingMethod,
                        interfaceNode,
                        interfaceMethod,
                        implPrecondition.Span);

                    if (result == null)  // null means valid (no violation)
                    {
                        hasValidMatch = true;
                        break;  // Found a matching precondition, move to next interface precondition
                    }
                    lastViolation = result;
                }

                // Only report violation if NO implementer precondition matched
                if (!hasValidMatch && lastViolation != null)
                {
                    violations.Add(lastViolation);
                }
            }

            // Check postconditions: implementer postcondition must imply interface postcondition
            // (implementer must guarantee at least what interface guarantees)
            // For each interface postcondition, check if ANY implementer postcondition satisfies it
            var outputType = implementingMethod.Output?.TypeName;
            foreach (var interfacePostcondition in interfaceMethod.Postconditions)
            {
                bool hasValidMatch = false;
                ContractViolation? lastViolation = null;

                foreach (var implPostcondition in implementingMethod.Postconditions)
                {
                    var result = CheckPostconditionImplication(
                        parameters,
                        outputType,
                        interfacePostcondition.Condition,
                        implPostcondition.Condition,
                        classNode,
                        implementingMethod,
                        interfaceNode,
                        interfaceMethod,
                        implPostcondition.Span);

                    if (result == null)  // null means valid (no violation)
                    {
                        hasValidMatch = true;
                        break;  // Found a matching postcondition, move to next interface postcondition
                    }
                    lastViolation = result;
                }

                // Only report violation if NO implementer postcondition matched
                if (!hasValidMatch && lastViolation != null)
                {
                    violations.Add(lastViolation);
                }
            }

            var status = violations.Count > 0
                ? ContractInheritanceStatus.Violation
                : ContractInheritanceStatus.Valid;

            if (status == ContractInheritanceStatus.Valid)
            {
                _diagnostics.ReportInfo(
                    implementingMethod.Span,
                    DiagnosticCode.ContractInheritanceValid,
                    $"Contract inheritance valid for '{classNode.Name}.{implementingMethod.Name}'");
            }

            return new MethodInheritanceResult(
                implementingMethod.Id,
                implementingMethod.Name,
                status,
                violations);
        }

        // Case 3: Neither has contracts, or only implementer has contracts → valid
        return new MethodInheritanceResult(
            implementingMethod.Id,
            implementingMethod.Name,
            ContractInheritanceStatus.NoContracts,
            violations);
    }

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
        InterfaceDefinitionNode interfaceNode,
        MethodSignatureNode interfaceMethod,
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
                        interfaceNode.Name,
                        interfaceMethod.Name,
                        $"Precondition is stronger than interface contract (LSP violation). {z3Result.CounterexampleDescription}");

                    _diagnostics.ReportError(
                        implSpan,
                        DiagnosticCode.StrongerPrecondition,
                        $"LSP violation: Precondition in '{classNode.Name}.{implementingMethod.Name}' is stronger than '{interfaceNode.Name}.{interfaceMethod.Name}'. {z3Result.CounterexampleDescription}");
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
            interfaceNode,
            interfaceMethod,
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
        InterfaceDefinitionNode interfaceNode,
        MethodSignatureNode interfaceMethod,
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
                        interfaceNode.Name,
                        interfaceMethod.Name,
                        $"Postcondition is weaker than interface contract (LSP violation). {z3Result.CounterexampleDescription}");

                    _diagnostics.ReportError(
                        implSpan,
                        DiagnosticCode.WeakerPostcondition,
                        $"LSP violation: Postcondition in '{classNode.Name}.{implementingMethod.Name}' is weaker than '{interfaceNode.Name}.{interfaceMethod.Name}'. {z3Result.CounterexampleDescription}");
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
            interfaceNode,
            interfaceMethod,
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
        InterfaceDefinitionNode interfaceNode,
        MethodSignatureNode interfaceMethod,
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
                interfaceNode.Name,
                interfaceMethod.Name,
                "Precondition is stronger than interface contract (LSP violation)");

            _diagnostics.ReportError(
                implSpan,
                DiagnosticCode.StrongerPrecondition,
                $"LSP violation: Precondition in '{classNode.Name}.{implementingMethod.Name}' is stronger than '{interfaceNode.Name}.{interfaceMethod.Name}'");
            return violation;
        }

        // Cannot determine - assume valid (conservative)
        return null;
    }

    /// <summary>
    /// Heuristic check for postcondition strengthening.
    /// </summary>
    private ContractViolation? CheckPostconditionHeuristic(
        ExpressionNode interfacePostcondition,
        ExpressionNode implementerPostcondition,
        ClassDefinitionNode classNode,
        MethodNode implementingMethod,
        InterfaceDefinitionNode interfaceNode,
        MethodSignatureNode interfaceMethod,
        TextSpan implSpan)
    {
        // Check if implementer postcondition is stronger or equal (valid)
        if (IsStrongerOrEqual(implementerPostcondition, interfacePostcondition))
        {
            return null;
        }

        // Check if implementer postcondition is strictly weaker (violation)
        if (IsWeaker(implementerPostcondition, interfacePostcondition))
        {
            var violation = new ContractViolation(
                ContractViolationType.WeakerPostcondition,
                interfaceNode.Name,
                interfaceMethod.Name,
                "Postcondition is weaker than interface contract (LSP violation)");

            _diagnostics.ReportError(
                implSpan,
                DiagnosticCode.WeakerPostcondition,
                $"LSP violation: Postcondition in '{classNode.Name}.{implementingMethod.Name}' is weaker than '{interfaceNode.Name}.{interfaceMethod.Name}'");
            return violation;
        }

        // Cannot determine - assume valid (conservative)
        return null;
    }

    private static bool ParametersMatch(
        IReadOnlyList<ParameterNode> impl,
        IReadOnlyList<ParameterNode> iface)
    {
        if (impl.Count != iface.Count)
            return false;

        for (int i = 0; i < impl.Count; i++)
        {
            if (!impl[i].TypeName.Equals(iface[i].TypeName, StringComparison.Ordinal))
                return false;
        }

        return true;
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
    WeakerPostcondition
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
    public string InterfaceName { get; }
    public string MethodName { get; }
    public IReadOnlyList<RequiresNode> Preconditions { get; }
    public IReadOnlyList<EnsuresNode> Postconditions { get; }

    public InheritedContractInfo(
        string interfaceName,
        string methodName,
        IReadOnlyList<RequiresNode> preconditions,
        IReadOnlyList<EnsuresNode> postconditions)
    {
        InterfaceName = interfaceName;
        MethodName = methodName;
        Preconditions = preconditions;
        Postconditions = postconditions;
    }
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
    /// Mapping of (ClassName, MethodName) to inherited contracts.
    /// Used by the emitter to emit inherited contract checks.
    /// </summary>
    public IReadOnlyDictionary<(string ClassName, string MethodName), InheritedContractInfo> InheritedContracts { get; }

    public ModuleInheritanceResult(
        IReadOnlyList<ClassInheritanceResult> classes,
        IReadOnlyDictionary<(string ClassName, string MethodName), InheritedContractInfo> inheritedContracts)
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
        return InheritedContracts.TryGetValue((className, methodName), out var info) ? info : null;
    }
}
