using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.TypeChecking;

namespace Calor.Compiler.Analysis;

/// <summary>
/// Checks pattern matching expressions for exhaustiveness and unreachable patterns.
/// This ensures that agents know all cases are handled and no dead code exists.
/// </summary>
public sealed class PatternChecker
{
    private readonly DiagnosticBag _diagnostics;
    private readonly TypeEnvironment _typeEnv;

    public PatternChecker(DiagnosticBag diagnostics, TypeEnvironment? typeEnv = null)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _typeEnv = typeEnv ?? new TypeEnvironment();
    }

    /// <summary>
    /// Check all match expressions and statements in a module for exhaustiveness.
    /// </summary>
    public void Check(ModuleNode module)
    {
        foreach (var func in module.Functions)
        {
            CheckFunction(func);
        }
    }

    private void CheckFunction(FunctionNode func)
    {
        foreach (var stmt in func.Body)
        {
            CheckStatement(stmt);
        }
    }

    private void CheckStatement(StatementNode stmt)
    {
        switch (stmt)
        {
            case MatchStatementNode match:
                CheckMatchExhaustiveness(match.Target, match.Cases, match.Span, isExpression: false);
                foreach (var c in match.Cases)
                {
                    foreach (var s in c.Body)
                        CheckStatement(s);
                }
                break;

            case IfStatementNode ifStmt:
                foreach (var s in ifStmt.ThenBody)
                    CheckStatement(s);
                foreach (var elseIf in ifStmt.ElseIfClauses)
                    foreach (var s in elseIf.Body)
                        CheckStatement(s);
                if (ifStmt.ElseBody != null)
                    foreach (var s in ifStmt.ElseBody)
                        CheckStatement(s);
                break;

            case ForStatementNode forStmt:
                foreach (var s in forStmt.Body)
                    CheckStatement(s);
                break;

            case WhileStatementNode whileStmt:
                foreach (var s in whileStmt.Body)
                    CheckStatement(s);
                break;
        }
    }

    /// <summary>
    /// Check a match expression/statement for exhaustiveness and unreachable patterns.
    /// </summary>
    private void CheckMatchExhaustiveness(
        ExpressionNode target,
        IReadOnlyList<MatchCaseNode> cases,
        Parsing.TextSpan matchSpan,
        bool isExpression)
    {
        // Step 1: Detect the target type
        var targetType = InferTargetType(target);

        // Step 2: Check for unreachable patterns (patterns after wildcard/catch-all)
        CheckUnreachablePatterns(cases);

        // Step 3: Check exhaustiveness based on type
        CheckTypeExhaustiveness(targetType, cases, matchSpan, isExpression);
    }

    private CalorType InferTargetType(ExpressionNode target)
    {
        // Simple type inference for pattern matching targets
        return target switch
        {
            ReferenceNode refNode => _typeEnv.LookupVariable(refNode.Name) ?? ErrorType.Instance,
            SomeExpressionNode some => new OptionType(InferTargetType(some.Value)),
            NoneExpressionNode none => none.TypeName != null
                ? new OptionType(ResolveTypeName(none.TypeName))
                : new OptionType(new TypeVariable()),
            OkExpressionNode ok => new ResultType(InferTargetType(ok.Value), new TypeVariable()),
            ErrExpressionNode err => new ResultType(new TypeVariable(), InferTargetType(err.Error)),
            IntLiteralNode => PrimitiveType.Int,
            FloatLiteralNode => PrimitiveType.Float,
            BoolLiteralNode => PrimitiveType.Bool,
            StringLiteralNode => PrimitiveType.String,
            _ => ErrorType.Instance
        };
    }

    private CalorType ResolveTypeName(string typeName)
    {
        return PrimitiveType.FromName(typeName) ?? _typeEnv.LookupType(typeName) ?? ErrorType.Instance;
    }

    /// <summary>
    /// Check for patterns that can never be reached (after wildcards, duplicates).
    /// </summary>
    private void CheckUnreachablePatterns(IReadOnlyList<MatchCaseNode> cases)
    {
        var seenCatchAll = false;
        var seenLiterals = new HashSet<string>();
        var seenPatternSignatures = new HashSet<string>();

        for (var i = 0; i < cases.Count; i++)
        {
            var matchCase = cases[i];
            var pattern = matchCase.Pattern;
            var hasGuard = matchCase.Guard != null;

            // Patterns with guards don't make subsequent patterns unreachable
            if (hasGuard) continue;

            // Check if this pattern is unreachable due to previous patterns
            if (seenCatchAll)
            {
                _diagnostics.ReportWarning(
                    pattern.Span,
                    DiagnosticCode.UnreachablePattern,
                    "This pattern is unreachable because a previous pattern already matches all values");
            }

            // Get the signature of this pattern for duplicate detection
            var signature = GetPatternSignature(pattern);

            // Check for duplicate patterns
            if (!string.IsNullOrEmpty(signature) && !seenPatternSignatures.Add(signature))
            {
                _diagnostics.ReportWarning(
                    pattern.Span,
                    DiagnosticCode.DuplicatePattern,
                    $"Duplicate pattern: this pattern was already handled above");
            }

            // Update state based on pattern type
            switch (pattern)
            {
                case WildcardPatternNode:
                    seenCatchAll = true;
                    break;

                case VariablePatternNode:
                case VarPatternNode:
                    seenCatchAll = true;
                    break;

                case LiteralPatternNode litPat:
                    var litValue = GetLiteralValue(litPat.Literal);
                    if (litValue != null && !seenLiterals.Add(litValue))
                    {
                        _diagnostics.ReportWarning(
                            pattern.Span,
                            DiagnosticCode.DuplicatePattern,
                            $"Duplicate literal pattern: '{litValue}' was already handled");
                    }
                    break;

                case ConstantPatternNode constPat:
                    var constValue = GetLiteralValue(constPat.Value);
                    if (constValue != null && !seenLiterals.Add(constValue))
                    {
                        _diagnostics.ReportWarning(
                            pattern.Span,
                            DiagnosticCode.DuplicatePattern,
                            $"Duplicate constant pattern: '{constValue}' was already handled");
                    }
                    break;
            }
        }
    }

    private string GetPatternSignature(PatternNode pattern)
    {
        return pattern switch
        {
            WildcardPatternNode => "_",
            VariablePatternNode => "var:_",
            VarPatternNode => "var:_",
            SomePatternNode some => $"Some({GetPatternSignature(some.InnerPattern)})",
            NonePatternNode => "None",
            OkPatternNode ok => $"Ok({GetPatternSignature(ok.InnerPattern)})",
            ErrPatternNode err => $"Err({GetPatternSignature(err.InnerPattern)})",
            LiteralPatternNode lit => $"lit:{GetLiteralValue(lit.Literal)}",
            ConstantPatternNode c => $"const:{GetLiteralValue(c.Value)}",
            PositionalPatternNode pos => $"pos:{pos.TypeName}",
            PropertyPatternNode prop => $"prop:{prop.TypeName}",
            _ => ""
        };
    }

    private string? GetLiteralValue(ExpressionNode expr)
    {
        return expr switch
        {
            IntLiteralNode i => i.Value.ToString(),
            FloatLiteralNode f => f.Value.ToString(),
            BoolLiteralNode b => b.Value.ToString(),
            StringLiteralNode s => $"\"{s.Value}\"",
            _ => null
        };
    }

    /// <summary>
    /// Check if the match is exhaustive for the given target type.
    /// </summary>
    private void CheckTypeExhaustiveness(
        CalorType targetType,
        IReadOnlyList<MatchCaseNode> cases,
        Parsing.TextSpan matchSpan,
        bool isExpression)
    {
        // Skip exhaustiveness check for error types
        if (targetType is ErrorType) return;

        // Get required patterns and check coverage
        var coverage = AnalyzeCoverage(targetType, cases);

        if (!coverage.IsExhaustive)
        {
            var severity = isExpression ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning;
            var missingCases = string.Join(", ", coverage.MissingPatterns);

            _diagnostics.Report(
                matchSpan,
                DiagnosticCode.NonExhaustiveMatch,
                $"Match is not exhaustive. Missing cases: {missingCases}",
                severity);
        }
    }

    private CoverageResult AnalyzeCoverage(CalorType targetType, IReadOnlyList<MatchCaseNode> cases)
    {
        // Check for catch-all patterns first (without guards)
        foreach (var matchCase in cases)
        {
            if (matchCase.Guard != null) continue;

            if (matchCase.Pattern is WildcardPatternNode or VariablePatternNode or VarPatternNode)
            {
                return new CoverageResult(true, Array.Empty<string>());
            }
        }

        // Type-specific exhaustiveness checking
        return targetType switch
        {
            OptionType => CheckOptionCoverage(cases),
            ResultType => CheckResultCoverage(cases),
            PrimitiveType pt when pt.Equals(PrimitiveType.Bool) => CheckBoolCoverage(cases),
            UnionType ut => CheckUnionCoverage(ut, cases),
            _ => new CoverageResult(true, Array.Empty<string>()) // Unknown types are considered exhaustive
        };
    }

    private CoverageResult CheckOptionCoverage(IReadOnlyList<MatchCaseNode> cases)
    {
        var hasSome = false;
        var hasNone = false;

        foreach (var matchCase in cases)
        {
            // Skip guarded patterns for exhaustiveness
            if (matchCase.Guard != null) continue;

            switch (matchCase.Pattern)
            {
                case SomePatternNode:
                    hasSome = true;
                    break;
                case NonePatternNode:
                    hasNone = true;
                    break;
            }
        }

        var missing = new List<string>();
        if (!hasSome) missing.Add("Some(_)");
        if (!hasNone) missing.Add("None");

        return new CoverageResult(hasSome && hasNone, missing);
    }

    private CoverageResult CheckResultCoverage(IReadOnlyList<MatchCaseNode> cases)
    {
        var hasOk = false;
        var hasErr = false;

        foreach (var matchCase in cases)
        {
            if (matchCase.Guard != null) continue;

            switch (matchCase.Pattern)
            {
                case OkPatternNode:
                    hasOk = true;
                    break;
                case ErrPatternNode:
                    hasErr = true;
                    break;
            }
        }

        var missing = new List<string>();
        if (!hasOk) missing.Add("Ok(_)");
        if (!hasErr) missing.Add("Err(_)");

        return new CoverageResult(hasOk && hasErr, missing);
    }

    private CoverageResult CheckBoolCoverage(IReadOnlyList<MatchCaseNode> cases)
    {
        var hasTrue = false;
        var hasFalse = false;

        foreach (var matchCase in cases)
        {
            if (matchCase.Guard != null) continue;

            if (matchCase.Pattern is LiteralPatternNode lit && lit.Literal is BoolLiteralNode boolLit)
            {
                if (boolLit.Value) hasTrue = true;
                else hasFalse = true;
            }
            else if (matchCase.Pattern is ConstantPatternNode constPat && constPat.Value is BoolLiteralNode constBool)
            {
                if (constBool.Value) hasTrue = true;
                else hasFalse = true;
            }
        }

        var missing = new List<string>();
        if (!hasTrue) missing.Add("true");
        if (!hasFalse) missing.Add("false");

        return new CoverageResult(hasTrue && hasFalse, missing);
    }

    private CoverageResult CheckUnionCoverage(UnionType unionType, IReadOnlyList<MatchCaseNode> cases)
    {
        var coveredVariants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var matchCase in cases)
        {
            if (matchCase.Guard != null) continue;

            var variantName = matchCase.Pattern switch
            {
                PositionalPatternNode pos => pos.TypeName,
                PropertyPatternNode prop => prop.TypeName,
                _ => null
            };

            if (variantName != null)
            {
                coveredVariants.Add(variantName);
            }
        }

        var missing = unionType.Variants
            .Where(v => !coveredVariants.Contains(v.Name))
            .Select(v => v.Name)
            .ToList();

        return new CoverageResult(missing.Count == 0, missing);
    }

    private sealed class CoverageResult
    {
        public bool IsExhaustive { get; }
        public IReadOnlyList<string> MissingPatterns { get; }

        public CoverageResult(bool isExhaustive, IReadOnlyList<string> missingPatterns)
        {
            IsExhaustive = isExhaustive;
            MissingPatterns = missingPatterns;
        }
    }
}
