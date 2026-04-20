using Calor.Compiler.Binding;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.Analysis.Dataflow;

/// <summary>
/// Represents a basic block in a control flow graph.
/// A basic block is a sequence of statements with one entry and one exit point.
/// </summary>
public sealed class BasicBlock
{
    private static int _nextId;

    public int Id { get; }
    public List<BoundStatement> Statements { get; } = new();
    public List<BasicBlock> Predecessors { get; } = new();
    public List<BasicBlock> Successors { get; } = new();

    /// <summary>
    /// The condition expression for conditional branches (null for unconditional).
    /// </summary>
    public BoundExpression? BranchCondition { get; set; }

    /// <summary>
    /// Whether this block ends with a return statement.
    /// </summary>
    public bool IsExit { get; set; }

    /// <summary>
    /// Whether this is the entry block of the function.
    /// </summary>
    public bool IsEntry { get; set; }

    /// <summary>
    /// The text span covering all statements in this block.
    /// </summary>
    public TextSpan Span => Statements.Count > 0
        ? new TextSpan(
            Statements[0].Span.Start,
            Statements[^1].Span.End,
            Statements[0].Span.Line,
            Statements[0].Span.Column)
        : default;

    public BasicBlock()
    {
        Id = Interlocked.Increment(ref _nextId);
    }

    public void AddSuccessor(BasicBlock successor)
    {
        if (!Successors.Contains(successor))
        {
            Successors.Add(successor);
            successor.Predecessors.Add(this);
        }
    }

    public override string ToString() => $"BB{Id} ({Statements.Count} stmts)";
}

/// <summary>
/// Represents a control flow graph for a function.
/// </summary>
public sealed class ControlFlowGraph
{
    public BasicBlock Entry { get; }
    public BasicBlock Exit { get; }
    public IReadOnlyList<BasicBlock> Blocks { get; }
    public BoundFunction Function { get; }

    private ControlFlowGraph(BoundFunction function, BasicBlock entry, BasicBlock exit, IReadOnlyList<BasicBlock> blocks)
    {
        Function = function;
        Entry = entry;
        Exit = exit;
        Blocks = blocks;
    }

    /// <summary>
    /// Builds a control flow graph from a bound function.
    /// </summary>
    public static ControlFlowGraph Build(BoundFunction function)
    {
        var builder = new CfgBuilder();
        return builder.Build(function);
    }

    /// <summary>
    /// Gets all blocks in reverse post-order (topological order for forward analysis).
    /// </summary>
    public IReadOnlyList<BasicBlock> GetReversePostOrder()
    {
        var visited = new HashSet<BasicBlock>();
        var postOrder = new List<BasicBlock>();

        void Visit(BasicBlock block)
        {
            if (!visited.Add(block))
                return;

            foreach (var succ in block.Successors)
                Visit(succ);

            postOrder.Add(block);
        }

        Visit(Entry);
        postOrder.Reverse();
        return postOrder;
    }

    /// <summary>
    /// Gets all blocks in post-order (for backward analysis).
    /// </summary>
    public IReadOnlyList<BasicBlock> GetPostOrder()
    {
        var visited = new HashSet<BasicBlock>();
        var postOrder = new List<BasicBlock>();

        void Visit(BasicBlock block)
        {
            if (!visited.Add(block))
                return;

            foreach (var succ in block.Successors)
                Visit(succ);

            postOrder.Add(block);
        }

        Visit(Entry);
        return postOrder;
    }

    private sealed class CfgBuilder
    {
        private readonly List<BasicBlock> _blocks = new();
        private BasicBlock _currentBlock = null!;
        private BasicBlock _exitBlock = null!;
        private readonly Dictionary<string, BasicBlock> _labelTargets = new();
        private readonly List<(BasicBlock Block, string Label)> _pendingGotos = new();

        // Loop context stacks for break/continue handling
        private readonly Stack<BasicBlock> _loopExitStack = new();
        private readonly Stack<BasicBlock> _loopConditionStack = new();
        // Try context: when inside a try, throws go to catch blocks instead of exit
        private readonly Stack<BasicBlock> _exceptionTargetStack = new();

