using System.Text;
using Calor.Compiler.Ast;
using Calor.Compiler.Effects;

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
    private bool _inInterpolation;
    private readonly List<string> _pendingHoistedLines = new();
    private int _ternaryCounter;
    private int _hoistCounter;
    private int _memberBodyDepth;

    public CalorEmitter(ConversionContext? context = null)
    {
        _context = context;
    }

    public string Emit(ModuleNode module)
    {
        _builder.Clear();
        _indentLevel = 0;
        _pendingHoistedLines.Clear();
        _ternaryCounter = 0;
        _hoistCounter = 0;
        _memberBodyDepth = 0;
        _inInterpolation = false;
        Visit(module);
        return _builder.ToString();
    }

    private void FlushHoistedLines()
    {
        if (_pendingHoistedLines.Count == 0) return;
        var lines = new List<string>(_pendingHoistedLines);
        _pendingHoistedLines.Clear();
        foreach (var line in lines)
        {
            _builder.Append(new string(' ', _indentLevel * 2));
            _builder.AppendLine(line);
        }
    }

    private void AppendLine(string line = "")
    {
        FlushHoistedLines();
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
    /// Emits doc comment lines (if present) as Calor line comments before a construct.
    /// </summary>
    private void EmitDocComment(AstNode node)
    {
        if (node.DocComment == null)
            return;
        // Normalize \r\n and \r to \n to avoid orphaned text from embedded carriage returns
        var normalized = node.DocComment.Replace("\r\n", "\n").Replace("\r", "");
        foreach (var line in normalized.Split('\n'))
        {
            AppendLine($"// {line.Trim()}");
        }
    }

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

        // Emit delegate declarations
        foreach (var del in node.Delegates)
        {
            Visit(del);
            AppendLine();
        }

        // Emit classes
        foreach (var cls in node.Classes)
        {
            Visit(cls);
            AppendLine();
        }

        // Emit refinement type definitions
        foreach (var rtype in node.RefinementTypes)
        {
            Visit(rtype);
        }

        // Emit indexed type definitions
        foreach (var itype in node.IndexedTypes)
        {
            Visit(itype);
        }

        // Emit module-level functions
        foreach (var func in node.Functions)
        {
            Visit(func);
            AppendLine();
        }

        // Emit C# interop blocks
        foreach (var interop in node.InteropBlocks)
        {
            Visit(interop);
            AppendLine();
        }

        // Emit type-level preprocessor blocks
        foreach (var tpp in node.TypePreprocessorBlocks)
        {
            Visit(tpp);
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
            AppendLine($"§U{{{node.Alias}:{node.Namespace}}}");
        }
        else
        {
            AppendLine($"§U{{{node.Namespace}}}");
        }
        return "";
    }

    public string Visit(InterfaceDefinitionNode node)
    {
        EmitDocComment(node);
        var typeParams = node.TypeParameters.Count > 0
            ? $"<{string.Join(",", node.TypeParameters.Select(tp => Visit(tp)))}>"
            : "";
        var baseList = node.BaseInterfaces.Count > 0
            ? $":{string.Join(",", node.BaseInterfaces)}"
            : "";
        var attrs = EmitCSharpAttributes(node.CSharpAttributes);

        AppendLine($"§IFACE{{{node.Id}:{node.Name}{typeParams}{baseList}}}{attrs}");
        Indent();

        EmitTypeParameterConstraints(node.TypeParameters);

        foreach (var prop in node.Properties)
        {
            Visit(prop);
        }

        foreach (var indexer in node.Indexers)
        {
            Visit(indexer);
        }

        foreach (var method in node.Methods)
        {
            Visit(method);
        }

        Dedent();
        AppendLine($"§/IFACE{{{node.Id}}}");

        return "";
    }

    /// <summary>
    /// Format inline parameter list for compact signatures: (type:name, type:name, ...)
    /// Returns null if any parameter has C# attributes or default values (which can't be inlined).
    /// </summary>
    private string? TryFormatInlineParams(IReadOnlyList<ParameterNode> parameters)
    {
        if (parameters.Any(p => p.CSharpAttributes.Count > 0 || p.DefaultValue != null || p.InlineRefinement != null))
            return null;

        return string.Join(", ", parameters.Select(p =>
        {
            var typeName = TypeMapper.CSharpToCalor(p.TypeName);
            var modifiers = new List<string>();
            if (p.Modifier.HasFlag(ParameterModifier.This)) modifiers.Add("this");
            if (p.Modifier.HasFlag(ParameterModifier.Ref)) modifiers.Add("ref");
            if (p.Modifier.HasFlag(ParameterModifier.Out)) modifiers.Add("out");
            if (p.Modifier.HasFlag(ParameterModifier.In)) modifiers.Add("in");
            if (p.Modifier.HasFlag(ParameterModifier.Params)) modifiers.Add("params");
            var modStr = modifiers.Count > 0 ? $":{string.Join(",", modifiers)}" : "";
            return $"{typeName}:{EscapeCalorIdentifier(p.Name)}{modStr}";
        }));
    }

    private void EmitParameterLines(IReadOnlyList<ParameterNode> parameters)
    {
        foreach (var param in parameters)
            AppendLine(Visit(param));
    }

    private void EmitOutputLine(OutputNode? output)
    {
        if (output != null)
            AppendLine($"§O{{{TypeMapper.CSharpToCalor(output.TypeName)}}}");
    }

    public string Visit(MethodSignatureNode node)
    {
        var typeParams = node.TypeParameters.Count > 0
            ? $"<{string.Join(",", node.TypeParameters.Select(tp => Visit(tp)))}>"
            : "";
        var attrs = EmitCSharpAttributes(node.CSharpAttributes);

        var inlineFmt = TryFormatInlineParams(node.Parameters);
        if (inlineFmt != null)
        {
            var inlineParams = node.Parameters.Count > 0 || node.Output != null ? $" ({inlineFmt})" : "";
            var inlineReturn = node.Output != null ? $" -> {TypeMapper.CSharpToCalor(node.Output.TypeName)}" : "";
            AppendLine($"§MT{{{node.Id}:{node.Name}{typeParams}}}{attrs}{inlineParams}{inlineReturn}");
        }
        else
        {
            AppendLine($"§MT{{{node.Id}:{node.Name}{typeParams}}}{attrs}");
            Indent();
            EmitParameterLines(node.Parameters);
            EmitOutputLine(node.Output);
            Dedent();
        }
        AppendLine($"§/MT{{{node.Id}}}");

        return "";
    }

    public string Visit(ClassDefinitionNode node)
    {
        EmitDocComment(node);
        var modifiers = new List<string>();
        if (node.IsAbstract) modifiers.Add("abs");
        if (node.IsSealed) modifiers.Add("seal");
        if (node.IsPartial) modifiers.Add("partial");
        if (node.IsStatic) modifiers.Add("stat");
        if (node.IsStruct) modifiers.Add("struct");
        if (node.IsReadOnly) modifiers.Add("readonly");

        var vis = GetVisibilityShorthand(node.Visibility);
        var modStr = modifiers.Count > 0 ? $":{string.Join(",", modifiers)}" : "";

        var typeParams = node.TypeParameters.Count > 0
            ? $"<{string.Join(",", node.TypeParameters.Select(tp => Visit(tp)))}>"
            : "";
        var attrs = EmitCSharpAttributes(node.CSharpAttributes);

        AppendLine($"§CL{{{node.Id}:{node.Name}{typeParams}:{vis}{modStr}}}{attrs}");
        Indent();

        // Emit type parameter constraints
        EmitTypeParameterConstraints(node.TypeParameters);

        // Emit base class as §EXT tag (not positional)
        if (node.BaseClass != null)
        {
            AppendLine($"§EXT{{{node.BaseClass}}}");
        }

        // Emit implemented interfaces
        foreach (var iface in node.ImplementedInterfaces)
        {
            AppendLine($"§IMPL{{{iface}}}");
        }
        if (node.BaseClass != null || node.ImplementedInterfaces.Count > 0 || node.TypeParameters.Any(tp => tp.Constraints.Count > 0))
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

        // Emit indexers
        foreach (var indexer in node.Indexers)
        {
            Visit(indexer);
        }
        if (node.Indexers.Count > 0)
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

        // Emit operator overloads
        foreach (var op in node.OperatorOverloads)
        {
            Visit(op);
            AppendLine();
        }

        // Emit C# interop blocks
        foreach (var interop in node.InteropBlocks)
        {
            Visit(interop);
            AppendLine();
        }

        // Emit preprocessor blocks
        foreach (var ppBlock in node.PreprocessorBlocks)
        {
            Visit(ppBlock);
            AppendLine();
        }

        // Emit nested types
        foreach (var nestedClass in node.NestedClasses)
        {
            Visit(nestedClass);
            AppendLine();
        }
        foreach (var nestedIface in node.NestedInterfaces)
        {
            Visit(nestedIface);
            AppendLine();
        }
        foreach (var nestedEnum in node.NestedEnums)
        {
            Visit(nestedEnum);
            AppendLine();
        }
        foreach (var nestedDelegate in node.NestedDelegates)
        {
            Visit(nestedDelegate);
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
        // For collection creation defaults (Dict/List/Set), emit "= default" instead of
        // the full §DICT/§LIST/§SET block, since the type is already on the §FLD tag.
        string defaultVal;
        if (node.DefaultValue is DictionaryCreationNode or ListCreationNode or SetCreationNode)
            defaultVal = " = default";
        else if (node.DefaultValue != null)
            defaultVal = $" = {node.DefaultValue.Accept(this)}";
        else
            defaultVal = "";
        var attrs = EmitCSharpAttributes(node.CSharpAttributes);

        var modifiers = new List<string>();
        if (node.IsRequired) modifiers.Add("req");
        if (node.Modifiers.HasFlag(MethodModifiers.Static)) modifiers.Add("stat");
        if (node.Modifiers.HasFlag(MethodModifiers.Const)) modifiers.Add("const");
        if (node.Modifiers.HasFlag(MethodModifiers.Readonly)) modifiers.Add("readonly");
        if (node.IsVolatile) modifiers.Add("volatile");
        var modStr = modifiers.Count > 0 ? $":{string.Join(",", modifiers)}" : "";

        AppendLine($"§FLD{{{typeName}:{EscapeCalorIdentifier(node.Name)}:{visibility}{modStr}}}{attrs}{defaultVal}");

        return "";
    }

    public string Visit(PropertyNode node)
    {
        EmitDocComment(node);
        var visibility = GetVisibilityShorthand(node.Visibility);
        var typeName = TypeMapper.CSharpToCalor(node.TypeName);
        var attrs = EmitCSharpAttributes(node.CSharpAttributes);

        var modifiers = new List<string>();
        if (node.IsRequired) modifiers.Add("req");
        if (node.IsVirtual) modifiers.Add("virt");
        if (node.IsOverride) modifiers.Add("over");
        if (node.IsAbstract) modifiers.Add("abs");
        if (node.IsSealed) modifiers.Add("seal");
        if (node.IsStatic) modifiers.Add("stat");
        var modStr = modifiers.Count > 0 ? $":{string.Join(",", modifiers)}" : "";

        // Compact auto-property shorthand
        // If default value is multi-line (e.g., §NEW with initializers), fall through to full syntax
        if (node.IsAutoProperty && (node.Getter != null || node.Setter != null || node.Initer != null))
        {
            // Pre-check: if default value would be multi-line, use full syntax instead
            if (node.DefaultValue is NewExpressionNode newExpr && newExpr.Initializers.Count > 0)
            {
                // Fall through to full property syntax below
            }
            else
            {
                var accessors = new List<string>();
                if (node.Getter != null)
                    accessors.Add(node.Getter.Visibility == Visibility.Private ? "priget" :
                                  node.Getter.Visibility == Visibility.Internal ? "intget" :
                                  node.Getter.Visibility == Visibility.Protected ? "proget" : "get");
                if (node.Setter != null)
                    accessors.Add(node.Setter.Visibility == Visibility.Private ? "priset" :
                                  node.Setter.Visibility == Visibility.Internal ? "intset" :
                                  node.Setter.Visibility == Visibility.Protected ? "proset" : "set");
                if (node.Initer != null)
                    accessors.Add("init");
                var accessorStr = string.Join(",", accessors);
                var defaultVal = node.DefaultValue != null ? $" = {node.DefaultValue.Accept(this)}" : "";
                AppendLine($"§PROP{{{node.Id}:{EscapeCalorIdentifier(node.Name)}:{typeName}:{visibility}{modStr}:{accessorStr}}}{attrs}{defaultVal}");
                return "";
            }
        }

        // Full property syntax with body and closing tag
        AppendLine($"§PROP{{{node.Id}:{EscapeCalorIdentifier(node.Name)}:{typeName}:{visibility}{modStr}}}{attrs}");
        Indent();

        if (node.Getter != null) Visit(node.Getter);
        if (node.Setter != null) Visit(node.Setter);
        if (node.Initer != null) Visit(node.Initer);

        if (node.DefaultValue != null)
            AppendLine($"= {node.DefaultValue.Accept(this)}");

        Dedent();
        AppendLine($"§/PROP{{{node.Id}}}");

        return "";
    }

    public string Visit(IndexerNode node)
    {
        var visibility = GetVisibilityShorthand(node.Visibility);
        var typeName = TypeMapper.CSharpToCalor(node.TypeName);
        var attrs = EmitCSharpAttributes(node.CSharpAttributes);

        var modifiers = new List<string>();
        if (node.IsVirtual) modifiers.Add("virt");
        if (node.IsOverride) modifiers.Add("over");
        if (node.IsAbstract) modifiers.Add("abs");
        if (node.IsSealed) modifiers.Add("seal");
        var modStr = modifiers.Count > 0 ? $":{string.Join(",", modifiers)}" : "";

        // Compact auto-indexer shorthand
        if (node.IsAutoIndexer && (node.Getter != null || node.Setter != null || node.Initer != null))
        {
            var accessors = new List<string>();
            if (node.Getter != null)
                accessors.Add(node.Getter.Visibility == Visibility.Private ? "priget" :
                              node.Getter.Visibility == Visibility.Internal ? "intget" :
                              node.Getter.Visibility == Visibility.Protected ? "proget" : "get");
            if (node.Setter != null)
                accessors.Add(node.Setter.Visibility == Visibility.Private ? "priset" :
                              node.Setter.Visibility == Visibility.Internal ? "intset" :
                              node.Setter.Visibility == Visibility.Protected ? "proset" : "set");
            if (node.Initer != null)
                accessors.Add("init");
            var accessorStr = string.Join(",", accessors);
            var paramParts = node.Parameters.Select(p =>
            {
                var pType = TypeMapper.CSharpToCalor(p.TypeName);
                return $"{pType}:{p.Name}";
            });
            var paramStr = string.Join(", ", paramParts);
            AppendLine($"§IXER{{{node.Id}:{typeName}:{visibility}{modStr}:{accessorStr}}}{attrs} ({paramStr})");
            return "";
        }

        // Full indexer syntax with body and closing tag
        AppendLine($"§IXER{{{node.Id}:{typeName}:{visibility}{modStr}}}{attrs}");
        Indent();

        foreach (var param in node.Parameters)
        {
            Visit(param);
        }

        if (node.Getter != null) Visit(node.Getter);
        if (node.Setter != null) Visit(node.Setter);
        if (node.Initer != null) Visit(node.Initer);

        Dedent();
        AppendLine($"§/IXER{{{node.Id}}}");

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
            _memberBodyDepth++;
            foreach (var stmt in node.Body)
            {
                stmt.Accept(this);
            }
            _memberBodyDepth--;
            Dedent();
            AppendLine($"§/{keyword}");
        }

        return "";
    }

    public string Visit(ConstructorNode node)
    {
        var visibility = node.IsStatic ? "stat" : GetVisibilityShorthand(node.Visibility);
        var attrs = EmitCSharpAttributes(node.CSharpAttributes);

        // Inline params: §CTOR{id:vis} (type:name, type:name)
        var inlineFmt = TryFormatInlineParams(node.Parameters);
        if (inlineFmt != null && node.Parameters.Count > 0)
            AppendLine($"§CTOR{{{node.Id}:{visibility}}}{attrs} ({inlineFmt})");
        else
            AppendLine($"§CTOR{{{node.Id}:{visibility}}}{attrs}");
        Indent();

        if (inlineFmt == null)
            EmitParameterLines(node.Parameters);

        // Emit preconditions
        foreach (var pre in node.Preconditions)
        {
            Visit(pre);
        }

        // Emit constructor initializer (base/this call)
        if (node.Initializer != null)
        {
            // Pre-evaluate args to collect any hoisted bindings BEFORE the §THIS/§BASE block
            var evalArgs = new List<string>();
            foreach (var arg in node.Initializer.Arguments)
            {
                var argExpr = arg.Accept(this);
                // Flush any hoisted lines before the block
                FlushHoistedLines();
                evalArgs.Add(argExpr);
            }
            var initKeyword = node.Initializer.IsBaseCall ? "§BASE" : "§THIS";
            AppendLine(initKeyword);
            Indent();
            foreach (var argExpr in evalArgs)
            {
                AppendLine($"§A {argExpr}");
            }
            Dedent();
            AppendLine(node.Initializer.IsBaseCall ? "§/BASE" : "§/THIS");
        }

        // Emit body statements
        _memberBodyDepth++;
        foreach (var stmt in node.Body)
        {
            stmt.Accept(this);
        }
        _memberBodyDepth--;

        Dedent();
        AppendLine($"§/CTOR{{{node.Id}}}");

        return "";
    }

    public string Visit(OperatorOverloadNode node)
    {
        var visibility = GetVisibilityShorthand(node.Visibility);
        var attrs = EmitCSharpAttributes(node.CSharpAttributes);

        var inlineFmt = TryFormatInlineParams(node.Parameters);
        if (inlineFmt != null)
        {
            var inlineParams = node.Parameters.Count > 0 || node.Output != null ? $" ({inlineFmt})" : "";
            var inlineReturn = node.Output != null ? $" -> {TypeMapper.CSharpToCalor(node.Output.TypeName)}" : "";
            AppendLine($"§OP{{{node.Id}:{node.OperatorToken}:{visibility}}}{attrs}{inlineParams}{inlineReturn}");
        }
        else
        {
            AppendLine($"§OP{{{node.Id}:{node.OperatorToken}:{visibility}}}{attrs}");
            Indent();
            EmitParameterLines(node.Parameters);
            EmitOutputLine(node.Output);
            Dedent();
        }
        Indent();

        // Emit preconditions
        foreach (var pre in node.Preconditions)
        {
            Visit(pre);
        }

        // Emit postconditions
        foreach (var post in node.Postconditions)
        {
            Visit(post);
        }

        // Emit body statements
        _memberBodyDepth++;
        foreach (var stmt in node.Body)
        {
            stmt.Accept(this);
        }
        _memberBodyDepth--;

        Dedent();
        AppendLine($"§/OP{{{node.Id}}}");

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
        EmitDocComment(node);
        var visibility = GetVisibilityShorthand(node.Visibility);
        var modifiers = new List<string>();

        if (node.IsPartial) modifiers.Add("part");
        if (node.IsVirtual) modifiers.Add("virt");
        if (node.IsOverride) modifiers.Add("over");
        if (node.IsAbstract) modifiers.Add("abs");
        if (node.IsSealed) modifiers.Add("seal");
        if (node.IsStatic) modifiers.Add("stat");
        if (node.IsUnsafe) modifiers.Add("unsafe");
        if (node.IsExtern) modifiers.Add("ext");

        var modStr = modifiers.Count > 0 ? $":{string.Join(",", modifiers)}" : "";

        var typeParams = node.TypeParameters.Count > 0
            ? $"<{string.Join(",", node.TypeParameters.Select(tp => Visit(tp)))}>"
            : "";

        var output = node.Output != null ? TypeMapper.CSharpToCalor(node.Output.TypeName) : "void";
        var attrs = EmitCSharpAttributes(node.CSharpAttributes);

        // Use §AMT for async methods, §MT for regular methods
        var methodTag = node.IsAsync ? "AMT" : "MT";
        var inlineFmt = TryFormatInlineParams(node.Parameters);
        if (inlineFmt != null)
        {
            var inlineParams = node.Parameters.Count > 0 || node.Output != null ? $" ({inlineFmt})" : "";
            var inlineReturn = node.Output != null ? $" -> {output}" : "";
            AppendLine($"§{methodTag}{{{node.Id}:{EscapeCalorIdentifier(node.Name)}{typeParams}:{visibility}{modStr}}}{attrs}{inlineParams}{inlineReturn}");
        }
        else
        {
            AppendLine($"§{methodTag}{{{node.Id}:{EscapeCalorIdentifier(node.Name)}{typeParams}:{visibility}{modStr}}}{attrs}");
        }
        Indent();

        // Emit type parameter constraints
        EmitTypeParameterConstraints(node.TypeParameters);

        if (inlineFmt == null)
        {
            EmitParameterLines(node.Parameters);
            EmitOutputLine(node.Output);
        }

        // Effects
        EmitEffects(node.Effects);

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

        // Body (only for non-abstract/non-extern methods)
        if (!node.IsAbstract && !node.IsExtern)
        {
            _memberBodyDepth++;
            if (node.Body.Count == 0)
            {
                // Empty method body — emit a void return placeholder so the parser
                // doesn't see EndMethod immediately after the opening tag
                AppendLine("§R");
            }
            foreach (var stmt in node.Body)
            {
                stmt.Accept(this);
            }
            _memberBodyDepth--;
        }

        Dedent();
        AppendLine($"§/{methodTag}{{{node.Id}}}");

        return "";
    }

    public string Visit(FunctionNode node)
    {
        var visibility = GetVisibilityShorthand(node.Visibility);
        var typeParams = node.TypeParameters.Count > 0
            ? $"<{string.Join(",", node.TypeParameters.Select(tp => Visit(tp)))}>"
            : "";

        var output = node.Output != null ? TypeMapper.CSharpToCalor(node.Output.TypeName) : "void";

        // Use §AF for async functions, §F for regular functions
        var funcTag = node.IsAsync ? "AF" : "F";
        var inlineFmt = TryFormatInlineParams(node.Parameters);
        if (inlineFmt != null)
        {
            var inlineParams = node.Parameters.Count > 0 || node.Output != null ? $" ({inlineFmt})" : "";
            var inlineReturn = node.Output != null ? $" -> {output}" : "";
            AppendLine($"§{funcTag}{{{node.Id}:{EscapeCalorIdentifier(node.Name)}{typeParams}:{visibility}}}{inlineParams}{inlineReturn}");
        }
        else
        {
            AppendLine($"§{funcTag}{{{node.Id}:{EscapeCalorIdentifier(node.Name)}{typeParams}:{visibility}}}");
        }
        Indent();

        // Emit type parameter constraints
        EmitTypeParameterConstraints(node.TypeParameters);

        if (inlineFmt == null)
        {
            EmitParameterLines(node.Parameters);
            EmitOutputLine(node.Output);
        }

        // Effects
        EmitEffects(node.Effects);

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
        _memberBodyDepth++;
        if (node.Body.Count == 0)
        {
            AppendLine("§R");
        }
        foreach (var stmt in node.Body)
        {
            stmt.Accept(this);
        }
        _memberBodyDepth--;

        Dedent();
        AppendLine($"§/{funcTag}{{{node.Id}}}");

        return "";
    }

    public string Visit(ParameterNode node)
    {
        var typeName = TypeMapper.CSharpToCalor(node.TypeName);
        var modifiers = new List<string>();
        if (node.Modifier.HasFlag(ParameterModifier.This)) modifiers.Add("this");
        if (node.Modifier.HasFlag(ParameterModifier.Ref)) modifiers.Add("ref");
        if (node.Modifier.HasFlag(ParameterModifier.Out)) modifiers.Add("out");
        if (node.Modifier.HasFlag(ParameterModifier.In)) modifiers.Add("in");
        if (node.Modifier.HasFlag(ParameterModifier.Params)) modifiers.Add("params");
        var modStr = modifiers.Count > 0 ? $":{string.Join(",", modifiers)}" : "";
        var attrs = EmitCSharpAttributes(node.CSharpAttributes);
        var result = $"§I{{{typeName}:{EscapeCalorIdentifier(node.Name)}{modStr}}}{attrs}";
        if (node.InlineRefinement != null)
        {
            var predicate = node.InlineRefinement.Predicate.Accept(this);
            result += $" | {predicate}";
        }
        if (node.DefaultValue != null)
        {
            result += $" = {node.DefaultValue.Accept(this)}";
        }
        return result;
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
        // Emit named argument labels as §A[name] value when present
        // Hoist arguments containing section markers (e.g., §NEW with initializers)
        // to avoid nested markers or raw '=' breaking the call parser
        var args = node.Arguments.Select((a, i) =>
        {
            var argValue = a.Accept(this);
            if (ContainsSectionMarker(argValue))
                argValue = HoistToTempVar(argValue);
            if (node.ArgumentNames != null && i < node.ArgumentNames.Count && node.ArgumentNames[i] != null)
                return $"§A[{node.ArgumentNames[i]}] {argValue}";
            return $"§A {argValue}";
        });
        var argsStr = node.Arguments.Count > 0 ? $" {string.Join(" ", args)}" : "";
        var target = ConvertVerbatimStringsInTarget(node.Target.Replace("->", "."));
        AppendLine($"§C{{{target}}}{argsStr} §/C");
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

    public string Visit(GotoStatementNode node)
    {
        if (node.CaseLabel != null)
        {
            AppendLine($"§GOTO{{CASE:{node.CaseLabel.Accept(this)}}}");
        }
        else if (node.IsDefault)
        {
            AppendLine("§GOTO{DEFAULT}");
        }
        else
        {
            AppendLine($"§GOTO{{{node.Label}}}");
        }
        return "";
    }

    public string Visit(LabelStatementNode node)
    {
        AppendLine($"§LABEL{{{node.Label}}}");
        return "";
    }

    public string Visit(YieldReturnStatementNode node)
    {
        if (node.Expression != null)
        {
            var expr = node.Expression.Accept(this);
            AppendLine($"§YIELD {expr}");
        }
        else
        {
            AppendLine("§YIELD");
        }
        return "";
    }

    public string Visit(YieldBreakStatementNode node)
    {
        AppendLine("§YBRK");
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
        if (node.Initializer is ArrayCreationNode arrNode)
        {
            EmitArrayCreationWithName(arrNode, node.Name);
            return "";
        }
        if (node.Initializer is MultiDimArrayCreationNode mdArrNode)
        {
            EmitMultiDimArrayCreationWithName(mdArrNode, node.Name, node.TypeName);
            return "";
        }

        // Parser expects: §B[type:name] expression (no = sign)
        var initPart = node.Initializer != null ? $" {node.Initializer.Accept(this)}" : "";

        if (node.IsMutable)
        {
            // Mutable: {~name} or {~name:type}
            var typePostfix = node.TypeName != null ? $":{TypeMapper.CSharpToCalor(node.TypeName)}" : "";
            AppendLine($"§B{{~{EscapeCalorIdentifier(node.Name)}{typePostfix}}}{initPart}");
        }
        else
        {
            // Immutable: {name} or {type:name}
            var typePrefix = node.TypeName != null ? $"{TypeMapper.CSharpToCalor(node.TypeName)}:" : "";
            AppendLine($"§B{{{typePrefix}{EscapeCalorIdentifier(node.Name)}}}{initPart}");
        }
        return "";
    }

    private void EmitListCreationWithName(ListCreationNode node, string variableName)
    {
        var elementType = TypeMapper.CSharpToCalor(node.ElementType);

        // Sanitize variable names containing section markers
        var originalTarget = variableName;
        if (ContainsSectionMarker(variableName))
            variableName = $"_hoist{_hoistCounter++:D3}";

        // Pre-evaluate all elements to collect hoisted bindings before the list block
        var evaluatedElements = new List<string>();
        var allHoisted = new List<string>();
        foreach (var element in node.Elements)
        {
            var val = element.Accept(this);
            if (_pendingHoistedLines.Count > 0)
            {
                allHoisted.AddRange(_pendingHoistedLines);
                _pendingHoistedLines.Clear();
            }
            evaluatedElements.Add(val);
        }

        // Emit hoisted bindings before the list
        foreach (var hoisted in allHoisted)
            AppendLine(hoisted);

        AppendLine($"§LIST{{{variableName}:{elementType}}}");
        Indent();

        foreach (var val in evaluatedElements)
        {
            AppendLine(val);
        }

        Dedent();
        AppendLine($"§/LIST{{{variableName}}}");
        if (originalTarget != variableName)
            AppendLine($"§ASSIGN {originalTarget} {variableName}");
    }

    private void EmitDictionaryCreationWithName(DictionaryCreationNode node, string variableName)
    {
        var keyType = TypeMapper.CSharpToCalor(node.KeyType);
        var valueType = TypeMapper.CSharpToCalor(node.ValueType);

        // Pre-evaluate entries to collect hoisted bindings before the dict block
        var evaluatedEntries = new List<(string Key, string Value)>();
        var allHoisted = new List<string>();
        foreach (var entry in node.Entries)
        {
            var key = entry.Key.Accept(this);
            var value = entry.Value.Accept(this);
            if (_pendingHoistedLines.Count > 0)
            {
                allHoisted.AddRange(_pendingHoistedLines);
                _pendingHoistedLines.Clear();
            }
            evaluatedEntries.Add((key, value));
        }

        // Emit hoisted bindings before the dict
        foreach (var hoisted in allHoisted)
            AppendLine(hoisted);

        AppendLine($"§DICT{{{variableName}:{keyType}:{valueType}}}");
        Indent();

        foreach (var (key, value) in evaluatedEntries)
        {
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

    private void EmitArrayCreationWithName(ArrayCreationNode node, string variableName)
    {
        var elementType = TypeMapper.CSharpToCalor(node.ElementType);

        // Sanitize variable names containing section markers (e.g., §IDX §THIS §C{GetInt} §/C)
        // These can't appear inside {…} attribute blocks. Use a hoisted temp var instead.
        var originalTarget = variableName;
        if (ContainsSectionMarker(variableName))
        {
            variableName = $"_hoist{_hoistCounter++:D3}";
        }

        if (node.Initializer.Count > 0)
        {
            // Pre-evaluate elements to flush hoisted bindings before the §ARR block
            var evalElements = new List<string>();
            foreach (var element in node.Initializer)
            {
                var val = element.Accept(this);
                FlushHoistedLines();
                if (ContainsSectionMarker(val))
                    val = HoistToTempVar(val);
                evalElements.Add(val);
            }
            AppendLine($"§ARR{{{variableName}:{elementType}}}");
            Indent();
            foreach (var val in evalElements)
            {
                AppendLine(val);
            }
            Dedent();
            AppendLine($"§/ARR{{{variableName}}}");
            // If we sanitized the name, assign the result to the original target
            if (originalTarget != variableName)
                AppendLine($"§ASSIGN {originalTarget} {variableName}");
        }
        else if (node.Size != null)
        {
            var size = node.Size.Accept(this);
            // Hoist complex size expressions out of the attribute braces
            // (§C calls, parenthesized expressions, commas break attribute parsing)
            if (ContainsSectionMarker(size) || size.Contains('(') || size.Contains(',') || size.Contains(':'))
                size = HoistToTempVar(size);
            AppendLine($"§B{{[{elementType}]:{variableName}}} §ARR{{{elementType}:{variableName}:{size}}}");
            if (originalTarget != variableName)
                AppendLine($"§ASSIGN {originalTarget} {variableName}");
        }
        else
        {
            AppendLine($"§ARR{{{elementType}:{variableName}}}");
            if (originalTarget != variableName)
                AppendLine($"§ASSIGN {originalTarget} {variableName}");
        }
    }

    private void EmitMultiDimArrayCreationWithName(MultiDimArrayCreationNode node, string variableName, string? typeName)
    {
        var elementType = TypeMapper.CSharpToCalor(node.ElementType);
        var mappedType = typeName != null ? TypeMapper.CSharpToCalor(typeName) : $"{elementType}[{new string(',', node.Rank - 1)}]";

        // Sanitize variable names containing section markers
        var originalTarget = variableName;
        if (ContainsSectionMarker(variableName))
            variableName = $"_hoist{_hoistCounter++:D3}";

        if (node.DimensionSizes.Count > 0)
        {
            // Hoist dimension sizes that contain section markers, parens, colons, or commas
            var evalDims = node.DimensionSizes.Select(d =>
            {
                var val = d.Accept(this);
                if (ContainsSectionMarker(val) || val.Contains('(') || val.Contains(',') || val.Contains(':'))
                    val = HoistToTempVar(val);
                return val;
            }).ToList();
            var dims = string.Join(":", evalDims);
            AppendLine($"§B{{{mappedType}:{variableName}}} §ARR2D{{{node.Id}:{variableName}:{elementType}:{dims}}}");
            if (originalTarget != variableName)
                AppendLine($"§ASSIGN {originalTarget} {variableName}");
        }
        else if (node.Initializer.Count > 0)
        {
            AppendLine($"§B{{{mappedType}:{variableName}}} §ARR2D{{{node.Id}:{variableName}:{elementType}}}");
            Indent();
            foreach (var row in node.Initializer)
            {
                var elements = string.Join(" ", row.Select(e => e.Accept(this)));
                AppendLine($"§ROW {elements}");
            }
            Dedent();
            AppendLine($"§/ARR2D{{{node.Id}}}");
        }
        else
        {
            var zeros = string.Join(":", Enumerable.Repeat("0", node.Rank));
            AppendLine($"§B{{{mappedType}:{variableName}}} §ARR2D{{{node.Id}:{variableName}:{elementType}:{zeros}}}");
        }
    }

    public string Visit(AssignmentStatementNode node)
    {
        var target = node.Target.Accept(this);

        // Handle collection initializers specially - emit as collection block with target name
        if (node.Value is ListCreationNode listNode)
        {
            EmitListCreationWithName(listNode, target);
            return "";
        }
        if (node.Value is DictionaryCreationNode dictNode)
        {
            EmitDictionaryCreationWithName(dictNode, target);
            return "";
        }
        if (node.Value is SetCreationNode setNode)
        {
            EmitSetCreationWithName(setNode, target);
            return "";
        }
        if (node.Value is ArrayCreationNode arrNode)
        {
            EmitArrayCreationWithName(arrNode, target);
            return "";
        }
        if (node.Value is MultiDimArrayCreationNode mdArrNode)
        {
            EmitMultiDimArrayCreationWithName(mdArrNode, target, null);
            return "";
        }

        var value = node.Value.Accept(this);

        // Skip empty assigns (can happen when value was hoisted as a side effect)
        if (string.IsNullOrWhiteSpace(value))
            return "";

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
            CompoundAssignmentOperator.NullCoalesce => "??",
            _ => "+"
        };
        // Parser expects: §ASSIGN <target> <value>
        if (node.Operator == CompoundAssignmentOperator.NullCoalesce)
            AppendLine($"§ASSIGN {target} (?? {target} {value})");
        else
            AppendLine($"§ASSIGN {target} ({opSymbol} {target} {value})");
        return "";
    }

    public string Visit(UsingStatementNode node)
    {
        var id = node.Id ?? $"use{_usingCounter++}";
        var namePart = node.VariableName ?? "_";
        var typePart = node.VariableType != null ? ":" + TypeMapper.CSharpToCalor(node.VariableType) : "";
        var resource = node.Resource.Accept(this);

        AppendLine($"§USE{{{id}:{namePart}{typePart}}} {resource}");
        Indent();

        foreach (var stmt in node.Body)
        {
            stmt.Accept(this);
        }

        Dedent();
        AppendLine($"§/USE{{{id}}}");
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

        // Hoist loop bounds that contain section markers or hex literals out of the braces
        // (hex literals like 0x100 break the ParseValue fallback inside attribute blocks)
        if (ContainsSectionMarker(from) || from.Contains("0x") || from.Contains(':'))
            from = HoistToTempVar(from);
        if (ContainsSectionMarker(to) || to.Contains("0x") || to.Contains(':'))
            to = HoistToTempVar(to);
        if (ContainsSectionMarker(step) || step.Contains("0x") || step.Contains(':'))
            step = HoistToTempVar(step);

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

        // Emit as §EACH{id:variable:type} or §EACH{id:variable:type:index}
        var indexPart = node.IndexVariableName != null ? $":{node.IndexVariableName}" : "";
        AppendLine($"§EACH{{{node.Id}:{node.VariableName}:{varType}{indexPart}}} {collection}");
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

        // Evaluate all elements first, collecting any hoisted bindings before the list
        var evaluatedElements = new List<string>();
        var allHoisted = new List<string>();
        foreach (var element in node.Elements)
        {
            var val = element.Accept(this);
            // Collect any hoisted lines generated during element evaluation
            if (_pendingHoistedLines.Count > 0)
            {
                allHoisted.AddRange(_pendingHoistedLines);
                _pendingHoistedLines.Clear();
            }
            evaluatedElements.Add(val);
        }

        // Emit hoisted bindings before the list
        foreach (var hoisted in allHoisted)
        {
            AppendLine(hoisted);
        }

        AppendLine($"§LIST{{{node.Id}:{elementType}}}");
        Indent();

        foreach (var val in evaluatedElements)
        {
            AppendLine(val);
        }

        Dedent();
        AppendLine($"§/LIST{{{node.Id}}}");
        return "";
    }

    public string Visit(DictionaryCreationNode node)
    {
        var keyType = TypeMapper.CSharpToCalor(node.KeyType);
        var valueType = TypeMapper.CSharpToCalor(node.ValueType);

        // Pre-evaluate entries to flush hoisted bindings before the §DICT block
        var evaluatedEntries = new List<(string key, string value)>();
        var allHoisted = new List<string>();
        foreach (var entry in node.Entries)
        {
            var key = entry.Key.Accept(this);
            var value = entry.Value.Accept(this);
            if (_pendingHoistedLines.Count > 0)
            {
                allHoisted.AddRange(_pendingHoistedLines);
                _pendingHoistedLines.Clear();
            }
            evaluatedEntries.Add((key, value));
        }

        // Emit hoisted bindings before the dict
        foreach (var hoisted in allHoisted)
        {
            AppendLine(hoisted);
        }

        AppendLine($"§DICT{{{node.Id}:{keyType}:{valueType}}}");
        Indent();

        foreach (var (key, value) in evaluatedEntries)
        {
            AppendLine($"§KV {key} {value}");
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
        var filterPart = node.Filter != null ? $" WHEN {node.Filter.Accept(this)}" : "";

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

    public string Visit(ThrowExpressionNode node)
    {
        var expr = node.Exception.Accept(this);
        return $"§TH {expr}";
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

        // Collect any hoisted lines from guard evaluation before emitting the case
        var guardHoisted = new List<string>();
        if (_pendingHoistedLines.Count > 0)
        {
            guardHoisted.AddRange(_pendingHoistedLines);
            _pendingHoistedLines.Clear();
        }

        // If guard produced hoisted bindings, always use block syntax
        if (guardHoisted.Count > 0)
        {
            AppendLine($"§K {pattern}{guard}");
            Indent();
            foreach (var h in guardHoisted)
                AppendLine(h);
            foreach (var stmt in node.Body)
                stmt.Accept(this);
            Dedent();
            AppendLine("§/K");
            return "";
        }

        // Check if this is a single-return case suitable for arrow syntax.
        // Block-level collection nodes (List, Dict, Set) emit via AppendLine and return "",
        // so they must use block syntax to avoid empty arrow expressions.
        if (node.Body.Count == 1 && node.Body[0] is ReturnStatementNode returnStmt && returnStmt.Expression != null
            && returnStmt.Expression is not ListCreationNode
            && returnStmt.Expression is not DictionaryCreationNode
            && returnStmt.Expression is not SetCreationNode)
        {
            // Evaluate the expression first — this may generate hoisted bindings
            // (e.g., §C calls inside Lisp expressions get hoisted to §B temp vars)
            var expr = returnStmt.Expression.Accept(this);

            if (_pendingHoistedLines.Count == 0)
            {
                // Use arrow syntax: §K pattern → expression
                AppendLine($"§K {pattern}{guard} → {expr}");
            }
            else
            {
                // Hoisted bindings exist — must use block syntax so they appear inside the arm
                var hoisted = new List<string>(_pendingHoistedLines);
                _pendingHoistedLines.Clear();
                AppendLine($"§K {pattern}{guard}");
                Indent();
                foreach (var line in hoisted)
                {
                    AppendLine(line);
                }
                AppendLine($"§R {expr}");
                Dedent();
                AppendLine("§/K");
            }
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
        if (node.IsHex)
        {
            if (node.IsUnsigned)
                return $"0x{node.UnsignedValue:X}";
            return $"0x{node.Value:X}";
        }
        return node.Value.ToString();
    }

    public string Visit(FloatLiteralNode node)
    {
        return node.IsDecimal
            ? $"DEC:{node.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
            : node.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public string Visit(DecimalLiteralNode node)
    {
        return "DEC:" + node.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public string Visit(StringLiteralNode node)
    {
        // Multiline strings round-trip as triple-quote
        if (node.IsMultiline && node.Value.Contains('\n'))
        {
            var multilineSuffix = node.IsUtf8 ? "u8" : "";
            return $"\"\"\"\n{node.Value}\"\"\"{multilineSuffix}";
        }

        var escaped = node.Value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
        var suffix = node.IsUtf8 ? "u8" : "";
        return $"\"{escaped}\"{suffix}";
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

        // If either branch contains section markers (§C, §NEW, §LAM, etc.) or commas
        // (tuple literals), decompose into §IF/§EL/§/I expression form.
        // Section markers and commas are invalid inside Lisp (?) expressions
        // but valid in §IF expression branches.
        if (ContainsSectionMarker(whenTrue) || ContainsSectionMarker(whenFalse)
            || whenTrue.Contains(',') || whenFalse.Contains(','))
        {
            var id = $"tern{_ternaryCounter++:D3}";
            return $"§IF{{{id}}} {condition} → {whenTrue} §EL → {whenFalse} §/I{{{id}}}";
        }

        return $"(? {condition} {whenTrue} {whenFalse})";
    }

    private static bool ContainsSectionMarker(string expr)
    {
        return expr.Contains('§');
    }

    /// <summary>
    /// Hoists an expression containing section markers to a temp variable.
    /// Adds a §B{~tempVar} expr line to _pendingHoistedLines and returns the temp var name.
    /// This prevents § markers from appearing inside Lisp (op ...) expressions.
    /// </summary>
    private string HoistToTempVar(string expr)
    {
        // Don't hoist outside of executable bodies (methods, ctors, operators, property accessors).
        // Hoisting at class/module scope would leak §B bindings between members.
        if (_memberBodyDepth == 0)
            return expr;

        var varName = $"_hoist{_hoistCounter++:D3}";
        _pendingHoistedLines.Add($"§B{{~{varName}}} {expr}");
        return varName;
    }

    public string Visit(ReferenceNode node)
    {
        return EscapeCalorIdentifier(node.Name);
    }

    public string Visit(BinaryOperationNode node)
    {
        var left = node.Left.Accept(this);
        var right = node.Right.Accept(this);
        var opSymbol = GetCalorOperatorSymbol(node.Operator);

        // Hoist operands containing section markers or commas (tuples) out of Lisp expression.
        // § markers and commas are invalid inside (op ...) expressions.
        if (ContainsSectionMarker(left) || left.Contains(','))
            left = HoistToTempVar(left);
        if (ContainsSectionMarker(right) || right.Contains(','))
            right = HoistToTempVar(right);

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

        if (ContainsSectionMarker(operand))
            operand = HoistToTempVar(operand);

        return $"({opSymbol} {operand})";
    }

    public string Visit(FieldAccessNode node)
    {
        var target = node.Target.Accept(this);
        if (node.Target is ThisExpressionNode)
            target = "this";
        else if (node.Target is BaseExpressionNode)
            target = "base";
        // Hoist call results that contain section markers (e.g., §C{method} §/C.Property)
        // Only hoist when inside an executable body (method, ctor, etc.)
        else if (ContainsSectionMarker(target) && _memberBodyDepth > 0)
            target = HoistToTempVar(target);
        return $"{target}.{node.FieldName}";
    }

    public string Visit(NewExpressionNode node)
    {
        var typeArgs = node.TypeArguments.Count > 0
            ? $"<{string.Join(",", node.TypeArguments)}>"
            : "";

        // Inside interpolation, use C# new() syntax instead of §NEW tags
        if (_inInterpolation)
        {
            var inlineArgs = node.Arguments.Select(a => a.Accept(this));
            return $"new {node.TypeName}{typeArgs}({string.Join(", ", inlineArgs)})";
        }

        // Arguments need §A prefix for parser to recognize them
        // Hoist arguments containing section markers (e.g., §C calls) to avoid nested §A conflicts
        var args = node.Arguments.Select(a =>
        {
            var val = a.Accept(this);
            if (ContainsSectionMarker(val))
                val = HoistToTempVar(val);
            return $"§A {val}";
        });
        var argsStr = node.Arguments.Count > 0 ? $" {string.Join(" ", args)}" : "";

        // Handle object initializers (multi-line block format)
        if (node.Initializers.Count > 0)
        {
            // Pre-evaluate initializer values to collect hoisted bindings
            var evalInits = new List<(string PropName, string Value)>();
            foreach (var init in node.Initializers)
            {
                var valueStr = init.Value.Accept(this);
                if (!string.IsNullOrWhiteSpace(valueStr))
                    evalInits.Add((init.PropertyName, valueStr));
            }

            var indent = new string(' ', _indentLevel * 2);
            var sb = new System.Text.StringBuilder();
            sb.Append($"§NEW{{{node.TypeName}{typeArgs}}}{argsStr}");
            foreach (var (propName, valueStr) in evalInits)
            {
                var safeName = propName.StartsWith("@") ? propName[1..] : propName;
                sb.Append($"\n{indent}  {safeName} = {valueStr}");
            }
            sb.Append($"\n{indent}§/NEW");
            return sb.ToString();
        }

        return $"§NEW{{{node.TypeName}{typeArgs}}}{argsStr} §/NEW";
    }

    public string Visit(AnonymousObjectCreationNode node)
    {
        var indent = new string(' ', _indentLevel * 2);
        var sb = new System.Text.StringBuilder();
        sb.Append("§ANON");
        foreach (var init in node.Initializers)
        {
            var value = init.Value.Accept(this);
            // Hoist values containing section markers
            if (ContainsSectionMarker(value))
                value = HoistToTempVar(value);
            // Strip @ prefix from verbatim C# identifiers (e.g., @class, @checked, @Version)
            var propName = init.PropertyName.StartsWith("@") ? init.PropertyName[1..] : init.PropertyName;
            sb.Append($"\n{indent}  {propName} = {value}");
        }
        sb.Append($"\n{indent}§/ANON");
        return sb.ToString();
    }

    public string Visit(CallExpressionNode node)
    {
        // Build type arguments suffix for generic method calls
        var typeArgsSuffix = node.TypeArguments is { Count: > 0 }
            ? $"<{string.Join(", ", node.TypeArguments)}>"
            : "";

        // Inside interpolation expressions, section markers (§C, §A, §/C) would be
        // treated as literal text by the parser. Use function-call syntax instead.
        if (_inInterpolation)
        {
            if (node.Arguments.Count == 0)
                return $"{node.Target}{typeArgsSuffix}()";

            var inlineArgs = node.Arguments.Select((a, i) =>
            {
                var argValue = a.Accept(this);
                if (node.ArgumentNames != null && i < node.ArgumentNames.Count && node.ArgumentNames[i] != null)
                    return $"{node.ArgumentNames[i]}: {argValue}";
                return argValue;
            });
            return $"{node.Target}{typeArgsSuffix}({string.Join(", ", inlineArgs)})";
        }

        // Escape braces in target to avoid conflicts with Calor tag syntax,
        // but preserve braces inside quoted string portions (e.g. "text {0}".FormatWith)
        var escapedTarget = EscapeBracesInIdentifier(node.Target.Replace("->", "."));
        var fullTarget = escapedTarget + typeArgsSuffix;

        if (node.Arguments.Count == 0)
            return $"§C{{{fullTarget}}} §/C";

        // Emit named argument labels as §A[name] value when present
        var args = node.Arguments.Select((a, i) =>
        {
            var argValue = a.Accept(this);
            if (node.ArgumentNames != null && i < node.ArgumentNames.Count && node.ArgumentNames[i] != null)
                return $"§A[{node.ArgumentNames[i]}] {argValue}";
            return $"§A {argValue}";
        });
        return $"§C{{{fullTarget}}} {string.Join(" ", args)} §/C";
    }

    /// <summary>
    /// Escapes braces in a method target to avoid conflicts with Calor tag syntax.
    /// Only escapes braces outside of quoted string portions — e.g. in
    /// <c>"Unexpected state: {0}".FormatWith</c>, the braces inside the string literal
    /// are preserved but any braces in the identifier portion are escaped.
    /// Handles regular strings ("..."), verbatim strings (@"..."), and character literals.
    /// </summary>
    internal static string EscapeBracesInIdentifier(string input)
    {
        if (!input.Contains('{') && !input.Contains('}'))
            return input;

        var sb = new StringBuilder(input.Length);
        bool inString = false;
        bool verbatim = false;
        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];

            if (!inString)
            {
                if (c == '@' && i + 1 < input.Length && input[i + 1] == '"')
                {
                    // Start of verbatim string @"..."
                    inString = true;
                    verbatim = true;
                    sb.Append(c);
                    sb.Append('"');
                    i++; // skip the "
                }
                else if (c == '"')
                {
                    // Start of regular string "..."
                    inString = true;
                    verbatim = false;
                    sb.Append(c);
                }
                else if (c == '{' || c == '}')
                {
                    sb.Append('\\');
                    sb.Append(c);
                }
                else
                {
                    sb.Append(c);
                }
            }
            else if (verbatim)
            {
                // Inside verbatim string: "" is an escaped quote, lone " ends the string
                if (c == '"')
                {
                    if (i + 1 < input.Length && input[i + 1] == '"')
                    {
                        // Escaped quote ""
                        sb.Append('"');
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        // End of verbatim string
                        inString = false;
                        sb.Append(c);
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            else
            {
                // Inside regular string: \" is an escaped quote, unescaped " ends the string
                if (c == '\\' && i + 1 < input.Length)
                {
                    // Escape sequence — emit both characters, skip next
                    sb.Append(c);
                    sb.Append(input[i + 1]);
                    i++;
                }
                else if (c == '"')
                {
                    // End of regular string
                    inString = false;
                    sb.Append(c);
                }
                else
                {
                    sb.Append(c);
                }
            }
        }
        return sb.ToString();
    }

    public string Visit(ThisExpressionNode node)
    {
        return "§THIS";
    }

    public string Visit(BaseExpressionNode node)
    {
        return "§BASE";
    }

    public string Visit(TupleLiteralNode node)
    {
        var evalElements = new List<string>();
        foreach (var e in node.Elements)
        {
            var val = e.Accept(this);
            // Hoist elements that start with '(' to avoid tuple/lisp ambiguity
            // e.g., ((> a b), (== c d)) would be misparse as nested lisp
            if (val.StartsWith("(") || ContainsSectionMarker(val))
                val = HoistToTempVar(val);
            evalElements.Add(val);
        }
        return $"({string.Join(", ", evalElements)})";
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
        var id = string.IsNullOrEmpty(node.Name) ? "_arr" : node.Name;

        if (node.Initializer.Count > 0)
        {
            // Return inline format so this works in expression context (return, call args, etc.)
            // Multi-line format is handled by EmitArrayCreationWithName for bind-statement context.
            var elements = string.Join(" ", node.Initializer.Select(e => e.Accept(this)));
            return $"§ARR{{{id}:{elementType}}} {elements} §/ARR{{{id}}}";
        }
        else if (node.Size != null)
        {
            var size = node.Size.Accept(this);
            // Hoist complex size expressions out of the attribute braces
            // (§C calls, parenthesized expressions, commas break attribute parsing)
            if (ContainsSectionMarker(size) || size.Contains('(') || size.Contains(',') || size.Contains(':'))
                size = HoistToTempVar(size);
            return $"§ARR{{{elementType}:{id}:{size}}}";
        }
        else
        {
            return $"§ARR{{{elementType}:{id}}}";
        }
    }

    public string Visit(ArrayAccessNode node)
    {
        var array = node.Array.Accept(this);
        var index = node.Index.Accept(this);
        if (_inInterpolation)
            return $"{array}[{index}]";
        if (node.Array is ReferenceNode)
            return $"§IDX{{{array}}} {index}";
        return $"§IDX {array} {index}";
    }

    public string Visit(ArrayLengthNode node)
    {
        var array = node.Array.Accept(this);
        return $"{array}.len";
    }

    public string Visit(LambdaExpressionNode node)
    {
        // Build §LAM{id:p1:t1:p2:t2} header
        // Format: §LAM{id[:async]:param1:type1[:param2:type2:...]}
        var headerParts = new List<string> { node.Id };
        if (node.IsStatic)
        {
            headerParts.Add("static");
        }
        if (node.IsAsync)
        {
            headerParts.Add("async");
        }
        foreach (var p in node.Parameters)
        {
            headerParts.Add(p.Name);
            headerParts.Add(p.TypeName != null ? TypeMapper.CSharpToCalor(p.TypeName) : "object");
        }
        var header = string.Join(":", headerParts);

        if (node.IsExpressionLambda && node.ExpressionBody != null)
        {
            var body = node.ExpressionBody.Accept(this);
            return $"§LAM{{{header}}} {body} §/LAM{{{node.Id}}}";
        }
        else if (node.StatementBody != null && node.StatementBody.Count > 0)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"§LAM{{{header}}}");

            // For short lambdas (1-2 statements), emit inline
            if (node.StatementBody.Count <= 2)
            {
                var stmts = node.StatementBody.Select(s => CaptureStatementOutput(s).Trim()).ToList();
                sb.Append(" ");
                sb.Append(string.Join(" ", stmts));
                sb.Append($" §/LAM{{{node.Id}}}");
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
                sb.Append($"§/LAM{{{node.Id}}}");
            }
            return sb.ToString();
        }
        else
        {
            // Empty lambda
            return $"§LAM{{{header}}} §/LAM{{{node.Id}}}";
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
        if (_inInterpolation)
            return $"await {awaited}";
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
                // Escape special characters for Calor string syntax
                var escaped = textPart.Text
                    .Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\n", "\\n")
                    .Replace("\r", "\\r")
                    .Replace("\t", "\\t");
                parts.Append(escaped);
            }
            else if (part is InterpolatedStringExpressionNode exprPart)
            {
                parts.Append("${");
                var savedInterpolation = _inInterpolation;
                _inInterpolation = true;
                parts.Append(exprPart.Expression.Accept(this));
                _inInterpolation = savedInterpolation;
                if (!string.IsNullOrEmpty(exprPart.AlignmentClause))
                {
                    parts.Append(",");
                    parts.Append(exprPart.AlignmentClause);
                }
                if (!string.IsNullOrEmpty(exprPart.FormatSpecifier))
                {
                    parts.Append(":");
                    parts.Append(exprPart.FormatSpecifier);
                }
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
        var savedInterpolation = _inInterpolation;
        _inInterpolation = true;
        var alignment = !string.IsNullOrEmpty(node.AlignmentClause) ? $",{node.AlignmentClause}" : "";
        var format = !string.IsNullOrEmpty(node.FormatSpecifier) ? $":{node.FormatSpecifier}" : "";
        var result = $"${{{node.Expression.Accept(this)}{alignment}{format}}}";
        _inInterpolation = savedInterpolation;
        return result;
    }

    public string Visit(NullCoalesceNode node)
    {
        var left = node.Left.Accept(this);
        var right = node.Right.Accept(this);
        return $"(?? {left} {right})";
    }

    public string Visit(NullConditionalNode node)
    {
        var target = node.Target.Accept(this);
        // Sanitize member name: collapse whitespace/newlines to single space
        // (multi-line C# chains from converter can produce newlines in member names)
        var memberName = System.Text.RegularExpressions.Regex.Replace(node.MemberName, @"\s+", " ").Trim();

        // Inside interpolation, use C# syntax to avoid nested quote conflicts
        if (_inInterpolation)
        {
            return $"{target}?.{memberName}";
        }

        // Escape quotes and backslashes in member name for safe embedding in string literal
        var escaped = memberName.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return $"(?. {target} \"{escaped}\")";
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
            NegatedPatternNode np => $"(not {EmitPattern(np.Inner)})",
            OrPatternNode orp => $"(or {EmitPattern(orp.Left)} {EmitPattern(orp.Right)})",
            AndPatternNode andp => $"(and {EmitPattern(andp.Left)} {EmitPattern(andp.Right)})",
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
    public string Visit(NegatedPatternNode node) => $"(not {EmitPattern(node.Inner)})";
    public string Visit(OrPatternNode node) => $"(or {EmitPattern(node.Left)} {EmitPattern(node.Right)})";
    public string Visit(AndPatternNode node) => $"(and {EmitPattern(node.Left)} {EmitPattern(node.Right)})";

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
        var parts = new List<string>();
        for (int i = 0; i < node.Patterns.Count; i++)
        {
            if (node.SlicePattern != null && i == node.SliceIndex)
            {
                parts.Add($"§REST{{{node.SlicePattern.Name}}}");
            }
            parts.Add(EmitPattern(node.Patterns[i]));
        }
        // Slice at end (or no non-slice patterns before it)
        if (node.SlicePattern != null && node.SliceIndex >= node.Patterns.Count)
        {
            parts.Add($"§REST{{{node.SlicePattern.Name}}}");
        }
        return $"§PLIST {string.Join(" ", parts)}";
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
        EmitDocComment(node);
        // Format: §EN{id:Name:vis} or §EN{id:Name:vis:underlyingType}
        var vis = GetVisibilityShorthand(node.Visibility);
        var attrs = EmitCSharpAttributes(node.CSharpAttributes);
        var header = node.UnderlyingType != null
            ? $"§EN{{{node.Id}:{node.Name}:{vis}:{node.UnderlyingType}}}{attrs}"
            : $"§EN{{{node.Id}:{node.Name}:{vis}}}{attrs}";
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
        var attrs = EmitCSharpAttributes(node.CSharpAttributes);
        if (attrs.Length > 0)
            AppendLine(attrs);
        var value = node.Value;
        // Strip C-style block comments from enum values (e.g., /*| StructureCollapse*/)
        if (value != null && value.Contains("/*"))
        {
            value = System.Text.RegularExpressions.Regex.Replace(value, @"/\*.*?\*/", "").Trim();
            // Clean up double spaces left by comment removal
            value = System.Text.RegularExpressions.Regex.Replace(value, @"\s+", " ").Trim();
        }
        // Convert char literal values (e.g., '\uE900') to hex integer format
        // since Calor lexer doesn't support single-quoted char literals
        if (value != null && value.StartsWith("'") && value.EndsWith("'") && value.Length > 2)
        {
            var charContent = value[1..^1];
            if (charContent.StartsWith("\\u") && charContent.Length == 6
                && int.TryParse(charContent[2..], System.Globalization.NumberStyles.HexNumber, null, out var charVal))
            {
                value = $"0x{charVal:X4}";
            }
            else if (charContent.Length == 1)
            {
                value = $"0x{(int)charContent[0]:X4}";
            }
        }
        var line = value != null
            ? $"{node.Name} = {value}"
            : node.Name;
        AppendLine(line);
        return "";
    }

    public string Visit(EnumExtensionNode node)
    {
        // Format: §EEXT{id:EnumName} (note: §EXT is for class inheritance)
        AppendLine($"§EEXT{{{node.Id}:{node.EnumName}}}");
        Indent();

        foreach (var method in node.Methods)
        {
            Visit(method);
            AppendLine();
        }

        Dedent();
        AppendLine($"§/EEXT{{{node.Id}}}");
        return "";
    }

    public string Visit(RecordCreationNode node)
    {
        var fields = string.Join(", ", node.Fields.Select(f => f.Value.Accept(this)));
        return $"§NEW{{{node.TypeName}}} {fields}";
    }

    // Generic type nodes
    public string Visit(TypeParameterNode node)
    {
        var variance = node.Variance switch
        {
            VarianceKind.In => "in ",
            VarianceKind.Out => "out ",
            _ => ""
        };
        return $"{variance}{node.Name}";
    }

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
            TypeConstraintKind.NotNull => "notnull",
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
        AppendLine($"§DEL{{{node.Id}:{node.Name}}}");
        Indent();

        foreach (var param in node.Parameters)
        {
            AppendLine(Visit(param));
        }
        if (node.Output != null)
        {
            AppendLine($"§O{{{TypeMapper.CSharpToCalor(node.Output.TypeName)}}}");
        }
        if (node.Effects != null)
        {
            var effects = string.Join(",", node.Effects.Effects);
            AppendLine($"§E{{{effects}}}");
        }

        Dedent();
        AppendLine($"§/DEL{{{node.Id}}}");
        return "";
    }

    public string Visit(EventDefinitionNode node)
    {
        var visibility = GetVisibilityShorthand(node.Visibility);
        var delegateType = TypeMapper.CSharpToCalor(node.DelegateType);

        if (node.HasAccessors)
        {
            AppendLine($"§EVT{{{node.Id}:{node.Name}:{visibility}:{delegateType}}}");
            Indent();

            if (node.AddBody != null)
            {
                AppendLine("§EADD");
                Indent();
                foreach (var stmt in node.AddBody)
                {
                    stmt.Accept(this);
                }
                Dedent();
                AppendLine("§/EADD");
            }

            if (node.RemoveBody != null)
            {
                AppendLine("§EREM");
                Indent();
                foreach (var stmt in node.RemoveBody)
                {
                    stmt.Accept(this);
                }
                Dedent();
                AppendLine("§/EREM");
            }

            Dedent();
            AppendLine($"§/EVT{{{node.Id}}}");
        }
        else
        {
            AppendLine($"§EVT{{{node.Id}:{node.Name}:{visibility}:{delegateType}}}");
        }

        return "";
    }

    public string Visit(EventSubscribeNode node)
    {
        var evt = node.Event.Accept(this);
        var handler = node.Handler.Accept(this);
        AppendLine($"§SUB {evt} {handler}");
        return "";
    }

    public string Visit(EventUnsubscribeNode node)
    {
        var evt = node.Event.Accept(this);
        var handler = node.Handler.Accept(this);
        AppendLine($"§UNSUB {evt} {handler}");
        return "";
    }

    // Modern operator nodes
    public string Visit(RangeExpressionNode node)
    {
        var parts = new List<string> { "§RANGE" };
        if (node.Start != null)
        {
            var start = node.Start.Accept(this);
            if (ContainsSectionMarker(start))
                start = HoistToTempVar(start);
            parts.Add(start);
        }
        if (node.End != null)
        {
            var end = node.End.Accept(this);
            // Don't hoist §^ expressions — they're lightweight range operands
            if (ContainsSectionMarker(end) && !end.StartsWith("§^"))
                end = HoistToTempVar(end);
            parts.Add(end);
        }
        return string.Join(" ", parts);
    }

    public string Visit(IndexFromEndNode node)
    {
        var offset = node.Offset.Accept(this);
        if (ContainsSectionMarker(offset))
            offset = HoistToTempVar(offset);
        return $"§^ {offset}";
    }

    public string Visit(WithExpressionNode node)
    {
        var target = node.Target.Accept(this);
        var assignments = string.Join("\n  ", node.Assignments.Select(a => a.Accept(this)));
        if (assignments.Length > 0)
            return $"§WITH {target}\n  {assignments}\n§/WITH";
        return $"§WITH {target}\n§/WITH";
    }

    public string Visit(WithPropertyAssignmentNode node)
    {
        var value = node.Value.Accept(this);
        return $"§SET{{{node.PropertyName}}} {value}";
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
            Visibility.ProtectedInternal => "prot-int",
            Visibility.Protected => "prot",
            Visibility.Internal => "int",
            Visibility.Private => "priv",
            _ => "priv"
        };
    }

    /// <summary>
    /// C# reserved words that require backtick escaping when used as identifiers in Calor.
    /// Matches the set in CSharpEmitter.SanitizeIdentifier (which adds @ prefix for C# output).
    /// </summary>
    private static readonly HashSet<string> CSharpReservedWords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "bool", "break",
        "case", "catch", "checked",
        "class", "const", "continue", "default",
        "delegate", "do", "double", "else", "enum",
        "event", "explicit", "extern", "finally", "fixed",
        "float", "for", "foreach", "goto", "if",
        "implicit", "in", "int", "interface", "internal",
        "is", "lock", "namespace", "new",
        "operator", "out", "override", "params",
        "private", "protected", "public", "readonly", "ref",
        "return", "sealed", "sizeof",
        "stackalloc", "static", "string", "struct", "switch",
        "throw", "try", "typeof",
        "unchecked", "unsafe", "using", "virtual",
        "void", "volatile", "while",
        // Contextual keywords that conflict when used as identifiers
        "var", "dynamic", "yield", "async", "await",
        "nameof", "when"
    };

    /// <summary>
    /// Wraps a name in backticks if it's a C# reserved word.
    /// This is the inverse of CSharpEmitter.SanitizeIdentifier which adds @ prefix.
    /// Handles both bare names ("class") and @-prefixed names ("@class") from Roslyn.
    /// </summary>
    /// <summary>
    /// Converts C# verbatim string literals (@"...") in raw passthrough targets
    /// to properly escaped regular string literals for Calor.
    /// </summary>
    private static string ConvertVerbatimStringsInTarget(string target)
    {
        // Find @"..." patterns and convert them
        var result = System.Text.RegularExpressions.Regex.Replace(target, @"@""((?:""""|[^""])*)""", match =>
        {
            // Decode verbatim string: "" → " and everything else is literal
            var content = match.Groups[1].Value.Replace("\"\"", "\"");
            // Re-escape for Calor regular strings
            content = content
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
            return $"\"{content}\"";
        });
        return result;
    }

    private static string EscapeCalorIdentifier(string name)
    {
        // Strip @ prefix if present (Roslyn's Identifier.Text includes it for verbatim identifiers)
        var bareName = name.StartsWith('@') ? name[1..] : name;
        return CSharpReservedWords.Contains(bareName) ? $"`{bareName}`" : name;
    }

    private void EmitEffects(EffectsNode? effects)
    {
        if (effects == null || effects.Effects.Count == 0)
            return;

        var effectCodes = effects.Effects
            .SelectMany(kvp => kvp.Value.Split(',').Select(v => EffectCodes.ToCompact(kvp.Key, v.Trim())))
            .Distinct();
        AppendLine($"§E{{{string.Join(",", effectCodes)}}}");
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
        var targetPrefix = node.Target != null ? $"{node.Target}:" : "";
        if (node.Arguments.Count == 0)
        {
            return $"[@{targetPrefix}{node.Name}]";
        }

        var args = string.Join(", ", node.Arguments.Select(FormatAttributeArgument));
        return $"[@{targetPrefix}{node.Name}({args})]";
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
        var args = node.Arguments.Select(a =>
        {
            var val = a.Accept(this);
            // Hoist values with section markers or unquoted commas (tuples)
            if (ContainsSectionMarker(val))
                val = HoistToTempVar(val);
            else if (val.Contains(',') && !val.StartsWith("\""))
                val = HoistToTempVar(val);
            return val;
        });
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

    public string Visit(TypeOperationNode node)
    {
        var operand = node.Operand.Accept(this);
        return node.Operation switch
        {
            TypeOp.Cast => $"(cast {StripNullableAnnotation(node.TargetType)} {operand})",
            TypeOp.Is => $"(is {operand} {node.TargetType})",
            TypeOp.As => $"(as {operand} {node.TargetType})",
            _ => throw new NotSupportedException($"Unknown type operation: {node.Operation}")
        };
    }

    /// <summary>
    /// Strips nullable annotation from cast types. For reference types (arrays, classes),
    /// the nullable `?` is a compile-time annotation with no runtime effect. Removing it
    /// prevents parser errors from `?` tokens inside Lisp expressions.
    /// </summary>
    private static string StripNullableAnnotation(string typeName)
    {
        if (typeName.StartsWith('?'))
            return typeName[1..];
        if (typeName.EndsWith('?'))
            return typeName[..^1];
        return typeName;
    }

    /// <summary>
    /// Converts Calor nullable prefix notation (?T) to postfix (T?) for cast expressions,
    /// so the parser can handle it via the existing postfix nullable path.
    /// </summary>
    private static string NullablePrefixToPostfix(string typeName)
    {
        if (typeName.StartsWith('?'))
        {
            return typeName[1..] + "?";
        }
        return typeName;
    }

    public string Visit(IsPatternNode node)
    {
        var operand = node.Operand.Accept(this);
        return node.VariableName != null
            ? $"(is {operand} {node.TargetType} {node.VariableName})"
            : $"(is {operand} {node.TargetType})";
    }

    // Fallback nodes for unsupported C# constructs

    public string Visit(FallbackExpressionNode node)
    {
        // Emit as §ERR "TODO: feature -- C#: code"
        // Uses §ERR followed by a string literal so the parser handles it
        // as ErrExpressionNode(StringLiteralNode) without cascading parse errors.
        var escapedCode = node.OriginalCSharp
            .Replace("\\", "\\\\")  // Escape backslashes first
            .Replace("\"", "'")     // Replace double quotes
            .Replace("\n", " ")     // Collapse newlines
            .Replace("\r", "");     // Remove carriage returns

        // Truncate very long code to avoid overly long error messages
        if (escapedCode.Length > 100)
        {
            escapedCode = escapedCode.Substring(0, 97) + "...";
        }

        return $"§ERR \"TODO: {node.FeatureName} -- C#: {escapedCode}\"";
    }

    public string Visit(ExpressionStatementNode node)
    {
        var expr = node.Expression.Accept(this);
        AppendLine(expr);
        return "";
    }

    public string Visit(FallbackCommentNode node)
    {
        // Emit as multi-line comment block with TODO
        AppendLine($"// TODO: Manual conversion needed [{node.FeatureName}]");

        // Split original C# into lines and emit each as a comment
        var lines = node.OriginalCSharp.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.TrimEnd('\r');
            AppendLine($"// C#: {trimmed}");
        }

        // Add suggestion if present
        if (!string.IsNullOrEmpty(node.Suggestion))
        {
            AppendLine($"// Suggestion: {node.Suggestion}");
        }

        return "";
    }

    public string Visit(TypeOfExpressionNode node)
    {
        return $"(typeof {node.TypeName})";
    }

    public string Visit(NameOfExpressionNode node)
    {
        var name = node.Name;
        // If the name contains generic brackets (e.g., AvaloniaList<object>.Count),
        // extract just the last identifier since nameof always evaluates to the simple name
        if (name.Contains('<') || name.Contains('>'))
        {
            var lastDot = name.LastIndexOf('.');
            if (lastDot >= 0)
                name = name[(lastDot + 1)..];
        }
        return $"(nameof {name})";
    }

    public string Visit(ExpressionCallNode node)
    {
        var target = node.TargetExpression.Accept(this);
        var args = node.Arguments.Select(a => $" §A {a.Accept(this)}").ToList();
        return $"§C {target}{string.Join("", args)} §/C";
    }

    public string Visit(RawCSharpNode node)
    {
        return $"§RAW\n{node.CSharpCode}\n§/RAW";
    }

    public string Visit(RawCSharpExpressionNode node)
    {
        return $"§CS{{{node.CSharpCode}}}";
    }

    public string Visit(PreprocessorDirectiveNode node)
    {
        AppendLine($"§PP{{{node.Condition}}}");
        foreach (var stmt in node.Body)
            stmt.Accept(this);
        if (node.ElseBody != null && node.ElseBody.Count > 0)
        {
            AppendLine("§PPE");
            foreach (var stmt in node.ElseBody)
                stmt.Accept(this);
        }
        AppendLine($"§/PP{{{node.Condition}}}");
        return "";
    }

    public string Visit(MemberPreprocessorBlockNode node)
    {
        AppendLine($"§PP{{{node.Condition}}}");
        EmitMemberPreprocessorMembers(node);
        if (node.ElseBranch != null)
        {
            AppendLine("§PPE");
            if (!string.IsNullOrEmpty(node.ElseBranch.Condition))
            {
                // #elif chain — emit as nested §PP{cond} inside the else
                Visit(node.ElseBranch);
            }
            else
            {
                // #else — emit members directly
                EmitMemberPreprocessorMembers(node.ElseBranch);
            }
        }
        AppendLine($"§/PP{{{node.Condition}}}");
        return "";
    }

    private void EmitMemberPreprocessorMembers(MemberPreprocessorBlockNode node)
    {
        foreach (var field in node.Fields) Visit(field);
        foreach (var prop in node.Properties) Visit(prop);
        foreach (var indexer in node.Indexers) Visit(indexer);
        foreach (var ctor in node.Constructors) Visit(ctor);
        foreach (var method in node.Methods) { Visit(method); AppendLine(); }
        foreach (var op in node.OperatorOverloads) { Visit(op); AppendLine(); }
        foreach (var evt in node.Events) Visit(evt);
    }

    public string Visit(TypePreprocessorBlockNode node)
    {
        AppendLine($"§PP{{{node.Condition}}}");
        EmitTypePreprocessorTypes(node);
        if (node.ElseBranch != null)
        {
            AppendLine("§PPE");
            if (!string.IsNullOrEmpty(node.ElseBranch.Condition))
            {
                Visit(node.ElseBranch);
            }
            else
            {
                EmitTypePreprocessorTypes(node.ElseBranch);
            }
        }
        AppendLine($"§/PP{{{node.Condition}}}");
        return "";
    }

    private void EmitTypePreprocessorTypes(TypePreprocessorBlockNode node)
    {
        foreach (var u in node.Usings) Visit(u);
        foreach (var cls in node.Classes) { Visit(cls); AppendLine(); }
        foreach (var iface in node.Interfaces) { Visit(iface); AppendLine(); }
        foreach (var en in node.Enums) { Visit(en); AppendLine(); }
        foreach (var del in node.Delegates) { Visit(del); AppendLine(); }
    }

    public string Visit(CSharpInteropBlockNode node)
    {
        AppendLine($"§CSHARP{{{node.CSharpCode}}}§/CSHARP");
        return "";
    }

    public string Visit(StackAllocNode node)
    {
        var elementType = TypeMapper.CSharpToCalor(node.ElementType);
        if (node.Size != null)
        {
            var size = node.Size.Accept(this);
            if (ContainsSectionMarker(size) || size.Contains(':'))
                size = HoistToTempVar(size);
            return $"§SALLOC{{{elementType}:{size}}}";
        }
        else if (node.Initializer.Count > 0)
        {
            var elements = string.Join(" ", node.Initializer.Select(e => e.Accept(this)));
            return $"§SALLOC{{{elementType}}} {elements} §/SALLOC";
        }
        return $"§SALLOC{{{elementType}:0}}";
    }

    public string Visit(UnsafeBlockNode node)
    {
        AppendLine($"§UNSAFE{{{node.Id}}}");
        Indent();
        foreach (var stmt in node.Body)
            stmt.Accept(this);
        Dedent();
        AppendLine($"§/UNSAFE{{{node.Id}}}");
        return "";
    }

    public string Visit(SyncBlockNode node)
    {
        var lockExpr = node.LockExpression.Accept(this);
        AppendLine($"§SYNC{{{node.Id}}} ({lockExpr})");
        Indent();
        foreach (var stmt in node.Body)
            stmt.Accept(this);
        Dedent();
        AppendLine($"§/SYNC{{{node.Id}}}");
        return "";
    }

    public string Visit(FixedStatementNode node)
    {
        var pointerType = TypeMapper.CSharpToCalor(node.PointerType);
        var init = node.Initializer.Accept(this);
        // Hoist complex initializers (e.g., §ADDR, §IDX) out of the braces
        if (ContainsSectionMarker(init) || init.Contains(':'))
            init = HoistToTempVar(init);
        AppendLine($"§FIXED{{{node.Id}:{node.PointerName}:{pointerType}:{init}}}");
        Indent();
        foreach (var stmt in node.Body)
            stmt.Accept(this);
        Dedent();
        AppendLine($"§/FIXED{{{node.Id}}}");
        return "";
    }

    public string Visit(AddressOfNode node)
    {
        var operand = node.Operand.Accept(this);
        return $"§ADDR {operand}";
    }

    public string Visit(PointerDereferenceNode node)
    {
        var operand = node.Operand.Accept(this);
        return $"§DEREF {operand}";
    }

    public string Visit(SizeOfNode node)
    {
        var typeName = TypeMapper.CSharpToCalor(node.TypeName);
        return $"§SIZEOF{{{typeName}}}";
    }

    public string Visit(MultiDimArrayCreationNode node)
    {
        var elementType = TypeMapper.CSharpToCalor(node.ElementType);

        if (node.DimensionSizes.Count > 0)
        {
            var evalDims = node.DimensionSizes.Select(d =>
            {
                var val = d.Accept(this);
                if (ContainsSectionMarker(val) || val.Contains('(') || val.Contains(',') || val.Contains(':'))
                    val = HoistToTempVar(val);
                return val;
            }).ToList();
            var dims = string.Join(":", evalDims);
            return $"§ARR2D{{{node.Id}:{node.Name}:{elementType}:{dims}}}";
        }
        else if (node.Initializer.Count > 0)
        {
            var id = string.IsNullOrEmpty(node.Name) ? "_arr2d" : node.Name;
            AppendLine($"§ARR2D{{{node.Id}:{id}:{elementType}}}");
            Indent();
            foreach (var row in node.Initializer)
            {
                var elements = string.Join(" ", row.Select(e => e.Accept(this)));
                AppendLine($"§ROW {elements}");
            }
            Dedent();
            AppendLine($"§/ARR2D{{{node.Id}}}");
            return "";
        }
        else
        {
            var zeros = string.Join(":", Enumerable.Repeat("0", node.Rank));
            return $"§ARR2D{{{node.Id}:{node.Name}:{elementType}:{zeros}}}";
        }
    }

    public string Visit(MultiDimArrayAccessNode node)
    {
        var array = node.Array.Accept(this);
        var indices = string.Join(" ", node.Indices.Select(i => i.Accept(this)));
        // For 3D+ arrays, the parser can't distinguish index count from surrounding args.
        // Encode dimension count in attributes: §IDX2D{3} for 3D access.
        if (node.Indices.Count > 2)
            return $"§IDX2D{{{node.Indices.Count}}} {array} {indices}";
        return $"§IDX2D {array} {indices}";
    }

    // Dependent Types: Refinement Types and Proof Obligations

    public string Visit(RefinementTypeNode node)
    {
        var predicate = node.Predicate.Accept(this);
        AppendLine($"§RTYPE{{{node.Id}:{node.Name}:{node.BaseTypeName}}} {predicate}");
        return "";
    }

    public string Visit(SelfRefNode node)
    {
        return "#";
    }

    public string Visit(ProofObligationNode node)
    {
        var condition = node.Condition.Accept(this);
        var desc = node.Description != null ? $":{node.Description}" : "";
        AppendLine($"§PROOF{{{node.Id}{desc}}} {condition}");
        return "";
    }

    public string Visit(IndexedTypeNode node)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"§ITYPE{{{node.Id}:{node.Name}:{node.BaseTypeName}:{node.SizeParam}}}");
        if (node.Constraint != null)
        {
            var constraint = node.Constraint.Accept(this);
            sb.Append($" {constraint}");
        }
        AppendLine(sb.ToString());
        return "";
    }
}
