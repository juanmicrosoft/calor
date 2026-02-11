using System.Text;
using Calor.Compiler.Ast;

namespace Calor.Compiler.Migration;

/// <summary>
/// Emits Calor v2+ source code from an Calor AST.
/// Uses Lisp-style expressions and arrow syntax for control flow.
/// </summary>
public sealed class CalorEmitter : IAstVisitor<string>
{
    private StringBuilder _builder = new();
    private int _indentLevel;
    private readonly ConversionContext? _context;

    public CalorEmitter(ConversionContext? context = null)
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

    /// <summary>
    /// Captures statement output to a separate string instead of the main builder.
    /// Used for lambda statement bodies where we need to embed statements inline.
    /// </summary>
    private string CaptureStatementOutput(StatementNode stmt)
    {
        // Save current builder state
        var savedBuilder = _builder;
        var savedIndent = _indentLevel;

        // Create temporary builder
        _builder = new StringBuilder();
        _indentLevel = 0;

        // Visit the statement (this will append to the temp builder)
        stmt.Accept(this);

        // Capture result
        var result = _builder.ToString().TrimEnd('\r', '\n');

        // Restore original builder
        _builder = savedBuilder;
        _indentLevel = savedIndent;

        return result;
    }

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

        // Emit enums
        foreach (var enumDef in node.Enums)
        {
            Visit(enumDef);
            AppendLine();
        }

        // Emit enum extensions
        foreach (var enumExt in node.EnumExtensions)
        {
            Visit(enumExt);
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
            AppendLine($"§U{{static:{node.Namespace}}}");
        }
        else if (node.Alias != null)
        {
            AppendLine($"§U{{{node.Alias}={node.Namespace}}}");
        }
        else
        {
            AppendLine($"§U{{{node.Namespace}}}");
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

        var output = node.Output != null ? TypeMapper.CSharpToCalor(node.Output.TypeName) : "void";
        var paramList = string.Join(",", node.Parameters.Select(p =>
            $"{TypeMapper.CSharpToCalor(p.TypeName)}:{p.Name}"));
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

        AppendLine($"§CL{{{node.Id}:{node.Name}{typeParams}{baseStr}{modStr}}}{attrs}");
        Indent();

        // Emit type parameter constraints
        EmitTypeParameterConstraints(node.TypeParameters);

        // Emit implemented interfaces
        foreach (var iface in node.ImplementedInterfaces)
        {
            AppendLine($"§IMPL{{{iface}}}");
        }
        if (node.ImplementedInterfaces.Count > 0 || node.TypeParameters.Any(tp => tp.Constraints.Count > 0))
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
        AppendLine($"§/CL{{{node.Id}}}");

        return "";
    }

    public string Visit(ClassFieldNode node)
    {
        var visibility = GetVisibilityShorthand(node.Visibility);
        var typeName = TypeMapper.CSharpToCalor(node.TypeName);
        var defaultVal = node.DefaultValue != null ? $" = {node.DefaultValue.Accept(this)}" : "";
        var attrs = EmitCSharpAttributes(node.CSharpAttributes);

        AppendLine($"§FLD{{{typeName}:{node.Name}:{visibility}}}{attrs}{defaultVal}");

        return "";
    }

