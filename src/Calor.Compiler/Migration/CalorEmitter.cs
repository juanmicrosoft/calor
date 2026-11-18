using System.Text;
using Calor.Compiler.Ast;
using Calor.Compiler.Effects;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.Migration;

/// <summary>
/// Emits native Calor source from a Calor AST.
/// Uses infix expressions and indentation-only block structure.
/// </summary>
public sealed class CalorEmitter : IAstVisitor<string>
{
    private StringBuilder _builder = new();
    private int _indentLevel;
    private readonly ConversionContext _context;
    private bool _inInterpolation;
    private readonly List<string> _pendingHoistedLines = new();
    private int _hoistCounter;
    private int _conditionalExpressionDepth;
    private int _memberBodyDepth;
    private int _inlineObjectInitializers;
    private int _inlineCollections;
    private IReadOnlySet<ReferenceNode>? _memberAccessRoots;
    private IReadOnlyDictionary<IfStatementNode, ReturnStatementNode> _canonicalLiteralReturns =
        new Dictionary<IfStatementNode, ReturnStatementNode>();

    // How many lambda bodies deep we are emitting. A block lambda nested inside
    // ANOTHER lambda's body must never be hoisted to the enclosing statement:
    // the hoisted §B lands outside the outer lambda's parameter scope, so the
    // emitted Calor references a parameter that is not in scope there (review C1
    // - the output still parses, so #717's parse-keyed post-validation does not
    // rewrap it, and round-trip C# validation then fails the whole conversion).
    private int _lambdaBodyDepth;

    // Counter for "inline sibling expression contexts" — places where multiple
    // expressions are joined on the same emitted line (e.g. §A arg chains,
    // §ARR / §ROW / §SALLOC initializers). In these contexts a zero-arg
    // §C{...} MUST keep an explicit §/C closer; otherwise the parser will
    // greedily absorb the following sibling as the call's inline argument.
    // See RFC v0.6-call-closer-elision §3.2 and v0.6.1 BLOCKER #1/#2/#3.
    private int _inInlineSiblingContext;

    /// <summary>
    /// Visits <paramref name="node"/> inside an inline-sibling context.
    /// Suppresses zero-arg §/C elision for the duration of the visit so that
    /// a call result emitted as one of several same-line siblings (e.g.
    /// inside an §A chain or array initializer) keeps its explicit closer.
    /// </summary>
    private string AcceptInInlineSibling(AstNode node)
    {
        _inInlineSiblingContext++;
        try { return node.Accept(this); }
        finally { _inInlineSiblingContext--; }
    }

    public CalorEmitter(ConversionContext? context = null)
    {
        _context = context ?? new ConversionContext();
    }

    public IReadOnlyList<ConversionIssue> Issues => _context.Issues;

    internal Action<AstNode, int>? EffectRowDeclarationEmitted { get; init; }

