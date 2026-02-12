using Calor.Compiler.Ast;
using Calor.Compiler.Parsing;

namespace Calor.LanguageServer.Utilities;

/// <summary>
/// A visitor that traverses the AST to find symbols at a specific offset.
/// Handles nested scopes, control flow statements, and expressions.
/// </summary>
public sealed class SymbolFinderVisitor
{
    private readonly int _targetOffset;
    private readonly string _source;
    private readonly List<ScopeInfo> _scopeStack = new();
    private SymbolLookupResult? _result;
    private AstNode? _containingNode;

    public SymbolFinderVisitor(int targetOffset, string source)
    {
        _targetOffset = targetOffset;
        _source = source;
    }

    /// <summary>
    /// Find the symbol at the target offset by traversing the AST.
    /// </summary>
    public SymbolLookupResult? FindSymbol(ModuleNode module)
    {
        _result = null;
        _containingNode = null;
        _scopeStack.Clear();

        // Push module scope
        PushScope(ScopeKind.Module, module.Name);

        // Visit module contents
        VisitModule(module);

        return _result;
    }

    /// <summary>
    /// Get all symbols visible at the target offset (for completions).
    /// </summary>
    public IReadOnlyList<VisibleSymbol> GetVisibleSymbols(ModuleNode module)
    {
        _scopeStack.Clear();
        var symbols = new List<VisibleSymbol>();

        // Push module scope
        PushScope(ScopeKind.Module, module.Name);

        // Collect module-level symbols
        foreach (var func in module.Functions)
        {
            symbols.Add(new VisibleSymbol(func.Name, "function", func.Output?.TypeName ?? "void", func.Span, func));

            // If we're inside this function, collect its symbols
            if (SpanContainsOffset(func.Span))
            {
                CollectFunctionSymbols(func, symbols);
            }
        }

        foreach (var cls in module.Classes)
        {
            symbols.Add(new VisibleSymbol(cls.Name, "class", null, cls.Span, cls));

            if (SpanContainsOffset(cls.Span))
            {
                CollectClassSymbols(cls, symbols);
            }
        }

        foreach (var iface in module.Interfaces)
        {
            symbols.Add(new VisibleSymbol(iface.Name, "interface", null, iface.Span, iface));
        }

        foreach (var enumDef in module.Enums)
        {
            symbols.Add(new VisibleSymbol(enumDef.Name, "enum", enumDef.UnderlyingType, enumDef.Span, enumDef));
        }

        foreach (var del in module.Delegates)
        {
            symbols.Add(new VisibleSymbol(del.Name, "delegate", del.Output?.TypeName ?? "void", del.Span, del));
        }

        // Note: Records and Unions are not yet part of ModuleNode structure
        // They would be added here when the module supports them

        return symbols;
    }

    private void CollectFunctionSymbols(FunctionNode func, List<VisibleSymbol> symbols)
    {
        // Add parameters
        foreach (var param in func.Parameters)
        {
            symbols.Add(new VisibleSymbol(param.Name, "parameter", param.TypeName, param.Span, param));
        }

        // Traverse body to find bindings visible at target offset
        CollectBindingsFromStatements(func.Body, symbols);
    }

    private void CollectClassSymbols(ClassDefinitionNode cls, List<VisibleSymbol> symbols)
    {
        // Add fields
        foreach (var field in cls.Fields)
        {
            symbols.Add(new VisibleSymbol(field.Name, "field", field.TypeName, field.Span, field));
        }

        // Add properties
        foreach (var prop in cls.Properties)
        {
            symbols.Add(new VisibleSymbol(prop.Name, "property", prop.TypeName, prop.Span, prop));
        }

        // Add methods
        foreach (var method in cls.Methods)
        {
            symbols.Add(new VisibleSymbol(method.Name, "method", method.Output?.TypeName ?? "void", method.Span, method));

            if (SpanContainsOffset(method.Span))
            {
                CollectMethodSymbols(method, symbols);
            }
        }

        // Check constructors
        foreach (var ctor in cls.Constructors)
        {
            if (SpanContainsOffset(ctor.Span))
            {
                foreach (var param in ctor.Parameters)
                {
                    symbols.Add(new VisibleSymbol(param.Name, "parameter", param.TypeName, param.Span, param));
                }
                CollectBindingsFromStatements(ctor.Body, symbols);
            }
        }
    }

    private void CollectMethodSymbols(MethodNode method, List<VisibleSymbol> symbols)
    {
        foreach (var param in method.Parameters)
        {
            symbols.Add(new VisibleSymbol(param.Name, "parameter", param.TypeName, param.Span, param));
        }

        CollectBindingsFromStatements(method.Body, symbols);
    }

