using System.Text;
using System.Text.RegularExpressions;
using Calor.Compiler.Ast;

namespace Calor.Compiler.Formatting;

/// <summary>
/// Formats Calor AST back to canonical Calor source code.
/// Produces agent-optimized compact format:
/// - No indentation (agents don't need visual hierarchy)
/// - One statement per line (enables clean diffs and targeted edits)
/// - No blank lines (reduces token count)
/// - Abbreviated IDs: m1, f1, l1, i1 (not m001, f001, for1, if1)
/// - Preserves spaces in expressions for token clarity
/// </summary>
public sealed class CalorFormatter
{
    private readonly StringBuilder _builder = new();

    /// <summary>
    /// Abbreviate IDs by stripping leading zeros from numeric suffix.
    /// Examples: m001→m1, f001→f1, for1→l1, if1→i1, while1→w1, do1→d1
    /// </summary>
    private static string AbbreviateId(string id)
    {
        // Handle loop prefix conversions: for→l, if→i, while→w, do→d
        var prefixMappings = new Dictionary<string, string>
        {
            { "for", "l" },
            { "if", "i" },
            { "while", "w" },
            { "do", "d" }
        };

        foreach (var (oldPrefix, newPrefix) in prefixMappings)
        {
            if (id.StartsWith(oldPrefix, StringComparison.Ordinal))
            {
                var suffix = id[oldPrefix.Length..];
                if (suffix.Length > 0 && char.IsDigit(suffix[0]))
                {
                    id = newPrefix + suffix;
                    break;
                }
            }
        }

        // Strip leading zeros from numeric suffix: m001→m1
        var match = Regex.Match(id, @"^([a-zA-Z]+)0*(\d+)$");
        if (match.Success)
            return match.Groups[1].Value + match.Groups[2].Value;
        return id;
    }

    /// <summary>
    /// Format a module AST to canonical Calor source.
    /// </summary>
    public string Format(ModuleNode module)
    {
        _builder.Clear();

        var moduleId = AbbreviateId(module.Id);

        // Module declaration
        AppendLine($"§M{{{moduleId}:{module.Name}}}");

        // Using directives
        foreach (var u in module.Usings)
        {
            AppendLine(FormatUsing(u));
        }

        // Functions
        foreach (var func in module.Functions)
        {
            FormatFunction(func);
        }

        // Closing module tag
        AppendLine($"§/M{{{moduleId}}}");

        // Trim trailing newline to avoid blank line at end
        return _builder.ToString().TrimEnd('\r', '\n');
    }

    private void AppendLine(string line)
    {
        _builder.AppendLine(line);
    }

    private string FormatUsing(UsingDirectiveNode node)
    {
        if (node.IsStatic)
            return $"§U{{static:{node.Namespace}}}";
        if (node.Alias != null)
            return $"§U{{{node.Alias}:{node.Namespace}}}";
        return $"§U{{{node.Namespace}}}";
    }