    public string Visit(PropertyNode node)
    {
        var visibility = GetVisibilityShorthand(node.Visibility);
        var typeName = TypeMapper.CSharpToCalor(node.TypeName);
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
            var paramType = TypeMapper.CSharpToCalor(param.TypeName);
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

        var output = node.Output != null ? TypeMapper.CSharpToCalor(node.Output.TypeName) : "void";
        var attrs = EmitCSharpAttributes(node.CSharpAttributes);

        // Use §AMT for async methods, §MT for regular methods
        var methodTag = node.IsAsync ? "AMT" : "MT";
        AppendLine($"§{methodTag}{{{node.Id}:{node.Name}{typeParams}:{visibility}{modStr}}}{attrs}");
        Indent();

        // Emit type parameter constraints
        EmitTypeParameterConstraints(node.TypeParameters);

        // Parameters
        foreach (var param in node.Parameters)
        {
            var paramType = TypeMapper.CSharpToCalor(param.TypeName);
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
        AppendLine($"§/{methodTag}{{{node.Id}}}");

        return "";
    }

    public string Visit(FunctionNode node)
    {
        var visibility = GetVisibilityShorthand(node.Visibility);
        var typeParams = node.TypeParameters.Count > 0
            ? $"<{string.Join(",", node.TypeParameters.Select(tp => tp.Name))}>"
            : "";

        var output = node.Output != null ? TypeMapper.CSharpToCalor(node.Output.TypeName) : "void";

        // Use §AF for async functions, §F for regular functions
        var funcTag = node.IsAsync ? "AF" : "F";
        AppendLine($"§{funcTag}{{{node.Id}:{node.Name}{typeParams}:{visibility}}}");
        Indent();

        // Emit type parameter constraints
        EmitTypeParameterConstraints(node.TypeParameters);

        // Parameters
        foreach (var param in node.Parameters)
        {
            var paramType = TypeMapper.CSharpToCalor(param.TypeName);
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
        AppendLine($"§/{funcTag}{{{node.Id}}}");

        return "";
    }

    public string Visit(ParameterNode node)
    {
        var typeName = TypeMapper.CSharpToCalor(node.TypeName);
        return $"§I{{{typeName}:{node.Name}}}";
    }

    public string Visit(RequiresNode node)
    {
        var condition = node.Condition.Accept(this);
        var message = node.Message != null ? $" \"{node.Message}\"" : "";
        AppendLine($"§Q {condition}{message}");
        return "";
    }

    public string Visit(EnsuresNode node)
    {
        var condition = node.Condition.Accept(this);
        var message = node.Message != null ? $" \"{node.Message}\"" : "";
        AppendLine($"§S {condition}{message}");
        return "";
    }

    public string Visit(InvariantNode node)
    {
        var condition = node.Condition.Accept(this);
        var message = node.Message != null ? $" \"{node.Message}\"" : "";
        AppendLine($"§IV {condition}{message}");
        return "";
    }

    // Statements

    public string Visit(ReturnStatementNode node)
    {
        if (node.Expression != null)
        {
            // If the expression is a match expression, emit it as a statement block
            // since each case already contains a return statement
            if (node.Expression is MatchExpressionNode matchExpr)
            {
                EmitMatchExpressionAsStatement(matchExpr);
            }
            else
            {
                var expr = node.Expression.Accept(this);
                AppendLine($"§R {expr}");
            }
        }
        else
        {
            AppendLine("§R");
        }
        return "";
    }

    /// <summary>
    /// Emits a MatchExpressionNode as a statement block instead of inline expression.
    /// Used when the match expression is the direct child of a return statement.
    /// Uses :expr suffix to distinguish from match statements when parsing back.
    /// </summary>
    private void EmitMatchExpressionAsStatement(MatchExpressionNode node)
    {
        var target = node.Target.Accept(this);
        var id = string.IsNullOrEmpty(node.Id) ? $"sw{_switchCounter++}" : node.Id;

        // Add :expr suffix to indicate this is a match expression (not statement)
        AppendLine($"§W{{{id}:expr}} {target}");
        Indent();

        foreach (var matchCase in node.Cases)
        {
            Visit(matchCase);
        }

        Dedent();
        AppendLine($"§/W{{{id}}}");
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
        AppendLine("§CN");
        return "";
    }

    public string Visit(BreakStatementNode node)
    {
        AppendLine("§BK");
        return "";
    }

    public string Visit(BindStatementNode node)
    {
        // Handle collection initializers specially - emit as collection block syntax
        if (node.Initializer is ListCreationNode listNode)
        {
            EmitListCreationWithName(listNode, node.Name);
            return "";
        }
        if (node.Initializer is DictionaryCreationNode dictNode)
        {
            EmitDictionaryCreationWithName(dictNode, node.Name);
            return "";
        }
        if (node.Initializer is SetCreationNode setNode)
        {
            EmitSetCreationWithName(setNode, node.Name);
            return "";
        }

        var typePart = node.TypeName != null ? $"{TypeMapper.CSharpToCalor(node.TypeName)}:" : "";
        var mutPart = node.IsMutable ? "" : ":const";
        // Parser expects: §B[type:name] expression (no = sign)
        var initPart = node.Initializer != null ? $" {node.Initializer.Accept(this)}" : "";

        AppendLine($"§B{{{typePart}{node.Name}{mutPart}}}{initPart}");
        return "";
    }

    private void EmitListCreationWithName(ListCreationNode node, string variableName)
    {
        var elementType = TypeMapper.CSharpToCalor(node.ElementType);

        AppendLine($"§LIST{{{variableName}:{elementType}}}");
        Indent();

        foreach (var element in node.Elements)
        {
            AppendLine(element.Accept(this));
        }

        Dedent();
        AppendLine($"§/LIST{{{variableName}}}");
    }

    private void EmitDictionaryCreationWithName(DictionaryCreationNode node, string variableName)
    {
        var keyType = TypeMapper.CSharpToCalor(node.KeyType);
        var valueType = TypeMapper.CSharpToCalor(node.ValueType);

        AppendLine($"§DICT{{{variableName}:{keyType}:{valueType}}}");
        Indent();

        foreach (var entry in node.Entries)
        {
            var key = entry.Key.Accept(this);
            var value = entry.Value.Accept(this);
            AppendLine($"§KV {key} {value}");
        }

        Dedent();
        AppendLine($"§/DICT{{{variableName}}}");
    }

    private void EmitSetCreationWithName(SetCreationNode node, string variableName)
    {
        var elementType = TypeMapper.CSharpToCalor(node.ElementType);

        AppendLine($"§HSET{{{variableName}:{elementType}}}");
        Indent();

        foreach (var element in node.Elements)
        {
            AppendLine(element.Accept(this));
        }

        Dedent();
        AppendLine($"§/HSET{{{variableName}}}");
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
        var typePart = node.VariableType != null ? TypeMapper.CSharpToCalor(node.VariableType) + ":" : "";
        var namePart = node.VariableName ?? "_using_resource";
        var resource = node.Resource.Accept(this);
        var tryId = $"using_{_usingCounter++}";

        // Bind the resource variable
        AppendLine($"§B{{{typePart}{namePart}}} {resource}");

        // Wrap body in try/finally to ensure disposal
        AppendLine($"§TR{{{tryId}}}");
        Indent();

        foreach (var stmt in node.Body)
        {
            stmt.Accept(this);
        }

        Dedent();
        AppendLine("§FI");
        Indent();
        // Dispose the resource if not null
        AppendLine($"§IF{{{tryId}_dispose] (!= {namePart} null)");
        Indent();
        AppendLine($"§C{{{namePart}.Dispose] §/C");
        Dedent();
        AppendLine($"§/I{{{tryId}_dispose]");
        Dedent();
        AppendLine($"§/TR{{{tryId}}}");
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

        AppendLine($"§WH{{{node.Id}}} {condition}");
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
        var varType = TypeMapper.CSharpToCalor(node.VariableType);

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

    // Phase 6 Extended: Collections (List, Dictionary, HashSet)

    public string Visit(ListCreationNode node)
    {
        var elementType = TypeMapper.CSharpToCalor(node.ElementType);

        AppendLine($"§LIST{{{node.Id}:{elementType}}}");
        Indent();

        foreach (var element in node.Elements)
        {
            AppendLine(element.Accept(this));
        }

        Dedent();
        AppendLine($"§/LIST{{{node.Id}}}");
        return "";
    }

    public string Visit(DictionaryCreationNode node)
    {
        var keyType = TypeMapper.CSharpToCalor(node.KeyType);
        var valueType = TypeMapper.CSharpToCalor(node.ValueType);

        AppendLine($"§DICT{{{node.Id}:{keyType}:{valueType}}}");
        Indent();

        foreach (var entry in node.Entries)
        {
            entry.Accept(this);
        }

        Dedent();
        AppendLine($"§/DICT{{{node.Id}}}");
        return "";
    }

    public string Visit(KeyValuePairNode node)
    {
        var key = node.Key.Accept(this);
        var value = node.Value.Accept(this);
        AppendLine($"§KV {key} {value}");
        return "";
    }

    public string Visit(SetCreationNode node)
    {
        var elementType = TypeMapper.CSharpToCalor(node.ElementType);

        AppendLine($"§HSET{{{node.Id}:{elementType}}}");
        Indent();

        foreach (var element in node.Elements)
        {
            AppendLine(element.Accept(this));
        }

        Dedent();
        AppendLine($"§/HSET{{{node.Id}}}");
        return "";
    }

    public string Visit(CollectionPushNode node)
    {
        var value = node.Value.Accept(this);
        AppendLine($"§PUSH{{{node.CollectionName}}} {value}");
        return "";
    }

    public string Visit(DictionaryPutNode node)
    {
        var key = node.Key.Accept(this);
        var value = node.Value.Accept(this);
        AppendLine($"§PUT{{{node.DictionaryName}}} {key} {value}");
        return "";
    }

    public string Visit(CollectionRemoveNode node)
    {
        var keyOrValue = node.KeyOrValue.Accept(this);
        AppendLine($"§REM{{{node.CollectionName}}} {keyOrValue}");
        return "";
    }

    public string Visit(CollectionSetIndexNode node)
    {
        var index = node.Index.Accept(this);
        var value = node.Value.Accept(this);
        AppendLine($"§SETIDX{{{node.CollectionName}}} {index} {value}");
        return "";
    }

    public string Visit(CollectionClearNode node)
    {
        AppendLine($"§CLR{{{node.CollectionName}}}");
        return "";
    }

    public string Visit(CollectionInsertNode node)
    {
        var index = node.Index.Accept(this);
        var value = node.Value.Accept(this);
        AppendLine($"§INS{{{node.CollectionName}}} {index} {value}");
        return "";
    }

    public string Visit(CollectionContainsNode node)
    {
        var keyOrValue = node.KeyOrValue.Accept(this);
        var modePrefix = node.Mode switch
        {
            ContainsMode.Key => "§KEY ",
            ContainsMode.DictValue => "§VAL ",
            _ => ""
        };
        return $"§HAS{{{node.CollectionName}}} {modePrefix}{keyOrValue}";
    }

    public string Visit(DictionaryForeachNode node)
    {
        var dictionary = node.Dictionary.Accept(this);

        AppendLine($"§EACHKV{{{node.Id}:{node.KeyName}:{node.ValueName}}} {dictionary}");
        Indent();

        foreach (var stmt in node.Body)
        {
            stmt.Accept(this);
        }

        Dedent();
        AppendLine($"§/EACHKV{{{node.Id}}}");
        return "";
    }

    public string Visit(CollectionCountNode node)
    {
        var collection = node.Collection.Accept(this);
        return $"§CNT {collection}";
    }

    public string Visit(TryStatementNode node)
    {
        AppendLine($"§TR{{{node.Id}}}");
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
            AppendLine("§FI");
            Indent();

            foreach (var stmt in node.FinallyBody)
            {
                stmt.Accept(this);
            }

            Dedent();
        }

        AppendLine($"§/TR{{{node.Id}}}");
        return "";
    }

    public string Visit(CatchClauseNode node)
    {
        var exType = node.ExceptionType ?? "Exception";
        var varPart = node.VariableName != null ? $":{node.VariableName}" : "";
        var filterPart = node.Filter != null ? $" when {node.Filter.Accept(this)}" : "";

        AppendLine($"§CA{{{exType}{varPart}}}{filterPart}");
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
            AppendLine($"§TH {expr}");
        }
        else
        {
            AppendLine("§RT");
        }
        return "";
    }

    public string Visit(RethrowStatementNode node)
    {
        AppendLine("§RT");
        return "";
    }

    public string Visit(MatchStatementNode node)
    {
        var target = node.Target.Accept(this);
        var id = string.IsNullOrEmpty(node.Id) ? $"sw{_switchCounter++}" : node.Id;

        AppendLine($"§W{{{id}}} {target}");
        Indent();

        foreach (var matchCase in node.Cases)
        {
            Visit(matchCase);
        }

        Dedent();
        AppendLine($"§/W{{{id}}}");
        return "";
    }
    private int _switchCounter = 0;

    public string Visit(MatchCaseNode node)
    {
        var pattern = EmitPattern(node.Pattern);
        var guard = node.Guard != null ? $" §WHEN {node.Guard.Accept(this)}" : "";

        // Check if this is a single-return case suitable for arrow syntax
        if (node.Body.Count == 1 && node.Body[0] is ReturnStatementNode returnStmt && returnStmt.Expression != null)
        {
            // Use arrow syntax: §K pattern → expression
            var expr = returnStmt.Expression.Accept(this);
            AppendLine($"§K {pattern}{guard} → {expr}");
        }
        else
        {
            // Use block syntax with §/K closing tag
            AppendLine($"§K {pattern}{guard}");
            Indent();

            foreach (var stmt in node.Body)
            {
                stmt.Accept(this);
            }

            Dedent();
            AppendLine("§/K");
        }

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
        var opSymbol = GetCalorOperatorSymbol(node.Operator);

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
        // Escape braces in target to avoid conflicts with Calor tag syntax
        var escapedTarget = EscapeBraces(node.Target);

        if (node.Arguments.Count == 0)
            return $"§C{{{escapedTarget}}} §/C";

        var args = node.Arguments.Select(a => $"§A {a.Accept(this)}");
        return $"§C{{{escapedTarget}}} {string.Join(" ", args)} §/C";
    }

    /// <summary>
    /// Escapes braces in a string to avoid conflicts with Calor tag syntax.
    /// { becomes \{ and } becomes \}
    /// </summary>
    private static string EscapeBraces(string input)
    {
        if (!input.Contains('{') && !input.Contains('}'))
            return input;

        return input.Replace("{", "\\{").Replace("}", "\\}");
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
        // Use block syntax that the Calor parser can understand
        // §W{id} target
        // §K pattern
        //     body statements
        // §/W{id}
        var target = node.Target.Accept(this);
        var id = string.IsNullOrEmpty(node.Id) ? $"sw{_switchCounter++}" : node.Id;

        var sb = new StringBuilder();
        sb.AppendLine($"§W{{{id}}} {target}");

        foreach (var matchCase in node.Cases)
        {
            var pattern = EmitPattern(matchCase.Pattern);
            sb.AppendLine($"  §K {pattern}");

            foreach (var stmt in matchCase.Body)
            {
                var stmtStr = CaptureStatementOutput(stmt);
                if (!string.IsNullOrWhiteSpace(stmtStr))
                {
                    sb.AppendLine($"    {stmtStr.Trim()}");
                }
            }
        }

        sb.Append($"§/W{{{id}}}");
        return sb.ToString();
    }

    public string Visit(SomeExpressionNode node)
    {
        var value = node.Value.Accept(this);
        return $"§SM {value}";
    }

    public string Visit(NoneExpressionNode node)
    {
        var typePart = node.TypeName != null ? $"{{{TypeMapper.CSharpToCalor(node.TypeName)}}}" : "";
        return $"§NN{typePart}";
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
        var elementType = TypeMapper.CSharpToCalor(node.ElementType);

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
        // Use §IDX syntax for element access
        return $"§IDX{{{array}}} {index}";
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
            p.TypeName != null ? $"{TypeMapper.CSharpToCalor(p.TypeName)}:{p.Name}" : p.Name));

