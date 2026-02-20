using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;

namespace Calor.Compiler.Effects;

/// <summary>
/// Verifies that effect declarations match actual effects in function bodies.
/// Effects track what side effects a function may have (I/O, mutation, etc.).
/// </summary>
public sealed class EffectChecker
{
    private readonly DiagnosticBag _diagnostics;

    // Known call targets and their effects
    private static readonly Dictionary<string, EffectInfo> KnownEffects = new(StringComparer.OrdinalIgnoreCase)
    {
        // Console I/O
        ["Console.WriteLine"] = new EffectInfo(EffectKind.IO, "console_write"),
        ["Console.Write"] = new EffectInfo(EffectKind.IO, "console_write"),
        ["Console.ReadLine"] = new EffectInfo(EffectKind.IO, "console_read"),
        ["Console.Read"] = new EffectInfo(EffectKind.IO, "console_read"),

        // File I/O
        ["File.ReadAllText"] = new EffectInfo(EffectKind.IO, "filesystem_read"),
        ["File.ReadAllLines"] = new EffectInfo(EffectKind.IO, "filesystem_read"),
        ["File.WriteAllText"] = new EffectInfo(EffectKind.IO, "filesystem_write"),
        ["File.WriteAllLines"] = new EffectInfo(EffectKind.IO, "filesystem_write"),
        ["File.AppendAllText"] = new EffectInfo(EffectKind.IO, "filesystem_write"),
        ["File.Delete"] = new EffectInfo(EffectKind.IO, "filesystem_write"),
        ["File.Exists"] = new EffectInfo(EffectKind.IO, "filesystem_read"),

        // Network (common patterns)
        ["HttpClient.GetAsync"] = new EffectInfo(EffectKind.IO, "network_read"),
        ["HttpClient.PostAsync"] = new EffectInfo(EffectKind.IO, "network_write"),
        ["HttpClient.SendAsync"] = new EffectInfo(EffectKind.IO, "network_write"),

        // Random/time (non-determinism)
        ["Random.Next"] = new EffectInfo(EffectKind.Nondeterminism, "random"),
        ["DateTime.Now"] = new EffectInfo(EffectKind.Nondeterminism, "time"),
        ["DateTime.UtcNow"] = new EffectInfo(EffectKind.Nondeterminism, "time"),
        ["Guid.NewGuid"] = new EffectInfo(EffectKind.Nondeterminism, "random"),
    };

    public EffectChecker(DiagnosticBag diagnostics)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    /// <summary>
    /// Looks up a known effect for a given call target (e.g. "Console.WriteLine").
    /// Returns null if the target has no known effects.
    /// </summary>
    internal static EffectInfo? TryGetKnownEffect(string target)
        => KnownEffects.TryGetValue(target, out var effect) ? effect : null;

    /// <summary>
    /// Checks all functions in a module for effect correctness.
    /// </summary>
    public void Check(ModuleNode module)
    {
        foreach (var function in module.Functions)
        {
            CheckFunction(function);
        }
    }

    /// <summary>
    /// Checks a single function for effect correctness.
    /// </summary>
    public void CheckFunction(FunctionNode function)
    {
        var declaredEffects = GetDeclaredEffects(function);
        var actualEffects = InferEffects(function);

        // Check for undeclared effects
        foreach (var effect in actualEffects)
        {
            if (!IsEffectDeclared(effect, declaredEffects))
            {
                _diagnostics.Report(
                    function.Span,
                    DiagnosticCode.UndeclaredEffect,
                    $"Function '{function.Name}' uses {effect.Kind}:{effect.Value} but does not declare it",
                    DiagnosticSeverity.Warning);
            }
        }

        // Check for unused effect declarations
        foreach (var declared in declaredEffects)
        {
            if (!actualEffects.Any(e => e.Kind == declared.Kind && e.Value == declared.Value))
            {
                _diagnostics.Report(
                    function.Effects?.Span ?? function.Span,
                    DiagnosticCode.UnusedEffectDeclaration,
                    $"Function '{function.Name}' declares {declared.Kind}:{declared.Value} but does not use it",
                    DiagnosticSeverity.Info);
            }
        }
    }

    private List<EffectInfo> GetDeclaredEffects(FunctionNode function)
    {
        var effects = new List<EffectInfo>();

        if (function.Effects != null)
        {
            foreach (var kvp in function.Effects.Effects)
            {
                var kind = ParseEffectKind(kvp.Key);
                effects.Add(new EffectInfo(kind, kvp.Value));
            }
        }

        return effects;
    }

    private HashSet<EffectInfo> InferEffects(FunctionNode function)
    {
        var effects = new HashSet<EffectInfo>();
        InferEffectsFromStatements(function.Body, effects);
        return effects;
    }

    private void InferEffectsFromStatements(IEnumerable<StatementNode> statements, HashSet<EffectInfo> effects)
    {
        foreach (var statement in statements)
        {
            InferEffectsFromStatement(statement, effects);
        }
    }

