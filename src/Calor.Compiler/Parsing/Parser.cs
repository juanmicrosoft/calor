using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;

namespace Calor.Compiler.Parsing;

/// <summary>
/// Recursive descent parser for Calor source code.
/// </summary>
public sealed class Parser
{
    private readonly List<Token> _tokens;
    private readonly DiagnosticBag _diagnostics;
    private int _position;
    private bool _insideArgContext;

    public Parser(IEnumerable<Token> tokens, DiagnosticBag diagnostics)
    {
        _tokens = tokens.ToList();
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    private Token Current => Peek(0);
    private Token Lookahead => Peek(1);

    private Token Peek(int offset)
    {
        var index = _position + offset;
        return index < _tokens.Count ? _tokens[index] : _tokens[^1];
    }

    private bool IsAtEnd => Current.Kind == TokenKind.Eof;

    private Token Advance()
    {
        var token = Current;
        if (!IsAtEnd)
        {
            _position++;
        }
        return token;
    }

    private bool Check(TokenKind kind) => Current.Kind == kind;

    private bool Match(TokenKind kind)
    {
        if (Check(kind))
        {
            Advance();
            return true;
        }
        return false;
    }

    private Token Expect(TokenKind kind)
    {
        if (Check(kind))
        {
            return Advance();
        }

        _diagnostics.ReportUnexpectedToken(Current.Span, kind, Current.Kind);
        return new Token(kind, "", Current.Span);
    }

    public ModuleNode Parse()
    {
        return ParseModule();
    }

    private ModuleNode ParseModule()
    {
        var startToken = Expect(TokenKind.Module);
        var attrs = ParseAttributes();

        var (id, moduleName) = AttributeHelper.InterpretModuleAttributes(attrs);
        if (string.IsNullOrEmpty(id))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "MODULE", "id");
            id = "";
        }
        if (string.IsNullOrEmpty(moduleName))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "MODULE", "name");
            moduleName = "";
        }

        var usings = new List<UsingDirectiveNode>();
        var interfaces = new List<InterfaceDefinitionNode>();
        var classes = new List<ClassDefinitionNode>();
        var enums = new List<EnumDefinitionNode>();
        var enumExtensions = new List<EnumExtensionNode>();
        var delegates = new List<DelegateDefinitionNode>();
        var functions = new List<FunctionNode>();

        // Extended Features: Module-level metadata
        var issues = new List<IssueNode>();
        var assumptions = new List<AssumeNode>();
        var invariants = new List<InvariantNode>();
        var decisions = new List<DecisionNode>();
        ContextNode? context = null;
        var interopBlocks = new List<CSharpInteropBlockNode>();

        while (!IsAtEnd && !Check(TokenKind.EndModule))
        {
            if (Check(TokenKind.Using))
            {
                usings.Add(ParseUsingDirective());
            }
            else if (Check(TokenKind.Interface))
            {
                interfaces.Add(ParseInterfaceDefinition());
            }
            else if (Check(TokenKind.Class))
            {
                classes.Add(ParseClassDefinition());
            }
            else if (Check(TokenKind.Func))
            {
                functions.Add(ParseFunction());
            }
            else if (Check(TokenKind.AsyncFunc))
            {
                functions.Add(ParseAsyncFunction());
            }
            else if (Check(TokenKind.Delegate))
            {
                delegates.Add(ParseDelegateDefinition());
            }
            else if (Check(TokenKind.Enum))
            {
                enums.Add(ParseEnumDefinition());
            }
            else if (Check(TokenKind.EnumExtension))
            {
                enumExtensions.Add(ParseEnumExtension());
            }
            // Extended Features: Module-level metadata
            else if (Check(TokenKind.Todo))
            {
                issues.Add(ParseTodoIssue());
            }
            else if (Check(TokenKind.Fixme))
            {
                issues.Add(ParseFixmeIssue());
            }
            else if (Check(TokenKind.Hack))
            {
                issues.Add(ParseHackIssue());
            }
            else if (Check(TokenKind.Assume))
            {
                assumptions.Add(ParseAssume());
            }
            else if (Check(TokenKind.Invariant))
            {
                // Use existing ParseInvariant if available, otherwise parse inline
                var invStartToken = Expect(TokenKind.Invariant);
                var invAttrs = ParseAttributes();
                var message = invAttrs["message"];
                var condition = ParseExpression();
                var invSpan = invStartToken.Span.Union(condition.Span);
                invariants.Add(new InvariantNode(invSpan, condition, message, invAttrs));
            }
            else if (Check(TokenKind.Decision))
            {
                decisions.Add(ParseDecision());
            }
            else if (Check(TokenKind.Context))
            {
                context = ParseContext();
            }
            else if (Check(TokenKind.CSharpInterop))
            {
                interopBlocks.Add(ParseCSharpInteropBlock());
            }
            else
            {
                _diagnostics.ReportUnexpectedToken(Current.Span, "USING, IFACE, CLASS, DEL, FUNC, CSHARP, or END_MODULE", Current.Kind);
                Advance();
            }
        }

        var endToken = Expect(TokenKind.EndModule);
        var endAttrs = ParseAttributes();
        var endId = AttributeHelper.InterpretEndModuleAttributes(endAttrs);

        // Validate ID matching
        if (endId != id)
        {
            _diagnostics.ReportMismatchedIdWithFix(endToken.Span, "MODULE", id, "END_MODULE", endId);
        }

        var span = startToken.Span.Union(endToken.Span);
        return new ModuleNode(span, id, moduleName, usings, interfaces, classes,
            enums, enumExtensions, delegates, functions, attrs,
            issues, assumptions, invariants, decisions, context, interopBlocks);
    }

    /// <summary>
    /// Parses a using directive.
    /// §U[System.Collections.Generic]            // using System.Collections.Generic;
    /// §U[Gen:System.Collections.Generic]        // using Gen = System.Collections.Generic;
    /// §U[static:System.Math]                    // using static System.Math;
    /// </summary>
    private UsingDirectiveNode ParseUsingDirective()
    {
        var startToken = Expect(TokenKind.Using);
        var attrs = ParseAttributes();

        // Interpret using attributes
        // Positional formats:
        // [namespace]                   -> using namespace;
        // [alias:namespace]             -> using alias = namespace;
        // [static:namespace]            -> using static namespace;
        var pos0 = attrs["_pos0"] ?? "";
        var pos1 = attrs["_pos1"];

        string @namespace;
        string? alias = null;
        bool isStatic = false;

        if (pos1 != null)
        {
            // Two-part format: [prefix:namespace]
            if (pos0.Equals("static", StringComparison.OrdinalIgnoreCase))
            {
                isStatic = true;
                @namespace = pos1;
            }
            else
            {
                // Alias format: [alias:namespace]
                alias = pos0;
                @namespace = pos1;
            }
        }
        else
        {
            // Single-part format: [namespace]
            @namespace = pos0;
        }

        if (string.IsNullOrEmpty(@namespace))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "USING", "namespace");
        }

        return new UsingDirectiveNode(startToken.Span, @namespace, alias, isStatic);
    }

    private FunctionNode ParseFunction()
    {
        var startToken = Expect(TokenKind.Func);
        var attrs = ParseAttributes();

        var (id, funcName, visibilityStr) = AttributeHelper.InterpretFuncAttributes(attrs);
        if (string.IsNullOrEmpty(id))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "FUNC", "id");
            id = "";
        }
        if (string.IsNullOrEmpty(funcName))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "FUNC", "name");
            funcName = "";
        }
        var visibility = ParseVisibility(visibilityStr);

        // NEW: Parse optional type parameters §F{...}<T, U>
        var typeParameters = ParseOptionalTypeParameterList(startToken.Span);
        var parameters = new List<ParameterNode>();
        OutputNode? output = null;
        EffectsNode? effects = null;
        var preconditions = new List<RequiresNode>();
        var postconditions = new List<EnsuresNode>();
        var body = new List<StatementNode>();

        // Extended Features: Function-level metadata
        var examples = new List<ExampleNode>();
        var issues = new List<IssueNode>();
        UsesNode? uses = null;
        UsedByNode? usedBy = null;
        var assumptions = new List<AssumeNode>();
        ComplexityNode? complexity = null;
        SinceNode? since = null;
        DeprecatedNode? deprecated = null;
        var breakingChanges = new List<BreakingChangeNode>();
        var properties = new List<PropertyTestNode>();
        LockNode? lockNode = null;
        AuthorNode? author = null;
        TaskRefNode? taskRef = null;

        // Parse optional sections before BODY
        while (!IsAtEnd && !Check(TokenKind.Body) && !Check(TokenKind.EndFunc))
        {
            if (Check(TokenKind.TypeParam))
            {
                typeParameters.Add(ParseTypeParameter());
            }
            else if (Check(TokenKind.Where))
            {
                // WHERE clauses add constraints to existing type parameters
                ParseWhereClause(typeParameters);
            }
            else if (Check(TokenKind.In))
            {
                parameters.Add(ParseParameter());
            }
            else if (Check(TokenKind.Out))
            {
                output = ParseOutput();
            }
            else if (Check(TokenKind.Effects))
            {
                effects = ParseEffects();
            }
            else if (Check(TokenKind.Requires))
            {
                preconditions.Add(ParseRequires());
            }
            else if (Check(TokenKind.Ensures))
            {
                postconditions.Add(ParseEnsures());
            }
            // Extended Features: Function-level metadata
            else if (Check(TokenKind.Example))
            {
                examples.Add(ParseExample());
            }
            else if (Check(TokenKind.Todo))
            {
                issues.Add(ParseTodoIssue());
            }
            else if (Check(TokenKind.Fixme))
            {
                issues.Add(ParseFixmeIssue());
            }
            else if (Check(TokenKind.Hack))
            {
                issues.Add(ParseHackIssue());
            }
            else if (Check(TokenKind.Uses))
            {
                uses = ParseUses();
            }
            else if (Check(TokenKind.UsedBy))
            {
                usedBy = ParseUsedBy();
            }
            else if (Check(TokenKind.Assume))
            {
                assumptions.Add(ParseAssume());
            }
            else if (Check(TokenKind.Complexity))
            {
                complexity = ParseComplexity();
            }
            else if (Check(TokenKind.Since))
            {
                since = ParseSince();
            }
            else if (Check(TokenKind.Deprecated))
            {
                deprecated = ParseDeprecated();
            }
            else if (Check(TokenKind.Breaking))
            {
                breakingChanges.Add(ParseBreaking());
            }
            else if (Check(TokenKind.PropertyTest))
            {
                properties.Add(ParsePropertyTest());
            }
            else if (Check(TokenKind.Lock))
            {
                lockNode = ParseLock();
            }
            else if (Check(TokenKind.AgentAuthor))
            {
                author = ParseAuthor();
            }
            else if (Check(TokenKind.TaskRef))
            {
                taskRef = ParseTaskRef();
            }
            else
            {
                break;
            }
        }

        // Parse BODY - either explicit §BODY/§END_BODY or implicit (no BODY markers)
        if (Check(TokenKind.Body))
        {
            // Explicit §BODY ... §END_BODY
            body = ParseBody();
        }
        else if (!Check(TokenKind.EndFunc))
        {
            // Implicit body - parse statements until §/F
            body = ParseImplicitBody();
        }

        var endToken = Expect(TokenKind.EndFunc);
        var endAttrs = ParseAttributes();
        var endId = AttributeHelper.InterpretEndFuncAttributes(endAttrs);

        // Validate ID matching
        if (endId != id)
        {
            _diagnostics.ReportMismatchedIdWithFix(endToken.Span, "FUNC", id, "END_FUNC", endId);
        }

        var span = startToken.Span.Union(endToken.Span);
        return new FunctionNode(span, id, funcName, visibility, typeParameters, parameters, output, effects,
            preconditions, postconditions, body, attrs,
            examples, issues, uses, usedBy, assumptions, complexity, since, deprecated, breakingChanges,
            properties, lockNode, author, taskRef);
    }

    /// <summary>
    /// Parses an async function definition.
    /// §AF{id:name:visibility}
    ///   §I{type:name} ...
    ///   §O{type}
    ///   ... body ...
    /// §/AF{id}
    /// </summary>
    private FunctionNode ParseAsyncFunction()
    {
        var startToken = Expect(TokenKind.AsyncFunc);
        var attrs = ParseAttributes();

        var (id, funcName, visibilityStr) = AttributeHelper.InterpretFuncAttributes(attrs);
        if (string.IsNullOrEmpty(id))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "AF", "id");
            id = "";
        }
        if (string.IsNullOrEmpty(funcName))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "AF", "name");
            funcName = "";
        }
        var visibility = ParseVisibility(visibilityStr);

        // Parse optional type parameters §AF{...}<T, U>
        var typeParameters = ParseOptionalTypeParameterList(startToken.Span);
        var parameters = new List<ParameterNode>();
        OutputNode? output = null;
        EffectsNode? effects = null;
        var preconditions = new List<RequiresNode>();
        var postconditions = new List<EnsuresNode>();
        var body = new List<StatementNode>();

        // Extended Features: Function-level metadata
        var examples = new List<ExampleNode>();
        var issues = new List<IssueNode>();
        UsesNode? uses = null;
        UsedByNode? usedBy = null;
        var assumptions = new List<AssumeNode>();
        ComplexityNode? complexity = null;
        SinceNode? since = null;
        DeprecatedNode? deprecated = null;
        var breakingChanges = new List<BreakingChangeNode>();
        var properties = new List<PropertyTestNode>();
        LockNode? lockNode = null;
        AuthorNode? author = null;
        TaskRefNode? taskRef = null;

        // Parse optional sections before BODY
        while (!IsAtEnd && !Check(TokenKind.Body) && !Check(TokenKind.EndAsyncFunc))
        {
            if (Check(TokenKind.TypeParam))
            {
                typeParameters.Add(ParseTypeParameter());
            }
            else if (Check(TokenKind.Where))
            {
                ParseWhereClause(typeParameters);
            }
            else if (Check(TokenKind.In))
            {
                parameters.Add(ParseParameter());
            }
            else if (Check(TokenKind.Out))
            {
                output = ParseOutput();
            }
            else if (Check(TokenKind.Effects))
            {
                effects = ParseEffects();
            }
            else if (Check(TokenKind.Requires))
            {
                preconditions.Add(ParseRequires());
            }
            else if (Check(TokenKind.Ensures))
            {
                postconditions.Add(ParseEnsures());
            }
            else if (Check(TokenKind.Example))
            {
                examples.Add(ParseExample());
            }
            else if (Check(TokenKind.Todo))
            {
                issues.Add(ParseTodoIssue());
            }
            else if (Check(TokenKind.Fixme))
            {
                issues.Add(ParseFixmeIssue());
            }
            else if (Check(TokenKind.Hack))
            {
                issues.Add(ParseHackIssue());
            }
            else if (Check(TokenKind.Uses))
            {
                uses = ParseUses();
            }
            else if (Check(TokenKind.UsedBy))
            {
                usedBy = ParseUsedBy();
            }
            else if (Check(TokenKind.Assume))
            {
                assumptions.Add(ParseAssume());
            }
            else if (Check(TokenKind.Complexity))
            {
                complexity = ParseComplexity();
            }
            else if (Check(TokenKind.Since))
            {
                since = ParseSince();
            }
            else if (Check(TokenKind.Deprecated))
            {
                deprecated = ParseDeprecated();
            }
            else if (Check(TokenKind.Breaking))
            {
                breakingChanges.Add(ParseBreaking());
            }
            else if (Check(TokenKind.PropertyTest))
            {
                properties.Add(ParsePropertyTest());
            }
            else if (Check(TokenKind.Lock))
            {
                lockNode = ParseLock();
            }
            else if (Check(TokenKind.AgentAuthor))
            {
                author = ParseAuthor();
            }
            else if (Check(TokenKind.TaskRef))
            {
                taskRef = ParseTaskRef();
            }
            else
            {
                break;
            }
        }

        // Parse BODY - either explicit §BODY/§END_BODY or implicit
        if (Check(TokenKind.Body))
        {
            body = ParseBody();
        }
        else if (!Check(TokenKind.EndAsyncFunc))
        {
            body = ParseImplicitBody();
        }

        var endToken = Expect(TokenKind.EndAsyncFunc);
        var endAttrs = ParseAttributes();
        var endId = AttributeHelper.InterpretEndFuncAttributes(endAttrs);

        if (endId != id)
        {
            _diagnostics.ReportMismatchedIdWithFix(endToken.Span, "AF", id, "END_AF", endId);
        }

        var span = startToken.Span.Union(endToken.Span);
        return new FunctionNode(span, id, funcName, visibility, typeParameters, parameters, output, effects,
            preconditions, postconditions, body, attrs,
            examples, issues, uses, usedBy, assumptions, complexity, since, deprecated, breakingChanges,
            properties, lockNode, author, taskRef, isAsync: true);
    }

    private ParameterNode ParseParameter()
    {
        var startToken = Expect(TokenKind.In);
        var attrs = ParseAttributes();

        var (typeName, paramName, semantic) = AttributeHelper.InterpretInputAttributes(attrs);
        if (string.IsNullOrEmpty(paramName))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "IN", "name");
            paramName = "";
        }
        if (string.IsNullOrEmpty(typeName))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "IN", "type");
            typeName = "";
        }

        // Add semantic to attrs if present
        if (!string.IsNullOrEmpty(semantic))
        {
            attrs.Add("semantic", semantic);
        }

        // Parse parameter modifier from semantic/pos2 position (this, ref, out, in, params)
        var modifier = ParameterModifier.None;
        if (!string.IsNullOrEmpty(semantic))
        {
            if (semantic.Contains("this", StringComparison.OrdinalIgnoreCase)) modifier |= ParameterModifier.This;
            if (semantic.Contains("ref", StringComparison.OrdinalIgnoreCase)) modifier |= ParameterModifier.Ref;
            if (semantic.Contains("out", StringComparison.OrdinalIgnoreCase)) modifier |= ParameterModifier.Out;
            if (semantic.Contains("in", StringComparison.OrdinalIgnoreCase) && !semantic.Contains("this", StringComparison.OrdinalIgnoreCase)) modifier |= ParameterModifier.In;
            if (semantic.Contains("params", StringComparison.OrdinalIgnoreCase)) modifier |= ParameterModifier.Params;
        }

        var csharpAttrs = ParseCSharpAttributes();
        return new ParameterNode(startToken.Span, paramName, typeName, modifier, attrs, csharpAttrs);
    }

    private OutputNode ParseOutput()
    {
        var startToken = Expect(TokenKind.Out);
        var attrs = ParseAttributes();

        // Interpret positional attributes
        var typeName = AttributeHelper.InterpretOutputAttributes(attrs);
        if (string.IsNullOrEmpty(typeName))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "OUT", "type");
            typeName = "";
        }

        return new OutputNode(startToken.Span, typeName);
    }

    private EffectsNode ParseEffects()
    {
        var startToken = Expect(TokenKind.Effects);
        var attrs = ParseAttributes();

        // Interpret positional attributes
        var effects = AttributeHelper.InterpretEffectsAttributes(attrs);

        return new EffectsNode(startToken.Span, effects);
    }

    private RequiresNode ParseRequires()
    {
        var startToken = Expect(TokenKind.Requires);
        var attrs = ParseAttributes();
        // v2 syntax: message is first positional §Q{"message"} (condition)
        var message = attrs["_pos0"];

        var condition = ParseExpression();

        var span = startToken.Span.Union(condition.Span);
        return new RequiresNode(span, condition, message, attrs);
    }

    private EnsuresNode ParseEnsures()
    {
        var startToken = Expect(TokenKind.Ensures);
        var attrs = ParseAttributes();
        // v2 syntax: message is first positional §S{"message"} (condition)
        var message = attrs["_pos0"];

        var condition = ParseExpression();

        var span = startToken.Span.Union(condition.Span);
        return new EnsuresNode(span, condition, message, attrs);
    }

    private List<StatementNode> ParseBody()
    {
        Expect(TokenKind.Body);

        var statements = new List<StatementNode>();

        while (!IsAtEnd && !Check(TokenKind.EndBody))
        {
            var statement = ParseStatement();
            if (statement != null)
            {
                statements.Add(statement);
            }
        }

        Expect(TokenKind.EndBody);

        return statements;
    }

    /// <summary>
    /// Parses an implicit body (no §BODY markers) - statements until §/F
    /// </summary>
    private List<StatementNode> ParseImplicitBody()
    {
        var statements = new List<StatementNode>();

        while (!IsAtEnd && !Check(TokenKind.EndFunc) && !Check(TokenKind.EndAsyncFunc))
        {
            var statement = ParseStatement();
            if (statement != null)
            {
                statements.Add(statement);
            }
        }

        return statements;
    }

    private StatementNode? ParseStatement()
    {
        if (Check(TokenKind.Call))
        {
            return ParseCallStatement();
        }
        else if (Check(TokenKind.Return))
        {
            return ParseReturnStatement();
        }
        else if (Check(TokenKind.For))
        {
            return ParseForStatement();
        }
        else if (Check(TokenKind.While))
        {
            return ParseWhileStatement();
        }
        else if (Check(TokenKind.Do))
        {
            return ParseDoWhileStatement();
        }
        else if (Check(TokenKind.If))
        {
            return ParseIfStatement();
        }
        else if (Check(TokenKind.Bind))
        {
            return ParseBindStatement();
        }
        else if (Check(TokenKind.Match))
        {
            return ParseMatchStatement();
        }
        else if (Check(TokenKind.Foreach))
        {
            return ParseForeachStatement();
        }
        else if (Check(TokenKind.Assign))
        {
            return ParseAssignmentStatement();
        }
        else if (Check(TokenKind.Try))
        {
            return ParseTryStatement();
        }
        else if (Check(TokenKind.Use))
        {
            return ParseUsingStatement();
        }
        else if (Check(TokenKind.Throw))
        {
            return ParseThrowStatement();
        }
        else if (Check(TokenKind.Rethrow))
        {
            return ParseRethrowStatement();
        }
        else if (Check(TokenKind.Subscribe))
        {
            return ParseEventSubscribe();
        }
        else if (Check(TokenKind.Unsubscribe))
        {
            return ParseEventUnsubscribe();
        }
        else if (Check(TokenKind.Break))
        {
            return ParseBreakStatement();
        }
        else if (Check(TokenKind.Continue))
        {
            return ParseContinueStatement();
        }
        else if (Check(TokenKind.Yield))
        {
            return ParseYieldReturnStatement();
        }
        else if (Check(TokenKind.YieldBreak))
        {
            return ParseYieldBreakStatement();
        }
        // Print aliases
        else if (Check(TokenKind.Print))
        {
            return ParsePrintStatement(isWriteLine: true);
        }
        else if (Check(TokenKind.PrintF))
        {
            return ParsePrintStatement(isWriteLine: false);
        }
        // Collection operations (statements)
        else if (Check(TokenKind.List))
        {
            return ParseListCreationStatement();
        }
        else if (Check(TokenKind.Dict))
        {
            return ParseDictionaryCreationStatement();
        }
        else if (Check(TokenKind.HashSet))
        {
            return ParseSetCreationStatement();
        }
        else if (Check(TokenKind.Array))
        {
            return ParseArrayCreationStatement();
        }
        else if (Check(TokenKind.Push) || Check(TokenKind.Add))
        {
            return ParseCollectionPush();
        }
        else if (Check(TokenKind.Put))
        {
            return ParseDictionaryPut();
        }
        else if (Check(TokenKind.Remove))
        {
            return ParseCollectionRemove();
        }
        else if (Check(TokenKind.SetIndex))
        {
            return ParseCollectionSetIndex();
        }
        else if (Check(TokenKind.Clear))
        {
            return ParseCollectionClear();
        }
        else if (Check(TokenKind.Insert))
        {
            return ParseCollectionInsert();
        }
        else if (Check(TokenKind.EachKV))
        {
            return ParseDictionaryForeach();
        }
        // Raw C# passthrough
        else if (Check(TokenKind.RawCSharp))
        {
            return ParseRawCSharpStatement();
        }
        // Expression statement: bare lisp expression like (inc x), (post-dec y)
        else if (Check(TokenKind.OpenParen))
        {
            var expr = ParseExpression();
            return new ExpressionStatementNode(expr.Span, expr);
        }

        _diagnostics.ReportUnexpectedToken(Current.Span, "statement", Current.Kind);
        Advance();
        return null;
    }

    /// <summary>
    /// Parses §P or §Pf print statements.
    /// §P expression  -> Console.WriteLine(expression)
    /// §Pf expression -> Console.Write(expression)
    /// </summary>
    private PrintStatementNode ParsePrintStatement(bool isWriteLine)
    {
        var startToken = Advance(); // consume §P or §Pf

        // Parse the expression to print
        var expression = ParseExpression();

        var span = startToken.Span.Union(expression.Span);
        return new PrintStatementNode(span, expression, isWriteLine);
    }

    private CallStatementNode ParseCallStatement()
    {
        var startToken = Expect(TokenKind.Call);
        var attrs = ParseAttributes();

        // Interpret call attributes
        var (target, fallible) = AttributeHelper.InterpretCallAttributes(attrs);
        if (string.IsNullOrEmpty(target))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "CALL", "target");
        }

        var arguments = new List<ExpressionNode>();

        // Support implicit closing - if a single expression follows without §A, treat it as the argument
        // §C[Console.WriteLine] "Hello" is equivalent to §C[Console.WriteLine] §A "Hello" §/C
        if (IsExpressionStart() && !Check(TokenKind.Arg))
        {
            // Single expression argument without §A prefix - implicit closing
            arguments.Add(ParseExpression());
            var span = startToken.Span.Union(arguments[0].Span);
            return new CallStatementNode(span, target, fallible, arguments, attrs);
        }

        // Standard format with explicit §A and §/C
        while (!IsAtEnd && !Check(TokenKind.EndCall))
        {
            if (Check(TokenKind.Arg))
            {
                arguments.Add(ParseArgument());
            }
            else
            {
                break;
            }
        }

        var endToken = Expect(TokenKind.EndCall);
        var span2 = startToken.Span.Union(endToken.Span);

        return new CallStatementNode(span2, target, fallible, arguments, attrs);
    }

    private ExpressionNode ParseArgument()
    {
        Expect(TokenKind.Arg);

        var saved = _insideArgContext;
        _insideArgContext = true;
        try
        {
            return ParseExpression();
        }
        finally
        {
            _insideArgContext = saved;
        }
    }

    private ReturnStatementNode ParseReturnStatement()
    {
        var startToken = Expect(TokenKind.Return);

        ExpressionNode? expression = null;

        // Check if there's an expression following RETURN
        if (IsExpressionStart())
        {
            expression = ParseExpression();
        }

        var span = expression != null ? startToken.Span.Union(expression.Span) : startToken.Span;
        return new ReturnStatementNode(span, expression);
    }

    private bool IsExpressionStart()
    {
        return Current.Kind is TokenKind.IntLiteral
            or TokenKind.StrLiteral
            or TokenKind.BoolLiteral
            or TokenKind.FloatLiteral
            or TokenKind.DecimalLiteral
            or TokenKind.Identifier
            // Lisp-style expression
            or TokenKind.OpenParen
            // Phase 2: Control Flow - IF as conditional expression
            or TokenKind.If
            // Phase 3: Type System
            or TokenKind.Some
            or TokenKind.None
            or TokenKind.Ok
            or TokenKind.Err
            or TokenKind.Match
            or TokenKind.Record
            // Phase 6: Arrays
            or TokenKind.Array
            or TokenKind.Index
            or TokenKind.Length
            // Phase 6 Extended: Collections
            or TokenKind.List
            or TokenKind.Dict
            or TokenKind.HashSet
            or TokenKind.Has
            or TokenKind.Count
            // Phase 7: Generics
            or TokenKind.Generic
            // Phase 8: Classes
            or TokenKind.New
            or TokenKind.AnonymousObject
            or TokenKind.This
            or TokenKind.Base
            or TokenKind.Call  // Call expressions (§C[...])
            // Phase 11: Lambdas
            or TokenKind.Lambda
            // Phase 12: Async/Await
            or TokenKind.Await
            // Phase 9: String Interpolation and Modern Operators
            or TokenKind.Interpolate
            or TokenKind.NullCoalesce
            or TokenKind.NullConditional
            or TokenKind.RangeOp
            or TokenKind.IndexEnd
            // Phase 10: Advanced Patterns
            or TokenKind.With;
    }

    private ExpressionNode ParseExpression()
    {
        return Current.Kind switch
        {
            TokenKind.IntLiteral => ParseIntLiteral(),
            TokenKind.StrLiteral => ParseStringLiteral(),
            TokenKind.BoolLiteral => ParseBoolLiteral(),
            TokenKind.FloatLiteral => ParseFloatLiteral(),
            TokenKind.DecimalLiteral => ParseDecimalLiteral(),
            TokenKind.Identifier => ParseReference(),
            // Lisp-style expression: (op args...) or inline lambda: () → body
            TokenKind.OpenParen => ParseParenExpressionOrInlineLambda(),
            // Collection/array initializer: {elem1, elem2, ...}
            TokenKind.OpenBrace => ParseCollectionInitializer(),
            // Phase 2: Control Flow - IF as conditional expression
            TokenKind.If => ParseIfExpression(),
            // Phase 3: Type System
            TokenKind.Some => ParseSomeExpression(),
            TokenKind.None => ParseNoneExpression(),
            TokenKind.Ok => ParseOkExpression(),
            TokenKind.Err => ParseErrExpression(),
            TokenKind.Match => ParseMatchExpression(),
            TokenKind.Record => ParseRecordCreation(),
            // Phase 6: Arrays
            TokenKind.Array => ParseArrayCreation(),
            TokenKind.Index => ParseArrayAccess(),
            TokenKind.Length => ParseArrayLength(),
            // Phase 6 Extended: Collections
            TokenKind.List => ParseListCreation(),
            TokenKind.Dict => ParseDictionaryCreation(),
            TokenKind.HashSet => ParseSetCreation(),
            TokenKind.Has => ParseCollectionContains(),
            TokenKind.Count => ParseCollectionCount(),
            // Phase 7: Generics
            TokenKind.Generic => ParseGenericType(),
            // Phase 8: Classes
            TokenKind.New => ParseNewExpression(),
            TokenKind.AnonymousObject => ParseAnonymousObjectCreation(),
            TokenKind.This => ParseThisExpression(),
            TokenKind.Base => ParseBaseExpression(),
            TokenKind.Call => ParseCallExpression(),
            // Phase 11: Lambdas
            TokenKind.Lambda => ParseLambdaExpression(),
            // Phase 12: Async/Await
            TokenKind.Await => ParseAwaitExpression(),
            // Phase 9: String Interpolation and Modern Operators
            TokenKind.Interpolate => ParseInterpolatedString(),
            TokenKind.NullCoalesce => ParseNullCoalesce(),
            TokenKind.NullConditional => ParseNullConditional(),
            TokenKind.RangeOp => ParseRangeExpression(),
            TokenKind.IndexEnd => ParseIndexFromEnd(),
            // Phase 10: Advanced Patterns
            TokenKind.With => ParseWithExpression(),
            _ => throw new InvalidOperationException($"Unexpected token {Current.Kind}")
        };
    }

    /// <summary>
    /// Parses a collection/array initializer: {elem1, elem2, ...}
    /// </summary>
    private ExpressionNode ParseCollectionInitializer()
    {
        var startToken = Expect(TokenKind.OpenBrace);
        var elements = new List<ExpressionNode>();

        // Parse elements until closing brace
        while (!IsAtEnd && !Check(TokenKind.CloseBrace))
        {
            var element = ParseExpression();
            elements.Add(element);

            // Elements are separated by commas
            if (Check(TokenKind.Comma))
            {
                Advance();
            }
            else if (!Check(TokenKind.CloseBrace))
            {
                // If there's no comma and no closing brace, something's wrong
                break;
            }
        }

        var endToken = Expect(TokenKind.CloseBrace);
        var span = startToken.Span.Union(endToken.Span);

        // Create an ArrayCreationNode with inferred type
        return new ArrayCreationNode(
            span,
            "arr_init",
            "arr_init",
            "any", // Type will be inferred by context
            null,  // No explicit size
            elements,
            new AttributeCollection());
    }

    /// <summary>
    /// Parses either a Lisp-style expression or an inline lambda.
    /// - Inline lambda: () → body or (param) → body
    /// - Lisp expression: (op args...)
    /// </summary>
    private ExpressionNode ParseParenExpressionOrInlineLambda()
    {
        // Look ahead to determine if this is an inline lambda: () → or (params) →
        // We need to see if after the closing paren there's an arrow
        var savedPosition = _position;
        var startToken = Expect(TokenKind.OpenParen);

        // Check for empty parens followed by arrow: () →
        if (Check(TokenKind.CloseParen))
        {
            Advance(); // consume )
            if (Check(TokenKind.Arrow))
            {
                // This is an inline lambda with no parameters
                Advance(); // consume →
                var (exprBody, stmtBody, endSpan) = ParseLambdaBody();
                var span = startToken.Span.Union(endSpan);
                return new LambdaExpressionNode(
                    span,
                    "inline",
                    new List<LambdaParameterNode>(),
                    null, // effects
                    false, // not async
                    exprBody, // expressionBody
                    stmtBody, // statementBody
                    new AttributeCollection());
            }
            // Not a lambda, restore and parse as Lisp (though empty Lisp is likely an error)
            _position = savedPosition;
            return ParseLispExpression();
        }

        // Check for single parameter: (param) → or (type:param) →
        if (Check(TokenKind.Identifier))
        {
            var firstToken = Advance();
            var paramName = firstToken.Text;
            string? paramType = null;

            // Check for typed parameter: type:name
            if (Check(TokenKind.Colon))
            {
                Advance(); // consume :
                paramType = paramName;
                paramName = Expect(TokenKind.Identifier).Text;
            }

            if (Check(TokenKind.CloseParen))
            {
                Advance(); // consume )
                if (Check(TokenKind.Arrow))
                {
                    // This is an inline lambda with one parameter
                    Advance(); // consume →
                    var (exprBody, stmtBody, endSpan) = ParseLambdaBody();
                    var span = startToken.Span.Union(endSpan);
                    var param = new LambdaParameterNode(firstToken.Span, paramName, paramType);
                    return new LambdaExpressionNode(
                        span,
                        "inline",
                        new List<LambdaParameterNode> { param },
                        null, // effects
                        false, // not async
                        exprBody, // expressionBody
                        stmtBody, // statementBody
                        new AttributeCollection());
                }
            }
            // Not a lambda, restore and parse as Lisp
            _position = savedPosition;
        }
        else
        {
            // Not an identifier, restore position (we already consumed the open paren)
            _position = savedPosition;
        }

        return ParseLispExpression();
    }

    /// <summary>
    /// Parses a lambda body after the arrow (→).
    /// Can be either an expression or a statement block.
    /// Returns (expressionBody, statementBody, endSpan).
    /// </summary>
    private (ExpressionNode?, IReadOnlyList<StatementNode>?, TextSpan) ParseLambdaBody()
    {
        // Check for statement block: { ... }
        if (Check(TokenKind.OpenBrace))
        {
            var statements = new List<StatementNode>();
            Advance(); // consume {

            // Parse statements until closing brace
            while (!IsAtEnd && !Check(TokenKind.CloseBrace))
            {
                var stmt = ParseStatement();
                if (stmt != null)
                {
                    statements.Add(stmt);
                }
            }

            var endToken = Expect(TokenKind.CloseBrace);
            return (null, statements, endToken.Span);
        }
        else
        {
            // Expression body
            var body = ParseExpression();
            return (body, null, body.Span);
        }
    }

    /// <summary>
    /// Parses a Lisp-style prefix expression: (op arg1 arg2 ...)
    /// Examples: (+ 1 2), (== x 0), (% i 15), (! flag), (- x)
    /// Also handles quantifiers: (forall ((var type)) body), (exists ((var type)) body)
    /// And implication: (-> antecedent consequent)
    /// </summary>
    private ExpressionNode ParseLispExpression()
    {
        var startToken = Expect(TokenKind.OpenParen);

        // Get the operator
        var (opKind, opText, opSpan) = ParseLispOperator();

        // Handle quantifiers
        if (opText == "forall" || opText == "exists")
        {
            return ParseQuantifierExpression(startToken, opText);
        }

        // Handle implication
        if (opText == "->")
        {
            return ParseImplicationExpression(startToken);
        }

        // Handle typeof: (typeof TypeName)
        if (opText == "typeof")
        {
            var typeName = ParseLispTypeName();
            var typeofEnd = Expect(TokenKind.CloseParen);
            return new TypeOfExpressionNode(startToken.Span.Union(typeofEnd.Span), typeName);
        }

        // Handle is/as with special type parsing to support generics
        if (opText == "is")
        {
            return ParseLispIsExpression(startToken);
        }
        if (opText == "as")
        {
            return ParseLispAsExpression(startToken);
        }

        // Parse arguments until we hit CloseParen
        var args = new List<ExpressionNode>();
        while (!Check(TokenKind.CloseParen) && !IsAtEnd)
        {
            args.Add(ParseLispArgument());
        }

        var endToken = Expect(TokenKind.CloseParen);
        var span = startToken.Span.Union(endToken.Span);

        // Handle ternary conditional: (? cond then else)
        if (opText == "?" && args.Count == 3)
        {
            return new ConditionalExpressionNode(span, args[0], args[1], args[2]);
        }

        // Handle null-coalescing: (?? x "default")
        if (opText == "??" && args.Count == 2)
        {
            return new NullCoalesceNode(span, args[0], args[1]);
        }
        if (opText == "??")
        {
            _diagnostics.ReportError(span, DiagnosticCode.OperatorArgumentCount,
                $"Null-coalescing '??' requires exactly 2 operands, got {args.Count}");
            return args.Count > 0 ? args[0] : new IntLiteralNode(span, 0);
        }

        // Determine if this is unary or binary based on argument count and operator
        if (args.Count == 1 && IsUnaryOperator(opKind))
        {
            var unaryOp = GetUnaryOperator(opKind, opText);
            if (unaryOp.HasValue)
            {
                return new UnaryOperationNode(span, unaryOp.Value, args[0]);
            }
        }

        if (args.Count >= 2)
        {
            var binaryOp = BinaryOperatorExtensions.FromString(opText);
            if (binaryOp.HasValue)
            {
                // For more than 2 arguments, chain them: (+ a b c) => ((a + b) + c)
                var result = args[0];
                for (int i = 1; i < args.Count; i++)
                {
                    result = new BinaryOperationNode(span, binaryOp.Value, result, args[i]);
                }
                return result;
            }
        }

        // Fallback: if single argument and can be binary op with implicit self
        if (args.Count == 1)
        {
            var binaryOp = BinaryOperatorExtensions.FromString(opText);
            if (binaryOp.HasValue)
            {
                // This is likely an error - binary op with only one argument
                _diagnostics.ReportError(span, DiagnosticCode.OperatorArgumentCount,
                    $"Binary operator '{opText}' requires at least two operands");
                return args[0];
            }
        }

        // If we have exactly 2 args, try to interpret as binary
        if (args.Count == 2)
        {
            var binaryOp = BinaryOperatorExtensions.FromString(opText);
            if (binaryOp.HasValue)
            {
                return new BinaryOperationNode(span, binaryOp.Value, args[0], args[1]);
            }
        }

        // Try string operations
        var stringOp = StringOpExtensions.FromString(opText);
        if (stringOp.HasValue)
        {
            // Extract keyword arguments (comparison modes) from the end of the args list
            StringComparisonMode? comparisonMode = null;
            var nonKeywordArgs = args;

            if (args.Count > 0 && args[^1] is KeywordArgNode keywordArg)
            {
                comparisonMode = StringComparisonModeExtensions.FromKeyword(keywordArg.Name);
                if (comparisonMode == null)
                {
                    _diagnostics.ReportError(keywordArg.Span, DiagnosticCode.InvalidComparisonMode,
                        $"Unknown comparison mode ':{keywordArg.Name}'. Valid modes: ordinal, ignore-case, invariant, invariant-ignore-case");
                }
                else if (!StringOperationNode.SupportsComparisonMode(stringOp.Value))
                {
                    _diagnostics.ReportError(keywordArg.Span, DiagnosticCode.InvalidComparisonMode,
                        $"Operation '{opText}' does not support comparison modes");
                    comparisonMode = null;
                }
                nonKeywordArgs = args.Take(args.Count - 1).ToList();
            }

            // Handle substr disambiguation: 2 args = SubstringFrom, 3 args = Substring
            var finalOp = stringOp.Value;
            if (stringOp.Value == StringOp.Substring && nonKeywordArgs.Count == 2)
            {
                finalOp = StringOp.SubstringFrom;
            }

            // Validate argument count
            // Special case for substr: it accepts 2 or 3 args (SubstringFrom or Substring)
            int minArgs, maxArgs;
            if (stringOp.Value == StringOp.Substring)
            {
                minArgs = 2; // substr can have 2 args (SubstringFrom)
                maxArgs = 3; // or 3 args (Substring)
            }
            else
            {
                minArgs = finalOp.GetMinArgCount();
                maxArgs = finalOp.GetMaxArgCount();
            }

            if (nonKeywordArgs.Count < minArgs)
            {
                var example = OperatorSuggestions.GetStringOpExample(opText);
                _diagnostics.ReportError(span, DiagnosticCode.OperatorArgumentCount,
                    $"String operation '{opText}' requires at least {minArgs} argument(s), got {nonKeywordArgs.Count}. Example: {example}");
            }
            else if (nonKeywordArgs.Count > maxArgs)
            {
                var example = OperatorSuggestions.GetStringOpExample(opText);
                _diagnostics.ReportError(span, DiagnosticCode.OperatorArgumentCount,
                    $"String operation '{opText}' accepts at most {maxArgs} argument(s), got {nonKeywordArgs.Count}. Example: {example}");
            }

            return new StringOperationNode(span, finalOp, nonKeywordArgs, comparisonMode);
        }

        // Try char operations
        var charOp = CharOpExtensions.FromString(opText);
        if (charOp.HasValue)
        {
            var minArgs = charOp.Value.GetMinArgCount();
            var maxArgs = charOp.Value.GetMaxArgCount();

            if (args.Count < minArgs)
            {
                var example = OperatorSuggestions.GetCharOpExample(opText);
                _diagnostics.ReportError(span, DiagnosticCode.OperatorArgumentCount,
                    $"Char operation '{opText}' requires at least {minArgs} argument(s), got {args.Count}. Example: {example}");
            }
            else if (args.Count > maxArgs)
            {
                var example = OperatorSuggestions.GetCharOpExample(opText);
                _diagnostics.ReportError(span, DiagnosticCode.OperatorArgumentCount,
                    $"Char operation '{opText}' accepts at most {maxArgs} argument(s), got {args.Count}. Example: {example}");
            }

            // Validate char-lit argument is a single-character string literal
            if (charOp.Value == CharOp.CharLiteral && args.Count == 1)
            {
                if (args[0] is StringLiteralNode strLit)
                {
                    // Value is already unescaped by the lexer ("\\" → \, "\n" → newline, etc.)
                    // so a single character is always length 1
                    if (strLit.Value.Length != 1)
                    {
                        _diagnostics.ReportError(span, DiagnosticCode.InvalidCharLiteral,
                            $"char-lit requires a single character, got \"{strLit.Value}\" ({strLit.Value.Length} characters). Example: (char-lit \"Y\")");
                    }
                }
                else
                {
                    _diagnostics.ReportError(span, DiagnosticCode.InvalidCharLiteral,
                        "char-lit requires a string literal argument. Example: (char-lit \"Y\")");
                }
            }

            return new CharOperationNode(span, charOp.Value, args);
        }

        // Try StringBuilder operations
        var sbOp = StringBuilderOpExtensions.FromString(opText);
        if (sbOp.HasValue)
        {
            var minArgs = sbOp.Value.GetMinArgCount();
            var maxArgs = sbOp.Value.GetMaxArgCount();

            if (args.Count < minArgs)
            {
                var example = OperatorSuggestions.GetStringBuilderOpExample(opText);
                _diagnostics.ReportError(span, DiagnosticCode.OperatorArgumentCount,
                    $"StringBuilder operation '{opText}' requires at least {minArgs} argument(s), got {args.Count}. Example: {example}");
            }
            else if (args.Count > maxArgs)
            {
                var example = OperatorSuggestions.GetStringBuilderOpExample(opText);
                _diagnostics.ReportError(span, DiagnosticCode.OperatorArgumentCount,
                    $"StringBuilder operation '{opText}' accepts at most {maxArgs} argument(s), got {args.Count}. Example: {example}");
            }

            return new StringBuilderOperationNode(span, sbOp.Value, args);
        }

        // Try type operations
        var typeOp = TypeOpExtensions.FromString(opText);
        if (typeOp.HasValue)
        {
            if (args.Count != 2)
            {
                var example = OperatorSuggestions.GetTypeOpExample(opText);
                _diagnostics.ReportError(span, DiagnosticCode.OperatorArgumentCount,
                    $"Type operation '{opText}' requires exactly 2 arguments, got {args.Count}. Example: {example}");
                return args.Count > 0 ? args[0] : new IntLiteralNode(span, 0);
            }

            // Extract type name and operand based on operator
            // cast: (cast Type expr) — type is args[0], operand is args[1]
            // is/as: (is expr Type) / (as expr Type) — operand is args[0], type is args[1]
            var (typeArg, operandArg) = typeOp.Value == TypeOp.Cast
                ? (args[0], args[1])
                : (args[1], args[0]);

            // Type argument must be a simple identifier (ReferenceNode)
            if (typeArg is not ReferenceNode typeRef)
            {
                _diagnostics.ReportError(typeArg.Span, DiagnosticCode.ExpectedTypeName,
                    $"Expected a type name, got {typeArg.GetType().Name}");
                return operandArg;
            }

            return new TypeOperationNode(span, typeOp.Value, operandArg, typeRef.Name);
        }

        // Unknown operator - provide helpful suggestions
        var csharpHint = OperatorSuggestions.GetCSharpHint(opText);
        var suggestion = OperatorSuggestions.FindSimilarOperator(opText);

        if (csharpHint != null)
        {
            // C# construct with Calor alternative
            _diagnostics.ReportError(opSpan, DiagnosticCode.InvalidOperator,
                $"Unknown operator '{opText}'. {csharpHint}");
        }
        else if (suggestion != null)
        {
            // Typo - suggest the correct operator
            // Use opSpan (the operator token's span) for precise fix positioning
            var filePath = _diagnostics.CurrentFilePath ?? "";
            var fix = new Diagnostics.SuggestedFix(
                $"Replace '{opText}' with '{suggestion}'",
                Diagnostics.TextEdit.Replace(filePath, opSpan.Line, opSpan.Column, opSpan.Line, opSpan.Column + opText.Length, suggestion));
            _diagnostics.ReportErrorWithFix(opSpan, DiagnosticCode.InvalidOperator,
                $"Unknown operator '{opText}'. Did you mean '{suggestion}'?", fix);
        }
        else
        {
            // No suggestion - show categories of valid operators
            _diagnostics.ReportError(opSpan, DiagnosticCode.InvalidOperator,
                $"Unknown operator '{opText}'. Valid operators include: {OperatorSuggestions.GetOperatorCategories()}");
        }

        return args.Count > 0 ? args[0] : new IntLiteralNode(span, 0);
    }

    private (TokenKind kind, string text, TextSpan span) ParseLispOperator()
    {
        var token = Current;
        var text = token.Text;
        var span = token.Span;

        switch (token.Kind)
        {
            case TokenKind.Plus:
                Advance();
                return (TokenKind.Plus, "+", span);
            case TokenKind.Minus:
                Advance();
                return (TokenKind.Minus, "-", span);
            case TokenKind.Star:
                Advance();
                return (TokenKind.Star, "*", span);
            case TokenKind.StarStar:
                Advance();
                return (TokenKind.StarStar, "**", span);
            case TokenKind.Slash:
                Advance();
                return (TokenKind.Slash, "/", span);
            case TokenKind.Percent:
                Advance();
                return (TokenKind.Percent, "%", span);
            case TokenKind.EqualEqual:
                Advance();
                return (TokenKind.EqualEqual, "==", span);
            case TokenKind.BangEqual:
                Advance();
                return (TokenKind.BangEqual, "!=", span);
            case TokenKind.Less:
                Advance();
                return (TokenKind.Less, "<", span);
            case TokenKind.LessEqual:
                Advance();
                return (TokenKind.LessEqual, "<=", span);
            case TokenKind.Greater:
                Advance();
                return (TokenKind.Greater, ">", span);
            case TokenKind.GreaterEqual:
                Advance();
                return (TokenKind.GreaterEqual, ">=", span);
            case TokenKind.AmpAmp:
                Advance();
                return (TokenKind.AmpAmp, "&&", span);
            case TokenKind.PipePipe:
                Advance();
                return (TokenKind.PipePipe, "||", span);
            case TokenKind.Amp:
                Advance();
                return (TokenKind.Amp, "&", span);
            case TokenKind.Pipe:
                Advance();
                return (TokenKind.Pipe, "|", span);
            case TokenKind.Caret:
                Advance();
                return (TokenKind.Caret, "^", span);
            case TokenKind.LessLess:
                Advance();
                return (TokenKind.LessLess, "<<", span);
            case TokenKind.GreaterGreater:
                Advance();
                return (TokenKind.GreaterGreater, ">>", span);
            case TokenKind.Exclamation:
                Advance();
                return (TokenKind.Exclamation, "!", span);
            case TokenKind.Tilde:
                Advance();
                return (TokenKind.Tilde, "~", span);
            case TokenKind.NullCoalesce:
                Advance();
                return (TokenKind.NullCoalesce, "??", span);
            case TokenKind.Question:
                Advance();
                return (TokenKind.Question, "?", span);
            case TokenKind.Arrow:
                Advance();
                return (TokenKind.Arrow, "->", span);
            case TokenKind.Identifier:
                // Support word operators like "and", "or", "not", "mod"
                // Also support hyphenated identifiers like "char-at", "is-letter", "sb-new", "regex-test"
                var startSpan = span;
                Advance();

                // Check for hyphenated identifier (e.g., char-at, is-letter, sb-new)
                var opName = text;
                var endSpan = span;
                while (Check(TokenKind.Minus) && Peek(1).Kind == TokenKind.Identifier)
                {
                    Advance(); // consume '-'
                    opName += "-" + Current.Text;
                    endSpan = Current.Span;
                    Advance(); // consume the next identifier part
                }

                // For hyphenated identifiers, compute the full span
                var fullSpan = opName.Contains('-') ? startSpan.Union(endSpan) : startSpan;

                return (TokenKind.Identifier, opName.ToLowerInvariant() switch
                {
                    "and" => "&&",
                    "or" => "||",
                    "not" => "!",
                    "mod" => "%",
                    "eq" => "==",
                    "ne" or "neq" => "!=",
                    "lt" => "<",
                    "le" or "lte" => "<=",
                    "gt" => ">",
                    "ge" or "gte" => ">=",
                    _ => opName
                }, fullSpan);
            default:
                // Provide helpful message based on what was found
                var hint = token.Kind switch
                {
                    TokenKind.IntLiteral or TokenKind.FloatLiteral or TokenKind.DecimalLiteral =>
                        "Operators come before arguments: (+ 1 2) not (1 + 2)",
                    TokenKind.StrLiteral =>
                        "Operators come before arguments: (contains str \"x\") not (str contains \"x\")",
                    TokenKind.OpenParen =>
                        "Nested expression found where operator expected. Check parentheses balance.",
                    TokenKind.CloseParen =>
                        "Unexpected closing parenthesis. Check parentheses balance.",
                    _ => $"Valid operators: {OperatorSuggestions.GetOperatorCategories()}"
                };
                _diagnostics.ReportError(span, DiagnosticCode.InvalidLispExpression,
                    $"Expected operator in Lisp expression, found '{token.Text}'. {hint}");
                Advance();
                return (token.Kind, text, span);
        }
    }

    private bool IsUnaryOperator(TokenKind kind)
    {
        return kind is TokenKind.Exclamation or TokenKind.Tilde or TokenKind.Minus
            or TokenKind.Identifier; // for 'not'
    }

    private UnaryOperator? GetUnaryOperator(TokenKind kind, string text)
    {
        return text switch
        {
            "!" or "not" => UnaryOperator.Not,
            "~" => UnaryOperator.BitwiseNot,
            "-" => UnaryOperator.Negate,
            "inc" or "pre-inc" => UnaryOperator.PreIncrement,
            "dec" or "pre-dec" => UnaryOperator.PreDecrement,
            "post-inc" => UnaryOperator.PostIncrement,
            "post-dec" => UnaryOperator.PostDecrement,
            _ => null
        };
    }

    /// <summary>
    /// Parses a quantifier expression: (forall ((var1 type1) (var2 type2) ...) body)
    /// or (exists ((var1 type1) ...) body)
    /// </summary>
    private ExpressionNode ParseQuantifierExpression(Token startToken, string kind)
    {
        // Parse the binding list: ((var1 type1) (var2 type2) ...)
        Expect(TokenKind.OpenParen);
        var boundVars = new List<QuantifierVariableNode>();

        while (Check(TokenKind.OpenParen))
        {
            var bindingStart = Expect(TokenKind.OpenParen);
            var nameToken = Expect(TokenKind.Identifier);
            var typeToken = Expect(TokenKind.Identifier);
            var bindingEnd = Expect(TokenKind.CloseParen);
            var bindingSpan = bindingStart.Span.Union(bindingEnd.Span);
            boundVars.Add(new QuantifierVariableNode(bindingSpan, nameToken.Text, typeToken.Text));
        }

        Expect(TokenKind.CloseParen);

        if (boundVars.Count == 0)
        {
            _diagnostics.ReportError(startToken.Span, DiagnosticCode.QuantifierNoBoundVars,
                $"Quantifier '{kind}' must have at least one bound variable");
            // Create a dummy to continue parsing
            boundVars.Add(new QuantifierVariableNode(startToken.Span, "_dummy", "i32"));
        }

        // Parse the body expression
        var body = ParseLispArgument();
        var endToken = Expect(TokenKind.CloseParen);
        var span = startToken.Span.Union(endToken.Span);

        return kind == "forall"
            ? new ForallExpressionNode(span, boundVars, body)
            : new ExistsExpressionNode(span, boundVars, body);
    }

    /// <summary>
    /// Parses an implication expression: (-> antecedent consequent)
    /// </summary>
    private ExpressionNode ParseImplicationExpression(Token startToken)
    {
        var antecedent = ParseLispArgument();
        var consequent = ParseLispArgument();
        var endToken = Expect(TokenKind.CloseParen);
        var span = startToken.Span.Union(endToken.Span);
        return new ImplicationExpressionNode(span, antecedent, consequent);
    }

    /// <summary>
    /// Parses an argument inside a Lisp expression.
    /// Can be a literal, identifier (bare variable), or nested Lisp expression.
    /// Supports trailing member access (e.g., §C[...] §/C.Length)
    /// Also handles 'is' pattern expressions: expr is Type [variable]
    /// </summary>
    private ExpressionNode ParseLispArgument()
    {
        // Handle keyword argument syntax: :keyword or :hyphenated-keyword
        if (Check(TokenKind.Colon))
        {
            var colonToken = Advance();
            if (Check(TokenKind.Identifier))
            {
                var identToken = Advance();
                var keywordName = identToken.Text;
                var endSpan = identToken.Span;

                // Handle hyphenated keywords (e.g., :ignore-case, :invariant-ignore-case)
                while (Check(TokenKind.Minus) && Peek(1).Kind == TokenKind.Identifier)
                {
                    Advance(); // consume '-'
                    var nextIdent = Advance();
                    keywordName += "-" + nextIdent.Text;
                    endSpan = nextIdent.Span;
                }

                var span = colonToken.Span.Union(endSpan);
                return new KeywordArgNode(span, keywordName);
            }
            // Standalone colon - error
            _diagnostics.ReportError(colonToken.Span, DiagnosticCode.InvalidLispExpression,
                "Expected identifier after ':' for keyword argument");
            return new IntLiteralNode(colonToken.Span, 0);
        }

        ExpressionNode expr;
        switch (Current.Kind)
        {
            case TokenKind.IntLiteral:
                expr = ParseIntLiteral();
                break;
            case TokenKind.StrLiteral:
                expr = ParseStringLiteral();
                break;
            case TokenKind.BoolLiteral:
                expr = ParseBoolLiteral();
                break;
            case TokenKind.FloatLiteral:
                expr = ParseFloatLiteral();
                break;
            case TokenKind.DecimalLiteral:
                expr = ParseDecimalLiteral();
                break;
            case TokenKind.Identifier:
                expr = ParseBareReference(); // Bare variable reference
                break;
            case TokenKind.OpenParen:
                expr = ParseLispExpression(); // Nested expression
                break;
            case TokenKind.Call:
                expr = ParseCallExpression(); // Call expression inside Lisp
                break;
            case TokenKind.New:
                expr = ParseNewExpression(); // New expression inside Lisp
                break;
            case TokenKind.Await:
                expr = ParseAwaitExpression(); // Await expression inside Lisp
                break;
            case TokenKind.Index:
                expr = ParseArrayAccess(); // §IDX inside Lisp
                break;
            case TokenKind.Length:
                expr = ParseArrayLength(); // §LEN inside Lisp
                break;
            case TokenKind.Has:
                expr = ParseCollectionContains(); // §HAS inside Lisp
                break;
            case TokenKind.Count:
                expr = ParseCollectionCount(); // §CNT inside Lisp
                break;
            default:
                // Provide helpful message for unexpected tokens
                var hint = Current.Kind switch
                {
                    TokenKind.Plus or TokenKind.Minus or TokenKind.Star or TokenKind.Slash =>
                        "Operators come before arguments in Lisp syntax: (+ a b) not (a + b)",
                    TokenKind.EqualEqual or TokenKind.BangEqual or TokenKind.Less or TokenKind.Greater =>
                        "Comparison operators come first: (== a b) not (a == b)",
                    TokenKind.AmpAmp or TokenKind.PipePipe =>
                        "Logical operators come first: (&& a b) not (a && b)",
                    TokenKind.CloseParen =>
                        "Unexpected closing parenthesis. Check expression structure.",
                    _ => "Expected a value (number, string, variable) or nested expression"
                };
                _diagnostics.ReportError(Current.Span, DiagnosticCode.InvalidLispExpression,
                    $"Unexpected token '{Current.Text}' in expression argument. {hint}");
                Advance();
                expr = new IntLiteralNode(Current.Span, 0); // Recovery: return dummy value
                break;
        }

        // Handle trailing member access (e.g., §C[...] §/C.Length or run?.Status)
        // and array access (e.g., array{index})
        expr = ParseTrailingMemberAccess(expr);

        // Handle 'is' pattern expression: expr is Type [variable]
        // This handles C# pattern matching like "other is UnitSystem otherUnitSystem"
        if (Check(TokenKind.Identifier) && Current.Text == "is")
        {
            expr = ParseIsPatternExpression(expr);
        }

        return expr;
    }

    /// <summary>
    /// Parses trailing member access chains: .Member, ?.Member, {index}
    /// Shared helper used by ParseLispArgument, ParseNewExpression, ParseCallExpression,
    /// ParseThisExpression, and ParseBaseExpression.
    /// </summary>
    private ExpressionNode ParseTrailingMemberAccess(ExpressionNode expr)
    {
        while (Check(TokenKind.Dot) || Check(TokenKind.NullConditional) || Check(TokenKind.OpenBrace))
        {
            if (Check(TokenKind.OpenBrace))
            {
                // Array access: array{index}
                Advance(); // consume '{'
                var indexExpr = ParseLispArgument();
                var endToken = Expect(TokenKind.CloseBrace);
                var span = expr.Span.Union(endToken.Span);
                expr = new ArrayAccessNode(span, expr, indexExpr);
            }
            else
            {
                var isNullConditional = Check(TokenKind.NullConditional);
                Advance(); // consume '.' or '?.'
                var memberToken = Expect(TokenKind.Identifier);
                var span = expr.Span.Union(memberToken.Span);
                if (isNullConditional)
                {
                    expr = new NullConditionalNode(span, expr, memberToken.Text);
                }
                else
                {
                    expr = new FieldAccessNode(span, expr, memberToken.Text);
                }
            }
        }
        return expr;
    }

    /// <summary>
    /// Parses a type name in a Lisp expression context, handling qualified names and generics.
    /// Examples: int, string, List&lt;string&gt;, Dictionary&lt;string, int&gt;, System.Collections.Generic.List&lt;int&gt;
    /// Used by typeof, is, and as expressions.
    /// </summary>
    private string ParseLispTypeName()
    {
        if (!Check(TokenKind.Identifier))
        {
            _diagnostics.ReportError(Current.Span, DiagnosticCode.ExpectedTypeName,
                $"Expected type name, found '{Current.Text}'");
            return "object";
        }

        var typeBuilder = new System.Text.StringBuilder();
        typeBuilder.Append(Current.Text);
        Advance();

        // Handle qualified names (Namespace.Type) and generic types (Type<T>)
        while (Check(TokenKind.Dot) || Check(TokenKind.Less))
        {
            if (Check(TokenKind.Dot))
            {
                typeBuilder.Append('.');
                Advance();
                if (Check(TokenKind.Identifier))
                {
                    typeBuilder.Append(Current.Text);
                    Advance();
                }
            }
            else if (Check(TokenKind.Less))
            {
                // Parse generic type arguments: Type<T, U>
                typeBuilder.Append('<');
                Advance();
                int depth = 1;
                while (depth > 0 && !IsAtEnd)
                {
                    if (Check(TokenKind.Less))
                    {
                        depth++;
                        typeBuilder.Append('<');
                        Advance();
                    }
                    else if (Check(TokenKind.GreaterGreater) && depth >= 2)
                    {
                        // >> closes two generic levels at once (e.g., Dictionary<string, List<int>>)
                        depth -= 2;
                        typeBuilder.Append(">>");
                        Advance();
                    }
                    else if (Check(TokenKind.Greater))
                    {
                        depth--;
                        if (depth > 0)
                        {
                            typeBuilder.Append('>');
                        }
                        Advance(); // consume '>'
                    }
                    else if (depth > 0)
                    {
                        typeBuilder.Append(Current.Text);
                        if (Check(TokenKind.Comma))
                            typeBuilder.Append(' ');
                        Advance();
                    }
                }
                if (depth == 0)
                {
                    // Only append closing '>' if we didn't already via '>>' handling
                    if (!typeBuilder.ToString().EndsWith('>'))
                        typeBuilder.Append('>');
                }
            }
        }

        // Handle nullable types: Type?
        if (Check(TokenKind.Question))
        {
            typeBuilder.Append('?');
            Advance();
        }

        // Handle array types: Type[]
        if (Check(TokenKind.OpenBracket) && Peek(1).Kind == TokenKind.CloseBracket)
        {
            typeBuilder.Append("[]");
            Advance(); // consume '['
            Advance(); // consume ']'
        }

        return typeBuilder.ToString();
    }

    /// <summary>
    /// Parses an 'is' expression in Lisp form: (is expr Type) or (is expr Type variable)
    /// Handles generic types like List&lt;string&gt; that would fail in general arg parsing.
    /// </summary>
    private ExpressionNode ParseLispIsExpression(Token startToken)
    {
        // Parse the expression operand
        var operand = ParseLispArgument();

        // Parse the type name using the shared helper
        var typeName = ParseLispTypeName();

        // Optionally parse a variable name for pattern matching: (is x MyType varName)
        string? variableName = null;
        if (Check(TokenKind.Identifier) && !IsLispOperatorStart(Current.Text))
        {
            variableName = Current.Text;
            Advance();
        }

        var endToken = Expect(TokenKind.CloseParen);
        var span = startToken.Span.Union(endToken.Span);

        return new IsPatternNode(span, operand, typeName, variableName);
    }

    /// <summary>
    /// Parses an 'as' expression in Lisp form: (as expr Type)
    /// Handles generic types like List&lt;string&gt; that would fail in general arg parsing.
    /// </summary>
    private ExpressionNode ParseLispAsExpression(Token startToken)
    {
        // Parse the expression operand
        var operand = ParseLispArgument();

        // Parse the type name using the shared helper
        var typeName = ParseLispTypeName();

        var endToken = Expect(TokenKind.CloseParen);
        var span = startToken.Span.Union(endToken.Span);

        return new TypeOperationNode(span, TypeOp.As, operand, typeName);
    }

    /// <summary>
    /// Parses an 'is' pattern expression: expr is Type [variable]
    /// The expr has already been parsed, we're at the 'is' keyword.
    /// </summary>
    private ExpressionNode ParseIsPatternExpression(ExpressionNode left)
    {
        var startSpan = left.Span;
        Advance(); // consume 'is'

        var typeName = ParseLispTypeName();
        string? variableName = null;

        // Check for optional variable declaration: is Type variableName
        // Only consume if it's an identifier and not the start of another expression or close paren
        if (Check(TokenKind.Identifier) && !IsLispOperatorStart(Current.Text))
        {
            variableName = Current.Text;
            Advance();
        }

        var endSpan = Peek(-1).Span;
        return new IsPatternNode(startSpan.Union(endSpan), left, typeName, variableName);
    }

    /// <summary>
    /// Checks if the identifier could be a Lisp operator (to avoid consuming it as a variable name).
    /// </summary>
    private bool IsLispOperatorStart(string text)
    {
        // Common Lisp operators that might appear after an 'is' pattern
        return text is "and" or "or" or "not";
    }

    /// <summary>
    /// Gets the string representation of an expression for use in pattern expressions.
    /// </summary>
    private string GetExpressionString(ExpressionNode expr)
    {
        return expr switch
        {
            ReferenceNode r => r.Name,
            FieldAccessNode f => $"{GetExpressionString(f.Target)}.{f.FieldName}",
            NullConditionalNode n => $"{GetExpressionString(n.Target)}?.{n.MemberName}",
            _ => expr.ToString() ?? ""
        };
    }

    /// <summary>
    /// Parses a bare identifier as a variable reference (used in Lisp expressions).
    /// Reserved words like true/false are handled as literals by the lexer.
    /// </summary>
    private ReferenceNode ParseBareReference()
    {
        var token = Expect(TokenKind.Identifier);
        return new ReferenceNode(token.Span, token.Text);
    }

    private IntLiteralNode ParseIntLiteral()
    {
        var token = Expect(TokenKind.IntLiteral);
        var value = token.Value is int i ? i : 0;
        return new IntLiteralNode(token.Span, value);
    }

    private StringLiteralNode ParseStringLiteral()
    {
        var token = Expect(TokenKind.StrLiteral);
        var value = token.Value as string ?? "";
        return new StringLiteralNode(token.Span, value)
        {
            IsMultiline = token.Text.StartsWith("\"\"\"")
        };
    }

    private BoolLiteralNode ParseBoolLiteral()
    {
        var token = Expect(TokenKind.BoolLiteral);
        var value = token.Value is bool b && b;
        return new BoolLiteralNode(token.Span, value);
    }

    private FloatLiteralNode ParseFloatLiteral()
    {
        var token = Expect(TokenKind.FloatLiteral);
        var value = token.Value is double d ? d : 0.0;
        return new FloatLiteralNode(token.Span, value);
    }

    private DecimalLiteralNode ParseDecimalLiteral()
    {
        var token = Expect(TokenKind.DecimalLiteral);
        var value = token.Value is decimal d ? d : 0m;
        return new DecimalLiteralNode(token.Span, value);
    }

    private ExpressionNode ParseReference()
    {
        var token = Expect(TokenKind.Identifier);
        ExpressionNode expr = new ReferenceNode(token.Span, token.Text);

        // Handle trailing member access (e.g., _startOptions?.Agenda or obj.Property)
        while (Check(TokenKind.Dot) || Check(TokenKind.NullConditional))
        {
            var isNullConditional = Check(TokenKind.NullConditional);
            Advance(); // consume '.' or '?.'
            var memberToken = Expect(TokenKind.Identifier);
            var span = expr.Span.Union(memberToken.Span);
            if (isNullConditional)
            {
                expr = new NullConditionalNode(span, expr, memberToken.Text);
            }
            else
            {
                expr = new FieldAccessNode(span, expr, memberToken.Text);
            }
        }

        return expr;
    }

    // Phase 3: Type System Expression Parsing

    private SomeExpressionNode ParseSomeExpression()
    {
        var startToken = Expect(TokenKind.Some);
        var value = ParseExpression();
        var span = startToken.Span.Union(value.Span);
        return new SomeExpressionNode(span, value);
    }

    private NoneExpressionNode ParseNoneExpression()
    {
        var startToken = Expect(TokenKind.None);
        var attrs = ParseAttributes();
        // v2 syntax: §NN{type} - type is positional
        var typeName = attrs["_pos0"];
        // Expand compact type name to internal form
        if (!string.IsNullOrEmpty(typeName))
        {
            typeName = AttributeHelper.ExpandType(typeName);
        }
        return new NoneExpressionNode(startToken.Span, typeName);
    }

    private OkExpressionNode ParseOkExpression()
    {
        var startToken = Expect(TokenKind.Ok);
        var value = ParseExpression();
        var span = startToken.Span.Union(value.Span);
        return new OkExpressionNode(span, value);
    }

    private ErrExpressionNode ParseErrExpression()
    {
        var startToken = Expect(TokenKind.Err);
        var error = ParseExpression();
        var span = startToken.Span.Union(error.Span);
        return new ErrExpressionNode(span, error);
    }

    private RecordCreationNode ParseRecordCreation()
    {
        var startToken = Expect(TokenKind.Record);
        var attrs = ParseAttributes();
        var typeName = GetRequiredAttribute(attrs, "type", "RECORD", startToken.Span);

        var fields = new List<FieldAssignmentNode>();
        while (Check(TokenKind.Field))
        {
            var fieldToken = Expect(TokenKind.Field);
            var fieldAttrs = ParseAttributes();
            var fieldName = GetRequiredAttribute(fieldAttrs, "name", "FIELD", fieldToken.Span);
            var value = ParseExpression();
            fields.Add(new FieldAssignmentNode(fieldToken.Span.Union(value.Span), fieldName, value));
        }

        var lastSpan = fields.Count > 0 ? fields[^1].Span : startToken.Span;
        return new RecordCreationNode(startToken.Span.Union(lastSpan), typeName, fields);
    }

    private MatchExpressionNode ParseMatchExpression()
    {
        var startToken = Expect(TokenKind.Match);
        var attrs = ParseAttributes();
        var id = AttributeHelper.InterpretMatchAttributes(attrs);
        if (string.IsNullOrEmpty(id))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "MATCH", "id");
            id = "";
        }

        var target = ParseExpression();
        var cases = ParseMatchCases();

        var endToken = Expect(TokenKind.EndMatch);
        var endAttrs = ParseAttributes();
        var endId = AttributeHelper.InterpretEndMatchAttributes(endAttrs);

        if (endId != id)
        {
            _diagnostics.ReportMismatchedIdWithFix(endToken.Span, "MATCH", id, "END_MATCH", endId);
        }

        var span = startToken.Span.Union(endToken.Span);
        return new MatchExpressionNode(span, id, target, cases, attrs);
    }

    private StatementNode ParseMatchStatement()
    {
        var startToken = Expect(TokenKind.Match);
        var attrs = ParseAttributes();
        var id = AttributeHelper.InterpretMatchAttributes(attrs);
        if (string.IsNullOrEmpty(id))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "MATCH", "id");
            id = "";
        }

        // Check if this is a match expression (indicated by :expr in second positional attribute)
        // {match003:expr} parses as _pos0="match003", _pos1="expr"
        var isExpression = attrs["_pos1"] == "expr";

        var target = ParseExpression();
        var cases = ParseMatchCases();

        var endToken = Expect(TokenKind.EndMatch);
        var endAttrs = ParseAttributes();
        var endId = AttributeHelper.InterpretEndMatchAttributes(endAttrs);

        if (endId != id)
        {
            _diagnostics.ReportMismatchedIdWithFix(endToken.Span, "MATCH", id, "END_MATCH", endId);
        }

        var span = startToken.Span.Union(endToken.Span);

        // Return MatchExpressionNode wrapped in ReturnStatementNode if :expr was present
        if (isExpression)
        {
            var matchExpr = new MatchExpressionNode(span, id, target, cases, attrs);
            return new ReturnStatementNode(span, matchExpr);
        }

        return new MatchStatementNode(span, id, target, cases, attrs);
    }

    private List<MatchCaseNode> ParseMatchCases()
    {
        var cases = new List<MatchCaseNode>();

        while (Check(TokenKind.Case))
        {
            var caseToken = Expect(TokenKind.Case);
            var pattern = ParsePattern();

            // Check for guard clause: §WHEN expression
            ExpressionNode? guard = null;
            if (Check(TokenKind.When))
            {
                Expect(TokenKind.When);
                guard = ParseExpression();
            }

            var body = new List<StatementNode>();

            // Check for arrow syntax: → expression (single expression case)
            if (Check(TokenKind.Arrow))
            {
                Expect(TokenKind.Arrow);
                var expr = ParseExpression();
                body.Add(new ReturnStatementNode(expr.Span, expr));
            }
            else
            {
                // Block syntax - parse statements until closing tag or next case
                while (!IsAtEnd && !Check(TokenKind.Case) && !Check(TokenKind.EndMatch) && !Check(TokenKind.EndCase))
                {
                    var stmt = ParseStatement();
                    if (stmt != null)
                    {
                        body.Add(stmt);
                    }
                }

                // Consume optional §/K closing tag
                if (Check(TokenKind.EndCase))
                {
                    Advance();
                }
            }

            var lastSpan = body.Count > 0 ? body[^1].Span : caseToken.Span;
            cases.Add(new MatchCaseNode(caseToken.Span.Union(lastSpan), pattern, guard, body));
        }

        return cases;
    }

    private PatternNode ParsePattern()
    {
        // Handle (not ...), (or ... ...), (and ... ...) pattern combinators
        if (Check(TokenKind.OpenParen))
        {
            var nextToken = Peek(1);
            if (nextToken.Kind == TokenKind.Identifier && nextToken.Text is "not" or "or" or "and")
            {
                var openParen = Expect(TokenKind.OpenParen);
                var keyword = Advance(); // consume not/or/and

                if (keyword.Text == "not")
                {
                    var inner = ParsePattern();
                    Expect(TokenKind.CloseParen);
                    return new NegatedPatternNode(openParen.Span.Union(inner.Span), inner);
                }
                else
                {
                    var left = ParsePattern();
                    var right = ParsePattern();
                    Expect(TokenKind.CloseParen);
                    var span = openParen.Span.Union(right.Span);
                    return keyword.Text == "or"
                        ? new OrPatternNode(span, left, right)
                        : new AndPatternNode(span, left, right);
                }
            }
        }

        // Handle §VAR{name} pattern
        if (Check(TokenKind.Var))
        {
            var varToken = Expect(TokenKind.Var);
            var attrs = ParseAttributes();
            var name = attrs["_pos0"] ?? attrs["name"] ?? "_";
            return new VarPatternNode(varToken.Span, name);
        }

        // Handle §PREL{op} value pattern (relational pattern)
        if (Check(TokenKind.RelationalPattern))
        {
            var relToken = Expect(TokenKind.RelationalPattern);
            var attrs = ParseAttributes();
            var opStr = attrs["_pos0"] ?? attrs["op"] ?? "gte";

            // Map operator keyword to C# operator
            var op = opStr switch
            {
                "gte" => ">=",
                "lte" => "<=",
                "gt" => ">",
                "lt" => "<",
                _ => ">="
            };

            var operand = ParseExpression();
            return new RelationalPatternNode(relToken.Span.Union(operand.Span), op, operand);
        }

        // Handle relational patterns: gte, lte, gt, lt followed by a value (legacy syntax)
        if (Check(TokenKind.Identifier))
        {
            var token = Current;
            var op = token.Text switch
            {
                "gte" => ">=",
                "lte" => "<=",
                "gt" => ">",
                "lt" => "<",
                _ => null
            };

            if (op != null)
            {
                Advance(); // consume the operator keyword
                var operand = ParseExpression();
                return new RelationalPatternNode(token.Span.Union(operand.Span), op, operand);
            }

            // Handle var pattern: var name (legacy syntax)
            if (token.Text == "var" && Peek(1).Kind == TokenKind.Identifier)
            {
                Advance(); // consume 'var'
                var nameToken = Advance(); // consume name
                return new VarPatternNode(token.Span.Union(nameToken.Span), nameToken.Text);
            }

            // Handle null pattern
            if (token.Text == "null")
            {
                Advance();
                return new ConstantPatternNode(token.Span, new ReferenceNode(token.Span, "null"));
            }

            Advance();
            if (token.Text == "_")
            {
                return new WildcardPatternNode(token.Span);
            }
            return new VariablePatternNode(token.Span, token.Text);
        }

        if (Check(TokenKind.IntLiteral) || Check(TokenKind.StrLiteral) ||
            Check(TokenKind.BoolLiteral) || Check(TokenKind.FloatLiteral) ||
            Check(TokenKind.DecimalLiteral))
        {
            var literal = ParseExpression();
            return new LiteralPatternNode(literal.Span, literal);
        }

        if (Check(TokenKind.Some))
        {
            var startToken = Expect(TokenKind.Some);
            var innerPattern = ParsePattern();
            return new SomePatternNode(startToken.Span.Union(innerPattern.Span), innerPattern);
        }

        if (Check(TokenKind.None))
        {
            var token = Expect(TokenKind.None);
            return new NonePatternNode(token.Span);
        }

        if (Check(TokenKind.Ok))
        {
            var startToken = Expect(TokenKind.Ok);
            var innerPattern = ParsePattern();
            return new OkPatternNode(startToken.Span.Union(innerPattern.Span), innerPattern);
        }

        if (Check(TokenKind.Err))
        {
            var startToken = Expect(TokenKind.Err);
            var innerPattern = ParsePattern();
            return new ErrPatternNode(startToken.Span.Union(innerPattern.Span), innerPattern);
        }

        // Default: wildcard
        return new WildcardPatternNode(Current.Span);
    }

    private ForStatementNode ParseForStatement()
    {
        var startToken = Expect(TokenKind.For);
        var attrs = ParseAttributes();

        // Interpret loop attributes
        var (id, varName, fromStr, toStr, stepStr) = AttributeHelper.InterpretForAttributes(attrs);
        if (string.IsNullOrEmpty(id))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "FOR", "id");
        }
        if (string.IsNullOrEmpty(varName))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "FOR", "var");
        }

        // Parse from/to/step - these can be expressions or literal values in attributes
        ExpressionNode from;
        ExpressionNode to;
        ExpressionNode? step = null;

        // FROM bound - can be int literal, variable reference, or S-expression like (- n 1)
        if (!string.IsNullOrEmpty(fromStr))
            from = ParseExpressionFromAttributeString(fromStr, startToken.Span);
        else
            from = ParseExpression();

        // TO bound - can be int literal, variable reference, or S-expression like (- n 1)
        if (!string.IsNullOrEmpty(toStr))
            to = ParseExpressionFromAttributeString(toStr, startToken.Span);
        else
            to = ParseExpression();

        // STEP bound - can be int literal, variable reference, or S-expression
        if (!string.IsNullOrEmpty(stepStr))
            step = ParseExpressionFromAttributeString(stepStr, startToken.Span);

        // Parse body statements
        var body = ParseStatementBlock(TokenKind.EndFor);

        var endToken = Expect(TokenKind.EndFor);
        var endAttrs = ParseAttributes();
        // Positional: [id]
        var endId = endAttrs["_pos0"] ?? endAttrs["id"] ?? "";

        if (endId != id)
        {
            _diagnostics.ReportMismatchedIdWithFix(endToken.Span, "FOR", id, "END_FOR", endId);
        }

        var span = startToken.Span.Union(endToken.Span);
        return new ForStatementNode(span, id, varName, from, to, step, body, attrs);
    }

    /// <summary>
    /// Parses an expression from an attribute string, supporting S-expressions like (- n 1),
    /// integer literals, and variable references.
    /// </summary>
    private ExpressionNode ParseExpressionFromAttributeString(string attrStr, TextSpan span)
    {
        if (attrStr.StartsWith("("))
        {
            // S-expression: create a temporary lexer/parser to parse it
            var tempLexer = new Lexer(attrStr, _diagnostics);
            var tokens = tempLexer.Tokenize().ToList();
            var tempParser = new Parser(tokens, _diagnostics);
            return tempParser.ParseExpression();
        }
        if (int.TryParse(attrStr, out var intVal))
            return new IntLiteralNode(span, intVal);
        return new ReferenceNode(span, attrStr);
    }

    private WhileStatementNode ParseWhileStatement()
    {
        var startToken = Expect(TokenKind.While);
        var attrs = ParseAttributes();

        // Positional: [id]
        var id = attrs["_pos0"] ?? attrs["id"] ?? "";
        if (string.IsNullOrEmpty(id))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "WHILE", "id");
        }

        // Parse condition expression
        var condition = ParseExpression();

        // Parse body statements
        var body = ParseStatementBlock(TokenKind.EndWhile);

        var endToken = Expect(TokenKind.EndWhile);
        var endAttrs = ParseAttributes();
        // Positional: [id]
        var endId = endAttrs["_pos0"] ?? endAttrs["id"] ?? "";

        if (endId != id)
        {
            _diagnostics.ReportMismatchedIdWithFix(endToken.Span, "WHILE", id, "END_WHILE", endId);
        }

        var span = startToken.Span.Union(endToken.Span);
        return new WhileStatementNode(span, id, condition, body, attrs);
    }

    private DoWhileStatementNode ParseDoWhileStatement()
    {
        var startToken = Expect(TokenKind.Do);
        var attrs = ParseAttributes();

        // Positional: [id]
        var id = attrs["_pos0"] ?? attrs["id"] ?? "";
        if (string.IsNullOrEmpty(id))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "DO", "id");
        }

        // Parse body statements
        var body = ParseStatementBlock(TokenKind.EndDo);

        var endToken = Expect(TokenKind.EndDo);
        var endAttrs = ParseAttributes();
        // Positional: [id]
        var endId = endAttrs["_pos0"] ?? endAttrs["id"] ?? "";

        if (endId != id)
        {
            _diagnostics.ReportMismatchedIdWithFix(endToken.Span, "DO", id, "END_DO", endId);
        }

        // Parse condition expression (comes after §/DO[id])
        var condition = ParseExpression();

        var span = startToken.Span.Union(Current.Span);
        return new DoWhileStatementNode(span, id, body, condition, attrs);
    }

    /// <summary>
    /// Parses IF as a conditional expression (ternary).
    /// §IF{id} condition → thenExpr §EL → elseExpr §/I{id}
    /// Returns a ConditionalExpressionNode for use in expression contexts.
    /// </summary>
    private ExpressionNode ParseIfExpression()
    {
        var startToken = Expect(TokenKind.If);
        var attrs = ParseAttributes();

        // Parse the condition
        var condition = ParseExpression();

        // Expect arrow and then expression
        if (!Check(TokenKind.Arrow))
        {
            _diagnostics.ReportError(Current.Span, DiagnosticCode.ExpectedExpression, "Expected '→' after IF condition in expression context");
            return new IntLiteralNode(startToken.Span, 0);
        }
        Advance(); // consume →

        // Parse then expression
        var thenExpr = ParseExpression();

        // Expect §EL (else)
        if (!Check(TokenKind.Else))
        {
            _diagnostics.ReportError(Current.Span, "Calor0101", "IF expression requires an else clause (§EL)");
            return thenExpr;
        }
        Expect(TokenKind.Else);

        // Expect arrow and else expression
        if (!Check(TokenKind.Arrow))
        {
            _diagnostics.ReportError(Current.Span, "Calor0102", "Expected '→' after §EL in IF expression");
            return thenExpr;
        }
        Advance(); // consume →

        // Parse else expression
        var elseExpr = ParseExpression();

        // Expect closing §/I{id}
        var endSpan = elseExpr.Span;
        if (Check(TokenKind.EndIf))
        {
            var endToken = Expect(TokenKind.EndIf);
            var endAttrs = ParseAttributes();
            endSpan = endToken.Span;
        }

        var span = startToken.Span.Union(endSpan);
        return new ConditionalExpressionNode(span, condition, thenExpr, elseExpr);
    }

    private IfStatementNode ParseIfStatement()
    {
        var startToken = Expect(TokenKind.If);
        var attrs = ParseAttributes();

        // Interpret if attributes
        var id = AttributeHelper.InterpretIfAttributes(attrs);
        if (string.IsNullOrEmpty(id))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "IF", "id");
        }

        // Parse condition expression
        var condition = ParseExpression();

        // Parse then body
        var thenBody = new List<StatementNode>();
        var elseIfClauses = new List<ElseIfClauseNode>();
        List<StatementNode>? elseBody = null;

        // Check for arrow syntax: §IF{id} condition → statement
        if (Check(TokenKind.Arrow))
        {
            Advance(); // consume arrow
            var singleStmt = ParseStatement();
            if (singleStmt != null)
            {
                thenBody.Add(singleStmt);
            }

            // Parse optional §EI (else if) and §EL (else) with arrow syntax
            while (Check(TokenKind.ElseIf))
            {
                var elseIfToken = Expect(TokenKind.ElseIf);
                var elseIfCondition = ParseExpression();
                var elseIfBody = new List<StatementNode>();

                if (Check(TokenKind.Arrow))
                {
                    Advance(); // consume arrow
                    var elseIfStmt = ParseStatement();
                    if (elseIfStmt != null)
                    {
                        elseIfBody.Add(elseIfStmt);
                    }
                }
                else
                {
                    // Multi-statement body (until next clause or end)
                    while (!IsAtEnd && !Check(TokenKind.EndIf) && !Check(TokenKind.Else) && !Check(TokenKind.ElseIf))
                    {
                        var stmt = ParseStatement();
                        if (stmt != null)
                        {
                            elseIfBody.Add(stmt);
                        }
                    }
                }

                elseIfClauses.Add(new ElseIfClauseNode(elseIfToken.Span, elseIfCondition, elseIfBody));
            }

            // Parse optional §EL (else) with arrow syntax
            if (Check(TokenKind.Else))
            {
                Expect(TokenKind.Else);
                elseBody = new List<StatementNode>();

                if (Check(TokenKind.Arrow))
                {
                    Advance(); // consume arrow
                    var elseStmt = ParseStatement();
                    if (elseStmt != null)
                    {
                        elseBody.Add(elseStmt);
                    }
                }
                else
                {
                    while (!IsAtEnd && !Check(TokenKind.EndIf))
                    {
                        var stmt = ParseStatement();
                        if (stmt != null)
                        {
                            elseBody.Add(stmt);
                        }
                    }
                }
            }

            var endToken = Expect(TokenKind.EndIf);
            var endAttrs = ParseAttributes();
            var endId = endAttrs["_pos0"] ?? endAttrs["id"] ?? "";

            if (endId != id)
            {
                _diagnostics.ReportMismatchedIdWithFix(endToken.Span, "IF", id, "END_IF", endId);
            }

            var span = startToken.Span.Union(endToken.Span);
            return new IfStatementNode(span, id, condition, thenBody, elseIfClauses, elseBody, attrs);
        }

        // Standard multi-statement body
        while (!IsAtEnd && !Check(TokenKind.EndIf) && !Check(TokenKind.Else) && !Check(TokenKind.ElseIf))
        {
            var stmt = ParseStatement();
            if (stmt != null)
            {
                thenBody.Add(stmt);
            }
        }

        // Parse ELSEIF clauses
        while (Check(TokenKind.ElseIf))
        {
            var elseIfToken = Expect(TokenKind.ElseIf);
            var elseIfCondition = ParseExpression();
            var elseIfBody = new List<StatementNode>();

            while (!IsAtEnd && !Check(TokenKind.EndIf) && !Check(TokenKind.Else) && !Check(TokenKind.ElseIf))
            {
                var stmt = ParseStatement();
                if (stmt != null)
                {
                    elseIfBody.Add(stmt);
                }
            }

            elseIfClauses.Add(new ElseIfClauseNode(elseIfToken.Span, elseIfCondition, elseIfBody));
        }

        // Parse ELSE clause
        if (Check(TokenKind.Else))
        {
            Expect(TokenKind.Else);
            elseBody = new List<StatementNode>();

            while (!IsAtEnd && !Check(TokenKind.EndIf))
            {
                var stmt = ParseStatement();
                if (stmt != null)
                {
                    elseBody.Add(stmt);
                }
            }
        }

        var endToken2 = Expect(TokenKind.EndIf);
        var endAttrs2 = ParseAttributes();
        // Positional: [id]
        var endId2 = endAttrs2["_pos0"] ?? endAttrs2["id"] ?? "";

        if (endId2 != id)
        {
            _diagnostics.ReportMismatchedIdWithFix(endToken2.Span, "IF", id, "END_IF", endId2);
        }

        var span2 = startToken.Span.Union(endToken2.Span);
        return new IfStatementNode(span2, id, condition, thenBody, elseIfClauses, elseBody, attrs);
    }

    private BindStatementNode ParseBindStatement()
    {
        var startToken = Expect(TokenKind.Bind);
        var attrs = ParseAttributes();

        // Interpret bind attributes - now includes type as third return value
        var (name, isMutable, typeName) = AttributeHelper.InterpretBindAttributes(attrs);
        if (string.IsNullOrEmpty(name))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "BIND", "name");
        }

        // Parse optional initializer expression
        ExpressionNode? initializer = null;
        if (IsExpressionStart())
        {
            initializer = ParseExpression();
        }

        var span = initializer != null ? startToken.Span.Union(initializer.Span) : startToken.Span;
        return new BindStatementNode(span, name, typeName, isMutable, initializer, attrs);
    }

    private List<StatementNode> ParseStatementBlock(params TokenKind[] terminators)
    {
        var statements = new List<StatementNode>();

        while (!IsAtEnd && !terminators.Any(Check))
        {
            var stmt = ParseStatement();
            if (stmt != null)
            {
                statements.Add(stmt);
            }
        }

        return statements;
    }

    private AttributeCollection ParseAttributes()
    {
        var attrs = new AttributeCollection();

        // Parse structural braces {} for tag attributes
        // Note: [] is now reserved for array types to align with LLM training
        while (Check(TokenKind.OpenBrace))
        {
            Advance(); // consume {

            // Positional format: {value1:value2:...} or {value}
            ParsePositionalAttributes(attrs);

            Expect(TokenKind.CloseBrace);
        }

        return attrs;
    }

    private void ParsePositionalAttributes(AttributeCollection attrs)
    {
        // Parse colon-separated values: {value1:value2:value3}
        // Store them as _pos0, _pos1, _pos2, etc. for later interpretation
        var values = new List<string>();
        var position = 0;

        do
        {
            var value = ParseValue();
            values.Add(value);
            attrs.Add($"_pos{position}", value);
            position++;
        }
        while (MatchColon());

        // Also store the raw positional count
        attrs.Add("_posCount", position.ToString());
    }

    /// <summary>
    /// Checks if current position has an escaped brace (\{ or \}).
    /// If so, returns the brace character and advances past both tokens.
    /// </summary>
    private char? TryParseEscapedBrace()
    {
        if (Check(TokenKind.Backslash))
        {
            var next = Peek(1).Kind;
            if (next == TokenKind.OpenBrace)
            {
                Advance(); // consume backslash
                Advance(); // consume {
                return '{';
            }
            if (next == TokenKind.CloseBrace)
            {
                Advance(); // consume backslash
                Advance(); // consume }
                return '}';
            }
        }
        return null;
    }

    private string ParseValue()
    {
        var sb = new System.Text.StringBuilder();

        // Handle ~ prefix for mutability
        if (Check(TokenKind.Tilde))
        {
            sb.Append('~');
            Advance();
        }

        // Handle # prefix for semantic shortcodes
        if (Check(TokenKind.Hash))
        {
            sb.Append('#');
            Advance();
        }

        // Handle ? prefix for Option types
        if (Check(TokenKind.Question))
        {
            sb.Append('?');
            Advance();
        }

        // Handle array types like [u8], [[i32]], [str]
        if (Check(TokenKind.OpenBracket))
        {
            var depth = 0;
            while (Check(TokenKind.OpenBracket))
            {
                sb.Append('[');
                Advance();
                depth++;
            }

            // Parse the element type
            if (Check(TokenKind.Identifier))
            {
                sb.Append(Advance().Text);

                // Handle generic element types like [List<int>]
                if (Check(TokenKind.Less))
                {
                    sb.Append('<');
                    Advance();
                    var genericDepth = 1;
                    while (!IsAtEnd && genericDepth > 0)
                    {
                        if (Check(TokenKind.Less))
                        {
                            sb.Append('<');
                            genericDepth++;
                            Advance();
                        }
                        else if (Check(TokenKind.Greater))
                        {
                            sb.Append('>');
                            genericDepth--;
                            Advance();
                        }
                        else if (Check(TokenKind.GreaterGreater))
                        {
                            // Handle >> from nested generics like [List<Task<int>>]
                            sb.Append(">>");
                            genericDepth -= 2;
                            Advance();
                        }
                        else if (Check(TokenKind.Identifier))
                        {
                            sb.Append(Advance().Text);
                        }
                        else if (Check(TokenKind.Comma))
                        {
                            sb.Append(',');
                            Advance();
                        }
                        else
                        {
                            break;
                        }
                    }
                }
            }

            // Close the array brackets
            while (depth > 0 && Check(TokenKind.CloseBracket))
            {
                sb.Append(']');
                Advance();
                depth--;
            }

            return sb.ToString();
        }

        // Parse the main value (identifier, literal, or compound identifier)
        if (Check(TokenKind.Identifier))
        {
            sb.Append(Advance().Text);

            // Handle generic types like ILogger<MeetingModeratorService> or List<string>
            if (Check(TokenKind.Less))
            {
                sb.Append('<');
                Advance(); // consume <

                // Parse generic type arguments (may be nested)
                var depth = 1;
                var lastWasIdentifier = false;
                while (!IsAtEnd && depth > 0)
                {
                    if (Check(TokenKind.Less))
                    {
                        sb.Append('<');
                        depth++;
                        lastWasIdentifier = false;
                        Advance();
                    }
                    else if (Check(TokenKind.Greater))
                    {
                        sb.Append('>');
                        depth--;
                        lastWasIdentifier = false;
                        Advance();
                    }
                    else if (Check(TokenKind.GreaterGreater))
                    {
                        // Handle >> from nested generics like Task<List<int>>
                        sb.Append(">>");
                        depth -= 2;
                        lastWasIdentifier = false;
                        Advance();
                    }
                    else if (Check(TokenKind.Identifier))
                    {
                        // Add space between consecutive identifiers (e.g., "out T", "in T")
                        if (lastWasIdentifier) sb.Append(' ');
                        sb.Append(Advance().Text);
                        lastWasIdentifier = true;
                    }
                    else if (Check(TokenKind.Comma))
                    {
                        sb.Append(',');
                        lastWasIdentifier = false;
                        Advance();
                    }
                    else if (Check(TokenKind.Question))
                    {
                        sb.Append('?');
                        lastWasIdentifier = false;
                        Advance();
                    }
                    else if (Current.Text == ".")
                    {
                        sb.Append('.');
                        lastWasIdentifier = false;
                        Advance();
                    }
                    else
                    {
                        // Unknown token in generic, stop parsing
                        break;
                    }
                }
            }

            // Handle compound identifiers like Console.WriteLine
            while (Current.Text == ".")
            {
                sb.Append(Advance().Text); // consume '.'
                if (Check(TokenKind.Identifier))
                {
                    sb.Append(Advance().Text);
                }
            }

            // Handle hyphenated identifiers like prot-int (protected internal)
            while (Check(TokenKind.Minus) && Peek(1).Kind == TokenKind.Identifier)
            {
                sb.Append('-');
                Advance(); // consume '-'
                sb.Append(Advance().Text); // consume identifier
            }

            // Handle comma-separated values like partial,static for modifiers
            // Only consume comma if followed by an identifier (not a colon or close brace)
            while (Check(TokenKind.Comma) && Peek(1).Kind == TokenKind.Identifier)
            {
                sb.Append(',');
                Advance(); // consume comma
                sb.Append(Advance().Text); // consume identifier
            }
        }
        else if (Check(TokenKind.StrLiteral))
        {
            sb.Append(Advance().Value as string ?? "");
        }
        else if (Check(TokenKind.IntLiteral))
        {
            sb.Append(Advance().Value?.ToString() ?? "");
        }
        else if (Check(TokenKind.BoolLiteral))
        {
            sb.Append(Advance().Value?.ToString()?.ToLowerInvariant() ?? "");
        }

        // Handle ! suffix for fallibility or Result types (T!E)
        if (Check(TokenKind.Exclamation))
        {
            sb.Append('!');
            Advance();

            // Check for error type after ! (for Result types)
            if (Check(TokenKind.Identifier))
            {
                sb.Append(Advance().Text);
            }
        }

        // Handle complex expressions that may contain escaped braces and other tokens
        // Continue parsing until we hit a colon (separator) or unescaped close brace (end of attributes)
        while (!IsAtEnd && !Check(TokenKind.Colon) && !Check(TokenKind.CloseBrace))
        {
            // Handle escaped braces: \{ and \}
            var escapedBrace = TryParseEscapedBrace();
            if (escapedBrace.HasValue)
            {
                sb.Append(escapedBrace.Value);
                continue;
            }

            // Handle common expression tokens
            // Add space before identifiers if there's content before them (to separate from operators/other tokens)
            if (Check(TokenKind.Identifier))
            {
                if (sb.Length > 0 && !char.IsWhiteSpace(sb[sb.Length - 1]) && sb[sb.Length - 1] != '(')
                    sb.Append(' ');
                sb.Append(Advance().Text);
            }
            else if (Current.Text == ".")
            {
                sb.Append('.');
                Advance();
            }
            else if (Check(TokenKind.OpenParen))
            {
                sb.Append('(');
                Advance();
            }
            else if (Check(TokenKind.CloseParen))
            {
                sb.Append(')');
                Advance();
            }
            else if (Check(TokenKind.Comma))
            {
                // Only consume comma if not followed by close brace (end of args)
                if (Peek(1).Kind == TokenKind.CloseBrace)
                    break;
                sb.Append(',');
                Advance();
            }
            else if (Check(TokenKind.Less))
            {
                sb.Append('<');
                Advance();
            }
            else if (Check(TokenKind.Greater))
            {
                sb.Append('>');
                Advance();
            }
            else if (Check(TokenKind.IntLiteral))
            {
                // Add space before integers if there's content before them (to separate from identifiers)
                if (sb.Length > 0 && !char.IsWhiteSpace(sb[sb.Length - 1]) && sb[sb.Length - 1] != '(')
                    sb.Append(' ');
                sb.Append(Advance().Value?.ToString() ?? "");
            }
            else if (Check(TokenKind.FloatLiteral) || Check(TokenKind.DecimalLiteral))
            {
                // Add space before floats if there's content before them
                if (sb.Length > 0 && !char.IsWhiteSpace(sb[sb.Length - 1]) && sb[sb.Length - 1] != '(')
                    sb.Append(' ');
                sb.Append(Advance().Value?.ToString() ?? "");
            }
            else if (Check(TokenKind.BoolLiteral))
            {
                // Add space before booleans if there's content before them
                if (sb.Length > 0 && !char.IsWhiteSpace(sb[sb.Length - 1]) && sb[sb.Length - 1] != '(')
                    sb.Append(' ');
                sb.Append(Advance().Value?.ToString()?.ToLowerInvariant() ?? "");
            }
            else if (Check(TokenKind.StrLiteral))
            {
                // Add space before strings if there's content before them
                if (sb.Length > 0 && !char.IsWhiteSpace(sb[sb.Length - 1]) && sb[sb.Length - 1] != '(')
                    sb.Append(' ');
                sb.Append('"');
                sb.Append(Advance().Value as string ?? "");
                sb.Append('"');
            }
            // Handle arithmetic operators for S-expressions in attributes
            // Add spaces around operators to preserve token boundaries when re-lexed
            else if (Check(TokenKind.Plus))
            {
                sb.Append(" + ");
                Advance();
            }
            else if (Check(TokenKind.Minus))
            {
                sb.Append(" - ");
                Advance();
            }
            else if (Check(TokenKind.Star))
            {
                sb.Append(" * ");
                Advance();
            }
            else if (Check(TokenKind.Slash))
            {
                sb.Append(" / ");
                Advance();
            }
            else if (Check(TokenKind.Percent))
            {
                sb.Append(" % ");
                Advance();
            }
            // Comparison operators
            else if (Check(TokenKind.EqualEqual))
            {
                sb.Append(" == ");
                Advance();
            }
            else if (Check(TokenKind.BangEqual))
            {
                sb.Append(" != ");
                Advance();
            }
            else if (Check(TokenKind.LessEqual))
            {
                sb.Append(" <= ");
                Advance();
            }
            else if (Check(TokenKind.GreaterEqual))
            {
                sb.Append(" >= ");
                Advance();
            }
            // Note: Less and Greater are already handled above for generic types
            // but we need them for comparisons too - they get special handling
            // Logical operators
            else if (Check(TokenKind.AmpAmp))
            {
                sb.Append(" && ");
                Advance();
            }
            else if (Check(TokenKind.PipePipe))
            {
                sb.Append(" || ");
                Advance();
            }
            else if (Check(TokenKind.Exclamation))
            {
                sb.Append("!");
                Advance();
            }
            // Bitwise operators
            else if (Check(TokenKind.Amp))
            {
                sb.Append(" & ");
                Advance();
            }
            else if (Check(TokenKind.Pipe))
            {
                sb.Append(" | ");
                Advance();
            }
            else if (Check(TokenKind.Caret))
            {
                sb.Append(" ^ ");
                Advance();
            }
            else
            {
                // Unknown token, stop parsing this value
                break;
            }
        }

        return sb.ToString();
    }

    private bool MatchColon()
    {
        if (Check(TokenKind.Colon))
        {
            Advance();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Parses C#-style attributes in the format [@AttributeName] or [@AttributeName(args)].
    /// These appear after the structural brackets: §CLASS[id:name][@Route("api")][@ApiController]
    /// </summary>
    private IReadOnlyList<CalorAttributeNode> ParseCSharpAttributes()
    {
        var attributes = new List<CalorAttributeNode>();

        // Keep parsing while we see [@
        while (Check(TokenKind.OpenBracket) && Peek(1).Kind == TokenKind.At)
        {
            var startToken = Advance(); // consume [
            Advance(); // consume @

            // Parse attribute name
            if (!Check(TokenKind.Identifier))
            {
                _diagnostics.ReportUnexpectedToken(Current.Span, "attribute name", Current.Kind);
                // Try to recover by finding the closing bracket
                while (!IsAtEnd && !Check(TokenKind.CloseBracket))
                {
                    Advance();
                }
                if (Check(TokenKind.CloseBracket))
                {
                    Advance();
                }
                continue;
            }

            var nameToken = Advance();
            var name = nameToken.Text;

            // Handle qualified names like System.ComponentModel.Description
            while (Current.Text == ".")
            {
                Advance(); // consume .
                if (Check(TokenKind.Identifier))
                {
                    name += "." + Advance().Text;
                }
            }

            var arguments = new List<CalorAttributeArgument>();

            // Check for arguments: [@Attr(args)]
            if (Check(TokenKind.OpenParen))
            {
                Advance(); // consume (
                arguments = ParseCSharpAttributeArguments();
                Expect(TokenKind.CloseParen);
            }

            Expect(TokenKind.CloseBracket);

            var span = startToken.Span.Union(Current.Span);
            attributes.Add(new CalorAttributeNode(span, name, arguments));
        }

        return attributes;
    }

    /// <summary>
    /// Parses the arguments inside a C# attribute: (arg1, "arg2", Name=value)
    /// </summary>
    private List<CalorAttributeArgument> ParseCSharpAttributeArguments()
    {
        var args = new List<CalorAttributeArgument>();

        if (Check(TokenKind.CloseParen))
        {
            return args;
        }

        do
        {
            // Check for named argument: Name=value
            if (Check(TokenKind.Identifier) && Peek(1).Kind == TokenKind.Equals)
            {
                var nameToken = Advance();
                Advance(); // consume =
                var value = ParseCSharpAttributeValue();
                args.Add(new CalorAttributeArgument(nameToken.Text, value));
            }
            else
            {
                // Positional argument
                var value = ParseCSharpAttributeValue();
                args.Add(new CalorAttributeArgument(value));
            }
        }
        while (MatchComma());

        return args;
    }

    /// <summary>
    /// Parses a single value in a C# attribute argument.
    /// </summary>
    private object ParseCSharpAttributeValue()
    {
        if (Check(TokenKind.StrLiteral))
        {
            return Advance().Value as string ?? "";
        }
        if (Check(TokenKind.IntLiteral))
        {
            return Advance().Value ?? 0;
        }
        if (Check(TokenKind.FloatLiteral) || Check(TokenKind.DecimalLiteral))
        {
            return Advance().Value ?? 0.0;
        }
        if (Check(TokenKind.BoolLiteral))
        {
            return Advance().Value ?? false;
        }
        if (Check(TokenKind.Identifier))
        {
            var text = Advance().Text;

            // Handle typeof(TypeName)
            if (text == "typeof" && Check(TokenKind.OpenParen))
            {
                Advance(); // consume (
                var typeName = "";
                while (!IsAtEnd && !Check(TokenKind.CloseParen))
                {
                    typeName += Advance().Text;
                }
                Expect(TokenKind.CloseParen);
                return new TypeOfReference(typeName);
            }

            // Handle qualified names like System.String or enum values like AccessLevel.Admin
            while (Current.Text == ".")
            {
                text += Advance().Text; // consume .
                if (Check(TokenKind.Identifier))
                {
                    text += Advance().Text;
                }
            }

            return new MemberAccessReference(text);
        }

        // Unrecognized value - return empty string
        _diagnostics.ReportUnexpectedToken(Current.Span, "attribute value", Current.Kind);
        return "";
    }

    private bool MatchComma()
    {
        if (Check(TokenKind.Comma))
        {
            Advance();
            return true;
        }
        return false;
    }

    private string GetRequiredAttribute(AttributeCollection attrs, string name, string tagName, TextSpan span)
    {
        var value = attrs[name];
        if (string.IsNullOrEmpty(value))
        {
            _diagnostics.ReportMissingRequiredAttribute(span, tagName, name);
            return "";
        }
        return value;
    }

    private static Visibility ParseVisibility(string? value)
    {
        return value?.ToLowerInvariant() switch
        {
            "public" or "pub" => Visibility.Public,
            "protected-internal" or "prot-int" => Visibility.ProtectedInternal,
            "internal" or "int" => Visibility.Internal,
            "protected" or "prot" => Visibility.Protected,
            "private" or "priv" => Visibility.Private,
            _ => Visibility.Private
        };
    }

    // Phase 6: Arrays and Collections

    /// <summary>
    /// Parses array creation.
    /// §ARR[arr1:i32:10]                         // int[] arr1 = new int[10]; (sized with literal)
    /// §ARR[arr1:i32:n]                          // int[] arr1 = new int[n]; (sized with variable)
    /// §ARR[arr2:i32] §A 1 §A 2 §A 3 §/ARR[arr2] // int[] arr2 = { 1, 2, 3 }; (initialized)
    /// </summary>
    private ArrayCreationNode ParseArrayCreation()
    {
        var startToken = Expect(TokenKind.Array);
        var attrs = ParseAttributes();

        // Positional: [id:type:size?] or [type:id:size?] for initialized arrays
        var rawPos0 = attrs["_pos0"] ?? "";
        var rawPos1 = attrs["_pos1"] ?? "i32";
        var sizeStr = attrs["_pos2"];

        string id;
        string elementType;

        // Detect format: {type:id:size} vs {id:type:size}
        // If pos0 looks like a type and pos1 doesn't, it's {type:id:size}
        if (AttributeHelper.IsLikelyType(rawPos0) && !AttributeHelper.IsLikelyType(rawPos1))
        {
            elementType = rawPos0;
            id = rawPos1;
        }
        else
        {
            id = rawPos0;
            elementType = rawPos1;
        }

        if (string.IsNullOrEmpty(id))
        {
            // Auto-generate id for unnamed arrays (e.g., §ARR{:i32} or §ARR{i32})
            id = $"_arr{startToken.Span.Start}";
        }

        ExpressionNode? size = null;
        var initializer = new List<ExpressionNode>();

        // If size is specified in attributes, use it
        if (!string.IsNullOrEmpty(sizeStr))
        {
            if (int.TryParse(sizeStr, out var sizeVal))
            {
                // Size is an integer literal
                size = new IntLiteralNode(startToken.Span, sizeVal);
            }
            else if (sizeStr.StartsWith("("))
            {
                // Size is an expression like (len data) - re-lex and parse it
                // This handles §ARR{a001:i32:(len data)}
                size = ParseEmbeddedExpression(sizeStr, startToken.Span);
            }
            else
            {
                // Size is a variable reference (e.g., §ARR{a001:i32:n} where n is a variable)
                size = new ReferenceNode(startToken.Span, sizeStr);
            }
        }
        else
        {
            // No size in attributes - parse as initialized array (like §LIST)
            // Support both bare expressions and §A-prefixed elements
            while (!IsAtEnd && !Check(TokenKind.EndArray))
            {
                if (Check(TokenKind.Arg))
                {
                    // §A-prefixed element (backward compat with emitter output)
                    initializer.Add(ParseArgument());
                }
                else if (IsExpressionStart())
                {
                    // Bare expression (documented syntax: §ARR{id:type} 1 2 3 §/ARR{id})
                    initializer.Add(ParseExpression());
                }
                else
                {
                    break;
                }
            }

            // If only one element and no end tag coming, it might be a size expression
            // e.g., §ARR{id:type} 10 (no §/ARR) → treat 10 as size, not initializer
            if (initializer.Count == 1 && !Check(TokenKind.EndArray))
            {
                size = initializer[0];
                initializer.Clear();
            }
        }

        var endSpan = startToken.Span;
        // Check for optional end tag (required for initialized arrays)
        if (Check(TokenKind.EndArray))
        {
            var endToken = Expect(TokenKind.EndArray);
            var endAttrs = ParseAttributes();
            var endId = endAttrs["_pos0"] ?? "";

            if (endId != id)
            {
                _diagnostics.ReportMismatchedIdWithFix(endToken.Span, "ARR", id, "END_ARR", endId);
            }
            endSpan = endToken.Span;
        }

        var span = startToken.Span.Union(endSpan);
        return new ArrayCreationNode(span, id, id, elementType, size, initializer, attrs);
    }

    /// <summary>
    /// Parses an embedded expression from a string (e.g., "(len data)" from array size attribute).
    /// Re-lexes and parses the string as an expression.
    /// </summary>
    private ExpressionNode ParseEmbeddedExpression(string expressionText, TextSpan fallbackSpan)
    {
        try
        {
            // Create a mini-lexer to tokenize the expression text
            var lexer = new Lexer(expressionText, _diagnostics);
            var tokens = lexer.Tokenize().ToList();

            if (tokens.Count == 0 || (tokens.Count == 1 && tokens[0].Kind == TokenKind.Eof))
            {
                _diagnostics.ReportError(fallbackSpan, "Calor0103", "Empty embedded expression");
                return new IntLiteralNode(fallbackSpan, 0);
            }

            // Create a mini-parser for the expression using shared diagnostics
            var parser = new Parser(tokens, _diagnostics);
            var expr = parser.ParseExpression();

            // Transfer the span to use fallback if the embedded one is synthetic
            return expr;
        }
        catch
        {
            // If parsing fails, treat as a reference (fallback behavior)
            _diagnostics.ReportError(fallbackSpan, "Calor0104", $"Failed to parse embedded expression: {expressionText}");
            return new ReferenceNode(fallbackSpan, expressionText.Trim('(', ')', ' '));
        }
    }

    /// <summary>
    /// Parses array element access.
    /// §IDX §REF[name=arr] 0                     // arr[0]
    /// §IDX{arr} 0                               // arr[0] (attribute shorthand)
    /// </summary>
    private ArrayAccessNode ParseArrayAccess()
    {
        var startToken = Expect(TokenKind.Index);
        var attrs = ParseAttributes();
        var pos0 = attrs["_pos0"];

        ExpressionNode array;
        if (!string.IsNullOrEmpty(pos0))
        {
            // §IDX{varName} index — attribute contains the array reference
            array = new ReferenceNode(startToken.Span, pos0);
        }
        else
        {
            // §IDX §REF[name=arr] index — two separate expressions
            array = ParseExpression();
        }

        var index = ParseExpression();

        var span = startToken.Span.Union(index.Span);
        return new ArrayAccessNode(span, array, index);
    }

    /// <summary>
    /// Parses array length access.
    /// §LEN §REF[name=arr]                       // arr.Length
    /// </summary>
    private ArrayLengthNode ParseArrayLength()
    {
        var startToken = Expect(TokenKind.Length);

        var array = ParseExpression();

        var span = startToken.Span.Union(array.Span);
        return new ArrayLengthNode(span, array);
    }

    /// <summary>
    /// Parses foreach statement.
    /// §EACH[each1:item:i32] §REF[name=arr]      // foreach (int item in arr)
    ///   ...
    /// §/EACH[each1]
    /// </summary>
    private ForeachStatementNode ParseForeachStatement()
    {
        var startToken = Expect(TokenKind.Foreach);
        var attrs = ParseAttributes();

        // Positional: [id:variable:type] or [id:variable:type:index]
        var id = attrs["_pos0"] ?? "";
        var variableName = attrs["_pos1"] ?? "item";
        var variableType = attrs["_pos2"] ?? "var";
        var indexVariableName = attrs["_pos3"]; // null if not provided

        if (string.IsNullOrEmpty(id))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "EACH", "id");
        }

        // Parse collection expression
        var collection = ParseExpression();

        // Parse body statements
        var body = ParseStatementBlock(TokenKind.EndForeach);

        var endToken = Expect(TokenKind.EndForeach);
        var endAttrs = ParseAttributes();
        var endId = endAttrs["_pos0"] ?? "";

        if (endId != id)
        {
            _diagnostics.ReportMismatchedIdWithFix(endToken.Span, "EACH", id, "END_EACH", endId);
        }

        var span = startToken.Span.Union(endToken.Span);
        return new ForeachStatementNode(span, id, variableName, variableType, collection, body, attrs, indexVariableName);
    }

    // Phase 6 Extended: Collections (List, Dictionary, HashSet)

    /// <summary>
    /// Parses list creation.
    /// §LIST{list1:i32}        // List&lt;int&gt;
    ///   1
    ///   2
    /// §/LIST{list1}
    /// </summary>
    private ListCreationNode ParseListCreation()
    {
        var startToken = Expect(TokenKind.List);
        var attrs = ParseAttributes();

        // Positional: {id:type}
        var id = attrs["_pos0"] ?? "";
        var elementType = attrs["_pos1"] ?? "i32";

        if (string.IsNullOrEmpty(id))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "LIST", "id");
        }

        var elements = new List<ExpressionNode>();

        // Parse elements until §/LIST
        while (!IsAtEnd && !Check(TokenKind.EndList) && IsExpressionStart())
        {
            elements.Add(ParseExpression());
        }

        var endToken = Expect(TokenKind.EndList);
        var endAttrs = ParseAttributes();
        var endId = endAttrs["_pos0"] ?? "";

        if (endId != id)
        {
            _diagnostics.ReportMismatchedIdWithFix(endToken.Span, "LIST", id, "END_LIST", endId);
        }

        var span = startToken.Span.Union(endToken.Span);
        return new ListCreationNode(span, id, id, elementType, elements, attrs);
    }

    /// <summary>
    /// Parses list creation as a statement (emits as var declaration).
    /// </summary>
    private BindStatementNode ParseListCreationStatement()
    {
        var listNode = ParseListCreation();
        // Wrap in bind statement: §B{type:name} = list
        return new BindStatementNode(
            listNode.Span,
            listNode.Name,
            $"List<{listNode.ElementType}>",
            isMutable: false,
            listNode,
            new AttributeCollection());
    }

    /// <summary>
    /// Parses array creation as a statement (emits as var declaration).
    /// §ARR{nums:i32} 1 2 3 §/ARR{nums}
    /// </summary>
    private BindStatementNode ParseArrayCreationStatement()
    {
        var arrNode = ParseArrayCreation();
        return new BindStatementNode(
            arrNode.Span,
            arrNode.Name,
            $"{arrNode.ElementType}[]",
            isMutable: false,
            arrNode,
            new AttributeCollection());
    }

    /// <summary>
    /// Parses dictionary creation.
    /// §DICT{dict1:str:i32}    // Dictionary&lt;string, int&gt;
    ///   §KV "one" 1
    ///   §KV "two" 2
    /// §/DICT{dict1}
    /// </summary>
    private DictionaryCreationNode ParseDictionaryCreation()
    {
        var startToken = Expect(TokenKind.Dict);
        var attrs = ParseAttributes();

        // Positional: {id:keyType:valueType}
        var id = attrs["_pos0"] ?? "";
        var keyType = attrs["_pos1"] ?? "str";
        var valueType = attrs["_pos2"] ?? "i32";

        if (string.IsNullOrEmpty(id))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "DICT", "id");
        }

        var entries = new List<KeyValuePairNode>();

        // Parse key-value pairs until §/DICT
        while (!IsAtEnd && !Check(TokenKind.EndDict) && Check(TokenKind.KeyValue))
        {
            entries.Add(ParseKeyValuePair());
        }

        var endToken = Expect(TokenKind.EndDict);
        var endAttrs = ParseAttributes();
        var endId = endAttrs["_pos0"] ?? "";

        if (endId != id)
        {
            _diagnostics.ReportMismatchedIdWithFix(endToken.Span, "DICT", id, "END_DICT", endId);
        }

        var span = startToken.Span.Union(endToken.Span);
        return new DictionaryCreationNode(span, id, id, keyType, valueType, entries, attrs);
    }

    /// <summary>
    /// Parses dictionary creation as a statement.
    /// </summary>
    private BindStatementNode ParseDictionaryCreationStatement()
    {
        var dictNode = ParseDictionaryCreation();
        return new BindStatementNode(
            dictNode.Span,
            dictNode.Name,
            $"Dictionary<{dictNode.KeyType},{dictNode.ValueType}>",
            isMutable: false,
            dictNode,
            new AttributeCollection());
    }

    /// <summary>
    /// Parses a key-value pair.
    /// §KV key value
    /// </summary>
    private KeyValuePairNode ParseKeyValuePair()
    {
        var startToken = Expect(TokenKind.KeyValue);
        var key = ParseExpression();
        var value = ParseExpression();
        var span = startToken.Span.Union(value.Span);
        return new KeyValuePairNode(span, key, value);
    }

    /// <summary>
    /// Parses set creation.
    /// §HSET{set1:str}          // HashSet&lt;string&gt;
    ///   "apple"
    ///   "banana"
    /// §/HSET{set1}
    /// </summary>
    private SetCreationNode ParseSetCreation()
    {
        var startToken = Expect(TokenKind.HashSet);
        var attrs = ParseAttributes();

        // Positional: {id:type}
        var id = attrs["_pos0"] ?? "";
        var elementType = attrs["_pos1"] ?? "str";

        if (string.IsNullOrEmpty(id))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "HSET", "id");
        }

        var elements = new List<ExpressionNode>();

        // Parse elements until §/HSET
        while (!IsAtEnd && !Check(TokenKind.EndHashSet) && IsExpressionStart())
        {
            elements.Add(ParseExpression());
        }

        var endToken = Expect(TokenKind.EndHashSet);
        var endAttrs = ParseAttributes();
        var endId = endAttrs["_pos0"] ?? "";

        if (endId != id)
        {
            _diagnostics.ReportMismatchedIdWithFix(endToken.Span, "HSET", id, "END_HSET", endId);
        }

        var span = startToken.Span.Union(endToken.Span);
        return new SetCreationNode(span, id, id, elementType, elements, attrs);
    }

    /// <summary>
    /// Parses set creation as a statement.
    /// </summary>
    private BindStatementNode ParseSetCreationStatement()
    {
        var setNode = ParseSetCreation();
        return new BindStatementNode(
            setNode.Span,
            setNode.Name,
            $"HashSet<{setNode.ElementType}>",
            isMutable: false,
            setNode,
            new AttributeCollection());
    }

    /// <summary>
    /// Parses collection push (add).
    /// §PUSH{list1} value      // list.Add(value)
    /// §ADD{set1} value        // set.Add(value) - alias for PUSH, idiomatic for HashSet
    /// </summary>
    private CollectionPushNode ParseCollectionPush()
    {
        // Accept either PUSH or ADD token
        var startToken = Check(TokenKind.Push) ? Expect(TokenKind.Push) : Expect(TokenKind.Add);
        var tokenName = startToken.Kind == TokenKind.Push ? "PUSH" : "ADD";
        var attrs = ParseAttributes();
        var collectionName = attrs["_pos0"] ?? "";

        if (string.IsNullOrEmpty(collectionName))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, tokenName, "collection name");
        }

        var value = ParseExpression();
        var span = startToken.Span.Union(value.Span);
        return new CollectionPushNode(span, collectionName, value);
    }

    /// <summary>
    /// Parses dictionary put (add/update).
    /// §PUT{dict1} key value   // dict[key] = value
    /// </summary>
    private DictionaryPutNode ParseDictionaryPut()
    {
        var startToken = Expect(TokenKind.Put);
        var attrs = ParseAttributes();
        var dictionaryName = attrs["_pos0"] ?? "";

        if (string.IsNullOrEmpty(dictionaryName))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "PUT", "dictionary name");
        }

        var key = ParseExpression();
        var value = ParseExpression();
        var span = startToken.Span.Union(value.Span);
        return new DictionaryPutNode(span, dictionaryName, key, value);
    }

    /// <summary>
    /// Parses collection remove.
    /// §REM{coll} keyOrValue   // coll.Remove(keyOrValue)
    /// </summary>
    private CollectionRemoveNode ParseCollectionRemove()
    {
        var startToken = Expect(TokenKind.Remove);
        var attrs = ParseAttributes();
        var collectionName = attrs["_pos0"] ?? "";

        if (string.IsNullOrEmpty(collectionName))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "REM", "collection name");
        }

        var keyOrValue = ParseExpression();
        var span = startToken.Span.Union(keyOrValue.Span);
        return new CollectionRemoveNode(span, collectionName, keyOrValue);
    }

    /// <summary>
    /// Parses collection set index.
    /// §SETIDX{list1} index value  // list[index] = value
    /// </summary>
    private CollectionSetIndexNode ParseCollectionSetIndex()
    {
        var startToken = Expect(TokenKind.SetIndex);
        var attrs = ParseAttributes();
        var collectionName = attrs["_pos0"] ?? "";

        if (string.IsNullOrEmpty(collectionName))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "SETIDX", "collection name");
        }

        var index = ParseExpression();
        var value = ParseExpression();
        var span = startToken.Span.Union(value.Span);
        return new CollectionSetIndexNode(span, collectionName, index, value);
    }

    /// <summary>
    /// Parses collection clear.
    /// §CLR{coll}              // coll.Clear()
    /// </summary>
    private CollectionClearNode ParseCollectionClear()
    {
        var startToken = Expect(TokenKind.Clear);
        var attrs = ParseAttributes();
        var collectionName = attrs["_pos0"] ?? "";

        if (string.IsNullOrEmpty(collectionName))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "CLR", "collection name");
        }

        return new CollectionClearNode(startToken.Span, collectionName);
    }

    /// <summary>
    /// Parses collection insert.
    /// §INS{list1} index value // list.Insert(index, value)
    /// </summary>
    private CollectionInsertNode ParseCollectionInsert()
    {
        var startToken = Expect(TokenKind.Insert);
        var attrs = ParseAttributes();
        var collectionName = attrs["_pos0"] ?? "";

        if (string.IsNullOrEmpty(collectionName))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "INS", "collection name");
        }

        var index = ParseExpression();
        var value = ParseExpression();
        var span = startToken.Span.Union(value.Span);
        return new CollectionInsertNode(span, collectionName, index, value);
    }

    /// <summary>
    /// Parses collection contains check.
    /// §HAS{list1} value           // list.Contains(value)
    /// §HAS{dict1} §KEY key        // dict.ContainsKey(key)
    /// §HAS{dict1} §VAL value      // dict.ContainsValue(value)
    /// </summary>
    private CollectionContainsNode ParseCollectionContains()
    {
        var startToken = Expect(TokenKind.Has);
        var attrs = ParseAttributes();
        var collectionName = attrs["_pos0"] ?? "";

        if (string.IsNullOrEmpty(collectionName))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "HAS", "collection name");
        }

        // Check for §KEY or §VAL modifier
        var mode = ContainsMode.Value;
        if (Check(TokenKind.Key))
        {
            Advance();
            mode = ContainsMode.Key;
        }
        else if (Check(TokenKind.Val))
        {
            Advance();
            mode = ContainsMode.DictValue;
        }

        var keyOrValue = ParseExpression();
        var span = startToken.Span.Union(keyOrValue.Span);
        return new CollectionContainsNode(span, collectionName, keyOrValue, mode);
    }

    /// <summary>
    /// Parses collection count.
    /// §CNT{collection}                  // collection.Count
    /// §CNT collection                   // collection.Count (legacy)
    /// </summary>
    private CollectionCountNode ParseCollectionCount()
    {
        var startToken = Expect(TokenKind.Count);

        // Support both §CNT{collection} and §CNT collection syntaxes
        ExpressionNode collection;
        if (Check(TokenKind.OpenBrace))
        {
            var attrs = ParseAttributes();
            var collectionName = attrs["_pos0"] ?? "";

            if (string.IsNullOrEmpty(collectionName))
            {
                _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "CNT", "collection name");
            }

            collection = new ReferenceNode(startToken.Span, collectionName);
        }
        else
        {
            collection = ParseExpression();
        }

        var span = startToken.Span.Union(collection.Span);
        return new CollectionCountNode(span, collection);
    }

    /// <summary>
    /// Parses dictionary foreach.
    /// §EACHKV{e2:key:value} dict1  // foreach (var (key, value) in dict1)
    ///   §P (+ key ": " value)
    /// §/EACHKV{e2}
    /// </summary>
    private DictionaryForeachNode ParseDictionaryForeach()
    {
        var startToken = Expect(TokenKind.EachKV);
        var attrs = ParseAttributes();

        // Positional: {id:keyName:valueName}
        var id = attrs["_pos0"] ?? "";
        var keyName = attrs["_pos1"] ?? "key";
        var valueName = attrs["_pos2"] ?? "value";

        if (string.IsNullOrEmpty(id))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "EACHKV", "id");
        }

        // Parse dictionary expression
        var dictionary = ParseExpression();

        // Parse body statements
        var body = ParseStatementBlock(TokenKind.EndEachKV);

        var endToken = Expect(TokenKind.EndEachKV);
        var endAttrs = ParseAttributes();
        var endId = endAttrs["_pos0"] ?? "";

        if (endId != id)
        {
            _diagnostics.ReportMismatchedIdWithFix(endToken.Span, "EACHKV", id, "END_EACHKV", endId);
        }

        var span = startToken.Span.Union(endToken.Span);
        return new DictionaryForeachNode(span, id, keyName, valueName, dictionary, body, attrs);
    }

    // Phase 7: Generics

    /// <summary>
    /// Parses an optional type parameter list in angle bracket syntax.
    /// &lt;T&gt; or &lt;T, U&gt; or &lt;TKey, TValue&gt;
    /// Returns an empty list if no type parameters are present.
    /// </summary>
    private List<TypeParameterNode> ParseOptionalTypeParameterList(TextSpan defaultSpan)
    {
        var typeParams = new List<TypeParameterNode>();

        if (!Check(TokenKind.Less))
        {
            return typeParams;
        }

        var startToken = Advance(); // consume <

        do
        {
            // Check for variance modifiers: in/out before the type parameter name
            var variance = Ast.VarianceKind.None;
            if (Check(TokenKind.Identifier) && Current.Text is "in" or "out")
            {
                variance = Current.Text == "out" ? Ast.VarianceKind.Out : Ast.VarianceKind.In;
                Advance(); // consume in/out
            }

            if (Check(TokenKind.Identifier))
            {
                var nameToken = Advance();
                typeParams.Add(new TypeParameterNode(nameToken.Span, nameToken.Text, Array.Empty<TypeConstraintNode>(), variance));
            }
            else
            {
                _diagnostics.ReportUnexpectedToken(Current.Span, "type parameter name", Current.Kind);
                break;
            }
        } while (Match(TokenKind.Comma));

        Expect(TokenKind.Greater); // >
        return typeParams;
    }

    /// <summary>
    /// Parses a type parameter declaration (legacy syntax, no longer generated by lexer).
    /// Was: §TP[T] - now use &lt;T&gt; suffix instead: §F{id:name:pub}&lt;T&gt;
    /// </summary>
    private TypeParameterNode ParseTypeParameter()
    {
        var startToken = Expect(TokenKind.TypeParam);
        var attrs = ParseAttributes();

        // Positional: [name]
        var name = attrs["_pos0"] ?? "";

        if (string.IsNullOrEmpty(name))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "TP", "name");
        }

        // Constraints are added later via WHERE clauses
        return new TypeParameterNode(startToken.Span, name, Array.Empty<TypeConstraintNode>());
    }

    /// <summary>
    /// Parses a WHERE clause and adds constraints to the appropriate type parameter.
    /// New syntax: §WHERE T : class, IComparable&lt;T&gt;
    /// Old syntax: §WR{T:constraint1,constraint2}
    /// </summary>
    private void ParseWhereClause(List<TypeParameterNode> typeParameters)
    {
        var startToken = Expect(TokenKind.Where);

        string typeParamName;
        var constraints = new List<string>();

        // Check for new syntax: §WHERE T : constraint1, constraint2
        // New syntax has identifier directly after §WHERE, not in braces
        if (Check(TokenKind.Identifier))
        {
            // NEW SYNTAX: §WHERE T : class, IComparable<T>
            var nameToken = Advance();
            typeParamName = nameToken.Text;

            Expect(TokenKind.Colon); // :

            // Parse constraints using specialized method that handles <> depth
            constraints = ParseWhereConstraintList();
        }
        else if (Check(TokenKind.OpenBrace))
        {
            // OLD SYNTAX: §WR{T:constraint1,constraint2}
            var attrs = ParseAttributes();

            typeParamName = attrs["_pos0"] ?? "";
            var constraintStr = attrs["_pos1"] ?? "";

            if (string.IsNullOrEmpty(typeParamName))
            {
                _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "WHERE", "type parameter name");
                return;
            }

            // Parse comma-separated constraints from attribute string
            constraints = constraintStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(c => c.Trim())
                .Where(c => !string.IsNullOrEmpty(c))
                .ToList();
        }
        else
        {
            _diagnostics.ReportUnexpectedToken(Current.Span, "type parameter name or {", Current.Kind);
            return;
        }

        // Find the type parameter to add the constraint to
        var typeParamIndex = typeParameters.FindIndex(tp => tp.Name == typeParamName);
        if (typeParamIndex < 0)
        {
            _diagnostics.ReportError(startToken.Span, DiagnosticCode.TypeParameterNotFound,
                $"Type parameter '{typeParamName}' not found");
            return;
        }

        // Convert constraint strings to TypeConstraintNodes
        var newConstraints = typeParameters[typeParamIndex].Constraints.ToList();

        foreach (var constraintText in constraints)
        {
            var (kind, typeName) = constraintText.ToLowerInvariant() switch
            {
                "class" => (TypeConstraintKind.Class, (string?)null),
                "struct" => (TypeConstraintKind.Struct, (string?)null),
                "new" or "new()" => (TypeConstraintKind.New, (string?)null),
                "notnull" => (TypeConstraintKind.NotNull, (string?)null),
                _ => (TypeConstraintKind.TypeName, constraintText)
            };

            var constraint = new TypeConstraintNode(startToken.Span, kind, typeName);
            newConstraints.Add(constraint);
        }

        // Replace the type parameter with one that includes the new constraints
        var oldTypeParam = typeParameters[typeParamIndex];
        typeParameters[typeParamIndex] = new TypeParameterNode(oldTypeParam.Span, oldTypeParam.Name, newConstraints);
    }

    /// <summary>
    /// Parses a comma-separated list of constraints in WHERE clause.
    /// Handles generic types like IComparable&lt;T&gt; correctly.
    /// </summary>
    private List<string> ParseWhereConstraintList()
    {
        var constraints = new List<string>();

        do
        {
            var constraint = ParseConstraintTypeName();
            if (!string.IsNullOrEmpty(constraint))
            {
                constraints.Add(constraint);
            }
        } while (Match(TokenKind.Comma));

        return constraints;
    }

    /// <summary>
    /// Parses a single constraint type name, including generic types.
    /// Handles: class, struct, new(), IComparable, IComparable&lt;T&gt;, List&lt;int&gt;
    /// </summary>
    private string ParseConstraintTypeName()
    {
        // Handle special keywords
        if (Check(TokenKind.Identifier))
        {
            var text = Current.Text.ToLowerInvariant();
            if (text == "class" || text == "struct" || text == "notnull")
            {
                Advance();
                return text;
            }
            if (text == "new")
            {
                Advance();
                // Check for optional ()
                if (Check(TokenKind.OpenParen))
                {
                    Advance();
                    Expect(TokenKind.CloseParen);
                }
                return "new()";
            }
        }

        // Parse type name with potential generic arguments
        var sb = new System.Text.StringBuilder();

        if (!Check(TokenKind.Identifier))
        {
            _diagnostics.ReportUnexpectedToken(Current.Span, "constraint type name", Current.Kind);
            return "";
        }

        sb.Append(Advance().Text);

        // Handle generic type arguments: <T, U>
        if (Check(TokenKind.Less))
        {
            sb.Append('<');
            Advance();

            var depth = 1;
            while (!IsAtEnd && depth > 0)
            {
                if (Check(TokenKind.Less))
                {
                    sb.Append('<');
                    depth++;
                    Advance();
                }
                else if (Check(TokenKind.Greater))
                {
                    sb.Append('>');
                    depth--;
                    Advance();
                }
                else if (Check(TokenKind.Comma))
                {
                    // Comma inside <> is a type argument separator
                    sb.Append(", ");
                    Advance();
                }
                else if (Check(TokenKind.Identifier))
                {
                    sb.Append(Current.Text);
                    Advance();
                }
                else
                {
                    // Unknown token - stop parsing
                    break;
                }
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Parses a generic type instantiation (legacy syntax, no longer generated by lexer).
    /// Was: §G[List:i32] - now use inline syntax instead: List&lt;i32&gt;
    /// </summary>
    private GenericTypeNode ParseGenericType()
    {
        var startToken = Expect(TokenKind.Generic);
        var attrs = ParseAttributes();

        // Positional: [typeName:typeArg1:typeArg2:...]
        var typeName = attrs["_pos0"] ?? "";

        if (string.IsNullOrEmpty(typeName))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "G", "type name");
        }

        // Collect type arguments from remaining positional attributes
        var typeArgs = new List<string>();
        var posCount = attrs["_posCount"];
        if (int.TryParse(posCount, out var count))
        {
            for (int i = 1; i < count; i++)
            {
                var typeArg = attrs[$"_pos{i}"];
                if (!string.IsNullOrEmpty(typeArg))
                {
                    typeArgs.Add(typeArg);
                }
            }
        }

        return new GenericTypeNode(startToken.Span, typeName, typeArgs);
    }

    // Phase 8: Classes, Interfaces, Inheritance

    /// <summary>
    /// Parses an interface definition.
    /// §IFACE[i001:IShape]
    ///   §METHOD[m001:Area] §O[f64] §E[] §/METHOD[m001]
    /// §/IFACE[i001]
    /// </summary>
    private InterfaceDefinitionNode ParseInterfaceDefinition()
    {
        var startToken = Expect(TokenKind.Interface);
        var attrs = ParseAttributes();
        var csharpAttrs = ParseCSharpAttributes();

        // Positional: [id:name]
        var id = attrs["_pos0"] ?? "";
        var name = attrs["_pos1"] ?? "";

        if (string.IsNullOrEmpty(id))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "IFACE", "id");
        }
        if (string.IsNullOrEmpty(name))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "IFACE", "name");
        }

        // NEW: Parse optional type parameters §IFACE{...}<T, U>
        var typeParameters = ParseOptionalTypeParameterList(startToken.Span);

        // Legacy: Extract type parameters from name if present (e.g., IProducer<out T>)
        if (typeParameters.Count == 0)
        {
            var typeParamStart = name.IndexOf('<');
            if (typeParamStart >= 0)
            {
                var typeParamEnd = name.LastIndexOf('>');
                if (typeParamEnd > typeParamStart)
                {
                    var typeParamStr = name.Substring(typeParamStart + 1, typeParamEnd - typeParamStart - 1);
                    name = name.Substring(0, typeParamStart);

                    foreach (var tpName in typeParamStr.Split(','))
                    {
                        var trimmed = tpName.Trim();
                        if (!string.IsNullOrEmpty(trimmed))
                        {
                            var variance = Ast.VarianceKind.None;
                            if (trimmed.StartsWith("out "))
                            {
                                variance = Ast.VarianceKind.Out;
                                trimmed = trimmed.Substring(4);
                            }
                            else if (trimmed.StartsWith("in "))
                            {
                                variance = Ast.VarianceKind.In;
                                trimmed = trimmed.Substring(3);
                            }
                            typeParameters.Add(new TypeParameterNode(startToken.Span, trimmed, Array.Empty<TypeConstraintNode>(), variance));
                        }
                    }
                }
            }
        }

        var baseInterfaces = new List<string>();
        var methods = new List<MethodSignatureNode>();
        var properties = new List<PropertyNode>();

        while (!IsAtEnd && !Check(TokenKind.EndInterface))
        {
            if (Check(TokenKind.TypeParam))
            {
                typeParameters.Add(ParseTypeParameter());
            }
            else if (Check(TokenKind.Where))
            {
                ParseWhereClause(typeParameters);
            }
            else if (Check(TokenKind.Extends))
            {
                Expect(TokenKind.Extends);
                var extAttrs = ParseAttributes();
                var baseIface = extAttrs["_pos0"] ?? "";
                if (!string.IsNullOrEmpty(baseIface))
                {
                    baseInterfaces.Add(baseIface);
                }
            }
            else if (Check(TokenKind.Method))
            {
                methods.Add(ParseMethodSignature());
            }
            else if (Check(TokenKind.Property))
            {
                properties.Add(ParseProperty());
            }
            else
            {
                _diagnostics.ReportUnexpectedToken(Current.Span, "EXT, METHOD, PROP, or END_IFACE", Current.Kind);
                Advance();
            }
        }

        var endToken = Expect(TokenKind.EndInterface);
        var endAttrs = ParseAttributes();
        var endId = endAttrs["_pos0"] ?? "";

        if (endId != id)
        {
            _diagnostics.ReportMismatchedIdWithFix(endToken.Span, "IFACE", id, "END_IFACE", endId);
        }

        var span = startToken.Span.Union(endToken.Span);
        return new InterfaceDefinitionNode(span, id, name, baseInterfaces, typeParameters, methods, properties, attrs, csharpAttrs);
    }

    /// <summary>
    /// Parses a method signature (for interfaces).
    /// §METHOD{m001:Area}&lt;T&gt;[@Obsolete] §O{f64} §/METHOD{m001}
    /// </summary>
    private MethodSignatureNode ParseMethodSignature()
    {
        var startToken = Expect(TokenKind.Method);
        var attrs = ParseAttributes();
        var csharpAttrs = ParseCSharpAttributes();

        // Positional: [id:name]
        var id = attrs["_pos0"] ?? "";
        var name = attrs["_pos1"] ?? "";

        if (string.IsNullOrEmpty(id))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "METHOD", "id");
        }

        // NEW: Parse optional type parameters §MT{...}<T, U>
        var typeParameters = ParseOptionalTypeParameterList(startToken.Span);
        var parameters = new List<ParameterNode>();
        OutputNode? output = null;
        EffectsNode? effects = null;
        var preconditions = new List<RequiresNode>();
        var postconditions = new List<EnsuresNode>();

        // Parse signature elements until END_METHOD
        while (!IsAtEnd && !Check(TokenKind.EndMethod))
        {
            if (Check(TokenKind.TypeParam))
            {
                typeParameters.Add(ParseTypeParameter());
            }
            else if (Check(TokenKind.Where))
            {
                ParseWhereClause(typeParameters);
            }
            else if (Check(TokenKind.In))
            {
                parameters.Add(ParseParameter());
            }
            else if (Check(TokenKind.Out))
            {
                output = ParseOutput();
            }
            else if (Check(TokenKind.Effects))
            {
                effects = ParseEffects();
            }
            else if (Check(TokenKind.Requires))
            {
                preconditions.Add(ParseRequires());
            }
            else if (Check(TokenKind.Ensures))
            {
                postconditions.Add(ParseEnsures());
            }
            else
            {
                break;
            }
        }

        var endToken = Expect(TokenKind.EndMethod);
        var endAttrs = ParseAttributes();
        var endId = endAttrs["_pos0"] ?? "";

        if (endId != id)
        {
            _diagnostics.ReportMismatchedIdWithFix(endToken.Span, "METHOD", id, "END_METHOD", endId);
        }

        var span = startToken.Span.Union(endToken.Span);
        return new MethodSignatureNode(span, id, name, typeParameters, parameters, output, effects, preconditions, postconditions, attrs, csharpAttrs);
    }

    /// <summary>
    /// Parses a class definition.
    /// §CLASS[c001:Shape:abs]
    ///   §IMPL[IShape]
    ///   §FLD[string:Name:pri]
    ///   §METHOD[m001:Area:pub:abs] §O[f64] §E[] §/METHOD[m001]
    /// §/CLASS[c001]
    /// </summary>
    private ClassDefinitionNode ParseClassDefinition()
    {
        var startToken = Expect(TokenKind.Class);
        var attrs = ParseAttributes();
        var csharpAttrs = ParseCSharpAttributes();

        // Positional: [id:name:modifiers?] or [id:name:baseClass:modifiers?]
        var id = attrs["_pos0"] ?? "";
        var name = attrs["_pos1"] ?? "";
        var pos2 = attrs["_pos2"] ?? "";
        var pos3 = attrs["_pos3"];

        if (string.IsNullOrEmpty(id))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "CLASS", "id");
        }
        if (string.IsNullOrEmpty(name))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "CLASS", "name");
        }

        // Disambiguate positionals:
        // 4 positionals (pos3 exists): pos2 is visibility, pos3 is modifiers — OR pos2 is baseClass, pos3 is modifiers
        // 3 positionals (pos3 null): if pos2 is all known modifiers/visibility → modifiers; else baseClass
        string modifiers;
        string? baseClass = null;
        Visibility visibility = Visibility.Internal;

        if (pos3 != null)
        {
            // 4 positionals: §CL{id:name:vis:modifiers} or §CL{id:name:BaseClass:modifiers}
            if (IsVisibilityKeyword(pos2))
            {
                // pos2 is a visibility like "pub", "int", etc.
                visibility = ParseVisibility(pos2);
                modifiers = pos3;
            }
            else
            {
                baseClass = pos2;
                modifiers = pos3;
            }
        }
        else
        {
            // 3 positionals: §CL{id:name:modifiers_or_vis_or_base}
            if (IsClassModifierOrVisibility(pos2))
            {
                // Extract visibility from the modifiers string if present
                modifiers = pos2;
                visibility = ExtractVisibilityFromModifiers(ref modifiers);
            }
            else
            {
                // Not a known modifier — treat as base class
                baseClass = string.IsNullOrEmpty(pos2) ? null : pos2;
                modifiers = "";
            }
        }

        // Token-based matching: split modifiers and check each token against known abbreviations
        var modifierTokens = modifiers.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var isAbstract = modifierTokens.Any(t => t.Equals("abs", StringComparison.OrdinalIgnoreCase)
            || t.Equals("abstract", StringComparison.OrdinalIgnoreCase));
        var isSealed = modifierTokens.Any(t => t.Equals("seal", StringComparison.OrdinalIgnoreCase)
            || t.Equals("sealed", StringComparison.OrdinalIgnoreCase));
        var isStatic = modifierTokens.Any(t => t.Equals("st", StringComparison.OrdinalIgnoreCase)
            || t.Equals("stat", StringComparison.OrdinalIgnoreCase)
            || t.Equals("static", StringComparison.OrdinalIgnoreCase));
        var isPartial = modifierTokens.Any(t => t.Equals("partial", StringComparison.OrdinalIgnoreCase));
        var isStruct = modifierTokens.Any(t => t.Equals("struct", StringComparison.OrdinalIgnoreCase));
        var isReadOnly = modifierTokens.Any(t => t.Equals("readonly", StringComparison.OrdinalIgnoreCase));

        // Structs cannot be abstract
        if (isStruct && isAbstract)
        {
            _diagnostics.ReportError(startToken.Span, DiagnosticCode.InvalidModifier,
                "Structs cannot be abstract. The 'abs' modifier will be ignored.");
            isAbstract = false;
        }
        var implementedInterfaces = new List<string>();

        // NEW: Parse optional type parameters §CL{...}<T, U>
        var typeParameters = ParseOptionalTypeParameterList(startToken.Span);

        // Also support legacy: Extract type parameters from class name if present (e.g., Repository<T>)
        if (typeParameters.Count == 0)
        {
            var typeParamStart = name.IndexOf('<');
            if (typeParamStart >= 0)
            {
                var typeParamEnd = name.LastIndexOf('>');
                if (typeParamEnd > typeParamStart)
                {
                    var typeParamStr = name.Substring(typeParamStart + 1, typeParamEnd - typeParamStart - 1);
                    name = name.Substring(0, typeParamStart);

                    // Parse type parameter names (comma-separated), with optional variance
                    foreach (var tpName in typeParamStr.Split(','))
                    {
                        var trimmedName = tpName.Trim();
                        if (!string.IsNullOrEmpty(trimmedName))
                        {
                            var variance = Ast.VarianceKind.None;
                            if (trimmedName.StartsWith("out "))
                            {
                                variance = Ast.VarianceKind.Out;
                                trimmedName = trimmedName.Substring(4);
                            }
                            else if (trimmedName.StartsWith("in "))
                            {
                                variance = Ast.VarianceKind.In;
                                trimmedName = trimmedName.Substring(3);
                            }
                            typeParameters.Add(new TypeParameterNode(startToken.Span, trimmedName, Array.Empty<TypeConstraintNode>(), variance));
                        }
                    }
                }
            }
        }
        var fields = new List<ClassFieldNode>();
        var properties = new List<PropertyNode>();
        var constructors = new List<ConstructorNode>();
        var methods = new List<MethodNode>();
        var events = new List<EventDefinitionNode>();
        var interopBlocks = new List<CSharpInteropBlockNode>();

        while (!IsAtEnd && !Check(TokenKind.EndClass))
        {
            if (Check(TokenKind.TypeParam))
            {
                typeParameters.Add(ParseTypeParameter());
            }
            else if (Check(TokenKind.Where))
            {
                ParseWhereClause(typeParameters);
            }
            else if (Check(TokenKind.Extends))
            {
                Expect(TokenKind.Extends);
                var extAttrs = ParseAttributes();
                baseClass = extAttrs["_pos0"] ?? "";
            }
            else if (Check(TokenKind.Implements))
            {
                Expect(TokenKind.Implements);
                var implAttrs = ParseAttributes();
                var iface = implAttrs["_pos0"] ?? "";
                if (!string.IsNullOrEmpty(iface))
                {
                    implementedInterfaces.Add(iface);
                }
            }
            else if (Check(TokenKind.FieldDef))
            {
                fields.Add(ParseClassField());
            }
            else if (Check(TokenKind.Property))
            {
                properties.Add(ParseProperty());
            }
            else if (Check(TokenKind.Constructor))
            {
                constructors.Add(ParseConstructor());
            }
            else if (Check(TokenKind.Method))
            {
                methods.Add(ParseMethodDefinition());
            }
            else if (Check(TokenKind.AsyncMethod))
            {
                methods.Add(ParseAsyncMethodDefinition());
            }
            else if (Check(TokenKind.Event))
            {
                events.Add(ParseEventDefinition());
            }
            else if (Check(TokenKind.CSharpInterop))
            {
                interopBlocks.Add(ParseCSharpInteropBlock());
            }
            else
            {
                _diagnostics.ReportUnexpectedToken(Current.Span, "TP, WHERE, EXT, IMPL, FLD, PROP, CTOR, METHOD, AMT, EVT, CSHARP, or END_CLASS", Current.Kind);
                Advance();
            }
        }

        var endToken = Expect(TokenKind.EndClass);
        var endAttrs = ParseAttributes();
        var endId = endAttrs["_pos0"] ?? "";

        if (endId != id)
        {
            _diagnostics.ReportMismatchedIdWithFix(endToken.Span, "CLASS", id, "END_CLASS", endId);
        }

        var span = startToken.Span.Union(endToken.Span);
        return new ClassDefinitionNode(span, id, name, isAbstract, isSealed, isPartial, isStatic, baseClass,
            implementedInterfaces, typeParameters, fields, properties, constructors, methods, events, attrs, csharpAttrs,
            isStruct: isStruct, isReadOnly: isReadOnly, visibility: visibility, interopBlocks: interopBlocks);
    }

    /// <summary>
    /// Parses a C# interop block.
    /// §CSHARP{...}§/CSHARP
    /// </summary>
    private CSharpInteropBlockNode ParseCSharpInteropBlock()
    {
        var token = Expect(TokenKind.CSharpInterop);
        var content = token.Value as string ?? "";
        return new CSharpInteropBlockNode(token.Span, content);
    }

    /// <summary>
    /// Parses a class field.
    /// §FLD[string:Name:pri][@JsonIgnore]
    /// </summary>
    private ClassFieldNode ParseClassField()
    {
        var startToken = Expect(TokenKind.FieldDef);
        var attrs = ParseAttributes();
        var csharpAttrs = ParseCSharpAttributes();

        // Positional: [type:name:visibility?:modifiers?]
        var typeName = attrs["_pos0"] ?? "object";
        var name = attrs["_pos1"] ?? "";
        var visStr = attrs["_pos2"] ?? "private";
        var modStr = attrs["_pos3"] ?? "";

        if (string.IsNullOrEmpty(name))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "FLD", "name");
        }

        var visibility = ParseVisibility(visStr);
        var fieldModifiers = ParseMethodModifiers(modStr);

        // Check for optional default value (can be prefixed with = or just a direct expression)
        ExpressionNode? defaultValue = null;
        if (Check(TokenKind.Equals))
        {
            Advance(); // consume =
            defaultValue = ParseExpression();
        }
        else if (IsExpressionStart())
        {
            defaultValue = ParseExpression();
        }

        var span = defaultValue != null ? startToken.Span.Union(defaultValue.Span) : startToken.Span;
        return new ClassFieldNode(span, name, typeName, visibility, fieldModifiers, defaultValue, attrs, csharpAttrs);
    }

    /// <summary>
    /// Parses a method definition (with body).
    /// §METHOD[m001:Area:pub:over][@HttpPost][@Authorize]
    ///   §O[f64] §E[]
    ///   §R §OP[kind=mul] 3.14159 §REF[name=Radius] §REF[name=Radius]
    /// §/METHOD[m001]
    /// </summary>
    private MethodNode ParseMethodDefinition()
    {
        var startToken = Expect(TokenKind.Method);
        var attrs = ParseAttributes();
        var csharpAttrs = ParseCSharpAttributes();

        // Positional: [id:name:visibility?:modifiers?]
        var id = attrs["_pos0"] ?? "";
        var name = attrs["_pos1"] ?? "";
        var visStr = attrs["_pos2"] ?? "private";
        var modStr = attrs["_pos3"] ?? "";

        if (string.IsNullOrEmpty(id))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "METHOD", "id");
        }

        var visibility = ParseVisibility(visStr);
        var modifiers = ParseMethodModifiers(modStr);

        // NEW: Parse optional type parameters §MT{...}<T, U>
        var typeParameters = ParseOptionalTypeParameterList(startToken.Span);

        // Also support legacy: Extract type parameters from method name if present (e.g., Create<T>)
        if (typeParameters.Count == 0)
        {
            var typeParamStart = name.IndexOf('<');
            if (typeParamStart >= 0)
            {
                var typeParamEnd = name.LastIndexOf('>');
                if (typeParamEnd > typeParamStart)
                {
                    var typeParamStr = name.Substring(typeParamStart + 1, typeParamEnd - typeParamStart - 1);
                    name = name.Substring(0, typeParamStart);

                    foreach (var tpName in typeParamStr.Split(','))
                    {
                        var trimmedName = tpName.Trim();
                        if (!string.IsNullOrEmpty(trimmedName))
                        {
                            typeParameters.Add(new TypeParameterNode(startToken.Span, trimmedName, Array.Empty<TypeConstraintNode>()));
                        }
                    }
                }
            }
        }

        var parameters = new List<ParameterNode>();
        OutputNode? output = null;
        EffectsNode? effects = null;
        var preconditions = new List<RequiresNode>();
        var postconditions = new List<EnsuresNode>();
        var body = new List<StatementNode>();

        // Parse signature and body until END_METHOD
        while (!IsAtEnd && !Check(TokenKind.EndMethod))
        {
            if (Check(TokenKind.TypeParam))
            {
                typeParameters.Add(ParseTypeParameter());
            }
            else if (Check(TokenKind.Where))
            {
                ParseWhereClause(typeParameters);
            }
            else if (Check(TokenKind.In))
            {
                parameters.Add(ParseParameter());
            }
            else if (Check(TokenKind.Out))
            {
                output = ParseOutput();
            }
            else if (Check(TokenKind.Effects))
            {
                effects = ParseEffects();
            }
            else if (Check(TokenKind.Requires))
            {
                preconditions.Add(ParseRequires());
            }
            else if (Check(TokenKind.Ensures))
            {
                postconditions.Add(ParseEnsures());
            }
            else
            {
                // Must be body statements
                var stmt = ParseStatement();
                if (stmt != null)
                {
                    body.Add(stmt);
                }
            }
        }

        var endToken = Expect(TokenKind.EndMethod);
        var endAttrs = ParseAttributes();
        var endId = endAttrs["_pos0"] ?? "";

        if (endId != id)
        {
            _diagnostics.ReportMismatchedIdWithFix(endToken.Span, "METHOD", id, "END_METHOD", endId);
        }

        var span = startToken.Span.Union(endToken.Span);
        return new MethodNode(span, id, name, visibility, modifiers, typeParameters, parameters,
            output, effects, preconditions, postconditions, body, attrs, csharpAttrs);
    }

    /// <summary>
    /// Parses an async method definition.
    /// §AMT{id:name:visibility:modifiers?}[@Attribute]
    ///   §I{type:name} ...
    ///   §O{type}
    ///   ... body ...
    /// §/AMT{id}
    /// </summary>
    private MethodNode ParseAsyncMethodDefinition()
    {
        var startToken = Expect(TokenKind.AsyncMethod);
        var attrs = ParseAttributes();
        var csharpAttrs = ParseCSharpAttributes();

        // Positional: [id:name:visibility?:modifiers?]
        var id = attrs["_pos0"] ?? "";
        var name = attrs["_pos1"] ?? "";
        var visStr = attrs["_pos2"] ?? "private";
        var modStr = attrs["_pos3"] ?? "";

        if (string.IsNullOrEmpty(id))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "AMT", "id");
        }

        var visibility = ParseVisibility(visStr);
        var modifiers = ParseMethodModifiers(modStr);

        // Parse optional type parameters §AMT{...}<T, U>
        var typeParameters = ParseOptionalTypeParameterList(startToken.Span);

        // Support legacy: Extract type parameters from method name if present
        if (typeParameters.Count == 0)
        {
            var typeParamStart = name.IndexOf('<');
            if (typeParamStart >= 0)
            {
                var typeParamEnd = name.LastIndexOf('>');
                if (typeParamEnd > typeParamStart)
                {
                    var typeParamStr = name.Substring(typeParamStart + 1, typeParamEnd - typeParamStart - 1);
                    name = name.Substring(0, typeParamStart);

                    foreach (var tpName in typeParamStr.Split(','))
                    {
                        var trimmedName = tpName.Trim();
                        if (!string.IsNullOrEmpty(trimmedName))
                        {
                            typeParameters.Add(new TypeParameterNode(startToken.Span, trimmedName, Array.Empty<TypeConstraintNode>()));
                        }
                    }
                }
            }
        }

        var parameters = new List<ParameterNode>();
        OutputNode? output = null;
        EffectsNode? effects = null;
        var preconditions = new List<RequiresNode>();
        var postconditions = new List<EnsuresNode>();
        var body = new List<StatementNode>();

        // Parse signature and body until END_AMT
        while (!IsAtEnd && !Check(TokenKind.EndAsyncMethod))
        {
            if (Check(TokenKind.TypeParam))
            {
                typeParameters.Add(ParseTypeParameter());
            }
            else if (Check(TokenKind.Where))
            {
                ParseWhereClause(typeParameters);
            }
            else if (Check(TokenKind.In))
            {
                parameters.Add(ParseParameter());
            }
            else if (Check(TokenKind.Out))
            {
                output = ParseOutput();
            }
            else if (Check(TokenKind.Effects))
            {
                effects = ParseEffects();
            }
            else if (Check(TokenKind.Requires))
            {
                preconditions.Add(ParseRequires());
            }
            else if (Check(TokenKind.Ensures))
            {
                postconditions.Add(ParseEnsures());
            }
            else
            {
                // Must be body statements
                var stmt = ParseStatement();
                if (stmt != null)
                {
                    body.Add(stmt);
                }
            }
        }

        var endToken = Expect(TokenKind.EndAsyncMethod);
        var endAttrs = ParseAttributes();
        var endId = endAttrs["_pos0"] ?? "";

        if (endId != id)
        {
            _diagnostics.ReportMismatchedIdWithFix(endToken.Span, "AMT", id, "END_AMT", endId);
        }

        var span = startToken.Span.Union(endToken.Span);
        return new MethodNode(span, id, name, visibility, modifiers, typeParameters, parameters,
            output, effects, preconditions, postconditions, body, attrs, csharpAttrs, isAsync: true);
    }

    private static MethodModifiers ParseMethodModifiers(string modStr)
    {
        var mods = MethodModifiers.None;
        // Token-based matching: split on commas/spaces and check each token
        var tokens = modStr.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
        {
            if (token.Equals("vr", StringComparison.OrdinalIgnoreCase)
                || token.Equals("virt", StringComparison.OrdinalIgnoreCase)
                || token.Equals("virtual", StringComparison.OrdinalIgnoreCase))
                mods |= MethodModifiers.Virtual;
            else if (token.Equals("ov", StringComparison.OrdinalIgnoreCase)
                || token.Equals("over", StringComparison.OrdinalIgnoreCase)
                || token.Equals("override", StringComparison.OrdinalIgnoreCase))
                mods |= MethodModifiers.Override;
            else if (token.Equals("abs", StringComparison.OrdinalIgnoreCase)
                || token.Equals("abstract", StringComparison.OrdinalIgnoreCase))
                mods |= MethodModifiers.Abstract;
            else if (token.Equals("seal", StringComparison.OrdinalIgnoreCase)
                || token.Equals("sealed", StringComparison.OrdinalIgnoreCase))
                mods |= MethodModifiers.Sealed;
            else if (token.Equals("st", StringComparison.OrdinalIgnoreCase)
                || token.Equals("stat", StringComparison.OrdinalIgnoreCase)
                || token.Equals("static", StringComparison.OrdinalIgnoreCase))
                mods |= MethodModifiers.Static;
            else if (token.Equals("const", StringComparison.OrdinalIgnoreCase))
                mods |= MethodModifiers.Const;
            else if (token.Equals("readonly", StringComparison.OrdinalIgnoreCase))
                mods |= MethodModifiers.Readonly;
            else if (token.Equals("req", StringComparison.OrdinalIgnoreCase)
                || token.Equals("required", StringComparison.OrdinalIgnoreCase))
                mods |= MethodModifiers.Required;
            else if (token.Equals("part", StringComparison.OrdinalIgnoreCase)
                || token.Equals("partial", StringComparison.OrdinalIgnoreCase))
                mods |= MethodModifiers.Partial;
        }
        return mods;
    }

    /// <summary>
    /// Parses a new expression.
    /// §NEW[Circle] §A "MyCircle" §A 5.0 §/NEW
    /// </summary>
    private ExpressionNode ParseNewExpression()
    {
        var startToken = Expect(TokenKind.New);
        var attrs = ParseAttributes();

        // Positional: [typeName:typeArg1:typeArg2:...]
        var rawTypeName = attrs["_pos0"] ?? "object";

        string typeName;
        var typeArgs = new List<string>();

        // Handle generic types in _pos0 like "Dictionary<char,str>"
        var angleIndex = rawTypeName.IndexOf('<');
        if (angleIndex >= 0 && rawTypeName.EndsWith('>'))
        {
            typeName = rawTypeName[..angleIndex];
            var innerArgs = rawTypeName[(angleIndex + 1)..^1]; // "char,str"
            typeArgs.AddRange(innerArgs.Split(',').Select(a => a.Trim()));
        }
        else
        {
            typeName = rawTypeName;
            // Collect type arguments from positional params (existing behavior: §NEW{Dict:char:str})
            var posCount = attrs["_posCount"];
            if (int.TryParse(posCount, out var count))
            {
                for (int i = 1; i < count; i++)
                {
                    var typeArg = attrs[$"_pos{i}"];
                    if (!string.IsNullOrEmpty(typeArg))
                    {
                        typeArgs.Add(typeArg);
                    }
                }
            }
        }

        // Handle optional empty parens: §NEW{X}() — consume silently
        if (Check(TokenKind.OpenParen))
        {
            var saved = _position;
            Advance(); // consume '('
            if (Check(TokenKind.CloseParen))
            {
                Advance(); // consume ')'
            }
            else
            {
                _position = saved; // not empty parens — backtrack
            }
        }

        // Parse arguments — only when not inside an argument context
        // (avoids stealing §A tokens that belong to the enclosing §C)
        var arguments = new List<ExpressionNode>();
        if (!_insideArgContext)
        {
            while (Check(TokenKind.Arg))
            {
                arguments.Add(ParseArgument());
            }
        }

        // Parse object initializer assignments:
        // Format 1: §INIT{PropName} expression (tag-based)
        // Format 2: PropName = expression (inline, between §NEW and §/NEW)
        var initializers = new List<ObjectInitializerAssignment>();
        while (Check(TokenKind.Init))
        {
            Advance(); // consume §INIT
            var initAttrs = ParseAttributes();
            var propName = initAttrs["_pos0"] ?? "";
            var value = ParseExpression();
            initializers.Add(new ObjectInitializerAssignment(propName, value));
        }
        while (!IsAtEnd && !Check(TokenKind.EndNew) && Check(TokenKind.Identifier))
        {
            // Peek ahead to check for "Identifier =" pattern
            var saved = _position;
            var identToken = Advance();
            if (Check(TokenKind.Equals))
            {
                Advance(); // consume '='
                var value = ParseExpression();
                initializers.Add(new ObjectInitializerAssignment(identToken.Text, value));
            }
            else
            {
                // Not an initializer — backtrack
                _position = saved;
                break;
            }
        }

        // Check for optional end tag
        var endSpan = startToken.Span;
        if (Check(TokenKind.EndNew))
        {
            endSpan = Advance().Span;
        }

        var span = endSpan != startToken.Span ? startToken.Span.Union(endSpan)
            : arguments.Count > 0 ? startToken.Span.Union(arguments[^1].Span)
            : startToken.Span;
        ExpressionNode expr = new NewExpressionNode(span, typeName, typeArgs, arguments, initializers);

        // Handle trailing member access (e.g., §NEW{Type}§/NEW.Method or §NEW{Type}§/NEW?.Prop)
        return ParseTrailingMemberAccess(expr);
    }

    /// <summary>
    /// Parses an anonymous object creation.
    /// §ANON
    ///   PropertyName = expression
    ///   OtherProp = expression
    /// §/ANON
    /// Emits: new { PropertyName = expression, OtherProp = expression }
    /// </summary>
    private AnonymousObjectCreationNode ParseAnonymousObjectCreation()
    {
        var startToken = Expect(TokenKind.AnonymousObject);
        var initializers = new List<ObjectInitializerAssignment>();

        while (!IsAtEnd && !Check(TokenKind.EndAnonymousObject))
        {
            if (Check(TokenKind.Identifier))
            {
                var saved = _position;
                var identToken = Advance();
                if (Check(TokenKind.Equals))
                {
                    Advance(); // consume '='
                    var value = ParseExpression();
                    initializers.Add(new ObjectInitializerAssignment(identToken.Text, value));
                }
                else
                {
                    // Shorthand: just identifier (inferred property name, e.g., §ANON x y §/ANON → new { x, y })
                    _position = saved;
                    var ident = Advance();
                    initializers.Add(new ObjectInitializerAssignment(ident.Text,
                        new ReferenceNode(ident.Span, ident.Text)));
                }
            }
            else
            {
                // Unexpected token in anonymous object
                break;
            }
        }

        var endSpan = startToken.Span;
        if (Check(TokenKind.EndAnonymousObject))
        {
            endSpan = Advance().Span;
        }

        return new AnonymousObjectCreationNode(startToken.Span.Union(endSpan), initializers);
    }

    /// <summary>
    /// Parses a 'this' expression with optional member access.
    /// §THIS or §THIS.property
    /// </summary>
    private ExpressionNode ParseThisExpression()
    {
        var token = Expect(TokenKind.This);
        ExpressionNode expr = new ThisExpressionNode(token.Span);
        return ParseTrailingMemberAccess(expr);
    }

    /// <summary>
    /// Parses a 'base' expression with optional member access.
    /// §BASE or §BASE.property
    /// </summary>
    private ExpressionNode ParseBaseExpression()
    {
        var token = Expect(TokenKind.Base);
        ExpressionNode expr = new BaseExpressionNode(token.Span);
        return ParseTrailingMemberAccess(expr);
    }

    /// <summary>
    /// Parses a call expression.
    /// §C[target] §A arg1 §A arg2 §/C
    /// </summary>
    private ExpressionNode ParseCallExpression()
    {
        var startToken = Expect(TokenKind.Call);
        var attrs = ParseAttributes();

        // Positional: [target]
        var target = attrs["_pos0"] ?? "";

        // If no bracket attribute target and next token starts an expression,
        // parse expression-based call target: §C §NEW{object}§/NEW.GetType §/C
        if (string.IsNullOrEmpty(target) && !Check(TokenKind.Arg) && !Check(TokenKind.EndCall) && IsExpressionStart())
        {
            var targetExpr = ParseExpression();

            var exprArgs = new List<ExpressionNode>();
            while (!IsAtEnd && !Check(TokenKind.EndCall))
            {
                if (Check(TokenKind.Arg))
                {
                    exprArgs.Add(ParseArgument());
                }
                else if (IsExpressionStart())
                {
                    exprArgs.Add(ParseExpression());
                }
                else
                {
                    break;
                }
            }

            var exprEndToken = Expect(TokenKind.EndCall);
            var exprSpan = startToken.Span.Union(exprEndToken.Span);
            ExpressionNode exprCall = new ExpressionCallNode(exprSpan, targetExpr, exprArgs);
            return ParseTrailingMemberAccess(exprCall);
        }

        var arguments = new List<ExpressionNode>();

        // Parse arguments until we hit EndCall
        while (!IsAtEnd && !Check(TokenKind.EndCall))
        {
            if (Check(TokenKind.Arg))
            {
                arguments.Add(ParseArgument());
            }
            else if (IsExpressionStart())
            {
                // Single expression argument without §A prefix
                arguments.Add(ParseExpression());
            }
            else
            {
                break;
            }
        }

        var endToken = Expect(TokenKind.EndCall);
        var span = startToken.Span.Union(endToken.Span);
        ExpressionNode expr = new CallExpressionNode(span, target, arguments);

        // Handle trailing member access (e.g., §C[Method]§/C.Property)
        return ParseTrailingMemberAccess(expr);
    }

    // Phase 9: Properties and Constructors

    /// <summary>
    /// Parses a property definition.
    /// §PROP[p001:Name:string:pub][@JsonProperty("name")]
    ///   §GET
    ///   §SET[pri]
    /// §/PROP[p001]
    /// </summary>
    private PropertyNode ParseProperty()
    {
        var startToken = Expect(TokenKind.Property);
        var attrs = ParseAttributes();
        var csharpAttrs = ParseCSharpAttributes();

        // Positional: [id:name:type:visibility?:modifiers?]
        var id = attrs["_pos0"] ?? "";
        var name = attrs["_pos1"] ?? "";
        var typeName = attrs["_pos2"] ?? "object";
        var visStr = attrs["_pos3"] ?? "public";
        var modStr = attrs["_pos4"] ?? "";

        if (string.IsNullOrEmpty(id))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "PROP", "id");
        }

        var visibility = ParseVisibility(visStr);
        var modifiers = ParseMethodModifiers(modStr);

        PropertyAccessorNode? getter = null;
        PropertyAccessorNode? setter = null;
        PropertyAccessorNode? initer = null;
        ExpressionNode? defaultValue = null;

        while (!IsAtEnd && !Check(TokenKind.EndProperty))
        {
            if (Check(TokenKind.Get))
            {
                getter = ParsePropertyAccessor(PropertyAccessorNode.AccessorKind.Get);
            }
            else if (Check(TokenKind.Set))
            {
                setter = ParsePropertyAccessor(PropertyAccessorNode.AccessorKind.Set);
            }
            else if (Check(TokenKind.Init))
            {
                initer = ParsePropertyAccessor(PropertyAccessorNode.AccessorKind.Init);
            }
            else if (Check(TokenKind.Equals))
            {
                // Default value prefixed with =
                Advance(); // consume =
                defaultValue = ParseExpression();
            }
            else if (IsExpressionStart())
            {
                // Default value without = prefix
                defaultValue = ParseExpression();
            }
            else
            {
                break;
            }
        }

        var endToken = Expect(TokenKind.EndProperty);
        var endAttrs = ParseAttributes();
        var endId = endAttrs["_pos0"] ?? "";

        if (endId != id)
        {
            _diagnostics.ReportMismatchedIdWithFix(endToken.Span, "PROP", id, "END_PROP", endId);
        }

        var span = startToken.Span.Union(endToken.Span);
        return new PropertyNode(span, id, name, typeName, visibility, modifiers, getter, setter, initer, defaultValue, attrs, csharpAttrs);
    }

    /// <summary>
    /// Parses a property accessor.
    /// §GET
    /// §SET[pri]
    /// </summary>
    private PropertyAccessorNode ParsePropertyAccessor(PropertyAccessorNode.AccessorKind kind)
    {
        Token startToken;
        if (kind == PropertyAccessorNode.AccessorKind.Get)
        {
            startToken = Expect(TokenKind.Get);
        }
        else if (kind == PropertyAccessorNode.AccessorKind.Set)
        {
            startToken = Expect(TokenKind.Set);
        }
        else
        {
            startToken = Expect(TokenKind.Init);
        }

        var attrs = ParseAttributes();
        var visStr = attrs["_pos0"];
        var visibility = visStr != null ? ParseVisibility(visStr) : (Visibility?)null;

        var preconditions = new List<RequiresNode>();
        var body = new List<StatementNode>();

        // Determine the end token for this accessor
        var endTokenKind = kind switch
        {
            PropertyAccessorNode.AccessorKind.Get => TokenKind.EndGet,
            PropertyAccessorNode.AccessorKind.Set => TokenKind.EndSet,
            _ => TokenKind.EndProperty // Init doesn't have its own end token
        };

        // Parse optional preconditions and body (for non-auto properties)
        // Also stop at Equals for property default values, next accessor, or end tokens
        while (!IsAtEnd && !Check(TokenKind.Get) && !Check(TokenKind.Set) &&
               !Check(TokenKind.Init) && !Check(TokenKind.EndProperty) && !Check(TokenKind.Equals) &&
               !Check(TokenKind.EndGet) && !Check(TokenKind.EndSet))
        {
            if (Check(TokenKind.Requires))
            {
                preconditions.Add(ParseRequires());
            }
            else
            {
                var stmt = ParseStatement();
                if (stmt != null)
                {
                    body.Add(stmt);
                }
            }
        }

        // Consume the closing token if present (§/GET or §/SET)
        if (Check(endTokenKind))
        {
            Advance();
        }

        return new PropertyAccessorNode(startToken.Span, kind, visibility, preconditions, body, attrs);
    }

    /// <summary>
    /// Parses a constructor.
    /// §CTOR[ctor1:pub]
    ///   §I[string:name]
    ///   §Q §OP[kind=gt] §REF[name=radius] 0
    ///   §BASE §A §REF[name=name] §/BASE
    ///   §ASSIGN §REF[name=Radius] §REF[name=radius]
    /// §/CTOR[ctor1]
    /// </summary>
    private ConstructorNode ParseConstructor()
    {
        var startToken = Expect(TokenKind.Constructor);
        var attrs = ParseAttributes();
        var csharpAttrs = ParseCSharpAttributes();

        // Positional: [id:visibility?]
        var id = attrs["_pos0"] ?? "";
        var visStr = attrs["_pos1"] ?? "public";

        if (string.IsNullOrEmpty(id))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "CTOR", "id");
        }

        var visibility = ParseVisibility(visStr);

        var parameters = new List<ParameterNode>();
        var preconditions = new List<RequiresNode>();
        ConstructorInitializerNode? initializer = null;
        var body = new List<StatementNode>();

        while (!IsAtEnd && !Check(TokenKind.EndConstructor))
        {
            if (Check(TokenKind.In))
            {
                parameters.Add(ParseParameter());
            }
            else if (Check(TokenKind.Requires))
            {
                preconditions.Add(ParseRequires());
            }
            else if (Check(TokenKind.Base))
            {
                initializer = ParseConstructorInitializer(isBase: true);
            }
            else if (Check(TokenKind.This))
            {
                initializer = ParseConstructorInitializer(isBase: false);
            }
            else
            {
                var stmt = ParseStatement();
                if (stmt != null)
                {
                    body.Add(stmt);
                }
            }
        }

        var endToken = Expect(TokenKind.EndConstructor);
        var endAttrs = ParseAttributes();
        var endId = endAttrs["_pos0"] ?? "";

        if (endId != id)
        {
            _diagnostics.ReportMismatchedIdWithFix(endToken.Span, "CTOR", id, "END_CTOR", endId);
        }

        var span = startToken.Span.Union(endToken.Span);
        return new ConstructorNode(span, id, visibility, parameters, preconditions, initializer, body, attrs, csharpAttrs);
    }

    /// <summary>
    /// Parses a constructor initializer (: base(...) or : this(...)).
    /// </summary>
    private ConstructorInitializerNode ParseConstructorInitializer(bool isBase)
    {
        var startToken = isBase ? Expect(TokenKind.Base) : Expect(TokenKind.This);

        var arguments = new List<ExpressionNode>();

        // Parse arguments until we encounter something that's not an ARG
        while (Check(TokenKind.Arg))
        {
            arguments.Add(ParseArgument());
        }

        // Consume the closing token (§/BASE or §/THIS)
        var endTokenKind = isBase ? TokenKind.EndBase : TokenKind.EndThis;
        if (Check(endTokenKind))
        {
            Advance();
        }

        return new ConstructorInitializerNode(startToken.Span, isBase, arguments);
    }

    /// <summary>
    /// Parses an assignment statement.
    /// §ASSIGN §REF[name=Radius] §REF[name=radius]
    /// </summary>
    private AssignmentStatementNode ParseAssignmentStatement()
    {
        var startToken = Expect(TokenKind.Assign);

        var target = ParseExpression();
        var value = ParseExpression();

        var span = startToken.Span.Union(value.Span);
        return new AssignmentStatementNode(span, target, value);
    }

    // Phase 10: Try/Catch/Finally

    /// <summary>
    /// Parses a try/catch/finally statement.
    /// §TRY[try1]
    ///   ...
    /// §CATCH[IOException:ex]
    ///   ...
    /// §FINALLY
    ///   ...
    /// §/TRY[try1]
    /// </summary>
    /// <summary>
    /// Parses a using statement.
    /// §USE{id:variableName:variableType} resource-expression
    ///   ...body...
    /// §/USE{id}
    /// </summary>
    private UsingStatementNode ParseUsingStatement()
    {
        var startToken = Expect(TokenKind.Use);
        var attrs = ParseAttributes();

        // Positional: {id:variableName:variableType}
        var id = attrs["_pos0"] ?? "";
        var variableName = attrs["_pos1"];
        var variableType = attrs["_pos2"];

        if (string.IsNullOrEmpty(id))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "USE", "id");
        }

        // Parse resource expression
        var resource = ParseExpression();

        // Parse body statements
        var body = ParseStatementBlock(TokenKind.EndUse);

        var endToken = Expect(TokenKind.EndUse);
        var endAttrs = ParseAttributes();
        var endId = endAttrs["_pos0"] ?? "";

        if (endId != id)
        {
            _diagnostics.ReportMismatchedIdWithFix(endToken.Span, "USE", id, "END_USE", endId);
        }

        var span = startToken.Span.Union(endToken.Span);
        return new UsingStatementNode(span, id, variableName, variableType, resource, body);
    }

    private TryStatementNode ParseTryStatement()
    {
        var startToken = Expect(TokenKind.Try);
        var attrs = ParseAttributes();

        // Positional: [id]
        var id = attrs["_pos0"] ?? "";

        if (string.IsNullOrEmpty(id))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "TRY", "id");
        }

        // Parse try body
        var tryBody = new List<StatementNode>();
        while (!IsAtEnd && !Check(TokenKind.Catch) && !Check(TokenKind.Finally) && !Check(TokenKind.EndTry))
        {
            var stmt = ParseStatement();
            if (stmt != null)
            {
                tryBody.Add(stmt);
            }
        }

        // Parse catch clauses
        var catchClauses = new List<CatchClauseNode>();
        while (Check(TokenKind.Catch))
        {
            catchClauses.Add(ParseCatchClause());
        }

        // Parse optional finally
        List<StatementNode>? finallyBody = null;
        if (Check(TokenKind.Finally))
        {
            Expect(TokenKind.Finally);
            finallyBody = new List<StatementNode>();
            while (!IsAtEnd && !Check(TokenKind.EndTry))
            {
                var stmt = ParseStatement();
                if (stmt != null)
                {
                    finallyBody.Add(stmt);
                }
            }
        }

        var endToken = Expect(TokenKind.EndTry);
        var endAttrs = ParseAttributes();
        var endId = endAttrs["_pos0"] ?? "";

        if (endId != id)
        {
            _diagnostics.ReportMismatchedIdWithFix(endToken.Span, "TRY", id, "END_TRY", endId);
        }

        var span = startToken.Span.Union(endToken.Span);
        return new TryStatementNode(span, id, tryBody, catchClauses, finallyBody, attrs);
    }

    /// <summary>
    /// Parses a catch clause.
    /// §CATCH[IOException:ex]
    /// §CATCH
    /// </summary>
    private CatchClauseNode ParseCatchClause()
    {
        var startToken = Expect(TokenKind.Catch);
        var attrs = ParseAttributes();

        // Positional: [exceptionType:variableName?] or empty for catch-all
        var exceptionType = attrs["_pos0"];
        var variableName = attrs["_pos1"];

        // Check for when filter
        ExpressionNode? filter = null;
        if (Check(TokenKind.When))
        {
            Expect(TokenKind.When);
            filter = ParseExpression();
        }

        // Parse catch body
        var body = new List<StatementNode>();
        while (!IsAtEnd && !Check(TokenKind.Catch) && !Check(TokenKind.Finally) && !Check(TokenKind.EndTry))
        {
            var stmt = ParseStatement();
            if (stmt != null)
            {
                body.Add(stmt);
            }
        }

        var span = body.Count > 0 ? startToken.Span.Union(body[^1].Span) : startToken.Span;
        return new CatchClauseNode(span, exceptionType, variableName, filter, body, attrs);
    }

    /// <summary>
    /// Parses a throw statement.
    /// §THROW §NEW[ArgumentException] §A "Invalid" §/NEW
    /// </summary>
    private ThrowStatementNode ParseThrowStatement()
    {
        var startToken = Expect(TokenKind.Throw);

        ExpressionNode? exception = null;
        if (IsExpressionStart())
        {
            exception = ParseExpression();
        }

        var span = exception != null ? startToken.Span.Union(exception.Span) : startToken.Span;
        return new ThrowStatementNode(span, exception);
    }

    /// <summary>
    /// Parses a rethrow statement.
    /// §RETHROW
    /// </summary>
    private RethrowStatementNode ParseRethrowStatement()
    {
        var token = Expect(TokenKind.Rethrow);
        return new RethrowStatementNode(token.Span);
    }

    /// <summary>
    /// Parses a break statement.
    /// §BREAK
    /// </summary>
    private BreakStatementNode ParseBreakStatement()
    {
        var token = Expect(TokenKind.Break);
        return new BreakStatementNode(token.Span);
    }

    /// <summary>
    /// Parses a continue statement.
    /// §CONTINUE
    /// </summary>
    private ContinueStatementNode ParseContinueStatement()
    {
        var token = Expect(TokenKind.Continue);
        return new ContinueStatementNode(token.Span);
    }

    /// <summary>
    /// Parses a raw C# passthrough block.
    /// §RAW ... §/RAW (content already captured by lexer as single token)
    /// </summary>
    private RawCSharpNode ParseRawCSharpStatement()
    {
        var token = Expect(TokenKind.RawCSharp);
        var content = token.Value as string ?? "";
        return new RawCSharpNode(token.Span, content);
    }

    /// <summary>
    /// Parses a yield return statement.
    /// §YIELD expression
    /// </summary>
    private YieldReturnStatementNode ParseYieldReturnStatement()
    {
        var startToken = Expect(TokenKind.Yield);

        ExpressionNode? expression = null;
        if (IsExpressionStart())
        {
            expression = ParseExpression();
        }

        var span = expression != null ? startToken.Span.Union(expression.Span) : startToken.Span;
        return new YieldReturnStatementNode(span, expression);
    }

    /// <summary>
    /// Parses a yield break statement.
    /// §YBRK
    /// </summary>
    private YieldBreakStatementNode ParseYieldBreakStatement()
    {
        var token = Expect(TokenKind.YieldBreak);
        return new YieldBreakStatementNode(token.Span);
    }

    // Phase 11: Lambdas, Delegates, Events

    /// <summary>
    /// Parses a lambda expression.
    /// §LAM[lam1:x:i32] §OP[kind=mul] §REF[name=x] 2 §/LAM[lam1]
    /// </summary>
    private LambdaExpressionNode ParseLambdaExpression()
    {
        var startToken = Expect(TokenKind.Lambda);
        var attrs = ParseAttributes();

        // Positional: [id:param1:type1:param2:type2:...] or [id:async:param1:type1:...]
        var id = attrs["_pos0"] ?? "";
        var isAsync = false;
        var isStatic = false;

        if (string.IsNullOrEmpty(id))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "LAM", "id");
        }

        // Parse parameters from attributes
        var parameters = new List<LambdaParameterNode>();
        var posCount = attrs["_posCount"];
        if (int.TryParse(posCount, out var count))
        {
            int i = 1;
            // Check for static/async modifiers
            while (i < count)
            {
                var pos = attrs[$"_pos{i}"];
                if (pos?.Equals("static", StringComparison.OrdinalIgnoreCase) == true)
                {
                    isStatic = true;
                    i++;
                }
                else if (pos?.Equals("async", StringComparison.OrdinalIgnoreCase) == true)
                {
                    isAsync = true;
                    i++;
                }
                else
                {
                    break;
                }
            }

            // Parse parameter pairs: name:type
            while (i < count)
            {
                var paramName = attrs[$"_pos{i}"];
                var paramType = attrs[$"_pos{i + 1}"];

                if (!string.IsNullOrEmpty(paramName))
                {
                    parameters.Add(new LambdaParameterNode(startToken.Span, paramName, paramType));
                }
                i += 2;
            }
        }

        // Parse optional effects
        EffectsNode? effects = null;
        if (Check(TokenKind.Effects))
        {
            effects = ParseEffects();
        }

        // Parse body - either expression or statements
        ExpressionNode? expressionBody = null;
        List<StatementNode>? statementBody = null;

        if (IsExpressionStart() && !Check(TokenKind.EndLambda))
        {
            expressionBody = ParseExpression();
        }

        // Check if there are more statements after the expression
        while (!IsAtEnd && !Check(TokenKind.EndLambda))
        {
            if (statementBody == null)
            {
                statementBody = new List<StatementNode>();
            }
            var stmt = ParseStatement();
            if (stmt != null)
            {
                statementBody.Add(stmt);
            }
        }

        var endToken = Expect(TokenKind.EndLambda);
        var endAttrs = ParseAttributes();
        var endId = endAttrs["_pos0"] ?? "";

        if (endId != id)
        {
            _diagnostics.ReportMismatchedIdWithFix(endToken.Span, "LAM", id, "END_LAM", endId);
        }

        var span = startToken.Span.Union(endToken.Span);
        return new LambdaExpressionNode(span, id, parameters, effects, isAsync, expressionBody, statementBody, attrs, isStatic);
    }

    /// <summary>
    /// Parses an event subscribe statement.
    /// §SUB §REF[name=button.Click] §REF[name=handler]
    /// </summary>
    private EventSubscribeNode ParseEventSubscribe()
    {
        var startToken = Expect(TokenKind.Subscribe);

        var @event = ParseExpression();
        var handler = ParseExpression();

        var span = startToken.Span.Union(handler.Span);
        return new EventSubscribeNode(span, @event, handler);
    }

    /// <summary>
    /// Parses an event unsubscribe statement.
    /// §UNSUB §REF[name=button.Click] §REF[name=handler]
    /// </summary>
    private EventUnsubscribeNode ParseEventUnsubscribe()
    {
        var startToken = Expect(TokenKind.Unsubscribe);

        var @event = ParseExpression();
        var handler = ParseExpression();

        var span = startToken.Span.Union(handler.Span);
        return new EventUnsubscribeNode(span, @event, handler);
    }

    /// <summary>
    /// Parses a delegate definition.
    /// §DEL[d001:Processor] §I[string:input] §O[bool] §E[fr,fw] §/DEL[d001]
    /// </summary>
    private DelegateDefinitionNode ParseDelegateDefinition()
    {
        var startToken = Expect(TokenKind.Delegate);
        var attrs = ParseAttributes();

        // Positional: [id:name]
        var id = attrs["_pos0"] ?? "";
        var name = attrs["_pos1"] ?? "";

        if (string.IsNullOrEmpty(id))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "DEL", "id");
        }
        if (string.IsNullOrEmpty(name))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "DEL", "name");
        }

        var parameters = new List<ParameterNode>();
        OutputNode? output = null;
        EffectsNode? effects = null;

        // Parse parameters, output, and effects until END_DEL
        while (!IsAtEnd && !Check(TokenKind.EndDelegate))
        {
            if (Check(TokenKind.In))
            {
                parameters.Add(ParseParameter());
            }
            else if (Check(TokenKind.Out))
            {
                output = ParseOutput();
            }
            else if (Check(TokenKind.Effects))
            {
                effects = ParseEffects();
            }
            else
            {
                _diagnostics.ReportUnexpectedToken(Current.Span, "I, O, E, or END_DEL", Current.Kind);
                Advance();
            }
        }

        var endToken = Expect(TokenKind.EndDelegate);
        var endAttrs = ParseAttributes();
        var endId = endAttrs["_pos0"] ?? "";

        if (endId != id)
        {
            _diagnostics.ReportMismatchedIdWithFix(endToken.Span, "DEL", id, "END_DEL", endId);
        }

        var span = startToken.Span.Union(endToken.Span);
        return new DelegateDefinitionNode(span, id, name, parameters, output, effects, attrs);
    }

    /// <summary>
    /// Parses an event definition.
    /// §EVT[e001:Click:pub:EventHandler]
    /// </summary>
    private EventDefinitionNode ParseEventDefinition()
    {
        var startToken = Expect(TokenKind.Event);
        var attrs = ParseAttributes();

        // Positional: [id:name:visibility:delegateType]
        var id = attrs["_pos0"] ?? "";
        var name = attrs["_pos1"] ?? "";
        var visStr = attrs["_pos2"] ?? "private";
        var delegateType = attrs["_pos3"] ?? "";

        if (string.IsNullOrEmpty(id))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "EVT", "id");
        }
        if (string.IsNullOrEmpty(name))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "EVT", "name");
        }
        if (string.IsNullOrEmpty(delegateType))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "EVT", "delegateType");
        }

        var visibility = ParseVisibility(visStr);

        return new EventDefinitionNode(startToken.Span, id, name, visibility, delegateType, attrs);
    }

    // Phase 12: Async/Await

    /// <summary>
    /// Parses an await expression.
    /// §AWAIT §C[client.GetStringAsync] §A §REF[name=url] §/C
    /// §AWAIT[false] §C[reader.ReadAsync] §/C   // ConfigureAwait(false)
    /// </summary>
    private AwaitExpressionNode ParseAwaitExpression()
    {
        var startToken = Expect(TokenKind.Await);
        var attrs = ParseAttributes();

        // Check for ConfigureAwait attribute: §AWAIT[false] or §AWAIT[true]
        bool? configureAwait = null;
        var configValue = attrs["_pos0"];
        if (!string.IsNullOrEmpty(configValue))
        {
            if (configValue.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                configureAwait = false;
            }
            else if (configValue.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                configureAwait = true;
            }
        }

        // Parse the awaited expression
        var awaited = ParseExpression();

        var span = startToken.Span.Union(awaited.Span);
        return new AwaitExpressionNode(span, awaited, configureAwait);
    }

    // Phase 9: String Interpolation and Modern Operators

    /// <summary>
    /// Parses an interpolated string.
    /// §INTERP["Hello, " §EXP §REF[name=name] "!"] §/INTERP
    /// </summary>
    private InterpolatedStringNode ParseInterpolatedString()
    {
        var startToken = Expect(TokenKind.Interpolate);
        var attrs = ParseAttributes();

        var parts = new List<InterpolatedStringPartNode>();

        // Parse parts until we hit the end tag
        while (!IsAtEnd && !Check(TokenKind.EndInterpolate))
        {
            if (Check(TokenKind.StrLiteral))
            {
                var strToken = Expect(TokenKind.StrLiteral);
                var text = strToken.Value as string ?? "";
                parts.Add(new InterpolatedStringTextNode(strToken.Span, text));
            }
            else if (Check(TokenKind.Expression))
            {
                Advance(); // consume §EXP
                var expr = ParseExpression();
                parts.Add(new InterpolatedStringExpressionNode(expr.Span, expr));
            }
            else
            {
                // Try to parse any other expression
                var expr = ParseExpression();
                parts.Add(new InterpolatedStringExpressionNode(expr.Span, expr));
            }
        }

        var endToken = Expect(TokenKind.EndInterpolate);
        var span = startToken.Span.Union(endToken.Span);
        return new InterpolatedStringNode(span, parts);
    }

    /// <summary>
    /// Parses a null-coalescing expression.
    /// §?? §REF[name=value] "default"
    /// </summary>
    private NullCoalesceNode ParseNullCoalesce()
    {
        var startToken = Expect(TokenKind.NullCoalesce);

        var left = ParseExpression();
        var right = ParseExpression();

        var span = startToken.Span.Union(right.Span);
        return new NullCoalesceNode(span, left, right);
    }

    /// <summary>
    /// Parses a null-conditional access expression.
    /// §?. §REF[name=person] Name
    /// </summary>
    private NullConditionalNode ParseNullConditional()
    {
        var startToken = Expect(TokenKind.NullConditional);

        var target = ParseExpression();

        // Parse the member name (identifier)
        var memberToken = Expect(TokenKind.Identifier);
        var memberName = memberToken.Text;

        var span = startToken.Span.Union(memberToken.Span);
        return new NullConditionalNode(span, target, memberName);
    }

    /// <summary>
    /// Parses a range expression.
    /// §RANGE 1 5
    /// §RANGE §^ 1   (open start to ^1)
    /// </summary>
    private RangeExpressionNode ParseRangeExpression()
    {
        var startToken = Expect(TokenKind.RangeOp);

        ExpressionNode? start = null;
        ExpressionNode? end = null;

        // Parse start if present
        if (IsExpressionStart())
        {
            start = ParseExpression();
        }

        // Parse end if present
        if (IsExpressionStart())
        {
            end = ParseExpression();
        }

        var span = end != null ? startToken.Span.Union(end.Span)
                 : start != null ? startToken.Span.Union(start.Span)
                 : startToken.Span;
        return new RangeExpressionNode(span, start, end);
    }

    /// <summary>
    /// Parses an index-from-end expression.
    /// §^ 1
    /// </summary>
    private IndexFromEndNode ParseIndexFromEnd()
    {
        var startToken = Expect(TokenKind.IndexEnd);

        var offset = ParseExpression();

        var span = startToken.Span.Union(offset.Span);
        return new IndexFromEndNode(span, offset);
    }

    // Phase 10: Advanced Patterns

    /// <summary>
    /// Parses a with-expression for non-destructive mutation.
    /// §WITH §REF[name=person]
    ///   §SET[Name] "New Name"
    /// §/WITH
    /// </summary>
    private WithExpressionNode ParseWithExpression()
    {
        var startToken = Expect(TokenKind.With);

        // Parse the target expression
        var target = ParseExpression();

        // Parse property assignments
        var assignments = new List<WithPropertyAssignmentNode>();
        while (!IsAtEnd && !Check(TokenKind.EndWith))
        {
            if (Check(TokenKind.Set))
            {
                var setToken = Expect(TokenKind.Set);
                var setAttrs = ParseAttributes();
                var propName = setAttrs["_pos0"] ?? "";
                var value = ParseExpression();
                var assignSpan = setToken.Span.Union(value.Span);
                assignments.Add(new WithPropertyAssignmentNode(assignSpan, propName, value));
            }
            else
            {
                // Skip unknown tokens
                Advance();
            }
        }

        var endToken = Expect(TokenKind.EndWith);
        var span = startToken.Span.Union(endToken.Span);
        return new WithExpressionNode(span, target, assignments);
    }

    /// <summary>
    /// Parses a positional pattern.
    /// §PPOS[Point] §VAR[x] §VAR[y]
    /// </summary>
    private PositionalPatternNode ParsePositionalPattern()
    {
        var startToken = Expect(TokenKind.PositionalPattern);
        var attrs = ParseAttributes();
        var typeName = attrs["_pos0"] ?? "";

        var patterns = new List<PatternNode>();
        while (!IsAtEnd && IsPatternStart())
        {
            patterns.Add(ParsePattern());
        }

        var span = patterns.Count > 0
            ? startToken.Span.Union(patterns[^1].Span)
            : startToken.Span;
        return new PositionalPatternNode(span, typeName, patterns);
    }

    /// <summary>
    /// Parses a property pattern.
    /// §PPROP[Person] §PMATCH[Age] §PREL[gte] 18
    /// </summary>
    private PropertyPatternNode ParsePropertyPattern()
    {
        var startToken = Expect(TokenKind.PropertyPattern);
        var attrs = ParseAttributes();
        var typeName = attrs["_pos0"];

        var matches = new List<PropertyMatchNode>();
        while (!IsAtEnd && Check(TokenKind.PropertyMatch))
        {
            var matchToken = Expect(TokenKind.PropertyMatch);
            var matchAttrs = ParseAttributes();
            var propName = matchAttrs["_pos0"] ?? "";
            var pattern = ParsePattern();
            var matchSpan = matchToken.Span.Union(pattern.Span);
            matches.Add(new PropertyMatchNode(matchSpan, propName, pattern));
        }

        var span = matches.Count > 0
            ? startToken.Span.Union(matches[^1].Span)
            : startToken.Span;
        return new PropertyPatternNode(span, typeName, matches);
    }

    /// <summary>
    /// Parses a relational pattern.
    /// §PREL[gte] 18
    /// </summary>
    private RelationalPatternNode ParseRelationalPattern()
    {
        var startToken = Expect(TokenKind.RelationalPattern);
        var attrs = ParseAttributes();
        var op = attrs["_pos0"] ?? "eq";

        var value = ParseExpression();

        var span = startToken.Span.Union(value.Span);
        return new RelationalPatternNode(span, op, value);
    }

    /// <summary>
    /// Parses a list pattern.
    /// §PLIST §VAR[first] §REST[rest]
    /// </summary>
    private ListPatternNode ParseListPattern()
    {
        var startToken = Expect(TokenKind.ListPattern);

        var patterns = new List<PatternNode>();
        VarPatternNode? slicePattern = null;

        while (!IsAtEnd && (IsPatternStart() || Check(TokenKind.Rest)))
        {
            if (Check(TokenKind.Rest))
            {
                var restToken = Expect(TokenKind.Rest);
                var restAttrs = ParseAttributes();
                var restName = restAttrs["_pos0"] ?? "_";
                slicePattern = new VarPatternNode(restToken.Span, restName);
            }
            else
            {
                patterns.Add(ParsePattern());
            }
        }

        var span = slicePattern != null
            ? startToken.Span.Union(slicePattern.Span)
            : patterns.Count > 0
                ? startToken.Span.Union(patterns[^1].Span)
                : startToken.Span;
        return new ListPatternNode(span, patterns, slicePattern);
    }

    /// <summary>
    /// Parses a var pattern.
    /// §VAR[x]
    /// </summary>
    private VarPatternNode ParseVarPattern()
    {
        var token = Expect(TokenKind.Var);
        var attrs = ParseAttributes();
        var name = attrs["_pos0"] ?? "_";
        return new VarPatternNode(token.Span, name);
    }

    /// <summary>
    /// Parses a constant pattern (literal value).
    /// </summary>
    private ConstantPatternNode ParseConstantPattern()
    {
        var expr = ParseExpression();
        return new ConstantPatternNode(expr.Span, expr);
    }

    /// <summary>
    /// Checks if the current token starts a pattern.
    /// </summary>
    private bool IsPatternStart()
    {
        return Current.Kind is TokenKind.PositionalPattern
            or TokenKind.PropertyPattern
            or TokenKind.ListPattern
            or TokenKind.Var
            or TokenKind.RelationalPattern
            || IsExpressionStart();
    }

    #region Extended Features Parsing

    /// <summary>
    /// Parses an inline example/test.
    /// §EX (Add 2 3) → 5
    /// §EX[ex001:msg:"edge case"] (Add 0 0) → 0
    /// </summary>
    private ExampleNode ParseExample()
    {
        var startToken = Expect(TokenKind.Example);
        var attrs = ParseAttributes();
        var (id, message) = AttributeHelper.InterpretExampleAttributes(attrs);

        var expression = ParseExpression();
        Expect(TokenKind.Arrow);
        var expected = ParseExpression();

        var span = startToken.Span.Union(expected.Span);
        return new ExampleNode(span, id, expression, expected, message, attrs);
    }

    /// <summary>
    /// Parses a TODO issue marker.
    /// §TODO[t001:perf:high] "Optimize for large n"
    /// </summary>
    private IssueNode ParseTodoIssue()
    {
        return ParseIssue(IssueKind.Todo, TokenKind.Todo);
    }

    /// <summary>
    /// Parses a FIXME issue marker.
    /// §FIXME[x001:bug:critical] "Integer overflow"
    /// </summary>
    private IssueNode ParseFixmeIssue()
    {
        return ParseIssue(IssueKind.Fixme, TokenKind.Fixme);
    }

    /// <summary>
    /// Parses a HACK issue marker.
    /// §HACK[h001] "Workaround for API bug"
    /// </summary>
    private IssueNode ParseHackIssue()
    {
        return ParseIssue(IssueKind.Hack, TokenKind.Hack);
    }

    private IssueNode ParseIssue(IssueKind kind, TokenKind tokenKind)
    {
        var startToken = Expect(tokenKind);
        var attrs = ParseAttributes();
        var (id, category, priority) = AttributeHelper.InterpretIssueAttributes(attrs);

        // Expect a string description
        string description = "";
        if (Check(TokenKind.StrLiteral))
        {
            var strToken = Advance();
            description = (string)strToken.Value!;
        }
        else
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, kind.ToString().ToUpperInvariant(), "description");
        }

        return new IssueNode(startToken.Span, kind, id, category, priority, description, attrs);
    }

    /// <summary>
    /// Parses a USES dependency declaration.
    /// §USES[ValidateOrder, CalculateTotal, SaveOrder]
    /// </summary>
    private UsesNode ParseUses()
    {
        var startToken = Expect(TokenKind.Uses);
        var attrs = ParseAttributes();
        var targets = AttributeHelper.InterpretDependencyListAttributes(attrs);

        var dependencies = new List<DependencyNode>();
        foreach (var target in targets)
        {
            var isOptional = target.EndsWith('?');
            var actualTarget = isOptional ? target[..^1] : target;

            // Check for version constraint: target@1.0.0
            string? version = null;
            var atIndex = actualTarget.IndexOf('@');
            if (atIndex > 0)
            {
                version = actualTarget[(atIndex + 1)..];
                actualTarget = actualTarget[..atIndex];
            }

            dependencies.Add(new DependencyNode(startToken.Span, actualTarget, version, isOptional, new AttributeCollection()));
        }

        return new UsesNode(startToken.Span, dependencies, attrs);
    }

    /// <summary>
    /// Parses a USEDBY reverse dependency declaration.
    /// §USEDBY[OrderController.Submit, BatchProcessor.Run]
    /// </summary>
    private UsedByNode ParseUsedBy()
    {
        var startToken = Expect(TokenKind.UsedBy);
        var attrs = ParseAttributes();
        var targets = AttributeHelper.InterpretDependencyListAttributes(attrs);

        var dependents = new List<DependencyNode>();
        bool hasUnknownCallers = false;

        foreach (var target in targets)
        {
            if (target == "*" || target.Equals("external", StringComparison.OrdinalIgnoreCase))
            {
                hasUnknownCallers = true;
                continue;
            }

            var isOptional = target.EndsWith('?');
            var actualTarget = isOptional ? target[..^1] : target;

            dependents.Add(new DependencyNode(startToken.Span, actualTarget, null, isOptional, new AttributeCollection()));
        }

        return new UsedByNode(startToken.Span, dependents, hasUnknownCallers, attrs);
    }

    /// <summary>
    /// Parses an ASSUME assumption declaration.
    /// §ASSUME[env] "Database connection pool initialized"
    /// </summary>
    private AssumeNode ParseAssume()
    {
        var startToken = Expect(TokenKind.Assume);
        var attrs = ParseAttributes();
        var category = AttributeHelper.InterpretAssumeAttributes(attrs);

        // Expect a string description
        string description = "";
        if (Check(TokenKind.StrLiteral))
        {
            var strToken = Advance();
            description = (string)strToken.Value!;
        }
        else
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "ASSUME", "description");
        }

        return new AssumeNode(startToken.Span, category, description, attrs);
    }

    /// <summary>
    /// Parses a COMPLEXITY performance contract.
    /// §COMPLEXITY[time:O(n)][space:O(1)]
    /// §COMPLEXITY[worst:time:O(n)][worst:space:O(n)]
    /// </summary>
    private ComplexityNode ParseComplexity()
    {
        var startToken = Expect(TokenKind.Complexity);
        var attrs = ParseAttributes();
        var (time, space, isWorstCase, custom) = AttributeHelper.InterpretComplexityAttributes(attrs);

        return new ComplexityNode(startToken.Span, time, space, isWorstCase, custom, attrs);
    }

    /// <summary>
    /// Parses a SINCE versioning marker.
    /// §SINCE[1.0.0]
    /// </summary>
    private SinceNode ParseSince()
    {
        var startToken = Expect(TokenKind.Since);
        var attrs = ParseAttributes();
        var version = AttributeHelper.InterpretSinceAttributes(attrs);

        if (string.IsNullOrEmpty(version))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "SINCE", "version");
            version = "0.0.0";
        }

        return new SinceNode(startToken.Span, version, attrs);
    }

    /// <summary>
    /// Parses a DEPRECATED marker.
    /// §DEPRECATED[since:1.5.0][use:NewMethod][reason:"Performance issues"]
    /// </summary>
    private DeprecatedNode ParseDeprecated()
    {
        var startToken = Expect(TokenKind.Deprecated);
        var attrs = ParseAttributes();
        var (since, replacement, reason, removedIn) = AttributeHelper.InterpretDeprecatedAttributes(attrs);

        if (string.IsNullOrEmpty(since))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "DEPRECATED", "since");
            since = "0.0.0";
        }

        return new DeprecatedNode(startToken.Span, since, replacement, reason, removedIn, attrs);
    }

    /// <summary>
    /// Parses a BREAKING change marker.
    /// §BREAKING[1.7.0] "Added required 'options' parameter"
    /// </summary>
    private BreakingChangeNode ParseBreaking()
    {
        var startToken = Expect(TokenKind.Breaking);
        var attrs = ParseAttributes();
        var version = AttributeHelper.InterpretBreakingAttributes(attrs);

        if (string.IsNullOrEmpty(version))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "BREAKING", "version");
            version = "0.0.0";
        }

        // Expect a string description
        string description = "";
        if (Check(TokenKind.StrLiteral))
        {
            var strToken = Advance();
            description = (string)strToken.Value!;
        }
        else
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "BREAKING", "description");
        }

        return new BreakingChangeNode(startToken.Span, version, description, attrs);
    }

    /// <summary>
    /// Parses a DECISION record.
    /// §DECISION[d001] "Algorithm selection"
    ///   §CHOSEN "QuickSort"
    ///   §REASON "Best average-case performance"
    ///   §REJECTED "MergeSort"
    ///     §REASON "Requires O(n) extra space"
    ///   §CONTEXT "Typical input: 1000-10000 items"
    ///   §DATE 2024-01-15
    ///   §AUTHOR "perf-team"
    /// §/DECISION[d001]
    /// </summary>
    private DecisionNode ParseDecision()
    {
        var startToken = Expect(TokenKind.Decision);
        var attrs = ParseAttributes();
        var id = AttributeHelper.InterpretDecisionAttributes(attrs);

        if (string.IsNullOrEmpty(id))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "DECISION", "id");
            id = "unknown";
        }

        // Parse title (string literal)
        string title = "";
        if (Check(TokenKind.StrLiteral))
        {
            var strToken = Advance();
            title = (string)strToken.Value!;
        }

        string chosenOption = "";
        var chosenReasons = new List<string>();
        var rejectedOptions = new List<RejectedOptionNode>();
        string? context = null;
        DateOnly? date = null;
        string? author = null;

        // Parse decision content
        while (!IsAtEnd && !Check(TokenKind.EndDecision))
        {
            if (Check(TokenKind.Chosen))
            {
                Advance();
                if (Check(TokenKind.StrLiteral))
                {
                    var strToken = Advance();
                    chosenOption = (string)strToken.Value!;
                }
            }
            else if (Check(TokenKind.Reason) && string.IsNullOrEmpty(chosenOption) == false && rejectedOptions.Count == 0)
            {
                // This REASON belongs to CHOSEN
                Advance();
                if (Check(TokenKind.StrLiteral))
                {
                    var strToken = Advance();
                    chosenReasons.Add((string)strToken.Value!);
                }
            }
            else if (Check(TokenKind.Rejected))
            {
                rejectedOptions.Add(ParseRejectedOption());
            }
            else if (Check(TokenKind.Context))
            {
                Advance();
                if (Check(TokenKind.StrLiteral))
                {
                    var strToken = Advance();
                    context = (string)strToken.Value!;
                }
            }
            else if (Check(TokenKind.DateMarker))
            {
                Advance();
                var dateAttrs = ParseAttributes();
                date = AttributeHelper.InterpretDateAttributes(dateAttrs);
                // Also check for bare date literal
                if (date == null && Check(TokenKind.Identifier))
                {
                    var dateStr = Advance().Text;
                    if (DateOnly.TryParse(dateStr, out var parsedDate))
                    {
                        date = parsedDate;
                    }
                }
            }
            else if (Check(TokenKind.AgentAuthor))
            {
                Advance();
                if (Check(TokenKind.StrLiteral))
                {
                    var strToken = Advance();
                    author = (string)strToken.Value!;
                }
            }
            else if (Check(TokenKind.Reason))
            {
                // Orphan REASON - skip
                Advance();
                if (Check(TokenKind.StrLiteral)) Advance();
            }
            else
            {
                // Unknown token - skip
                Advance();
            }
        }

        var endToken = Expect(TokenKind.EndDecision);
        var endAttrs = ParseAttributes();
        var endId = endAttrs["_pos0"] ?? "";

        if (endId != id)
        {
            _diagnostics.ReportMismatchedIdWithFix(endToken.Span, "DECISION", id, "END_DECISION", endId);
        }

        var span = startToken.Span.Union(endToken.Span);
        return new DecisionNode(span, id, title, chosenOption, chosenReasons, rejectedOptions, context, date, author, attrs);
    }

    private RejectedOptionNode ParseRejectedOption()
    {
        var startToken = Expect(TokenKind.Rejected);
        string name = "";
        if (Check(TokenKind.StrLiteral))
        {
            var strToken = Advance();
            name = (string)strToken.Value!;
        }

        var reasons = new List<string>();
        while (Check(TokenKind.Reason))
        {
            Advance();
            if (Check(TokenKind.StrLiteral))
            {
                var strToken = Advance();
                reasons.Add((string)strToken.Value!);
            }
        }

        return new RejectedOptionNode(startToken.Span, name, reasons, new AttributeCollection());
    }

    /// <summary>
    /// Parses a CONTEXT partial view marker.
    /// §CONTEXT[partial]
    ///   §VISIBLE
    ///     §FILE[OrderService.calr]
    ///   §/VISIBLE
    ///   §HIDDEN
    ///     §FILE[PaymentService.calr] "Not loaded - external"
    ///   §/HIDDEN
    ///   §FOCUS[OrderService.ProcessOrder]
    /// §/CONTEXT
    /// </summary>
    private ContextNode ParseContext()
    {
        var startToken = Expect(TokenKind.Context);
        var attrs = ParseAttributes();
        var isPartial = AttributeHelper.InterpretContextAttributes(attrs);

        var visibleFiles = new List<FileRefNode>();
        var hiddenFiles = new List<FileRefNode>();
        string? focusTarget = null;

        while (!IsAtEnd && !Check(TokenKind.EndContext))
        {
            if (Check(TokenKind.Visible))
            {
                Advance();
                while (!IsAtEnd && !Check(TokenKind.EndVisible))
                {
                    if (Check(TokenKind.FileRef))
                    {
                        visibleFiles.Add(ParseFileRef());
                    }
                    else
                    {
                        Advance();
                    }
                }
                if (Check(TokenKind.EndVisible)) Advance();
            }
            else if (Check(TokenKind.HiddenSection))
            {
                Advance();
                while (!IsAtEnd && !Check(TokenKind.EndHidden))
                {
                    if (Check(TokenKind.FileRef))
                    {
                        hiddenFiles.Add(ParseFileRef());
                    }
                    else
                    {
                        Advance();
                    }
                }
                if (Check(TokenKind.EndHidden)) Advance();
            }
            else if (Check(TokenKind.Focus))
            {
                Advance();
                var focusAttrs = ParseAttributes();
                focusTarget = AttributeHelper.InterpretFocusAttributes(focusAttrs);
            }
            else if (Check(TokenKind.FileRef))
            {
                // Standalone FILE goes to visible by default
                visibleFiles.Add(ParseFileRef());
            }
            else
            {
                Advance();
            }
        }

        var endToken = Expect(TokenKind.EndContext);
        var span = startToken.Span.Union(endToken.Span);
        return new ContextNode(span, isPartial, visibleFiles, hiddenFiles, focusTarget, attrs);
    }

    private FileRefNode ParseFileRef()
    {
        var startToken = Expect(TokenKind.FileRef);
        var attrs = ParseAttributes();
        var filePath = AttributeHelper.InterpretFileRefAttributes(attrs);

        string? description = null;
        if (Check(TokenKind.StrLiteral))
        {
            var strToken = Advance();
            description = (string)strToken.Value!;
        }

        return new FileRefNode(startToken.Span, filePath, description, attrs);
    }

    /// <summary>
    /// Parses a PROPERTY (property-based testing) declaration.
    /// §PROPERTY ∀arr: (== (Reverse (Reverse arr)) arr)
    /// </summary>
    private PropertyTestNode ParsePropertyTest()
    {
        var startToken = Expect(TokenKind.PropertyTest);
        var attrs = ParseAttributes();

        var quantifiers = new List<string>();

        // Check for universal quantifier ∀ or "forall" keyword
        if (Check(TokenKind.Identifier))
        {
            var quantifierText = Current.Text;
            if (quantifierText.StartsWith("∀") || quantifierText.Equals("forall", StringComparison.OrdinalIgnoreCase))
            {
                Advance();
                // Parse variable names: ∀x,y,z:
                if (quantifierText.StartsWith("∀") && quantifierText.Length > 1)
                {
                    // ∀x format - variable is part of the token
                    var varPart = quantifierText[1..];
                    if (varPart.EndsWith(':'))
                        varPart = varPart[..^1];
                    foreach (var v in varPart.Split(','))
                    {
                        if (!string.IsNullOrWhiteSpace(v))
                            quantifiers.Add(v.Trim());
                    }
                }
                else
                {
                    // Separate variable list
                    while (Check(TokenKind.Identifier))
                    {
                        var varName = Advance().Text;
                        if (varName.EndsWith(':'))
                            varName = varName[..^1];
                        foreach (var v in varName.Split(','))
                        {
                            if (!string.IsNullOrWhiteSpace(v))
                                quantifiers.Add(v.Trim());
                        }
                        // Skip colon if present
                        if (Check(TokenKind.Colon))
                        {
                            Advance();
                            break;
                        }
                    }
                }
            }
        }

        var predicate = ParseExpression();
        var span = startToken.Span.Union(predicate.Span);
        return new PropertyTestNode(span, quantifiers, predicate, attrs);
    }

    /// <summary>
    /// Parses a LOCK multi-agent locking declaration.
    /// §LOCK[agent:agent-123][expires:2024-01-15T11:00:00Z]
    /// </summary>
    private LockNode ParseLock()
    {
        var startToken = Expect(TokenKind.Lock);
        var attrs = ParseAttributes();
        var (agentId, acquired, expires) = AttributeHelper.InterpretLockAttributes(attrs);

        if (string.IsNullOrEmpty(agentId))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "LOCK", "agent");
            agentId = "unknown";
        }

        return new LockNode(startToken.Span, agentId, acquired, expires, attrs);
    }

    /// <summary>
    /// Parses an AUTHOR agent authorship tracking declaration.
    /// §AUTHOR[agent:agent-456][date:2024-01-10][task:PROJ-789]
    /// </summary>
    private AuthorNode ParseAuthor()
    {
        var startToken = Expect(TokenKind.AgentAuthor);
        var attrs = ParseAttributes();
        var (agentId, date, taskId) = AttributeHelper.InterpretAuthorAttributes(attrs);

        if (string.IsNullOrEmpty(agentId))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "AUTHOR", "agent");
            agentId = "unknown";
        }

        return new AuthorNode(startToken.Span, agentId, date, taskId, attrs);
    }

    /// <summary>
    /// Parses a TASK reference.
    /// §TASK[PROJ-789] "Implement validation"
    /// </summary>
    private TaskRefNode ParseTaskRef()
    {
        var startToken = Expect(TokenKind.TaskRef);
        var attrs = ParseAttributes();
        var taskId = AttributeHelper.InterpretTaskRefAttributes(attrs);

        if (string.IsNullOrEmpty(taskId))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "TASK", "id");
            taskId = "unknown";
        }

        // Expect a string description
        string description = "";
        if (Check(TokenKind.StrLiteral))
        {
            var strToken = Advance();
            description = (string)strToken.Value!;
        }

        return new TaskRefNode(startToken.Span, taskId, description, attrs);
    }

    /// <summary>
    /// Checks if the current token starts an extended metadata declaration.
    /// </summary>
    private bool IsExtendedMetadataStart()
    {
        return Current.Kind is TokenKind.Example
            or TokenKind.Todo
            or TokenKind.Fixme
            or TokenKind.Hack
            or TokenKind.Uses
            or TokenKind.UsedBy
            or TokenKind.Assume
            or TokenKind.Complexity
            or TokenKind.Since
            or TokenKind.Deprecated
            or TokenKind.Breaking
            or TokenKind.PropertyTest
            or TokenKind.Lock
            or TokenKind.AgentAuthor
            or TokenKind.TaskRef;
    }

    /// <summary>
    /// Checks if the current token starts a module-level extended metadata declaration.
    /// </summary>
    private bool IsModuleLevelExtendedMetadataStart()
    {
        return Current.Kind is TokenKind.Todo
            or TokenKind.Fixme
            or TokenKind.Hack
            or TokenKind.Assume
            or TokenKind.Invariant
            or TokenKind.Decision
            or TokenKind.Context;
    }

    #endregion

    #region Enum and Enum Extensions

    /// <summary>
    /// Parses an enum definition.
    /// §EN{id:Name} or §EN{id:Name:underlyingType}
    ///   Red
    ///   Green = 1
    ///   Blue = 2
    /// §/EN{id}
    /// </summary>
    private EnumDefinitionNode ParseEnumDefinition()
    {
        var startToken = Expect(TokenKind.Enum);
        var attrs = ParseAttributes();

        // Positional: [id:name] or [id:name:underlyingType]
        var id = attrs["_pos0"] ?? "";
        var name = attrs["_pos1"] ?? "";
        var underlyingType = attrs["_pos2"]; // optional

        if (string.IsNullOrEmpty(id))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "EN", "id");
        }
        if (string.IsNullOrEmpty(name))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "EN", "name");
        }

        var members = new List<EnumMemberNode>();

        // Parse enum members until we hit the closing tag
        while (!IsAtEnd && !Check(TokenKind.EndEnum))
        {
            // Each enum member is an identifier optionally followed by = value
            if (Check(TokenKind.Identifier))
            {
                var memberStartToken = Current;
                var memberName = Advance().Text;
                string? memberValue = null;

                // Check for = value
                if (Check(TokenKind.Equals))
                {
                    Advance(); // consume =
                    // Value can be an integer literal or identifier
                    if (Check(TokenKind.IntLiteral))
                    {
                        var valueToken = Advance();
                        memberValue = valueToken.Value?.ToString();
                    }
                    else if (Check(TokenKind.Identifier))
                    {
                        // Could be a reference to another enum member or expression
                        var valueToken = Advance();
                        memberValue = valueToken.Text;
                    }
                    else if (Check(TokenKind.Minus) && Peek(1).Kind == TokenKind.IntLiteral)
                    {
                        // Negative value
                        Advance(); // consume -
                        var valueToken = Advance();
                        memberValue = $"-{valueToken.Value}";
                    }
                }

                var memberSpan = memberStartToken.Span.Union(Current.Span);
                members.Add(new EnumMemberNode(memberSpan, memberName, memberValue));
            }
            else
            {
                _diagnostics.ReportUnexpectedToken(Current.Span, "enum member name", Current.Kind);
                Advance();
            }
        }

        var endToken = Expect(TokenKind.EndEnum);
        var endAttrs = ParseAttributes();
        var endId = endAttrs["_pos0"] ?? "";

        if (endId != id)
        {
            _diagnostics.ReportMismatchedIdWithFix(endToken.Span, "EN", id, "END_EN", endId);
        }

        var span = startToken.Span.Union(endToken.Span);
        return new EnumDefinitionNode(span, id, name, underlyingType, members, attrs);
    }

    /// <summary>
    /// Parses an enum extension definition.
    /// §EEXT{id:EnumName}
    ///   §F{f001:MethodName:pub}
    ///     §I{EnumType:self}
    ///     §O{returnType}
    ///     // body
    ///   §/F{f001}
    /// §/EEXT{id}
    /// </summary>
    private EnumExtensionNode ParseEnumExtension()
    {
        var startToken = Expect(TokenKind.EnumExtension);
        var attrs = ParseAttributes();

        // Positional: [id:enumName]
        var id = attrs["_pos0"] ?? "";
        var enumName = attrs["_pos1"] ?? "";

        if (string.IsNullOrEmpty(id))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "EXT", "id");
        }
        if (string.IsNullOrEmpty(enumName))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "EXT", "enumName");
        }

        var methods = new List<FunctionNode>();

        // Parse extension methods until we hit the closing tag
        while (!IsAtEnd && !Check(TokenKind.EndEnumExtension))
        {
            if (Check(TokenKind.Func))
            {
                methods.Add(ParseFunction());
            }
            else if (Check(TokenKind.AsyncFunc))
            {
                methods.Add(ParseAsyncFunction());
            }
            else
            {
                _diagnostics.ReportUnexpectedToken(Current.Span, "F or AF (function definition)", Current.Kind);
                Advance();
            }
        }

        var endToken = Expect(TokenKind.EndEnumExtension);
        var endAttrs = ParseAttributes();
        var endId = endAttrs["_pos0"] ?? "";

        if (endId != id)
        {
            _diagnostics.ReportMismatchedIdWithFix(endToken.Span, "EXT", id, "END_EXT", endId);
        }

        // Validate that each extension method has a parameter of the enum type
        if (!string.IsNullOrEmpty(enumName))
        {
            foreach (var method in methods)
            {
                var hasEnumParam = method.Parameters.Any(p =>
                    string.Equals(p.TypeName, enumName, StringComparison.OrdinalIgnoreCase));

                if (!hasEnumParam)
                {
                    _diagnostics.ReportMissingExtensionSelf(method.Span, method.Name, enumName);
                }
            }
        }

        var span = startToken.Span.Union(endToken.Span);
        return new EnumExtensionNode(span, id, enumName, methods, attrs);
    }

    #endregion

    #region Class Modifier Helpers

    private static bool IsVisibilityKeyword(string value)
    {
        return value.Equals("pub", StringComparison.OrdinalIgnoreCase)
            || value.Equals("pri", StringComparison.OrdinalIgnoreCase)
            || value.Equals("pro", StringComparison.OrdinalIgnoreCase)
            || value.Equals("prot", StringComparison.OrdinalIgnoreCase)
            || value.Equals("priv", StringComparison.OrdinalIgnoreCase)
            || value.Equals("int", StringComparison.OrdinalIgnoreCase)
            || value.Equals("public", StringComparison.OrdinalIgnoreCase)
            || value.Equals("private", StringComparison.OrdinalIgnoreCase)
            || value.Equals("protected", StringComparison.OrdinalIgnoreCase)
            || value.Equals("internal", StringComparison.OrdinalIgnoreCase);
    }

    private static readonly HashSet<string> VisibilityKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "pub", "pri", "pro", "prot", "priv", "int",
        "public", "private", "protected", "internal"
    };

    /// <summary>
    /// Extracts a visibility keyword from a comma/space-separated modifiers string,
    /// removes it from the modifiers, and returns the parsed visibility.
    /// </summary>
    private static Visibility ExtractVisibilityFromModifiers(ref string modifiers)
    {
        if (string.IsNullOrEmpty(modifiers))
            return Visibility.Internal;

        var parts = modifiers.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        Visibility vis = Visibility.Internal;
        bool found = false;
        var remaining = new List<string>();

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (!found && VisibilityKeywords.Contains(trimmed))
            {
                vis = ParseVisibility(trimmed);
                found = true;
            }
            else
            {
                remaining.Add(trimmed);
            }
        }

        modifiers = string.Join(",", remaining);
        return vis;
    }

    private static readonly HashSet<string> ClassModifierKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "abs", "abstract", "seal", "sealed", "st", "stat", "static",
        "partial", "struct", "readonly",
        "pub", "pri", "pro", "prot", "priv", "int",
        "public", "private", "protected", "internal"
    };

    private static bool IsClassModifierOrVisibility(string value)
    {
        if (string.IsNullOrEmpty(value))
            return true; // empty string is "no modifiers" — treat as modifiers field

        // value may be comma-separated ("abs,sealed") or space-separated ("abs seal")
        foreach (var part in value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = part.Trim();
            if (trimmed.Length > 0 && !ClassModifierKeywords.Contains(trimmed))
                return false;
        }
        return true;
    }

    #endregion
}
