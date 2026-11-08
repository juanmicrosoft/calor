using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
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
    if (args is ["--check-native"])
    {
        using var context = new Context();
        using var solver = context.MkSolver();
        solver.Assert(context.MkTrue());
        if (solver.Check() != Status.SATISFIABLE)
            throw new InvalidOperationException("Selected native Z3 prerequisite did not solve the control.");
        Console.WriteLine("Selected native Z3 prerequisite passed: SATISFIABLE.");
        return 0;
    }
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

static void Verify(string profile, string path)
{
    // Every profile validates the selected native prerequisite, even when it
    // needs no contract proof. A corrupt/mismatched asset must not go unused.
    using var nativePrerequisite = new Context();
    if (profile is "gen001" or "gen002" or "gen003")
    {
        var genericSource = File.ReadAllText(path);
        var genericCompilation = Calor.Compiler.Program.Compile(
            genericSource, path, new Calor.Compiler.CompilationOptions { ContractMode = ContractMode.Debug });
        if (genericCompilation.HasErrors || genericCompilation.Ast == null)
            throw new InvalidOperationException($"Calor compilation failed:\n{string.Join("\n", genericCompilation.Diagnostics)}");
        VerifyGeneric(profile, genericCompilation.Ast, genericCompilation.GeneratedCode);
        return;
    }
    if (profile is "str001" or "str002" or "str003" or "str004" or "str005")
    {
        VerifyStrings(profile, path);
        return;
    }
    if (profile is "enum001" or "enum002" or "enum003")
    {
        var enumSource = File.ReadAllText(path);
        var enumCompilation = Calor.Compiler.Program.Compile(
            enumSource, path, new Calor.Compiler.CompilationOptions { ContractMode = ContractMode.Debug });
        if (enumCompilation.HasErrors || enumCompilation.Ast == null)
            throw new InvalidOperationException($"Calor compilation failed:\n{string.Join("\n", enumCompilation.Diagnostics)}");
        VerifyEnum(profile, enumCompilation.Ast, enumCompilation.GeneratedCode);
        return;
    }
    var basic = profile is "basic001" or "basic002" or "basic003" or "basic004";
    var contractWriting = profile is "contract001" or "contract002" or "contract003" or "contract004" or "contract005";
    var idiom = profile is "idiom001" or "idiom002";
    var name = profile switch
    {
        "idiom001" => "Clamp",
        "idiom002" => "IsEven",
        "contract001" => "SquareRoot",
        "contract002" or "contract003" => "Abs",
        "contract004" => "SafeDivide",
        "contract005" => "SumToN",
        "basic001" => "GetZero",
        "basic002" => "Multiply",
        "basic003" => "IsPositive",
        "basic004" => "PrintNumber",
        "ControlMax" => "Max",
        _ => profile
    };
    var names = name switch
    {
        "GetZero" when basic => Array.Empty<string>(),
        "Multiply" when basic => new[] { "a", "b" },
        "PrintNumber" when basic => new[] { "x" },
        "SquareRoot" or "Abs" when contractWriting => new[] { "x" },
        "SafeDivide" when contractWriting => new[] { "a", "b" },
        "SumToN" when contractWriting => new[] { "n" },
        "Clamp" when idiom => new[] { "min", "max", "value" },
        "IsEven" when idiom => new[] { "n" },
        "Factorial" => new[] { "n" },
        "Clamp" => new[] { "value", "min", "max" },
        "IsPositive" => new[] { "x" },
        "Max" => new[] { "a", "b" },
        "Classify" => new[] { "x" },
        "GradeToLetter" => new[] { "grade" },
        "PrintDictionary" => new[] { "dictionary" },
        "PrintNumbers" or "Countdown" or "DoWhileDemo" or "PrintEvenNumbers" => Array.Empty<string>(),
        _ => throw new InvalidOperationException($"Unknown task: {name}")
    };
    var controlFlow = name is "PrintNumbers" or "Countdown" or "DoWhileDemo" or "Classify"
        or "GradeToLetter" or "PrintEvenNumbers" or "PrintDictionary";
    var dictionary = name == "PrintDictionary";
    var outputType = name == "GetZero" ? "i32"
        : name == "PrintNumber" || names.Length == 0 || dictionary ? "void" : name is "IsPositive" or "IsEven" ? "bool" : "i32";
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
        || (!dictionary && !function.Parameters.Select(parameter => parameter.Name).SequenceEqual(names))
        || function.Parameters.Count != names.Length
        || function.Parameters.Any(parameter => (dictionary
                ? !IsDictionaryType(parameter.TypeName) : AttributeHelper.ExpandType(parameter.TypeName) != "INT")
            || parameter.Modifier != Calor.Compiler.Ast.ParameterModifier.None
            || !basic && !contractWriting && !idiom && name != "GradeToLetter" && parameter.InlineRefinement != null))
        throw new InvalidOperationException($"Wrong public signature for {name}.");

    StatementNode[] witnesses = [];
    if (controlFlow || basic && name == "PrintNumber")
    {
        // Inspect the parsed declaration before any compilation pass can synthesize effects.
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, diagnostics).TokenizeAllForParser();
        var parsed = new Parser(tokens, diagnostics).Parse();
        if (diagnostics.HasErrors)
            throw new InvalidOperationException($"Calor parsing failed:\n{string.Join("\n", diagnostics)}");
        var declared = parsed.Functions.Single(candidate => candidate.Name == name);
        if (controlFlow)
            witnesses = CheckControlFlow(declared);
        else if (!(declared.Effects?.Effects.TryGetValue("io", out var effects) == true
            && effects.Split(',').Contains("console_write", StringComparer.Ordinal)))
            throw new InvalidOperationException("Declared console-write effect missing from PrintNumber.");
    }
    if (contractWriting || idiom)
        CheckWritingContracts(function, names, outputType);
    else if (basic)
        CheckBasicEntryDomain(function, names);
    else if (!controlFlow || name == "GradeToLetter")
        CheckContracts(function, names, outputType, profile == "ControlMax");

    var (assembly, trees) = CompileRuntime(compilation.GeneratedCode);
    var moduleName = compilation.Ast.Name;
    var type = assembly.GetType($"{moduleName}.{moduleName}Module")
        ?? throw new InvalidOperationException("Generated module type not found.");
    var parameterTypes = names.Select(_ => dictionary ? typeof(Dictionary<string, int>) : typeof(int)).ToArray();
    var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.Static, parameterTypes)
        ?? throw new InvalidOperationException("Generated public signature not found.");
    var returnType = outputType == "void" ? typeof(void) : outputType == "bool" ? typeof(bool) : typeof(int);
    if (!type.IsPublic || !method.IsPublic || !method.IsStatic || method.IsGenericMethod
        || method.ReturnType != returnType
        || !method.GetParameters().Select(parameter => parameter.ParameterType)
            .SequenceEqual(parameterTypes))
        throw new InvalidOperationException("Wrong generated public/static scalar signature.");
    if (contractWriting && name == "SumToN")
        CheckGaussFormula(compilation.Ast, trees[1], function, path);
    if (basic && name == "PrintNumber")
    {
        var outputCaseCount = 0;
        foreach (var (inputs, _) in Cases("IsPositive"))
        {
            var previous = Console.Out;
            using var output = new BoundedOutput();
            try
            {
                Console.SetOut(output);
                method.Invoke(null, inputs.Select(value => (object)value).ToArray());
            }
            finally
            {
                Console.SetOut(previous);
            }
            var expected = inputs[0].ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n";
            var actual = output.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
            if (actual != expected)
                throw new InvalidOperationException($"Wrong stdout for PrintNumber({inputs[0]}): "
                    + $"{System.Text.Json.JsonSerializer.Serialize(actual)}; expected {System.Text.Json.JsonSerializer.Serialize(expected)}.");
            outputCaseCount++;
        }
        Console.WriteLine($"Verification passed: {name}; declared console-write and {outputCaseCount} exact stdout cases.");
        return;
    }
    if (outputType == "void")
    {
        if (dictionary)
        {
            foreach (var input in Dictionaries())
                CheckDictionaryOutput(input, Capture(method, [new Dictionary<string, int>(input)]));
            CheckControlFlowCoupling(compilation.Ast, trees[1], function, witnesses, path, null);
            Console.WriteLine($"Verification passed: {name}; native control flow and 5 dictionary cases.");
            return;
        }
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
            "PrintEvenNumbers" => "2\n4\n6\n8\n10\n",
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
    foreach (var (inputs, expected) in idiom ? IdiomCases(name) : contractWriting ? WritingCases(name) : Cases(name))
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
    if (name == "GradeToLetter")
        foreach (var invalid in new[] { int.MinValue, -1, 101, int.MaxValue })
        {
            try
            {
                method.Invoke(null, [invalid]);
                throw new InvalidOperationException($"GradeToLetter({invalid}) did not fail its domain contract.");
            }
            catch (TargetInvocationException error) when (error.InnerException?.GetType().Name == "ContractViolationException"
                || error.InnerException is ArgumentOutOfRangeException { ParamName: "grade" })
            {
            }
        }
    if (idiom)
    {
        var invalidCases = name == "Clamp"
            ? new[] { new[] { 1, 0, 0 }, [int.MaxValue, int.MinValue, 0], [0, -1, int.MaxValue] }
            : Array.Empty<int[]>();
        foreach (var inputs in invalidCases)
        {
            try
            {
                method.Invoke(null, inputs.Select(value => (object)value).ToArray());
                throw new InvalidOperationException($"{name}({string.Join(", ", inputs)}) did not fail its domain contract.");
            }
            catch (TargetInvocationException error) when (error.InnerException?.GetType().Name == "ContractViolationException"
                || error.InnerException is ArgumentOutOfRangeException argument && names.Contains(argument.ParamName))
            {
            }
        }
        Console.WriteLine($"Verification passed: {name}; idiom predicates and {count} runtime cases; "
            + $"{invalidCases.Length} domain rejection cases. Limits: full signed Int32 prompt domain for predicate proofs; "
            + "independent signed/boundary runtime sampling, not a universal body proof.");
        return;
    }
    if (contractWriting)
    {
        var invalidCases = name switch
        {
            "SquareRoot" => new[] { new[] { int.MinValue }, [-100], [-1] },
            "SafeDivide" => new[] { new[] { 0, 0 }, [1, 0], [int.MinValue, 0], [1, 2], [-7, 3], [7, -3] },
            "SumToN" => new[] { new[] { int.MinValue }, [-100], [-1] },
            _ => Array.Empty<int[]>()
        };
        foreach (var inputs in invalidCases)
        {
            try
            {
                method.Invoke(null, inputs.Select(value => (object)value).ToArray());
                throw new InvalidOperationException($"{name}({string.Join(", ", inputs)}) did not fail its domain contract.");
            }
            catch (TargetInvocationException error) when (error.InnerException?.GetType().Name == "ContractViolationException"
                || error.InnerException is ArgumentOutOfRangeException argument && names.Contains(argument.ParamName))
            {
            }
        }
        var limit = name switch
        {
            "Abs" => "Int32.MinValue has no representable absolute value; unspecified overflow behavior is not assessed",
            "SafeDivide" => "Int32.MinValue / -1 is unrepresentable; overflowing hypothetical postcondition products are outside the proof scope",
            "SumToN" => "all 0..65535 representable Int32 sums are executed; sums above 65535 are unrepresentable and not assessed",
            _ => "full nonnegative Int32 input domain for predicate proofs"
        };
        Console.WriteLine($"Verification passed: {name}; contract-writing predicates and {count} runtime cases; "
            + $"{invalidCases.Length} domain rejection cases. Limits: {limit}; no universal body proof.");
        return;
    }
    Console.WriteLine($"Verification passed: {name}; {(basic ? "basic signature/behavior" : controlFlow ? "native control flow" : "semantic contracts")} and {count} runtime cases.");
}

