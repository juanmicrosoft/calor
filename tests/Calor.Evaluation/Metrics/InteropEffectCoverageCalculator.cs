using Calor.Evaluation.Core;
using Calor.Compiler.Ast;
using Calor.Compiler.Effects;
using Calor.Compiler.Effects.Manifests;

namespace Calor.Evaluation.Metrics;

/// <summary>
/// Measures BCL manifest resolution coverage for .NET interop.
/// This is a Calor-only metric as C# has no equivalent effect manifest system.
/// </summary>
public class InteropEffectCoverageCalculator : IMetricCalculator
{
    public string Category => "InteropEffectCoverage";
    public string Description => "BCL manifest resolution coverage for .NET interop";

    public Task<MetricResult> CalculateAsync(EvaluationContext context)
    {
        const double csharpScore = 0.0;

        if (!context.CalorCompilation.Success || context.CalorCompilation.Module == null)
        {
            return Task.FromResult(MetricResult.CreateHigherIsBetter(
                Category, "ManifestCoverage", 0.0, csharpScore,
                new Dictionary<string, object> { ["error"] = "Compilation failed", ["isCalorOnly"] = true }));
        }

        var loader = new ManifestLoader();
        var resolver = new EffectResolver(loader, EffectsCatalog.CreateDefault());
        resolver.Initialize();

        // Collect external calls from AST
        var externalCalls = CollectExternalCalls(context.CalorCompilation.Module);

        var resolved = 0;
        var unknown = 0;
        var unknownCalls = new List<string>();

        foreach (var (typeName, methodName) in externalCalls)
        {
            var resolution = resolver.Resolve(typeName, methodName);
            if (resolution.Status == EffectResolutionStatus.Unknown)
            {
                unknown++;
                unknownCalls.Add($"{typeName}.{methodName}");
            }
            else
            {
                resolved++;
            }
        }

        var total = resolved + unknown;
        var calorScore = total > 0 ? (double)resolved / total : 1.0;

        var details = new Dictionary<string, object>
        {
            ["resolved"] = resolved,
            ["unknown"] = unknown,
            ["total"] = total,
            ["coveragePercent"] = calorScore * 100,
            ["isCalorOnly"] = true
        };

        // Include up to 10 unknown calls for debugging
        if (unknownCalls.Count > 0)
        {
            details["unknownCalls"] = unknownCalls.Take(10).ToList();
        }

        return Task.FromResult(MetricResult.CreateHigherIsBetter(
            Category, "ManifestCoverage", calorScore, csharpScore, details));
    }

    /// <summary>
    /// Collects external method calls from the module AST.
    /// </summary>
    private static List<(string TypeName, string MethodName)> CollectExternalCalls(ModuleNode module)
    {
        var calls = new List<(string, string)>();
        var collector = new ExternalCallCollector(calls);

        foreach (var function in module.Functions)
        {
            collector.CollectFromStatements(function.Body);
        }

        return calls.Distinct().ToList();
    }

    /// <summary>
    /// Walks the AST to collect external method invocations.
    /// </summary>
    private sealed class ExternalCallCollector
    {
        private readonly List<(string, string)> _calls;

        public ExternalCallCollector(List<(string, string)> calls)
        {
            _calls = calls;
        }

        public void CollectFromStatements(IEnumerable<StatementNode> statements)
        {
            foreach (var statement in statements)
            {
                CollectFromStatement(statement);
            }
        }

        private void CollectFromStatement(StatementNode statement)
        {
            switch (statement)
            {
                case CallStatementNode call:
                    TryAddExternalCall(call.Target);
                    CollectFromExpressions(call.Arguments);
                    break;
                case IfStatementNode ifStmt:
                    CollectFromExpression(ifStmt.Condition);
                    CollectFromStatements(ifStmt.ThenBody);
                    foreach (var elseIf in ifStmt.ElseIfClauses)
                    {
                        CollectFromExpression(elseIf.Condition);
                        CollectFromStatements(elseIf.Body);
                    }
                    if (ifStmt.ElseBody != null)
                        CollectFromStatements(ifStmt.ElseBody);
                    break;
                case ForStatementNode forStmt:
                    CollectFromStatements(forStmt.Body);
                    break;
                case WhileStatementNode whileStmt:
                    CollectFromExpression(whileStmt.Condition);
                    CollectFromStatements(whileStmt.Body);
                    break;
                case DoWhileStatementNode doWhile:
                    CollectFromStatements(doWhile.Body);
                    CollectFromExpression(doWhile.Condition);
                    break;
                case ForeachStatementNode foreach_:
                    CollectFromExpression(foreach_.Collection);
                    CollectFromStatements(foreach_.Body);
                    break;
                case MatchStatementNode matchStmt:
                    CollectFromExpression(matchStmt.Target);
                    foreach (var matchCase in matchStmt.Cases)
                        CollectFromStatements(matchCase.Body);
                    break;
                case TryStatementNode tryStmt:
                    CollectFromStatements(tryStmt.TryBody);
                    foreach (var catchClause in tryStmt.CatchClauses)
                        CollectFromStatements(catchClause.Body);
                    if (tryStmt.FinallyBody != null)
                        CollectFromStatements(tryStmt.FinallyBody);
                    break;
                case ReturnStatementNode ret:
                    if (ret.Expression != null)
                        CollectFromExpression(ret.Expression);
                    break;
                case BindStatementNode bind:
                    if (bind.Initializer != null)
                        CollectFromExpression(bind.Initializer);
                    break;
                case AssignmentStatementNode assign:
                    CollectFromExpression(assign.Target);
                    CollectFromExpression(assign.Value);
                    break;
            }
        }