    private void CollectBindingsFromStatements(IReadOnlyList<StatementNode> statements, List<VisibleSymbol> symbols)
    {
        foreach (var stmt in statements)
        {
            // Only include bindings that appear before the target offset
            if (stmt.Span.Start > _targetOffset)
                break;

            switch (stmt)
            {
                case BindStatementNode bind:
                    symbols.Add(new VisibleSymbol(
                        bind.Name,
                        bind.IsMutable ? "mutable variable" : "variable",
                        bind.TypeName,
                        bind.Span,
                        bind));
                    break;

                case ForStatementNode forStmt:
                    // For loop variable is visible inside the loop
                    if (SpanContainsOffset(forStmt.Span))
                    {
                        symbols.Add(new VisibleSymbol(forStmt.VariableName, "loop variable", "i32", forStmt.Span, forStmt));
                        CollectBindingsFromStatements(forStmt.Body, symbols);
                    }
                    break;

                case WhileStatementNode whileStmt:
                    if (SpanContainsOffset(whileStmt.Span))
                    {
                        CollectBindingsFromStatements(whileStmt.Body, symbols);
                    }
                    break;

                case DoWhileStatementNode doWhileStmt:
                    if (SpanContainsOffset(doWhileStmt.Span))
                    {
                        CollectBindingsFromStatements(doWhileStmt.Body, symbols);
                    }
                    break;

                case IfStatementNode ifStmt:
                    if (SpanContainsOffset(ifStmt.Span))
                    {
                        // Check which branch contains the offset
                        CollectBindingsFromStatements(ifStmt.ThenBody, symbols);
                        foreach (var elseIf in ifStmt.ElseIfClauses)
                        {
                            if (SpanContainsOffset(elseIf.Span))
                            {
                                CollectBindingsFromStatements(elseIf.Body, symbols);
                            }
                        }
                        if (ifStmt.ElseBody != null)
                        {
                            CollectBindingsFromStatements(ifStmt.ElseBody, symbols);
                        }
                    }
                    break;

                case TryStatementNode tryStmt:
                    if (SpanContainsOffset(tryStmt.Span))
                    {
                        CollectBindingsFromStatements(tryStmt.TryBody, symbols);
                        foreach (var catchClause in tryStmt.CatchClauses)
                        {
                            if (SpanContainsOffset(catchClause.Span) && !string.IsNullOrEmpty(catchClause.VariableName))
                            {
                                symbols.Add(new VisibleSymbol(
                                    catchClause.VariableName,
                                    "catch variable",
                                    catchClause.ExceptionType ?? "Exception",
                                    catchClause.Span,
                                    catchClause));
                                CollectBindingsFromStatements(catchClause.Body, symbols);
                            }
                        }
                        if (tryStmt.FinallyBody != null)
                        {
                            CollectBindingsFromStatements(tryStmt.FinallyBody, symbols);
                        }
                    }
                    break;

                case ForeachStatementNode foreachStmt:
                    if (SpanContainsOffset(foreachStmt.Span))
                    {
                        symbols.Add(new VisibleSymbol(
                            foreachStmt.VariableName,
                            "iteration variable",
                            foreachStmt.VariableType,
                            foreachStmt.Span,
                            foreachStmt));
                        CollectBindingsFromStatements(foreachStmt.Body, symbols);
                    }
                    break;

                case UsingStatementNode usingStmt:
                    if (SpanContainsOffset(usingStmt.Span))
                    {
                        if (!string.IsNullOrEmpty(usingStmt.VariableName))
                        {
                            symbols.Add(new VisibleSymbol(
                                usingStmt.VariableName,
                                "using variable",
                                usingStmt.VariableType,
                                usingStmt.Span,
                                usingStmt));
                        }
                        CollectBindingsFromStatements(usingStmt.Body, symbols);
                    }
                    break;

                case MatchStatementNode matchStmt:
                    if (SpanContainsOffset(matchStmt.Span))
                    {
                        foreach (var caseNode in matchStmt.Cases)
                        {
                            if (SpanContainsOffset(caseNode.Span))
                            {
                                CollectPatternVariables(caseNode.Pattern, symbols);
                                CollectBindingsFromStatements(caseNode.Body, symbols);
                            }
                        }
                    }
                    break;

                case DictionaryForeachNode dictForeach:
                    if (SpanContainsOffset(dictForeach.Span))
                    {
                        symbols.Add(new VisibleSymbol(dictForeach.KeyName, "key variable", null, dictForeach.Span, dictForeach));
                        symbols.Add(new VisibleSymbol(dictForeach.ValueName, "value variable", null, dictForeach.Span, dictForeach));
                        CollectBindingsFromStatements(dictForeach.Body, symbols);
                    }
                    break;
            }
        }
    }

    private void CollectPatternVariables(AstNode? pattern, List<VisibleSymbol> symbols)
    {
        if (pattern == null) return;

        switch (pattern)
        {
            case VariablePatternNode varPat:
                symbols.Add(new VisibleSymbol(varPat.Name, "pattern variable", null, varPat.Span, varPat));
                break;

            case VarPatternNode vPat:
                symbols.Add(new VisibleSymbol(vPat.Name, "var pattern", null, vPat.Span, vPat));
                break;

            case SomePatternNode somePat:
                CollectPatternVariables(somePat.InnerPattern, symbols);
                break;

            case OkPatternNode okPat:
                CollectPatternVariables(okPat.InnerPattern, symbols);
                break;

            case ErrPatternNode errPat:
                CollectPatternVariables(errPat.InnerPattern, symbols);
                break;

            case PositionalPatternNode posPat:
                foreach (var inner in posPat.Patterns)
                {
                    CollectPatternVariables(inner, symbols);
                }
                break;

            case PropertyPatternNode propPat:
                foreach (var match in propPat.Matches)
                {
                    CollectPatternVariables(match.Pattern, symbols);
                }
                break;

            case ListPatternNode listPat:
                foreach (var inner in listPat.Patterns)
                {
                    CollectPatternVariables(inner, symbols);
                }
                if (listPat.SlicePattern != null)
                {
                    CollectPatternVariables(listPat.SlicePattern, symbols);
                }
                break;

            // These patterns don't introduce variables
            case WildcardPatternNode:
            case LiteralPatternNode:
            case ConstantPatternNode:
            case NonePatternNode:
            case RelationalPatternNode:
                break;
        }
    }