static void CheckWritingContracts(FunctionNode function, string[] names, string outputType)
{
    using var context = new Context();
    var translator = new ContractTranslator(context);
    foreach (var name in names)
        if (!translator.DeclareVariable(name, "i32"))
            throw new InvalidOperationException($"Cannot model contract-writing variable {name}.");
    if (!translator.DeclareVariable("result", outputType))
        throw new InvalidOperationException("Cannot model contract-writing result.");
    BitVecExpr Integer(string name) => context.MkBVConst(name, 32);
    BitVecNum Constant(int value) => context.MkBV(value, 32);
    BoolExpr Fits(BitVecExpr value) => context.MkAnd(
        context.MkBVSGE(value, context.MkBV(int.MinValue, 64)),
        context.MkBVSLE(value, context.MkBV(int.MaxValue, 64)));
    BitVecExpr Wide(BitVecExpr value) => context.MkSignExt(32, value);
    BitVecExpr Value(ExpressionNode expression) => translator.TranslateBitVecExpr(expression) is { SortSize: 32 } value
        && translator.Warnings.Count == 0 ? value
        : throw new InvalidOperationException("Contract-writing arithmetic translation failed or is not signed Int32.");

    BoolExpr Safe(ExpressionNode expression, bool entry)
    {
        switch (expression)
        {
            case IntLiteralNode literal when !literal.IsLong && !literal.IsUnsigned
                && literal.Value >= int.MinValue && literal.Value <= int.MaxValue:
            case BoolLiteralNode:
                return context.MkTrue();
            case ReferenceNode reference when names.Contains(reference.Name) || !entry && reference.Name == "result":
                return context.MkTrue();
            case SelfRefNode when entry:
                _ = Value(expression);
                return context.MkTrue();
            case BinaryOperationNode binary when binary.Operator is BinaryOperator.Add or BinaryOperator.Subtract
                or BinaryOperator.Multiply or BinaryOperator.Divide or BinaryOperator.Modulo:
                var operands = context.MkAnd(Safe(binary.Left, entry), Safe(binary.Right, entry));
                var left = Value(binary.Left);
                var right = Value(binary.Right);
                var arithmetic = binary.Operator switch
                {
                    BinaryOperator.Add => Fits(context.MkBVAdd(Wide(left), Wide(right))),
                    BinaryOperator.Subtract => Fits(context.MkBVSub(Wide(left), Wide(right))),
                    BinaryOperator.Multiply => Fits(context.MkBVMul(Wide(left), Wide(right))),
                    _ => context.MkAnd(context.MkNot(context.MkEq(right, Constant(0))),
                        context.MkNot(context.MkAnd(context.MkEq(left, Constant(int.MinValue)),
                            context.MkEq(right, Constant(-1)))))
                };
                return context.MkAnd(operands, arithmetic);
            case BinaryOperationNode binary when binary.Operator is BinaryOperator.Equal or BinaryOperator.NotEqual
                or BinaryOperator.LessThan or BinaryOperator.LessOrEqual or BinaryOperator.GreaterThan
                or BinaryOperator.GreaterOrEqual:
                return context.MkAnd(Safe(binary.Left, entry), Safe(binary.Right, entry));
            case BinaryOperationNode binary when binary.Operator is BinaryOperator.And or BinaryOperator.Or:
                var first = Safe(binary.Left, entry);
                var condition = Translate(binary.Left);
                return context.MkAnd(first, context.MkImplies(
                    binary.Operator == BinaryOperator.And ? condition : context.MkNot(condition), Safe(binary.Right, entry)));
            case UnaryOperationNode unary when unary.Operator == UnaryOperator.Not:
                return Safe(unary.Operand, entry);
            case ConditionalExpressionNode conditional:
                return context.MkAnd(Safe(conditional.Condition, entry),
                    (BoolExpr)context.MkITE(Translate(conditional.Condition),
                        Safe(conditional.WhenTrue, entry), Safe(conditional.WhenFalse, entry)));
            case ImplicationExpressionNode implication:
                return context.MkAnd(Safe(implication.Antecedent, entry),
                    context.MkImplies(Translate(implication.Antecedent), Safe(implication.Consequent, entry)));
            default:
                throw new InvalidOperationException($"Contract-writing proof does not support {expression.GetType().Name} or foreign/scoped reference.");
        }
    }
    BoolExpr Translate(ExpressionNode expression) => translator.TranslateBoolExpr(expression) is { } translated
        && translator.Warnings.Count == 0 ? translated
        : throw new InvalidOperationException($"Contract-writing translation failed: {string.Join("; ", translator.Warnings)}");
    var entrySafety = new List<BoolExpr>();
    var actualPre = context.MkAnd(TranslateEntryConditions(function, translator, expression =>
    {
        entrySafety.Add(Safe(expression, entry: true));
        return Translate(expression);
    }));
    var postSafety = function.Postconditions.Select(contract => Safe(contract.Condition, entry: false)).ToArray();
    var actualPost = context.MkAnd(function.Postconditions.Select(contract => Translate(contract.Condition)).ToArray());
    var result = Integer("result");
    var limit = context.MkTrue();
    var postScope = context.MkTrue();
    var requiredPre = context.MkTrue();
    var requiredPost = context.MkTrue();
    BoolExpr mathematicalOutput;
    switch (function.Name)
    {
        case "SquareRoot":
            requiredPre = context.MkBVSGE(Integer("x"), Constant(0));
            mathematicalOutput = context.MkEq(result, Integer("x"));
            break;
        case "Abs":
            var x = Integer("x");
            limit = context.MkNot(context.MkEq(x, Constant(int.MinValue)));
            mathematicalOutput = context.MkEq(result, context.MkITE(context.MkBVSLT(x, Constant(0)), context.MkBVNeg(x), x));
            requiredPost = context.MkBVSGE(result, Constant(0));
            break;
        case "SafeDivide":
            var a = Integer("a");
            var b = Integer("b");
            limit = context.MkNot(context.MkAnd(context.MkEq(a, Constant(int.MinValue)), context.MkEq(b, Constant(-1))));
            requiredPre = context.MkAnd(context.MkNot(context.MkEq(b, Constant(0))),
                context.MkEq(context.MkBVSRem(a, b), Constant(0)));
            var product = context.MkBVMul(Wide(result), Wide(b));
            postScope = Fits(product);
            requiredPost = context.MkEq(product, Wide(a));
            mathematicalOutput = requiredPost;
            break;
        case "SumToN":
            var n = Integer("n");
            limit = context.MkBVSLE(n, Constant(65535));
            requiredPre = context.MkBVSGE(n, Constant(0));
            requiredPost = context.MkBVSGE(result, Constant(0));
            mathematicalOutput = context.MkTrue();
            break;
        case "Clamp":
            var value = Integer("value");
            var min = Integer("min");
            var max = Integer("max");
            requiredPre = context.MkBVSLE(min, max);
            requiredPost = context.MkAnd(context.MkBVSGE(result, min), context.MkBVSLE(result, max));
            mathematicalOutput = context.MkEq(result, context.MkITE(context.MkBVSLT(value, min), min,
                context.MkITE(context.MkBVSGT(value, max), max, value)));
            break;
        case "IsEven":
            requiredPost = context.MkEq(context.MkBoolConst("result"),
                context.MkEq(context.MkBVSRem(Integer("n"), Constant(2)), Constant(0)));
            mathematicalOutput = requiredPost;
            break;
        default:
            throw new InvalidOperationException("Unknown contract-writing oracle.");
    }
    void Prove(BoolExpr counterexample, string message)
    {
        using var solver = context.MkSolver();
        using var settings = context.MkParams();
        settings.Add("timeout", 5000u);
        solver.Parameters = settings;
        solver.Assert(counterexample);
        var status = solver.Check();
        if (status != Status.UNSATISFIABLE)
            throw new InvalidOperationException($"{message}: {status}; "
                + (status == Status.SATISFIABLE ? solver.Model.ToString() : solver.ReasonUnknown));
    }
    var entryLimit = function.Name == "SumToN" ? context.MkTrue() : limit;
    Prove(context.MkAnd(entryLimit, context.MkNot(context.MkEq(actualPre, requiredPre))), "Missing/wrong writing preconditions");
    Prove(context.MkAnd(limit, requiredPre, postScope, actualPost, context.MkNot(context.MkAnd(postSafety))),
        "Contract-writing postcondition evidence has undefined/overflowing arithmetic");
    Prove(context.MkAnd(limit, requiredPre, postScope, actualPost, context.MkNot(requiredPost)),
        "Missing/weakened writing postconditions");
    if (function.Name == "SumToN")
    {
        var variables = new Expr[] { Integer("n"), result };
        var safety = context.MkAnd(entrySafety.Concat(postSafety).ToArray());
        foreach (var (inputs, output) in WritingCases(function.Name))
        {
            var values = new Expr[] { Constant(inputs[0]), Constant((int)output) };
            void RequireTrue(BoolExpr predicate, string message)
            {
                var evaluated = predicate.Substitute(variables, values).Simplify();
                if (!evaluated.IsTrue)
                    throw new InvalidOperationException($"{message} at n={inputs[0]}, result={output}: {evaluated}.");
            }
            // Exhaust the finite representable domain using the selected
            // translator's expressions, not a nonlinear approximation or body result.
            RequireTrue(safety, "Contract-writing arithmetic is undefined or overflows for required inputs/outputs");
            RequireTrue(actualPost, "Writing postconditions exclude a required output");
        }
        return;
    }
    var correct = context.MkAnd(limit, requiredPre, mathematicalOutput);
    Prove(context.MkAnd(correct, context.MkNot(context.MkAnd(entrySafety.Concat(postSafety).ToArray()))),
        "Contract-writing arithmetic is undefined or overflows for required inputs/outputs");
    Prove(context.MkAnd(correct, context.MkNot(actualPost)), "Writing postconditions exclude a required output");
}

