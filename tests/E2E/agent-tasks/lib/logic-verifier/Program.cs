using System.Reflection;
using Calor.Compiler;
using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification.Z3;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Z3;

try
{
    if (args.Length != 2)
        throw new InvalidOperationException("Expected function name and Calculator.calr path.");
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
        _ => throw new InvalidOperationException($"Unknown logic task: {name}")
    };
    var outputType = name == "IsPositive" ? "bool" : "i32";
    var compilation = Calor.Compiler.Program.Compile(
        File.ReadAllText(path), path, new Calor.Compiler.CompilationOptions { ContractMode = ContractMode.Debug });
    if (compilation.HasErrors || compilation.Ast == null)
        throw new InvalidOperationException($"Calor compilation failed:\n{string.Join("\n", compilation.Diagnostics)}");
    var functions = compilation.Ast.Functions.Where(function => function.Name == name).ToArray();
    if (functions.Length != 1)
        throw new InvalidOperationException($"Expected exactly one module function named {name}.");
    var function = functions[0];
    if (function.Visibility != Visibility.Public || function.IsAsync || function.TypeParameters.Count != 0
        || function.Output == null
        || AttributeHelper.ExpandType(function.Output.TypeName) != (outputType == "bool" ? "BOOL" : "INT")
        || !function.Parameters.Select(parameter => parameter.Name).SequenceEqual(names)
        || function.Parameters.Any(parameter => AttributeHelper.ExpandType(parameter.TypeName) != "INT"
            || parameter.Modifier != Calor.Compiler.Ast.ParameterModifier.None || parameter.InlineRefinement != null))
        throw new InvalidOperationException($"Wrong public signature for {name}.");

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
    if (method.ReturnType != (outputType == "bool" ? typeof(bool) : typeof(int)))
        throw new InvalidOperationException("Wrong generated return type.");
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
    Console.WriteLine($"Verification passed: {name}; semantic contracts and {count} runtime cases.");
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
