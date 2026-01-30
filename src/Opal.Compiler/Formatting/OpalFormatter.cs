using System.Text;
using Opal.Compiler.Ast;

namespace Opal.Compiler.Formatting;

/// <summary>
/// Formats OPAL AST back to canonical OPAL source code.
/// This is the canonical formatter ensuring consistent formatting across the codebase.
/// </summary>
public sealed class OpalFormatter
{
    private readonly StringBuilder _builder = new();
    private int _indentLevel;

    /// <summary>
    /// Format a module AST to canonical OPAL source.
    /// </summary>
    public string Format(ModuleNode module)
    {
        _builder.Clear();
        _indentLevel = 0;

        // Module declaration
        AppendLine($"§M[{module.Id}:{module.Name}]");
        AppendLine();

        // Using directives
        foreach (var u in module.Usings)
        {
            AppendLine(FormatUsing(u));
        }
        if (module.Usings.Count > 0) AppendLine();

        // Functions
        foreach (var func in module.Functions)
        {
            FormatFunction(func);
            AppendLine();
        }

        // Closing module tag
        AppendLine($"§/M[{module.Id}]");

        return _builder.ToString();
    }

    private void AppendLine(string line = "")
    {
        if (string.IsNullOrEmpty(line))
        {
            _builder.AppendLine();
        }
        else
        {
            _builder.Append(new string(' ', _indentLevel * 2));
            _builder.AppendLine(line);
        }
    }

    private void Indent() => _indentLevel++;
    private void Dedent() => _indentLevel = Math.Max(0, _indentLevel - 1);

    private string FormatUsing(UsingDirectiveNode node)
    {
        if (node.IsStatic)
            return $"§U[static:{node.Namespace}]";
        if (node.Alias != null)
            return $"§U[{node.Alias}:{node.Namespace}]";
        return $"§U[{node.Namespace}]";
    }

    private void FormatFunction(FunctionNode func)
    {
        // Function declaration
        var visibility = func.Visibility == Visibility.Public ? "pub" : "pri";
        AppendLine($"§F[{func.Id}:{func.Name}] {visibility}");
        Indent();

        // Parameters
        foreach (var param in func.Parameters)
        {
            AppendLine($"§I[{param.TypeName}:{param.Name}]");
        }

        // Output type
        if (func.Output != null)
        {
            AppendLine($"§O[{func.Output.TypeName}]");
        }

        // Effects
        if (func.Effects != null)
        {
            var effectsList = string.Join(",", func.Effects.Effects.Keys);
            AppendLine($"§E[{effectsList}]");
        }

        // Preconditions
        foreach (var pre in func.Preconditions)
        {
            AppendLine($"§Q {FormatExpression(pre.Condition)}");
        }

        // Postconditions
        foreach (var post in func.Postconditions)
        {
            AppendLine($"§S {FormatExpression(post.Condition)}");
        }

        // Body
        AppendLine("§BODY");
        Indent();
        foreach (var stmt in func.Body)
        {
            FormatStatement(stmt);
        }
        Dedent();
        AppendLine("§/BODY");

        Dedent();
        AppendLine($"§/F[{func.Id}]");
    }