static void CheckGaussFormula(ModuleNode module, SyntaxTree original, FunctionNode function, string path)
{
    using var algebra = new Context();
    using var normalization = algebra.MkParams();
    normalization.Add("som", true);
    var n = algebra.MkRealConst("n");
    var gauss = algebra.MkDiv(algebra.MkMul(n, algebra.MkAdd(n, algebra.MkReal(1))), algebra.MkReal(2));
    var bindings = function.Body.OfType<BindStatementNode>().Where(binding => !binding.IsMutable && binding.Initializer != null)
        .GroupBy(binding => binding.Name).Where(group => group.Count() == 1)
        .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);
    bool Same(ArithExpr left, ArithExpr right) => algebra.MkEq(left, right).Simplify(normalization).IsTrue;
    ArithExpr? Polynomial(ExpressionNode expression, HashSet<string> resolving)
    {
        // This is algebraic witness recognition, not a model of integer casts or
        // division rounding. The unchanged emitted body must separately pass
        // every representable input, including widened and parity-factored forms.
        switch (expression)
        {
            case IntLiteralNode literal when !literal.IsUnsigned:
                return algebra.MkReal(literal.Value);
            case ReferenceNode { Name: "n" }:
                return n;
            case ReferenceNode reference when bindings.TryGetValue(reference.Name, out var binding)
                && resolving.Add(reference.Name):
                try
                {
                    return Polynomial(binding.Initializer!, resolving);
                }
                finally
                {
                    resolving.Remove(reference.Name);
                }
            case TypeOperationNode { Operation: TypeOp.Cast } cast
                when AttributeHelper.ExpandType(cast.TargetType) is "INT" or "INT[bits=64][signed=true]":
                return Polynomial(cast.Operand, resolving);
            case UnaryOperationNode { Operator: UnaryOperator.Negate } unary:
                return Polynomial(unary.Operand, resolving) is { } operand ? algebra.MkUnaryMinus(operand) : null;
            case ConditionalExpressionNode conditional:
                var whenTrue = Polynomial(conditional.WhenTrue, resolving);
                var whenFalse = Polynomial(conditional.WhenFalse, resolving);
                return whenTrue != null && whenFalse != null && Same(whenTrue, whenFalse) ? whenTrue : null;
            case BinaryOperationNode binary when binary.Operator is BinaryOperator.Add or BinaryOperator.Subtract
                or BinaryOperator.Multiply or BinaryOperator.Divide:
                var left = Polynomial(binary.Left, resolving);
                var right = Polynomial(binary.Right, resolving);
                if (left == null || right == null)
                    return null;
                return binary.Operator switch
                {
                    BinaryOperator.Add => algebra.MkAdd(left, right),
                    BinaryOperator.Subtract => algebra.MkSub(left, right),
                    BinaryOperator.Multiply => algebra.MkMul(left, right),
                    BinaryOperator.Divide when right.Simplify(normalization) is RatNum denominator
                        && !Same(denominator, algebra.MkReal(0)) => algebra.MkDiv(left, right),
                    _ => null
                };
            default:
                return null;
        }
    }
    bool IsFormula(ExpressionNode expression) => Polynomial(expression, new(StringComparer.Ordinal)) is { } polynomial
        && Same(polynomial, gauss);
    var witnesses = RecursiveAstWalker.EnumerateStatements(function.Body).Where(statement => statement switch
    {
        ReturnStatementNode { Expression: { } expression } => IsFormula(expression),
        BindStatementNode { Initializer: { } expression } => IsFormula(expression),
        _ => false
    }).ToArray();
    if (witnesses.Length == 0)
        throw new InvalidOperationException("Required native Gauss formula missing from SumToN; no loop/invariant is required.");
    var options = new CSharpParseOptions(LanguageVersion.Preview);
    var debugMapped = CSharpSyntaxTree.ParseText(
        new CSharpEmitter(ContractMode.Debug) { LineDirectiveFilePath = path }.Emit(module), options);
    if (!SyntaxFactory.AreEquivalent(original.GetRoot(), debugMapped.GetRoot()))
        throw new InvalidOperationException("Cannot establish Gauss coupling: mapped emission differs from compiled C#.");
    var mapped = CSharpSyntaxTree.ParseText(
        new CSharpEmitter(ContractMode.Off) { LineDirectiveFilePath = path }.Emit(module), options);
    var root = mapped.GetCompilationUnitRoot();
    var method = root.DescendantNodes().OfType<ClassDeclarationSyntax>()
        .Single(type => type.Identifier.ValueText == module.Name + "Module")
        .Members.OfType<MethodDeclarationSyntax>().Single(candidate => candidate.Identifier.ValueText == function.Name);
    MethodInfo BuildProbe(CompilationUnitSyntax unit)
    {
        var runtime = CSharpCompilation.Create("GaussCoupling_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble, options), CSharpSyntaxTree.Create(unit, options)],
            GeneratedCSharpCompiler.References, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emitted = runtime.Emit(stream);
        if (!emitted.Success)
            throw new InvalidOperationException($"Gauss probe compilation failed:\n{string.Join("\n", emitted.Diagnostics)}");
        return Assembly.Load(stream.ToArray()).GetType($"{module.Name}.{module.Name}Module")
            ?.GetMethod(function.Name, BindingFlags.Public | BindingFlags.Static, [typeof(int)])
            ?? throw new InvalidOperationException("Gauss probe signature missing.");
    }
    var baseline = BuildProbe(root);
    foreach (var (inputs, expected) in WritingCases(function.Name))
    {
        var actual = baseline.Invoke(null, inputs.Select(value => (object)value).ToArray());
        if (!Equals(actual, expected))
            throw new InvalidOperationException($"Gauss contract-disabled baseline: SumToN({inputs[0]}) returned {actual}; expected {expected}.");
    }
    var changedOutput = false;
    foreach (var witness in witnesses)
    {
        var matched = method.DescendantNodes().OfType<StatementSyntax>().Where(statement =>
            (witness is ReturnStatementNode && (statement is ReturnStatementSyntax
                    || statement is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax })
                || witness is BindStatementNode && statement is LocalDeclarationStatementSyntax)
            && statement.GetFirstToken().LeadingTrivia.Any(trivia =>
                trivia.GetStructure() is LineDirectiveTriviaSyntax directive && directive.IsActive
                && directive.File.ValueText == path
                && directive.Line.ValueText == witness.Span.Line.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToArray();
        if (matched.Length != 1)
            throw new InvalidOperationException("Cannot establish Gauss coupling: ambiguous emitted formula witness.");
        var statement = matched[0];
        var expression = statement switch
        {
            ReturnStatementSyntax returned => returned.Expression,
            ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assignment } => assignment.Right,
            LocalDeclarationStatementSyntax declaration when declaration.Declaration.Variables.Count == 1
                => declaration.Declaration.Variables[0].Initializer?.Value,
            _ => null
        } ?? throw new InvalidOperationException("Cannot establish Gauss coupling: missing emitted formula expression.");
        var changed = root.ReplaceNode(expression, SyntaxFactory.LiteralExpression(
            SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(0)));
        var target = BuildProbe(changed);
        foreach (var (inputs, expected) in WritingCases(function.Name))
        {
            try
            {
                changedOutput |= !Equals(target.Invoke(null, inputs.Select(value => (object)value).ToArray()), expected);
            }
            catch (TargetInvocationException error)
            {
                throw new InvalidOperationException("Gauss counterfactual runtime failed; exceptions are not output-dependence evidence.", error);
            }
        }
    }
    if (!changedOutput)
        throw new InvalidOperationException("Gauss coupling failed: native formulas do not affect any representable result.");
}

