using Calor.Compiler.Ast;
using Calor.Compiler.Parsing;
using Calor.LanguageServer.State;
using Calor.LanguageServer.Utilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

using TextDocumentSelector = OmniSharp.Extensions.LanguageServer.Protocol.Models.TextDocumentSelector;

namespace Calor.LanguageServer.Handlers;

/// <summary>
/// Handles find-all-references requests.
/// </summary>
public sealed class ReferencesHandler : ReferencesHandlerBase
{
    private readonly WorkspaceState _workspace;

    public ReferencesHandler(WorkspaceState workspace)
    {
        _workspace = workspace;
    }

    public override Task<LocationContainer?> Handle(ReferenceParams request, CancellationToken cancellationToken)
    {
        var state = _workspace.Get(request.TextDocument.Uri);
        if (state?.Ast == null)
        {
            return Task.FromResult<LocationContainer?>(null);
        }

        // Convert LSP position to Calor position
        var (line, column) = PositionConverter.ToCalorPosition(request.Position);

        // Find the symbol at the cursor position
        var result = SymbolFinder.FindSymbolAtPosition(state.Ast, line, column, state.Source);
        if (result == null || string.IsNullOrEmpty(result.Name))
        {
            return Task.FromResult<LocationContainer?>(null);
        }

        var locations = new List<Location>();
        var symbolName = result.Name;

        // Search for references in all open documents
        foreach (var doc in _workspace.GetAllDocuments())
        {
            if (doc.Ast == null) continue;

            var refs = FindReferencesInDocument(doc, symbolName, request.Context.IncludeDeclaration);
            locations.AddRange(refs);
        }

        if (locations.Count == 0)
        {
            return Task.FromResult<LocationContainer?>(null);
        }

        return Task.FromResult<LocationContainer?>(new LocationContainer(locations));
    }

    private IEnumerable<Location> FindReferencesInDocument(DocumentState doc, string symbolName, bool includeDeclaration)
    {
        if (doc.Ast == null) yield break;

        var uri = OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.From(doc.Uri);
        var collector = new ReferenceCollector(symbolName, includeDeclaration);
        collector.Visit(doc.Ast);

        foreach (var span in collector.References)
        {
            var range = PositionConverter.ToLspRange(span, doc.Source);
            yield return new Location { Uri = uri, Range = range };
        }
    }

    protected override ReferenceRegistrationOptions CreateRegistrationOptions(
        ReferenceCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new ReferenceRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("calor")
        };
    }
}

/// <summary>
/// Collects all references to a symbol in an AST.
/// </summary>
public sealed class ReferenceCollector
{
    private readonly string _symbolName;
    private readonly bool _includeDeclaration;
    private readonly List<TextSpan> _references = new();

    public IReadOnlyList<TextSpan> References => _references;

    public ReferenceCollector(string symbolName, bool includeDeclaration)
    {
        _symbolName = symbolName;
        _includeDeclaration = includeDeclaration;
    }

    public void Visit(ModuleNode module)
    {
        // Check module-level declarations
        foreach (var func in module.Functions)
        {
            if (_includeDeclaration && func.Name == _symbolName)
            {
                _references.Add(func.Span);
            }
            VisitFunction(func);
        }

        foreach (var cls in module.Classes)
        {
            if (_includeDeclaration && cls.Name == _symbolName)
            {
                _references.Add(cls.Span);
            }
            VisitClass(cls);
        }

        foreach (var iface in module.Interfaces)
        {
            if (_includeDeclaration && iface.Name == _symbolName)
            {
                _references.Add(iface.Span);
            }
        }

        foreach (var enumDef in module.Enums)
        {
            if (_includeDeclaration && enumDef.Name == _symbolName)
            {
                _references.Add(enumDef.Span);
            }
            foreach (var member in enumDef.Members)
            {
                if (_includeDeclaration && member.Name == _symbolName)
                {
                    _references.Add(member.Span);
                }
            }
        }

        foreach (var enumExt in module.EnumExtensions)
        {
            foreach (var method in enumExt.Methods)
            {
                VisitFunction(method);
            }
        }

        foreach (var del in module.Delegates)
        {
            if (_includeDeclaration && del.Name == _symbolName)
            {
                _references.Add(del.Span);
            }
        }
    }