    private void VisitModule(ModuleNode module)
    {
        // Check if offset is on module name
        if (SpanContainsOffset(module.Span))
        {
            // Check functions
            foreach (var func in module.Functions)
            {
                if (SpanContainsOffset(func.Span))
                {
                    VisitFunction(func);
                    return;
                }
            }

            // Check classes
            foreach (var cls in module.Classes)
            {
                if (SpanContainsOffset(cls.Span))
                {
                    VisitClass(cls);
                    return;
                }
            }

            // Check interfaces
            foreach (var iface in module.Interfaces)
            {
                if (SpanContainsOffset(iface.Span))
                {
                    VisitInterface(iface);
                    return;
                }
            }

            // Check enums
            foreach (var enumDef in module.Enums)
            {
                if (SpanContainsOffset(enumDef.Span))
                {
                    VisitEnum(enumDef);
                    return;
                }
            }

            // Check enum extensions
            foreach (var enumExt in module.EnumExtensions)
            {
                if (SpanContainsOffset(enumExt.Span))
                {
                    VisitEnumExtension(enumExt);
                    return;
                }
            }

            // Check delegates
            foreach (var del in module.Delegates)
            {
                if (SpanContainsOffset(del.Span))
                {
                    VisitDelegate(del);
                    return;
                }
            }

            // Note: Records and Unions would be visited here when added to ModuleNode
        }
    }

    private void VisitFunction(FunctionNode func)
    {
        _containingNode = func;
        PushScope(ScopeKind.Function, func.Name);

        // Add parameters to scope
        foreach (var param in func.Parameters)
        {
            AddToScope(param.Name, "parameter", param.TypeName, param.Span, param);
        }

        // Visit body
        VisitStatements(func.Body);

        PopScope();
    }

    private void VisitClass(ClassDefinitionNode cls)
    {
        _containingNode = cls;
        PushScope(ScopeKind.Class, cls.Name);

        // Add fields
        foreach (var field in cls.Fields)
        {
            AddToScope(field.Name, "field", field.TypeName, field.Span, field);
        }

        // Add properties
        foreach (var prop in cls.Properties)
        {
            AddToScope(prop.Name, "property", prop.TypeName, prop.Span, prop);
        }

        // Check methods
        foreach (var method in cls.Methods)
        {
            if (SpanContainsOffset(method.Span))
            {
                VisitMethod(method);
                PopScope();
                return;
            }
        }

        // Check constructors
        foreach (var ctor in cls.Constructors)
        {
            if (SpanContainsOffset(ctor.Span))
            {
                VisitConstructor(ctor);
                PopScope();
                return;
            }
        }

        PopScope();
    }

    private void VisitMethod(MethodNode method)
    {
        _containingNode = method;
        PushScope(ScopeKind.Method, method.Name);

        foreach (var param in method.Parameters)
        {
            AddToScope(param.Name, "parameter", param.TypeName, param.Span, param);
        }

        VisitStatements(method.Body);

        PopScope();
    }

    private void VisitConstructor(ConstructorNode ctor)
    {
        _containingNode = ctor;
        PushScope(ScopeKind.Constructor, "constructor");

        foreach (var param in ctor.Parameters)
        {
            AddToScope(param.Name, "parameter", param.TypeName, param.Span, param);
        }

        VisitStatements(ctor.Body);

        PopScope();
    }

    private void VisitInterface(InterfaceDefinitionNode iface)
    {
        _containingNode = iface;

        foreach (var method in iface.Methods)
        {
            if (SpanContainsOffset(method.Span))
            {
                _result = new SymbolLookupResult(
                    method.Name, "method signature", method.Output?.TypeName ?? "void",
                    method.Span, method.Span, method);
                return;
            }
        }
    }

    private void VisitEnum(EnumDefinitionNode enumDef)
    {
        _containingNode = enumDef;

        foreach (var member in enumDef.Members)
        {
            if (SpanContainsOffset(member.Span))
            {
                _result = new SymbolLookupResult(
                    member.Name, "enum member", enumDef.Name,
                    member.Span, member.Span, member);
                return;
            }
        }
    }

    private void VisitEnumExtension(EnumExtensionNode enumExt)
    {
        _containingNode = enumExt;
        PushScope(ScopeKind.EnumExtension, enumExt.EnumName);

        foreach (var method in enumExt.Methods)
        {
            if (SpanContainsOffset(method.Span))
            {
                VisitFunction(method);
                PopScope();
                return;
            }
        }

        PopScope();
    }

    private void VisitDelegate(DelegateDefinitionNode del)
    {
        _containingNode = del;

        foreach (var param in del.Parameters)
        {
            if (SpanContainsOffset(param.Span))
            {
                _result = new SymbolLookupResult(
                    param.Name, "parameter", param.TypeName,
                    param.Span, param.Span, param);
                return;
            }
        }
    }

    private void VisitStatements(IReadOnlyList<StatementNode> statements)
    {
        foreach (var stmt in statements)
        {
            if (SpanContainsOffset(stmt.Span))
            {
                VisitStatement(stmt);
                if (_result != null) return;
            }
            else if (stmt is BindStatementNode bind && stmt.Span.Start < _targetOffset)
            {
                // Add bindings that appear before the target offset to scope
                AddToScope(bind.Name, bind.IsMutable ? "mutable variable" : "variable", bind.TypeName, bind.Span, bind);
            }
        }
    }

