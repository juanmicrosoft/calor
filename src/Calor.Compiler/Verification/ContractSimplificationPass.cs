using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.Verification;

/// <summary>
/// Compiler pass that simplifies contract expressions before Z3 verification and runtime code generation.
/// Applies algebraic simplifications iteratively until a fixed point is reached.
/// </summary>
public sealed class ContractSimplificationPass
{
    private readonly DiagnosticBag _diagnostics;
    private const int MaxIterations = 10;

    public ContractSimplificationPass(DiagnosticBag diagnostics)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    /// <summary>
    /// Simplify all contracts in a module.
    /// Applies simplification rules iteratively until no more changes occur or max iterations reached.
    /// </summary>
    public ModuleNode Simplify(ModuleNode module)
    {
        var simplifiedFunctions = new List<FunctionNode>();
        var moduleChanged = false;

        foreach (var function in module.Functions)
        {
            var simplified = SimplifyFunction(function);
            simplifiedFunctions.Add(simplified);
            if (!ReferenceEquals(simplified, function))
            {
                moduleChanged = true;
            }
        }

        // Also simplify contracts in classes
        var simplifiedClasses = new List<ClassDefinitionNode>();
        foreach (var cls in module.Classes)
        {
            var simplified = SimplifyClass(cls);
            simplifiedClasses.Add(simplified);
            if (!ReferenceEquals(simplified, cls))
            {
                moduleChanged = true;
            }
        }

        // Also simplify contracts in interfaces
        var simplifiedInterfaces = new List<InterfaceDefinitionNode>();
        foreach (var iface in module.Interfaces)
        {
            var simplified = SimplifyInterface(iface);
            simplifiedInterfaces.Add(simplified);
            if (!ReferenceEquals(simplified, iface))
            {
                moduleChanged = true;
            }
        }

        // Also simplify module-level invariants
        var simplifiedInvariants = SimplifyInvariants(module.Invariants);
        if (!ReferenceEquals(simplifiedInvariants, module.Invariants))
        {
            moduleChanged = true;
        }

        if (!moduleChanged)
        {
            return module;
        }

        return new ModuleNode(
            module.Span,
            module.Id,
            module.Name,
            module.Usings,
            simplifiedInterfaces,
            simplifiedClasses,
            module.Enums,
            module.Delegates,
            simplifiedFunctions,
            module.Attributes,
            module.Issues,
            module.Assumptions,
            simplifiedInvariants,
            module.Decisions,
            module.Context);
    }

    private FunctionNode SimplifyFunction(FunctionNode function)
    {
        if (!function.HasContracts)
        {
            return function;
        }

        var simplifiedPreconditions = SimplifyContracts(function.Preconditions, SimplifyRequires);
        var simplifiedPostconditions = SimplifyContracts(function.Postconditions, SimplifyEnsures);

        var preChanged = !ReferenceEquals(simplifiedPreconditions, function.Preconditions);
        var postChanged = !ReferenceEquals(simplifiedPostconditions, function.Postconditions);

        if (!preChanged && !postChanged)
        {
            return function;
        }

        return new FunctionNode(
            function.Span,
            function.Id,
            function.Name,
            function.Visibility,
            function.TypeParameters,
            function.Parameters,
            function.Output,
            function.Effects,
            simplifiedPreconditions,
            simplifiedPostconditions,
            function.Body,
            function.Attributes,
            function.Examples,
            function.Issues,
            function.Uses,
            function.UsedBy,
            function.Assumptions,
            function.Complexity,
            function.Since,
            function.Deprecated,
            function.BreakingChanges,
            function.Properties,
            function.Lock,
            function.Author,
            function.TaskRef,
            function.IsAsync);
    }

    private ClassDefinitionNode SimplifyClass(ClassDefinitionNode cls)
    {
        var simplifiedMethods = new List<MethodNode>();
        var methodsChanged = false;

        foreach (var method in cls.Methods)
        {
            var simplified = SimplifyMethod(method);
            simplifiedMethods.Add(simplified);
            if (!ReferenceEquals(simplified, method))
            {
                methodsChanged = true;
            }
        }

        if (!methodsChanged)
        {
            return cls;
        }

        return new ClassDefinitionNode(
            cls.Span,
            cls.Id,
            cls.Name,
            cls.IsAbstract,
            cls.IsSealed,
            cls.IsPartial,
            cls.IsStatic,
            cls.BaseClass,
            cls.ImplementedInterfaces,
            cls.TypeParameters,
            cls.Fields,
            cls.Properties,
            cls.Constructors,
            simplifiedMethods,
            cls.Events,
            cls.Attributes,
            cls.CSharpAttributes,
            cls.IsStruct,
            cls.IsReadOnly);
    }

