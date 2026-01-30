using System.Text;
using Opal.Compiler.Ast;

namespace Opal.Compiler.Migration;

/// <summary>
/// Emits OPAL v2+ source code from an OPAL AST.
/// Uses Lisp-style expressions and arrow syntax for control flow.
/// </summary>
public sealed class OpalEmitter : IAstVisitor<string>
{
    private readonly StringBuilder _builder = new();
    private int _indentLevel;
    private readonly ConversionContext? _context;

    public OpalEmitter(ConversionContext? context = null)
    {
        _context = context;
    }

    public string Emit(ModuleNode module)
    {
        _builder.Clear();
        _indentLevel = 0;
        Visit(module);
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

    private void Append(string text)
    {
        _builder.Append(text);
    }

    private void Indent() => _indentLevel++;
    private void Dedent() => _indentLevel--;

    public string Visit(ModuleNode node)
    {
        // Module header
        AppendLine($"§M{{{node.Id}:{node.Name}}}");
        Indent();

        // Emit using directives
        foreach (var usingDir in node.Usings)
        {
            Visit(usingDir);
        }
        if (node.Usings.Count > 0)
            AppendLine();

        // Emit interfaces
        foreach (var iface in node.Interfaces)
        {
            Visit(iface);
            AppendLine();
        }

        // Emit classes
        foreach (var cls in node.Classes)
        {
            Visit(cls);
            AppendLine();
        }

        // Emit module-level functions
        foreach (var func in node.Functions)
        {
            Visit(func);
            AppendLine();
        }

        Dedent();
        AppendLine($"§/M{{{node.Id}}}");

        return _builder.ToString();
    }

    public string Visit(UsingDirectiveNode node)
    {
        if (node.IsStatic)
        {
            AppendLine($"§USING{{static:{node.Namespace}}}");
        }
        else if (node.Alias != null)
        {
            AppendLine($"§USING{{{node.Alias}={node.Namespace}}}");
        }
        else
        {
            AppendLine($"§USING{{{node.Namespace}}}");
        }
        return "";
    }

    public string Visit(InterfaceDefinitionNode node)
    {
        var baseList = node.BaseInterfaces.Count > 0
            ? $":{string.Join(",", node.BaseInterfaces)}"
            : "";
        var attrs = EmitCSharpAttributes(node.CSharpAttributes);

        AppendLine($"§IFACE{{{node.Id}:{node.Name}{baseList}}}{attrs}");
        Indent();

        foreach (var method in node.Methods)
        {
            Visit(method);
        }

        Dedent();
        AppendLine($"§/IFACE{{{node.Id}}}");

        return "";
    }

    public string Visit(MethodSignatureNode node)
    {
        var typeParams = node.TypeParameters.Count > 0
            ? $"<{string.Join(",", node.TypeParameters.Select(tp => tp.Name))}>"
            : "";

        var output = node.Output != null ? TypeMapper.CSharpToOpal(node.Output.TypeName) : "void";
        var paramList = string.Join(",", node.Parameters.Select(p =>
            $"{TypeMapper.CSharpToOpal(p.TypeName)}:{p.Name}"));
        var attrs = EmitCSharpAttributes(node.CSharpAttributes);

        AppendLine($"§SIG{{{node.Id}:{node.Name}{typeParams}}}{attrs} ({paramList}) → {output}");

        return "";
    }

    public string Visit(ClassDefinitionNode node)
    {
        var modifiers = new List<string>();
        if (node.IsAbstract) modifiers.Add("abs");
        if (node.IsSealed) modifiers.Add("sealed");
        if (node.IsPartial) modifiers.Add("partial");
        if (node.IsStatic) modifiers.Add("static");

        var modStr = modifiers.Count > 0 ? $":{string.Join(",", modifiers)}" : "";
        var baseStr = node.BaseClass != null ? $":{node.BaseClass}" : "";

        var typeParams = node.TypeParameters.Count > 0
            ? $"<{string.Join(",", node.TypeParameters.Select(tp => tp.Name))}>"
            : "";
        var attrs = EmitCSharpAttributes(node.CSharpAttributes);

        AppendLine($"§CLASS{{{node.Id}:{node.Name}{typeParams}{baseStr}{modStr}}}{attrs}");
        Indent();

        // Emit implemented interfaces
        foreach (var iface in node.ImplementedInterfaces)
        {
            AppendLine($"§IMPL{{{iface}}}");
        }
        if (node.ImplementedInterfaces.Count > 0)
            AppendLine();

        // Emit fields
        foreach (var field in node.Fields)
        {
            Visit(field);
        }
        if (node.Fields.Count > 0)
            AppendLine();

        // Emit events
        foreach (var evt in node.Events)
        {
            Visit(evt);
        }
        if (node.Events.Count > 0)
            AppendLine();

        // Emit properties
        foreach (var prop in node.Properties)
        {
            Visit(prop);
        }
        if (node.Properties.Count > 0)
            AppendLine();

        // Emit constructors
        foreach (var ctor in node.Constructors)
        {
            Visit(ctor);
            AppendLine();
        }

        // Emit methods
        foreach (var method in node.Methods)
        {
            Visit(method);
            AppendLine();
        }

        Dedent();
        AppendLine($"§/CLASS{{{node.Id}}}");

        return "";
    }

    public string Visit(ClassFieldNode node)
    {
        var visibility = GetVisibilityShorthand(node.Visibility);
        var typeName = TypeMapper.CSharpToOpal(node.TypeName);
        var defaultVal = node.DefaultValue != null ? $" = {node.DefaultValue.Accept(this)}" : "";
        var attrs = EmitCSharpAttributes(node.CSharpAttributes);

        AppendLine($"§FLD{{{typeName}:{node.Name}:{visibility}}}{attrs}{defaultVal}");

        return "";
    }

    public string Visit(PropertyNode node)
    {
        var visibility = GetVisibilityShorthand(node.Visibility);
        var typeName = TypeMapper.CSharpToOpal(node.TypeName);
        var attrs = EmitCSharpAttributes(node.CSharpAttributes);
        var defaultVal = node.DefaultValue != null ? $" = {node.DefaultValue.Accept(this)}" : "";

        // Always emit full property syntax with body and closing tag
        // Parser expects: §PROP[id:name:type:vis] §GET §SET §/PROP[id]
        AppendLine($"§PROP{{{node.Id}:{node.Name}:{typeName}:{visibility}}}{attrs}");
        Indent();

        if (node.Getter != null)
        {
            Visit(node.Getter);
        }
        if (node.Setter != null)
        {
            Visit(node.Setter);
        }
        if (node.Initer != null)
        {
            Visit(node.Initer);
        }

        // Emit default value if present (as direct expression, parser handles it)
        if (node.DefaultValue != null)
        {
            var defaultExpr = node.DefaultValue.Accept(this);
            AppendLine($"= {defaultExpr}");
        }

        Dedent();
        AppendLine($"§/PROP{{{node.Id}}}");

        return "";
    }

    public string Visit(PropertyAccessorNode node)
    {
        var keyword = node.Kind switch
        {
            PropertyAccessorNode.AccessorKind.Get => "GET",
            PropertyAccessorNode.AccessorKind.Set => "SET",
            PropertyAccessorNode.AccessorKind.Init => "INIT",
            _ => "GET"
        };

        // Add visibility if different from property visibility
        var visStr = node.Visibility.HasValue ? $"{{{GetVisibilityShorthand(node.Visibility.Value)}}}" : "";

        if (node.IsAutoImplemented)
        {
            // Auto-implemented: just §GET or §GET{{pri}} for restricted visibility
            AppendLine($"§{keyword}{visStr}");
        }
        else
        {
            // Full body: §GET ... §/GET
            AppendLine($"§{keyword}{visStr}");
            Indent();
            foreach (var stmt in node.Body)
            {
                stmt.Accept(this);
            }
            Dedent();
            AppendLine($"§/{keyword}");
        }

        return "";
    }

    public string Visit(ConstructorNode node)
    {
        var visibility = GetVisibilityShorthand(node.Visibility);
        var attrs = EmitCSharpAttributes(node.CSharpAttributes);

        // Parser expects: §CTOR[id:visibility] with §I[type:name] for params
        AppendLine($"§CTOR{{{node.Id}:{visibility}}}{attrs}");
        Indent();

        // Emit parameters as separate §I[type:name] lines
        foreach (var param in node.Parameters)
        {
            var paramType = TypeMapper.CSharpToOpal(param.TypeName);
            AppendLine($"§I{{{paramType}:{param.Name}}}");
        }

        // Emit preconditions
        foreach (var pre in node.Preconditions)
        {
            Visit(pre);
        }

        // Emit constructor initializer (base/this call)
        if (node.Initializer != null)
        {
            var initKeyword = node.Initializer.IsBaseCall ? "§BASE" : "§THIS";
            AppendLine(initKeyword);
            Indent();
            foreach (var arg in node.Initializer.Arguments)
            {
                AppendLine($"§A {arg.Accept(this)}");
            }
            Dedent();
            AppendLine(node.Initializer.IsBaseCall ? "§/BASE" : "§/THIS");
        }

        // Emit body statements
        foreach (var stmt in node.Body)
        {
            stmt.Accept(this);
        }

        Dedent();
        AppendLine($"§/CTOR{{{node.Id}}}");

        return "";
    }

    public string Visit(ConstructorInitializerNode node)
    {
        var keyword = node.IsBaseCall ? "base" : "this";
        var args = string.Join(", ", node.Arguments.Select(a => a.Accept(this)));
        return $"{keyword}({args})";
    }

    public string Visit(MethodNode node)
    {
        var visibility = GetVisibilityShorthand(node.Visibility);
        var modifiers = new List<string>();

        if (node.IsVirtual) modifiers.Add("virt");
        if (node.IsOverride) modifiers.Add("over");
        if (node.IsAbstract) modifiers.Add("abs");
        if (node.IsSealed) modifiers.Add("sealed");
        if (node.IsStatic) modifiers.Add("static");

        var modStr = modifiers.Count > 0 ? $":{string.Join(",", modifiers)}" : "";

        var typeParams = node.TypeParameters.Count > 0
            ? $"<{string.Join(",", node.TypeParameters.Select(tp => tp.Name))}>"
            : "";

        var output = node.Output != null ? TypeMapper.CSharpToOpal(node.Output.TypeName) : "void";
        var attrs = EmitCSharpAttributes(node.CSharpAttributes);

        AppendLine($"§METHOD{{{node.Id}:{node.Name}{typeParams}:{visibility}{modStr}}}{attrs}");
        Indent();

        // Parameters
        foreach (var param in node.Parameters)
        {
            var paramType = TypeMapper.CSharpToOpal(param.TypeName);
            AppendLine($"§I{{{paramType}:{param.Name}}}");
        }

        // Output
        if (node.Output != null)
        {
            AppendLine($"§O{{{output}}}");
        }

        // Preconditions
        foreach (var pre in node.Preconditions)
        {
            Visit(pre);
        }

        // Postconditions
        foreach (var post in node.Postconditions)
        {
            Visit(post);
        }

        // Body (only for non-abstract methods)
        if (!node.IsAbstract)
        {
            foreach (var stmt in node.Body)
            {
                stmt.Accept(this);
            }
        }

        Dedent();
        AppendLine($"§/METHOD{{{node.Id}}}");

        return "";
    }

    public string Visit(FunctionNode node)
    {
        var visibility = GetVisibilityShorthand(node.Visibility);
        var typeParams = node.TypeParameters.Count > 0
            ? $"<{string.Join(",", node.TypeParameters.Select(tp => tp.Name))}>"
            : "";

        var output = node.Output != null ? TypeMapper.CSharpToOpal(node.Output.TypeName) : "void";

        AppendLine($"§F{{{node.Id}:{node.Name}{typeParams}:{visibility}}}");
        Indent();

        // Parameters
        foreach (var param in node.Parameters)
        {
            var paramType = TypeMapper.CSharpToOpal(param.TypeName);
            AppendLine($"§I{{{paramType}:{param.Name}}}");
        }

        // Output
        if (node.Output != null)
        {
            AppendLine($"§O{{{output}}}");
        }

        // Preconditions
        foreach (var pre in node.Preconditions)
        {
            Visit(pre);
        }

        // Postconditions
        foreach (var post in node.Postconditions)
        {
            Visit(post);
        }

        // Body
        foreach (var stmt in node.Body)
        {
            stmt.Accept(this);
        }

        Dedent();
        AppendLine($"§/F{{{node.Id}}}");

        return "";
    }

    public string Visit(ParameterNode node)
    {
        var typeName = TypeMapper.CSharpToOpal(node.TypeName);
        return $"§I{{{typeName}:{node.Name}}}";
    }

    public string Visit(RequiresNode node)
    {
        var condition = node.Condition.Accept(this);
        var message = node.Message != null ? $" \"{node.Message}\"" : "";
        AppendLine($"§REQ {condition}{message}");
        return "";
    }

    public string Visit(EnsuresNode node)
    {
        var condition = node.Condition.Accept(this);
        var message = node.Message != null ? $" \"{node.Message}\"" : "";
        AppendLine($"§ENS {condition}{message}");
        return "";
    }

    public string Visit(InvariantNode node)
    {
        var condition = node.Condition.Accept(this);
        var message = node.Message != null ? $" \"{node.Message}\"" : "";
        AppendLine($"§INV {condition}{message}");
        return "";
    }

    // Statements

    public string Visit(ReturnStatementNode node)
    {
        if (node.Expression != null)
        {
            var expr = node.Expression.Accept(this);
            AppendLine($"§R {expr}");
        }
        else
        {
            AppendLine("§R");
        }
        return "";
    }

    public string Visit(CallStatementNode node)
    {
        // Arguments need §A prefix and call needs §/C closing tag
        var args = node.Arguments.Select(a => $"§A {a.Accept(this)}");
        var argsStr = node.Arguments.Count > 0 ? $" {string.Join(" ", args)}" : "";
        AppendLine($"§C{{{node.Target}}}{argsStr} §/C");
        return "";
    }

    public string Visit(PrintStatementNode node)
    {
        var expr = node.Expression.Accept(this);
        var tag = node.IsWriteLine ? "§P" : "§Pf";
        AppendLine($"{tag} {expr}");
        return "";
    }

    public string Visit(ContinueStatementNode node)
    {
        AppendLine("§CONTINUE");
        return "";
    }

    public string Visit(BreakStatementNode node)
    {
        AppendLine("§BREAK");
        return "";
    }

    public string Visit(BindStatementNode node)
    {
        var typePart = node.TypeName != null ? $"{TypeMapper.CSharpToOpal(node.TypeName)}:" : "";
        var mutPart = node.IsMutable ? "" : ":const";
        // Parser expects: §B[type:name] expression (no = sign)
        var initPart = node.Initializer != null ? $" {node.Initializer.Accept(this)}" : "";

        AppendLine($"§B{{{typePart}{node.Name}{mutPart}}}{initPart}");
        return "";
    }

    public string Visit(AssignmentStatementNode node)
    {
        var target = node.Target.Accept(this);
        var value = node.Value.Accept(this);
        // Parser expects: §ASSIGN <target> <value>
        AppendLine($"§ASSIGN {target} {value}");
        return "";
    }

    public string Visit(CompoundAssignmentStatementNode node)
    {
        var target = node.Target.Accept(this);
        var value = node.Value.Accept(this);
        var opSymbol = node.Operator switch
        {
            CompoundAssignmentOperator.Add => "+",
            CompoundAssignmentOperator.Subtract => "-",
            CompoundAssignmentOperator.Multiply => "*",
            CompoundAssignmentOperator.Divide => "/",
            CompoundAssignmentOperator.Modulo => "%",
            CompoundAssignmentOperator.BitwiseAnd => "&",
            CompoundAssignmentOperator.BitwiseOr => "|",
            CompoundAssignmentOperator.BitwiseXor => "^",
            CompoundAssignmentOperator.LeftShift => "<<",
            CompoundAssignmentOperator.RightShift => ">>",
            _ => "+"
        };
        // Parser expects: §ASSIGN <target> <value>
        AppendLine($"§ASSIGN {target} ({opSymbol} {target} {value})");
        return "";
    }

    public string Visit(UsingStatementNode node)
    {
        // The parser doesn't support §USING statements, so emit as try/finally
        // which is semantically equivalent (how C# compiles using statements)
        var typePart = node.VariableType != null ? TypeMapper.CSharpToOpal(node.VariableType) + ":" : "";
        var namePart = node.VariableName ?? "_using_resource";
        var resource = node.Resource.Accept(this);
        var tryId = $"using_{_usingCounter++}";

        // Bind the resource variable
        AppendLine($"§B{{{typePart}{namePart}}} {resource}");

        // Wrap body in try/finally to ensure disposal
        AppendLine($"§TRY{{{tryId}}}");
        Indent();

        foreach (var stmt in node.Body)
        {
            stmt.Accept(this);
        }

        Dedent();
        AppendLine("§FINALLY");
        Indent();
        // Dispose the resource if not null
        AppendLine($"§IF{{{tryId}_dispose] (!= {namePart} null)");
        Indent();
        AppendLine($"§C{{{namePart}.Dispose] §/C");
        Dedent();
        AppendLine($"§/I{{{tryId}_dispose]");
        Dedent();
        AppendLine($"§/TRY{{{tryId}}}");
        return "";
    }
    private int _usingCounter = 0;

    public string Visit(IfStatementNode node)
    {
        var condition = node.Condition.Accept(this);

        AppendLine($"§IF{{{node.Id}}} {condition}");
        Indent();

        foreach (var stmt in node.ThenBody)
        {
            stmt.Accept(this);
        }

        Dedent();

        // ElseIf clauses
        foreach (var elseIf in node.ElseIfClauses)
        {
            var elseIfCondition = elseIf.Condition.Accept(this);
            AppendLine($"§EI {elseIfCondition}");
            Indent();

            foreach (var stmt in elseIf.Body)
            {
                stmt.Accept(this);
            }

            Dedent();
        }

        // Else clause
        if (node.ElseBody != null)
        {
            AppendLine("§EL");
            Indent();

            foreach (var stmt in node.ElseBody)
            {
                stmt.Accept(this);
            }

            Dedent();
        }

        AppendLine($"§/I{{{node.Id}}}");
        return "";
    }

    public string Visit(ForStatementNode node)
    {
        var from = node.From.Accept(this);
        var to = node.To.Accept(this);
        var step = node.Step?.Accept(this) ?? "1";

        AppendLine($"§L{{{node.Id}:{node.VariableName}:{from}:{to}:{step}}}");
        Indent();

        foreach (var stmt in node.Body)
        {
            stmt.Accept(this);
        }

        Dedent();
        AppendLine($"§/L{{{node.Id}}}");
        return "";
    }

    public string Visit(WhileStatementNode node)
    {
        var condition = node.Condition.Accept(this);

        AppendLine($"§WHILE{{{node.Id}}} {condition}");
        Indent();

        foreach (var stmt in node.Body)
        {
            stmt.Accept(this);
        }

        Dedent();
        AppendLine($"§/WH{{{node.Id}}}");
        return "";
    }

    public string Visit(DoWhileStatementNode node)
    {
        var condition = node.Condition.Accept(this);

        AppendLine($"§DO{{{node.Id}}}");
        Indent();

        foreach (var stmt in node.Body)
        {
            stmt.Accept(this);
        }

        Dedent();
        AppendLine($"§/DO{{{node.Id}}} {condition}");
        return "";
    }

    public string Visit(ForeachStatementNode node)
    {
        var collection = node.Collection.Accept(this);
        var varType = TypeMapper.CSharpToOpal(node.VariableType);

        AppendLine($"§EACH{{{node.Id}:{varType}:{node.VariableName}}} {collection}");
        Indent();

        foreach (var stmt in node.Body)
        {
            stmt.Accept(this);
        }

        Dedent();
        AppendLine($"§/EACH{{{node.Id}}}");
        return "";
    }

    public string Visit(TryStatementNode node)
    {
        AppendLine($"§TRY{{{node.Id}}}");
        Indent();

        foreach (var stmt in node.TryBody)
        {
            stmt.Accept(this);
        }

        Dedent();

        foreach (var catchClause in node.CatchClauses)
        {
            Visit(catchClause);
        }

        if (node.FinallyBody != null)
        {
            AppendLine("§FINALLY");
            Indent();

            foreach (var stmt in node.FinallyBody)
            {
                stmt.Accept(this);
            }

            Dedent();
        }

        AppendLine($"§/TRY{{{node.Id}}}");
        return "";
    }

    public string Visit(CatchClauseNode node)
    {
        var exType = node.ExceptionType ?? "Exception";
        var varPart = node.VariableName != null ? $":{node.VariableName}" : "";
        var filterPart = node.Filter != null ? $" when {node.Filter.Accept(this)}" : "";

        AppendLine($"§CATCH{{{exType}{varPart}}}{filterPart}");
        Indent();

        foreach (var stmt in node.Body)
        {
            stmt.Accept(this);
        }

        Dedent();
        return "";
    }

    public string Visit(ThrowStatementNode node)
    {
        if (node.Exception != null)
        {
            var expr = node.Exception.Accept(this);
            AppendLine($"§THROW {expr}");
        }
        else
        {
            AppendLine("§RETHROW");
        }
        return "";
    }

    public string Visit(RethrowStatementNode node)
    {
        AppendLine("§RETHROW");
        return "";
    }

    public string Visit(MatchStatementNode node)
    {
        var target = node.Target.Accept(this);
        var id = string.IsNullOrEmpty(node.Id) ? $"sw{_switchCounter++}" : node.Id;

        AppendLine($"§MATCH{{{id}}} {target}");
        Indent();

        foreach (var matchCase in node.Cases)
        {
            Visit(matchCase);
        }

        Dedent();
        AppendLine($"§/MATCH{{{id}}}");
        return "";
    }
    private int _switchCounter = 0;

    public string Visit(MatchCaseNode node)
    {
        var pattern = EmitPattern(node.Pattern);
        AppendLine($"§CASE {pattern} →");
        Indent();

        foreach (var stmt in node.Body)
        {
            stmt.Accept(this);
        }

        Dedent();
        return "";
    }

    // Expressions - return strings

    public string Visit(IntLiteralNode node)
    {
        return node.Value.ToString();
    }

    public string Visit(FloatLiteralNode node)
    {
        return node.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public string Visit(StringLiteralNode node)
    {
        var escaped = node.Value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
        return $"\"{escaped}\"";
    }

    public string Visit(BoolLiteralNode node)
    {
        return node.Value ? "true" : "false";
    }

    public string Visit(ConditionalExpressionNode node)
    {
        var condition = node.Condition.Accept(this);
        var whenTrue = node.WhenTrue.Accept(this);
        var whenFalse = node.WhenFalse.Accept(this);
        return $"(? {condition} {whenTrue} {whenFalse})";
    }

    public string Visit(ReferenceNode node)
    {
        return node.Name;
    }

    public string Visit(BinaryOperationNode node)
    {
        var left = node.Left.Accept(this);
        var right = node.Right.Accept(this);
        var opSymbol = GetOpalOperatorSymbol(node.Operator);

        // Use Lisp-style prefix notation: (op left right)
        return $"({opSymbol} {left} {right})";
    }

    public string Visit(UnaryOperationNode node)
    {
        var operand = node.Operand.Accept(this);
        var opSymbol = node.Operator switch
        {
            UnaryOperator.Negate => "-",
            UnaryOperator.Not => "!",
            UnaryOperator.BitwiseNot => "~",
            _ => "-"
        };

        return $"({opSymbol} {operand})";
    }

    public string Visit(FieldAccessNode node)
    {
        var target = node.Target.Accept(this);
        return $"{target}.{node.FieldName}";
    }

    public string Visit(NewExpressionNode node)
    {
        var typeArgs = node.TypeArguments.Count > 0
            ? $"<{string.Join(",", node.TypeArguments)}>"
            : "";
        // Arguments need §A prefix for parser to recognize them
        var args = node.Arguments.Select(a => $"§A {a.Accept(this)}");
        var argsStr = node.Arguments.Count > 0 ? $" {string.Join(" ", args)}" : "";

        // Handle object initializers
        var initStr = "";
        if (node.Initializers.Count > 0)
        {
            var inits = node.Initializers.Select(i => $"{i.PropertyName}: {i.Value.Accept(this)}");
            initStr = $" {{ {string.Join(", ", inits)} }}";
        }

        return $"§NEW{{{node.TypeName}{typeArgs}}}{argsStr}{initStr}";
    }

    public string Visit(CallExpressionNode node)
    {
        if (node.Arguments.Count == 0)
            return $"§C{{{node.Target}}} §/C";

        var args = node.Arguments.Select(a => $"§A {a.Accept(this)}");
        return $"§C{{{node.Target}}} {string.Join(" ", args)} §/C";
    }

    public string Visit(ThisExpressionNode node)
    {
        return "§THIS";
    }

    public string Visit(BaseExpressionNode node)
    {
        return "§BASE";
    }

    public string Visit(MatchExpressionNode node)
    {
        var target = node.Target.Accept(this);
        var cases = string.Join(", ", node.Cases.Select(c =>
        {
            var pattern = EmitPattern(c.Pattern);
            var body = c.Body.Count > 0 && c.Body[^1] is ReturnStatementNode ret && ret.Expression != null
                ? ret.Expression.Accept(this)
                : "default";
            return $"{pattern} → {body}";
        }));

        return $"(match {target} {{ {cases} }})";
    }

    public string Visit(SomeExpressionNode node)
    {
        var value = node.Value.Accept(this);
        return $"§SOME{{{value}}}";
    }

    public string Visit(NoneExpressionNode node)
    {
        var typePart = node.TypeName != null ? $"<{TypeMapper.CSharpToOpal(node.TypeName)}>" : "";
        return $"§NONE{typePart}";
    }

    public string Visit(OkExpressionNode node)
    {
        var value = node.Value.Accept(this);
        return $"§OK{{{value}}}";
    }

    public string Visit(ErrExpressionNode node)
    {
        var error = node.Error.Accept(this);
        return $"§ERR{{{error}}}";
    }

    public string Visit(ArrayCreationNode node)
    {
        var elementType = TypeMapper.CSharpToOpal(node.ElementType);

        if (node.Initializer.Count > 0)
        {
            var elements = string.Join(", ", node.Initializer.Select(e => e.Accept(this)));
            return $"{{{elements}}}";
        }
        else if (node.Size != null)
        {
            var size = node.Size.Accept(this);
            return $"§ARR{{{elementType}:{node.Name}:{size}}}";
        }
        else
        {
            return $"§ARR{{{elementType}:{node.Name}}}";
        }
    }

    public string Visit(ArrayAccessNode node)
    {
        var array = node.Array.Accept(this);
        var index = node.Index.Accept(this);
        return $"{array}{{{index}}}";
    }

    public string Visit(ArrayLengthNode node)
    {
        var array = node.Array.Accept(this);
        return $"{array}.len";
    }

    public string Visit(LambdaExpressionNode node)
    {
        var asyncPart = node.IsAsync ? "async " : "";
        var paramList = string.Join(", ", node.Parameters.Select(p =>
            p.TypeName != null ? $"{TypeMapper.CSharpToOpal(p.TypeName)}:{p.Name}" : p.Name));

        if (node.IsExpressionLambda && node.ExpressionBody != null)
        {
            var body = node.ExpressionBody.Accept(this);
            return $"{asyncPart}({paramList}) → {body}";
        }
        else
        {
            return $"{asyncPart}({paramList}) → {{ ... }}";
        }
    }

    public string Visit(LambdaParameterNode node)
    {
        if (node.TypeName != null)
        {
            return $"{TypeMapper.CSharpToOpal(node.TypeName)}:{node.Name}";
        }
        return node.Name;
    }

    public string Visit(AwaitExpressionNode node)
    {
        var awaited = node.Awaited.Accept(this);
        return $"§AWAIT {awaited}";
    }

    public string Visit(InterpolatedStringNode node)
    {
        var parts = new StringBuilder();
        parts.Append("\"");

        foreach (var part in node.Parts)
        {
            if (part is InterpolatedStringTextNode textPart)
            {
                parts.Append(textPart.Text);
            }
            else if (part is InterpolatedStringExpressionNode exprPart)
            {
                parts.Append("${");
                parts.Append(exprPart.Expression.Accept(this));
                parts.Append("}");
            }
        }

        parts.Append("\"");
        return parts.ToString();
    }

    public string Visit(InterpolatedStringTextNode node)
    {
        return node.Text;
    }

    public string Visit(InterpolatedStringExpressionNode node)
    {
        return $"${{{node.Expression.Accept(this)}}}";
    }

    public string Visit(NullCoalesceNode node)
    {
        var left = node.Left.Accept(this);
        var right = node.Right.Accept(this);
        return $"({left} ?? {right})";
    }

    public string Visit(NullConditionalNode node)
    {
        var target = node.Target.Accept(this);
        return $"{target}?.{node.MemberName}";
    }

    // Pattern-related methods

    private string EmitPattern(PatternNode pattern)
    {
        return pattern switch
        {
            WildcardPatternNode => "_",
            VariablePatternNode vp => $"var {vp.Name}",
            LiteralPatternNode lp => lp.Literal.Accept(this),
            SomePatternNode sp => $"some({EmitPattern(sp.InnerPattern)})",
            NonePatternNode => "none",
            OkPatternNode op => $"ok({EmitPattern(op.InnerPattern)})",
            ErrPatternNode ep => $"err({EmitPattern(ep.InnerPattern)})",
            VarPatternNode varp => $"var {varp.Name}",
            ConstantPatternNode cp => cp.Value.Accept(this),
            _ => "_"
        };
    }

    public string Visit(WildcardPatternNode node) => "_";
    public string Visit(VariablePatternNode node) => $"var {node.Name}";
    public string Visit(LiteralPatternNode node) => node.Literal.Accept(this);
    public string Visit(SomePatternNode node) => $"some({EmitPattern(node.InnerPattern)})";
    public string Visit(NonePatternNode node) => "none";
    public string Visit(OkPatternNode node) => $"ok({EmitPattern(node.InnerPattern)})";
    public string Visit(ErrPatternNode node) => $"err({EmitPattern(node.InnerPattern)})";
    public string Visit(VarPatternNode node) => $"var {node.Name}";
    public string Visit(ConstantPatternNode node) => node.Value.Accept(this);

    // Additional pattern nodes
    public string Visit(PositionalPatternNode node)
    {
        var patterns = string.Join(", ", node.Patterns.Select(EmitPattern));
        return $"{node.TypeName}({patterns})";
    }

    public string Visit(PropertyPatternNode node)
    {
        var matches = string.Join(", ", node.Matches.Select(m => m.Accept(this)));
        var typePart = string.IsNullOrEmpty(node.TypeName) ? "" : $"{node.TypeName} ";
        return $"{typePart}{{ {matches} }}";
    }

    public string Visit(PropertyMatchNode node)
    {
        return $"{node.PropertyName}: {EmitPattern(node.Pattern)}";
    }

    public string Visit(RelationalPatternNode node)
    {
        var value = node.Value.Accept(this);
        return $"{node.Operator} {value}";
    }

    public string Visit(ListPatternNode node)
    {
        var patterns = string.Join(", ", node.Patterns.Select(EmitPattern));
        var slice = node.SlicePattern != null ? $", ..{EmitPattern(node.SlicePattern)}" : "";
        return $"{{{patterns}{slice}}}";
    }

    // Type system nodes
    public string Visit(RecordDefinitionNode node)
    {
        var fields = string.Join(", ", node.Fields.Select(f =>
            $"{TypeMapper.CSharpToOpal(f.TypeName)}:{f.Name}"));
        AppendLine($"§RECORD{{{node.Name}}} ({fields})");
        return "";
    }

    public string Visit(UnionTypeDefinitionNode node)
    {
        AppendLine($"§UNION{{{node.Name}}}");
        Indent();
        foreach (var variant in node.Variants)
        {
            var fields = variant.Fields.Count > 0
                ? $"({string.Join(", ", variant.Fields.Select(f => $"{TypeMapper.CSharpToOpal(f.TypeName)}:{f.Name}"))})"
                : "";
            AppendLine($"§V{{{variant.Name}}}{fields}");
        }
        Dedent();
        AppendLine("§/UNION");
        return "";
    }

    public string Visit(RecordCreationNode node)
    {
        var fields = string.Join(", ", node.Fields.Select(f => f.Value.Accept(this)));
        return $"§NEW{{{node.TypeName}}} {fields}";
    }

    // Generic type nodes
    public string Visit(TypeParameterNode node) => node.Name;
    public string Visit(TypeConstraintNode node) => node.TypeName ?? "";
    public string Visit(GenericTypeNode node)
    {
        if (node.TypeArguments.Count == 0)
            return TypeMapper.CSharpToOpal(node.TypeName);
        var args = string.Join(", ", node.TypeArguments.Select(TypeMapper.CSharpToOpal));
        return $"{TypeMapper.CSharpToOpal(node.TypeName)}<{args}>";
    }

    // Delegate and event nodes
    public string Visit(DelegateDefinitionNode node)
    {
        var output = node.Output != null ? TypeMapper.CSharpToOpal(node.Output.TypeName) : "void";
        var paramList = string.Join(", ", node.Parameters.Select(p =>
            $"{TypeMapper.CSharpToOpal(p.TypeName)}:{p.Name}"));
        AppendLine($"§DELEGATE{{{node.Name}}} ({paramList}) → {output}");
        return "";
    }

    public string Visit(EventDefinitionNode node)
    {
        // Events are emitted as fields since the parser doesn't support §EVT in class bodies
        var visibility = GetVisibilityShorthand(node.Visibility);
        var delegateType = TypeMapper.CSharpToOpal(node.DelegateType);
        AppendLine($"§FLD{{{delegateType}:{node.Name}:{visibility}}}");
        return "";
    }

    public string Visit(EventSubscribeNode node)
    {
        var evt = node.Event.Accept(this);
        var handler = node.Handler.Accept(this);
        AppendLine($"§SUB {evt} += {handler}");
        return "";
    }

    public string Visit(EventUnsubscribeNode node)
    {
        var evt = node.Event.Accept(this);
        var handler = node.Handler.Accept(this);
        AppendLine($"§UNSUB {evt} -= {handler}");
        return "";
    }

    // Modern operator nodes
    public string Visit(RangeExpressionNode node)
    {
        var start = node.Start?.Accept(this) ?? "";
        var end = node.End?.Accept(this) ?? "";
        return $"{start}..{end}";
    }

    public string Visit(IndexFromEndNode node)
    {
        var offset = node.Offset.Accept(this);
        return $"^{offset}";
    }

    public string Visit(WithExpressionNode node)
    {
        var target = node.Target.Accept(this);
        var assignments = string.Join(", ", node.Assignments.Select(a => a.Accept(this)));
        return $"{target} with {{ {assignments} }}";
    }

    public string Visit(WithPropertyAssignmentNode node)
    {
        var value = node.Value.Accept(this);
        return $"{node.PropertyName} = {value}";
    }

    // Extended metadata nodes - emit as comments
    public string Visit(ExampleNode node)
    {
        var expr = node.Expression.Accept(this);
        var expected = node.Expected.Accept(this);
        AppendLine($"§EX{{{node.Id ?? ""}}} {expr} == {expected}");
        return "";
    }

    public string Visit(IssueNode node)
    {
        var id = node.Id != null ? $"{node.Id}:" : "";
        AppendLine($"§{node.Kind.ToString().ToUpper()}{{{id}{node.Category ?? ""}}} {node.Description}");
        return "";
    }

    public string Visit(DependencyNode node)
    {
        var version = node.Version != null ? $"@{node.Version}" : "";
        var optional = node.IsOptional ? "?" : "";
        return $"{node.Target}{version}{optional}";
    }

    public string Visit(UsesNode node)
    {
        var deps = string.Join(", ", node.Dependencies.Select(d => d.Accept(this)));
        AppendLine($"§USES {deps}");
        return "";
    }

    public string Visit(UsedByNode node)
    {
        var deps = string.Join(", ", node.Dependents.Select(d => d.Accept(this)));
        var external = node.HasUnknownCallers ? ", [external]" : "";
        AppendLine($"§USEDBY {deps}{external}");
        return "";
    }

    public string Visit(AssumeNode node)
    {
        var category = node.Category.HasValue ? $"{{{node.Category.Value.ToString().ToLower()}}}" : "";
        AppendLine($"§ASSUME{category} {node.Description}");
        return "";
    }

    public string Visit(ComplexityNode node)
    {
        var parts = new List<string>();
        if (node.TimeComplexity.HasValue) parts.Add($"time:{FormatComplexity(node.TimeComplexity.Value)}");
        if (node.SpaceComplexity.HasValue) parts.Add($"space:{FormatComplexity(node.SpaceComplexity.Value)}");
        if (node.CustomExpression != null) parts.Add(node.CustomExpression);
        var worst = node.IsWorstCase ? "worst:" : "";
        AppendLine($"§COMPLEXITY{{{worst}{string.Join(",", parts)}}}");
        return "";
    }

    public string Visit(SinceNode node)
    {
        AppendLine($"§SINCE{{{node.Version}}}");
        return "";
    }

    public string Visit(DeprecatedNode node)
    {
        var replacement = node.Replacement != null ? $":use={node.Replacement}" : "";
        var removed = node.RemovedInVersion != null ? $":removed={node.RemovedInVersion}" : "";
        AppendLine($"§DEPRECATED{{{node.SinceVersion}{replacement}{removed}}}");
        return "";
    }

    public string Visit(BreakingChangeNode node)
    {
        AppendLine($"§BREAKING{{{node.Version}}} {node.Description}");
        return "";
    }

    public string Visit(DecisionNode node)
    {
        AppendLine($"§DECISION{{{node.Id}:{node.Title}}}");
        Indent();
        AppendLine($"chosen: {node.ChosenOption}");
        foreach (var reason in node.ChosenReasons)
        {
            AppendLine($"reason: {reason}");
        }
        Dedent();
        AppendLine("§/DECISION");
        return "";
    }

    public string Visit(RejectedOptionNode node)
    {
        AppendLine($"rejected: {node.Name}");
        foreach (var reason in node.Reasons)
        {
            AppendLine($"  reason: {reason}");
        }
        return "";
    }

    public string Visit(ContextNode node)
    {
        var partial = node.IsPartial ? ":partial" : "";
        AppendLine($"§CONTEXT{partial}");
        return "";
    }

    public string Visit(FileRefNode node)
    {
        var desc = node.Description != null ? $" ({node.Description})" : "";
        return $"§FILE{{{node.FilePath}}}{desc}";
    }

    public string Visit(PropertyTestNode node)
    {
        var quantifiers = node.Quantifiers.Count > 0 ? $"∀{string.Join(",", node.Quantifiers)}: " : "";
        var predicate = node.Predicate.Accept(this);
        AppendLine($"§PROP{{{quantifiers}{predicate}}}");
        return "";
    }

    public string Visit(LockNode node)
    {
        var acquired = node.Acquired.HasValue ? $":acquired={node.Acquired.Value:O}" : "";
        var expires = node.Expires.HasValue ? $":expires={node.Expires.Value:O}" : "";
        AppendLine($"§LOCK{{agent={node.AgentId}{acquired}{expires}}}");
        return "";
    }

    public string Visit(AuthorNode node)
    {
        var task = node.TaskId != null ? $":task={node.TaskId}" : "";
        AppendLine($"§AUTHOR{{agent={node.AgentId}:date={node.Date:yyyy-MM-dd}{task}}}");
        return "";
    }

    public string Visit(TaskRefNode node)
    {
        AppendLine($"§TASK{{{node.TaskId}}} {node.Description}");
        return "";
    }

    // Helper methods

    private static string GetVisibilityShorthand(Visibility visibility)
    {
        return visibility switch
        {
            Visibility.Public => "pub",
            Visibility.Protected => "prot",
            Visibility.Internal => "int",
            Visibility.Private => "priv",
            _ => "priv"
        };
    }

    private static string GetOpalOperatorSymbol(BinaryOperator op) => op switch
    {
        BinaryOperator.Add => "+",
        BinaryOperator.Subtract => "-",
        BinaryOperator.Multiply => "*",
        BinaryOperator.Divide => "/",
        BinaryOperator.Modulo => "%",
        BinaryOperator.Power => "**",
        BinaryOperator.Equal => "==",
        BinaryOperator.NotEqual => "!=",
        BinaryOperator.LessThan => "<",
        BinaryOperator.LessOrEqual => "<=",
        BinaryOperator.GreaterThan => ">",
        BinaryOperator.GreaterOrEqual => ">=",
        BinaryOperator.And => "&&",
        BinaryOperator.Or => "||",
        BinaryOperator.BitwiseAnd => "&",
        BinaryOperator.BitwiseOr => "|",
        BinaryOperator.BitwiseXor => "^",
        BinaryOperator.LeftShift => "<<",
        BinaryOperator.RightShift => ">>",
        _ => "+"
    };

    private static string GetOpalOperatorKind(BinaryOperator op)
    {
        return op switch
        {
            BinaryOperator.Add => "add",
            BinaryOperator.Subtract => "sub",
            BinaryOperator.Multiply => "mul",
            BinaryOperator.Divide => "div",
            BinaryOperator.Modulo => "mod",
            BinaryOperator.Power => "pow",
            BinaryOperator.Equal => "eq",
            BinaryOperator.NotEqual => "neq",
            BinaryOperator.LessThan => "lt",
            BinaryOperator.LessOrEqual => "lte",
            BinaryOperator.GreaterThan => "gt",
            BinaryOperator.GreaterOrEqual => "gte",
            BinaryOperator.And => "and",
            BinaryOperator.Or => "or",
            BinaryOperator.BitwiseAnd => "band",
            BinaryOperator.BitwiseOr => "bor",
            BinaryOperator.BitwiseXor => "xor",
            BinaryOperator.LeftShift => "shl",
            BinaryOperator.RightShift => "shr",
            _ => "add"
        };
    }

    private static string FormatComplexity(ComplexityClass c)
    {
        return c switch
        {
            ComplexityClass.O1 => "O(1)",
            ComplexityClass.OLogN => "O(logn)",
            ComplexityClass.ON => "O(n)",
            ComplexityClass.ONLogN => "O(nlogn)",
            ComplexityClass.ON2 => "O(n²)",
            ComplexityClass.ON3 => "O(n³)",
            ComplexityClass.O2N => "O(2ⁿ)",
            ComplexityClass.ONFact => "O(n!)",
            _ => c.ToString()
        };
    }

    /// <summary>
    /// Emits C#-style attributes in the [@Attr] format.
    /// </summary>
    private string EmitCSharpAttributes(IReadOnlyList<OpalAttributeNode> attributes)
    {
        if (attributes.Count == 0)
            return "";

        var sb = new StringBuilder();
        foreach (var attr in attributes)
        {
            sb.Append(Visit(attr));
        }
        return sb.ToString();
    }

    public string Visit(OpalAttributeNode node)
    {
        if (node.Arguments.Count == 0)
        {
            return $"[@{node.Name}}}";
        }

        var args = string.Join(", ", node.Arguments.Select(FormatAttributeArgument));
        return $"[@{node.Name}({args})]";
    }

    private static string FormatAttributeArgument(OpalAttributeArgument arg)
    {
        var value = arg.GetFormattedValue();

        if (arg.IsNamed)
        {
            return $"{arg.Name}={value}";
        }
        return value;
    }
}