    private void VisitStatement(StatementNode stmt)
    {
        switch (stmt)
        {
            case BindStatementNode bind:
                VisitBindStatement(bind);
                break;

            case ForStatementNode forStmt:
                VisitForStatement(forStmt);
                break;

            case WhileStatementNode whileStmt:
                VisitWhileStatement(whileStmt);
                break;

            case DoWhileStatementNode doWhileStmt:
                VisitDoWhileStatement(doWhileStmt);
                break;

            case IfStatementNode ifStmt:
                VisitIfStatement(ifStmt);
                break;

            case TryStatementNode tryStmt:
                VisitTryStatement(tryStmt);
                break;

            case ForeachStatementNode foreachStmt:
                VisitForeachStatement(foreachStmt);
                break;

            case MatchStatementNode matchStmt:
                VisitMatchStatement(matchStmt);
                break;

            case ReturnStatementNode returnStmt:
                if (returnStmt.Expression != null)
                {
                    VisitExpression(returnStmt.Expression);
                }
                break;

            case PrintStatementNode printStmt:
                VisitExpression(printStmt.Expression);
                break;

            case CallStatementNode callStmt:
                // Check if target name is at offset
                VisitCallTarget(callStmt.Target, callStmt.Span);
                foreach (var arg in callStmt.Arguments)
                {
                    VisitExpression(arg);
                }
                break;

            case AssignmentStatementNode assignStmt:
                VisitExpression(assignStmt.Target);
                VisitExpression(assignStmt.Value);
                break;

            case UsingStatementNode usingStmt:
                VisitUsingStatement(usingStmt);
                break;

            case DictionaryForeachNode dictForeach:
                VisitDictionaryForeach(dictForeach);
                break;

            // Collection operations
            case CollectionPushNode push:
                VisitExpression(push.Value);
                break;

            case DictionaryPutNode put:
                VisitExpression(put.Key);
                VisitExpression(put.Value);
                break;

            case ThrowStatementNode throwStmt:
                if (throwStmt.Exception != null)
                {
                    VisitExpression(throwStmt.Exception);
                }
                break;

            case EventSubscribeNode evtSub:
                // Event names
                break;

            case EventUnsubscribeNode evtUnsub:
                // Event names
                break;

            // Simple control flow statements with no expressions
            case BreakStatementNode:
            case ContinueStatementNode:
            case RethrowStatementNode:
                // These statements have no child expressions to visit
                break;

            case CompoundAssignmentStatementNode compoundAssign:
                VisitExpression(compoundAssign.Target);
                VisitExpression(compoundAssign.Value);
                break;

            // Note: FieldAssignmentNode is used in RecordCreationNode, not as a standalone statement
            // Note: LockNode is a metadata node for multi-agent locking, not a C# lock statement

            // Additional collection operations
            case CollectionClearNode:
                // Clear has no value expression
                break;

            case CollectionInsertNode insert:
                VisitExpression(insert.Index);
                VisitExpression(insert.Value);
                break;

            case CollectionRemoveNode remove:
                VisitExpression(remove.KeyOrValue);
                break;

            case CollectionSetIndexNode setIndex:
                VisitExpression(setIndex.Index);
                VisitExpression(setIndex.Value);
                break;
        }
    }

    private void VisitBindStatement(BindStatementNode bind)
    {
        // Check if we're on the variable name
        // Add to scope for later references
        AddToScope(bind.Name, bind.IsMutable ? "mutable variable" : "variable", bind.TypeName, bind.Span, bind);

        if (bind.Initializer != null && SpanContainsOffset(bind.Initializer.Span))
        {
            VisitExpression(bind.Initializer);
        }
    }

    private void VisitForStatement(ForStatementNode forStmt)
    {
        PushScope(ScopeKind.Loop, forStmt.Id);

        // For loop variable
        AddToScope(forStmt.VariableName, "loop variable", "i32", forStmt.Span, forStmt);

        // Check expressions
        if (SpanContainsOffset(forStmt.From.Span))
        {
            VisitExpression(forStmt.From);
        }
        else if (SpanContainsOffset(forStmt.To.Span))
        {
            VisitExpression(forStmt.To);
        }
        else if (forStmt.Step != null && SpanContainsOffset(forStmt.Step.Span))
        {
            VisitExpression(forStmt.Step);
        }
        else
        {
            VisitStatements(forStmt.Body);
        }

        PopScope();
    }

    private void VisitWhileStatement(WhileStatementNode whileStmt)
    {
        PushScope(ScopeKind.Loop, whileStmt.Id);

        if (SpanContainsOffset(whileStmt.Condition.Span))
        {
            VisitExpression(whileStmt.Condition);
        }
        else
        {
            VisitStatements(whileStmt.Body);
        }

        PopScope();
    }

    private void VisitDoWhileStatement(DoWhileStatementNode doWhileStmt)
    {
        PushScope(ScopeKind.Loop, doWhileStmt.Id);

        if (SpanContainsOffset(doWhileStmt.Condition.Span))
        {
            VisitExpression(doWhileStmt.Condition);
        }
        else
        {
            VisitStatements(doWhileStmt.Body);
        }

        PopScope();
    }

    private void VisitIfStatement(IfStatementNode ifStmt)
    {
        if (SpanContainsOffset(ifStmt.Condition.Span))
        {
            VisitExpression(ifStmt.Condition);
            return;
        }

        // Check then body
        foreach (var stmt in ifStmt.ThenBody)
        {
            if (SpanContainsOffset(stmt.Span))
            {
                PushScope(ScopeKind.Block, "then");
                VisitStatement(stmt);
                PopScope();
                return;
            }
        }

        // Check else-if clauses
        foreach (var elseIf in ifStmt.ElseIfClauses)
        {
            if (SpanContainsOffset(elseIf.Condition.Span))
            {
                VisitExpression(elseIf.Condition);
                return;
            }

            foreach (var stmt in elseIf.Body)
            {
                if (SpanContainsOffset(stmt.Span))
                {
                    PushScope(ScopeKind.Block, "elseif");
                    VisitStatement(stmt);
                    PopScope();
                    return;
                }
            }
        }

        // Check else body
        if (ifStmt.ElseBody != null)
        {
            foreach (var stmt in ifStmt.ElseBody)
            {
                if (SpanContainsOffset(stmt.Span))
                {
                    PushScope(ScopeKind.Block, "else");
                    VisitStatement(stmt);
                    PopScope();
                    return;
                }
            }
        }
    }

