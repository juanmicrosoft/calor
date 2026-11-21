using Calor.Compiler.Ast;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification.Z3;

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

    /// <summary>Where to insert the guard: "refinement", "if_guard", "assert".</summary>
    public required string InsertionKind { get; init; }

    /// <summary>Confidence: "high", "medium", "low".</summary>
    public required string Confidence { get; init; }

    /// <summary>True if Z3 confirmed this guard discharges the obligation. Null if not checked.</summary>
    public bool? Validated { get; internal set; }
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

        // Option 1: refine the parameter (highest confidence). #1618: a parameter
        // precondition is written as an inline refinement on that parameter.
        guards.Add(new DiscoveredGuard
        {
            ObligationId = obligation.Id,
            Description = $"Refine '{paramName}' so callers must satisfy the condition",
            CalorExpression = $"{paramName} | ({FormatCondition(obligation.Condition, "#")})",
            InsertionKind = "refinement",
            Confidence = "high"
        });

        // Option 2: Add an if-guard at function entry
        guards.Add(new DiscoveredGuard
        {
            ObligationId = obligation.Id,
            Description = $"Add runtime guard at function entry for '{paramName}' (the row needs throw, alloc)",
            CalorExpression = Guard(condStr, $"Refinement violated: {paramName}"),
            InsertionKind = "if_guard",
            Confidence = "medium"
        });

        return guards;
    }

    private List<DiscoveredGuard> DiscoverProofGuards(Obligation obligation)
    {
        var guards = new List<DiscoveredGuard>();
        var condStr = FormatCondition(obligation.Condition, null);

        // Option 1: strengthen the entry condition (#1618: as a refinement on the last
        // parameter the condition mentions, with that parameter written as #).
        guards.Add(new DiscoveredGuard
        {
            ObligationId = obligation.Id,
            Description = "Refine the last parameter this condition mentions (write it as #) so the obligation holds",
            CalorExpression = $"| ({condStr})",
            InsertionKind = "refinement",
            Confidence = "high"
        });

        // Option 2: Add guard before the proof site
        guards.Add(new DiscoveredGuard
        {
            ObligationId = obligation.Id,
            Description = "Add guard before the proof obligation site (the row needs throw, alloc)",
            CalorExpression = Guard(condStr, "Proof obligation violated"),
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
            CalorExpression = $"proof {condStr}",
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
            Description = "Add bounds check before array access (the row needs throw, alloc)",
            CalorExpression = Guard(condStr, "Index out of bounds"),
            InsertionKind = "if_guard",
            Confidence = "high"
        });

        return guards;
    }

    /// <summary>
    /// Formats an obligation condition as a readable Calor expression string.
    /// </summary>
    /// <summary>A native guard: throw when the condition does not hold. Failure uses exceptions.</summary>
    private static string Guard(string condition, string message) =>
        $"if !({condition})\n  throw new InvalidOperationException(\"{message}\")";

    /// <summary>The condition in native infix form, for example <c>x >= 0 && x < n</c>.</summary>
    private static string FormatCondition(ExpressionNode condition, string? selfRefReplacement)
    {
        return condition switch
        {
            BinaryOperationNode binOp => FormatBinaryOp(binOp, selfRefReplacement),
            UnaryOperationNode unOp => $"{unOp.Operator.ToCSharpOperator()}{Operand(unOp.Operand, selfRefReplacement)}",
            IntLiteralNode intLit => intLit.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            BoolLiteralNode boolLit => boolLit.Value ? "true" : "false",
            ReferenceNode refNode => refNode.Name,
            SelfRefNode => selfRefReplacement ?? "value",
            _ => condition.GetType().Name
        };
    }

    private static string FormatBinaryOp(BinaryOperationNode binOp, string? selfRefReplacement)
    {
        var left = Operand(binOp.Left, selfRefReplacement);
        var right = Operand(binOp.Right, selfRefReplacement);
        return $"{left} {binOp.Operator.ToCSharpOperator()} {right}";
    }

    /// <summary>A nested operation is parenthesized so the printed text keeps the tree's grouping.</summary>
    private static string Operand(ExpressionNode operand, string? selfRefReplacement)
    {
        var text = FormatCondition(operand, selfRefReplacement);
        return operand is BinaryOperationNode ? $"({text})" : text;
    }

    /// <summary>
    /// Validates discovered guards using Z3: checks whether adding the guard as a
    /// precondition would discharge the obligation. Sets Validated=true/false on each guard.
    /// </summary>
    /// <param name="guards">Guards to validate.</param>
    /// <param name="obligations">Obligations the guards target.</param>
    /// <param name="functionInfo">Parameter types for Z3 variable declarations.</param>
    public void ValidateWithZ3(
        List<DiscoveredGuard> guards,
        IReadOnlyList<Obligation> obligations,
        IReadOnlyList<(string Name, string TypeName)> parameters)
    {
        if (!Z3ContextFactory.IsAvailable)
            return;

        using var ctx = Z3ContextFactory.Create();

        foreach (var guard in guards)
        {
            // Only validate refinement guards (they have the same condition as the obligation).
            // #1618 renamed this kind from "precondition"; the check must follow it.
            if (guard.InsertionKind != "refinement")
            {
                guard.Validated = null; // Not applicable
                continue;
            }

            var obligation = obligations.FirstOrDefault(o => o.Id == guard.ObligationId);
            if (obligation == null)
            {
                guard.Validated = null;
                continue;
            }

            guard.Validated = ValidateSingleGuard(ctx, obligation, parameters);
        }
    }

    /// <summary>
    /// Checks: assuming the obligation condition as a precondition, can we discharge it?
    /// For RefinementEntry: assume condition, negate condition → trivially UNSAT → validated.
    /// For ProofObligation: the guard IS the obligation condition, so assuming it discharges it.
    /// </summary>
    private static bool ValidateSingleGuard(
        Microsoft.Z3.Context ctx,
        Obligation obligation,
        IReadOnlyList<(string Name, string TypeName)> parameters)
    {
        try
        {
            // #1413: a nested quantifier is refused by every verifier channel; no validated guard.
            if (QuantifierNesting.ContainsNestedQuantifier(obligation.Condition))
                return false;

            var translator = new ContractTranslator(ctx);

            foreach (var (name, type) in parameters)
            {
                if (!translator.DeclareVariable(name, type))
                    return false;
            }

            // Push self-variable for refinement predicates
            if (obligation.Kind == ObligationKind.RefinementEntry && obligation.ParameterName != null)
                translator.PushSelfVariable(obligation.ParameterName);

            var condExpr = translator.TranslateBoolExpr(obligation.Condition);
            if (condExpr == null)
                return false;

            var solver = ctx.MkSolver();
            solver.Set("timeout", 2000u);

            // ASSUME: the guard (which is the obligation condition itself for precondition guards)
            solver.Assert(condExpr);

            // NEGATE: the obligation condition
            solver.Assert(ctx.MkNot(condExpr));

            // This is trivially UNSAT: we assumed P and negated P
            var status = solver.Check();

            if (obligation.Kind == ObligationKind.RefinementEntry && obligation.ParameterName != null)
                translator.PopSelfVariable();

            return status == Microsoft.Z3.Status.UNSATISFIABLE;
        }
        catch
        {
            return false;
        }
    }
}