static void VerifyStrings(string profile, string path)
{
    var name = profile switch
    {
        "str001" => "StringLength", "str002" => "StringContains", "str003" => "GetFirstChar",
        "str004" => "IsValidEmail", "str005" => "BuildGreeting",
        _ => throw new InvalidOperationException("Unknown string profile.")
    };
    string[] names = profile switch
    {
        "str002" => ["s", "sub"], "str004" => ["email"], "str005" => ["name"], _ => ["s"]
    };
    var returnType = profile switch
    {
        "str001" => typeof(int), "str003" => typeof(char), "str005" => typeof(string), _ => typeof(bool)
    };
    var source = File.ReadAllText(path);
    var compilation = Calor.Compiler.Program.Compile(source, path,
        new Calor.Compiler.CompilationOptions { ContractMode = ContractMode.Debug });
    if (compilation.HasErrors || compilation.Ast == null)
        throw new InvalidOperationException($"Calor compilation failed:\n{string.Join("\n", compilation.Diagnostics)}");
    var module = compilation.Ast;
    var functions = module.Functions.Where(candidate => candidate.Name == name).ToArray();
    if (functions.Length != 1)
        throw new InvalidOperationException($"Expected exactly one native module function named {name}.");
    var function = functions[0];
    var output = profile switch { "str001" => "i32", "str003" => "char", "str005" => "str", _ => "bool" };
    if (function.Visibility != Visibility.Public || function.IsAsync || function.TypeParameters.Count != 0
        || function.Output == null || AttributeHelper.ExpandType(function.Output.TypeName) != AttributeHelper.ExpandType(output)
        || !function.Parameters.Select(parameter => parameter.Name).SequenceEqual(names)
        || function.Parameters.Any(parameter => AttributeHelper.ExpandType(parameter.TypeName) != AttributeHelper.ExpandType("str")
            || parameter.Modifier != Calor.Compiler.Ast.ParameterModifier.None))
        throw new InvalidOperationException($"Wrong public string signature for {name}.");
    CheckStringDomain(function, names, profile == "str003");
    var (assembly, original) = CompileRuntime(compilation.GeneratedCode);
    MethodInfo Method(Assembly runtime)
    {
        var type = runtime.GetType($"{module.Name}.{module.Name}Module")
            ?? throw new InvalidOperationException("Generated string module type missing.");
        var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.Static, names.Select(_ => typeof(string)).ToArray())
            ?? throw new InvalidOperationException("Generated exact CLR string signature missing.");
        if (!type.IsPublic || method.IsGenericMethod || method.ReturnType != returnType
            || !method.GetParameters().Select(parameter => parameter.Name).SequenceEqual(names))
            throw new InvalidOperationException("Wrong reflected CLR string signature.");
        return method;
    }
    var cases = StringCases(profile).ToArray();
    void Behavior(MethodInfo method)
    {
        foreach (var (inputs, expected) in cases)
            if (!Equals(method.Invoke(null, inputs.Cast<object>().ToArray()), expected))
                throw new InvalidOperationException($"Wrong string behavior for {name}({System.Text.Json.JsonSerializer.Serialize(inputs)}).");
    }
    var target = Method(assembly);
    Behavior(target);
    if (profile == "str003")
    {
        try
        {
            target.Invoke(null, [""]);
            throw new InvalidOperationException("GetFirstChar accepted an empty string.");
        }
        catch (TargetInvocationException error) when (error.InnerException?.GetType().FullName == "Calor.Runtime.ContractViolationException")
        {
            // An index exception is not precondition enforcement.
        }
        catch (TargetInvocationException error) when (function.Parameters[0].InlineRefinement != null
            && error.InnerException is ArgumentOutOfRangeException { ParamName: "s" } refinement
            && refinement.Message.StartsWith("Violation of inline refinement for parameter 's'", StringComparison.Ordinal))
        {
        }
    }
    var options = new CSharpParseOptions(LanguageVersion.Preview);
    var debugMapped = CSharpSyntaxTree.ParseText(
        new CSharpEmitter(ContractMode.Debug) { LineDirectiveFilePath = path }.Emit(module), options);
    if (!SyntaxFactory.AreEquivalent(original[1].GetRoot(), debugMapped.GetRoot()))
        throw new InvalidOperationException("String mapping differs from the independently compiled public emission.");
    var mapped = CSharpSyntaxTree.ParseText(
        new CSharpEmitter(ContractMode.Off) { LineDirectiveFilePath = path }.Emit(module), options);
    var root = mapped.GetCompilationUnitRoot();
    var methodSyntax = root.DescendantNodes().OfType<ClassDeclarationSyntax>()
        .Single(type => type.Identifier.ValueText == module.Name + "Module").Members.OfType<MethodDeclarationSyntax>()
        .Single(method => method.Identifier.ValueText == name);
    var semanticCompilation = CSharpCompilation.Create("StringMapping", [
        CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble, options), mapped],
        GeneratedCSharpCompiler.References, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    var semantic = semanticCompilation.GetSemanticModel(mapped);
    var bindings = RecursiveAstWalker.EnumerateStatements(function.Body).OfType<BindStatementNode>()
        .GroupBy(binding => binding.Name).Where(group => group.Count() == 1)
        .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);
    var returnedData = new HashSet<AstNode>(ReferenceEqualityComparer.Instance);
    void ReturnData(AstNode node)
    {
        if (!returnedData.Add(node))
            return;
        if (node is ReferenceNode reference && bindings.TryGetValue(reference.Name, out var binding)
            && !binding.IsMutable && binding.Initializer != null)
            ReturnData(binding.Initializer);
        if (node is ConditionalExpressionNode conditional)
        {
            ReturnData(conditional.WhenTrue);
            ReturnData(conditional.WhenFalse);
        }
        else if (node is not LambdaExpressionNode)
            foreach (var child in RecursiveAstWalker.GetAllChildren(node))
                ReturnData(child);
    }
    foreach (var returned in RecursiveAstWalker.EnumerateStatements(function.Body).OfType<ReturnStatementNode>())
        if (returned.Expression != null)
            ReturnData(returned.Expression);
    var witnesses = new List<(ExpressionSyntax Expression, string Kind)>();
    foreach (var statement in RecursiveAstWalker.EnumerateStatements(function.Body))
    {
        foreach (var native in StringNativeExpressions(statement).Cast<AstNode>()
            .Concat(statement is CallStatementNode ? [statement] : []))
        {
            if (native is not (StringOperationNode or CharOperationNode or StringBuilderOperationNode
                or FieldAccessNode or ArrayAccessNode or CallExpressionNode or NewExpressionNode or CallStatementNode or ReferenceNode))
                continue;
            var emitted = SyntaxFactory.ParseExpression(native.Accept(new CSharpEmitter(ContractMode.Off)).TrimEnd(';'));
            var matches = methodSyntax.DescendantNodes().OfType<ExpressionSyntax>()
                .Where(expression => SyntaxFactory.AreEquivalent(expression, emitted)
                    && expression.AncestorsAndSelf().OfType<StatementSyntax>()
                        .SelectMany(container => container.GetFirstToken().LeadingTrivia)
                        .Select(trivia => trivia.GetStructure()).OfType<LineDirectiveTriviaSyntax>()
                        .FirstOrDefault(directive => directive.IsActive && directive.File.ValueText == path)
                        ?.Line.ValueText == statement.Span.Line.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .ToArray();
            if (matches.Length == 0)
            {
                if (native is StringOperationNode or CharOperationNode or StringBuilderOperationNode)
                    throw new InvalidOperationException("Cannot map native string expression to selected public emission.");
                continue;
            }
            foreach (var expression in matches)
            {
                var symbol = semantic.GetSymbolInfo(expression).Symbol;
                var kind = symbol switch
                {
                    IPropertySymbol { Name: "Length", ContainingType.SpecialType: SpecialType.System_String } => "length",
                    IPropertySymbol { IsIndexer: true, ContainingType.SpecialType: SpecialType.System_String } => "char",
                    IMethodSymbol { Name: "Contains", ContainingType.SpecialType: SpecialType.System_String } => "contains",
                    IMethodSymbol { Name: "IsMatch" } member when member.ContainingType.ToDisplayString() == "System.Text.RegularExpressions.Regex"
                        => "regex",
                    IMethodSymbol member when member.ContainingType.ToDisplayString() == "System.Text.StringBuilder"
                        => member.MethodKind == MethodKind.Constructor ? "new" : member.Name,
                    _ => null
                };
                if (kind == null)
                    continue;
                if (matches.Length != 1)
                    throw new InvalidOperationException("Ambiguous native string expression mapping.");
                if (kind == "regex" && expression is InvocationExpressionSyntax regex
                    && (regex.ArgumentList.Arguments.Count != 2
                        || !IsAtPattern(regex.ArgumentList.Arguments[1].Expression)))
                    continue;
                if (kind == "char" && expression is ElementAccessExpressionSyntax element
                    && (element.ArgumentList.Arguments.Count != 1
                        || semantic.GetConstantValue(element.ArgumentList.Arguments[0].Expression) is not { HasValue: true, Value: 0 }))
                    continue;
                if (kind is "length" or "contains" or "char" or "regex" or "ToString"
                    && !returnedData.Contains(native))
                    continue;
                var operands = expression switch
                {
                    MemberAccessExpressionSyntax member => new[] { member.Expression },
                    ElementAccessExpressionSyntax access => new[] { access.Expression },
                    InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax member } call
                        => kind == "regex" ? call.ArgumentList.Arguments.Select(argument => argument.Expression).ToArray()
                            : new[] { member.Expression }.Concat(call.ArgumentList.Arguments.Select(argument => argument.Expression)).ToArray(),
                    _ => Array.Empty<ExpressionSyntax>()
                };
                if (kind is "length" or "contains" or "char" or "regex"
                    && (operands.Length == 0 || !IsInput(operands[0], names[0], new(StringComparer.Ordinal))
                        || kind == "contains" && (operands.Length < 2 || !IsInput(operands[1], "sub", new(StringComparer.Ordinal)))))
                    continue;
                if (!witnesses.Any(witness => witness.Expression == expression))
                    witnesses.Add((expression, kind));
            }
        }
    }
    var required = profile switch { "str001" => "length", "str002" => "contains", "str003" => "char", "str004" => "regex", _ => "ToString" };
    var relevant = witnesses.Where(witness => witness.Kind == required).ToArray();
    if (relevant.Length == 0)
        throw new InvalidOperationException($"Required native string operation {required} missing.");
    MethodInfo Build(CompilationUnitSyntax unit) => Method(CompileRuntime(unit.ToFullString()).Assembly);
    Behavior(Build(root));
    bool Changes(ExpressionSyntax expression, ExpressionSyntax replacement)
    {
        var probe = Build(root.ReplaceNode(expression, replacement));
        var changed = false;
        foreach (var (inputs, expected) in cases)
        {
            try
            {
                changed |= !Equals(probe.Invoke(null, inputs.Cast<object>().ToArray()), expected);
            }
            catch (TargetInvocationException error)
            {
                throw new InvalidOperationException("String counterfactual runtime failed; exceptions are not coupling evidence.", error);
            }
        }
        return changed;
    }
    ExpressionSyntax Neutral() => SyntaxFactory.ParseExpression(profile switch
    {
        "str001" => "0", "str003" => "'\\0'", "str005" => "\"\"", _ => "false"
    });
    if (!relevant.Select(witness => Changes(witness.Expression, Neutral())).ToArray().Any(changed => changed))
        throw new InvalidOperationException("String coupling failed: required operations do not contribute to returned results.");
    if (profile == "str005")
    {
        if (!witnesses.Any(witness => witness.Kind == "new") || !witnesses.Any(witness => witness.Kind == "Append"))
            throw new InvalidOperationException("Required native StringBuilder construction/appends missing.");
        var instrumented = root.ReplaceNodes(witnesses.Where(witness => witness.Kind is "new" or "Append" or "ToString")
            .Select(witness => witness.Expression), (before, rewritten) =>
            {
                var kind = witnesses.Single(witness => witness.Expression == before).Kind;
                if (kind == "new")
                    return SyntaxFactory.ParseExpression($"global::StringTaskProbe.New({rewritten})");
                if (rewritten is not InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax member } invocation)
                    throw new InvalidOperationException("Cannot instrument native builder call.");
                if (kind == "Append" && invocation.ArgumentList.Arguments.Count != 1)
                    throw new InvalidOperationException("Unmodeled StringBuilder append overload.");
                return kind == "Append"
                    ? SyntaxFactory.ParseExpression($"global::StringTaskProbe.Append({member.Expression}, {invocation.ArgumentList.Arguments})")
                    : SyntaxFactory.ParseExpression($"global::StringTaskProbe.Text({member.Expression})");
            });
        instrumented = instrumented.AddMembers(SyntaxFactory.ParseMemberDeclaration(StringTaskProbe.Source)
            ?? throw new InvalidOperationException("Cannot parse builder instrumentation."));
        var probeAssembly = CompileRuntime(instrumented.ToFullString()).Assembly;
        var probeMethod = Method(probeAssembly);
        var probeType = probeAssembly.GetType("StringTaskProbe")
            ?? throw new InvalidOperationException("Builder probe type missing.");
        var reset = probeType.GetMethod("Reset", [typeof(int)])
            ?? throw new InvalidOperationException("Builder probe reset signature missing.");
        var check = probeType.GetMethod("Check", [typeof(object), typeof(string)])
            ?? throw new InvalidOperationException("Builder probe check signature missing.");
        foreach (var (inputs, expected) in cases)
        {
            reset.Invoke(null, [-1]);
            var actual = probeMethod.Invoke(null, inputs.Cast<object>().ToArray());
            if (!Equals(actual, expected))
                throw new InvalidOperationException("Builder instrumented baseline changed behavior.");
            check.Invoke(null, [actual, inputs[0]]);
        }
        for (var step = 0; step < 3; step++)
        {
            var changed = false;
            foreach (var (inputs, expected) in cases)
            {
                reset.Invoke(null, [step]);
                try
                {
                    changed |= !Equals(probeMethod.Invoke(null, inputs.Cast<object>().ToArray()), expected);
                }
                catch (TargetInvocationException error)
                {
                    throw new InvalidOperationException("StringBuilder counterfactual runtime failed; exceptions are not coupling evidence.", error);
                }
            }
            if (!changed)
                throw new InvalidOperationException($"Builder append {step + 1} does not contribute to the returned greeting.");
        }
    }
    Console.WriteLine($"Verification passed: {name}; exact native/CLR signatures, {cases.Length} runtime cases and native return coupling.");
    Console.WriteLine("Limits: finite non-null CLR UTF-16 cases, not a universal body proof; contracts use only modeled integer lengths, not Z3 Unicode strings.");

    bool IsAtPattern(ExpressionSyntax expression)
    {
        if (semantic.GetConstantValue(expression) is { HasValue: true, Value: "@" })
            return true;
        if (expression is not IdentifierNameSyntax identifier)
            return false;
        return bindings.TryGetValue(identifier.Identifier.ValueText, out var binding)
            && !binding.IsMutable && binding.Initializer is StringLiteralNode { Value: "@" };
    }
    bool IsInput(ExpressionSyntax expression, string name, HashSet<string> resolving)
    {
        if (expression is ParenthesizedExpressionSyntax grouped)
            return IsInput(grouped.Expression, name, resolving);
        if (expression is not IdentifierNameSyntax identifier)
            return false;
        var value = identifier.Identifier.ValueText;
        if (value == name)
            return semantic.GetSymbolInfo(expression).Symbol is IParameterSymbol;
        bool Alias(string alias)
        {
            if (alias == name)
                return true;
            return resolving.Add(alias) && bindings.TryGetValue(alias, out var binding) && !binding.IsMutable
                && binding.Initializer is ReferenceNode reference && Alias(reference.Name);
        }
        return semantic.GetSymbolInfo(expression).Symbol is ILocalSymbol && Alias(value);
    }
}

static IEnumerable<ExpressionNode> StringNativeExpressions(AstNode node)
{
    foreach (var child in RecursiveAstWalker.GetAllChildren(node))
    {
        if (child is ExpressionNode expression)
            yield return expression;
        if (child is not StatementNode && child is not LambdaExpressionNode)
            foreach (var descendant in StringNativeExpressions(child))
                yield return descendant;
    }
}

static void CheckStringDomain(FunctionNode function, string[] names, bool nonempty)
{
    using var context = new Context();
    var lengths = names.ToDictionary(name => name, name => context.MkIntConst(name + "_clr_length"));
    ArithExpr Length(ExpressionNode expression, string? parameter) => expression switch
    {
        IntLiteralNode { IsUnsigned: false } literal => context.MkInt(literal.Value),
        StringOperationNode { Operation: StringOp.Length, Arguments: [ReferenceNode reference] }
            when lengths.ContainsKey(reference.Name == "#" ? parameter ?? "" : reference.Name)
            => lengths[reference.Name == "#" ? parameter! : reference.Name],
        StringOperationNode { Operation: StringOp.Length, Arguments: [SelfRefNode] }
            when parameter != null => lengths[parameter],
        FieldAccessNode { FieldName: "Length", Target: ReferenceNode reference }
            when lengths.ContainsKey(reference.Name == "#" ? parameter ?? "" : reference.Name)
            => lengths[reference.Name == "#" ? parameter! : reference.Name],
        ReferenceNode reference when names.Any(name => reference.Name == name + ".Length")
            => lengths[reference.Name[..^7]],
        _ => throw new InvalidOperationException("Unmodeled string contract operand; no Unicode/nullability proof is inferred.")
    };
    BoolExpr Predicate(ExpressionNode expression, string? parameter) => expression switch
    {
        BoolLiteralNode literal => context.MkBool(literal.Value),
        UnaryOperationNode { Operator: UnaryOperator.Not } unary => context.MkNot(Predicate(unary.Operand, parameter)),
        BinaryOperationNode { Operator: BinaryOperator.And } binary => context.MkAnd(Predicate(binary.Left, parameter), Predicate(binary.Right, parameter)),
        BinaryOperationNode { Operator: BinaryOperator.Or } binary => context.MkOr(Predicate(binary.Left, parameter), Predicate(binary.Right, parameter)),
        BinaryOperationNode binary => binary.Operator switch
        {
            BinaryOperator.Equal => context.MkEq(Length(binary.Left, parameter), Length(binary.Right, parameter)),
            BinaryOperator.NotEqual => context.MkNot(context.MkEq(Length(binary.Left, parameter), Length(binary.Right, parameter))),
            BinaryOperator.GreaterThan => context.MkGt(Length(binary.Left, parameter), Length(binary.Right, parameter)),
            BinaryOperator.GreaterOrEqual => context.MkGe(Length(binary.Left, parameter), Length(binary.Right, parameter)),
            BinaryOperator.LessThan => context.MkLt(Length(binary.Left, parameter), Length(binary.Right, parameter)),
            BinaryOperator.LessOrEqual => context.MkLe(Length(binary.Left, parameter), Length(binary.Right, parameter)),
            _ => throw new InvalidOperationException("Unmodeled string contract operator.")
        },
        _ => throw new InvalidOperationException("Unmodeled string contract predicate.")
    };
    var preconditions = function.Preconditions.Select(pre => Predicate(pre.Condition, null)).ToList();
    foreach (var parameter in function.Parameters)
        if (parameter.InlineRefinement != null)
            preconditions.Add(Predicate(parameter.InlineRefinement.Predicate, parameter.Name));
    var domain = context.MkAnd(lengths.Values.SelectMany(length => new[] {
        context.MkGe(length, context.MkInt(0)), context.MkLe(length, context.MkInt(int.MaxValue)) }).ToArray());
    var expected = nonempty ? context.MkGt(lengths["s"], context.MkInt(0)) : context.MkTrue();
    using var solver = context.MkSolver();
    using var settings = context.MkParams();
    settings.Add("timeout", 5000u);
    solver.Parameters = settings;
    solver.Assert(domain, context.MkNot(context.MkEq(context.MkAnd(preconditions.ToArray()), expected)));
    if (solver.Check() != Status.UNSATISFIABLE)
        throw new InvalidOperationException("Missing/wrong string preconditions or narrowed prompt domain.");
    foreach (var postcondition in function.Postconditions)
    {
        solver.Reset();
        solver.Assert(domain, expected, context.MkNot(Predicate(postcondition.Condition, null)));
        if (solver.Check() != Status.UNSATISFIABLE)
            throw new InvalidOperationException("String postconditions exclude prompt inputs or are unmodeled.");
    }
}