    private void VisitTryStatement(TryStatementNode tryStmt)
    {
        // Check try body
        foreach (var stmt in tryStmt.TryBody)
        {
            if (SpanContainsOffset(stmt.Span))
            {
                PushScope(ScopeKind.Block, "try");
                VisitStatement(stmt);
                PopScope();
                return;
            }
        }

        // Check catch clauses
        foreach (var catchClause in tryStmt.CatchClauses)
        {
            if (SpanContainsOffset(catchClause.Span))
            {
                PushScope(ScopeKind.Catch, catchClause.ExceptionType ?? "Exception");

                if (!string.IsNullOrEmpty(catchClause.VariableName))
                {
                    AddToScope(catchClause.VariableName, "catch variable",
                        catchClause.ExceptionType ?? "Exception", catchClause.Span, catchClause);
                }

                VisitStatements(catchClause.Body);
                PopScope();
                return;
            }
        }

        // Check finally body
        if (tryStmt.FinallyBody != null)
        {
            foreach (var stmt in tryStmt.FinallyBody)
            {
                if (SpanContainsOffset(stmt.Span))
                {
                    PushScope(ScopeKind.Block, "finally");
                    VisitStatement(stmt);
                    PopScope();
                    return;
                }
            }
        }
    }

    private void VisitForeachStatement(ForeachStatementNode foreachStmt)
    {
        PushScope(ScopeKind.Loop, foreachStmt.Id);

        AddToScope(foreachStmt.VariableName, "iteration variable",
            foreachStmt.VariableType, foreachStmt.Span, foreachStmt);

        if (SpanContainsOffset(foreachStmt.Collection.Span))
        {
            VisitExpression(foreachStmt.Collection);
        }
        else
        {
            VisitStatements(foreachStmt.Body);
        }

        PopScope();
    }

    private void VisitMatchStatement(MatchStatementNode matchStmt)
    {
        if (SpanContainsOffset(matchStmt.Target.Span))
        {
            VisitExpression(matchStmt.Target);
            return;
        }

        foreach (var caseNode in matchStmt.Cases)
        {
            if (SpanContainsOffset(caseNode.Span))
            {
                PushScope(ScopeKind.Block, "case");

                // Add pattern variables to scope
                CollectPatternVariablesToScope(caseNode.Pattern);

                VisitStatements(caseNode.Body);
                PopScope();
                return;
            }
        }
    }

    private void CollectPatternVariablesToScope(AstNode? pattern)
    {
        if (pattern == null) return;

        switch (pattern)
        {
            case VariablePatternNode varPat:
                AddToScope(varPat.Name, "pattern variable", null, varPat.Span, varPat);
                break;

            case VarPatternNode vPat:
                AddToScope(vPat.Name, "var pattern", null, vPat.Span, vPat);
                break;

            case SomePatternNode somePat:
                CollectPatternVariablesToScope(somePat.InnerPattern);
                break;

            case OkPatternNode okPat:
                CollectPatternVariablesToScope(okPat.InnerPattern);
                break;

            case ErrPatternNode errPat:
                CollectPatternVariablesToScope(errPat.InnerPattern);
                break;

            case PositionalPatternNode posPat:
                foreach (var inner in posPat.Patterns)
                {
                    CollectPatternVariablesToScope(inner);
                }
                break;

            case PropertyPatternNode propPat:
                foreach (var match in propPat.Matches)
                {
                    CollectPatternVariablesToScope(match.Pattern);
                }
                break;

            case ListPatternNode listPat:
                foreach (var inner in listPat.Patterns)
                {
                    CollectPatternVariablesToScope(inner);
                }
                if (listPat.SlicePattern != null)
                {
                    CollectPatternVariablesToScope(listPat.SlicePattern);
                }
                break;

            // These patterns don't introduce variables
            case WildcardPatternNode:
            case LiteralPatternNode:
            case ConstantPatternNode:
            case NonePatternNode:
            case RelationalPatternNode:
                break;
        }
    }

    private void VisitUsingStatement(UsingStatementNode usingStmt)
    {
        PushScope(ScopeKind.Using, "using");

        if (!string.IsNullOrEmpty(usingStmt.VariableName))
        {
            AddToScope(usingStmt.VariableName, "using variable",
                usingStmt.VariableType, usingStmt.Span, usingStmt);
        }

        if (SpanContainsOffset(usingStmt.Resource.Span))
        {
            VisitExpression(usingStmt.Resource);
        }
        else
        {
            VisitStatements(usingStmt.Body);
        }

        PopScope();
    }

    private void VisitDictionaryForeach(DictionaryForeachNode dictForeach)
    {
        PushScope(ScopeKind.Loop, dictForeach.Id);

        AddToScope(dictForeach.KeyName, "key variable", null, dictForeach.Span, dictForeach);
        AddToScope(dictForeach.ValueName, "value variable", null, dictForeach.Span, dictForeach);

        VisitStatements(dictForeach.Body);

        PopScope();
    }

