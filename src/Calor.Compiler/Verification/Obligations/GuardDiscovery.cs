using Calor.Compiler.Ast;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.Verification.Obligations;

/// <summary>
/// A discovered guard that would discharge a failed obligation.
/// </summary>
public sealed class DiscoveredGuard
{
    /// <summary>The obligation this guard would discharge.</summary>
    public required string ObligationId { get; init; }

    /// <summary>Human-readable description of the guard.</summary>
    public required string Description { get; init; }

    /// <summary>The guard condition as a Calor expression string.</summary>
    public required string CalorExpression { get; init; }

    /// <summary>Where to insert the guard: "precondition", "if_guard", "assert".</summary>
    public required string InsertionKind { get; init; }

    /// <summary>Confidence: "high", "medium", "low".</summary>
    public required string Confidence { get; init; }
}

/// <summary>
/// Given failed obligations, computes the simplest guards that would discharge them.
/// </summary>
public sealed class GuardDiscovery
{
    /// <summary>
    /// Discover guards for all failed obligations in the tracker.
    /// </summary>
    public List<DiscoveredGuard> DiscoverGuards(ObligationTracker tracker)
    {
        var guards = new List<DiscoveredGuard>();

        foreach (var obl in tracker.Obligations)
        {
            if (obl.Status is ObligationStatus.Failed or ObligationStatus.Timeout or ObligationStatus.Boundary)
            {
                guards.AddRange(DiscoverForObligation(obl));
            }
        }

        return guards;
    }

    /// <summary>
    /// Discover guards for a specific obligation.
    /// </summary>
    public List<DiscoveredGuard> DiscoverForObligation(Obligation obligation)
    {
        var guards = new List<DiscoveredGuard>();

        switch (obligation.Kind)
        {
            case ObligationKind.RefinementEntry:
                guards.AddRange(DiscoverRefinementEntryGuards(obligation));
                break;

            case ObligationKind.ProofObligation:
                guards.AddRange(DiscoverProofGuards(obligation));
                break;

            case ObligationKind.Subtype:
                guards.AddRange(DiscoverSubtypeGuards(obligation));
                break;

            case ObligationKind.IndexBounds:
                guards.AddRange(DiscoverBoundsGuards(obligation));
                break;
        }

        return guards;
    }

    private List<DiscoveredGuard> DiscoverRefinementEntryGuards(Obligation obligation)
    {
        var guards = new List<DiscoveredGuard>();
        var paramName = obligation.ParameterName ?? "param";
        var condStr = FormatCondition(obligation.Condition, paramName);

        // Option 1: Add a precondition (highest confidence)
        guards.Add(new DiscoveredGuard
        {
            ObligationId = obligation.Id,
            Description = $"Add precondition requiring '{paramName}' satisfies the refinement",
            CalorExpression = $"§Q ({condStr})",
            InsertionKind = "precondition",
            Confidence = "high"
        });

        // Option 2: Add an if-guard at function entry
        guards.Add(new DiscoveredGuard
        {
            ObligationId = obligation.Id,
            Description = $"Add runtime guard at function entry for '{paramName}'",
            CalorExpression = $"§IF{{g1}} (! ({condStr})) → §R (err \"Refinement violated: {paramName}\")",
            InsertionKind = "if_guard",
            Confidence = "medium"
        });

        return guards;
    }

    private List<DiscoveredGuard> DiscoverProofGuards(Obligation obligation)
    {
        var guards = new List<DiscoveredGuard>();
        var condStr = FormatCondition(obligation.Condition, null);

        // Option 1: Strengthen preconditions
        guards.Add(new DiscoveredGuard
        {
            ObligationId = obligation.Id,
            Description = "Add precondition that implies the proof obligation",
            CalorExpression = $"§Q ({condStr})",
            InsertionKind = "precondition",
            Confidence = "high"
        });

        // Option 2: Add guard before the proof site
        guards.Add(new DiscoveredGuard
        {
            ObligationId = obligation.Id,
            Description = "Add guard before the proof obligation site",
            CalorExpression = $"§IF{{g1}} (! ({condStr})) → §R (err \"Proof obligation violated\")",
            InsertionKind = "if_guard",
            Confidence = "medium"
        });

        return guards;
    }

    private List<DiscoveredGuard> DiscoverSubtypeGuards(Obligation obligation)
    {
        var guards = new List<DiscoveredGuard>();
        var condStr = FormatCondition(obligation.Condition, null);

        guards.Add(new DiscoveredGuard
        {
            ObligationId = obligation.Id,
            Description = "Add assertion before the assignment",
            CalorExpression = $"§PROOF{{p1:subtype}} ({condStr})",
            InsertionKind = "assert",
            Confidence = "medium"
        });

        return guards;
    }

    private List<DiscoveredGuard> DiscoverBoundsGuards(Obligation obligation)
    {
        var guards = new List<DiscoveredGuard>();
        var condStr = FormatCondition(obligation.Condition, null);

        guards.Add(new DiscoveredGuard
        {
            ObligationId = obligation.Id,
            Description = "Add bounds check before array access",
            CalorExpression = $"§IF{{g1}} (! ({condStr})) → §R (err \"Index out of bounds\")",
            InsertionKind = "if_guard",
            Confidence = "high"
        });

        return guards;
    }

    /// <summary>
    /// Formats an obligation condition as a readable Calor expression string.
    /// </summary>
    private static string FormatCondition(ExpressionNode condition, string? selfRefReplacement)
    {
        return condition switch
        {
            BinaryOperationNode binOp => FormatBinaryOp(binOp, selfRefReplacement),
            UnaryOperationNode unOp => $"(! {FormatCondition(unOp.Operand, selfRefReplacement)})",
            IntLiteralNode intLit => $"INT:{intLit.Value}",
            BoolLiteralNode boolLit => $"BOOL:{(boolLit.Value ? "true" : "false")}",
            ReferenceNode refNode => refNode.Name,
            SelfRefNode => selfRefReplacement ?? "#",
            _ => condition.GetType().Name
        };
    }

    private static string FormatBinaryOp(BinaryOperationNode binOp, string? selfRefReplacement)
    {
        var left = FormatCondition(binOp.Left, selfRefReplacement);
        var right = FormatCondition(binOp.Right, selfRefReplacement);
        var op = binOp.Operator switch
        {
            BinaryOperator.GreaterOrEqual => ">=",
            BinaryOperator.GreaterThan => ">",
            BinaryOperator.LessOrEqual => "<=",
            BinaryOperator.LessThan => "<",
            BinaryOperator.Equal => "==",
            BinaryOperator.NotEqual => "!=",
            BinaryOperator.And => "&&",
            BinaryOperator.Or => "||",
            BinaryOperator.Add => "+",
            BinaryOperator.Subtract => "-",
            _ => binOp.Operator.ToString()
        };
        return $"{op} {left} {right}";
    }
}