        public ControlFlowGraph Build(BoundFunction function)
        {
            // Create entry and exit blocks
            var entry = CreateBlock();
            entry.IsEntry = true;

            _exitBlock = CreateBlock();
            _exitBlock.IsExit = true;

            _currentBlock = entry;

            // Process all statements
            foreach (var stmt in function.Body)
            {
                ProcessStatement(stmt);
            }

            // Connect the last block to exit if it doesn't already have a terminator
            if (_currentBlock != _exitBlock && _currentBlock.Successors.Count == 0)
            {
                _currentBlock.AddSuccessor(_exitBlock);
            }

            // Resolve pending gotos (for future IR-level CFG construction)
            foreach (var (block, label) in _pendingGotos)
            {
                if (_labelTargets.TryGetValue(label, out var target))
                {
                    block.AddSuccessor(target);
                }
            }

            return new ControlFlowGraph(function, entry, _exitBlock, _blocks);
        }

        private BasicBlock CreateBlock()
        {
            var block = new BasicBlock();
            _blocks.Add(block);
            return block;
        }

        private void ProcessStatement(BoundStatement stmt)
        {
            switch (stmt)
            {
                case BoundBindStatement bind:
                    _currentBlock.Statements.Add(bind);
                    break;

                case BoundCallStatement call:
                    _currentBlock.Statements.Add(call);
                    break;

                case BoundReturnStatement ret:
                    _currentBlock.Statements.Add(ret);
                    _currentBlock.AddSuccessor(_exitBlock);
                    _currentBlock = CreateBlock(); // Dead block after return
                    break;

                case BoundIfStatement ifStmt:
                    ProcessIfStatement(ifStmt);
                    break;

                case BoundWhileStatement whileStmt:
                    ProcessWhileStatement(whileStmt);
                    break;

                case BoundForStatement forStmt:
                    ProcessForStatement(forStmt);
                    break;

                case BoundBreakStatement breakStmt:
                    ProcessBreakStatement(breakStmt);
                    break;

                case BoundContinueStatement continueStmt:
                    ProcessContinueStatement(continueStmt);
                    break;

                case BoundTryStatement tryStmt:
                    ProcessTryStatement(tryStmt);
                    break;

                case BoundMatchStatement matchStmt:
                    ProcessMatchStatement(matchStmt);
                    break;

                // New statement types for class member analysis
                case BoundAssignmentStatement assign:
                    _currentBlock.Statements.Add(assign);
                    break;

                case BoundCompoundAssignment compound:
                    _currentBlock.Statements.Add(compound);
                    break;

                case BoundExpressionStatement exprStmt:
                    _currentBlock.Statements.Add(exprStmt);
                    break;

                case BoundThrowStatement throwStmt:
                    _currentBlock.Statements.Add(throwStmt);
                    // If inside a try block, throw goes to catch; otherwise to exit
                    var throwTarget = _exceptionTargetStack.Count > 0
                        ? _exceptionTargetStack.Peek()
                        : _exitBlock;
                    _currentBlock.AddSuccessor(throwTarget);
                    _currentBlock = CreateBlock(); // Dead block after throw
                    break;

                case BoundForeachStatement forEach:
                    ProcessForeachStatement(forEach);
                    break;

                case BoundDoWhileStatement doWhile:
                    ProcessDoWhileStatement(doWhile);
                    break;

                case BoundUsingStatement usingStmt:
                    ProcessUsingStatement(usingStmt);
                    break;

                case BoundUnsupportedStatement unsupported:
                    // Best-effort: may throw/return (exit edge) or fall through.
                    // Split block so later statements don't share the exit edge.
                    _currentBlock.Statements.Add(unsupported);
                    _currentBlock.AddSuccessor(_exitBlock);
                    var afterUnsupported = CreateBlock();
                    _currentBlock.AddSuccessor(afterUnsupported);
                    _currentBlock = afterUnsupported;
                    break;

                default:
                    // Unknown statement type - just add it
                    _currentBlock.Statements.Add(stmt);
                    break;
            }
        }