    private void VisitExpression(ExpressionNode expr)
    {
        switch (expr)
        {
            case ReferenceNode refNode:
                // This is a variable reference - try to resolve it
                var symbol = LookupInScope(refNode.Name);
                if (symbol != null)
                {
                    _result = new SymbolLookupResult(
                        refNode.Name, "variable reference", symbol.Type,
                        refNode.Span, symbol.Span, symbol.Node);
                }
                else
                {
                    _result = new SymbolLookupResult(
                        refNode.Name, "reference", null,
                        refNode.Span, null, null);
                }
                break;

            case BinaryOperationNode binOp:
                if (SpanContainsOffset(binOp.Left.Span))
                {
                    VisitExpression(binOp.Left);
                }
                else if (SpanContainsOffset(binOp.Right.Span))
                {
                    VisitExpression(binOp.Right);
                }
                break;

            case UnaryOperationNode unaryOp:
                if (SpanContainsOffset(unaryOp.Operand.Span))
                {
                    VisitExpression(unaryOp.Operand);
                }
                break;

            case FieldAccessNode fieldAccess:
                // Resolve the type of the target expression to enable cross-file lookup
                var targetType = ResolveExpressionType(fieldAccess.Target);
                _result = new SymbolLookupResult(
                    fieldAccess.FieldName, "field access", null,
                    fieldAccess.Span, null, fieldAccess, targetType);
                break;

            case CallExpressionNode callExpr:
                VisitCallTarget(callExpr.Target, callExpr.Span);
                foreach (var arg in callExpr.Arguments)
                {
                    if (SpanContainsOffset(arg.Span))
                    {
                        VisitExpression(arg);
                    }
                }
                break;

            case NewExpressionNode newExpr:
                _result = new SymbolLookupResult(
                    newExpr.TypeName, "constructor call", newExpr.TypeName,
                    newExpr.Span, null, newExpr);
                break;

            case ThisExpressionNode:
                _result = new SymbolLookupResult(
                    "this", "keyword", GetCurrentClassName(),
                    expr.Span, null, null);
                break;

            case BaseExpressionNode:
                _result = new SymbolLookupResult(
                    "base", "keyword", GetCurrentBaseClassName(),
                    expr.Span, null, null);
                break;

            case IntLiteralNode intLit:
                _result = new SymbolLookupResult(
                    intLit.Value.ToString(), "integer literal", "i32",
                    intLit.Span, null, intLit);
                break;

            case FloatLiteralNode floatLit:
                _result = new SymbolLookupResult(
                    floatLit.Value.ToString(), "float literal", "f64",
                    floatLit.Span, null, floatLit);
                break;

            case StringLiteralNode strLit:
                _result = new SymbolLookupResult(
                    $"\"{strLit.Value}\"", "string literal", "str",
                    strLit.Span, null, strLit);
                break;

            case BoolLiteralNode boolLit:
                _result = new SymbolLookupResult(
                    boolLit.Value.ToString().ToLowerInvariant(), "boolean literal", "bool",
                    boolLit.Span, null, boolLit);
                break;

            case ConditionalExpressionNode condExpr:
                if (SpanContainsOffset(condExpr.Condition.Span))
                {
                    VisitExpression(condExpr.Condition);
                }
                else if (SpanContainsOffset(condExpr.WhenTrue.Span))
                {
                    VisitExpression(condExpr.WhenTrue);
                }
                else if (SpanContainsOffset(condExpr.WhenFalse.Span))
                {
                    VisitExpression(condExpr.WhenFalse);
                }
                break;

            case AwaitExpressionNode awaitExpr:
                VisitExpression(awaitExpr.Awaited);
                break;

            case LambdaExpressionNode lambdaExpr:
                PushScope(ScopeKind.Lambda, "lambda");
                foreach (var param in lambdaExpr.Parameters)
                {
                    AddToScope(param.Name, "parameter", param.TypeName, param.Span, param);
                }
                if (lambdaExpr.ExpressionBody != null && SpanContainsOffset(lambdaExpr.ExpressionBody.Span))
                {
                    VisitExpression(lambdaExpr.ExpressionBody);
                }
                else if (lambdaExpr.StatementBody != null)
                {
                    VisitStatements(lambdaExpr.StatementBody);
                }
                PopScope();
                break;

            case MatchExpressionNode matchExpr:
                if (SpanContainsOffset(matchExpr.Target.Span))
                {
                    VisitExpression(matchExpr.Target);
                }
                else
                {
                    foreach (var caseNode in matchExpr.Cases)
                    {
                        if (SpanContainsOffset(caseNode.Span))
                        {
                            PushScope(ScopeKind.Block, "case");
                            CollectPatternVariablesToScope(caseNode.Pattern);
                            // Body is a list of statements, visit them
                            VisitStatements(caseNode.Body);
                            PopScope();
                            return;
                        }
                    }
                }
                break;

            case SomeExpressionNode someExpr:
                VisitExpression(someExpr.Value);
                break;

            case OkExpressionNode okExpr:
                VisitExpression(okExpr.Value);
                break;

            case ErrExpressionNode errExpr:
                VisitExpression(errExpr.Error);
                break;

            case ArrayAccessNode arrayAccess:
                if (SpanContainsOffset(arrayAccess.Array.Span))
                {
                    VisitExpression(arrayAccess.Array);
                }
                else if (SpanContainsOffset(arrayAccess.Index.Span))
                {
                    VisitExpression(arrayAccess.Index);
                }
                break;

            case NullCoalesceNode nullCoalesce:
                if (SpanContainsOffset(nullCoalesce.Left.Span))
                {
                    VisitExpression(nullCoalesce.Left);
                }
                else if (SpanContainsOffset(nullCoalesce.Right.Span))
                {
                    VisitExpression(nullCoalesce.Right);
                }
                break;

            case NullConditionalNode nullCond:
                if (SpanContainsOffset(nullCond.Target.Span))
                {
                    VisitExpression(nullCond.Target);
                }
                break;

            case InterpolatedStringNode interpStr:
                foreach (var part in interpStr.Parts)
                {
                    if (part is InterpolatedStringExpressionNode interpExpr && SpanContainsOffset(interpExpr.Span))
                    {
                        VisitExpression(interpExpr.Expression);
                    }
                }
                break;

            // Collection creation expressions
            case ArrayCreationNode arrayCreate:
                _result = new SymbolLookupResult(
                    arrayCreate.ElementType, "array creation", $"[{arrayCreate.ElementType}]",
                    arrayCreate.Span, null, arrayCreate);
                foreach (var elem in arrayCreate.Initializer)
                {
                    if (SpanContainsOffset(elem.Span))
                    {
                        VisitExpression(elem);
                        return;
                    }
                }
                if (arrayCreate.Size != null && SpanContainsOffset(arrayCreate.Size.Span))
                {
                    VisitExpression(arrayCreate.Size);
                }
                break;

            case ListCreationNode listCreate:
                _result = new SymbolLookupResult(
                    "List", "list creation", $"List<{listCreate.ElementType}>",
                    listCreate.Span, null, listCreate);
                foreach (var elem in listCreate.Elements)
                {
                    if (SpanContainsOffset(elem.Span))
                    {
                        VisitExpression(elem);
                        return;
                    }
                }
                break;

            case SetCreationNode setCreate:
                _result = new SymbolLookupResult(
                    "Set", "set creation", $"Set<{setCreate.ElementType}>",
                    setCreate.Span, null, setCreate);
                foreach (var elem in setCreate.Elements)
                {
                    if (SpanContainsOffset(elem.Span))
                    {
                        VisitExpression(elem);
                        return;
                    }
                }
                break;

            case DictionaryCreationNode dictCreate:
                _result = new SymbolLookupResult(
                    "Dict", "dictionary creation", $"Dict<{dictCreate.KeyType},{dictCreate.ValueType}>",
                    dictCreate.Span, null, dictCreate);
                foreach (var kvp in dictCreate.Entries)
                {
                    if (SpanContainsOffset(kvp.Span))
                    {
                        if (SpanContainsOffset(kvp.Key.Span))
                            VisitExpression(kvp.Key);
                        else if (SpanContainsOffset(kvp.Value.Span))
                            VisitExpression(kvp.Value);
                        return;
                    }
                }
                break;

            case RecordCreationNode recordCreate:
                _result = new SymbolLookupResult(
                    recordCreate.TypeName, "record creation", recordCreate.TypeName,
                    recordCreate.Span, null, recordCreate);
                foreach (var field in recordCreate.Fields)
                {
                    if (SpanContainsOffset(field.Value.Span))
                    {
                        VisitExpression(field.Value);
                        return;
                    }
                }
                break;

            // Array operations
            case ArrayLengthNode arrayLen:
                if (SpanContainsOffset(arrayLen.Array.Span))
                {
                    VisitExpression(arrayLen.Array);
                }
                else
                {
                    _result = new SymbolLookupResult(
                        "Length", "array length", "i32",
                        arrayLen.Span, null, arrayLen);
                }
                break;

            // None expression
            case NoneExpressionNode noneExpr:
                _result = new SymbolLookupResult(
                    "None", "none literal", noneExpr.TypeName ?? "?T",
                    noneExpr.Span, null, noneExpr);
                break;

            // Range and index expressions
            case RangeExpressionNode rangeExpr:
                if (rangeExpr.Start != null && SpanContainsOffset(rangeExpr.Start.Span))
                {
                    VisitExpression(rangeExpr.Start);
                }
                else if (rangeExpr.End != null && SpanContainsOffset(rangeExpr.End.Span))
                {
                    VisitExpression(rangeExpr.End);
                }
                else
                {
                    _result = new SymbolLookupResult(
                        "..", "range", "Range",
                        rangeExpr.Span, null, rangeExpr);
                }
                break;

            case IndexFromEndNode indexFromEnd:
                if (SpanContainsOffset(indexFromEnd.Offset.Span))
                {
                    VisitExpression(indexFromEnd.Offset);
                }
                else
                {
                    _result = new SymbolLookupResult(
                        "^", "index from end", "Index",
                        indexFromEnd.Span, null, indexFromEnd);
                }
                break;

            // Quantifier expressions
            case ForallExpressionNode forallExpr:
                PushScope(ScopeKind.Block, "forall");
                foreach (var qvar in forallExpr.BoundVariables)
                {
                    AddToScope(qvar.Name, "quantifier variable", qvar.TypeName, qvar.Span, qvar);
                }
                if (SpanContainsOffset(forallExpr.Body.Span))
                {
                    VisitExpression(forallExpr.Body);
                }
                PopScope();
                break;

            case ExistsExpressionNode existsExpr:
                PushScope(ScopeKind.Block, "exists");
                foreach (var qvar in existsExpr.BoundVariables)
                {
                    AddToScope(qvar.Name, "quantifier variable", qvar.TypeName, qvar.Span, qvar);
                }
                if (SpanContainsOffset(existsExpr.Body.Span))
                {
                    VisitExpression(existsExpr.Body);
                }
                PopScope();
                break;

            case ImplicationExpressionNode implExpr:
                if (SpanContainsOffset(implExpr.Antecedent.Span))
                {
                    VisitExpression(implExpr.Antecedent);
                }
                else if (SpanContainsOffset(implExpr.Consequent.Span))
                {
                    VisitExpression(implExpr.Consequent);
                }
                break;

            // String operations
            case CharOperationNode charOp:
                foreach (var arg in charOp.Arguments)
                {
                    if (SpanContainsOffset(arg.Span))
                    {
                        VisitExpression(arg);
                        return;
                    }
                }
                break;

            case StringOperationNode strOp:
                foreach (var arg in strOp.Arguments)
                {
                    if (SpanContainsOffset(arg.Span))
                    {
                        VisitExpression(arg);
                        return;
                    }
                }
                break;

            case StringBuilderOperationNode sbOp:
                foreach (var arg in sbOp.Arguments)
                {
                    if (SpanContainsOffset(arg.Span))
                    {
                        VisitExpression(arg);
                        return;
                    }
                }
                break;

            // With expression (record copying with modifications)
            case WithExpressionNode withExpr:
                if (SpanContainsOffset(withExpr.Target.Span))
                {
                    VisitExpression(withExpr.Target);
                }
                else
                {
                    foreach (var assignment in withExpr.Assignments)
                    {
                        if (SpanContainsOffset(assignment.Value.Span))
                        {
                            VisitExpression(assignment.Value);
                            return;
                        }
                    }
                }
                break;

            // Type expressions
            case GenericTypeNode genericType:
                _result = new SymbolLookupResult(
                    genericType.TypeName, "generic type", genericType.TypeName,
                    genericType.Span, null, genericType);
                break;

            // Note: TypeReferenceNode doesn't extend ExpressionNode, so it won't be matched here

            // Collection query expressions
            case CollectionContainsNode contains:
                if (SpanContainsOffset(contains.KeyOrValue.Span))
                {
                    VisitExpression(contains.KeyOrValue);
                }
                break;

            case CollectionCountNode countNode:
                _result = new SymbolLookupResult(
                    "Count", "count", "i32",
                    countNode.Span, null, countNode);
                break;
        }
    }