        if (node.IsExpressionLambda && node.ExpressionBody != null)
        {
            var body = node.ExpressionBody.Accept(this);
            return $"{asyncPart}({paramList}) → {body}";
        }
        else if (node.StatementBody != null && node.StatementBody.Count > 0)
        {
            // Emit statement lambda with block syntax
            // Use CaptureStatementOutput to avoid appending to main builder
            var sb = new System.Text.StringBuilder();
            sb.Append($"{asyncPart}({paramList}) → {{");

            // For short lambdas (1-2 statements), emit inline
            if (node.StatementBody.Count <= 2)
            {
                var stmts = node.StatementBody.Select(s => CaptureStatementOutput(s).Trim()).ToList();
                sb.Append(" ");
                sb.Append(string.Join(" ", stmts));
                sb.Append(" }");
            }
            else
            {
                // For longer lambdas, emit multi-line
                sb.AppendLine();
                var indent = "  ";
                foreach (var stmt in node.StatementBody)
                {
                    var stmtStr = CaptureStatementOutput(stmt);
                    if (!string.IsNullOrWhiteSpace(stmtStr))
                    {
                        sb.Append(indent);
                        sb.AppendLine(stmtStr.Trim());
                    }
                }
                sb.Append("}");
            }
            return sb.ToString();
        }
        else
        {
            // Empty lambda
            return $"{asyncPart}({paramList}) → {{ }}";
        }
    }

    public string Visit(LambdaParameterNode node)
    {
        if (node.TypeName != null)
        {
            return $"{TypeMapper.CSharpToCalor(node.TypeName)}:{node.Name}";
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
            VariablePatternNode vp => $"§VAR{{{vp.Name}}}",
            LiteralPatternNode lp => lp.Literal.Accept(this),
            SomePatternNode sp => $"§SM {EmitPattern(sp.InnerPattern)}",
            NonePatternNode => "§NN",
            OkPatternNode op => $"§OK {EmitPattern(op.InnerPattern)}",
            ErrPatternNode ep => $"§ERR {EmitPattern(ep.InnerPattern)}",
            VarPatternNode varp => $"§VAR{{{varp.Name}}}",
            ConstantPatternNode cp => cp.Value.Accept(this),
            RelationalPatternNode rp => EmitRelationalPattern(rp),
            PropertyPatternNode pp => Visit(pp),
            PositionalPatternNode pos => Visit(pos),
            ListPatternNode lp => Visit(lp),
            _ => "_"
        };
    }

    private string EmitRelationalPattern(RelationalPatternNode node)
    {
        // Map C# operator back to Calor keyword
        var opKeyword = node.Operator switch
        {
            ">=" => "gte",
            "<=" => "lte",
            ">" => "gt",
            "<" => "lt",
            "gte" => "gte",
            "lte" => "lte",
            "gt" => "gt",
            "lt" => "lt",
            _ => "gte"
        };
        var value = node.Value.Accept(this);
        return $"§PREL{{{opKeyword}}} {value}";
    }

    public string Visit(WildcardPatternNode node) => "_";
    public string Visit(VariablePatternNode node) => $"§VAR{{{node.Name}}}";
    public string Visit(LiteralPatternNode node) => node.Literal.Accept(this);
    public string Visit(SomePatternNode node) => $"§SM {EmitPattern(node.InnerPattern)}";
    public string Visit(NonePatternNode node) => "§NN";
    public string Visit(OkPatternNode node) => $"§OK {EmitPattern(node.InnerPattern)}";
    public string Visit(ErrPatternNode node) => $"§ERR {EmitPattern(node.InnerPattern)}";
    public string Visit(VarPatternNode node) => $"§VAR{{{node.Name}}}";
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
        return EmitRelationalPattern(node);
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
            $"{TypeMapper.CSharpToCalor(f.TypeName)}:{f.Name}"));
        AppendLine($"§D{{{node.Name}}} ({fields})");
        return "";
    }

    public string Visit(UnionTypeDefinitionNode node)
    {
        // Emit union types using the type/variant syntax
        AppendLine($"§T{{{node.Name}}}");
        Indent();
        foreach (var variant in node.Variants)
        {
            var fields = variant.Fields.Count > 0
                ? $"({string.Join(", ", variant.Fields.Select(f => $"{TypeMapper.CSharpToCalor(f.TypeName)}:{f.Name}"))})"
                : "";
            AppendLine($"§V{{{variant.Name}}}{fields}");
        }
        Dedent();
        AppendLine("§/T");
        return "";
    }

    public string Visit(EnumDefinitionNode node)
    {
        // Format: §EN{id:Name} or §EN{id:Name:underlyingType}
        var header = node.UnderlyingType != null
            ? $"§EN{{{node.Id}:{node.Name}:{node.UnderlyingType}}}"
            : $"§EN{{{node.Id}:{node.Name}}}";
        AppendLine(header);
        Indent();

        foreach (var member in node.Members)
        {
            Visit(member);
        }

        Dedent();
        AppendLine($"§/EN{{{node.Id}}}");
        return "";
    }

    public string Visit(EnumMemberNode node)
    {
        var line = node.Value != null
            ? $"{node.Name} = {node.Value}"
            : node.Name;
        AppendLine(line);
        return "";
    }

    public string Visit(EnumExtensionNode node)
    {
        // Format: §EXT{id:EnumName}
        AppendLine($"§EXT{{{node.Id}:{node.EnumName}}}");
        Indent();

        foreach (var method in node.Methods)
        {
            Visit(method);
            AppendLine();
        }

        Dedent();
        AppendLine($"§/EXT{{{node.Id}}}");
        return "";
    }

    public string Visit(RecordCreationNode node)
    {
        var fields = string.Join(", ", node.Fields.Select(f => f.Value.Accept(this)));
        return $"§NEW{{{node.TypeName}}} {fields}";
    }

    // Generic type nodes
    public string Visit(TypeParameterNode node) => node.Name;

    public string Visit(TypeConstraintNode node)
    {
        return node.Kind switch
        {
            TypeConstraintKind.Class => "class",
            TypeConstraintKind.Struct => "struct",
            TypeConstraintKind.New => "new",
            TypeConstraintKind.Interface => node.TypeName ?? "",
            TypeConstraintKind.BaseClass => node.TypeName ?? "",
            TypeConstraintKind.TypeName => node.TypeName ?? "",
            _ => node.TypeName ?? ""
        };
    }

    /// <summary>
    /// Emits §WHERE clauses for type parameters with constraints.
    /// New format: §WHERE T : class, IComparable&lt;T&gt;
    /// </summary>
    private void EmitTypeParameterConstraints(IReadOnlyList<TypeParameterNode> typeParameters)
    {
        foreach (var tp in typeParameters)
        {
            if (tp.Constraints.Count > 0)
            {
                var constraints = string.Join(", ", tp.Constraints.Select(c => Visit(c)));
                AppendLine($"§WHERE {tp.Name} : {constraints}");
            }
        }
    }

    public string Visit(GenericTypeNode node)
    {
        if (node.TypeArguments.Count == 0)
            return TypeMapper.CSharpToCalor(node.TypeName);
        var args = string.Join(", ", node.TypeArguments.Select(TypeMapper.CSharpToCalor));
        return $"{TypeMapper.CSharpToCalor(node.TypeName)}<{args}>";
    }

    // Delegate and event nodes
    public string Visit(DelegateDefinitionNode node)
    {
        var output = node.Output != null ? TypeMapper.CSharpToCalor(node.Output.TypeName) : "void";
        var paramList = string.Join(", ", node.Parameters.Select(p =>
            $"{TypeMapper.CSharpToCalor(p.TypeName)}:{p.Name}"));
        AppendLine($"§DEL{{{node.Name}}} ({paramList}) → {output}");
        return "";
    }

    public string Visit(EventDefinitionNode node)
    {
        // Events are emitted as fields since the parser doesn't support §EVT in class bodies
        var visibility = GetVisibilityShorthand(node.Visibility);
        var delegateType = TypeMapper.CSharpToCalor(node.DelegateType);
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

    private static string GetCalorOperatorSymbol(BinaryOperator op) => op switch
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

    private static string GetCalorOperatorKind(BinaryOperator op)
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
    private string EmitCSharpAttributes(IReadOnlyList<CalorAttributeNode> attributes)
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

    public string Visit(CalorAttributeNode node)
    {
        if (node.Arguments.Count == 0)
        {
            return $"[@{node.Name}]";
        }

        var args = string.Join(", ", node.Arguments.Select(FormatAttributeArgument));
        return $"[@{node.Name}({args})]";
    }

    private static string FormatAttributeArgument(CalorAttributeArgument arg)
    {
        var value = arg.GetFormattedValue();

        if (arg.IsNamed)
        {
            return $"{arg.Name}={value}";
        }
        return value;
    }

    // Quantified Contracts

    public string Visit(QuantifierVariableNode node)
    {
        return $"({node.Name} {node.TypeName})";
    }

    public string Visit(ForallExpressionNode node)
    {
        var vars = string.Join(" ", node.BoundVariables.Select(v => v.Accept(this)));
        var body = node.Body.Accept(this);
        return $"(forall ({vars}) {body})";
    }

    public string Visit(ExistsExpressionNode node)
    {
        var vars = string.Join(" ", node.BoundVariables.Select(v => v.Accept(this)));
        var body = node.Body.Accept(this);
        return $"(exists ({vars}) {body})";
    }

    public string Visit(ImplicationExpressionNode node)
    {
        var ante = node.Antecedent.Accept(this);
        var cons = node.Consequent.Accept(this);
        return $"(-> {ante} {cons})";
    }

    // Native String Operations

    public string Visit(StringOperationNode node)
    {
        var opName = node.Operation.ToCalorName();
        var args = node.Arguments.Select(a => a.Accept(this));
        var argsStr = string.Join(" ", args);

        // Append comparison mode keyword if present
        if (node.ComparisonMode.HasValue)
        {
            var keyword = node.ComparisonMode.Value.ToKeyword();
            return $"({opName} {argsStr} :{keyword})";
        }

        return $"({opName} {argsStr})";
    }

    public string Visit(CharOperationNode node)
    {
        var opName = node.Operation.ToCalorName();
        var args = node.Arguments.Select(a => a.Accept(this));
        return $"({opName} {string.Join(" ", args)})";
    }

    public string Visit(StringBuilderOperationNode node)
    {
        var opName = node.Operation.ToCalorName();
        var args = node.Arguments.Select(a => a.Accept(this));
        var argsStr = string.Join(" ", args);
        return args.Any() ? $"({opName} {argsStr})" : $"({opName})";
    }
}