    private void VisitFunction(FunctionNode func)
    {
        // Check parameters
        foreach (var param in func.Parameters)
        {
            if (_includeDeclaration && param.Name == _symbolName)
            {
                _references.Add(param.Span);
            }
        }

        // Visit body
        VisitStatements(func.Body);
    }

    private void VisitClass(ClassDefinitionNode cls)
    {
        // Check fields
        foreach (var field in cls.Fields)
        {
            if (_includeDeclaration && field.Name == _symbolName)
            {
                _references.Add(field.Span);
            }
        }

        // Check properties
        foreach (var prop in cls.Properties)
        {
            if (_includeDeclaration && prop.Name == _symbolName)
            {
                _references.Add(prop.Span);
            }
        }

        // Check methods
        foreach (var method in cls.Methods)
        {
            if (_includeDeclaration && method.Name == _symbolName)
            {
                _references.Add(method.Span);
            }
            VisitMethod(method);
        }

        // Check constructors
        foreach (var ctor in cls.Constructors)
        {
            VisitConstructor(ctor);
        }
    }

    private void VisitMethod(MethodNode method)
    {
        foreach (var param in method.Parameters)
        {
            if (_includeDeclaration && param.Name == _symbolName)
            {
                _references.Add(param.Span);
            }
        }
        VisitStatements(method.Body);
    }

    private void VisitConstructor(ConstructorNode ctor)
    {
        foreach (var param in ctor.Parameters)
        {
            if (_includeDeclaration && param.Name == _symbolName)
            {
                _references.Add(param.Span);
            }
        }
        VisitStatements(ctor.Body);
    }

    private void VisitStatements(IReadOnlyList<StatementNode> statements)
    {
        foreach (var stmt in statements)
        {
            VisitStatement(stmt);
        }
    }