        private void ProcessIfStatement(BoundIfStatement ifStmt)
        {
            // Current block ends with the condition
            _currentBlock.BranchCondition = ifStmt.Condition;

            var thenBlock = CreateBlock();
            var mergeBlock = CreateBlock();

            // Process then branch
            _currentBlock.AddSuccessor(thenBlock);
            _currentBlock = thenBlock;

            foreach (var s in ifStmt.ThenBody)
                ProcessStatement(s);

            // Connect then branch to merge (if it didn't return)
            if (_currentBlock.Successors.Count == 0)
                _currentBlock.AddSuccessor(mergeBlock);

            // Process else-if clauses
            var prevBlock = _blocks[^3]; // The block that had the original condition
            foreach (var elseIf in ifStmt.ElseIfClauses)
            {
                var elseIfCondBlock = CreateBlock();
                prevBlock.AddSuccessor(elseIfCondBlock);
                elseIfCondBlock.BranchCondition = elseIf.Condition;

                var elseIfBodyBlock = CreateBlock();
                elseIfCondBlock.AddSuccessor(elseIfBodyBlock);
                _currentBlock = elseIfBodyBlock;

                foreach (var s in elseIf.Body)
                    ProcessStatement(s);

                if (_currentBlock.Successors.Count == 0)
                    _currentBlock.AddSuccessor(mergeBlock);

                prevBlock = elseIfCondBlock;
            }

            // Process else branch
            if (ifStmt.ElseBody != null && ifStmt.ElseBody.Count > 0)
            {
                var elseBlock = CreateBlock();
                prevBlock.AddSuccessor(elseBlock);
                _currentBlock = elseBlock;

                foreach (var s in ifStmt.ElseBody)
                    ProcessStatement(s);

                if (_currentBlock.Successors.Count == 0)
                    _currentBlock.AddSuccessor(mergeBlock);
            }
            else
            {
                // No else branch - fall through to merge
                prevBlock.AddSuccessor(mergeBlock);
            }

            _currentBlock = mergeBlock;
        }

        private void ProcessWhileStatement(BoundWhileStatement whileStmt)
        {
            // Create blocks for the loop structure
            var conditionBlock = CreateBlock();
            var bodyBlock = CreateBlock();
            var afterBlock = CreateBlock();

            // Push loop context for break/continue
            _loopConditionStack.Push(conditionBlock);
            _loopExitStack.Push(afterBlock);

            // Connect current block to condition
            _currentBlock.AddSuccessor(conditionBlock);

            // Condition block
            conditionBlock.BranchCondition = whileStmt.Condition;
            conditionBlock.AddSuccessor(bodyBlock);
            conditionBlock.AddSuccessor(afterBlock);

            // Process body
            _currentBlock = bodyBlock;
            foreach (var s in whileStmt.Body)
                ProcessStatement(s);

            // Loop back to condition (if we didn't return/break)
            if (_currentBlock.Successors.Count == 0)
                _currentBlock.AddSuccessor(conditionBlock);

            // Pop loop context
            _loopConditionStack.Pop();
            _loopExitStack.Pop();

            _currentBlock = afterBlock;
        }

        private void ProcessForStatement(BoundForStatement forStmt)
        {
            // Create blocks for the loop structure
            // For loop: init -> condition -> body -> update -> condition
            var conditionBlock = CreateBlock();
            var bodyBlock = CreateBlock();
            var afterBlock = CreateBlock();

            // Push loop context for break/continue
            _loopConditionStack.Push(conditionBlock);
            _loopExitStack.Push(afterBlock);

            // Initialize loop variable (already in forStmt.From)
            // The loop variable initialization is implicit in the for structure

            // Connect to condition block
            _currentBlock.AddSuccessor(conditionBlock);

            // Condition block - comparison with To value
            conditionBlock.AddSuccessor(bodyBlock);
            conditionBlock.AddSuccessor(afterBlock);

            // Process body
            _currentBlock = bodyBlock;
            foreach (var s in forStmt.Body)
                ProcessStatement(s);

            // Loop back to condition (the step is implicit in for loop semantics)
            if (_currentBlock.Successors.Count == 0)
                _currentBlock.AddSuccessor(conditionBlock);

            // Pop loop context
            _loopConditionStack.Pop();
            _loopExitStack.Pop();

            _currentBlock = afterBlock;
        }

        private void ProcessBreakStatement(BoundBreakStatement breakStmt)
        {
            _currentBlock.Statements.Add(breakStmt);

            if (_loopExitStack.Count > 0)
            {
                // Connect to the loop exit block
                _currentBlock.AddSuccessor(_loopExitStack.Peek());
            }

            // Create a dead block after break (code after break is unreachable)
            _currentBlock = CreateBlock();
        }