static IEnumerable<(string[] Inputs, object Expected)> StringCases(string profile)
{
    string[] strings = ["", "a", "A", "-17", "+0", "@", "@@", "not an email@", "x@y", "x.y",
        "Hello, !", "\"quoted\"", "line\nbreak", " \t\r\n", "caf\u00e9", "e\u0301", "\u4e2d\u6587",
        "\ud83d\ude00", "a\ud83d\ude00z", "\ud800", "\udc00", "\0", "a\0b"];
    foreach (var text in strings)
    {
        if (profile == "str001")
            yield return ([text], text.Length);
        else if (profile == "str003" && text.Length > 0)
            yield return ([text], text[0]);
        else if (profile == "str004")
            yield return ([text], text.IndexOf('@') >= 0);
        else if (profile == "str005")
            yield return ([text], "Hello, " + text + "!");
        else if (profile == "str002")
            foreach (var substring in strings)
            {
                var found = false;
                for (var index = 0; index <= text.Length - substring.Length; index++)
                {
                    var equal = true;
                    for (var character = 0; character < substring.Length; character++)
                        equal &= text[index + character] == substring[character];
                    found |= equal;
                }
                yield return ([text, substring], found);
            }
    }
}

static IEnumerable<(int[] Inputs, object Expected)> WritingCases(string name)
{
    var values = new[] { int.MinValue, int.MinValue + 1, -1073741824, -46340, -100, -7, -3, -2, -1,
        0, 1, 2, 3, 7, 100, 46340, 1073741823, int.MaxValue - 1, int.MaxValue };
    if (name == "SumToN")
    {
        long sum = 0;
        for (var n = 0; n <= 65535; n++)
        {
            sum += n;
            yield return ([n], checked((int)sum));
        }
        yield break;
    }
    foreach (var first in values)
    {
        if (name == "SquareRoot" && first >= 0)
            yield return ([first], first);
        else if (name == "Abs" && first != int.MinValue)
            yield return ([first], (int)Math.Abs((long)first));
        else if (name == "SafeDivide")
            foreach (var second in values)
                if (second != 0 && (long)first % second == 0)
                {
                    var quotient = (long)first / second;
                    if (quotient >= int.MinValue && quotient <= int.MaxValue)
                        yield return ([first, second], (int)quotient);
                }
    }
}

static IEnumerable<(int[] Inputs, object Expected)> IdiomCases(string name)
{
    var boundaries = new[] { int.MinValue, int.MinValue + 1, -1073741824, -46340, -100, -7, -3, -2, -1,
        0, 1, 2, 3, 7, 100, 46340, 1073741823, int.MaxValue - 1, int.MaxValue };
    if (name == "IsEven")
    {
        foreach (var n in boundaries.Concat(Enumerable.Range(-256, 513)).Distinct())
            yield return ([n], (long)n % 2 == 0);
        yield break;
    }
    if (name != "Clamp")
        throw new InvalidOperationException($"Unknown idiom runtime oracle: {name}.");
    foreach (var min in boundaries)
        foreach (var max in boundaries)
            if (min <= max)
                foreach (var value in boundaries)
                {
                    var expected = value < min ? min : value > max ? max : value;
                    yield return ([min, max, value], expected);
                }
}

static (Assembly Assembly, SyntaxTree[] Trees) CompileRuntime(string generatedCode)
{
    var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
    SyntaxTree[] trees =
    [
        CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble, parseOptions),
        CSharpSyntaxTree.ParseText(generatedCode, parseOptions)
    ];
    var runtime = CSharpCompilation.Create(
        "LogicTaskRuntime", trees, GeneratedCSharpCompiler.References,
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    using var stream = new MemoryStream();
    var emitted = runtime.Emit(stream);
    if (!emitted.Success)
        throw new InvalidOperationException($"Generated C# compilation failed:\n{string.Join("\n", emitted.Diagnostics)}");
    return (Assembly.Load(stream.ToArray()), trees);
}

static void VerifyGeneric(string profile, ModuleNode module, string generatedCode)
{
    const BindingFlags members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
        | BindingFlags.Static | BindingFlags.DeclaredOnly;
    var name = profile switch { "gen001" => "Identity", "gen002" => "Container", _ => "Repository" };
    var pending = new Stack<AstNode>();
    var seen = new HashSet<AstNode>(ReferenceEqualityComparer.Instance);
    pending.Push(module);
    while (pending.TryPop(out var node))
    {
        if (!seen.Add(node))
            continue;
        if (node is CSharpInteropBlockNode or RawCSharpNode or RawCSharpExpressionNode)
            throw new InvalidOperationException("Raw interop cannot establish native generic behavior.");
        foreach (var child in RecursiveAstWalker.GetAllChildren(node))
            pending.Push(child);
        if (node is LocalFunctionStatementNode local)
            pending.Push(local.Function);
    }
    var (assembly, _) = CompileRuntime(generatedCode);
    Type generic;
    MethodInfo? identity = null;
    string[] fieldNames = [];
    if (profile == "gen001")
    {
        var native = module.Functions.Where(function => function.Name == name).ToArray();
        if (native.Length != 1)
            throw new InvalidOperationException("Expected exactly one native Identity function.");
        var function = native[0];
        if (function.Visibility != Visibility.Public || function.IsAsync
            || function.TypeParameters.Count != 1 || function.TypeParameters[0].Name != "T"
            || function.Parameters.Count != 1 || function.Parameters[0].TypeName != "T"
            || function.Parameters[0].Modifier != Calor.Compiler.Ast.ParameterModifier.None
            || function.Output?.TypeName != "T")
            throw new InvalidOperationException("Wrong native generic Identity signature.");
        var owner = assembly.GetType($"{module.Name}.{module.Name}Module")
            ?? throw new InvalidOperationException("Missing emitted Identity owner.");
        var methods = owner.GetMethods(members).Where(method => method.Name == name).ToArray();
        if (!owner.IsPublic || methods.Length != 1)
            throw new InvalidOperationException("Wrong reflected Identity identity.");
        identity = methods[0];
        if (!identity.IsPublic || !identity.IsStatic || !identity.IsGenericMethodDefinition
            || identity.GetGenericArguments().Length != 1)
            throw new InvalidOperationException("Wrong reflected generic Identity signature.");
        generic = identity.GetGenericArguments()[0];
        if (generic.Name != "T")
            throw new InvalidOperationException("Wrong reflected Identity type-parameter name.");
        if (identity.ReturnType != generic || identity.GetParameters().Length != 1
            || identity.GetParameters()[0].ParameterType != generic)
            throw new InvalidOperationException("Wrong reflected Identity parameter/return type.");
    }
    else
    {
        var native = module.Classes.Where(type => type.Name == name).ToArray();
        if (native.Length != 1)
            throw new InvalidOperationException($"Expected exactly one native {name} class.");
        var declaration = native[0];
        if (declaration.Visibility != Visibility.Public || declaration.IsStatic
            || declaration.TypeParameters.Count != 1 || declaration.TypeParameters[0].Name != "T")
            throw new InvalidOperationException($"Wrong native public generic {name} identity.");
        if (profile == "gen003" && !declaration.TypeParameters[0].Constraints.Any(
            constraint => constraint.Kind is TypeConstraintKind.Class or TypeConstraintKind.ClassNullable))
            throw new InvalidOperationException("Missing native reference-type constraint.");
        bool ListType(string type) => Regex.Replace(type, @"\s+", "") is "List<T>" or "System.Collections.Generic.List<T>";
        var fields = declaration.Fields.Where(field => field.Visibility == Visibility.Private && !field.IsStatic
            && (profile == "gen002" ? field.TypeName == "T" : ListType(field.TypeName))).ToArray();
        if (fields.Length == 0)
            throw new InvalidOperationException("Expected a native private generic storage field.");
        fieldNames = fields.Select(field => field.Name).ToArray();
        foreach (var required in profile == "gen002" ? new[] { "Get", "Set" } : new[] { "Add" })
        {
            var methods = declaration.Methods.Where(method => method.Name == required).ToArray();
            if (methods.Length != 1)
                throw new InvalidOperationException($"Expected exactly one native {name}.{required} method.");
            var method = methods[0];
            var get = required == "Get";
            if (method.IsStatic || method.IsAsync || method.TypeParameters.Count != 0
                || (required == "Add" && method.Visibility != Visibility.Public)
                || method.Parameters.Count != (get ? 0 : 1)
                || (!get && (method.Parameters[0].TypeName != "T"
                    || method.Parameters[0].Modifier != Calor.Compiler.Ast.ParameterModifier.None))
                || (get && method.Output?.TypeName != "T"))
                throw new InvalidOperationException($"Wrong native {name}.{required} signature.");
        }
        generic = assembly.GetType($"{module.Name}.{name}`1")
            ?? throw new InvalidOperationException($"Missing emitted generic {name}.");
        if (!generic.IsPublic || !generic.IsClass || !generic.IsGenericTypeDefinition
            || generic.GetGenericArguments().Length != 1 || generic.GetGenericArguments()[0].Name != "T")
            throw new InvalidOperationException($"Wrong reflected public generic {name} identity.");
        if (profile == "gen003" && !generic.GetGenericArguments()[0].GenericParameterAttributes
            .HasFlag(GenericParameterAttributes.ReferenceTypeConstraint))
            throw new InvalidOperationException("Missing reflected reference-type constraint.");
        var parameter = generic.GetGenericArguments()[0];
        var expected = profile == "gen002" ? parameter : typeof(List<>).MakeGenericType(parameter);
        foreach (var fieldName in fieldNames)
        {
            var field = generic.GetField(fieldName, members);
            if (field == null || !field.IsPrivate || field.IsStatic || field.FieldType != expected)
                throw new InvalidOperationException("Wrong reflected actual generic storage field.");
        }
    }

    // Independently chosen CLR types/values, not values supplied by the candidate.
    (Type Type, object?[] Values)[] candidates =
    [
        (typeof(int), [0, -7, 42, int.MinValue]),
        (typeof(long), [0L, -11L, long.MaxValue]),
        (typeof(Guid), [Guid.Empty, new Guid("835cdf34-c9f7-49db-a954-d0a8a2f38021")]),
        (typeof(string), [null, "", new string('x', 3), new string('x', 3), "different"]),
        (typeof(object), [null, new object(), new object()]),
        (typeof(List<int>), [null, new List<int> { 1 }, new List<int> { 1 }, new List<int>()])
    ];
    var closedCount = 0;
    var caseCount = 0;
    foreach (var (argument, values) in candidates)
    {
        if (profile == "gen003" && argument.IsValueType)
            continue;
        MethodInfo? closedIdentity = null;
        Type? closedClass = null;
        try
        {
            if (identity != null)
                closedIdentity = identity.MakeGenericMethod(argument);
            else
                closedClass = generic.MakeGenericType(argument);
        }
        catch (ArgumentException)
        {
            // Additional prompt-unspecified constraints are allowed. They can
            // leave this finite setup unsupported, never silently accepted.
            continue;
        }
        closedCount++;
        bool Same(object? actual, object? expected) => argument.IsValueType
            ? Equals(actual, expected) : ReferenceEquals(actual, expected);
        void Require(bool condition, string detail)
        {
            if (!condition)
                throw new InvalidOperationException($"{name}<{argument}>: {detail}.");
        }
        if (closedIdentity != null)
        {
            foreach (var value in values)
            {
                Require(Same(closedIdentity.Invoke(null, [value]), value), "expected unchanged input identity");
                caseCount++;
            }
            continue;
        }
        var type = closedClass!;
        object Construct()
        {
            var constructors = type.GetConstructors(members).Where(constructor => !constructor.IsStatic).ToArray();
            var empty = constructors.SingleOrDefault(constructor => constructor.GetParameters().Length == 0);
            if (empty != null)
                return empty.Invoke([]);
            var unary = constructors.Where(constructor => constructor.GetParameters().Length == 1
                && (constructor.GetParameters()[0].ParameterType == argument
                    || constructor.GetParameters()[0].ParameterType == typeof(List<>).MakeGenericType(argument))).ToArray();
            if (unary.Length != 1)
                throw new InvalidOperationException("Unsupported finite constructor setup: need a real zero-argument or unambiguous T/List<T> constructor.");
            var inputType = unary[0].GetParameters()[0].ParameterType;
            object? input = values.First(value => value != null);
            if (inputType != argument)
            {
                var seed = (System.Collections.IList)(Activator.CreateInstance(inputType)
                    ?? throw new InvalidOperationException("Cannot create constructor list input."));
                foreach (var value in values)
                    seed.Add(value);
                input = seed;
            }
            return unary[0].Invoke([input]);
        }
        MethodInfo Method(string methodName, bool get)
        {
            var methods = type.GetMethods(members).Where(method => method.Name == methodName).ToArray();
            Require(methods.Length == 1, $"expected one reflected {methodName}");
            var method = methods[0];
            Require(!method.IsStatic && !method.IsGenericMethod && method.GetParameters().Length == (get ? 0 : 1)
                && (get ? method.ReturnType == argument : method.GetParameters()[0].ParameterType == argument)
                && (methodName != "Add" || method.IsPublic), $"wrong reflected {methodName} signature");
            return method;
        }
        bool MatchesStorage(FieldInfo storage)
        {
            var instance = Construct();
            var other = Construct();
            if (profile == "gen002")
            {
                var get = Method("Get", true);
                var set = Method("Set", false);
                var untouched = storage.GetValue(other);
                foreach (var value in values.Concat(values.Reverse()))
                {
                    storage.SetValue(instance, value);
                    if (!Same(get.Invoke(instance, []), value))
                        return false;
                    storage.SetValue(instance, values.First(candidate => !Same(candidate, value)));
                    set.Invoke(instance, [value]);
                    if (!Same(storage.GetValue(instance), value)
                        || !Same(get.Invoke(instance, []), value)
                        || !Same(storage.GetValue(other), untouched))
                        return false;
                }
            }
            else
            {
                var add = Method("Add", false);
                var list = (System.Collections.IList?)storage.GetValue(instance);
                var expected = list == null ? [] : list.Cast<object?>().ToList();
                var otherList = (System.Collections.IList?)storage.GetValue(other);
                var untouched = otherList?.Cast<object?>().ToArray();
                foreach (var value in values.Concat(values.Reverse()))
                {
                    add.Invoke(instance, [value]);
                    expected.Add(value);
                    list = storage.GetValue(instance) as System.Collections.IList;
                    if (list == null || list.Count != expected.Count
                        || !list.Cast<object?>().Zip(expected).All(pair => Same(pair.First, pair.Second)))
                        return false;
                    otherList = (System.Collections.IList?)storage.GetValue(other);
                    if (!(untouched == null ? otherList == null
                        : otherList != null && otherList.Count == untouched.Length
                            && otherList.Cast<object?>().Zip(untouched).All(pair => Same(pair.First, pair.Second))))
                        return false;
                }
            }
            return true;
        }
        var matchingFields = fieldNames.Count(fieldName => MatchesStorage(type.GetField(fieldName, members)!));
        Require(matchingFields > 0, "expected one actual private storage field to satisfy every state and identity check");
        caseCount += values.Length * 2;
    }
    if (closedCount < 2)
        throw new InvalidOperationException("Unsupported finite closed-type setup: fewer than two independent CLR types satisfy the declared constraints.");
    Console.WriteLine($"Verification passed: {name}; native/reflected generic identity"
        + (identity == null ? " and actual private storage" : "")
        + $"; {closedCount} closed types, {caseCount} runtime cases.");
}