    public string Emit(ModuleNode module)
    {
        _context.EmittedPreservations.Clear();
        _builder.Clear();
        _indentLevel = 0;
        _pendingHoistedLines.Clear();
        _hoistCounter = 0;
        _memberBodyDepth = 0;
        _lambdaBodyDepth = 0;
        _inInterpolation = false;
        _inInlineSiblingContext = 0;
        _inlineObjectInitializers = 0;
        _inlineCollections = 0;
        _canonicalLiteralReturns = IfStatementNode.GetCanonicalLiteralReturns(module);
        _memberAccessRoots = Parser.FindMemberAccessRoots(module);
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
            AppendIndentedLine(line);
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
            AppendIndentedLine(line);
        }
    }

    private void AppendIndentedLine(string line)
    {
        var indent = new string(' ', _indentLevel * 2);
        if (line.Contains('\n'))
        {
            _builder.AppendLine(RawCSharpFrame.RewriteOutsideFrames(line, text =>
                indent + text.Replace("\n", "\n" + indent, StringComparison.Ordinal)));
        }
        else
        {
            _builder.Append(indent);
            _builder.AppendLine(line);
        }
    }

    private void AppendEffectRowDeclaration(AstNode declaration, string line)
    {
        FlushHoistedLines();
        EffectRowDeclarationEmitted?.Invoke(declaration, _builder.Length + _indentLevel * 2);
        AppendLine(line);
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
        EmitDocumentationText(node.DocComment);
    }

    private void EmitDocumentationText(string text)
    {
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        foreach (var line in normalized.Split('\n'))
        {
            AppendLine($"/// {line.Trim()}");
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
        // #903 cluster 1: isolate the hoist queue. Without this, bindings hoisted by
        // an EARLIER sibling expression (still pending at capture time) were flushed
        // into the captured statement at column 0, and bindings hoisted INSIDE the
        // capture stayed pending for the enclosing statement. Either way the
        // captured text carried a raw newline into a single-line context.
        var savedPending = _pendingHoistedLines.ToList();
        _pendingHoistedLines.Clear();

        // Create temporary builder
        _builder = new StringBuilder();
        _indentLevel = 0;

        // Visit the statement (this will append to the temp builder)
        stmt.Accept(this);

        // Capture result. Any binding still pending belongs to this statement
        // (the visitor returned without an AppendLine), so it leads the capture.
        var leftover = _pendingHoistedLines.ToList();
        var result = RawCSharpFrame.RewriteOutsideFrames(
            _builder.ToString(), text => text.TrimEnd('\r', '\n'));
        if (leftover.Count > 0)
            result = string.Join("\n", leftover) + "\n" + result;

        // Restore original builder and the caller's hoist queue
        _builder = savedBuilder;
        _indentLevel = savedIndent;
        _pendingHoistedLines.Clear();
        _pendingHoistedLines.AddRange(savedPending);

        return result;
    }

    /// <summary>
    /// Renders every statement of a block-bodied lambda at indent 0 (each capture
    /// keeps its own relative indentation). Shared by the inline attempt and the
    /// block form so a lambda is visited exactly once regardless of which form wins.
    /// </summary>
    private List<string> CaptureLambdaStatements(LambdaExpressionNode node, bool inlineSibling)
    {
        if (inlineSibling) _inInlineSiblingContext++;
        _lambdaBodyDepth++;
        try
        {
            return node.StatementBody!
                .Select(s => RawCSharpFrame.RewriteOutsideFrames(
                    CaptureStatementOutput(s), text => text.Trim()))
                .ToList();
        }
        finally
        {
            _lambdaBodyDepth--;
            if (inlineSibling) _inInlineSiblingContext--;
        }
    }

    public string Visit(ModuleNode node)
    {
        EmitDocComment(node);
        foreach (var issue in node.Issues) Visit(issue);
        foreach (var decision in node.Decisions) Visit(decision);
        if (node.Context != null) Visit(node.Context);
        // Module header — sanitize backtick (C# generic arity) and @ from name
        var moduleName = node.Name.Replace("`", "").Replace("@", "");
        if (node.NamespaceScopes.Count > 1 || node.NamespaceScopes.Any(scope => scope.ParentScopeId != null))
            throw new InvalidOperationException("Native modules support one namespace per file; preserve other topologies as whole-file C#.");
        var namespaceScope = node.NamespaceScopes.SingleOrDefault();
        var declarationNamespaces = EnumerateModuleDeclarations(node)
            .Select(declaration => declaration.NamespaceIdentity).Where(name => name != null)
            .Distinct(StringComparer.Ordinal).ToArray();
        var namespaceName = namespaceScope?.FullName
            ?? node.NamespaceIdentity ?? declarationNamespaces.FirstOrDefault() ?? "";
        if (declarationNamespaces.Any(name => name != namespaceName)
            || node.Usings.Any(directive => directive.NamespaceScopeId != null
                && (namespaceScope == null || namespaceScope.IsGlobal
                    || directive.NamespaceScopeId != namespaceScope.Id
                    || directive.NamespaceIdentity != namespaceName || directive.IsGlobal)))
            throw new InvalidOperationException("Native module headers cannot flatten distinct namespace or using scopes; preserve the original C#.");
        var namespaceHeader = namespaceName.Length == 0 ? "" : " " + EscapeNamespaceName(namespaceName);
        var origin = namespaceScope is { IsGlobal: false, IsFileScoped: false } ? " block" : "";
        var overflow = node.Attributes["overflow"] is { } mode ? $" overflow {mode}" : "";
        var nullable = node.HasObliviousNullability() ? " nullable oblivious" : "";
        AppendLine($"module {EscapeCalorIdentifier(moduleName)}{namespaceHeader}{origin}{overflow}{nullable}");
        Indent();

        if (node.DeclaredSemanticsVersion != null)
        {
            AppendLine($"semver \"{node.DeclaredSemanticsVersion}\"");
        }
        foreach (var assumption in node.Assumptions) Visit(assumption);
        foreach (var invariant in node.Invariants) Visit(invariant);

        EmitModuleItems(node, scopeId: null, includeUsings: true);

        Dedent();

        return _builder.ToString();
    }

    private void EmitNamespaceScope(
        ModuleNode module,
        NamespaceScopeInfo scope,
        HashSet<string> emittedScopeIds)
    {
        emittedScopeIds.Add(scope.Id);
        AppendLine(
            $"§NS{{{scope.Name}{GetNamespaceScopeMarkers(scope)}}}");
        Indent();
        EmitModuleItems(module, scope.Id, includeUsings: true);
        foreach (var child in module.NamespaceScopes.Where(item => item.ParentScopeId == scope.Id))
            EmitNamespaceScope(module, child, emittedScopeIds);
        Dedent();
        AppendLine();
    }

    private void EmitModuleItems(
        ModuleNode node,
        string? scopeId,
        bool includeUsings,
        bool exactUnscoped = false,
        string? unscopedNamespaceIdentity = null,
        bool includeMissingNamespaceIdentity = false,
        bool includeUnscopedCompilerItems = false,
        IReadOnlyCollection<AstNode>? excludedItems = null)
    {
        bool InScope(AstNode item)
        {
            if (excludedItems?.Contains(item) == true)
                return false;
            if (exactUnscoped)
            {
                return item.NamespaceScopeId == null
                       && (string.Equals(
                               item.NamespaceIdentity,
                               unscopedNamespaceIdentity,
                               StringComparison.Ordinal)
                           || includeMissingNamespaceIdentity
                           && item.NamespaceIdentity == null);
            }

            return scopeId == null
                   || item.NamespaceScopeId == scopeId
                   || includeUnscopedCompilerItems
                   && scopeId == ""
                   && item.NamespaceScopeId == null
                   && item is CompilerDirectiveNode;
        }

        if (node.Items.Count > 0)
        {
            foreach (var item in node.Items.Where(InScope))
            {
                if (!includeUsings && item is UsingDirectiveNode)
                    continue;
                EmitModuleItem(item);
            }
            return;
        }

        if (includeUsings)
        {
            foreach (var usingDirective in node.Usings.Where(InScope))
                Visit(usingDirective);
            if (node.Usings.Any(InScope))
                AppendLine();
        }

        foreach (var iface in node.Interfaces.Where(InScope))
        {
            Visit(iface);
            AppendLine();
        }
        foreach (var enumDef in node.Enums.Where(InScope))
        {
            Visit(enumDef);
            AppendLine();
        }
        foreach (var enumExt in node.EnumExtensions.Where(InScope))
        {
            Visit(enumExt);
            AppendLine();
        }
        foreach (var del in node.Delegates.Where(InScope))
        {
            Visit(del);
            AppendLine();
        }
        foreach (var cls in node.Classes.Where(InScope))
        {
            Visit(cls);
            AppendLine();
        }
        foreach (var rtype in node.RefinementTypes.Where(InScope))
            Visit(rtype);
        foreach (var itype in node.IndexedTypes.Where(InScope))
            Visit(itype);
        foreach (var func in node.Functions.Where(InScope))
        {
            Visit(func);
            AppendLine();
        }
        foreach (var interop in node.InteropBlocks.Where(InScope))
        {
            Visit(interop);
            AppendLine();
        }
        foreach (var preprocessor in node.TypePreprocessorBlocks.Where(InScope))
        {
            Visit(preprocessor);
            AppendLine();
        }
    }

    private static IEnumerable<AstNode> EnumerateModuleDeclarations(ModuleNode node)
        => node.Interfaces.Cast<AstNode>()
            .Concat(node.Enums)
            .Concat(node.EnumExtensions)
            .Concat(node.Delegates)
            .Concat(node.Classes)
            .Concat(node.RefinementTypes)
            .Concat(node.IndexedTypes)
            .Concat(node.Functions)
            .Concat(node.InteropBlocks)
            .Concat(node.TypePreprocessorBlocks.Where(
                ContainsConditionalDeclarations));

    private static bool ContainsConditionalDeclarations(
        TypePreprocessorBlockNode block)
        => block.Classes.Count > 0
           || block.Interfaces.Count > 0
           || block.Enums.Count > 0
           || block.Delegates.Count > 0
           || block.InteropBlocks.Any(interop =>
               !IsCompilerDirectiveCode(interop.CSharpCode))
           || block.NestedBlocks.Any(ContainsConditionalDeclarations)
           || block.ElseBranch != null
           && ContainsConditionalDeclarations(block.ElseBranch);

    private static bool IsCompilerDirectiveCode(string code)
        => code.TrimStart().StartsWith("#", StringComparison.Ordinal);

    private static string GetNamespaceScopeMarkers(NamespaceScopeInfo scope)
    {
        var markers = new List<string>();
        if (scope.IsGlobal)
            markers.Add("global");
        else if (scope.Name == "_global")
            markers.Add("named");
        if (scope.IsFileScoped)
            markers.Add("file");
        return markers.Count == 0 ? "" : ":" + string.Join(":", markers);
    }

    private void EmitModuleItem(AstNode item)
    {
        switch (item)
        {
            case UsingDirectiveNode node:
                Visit(node);
                break;
            case InterfaceDefinitionNode node:
                Visit(node);
                AppendLine();
                break;
            case ClassDefinitionNode node:
                Visit(node);
                AppendLine();
                break;
            case EnumDefinitionNode node:
                Visit(node);
                AppendLine();
                break;
            case EnumExtensionNode node:
                Visit(node);
                AppendLine();
                break;
            case DelegateDefinitionNode node:
                Visit(node);
                AppendLine();
                break;
            case FunctionNode node:
                Visit(node);
                AppendLine();
                break;
            case CSharpInteropBlockNode node:
                Visit(node);
                AppendLine();
                break;
            case CompilerDirectiveNode node:
                Visit(node);
                break;
            case TypePreprocessorBlockNode node:
                Visit(node);
                AppendLine();
                break;
            case RefinementTypeNode node:
                Visit(node);
                break;
            case IndexedTypeNode node:
                Visit(node);
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported source-ordered module item: {item.GetType().Name}");
        }
    }

    public string Visit(UsingDirectiveNode node)
    {
        var globalPrefix = !string.IsNullOrEmpty(node.NamespaceScopeId) && !string.IsNullOrEmpty(node.NamespaceIdentity)
            ? "namespace " : node.IsGlobal ? "global " : "";
        if (node.IsStatic)
        {
            AppendLine($"{globalPrefix}using static {node.Namespace}");
        }
        else if (node.Alias != null)
        {
            AppendLine($"{globalPrefix}using {EscapeCalorIdentifier(node.Alias)} = {node.Namespace}");
        }
        else
        {
            AppendLine($"{globalPrefix}using {node.Namespace}");
        }
        return "";
    }

    private static string EscapeNamespaceName(string name) =>
        string.Join(".", name.Split('.').Select(segment => segment is "overflow" or "block" or "nullable"
            ? "@" + segment : EscapeCalorIdentifier(segment.TrimStart('@'))));

    public string Visit(InterfaceDefinitionNode node)
    {
        EmitDocComment(node);
        var typeParams = node.TypeParameters.Count > 0
            ? $"<{string.Join(",", node.TypeParameters.Select(tp => Visit(tp)))}>"
            : "";
        var visibility = node.Visibility == Visibility.Public ? "" : " " + GetVisibilityShorthand(node.Visibility);
        var attrs = EmitCSharpAttributes(node.CSharpAttributes);

        AppendLine($"interface {EscapeCalorIdentifier(node.Name)}{typeParams}{visibility}{attrs}");
        Indent();

        EmitTypeParameterConstraints(node.TypeParameters);
        foreach (var baseInterface in node.BaseInterfaces)
            AppendLine($"extends {TypeMapper.ToSurfaceType(baseInterface)}");

        if (node.Items.Count > 0)
        {
            foreach (var item in node.Items)
            {
                switch (item)
                {
                    case PropertyNode property:
                        Visit(property);
                        break;
                    case IndexerNode indexer:
                        Visit(indexer);
                        break;
                    case MethodSignatureNode method:
                        Visit(method);
                        break;
                    case CSharpInteropBlockNode interop:
                        Visit(interop);
                        break;
                    case CompilerDirectiveNode directive:
                        Visit(directive);
                        break;
                    case MemberPreprocessorBlockNode preprocessor:
                        Visit(preprocessor);
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"Unsupported source-ordered interface item: {item.GetType().Name}");
                }
            }
        }
        else
        {
            foreach (var prop in node.Properties)
                Visit(prop);
            foreach (var indexer in node.Indexers)
                Visit(indexer);
            foreach (var method in node.Methods)
                Visit(method);
            foreach (var interop in node.InteropBlocks)
                Visit(interop);
            foreach (var preprocessor in node.PreprocessorBlocks)
                Visit(preprocessor);
        }

        Dedent();

        return "";
    }

    /// <summary>
    /// Format inline parameter list for compact signatures: (type:name, type:name, ...)
    /// </summary>
    private string? TryFormatInlineParams(IReadOnlyList<ParameterNode> parameters)
    {
        return string.Join(", ", parameters.Select((p, index) =>
        {
            var typeName = TypeMapper.ToSurfaceType(p.TypeName);
            if (index > 0 && parameters[index - 1].Row != null
                && EffectCodes.IsKnownCompactCode($"{typeName}:{p.Name}"))
                typeName = "@" + typeName;
            var modifiers = new List<string>();
            if (p.Modifier.HasFlag(ParameterModifier.This)) modifiers.Add("this");
            if (p.Modifier.HasFlag(ParameterModifier.Ref)) modifiers.Add("ref");
            if (p.Modifier.HasFlag(ParameterModifier.Out)) modifiers.Add("out");
            if (p.Modifier.HasFlag(ParameterModifier.In)) modifiers.Add("in");
            if (p.Modifier.HasFlag(ParameterModifier.Params)) modifiers.Add("params");
            var modStr = modifiers.Count > 0 ? $"{string.Join(" ", modifiers)} " : "";
            var attributes = p.CSharpAttributes.Count == 0 ? "" : $" {EmitCSharpAttributes(p.CSharpAttributes)}";
            var refinement = p.InlineRefinement == null ? "" : $" | {p.InlineRefinement.Predicate.Accept(this)}";
            var defaultValue = p.DefaultValue == null ? "" : $" = {p.DefaultValue.Accept(this)}";
            return $"{modStr}{typeName}:{EscapeCalorIdentifier(p.Name)}{attributes}{FormatEffectRow(p.Row)}{refinement}{defaultValue}";
        }));
    }

    private void EmitParameterLines(IReadOnlyList<ParameterNode> parameters)
    {
        foreach (var param in parameters)
            AppendLine(Visit(param));
    }

    // Otherwise an initial parenthesized callee can be mistaken for the declaration's signature.
    private void EmitOutputLine(OutputNode? output)
    {
        if (output != null)
            AppendLine(Visit(output));
    }

    public string Visit(MethodSignatureNode node)
    {
        EmitDocComment(node);
        var (typeParams, typeParamsAfterGroup) =
            SplitTypeParameterList(node.TypeParameters, node.EffectParameters);
        var attrs = EmitCSharpAttributes(node.CSharpAttributes);

        var inlineFmt = TryFormatInlineParams(node.Parameters);
        if (inlineFmt != null)
        {
            var inlineParams = $" ({inlineFmt})";
            var inlineReturn = node.Output != null ? $" → {TypeMapper.ToSurfaceType(node.Output.TypeName)}{FormatEffectRow(node.Output.Row)}" : "";
            AppendEffectRowDeclaration(node, $"func {EscapeCalorIdentifier(node.Name)}{attrs}{typeParams}{typeParamsAfterGroup}{inlineParams}{inlineReturn}");
        }
        else
        {
            throw new InvalidOperationException("Canonical interface method parameters require an inline signature.");
        }
        if (node.TypeParameters.Any(typeParameter =>
                typeParameter.Constraints.Count > 0))
        {
            Indent();
            EmitTypeParameterConstraints(node.TypeParameters);
            Dedent();
        }

        // #1173: an interface member's own §E. The parser has always read one here
        // and CheckEffectVariance (Calor0421) has always checked against it, but
        // this visitor dropped it, so a row on an interface member could not
        // survive a round trip. That is load-bearing now: an implementation may not
        // broaden its interface's row, so the row synthesised for a method has to
        // travel to the interface it implements or the converter would trade
        // Calor0410 for Calor0421.
        Indent();
        EmitEffects(node.Effects, required: true);
        foreach (var precondition in node.Preconditions) Visit(precondition);
        foreach (var postcondition in node.Postconditions) Visit(postcondition);
        Dedent();


        return "";
    }

    private bool _emittingModuleMembers;

    public string Visit(ClassDefinitionNode node)
    {
        var previous = _emittingModuleMembers;
        _emittingModuleMembers = node.IsModule;
        try
        {
            return EmitTypeDeclaration(node);
        }
        finally
        {
            _emittingModuleMembers = previous;
        }
    }

    private string EmitTypeDeclaration(ClassDefinitionNode node)
    {
        EmitDocComment(node);
        var modifiers = new List<string>();
        if (node.IsAbstract) modifiers.Add("abstract");
        if (node.IsSealed) modifiers.Add("sealed");
        if (node.IsPartial) modifiers.Add("partial");
        if (node.IsStatic && !node.IsModule) modifiers.Add("static");
        if (node.IsStruct) modifiers.Add("struct");
        if (node.IsReadOnly) modifiers.Add("readonly");

        var vis = node.Visibility == Visibility.Public ? "" : " " + GetVisibilityShorthand(node.Visibility);
        var modStr = modifiers.Count > 0 ? $" {string.Join(" ", modifiers)}" : "";

        var typeParams = node.TypeParameters.Count > 0
            ? $"<{string.Join(",", node.TypeParameters.Select(tp => Visit(tp)))}>"
            : "";
        var attrs = EmitCSharpAttributes(node.CSharpAttributes);

        var declarationTag = node.IsModule ? "M" : "CL";
        if (node.IsRecord)
        {
            var positional = node.RecordParameters == null ? "" :
                " (" + TryFormatInlineParams(node.RecordParameters) + ")";
            var baseCall = node.RecordBaseArguments == null ? "" :
                $" : {TypeMapper.ToSurfaceType(node.BaseClass!)}({string.Join(", ", node.RecordBaseArguments.Select(argument => argument.Accept(this)))})";
            AppendEffectRowDeclaration(node, $"record {EscapeCalorIdentifier(node.Name)}{typeParams}{vis}{modStr}{attrs}{positional}{baseCall}");
        }
        else
            AppendEffectRowDeclaration(node, $"{(node.IsModule ? "module" : "class")} {EscapeCalorIdentifier(node.Name)}{typeParams}{vis}{modStr}{attrs}");
        Indent();
        // 0.30 (D1): the implicit static initialization row is the type's first header clause.
        EmitEffects(node.StaticInitializerEffects);

        // Emit type parameter constraints
        EmitTypeParameterConstraints(node.TypeParameters);

        // Emit base class as §EXT tag (not positional)
        if (node.BaseClass != null && node.RecordBaseArguments == null)
        {
            AppendLine($"extends {TypeMapper.ToSurfaceType(node.BaseClass)}");
        }

        // Emit implemented interfaces
        foreach (var iface in node.ImplementedInterfaces)
        {
            AppendLine($"implements {TypeMapper.ToSurfaceType(iface)}");
        }
        if (node.BaseClass != null || node.ImplementedInterfaces.Count > 0 || node.TypeParameters.Any(tp => tp.Constraints.Count > 0))
            AppendLine();

        if (node.Items.Count > 0)
        {
            foreach (var item in node.Items)
            {
                if (!ReferenceEquals(item, node.RecordPrimaryConstructor)
                    && !ReferenceEquals(item, node.RecordCopyConstructor)
                    && !node.RecordPositionalProperties.Contains(item))
                    EmitClassItem(item);
            }
        }
        else
        {
            foreach (var field in node.Fields)
                Visit(field);
            if (node.Fields.Count > 0)
                AppendLine();
            foreach (var evt in node.Events)
                Visit(evt);
            if (node.Events.Count > 0)
                AppendLine();
            foreach (var prop in node.Properties)
            {
                if (node.RecordPositionalProperties.Contains(prop))
                    continue;
                Visit(prop);
            }
            if (node.Properties.Count > 0)
                AppendLine();
            foreach (var indexer in node.Indexers)
                Visit(indexer);
            if (node.Indexers.Count > 0)
                AppendLine();
            foreach (var ctor in node.Constructors)
            {
                if (ReferenceEquals(ctor, node.RecordPrimaryConstructor) || ReferenceEquals(ctor, node.RecordCopyConstructor))
                    continue;
                Visit(ctor);
                AppendLine();
            }
            foreach (var method in node.Methods)
            {
                Visit(method);
                AppendLine();
            }
            foreach (var op in node.OperatorOverloads)
            {
                Visit(op);
                AppendLine();
            }
            foreach (var interop in node.InteropBlocks)
            {
                Visit(interop);
                AppendLine();
            }
            foreach (var ppBlock in node.PreprocessorBlocks)
            {
                Visit(ppBlock);
                AppendLine();
            }
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
        }

        Dedent();

        return "";
    }

    private void EmitClassItem(AstNode item)
    {
        switch (item)
        {
            case ClassFieldNode node:
                Visit(node);
                break;
            case PropertyNode node:
                Visit(node);
                break;
            case IndexerNode node:
                Visit(node);
                break;
            case ConstructorNode node:
                Visit(node);
                AppendLine();
                break;
            case MethodNode node:
                Visit(node);
                AppendLine();
                break;
            case EventDefinitionNode node:
                Visit(node);
                break;
            case OperatorOverloadNode node:
                Visit(node);
                AppendLine();
                break;
            case CSharpInteropBlockNode node:
                Visit(node);
                AppendLine();
                break;
            case CompilerDirectiveNode node:
                Visit(node);
                break;
            case MemberPreprocessorBlockNode node:
                Visit(node);
                AppendLine();
                break;
            case ClassDefinitionNode node:
                Visit(node);
                AppendLine();
                break;
            case InterfaceDefinitionNode node:
                Visit(node);
                AppendLine();
                break;
            case EnumDefinitionNode node:
                Visit(node);
                AppendLine();
                break;
            case DelegateDefinitionNode node:
                Visit(node);
                AppendLine();
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported source-ordered class item: {item.GetType().Name}");
        }
    }

    public string Visit(ClassFieldNode node)
    {
        EmitDocComment(node);
        var visibility = node.Visibility == Visibility.Public ? "" : " " + GetVisibilityShorthand(node.Visibility);
        var typeName = TypeMapper.ToSurfaceType(node.TypeName);
        // #1595: a collection default (§LIST/§HSET/§DICT) is written inline with its closer. It
        // used to become "= default" (null), silently dropping the collection and its elements.
        string defaultVal;
        if (node.DefaultValue != null)
            defaultVal = $" = {AcceptMemberDefault(node.DefaultValue)}";
        else
            defaultVal = "";
        var attrs = EmitCSharpAttributes(node.CSharpAttributes);

        var modifiers = new List<string>();
        if (node.IsRequired) modifiers.Add("required");
        if (node.Modifiers.HasFlag(MethodModifiers.Static)) modifiers.Add("static");
        if (node.Modifiers.HasFlag(MethodModifiers.Const)) modifiers.Add("const");
        if (node.Modifiers.HasFlag(MethodModifiers.Readonly)) modifiers.Add("readonly");
        if (node.IsVolatile) modifiers.Add("volatile");
        var modStr = modifiers.Count > 0 ? $" {string.Join(" ", modifiers)}" : "";

        AppendLine($"field {typeName}:{EscapeCalorIdentifier(node.Name)}{visibility}{modStr}{attrs}{FormatEffectRow(node.Row)}{defaultVal}");

        return "";
    }

    /// <summary>
    /// #1524: a field or property default renders object initializers on its own line
    /// (<c>§NEW{T} A = x §/NEW</c>). The multi-line form is a statement-level layout; after a
    /// member header it was not read back as the default.
    /// </summary>
    private string AcceptMemberDefault(ExpressionNode value)
    {
        _inlineObjectInitializers++;
        _inlineCollections++;
        try
        {
            return value.Accept(this);
        }
        finally
        {
            _inlineObjectInitializers--;
            _inlineCollections--;
        }
    }

    public string Visit(PropertyNode node)
    {
        EmitDocComment(node);
        var visibility = node.Visibility == Visibility.Public ? "" : " " + GetVisibilityShorthand(node.Visibility);
        var typeName = TypeMapper.ToSurfaceType(node.TypeName);
        var attrs = EmitCSharpAttributes(node.CSharpAttributes);

        var modifiers = new List<string>();
        if (node.IsRequired) modifiers.Add("required");
        if (node.IsVirtual) modifiers.Add("virtual");
        if (node.IsOverride) modifiers.Add("override");
        if (node.IsAbstract) modifiers.Add("abstract");
        if (node.IsSealed) modifiers.Add("sealed");
        if (node.IsStatic) modifiers.Add("static");
        var modStr = modifiers.Count > 0 ? $" {string.Join(" ", modifiers)}" : "";

        // Compact auto-property shorthand. #1524: a default with an object initializer stays on
        // the header line too (see AcceptMemberDefault).
        if (node.IsAutoProperty && (node.Getter != null || node.Setter != null || node.Initer != null))
        {
            var defaultVal = node.DefaultValue != null ? $" = {AcceptMemberDefault(node.DefaultValue)}" : "";
            // v0.17 S1, round-4 finding — the row goes here, before the
            // default value, because that is where the parser reads it
            // (Parser.cs TryParseSameLineRow(EffectRowPosition.Property)).
            // Omitting it let a Calor -> Calor re-emit silently strip a
            // hand-written `§PROP{...} §E{cw}` and reopen the escape S1 closed.
            AppendLine($"property {typeName}:{EscapeCalorIdentifier(node.Name)}{visibility}{modStr}{attrs}{FormatEffectRow(node.Row)}{defaultVal}");
            Indent();
            if (node.Getter != null) Visit(node.Getter);
            if (node.Setter != null) Visit(node.Setter);
            if (node.Initer != null) Visit(node.Initer);
            Dedent();
            return "";
        }

        // Full property syntax with body and closing tag
        AppendLine($"property {typeName}:{EscapeCalorIdentifier(node.Name)}{visibility}{modStr}{attrs}{FormatEffectRow(node.Row)}");
        Indent();

        if (node.Getter != null) Visit(node.Getter);
        if (node.Setter != null) Visit(node.Setter);
        if (node.Initer != null) Visit(node.Initer);

        if (node.DefaultValue != null)
        {
            AppendLine($"= {AcceptMemberDefault(node.DefaultValue)}");
        }

        Dedent();

        return "";
    }

    public string Visit(IndexerNode node)
    {
        EmitDocComment(node);
        var visibility = node.Visibility == Visibility.Public ? "" : " " + GetVisibilityShorthand(node.Visibility);
        var typeName = TypeMapper.ToSurfaceType(node.TypeName);
        var attrs = EmitCSharpAttributes(node.CSharpAttributes);

        var modifiers = new List<string>();
        if (node.IsVirtual) modifiers.Add("virtual");
        if (node.IsOverride) modifiers.Add("override");
        if (node.IsAbstract) modifiers.Add("abstract");
        if (node.IsSealed) modifiers.Add("sealed");
        var modStr = modifiers.Count > 0 ? $" {string.Join(" ", modifiers)}" : "";
        var parameters = TryFormatInlineParams(node.Parameters)
            ?? throw new InvalidOperationException("Canonical indexer parameters require an inline signature.");
        AppendLine($"this[{parameters}] → {typeName}{visibility}{modStr}{attrs}");
        Indent();
        if (node.Getter != null) Visit(node.Getter);
        if (node.Setter != null) Visit(node.Setter);
        if (node.Initer != null) Visit(node.Initer);

        Dedent();
        return "";
    }

    public string Visit(PropertyAccessorNode node)
    {
        var keyword = node.Kind switch
        {
            PropertyAccessorNode.AccessorKind.Get => "get",
            PropertyAccessorNode.AccessorKind.Set => "set",
            PropertyAccessorNode.AccessorKind.Init => "init",
            _ => throw new InvalidOperationException($"Unsupported accessor kind: {node.Kind}.")
        };

        // Add visibility if different from property visibility
        var visStr = node.Visibility.HasValue ? $" {GetVisibilityShorthand(node.Visibility.Value)}" : "";

        if (node.IsAutoImplemented)
        {
            // Auto-implemented: just §GET or §GET{{pri}} for restricted visibility
            AppendLine($"{keyword}{visStr}");
        }
        else
        {
            // Full body: §GET ... §/GET
            AppendEffectRowDeclaration(node, $"{keyword}{visStr}");
            Indent();
            EmitEffects(node.Effects);
            _memberBodyDepth++;
            foreach (var stmt in node.Body)
            {
                stmt.Accept(this);
            }
            _memberBodyDepth--;
            Dedent();
        }

        return "";
    }

    public string Visit(ConstructorNode node)
    {
        EmitDocComment(node);
        var visibility = node.IsStatic ? " stat"
            : node.Visibility == Visibility.Public ? "" : " " + GetVisibilityShorthand(node.Visibility);
        var attrs = EmitCSharpAttributes(node.CSharpAttributes);

        // Inline params: §CTOR{vis} (type:name, type:name)
        var inlineFmt = TryFormatInlineParams(node.Parameters);
        if (inlineFmt == null)
            throw new InvalidOperationException("Canonical constructor parameters require an inline signature.");
        AppendEffectRowDeclaration(node, $"ctor{visibility}{attrs} ({inlineFmt})");
        Indent();
        EmitEffects(node.Effects);

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
            // Pre-evaluate args to collect any hoisted bindings BEFORE the §THIS/§BASE block.
            // Use AcceptInInlineSibling so nested zero-arg calls keep their explicit §/C
            // (the §A tokens of base/this are on separate lines and the v0.6.1 parser
            // line-based guard already separates them, but explicit closers eliminate
            // any ambiguity for older parsers / mixed-form sources).
            var evalArgs = new List<string>();
            foreach (var arg in node.Initializer.Arguments)
            {
                var argExpr = AcceptInInlineSibling(arg);
                // Flush any hoisted lines before the block
                FlushHoistedLines();
                evalArgs.Add(argExpr);
            }
            AppendLine($"{(node.Initializer.IsBaseCall ? "base" : "this")}({string.Join(", ", evalArgs)})");
        }

        // Emit body statements
        _memberBodyDepth++;
        foreach (var stmt in node.Body)
        {
            stmt.Accept(this);
        }
        _memberBodyDepth--;

        Dedent();

        return "";
    }

    public string Visit(OperatorOverloadNode node)
    {
        EmitDocComment(node);
        if (node.Visibility != Visibility.Public)
            throw new InvalidOperationException("Native operators require public visibility.");
        var attrs = EmitCSharpAttributes(node.CSharpAttributes);

        var inlineFmt = TryFormatInlineParams(node.Parameters)
            ?? throw new InvalidOperationException("Canonical operator parameters require an inline signature.");
        var output = node.Output
            ?? throw new InvalidOperationException("Canonical operators require a return type.");
        AppendEffectRowDeclaration(node, $"op {node.OperatorToken}{attrs} ({inlineFmt}) → {TypeMapper.ToSurfaceType(output.TypeName)}{FormatEffectRow(output.Row)}");
        Indent();
        EmitEffects(node.Effects);

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
        EmitCallableDocumentation(node, node.Since, node.Deprecated, node.BreakingChanges);
        var visibility = node.Visibility == Visibility.Public ? "" : " " + GetVisibilityShorthand(node.Visibility);
        var modifiers = new List<string>();

        if (node.IsPartial) modifiers.Add("partial");
        if (node.IsVirtual) modifiers.Add("virtual");
        if (node.IsOverride) modifiers.Add("override");
        if (node.IsAbstract) modifiers.Add("abstract");
        if (node.IsSealed) modifiers.Add("sealed");
        if (node.IsStatic && !_emittingModuleMembers) modifiers.Add("static");
        if (node.IsAsync) modifiers.Add("async");
        if (node.IsUnsafe) modifiers.Add("unsafe");
        if (node.IsExtern) modifiers.Add("extern");

        var modStr = modifiers.Count > 0 ? $" {string.Join(" ", modifiers)}" : "";

        var (typeParams, typeParamsAfterGroup) =
            SplitTypeParameterList(node.TypeParameters, node.EffectParameters);

        var output = node.Output != null ? TypeMapper.ToSurfaceType(node.Output.TypeName) : "void";
        var attrs = EmitCSharpAttributes(node.CSharpAttributes);

        // Use §AMT for async methods, §MT for regular methods
        var methodTag = _emittingModuleMembers
            ? (node.IsAsync ? "AF" : "F")
            : (node.IsAsync ? "AMT" : "MT");
        var inlineFmt = TryFormatInlineParams(node.Parameters);
        if (inlineFmt != null)
        {
            var inlineParams = $" ({inlineFmt})";
            var inlineReturn = node.Output != null ? $" → {output}{FormatEffectRow(node.Output.Row)}" : "";
            AppendEffectRowDeclaration(node, $"func {EscapeCalorIdentifier(node.Name)}{attrs}{typeParams}{typeParamsAfterGroup}{visibility}{modStr}{inlineParams}{inlineReturn}");
        }
        else
        {
            throw new InvalidOperationException("Canonical function parameters must be representable in an inline signature.");
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
        // A private/internal body infers its row; ! unknown is the one own row it may carry.
        if (node.IsAbstract || node.IsExtern
            || node.Visibility is not (Visibility.Private or Visibility.Internal))
            EmitEffects(node.Effects, required: true);
        else if (node.Effects is { IsUnknown: true, EffectVariables.Count: 0 })
            EmitEffects(node.Effects, required: true);

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
                AppendLine("return");
            }
            foreach (var stmt in node.Body)
            {
                stmt.Accept(this);
            }
            _memberBodyDepth--;
        }

        Dedent();

        return "";
    }

    public string Visit(LocalFunctionStatementNode node)
        => throw new InvalidOperationException("Local functions must be lowered to §B lambda bindings before Calor emission");

    private void EmitCallableDocumentation(AstNode node, SinceNode? since, DeprecatedNode? deprecated,
        IReadOnlyList<BreakingChangeNode> breakingChanges)
    {
        if (node.DocComment != null)
        {
            foreach (var line in node.DocComment.Split('\n'))
            {
                var text = line.TrimStart();
                if (since != null && text.StartsWith("since ", StringComparison.Ordinal)
                    || deprecated != null && text.StartsWith("deprecated ", StringComparison.Ordinal)
                    || breakingChanges.Count > 0 && text.StartsWith("breaking ", StringComparison.Ordinal))
                    continue;
                EmitDocumentationText(line);
            }
        }
        if (since != null) Visit(since);
        if (deprecated != null) Visit(deprecated);
        foreach (var breaking in breakingChanges) Visit(breaking);
    }

    private void EmitRetiredFunctionMetadata(FunctionNode node)
    {
        foreach (var issue in node.Issues) Visit(issue);
        if (node.Uses != null) Visit(node.Uses);
        if (node.UsedBy != null) Visit(node.UsedBy);
        if (node.Complexity != null) Visit(node.Complexity);
        if (node.Lock != null) Visit(node.Lock);
        if (node.Author != null) Visit(node.Author);
        if (node.TaskRef != null) Visit(node.TaskRef);
    }

    public string Visit(FunctionNode node)
    {
        EmitCallableDocumentation(node, node.Since, node.Deprecated, node.BreakingChanges);
        EmitRetiredFunctionMetadata(node);
        var visibility = GetVisibilityShorthand(node.Visibility);
        var visibilitySegment = node.Visibility == Visibility.Public ? "" : " " + visibility;
        var (typeParams, typeParamsAfterGroup) =
            SplitTypeParameterList(node.TypeParameters, node.EffectParameters);
        var functionName = node.Name;
        var genericStart = functionName.LastIndexOf('<');
        if (node.TypeParameters.Count > 0
            && genericStart > 0
            && functionName.EndsWith('>'))
        {
            functionName = functionName[..genericStart];
        }

        var output = node.Output != null ? TypeMapper.ToSurfaceType(node.Output.TypeName) : "void";

        // Use §AF for async functions, §F for regular functions
        var funcTag = node.IsAsync ? "AF" : "F";
        var inlineFmt = TryFormatInlineParams(node.Parameters);
        if (inlineFmt != null)
        {
            var inlineParams = $" ({inlineFmt})";
            var inlineReturn = node.Output != null ? $" → {output}{FormatEffectRow(node.Output.Row)}" : "";
            AppendEffectRowDeclaration(node, $"func {EscapeCalorIdentifier(functionName)}{typeParams}{typeParamsAfterGroup}{visibilitySegment}{(node.IsAsync ? " async" : "")}{inlineParams}{inlineReturn}");
        }
        else
        {
            throw new InvalidOperationException("Canonical function parameters must be representable in an inline signature.");
        }
        Indent();

        // Emit type parameter constraints
        EmitTypeParameterConstraints(node.TypeParameters);

        if (inlineFmt == null)
        {
            EmitParameterLines(node.Parameters);
            EmitOutputLine(node.Output);
        }

        // Effects (a private/internal body may carry only ! unknown)
        if (node.Visibility is not (Visibility.Private or Visibility.Internal)
            || node.Effects is { IsUnknown: true, EffectVariables.Count: 0 })
            EmitEffects(node.Effects, required: true);

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

        foreach (var example in node.Examples) Visit(example);
        foreach (var assumption in node.Assumptions) Visit(assumption);
        foreach (var property in node.Properties) Visit(property);

        // Body
        _memberBodyDepth++;
        if (node.Body.Count == 0)
        {
            AppendLine("return");
        }
        foreach (var stmt in node.Body)
        {
            stmt.Accept(this);
        }
        _memberBodyDepth--;

        Dedent();

        return "";
    }

    public string Visit(OutputNode node)
        => $"§O{{{TypeMapper.ToSurfaceType(node.TypeName)}}}{FormatEffectRow(node.Row)}";

    public string Visit(EffectsNode node)
    {
        if (node.Effects.Count == 0 && node.EffectVariables.Count == 0)
            return "";

        return FormatEffectsTag(node);
    }

    public string Visit(ParameterNode node)
    {
        var typeName = TypeMapper.ToSurfaceType(node.TypeName);
        var modifiers = new List<string>();
        if (node.Modifier.HasFlag(ParameterModifier.This)) modifiers.Add("this");
        if (node.Modifier.HasFlag(ParameterModifier.Ref)) modifiers.Add("ref");
        if (node.Modifier.HasFlag(ParameterModifier.Out)) modifiers.Add("out");
        if (node.Modifier.HasFlag(ParameterModifier.In)) modifiers.Add("in");
        if (node.Modifier.HasFlag(ParameterModifier.Params)) modifiers.Add("params");
        var modStr = modifiers.Count > 0 ? $":{string.Join(",", modifiers)}" : "";
        var attrs = EmitCSharpAttributes(node.CSharpAttributes);
        var result = $"§I{{{typeName}:{EscapeCalorIdentifier(node.Name)}{modStr}}}{attrs}{FormatEffectRow(node.Row)}";
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
        if (node.Message != null)
            throw new InvalidOperationException("Canonical precondition message syntax has not been selected.");
        AppendLine($"requires {condition}");
        return "";
    }

    public string Visit(EnsuresNode node)
    {
        var condition = node.Condition.Accept(this);
        if (node.Message != null)
            throw new InvalidOperationException("Canonical postcondition message syntax has not been selected.");
        AppendLine($"=> {condition}");
        return "";
    }

    public string Visit(InvariantNode node)
    {
        var condition = node.Condition.Accept(this);
        if (node.Message != null)
            throw new InvalidOperationException("Canonical invariant message syntax has not been selected.");
        AppendLine($"invariant {condition}");
        return "";
    }

    // Statements

    public string Visit(ReturnStatementNode node)
    {
        if (node.Expression != null)
        {
            // Match arms need their own lines under the return expression.
            if (node.Expression is MatchExpressionNode matchExpr)
            {
                EmitMatchExpressionAsStatement(matchExpr);
            }
            else
            {
                var expr = node.Expression.Accept(this);

                // Block-emitting collection initializers (§DICT/§HSET/§LIST
                // blocks) append their binding block and return "" — the naive
                // `§R {expr}` then emits a bare `§R` that re-parses as
                // return-VOID, silently dropping the return value (exposed by
                // the #771 compile gate). Reference the block's binding instead.
                // Inline-emitting collections (e.g. size-only §ARR) return a
                // non-empty string and take the normal path.
                if (string.IsNullOrWhiteSpace(expr))
                {
                    var collectionRef = node.Expression switch
                    {
                        ListCreationNode l => l.Id,
                        DictionaryCreationNode d => d.Id,
                        SetCreationNode s => s.Id,
                        ArrayCreationNode a => a.Id,
                        _ => null
                    };
                    if (collectionRef != null)
                    {
                        expr = collectionRef;
                    }
                }

                if (string.IsNullOrWhiteSpace(expr))
                    throw new InvalidOperationException("A returned expression must emit a native value.");
                AppendLine($"return {expr}");
            }
        }
        else
        {
            AppendLine("return");
        }
        return "";
    }

    /// <summary>
    /// Emits a multiline returned match expression.
    /// Used when the match expression is the direct child of a return statement.
    /// </summary>
    private void EmitMatchExpressionAsStatement(MatchExpressionNode node)
    {
        var target = node.Target.Accept(this);

        AppendLine($"return match {target}");
        Indent();

        foreach (var matchCase in node.Cases)
        {
            Visit(matchCase);
        }

        Dedent();
    }

    /// <summary>
    /// Emits a match expression after the native binding's equals sign.
    /// The binding and match target sit on the same line; case arms are emitted via
    /// AppendLine + Indent so their indentation respects the current binding's
    /// indent (rather than the multi-line hardcoded indent produced by
    /// Visit(MatchExpressionNode), which is only correct in pure expression contexts).
    /// </summary>
    private void EmitMatchExpressionAsBindingInitializer(string bindHeader, MatchExpressionNode node)
    {
        var target = node.Target.Accept(this);

        AppendLine($"{bindHeader} = match {target}");
        Indent();

        foreach (var matchCase in node.Cases)
        {
            Visit(matchCase);
        }

        Dedent();
    }

    /// <summary>
    /// Emits a native block lambda beneath its statement header without lifting it.
    /// </summary>
    private void EmitBlockLambdaWithHeader(string statementHeader, LambdaExpressionNode lambda)
    {
        AppendLine($"{statementHeader} {lambda.Accept(this)}");
    }

    public string Visit(CallStatementNode node)
    {
        AppendLine(new CallExpressionNode(node.Span, node.Target, node.Arguments,
            node.ArgumentNames, node.ArgumentModifiers, node.TypeArguments).Accept(this));
        return "";
    }

    public string Visit(PrintStatementNode node)
    {
        var expr = node.Expression.Accept(this);
        var method = node.IsWriteLine ? "WriteLine" : "Write";
        AppendLine($"System.Console.{method}({expr})");
        return "";
    }

    public string Visit(ContinueStatementNode node)
    {
        AppendLine("continue");
        return "";
    }

    public string Visit(BreakStatementNode node)
    {
        AppendLine("break");
        return "";
    }

    public string Visit(GotoStatementNode node)
    {
        if (node.CaseLabel != null)
        {
            AppendLine($"goto case {node.CaseLabel.Accept(this)}");
        }
        else if (node.IsDefault)
        {
            AppendLine("goto default");
        }
        else
        {
            AppendLine($"goto {EscapeCalorIdentifier(node.Label)}");
        }
        return "";
    }

    public string Visit(LabelStatementNode node)
    {
        AppendLine($"{EscapeCalorIdentifier(node.Label)}:");
        return "";
    }

    public string Visit(YieldReturnStatementNode node)
    {
        if (node.Expression == null)
            throw new InvalidOperationException("Use 'yield value' or 'yield break'; a valueless yield is invalid.");
        var expr = node.Expression.Accept(this);
        AppendLine($"yield {expr}");
        return "";
    }

    public string Visit(YieldBreakStatementNode node)
    {
        AppendLine("yield break");
        return "";
    }

    public string Visit(BindStatementNode node)
    {
        var mappedType = node.TypeName != null ? TypeMapper.ToSurfaceType(node.TypeName) : null;
        var typedName = mappedType is null or "var"
            ? EscapeCalorIdentifier(node.Name)
            : $"{mappedType}:{EscapeCalorIdentifier(node.Name)}";
        var bindHeader = $"{(node.IsMutable ? "var" : "let")} {typedName}";

        // Position 7 (§3.3): the row sits between the header group and the initializer.
        bindHeader += FormatEffectRow(node.Row);

        // Match expression as initializer must use block form so case arms are emitted
        // at the correct indent. The naive `Initializer.Accept(this)` returns a
        // multi-line string with hardcoded 2/4-space indents that get jammed onto
        // the §B line and end up dedented below the enclosing scope.
        if (node.Initializer is MatchExpressionNode matchExpr)
        {
            EmitMatchExpressionAsBindingInitializer(bindHeader, matchExpr);
            return "";
        }

        var initializer = node.Initializer is ReferenceNode { Name: "default", IsVerbatim: false }
            && mappedType is not null and not "var"
                ? mappedType is "string?" or "object?"
                    ? $"(({mappedType})null)"
                    : $"default({mappedType})"
                : node.Initializer?.Accept(this);
        var initPart = initializer is not null ? $" = {initializer}" : "";
        AppendLine($"{bindHeader}{initPart}");
        return "";
    }

    /// <summary>
    /// #1132: renders elements (or arguments) left to right, object initializers on one line.
    /// If any must leave its line, every non-literal one is bound in source order, so none
    /// runs ahead of an earlier sibling; the bindings stay queued for the caller.
    /// </summary>
    private List<string> RenderArrayElements(
        IEnumerable<ExpressionNode> elements, bool hoistSectionMarkers, bool inlineSibling = true,
        Func<string, bool>? mustLeaveLine = null)
    {
        var queued = _pendingHoistedLines.ToList();
        _pendingHoistedLines.Clear();
        var rendered = new List<(ExpressionNode Node, string Value, List<string> Hoisted)>();
        _inlineObjectInitializers++;
        try
        {
            foreach (var element in elements)
            {
                var value = inlineSibling ? AcceptInInlineSibling(element) : element.Accept(this);
                rendered.Add((element, value, _pendingHoistedLines.ToList()));
                _pendingHoistedLines.Clear();
            }
        }
        finally
        {
            _inlineObjectInitializers--;
            _pendingHoistedLines.InsertRange(0, queued);
        }

        var hoistAll = rendered.Any(r => r.Hoisted.Count > 0 || r.Value.Contains('\n')
            || hoistSectionMarkers && ContainsSectionMarker(r.Value)
            || mustLeaveLine?.Invoke(r.Value) == true);
        var values = new List<string>(rendered.Count);
        foreach (var (node, value, hoisted) in rendered)
        {
            _pendingHoistedLines.AddRange(hoisted);
            var literal = node is IntLiteralNode or StringLiteralNode or BoolLiteralNode
                or FloatLiteralNode or DecimalLiteralNode
                // `null`/`default` take their type from the target; a temp would lose it.
                or ReferenceNode { IsNullLiteral: true }
                or ReferenceNode { Name: "default", IsVerbatim: false };
            values.Add(hoistAll && !literal ? HoistToTempVar(value) : value);
        }
        return values;
    }

    /// <summary>#1132: array elements, never hoisted (a hoist ran an element or its conversion early).</summary>
    private List<string> RenderInPlace(IEnumerable<ExpressionNode> elements)
    {
        _conditionalExpressionDepth++;
        try
        {
            return RenderArrayElements(elements, hoistSectionMarkers: false);
        }
        finally
        {
            _conditionalExpressionDepth--;
        }
    }

    /// <summary>
    /// #1132: whether <paramref name="element"/> is written in place as an array element:
    /// nothing emitted or bound ahead of the array (a collection block, whose element was
    /// lost; a block lambda bound to an untyped temp, which loses its delegate type) and,
    /// unless <paramref name="allowMultiline"/>, one line (a §ROW holds no block).
    /// </summary>
    internal static bool WritesInPlace(ExpressionNode element)
    {
        var probe = new CalorEmitter { _memberBodyDepth = 1 };
        var value = probe.RenderInPlace([element])[0];
        return probe._builder.Length == 0 && probe._pendingHoistedLines.Count == 0
            && !value.Contains('\n');
    }

    internal static bool HoistsInExpression(ExpressionNode expression)
    {
        var probe = new CalorEmitter { _memberBodyDepth = 1 };
        probe.AcceptInInlineSibling(expression);
        return probe._builder.Length != 0 || probe._pendingHoistedLines.Count != 0;
    }

    public string Visit(AssignmentStatementNode node)
    {
        var target = AcceptInInlineSibling(node.Target);

        if (node.Value is LambdaExpressionNode { IsExpressionLambda: false, StatementBody.Count: > 0 } lambda)
        {
            EmitBlockLambdaWithHeader($"set {target}", lambda);
            return "";
        }

        var value = node.Value.Accept(this);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("Assignment value must emit a native expression.");
        AppendLine($"set {target} {value}");
        return "";
    }

    /// <summary>
    /// A compound store that reached the emitter unlowered (the converter lowers it itself when
    /// it has a semantic model) is written as the native plain store <c>set t (t op v)</c>. The
    /// target text is repeated, as the retired <c>§ASSIGN</c> form also did.
    /// </summary>
    public string Visit(CompoundAssignmentStatementNode node)
    {
        if (node.Operator == CompoundAssignmentOperator.NullCoalesce)
            throw new NotSupportedException(
                "Null-coalescing stores require declaration-proven lowering before Calor emission.");

        return Visit(new AssignmentStatementNode(node.Span, node.Target,
            new BinaryOperationNode(node.Span, RoslynSyntaxVisitor.ToBinaryOperator(node.Operator),
                node.Target, node.Value)));
    }

    public string Visit(UsingStatementNode node)
    {
        var resource = node.Resource.Accept(this);
        var binder = node.VariableType == null || node.VariableType == "var" ? node.VariableName
            : $"{TypeMapper.ToSurfaceType(node.VariableType)}:{node.VariableName}";
        AppendLine(node.VariableName == null ? $"using {resource}" : $"using {binder} = {resource}");
        Indent();

        foreach (var stmt in node.Body)
        {
            stmt.Accept(this);
        }

        Dedent();
        return "";
    }

    public string Visit(IfStatementNode node)
    {
        if (_canonicalLiteralReturns.TryGetValue(node, out var canonicalReturn))
            return Visit(canonicalReturn);

        var condition = node.Condition.Accept(this);
        EmitIfChain(condition, node.ThenBody, node.ElseIfClauses, 0, node.ElseBody);
        return "";
    }

    /// <summary>
    /// Emits <c>§IF cond</c> / body / the else-if clauses from
    /// <paramref name="firstElseIf"/> on / <c>§EL</c>. An else-if condition that
    /// hoists a temp (an indexer read, a call result) used to flush that
    /// <c>§B{~_hoistNNN}</c> line between the previous body and its <c>§EI</c>,
    /// closing the if-chain before the clause was read (Calor0117, #903 cluster
    /// 3). Such a clause is re-nested instead: <c>§EL</c>, the hoisted binding,
    /// and a fresh <c>§IF</c> carrying the rest of the chain
    /// — the C# meaning of
    /// <c>else if</c> is exactly that nesting, so the emitted program is unchanged.
    /// </summary>
    private void EmitIfChain(
        string condition,
        IReadOnlyList<StatementNode> thenBody,
        IReadOnlyList<ElseIfClauseNode> elseIfs,
        int firstElseIf,
        IReadOnlyList<StatementNode>? elseBody)
    {
        AppendLine($"if {condition}");
        Indent();
        foreach (var stmt in thenBody)
            stmt.Accept(this);
        Dedent();

        for (var i = firstElseIf; i < elseIfs.Count; i++)
        {
            var elseIf = elseIfs[i];
            var pendingBefore = _pendingHoistedLines.Count;
            var elseIfCondition = elseIf.Condition.Accept(this);
            if (_pendingHoistedLines.Count > pendingBefore)
            {
                // Park the condition's hoists so §EL is written first, then let
                // them flush INSIDE the else body ahead of the nested §IF.
                var conditionHoists = _pendingHoistedLines.Skip(pendingBefore).ToList();
                _pendingHoistedLines.RemoveRange(pendingBefore, conditionHoists.Count);
                AppendLine("else");
                Indent();
                _pendingHoistedLines.AddRange(conditionHoists);
                EmitIfChain(elseIfCondition, elseIf.Body, elseIfs, i + 1, elseBody);
                Dedent();
                return;
            }

            AppendLine($"else if {elseIfCondition}");
            Indent();
            foreach (var stmt in elseIf.Body)
                stmt.Accept(this);
            Dedent();
        }

        if (elseBody != null)
        {
            AppendLine("else");
            Indent();
            foreach (var stmt in elseBody)
                stmt.Accept(this);
            Dedent();
        }
    }

    public string Visit(ElseIfClauseNode node)
    {
        AppendLine($"else if {node.Condition.Accept(this)}");
        Indent();
        foreach (var stmt in node.Body)
        {
            stmt.Accept(this);
        }
        Dedent();
        return "";
    }

    public string Visit(ForStatementNode node)
    {
        var from = node.From.Accept(this);
        var variableName = EscapeCalorIdentifier(node.VariableName);
        var binder = node.VariableType == "int" ? variableName
            : $"{TypeMapper.ToSurfaceType(node.VariableType)}:{variableName}";
        if (node.Condition != null && node.Update != null)
        {
            var update = node.Update switch
            {
                AssignmentStatementNode assignment =>
                    $"{assignment.Target.Accept(this)} = {assignment.Value.Accept(this)}",
                ExpressionStatementNode expression => expression.Expression.Accept(this),
                _ => throw new InvalidOperationException("For update must be an assignment or invocation.")
            };
            AppendLine($"for {binder} = {from}; {node.Condition.Accept(this)}; " +
                update);
        }
        else
            AppendLine($"for {binder} from {from} to {node.To.Accept(this)} step {node.Step?.Accept(this) ?? "1"}");
        Indent();

        foreach (var stmt in node.Body)
        {
            stmt.Accept(this);
        }

        Dedent();
        return "";
    }

    public string Visit(WhileStatementNode node)
    {
        var condition = node.Condition.Accept(this);

        AppendLine($"while {condition}");
        Indent();

        foreach (var stmt in node.Body)
        {
            stmt?.Accept(this);
        }

        Dedent();
        return "";
    }

    public string Visit(DoWhileStatementNode node)
    {
        var condition = node.Condition.Accept(this);

        AppendLine("do");
        Indent();

        foreach (var stmt in node.Body)
        {
            stmt.Accept(this);
        }

        Dedent();
        AppendLine($"while {condition}");
        return "";
    }

    public string Visit(ForeachStatementNode node)
    {
        var collection = node.Collection.Accept(this);
        var varType = TypeMapper.ToSurfaceType(node.VariableType);

        if (node.IndexVariableName != null)
            throw new InvalidOperationException("Indexed foreach is removed; use an explicit counter with each.");
        var variableName = EscapeCalorIdentifier(node.VariableName);
        var binder = varType == "var" ? variableName : $"{varType}:{variableName}";
        AppendLine($"each {binder} in {collection}");
        Indent();

        foreach (var stmt in node.Body)
        {
            stmt.Accept(this);
        }

        Dedent();
        return "";
    }

    // Phase 6 Extended: Collections (List, Dictionary, HashSet)

    public string Visit(ListCreationNode node)
    {
        return Visit(new NewExpressionNode(node.Span, "System.Collections.Generic.List",
            [node.ElementType], [], [],
            collectionInitializers: node.Elements.Select(element =>
                new CallExpressionNode(element.Span, "Add", [element])).ToArray()));
    }

    public string Visit(DictionaryCreationNode node)
    {
        return Visit(new NewExpressionNode(node.Span, "System.Collections.Generic.Dictionary",
            [node.KeyType, node.ValueType], [], [],
            collectionInitializers: node.Entries.Select(entry =>
                new CallExpressionNode(entry.Span, "Add", [entry.Key, entry.Value])).ToArray()));
    }

    public string Visit(KeyValuePairNode node)
    {
        // AcceptInInlineSibling: nested zero-arg calls in key/value must keep §/C
        // so the parser doesn't absorb the value as the call's inline argument.
        var key = AcceptInInlineSibling(node.Key);
        var value = AcceptInInlineSibling(node.Value);
        AppendLine($"§KV {key} {value}");
        return "";
    }

    public string Visit(SetCreationNode node)
    {
        return Visit(new NewExpressionNode(node.Span, "System.Collections.Generic.HashSet",
            [node.ElementType], [], [],
            collectionInitializers: node.Elements.Select(element =>
                new CallExpressionNode(element.Span, "Add", [element])).ToArray()));
    }

    public string Visit(CollectionPushNode node)
        => Visit(new ExpressionStatementNode(node.Span,
            new CallExpressionNode(node.Span, $"{EscapeCalorIdentifier(node.CollectionName)}.Add", [node.Value])));

    public string Visit(DictionaryPutNode node)
        => Visit(new AssignmentStatementNode(node.Span,
            new ArrayAccessNode(node.Span, new ReferenceNode(node.Span, node.DictionaryName), node.Key),
            node.Value));

    public string Visit(CollectionRemoveNode node)
        => Visit(new ExpressionStatementNode(node.Span,
            new CallExpressionNode(node.Span, $"{EscapeCalorIdentifier(node.CollectionName)}.Remove", [node.KeyOrValue])));

    public string Visit(CollectionSetIndexNode node)
        => Visit(new AssignmentStatementNode(node.Span,
            new ArrayAccessNode(node.Span, new ReferenceNode(node.Span, node.CollectionName), node.Index),
            node.Value));

    public string Visit(CollectionClearNode node)
        => Visit(new ExpressionStatementNode(node.Span,
            new CallExpressionNode(node.Span, $"{EscapeCalorIdentifier(node.CollectionName)}.Clear", [])));

    public string Visit(CollectionInsertNode node)
        => Visit(new ExpressionStatementNode(node.Span,
            new CallExpressionNode(node.Span, $"{EscapeCalorIdentifier(node.CollectionName)}.Insert", [node.Index, node.Value])));

    public string Visit(CollectionContainsNode node)
    {
        var method = node.Mode switch
        {
            ContainsMode.Key => "ContainsKey",
            ContainsMode.DictValue => "ContainsValue",
            _ => "Contains"
        };
        return Visit(new CallExpressionNode(node.Span,
            $"{EscapeCalorIdentifier(node.CollectionName)}.{method}", [node.KeyOrValue]));
    }

    public string Visit(DictionaryForeachNode node)
    {
        var dictionary = node.Dictionary.Accept(this);

        AppendLine($"each {EscapeCalorIdentifier(node.KeyName)}, {EscapeCalorIdentifier(node.ValueName)} in {dictionary}");
        Indent();

        foreach (var stmt in node.Body)
        {
            stmt.Accept(this);
        }

        Dedent();
        return "";
    }

    public string Visit(CollectionCountNode node)
        => Visit(new FieldAccessNode(node.Span, node.Collection, "Count"));

    public string Visit(TryStatementNode node)
    {
        AppendLine("try");
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
            AppendLine("finally");
            Indent();

            foreach (var stmt in node.FinallyBody)
            {
                stmt.Accept(this);
            }

            Dedent();
        }

        return "";
    }

    public string Visit(CatchClauseNode node)
    {
        var exType = node.ExceptionType;
        var varPart = !string.IsNullOrEmpty(node.VariableName)
            ? $":{EscapeCalorIdentifier(node.VariableName)}" : "";
        var filterPart = node.Filter != null ? $" when {node.Filter.Accept(this)}" : "";

        AppendLine($"catch{(exType == null ? "" : $" {exType}{varPart}")}{filterPart}");
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
            AppendLine($"throw {expr}");
        }
        else
        {
            AppendLine("throw");
        }
        return "";
    }

    public string Visit(ThrowExpressionNode node)
    {
        var expr = node.Exception.Accept(this);
        return $"throw {expr}";
    }

    public string Visit(RethrowStatementNode node)
    {
        AppendLine("throw");
        return "";
    }

    public string Visit(MatchStatementNode node)
    {
        var target = node.Target.Accept(this);

        AppendLine($"match {target}");
        Indent();

        foreach (var matchCase in node.Cases)
        {
            EmitMatchCase(matchCase, expressionContext: false);
        }

        Dedent();
        return "";
    }

    public string Visit(MatchCaseNode node) => EmitMatchCase(node, expressionContext: true);

    private string EmitMatchCase(MatchCaseNode node, bool expressionContext)
    {
        var pattern = EmitPattern(node.Pattern);
        var guard = node.Guard != null ? $" when {node.Guard.Accept(this)}" : "";

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
            AppendLine($"case {pattern}{guard}");
            Indent();
            foreach (var h in guardHoisted)
                AppendLine(h);
            foreach (var stmt in node.Body)
                stmt.Accept(this);
            Dedent();
            return "";
        }

        // Check if this is a single-return case suitable for arrow syntax.
        // Block-level collection nodes (List, Dict, Set) emit via AppendLine and return "",
        // so they must use block syntax to avoid empty arrow expressions.
        if (expressionContext && node.Body.Count == 1 && node.Body[0] is ReturnStatementNode returnStmt && returnStmt.Expression != null
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
                AppendLine($"case {pattern}{guard} → {expr}");
            }
            else
            {
                // Hoisted bindings exist — must use block syntax so they appear inside the arm
                var hoisted = new List<string>(_pendingHoistedLines);
                _pendingHoistedLines.Clear();
                AppendLine($"case {pattern}{guard}");
                Indent();
                foreach (var line in hoisted)
                {
                    AppendLine(line);
                }
                AppendLine($"return {expr}");
                Dedent();
            }
        }
        else
        {
            // Statement arms end at dedent; expression payloads may still need a closer.
            AppendLine($"case {pattern}{guard}");
            Indent();

            foreach (var stmt in node.Body)
            {
                stmt.Accept(this);
            }

            Dedent();
        }

        return "";
    }

    // Expressions - return strings

    public string Visit(IntLiteralNode node)
    {
        var sign = node.Sign == IntegerLiteralSign.Negative ? "-" : "";
        var digits = node.IsHex
            ? $"0x{node.Magnitude:X}"
            : node.Magnitude.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var value = sign + digits;

        if (node.IsUnsigned)
            return node.IsLong
                ? $"{value}UL"
                : $"{value}U";
        if (node.IsLong)
            return $"{value}L";
        return value;
    }

    public string Visit(FloatLiteralNode node)
    {
        if (!double.IsFinite(node.Value) || (node.IsSingle && !float.IsFinite((float)node.Value)))
            throw new InvalidOperationException("A non-finite value cannot be emitted as a bare numeric literal.");
        if (node.IsSingle)
            return $"{((float)node.Value).ToString(System.Globalization.CultureInfo.InvariantCulture)}f";
        if (node.IsDecimal)
            return $"{node.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}m";
        // #1528: the lexer reads a bare literal as a double only when it has a
        // fraction or an exponent. A whole-number double such as 2.0 prints as "2",
        // which would re-parse as an int and turn x / 2.0 into integer division.
        var text = node.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return double.IsFinite(node.Value) && text.IndexOfAny(['.', 'E', 'e']) < 0
            ? text + ".0"
            : text;
    }

    public string Visit(DecimalLiteralNode node)
    {
        var text = node.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var negativeZero = node.Value == 0 && (decimal.GetBits(node.Value)[3] & int.MinValue) != 0;
        return (negativeZero ? "-" : "") + text + "m";
    }

    public string Visit(StringLiteralNode node)
    {
        // Multiline strings round-trip as triple-quote
        if (node.IsMultiline && node.Value.Contains('\n'))
        {
            var multilineSuffix = node.IsUtf8 ? "u8" : "";
            return $"\"\"\"\n{EscapeStringText(node.Value, multiline: true)}\"\"\"{multilineSuffix}";
        }

        var escaped = EscapeStringText(node.Value, multiline: false);
        var suffix = node.IsUtf8 ? "u8" : "";
        return $"\"{escaped}\"{suffix}";
    }

    private static string EscapeStringText(string text, bool multiline)
    {
        var escaped = new StringBuilder();
        foreach (var character in text)
        {
            escaped.Append(character switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\n' when multiline => "\n",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                '\0' => "\\0",
                '\a' => "\\a",
                '\b' => "\\b",
                '\f' => "\\f",
                '\v' => "\\v",
                _ when char.IsControl(character) || char.IsSurrogate(character) =>
                    "\\u" + ((int)character).ToString("X4", System.Globalization.CultureInfo.InvariantCulture),
                _ => character.ToString()
            });
        }
        return escaped.ToString().Replace("${", "\\${", StringComparison.Ordinal);
    }

    public string Visit(BoolLiteralNode node)
    {
        return node.Value ? "true" : "false";
    }

    public string Visit(ConditionalExpressionNode node)
    {
        var condition = AcceptInConditionalRegion(node.Condition);
        var whenTrue = AcceptInConditionalRegion(node.WhenTrue);
        var whenFalse = AcceptInConditionalRegion(node.WhenFalse);

        return $"({condition} ? {whenTrue} : {whenFalse})";
    }

    private static bool ContainsSectionMarker(string expr)
    {
        return expr.Contains('§');
    }

    private string EmitSizeAttribute(ExpressionNode expression)
    {
        // #1132: an expression-position size is never hoisted (that ran it before the
        // earlier parts of its statement); a complex one is quoted and re-parsed in place.
        var size = AcceptInConditionalRegion(expression);
        if (!ContainsSectionMarker(size) && !size.Contains('(') && !size.Contains(',')
            && !size.Contains(':') && !size.Contains("0x"))
            return size;

        return new StringLiteralNode(expression.Span, size).Accept(this);
    }

    private string AcceptInConditionalRegion(ExpressionNode expression)
    {
        _conditionalExpressionDepth++;
        try
        {
            return AcceptInInlineSibling(expression);
        }
        finally
        {
            _conditionalExpressionDepth--;
        }
    }

    private string AcceptArgument(ExpressionNode expression)
    {
        var rendered = AcceptInConditionalRegion(expression);
        // Dedent before the next argument's lambda head, without hoisting its closure.
        return expression is LambdaExpressionNode { ExpressionBody: null }
            ? $"({rendered}\n)" : rendered;
    }

    /// <summary>
    /// Hoists an expression containing section markers to a temp variable.
    /// Adds a §B{~tempVar} expr line to _pendingHoistedLines and returns the temp var name.
    /// This prevents § markers from appearing inside Lisp (op ...) expressions.
    /// </summary>
    /// <summary>
    /// #903 cluster 1 (second shape): a fluent chain the converter could not
    /// decompose keeps the C# line break before its <c>.Member</c>, so the target
    /// reached <c>§C{receiver\n    .Member}</c> and the lexer split the tag across
    /// lines. Line breaks (and the indentation around them) carry no meaning
    /// inside a member path, so they are removed rather than replaced by a space.
    /// </summary>
    private static string NormalizeCallTarget(string target)
    {
        if (target.IndexOfAny(['\n', '\r']) < 0)
            return target;

        // Review m10: the comment stripping below is not string-literal aware, so
        // `"a//b"` inside a target would lose half the literal. No converter path
        // produces a multi-line target containing a literal today; rather than
        // rely on that, refuse outright and let the fallback preserve the member.
        if (target.Contains('"'))
            return target;

        // Review M1: a chain can carry COMMENTS across the break
        // (`xs\n  // sort in place\n  .Sort()`). Joining blindly would bake the
        // comment text into the member path - a corrupted identifier, and for a
        // `//` comment everything after it is swallowed. Drop comments first; if
        // anything comment-like survives (an unterminated block comment, a `//`
        // inside a string literal in the target) refuse to normalise and hand the
        // original back, so the raw-frame fallbacks preserve the member
        // instead of the emitter shipping a corrupt one.
        var stripped = System.Text.RegularExpressions.Regex.Replace(
            target, @"/\*.*?\*/", " ", System.Text.RegularExpressions.RegexOptions.Singleline);
        stripped = System.Text.RegularExpressions.Regex.Replace(stripped, @"//[^\n\r]*", " ");
        if (stripped.Contains("/*", StringComparison.Ordinal)
            || stripped.Contains("*/", StringComparison.Ordinal)
            || stripped.Contains("//", StringComparison.Ordinal))
        {
            return target;
        }

        var joined = System.Text.RegularExpressions.Regex.Replace(
            stripped, @"[ \t]*\r?\n[ \t]*", "");
        // Where a comment stood between receiver and member the join leaves the
        // space that replaced it (`xs .Sort`). A member path has no spaces: close
        // them up around the '.', and refuse if any whitespace still remains.
        joined = System.Text.RegularExpressions.Regex.Replace(joined, @"\s*\.\s*", ".").Trim();
        return joined.Any(char.IsWhiteSpace) ? target : joined;
    }

    private static bool IsElementFieldPath(ExpressionNode node) =>
        node is FieldAccessNode field
        && (field.Target is ArrayAccessNode or MultiDimArrayAccessNode || IsElementFieldPath(field.Target));

    private string HoistToTempVar(string expr)
    {
        // Don't hoist outside of executable bodies (methods, ctors, operators, property accessors).
        // Hoisting at class/module scope would leak bindings between members.
        if (_memberBodyDepth == 0 || _conditionalExpressionDepth > 0)
            return expr;

        var varName = FreshTemp("_hoist");
        _pendingHoistedLines.Add($"var {varName} = {expr}");
        return varName;
    }

    /// <summary>#1132: a generated temp name not spelled in the source (a user `_hoist000` was rebound).</summary>
    private string FreshTemp(string prefix)
    {
        string name;
        do
        {
            name = $"{prefix}{_hoistCounter++:D3}";
        }
        while (_context.IsReservedName(name));
        return name;
    }

    public string Visit(ReferenceNode node)
    {
        var name = node.Name.Replace("@", "");
        if (node.IsNullLiteral)
            return name;
        // The default literal: native source spells it `default`, and escaping it would turn
        // it into a reference to a member named `@default`.
        if (node.Name == "default" && !node.IsVerbatim)
            return "default";
        if (node.IsVerbatim)
            return $"@{name}";
        if (TypeMapper.IsPrimitiveType(name) && TypeMapper.ToSurfaceType(name) == name)
            return name;
        return EscapeCalorIdentifier(name.Contains('<') ? AttributeHelper.ToSurfaceSpelling(name) : name);
    }

    public string Visit(BinaryOperationNode node)
    {
        // Both lazy and eager operators need intact operand regions. Hoisting a
        // nested right operand can run it before the left call or variable read.
        // Explicit inner call closers keep section-bearing Lisp operands unambiguous.
        var left = AcceptInConditionalRegion(node.Left);
        var right = AcceptInConditionalRegion(node.Right);
        var opSymbol = GetCalorOperatorSymbol(node.Operator);

        return $"({left} {opSymbol} {right})";
    }

    public string Visit(UnaryOperationNode node)
    {
        if (node.Operator is UnaryOperator.PreIncrement or UnaryOperator.PreDecrement
            or UnaryOperator.PostIncrement or UnaryOperator.PostDecrement)
            throw new InvalidOperationException("Increment/decrement expressions must be lowered to §ASSIGN before Calor emission");
        var operand = node.Operand.Accept(this);
        var opSymbol = node.Operator switch
        {
            UnaryOperator.Negate => "-",
            UnaryOperator.Not => "!",
            UnaryOperator.BitwiseNot => "~",
            _ => throw new ArgumentOutOfRangeException(nameof(node.Operator), node.Operator, null)
        };

        return $"({opSymbol}{operand})";
    }

    public string Visit(FieldAccessNode node)
    {
        // #1132: `(§IDX2D g 0 1).F`; bare, `.F` bound to the last index (CS1061).
        if (!_inInterpolation && node.Target is ArrayAccessNode or MultiDimArrayAccessNode)
        {
            var element = AcceptInConditionalRegion(node.Target);
            return $"({element}).{EscapeCalorIdentifier(node.FieldName)}";
        }
        // `new T[,] { … }.Length` in place: the inline creation ends at its closer.
        if (!_inInterpolation && node.Target is ArrayCreationNode { Initializer.Count: > 0 }
                or MultiDimArrayCreationNode { Initializer.Count: > 0 })
        {
            var creation = AcceptInConditionalRegion(node.Target);
            return $"{creation}.{EscapeCalorIdentifier(node.FieldName)}";
        }

        var conditional = IsConditionalCallTarget(node.Target);
        var target = conditional ? AcceptInConditionalRegion(node.Target) : node.Target.Accept(this);
        if (node.Target is ThisExpressionNode)
            target = "this";
        else if (node.Target is BaseExpressionNode)
            target = "base";
        // Hoist call results that contain section markers (e.g., §C{method} §/C.Property)
        // Only hoist when inside an executable body (method, ctor, etc.)
        // #943 (D1 RO-COPY-1): never a field path rooted in an element (`a[0].X.Y`); a temp
        // would copy the value-type step and lose by-reference and store identity.
        else if (!conditional && ContainsSectionMarker(target) && _memberBodyDepth > 0 && !IsElementFieldPath(node.Target))
            target = HoistToTempVar(target);
        var fieldName = EscapeCalorIdentifier(node.FieldName);
        // 0.30 member access is C# punctuation, written unspaced. After a receiver that ends a
        // token on its own (`f().X`, `a[0].X`, `this.X`, `"s".Length`) the `.` reparses as the
        // same member access. After an identifier the lexer folds `x.F` into one dotted name,
        // which the parser reads as this member access when its root is a value in scope or a
        // source class of the module with that member (Parser.FindMemberAccessRoots), and as a
        // qualified name otherwise (`Math.PI`). A longer chain on any other root would read
        // back as one dotted name (`Console.Out.NewLine` names neither a type nor a value), so
        // its receiver is parenthesized: `(Console.Out).NewLine`. A numeric literal receiver
        // is parenthesized so `1.F` cannot lex as a number.
        if (EndsInIdentifier(target) && IsUnrootedMemberChain(node.Target))
            target = $"({target})";
        else if (node.Target is IntLiteralNode or FloatLiteralNode or DecimalLiteralNode)
            target = $"({target})";
        return $"{target}.{fieldName}";
    }

    /// <summary>
    /// True when <paramref name="receiver"/> prints as a dotted identifier path whose root the
    /// parser does not read as a member-access root (see <see cref="Parser.FindMemberAccessRoots"/>).
    /// </summary>
    private bool IsUnrootedMemberChain(ExpressionNode receiver)
    {
        switch (receiver)
        {
            case ReferenceNode reference:
                return reference.Name.Contains('.');
            case FieldAccessNode member:
                var root = member.Target;
                while (root is FieldAccessNode inner)
                    root = inner.Target;
                return root is ReferenceNode rootReference
                    && (rootReference.Name.Contains('.') || _memberAccessRoots?.Contains(rootReference) != true);
            default:
                return false;
        }
    }

    private static bool EndsInIdentifier(string target)
        => target.Length > 0 && (char.IsLetterOrDigit(target[^1]) || target[^1] == '_')
            && target is not ("this" or "base");

    public string Visit(NewExpressionNode node)
    {
        var types = node.TypeArguments.Count == 0 ? ""
            : $"<{string.Join(", ", node.TypeArguments.Select(TypeMapper.ToSurfaceType))}>";
        var arguments = node.Arguments.Select((argument, index) =>
            (node.ArgumentNames?.ElementAtOrDefault(index) is { } name
                ? $"{EscapeCalorIdentifier(name)}: " : "")
            + AcceptArgument(argument));
        var initializers = node.Initializers.Count == 0 ? ""
            : " { " + string.Join(", ", node.Initializers.Select(initializer =>
                $"{EscapeCalorIdentifier(initializer.PropertyName)} = {AcceptInConditionalRegion(initializer.Value)}")) + " }";
        if (node.CollectionInitializers.Count > 0)
            initializers = " { " + string.Join(", ", node.CollectionInitializers.Select(entry =>
                entry.Arguments.Count == 1 ? AcceptInConditionalRegion(entry.Arguments[0])
                : "{ " + string.Join(", ", entry.Arguments.Select(AcceptInConditionalRegion)) + " }")) + " }";
        return $"new {TypeMapper.ToSurfaceType(node.TypeName)}{types}({string.Join(", ", arguments)}){initializers}";
    }

    public string Visit(AnonymousObjectCreationNode node)
    {
        var values = RenderArrayElements(node.Initializers.Select(init => init.Value), hoistSectionMarkers: true);
        return $"new {{ {string.Join(", ", node.Initializers.Zip(values,
            (init, value) => $"{EscapeCalorIdentifier(init.PropertyName)} = {value}"))} }}";
    }

    public string Visit(CallExpressionNode node)
    {
        var types = node.TypeArguments is { Count: > 0 }
            ? $"<{string.Join(", ", node.TypeArguments.Select(TypeMapper.ToSurfaceType))}>"
            : "";
        var target = NormalizeCallTarget(node.Target);
        if (target.IndexOfAny(['(', ')', '[', ']']) >= 0)
            throw new InvalidOperationException("A computed call requires an expression-valued callee.");
        if (!target.Contains('.'))
            target = EscapeCalorIdentifier(target);
        else
        {
            var separator = target.LastIndexOf('.');
            target = target[..(separator + 1)] + EscapeCalorIdentifier(target[(separator + 1)..]);
        }
        var arguments = node.Arguments.Select((argument, index) =>
        {
            var name = node.ArgumentNames?.ElementAtOrDefault(index);
            var modifier = node.ArgumentModifiers?.ElementAtOrDefault(index);
            return (name is null ? "" : $"{EscapeCalorIdentifier(name)}: ")
                + (modifier is null ? "" : $"{modifier} ")
                + AcceptArgument(argument);
        });
        return $"{target}{types}({string.Join(", ", arguments)})";
    }

    public string Visit(ThisExpressionNode node)
    {
        return "this";
    }

    public string Visit(BaseExpressionNode node)
    {
        return "base";
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

    public string Visit(TupleLiteralNode node)
    {
        // Hoist elements that start with '(' to avoid tuple/lisp ambiguity
        // e.g., ((> a b), (== c d)) would be misparse as nested lisp, and elements with section
        // markers. #1541 review: when one is hoisted, every non-literal element is, in order, so
        // `(n, new List<int> { Next() })` still reads n before Next() runs.
        var evalElements = RenderArrayElements(node.Elements, hoistSectionMarkers: true,
            mustLeaveLine: value => value.StartsWith("(", StringComparison.Ordinal));
        for (var i = 0; i < evalElements.Count; i++)
        {
            if (evalElements[i].StartsWith("(", StringComparison.Ordinal))
                evalElements[i] = HoistToTempVar(evalElements[i]);
        }
        return $"({string.Join(", ", evalElements)})";
    }

    public string Visit(MatchExpressionNode node)
    {
        var savedBuilder = _builder;
        var savedIndent = _indentLevel;
        var savedPending = _pendingHoistedLines.ToList();
        _builder = new StringBuilder();
        _indentLevel = 0;
        _pendingHoistedLines.Clear();
        try
        {
            AppendLine($"match {node.Target.Accept(this)}");
            Indent();
            foreach (var matchCase in node.Cases)
                EmitMatchCase(matchCase, expressionContext: true);
            Dedent();
            return RawCSharpFrame.RewriteOutsideFrames(_builder.ToString(),
                text => text.TrimEnd('\r', '\n'));
        }
        finally
        {
            _builder = savedBuilder;
            _indentLevel = savedIndent;
            _pendingHoistedLines.Clear();
            _pendingHoistedLines.AddRange(savedPending);
        }
    }

    public string Visit(ArrayCreationNode node)
    {
        var (elementType, elementRanks) = CodeGen.CSharpEmitter.SplitArrayRanks(
            TypeMapper.ToSurfaceType(node.ElementType));
        var size = node.Size is null ? "" : AcceptInConditionalRegion(node.Size);
        var initializer = node.Size is null || node.Initializer.Count > 0
            ? $" {{ {string.Join(", ", RenderInPlace(node.Initializer))} }}" : "";
        return $"new {elementType}[{size}]{elementRanks}{initializer}";
    }

    public string Visit(ArrayAccessNode node)
    {
        // AcceptInInlineSibling: nested zero-arg calls in array or index expressions
        // must keep §/C or the parser would absorb the following token (the index,
        // or for the index, the next outer §A) as the call's inline argument.
        var array = AcceptInInlineSibling(node.Array);
        var index = AcceptInInlineSibling(node.Index);
        return $"{EmitPostfixOperand(node.Array)}[{index}]";
    }

    public string Visit(ArrayLengthNode node)
    {
        var array = node.Array.Accept(this);
        return $"{array}.Length";
    }

    public string Visit(LambdaExpressionNode node)
    {
        var parameters = string.Join(", ", node.Parameters.Select(parameter =>
            (parameter.TypeName is null ? "" : $"{TypeMapper.ToSurfaceType(parameter.TypeName)}:")
            + EscapeCalorIdentifier(parameter.Name)));
        var head = (node.IsStatic ? "static " : "") + (node.IsAsync ? "async " : "")
            + $"({parameters}){FormatEffectRow(node.Effects)} =>";
        if (node.ExpressionBody is not null)
            return head + " " + AcceptInConditionalRegion(node.ExpressionBody);
        var statements = CaptureLambdaStatements(node, inlineSibling: false);
        return head + "\n" + string.Join("\n", statements.Select(statement =>
            RawCSharpFrame.RewriteOutsideFrames(statement,
                text => "  " + text.Replace("\n", "\n  ", StringComparison.Ordinal))));
    }

    public string Visit(LambdaParameterNode node)
    {
        if (node.TypeName != null)
        {
            return $"{TypeMapper.ToSurfaceType(node.TypeName)}:{node.Name}";
        }
        return node.Name;
    }

    public string Visit(AwaitExpressionNode node)
    {
        var awaited = node.ConfigureAwait is { } configure
            ? $"{EmitPostfixOperand(node.Awaited)}.ConfigureAwait({(configure ? "true" : "false")})"
            : AcceptInConditionalRegion(node.Awaited);
        return $"await {awaited}";
    }

    public string Visit(InterpolatedStringNode node)
    {
        var parts = new StringBuilder();
        parts.Append(node.IsMultiline ? "\"\"\"\n" : "\"");

        foreach (var part in node.Parts)
        {
            if (part is InterpolatedStringTextNode textPart)
            {
                parts.Append(EscapeStringText(textPart.Text, node.IsMultiline));
            }
            else if (part is InterpolatedStringExpressionNode exprPart)
            {
                parts.Append(Visit(exprPart));
            }
        }

        parts.Append(node.IsMultiline ? "\"\"\"" : "\"");
        if (node.IsUtf8)
            parts.Append("u8");
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
        try
        {
            var alignment = !string.IsNullOrEmpty(node.AlignmentClause) ? $",{node.AlignmentClause}" : "";
            var format = !string.IsNullOrEmpty(node.FormatSpecifier) ? $":{node.FormatSpecifier}" : "";
            var hole = $"{node.Expression.Accept(this)}{alignment}{format}";
            return node.Intent == InterpolationPartIntent.LiteralPlaceholder
                ? "\\${" + EscapeStringText(node.SourceText ?? hole, multiline: false) + "}"
                : "${" + hole + "}";
        }
        finally
        {
            _inInterpolation = savedInterpolation;
        }
    }

    public string Visit(NullCoalesceNode node)
    {
        var left = AcceptInConditionalRegion(node.Left);
        var right = AcceptInConditionalRegion(node.Right);
        return $"({left} ?? {right})";
    }

    public string Visit(NullConditionalNode node)
    {
        // Sanitize member name: collapse whitespace/newlines to single space
        // (multi-line C# chains from converter can produce newlines in member names)
        var memberName = System.Text.RegularExpressions.Regex.Replace(node.MemberName, @"\s+", " ").Trim();

        return $"{EmitPostfixOperand(node.Target)}?.{EscapeCalorIdentifier(memberName)}";
    }

    // Pattern-related methods

    private string EmitPattern(PatternNode pattern)
    {
        return pattern switch
        {
            WildcardPatternNode => "_",
            VariablePatternNode vp => Visit(vp),
            LiteralPatternNode lp => lp.Literal.Accept(this),
            VarPatternNode varp => Visit(varp),
            TypePatternNode tp => Visit(tp),
            ConstantPatternNode cp => cp.Value.Accept(this),
            RelationalPatternNode rp => EmitRelationalPattern(rp),
            PropertyPatternNode pp => Visit(pp),
            PositionalPatternNode pos => Visit(pos),
            ListPatternNode lp => Visit(lp),
            NegatedPatternNode np => $"(not {EmitPattern(np.Inner)})",
            OrPatternNode orp => $"(or {EmitPattern(orp.Left)} {EmitPattern(orp.Right)})",
            AndPatternNode andp => $"(and {EmitPattern(andp.Left)} {EmitPattern(andp.Right)})",
            // #774: no silent wildcard fallback — an unhandled pattern would
            // broaden the arm to match everything. Fail loud instead.
            _ => throw new ArgumentOutOfRangeException(nameof(pattern),
                $"Unhandled pattern node in Calor emitter: {pattern.GetType().Name}")
        };
    }

    private string EmitRelationalPattern(RelationalPatternNode node)
    {
        var operation = node.Operator switch
        {
            ">=" or "gte" => ">=",
            "<=" or "lte" => "<=",
            ">" or "gt" => ">",
            "<" or "lt" => "<",
            _ => throw new ArgumentOutOfRangeException(nameof(node), $"Unknown relational pattern operator: {node.Operator}")
        };
        var value = node.Value.Accept(this);
        return $"{operation} {value}";
    }

    public string Visit(WildcardPatternNode node) => "_";
    public string Visit(TypePatternNode node) => node.BindingName is { } name
        ? $"{TypeMapper.ToSurfaceType(node.TypeName)} {EscapeCalorIdentifier(name)}"
        : $"{TypeMapper.ToSurfaceType(node.TypeName)} _";
    public string Visit(VariablePatternNode node) => $"var {EscapeCalorIdentifier(node.Name)}";
    public string Visit(LiteralPatternNode node) => node.Literal.Accept(this);
    public string Visit(VarPatternNode node) => $"var {EscapeCalorIdentifier(node.Name)}";
    public string Visit(ConstantPatternNode node) => node.Value.Accept(this);
    public string Visit(NegatedPatternNode node) => $"(not {EmitPattern(node.Inner)})";
    public string Visit(OrPatternNode node) => $"(or {EmitPattern(node.Left)} {EmitPattern(node.Right)})";
    public string Visit(AndPatternNode node) => $"(and {EmitPattern(node.Left)} {EmitPattern(node.Right)})";

    // Additional pattern nodes
    public string Visit(PositionalPatternNode node)
    {
        var patterns = string.Join(", ", node.Patterns.Select(EmitPattern));
        return $"{TypeMapper.ToSurfaceType(node.TypeName)}({patterns})";
    }

    public string Visit(PropertyPatternNode node)
    {
        var matches = string.Join(", ", node.Matches.Select(m => m.Accept(this)));
        var typePart = string.IsNullOrEmpty(node.TypeName) ? "" : $"{TypeMapper.ToSurfaceType(node.TypeName)} ";
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
        var slice = node.SlicePattern == null ? null
            : node.SlicePattern.Name == "_" ? ".."
            : $".. {Visit(node.SlicePattern)}";
        for (int i = 0; i < node.Patterns.Count; i++)
        {
            if (slice != null && i == node.SliceIndex)
            {
                parts.Add(slice);
            }
            parts.Add(EmitPattern(node.Patterns[i]));
        }
        // Slice at end (or no non-slice patterns before it)
        if (slice != null && node.SliceIndex >= node.Patterns.Count)
        {
            parts.Add(slice);
        }
        return $"[{string.Join(", ", parts)}]";
    }

    // Type system nodes
    public string Visit(RecordDefinitionNode node)
    {
        var fields = string.Join(", ", node.Fields.Select(f =>
            $"{TypeMapper.ToSurfaceType(f.TypeName)}:{f.Name}"));
        AppendLine($"§D{{{node.Name}}} ({fields})");
        return "";
    }

    public string Visit(FieldDefinitionNode node)
    {
        var defaultValue = node.DefaultValue is null ? "" : $" = {node.DefaultValue.Accept(this)}";
        return $"{TypeMapper.ToSurfaceType(node.TypeName)}:{node.Name}{defaultValue}";
    }

    public string Visit(UnionTypeDefinitionNode node)
    {
        // Emit union types using the type/variant syntax
        AppendLine($"§T{{{node.Name}}}");
        Indent();
        foreach (var variant in node.Variants)
        {
            var fields = variant.Fields.Count > 0
                ? $"({string.Join(", ", variant.Fields.Select(f => $"{TypeMapper.ToSurfaceType(f.TypeName)}:{f.Name}"))})"
                : "";
            AppendLine($"§V{{{variant.Name}}}{fields}");
        }
        Dedent();
        AppendLine("§/T");
        return "";
    }

    public string Visit(VariantDefinitionNode node)
    {
        var fields = node.Fields.Count == 0
            ? ""
            : $"({string.Join(", ", node.Fields.Select(Visit))})";
        return $"§V{{{node.Name}}}{fields}";
    }

    public string Visit(TypeReferenceNode node)
    {
        var typeName = TypeMapper.ToSurfaceType(node.Name);
        return node.TypeArguments.Count == 0
            ? typeName
            : $"{typeName}<{string.Join(",", node.TypeArguments.Select(Visit))}>";
    }

    public string Visit(EnumDefinitionNode node)
    {
        EmitDocComment(node);
        // Format: §EN{Name:vis} or §EN{Name:vis:underlyingType}
        var vis = node.Visibility == Visibility.Public ? "" : " " + GetVisibilityShorthand(node.Visibility);
        var attrs = EmitCSharpAttributes(node.CSharpAttributes);
        var header = node.UnderlyingType != null
            ? $"enum {EscapeCalorIdentifier(node.Name)}{vis} {TypeMapper.ToSurfaceType(node.UnderlyingType)}{attrs}"
            : $"enum {EscapeCalorIdentifier(node.Name)}{vis}{attrs}";
        AppendLine(header);
        Indent();

        foreach (var member in node.Members)
        {
            Visit(member);
        }

        Dedent();
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
            ? $"{EscapeCalorIdentifier(node.Name)} = {value}"
            : EscapeCalorIdentifier(node.Name);
        AppendLine(line);
        return "";
    }

    public string Visit(EnumExtensionNode node)
    {
        return Visit(LowerEnumExtension(node));
    }

    internal static ClassDefinitionNode LowerEnumExtension(EnumExtensionNode node)
    {
        var enumName = CodeGen.CSharpEmitter.SanitizeIdentifier(node.EnumName);
        if (enumName.Contains('.') || enumName.Contains(':'))
            throw new InvalidOperationException("An enum extension requires an unqualified enum name.");
        var methods = node.Methods.Select(function =>
        {
            if (function.Examples.Count > 0 || function.Assumptions.Count > 0 || function.Properties.Count > 0)
                throw new InvalidOperationException("Enum extension metadata requires native method metadata support.");
            var receiver = function.Parameters.FirstOrDefault(parameter =>
                parameter.TypeName.Equals(enumName, StringComparison.OrdinalIgnoreCase)
                || parameter.Name.Equals("self", StringComparison.OrdinalIgnoreCase));
            if (receiver != null && !ReferenceEquals(receiver, function.Parameters[0]))
                throw new InvalidOperationException("An enum extension receiver must be the first parameter.");
            var parameters = function.Parameters.Select(parameter =>
                ReferenceEquals(parameter, receiver)
                    ? parameter.CopyMetadataTo(new ParameterNode(parameter.Span, parameter.Name, parameter.TypeName,
                        parameter.Modifier | ParameterModifier.This, parameter.Attributes, parameter.CSharpAttributes,
                        parameter.DefaultValue, parameter.InlineRefinement, parameter.IdentifierSpan,
                        parameter.TypeNameSpan, parameter.Row))
                    : parameter).ToArray();
            var method = function.CopyMetadataTo(new MethodNode(function.Span, function.LocalId, function.Name,
                function.Visibility, MethodModifiers.Static, function.TypeParameters, parameters, function.Output,
                function.Effects, function.Preconditions, function.Postconditions, function.Body, function.Attributes,
                [], function.IsAsync, function.IdentifierSpan, function.Since, function.Deprecated,
                function.BreakingChanges)
            {
                EffectParameters = function.EffectParameters
            });
            var guidance = new CalorEmitter();
            guidance.EmitRetiredFunctionMetadata(function);
            method.DocComment = string.Join("\n", new[] { function.DocComment }
                .Concat(guidance._builder.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => line.StartsWith("/// ", StringComparison.Ordinal) ? line[4..] : line[3..]))
                .Where(line => line != null));
            if (function.ToolingId != null) method.ToolingIdAllocationKind = function.ToolingIdAllocationKind ?? Ids.IdKind.Function;
            return method;
        }).ToArray();
        var result = node.CopyMetadataTo(new ClassDefinitionNode(node.Span, node.LocalId,
            (enumName + "Extensions").TrimStart('@'), false, false, false, true, null,
            [], [], [], [], [], methods, [], [], node.Attributes, [],
            visibility: Visibility.Public, isModule: true));
        if (node.ToolingId != null) result.ToolingIdAllocationKind = Ids.IdKind.EnumExtension;
        return result;
    }

    public string Visit(RecordCreationNode node)
    {
        return Visit(new NewExpressionNode(
            node.Span, node.TypeName, [],
            node.Fields.Select(field => field.Value).ToArray(), [],
            typeNameSpan: node.TypeNameSpan,
            argumentNames: node.Fields.Select(field => (string?)field.FieldName).ToArray()));
    }

    public string Visit(FieldAssignmentNode node)
        => $"§A[{node.FieldName}] {node.Value.Accept(this)}";

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
            TypeConstraintKind.Interface => TypeMapper.ToSurfaceType(node.TypeName ?? ""),
            TypeConstraintKind.BaseClass => TypeMapper.ToSurfaceType(node.TypeName ?? ""),
            TypeConstraintKind.TypeName => TypeMapper.ToSurfaceType(node.TypeName ?? ""),
            TypeConstraintKind.NotNull => "notnull",
            TypeConstraintKind.Unmanaged => "unmanaged",
            TypeConstraintKind.ClassNullable => "class?",
            TypeConstraintKind.Default => "default",
            TypeConstraintKind.AllowsRefStruct => "allows ref struct",
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
                AppendLine($"where {EscapeCalorIdentifier(tp.Name)} : {constraints}");
            }
        }
    }

    public string Visit(GenericTypeNode node)
    {
        if (node.TypeArguments.Count == 0)
            return TypeMapper.ToSurfaceType(node.TypeName);
        var args = string.Join(", ", node.TypeArguments.Select(TypeMapper.ToSurfaceType));
        return $"{TypeMapper.ToSurfaceType(node.TypeName)}<{args}>";
    }

    // Delegate and event nodes
    public string Visit(DelegateDefinitionNode node)
    {
        EmitDocComment(node);
        var parameters = TryFormatInlineParams(node.Parameters)
            ?? throw new InvalidOperationException("Native delegates require inline parameters.");
        var output = node.Output == null ? "" : $" → {TypeMapper.ToSurfaceType(node.Output.TypeName)}{FormatEffectRow(node.Output.Row)}";
        AppendLine($"delegate {EscapeCalorIdentifier(node.Name)} ({parameters}){output}");
        Indent();
        AppendLine(node.Effects == null ? "! unknown" : FormatEffectsTag(node.Effects));

        Dedent();
        return "";
    }

    public string Visit(EventDefinitionNode node)
    {
        EmitDocComment(node);
        var visibility = node.Visibility == Visibility.Public ? "" : " " + GetVisibilityShorthand(node.Visibility);
        var modifiers = node.IsStatic ? " static" : "";
        var delegateType = TypeMapper.ToSurfaceType(node.DelegateType);

        if (node.HasAccessors)
        {
            AppendLine($"event {delegateType}:{EscapeCalorIdentifier(node.Name)}{visibility}{modifiers}");
            Indent();
            _memberBodyDepth++;

            if (node.AddBody != null)
            {
                AppendLine("add");
                Indent();
                foreach (var stmt in node.AddBody)
                {
                    stmt.Accept(this);
                }
                Dedent();
            }

            if (node.RemoveBody != null)
            {
                AppendLine("remove");
                Indent();
                foreach (var stmt in node.RemoveBody)
                {
                    stmt.Accept(this);
                }
                Dedent();
            }

            _memberBodyDepth--;
            Dedent();
        }
        else
        {
            AppendLine($"event {delegateType}:{EscapeCalorIdentifier(node.Name)}{visibility}{modifiers}");
        }

        return "";
    }

    public string Visit(EventSubscribeNode node)
    {
        var evt = AcceptInInlineSibling(node.Event);
        var handler = AcceptInInlineSibling(node.Handler);
        AppendLine($"§SUB {evt} {handler}");
        return "";
    }

    public string Visit(EventUnsubscribeNode node)
    {
        var evt = AcceptInInlineSibling(node.Event);
        var handler = AcceptInInlineSibling(node.Handler);
        AppendLine($"§UNSUB {evt} {handler}");
        return "";
    }

    // Modern operator nodes
    public string Visit(RangeExpressionNode node)
    {
        var start = node.Start is null ? "" : AcceptInConditionalRegion(node.Start);
        var end = node.End is null ? "" : AcceptInConditionalRegion(node.End);
        return $"({start}..{end})";
    }

    public string Visit(IndexFromEndNode node)
    {
        var offset = AcceptInConditionalRegion(node.Offset);
        return $"(^{offset})";
    }

    public string Visit(WithExpressionNode node)
    {
        var target = AcceptInConditionalRegion(node.Target);
        var assignments = string.Join(", ", node.Assignments.Select(a => a.Accept(this)));
        return $"({target} with {{ {assignments} }})";
    }

    public string Visit(WithPropertyAssignmentNode node)
    {
        var value = AcceptInConditionalRegion(node.Value);
        return $"{EscapeCalorIdentifier(node.PropertyName)} = {value}";
    }

    // Retained semantic metadata keeps its current syntax until its replacement is measured.
    public string Visit(ExampleNode node)
    {
        var expr = node.Expression.Accept(this);
        var expected = node.Expected.Accept(this);
        var message = node.Message != null ? $":{Visit(new StringLiteralNode(node.Span, node.Message))}" : "";
        var attributes = node.Id != null || node.Message != null
            ? $"{{{Visit(new StringLiteralNode(node.Span, node.Id ?? ""))}{message}}}"
            : "";
        AppendLine($"§EX{attributes} {expr} -> {expected}");
        return "";
    }

    public string Visit(IssueNode node)
    {
        var id = node.Id != null ? $"{node.Id}:" : "";
        EmitDocumentationText($"{node.Kind} {id}{node.Category ?? ""} ({node.Priority}): {node.Description}");
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
        EmitDocumentationText($"Uses: {deps}");
        return "";
    }

    public string Visit(UsedByNode node)
    {
        var deps = string.Join(", ", node.Dependents.Select(d => d.Accept(this)));
        var external = node.HasUnknownCallers ? ", [external]" : "";
        EmitDocumentationText($"Used by: {deps}{external}");
        return "";
    }

    public string Visit(AssumeNode node)
    {
        var category = node.Category.HasValue ? $"{{{node.Category.Value.ToString().ToLowerInvariant()}}}" : "";
        AppendLine($"§AS{category} {Visit(new StringLiteralNode(node.Span, node.Description))}");
        return "";
    }

    public string Visit(ComplexityNode node)
    {
        var parts = new List<string>();
        if (node.TimeComplexity.HasValue) parts.Add($"time:{FormatComplexity(node.TimeComplexity.Value)}");
        if (node.SpaceComplexity.HasValue) parts.Add($"space:{FormatComplexity(node.SpaceComplexity.Value)}");
        if (node.CustomExpression != null) parts.Add(node.CustomExpression);
        var worst = node.IsWorstCase ? "worst:" : "";
        EmitDocumentationText($"Complexity: {worst}{string.Join(",", parts)}");
        return "";
    }

    public string Visit(SinceNode node)
    {
        AppendLine($"/// since {node.Version}");
        return "";
    }

    public string Visit(DeprecatedNode node)
    {
        if (node.Reason != null || node.RemovedInVersion != null)
            throw new InvalidOperationException(
                "The selected documentation syntax cannot preserve deprecation reason/removal fields. " +
                "A measured structured-documentation replacement is required before formatting these fields.");
        var replacement = node.Replacement != null
            ? $" {node.Replacement}" : "";
        if (node.Replacement?.Any(char.IsWhiteSpace) == true)
            throw new InvalidOperationException("The selected deprecation replacement must be one word.");
        AppendLine($"/// deprecated {node.SinceVersion}{replacement}");
        return "";
    }

    public string Visit(BreakingChangeNode node)
    {
        AppendLine($"/// breaking {node.Version} "
            + Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(node.Description, quote: true));
        return "";
    }

    public string Visit(DecisionNode node)
    {
        EmitDocumentationText($"Decision {node.Id}: {node.Title}");
        EmitDocumentationText($"Chosen: {node.ChosenOption}");
        foreach (var reason in node.ChosenReasons)
        {
            EmitDocumentationText($"Reason: {reason}");
        }
        foreach (var rejected in node.RejectedOptions) Visit(rejected);
        if (node.Context != null) EmitDocumentationText($"Context: {node.Context}");
        if (node.Date != null)
            EmitDocumentationText($"Date: {node.Date.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)}");
        if (node.Author != null) EmitDocumentationText($"Author: {node.Author}");
        return "";
    }

    public string Visit(RejectedOptionNode node)
    {
        EmitDocumentationText($"Rejected: {node.Name}");
        foreach (var reason in node.Reasons)
        {
            EmitDocumentationText($"Reason: {reason}");
        }
        return "";
    }

    public string Visit(ContextNode node)
    {
        var partial = node.IsPartial ? ":partial" : "";
        EmitDocumentationText($"Context{partial}");
        foreach (var file in node.VisibleFiles) EmitDocumentationText($"Visible: {Visit(file)}");
        foreach (var file in node.HiddenFiles) EmitDocumentationText($"Hidden: {Visit(file)}");
        if (node.FocusTarget != null) EmitDocumentationText($"Focus: {node.FocusTarget}");
        return "";
    }

    public string Visit(FileRefNode node)
    {
        var desc = node.Description != null ? $" ({node.Description})" : "";
        return $"{node.FilePath}{desc}";
    }

    public string Visit(PropertyTestNode node)
    {
        var quantifiers = node.Quantifiers.Count > 0 ? $"forall {string.Join(",", node.Quantifiers)}: " : "";
        var predicate = node.Predicate.Accept(this);
        AppendLine($"§PT {quantifiers}{predicate}");
        return "";
    }

    public string Visit(LockNode node)
    {
        var acquired = node.Acquired.HasValue ? $":acquired={node.Acquired.Value:O}" : "";
        var expires = node.Expires.HasValue ? $":expires={node.Expires.Value:O}" : "";
        EmitDocumentationText($"Lock: agent={node.AgentId}{acquired}{expires}");
        return "";
    }

    public string Visit(AuthorNode node)
    {
        var task = node.TaskId != null ? $":task={node.TaskId}" : "";
        var date = node.Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        EmitDocumentationText($"Author: agent={node.AgentId}:date={date}{task}");
        return "";
    }

    public string Visit(TaskRefNode node)
    {
        EmitDocumentationText($"Task {node.TaskId}: {node.Description}");
        return "";
    }

    // Helper methods

    private static string GetVisibilityShorthand(Visibility visibility)
    {
        return visibility switch
        {
            Visibility.Public => "public",
            Visibility.ProtectedInternal => "protected internal",
            Visibility.PrivateProtected => "private protected",
            Visibility.Protected => "protected",
            Visibility.Internal => "internal",
            Visibility.Private => "private",
            _ => throw new InvalidOperationException($"Unsupported visibility: {visibility}.")
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
        "nameof", "when",
        // Literal keywords — conflict with Calor typed literals when used as identifiers
        "true", "false", "null"
    };

    private static string EscapeCalorIdentifier(string name)
    {
        // Strip @ prefix if present (Roslyn's Identifier.Text includes it for verbatim identifiers)
        var bareName = name.StartsWith('@') ? name[1..] : name;
        return CSharpReservedWords.Contains(bareName) || Lexer.KeywordNames.Contains(bareName)
            ? $"@{bareName}" : bareName;
    }

    private void EmitEffects(EffectsNode? effects, bool required = false)
    {
        if (effects == null)
        {
            if (required)
                AppendLine("!");
            return;
        }

        AppendLine(FormatEffectsTag(effects));
    }

    /// <summary>
    /// Renders a <c>§E{…}</c> tag: concrete codes first, then any effect variables.
    /// <c>§E{cw, e}</c> round-trips to <c>§E{cw,e}</c> — the same normalisation the
    /// emitter has always applied to code lists.
    /// </summary>
    private static string FormatEffectsTag(EffectsNode effects)
    {
        if (effects.IsUnknown)
            return "! unknown";
        var effectCodes = effects.Effects
            .SelectMany(kvp => kvp.Value.Split(',').Select(v => EffectCodes.ToCompact(kvp.Key, v.Trim())))
            .Distinct()
            .Concat(effects.EffectVariables);
        var codes = string.Join(", ", effectCodes);
        return codes.Length == 0 ? "!" : $"! {codes}";
    }

    /// <summary>
    /// Renders an effect row as a same-line suffix. Unlike a declaration's own
    /// <c>§E</c>, an <b>empty</b> row is still emitted: design-doc §3.5 gives
    /// <c>§E{}</c> (pure) and an omitted row (Unknown) different meanings, so
    /// dropping the tag would change what the source says.
    /// </summary>
    private static string FormatEffectRow(EffectsNode? row)
        => row == null ? "" : " " + FormatEffectsTag(row);

    /// <summary>
    /// Splits a declaration's <c>&lt;…&gt;</c> list into the part that goes INSIDE the
    /// header group — where <c>§F</c> and <c>§MT</c> have always written it — and the
    /// part that goes after it.
    /// </summary>
    /// <remarks>
    /// The in-group spelling is textually stable but semantically lossy: on re-parse
    /// <c>§F{f001:Map&lt;T&gt;:pub}</c> keeps <c>&lt;T&gt;</c> as part of the NAME and
    /// <c>TypeParameters</c> comes back empty. That is pre-existing and harmless for
    /// plain type parameters, because the text round-trips unchanged. It is <b>not</b>
    /// harmless for an <c>eff</c> binder: an absorbed binder is no longer bound, and
    /// the re-parsed program fails with <c>Calor0403</c> on its own rows. So a list
    /// carrying a binder is written after the group instead, where
    /// <c>ParseOptionalTypeParameterList</c> — the branch that understands
    /// <c>eff</c> — actually reads it. Lists with no binder keep the old placement
    /// byte for byte.
    /// </remarks>
    private (string InGroup, string AfterGroup) SplitTypeParameterList(
        IReadOnlyList<TypeParameterNode> typeParameters,
        IReadOnlyList<EffectParameterInfo> effectParameters)
    {
        var rendered = FormatTypeParameterList(typeParameters, effectParameters);
        return effectParameters.Count > 0 ? ("", " " + rendered) : (rendered, "");
    }

    /// <summary>
    /// Renders a declaration's <c>&lt;…&gt;</c> list, interleaving ordinary type
    /// parameters with <c>eff</c> binders at the ordinals they were written at, so
    /// <c>&lt;T, eff e&gt;</c> and <c>&lt;eff e, T&gt;</c> both round-trip.
    /// </summary>
    private string FormatTypeParameterList(
        IReadOnlyList<TypeParameterNode> typeParameters,
        IReadOnlyList<EffectParameterInfo> effectParameters)
    {
        if (typeParameters.Count == 0 && effectParameters.Count == 0)
            return "";

        var slots = new string?[typeParameters.Count + effectParameters.Count];
        foreach (var binder in effectParameters)
        {
            if (binder.Ordinal >= 0 && binder.Ordinal < slots.Length && slots[binder.Ordinal] is null)
                slots[binder.Ordinal] = $"! {EscapeCalorIdentifier(binder.Name)}";
        }

        var next = 0;
        foreach (var typeParameter in typeParameters)
        {
            while (next < slots.Length && slots[next] is not null)
                next++;
            if (next < slots.Length)
                slots[next] = Visit(typeParameter);
        }

        return $"<{string.Join(",", slots.Where(slot => slot is not null))}>";
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
        // #774: no silent fallback to "+" — an unmapped operator must never be
        // emitted as addition. This switch is exhaustive over BinaryOperator.
        _ => throw new ArgumentOutOfRangeException(nameof(op),
            $"Unhandled binary operator in Calor emitter: {op}")
    };

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
        var value = arg.Value is string text
            ? $"\"{EscapeStringText(text, multiline: false)}\""
            : arg.GetCalorFormattedValue();

        if (arg.IsNamed)
        {
            return $"{arg.Name}={value}";
        }
        return value;
    }

    // Quantified Contracts

    public string Visit(QuantifierVariableNode node)
    {
        return $"{TypeMapper.ToSurfaceType(node.TypeName)}:{EscapeCalorIdentifier(node.Name)}";
    }

    public string Visit(ForallExpressionNode node)
        => node.Body is ImplicationExpressionNode implication
            ? EmitNativeQuantifier("forall", node.BoundVariables, implication.Antecedent, implication.Consequent)
            : EmitNativeQuantifier("forall", node.BoundVariables, new BoolLiteralNode(node.Span, true), node.Body);

    public string Visit(ExistsExpressionNode node)
        => node.Body is BinaryOperationNode { Operator: BinaryOperator.And } conjunction
            ? EmitNativeQuantifier("exists", node.BoundVariables, conjunction.Left, conjunction.Right)
            : EmitNativeQuantifier("exists", node.BoundVariables, new BoolLiteralNode(node.Span, true), node.Body);

    private string EmitNativeQuantifier(string kind, IReadOnlyList<QuantifierVariableNode> variables,
        ExpressionNode guard, ExpressionNode predicate)
    {
        var bounds = new List<ExpressionNode>();
        void Collect(ExpressionNode expression)
        {
            if (expression is BinaryOperationNode { Operator: BinaryOperator.And } conjunction)
            {
                Collect(conjunction.Left);
                Collect(conjunction.Right);
            }
            else
                bounds.Add(expression);
        }
        Collect(guard);
        var ranges = new List<(QuantifierVariableNode Variable, ExpressionNode Lower, ExpressionNode Upper)>();
        for (var index = 0; index < variables.Count && bounds.Count == variables.Count * 2; index++)
        {
            var variable = variables[index];
            if (bounds[index * 2] is not BinaryOperationNode
                {
                    Operator: BinaryOperator.GreaterOrEqual,
                    Left: ReferenceNode lowerVariable
                } lower
                || lowerVariable.IsNullLiteral || lowerVariable.Name != variable.Name
                || bounds[index * 2 + 1] is not BinaryOperationNode
                {
                    Operator: BinaryOperator.LessThan,
                    Left: ReferenceNode upperVariable
                } upper
                || upperVariable.IsNullLiteral || upperVariable.Name != variable.Name)
                break;
            ranges.Add((variable, lower.Right, upper.Right));
        }
        var bindings = ranges.Count == variables.Count
            ? string.Join(", ", ranges.Select(range =>
                $"{range.Variable.Accept(this)} in {AcceptInConditionalRegion(range.Lower)}"
                + $"..{AcceptInConditionalRegion(range.Upper)}"))
            : string.Join(", ", variables.Select(variable => variable.Accept(this)))
                + $" where {AcceptInConditionalRegion(guard)}";
        return $"({kind} {bindings} : {AcceptInConditionalRegion(predicate)})";
    }

    public string Visit(ImplicationExpressionNode node)
    {
        var ante = AcceptInConditionalRegion(node.Antecedent);
        var cons = AcceptInConditionalRegion(node.Consequent);
        return $"(!({ante}) || {cons})";
    }

    // Native String Operations

    public string Visit(StringOperationNode node)
    {
        var arguments = node.Arguments.Select(AcceptInConditionalRegion).ToList();
        var method = node.Operation switch
        {
            StringOp.SubstringFrom => "Substring",
            StringOp.ToUpper => "ToUpper",
            StringOp.ToLower => "ToLower",
            StringOp.RegexTest => "IsMatch",
            StringOp.RegexMatch => "Match",
            StringOp.RegexReplace => "Replace",
            StringOp.RegexSplit => "Split",
            _ => node.Operation.ToString()
        };
        var staticString = node.Operation is StringOp.Join or StringOp.Format or StringOp.Concat
            or StringOp.IsNullOrEmpty or StringOp.IsNullOrWhiteSpace;
        var regex = node.Operation is StringOp.RegexTest or StringOp.RegexMatch
            or StringOp.RegexReplace or StringOp.RegexSplit;
        var receiver = staticString ? "string"
            : regex ? "System.Text.RegularExpressions.Regex" : $"({arguments[0]})";
        if (node.Operation == StringOp.Length && node.Arguments[0] is ReferenceNode)
            receiver = arguments[0];
        if (!staticString && !regex)
            arguments.RemoveAt(0);
        if (node.Operation == StringOp.Length)
            return $"{receiver}.Length";
        if (node.ComparisonMode is { } comparison)
            arguments.Add(comparison.ToCSharpName());
        return $"{receiver}.{method}({string.Join(", ", arguments)})";
    }

    public string Visit(CharOperationNode node)
    {
        if (node.Operation == CharOp.CharLiteral
            && node.Arguments is [StringLiteralNode { Value.Length: 1 } literal])
            return Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(literal.Value[0], quote: true);

        var arguments = node.Arguments.Select(AcceptInConditionalRegion).ToArray();
        return node.Operation switch
        {
            CharOp.CharAt => $"({arguments[0]})[{arguments[1]}]",
            CharOp.CharCode => $"((int){arguments[0]})",
            CharOp.CharFromCode => $"((char){arguments[0]})",
            CharOp.ToUpperChar => $"char.ToUpper({arguments[0]})",
            CharOp.ToLowerChar => $"char.ToLower({arguments[0]})",
            CharOp.IsLetter or CharOp.IsDigit or CharOp.IsWhiteSpace or CharOp.IsUpper or CharOp.IsLower
                => $"char.{node.Operation}({string.Join(", ", arguments)})",
            _ => throw new NotSupportedException($"Unsupported native char operation {node.Operation}.")
        };
    }

    public string Visit(StringBuilderOperationNode node)
    {
        var arguments = node.Arguments.Select(AcceptInConditionalRegion).ToList();
        if (node.Operation == StringBuilderOp.New)
            return $"new System.Text.StringBuilder({string.Join(", ", arguments)})";
        var receiver = arguments[0];
        arguments.RemoveAt(0);
        return node.Operation == StringBuilderOp.Length ? $"({receiver}).Length"
            : $"({receiver}).{node.Operation}({string.Join(", ", arguments)})";
    }

    public string Visit(TypeOperationNode node)
    {
        var operand = AcceptInInlineSibling(node.Operand);
        if (node.Operation == TypeOp.Cast && node.Operand is LambdaExpressionNode)
            operand = $"({operand})";
        return node.Operation switch
        {
            TypeOp.Cast => $"(({TypeMapper.ToSurfaceType(node.TargetType)}){operand})",
            TypeOp.Is => $"({operand} is {TypeMapper.ToSurfaceType(node.TargetType)})",
            TypeOp.As => $"({operand} as {TypeMapper.ToSurfaceType(node.TargetType)})",
            _ => throw new NotSupportedException($"Unknown type operation: {node.Operation}")
        };
    }

    public string Visit(IsPatternNode node)
    {
        // AcceptInInlineSibling: operand is space-separated from the type in
        // Lisp `(is OPERAND Type)` form. A naked zero-arg §C{Foo} operand would
        // absorb the type as its inline arg on re-parse.
        // Defect: v0.6.1 loop-4 devil's-advocate finding #2.
        var operand = AcceptInInlineSibling(node.Operand);
        return node.VariableName != null
            ? $"({operand} is {TypeMapper.ToSurfaceType(node.TargetType)} {EscapeCalorIdentifier(node.VariableName)})"
            : $"({operand} is {TypeMapper.ToSurfaceType(node.TargetType)})";
    }

    // Fallback nodes for unsupported C# constructs

    public string Visit(FallbackExpressionNode node)
        => EmitRawCSharpExpression(node.OriginalCSharp, nameof(FallbackExpressionNode));

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
        return $"typeof({TypeMapper.ToSurfaceType(node.TypeName)})";
    }

    public string Visit(OverflowContextExpressionNode node)
    {
        var keyword = node.IsChecked ? "checked" : "unchecked";
        return $"{keyword}({AcceptInConditionalRegion(node.Operand)})";
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
        return $"nameof({name})";
    }

    public string Visit(ExpressionCallNode node)
    {
        if (IsConditionalCallTarget(node.TargetExpression) && node.TypeArguments is { Count: > 0 })
            throw new NotSupportedException("Null-conditional computed generic calls remain staged.");

        var callee = EmitPostfixOperand(node.TargetExpression);
        var types = node.TypeArguments is { Count: > 0 }
            ? $"<{string.Join(", ", node.TypeArguments.Select(TypeMapper.ToSurfaceType))}>"
            : "";
        var arguments = node.Arguments.Select((argument, index) =>
        {
            var name = node.ArgumentNames?.ElementAtOrDefault(index);
            var modifier = node.ArgumentModifiers?.ElementAtOrDefault(index);
            return (name is null ? "" : $"{EscapeCalorIdentifier(name.TrimStart('@'))}: ")
                + (modifier is null ? "" : $"{modifier} ") + AcceptArgument(argument);
        });
        return $"{callee}{types}({string.Join(", ", arguments)})";
    }

    private string EmitPostfixOperand(ExpressionNode node) => node switch
    {
        FieldAccessNode field =>
            $"{EmitPostfixOperand(field.Target)}.{EscapeCalorIdentifier(field.FieldName.TrimStart('@'))}",
        ArrayAccessNode array =>
            $"{EmitPostfixOperand(array.Array)}[{AcceptInConditionalRegion(array.Index)}]",
        MultiDimArrayAccessNode array =>
            $"{EmitPostfixOperand(array.Array)}[{string.Join(", ", array.Indices.Select(AcceptInConditionalRegion))}]",
        ReferenceNode or ExpressionCallNode or NullConditionalNode => AcceptInConditionalRegion(node),
        _ => $"({AcceptInConditionalRegion(node)})"
    };

    private static bool IsConditionalCallTarget(ExpressionNode expression) => expression switch
    {
        NullConditionalNode => true,
        FieldAccessNode field => IsConditionalCallTarget(field.Target),
        ArrayAccessNode array => IsConditionalCallTarget(array.Array),
        MultiDimArrayAccessNode array => IsConditionalCallTarget(array.Array),
        ExpressionCallNode call => IsConditionalCallTarget(call.TargetExpression),
        _ => false
    };

    public string Visit(RawCSharpNode node)
    {
        if (_context.CapturePreservationContext
            && RawCSharpFrame.TryParseReason(node.FramingReason ?? "statement", out var frame))
            _context.RecordPreservationEmission(node, frame with { Code = node.CSharpCode });
        EmitRawFrame(node.FramingReason ?? "statement", node.CSharpCode);
        return "";
    }

    private void EmitRawFrame(string reason, string code)
    {
        var byteCount = new UTF8Encoding(false, true).GetByteCount(code);
        AppendLine($"raw {reason} {byteCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        _builder.Append(code);
        // This separator belongs to the Calor envelope, never to the payload.
        _builder.AppendLine();
    }

    public string Visit(CompilerDirectiveNode node)
    {
        var frame = new RawCSharpFrame(node.Code, node.Feature, IsCompilerDirective: true);
        if (!frame.TryValidateCompilerDirective(out var error))
            throw new InvalidOperationException(error);
        EmitRawFrame(frame.ReasonMarker, node.Code);
        return "";
    }

    public string Visit(RawCSharpExpressionNode node)
        => EmitRawCSharpExpression(node.CSharpCode, nameof(RawCSharpExpressionNode));

    /// <summary>
    /// 0.30 has no inline raw C# expression form: the retired <c>§CS{…}</c> marker is a lexer
    /// error. An interpolation hole is itself C# source (the parser reads <c>${…}</c> as a C#
    /// expression), so a raw hole is written back verbatim. Anywhere else a raw expression
    /// cannot be spelled; the converter preserves the enclosing statement or member instead
    /// (<c>raw reason N</c>), so reaching this is an emitter contract violation, not output.
    /// </summary>
    private string EmitRawCSharpExpression(string code, string nodeKind)
    {
        if (_inInterpolation)
            return code;
        throw new InvalidOperationException(
            $"{nodeKind} has no native 0.30 spelling outside an interpolation hole; "
            + "preserve the enclosing statement or member as a raw frame instead.");
    }

    public string Visit(PreprocessorDirectiveNode node)
    {
        var first = true;
        for (var branch = node; ; first = false)
        {
            AppendLine(first ? $"pp {branch.Condition}" : $"pp elif {branch.Condition}");
            Indent();
            foreach (var stmt in branch.Body)
                stmt.Accept(this);
            Dedent();
            if (branch.ElseBody is { Count: 1 } && branch.ElseBody[0] is PreprocessorDirectiveNode next)
            {
                branch = next;
                continue;
            }
            if (branch.ElseBody is { Count: > 0 })
            {
                AppendLine("pp else");
                Indent();
                foreach (var stmt in branch.ElseBody)
                    stmt.Accept(this);
                Dedent();
            }
            break;
        }
        return "";
    }

    public string Visit(MemberPreprocessorBlockNode node)
    {
        var first = true;
        for (var branch = node; branch != null; branch = branch.ElseBranch)
        {
            AppendLine(first ? $"pp {branch.Condition}"
                : branch.Condition.Length == 0 ? "pp else" : $"pp elif {branch.Condition}");
            Indent();
            EmitMemberPreprocessorMembers(branch);
            Dedent();
            first = false;
        }
        return "";
    }

    private void EmitMemberPreprocessorMembers(MemberPreprocessorBlockNode node)
    {
        foreach (var item in node.Items)
        {
            switch (item)
            {
                case ClassFieldNode field:
                    Visit(field);
                    break;
                case PropertyNode property:
                    Visit(property);
                    break;
                case IndexerNode indexer:
                    Visit(indexer);
                    break;
                case ConstructorNode constructor:
                    Visit(constructor);
                    break;
                case MethodNode method:
                    Visit(method);
                    AppendLine();
                    break;
                case MethodSignatureNode method:
                    Visit(method);
                    break;
                case OperatorOverloadNode op:
                    Visit(op);
                    AppendLine();
                    break;
                case EventDefinitionNode evt:
                    Visit(evt);
                    break;
                case CSharpInteropBlockNode interop:
                    Visit(interop);
                    break;
                case CompilerDirectiveNode directive:
                    Visit(directive);
                    break;
                case MemberPreprocessorBlockNode nested:
                    Visit(nested);
                    break;
                case ClassDefinitionNode nestedClass:
                    Visit(nestedClass);
                    break;
                case InterfaceDefinitionNode nestedInterface:
                    Visit(nestedInterface);
                    break;
                case EnumDefinitionNode nestedEnum:
                    Visit(nestedEnum);
                    break;
                case DelegateDefinitionNode nestedDelegate:
                    Visit(nestedDelegate);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported member preprocessor item: {item.GetType().Name}");
            }
        }
    }

    public string Visit(TypePreprocessorBlockNode node)
    {
        var first = true;
        for (var branch = node; branch != null; branch = branch.ElseBranch)
        {
            AppendLine(first ? $"pp {branch.Condition}"
                : branch.Condition.Length == 0 ? "pp else" : $"pp elif {branch.Condition}");
            Indent();
            EmitTypePreprocessorTypes(branch);
            Dedent();
            first = false;
        }
        return "";
    }

    private void EmitTypePreprocessorTypes(TypePreprocessorBlockNode node)
    {
        foreach (var item in node.Items)
        {
            switch (item)
            {
                case UsingDirectiveNode u:
                    Visit(u);
                    break;
                case ClassDefinitionNode cls:
                    Visit(cls);
                    AppendLine();
                    break;
                case InterfaceDefinitionNode iface:
                    Visit(iface);
                    AppendLine();
                    break;
                case EnumDefinitionNode en:
                    Visit(en);
                    AppendLine();
                    break;
                case DelegateDefinitionNode del:
                    Visit(del);
                    AppendLine();
                    break;
                case TypePreprocessorBlockNode nested:
                    Visit(nested);
                    break;
                case CSharpInteropBlockNode interop:
                    Visit(interop);
                    break;
                case CompilerDirectiveNode directive:
                    Visit(directive);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported type preprocessor item: {item.GetType().Name}");
            }
        }
    }

    public string Visit(CSharpInteropBlockNode node)
    {
        var frame = new RawCSharpFrame(node.CSharpCode, node.FeatureName,
            node.Reason, node.MemberKind, node.IsCompilationUnitPassthrough);
        _context.RecordPreservationEmission(node, frame);
        EmitRawFrame(frame.ReasonMarker, frame.Code);
        return "";
    }

    public string Visit(StackAllocNode node)
    {
        var elementType = TypeMapper.ToSurfaceType(node.ElementType);
        if (node.Size != null)
        {
            var size = EmitSizeAttribute(node.Size);
            return $"§SALLOC{{{elementType}:{size}}}";
        }
        else if (node.Initializer.Count > 0)
        {
            var elements = string.Join(" ", node.Initializer.Select(e => AcceptInInlineSibling(e)));
            return $"§SALLOC{{{elementType}}} {elements} §/SALLOC";
        }
        return $"§SALLOC{{{elementType}:0}}";
    }

    public string Visit(UnsafeBlockNode node)
    {
        AppendLine("unsafe");
        Indent();
        foreach (var stmt in node.Body)
            stmt.Accept(this);
        Dedent();
        return "";
    }

    public string Visit(SyncBlockNode node)
    {
        var lockExpr = node.LockExpression.Accept(this);
        AppendLine($"lock {lockExpr}");
        Indent();
        foreach (var stmt in node.Body)
            stmt.Accept(this);
        Dedent();
        return "";
    }

    public string Visit(FixedStatementNode node)
    {
        var pointerType = TypeMapper.ToSurfaceType(node.PointerType);
        var init = node.Initializer.Accept(this);
        AppendLine($"fixed {pointerType}:{node.PointerName} = {init}");
        Indent();
        foreach (var stmt in node.Body)
            stmt.Accept(this);
        Dedent();
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
        var typeName = TypeMapper.ToSurfaceType(node.TypeName);
        return $"sizeof({typeName})";
    }

    public string Visit(MultiDimArrayCreationNode node)
        => CodeGen.CSharpEmitter.EmitMultiDimArrayCreation(node, AcceptInConditionalRegion,
            TypeMapper.ToSurfaceType(node.ElementType));

    public string Visit(MultiDimArrayAccessNode node)
    {
        var indices = string.Join(", ", node.Indices.Select(AcceptInConditionalRegion));
        return $"{EmitPostfixOperand(node.Array)}[{indices}]";
    }

    // Dependent Types: Refinement Types and Proof Obligations

    public string Visit(RefinementTypeNode node)
    {
        var predicate = node.Predicate.Accept(this);
        AppendLine($"§RTYPE{{{node.Name}:{TypeMapper.ToSurfaceType(node.BaseTypeName)}}} {predicate}");
        return "";
    }

    public string Visit(SelfRefNode node)
    {
        return "#";
    }

    public string Visit(ProofObligationNode node)
    {
        var condition = node.Condition.Accept(this);
        var header = node.Description == null
            ? "proof"
            : $"proof{{{Visit(new StringLiteralNode(node.Span, node.Description))}}}";
        AppendLine($"{header} {condition}");
        return "";
    }

    public string Visit(IndexedTypeNode node)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"§ITYPE{{{node.Name}:{TypeMapper.ToSurfaceType(node.BaseTypeName)}:{node.SizeParam}}}");
        if (node.Constraint != null)
        {
            var constraint = node.Constraint.Accept(this);
            sb.Append($" {constraint}");
        }
        AppendLine(sb.ToString());
        return "";
    }
}