        private void CollectFromExpressions(IEnumerable<ExpressionNode> expressions)
        {
            foreach (var expr in expressions)
                CollectFromExpression(expr);
        }

        private void CollectFromExpression(ExpressionNode expr)
        {
            switch (expr)
            {
                case CallExpressionNode call:
                    TryAddExternalCall(call.Target);
                    CollectFromExpressions(call.Arguments);
                    break;
                case BinaryOperationNode binOp:
                    CollectFromExpression(binOp.Left);
                    CollectFromExpression(binOp.Right);
                    break;
                case UnaryOperationNode unOp:
                    CollectFromExpression(unOp.Operand);
                    break;
                case ConditionalExpressionNode cond:
                    CollectFromExpression(cond.Condition);
                    CollectFromExpression(cond.WhenTrue);
                    CollectFromExpression(cond.WhenFalse);
                    break;
                case MatchExpressionNode match:
                    CollectFromExpression(match.Target);
                    foreach (var matchCase in match.Cases)
                        CollectFromStatements(matchCase.Body);
                    break;
                case NewExpressionNode newExpr:
                    CollectFromExpressions(newExpr.Arguments);
                    break;
                case FieldAccessNode field:
                    CollectFromExpression(field.Target);
                    break;
                case ArrayAccessNode array:
                    CollectFromExpression(array.Array);
                    CollectFromExpression(array.Index);
                    break;
                case LambdaExpressionNode lambda:
                    if (lambda.ExpressionBody != null)
                        CollectFromExpression(lambda.ExpressionBody);
                    if (lambda.StatementBody != null)
                        CollectFromStatements(lambda.StatementBody);
                    break;
                case AwaitExpressionNode await_:
                    CollectFromExpression(await_.Awaited);
                    break;
                case SomeExpressionNode some:
                    CollectFromExpression(some.Value);
                    break;
                case OkExpressionNode ok:
                    CollectFromExpression(ok.Value);
                    break;
                case ErrExpressionNode err:
                    CollectFromExpression(err.Error);
                    break;
            }
        }

        private void TryAddExternalCall(string target)
        {
            // Parse call target to extract type and method
            var (typeName, methodName) = ParseCallTarget(target);
            if (!string.IsNullOrEmpty(typeName) && !string.IsNullOrEmpty(methodName))
            {
                _calls.Add((typeName, methodName));
            }
        }

        private static (string TypeName, string MethodName) ParseCallTarget(string target)
        {
            // Handle patterns like "Console.WriteLine", "File.ReadAllText", "System.IO.File.ReadAllText"
            var lastDot = target.LastIndexOf('.');
            if (lastDot <= 0)
                return ("", "");

            var methodName = target[(lastDot + 1)..];
            var typePart = target[..lastDot];

            // If type part doesn't contain a dot, try common namespaces
            if (!typePart.Contains('.'))
            {
                // Map common short names to full types
                typePart = typePart switch
                {
                    "Console" => "System.Console",
                    "File" => "System.IO.File",
                    "Directory" => "System.IO.Directory",
                    "Path" => "System.IO.Path",
                    "Random" => "System.Random",
                    "DateTime" => "System.DateTime",
                    "Environment" => "System.Environment",
                    "Process" => "System.Diagnostics.Process",
                    "HttpClient" => "System.Net.Http.HttpClient",
                    "Math" => "System.Math",
                    "Guid" => "System.Guid",
                    "DbContext" => "Microsoft.EntityFrameworkCore.DbContext",
                    _ => typePart
                };
            }

            return (typePart, methodName);
        }
    }
}