static void VerifyEnum(string profile, ModuleNode module, string generatedCode)
{
    var name = profile switch { "enum001" => "Status", "enum002" => "Color", _ => "Priority" };
    string[] required = profile switch
    {
        "enum001" => ["Pending", "InProgress", "Completed"],
        "enum002" => ["Red", "Green", "Blue"],
        _ => ["Low", "Medium", "High", "Critical"]
    };
    var declarations = module.Enums.Where(candidate => candidate.Name == name).ToArray();
    if (declarations.Length != 1)
        throw new InvalidOperationException($"Expected exactly one native module enum named {name}.");
    var declaration = declarations[0];
    if (declaration.Visibility != Visibility.Public)
        throw new InvalidOperationException($"Native enum {name} must be public.");
    foreach (var memberName in required)
    {
        var members = declaration.Members.Where(member => member.Name == memberName).ToArray();
        if (members.Length != 1)
            throw new InvalidOperationException($"Expected exactly one native {name}.{memberName} member.");
        if (profile == "enum002" && members[0].Value == null)
            throw new InvalidOperationException($"Native {name}.{memberName} requires an explicit integer assignment.");
    }

    FunctionNode? urgent = null;
    if (profile == "enum003")
    {
        var extensions = module.EnumExtensions.Where(extension => extension.EnumName == name).ToArray();
        if (extensions.Length != 1)
            throw new InvalidOperationException("Expected exactly one native Priority enum extension.");
        var methods = extensions[0].Methods.Where(method => method.Name == "IsUrgent").ToArray();
        if (methods.Length != 1)
            throw new InvalidOperationException("Expected exactly one native IsUrgent method in the Priority extension.");
        urgent = methods[0];
        if (urgent.Visibility != Visibility.Public || urgent.IsAsync || urgent.TypeParameters.Count != 0
            || urgent.Output == null || AttributeHelper.ExpandType(urgent.Output.TypeName) != "BOOL"
            || urgent.Parameters.Count != 1 || urgent.Parameters[0].TypeName != name
            || urgent.Parameters[0].Modifier != Calor.Compiler.Ast.ParameterModifier.None)
            throw new InvalidOperationException("Wrong native public bool IsUrgent(Priority) extension signature.");
    }

    var (assembly, _) = CompileRuntime(generatedCode);
    var type = assembly.GetType($"{module.Name}.{name}")
        ?? throw new InvalidOperationException($"Generated CLR enum {name} not found.");
    if (!type.IsEnum || !type.IsPublic || type.Assembly != assembly)
        throw new InvalidOperationException($"Wrong generated public CLR enum identity for {name}.");
    var values = new Dictionary<string, object>(StringComparer.Ordinal);
    for (var index = 0; index < required.Length; index++)
    {
        var memberName = required[index];
        var field = type.GetField(memberName, BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            ?? throw new InvalidOperationException($"Generated CLR member {name}.{memberName} missing.");
        if (!field.IsLiteral || field.FieldType != type)
            throw new InvalidOperationException($"Wrong generated CLR enum member identity for {name}.{memberName}.");
        var value = field.GetValue(null)
            ?? throw new InvalidOperationException($"Generated CLR member {name}.{memberName} has no value.");
        if (profile == "enum002"
            && Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture) != new[] { 1m, 2m, 4m }[index])
            throw new InvalidOperationException($"Wrong explicit CLR value for Color.{memberName}; expected {new[] { 1, 2, 4 }[index]}.");
        values.Add(memberName, value);
    }
    if (urgent != null)
    {
        var extension = assembly.GetType($"{module.Name}.PriorityExtensions")
            ?? throw new InvalidOperationException("Generated Priority extension class not found.");
        var methods = extension.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => method.Name == "IsUrgent").ToArray();
        if (!extension.IsPublic || !extension.IsAbstract || !extension.IsSealed || methods.Length != 1)
            throw new InvalidOperationException("Wrong generated public Priority extension identity.");
        var method = methods[0];
        if (!method.IsPublic || !method.IsStatic || method.IsGenericMethod || method.ReturnType != typeof(bool)
            || !method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(new[] { type })
            || !method.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false))
            throw new InvalidOperationException("Wrong reflected public bool IsUrgent(Priority) extension signature.");
        // Do not filter named values through optional guards. Runtime contracts
        // must admit every required input, including inline parameter refinements.
        foreach (var memberName in required)
        {
            var expected = memberName is "High" or "Critical";
            var actual = method.Invoke(null, [values[memberName]]);
            if (!Equals(actual, expected))
                throw new InvalidOperationException($"IsUrgent(Priority.{memberName}) returned {actual}; expected {expected}.");
        }
    }
    Console.WriteLine($"Verification passed: {name}; native public enum and {required.Length} reflected CLR members"
        + (urgent == null ? "." : "; native/reflected extension and 4 named-value runtime cases."));
}

static void CheckBasicEntryDomain(FunctionNode function, string[] names)
{
    using var context = new Context();
    var translator = new ContractTranslator(context);
    foreach (var name in names)
        if (!translator.DeclareVariable(name, "i32"))
            throw new InvalidOperationException($"Cannot model parameter {name}.");
    BoolExpr Translate(ExpressionNode expression)
    {
        CheckEntryConditionExpression(expression);
        var translated = translator.TranslateBoolExpr(expression);
        if (translated == null || translator.Warnings.Count != 0)
            throw new InvalidOperationException($"Basic precondition translation failed: {string.Join("; ", translator.Warnings)}");
        return translated;
    }
    var conditions = TranslateEntryConditions(function, translator, Translate);
    BoolExpr domain = context.MkTrue();
    if (function.Name == "Multiply")
    {
        // The prompt specifies no overflow policy. Only representable products
        // constrain entry contracts or runtime cases, for checked and unchecked bodies.
        var product = context.MkBVMul(context.MkSignExt(32, context.MkBVConst("a", 32)),
            context.MkSignExt(32, context.MkBVConst("b", 32)));
        domain = context.MkAnd(context.MkBVSGE(product, context.MkBV(int.MinValue, 64)),
            context.MkBVSLE(product, context.MkBV(int.MaxValue, 64)));
    }
    using var solver = context.MkSolver();
    using var settings = context.MkParams();
    settings.Add("timeout", 5000u);
    solver.Parameters = settings;
    solver.Assert(context.MkAnd(domain, context.MkNot(context.MkAnd(conditions))));
    var status = solver.Check();
    if (status != Status.UNSATISFIABLE)
        throw new InvalidOperationException($"Basic preconditions exclude prompt inputs: {status}; "
            + (status == Status.SATISFIABLE ? solver.Model.ToString() : solver.ReasonUnknown));
}

static BoolExpr[] TranslateEntryConditions(
    FunctionNode function, ContractTranslator translator, Func<ExpressionNode, BoolExpr> translate)
{
    var conditions = function.Preconditions.Select(contract => translate(contract.Condition)).ToList();
    foreach (var parameter in function.Parameters)
    {
        if (parameter.InlineRefinement == null)
            continue;
        translator.PushSelfVariable(parameter.Name);
        try
        {
            conditions.Add(translate(parameter.InlineRefinement.Predicate));
        }
        finally
        {
            translator.PopSelfVariable();
        }
    }
    return conditions.ToArray();
}