        private void ProcessContinueStatement(BoundContinueStatement continueStmt)
        {
            _currentBlock.Statements.Add(continueStmt);

            if (_loopConditionStack.Count > 0)
            {
                // Connect to the loop condition block
                _currentBlock.AddSuccessor(_loopConditionStack.Peek());
            }

            // Create a dead block after continue (code after continue is unreachable)
            _currentBlock = CreateBlock();
        }

        private void ProcessTryStatement(BoundTryStatement tryStmt)
        {
            // Create blocks for try-catch-finally structure
            var tryBlock = CreateBlock();
            var afterBlock = CreateBlock();

            // Create a landing block for exceptions thrown inside the try body
            var exceptionLanding = CreateBlock();
            _exceptionTargetStack.Push(exceptionLanding);

            // Connect current block to try block
            _currentBlock.AddSuccessor(tryBlock);
            _currentBlock = tryBlock;

            // Process try body (throws inside will now go to exceptionLanding)
            foreach (var s in tryStmt.TryBody)
                ProcessStatement(s);

            _exceptionTargetStack.Pop();

            // Save the block that ends the try body
            var tryEndBlock = _currentBlock;

            // Create blocks for catch clauses
            var catchBlocks = new List<BasicBlock>();
            foreach (var catchClause in tryStmt.CatchClauses)
            {
                var catchBlock = CreateBlock();
                catchBlocks.Add(catchBlock);

                // Exception edges: try body can jump to catch on exception
                tryBlock.AddSuccessor(catchBlock);
                // Throws inside try also land here
                exceptionLanding.AddSuccessor(catchBlock);

                _currentBlock = catchBlock;
                foreach (var s in catchClause.Body)
                    ProcessStatement(s);

                // Connect catch end to after block (if it didn't return/throw)
                if (_currentBlock.Successors.Count == 0)
                    _currentBlock.AddSuccessor(afterBlock);
            }

            // If no catch blocks, exception landing goes to exit
            if (catchBlocks.Count == 0 && exceptionLanding.Successors.Count == 0)
                exceptionLanding.AddSuccessor(_exitBlock);

            // Process finally block if present
            if (tryStmt.FinallyBody != null && tryStmt.FinallyBody.Count > 0)
            {
                var finallyBlock = CreateBlock();

                // All paths go through finally
                if (tryEndBlock.Successors.Count == 0)
                    tryEndBlock.AddSuccessor(finallyBlock);

                foreach (var catchBlock in catchBlocks)
                {
                    // Also connect catch blocks to finally (already connected to after or exit)
                }

                _currentBlock = finallyBlock;
                foreach (var s in tryStmt.FinallyBody)
                    ProcessStatement(s);

                // Finally block continues to after block
                if (_currentBlock.Successors.Count == 0)
                    _currentBlock.AddSuccessor(afterBlock);
            }
            else
            {
                // No finally - try end goes directly to after
                if (tryEndBlock.Successors.Count == 0)
                    tryEndBlock.AddSuccessor(afterBlock);
            }

            _currentBlock = afterBlock;
        }

        private void ProcessMatchStatement(BoundMatchStatement matchStmt)
        {
            // Pattern matching is essentially a multi-way branch
            var afterBlock = CreateBlock();

            // Current block has the target expression
            var matchConditionBlock = _currentBlock;

            foreach (var matchCase in matchStmt.Cases)
            {
                var caseBlock = CreateBlock();
                matchConditionBlock.AddSuccessor(caseBlock);

                // If there's a guard, the case block has a branch condition
                if (matchCase.Guard != null)
                {
                    caseBlock.BranchCondition = matchCase.Guard;
                }

                _currentBlock = caseBlock;

                foreach (var s in matchCase.Body)
                    ProcessStatement(s);

                // Connect case end to after block (if it didn't return/break)
                if (_currentBlock.Successors.Count == 0)
                    _currentBlock.AddSuccessor(afterBlock);
            }

            // If no default case, the match condition block might also fall through
            // (though well-formed match should be exhaustive)
            var hasDefault = matchStmt.Cases.Any(c => c.IsDefault);
            if (!hasDefault && matchConditionBlock.Successors.All(s => s != afterBlock))
            {
                matchConditionBlock.AddSuccessor(afterBlock);
            }

            _currentBlock = afterBlock;
        }