    private InterfaceDefinitionNode SimplifyInterface(InterfaceDefinitionNode iface)
    {
        var simplifiedMethods = new List<MethodSignatureNode>();
        var methodsChanged = false;

        foreach (var method in iface.Methods)
        {
            var simplified = SimplifyMethodSignature(method);
            simplifiedMethods.Add(simplified);
            if (!ReferenceEquals(simplified, method))
            {
                methodsChanged = true;
            }
        }

        if (!methodsChanged)
        {
            return iface;
        }

        return new InterfaceDefinitionNode(
            iface.Span,
            iface.Id,
            iface.Name,
            iface.BaseInterfaces,
            iface.TypeParameters,
            simplifiedMethods,
            iface.Attributes,
            iface.CSharpAttributes);
    }

    private MethodNode SimplifyMethod(MethodNode method)
    {
        if (method.Preconditions.Count == 0 && method.Postconditions.Count == 0)
        {
            return method;
        }

        var simplifiedPreconditions = SimplifyContracts(method.Preconditions, SimplifyRequires);
        var simplifiedPostconditions = SimplifyContracts(method.Postconditions, SimplifyEnsures);

        var preChanged = !ReferenceEquals(simplifiedPreconditions, method.Preconditions);
        var postChanged = !ReferenceEquals(simplifiedPostconditions, method.Postconditions);

        if (!preChanged && !postChanged)
        {
            return method;
        }

        return new MethodNode(
            method.Span,
            method.Id,
            method.Name,
            method.Visibility,
            method.Modifiers,
            method.TypeParameters,
            method.Parameters,
            method.Output,
            method.Effects,
            simplifiedPreconditions,
            simplifiedPostconditions,
            method.Body,
            method.Attributes,
            method.CSharpAttributes,
            method.IsAsync);
    }

    private MethodSignatureNode SimplifyMethodSignature(MethodSignatureNode method)
    {
        if (method.Preconditions.Count == 0 && method.Postconditions.Count == 0)
        {
            return method;
        }

        var simplifiedPreconditions = SimplifyContracts(method.Preconditions, SimplifyRequires);
        var simplifiedPostconditions = SimplifyContracts(method.Postconditions, SimplifyEnsures);

        var preChanged = !ReferenceEquals(simplifiedPreconditions, method.Preconditions);
        var postChanged = !ReferenceEquals(simplifiedPostconditions, method.Postconditions);

        if (!preChanged && !postChanged)
        {
            return method;
        }

        return new MethodSignatureNode(
            method.Span,
            method.Id,
            method.Name,
            method.TypeParameters,
            method.Parameters,
            method.Output,
            method.Effects,
            simplifiedPreconditions,
            simplifiedPostconditions,
            method.Attributes,
            method.CSharpAttributes);
    }

    private IReadOnlyList<T> SimplifyContracts<T>(IReadOnlyList<T> contracts, Func<T, T> simplifier)
    {
        var simplified = new List<T>();
        var changed = false;

        foreach (var contract in contracts)
        {
            var simplifiedContract = simplifier(contract);
            simplified.Add(simplifiedContract);
            if (!ReferenceEquals(simplifiedContract, contract))
            {
                changed = true;
            }
        }

        return changed ? simplified : contracts;
    }

    private IReadOnlyList<InvariantNode> SimplifyInvariants(IReadOnlyList<InvariantNode> invariants)
    {
        return SimplifyContracts(invariants, SimplifyInvariant);
    }

    private RequiresNode SimplifyRequires(RequiresNode requires)
    {
        var simplifiedCondition = SimplifyExpression(requires.Condition);

        if (ReferenceEquals(simplifiedCondition, requires.Condition))
        {
            return requires;
        }

        return new RequiresNode(requires.Span, simplifiedCondition, requires.Message, requires.Attributes);
    }

    private EnsuresNode SimplifyEnsures(EnsuresNode ensures)
    {
        var simplifiedCondition = SimplifyExpression(ensures.Condition);

        if (ReferenceEquals(simplifiedCondition, ensures.Condition))
        {
            return ensures;
        }

        return new EnsuresNode(ensures.Span, simplifiedCondition, ensures.Message, ensures.Attributes);
    }

    private InvariantNode SimplifyInvariant(InvariantNode invariant)
    {
        var simplifiedCondition = SimplifyExpression(invariant.Condition);

        if (ReferenceEquals(simplifiedCondition, invariant.Condition))
        {
            return invariant;
        }

        return new InvariantNode(invariant.Span, simplifiedCondition, invariant.Message, invariant.Attributes);
    }

    /// <summary>
    /// Simplify an expression with fixed-point iteration.
    /// </summary>
    private ExpressionNode SimplifyExpression(ExpressionNode expression)
    {
        var current = expression;

        for (int i = 0; i < MaxIterations; i++)
        {
            var simplifier = new ExpressionSimplifier(_diagnostics);
            var simplified = simplifier.Simplify(current);

            if (!simplifier.Changed)
            {
                // Fixed point reached
                return simplified;
            }

            current = simplified;
        }

        // Max iterations reached, return current state
        return current;
    }
}