static void CheckEntryConditionExpression(ExpressionNode expression)
{
    // Entry predicates can reference the inline refinement's scoped # binding.
    if (expression is SelfRefNode)
        return;
    if (expression is BinaryOperationNode binary && binary.Operator is BinaryOperator.Equal or BinaryOperator.NotEqual
        or BinaryOperator.LessThan or BinaryOperator.LessOrEqual or BinaryOperator.GreaterThan
        or BinaryOperator.GreaterOrEqual or BinaryOperator.And or BinaryOperator.Or)
    {
        CheckEntryConditionExpression(binary.Left);
        CheckEntryConditionExpression(binary.Right);
    }
    else if (expression is UnaryOperationNode unary && unary.Operator == UnaryOperator.Not)
        CheckEntryConditionExpression(unary.Operand);
    else if (expression is ConditionalExpressionNode conditional)
    {
        CheckEntryConditionExpression(conditional.Condition);
        CheckEntryConditionExpression(conditional.WhenTrue);
        CheckEntryConditionExpression(conditional.WhenFalse);
    }
    else if (expression is ImplicationExpressionNode implication)
    {
        CheckEntryConditionExpression(implication.Antecedent);
        CheckEntryConditionExpression(implication.Consequent);
    }
    else
        CheckModeledExpression(expression);
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
        "GradeToLetter" => GradeExpression(statement) is { } expression && ConditionalCount(expression) > 1
            || statement is IfStatementNode inner && statements.OfType<IfStatementNode>()
            .Any(outer => !ReferenceEquals(inner, outer)
                && RecursiveAstWalker.EnumerateStatements(outer.ThenBody)
                    .Concat(outer.ElseBody == null ? [] : RecursiveAstWalker.EnumerateStatements(outer.ElseBody))
                    .Concat(outer.ElseIfClauses.SelectMany(clause => RecursiveAstWalker.EnumerateStatements(clause.Body)))
                    .Any(candidate => ReferenceEquals(candidate, inner))),
        "PrintEvenNumbers" => statement is ForStatementNode loop
            && RecursiveAstWalker.EnumerateStatements(loop.Body).OfType<IfStatementNode>().Any(),
        "PrintDictionary" => statement is ForeachStatementNode or DictionaryForeachNode,
        _ => false
    }).ToArray();
    if (witnesses.Length == 0)
        throw new InvalidOperationException($"Required native control flow missing from {function.Name}.");
    if (function.Name is not ("Classify" or "GradeToLetter")
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
        new CSharpEmitter(ContractMode.Debug) { LineDirectiveFilePath = path }.Emit(module), options);
    if (!SyntaxFactory.AreEquivalent(original.GetRoot(), mapped.GetRoot()))
        throw new InvalidOperationException("Cannot establish control-flow coupling: source-mapped emission differs from compiled C#.");
    var root = mapped.GetCompilationUnitRoot();
    var method = root.DescendantNodes().OfType<ClassDeclarationSyntax>()
        .Single(type => type.Identifier.ValueText == module.Name + "Module")
        .Members.OfType<MethodDeclarationSyntax>().Single(candidate => candidate.Identifier.ValueText == function.Name);
    var marker = new SyntaxAnnotation("control-flow-witness");
    StatementSyntax Match(StatementNode witness)
    {
        var candidates = method.DescendantNodes().OfType<StatementSyntax>().Where(statement =>
            (witness is ForStatementNode && statement is BlockSyntax or ForStatementSyntax
                || witness is WhileStatementNode && statement is WhileStatementSyntax
                || witness is DoWhileStatementNode && statement is DoStatementSyntax
                || witness is IfStatementNode && statement is IfStatementSyntax
                || function.Name == "GradeToLetter" && witness is ReturnStatementNode && statement is ReturnStatementSyntax
                || function.Name == "GradeToLetter" && witness is BindStatementNode && statement is LocalDeclarationStatementSyntax
                || witness is ForeachStatementNode or DictionaryForeachNode && statement is CommonForEachStatementSyntax)
            && statement.GetFirstToken().LeadingTrivia.Any(trivia =>
                trivia.GetStructure() is LineDirectiveTriviaSyntax directive && directive.IsActive
                && directive.File.ValueText == path
                && directive.Line.ValueText == witness.Span.Line.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            .ToArray();
        if (candidates.Length != 1)
            throw new InvalidOperationException($"Cannot establish control-flow coupling: expected one emitted witness at line {witness.Span.Line}, found {candidates.Length}.");
        return candidates[0];
    }
    var matched = witnesses.Select(Match).ToArray();
    if (matched.Distinct().Count() != witnesses.Length)
        throw new InvalidOperationException("Cannot establish control-flow coupling: ambiguous emitted witnesses.");
    // Trace loop bodies, not headers: printing while evaluating a bound is not
    // output produced by an iteration. Both selected emitters lower native for
    // into a mapped block with one direct while; no generated names are matched.
    var scopes = matched.SelectMany((statement, index) => statement switch
    {
        ReturnStatementSyntax or LocalDeclarationStatementSyntax when function.Name == "GradeToLetter" =>
            NestedExpressions(statement, witnesses[index]).Cast<SyntaxNode>(),
        ForStatementSyntax loop => [loop.Statement],
        BlockSyntax block when function.Name is "PrintNumbers" or "PrintEvenNumbers" =>
            [block.Statements.OfType<WhileStatementSyntax>().Single().Statement],
        WhileStatementSyntax loop => [loop.Statement],
        DoStatementSyntax loop => [loop.Statement],
        IfStatementSyntax conditional => [(SyntaxNode)conditional],
        CommonForEachStatementSyntax loop => [loop.Statement],
        _ => throw new InvalidOperationException("Cannot establish control-flow coupling: unsupported emitted witness shape.")
    }).ToArray();
    var tracedRoot = root.ReplaceNodes(scopes, (originalScope, rewritten) =>
    {
        rewritten = rewritten.WithAdditionalAnnotations(marker);
        if (function.Name != "PrintDictionary")
            return rewritten;
        var loop = originalScope.Parent as CommonForEachStatementSyntax
            ?? throw new InvalidOperationException("Dictionary iteration scope missing.");
        var binding = loop switch
        {
            ForEachStatementSyntax pair => $"{pair.Identifier.Text}.Key, {pair.Identifier.Text}.Value",
            ForEachVariableStatementSyntax tuple when tuple.Variable is DeclarationExpressionSyntax
            { Designation: ParenthesizedVariableDesignationSyntax { Variables.Count: 2 } designation }
                && designation.Variables.All(variable => variable is SingleVariableDesignationSyntax)
                => string.Join(", ", designation.Variables.Cast<SingleVariableDesignationSyntax>()
                    .Select(variable => variable.Identifier.Text)),
            _ => throw new InvalidOperationException("Cannot establish dictionary pair binding.")
        };
        return rewritten.WithAdditionalAnnotations(new SyntaxAnnotation("dictionary-binding", binding));
    });
    var probe = "__CalorWitness_" + Guid.NewGuid().ToString("N");
    var traced = new WitnessRewriter(marker, probe, neutralize: false).Visit(tracedRoot) as CompilationUnitSyntax
        ?? throw new InvalidOperationException("Cannot instrument control-flow witnesses.");
    var probeDeclaration = function.Name == "PrintDictionary"
        ? $"internal static class {probe} {{ public static int Depth; public static int Visits; "
            + "public static readonly System.Collections.Generic.List<(string Key, int Value, string Output)> Records = new(); "
            + "private static string Key = \"\"; private static int Value; private static System.Text.StringBuilder Output = new(); "
            + "public static void Enter(string key, int value) { if (Depth != 0) throw new System.InvalidOperationException(\"Ambiguous nested dictionary iteration\"); "
            + "Depth++; Visits++; Key = key; Value = value; Output.Clear(); } "
            + "public static void Record(char value) { if (Depth > 0) Output.Append(value); } "
            + "public static void Exit() { Records.Add((Key, Value, Output.ToString())); Depth--; } }"
        : $"internal static class {probe} {{ public static int Depth; public static int Visits; "
            + "public static void Enter() { Depth++; Visits++; } public static void Exit() { Depth--; } "
            + "public static int Observe(int value) { Visits++; return value; } }";
    traced = traced.AddMembers(SyntaxFactory.ParseMemberDeclaration(probeDeclaration)
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
    if (function.Name == "PrintDictionary")
    {
        foreach (var input in Dictionaries())
        {
            var record = probeType.GetMethod("Record") ?? throw new InvalidOperationException("Dictionary output probe missing.");
            var records = probeType.GetField("Records")?.GetValue(null) as List<(string Key, int Value, string Output)>
                ?? throw new InvalidOperationException("Dictionary iteration records missing.");
            records.Clear();
            using var output = new BoundedOutput(() => depth.GetValue(null) is int value && value > 0,
                character => record.Invoke(null, [character]));
            var actual = Capture(target, [new Dictionary<string, int>(input)], output);
            CheckDictionaryOutput(input, actual);
            if (output.WitnessCharacters != actual.Length || records.Count != input.Count)
                throw new InvalidOperationException("Control-flow coupling failed: dictionary output must be produced within native foreach iterations.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var iteration in records)
            {
                if (!input.TryGetValue(iteration.Key, out var value) || value != iteration.Value || !seen.Add(iteration.Key))
                    throw new InvalidOperationException("Control-flow coupling failed: foreach must iterate each actual input pair exactly once.");
                CheckDictionaryOutput(new() { [iteration.Key] = iteration.Value }, iteration.Output);
            }
        }
        return;
    }
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
        if (function.Name == "PrintEvenNumbers")
        {
            // A native inner decision must take both outcomes and control stdout.
            // Counterfactual decisions also support inverted tests/continue guards;
            // merely executing a nonproducing conditional is not enough.
            var conditionals = witnesses.OfType<ForStatementNode>()
                .SelectMany(loop => RecursiveAstWalker.EnumerateStatements(loop.Body).OfType<IfStatementNode>())
                .Distinct().Select(conditional => Match(conditional) as IfStatementSyntax
                    ?? throw new InvalidOperationException("Cannot map native inner conditional.")).ToArray();
            if (conditionals.Length == 0)
                throw new InvalidOperationException("Control-flow coupling failed: missing emitted inner conditional.");
            var useful = false;
            var expectedLines = expectedOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var gatedLines = new HashSet<string>(StringComparer.Ordinal);
            foreach (var conditional in conditionals)
            {
                var conditionalTrace = root.ReplaceNode(conditional, conditional.WithCondition(
                    SyntaxFactory.ParseExpression($"global::{probe}.Decide({conditional.Condition})")));
                conditionalTrace = conditionalTrace.AddMembers(SyntaxFactory.ParseMemberDeclaration(
                    $"internal static class {probe} {{ public static int Taken; public static int NotTaken; "
                    + "public static bool Decide(bool value) { if (value) Taken++; else NotTaken++; return value; } }")
                    ?? throw new InvalidOperationException("Cannot declare conditional probe."));
                var conditionalAssembly = Build(conditionalTrace);
                var conditionalProbe = conditionalAssembly.GetType(probe)
                    ?? throw new InvalidOperationException("Conditional probe missing.");
                if (Capture(Target(conditionalAssembly), null).Replace("\r\n", "\n", StringComparison.Ordinal) != expectedOutput)
                    throw new InvalidOperationException("Control-flow coupling failed: conditional instrumentation changed stdout.");
                var taken = conditionalProbe.GetField("Taken")?.GetValue(null) is int trueCount && trueCount > 0;
                var notTaken = conditionalProbe.GetField("NotTaken")?.GetValue(null) is int falseCount && falseCount > 0;
                if (!taken || !notTaken)
                    continue;
                var dependent = true;
                var disabledLines = new HashSet<string>(StringComparer.Ordinal);
                foreach (var value in new[] { true, false })
                {
                    var changed = root.ReplaceNode(conditional,
                        conditional.WithCondition(SyntaxFactory.LiteralExpression(
                            value ? SyntaxKind.TrueLiteralExpression : SyntaxKind.FalseLiteralExpression)));
                    var changedOutput = Capture(Target(Build(changed)), null).Replace("\r\n", "\n", StringComparison.Ordinal);
                    dependent &= changedOutput != expectedOutput;
                    var changedLines = changedOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in expectedLines)
                        if (!changedLines.Contains(line, StringComparer.Ordinal))
                            disabledLines.Add(line);
                }
                useful |= dependent;
                if (dependent)
                    gatedLines.UnionWith(disabledLines);
            }
            if (!useful || expectedLines.Any(line => !gatedLines.Contains(line)))
                throw new InvalidOperationException("Control-flow coupling failed: inner conditionals must gate every printed even number.");
        }
        return;
    }
    var executed = false;
    foreach (var (inputs, expected) in Cases(function.Name))
    {
        visits.SetValue(null, 0);
        var actual = target.Invoke(null, inputs.Select(value => (object)value).ToArray());
        executed |= !Equals(visits.GetValue(null), 0);
        if (!Equals(actual, expected) || (function.Name != "GradeToLetter" && Equals(visits.GetValue(null), 0)))
            throw new InvalidOperationException($"Control-flow coupling failed: {function.Name} must execute a required conditional for input {inputs[0]}.");
    }
    if (!executed)
        throw new InvalidOperationException("Control-flow coupling failed: nested conditionals are dormant.");
    // A counterfactual removes only AST-mapped required conditionals. A sentinel
    // return keeps direct-return implementations compilable without treating
    // arbitrary compiler failures as evidence that the construct was necessary.
    var neutralizedRoot = root.ReplaceNodes(scopes, (_, rewritten) => rewritten.WithAdditionalAnnotations(marker));
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

static ExpressionNode? GradeExpression(StatementNode statement) => statement switch
{
    ReturnStatementNode returned => returned.Expression is ConditionalExpressionNode ? returned.Expression : null,
    BindStatementNode binding => binding.Initializer is ConditionalExpressionNode ? binding.Initializer : null,
    _ => null
};

static int ConditionalCount(ExpressionNode expression) => expression is ConditionalExpressionNode conditional
    ? 1 + ConditionalCount(conditional.Condition) + ConditionalCount(conditional.WhenTrue) + ConditionalCount(conditional.WhenFalse)
    : 0;

static ConditionalExpressionSyntax[] NestedExpressions(StatementSyntax statement, StatementNode witness)
{
    var expressions = statement.DescendantNodes().OfType<ConditionalExpressionSyntax>().ToArray();
    var expression = GradeExpression(witness) ?? throw new InvalidOperationException("Native grade conditional expression missing.");
    if (expressions.Length != ConditionalCount(expression))
        throw new InvalidOperationException("Cannot establish control-flow coupling: conditional expression mapping is ambiguous.");
    return expressions.Where(candidate => candidate.Ancestors().OfType<ConditionalExpressionSyntax>().Any()).ToArray();
}

static void CheckContracts(FunctionNode function, string[] names, string outputType, bool optionalMaxPost = false)
{
    using var context = new Context();
    var translator = new ContractTranslator(context);
    foreach (var name in names)
        if (!translator.DeclareVariable(name, "i32"))
            throw new InvalidOperationException($"Cannot model parameter {name}.");
    if (!translator.DeclareVariable("result", outputType))
        throw new InvalidOperationException("Cannot model result.");

    BoolExpr Translate(ExpressionNode expression, bool entryCondition = false)
    {
        if (entryCondition)
            CheckEntryConditionExpression(expression);
        else
            CheckModeledExpression(expression);
        var result = translator.TranslateBoolExpr(expression);
        if (result == null || translator.Warnings.Count != 0)
            throw new InvalidOperationException($"Contract translation failed: {string.Join("; ", translator.Warnings)}");
        return result;
    }
    BitVecExpr Integer(string name) => context.MkBVConst(name, 32);
    BitVecNum Constant(int value) => context.MkBV(value, 32);
    var resultValue = Integer("result");
    var actualPre = context.MkAnd(TranslateEntryConditions(function, translator,
        expression => Translate(expression, entryCondition: true)));
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
            if (optionalMaxPost)
                requiredPost = context.MkTrue();
            break;
        case "GradeToLetter":
            var grade = Integer("grade");
            requiredPre = context.MkAnd(context.MkBVSGE(grade, Constant(0)), context.MkBVSLE(grade, Constant(100)));
            requiredPost = context.MkTrue();
            expected = Constant(0);
            for (var category = 1; category <= 4; category++)
                expected = context.MkITE(context.MkBVSGE(grade, Constant(50 + category * 10)), Constant(category), expected);
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
    if (name == "GetZero")
    {
        yield return ([], 0);
        yield break;
    }
    if (name == "Multiply")
    {
        var factors = new[] { int.MinValue, int.MinValue + 1, -1073741824, -46341, -46340,
            -32768, -100, -2, -1, 0, 1, 2, 100, 32767, 46340, 46341, 1073741823, int.MaxValue - 1, int.MaxValue };
        foreach (var first in factors)
            foreach (var second in factors)
            {
                var product = (long)first * second;
                if (product >= int.MinValue && product <= int.MaxValue)
                    yield return ([first, second], (int)product);
            }
        yield break;
    }
    if (name == "Factorial")
    {
        var values = new[] { 1, 1, 2, 6, 24, 120 };
        for (var n = 0; n <= 5; n++)
            yield return ([n], values[n]);
        yield break;
    }
    if (name == "GradeToLetter")
    {
        for (var grade = 0; grade <= 100; grade++)
            yield return ([grade], grade >= 90 ? 4 : grade >= 80 ? 3 : grade >= 70 ? 2 : grade >= 60 ? 1 : 0);
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

static bool IsDictionaryType(string type)
{
    var normalized = type.Replace(" ", "", StringComparison.Ordinal);
    foreach (var prefix in new[] { "Dictionary", "System.Collections.Generic.Dictionary" })
        if (normalized.StartsWith(prefix + "<", StringComparison.Ordinal) && normalized.EndsWith('>'))
        {
            var arguments = normalized[(prefix.Length + 1)..^1].Split(',');
            return arguments.Length == 2 && AttributeHelper.ExpandType(arguments[0]) == "STRING"
                && AttributeHelper.ExpandType(arguments[1]) == "INT";
        }
    return false;
}

static string Capture(MethodInfo method, object[]? inputs, BoundedOutput? output = null)
{
    var previous = Console.Out;
    using var owned = output == null ? new BoundedOutput() : null;
    var writer = output ?? owned ?? throw new InvalidOperationException("Output capture missing.");
    try
    {
        Console.SetOut(writer);
        method.Invoke(null, inputs);
        return writer.ToString();
    }
    finally
    {
        Console.SetOut(previous);
    }
}

static IEnumerable<Dictionary<string, int>> Dictionaries()
{
    yield return new();
    yield return new() { ["solo"] = -1 };
    yield return new() { ["upper"] = int.MaxValue };
    yield return new() { ["lower"] = int.MinValue, ["zero"] = 0, ["positive"] = 17, ["negative"] = -23 };
    yield return new() { ["negative"] = int.MaxValue, ["positive"] = int.MinValue, ["zero"] = -97, ["lower"] = 42 };
}

static void CheckDictionaryOutput(Dictionary<string, int> input, string output)
{
    // Bounded formatting interpretation: adjacent key/integer tokens (either
    // orientation), separated by whitespace/punctuation and optional Key/Value labels.
    // This accepts separate lines, interpolation, tuple and JSON-like records;
    // arbitrary prose/ambiguous numeric keys are not guessed into successful pairs.
    if (input.Count == 0)
    {
        if (output.Length != 0)
            throw new InvalidOperationException("Wrong dictionary output: empty input must produce no pairs/output.");
        return;
    }
    var pattern = string.Join("|", input.Keys.OrderByDescending(key => key.Length).Select(Regex.Escape)) + @"|[+-]?\d+";
    var matches = Regex.Matches(output, pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    var remainder = Regex.Replace(output, pattern, "", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    foreach (var label in new[] { "Key", "Value" })
    {
        var labelPattern = $@"\b{label}\b";
        if (Regex.Matches(remainder, labelPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1)).Count > input.Count)
            throw new InvalidOperationException("Wrong dictionary output: extra pair labels.");
        remainder = Regex.Replace(remainder, labelPattern, "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
    }
    if (matches.Count != input.Count * 2 || remainder.Any(character => !char.IsWhiteSpace(character) && !char.IsPunctuation(character)
            && character != '='))
        throw new InvalidOperationException("Wrong dictionary output: incomplete, extra or ambiguous pair tokens.");
    var seen = new HashSet<string>(StringComparer.Ordinal);
    for (var index = 0; index < matches.Count; index += 2)
    {
        var first = matches[index].Value;
        var second = matches[index + 1].Value;
        var key = input.ContainsKey(first) ? first : second;
        var value = key == first ? second : first;
        if (!input.TryGetValue(key, out var expected) || !seen.Add(key)
            || !int.TryParse(value, System.Globalization.NumberStyles.AllowLeadingSign,
                System.Globalization.CultureInfo.InvariantCulture, out var actual) || actual != expected)
            throw new InvalidOperationException("Wrong dictionary output: key/value pairing or signed value mismatch.");
    }
}
sealed class BoundedOutput(Func<bool>? isWitnessActive = null, Action<char>? observe = null) : TextWriter
{
    private readonly StringBuilder output = new();
    public int WitnessCharacters { get; private set; }
    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value)
    {
        if (output.Length >= 4096)
            throw new InvalidOperationException("Excessive stdout: control-flow output exceeds 4096 characters.");
        output.Append(value);
        observe?.Invoke(value);
        if (isWitnessActive?.Invoke() == true)
            WitnessCharacters++;
    }

    public override string ToString() => output.ToString();
}

static class StringTaskProbe
{
    public const string Source = """
        public static class StringTaskProbe
        {
            static readonly System.Collections.Generic.HashSet<System.Text.StringBuilder> Created = new();
            static readonly System.Collections.Generic.Dictionary<System.Text.StringBuilder, System.Collections.Generic.List<string>> Appends = new();
            static readonly System.Collections.Generic.List<(System.Text.StringBuilder Builder, string Text)> Returned = new();
            static int Neutralized;
            public static void Reset(int neutralized) { Created.Clear(); Appends.Clear(); Returned.Clear(); Neutralized = neutralized; }
            public static System.Text.StringBuilder New(System.Text.StringBuilder builder)
            {
                if (!Created.Add(builder)) throw new System.InvalidOperationException("Duplicate builder construction.");
                Appends.Add(builder, new());
                return builder;
            }
            public static System.Text.StringBuilder Append(System.Text.StringBuilder builder, string value)
            {
                if (!Created.Contains(builder)) throw new System.InvalidOperationException("Append receiver was not constructed by the native witness.");
                var index = Appends[builder].Count;
                Appends[builder].Add(value);
                return builder.Append(index == Neutralized ? "" : value);
            }
            public static string Text(System.Text.StringBuilder builder)
            {
                var text = builder.ToString();
                Returned.Add((builder, text));
                return text;
            }
            public static void Check(object result, string name)
            {
                var matches = Returned.FindAll(entry => object.ReferenceEquals(entry.Text, result));
                if (matches.Count != 1 || !Created.Contains(matches[0].Builder)
                    || !Appends[matches[0].Builder].SequenceEqual(new[] { "Hello, ", name, "!" }))
                    throw new System.InvalidOperationException("StringBuilder coupling failed: returned ToString must use the same new instance and ordered required appends.");
            }
        }
        """;
}

sealed class WitnessRewriter(SyntaxAnnotation marker, string probe, bool neutralize) : CSharpSyntaxRewriter
{
    public override SyntaxNode? Visit(SyntaxNode? node)
    {
        if (node is ConditionalExpressionSyntax expression && node.HasAnnotation(marker))
        {
            if (neutralize)
                return SyntaxFactory.ParseExpression("int.MinValue").WithTriviaFrom(expression);
            var rewritten = base.Visit(expression) as ExpressionSyntax
                ?? throw new InvalidOperationException("Cannot rewrite conditional expression.");
            return SyntaxFactory.ParseExpression($"global::{probe}.Observe({rewritten})").WithTriviaFrom(expression);
        }
        if (node is StatementSyntax statement && node.HasAnnotation(marker))
        {
            if (neutralize)
                return SyntaxFactory.EmptyStatement().WithTriviaFrom(statement);
            var rewritten = base.Visit(node) as StatementSyntax
                ?? throw new InvalidOperationException("Cannot rewrite witness statement.");
            return SyntaxFactory.Block(
                SyntaxFactory.ParseStatement($"global::{probe}.Enter({statement.GetAnnotations("dictionary-binding").SingleOrDefault()?.Data});"),
                SyntaxFactory.TryStatement(SyntaxFactory.Block(rewritten), [],
                    SyntaxFactory.FinallyClause(SyntaxFactory.Block(
                        SyntaxFactory.ParseStatement($"global::{probe}.Exit();")))))
                .WithTriviaFrom(statement);
        }
        return base.Visit(node);
    }
}