        private void ProcessForeachStatement(BoundForeachStatement forEach)
        {
            // Foreach: condition (has next?) -> body -> condition
            var conditionBlock = CreateBlock();
            var bodyBlock = CreateBlock();
            var afterBlock = CreateBlock();

            _loopConditionStack.Push(conditionBlock);
            _loopExitStack.Push(afterBlock);

            _currentBlock.AddSuccessor(conditionBlock);

            conditionBlock.AddSuccessor(bodyBlock);
            conditionBlock.AddSuccessor(afterBlock);

            _currentBlock = bodyBlock;
            foreach (var s in forEach.Body)
                ProcessStatement(s);

            if (_currentBlock.Successors.Count == 0)
                _currentBlock.AddSuccessor(conditionBlock);

            _loopConditionStack.Pop();
            _loopExitStack.Pop();

            _currentBlock = afterBlock;
        }

        private void ProcessDoWhileStatement(BoundDoWhileStatement doWhile)
        {
            // Do-while: body -> condition -> body (back edge) or after
            var bodyBlock = CreateBlock();
            var conditionBlock = CreateBlock();
            var afterBlock = CreateBlock();

            _loopConditionStack.Push(conditionBlock);
            _loopExitStack.Push(afterBlock);

            _currentBlock.AddSuccessor(bodyBlock);

            _currentBlock = bodyBlock;
            foreach (var s in doWhile.Body)
                ProcessStatement(s);

            if (_currentBlock.Successors.Count == 0)
                _currentBlock.AddSuccessor(conditionBlock);

            conditionBlock.BranchCondition = doWhile.Condition;
            conditionBlock.AddSuccessor(bodyBlock);  // loop back
            conditionBlock.AddSuccessor(afterBlock);  // exit

            _loopConditionStack.Pop();
            _loopExitStack.Pop();

            _currentBlock = afterBlock;
        }

        private void ProcessUsingStatement(BoundUsingStatement usingStmt)
        {
            // Model as try/finally: body can throw, dispose runs on both normal and exception paths
            _currentBlock.Statements.Add(usingStmt);

            var bodyBlock = CreateBlock();
            var disposeBlock = CreateBlock(); // implicit finally (Dispose)
            var afterBlock = CreateBlock();

            // Exception landing for throws inside the using body
            var exceptionLanding = CreateBlock();
            _exceptionTargetStack.Push(exceptionLanding);

            _currentBlock.AddSuccessor(bodyBlock);
            _currentBlock = bodyBlock;

            foreach (var s in usingStmt.Body)
                ProcessStatement(s);

            _exceptionTargetStack.Pop();

            // Normal path: body → dispose → after
            if (_currentBlock.Successors.Count == 0)
                _currentBlock.AddSuccessor(disposeBlock);

            // Exception path: throw → exception landing → dispose → exit
            exceptionLanding.AddSuccessor(disposeBlock);

            _currentBlock = disposeBlock;
            // Dispose block flows to after (normal) or exit (exception re-throw)
            disposeBlock.AddSuccessor(afterBlock);
            disposeBlock.AddSuccessor(_exitBlock);

            _currentBlock = afterBlock;
        }
    }

    /// <summary>
    /// Generates a DOT representation for visualization.
    /// </summary>
    public string ToDot()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("digraph CFG {");
        sb.AppendLine("  node [shape=box];");

        foreach (var block in Blocks)
        {
            var label = $"BB{block.Id}";
            if (block.IsEntry) label += " (entry)";
            if (block.IsExit) label += " (exit)";
            if (block.Statements.Count > 0)
            {
                label += $"\\n{block.Statements.Count} stmts";
            }

            sb.AppendLine($"  BB{block.Id} [label=\"{label}\"];");

            foreach (var succ in block.Successors)
            {
                var edgeLabel = block.BranchCondition != null ? "cond" : "";
                sb.AppendLine($"  BB{block.Id} -> BB{succ.Id} [label=\"{edgeLabel}\"];");
            }
        }

        sb.AppendLine("}");
        return sb.ToString();
    }
}