    private void VisitCallTarget(string targetName, TextSpan span)
    {
        // Parse the target name (e.g., "Math.Max" or "obj.Method")
        var parts = targetName.Split('.');
        if (parts.Length == 1)
        {
            // Simple function call
            var symbol = LookupInScope(targetName);
            if (symbol != null)
            {
                _result = new SymbolLookupResult(
                    targetName, "function call", symbol.Type,
                    span, symbol.Span, symbol.Node);
            }
            else
            {
                _result = new SymbolLookupResult(
                    targetName, "function call", null,
                    span, null, null);
            }
        }
        else
        {
            // Method call on object/type (e.g., "person.GetName" or "Math.Max")
            var targetPart = parts[0];
            var methodName = parts[^1];

            // Try to resolve the type of the target
            string? containingType = null;

            // First check if it's a variable in scope
            var targetSymbol = LookupInScope(targetPart);
            if (targetSymbol != null)
            {
                containingType = targetSymbol.Type;
            }
            else
            {
                // It might be a type name for static method call (e.g., Math.Max)
                containingType = targetPart;
            }

            _result = new SymbolLookupResult(
                methodName, "method call", null,
                span, null, null, containingType);
        }
    }

    private string? GetCurrentClassName()
    {
        for (int i = _scopeStack.Count - 1; i >= 0; i--)
        {
            if (_scopeStack[i].Kind == ScopeKind.Class)
            {
                return _scopeStack[i].Name;
            }
        }
        return null;
    }