    private void FormatStatement(StatementNode stmt)
    {
        switch (stmt)
        {
            case BindStatementNode bind:
                var mutability = bind.IsMutable ? "MUT" : "LET";
                var typeAnnotation = bind.TypeName != null ? $":{bind.TypeName}" : "";
                var initializer = bind.Initializer != null ? $" {FormatExpression(bind.Initializer)}" : "";
                AppendLine($"§{mutability}[{bind.Name}{typeAnnotation}]{initializer}");
                break;

            case CallStatementNode call:
                var args = string.Join(" ", call.Arguments.Select(FormatExpression));
                AppendLine($"§CALL[{call.Target}] {args}".TrimEnd());
                break;

            case ReturnStatementNode ret:
                if (ret.Expression != null)
                    AppendLine($"§RET {FormatExpression(ret.Expression)}");
                else
                    AppendLine("§RET");
                break;

            case IfStatementNode ifStmt:
                AppendLine($"§IF {FormatExpression(ifStmt.Condition)}");
                Indent();
                foreach (var s in ifStmt.ThenBody) FormatStatement(s);
                Dedent();
                foreach (var elseIf in ifStmt.ElseIfClauses)
                {
                    AppendLine($"§ELIF {FormatExpression(elseIf.Condition)}");
                    Indent();
                    foreach (var s in elseIf.Body) FormatStatement(s);
                    Dedent();
                }
                if (ifStmt.ElseBody != null && ifStmt.ElseBody.Count > 0)
                {
                    AppendLine("§ELSE");
                    Indent();
                    foreach (var s in ifStmt.ElseBody) FormatStatement(s);
                    Dedent();
                }
                AppendLine("§/IF");
                break;

            case ForStatementNode forStmt:
                var step = forStmt.Step != null ? $" §STEP {FormatExpression(forStmt.Step)}" : "";
                AppendLine($"§FOR[{forStmt.VariableName}] {FormatExpression(forStmt.From)} §TO {FormatExpression(forStmt.To)}{step}");
                Indent();
                foreach (var s in forStmt.Body) FormatStatement(s);
                Dedent();
                AppendLine("§/FOR");
                break;

            case WhileStatementNode whileStmt:
                AppendLine($"§WHILE {FormatExpression(whileStmt.Condition)}");
                Indent();
                foreach (var s in whileStmt.Body) FormatStatement(s);
                Dedent();
                AppendLine("§/WHILE");
                break;

            case MatchStatementNode match:
                AppendLine($"§MATCH[{match.Id}] {FormatExpression(match.Target)}");
                Indent();
                foreach (var c in match.Cases)
                {
                    var guard = c.Guard != null ? $" §WHEN {FormatExpression(c.Guard)}" : "";
                    AppendLine($"§CASE {FormatPattern(c.Pattern)}{guard}");
                    Indent();
                    foreach (var s in c.Body) FormatStatement(s);
                    Dedent();
                    AppendLine("§/CASE");
                }
                Dedent();
                AppendLine($"§/MATCH[{match.Id}]");
                break;

            case TryStatementNode tryStmt:
                AppendLine("§TRY");
                Indent();
                foreach (var s in tryStmt.TryBody) FormatStatement(s);
                Dedent();
                foreach (var catchClause in tryStmt.CatchClauses)
                {
                    AppendLine($"§CATCH[{catchClause.ExceptionType}:{catchClause.VariableName}]");
                    Indent();
                    foreach (var s in catchClause.Body) FormatStatement(s);
                    Dedent();
                }
                if (tryStmt.FinallyBody != null && tryStmt.FinallyBody.Count > 0)
                {
                    AppendLine("§FINALLY");
                    Indent();
                    foreach (var s in tryStmt.FinallyBody) FormatStatement(s);
                    Dedent();
                }
                AppendLine("§/TRY");
                break;

            case ThrowStatementNode throwStmt:
                AppendLine($"§THROW {FormatExpression(throwStmt.Exception!)}");
                break;

            default:
                AppendLine($"§STMT /* {stmt.GetType().Name} */");
                break;
        }
    }