    private void VisitStatement(StatementNode stmt)
    {
        switch (stmt)
        {
            case BindStatementNode bind:
                if (_includeDeclaration && bind.Name == _symbolName)
                {
                    _references.Add(bind.Span);
                }
                if (bind.Initializer != null)
                {
                    VisitExpression(bind.Initializer);
                }
                break;

            case ReturnStatementNode ret:
                if (ret.Expression != null) VisitExpression(ret.Expression);
                break;

            case PrintStatementNode print:
                VisitExpression(print.Expression);
                break;

            case CallStatementNode call:
                if (call.Target == _symbolName || call.Target.EndsWith("." + _symbolName))
                {
                    _references.Add(call.Span);
                }
                foreach (var arg in call.Arguments)
                {
                    VisitExpression(arg);
                }
                break;

            case AssignmentStatementNode assign:
                VisitExpression(assign.Target);
                VisitExpression(assign.Value);
                break;

            case ForStatementNode forStmt:
                if (_includeDeclaration && forStmt.VariableName == _symbolName)
                {
                    _references.Add(forStmt.Span);
                }
                VisitExpression(forStmt.From);
                VisitExpression(forStmt.To);
                if (forStmt.Step != null) VisitExpression(forStmt.Step);
                VisitStatements(forStmt.Body);
                break;

            case WhileStatementNode whileStmt:
                VisitExpression(whileStmt.Condition);
                VisitStatements(whileStmt.Body);
                break;

            case DoWhileStatementNode doWhileStmt:
                VisitStatements(doWhileStmt.Body);
                VisitExpression(doWhileStmt.Condition);
                break;

            case IfStatementNode ifStmt:
                VisitExpression(ifStmt.Condition);
                VisitStatements(ifStmt.ThenBody);
                foreach (var elseIf in ifStmt.ElseIfClauses)
                {
                    VisitExpression(elseIf.Condition);
                    VisitStatements(elseIf.Body);
                }
                if (ifStmt.ElseBody != null)
                {
                    VisitStatements(ifStmt.ElseBody);
                }
                break;

            case TryStatementNode tryStmt:
                VisitStatements(tryStmt.TryBody);
                foreach (var catchClause in tryStmt.CatchClauses)
                {
                    if (_includeDeclaration && catchClause.VariableName == _symbolName)
                    {
                        _references.Add(catchClause.Span);
                    }
                    VisitStatements(catchClause.Body);
                }
                if (tryStmt.FinallyBody != null)
                {
                    VisitStatements(tryStmt.FinallyBody);
                }
                break;

            case ForeachStatementNode foreachStmt:
                if (_includeDeclaration && foreachStmt.VariableName == _symbolName)
                {
                    _references.Add(foreachStmt.Span);
                }
                VisitExpression(foreachStmt.Collection);
                VisitStatements(foreachStmt.Body);
                break;

            case MatchStatementNode matchStmt:
                VisitExpression(matchStmt.Target);
                foreach (var caseNode in matchStmt.Cases)
                {
                    VisitStatements(caseNode.Body);
                }
                break;

            case UsingStatementNode usingStmt:
                if (_includeDeclaration && usingStmt.VariableName == _symbolName)
                {
                    _references.Add(usingStmt.Span);
                }
                VisitExpression(usingStmt.Resource);
                VisitStatements(usingStmt.Body);
                break;

            case DictionaryForeachNode dictForeach:
                if (_includeDeclaration && (dictForeach.KeyName == _symbolName || dictForeach.ValueName == _symbolName))
                {
                    _references.Add(dictForeach.Span);
                }
                VisitStatements(dictForeach.Body);
                break;

            case ThrowStatementNode throwStmt:
                if (throwStmt.Exception != null) VisitExpression(throwStmt.Exception);
                break;

            case CollectionPushNode push:
                VisitExpression(push.Value);
                break;

            case DictionaryPutNode put:
                VisitExpression(put.Key);
                VisitExpression(put.Value);
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

            case CompoundAssignmentStatementNode compoundAssign:
                VisitExpression(compoundAssign.Target);
                VisitExpression(compoundAssign.Value);
                break;
        }
    }