    /// <summary>
    /// Resolves the type of an expression for cross-file member lookup.
    /// </summary>
    private string? ResolveExpressionType(ExpressionNode expr)
    {
        switch (expr)
        {
            case ReferenceNode refNode:
                // Look up the variable in scope to get its type
                var symbol = LookupInScope(refNode.Name);
                return symbol?.Type;

            case ThisExpressionNode:
                return GetCurrentClassName();

            case NewExpressionNode newExpr:
                return newExpr.TypeName;

            case FieldAccessNode fieldAccess:
                // For chained access (a.b.c), recursively resolve
                var parentType = ResolveExpressionType(fieldAccess.Target);
                // The field's type would need type info from the class definition
                // For now, return null to fall back to other resolution methods
                return null;

            case CallExpressionNode callExpr:
                // Would need to look up the function/method return type
                return null;

            case ArrayAccessNode:
                // Would need to resolve element type of the array/list
                return null;

            case RecordCreationNode recordCreate:
                return recordCreate.TypeName;

            default:
                return null;
        }
    }

    private string? GetCurrentBaseClassName()
    {
        // Would need to track base class info in the class scope
        return null;
    }

    private bool SpanContainsOffset(TextSpan span)
    {
        return _targetOffset >= span.Start && _targetOffset < span.Start + span.Length;
    }

    private void PushScope(ScopeKind kind, string name)
    {
        _scopeStack.Add(new ScopeInfo(kind, name));
    }

    private void PopScope()
    {
        if (_scopeStack.Count > 0)
        {
            _scopeStack.RemoveAt(_scopeStack.Count - 1);
        }
    }

    private void AddToScope(string name, string kind, string? type, TextSpan span, AstNode node)
    {
        if (_scopeStack.Count > 0)
        {
            _scopeStack[^1].Symbols[name] = new ScopeSymbol(name, kind, type, span, node);
        }
    }

    private ScopeSymbol? LookupInScope(string name)
    {
        // Search from innermost to outermost scope
        for (int i = _scopeStack.Count - 1; i >= 0; i--)
        {
            if (_scopeStack[i].Symbols.TryGetValue(name, out var symbol))
            {
                return symbol;
            }
        }
        return null;
    }
}

/// <summary>
/// Represents a symbol visible at a position.
/// </summary>
public sealed class VisibleSymbol
{
    public string Name { get; }
    public string Kind { get; }
    public string? Type { get; }
    public TextSpan Span { get; }
    public AstNode? Node { get; }

    public VisibleSymbol(string name, string kind, string? type, TextSpan span, AstNode? node)
    {
        Name = name;
        Kind = kind;
        Type = type;
        Span = span;
        Node = node;
    }
}

internal enum ScopeKind
{
    Module,
    Function,
    Class,
    Method,
    Constructor,
    Loop,
    Block,
    Catch,
    Using,
    Lambda,
    EnumExtension
}

internal sealed class ScopeInfo
{
    public ScopeKind Kind { get; }
    public string Name { get; }
    public Dictionary<string, ScopeSymbol> Symbols { get; } = new(StringComparer.Ordinal);

    public ScopeInfo(ScopeKind kind, string name)
    {
        Kind = kind;
        Name = name;
    }
}

internal sealed class ScopeSymbol
{
    public string Name { get; }
    public string Kind { get; }
    public string? Type { get; }
    public TextSpan Span { get; }
    public AstNode? Node { get; }

    public ScopeSymbol(string name, string kind, string? type, TextSpan span, AstNode? node)
    {
        Name = name;
        Kind = kind;
        Type = type;
        Span = span;
        Node = node;
    }
}