    private string FormatExpression(ExpressionNode expr)
    {
        return expr switch
        {
            IntLiteralNode i => i.Value.ToString(),
            FloatLiteralNode f => f.Value.ToString("G"),
            BoolLiteralNode b => b.Value ? "true" : "false",
            StringLiteralNode s => $"\"{EscapeString(s.Value)}\"",
            ReferenceNode r => $"§REF[{r.Name}]",
            BinaryOperationNode bin => $"({FormatExpression(bin.Left)} {FormatOperator(bin.Operator)} {FormatExpression(bin.Right)})",
            UnaryOperationNode un => $"{FormatUnaryOperator(un.Operator)}{FormatExpression(un.Operand)}",
            CallExpressionNode call => $"§CALL[{call.Target}] {string.Join(" ", call.Arguments.Select(FormatExpression))}".TrimEnd(),
            SomeExpressionNode some => $"§SOME {FormatExpression(some.Value)}",
            NoneExpressionNode none => none.TypeName != null ? $"§NONE[{none.TypeName}]" : "§NONE",
            OkExpressionNode ok => $"§OK {FormatExpression(ok.Value)}",
            ErrExpressionNode err => $"§ERR {FormatExpression(err.Error)}",
            NewExpressionNode newExpr => $"§NEW[{newExpr.TypeName}] {string.Join(" ", newExpr.Arguments.Select(FormatExpression))}".TrimEnd(),
            RecordCreationNode rec => FormatRecordCreation(rec),
            FieldAccessNode field => $"{FormatExpression(field.Target)}.{field.FieldName}",
            ArrayAccessNode arr => $"{FormatExpression(arr.Array)}[{FormatExpression(arr.Index)}]",
            MatchExpressionNode match => $"§MATCH[{match.Id}] ...",
            LambdaExpressionNode lambda => FormatLambda(lambda),
            ArrayCreationNode arr => FormatArrayCreation(arr),
            AwaitExpressionNode await => $"§AWAIT {FormatExpression(await.Awaited)}",
            NullCoalesceNode nc => $"({FormatExpression(nc.Left)} ?? {FormatExpression(nc.Right)})",
            NullConditionalNode nc => $"{FormatExpression(nc.Target)}?.{nc.MemberName}",
            ThisExpressionNode => "§THIS",
            BaseExpressionNode => "§BASE",
            _ => $"/* {expr.GetType().Name} */"
        };
    }

    private string FormatPattern(PatternNode pattern)
    {
        return pattern switch
        {
            WildcardPatternNode => "_",
            VariablePatternNode v => v.Name,
            VarPatternNode var => $"§VAR[{var.Name}]",
            LiteralPatternNode lit => FormatExpression(lit.Literal),
            ConstantPatternNode c => FormatExpression(c.Value),
            SomePatternNode some => $"§SOME {FormatPattern(some.InnerPattern)}",
            NonePatternNode => "§NONE",
            OkPatternNode ok => $"§OK {FormatPattern(ok.InnerPattern)}",
            ErrPatternNode err => $"§ERR {FormatPattern(err.InnerPattern)}",
            _ => $"/* {pattern.GetType().Name} */"
        };
    }

    private string FormatRecordCreation(RecordCreationNode rec)
    {
        var fields = string.Join(" ", rec.Fields.Select(f => $"§SET[{f.FieldName}] {FormatExpression(f.Value)}"));
        return $"§NEW[{rec.TypeName}] {fields}".TrimEnd();
    }

    private string FormatLambda(LambdaExpressionNode lambda)
    {
        var parameters = string.Join(",", lambda.Parameters.Select(p => $"{p.Name}:{p.TypeName}"));
        return $"§LAMBDA[{parameters}] => ...";
    }

    private string FormatArrayCreation(ArrayCreationNode arr)
    {
        var size = arr.Size != null ? FormatExpression(arr.Size) : "";
        return $"§ARR[{arr.ElementType}] {size}".TrimEnd();
    }

    private static string FormatOperator(BinaryOperator op) => op switch
    {
        BinaryOperator.Add => "+",
        BinaryOperator.Subtract => "-",
        BinaryOperator.Multiply => "*",
        BinaryOperator.Divide => "/",
        BinaryOperator.Modulo => "%",
        BinaryOperator.Equal => "==",
        BinaryOperator.NotEqual => "!=",
        BinaryOperator.LessThan => "<",
        BinaryOperator.LessOrEqual => "<=",
        BinaryOperator.GreaterThan => ">",
        BinaryOperator.GreaterOrEqual => ">=",
        BinaryOperator.And => "&&",
        BinaryOperator.Or => "||",
        _ => "?"
    };

    private static string FormatUnaryOperator(UnaryOperator op) => op switch
    {
        UnaryOperator.Negate => "-",
        UnaryOperator.Not => "!",
        _ => "?"
    };

    private static string EscapeString(string s)
    {
        return s.Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
    }
}