    private void InferEffectsFromStatement(StatementNode statement, HashSet<EffectInfo> effects)
    {
        switch (statement)
        {
            case CallStatementNode call:
                InferEffectsFromCall(call, effects);
                break;
            case IfStatementNode ifStmt:
                InferEffectsFromStatements(ifStmt.ThenBody, effects);
                foreach (var elseIf in ifStmt.ElseIfClauses)
                {
                    InferEffectsFromStatements(elseIf.Body, effects);
                }
                if (ifStmt.ElseBody != null)
                {
                    InferEffectsFromStatements(ifStmt.ElseBody, effects);
                }
                break;
            case ForStatementNode forStmt:
                InferEffectsFromStatements(forStmt.Body, effects);
                break;
            case WhileStatementNode whileStmt:
                InferEffectsFromStatements(whileStmt.Body, effects);
                break;
            case MatchStatementNode matchStmt:
                foreach (var matchCase in matchStmt.Cases)
                {
                    InferEffectsFromStatements(matchCase.Body, effects);
                }
                break;
            case ForeachStatementNode foreachStmt:
                InferEffectsFromStatements(foreachStmt.Body, effects);
                break;
            // Collection mutations
            case CollectionPushNode:
            case DictionaryPutNode:
            case CollectionRemoveNode:
            case CollectionSetIndexNode:
            case CollectionClearNode:
            case CollectionInsertNode:
                effects.Add(new EffectInfo(EffectKind.Mutation, "collection"));
                break;
            case DictionaryForeachNode dictForeach:
                InferEffectsFromStatements(dictForeach.Body, effects);
                break;
        }
    }

    private void InferEffectsFromCall(CallStatementNode call, HashSet<EffectInfo> effects)
    {
        // Check if the call target has known effects
        if (KnownEffects.TryGetValue(call.Target, out var effect))
        {
            effects.Add(effect);
        }
        else
        {
            // Unknown calls are assumed to potentially have all effects
            // In a full implementation, we would track function signatures
        }

        // Recursively check arguments (they might have effects too)
        foreach (var arg in call.Arguments)
        {
            InferEffectsFromExpression(arg, effects);
        }
    }

    private void InferEffectsFromExpression(ExpressionNode expr, HashSet<EffectInfo> effects)
    {
        // Most expressions are pure, but some might have effects
        switch (expr)
        {
            case MatchExpressionNode matchExpr:
                foreach (var matchCase in matchExpr.Cases)
                {
                    InferEffectsFromStatements(matchCase.Body, effects);
                }
                break;
            case RecordCreationNode recordCreate:
                foreach (var field in recordCreate.Fields)
                {
                    InferEffectsFromExpression(field.Value, effects);
                }
                break;
            case BinaryOperationNode binOp:
                InferEffectsFromExpression(binOp.Left, effects);
                InferEffectsFromExpression(binOp.Right, effects);
                break;
            case SomeExpressionNode someExpr:
                InferEffectsFromExpression(someExpr.Value, effects);
                break;
            case OkExpressionNode okExpr:
                InferEffectsFromExpression(okExpr.Value, effects);
                break;
            case ErrExpressionNode errExpr:
                InferEffectsFromExpression(errExpr.Error, effects);
                break;
        }
    }

    private bool IsEffectDeclared(EffectInfo effect, List<EffectInfo> declaredEffects)
    {
        return declaredEffects.Any(d =>
            d.Kind == effect.Kind &&
            (d.Value == effect.Value || d.Value == "*")); // Allow wildcard
    }

    private static EffectKind ParseEffectKind(string kind)
    {
        return kind.ToLowerInvariant() switch
        {
            "io" => EffectKind.IO,
            "mutation" => EffectKind.Mutation,
            "memory" => EffectKind.Memory,
            "exception" => EffectKind.Exception,
            "nondeterminism" => EffectKind.Nondeterminism,
            _ => EffectKind.Unknown
        };
    }
}

/// <summary>
/// Categories of effects.
/// </summary>
public enum EffectKind
{
    Unknown,
    IO,
    Mutation,
    Memory,
    Exception,
    Nondeterminism
}

/// <summary>
/// Represents a specific effect (kind + value).
/// </summary>
public sealed class EffectInfo : IEquatable<EffectInfo>
{
    public EffectKind Kind { get; }
    public string Value { get; }

    public EffectInfo(EffectKind kind, string value)
    {
        Kind = kind;
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public bool Equals(EffectInfo? other)
    {
        if (other is null) return false;
        return Kind == other.Kind && Value == other.Value;
    }

    public override bool Equals(object? obj)
        => obj is EffectInfo other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(Kind, Value);

    public override string ToString() => $"{Kind}:{Value}";
}

/// <summary>
/// Shared utility for converting internal effect category/value pairs to compact surface codes.
/// Used by both CalorEmitter and CalorFormatter to ensure consistent serialization.
/// </summary>
public static class EffectCodes
{
    /// <summary>
    /// Convert internal effect category/value to compact code.
    /// E.g., ("io", "console_write") → "cw", ("io", "filesystem_read") → "fs:r"
    /// </summary>
    public static string ToCompact(string category, string value)
    {
        return (category.ToLowerInvariant(), value.ToLowerInvariant()) switch
        {
            // Console I/O
            ("io", "console_write") => "cw",
            ("io", "console_read") => "cr",
            // File I/O (new converter-produced values)
            ("io", "filesystem_write") => "fs:w",
            ("io", "filesystem_read") => "fs:r",
            // File I/O (legacy formatter values)
            ("io", "file_write") => "fw",
            ("io", "file_read") => "fr",
            ("io", "file_delete") => "fd",
            // Network
            ("io", "network") => "net",
            ("io", "network_read") => "net:r",
            ("io", "network_write") => "net:w",
            ("io", "http") => "http",
            // Database
            ("io", "database") => "db",
            ("io", "database_read") => "dbr",
            ("io", "database_write") => "dbw",
            // Environment / process
            ("io", "environment") => "env",
            ("io", "process") => "proc",
            // Mutation
            ("mutation", "collection") => "mut:col",
            // Memory
            ("memory", "allocation") => "alloc",
            // Nondeterminism
            ("nondeterminism", "time") => "time",
            ("nondeterminism", "random") => "rand",
            // Exception
            ("exception", "intentional") => "throw",
            _ => value // Pass through unknown values
        };
    }
}