    private void FormatFunction(FunctionNode func)
    {
        var funcId = AbbreviateId(func.Id);

        // Function declaration - visibility INSIDE braces as third positional parameter
        var visibility = func.Visibility == Visibility.Public ? "pub" : "pri";
        AppendLine($"§F{{{funcId}:{func.Name}:{visibility}}}");

        // Parameters - type name should be lowercase compact form
        foreach (var param in func.Parameters)
        {
            var typeName = CompactTypeName(param.TypeName);
            AppendLine($"§I{{{typeName}:{param.Name}}}");
        }

        // Output type - lowercase compact form
        if (func.Output != null)
        {
            var typeName = CompactTypeName(func.Output.TypeName);
            AppendLine($"§O{{{typeName}}}");
        }

        // Effects - use compact effect codes
        if (func.Effects != null)
        {
            var effectCodes = func.Effects.Effects
                .SelectMany(kvp => kvp.Value.Split(',').Select(v => CompactEffectCode(kvp.Key, v.Trim())))
                .Distinct();
            AppendLine($"§E{{{string.Join(",", effectCodes)}}}");
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

        // Body (v2 implicit format - no §BODY tags needed)
        foreach (var stmt in func.Body)
        {
            FormatStatement(stmt);
        }

        AppendLine($"§/F{{{funcId}}}");
    }

    private void FormatStatement(StatementNode stmt)
    {
        switch (stmt)
        {
            case BindStatementNode bind:
                var mutability = bind.IsMutable ? "MUT" : "LET";
                var typeAnnotation = bind.TypeName != null ? $":{bind.TypeName}" : "";
                var initializer = bind.Initializer != null ? $" {FormatExpression(bind.Initializer)}" : "";
                AppendLine($"§{mutability}{{{bind.Name}{typeAnnotation}}}{initializer}");
                break;

            case CallStatementNode call:
                var args = string.Join(" ", call.Arguments.Select(FormatExpression));
                AppendLine($"§C{{{call.Target}}} {args}".TrimEnd());
                break;

            case ReturnStatementNode ret:
                if (ret.Expression != null)
                    AppendLine($"§R {FormatExpression(ret.Expression)}");
                else
                    AppendLine("§R");
                break;

            case IfStatementNode ifStmt:
                var ifId = AbbreviateId(ifStmt.Id);
                AppendLine($"§IF{{{ifId}}} {FormatExpression(ifStmt.Condition)}");
                foreach (var s in ifStmt.ThenBody) FormatStatement(s);
                foreach (var elseIf in ifStmt.ElseIfClauses)
                {
                    AppendLine($"§EI {FormatExpression(elseIf.Condition)}");
                    foreach (var s in elseIf.Body) FormatStatement(s);
                }
                if (ifStmt.ElseBody != null && ifStmt.ElseBody.Count > 0)
                {
                    AppendLine("§EL");
                    foreach (var s in ifStmt.ElseBody) FormatStatement(s);
                }
                AppendLine($"§/I{{{ifId}}}");
                break;

            case ForStatementNode forStmt:
                var loopId = AbbreviateId(forStmt.Id);
                var fromExpr = FormatExpression(forStmt.From);
                var toExpr = FormatExpression(forStmt.To);
                var stepExpr = forStmt.Step != null ? FormatExpression(forStmt.Step) : "1";
                AppendLine($"§L{{{loopId}:{forStmt.VariableName}:{fromExpr}:{toExpr}:{stepExpr}}}");
                foreach (var s in forStmt.Body) FormatStatement(s);
                AppendLine($"§/L{{{loopId}}}");
                break;

            case WhileStatementNode whileStmt:
                AppendLine($"§WH {FormatExpression(whileStmt.Condition)}");
                foreach (var s in whileStmt.Body) FormatStatement(s);
                AppendLine("§/WH");
                break;

            case MatchStatementNode match:
                var matchId = AbbreviateId(match.Id);
                AppendLine($"§MATCH{{{matchId}}} {FormatExpression(match.Target)}");
                foreach (var c in match.Cases)
                {
                    var guard = c.Guard != null ? $" §WHEN {FormatExpression(c.Guard)}" : "";
                    AppendLine($"§CASE {FormatPattern(c.Pattern)}{guard}");
                    foreach (var s in c.Body) FormatStatement(s);
                    AppendLine("§/CASE");
                }
                AppendLine($"§/MATCH{{{matchId}}}");
                break;

            case TryStatementNode tryStmt:
                AppendLine("§TRY");
                foreach (var s in tryStmt.TryBody) FormatStatement(s);
                foreach (var catchClause in tryStmt.CatchClauses)
                {
                    AppendLine($"§CATCH{{{catchClause.ExceptionType}:{catchClause.VariableName}}}");
                    foreach (var s in catchClause.Body) FormatStatement(s);
                }
                if (tryStmt.FinallyBody != null && tryStmt.FinallyBody.Count > 0)
                {
                    AppendLine("§FINALLY");
                    foreach (var s in tryStmt.FinallyBody) FormatStatement(s);
                }
                AppendLine("§/TRY");
                break;

            case ThrowStatementNode throwStmt:
                AppendLine($"§THROW {FormatExpression(throwStmt.Exception!)}");
                break;

            case PrintStatementNode print:
                var printTag = print.IsWriteLine ? "§P" : "§Pf";
                AppendLine($"{printTag} {FormatExpression(print.Expression)}");
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
            ReferenceNode r => r.Name, // Just the variable name, not §REF{name}
            BinaryOperationNode bin => $"({FormatOperator(bin.Operator)} {FormatExpression(bin.Left)} {FormatExpression(bin.Right)})", // Lisp prefix: (op left right)
            UnaryOperationNode un => $"({FormatUnaryOperator(un.Operator)} {FormatExpression(un.Operand)})",
            CallExpressionNode call => $"§CALL{{{call.Target}}} {string.Join(" ", call.Arguments.Select(FormatExpression))}".TrimEnd(),
            SomeExpressionNode some => $"§SOME {FormatExpression(some.Value)}",
            NoneExpressionNode none => none.TypeName != null ? $"§NONE{{{none.TypeName}}}" : "§NONE",
            OkExpressionNode ok => $"§OK {FormatExpression(ok.Value)}",
            ErrExpressionNode err => $"§ERR {FormatExpression(err.Error)}",
            NewExpressionNode newExpr => $"§NEW{{{newExpr.TypeName}}} {string.Join(" ", newExpr.Arguments.Select(FormatExpression))}".TrimEnd(),
            RecordCreationNode rec => FormatRecordCreation(rec),
            FieldAccessNode field => $"{FormatExpression(field.Target)}.{field.FieldName}",
            ArrayAccessNode arr => $"{FormatExpression(arr.Array)}[{FormatExpression(arr.Index)}]",
            MatchExpressionNode match => $"§MATCH{{{match.Id}}} ...",
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
            VarPatternNode var => $"§VAR{{{var.Name}}}",
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
        var fields = string.Join(" ", rec.Fields.Select(f => $"§SET{{{f.FieldName}}} {FormatExpression(f.Value)}"));
        return $"§NEW{{{rec.TypeName}}} {fields}".TrimEnd();
    }

    private string FormatLambda(LambdaExpressionNode lambda)
    {
        var parameters = string.Join(",", lambda.Parameters.Select(p => $"{p.Name}:{p.TypeName}"));
        return $"§LAMBDA{{{parameters}}} => ...";
    }

    private string FormatArrayCreation(ArrayCreationNode arr)
    {
        var size = arr.Size != null ? FormatExpression(arr.Size) : "";
        return $"§ARR{{{arr.ElementType}}} {size}".TrimEnd();
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

    /// <summary>
    /// Convert internal type names to compact v2 format.
    /// E.g., INT → i32, VOID → void, STRING → str
    /// </summary>
    private static string CompactTypeName(string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            return typeName;

        return typeName.ToUpperInvariant() switch
        {
            "INT" => "i32",
            "INT[BITS=8][SIGNED=TRUE]" => "i8",
            "INT[BITS=16][SIGNED=TRUE]" => "i16",
            "INT[BITS=64][SIGNED=TRUE]" => "i64",
            "INT[BITS=8][SIGNED=FALSE]" => "u8",
            "INT[BITS=16][SIGNED=FALSE]" => "u16",
            "INT[BITS=32][SIGNED=FALSE]" => "u32",
            "INT[BITS=64][SIGNED=FALSE]" => "u64",
            "FLOAT" => "f64",
            "FLOAT[BITS=32]" => "f32",
            "STRING" => "str",
            "BOOL" => "bool",
            "VOID" => "void",
            "NEVER" => "never",
            "CHAR" => "char",
            _ => typeName.ToLowerInvariant() // Pass through, lowercase
        };
    }

    /// <summary>
    /// Convert internal effect category/value to compact v2 code.
    /// E.g., io/console_write → cw, io/file_read → fr
    /// </summary>
    private static string CompactEffectCode(string category, string value)
    {
        return (category.ToLowerInvariant(), value.ToLowerInvariant()) switch
        {
            ("io", "console_write") => "cw",
            ("io", "console_read") => "cr",
            ("io", "file_write") => "fw",
            ("io", "file_read") => "fr",
            ("io", "file_delete") => "fd",
            ("io", "network") => "net",
            ("io", "http") => "http",
            ("io", "database") => "db",
            ("io", "database_read") => "dbr",
            ("io", "database_write") => "dbw",
            ("io", "environment") => "env",
            ("io", "process") => "proc",
            ("memory", "allocation") => "alloc",
            ("nondeterminism", "time") => "time",
            ("nondeterminism", "random") => "rand",
            _ => value // Pass through unknown values
        };
    }
}