    private void VisitExpression(ExpressionNode expr)
    {
        switch (expr)
        {
            case ReferenceNode refNode:
                if (refNode.Name == _symbolName)
                {
                    _references.Add(refNode.Span);
                }
                break;

            case BinaryOperationNode binOp:
                VisitExpression(binOp.Left);
                VisitExpression(binOp.Right);
                break;

            case UnaryOperationNode unaryOp:
                VisitExpression(unaryOp.Operand);
                break;

            case CallExpressionNode callExpr:
                if (callExpr.Target == _symbolName || callExpr.Target.EndsWith("." + _symbolName))
                {
                    _references.Add(callExpr.Span);
                }
                foreach (var arg in callExpr.Arguments)
                {
                    VisitExpression(arg);
                }
                break;

            case FieldAccessNode fieldAccess:
                VisitExpression(fieldAccess.Target);
                if (fieldAccess.FieldName == _symbolName)
                {
                    _references.Add(fieldAccess.Span);
                }
                break;

            case NewExpressionNode newExpr:
                if (newExpr.TypeName == _symbolName)
                {
                    _references.Add(newExpr.Span);
                }
                foreach (var arg in newExpr.Arguments)
                {
                    VisitExpression(arg);
                }
                break;

            case ConditionalExpressionNode condExpr:
                VisitExpression(condExpr.Condition);
                VisitExpression(condExpr.WhenTrue);
                VisitExpression(condExpr.WhenFalse);
                break;

            case AwaitExpressionNode awaitExpr:
                VisitExpression(awaitExpr.Awaited);
                break;

            case LambdaExpressionNode lambdaExpr:
                foreach (var param in lambdaExpr.Parameters)
                {
                    if (_includeDeclaration && param.Name == _symbolName)
                    {
                        _references.Add(param.Span);
                    }
                }
                if (lambdaExpr.ExpressionBody != null)
                {
                    VisitExpression(lambdaExpr.ExpressionBody);
                }
                else if (lambdaExpr.StatementBody != null)
                {
                    VisitStatements(lambdaExpr.StatementBody);
                }
                break;

            case MatchExpressionNode matchExpr:
                VisitExpression(matchExpr.Target);
                foreach (var caseNode in matchExpr.Cases)
                {
                    VisitStatements(caseNode.Body);
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
                VisitExpression(arrayAccess.Array);
                VisitExpression(arrayAccess.Index);
                break;

            case ArrayCreationNode arrayCreate:
                foreach (var elem in arrayCreate.Initializer)
                {
                    VisitExpression(elem);
                }
                if (arrayCreate.Size != null) VisitExpression(arrayCreate.Size);
                break;

            case ArrayLengthNode arrayLen:
                VisitExpression(arrayLen.Array);
                break;

            case ListCreationNode listCreate:
                foreach (var elem in listCreate.Elements)
                {
                    VisitExpression(elem);
                }
                break;

            case SetCreationNode setCreate:
                foreach (var elem in setCreate.Elements)
                {
                    VisitExpression(elem);
                }
                break;

            case DictionaryCreationNode dictCreate:
                foreach (var entry in dictCreate.Entries)
                {
                    VisitExpression(entry.Key);
                    VisitExpression(entry.Value);
                }
                break;

            case RecordCreationNode recordCreate:
                if (recordCreate.TypeName == _symbolName)
                {
                    _references.Add(recordCreate.Span);
                }
                foreach (var field in recordCreate.Fields)
                {
                    VisitExpression(field.Value);
                }
                break;

            case NullCoalesceNode nullCoalesce:
                VisitExpression(nullCoalesce.Left);
                VisitExpression(nullCoalesce.Right);
                break;

            case NullConditionalNode nullCond:
                VisitExpression(nullCond.Target);
                break;

            case InterpolatedStringNode interpStr:
                foreach (var part in interpStr.Parts)
                {
                    if (part is InterpolatedStringExpressionNode interpExpr)
                    {
                        VisitExpression(interpExpr.Expression);
                    }
                }
                break;

            case RangeExpressionNode rangeExpr:
                if (rangeExpr.Start != null) VisitExpression(rangeExpr.Start);
                if (rangeExpr.End != null) VisitExpression(rangeExpr.End);
                break;

            case IndexFromEndNode indexFromEnd:
                VisitExpression(indexFromEnd.Offset);
                break;

            case ForallExpressionNode forallExpr:
                VisitExpression(forallExpr.Body);
                break;

            case ExistsExpressionNode existsExpr:
                VisitExpression(existsExpr.Body);
                break;

            case ImplicationExpressionNode implExpr:
                VisitExpression(implExpr.Antecedent);
                VisitExpression(implExpr.Consequent);
                break;

            case WithExpressionNode withExpr:
                VisitExpression(withExpr.Target);
                foreach (var assignment in withExpr.Assignments)
                {
                    VisitExpression(assignment.Value);
                }
                break;

            case CollectionContainsNode contains:
                VisitExpression(contains.KeyOrValue);
                break;

            case CollectionCountNode countNode:
                VisitExpression(countNode.Collection);
                break;

            case CharOperationNode charOp:
                foreach (var arg in charOp.Arguments)
                {
                    VisitExpression(arg);
                }
                break;

            case StringOperationNode strOp:
                foreach (var arg in strOp.Arguments)
                {
                    VisitExpression(arg);
                }
                break;

            case StringBuilderOperationNode sbOp:
                foreach (var arg in sbOp.Arguments)
                {
                    VisitExpression(arg);
                }
                break;
        }
    }
}
