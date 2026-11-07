using System.Reflection;
using System.Text;
using Calor.Compiler;
using Calor.Compiler.Analysis;
using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification.Z3;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Z3;

try
{
    if (args.Length != 2)
        throw new InvalidOperationException("Expected function name and Calor source path.");
    Verify(args[0], args[1]);
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine($"Verification failed: {error}");
    return 1;
}

static void Verify(string name, string path)
{
    var names = name switch
    {
        "Factorial" => new[] { "n" },
        "Clamp" => new[] { "value", "min", "max" },
        "IsPositive" => new[] { "x" },
        "Max" => new[] { "a", "b" },
        "Classify" => new[] { "x" },
        "PrintNumbers" or "Countdown" or "DoWhileDemo" => Array.Empty<string>(),
        _ => throw new InvalidOperationException($"Unknown task: {name}")
    };
    var controlFlow = name is "PrintNumbers" or "Countdown" or "DoWhileDemo" or "Classify";
    var outputType = names.Length == 0 ? "void" : name == "IsPositive" ? "bool" : "i32";
    var source = File.ReadAllText(path);
    var compilation = Calor.Compiler.Program.Compile(
        source, path, new Calor.Compiler.CompilationOptions { ContractMode = ContractMode.Debug });
    if (compilation.HasErrors || compilation.Ast == null)
        throw new InvalidOperationException($"Calor compilation failed:\n{string.Join("\n", compilation.Diagnostics)}");
    var functions = compilation.Ast.Functions.Where(function => function.Name == name).ToArray();
    if (functions.Length != 1)
        throw new InvalidOperationException($"Expected exactly one module function named {name}.");
    var function = functions[0];
    if (function.Visibility != Visibility.Public || function.IsAsync || function.TypeParameters.Count != 0
        || function.Output == null
        || AttributeHelper.ExpandType(function.Output.TypeName) != AttributeHelper.ExpandType(outputType)
        || !function.Parameters.Select(parameter => parameter.Name).SequenceEqual(names)
        || function.Parameters.Any(parameter => AttributeHelper.ExpandType(parameter.TypeName) != "INT"
            || parameter.Modifier != Calor.Compiler.Ast.ParameterModifier.None || parameter.InlineRefinement != null))
        throw new InvalidOperationException($"Wrong public signature for {name}.");

    StatementNode[] witnesses = [];
    if (controlFlow)
    {
        // Inspect the parsed declaration before any compilation pass can synthesize effects.
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, diagnostics).TokenizeAllForParser();
        var parsed = new Parser(tokens, diagnostics).Parse();
        if (diagnostics.HasErrors)
            throw new InvalidOperationException($"Calor parsing failed:\n{string.Join("\n", diagnostics)}");
        witnesses = CheckControlFlow(parsed.Functions.Single(candidate => candidate.Name == name));
    }
    else
        CheckContracts(function, names, outputType);

    var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
    var trees = new[]
    {
        CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble, parseOptions),
        CSharpSyntaxTree.ParseText(compilation.GeneratedCode, parseOptions)
    };
    var runtime = CSharpCompilation.Create(
        "LogicTaskRuntime", trees, GeneratedCSharpCompiler.References,
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    using var stream = new MemoryStream();
    var emitted = runtime.Emit(stream);
    if (!emitted.Success)
        throw new InvalidOperationException($"Generated C# compilation failed:\n{string.Join("\n", emitted.Diagnostics)}");
    var assembly = Assembly.Load(stream.ToArray());
    var moduleName = compilation.Ast.Name;
    var type = assembly.GetType($"{moduleName}.{moduleName}Module")
        ?? throw new InvalidOperationException("Generated module type not found.");
    var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.Static, names.Select(_ => typeof(int)).ToArray())
        ?? throw new InvalidOperationException("Generated public signature not found.");
    var returnType = outputType == "void" ? typeof(void) : outputType == "bool" ? typeof(bool) : typeof(int);
    if (!type.IsPublic || !method.IsPublic || !method.IsStatic || method.IsGenericMethod
        || method.ReturnType != returnType
        || !method.GetParameters().Select(parameter => parameter.ParameterType)
            .SequenceEqual(names.Select(_ => typeof(int))))
        throw new InvalidOperationException("Wrong generated public/static scalar signature.");
    if (outputType == "void")
    {
        var previous = Console.Out;
        using var output = new BoundedOutput();
        try
        {
            Console.SetOut(output);
            method.Invoke(null, null);
        }
        finally
        {
            Console.SetOut(previous);
        }
        var expected = name switch
        {
            "PrintNumbers" => "1\n2\n3\n4\n5\n",
            "Countdown" => "5\n4\n3\n2\n1\n",
            "DoWhileDemo" => "0\n1\n2\n",
            _ => throw new InvalidOperationException("Unknown stdout oracle.")
        };
        var actual = output.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
        if (actual != expected)
            throw new InvalidOperationException($"Wrong stdout for {name}: {System.Text.Json.JsonSerializer.Serialize(actual)}; "
                + $"expected {System.Text.Json.JsonSerializer.Serialize(expected)}.");
        CheckControlFlowCoupling(compilation.Ast, trees[1], function, witnesses, path, expected);
        Console.WriteLine($"Verification passed: {name}; native control flow and exact stdout.");
        return;
    }
    var count = 0;
    foreach (var (inputs, expected) in Cases(name))
    {
        var actual = method.Invoke(null, inputs.Select(value => (object)value).ToArray());
        if (!Equals(actual, expected))
            throw new InvalidOperationException($"{name}({string.Join(", ", inputs)}) returned {actual}; expected {expected}.");
        count++;
    }
    if (count == 0)
        throw new InvalidOperationException("No runtime cases executed.");
    if (controlFlow)
        CheckControlFlowCoupling(compilation.Ast, trees[1], function, witnesses, path, null);
    Console.WriteLine($"Verification passed: {name}; {(controlFlow ? "native control flow" : "semantic contracts")} and {count} runtime cases.");
}

