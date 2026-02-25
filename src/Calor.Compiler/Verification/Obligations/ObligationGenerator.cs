using Calor.Compiler.Ast;
using Calor.Compiler.TypeChecking;

namespace Calor.Compiler.Verification.Obligations;

/// <summary>
/// Walks the AST and generates obligations for refinement types and proof obligations.
/// </summary>
public sealed class ObligationGenerator
{
    private readonly ObligationTracker _tracker;

    /// <summary>
    /// Refinement type definitions indexed by name, for looking up predicates.
    /// </summary>
    private readonly Dictionary<string, RefinementTypeNode> _refinementTypes = new(StringComparer.Ordinal);

    public ObligationGenerator(ObligationTracker tracker)
    {
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
    }

    /// <summary>
    /// Generates obligations for an entire module.
    /// </summary>
    public void Generate(ModuleNode module)
    {
        // Register refinement type definitions
        foreach (var rtype in module.RefinementTypes)
        {
            _refinementTypes[rtype.Name] = rtype;
        }

        // Generate obligations for each function
        foreach (var func in module.Functions)
        {
            GenerateForFunction(func);
        }

        // Generate for methods inside classes
        foreach (var cls in module.Classes)
        {
            foreach (var method in cls.Methods)
            {
                GenerateForMethod(method, cls);
            }
        }
    }

    private void GenerateForFunction(FunctionNode func)
    {
        // 1. Refined parameter entry obligations
        foreach (var param in func.Parameters)
        {
            GenerateParameterObligation(param, func.Id, func.Visibility);
        }

        // 2. Proof obligations from body
        foreach (var stmt in func.Body)
        {
            if (stmt is ProofObligationNode proof)
            {
                var obl = _tracker.Add(
                    ObligationKind.ProofObligation,
                    func.Id,
                    proof.Description ?? $"Proof obligation {proof.Id}",
                    proof.Condition,
                    proof.Span);
                obl.SourceProofId = proof.Id;
            }
        }
    }

    private void GenerateForMethod(MethodNode method, ClassDefinitionNode cls)
    {
        foreach (var param in method.Parameters)
        {
            GenerateParameterObligation(param, method.Id, method.Visibility);
        }

        foreach (var stmt in method.Body)
        {
            if (stmt is ProofObligationNode proof)
            {
                var obl = _tracker.Add(
                    ObligationKind.ProofObligation,
                    method.Id,
                    proof.Description ?? $"Proof obligation {proof.Id}",
                    proof.Condition,
                    proof.Span);
                obl.SourceProofId = proof.Id;
            }
        }
    }

    private void GenerateParameterObligation(ParameterNode param, string functionId, Visibility visibility)
    {
        if (param.InlineRefinement != null)
        {
            var obl = _tracker.Add(
                ObligationKind.RefinementEntry,
                functionId,
                $"Parameter '{param.Name}' must satisfy inline refinement",
                param.InlineRefinement.Predicate,
                param.Span);
            obl.ParameterName = param.Name;

            // Public functions get boundary status — can't statically verify caller behavior
            if (visibility == Visibility.Public)
            {
                obl.Status = ObligationStatus.Boundary;
                obl.SuggestedFix = $"Add runtime guard: if (!({param.Name} satisfies predicate)) throw";
            }
        }

        // Check if parameter type name matches a known refinement type
        if (_refinementTypes.TryGetValue(param.TypeName, out var rtype))
        {
            var obl = _tracker.Add(
                ObligationKind.RefinementEntry,
                functionId,
                $"Parameter '{param.Name}' must satisfy refinement type '{rtype.Name}'",
                rtype.Predicate,
                param.Span);
            obl.ParameterName = param.Name;

            if (visibility == Visibility.Public)
            {
                obl.Status = ObligationStatus.Boundary;
                obl.SuggestedFix = $"Add runtime guard for '{rtype.Name}' constraint on '{param.Name}'";
            }
        }
    }
}