static StatementNode[] CheckControlFlow(FunctionNode function)
{
    var statements = RecursiveAstWalker.EnumerateStatements(function.Body).ToArray();
    var witnesses = statements.Where(statement => function.Name switch
    {
        "PrintNumbers" => statement is ForStatementNode,
        "Countdown" => statement is WhileStatementNode,
        "DoWhileDemo" => statement is DoWhileStatementNode,
        "Classify" => statement is IfStatementNode conditional
            && conditional.ElseIfClauses.Count > 0 && conditional.ElseBody != null,
        _ => false
    }).ToArray();
    if (witnesses.Length == 0)
        throw new InvalidOperationException($"Required native control flow missing from {function.Name}.");
    if (function.Name != "Classify"
        && !(function.Effects?.Effects.TryGetValue("io", out var effects) == true
            && effects.Split(',').Contains("console_write", StringComparer.Ordinal)))
        throw new InvalidOperationException($"Declared console-write effect missing from {function.Name}.");
    return witnesses;
}

static void CheckControlFlowCoupling(
    ModuleNode module, SyntaxTree original, FunctionNode function, StatementNode[] witnesses, string path, string? expectedOutput)
{
    var options = new CSharpParseOptions(LanguageVersion.Preview);
    var mapped = CSharpSyntaxTree.ParseText(
        new CSharpEmitter(ContractMode.Debug) { LineDirectiveFilePath = path }.Emit(module, path), options);
    if (!SyntaxFactory.AreEquivalent(original.GetRoot(), mapped.GetRoot()))
        throw new InvalidOperationException("Cannot establish control-flow coupling: source-mapped emission differs from compiled C#.");
    var root = mapped.GetCompilationUnitRoot();
    var method = root.DescendantNodes().OfType<ClassDeclarationSyntax>()
        .Single(type => type.Identifier.ValueText == module.Name + "Module")
        .Members.OfType<MethodDeclarationSyntax>().Single(candidate => candidate.Identifier.ValueText == function.Name);
    var marker = new SyntaxAnnotation("control-flow-witness");
    var matched = witnesses.Select(witness =>
    {
        var candidates = method.DescendantNodes().OfType<StatementSyntax>().Where(statement =>
            (witness is ForStatementNode && statement is BlockSyntax or ForStatementSyntax
                || witness is WhileStatementNode && statement is WhileStatementSyntax
                || witness is DoWhileStatementNode && statement is DoStatementSyntax
                || witness is IfStatementNode && statement is IfStatementSyntax)
            && statement.GetFirstToken().LeadingTrivia.Any(trivia =>
                trivia.GetStructure() is LineDirectiveTriviaSyntax directive && directive.IsActive
                && directive.File.ValueText == path
                && directive.Line.ValueText == witness.Span.Line.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            .ToArray();
        if (candidates.Length != 1)
            throw new InvalidOperationException($"Cannot establish control-flow coupling: expected one emitted witness at line {witness.Span.Line}, found {candidates.Length}.");
        return candidates[0];
    }).ToArray();
    if (matched.Distinct().Count() != witnesses.Length)
        throw new InvalidOperationException("Cannot establish control-flow coupling: ambiguous emitted witnesses.");
    // Trace loop bodies, not headers: printing while evaluating a bound is not
    // output produced by an iteration. Both selected emitters lower native for
    // into a mapped block with one direct while; no generated names are matched.
    var scopes = matched.Select(statement => statement switch
    {
        ForStatementSyntax loop => loop.Statement,
        BlockSyntax block when function.Name == "PrintNumbers" =>
            block.Statements.OfType<WhileStatementSyntax>().Single().Statement,
        WhileStatementSyntax loop => loop.Statement,
        DoStatementSyntax loop => loop.Statement,
        IfStatementSyntax conditional => (StatementSyntax)conditional,
        _ => throw new InvalidOperationException("Cannot establish control-flow coupling: unsupported emitted witness shape.")
    }).ToArray();
    var tracedRoot = root.ReplaceNodes(scopes, (_, rewritten) => rewritten.WithAdditionalAnnotations(marker));
    var probe = "__CalorWitness_" + Guid.NewGuid().ToString("N");
    var traced = new WitnessRewriter(marker, probe, neutralize: false).Visit(tracedRoot) as CompilationUnitSyntax
        ?? throw new InvalidOperationException("Cannot instrument control-flow witnesses.");
    traced = traced.AddMembers(SyntaxFactory.ParseMemberDeclaration(
        $"internal static class {probe} {{ public static int Depth; public static int Visits; "
        + "public static void Enter() { Depth++; Visits++; } public static void Exit() { Depth--; } }")
        ?? throw new InvalidOperationException("Cannot declare witness instrumentation."));

    Assembly Build(CompilationUnitSyntax unit)
    {
        var runtime = CSharpCompilation.Create(
            "ControlFlowCoupling_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble, options),
                CSharpSyntaxTree.Create(unit, options)],
            GeneratedCSharpCompiler.References, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emitted = runtime.Emit(stream);
        if (!emitted.Success)
            throw new InvalidOperationException($"Cannot establish control-flow coupling: instrumented C# compilation failed:\n{string.Join("\n", emitted.Diagnostics)}");
        return Assembly.Load(stream.ToArray());
    }
    MethodInfo Target(Assembly assembly) => assembly.GetType($"{module.Name}.{module.Name}Module")
        ?.GetMethod(function.Name, BindingFlags.Public | BindingFlags.Static)
        ?? throw new InvalidOperationException("Coupling runtime signature missing.");

    var tracedAssembly = Build(traced);
    var target = Target(tracedAssembly);
    var probeType = tracedAssembly.GetType(probe) ?? throw new InvalidOperationException("Witness probe missing.");
    var depth = probeType.GetField("Depth") ?? throw new InvalidOperationException("Witness depth missing.");
    var visits = probeType.GetField("Visits") ?? throw new InvalidOperationException("Witness visits missing.");
    if (expectedOutput != null)
    {
        var previous = Console.Out;
        using var output = new BoundedOutput(() => depth.GetValue(null) is int value && value > 0);
        try
        {
            Console.SetOut(output);
            target.Invoke(null, null);
        }
        finally
        {
            Console.SetOut(previous);
        }
        var actual = output.ToString();
        if (actual.Replace("\r\n", "\n", StringComparison.Ordinal) != expectedOutput
            || output.WitnessCharacters != actual.Length || actual.Length == 0)
            throw new InvalidOperationException($"Control-flow coupling failed: {function.Name} stdout must be produced within a required native loop.");
        return;
    }
    foreach (var (inputs, expected) in Cases(function.Name))
    {
        visits.SetValue(null, 0);
        var actual = target.Invoke(null, inputs.Select(value => (object)value).ToArray());
        if (!Equals(actual, expected) || Equals(visits.GetValue(null), 0))
            throw new InvalidOperationException($"Control-flow coupling failed: {function.Name} must execute a required conditional for input {inputs[0]}.");
    }
    // A counterfactual removes only AST-mapped required conditionals. A sentinel
    // return keeps direct-return implementations compilable without treating
    // arbitrary compiler failures as evidence that the construct was necessary.
    var neutralizedRoot = root.ReplaceNodes(matched, (_, rewritten) => rewritten.WithAdditionalAnnotations(marker));
    var neutralized = new WitnessRewriter(marker, probe, neutralize: true).Visit(neutralizedRoot) as CompilationUnitSyntax
        ?? throw new InvalidOperationException("Cannot neutralize control-flow witnesses.");
    var neutralizedMethod = neutralized.DescendantNodes().OfType<MethodDeclarationSyntax>()
        .Single(candidate => candidate.Identifier.ValueText == function.Name
            && candidate.Parent is ClassDeclarationSyntax type && type.Identifier.ValueText == module.Name + "Module");
    var body = neutralizedMethod.Body ?? throw new InvalidOperationException("Coupling method body missing.");
    neutralized = neutralized.ReplaceNode(neutralizedMethod,
        neutralizedMethod.WithBody(body.AddStatements(SyntaxFactory.ParseStatement("return int.MinValue;"))));
    var counterfactual = Target(Build(neutralized));
    if (Cases(function.Name).All(test => Equals(
            counterfactual.Invoke(null, test.Inputs.Select(value => (object)value).ToArray()), test.Expected)))
        throw new InvalidOperationException("Control-flow coupling failed: required conditionals do not affect any sampled classification result.");
}

static void CheckContracts(FunctionNode function, string[] names, string outputType)
{
    using var context = new Context();
    var translator = new ContractTranslator(context);
    foreach (var name in names)
        if (!translator.DeclareVariable(name, "i32"))
            throw new InvalidOperationException($"Cannot model parameter {name}.");
    if (!translator.DeclareVariable("result", outputType))
        throw new InvalidOperationException("Cannot model result.");

    BoolExpr Translate(ExpressionNode expression)
    {
        CheckModeledExpression(expression);
        var result = translator.TranslateBoolExpr(expression);
        if (result == null || translator.Warnings.Count != 0)
            throw new InvalidOperationException($"Contract translation failed: {string.Join("; ", translator.Warnings)}");
        return result;
    }
    BitVecExpr Integer(string name) => context.MkBVConst(name, 32);
    BitVecNum Constant(int value) => context.MkBV(value, 32);
    var resultValue = Integer("result");
    var actualPre = context.MkAnd(function.Preconditions.Select(contract => Translate(contract.Condition)).ToArray());
    var actualPost = context.MkAnd(function.Postconditions.Select(contract => Translate(contract.Condition)).ToArray());
    var requiredPre = context.MkTrue();
    BoolExpr requiredPost;
    Expr expected;
    switch (function.Name)
    {
        case "Factorial":
            var n = Integer("n");
            requiredPre = context.MkAnd(context.MkBVSGE(n, Constant(0)), context.MkBVSLE(n, Constant(5)));
            requiredPost = context.MkBVSGE(resultValue, Constant(1));
            expected = Constant(120);
            var values = new[] { 1, 1, 2, 6, 24 };
            for (var index = 4; index >= 0; index--)
                expected = context.MkITE(context.MkEq(n, Constant(index)), Constant(values[index]), expected);
            break;
        case "Clamp":
            var value = Integer("value");
            var min = Integer("min");
            var max = Integer("max");
            requiredPre = context.MkBVSLE(min, max);
            requiredPost = context.MkAnd(context.MkBVSGE(resultValue, min), context.MkBVSLE(resultValue, max));
            expected = context.MkITE(context.MkBVSLT(value, min), min,
                context.MkITE(context.MkBVSGT(value, max), max, value));
            break;
        case "IsPositive":
            expected = context.MkBVSGT(Integer("x"), Constant(0));
            requiredPost = context.MkEq(context.MkBoolConst("result"), expected);
            break;
        case "Max":
            var a = Integer("a");
            var b = Integer("b");
            requiredPost = context.MkAnd(context.MkBVSGE(resultValue, a), context.MkBVSGE(resultValue, b),
                context.MkOr(context.MkEq(resultValue, a), context.MkEq(resultValue, b)));
            expected = context.MkITE(context.MkBVSGE(a, b), a, b);
            break;
        default:
            throw new InvalidOperationException("Unknown contract oracle.");
    }

    void RequireUnsatisfiable(BoolExpr counterexample, string message)
    {
        using var solver = context.MkSolver();
        using var settings = context.MkParams();
        settings.Add("timeout", 5000u);
        solver.Parameters = settings;
        solver.Assert(counterexample);
        var status = solver.Check();
        if (status != Status.UNSATISFIABLE)
            throw new InvalidOperationException($"{message}: {status}; {(status == Status.SATISFIABLE ? solver.Model.ToString() : solver.ReasonUnknown)}");
    }
    // Equivalent entry domains forbid vacuous or strengthened preconditions.
    RequireUnsatisfiable(context.MkNot(context.MkEq(actualPre, requiredPre)), "Missing/wrong preconditions");
    RequireUnsatisfiable(context.MkAnd(requiredPre, actualPost, context.MkNot(requiredPost)),
        "Missing/weakened postconditions");
    var result = outputType == "bool" ? (Expr)context.MkBoolConst("result") : resultValue;
    RequireUnsatisfiable(context.MkAnd(requiredPre, context.MkEq(result, expected), context.MkNot(actualPost)),
        "Postconditions exclude a required output");
}

static void CheckModeledExpression(ExpressionNode expression)
{
    // Restrict proof input to total, side-effect-free operators. Calls and unchecked
    // arithmetic must not turn an approximation into contract evidence.
    switch (expression)
    {
        case IntLiteralNode or BoolLiteralNode or ReferenceNode:
            return;
        case BinaryOperationNode binary when binary.Operator is BinaryOperator.Equal or BinaryOperator.NotEqual
            or BinaryOperator.LessThan or BinaryOperator.LessOrEqual or BinaryOperator.GreaterThan
            or BinaryOperator.GreaterOrEqual or BinaryOperator.And or BinaryOperator.Or:
            CheckModeledExpression(binary.Left);
            CheckModeledExpression(binary.Right);
            return;
        case UnaryOperationNode unary when unary.Operator == UnaryOperator.Not:
            CheckModeledExpression(unary.Operand);
            return;
        case ConditionalExpressionNode conditional:
            CheckModeledExpression(conditional.Condition);
            CheckModeledExpression(conditional.WhenTrue);
            CheckModeledExpression(conditional.WhenFalse);
            return;
        case ImplicationExpressionNode implication:
            CheckModeledExpression(implication.Antecedent);
            CheckModeledExpression(implication.Consequent);
            return;
        default:
            throw new InvalidOperationException($"Contract proof does not support {expression.GetType().Name}.");
    }
}

static IEnumerable<(int[] Inputs, object Expected)> Cases(string name)
{
    if (name == "Factorial")
    {
        var values = new[] { 1, 1, 2, 6, 24, 120 };
        for (var n = 0; n <= 5; n++)
            yield return ([n], values[n]);
        yield break;
    }
    var valuesToCheck = new[] { int.MinValue, int.MinValue + 1, -100, -2, -1, 0, 1, 2, 100, int.MaxValue - 1, int.MaxValue };
    foreach (var first in valuesToCheck)
    {
        if (name == "IsPositive")
        {
            yield return ([first], first > 0);
            continue;
        }
        if (name == "Classify")
        {
            yield return ([first], Math.Sign(first));
            continue;
        }
        foreach (var second in valuesToCheck)
        {
            if (name == "Max")
            {
                yield return ([first, second], Math.Max(first, second));
                continue;
            }
            if (first > second)
                continue;
            foreach (var value in valuesToCheck)
                yield return ([value, first, second], Math.Clamp(value, first, second));
        }
    }
}

sealed class BoundedOutput(Func<bool>? isWitnessActive = null) : TextWriter
{
    private readonly StringBuilder output = new();
    public int WitnessCharacters { get; private set; }
    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value)
    {
        if (output.Length >= 4096)
            throw new InvalidOperationException("Excessive stdout: control-flow output exceeds 4096 characters.");
        output.Append(value);
        if (isWitnessActive?.Invoke() == true)
            WitnessCharacters++;
    }

    public override string ToString() => output.ToString();
}

sealed class WitnessRewriter(SyntaxAnnotation marker, string probe, bool neutralize) : CSharpSyntaxRewriter
{
    public override SyntaxNode? Visit(SyntaxNode? node)
    {
        if (node is StatementSyntax statement && node.HasAnnotation(marker))
        {
            if (neutralize)
                return SyntaxFactory.EmptyStatement().WithTriviaFrom(statement);
            var rewritten = base.Visit(node) as StatementSyntax
                ?? throw new InvalidOperationException("Cannot rewrite witness statement.");
            return SyntaxFactory.Block(
                SyntaxFactory.ParseStatement($"global::{probe}.Enter();"),
                SyntaxFactory.TryStatement(SyntaxFactory.Block(rewritten), [],
                    SyntaxFactory.FinallyClause(SyntaxFactory.Block(
                        SyntaxFactory.ParseStatement($"global::{probe}.Exit();")))))
                .WithTriviaFrom(statement);
        }
        return base.Visit(node);
    }
}
