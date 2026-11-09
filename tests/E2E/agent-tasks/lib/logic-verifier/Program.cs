using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Calor.Compiler;
using Calor.Compiler.Analysis;
using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Migration;
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
    if (profile is "ref001" or "ref003" or "ref004" or "ref005" or "ref006" or "ref007" or "ref008")
    {
        VerifyRefactoring(profile, path);
        return;
    }
    if (profile is "lam001" or "lam002" or "lam003" or "lam004")
    {
        VerifyLambdaDelegate(profile, path);
        return;
    }
    if (profile is "pm004" or "pm005")
    {
        VerifyCarrierPatterns(profile, path);
        return;
    }
    if (profile is "pm001" or "pm002" or "pm003" or "pm006")
    {
        VerifyScalarPatterns(profile, path);
        return;
    }
    if (profile is "oop001" or "oop002" or "oop003" or "oop004" or "oop005" or "oop006")
    {
        var oopCompilation = Calor.Compiler.Program.Compile(
            File.ReadAllText(path), path, new Calor.Compiler.CompilationOptions { ContractMode = ContractMode.Debug });
        if (oopCompilation.HasErrors || oopCompilation.Ast == null)
            throw new InvalidOperationException($"Calor compilation failed:\n{string.Join("\n", oopCompilation.Diagnostics)}");
        VerifyOop(profile, oopCompilation.Ast, oopCompilation.GeneratedCode, path);
        return;
    }
    if (profile is "type005" or "type006")
    {
        VerifyTypeArithmetic(profile, path);
        return;
    }
    if (profile is "type001" or "type002" or "type003" or "type004")
    {
        var carrierSource = File.ReadAllText(path);
        var carrierCompilation = Calor.Compiler.Program.Compile(
            carrierSource, path, new Calor.Compiler.CompilationOptions { ContractMode = ContractMode.Debug });
        if (carrierCompilation.HasErrors || carrierCompilation.Ast == null)
            throw new InvalidOperationException($"Calor compilation failed:\n{string.Join("\n", carrierCompilation.Diagnostics)}");
        VerifyCarrier(profile, carrierCompilation.Ast, carrierCompilation.GeneratedCode);
        return;
    }
    if (profile is "async001" or "async002" or "async003" or "async004")
    {
        VerifyAsync(profile, path);
        return;
    }
    if (profile is "eff001" or "eff002" or "eff003" or "eff004" or "eff005" or "eff006" or "ref002")
    {
        VerifyEffects(profile, path);
        return;
    }
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
    if (profile is "coll001" or "coll002" or "coll003" or "coll004" or "coll005" or "coll006")
    {
        VerifyCollection(profile, path);
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
        case "DoubleValue":
        case "LogAndDouble":
            var doubled = context.MkBVMul(Wide(Integer("x")), context.MkBV(2, 64));
            limit = Fits(doubled);
            mathematicalOutput = context.MkEq(Wide(result), doubled);
            break;
        case "JustCompute":
        case "NoContractAdd":
            var addition = context.MkBVAdd(Wide(Integer(names[0])), Wide(Integer(names[1])));
            limit = Fits(addition);
            mathematicalOutput = context.MkEq(Wide(result), addition);
            if (function.Name == "NoContractAdd")
            {
                limit = context.MkTrue();
                requiredPre = context.MkAnd(names.SelectMany(name => new[]
                {
                    context.MkBVSGE(Integer(name), Constant(-1000000)),
                    context.MkBVSLE(Integer(name), Constant(1000000))
                }).ToArray());
                requiredPost = mathematicalOutput;
            }
            break;
        case "SafeMultiply":
            var multiplication = context.MkBVMul(Wide(Integer("a")), Wide(Integer("b")));
            limit = Fits(multiplication);
            requiredPost = context.MkAnd(
                context.MkImplies(context.MkAnd(context.MkBVSGT(Integer("a"), Constant(0)),
                    context.MkBVSGT(Integer("b"), Constant(0))), context.MkBVSGT(result, Constant(0))),
                context.MkImplies(context.MkAnd(context.MkBVSLT(Integer("a"), Constant(0)),
                    context.MkBVSLT(Integer("b"), Constant(0))), context.MkBVSGT(result, Constant(0))));
            // The required contracts specify signs, not an exact product.
            // Use the mathematical sign relation as a conservative output
            // overapproximation; runtime product cases remain independent.
            mathematicalOutput = context.MkAnd(requiredPost,
                context.MkImplies(context.MkOr(context.MkEq(Integer("a"), Constant(0)),
                    context.MkEq(Integer("b"), Constant(0))), context.MkEq(result, Constant(0))),
                context.MkImplies(context.MkOr(
                    context.MkAnd(context.MkBVSLT(Integer("a"), Constant(0)), context.MkBVSGT(Integer("b"), Constant(0))),
                    context.MkAnd(context.MkBVSGT(Integer("a"), Constant(0)), context.MkBVSLT(Integer("b"), Constant(0)))),
                    context.MkBVSLT(result, Constant(0))));
            break;
        case "ComputeWithTemp":
            var sum = context.MkBVAdd(Wide(Integer("a")), Wide(Integer("b")));
            var difference = context.MkBVSub(Wide(Integer("a")), Wide(Integer("b")));
            var arithmeticProduct = context.MkBVMul(sum, difference);
            limit = context.MkAnd(Fits(sum), Fits(difference), Fits(arithmeticProduct));
            mathematicalOutput = context.MkEq(Wide(result), arithmeticProduct);
            break;
        case "Accumulate":
            var accumulated = context.MkBVMul(Wide(Integer("n")), context.MkBV(3, 64));
            limit = Fits(accumulated);
            mathematicalOutput = context.MkEq(Wide(result), accumulated);
            break;
        case "GetListLength":
            requiredPre = context.MkBVSGE(Integer("n"), Constant(0));
            mathematicalOutput = context.MkEq(result, Integer("n"));
            break;
        case "HasValue":
            mathematicalOutput = context.MkEq(context.MkBoolConst("result"), context.MkOr(
                new[] { 10, 20, 30 }.Select(value => context.MkEq(Integer("value"), Constant(value))).ToArray()));
            break;
        case "SumList":
        case "CreateScores":
        case "UniqueCount":
        case "BuildCollection":
            mathematicalOutput = context.MkEq(result, Constant(function.Name switch
                { "SumList" => 60, "BuildCollection" => 5, _ => 3 }));
            break;
        case "SquareRoot":
            requiredPre = context.MkBVSGE(Integer("x"), Constant(0));
            mathematicalOutput = context.MkEq(result, Integer("x"));
            break;
        case "AbsoluteValue":
        case "Abs":
            var x = Integer("x");
            limit = context.MkNot(context.MkEq(x, Constant(int.MinValue)));
            mathematicalOutput = context.MkEq(result, context.MkITE(context.MkBVSLT(x, Constant(0)), context.MkBVNeg(x), x));
            requiredPost = context.MkBVSGE(result, Constant(0));
            if (function.Name == "AbsoluteValue")
                requiredPost = context.MkAnd(requiredPost, mathematicalOutput);
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
    var entryCounterexample = context.MkAnd(entryLimit, context.MkNot(context.MkEq(actualPre, requiredPre)));
    if (function.Name is "ComputeWithTemp" or "Accumulate")
    {
        var variables = names.Select(name => (Expr)Integer(name)).ToArray();
        foreach (var inputs in ArithmeticCases(function.Name))
        {
            var values = inputs.Select(value => (Expr)Constant(value)).ToArray();
            // A concrete true counterexample is sufficient to reject. False or
            // unresolved substitutions never replace the universal entry proof.
            if (entryCounterexample.Substitute(variables, values).Simplify().IsTrue)
                throw new InvalidOperationException("Missing/wrong writing preconditions: "
                    + $"Concrete representable entry counterexample ({string.Join(", ", names.Zip(inputs, (name, value) => $"{name}={value}"))}).");
        }
    }
    Prove(entryCounterexample, "Missing/wrong writing preconditions");
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

static void VerifyTypeArithmetic(string profile, string path)
{
    var name = profile == "type005" ? "ComputeWithTemp" : "Accumulate";
    string[] names = profile == "type005" ? ["a", "b"] : ["n"];
    var compiled = Calor.Compiler.Program.Compile(File.ReadAllText(path), path,
        new Calor.Compiler.CompilationOptions { ContractMode = ContractMode.Debug });
    if (compiled.HasErrors || compiled.Ast == null)
        throw new InvalidOperationException($"Calor compilation failed:\n{string.Join("\n", compiled.Diagnostics)}");
    var module = compiled.Ast;
    var functions = module.Functions.Where(candidate => candidate.Name == name).ToArray();
    if (functions.Length != 1)
        throw new InvalidOperationException($"Expected exactly one module function named {name}.");
    var function = functions[0];
    if (function.Visibility != Visibility.Public || function.IsAsync || function.TypeParameters.Count != 0
        || function.Output == null || AttributeHelper.ExpandType(function.Output.TypeName) != "INT"
        || !function.Parameters.Select(parameter => parameter.Name).SequenceEqual(names)
        || function.Parameters.Any(parameter => AttributeHelper.ExpandType(parameter.TypeName) != "INT"
            || parameter.Modifier != Calor.Compiler.Ast.ParameterModifier.None))
        throw new InvalidOperationException($"Wrong public signature for {name}.");
    var pending = new Stack<AstNode>();
    var native = new HashSet<AstNode>(ReferenceEqualityComparer.Instance);
    pending.Push(function);
    while (pending.TryPop(out var node))
        if (native.Add(node))
        {
            if (node is CSharpInteropBlockNode or RawCSharpNode or RawCSharpExpressionNode or LocalFunctionStatementNode)
                throw new InvalidOperationException("Arithmetic provenance requires native selected-function statements; raw/local-function fragments unsupported.");
            foreach (var child in RecursiveAstWalker.GetAllChildren(node))
                pending.Push(child);
        }
    CheckWritingContracts(function, names, "i32");
    var (assembly, trees) = CompileRuntime(compiled.GeneratedCode);
    MethodInfo Target(Assembly runtime)
    {
        var type = runtime.GetType($"{module.Name}.{module.Name}Module")
            ?? throw new InvalidOperationException("Arithmetic module CLR identity missing.");
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(candidate => candidate.Name == name).ToArray();
        if (!type.IsPublic || methods.Length != 1 || methods[0].IsGenericMethod || methods[0].ReturnType != typeof(int)
            || !methods[0].GetParameters().Select(parameter => parameter.ParameterType)
                .SequenceEqual(names.Select(_ => typeof(int))))
            throw new InvalidOperationException("Wrong generated public/static arithmetic signature.");
        return methods[0];
    }
    var cases = ArithmeticCases(name).ToArray();
    long Expected(int[] input) => name == "Accumulate" ? 3L * input[0]
        : ((long)input[0] + input[1]) * ((long)input[0] - input[1]);
    object[] Arguments(int[] input) => input.Cast<object>().ToArray();
    long Invoke(MethodInfo method, int[] input, string stage)
    {
        try
        {
            return (int)(method.Invoke(null, Arguments(input))
                ?? throw new InvalidOperationException("Arithmetic runtime returned null."));
        }
        catch (TargetInvocationException error)
        {
            throw new InvalidOperationException($"Arithmetic {stage} runtime failed at ({string.Join(",", input)}); "
                + "exceptions are not provenance/coupling evidence.", error);
        }
    }
    void Check(MethodInfo method, int[] input, string stage)
    {
        var actual = Invoke(method, input, stage);
        if (actual != Expected(input))
            throw new InvalidOperationException($"Arithmetic {stage}: {name}({string.Join(",", input)}) returned {actual}; expected {Expected(input)}.");
    }
    var options = new CSharpParseOptions(LanguageVersion.Preview);
    var debug = CSharpSyntaxTree.ParseText(
        new CSharpEmitter(ContractMode.Debug) { LineDirectiveFilePath = path }.Emit(module), options);
    if (!SyntaxFactory.AreEquivalent(trees[1].GetRoot(), debug.GetRoot()))
        throw new InvalidOperationException("Arithmetic native mapping failed: debug emission differs from compiled C#.");
    var mapped = CSharpSyntaxTree.ParseText(
        new CSharpEmitter(ContractMode.Off) { LineDirectiveFilePath = path }.Emit(module), options);
    var off = Target(CompileRuntime(mapped.ToString()).Assembly);
    var baseline = Target(assembly);
    foreach (var input in cases)
    {
        Check(baseline, input, "original baseline");
        Check(off, input, "contract-disabled baseline");
    }
    var mappingCompilation = CSharpCompilation.Create("ArithmeticMapping",
        [trees[0], mapped], GeneratedCSharpCompiler.References,
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    var probe = "__CalorArithmetic_" + Guid.NewGuid().ToString("N");
    var rewriter = new ArithmeticRewriter(mappingCompilation.GetSemanticModel(mapped), module.Name, function, path, probe);
    var instrumented = rewriter.Visit(mapped.GetRoot()) as CompilationUnitSyntax
        ?? throw new InvalidOperationException("Arithmetic instrumentation failed.");
    instrumented = instrumented.AddMembers(SyntaxFactory.ParseMemberDeclaration(ArithmeticRewriter.Probe(probe))
        ?? throw new InvalidOperationException("Arithmetic probe declaration failed."));
    var tracedAssembly = CompileRuntime(instrumented.NormalizeWhitespace().ToFullString()).Assembly;
    var traced = Target(tracedAssembly);
    var probeType = tracedAssembly.GetType(probe) ?? throw new InvalidOperationException("Arithmetic probe identity missing.");
    var records = probeType.GetField("Records")?.GetValue(null) as List<(string Key, bool Initial, long Value, int[] Reads, int Site)>
        ?? throw new InvalidOperationException("Arithmetic trace records missing.");
    var reset = probeType.GetMethod("Reset") ?? throw new InvalidOperationException("Arithmetic probe reset missing.");
    void Reset(string key = "", int occurrence = -1, bool zero = false) => reset.Invoke(null, [key, occurrence, zero]);
    bool Depends((string Key, bool Initial, long Value, int[] Reads, int Site)[] events, int from, int target)
    {
        var visited = new HashSet<int>();
        var work = new Stack<int>();
        work.Push(from);
        while (work.TryPop(out var index))
        {
            if (index == target)
                return true;
            if (index < 0 || index >= events.Length || !visited.Add(index))
                continue;
            foreach (var read in events[index].Reads)
                work.Push(read);
        }
        return false;
    }
    using var polynomial = new ArithmeticPolynomial();
    var bindings = rewriter.Bindings;
    var observations = cases.Select(input =>
    {
        Reset();
        Check(traced, input, "instrumented baseline");
        return records.ToArray();
    }).ToArray();
    var initialEvents = observations[0];
    string[] Keys(string kind) => initialEvents.Select((record, index) => (record, index))
        .Where(item => item.record.Initial && bindings.ContainsKey(item.record.Key)
            && polynomial.IsEvent(initialEvents, item.index, rewriter.Expressions, bindings, kind, []))
        .Select(item => item.record.Key).Distinct(StringComparer.Ordinal).ToArray();
    var sumKeys = Keys("sum");
    var differenceKeys = Keys("difference");
    var accumulatorKeys = Keys("zero").Where(key => bindings[key].IsMutable).ToArray();
    var candidates = name == "ComputeWithTemp"
        ? sumKeys.SelectMany(sumKey => differenceKeys.Where(key => key != sumKey)
            .Select(differenceKey => new[] { sumKey, differenceKey })).ToArray()
        : accumulatorKeys.Select(key => new[] { key }).ToArray();
    if (candidates.Length == 0 || candidates.Length > 64)
        throw new InvalidOperationException("Arithmetic native provenance missing/overbound: required sum/difference bindings or mutable zero accumulator.");
    var successful = 0;
    string[]? selectedKeys = null;
    foreach (var keys in candidates)
    {
        var valid = true;
        var changes = new bool[keys.Length];
        var count = 0;
        for (var caseIndex = 0; caseIndex < cases.Length; caseIndex++)
        {
            var input = cases[caseIndex];
            var events = observations[caseIndex];
            var returned = Array.FindLastIndex(events, record => record.Key.StartsWith("r:", StringComparison.Ordinal));
            if (returned < 0 || events.Count(record => record.Key.StartsWith("r:", StringComparison.Ordinal)) != 1)
                throw new InvalidOperationException("Arithmetic trace requires one actual native selected return.");
            var nRead = Array.FindIndex(events, record => record.Key == "p:n");
            var writes = keys.Select(key => events.Select((record, index) => (record, index))
                .Where(item => item.record.Key == key).ToArray()).ToArray();
            if (name == "ComputeWithTemp")
            {
                for (var index = 0; index < 2; index++)
                    valid &= writes[index].Length == 1 && writes[index][0].record.Initial
                        && writes[index][0].record.Value == (index == 0 ? (long)input[0] + input[1] : (long)input[0] - input[1])
                        && polynomial.IsEvent(events, writes[index][0].index, rewriter.Expressions, bindings,
                            index == 0 ? "sum" : "difference", [])
                        && Depends(events, returned, writes[index][0].index)
                        && Depends(events, writes[index][0].index, 0) && Depends(events, writes[index][0].index, 1);
                if (valid)
                    valid &= polynomial.IsEvent(events, returned, rewriter.Expressions, bindings, "product",
                        writes.Select(state => state[0].index).ToArray());
            }
            else
            {
                var state = writes[0];
                valid &= state.Length == 4 && state[0].record.Initial && state[0].record.Value == 0
                    && polynomial.IsEvent(events, state[0].index, rewriter.Expressions, bindings, "zero", [])
                    && state.Skip(1).All(item => !item.record.Initial) && Depends(events, returned, state[^1].index);
                if (valid)
                    for (var update = 1; update <= 3; update++)
                        valid &= state[update].record.Value == update * (long)input[0]
                            && Depends(events, state[update].index, state[update - 1].index)
                            && Depends(events, state[update].index, nRead)
                            && polynomial.IsEvent(events, state[update].index, rewriter.Expressions, bindings, "update",
                                [state[update - 1].index]);
                if (valid)
                    valid &= polynomial.IsEvent(events, returned, rewriter.Expressions, bindings, "return", [state[^1].index]);
            }
            if (!valid)
                break;
            for (var index = 0; index < keys.Length; index++)
            {
                var mutations = name == "ComputeWithTemp" ? 1 : 3;
                for (var update = 1; update <= mutations; update++)
                {
                    Reset(keys[index], name == "ComputeWithTemp" ? 0 : update, name == "ComputeWithTemp");
                    var actual = Invoke(traced, input, "counterfactual");
                    var expected = name == "ComputeWithTemp" ? 0 : Expected(input) + 1;
                    if (actual != expected)
                    {
                        valid = false;
                        break;
                    }
                    changes[index] |= actual != Expected(input);
                    count++;
                }
            }
            if (!valid)
                break;
        }
        if (valid && changes.All(changed => changed))
        {
            successful = count;
            selectedKeys = keys;
            break;
        }
    }
    if (selectedKeys == null)
        throw new InvalidOperationException("Arithmetic provenance/coupling failed: exact executed binding/state reads and complete successful counterfactual outputs required.");
    var wider = 0;
    if (name == "ComputeWithTemp")
    {
        // A representable product with an unrepresentable signed Int32 sum or
        // difference is necessarily zero for integer a,b. Do not turn a checked
        // Int32 intermediate overflow into admission or coupling evidence.
        var wideBindings = selectedKeys.All(key => rewriter.HasWideInitializer(key, initialEvents));
        var checkedArithmetic = mapped.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == name).DescendantNodes()
            .Any(node => node is CheckedExpressionSyntax checkedExpression
                && checkedExpression.Keyword.IsKind(SyntaxKind.CheckedKeyword));
        if (wideBindings || !checkedArithmetic)
            foreach (var input in ArithmeticWideCases())
            {
                Check(baseline, input, "wider-intermediate original");
                Check(off, input, "wider-intermediate contract-disabled");
                Reset();
                Check(traced, input, "wider-intermediate instrumented");
                wider++;
            }
    }
    Console.WriteLine($"Verification passed: {name}; native/CLR signature; {cases.Length} representable runtime/state cases; "
        + $"{successful} complete-case binding counterfactuals; {wider} wider-intermediate cases.");
    Console.WriteLine("Limits: exact representable-domain entry/output contract proof; finite native polynomial/read/write/state provenance, "
        + "not a universal body proof. Core requires signed Int32 mathematical sum, difference and product, or n,2n,3n. "
        + "No invented overflow policy outside that domain. Wider-intermediate cases are six zero-product boundaries, "
        + "run only for both Int64 bindings with widened initializer evaluation or unchecked emitted arithmetic; "
        + "checked narrow intermediates are excluded, not accepted exceptions. "
        + "Provenance supports selected-public polynomial arithmetic/casts/aliases and mapped executed loop/branch statements; helper-call result dependence, "
        + "raw interop, local functions and unsupported proof fragments fail explicitly; no unknown-proof fallback.");
}

static IEnumerable<int[]> ArithmeticCases(string name)
{
    if (name == "Accumulate")
    {
        foreach (var n in new[] { -715827882, -715827881, -1000000, -46341, -31, -3, -2, -1,
            0, 1, 2, 3, 31, 46341, 1000000, 715827881, 715827882 })
            yield return [n];
        yield break;
    }
    var seen = new HashSet<(int, int)>();
    foreach (var a in new[] { -46340, -1024, -31, -2, -1, 0, 1, 2, 31, 1024, 46340 })
        foreach (var b in new[] { -46340, -1024, -31, -2, -1, 0, 1, 2, 31, 1024, 46340 })
            if (seen.Add((a, b)))
                yield return [a, b];
    foreach (var pair in new[] { (1073741824, 1073741823), (1073741823, 1073741824),
        (-1073741824, -1073741823), (-1073741823, -1073741824),
        (1073741824, -1073741823), (-1073741824, 1073741823),
        (-1073741823, 1073741824), (1073741823, -1073741824),
        (46341, 46340), (46340, 46341), (-46341, 46340), (46340, -46341) })
        if (seen.Add(pair))
            yield return [pair.Item1, pair.Item2];
}

static IEnumerable<int[]> ArithmeticWideCases()
{
    foreach (var pair in new[] { (int.MaxValue, int.MaxValue), (int.MinValue, int.MinValue),
        (int.MaxValue, -int.MaxValue), (-int.MaxValue, int.MaxValue),
        (1073741824, 1073741824), (-1073741824, -1073741824) })
        yield return [pair.Item1, pair.Item2];
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

static void VerifyScalarPatterns(string profile, string path)
{
    var name = profile switch
    {
        "pm001" => "DayName", "pm002" => "ScoreCategory",
        "pm003" => "DescribeNumber", "pm006" => "IsSingleDigit",
        _ => throw new InvalidOperationException("Unknown scalar pattern profile.")
    };
    var boolean = profile == "pm006";
    object Expected(int value) => profile switch
    {
        "pm001" => value is >= 1 and <= 5 ? 100 : value is 6 or 7 ? 200 : 0,
        "pm002" => value >= 90 ? 3 : value >= 70 ? 2 : value >= 50 ? 1 : 0,
        "pm003" => value > 0 ? 1 : value < 0 ? -1 : 0,
        "pm006" => value is >= 0 and <= 9,
        _ => throw new InvalidOperationException("Unknown pattern oracle.")
    };
    IEnumerable<AstNode> Nodes(AstNode node)
    {
        yield return node;
        foreach (var child in RecursiveAstWalker.GetAllChildren(node))
            foreach (var descendant in Nodes(child))
                yield return descendant;
    }
    var compiled = Calor.Compiler.Program.Compile(File.ReadAllText(path), path,
        new Calor.Compiler.CompilationOptions { ContractMode = ContractMode.Debug });
    if (compiled.HasErrors || compiled.Ast == null)
        throw new InvalidOperationException($"Calor compilation failed:\n{string.Join("\n", compiled.Diagnostics)}");
    var module = compiled.Ast;
    if (Nodes(module).Any(node => node is CSharpInteropBlockNode or RawCSharpNode or RawCSharpExpressionNode))
        throw new InvalidOperationException("Raw interop cannot establish native pattern behavior.");
    var targets = module.Functions.Where(candidate => candidate.Name == name).ToArray();
    if (targets.Length != 1)
        throw new InvalidOperationException($"Expected exactly one native module function named {name}.");
    var function = targets[0];
    if (function.Visibility != Visibility.Public || function.IsAsync || function.TypeParameters.Count != 0
        || function.Parameters.Count != 1
        || function.Parameters[0].Modifier != Calor.Compiler.Ast.ParameterModifier.None
        || AttributeHelper.ExpandType(function.Parameters[0].TypeName) != "INT"
        || function.Output == null
        || AttributeHelper.ExpandType(function.Output.TypeName) != (boolean ? "BOOL" : "INT"))
        throw new InvalidOperationException($"Wrong native public scalar signature for {name}.");
    CheckBasicEntryDomain(function, [function.Parameters[0].Name]);
    var aliases = new HashSet<string>(StringComparer.Ordinal) { function.Parameters[0].Name };
    foreach (var binding in function.Body.OfType<BindStatementNode>())
        if (!binding.IsMutable && binding.Initializer is ReferenceNode reference && aliases.Contains(reference.Name))
            aliases.Add(binding.Name);
    var matches = Nodes(function).Where(node => node switch
    {
        MatchExpressionNode match => match.Target is ReferenceNode reference && aliases.Contains(reference.Name),
        MatchStatementNode match when profile == "pm003"
            => match.Target is ReferenceNode reference && aliases.Contains(reference.Name),
        _ => false
    }).ToArray();
    if (matches.Length == 0)
        throw new InvalidOperationException("Required native input-dependent switch missing.");
    var values = Enumerable.Range(-512, 1025).Concat(
        [int.MinValue, int.MinValue + 1, int.MaxValue - 1, int.MaxValue, -65536, 65536]).ToHashSet();
    foreach (var literal in Nodes(function).OfType<IntLiteralNode>())
    {
        var value = (long)literal.Value;
        if (value >= int.MinValue && value <= int.MaxValue)
        {
            values.Add((int)value);
            if (value > int.MinValue) values.Add((int)value - 1);
            if (value < int.MaxValue) values.Add((int)value + 1);
        }
    }
    var inputs = values.Order().ToArray();
    MethodInfo Method(Assembly assembly)
    {
        var type = assembly.GetType($"{module.Name}.{module.Name}Module")
            ?? throw new InvalidOperationException("Generated pattern module missing.");
        var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.Static, [typeof(int)])
            ?? throw new InvalidOperationException("Generated exact public pattern signature missing.");
        if (!type.IsPublic || method.IsGenericMethod || method.ReturnType != (boolean ? typeof(bool) : typeof(int)))
            throw new InvalidOperationException("Wrong reflected pattern signature.");
        return method;
    }
    void Behavior(MethodInfo method)
    {
        foreach (var input in inputs)
            if (!Equals(method.Invoke(null, [input]), Expected(input)))
                throw new InvalidOperationException($"Wrong {name} output for {input}.");
    }
    var (original, trees) = CompileRuntime(compiled.GeneratedCode);
    Behavior(Method(original));
    var options = new CSharpParseOptions(LanguageVersion.Preview);
    var debug = CSharpSyntaxTree.ParseText(
        new CSharpEmitter(ContractMode.Debug) { LineDirectiveFilePath = path }.Emit(module), options);
    if (!SyntaxFactory.AreEquivalent(debug.GetRoot(), trees[1].GetRoot()))
        throw new InvalidOperationException("Mapped selected pattern emission differs from compiled C#.");
    var off = CSharpSyntaxTree.ParseText(
        new CSharpEmitter(ContractMode.Off) { LineDirectiveFilePath = path }.Emit(module), options).GetCompilationUnitRoot();
    Behavior(Method(CompileRuntime(off.ToFullString()).Assembly));
    var declaration = off.DescendantNodes().OfType<ClassDeclarationSyntax>()
        .Single(type => type.Identifier.ValueText == module.Name + "Module")
        .Members.OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == name);
    bool Default(MatchCaseNode arm) => arm.Pattern is WildcardPatternNode
        && (arm.Guard == null || arm.Guard is BoolLiteralNode { Value: true });
    var accepted = 0;
    foreach (var match in matches)
    {
        var cases = match is MatchExpressionNode expression ? expression.Cases : ((MatchStatementNode)match).Cases;
        var nativeRequired = profile switch
        {
            "pm001" => Enumerable.Range(1, 7).All(value =>
                cases.Any(arm => Nodes(arm.Pattern).OfType<LiteralPatternNode>()
                    .Any(pattern => pattern.Literal is IntLiteralNode literal && literal.Value == value))),
            "pm006" => Enumerable.Range(0, 10).All(value =>
                cases.Any(arm => Nodes(arm.Pattern).OfType<LiteralPatternNode>()
                    .Any(pattern => pattern.Literal is IntLiteralNode literal && literal.Value == value))),
            "pm002" => cases.Count(arm => Nodes(arm.Pattern).Any(node => node is RelationalPatternNode)) >= 3,
            "pm003" => cases.Count(arm => arm.Guard != null && arm.Pattern is VarPatternNode or VariablePatternNode) >= 2,
            _ => false
        };
        if (!nativeRequired || !cases.Any(Default))
            continue;
        var emitted = declaration.DescendantNodes().Where(node =>
            (match is MatchExpressionNode && node is SwitchExpressionSyntax
                || match is MatchStatementNode && node is SwitchStatementSyntax)
            && node.GetLocation().GetMappedLineSpan().Path == path
            && node.GetLocation().GetMappedLineSpan().StartLinePosition.Line + 1 == match.Span.Line).ToArray();
        if (emitted.Length != 1)
            throw new InvalidOperationException("Ambiguous native-to-emitted pattern witness.");
        var selected = emitted[0];
        var probeName = "PatternTrace_" + Guid.NewGuid().ToString("N");
        ExpressionSyntax Observe(ExpressionSyntax value, int index) => SyntaxFactory.ParseExpression(
            $"{probeName}.Observe({index}, ({value.ToFullString()}))");
        SyntaxNode traced;
        ExpressionSyntax governing;
        if (selected is SwitchExpressionSyntax switchExpression)
        {
            if (switchExpression.Arms.Count != cases.Count)
                throw new InvalidOperationException("Native/emitted pattern arm count differs.");
            traced = switchExpression.WithArms(SyntaxFactory.SeparatedList(switchExpression.Arms.Select((arm, index) =>
                arm.WithExpression(Observe(arm.Expression, index)))));
            governing = switchExpression.GoverningExpression;
        }
        else
        {
            var statement = (SwitchStatementSyntax)selected;
            if (statement.Sections.Count != cases.Count)
                throw new InvalidOperationException("Native/emitted pattern section count differs.");
            traced = statement.WithSections(SyntaxFactory.List(statement.Sections.Select((section, index) =>
                section.ReplaceNodes(section.DescendantNodes().OfType<ReturnStatementSyntax>()
                    .Where(returned => returned.Expression != null),
                    (returned, _) => returned.WithExpression(Observe(returned.Expression!, index))))));
            governing = statement.Expression;
        }
        var traceType = SyntaxFactory.ParseMemberDeclaration(
            $"internal static class {probeName} {{ public static int Arm = -1; public static object? Value; "
            + "public static T Observe<T>(int arm, T value) { Arm = arm; Value = value; return value; } }")
            ?? throw new InvalidOperationException("Controlled pattern trace declaration failed.");
        var instrumented = off.ReplaceNode(selected, traced).AddMembers(traceType);
        var traceAssembly = CompileRuntime(instrumented.ToFullString()).Assembly;
        var traceMethod = Method(traceAssembly);
        var trace = traceAssembly.GetType(probeName)
            ?? throw new InvalidOperationException("Controlled pattern trace type missing.");
        var armField = trace.GetField("Arm") ?? throw new InvalidOperationException("Pattern arm trace missing.");
        var valueField = trace.GetField("Value") ?? throw new InvalidOperationException("Pattern value trace missing.");
        var hits = new HashSet<int>();
        var suppliesRequiredOutput = true;
        foreach (var input in inputs)
        {
            armField.SetValue(null, -1);
            valueField.SetValue(null, null);
            if (!Equals(traceMethod.Invoke(null, [input]), Expected(input)))
                throw new InvalidOperationException("Pattern instrumentation changed the original output.");
            suppliesRequiredOutput &= Equals(valueField.GetValue(null), Expected(input));
            var index = (int)(armField.GetValue(null) ?? throw new InvalidOperationException("Missing pattern arm trace."));
            if (index < 0 || index >= cases.Count)
                suppliesRequiredOutput = false;
            else
                hits.Add(index);
        }
        if (!suppliesRequiredOutput)
            continue;
        if (!hits.Any(index => Default(cases[index])))
            continue;
        var literalCoverage = true;
        if (profile is "pm001" or "pm006")
            foreach (var value in Enumerable.Range(profile == "pm001" ? 1 : 0, profile == "pm001" ? 7 : 10))
                if (!hits.Any(index => Nodes(cases[index].Pattern).OfType<LiteralPatternNode>()
                    .Any(pattern => pattern.Literal is IntLiteralNode literal && literal.Value == value)))
                    literalCoverage = false;
        if (!literalCoverage)
            continue;
        if (profile == "pm002" && hits.Count(index => Nodes(cases[index].Pattern).Any(node => node is RelationalPatternNode)) < 3)
            continue;
        if (profile == "pm003")
        {
            var guarded = hits.Where(index => cases[index].Guard != null
                && cases[index].Pattern is VarPatternNode or VariablePatternNode).ToArray();
            if (guarded.Length < 2 || guarded.Any(index =>
            {
                var capture = cases[index].Pattern is VarPatternNode variable ? variable.Name : ((VariablePatternNode)cases[index].Pattern).Name;
                return !Nodes(cases[index].Guard!).OfType<ReferenceNode>().Any(reference => reference.Name == capture);
            }))
                continue;
        }
        var flipped = off.ReplaceNode(governing, SyntaxFactory.ParseExpression($"~({governing.ToFullString()})"));
        var counterfactual = Method(CompileRuntime(flipped.ToFullString()).Assembly);
        var coupled = true;
        foreach (var input in inputs)
            coupled &= Equals(counterfactual.Invoke(null, [input]), Expected(~input));
        if (coupled)
            accepted++;
    }
    if (accepted == 0)
        throw new InvalidOperationException("Required native scalar pattern constructs missing or do not supply the output.");
    Console.WriteLine($"Verification passed: {name}; {inputs.Length} independent runtime cases, actual native arms and successful input/output coupling.");
}

static IEnumerable<AstNode> LambdaNodes(AstNode node)
{
    yield return node;
    foreach (var child in RecursiveAstWalker.GetAllChildren(node))
        foreach (var descendant in LambdaNodes(child))
            yield return descendant;
}

static void VerifyLambdaDelegate(string profile, string path)
{
    var source = File.ReadAllText(path);
    var compiled = Calor.Compiler.Program.Compile(source, path, new Calor.Compiler.CompilationOptions
    {
        ContractMode = ContractMode.Debug,
        UnknownCallPolicy = profile == "lam003" ? UnknownCallPolicy.Strict : UnknownCallPolicy.Permissive,
        VerifyContracts = false
    });
    if (compiled.HasErrors || compiled.Ast == null)
        throw new InvalidOperationException($"Calor compilation failed:\n{string.Join("\n", compiled.Diagnostics)}");
    var module = compiled.Ast;
    RejectRawInterop(module, "lambda/delegate/event admission");
    var previous = Console.Out;
    using var output = new BoundedOutput();
    try
    {
        Console.SetOut(output);
        var (assembly, trees) = CompileRuntime(compiled.GeneratedCode);
        var options = new CSharpParseOptions(LanguageVersion.Preview);
        var mapped = CSharpSyntaxTree.ParseText(
            new CSharpEmitter(ContractMode.Debug) { LineDirectiveFilePath = path }.Emit(module), options);
        if (!SyntaxFactory.AreEquivalent(trees[1].GetRoot(), mapped.GetRoot()))
            throw new InvalidOperationException("Native lambda/delegate/event mapping differs from compiled emission.");
        var debugMapping = CSharpCompilation.Create("LambdaDebugMapping",
            [CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble, options), mapped],
            GeneratedCSharpCompiler.References, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var debugSemantic = debugMapping.GetSemanticModel(mapped);
        var debugRoot = mapped.GetCompilationUnitRoot();
        var name = profile switch { "lam001" => "ApplyDouble", "lam002" => "ApplyComplex", _ => "Subscribe" };
        if (profile != "lam003")
        {
            foreach (var method in debugRoot.DescendantNodes().OfType<MethodDeclarationSyntax>().Where(method =>
                method.Identifier.ValueText == name && method.Parent is ClassDeclarationSyntax container
                && container.Identifier.ValueText == (profile == "lam004" ? "EventPublisher" : module.Name + "Module")))
                RefuseLambdaExternalExecution(method, debugSemantic);
            if (profile == "lam004")
                foreach (var member in debugRoot.DescendantNodes().OfType<ClassDeclarationSyntax>()
                    .Where(type => type.Identifier.ValueText == "EventPublisher").SelectMany(type => type.Members)
                    .Where(member => member is ConstructorDeclarationSyntax or EventDeclarationSyntax))
                    RefuseLambdaExternalExecution(member, debugSemantic);
        }
        // Execute original runtime guards above; interventions must not be
        // obstructed by contracts that correctly describe the unmodified task.
        mapped = CSharpSyntaxTree.ParseText(
            new CSharpEmitter(ContractMode.Off) { LineDirectiveFilePath = path }.Emit(module), options);
        var mapping = CSharpCompilation.Create("LambdaMapping",
            [CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble, options), mapped],
            GeneratedCSharpCompiler.References, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semantic = mapping.GetSemanticModel(mapped);
        var root = mapped.GetCompilationUnitRoot();
        if (profile == "lam003")
            VerifyNativeDelegate(module, assembly, root, semantic, path);
        else if (profile == "lam004")
            VerifyNativeEvent(module, assembly, root, semantic, path);
        else
            VerifyNativeLambda(profile, module, assembly, root, semantic, path);
    }
    finally
    {
        Console.SetOut(previous);
    }
}

static int[] LambdaInputs(bool block) => block
    ? [int.MinValue, int.MinValue + 1, -1073741825, -1073741824, -65536, -2, -1, 0, 1, 2, 3, 65536, 1073741822, 1073741823]
    : [-1073741824, -1073741823, -65536, -2, -1, 0, 1, 2, 3, 65535, 65536, 1073741822, 1073741823];

static bool LambdaIntegerType(string name) =>
    AttributeHelper.ExpandType(name) == AttributeHelper.ExpandType("i32") || name == "System.Int32";

static void VerifyNativeLambda(string profile, ModuleNode module, Assembly original,
    CompilationUnitSyntax root, SemanticModel semantic, string path)
{
    var block = profile == "lam002";
    var name = block ? "ApplyComplex" : "ApplyDouble";
    var functions = module.Functions.Where(function => function.Name == name).ToArray();
    if (functions.Length > 1)
        throw new InvalidOperationException("Bounded unsupported lambda scope: overloaded target identity requires disambiguation.");
    if (functions.Length != 1)
        throw new InvalidOperationException($"Expected exactly one native function named {name}.");
    var function = functions[0];
    if (function.TypeParameters.Count != 0)
        throw new InvalidOperationException("Bounded unsupported lambda scope: generic function requires type arguments.");
    if (function.Visibility != Visibility.Public || function.IsAsync
        || function.Parameters is not [var parameter] || parameter.Name != "x"
        || parameter.Modifier != Calor.Compiler.Ast.ParameterModifier.None
        || !LambdaIntegerType(parameter.TypeName)
        || function.Output == null || !LambdaIntegerType(function.Output.TypeName))
        throw new InvalidOperationException($"Wrong native public lambda function signature for {name}.");
    MethodInfo Target(Assembly assembly)
    {
        var type = assembly.GetType($"{module.Name}.{module.Name}Module")
            ?? throw new InvalidOperationException("Missing native lambda module CLR type.");
        var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.Static, [typeof(int)])
            ?? throw new InvalidOperationException("Wrong CLR lambda function signature.");
        if (!type.IsPublic || method.IsGenericMethod || method.ReturnType != typeof(int)
            || method.GetParameters()[0].Name != "x")
            throw new InvalidOperationException("Wrong CLR lambda function signature.");
        return method;
    }
    var inputs = LambdaInputs(block);
    int Expected(int x) => block && x <= 0 ? 0 : checked(x * 2);
    int Invoke(MethodInfo method, int x) => (int)(method.Invoke(null, [x])
        ?? throw new InvalidOperationException("Null native lambda output."));
    var declaration = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method =>
        method.Identifier.ValueText == name && method.Parent is ClassDeclarationSyntax container
        && container.Identifier.ValueText == module.Name + "Module");
    RefuseLambdaExternalExecution(declaration, semantic);
    foreach (var x in inputs)
        if (Invoke(Target(original), x) != Expected(x))
            throw new InvalidOperationException($"Wrong native lambda output for {name}({x}).");
    var candidates = LambdaNodes(function).OfType<BindStatementNode>()
        .Where(binding => binding.Initializer is LambdaExpressionNode lambda && !lambda.IsAsync
            && lambda.Parameters.Count == 1 && (block
                ? lambda.StatementBody is { Count: >= 2 }
                : lambda.ExpressionBody != null)).ToArray();
    var admitted = 0;
    var counterfactuals = 0;
    var capturesChecked = 0;
    foreach (var binding in candidates)
    {
        var native = (LambdaExpressionNode)binding.Initializer!;
        var emitted = declaration.DescendantNodes().OfType<VariableDeclaratorSyntax>()
            .Where(variable => variable.Identifier.ValueText == binding.Name
                && variable.Initializer?.Value is LambdaExpressionSyntax lambda
                && lambda.GetLocation().GetMappedLineSpan().Path == path
                && lambda.GetLocation().GetMappedLineSpan().StartLinePosition.Line + 1 == native.Span.Line).ToArray();
        if (emitted.Length != 1 || emitted[0].Initializer?.Value is not LambdaExpressionSyntax selected
            || semantic.GetDeclaredSymbol(emitted[0]) is not ILocalSymbol local
            || local.Type is not INamedTypeSymbol { TypeKind: TypeKind.Delegate, DelegateInvokeMethod: var invoke }
            || invoke == null || invoke.Parameters is not [var argument]
            || argument.Type.SpecialType != SpecialType.System_Int32 || invoke.ReturnType.SpecialType != SpecialType.System_Int32)
            throw new InvalidOperationException("Ambiguous native/bound/emitted lambda symbol or non-Int32 delegate.");
        var lambdaParameter = selected switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Parameter,
            ParenthesizedLambdaExpressionSyntax parenthesized when parenthesized.ParameterList.Parameters.Count == 1
                => parenthesized.ParameterList.Parameters[0],
            _ => throw new InvalidOperationException("Wrong emitted native lambda parameter count.")
        };
        if (lambdaParameter.Identifier.ValueText != native.Parameters[0].Name
            || block != (selected.Body is BlockSyntax))
            throw new InvalidOperationException("Native/emitted lambda body or parameter mapping differs.");
        var captureSymbols = selected.DescendantNodes().OfType<IdentifierNameSyntax>()
            .Select(identifier => semantic.GetSymbolInfo(identifier).Symbol)
            .Where(symbol => symbol is ILocalSymbol or IParameterSymbol
                && symbol.DeclaringSyntaxReferences.All(reference => !selected.Span.Contains(reference.Span)))
            .Distinct(SymbolEqualityComparer.Default).ToArray();
        if (captureSymbols.Any(symbol => symbol switch
            {
                ILocalSymbol captured => captured.Type.SpecialType != SpecialType.System_Int32,
                IParameterSymbol captured => captured.Type.SpecialType != SpecialType.System_Int32,
                _ => true
            }))
            throw new InvalidOperationException("Bounded unsupported lambda scope: non-Int32 capture.");
        if (selected.DescendantNodes().OfType<LambdaExpressionSyntax>().Any())
            throw new InvalidOperationException("Bounded unsupported lambda scope: nested lambda.");
        var probe = "LambdaTrace_" + Guid.NewGuid().ToString("N");
        var rewriter = new LambdaAdmissionRewriter(semantic, captureSymbols, probe);
        var body = (CSharpSyntaxNode)(rewriter.Visit(selected.Body)
            ?? throw new InvalidOperationException("Missing rewritten native lambda body."));
        var prefix = SyntaxFactory.ParseStatement(
            $"{{ {probe}.Start({lambdaParameter.Identifier.Text}); {lambdaParameter.Identifier.Text} = {probe}.Argument({lambdaParameter.Identifier.Text}); }}") as BlockSyntax
            ?? throw new InvalidOperationException("Missing lambda entry intervention.");
        BlockSyntax tracedBody;
        if (body is BlockSyntax statements)
            tracedBody = statements.WithStatements(statements.Statements.InsertRange(0, prefix.Statements));
        else
            tracedBody = prefix.AddStatements(SyntaxFactory.ParseStatement($"return {probe}.Output(({body.ToFullString()}));"));
        var traced = selected switch
        {
            SimpleLambdaExpressionSyntax simple => (LambdaExpressionSyntax)simple.WithBody(tracedBody),
            ParenthesizedLambdaExpressionSyntax parenthesized => parenthesized.WithBody(tracedBody),
            _ => throw new InvalidOperationException("Missing lambda intervention form.")
        };
        var traceClass = SyntaxFactory.ParseMemberDeclaration(
            $"public static class {probe} {{ public static int Mode, Replacement, Calls, Arg, Result, CaptureIndex;"
            + $" public static int[] Captures = new int[{captureSymbols.Length}];"
            + " public static void Start(int arg) { Calls++; Arg=arg; }"
            + " public static int Argument(int arg) => Mode==1 ? Replacement : arg;"
            + " public static int Capture(int index,int value) { Captures[index]=value; return Mode==3 && index==CaptureIndex ? Replacement : value; }"
            + " public static int Output(int value) { Result=Mode==2 ? Replacement : value; return Result; } }")
            ?? throw new InvalidOperationException("Missing lambda intervention type.");
        var instrumented = CompileRuntime(root.ReplaceNode(selected, traced).AddMembers(traceClass)
            .NormalizeWhitespace().ToFullString()).Assembly;
        var target = Target(instrumented);
        var trace = instrumented.GetType(probe) ?? throw new InvalidOperationException("Missing lambda trace CLR identity.");
        FieldInfo Field(string field) => trace.GetField(field) ?? throw new InvalidOperationException("Missing lambda trace field.");
        var validArgument = true;
        int Run(int x, int mode, int replacement, int capture = -1)
        {
            Field("Mode").SetValue(null, mode);
            Field("Replacement").SetValue(null, replacement);
            Field("CaptureIndex").SetValue(null, capture);
            Field("Calls").SetValue(null, 0);
            var result = Invoke(target, x);
            if ((int)Field("Calls").GetValue(null)! == 0 || (int)Field("Arg").GetValue(null)! != x)
                validArgument = false;
            return result;
        }
        var supplying = true;
        var candidateCounterfactuals = 0;
        var candidateCaptures = 0;
        foreach (var x in inputs)
        {
            if (Run(x, 0, 0) != Expected(x) || (int)Field("Result").GetValue(null)! != Expected(x))
                supplying = false;
            foreach (var y in inputs)
            {
                if (Run(x, 1, y) != Expected(y) || (int)Field("Result").GetValue(null)! != Expected(y))
                    supplying = false;
                candidateCounterfactuals++;
            }
            if (Run(x, 2, ~Expected(x)) != ~Expected(x))
                supplying = false;
            candidateCounterfactuals++;
            Run(x, 0, 0);
            var observed = ((int[])Field("Captures").GetValue(null)!).ToArray();
            for (var index = 0; index < observed.Length; index++)
            {
                foreach (var replacement in new[] { observed[index], 0, 1 })
                {
                    if (Run(x, 3, replacement, index) != (int)Field("Result").GetValue(null)!)
                        supplying = false;
                    candidateCaptures++;
                }
            }
        }
        if (supplying && validArgument)
        {
            admitted++;
            counterfactuals += candidateCounterfactuals;
            capturesChecked += candidateCaptures;
        }
    }
    if (admitted == 0)
        throw new InvalidOperationException("Required native bound/called supplying lambda missing or complete argument/output/capture counterfactual failed.");
    Console.Error.WriteLine($"Verification passed: {name}; {inputs.Length} independent representable signed/boundary cases; "
        + $"{counterfactuals} successful complete-case argument/output interventions; {capturesChecked} capture interventions; native/CLR mapping.");
}

static void RefuseLambdaExternalExecution(SyntaxNode declaration, SemanticModel semantic)
{
    foreach (var expression in declaration.DescendantNodes().OfType<ExpressionSyntax>())
        if (semantic.GetSymbolInfo(expression).Symbol is IPropertySymbol
            || semantic.GetSymbolInfo(expression).Symbol is IFieldSymbol { IsStatic: true } field
                && !(field.ContainingType.ToDisplayString() == "System.EventArgs" && field.Name == "Empty")
                && field.ContainingType.ToDisplayString() != "Calor.Runtime.ContractKind")
            throw new InvalidOperationException("Bounded unsupported lambda/event scope: property or external static state.");
    foreach (var call in declaration.DescendantNodes().OfType<InvocationExpressionSyntax>())
        if (semantic.GetSymbolInfo(call).Symbol is not IMethodSymbol { MethodKind: MethodKind.DelegateInvoke })
            throw new InvalidOperationException("Bounded unsupported lambda/event scope: non-delegate call in executed declaration.");
    foreach (var allocation in declaration.DescendantNodes().Where(node =>
        node is ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax))
        if (semantic.GetTypeInfo(allocation).Type?.ToDisplayString() != "Calor.Runtime.ContractViolationException")
            throw new InvalidOperationException("Bounded unsupported lambda/event scope: allocation in executed declaration.");
}

static void VerifyNativeDelegate(ModuleNode module, Assembly assembly, CompilationUnitSyntax root,
    SemanticModel semantic, string path)
{
    var declarations = module.Delegates.Where(declaration => declaration.Name == "MathOperation").ToArray();
    if (declarations.Length != 1)
        throw new InvalidOperationException("Expected exactly one native MathOperation delegate.");
    var native = declarations[0];
    if (!native.Parameters.Select(parameter => parameter.Name).SequenceEqual(new[] { "a", "b" })
        || native.Parameters.Any(parameter => parameter.Modifier != Calor.Compiler.Ast.ParameterModifier.None
            || !LambdaIntegerType(parameter.TypeName))
        || native.Output == null || !LambdaIntegerType(native.Output.TypeName))
        throw new InvalidOperationException("Wrong native MathOperation delegate signature.");
    var syntax = root.DescendantNodes().OfType<DelegateDeclarationSyntax>()
        .Where(declaration => declaration.Identifier.ValueText == native.Name).ToArray();
    var type = assembly.GetType($"{module.Name}.MathOperation");
    if (syntax.Length != 1
        || semantic.GetDeclaredSymbol(syntax[0]) is not INamedTypeSymbol { TypeKind: TypeKind.Delegate } symbol
        || symbol.DelegateInvokeMethod == null
        || !symbol.DelegateInvokeMethod.Parameters.Select(parameter => parameter.Name)
            .SequenceEqual(native.Parameters.Select(parameter => parameter.Name))
        || type == null || !type.IsPublic || type.IsGenericType || type.BaseType != typeof(MulticastDelegate))
        throw new InvalidOperationException("Wrong native/mapped/public nongeneric CLR multicast delegate.");
    var invoke = type.GetMethod("Invoke") ?? throw new InvalidOperationException("Missing delegate Invoke.");
    if (invoke.ReturnType != typeof(int) || !invoke.GetParameters().Select(parameter => parameter.ParameterType)
            .SequenceEqual(new[] { typeof(int), typeof(int) })
        || !invoke.GetParameters().Select(parameter => parameter.Name).SequenceEqual(new[] { "a", "b" }))
        throw new InvalidOperationException("Wrong CLR MathOperation Invoke signature/declared parameters.");
    int[] inputs = [int.MinValue, int.MinValue + 1, -65536, -2, -1, 0, 1, 2, 3, 65536, int.MaxValue - 1, int.MaxValue];
    var cases = 0;
    var observations = new List<(string, int, int)>();
    Func<int, int, int> first = (a, b) => { observations.Add(("first", a, b)); return a; };
    Func<int, int, int> second = (a, b) => { observations.Add(("second", a, b)); return b; };
    var left = Delegate.CreateDelegate(type, first.Target, first.Method);
    var right = Delegate.CreateDelegate(type, second.Target, second.Method);
    foreach (var a in inputs)
        foreach (var b in inputs)
            foreach (var (candidate, labels, expected) in new[]
                { (left, new[] { "first" }, a), (right, new[] { "second" }, b),
                  (Delegate.Combine(left, right, left)!, new[] { "first", "second", "first" }, a) })
            {
                observations.Clear();
                if ((int)candidate.DynamicInvoke(a, b)! != expected
                    || !observations.SequenceEqual(labels.Select(label => (label, a, b)))
                    || candidate.GetInvocationList().Length != labels.Length)
                    throw new InvalidOperationException("Wrong independently bound native CLR delegate invocation/order/arguments.");
                cases++;
            }
    Console.Error.WriteLine($"Verification passed: MathOperation; {cases} independent CLR delegate binding/invocation cases; native/CLR mapping.");
}

static void VerifyNativeEvent(ModuleNode module, Assembly original, CompilationUnitSyntax root,
    SemanticModel semantic, string path)
{
    var classes = module.Classes.Where(type => type.Name == "EventPublisher").ToArray();
    if (classes.Length != 1 || classes[0].Visibility != Visibility.Public)
        throw new InvalidOperationException("Expected exactly one native public EventPublisher class.");
    var native = classes[0];
    if (native.TypeParameters.Count != 0)
        throw new InvalidOperationException("Bounded unsupported event scope: generic publisher requires type arguments.");
    var events = native.Events.Where(member => member.Name == "OnDataReceived").ToArray();
    var methods = native.Methods.Where(member => member.Name == "Subscribe").ToArray();
    if (methods.Length > 1)
        throw new InvalidOperationException("Bounded unsupported event scope: overloaded Subscribe identity requires disambiguation.");
    if (events.Length != 1 || events[0].Visibility != Visibility.Public || methods.Length != 1
        || methods[0].Visibility != Visibility.Public || methods[0].IsAsync
        || methods[0].Parameters.Count != 1 || methods[0].Parameters[0].Modifier != Calor.Compiler.Ast.ParameterModifier.None)
        throw new InvalidOperationException("Wrong native public EventPublisher event/Subscribe signature.");
    if (methods[0].TypeParameters.Count != 0)
        throw new InvalidOperationException("Bounded unsupported event scope: generic Subscribe requires type arguments.");
    var declaration = root.DescendantNodes().OfType<ClassDeclarationSyntax>()
        .Single(type => type.Identifier.ValueText == native.Name);
    var methodSyntax = declaration.Members.OfType<MethodDeclarationSyntax>()
        .Single(method => method.Identifier.ValueText == "Subscribe");
    var eventSyntax = declaration.Members.Where(member => member is EventDeclarationSyntax evt
        && evt.Identifier.ValueText == "OnDataReceived" || member is EventFieldDeclarationSyntax field
        && field.Declaration.Variables.Any(variable => variable.Identifier.ValueText == "OnDataReceived")).Single();
    ISymbol? eventSymbol = eventSyntax is EventDeclarationSyntax custom ? semantic.GetDeclaredSymbol(custom)
        : semantic.GetDeclaredSymbol(((EventFieldDeclarationSyntax)eventSyntax).Declaration.Variables.Single());
    if (eventSymbol is IEventSymbol { IsStatic: true }
        || semantic.GetDeclaredSymbol(methodSyntax) is IMethodSymbol { IsStatic: true })
        throw new InvalidOperationException("Bounded unsupported event scope: static event or Subscribe.");
    if (eventSymbol is not IEventSymbol { Type: var eventType, IsStatic: false }
        || eventType.ToDisplayString().TrimEnd('?') != "System.EventHandler"
        || semantic.GetDeclaredSymbol(methodSyntax) is not IMethodSymbol { Parameters: [var handler] } subscribeSymbol
        || !SymbolEqualityComparer.Default.Equals(handler.Type, eventType))
        throw new InvalidOperationException($"Wrong native/mapped CLR EventHandler event/Subscribe signature: {eventSymbol?.ToDisplayString()} / {semantic.GetDeclaredSymbol(methodSyntax)?.ToDisplayString()}.");
    var nullableHandler = handler.NullableAnnotation == NullableAnnotation.Annotated;
    RefuseLambdaExternalExecution(methodSyntax, semantic);
    foreach (var accessor in eventSyntax.DescendantNodes().OfType<AccessorDeclarationSyntax>())
        RefuseLambdaExternalExecution(accessor, semantic);
    foreach (var constructor in declaration.Members.OfType<ConstructorDeclarationSyntax>())
    {
        RefuseLambdaExternalExecution(constructor, semantic);
        if (constructor.Initializer != null)
            throw new InvalidOperationException("Bounded unsupported event scope: constructor initializer.");
    }
    if (declaration.Members.OfType<FieldDeclarationSyntax>().Any(field =>
        field.Declaration.Variables.Any(variable => variable.Initializer != null)))
        throw new InvalidOperationException("Bounded unsupported event scope: field initializer.");
    (Type Type, EventInfo Event, MethodInfo Subscribe) Target(Assembly assembly)
    {
        var type = assembly.GetType($"{module.Name}.EventPublisher")
            ?? throw new InvalidOperationException("Missing EventPublisher CLR type.");
        var evt = type.GetEvent("OnDataReceived", BindingFlags.Public | BindingFlags.Instance);
        var subscribe = type.GetMethod("Subscribe", BindingFlags.Public | BindingFlags.Instance, [typeof(EventHandler)]);
        if (!type.IsPublic || type.IsGenericType || evt?.EventHandlerType != typeof(EventHandler)
            || evt.AddMethod is not { IsPublic: true, IsStatic: false }
            || evt.RemoveMethod is not { IsPublic: true, IsStatic: false }
            || subscribe == null || subscribe.IsGenericMethod)
            throw new InvalidOperationException("Wrong actual CLR EventHandler event/Subscribe shape.");
        if (type.IsAbstract || type.GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            null, Type.EmptyTypes, null) == null)
            throw new InvalidOperationException("Bounded unsupported event scope: no usable parameterless instance construction.");
        return (type, evt, subscribe);
    }
    int Behavior(Assembly assembly, string? probe = null, int mode = 0)
    {
        var target = Target(assembly);
        var trace = probe == null ? null : assembly.GetType(probe)
            ?? throw new InvalidOperationException("Missing event intervention identity.");
        trace?.GetField("Mode")!.SetValue(null, mode);
        var observed = new List<(int, object?, EventArgs)>();
        EventHandler a = (sender, args) => observed.Add((1, sender, args));
        EventHandler b = (sender, args) => observed.Add((2, sender, args));
        trace?.GetField("Replacement")!.SetValue(null, b);
        var checks = 0;
        foreach (var sequence in new EventHandler?[][] { [a], [a, b, a], [b, a, b, b],
            nullableHandler ? [null, a, null, b] : [a, b, a, b] })
        {
            var instance = Activator.CreateInstance(target.Type, nonPublic: true)
                ?? throw new InvalidOperationException("EventPublisher has no usable parameterless construction.");
            var independent = Activator.CreateInstance(target.Type, nonPublic: true)!;
            var expected = new List<EventHandler> { b };
            target.Event.AddEventHandler(instance, b);
            var fields = target.Type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Where(field => field.FieldType == typeof(EventHandler)).ToArray();
            var backing = fields.Where(field =>
                ((Delegate?)field.GetValue(instance))?.GetInvocationList().SequenceEqual(expected) == true).ToArray();
            if (backing.Length != 1)
                throw new InvalidOperationException("Bounded unsupported event scope: ambiguous authoritative event backing storage.");
            var storage = backing[0];
            foreach (var supplied in sequence)
            {
                observed.Clear();
                target.Subscribe.Invoke(instance, [supplied]);
                if (observed.Count != 0)
                    throw new InvalidOperationException("Event Subscribe immediately invoked a handler.");
                if (mode != 2 && (mode == 1 || supplied != null))
                    expected.Add(mode == 1 ? b : supplied!);
                if (!(((Delegate?)storage.GetValue(instance))?.GetInvocationList() ?? []).SequenceEqual(expected))
                    throw new InvalidOperationException("Wrong actual event subscriber state/order/duplicates/replacement (or bounded ambiguous backing storage).");
                if (target.Type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                    .Where(field => field.FieldType == typeof(EventHandler)).Any(field => field.GetValue(independent) != null))
                    throw new InvalidOperationException("Event subscriber state leaked between instances.");
                var sender = new object();
                var args = new EventArgs();
                ((Delegate)storage.GetValue(instance)!).DynamicInvoke(sender, args);
                var wanted = expected.Select(handler => (handler == a ? 1 : 2, (object?)sender, args));
                if (!observed.SequenceEqual(wanted))
                    throw new InvalidOperationException("Wrong actual event invocation arguments/order/duplicates.");
                checks++;
            }
            target.Event.RemoveEventHandler(instance, b);
            expected.RemoveAt(expected.FindLastIndex(handler => handler == b));
            if (!(((Delegate?)storage.GetValue(instance))?.GetInvocationList() ?? []).SequenceEqual(expected))
                throw new InvalidOperationException("Wrong actual event removal/subscriber preservation.");
            checks++;
        }
        return checks;
    }
    var cases = Behavior(original);
    var subscriptions = LambdaNodes(methods[0]).OfType<EventSubscribeNode>().ToArray();
    var admitted = 0;
    foreach (var subscription in subscriptions)
    {
        var nativeStatement = SyntaxFactory.ParseStatement(new CSharpEmitter(ContractMode.Debug).Visit(subscription));
        var assignments = methodSyntax.DescendantNodes().OfType<AssignmentExpressionSyntax>().Where(assignment =>
            assignment.IsKind(SyntaxKind.AddAssignmentExpression)
            && SyntaxFactory.AreEquivalent(nativeStatement, assignment.Parent)
            && SymbolEqualityComparer.Default.Equals(semantic.GetSymbolInfo(assignment.Left).Symbol, eventSymbol)).ToArray();
        if (assignments.Length != 1)
            throw new InvalidOperationException("Ambiguous native EventSubscribe/emitted event symbol mapping.");
        var selected = assignments[0];
        var probe = "EventTrace_" + Guid.NewGuid().ToString("N");
        var replacement = SyntaxFactory.ParseExpression(
            $"{probe}.Mode==2 ? 0 : {probe}.Apply(() => {{ {selected.Left.WithoutTrivia().ToFullString()} += {probe}.Mode==1 ? {probe}.Replacement : ({selected.Right.WithoutTrivia().ToFullString()}); }})");
        var traceClass = SyntaxFactory.ParseMemberDeclaration(
            $"public static class {probe} {{ public static int Mode; public static EventHandler Replacement=null!;"
            + " public static int Apply(Action action) { action(); return 0; } }")!;
        var statement = selected.Parent as ExpressionStatementSyntax
            ?? throw new InvalidOperationException("Bounded unsupported event scope: non-statement subscription.");
        var rewritten = SyntaxFactory.ParseStatement($"_ = ({replacement.ToFullString()});").WithTriviaFrom(statement);
        var assembly = CompileRuntime(root.ReplaceNode(statement, rewritten).AddMembers(traceClass)
            .NormalizeWhitespace().ToFullString()).Assembly;
        Behavior(assembly, probe);
        Behavior(assembly, probe, 1);
        Behavior(assembly, probe, 2);
        admitted++;
    }
    if (admitted == 0)
        throw new InvalidOperationException("Required native supplying EventSubscribe missing.");
    Console.Error.WriteLine($"Verification passed: EventPublisher; {cases} actual event state/invocation cases; "
        + $"{cases * 2 * admitted} successful complete-case handler/neutralization interventions; native/CLR mapping.");
}

static void VerifyAsync(string profile, string path)
{
    var name = profile switch
    {
        "async001" => "GetDataAsync", "async002" => "ProcessAsync",
        "async003" => "BackgroundProcessAsync", _ => "LoadAsync"
    };
    var helperName = profile == "async002" ? "FetchDataAsync" : "SlowOperationAsync";
    var http = profile == "async004";
    var source = File.ReadAllText(path);
    var diagnostics = new DiagnosticBag();
    var parsed = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
    if (diagnostics.HasErrors)
        throw new InvalidOperationException($"Calor parsing failed:\n{string.Join("\n", diagnostics)}");
    void NativeOnly(AstNode node)
    {
        if (node is CSharpInteropBlockNode or RawCSharpNode or RawCSharpExpressionNode)
            throw new InvalidOperationException("Async admission requires native Calor, not raw C#.");
        foreach (var child in RecursiveAstWalker.GetAllChildren(node))
            NativeOnly(child);
    }
    NativeOnly(parsed);
    AstNode declaration;
    IReadOnlyList<StatementNode> body;
    OutputNode? output;
    EffectsNode? effects;
    bool signature;
    if (http)
    {
        var classes = parsed.Classes.Where(candidate => candidate.Name == "DataService").ToArray();
        if (classes.Length != 1 || classes[0].Visibility != Visibility.Public || classes[0].TypeParameters.Count != 0)
            throw new InvalidOperationException("Expected exactly one native public DataService class.");
        var methods = classes[0].Methods.Where(candidate => candidate.Name == name).ToArray();
        if (methods.Length != 1)
            throw new InvalidOperationException($"Expected exactly one native method named {name}.");
        var method = methods[0];
        declaration = method;
        body = method.Body;
        output = method.Output;
        effects = method.Effects;
        signature = method.Visibility == Visibility.Public && method.IsAsync && method.TypeParameters.Count == 0
            && method.Parameters.Count == 0 && method.Modifiers == MethodModifiers.None;
    }
    else
    {
        var functions = parsed.Functions.Where(candidate => candidate.Name == name).ToArray();
        if (functions.Length != 1)
            throw new InvalidOperationException($"Expected exactly one native module function named {name}.");
        var function = functions[0];
        declaration = function;
        body = function.Body;
        output = function.Output;
        effects = function.Effects;
        signature = function.Visibility == Visibility.Public && function.IsAsync && function.TypeParameters.Count == 0
            && function.Parameters.Count == 0;
    }
    bool IntegerTaskOutput(string type)
    {
        if (AttributeHelper.ExpandType(type) == AttributeHelper.ExpandType("i32"))
            return true;
        var generic = type.IndexOf('<');
        return generic > 0 && type.EndsWith('>')
            && type[..generic] is "Task" or "System.Threading.Tasks.Task"
            && AttributeHelper.ExpandType(type[(generic + 1)..^1]) == AttributeHelper.ExpandType("i32");
    }
    if (!signature || output == null || !IntegerTaskOutput(output.TypeName))
        throw new InvalidOperationException($"Wrong native public async signature for {name}.");
    if (profile != "async001" && !(effects?.Effects.TryGetValue("io", out var row) == true
        && row.Split(',').Contains(http ? "network" : "network_read", StringComparer.Ordinal)))
        throw new InvalidOperationException($"Declared native {(http ? "network" : "network-read")} effect missing.");
    var compilation = Calor.Compiler.Program.Compile(source, path,
        new Calor.Compiler.CompilationOptions { ContractMode = ContractMode.Debug });
    if (compilation.HasErrors || compilation.Ast == null)
        throw new InvalidOperationException($"Calor compilation failed:\n{string.Join("\n", compilation.Diagnostics)}");
    var (originalAssembly, original) = CompileRuntime(compilation.GeneratedCode);
    var module = compilation.Ast;
    MethodInfo Method(Assembly assembly)
    {
        var type = assembly.GetType(http ? $"{module.Name}.DataService" : $"{module.Name}.{module.Name}Module")
            ?? throw new InvalidOperationException("Generated async type missing.");
        var method = type.GetMethod(name, BindingFlags.Public | (http ? BindingFlags.Instance : BindingFlags.Static),
            Type.EmptyTypes) ?? throw new InvalidOperationException("Generated exact CLR async signature missing.");
        if (!type.IsPublic || type.IsGenericType || method.IsGenericMethod || method.ReturnType != typeof(Task<int>)
            || method.GetCustomAttribute<System.Runtime.CompilerServices.AsyncStateMachineAttribute>() == null)
            throw new InvalidOperationException("Wrong reflected CLR async signature/state machine.");
        return method;
    }
    _ = Method(originalAssembly);
    var options = new CSharpParseOptions(LanguageVersion.Preview);
    var mapped = CSharpSyntaxTree.ParseText(
        new CSharpEmitter(ContractMode.Debug) { LineDirectiveFilePath = path }.Emit(module), options);
    if (!SyntaxFactory.AreEquivalent(original[1].GetRoot(), mapped.GetRoot()))
        throw new InvalidOperationException("Async mapping differs from independently compiled public emission.");
    var root = mapped.GetCompilationUnitRoot();
    var methodSyntax = root.DescendantNodes().OfType<ClassDeclarationSyntax>()
        .Single(type => type.Identifier.ValueText == (http ? "DataService" : module.Name + "Module"))
        .Members.OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == name);
    var container = (ClassDeclarationSyntax)methodSyntax.Parent!;
    if (container.Members.OfType<ConstructorDeclarationSyntax>().Any(constructor =>
            constructor.Modifiers.Any(SyntaxKind.StaticKeyword))
        || container.Members.OfType<FieldDeclarationSyntax>().Any(field =>
            field.Modifiers.Any(SyntaxKind.StaticKeyword) && field.Declaration.Variables.Any(variable => variable.Initializer != null)))
        throw new InvalidOperationException("Unmodeled async static initialization.");
    var semanticCompilation = CSharpCompilation.Create("AsyncMapping", [
        CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble, options), mapped],
        GeneratedCSharpCompiler.References, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    var semantic = semanticCompilation.GetSemanticModel(mapped);
    bool HttpClientType(ITypeSymbol type) => type.ToDisplayString() == "System.Net.Http.HttpClient"
        && type.ContainingAssembly.Name == "System.Net.Http";
    bool BclTaskType(ITypeSymbol type) => type.ContainingAssembly.Name == "System.Private.CoreLib"
        && type.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks"
        && type.Name == "Task";
    if (methodSyntax.DescendantNodes().Any(node => node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
        throw new InvalidOperationException("Unmodeled async lambda/local function in target.");
    var natives = RecursiveAstWalker.EnumerateStatements(body)
        .SelectMany(statement => StringNativeExpressions(statement).Select(expression => (Statement: statement, Expression: expression)))
        .ToArray();
    ExpressionSyntax Map(ExpressionNode native, StatementNode statement)
    {
        var emitted = SyntaxFactory.ParseExpression(native.Accept(new CSharpEmitter(ContractMode.Debug)));
        var matches = methodSyntax.DescendantNodes().OfType<ExpressionSyntax>()
            .Where(expression => SyntaxFactory.AreEquivalent(expression, emitted)
                && expression.AncestorsAndSelf().OfType<StatementSyntax>()
                    .SelectMany(container => container.GetFirstToken().LeadingTrivia)
                    .Select(trivia => trivia.GetStructure()).OfType<LineDirectiveTriviaSyntax>()
                    .FirstOrDefault(directive => directive.IsActive && directive.File.ValueText == path)
                    ?.Line.ValueText == statement.Span.Line.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException("Ambiguous/missing native async expression mapping.");
        return matches[0];
    }
    var calls = natives.Where(native => native.Expression is CallExpressionNode)
        .Select(native => Map(native.Expression, native.Statement)).OfType<InvocationExpressionSyntax>()
        .Where(call => semantic.GetSymbolInfo(call).Symbol is IMethodSymbol symbol
            && (http ? symbol.Name == "GetAsync" && HttpClientType(symbol.ContainingType)
                : symbol.Name == helperName && SymbolEqualityComparer.Default.Equals(
                    symbol.ContainingType, semantic.GetDeclaredSymbol(container))))
        .Distinct().ToArray();
    if (profile != "async001" && calls.Length != 1)
        throw new InvalidOperationException("Expected exactly one mapped required native async call.");
    if (!http && profile != "async001")
    {
        var helpers = parsed.Functions.Where(function => function.Name == helperName).ToArray();
        if (helpers.Length != 1 || helpers[0].Parameters.Count != 0 || helpers[0].TypeParameters.Count != 0
            || helpers[0].Output == null || !IntegerTaskOutput(helpers[0].Output!.TypeName)
            || !helpers[0].IsAsync && AttributeHelper.ExpandType(helpers[0].Output!.TypeName) == AttributeHelper.ExpandType("i32")
            || calls[0].ArgumentList.Arguments.Count != 0)
            throw new InvalidOperationException("Required native auxiliary Task<int> function missing/wrong.");
    }
    // Nothing outside the mapped task call may execute a provider or arbitrary helper.
    foreach (var call in methodSyntax.DescendantNodes().OfType<InvocationExpressionSyntax>())
    {
        var symbol = semantic.GetSymbolInfo(call).Symbol as IMethodSymbol;
        if (calls.Contains(call))
            continue;
        if (symbol is { Name: "ConfigureAwait" } && BclTaskType(symbol.ContainingType)
            && symbol.ContainingType.OriginalDefinition.ToDisplayString()
            == "System.Threading.Tasks.Task<TResult>")
            continue;
        if (symbol is { Name: "FromResult" } && BclTaskType(symbol.ContainingType))
            continue;
        throw new InvalidOperationException("Unmodeled executable async call; no network/provider fallback.");
    }
    foreach (var creation in methodSyntax.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
        if (semantic.GetSymbolInfo(creation).Symbol is not IMethodSymbol
            { ContainingType: { } constructed } || !HttpClientType(constructed)
            || creation.ArgumentList?.Arguments.Count != 0 || creation.Initializer != null)
            throw new InvalidOperationException("Unmodeled async construction.");
    foreach (var access in methodSyntax.DescendantNodes().OfType<ExpressionSyntax>())
        if (semantic.GetSymbolInfo(access).Symbol is IPropertySymbol
            or IFieldSymbol { IsStatic: true, IsConst: false }
            or IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator or MethodKind.Conversion })
            throw new InvalidOperationException("Unmodeled async property access (blocking Result is not await).");
    var bindings = RecursiveAstWalker.EnumerateStatements(body).OfType<BindStatementNode>()
        .GroupBy(binding => binding.Name).Where(group => group.Count() == 1)
        .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);
    ExpressionSyntax Ungroup(ExpressionSyntax expression) => expression is ParenthesizedExpressionSyntax parenthesized
        ? Ungroup(parenthesized.Expression) : expression;
    ExpressionSyntax ResolveLocal(ExpressionSyntax expression, HashSet<string> resolving)
    {
        expression = Ungroup(expression);
        if (expression is IdentifierNameSyntax identifier && semantic.GetSymbolInfo(expression).Symbol is ILocalSymbol
            && resolving.Add(identifier.Identifier.ValueText)
            && bindings.TryGetValue(identifier.Identifier.ValueText, out var binding)
            && !binding.IsMutable && binding.Initializer != null)
            return ResolveLocal(Map(binding.Initializer, binding), resolving);
        return expression;
    }
    ObjectCreationExpressionSyntax? httpReceiverCreation = null;
    string? httpAddress = null;
    var httpCreations = new List<ObjectCreationExpressionSyntax>();
    var httpFieldCount = 0;
    if (http)
    {
        var nativeClass = parsed.Classes.Single(candidate => candidate.Name == "DataService");
        if (nativeClass.IsAbstract || nativeClass.IsStatic || nativeClass.IsStruct || nativeClass.BaseClass != null
            || container.BaseList != null || container.Members.Any(member =>
                member is not (MethodDeclarationSyntax or FieldDeclarationSyntax or ConstructorDeclarationSyntax))
            || container.Members.OfType<ConstructorDeclarationSyntax>().Any(constructor =>
                !constructor.Modifiers.Any(SyntaxKind.PublicKeyword) || constructor.ParameterList.Parameters.Count != 0
                || constructor.Initializer != null || constructor.ExpressionBody != null
                || constructor.Body == null || constructor.Body.Statements.Count != 0))
            throw new InvalidOperationException("Unmodeled HTTP instance construction: require a real safe public zero-argument constructor.");
        foreach (var field in container.Members.OfType<FieldDeclarationSyntax>())
        {
            foreach (var variable in field.Declaration.Variables)
            {
                var nativeFields = nativeClass.Fields.Where(candidate => candidate.Name == variable.Identifier.ValueText).ToArray();
                if (nativeFields.Length != 1 || nativeFields[0].Visibility != Visibility.Private
                    || nativeFields[0].Modifiers != MethodModifiers.Readonly || nativeFields[0].DefaultValue is not NewExpressionNode
                    || semantic.GetDeclaredSymbol(variable) is not IFieldSymbol
                        { IsStatic: false, IsReadOnly: true, DeclaredAccessibility: Accessibility.Private } symbol
                    || !HttpClientType(symbol.Type) || variable.Initializer?.Value is not ObjectCreationExpressionSyntax creation
                    || creation.ArgumentList?.Arguments.Count != 0 || creation.Initializer != null
                    || semantic.GetSymbolInfo(creation).Symbol is not IMethodSymbol fieldConstructor
                    || !HttpClientType(fieldConstructor.ContainingType)
                    || !SyntaxFactory.AreEquivalent(creation, SyntaxFactory.ParseExpression(
                        nativeFields[0].DefaultValue!.Accept(new CSharpEmitter(ContractMode.Debug)))))
                    throw new InvalidOperationException("Unmodeled HTTP field: require a native private readonly default HttpClient initializer.");
                httpCreations.Add(creation);
                httpFieldCount++;
            }
        }
        foreach (var native in natives.Where(native => native.Expression is NewExpressionNode))
            httpCreations.Add((ObjectCreationExpressionSyntax)Map(native.Expression, native.Statement));
        foreach (var attribute in root.DescendantNodes().OfType<AttributeSyntax>())
            if (semantic.GetSymbolInfo(attribute).Symbol is IMethodSymbol initializerAttribute
                && initializerAttribute.ContainingType.ToDisplayString() == "System.Runtime.CompilerServices.ModuleInitializerAttribute")
                throw new InvalidOperationException("Unmodeled HTTP module initializer.");
        ExpressionSyntax ResolveReceiver(ExpressionSyntax expression)
        {
            expression = ResolveLocal(expression, new(StringComparer.Ordinal));
            if (semantic.GetSymbolInfo(expression).Symbol is IFieldSymbol field
                && SymbolEqualityComparer.Default.Equals(field.ContainingType, semantic.GetDeclaredSymbol(container)))
                return container.Members.OfType<FieldDeclarationSyntax>().SelectMany(field => field.Declaration.Variables)
                    .Single(variable => variable.Identifier.ValueText == field.Name).Initializer!.Value;
            return expression;
        }
        var call = calls[0];
        if (call.Expression is not MemberAccessExpressionSyntax member
            || Ungroup(member.Expression) is not (IdentifierNameSyntax or MemberAccessExpressionSyntax)
            || ResolveReceiver(member.Expression) is not ObjectCreationExpressionSyntax client
            || !httpCreations.Contains(client)
            || semantic.GetSymbolInfo(client).Symbol is not IMethodSymbol constructor
            || !HttpClientType(constructor.ContainingType)
            || call.ArgumentList.Arguments.Count != 1
            || semantic.GetConstantValue(ResolveLocal(call.ArgumentList.Arguments[0].Expression, new(StringComparer.Ordinal)))
                is not { HasValue: true, Value: string address }
            || !Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("Unmodeled HTTP receiver/input: require a native local or readonly-field HttpClient and constant absolute HTTP(S) GetAsync(string) input.");
        httpReceiverCreation = client;
        httpAddress = address;
    }
    bool SameTask(ExpressionSyntax expression, HashSet<string> resolving)
    {
        expression = Ungroup(expression);
        if (calls.Contains(expression))
            return true;
        if (expression is not IdentifierNameSyntax identifier
            || semantic.GetSymbolInfo(expression).Symbol is not ILocalSymbol)
            return false;
        var alias = identifier.Identifier.ValueText;
        if (!resolving.Add(alias) || !bindings.TryGetValue(alias, out var binding) || binding.IsMutable
            || binding.Initializer == null)
            return false;
        return SameTask(Map(binding.Initializer, binding), resolving);
    }
    var witnesses = new List<(AwaitExpressionSyntax Await, ExpressionSyntax Task)>();
    foreach (var native in natives.Where(native => native.Expression is AwaitExpressionNode))
    {
        var awaited = (AwaitExpressionSyntax)Map(native.Expression, native.Statement);
        var operand = Ungroup(awaited.Expression);
        var configuredFalse = false;
        if (operand is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax member } configure
            && semantic.GetSymbolInfo(configure).Symbol is IMethodSymbol { Name: "ConfigureAwait" } symbol
            && BclTaskType(symbol.ContainingType)
            && symbol.ContainingType.OriginalDefinition.ToDisplayString() == "System.Threading.Tasks.Task<TResult>")
        {
            configuredFalse = configure.ArgumentList.Arguments.Count == 1
                && semantic.GetConstantValue(configure.ArgumentList.Arguments[0].Expression) is { HasValue: true, Value: false };
            operand = Ungroup(member.Expression);
        }
        if (profile != "async001" && SameTask(operand, new(StringComparer.Ordinal))
            && (profile != "async003" || configuredFalse))
            witnesses.Add((awaited, operand));
    }
    if (profile != "async001" && witnesses.Count != 1)
        throw new InvalidOperationException("Required actual same-task await/ConfigureAwait(false) missing or ambiguous.");
    if (http)
    {
        VerifyActualHttp();
        return;
    }

    void VerifyActualHttp()
    {
        var witness = witnesses[0];
        var expectedSite = httpCreations.IndexOf(httpReceiverCreation!);
        const string httpProbeSource = """
            public static class AsyncTaskProbe
            {
                public static System.Threading.Tasks.Task<System.Net.Http.HttpResponseMessage> ResponseTask = null!;
                public static System.Threading.Tasks.Task<System.Net.Http.HttpResponseMessage> HttpTask = null!;
                public static string Address = "";
                public static int Site, Calls, Awaits, Requests, Creates;
                static readonly System.Collections.Generic.Dictionary<System.Net.Http.HttpClient, int> Clients = new();
                public static void Reset(System.Threading.Tasks.Task<System.Net.Http.HttpResponseMessage> response, string address, int site)
                {
                    DisposeAll();
                    ResponseTask = response; HttpTask = null!; Address = address; Site = site;
                    Calls = Awaits = Requests = Creates = 0;
                }
                public static System.Net.Http.HttpClient NewClient(int site)
                {
                    var handler = new Handler();
                    var client = new System.Net.Http.HttpClient(handler, true);
                    handler.Client = client;
                    Clients.Add(client, site); Creates++;
                    return client;
                }
                sealed class Handler : System.Net.Http.HttpMessageHandler
                {
                    public System.Net.Http.HttpClient Client = null!;
                    protected override System.Threading.Tasks.Task<System.Net.Http.HttpResponseMessage> SendAsync(
                        System.Net.Http.HttpRequestMessage request, System.Threading.CancellationToken cancellation)
                    {
                        if (!Clients.TryGetValue(Client, out var site) || site != Site
                            || request.Method != System.Net.Http.HttpMethod.Get
                            || request.RequestUri != new System.Uri(Address) || cancellation.IsCancellationRequested)
                            throw new System.InvalidOperationException("Wrong actual HTTP receiver/request.");
                        Requests++;
                        return ResponseTask;
                    }
                }
                public static System.Threading.Tasks.Task<System.Net.Http.HttpResponseMessage> Observe(
                    System.Net.Http.HttpClient client, string address,
                    System.Threading.Tasks.Task<System.Net.Http.HttpResponseMessage> actual)
                {
                    if (!Clients.TryGetValue(client, out var site) || site != Site
                        || address != Address || Requests != 1 || Calls != 0)
                        throw new System.InvalidOperationException("Wrong actual GetAsync receiver/address/call.");
                    Calls++; HttpTask = actual;
                    return actual;
                }
                public static System.Threading.Tasks.Task<System.Net.Http.HttpResponseMessage> Track(
                    System.Threading.Tasks.Task<System.Net.Http.HttpResponseMessage> task)
                {
                    if (!object.ReferenceEquals(task, HttpTask) || Calls != 1 || Requests != 1)
                        throw new System.InvalidOperationException("Awaited task is not the actual GetAsync task.");
                    Awaits++;
                    return task;
                }
                public static System.Net.Http.HttpResponseMessage Ignore(
                    System.Threading.Tasks.Task<System.Net.Http.HttpResponseMessage> task)
                {
                    if (!object.ReferenceEquals(task, HttpTask))
                        throw new System.InvalidOperationException("Wrong await-neutralized GetAsync task.");
                    return null!;
                }
                public static void DisposeAll()
                {
                    foreach (var client in Clients.Keys) client.Dispose();
                    Clients.Clear();
                }
            }
            """;
        var annotation = new SyntaxAnnotation("ActualHttpAwait");
        var nodes = httpCreations.Cast<ExpressionSyntax>().Concat([calls[0], witness.Task, witness.Await]).Distinct().ToArray();
        var controlled = root.ReplaceNodes(nodes, (before, rewritten) =>
        {
            ExpressionSyntax replacement = rewritten;
            if (before is ObjectCreationExpressionSyntax creation && httpCreations.Contains(creation))
                replacement = SyntaxFactory.ParseExpression($"global::AsyncTaskProbe.NewClient({httpCreations.IndexOf(creation)})");
            if (before == calls[0])
            {
                var actualCall = (InvocationExpressionSyntax)rewritten;
                var receiver = ((MemberAccessExpressionSyntax)actualCall.Expression).Expression;
                replacement = SyntaxFactory.ParseExpression(
                    $"global::AsyncTaskProbe.Observe({receiver}, {actualCall.ArgumentList.Arguments[0].Expression}, {actualCall})");
            }
            if (before == witness.Task)
                replacement = SyntaxFactory.ParseExpression($"global::AsyncTaskProbe.Track({replacement})");
            if (before == witness.Await)
                replacement = replacement.WithAdditionalAnnotations(annotation);
            return replacement.WithTriviaFrom(before);
        }).AddMembers(SyntaxFactory.ParseMemberDeclaration(httpProbeSource)
            ?? throw new InvalidOperationException("Cannot parse real HTTP instrumentation."));
        var awaitSyntax = (AwaitExpressionSyntax)controlled.GetAnnotatedNodes(annotation).Single();
        ExpressionSyntax actualTask = awaitSyntax.Expression;
        if (actualTask is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "ConfigureAwait" } } configured)
            actualTask = ((MemberAccessExpressionSyntax)configured.Expression).Expression;
        var counter = controlled.ReplaceNode(awaitSyntax,
            SyntaxFactory.ParseExpression($"global::AsyncTaskProbe.Ignore({actualTask})").WithTriviaFrom(awaitSyntax));
        // Neutralization keeps GetAsync and its request; only awaiting is removed.
        var trackCalls = counter.DescendantNodes().OfType<InvocationExpressionSyntax>().Where(invocation =>
            invocation.Expression.ToString() == "global::AsyncTaskProbe.Track").ToArray();
        counter = counter.ReplaceNodes(trackCalls, (_, rewritten) => rewritten.ArgumentList.Arguments[0].Expression);
        var baselineAssembly = CompileRuntime(controlled.ToFullString()).Assembly;
        var counterAssembly = CompileRuntime(counter.ToFullString()).Assembly;
        int Read(Type probe, string field) => (int)probe.GetField(field)!.GetValue(null)!;
        void Run(Assembly assembly, bool pending, bool faulted, int status, bool neutralized = false)
        {
            var method = Method(assembly);
            var probe = assembly.GetType("AsyncTaskProbe") ?? throw new InvalidOperationException("HTTP probe missing.");
            var response = new TaskCompletionSource<System.Net.Http.HttpResponseMessage>();
            using var message = new System.Net.Http.HttpResponseMessage((System.Net.HttpStatusCode)status);
            var fault = new InvalidOperationException("independent actual HTTP handler fault");
            probe.GetMethod("Reset")!.Invoke(null, [response.Task, httpAddress!, expectedSite]);
            if (!pending)
            {
                if (faulted) response.SetException(fault); else response.SetResult(message);
            }
            try
            {
                var constructor = method.DeclaringType!.GetConstructor(Type.EmptyTypes)
                    ?? throw new InvalidOperationException("Real public zero-argument DataService constructor missing.");
                var instance = constructor.Invoke(null);
                if (Read(probe, "Creates") != httpFieldCount)
                    throw new InvalidOperationException("Native field initializers did not execute in real instance construction.");
                var result = (Task<int>)method.Invoke(instance, null)!;
                var actual = (Task<System.Net.Http.HttpResponseMessage>)probe.GetField("HttpTask")!.GetValue(null)!;
                if (Read(probe, "Calls") != 1 || Read(probe, "Requests") != 1
                    || Read(probe, "Awaits") != (neutralized ? 0 : 1) || Read(probe, "Creates") < 1
                    || ReferenceEquals(actual, response.Task))
                    throw new InvalidOperationException("Actual BCL GetAsync/request/task/constructor evidence missing.");
                if (pending && actual.IsCompleted)
                    throw new InvalidOperationException("Actual GetAsync task completed before the controlled handler operation.");
                if (neutralized)
                {
                    if (!result.IsCompletedSuccessfully || result.GetAwaiter().GetResult() != 100)
                        throw new InvalidOperationException("Await-neutralized HTTP control did not successfully remove completion dependence.");
                }
                else if (pending && result.IsCompleted)
                    throw new InvalidOperationException("LoadAsync completed before its actual BCL GetAsync operation.");
                if (pending)
                {
                    if (faulted) response.SetException(fault); else response.SetResult(message);
                }
                if (faulted)
                {
                    foreach (Task task in new Task[] { actual, result })
                    {
                        try
                        {
                            task.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
                            throw new InvalidOperationException("Actual controlled HTTP fault was swallowed.");
                        }
                        catch (InvalidOperationException error) when (ReferenceEquals(error, fault)) { }
                    }
                }
                else
                {
                    if (!ReferenceEquals(actual.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult(), message)
                        || result.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult() != 100)
                        throw new InvalidOperationException("Wrong controlled async result from actual HTTP operation.");
                }
            }
            finally
            {
                probe.GetMethod("DisposeAll")!.Invoke(null, null);
            }
        }
        foreach (var status in new[] { 200, 202 })
        foreach (var pending in new[] { false, true })
            Run(baselineAssembly, pending, false, status);
        foreach (var pending in new[] { false, true })
            Run(baselineAssembly, pending, true, 200);
        Run(counterAssembly, true, false, 200, true);
        Console.WriteLine("Verification passed: LoadAsync; exact native/CLR async signatures, 6 controlled cases and same-task await coupling.");
        Console.WriteLine("Actual HTTP evidence: real zero-argument instance/field construction, BCL GetAsync, controlled handler receiver/address/GET request, actual returned task and successful await-only neutralization.");
        Console.WriteLine("Limits: finite completed/pending/faulted tasks; no scheduling/delay or universal body proof. No real network; native local/default readonly-field clients and safe zero-argument constructors only.");
    }
    var probeSource = """
        public static class AsyncTaskProbe
        {
            public static System.Threading.Tasks.Task<int> IntegerTask = null!;
            public static System.Threading.Tasks.Task<System.Net.Http.HttpResponseMessage> HttpTask = null!;
            public static int Calls, Awaits;
            public static System.Threading.Tasks.Task<int> NextInt() { Calls++; return IntegerTask; }
            public static System.Threading.Tasks.Task<System.Net.Http.HttpResponseMessage> NextHttp() { Calls++; return HttpTask; }
            public static System.Threading.Tasks.Task<T> Track<T>(System.Threading.Tasks.Task<T> task)
            {
                if (!object.ReferenceEquals(task, typeof(T) == typeof(int) ? (object)IntegerTask : HttpTask))
                    throw new System.InvalidOperationException("Wrong task identity.");
                Awaits++;
                return task;
            }
        }
        """;
    var targetNodes = profile == "async001" ? Array.Empty<ExpressionSyntax>()
        : new[] { (ExpressionSyntax)calls[0], witnesses[0].Task }.Distinct().ToArray();
    var instrumented = root.ReplaceNodes(targetNodes, (before, rewritten) =>
    {
        ExpressionSyntax replacement = calls.Contains(before)
            ? SyntaxFactory.ParseExpression(http ? "global::AsyncTaskProbe.NextHttp()" : "global::AsyncTaskProbe.NextInt()")
            : rewritten;
        if (witnesses.Any(witness => witness.Task == before))
            replacement = SyntaxFactory.ParseExpression($"global::AsyncTaskProbe.Track({replacement})");
        return replacement.WithTriviaFrom(before);
    }).AddMembers(SyntaxFactory.ParseMemberDeclaration(probeSource)
        ?? throw new InvalidOperationException("Cannot parse async instrumentation."));
    var probeAssembly = CompileRuntime(instrumented.ToFullString()).Assembly;
    var target = Method(probeAssembly);
    var probe = probeAssembly.GetType("AsyncTaskProbe") ?? throw new InvalidOperationException("Async probe missing.");
    object? instance = null;
    Task<int> Invoke() => target.Invoke(instance, null) as Task<int>
        ?? throw new InvalidOperationException("Async invocation did not return Task<int>.");
    var values = new[] { 0, 1, -1, 21, 42, -101, 103, int.MinValue / 2, int.MaxValue / 2 };
    if (profile == "async003")
        values = [.. values, int.MinValue, int.MaxValue];
    int Expected(int value) => profile switch
    {
        "async001" => 42, "async002" => checked(value * 2), "async003" => value, _ => 100
    };
    var executed = 0;
    if (profile == "async001")
    {
        for (var index = 0; index < 3; index++)
        {
            if (Invoke().WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult() != 42)
                throw new InvalidOperationException("Wrong asynchronous GetDataAsync value.");
            executed++;
        }
    }
    else
    {
        foreach (var value in http ? new[] { 0, 1 } : values)
        foreach (var pending in new[] { false, true })
        {
            var integer = new TaskCompletionSource<int>();
            var response = new TaskCompletionSource<System.Net.Http.HttpResponseMessage>();
            using var message = new System.Net.Http.HttpResponseMessage(
                value == 0 ? System.Net.HttpStatusCode.OK : System.Net.HttpStatusCode.Accepted);
            probe.GetField("IntegerTask")!.SetValue(null, integer.Task);
            probe.GetField("HttpTask")!.SetValue(null, response.Task);
            probe.GetField("Calls")!.SetValue(null, 0);
            probe.GetField("Awaits")!.SetValue(null, 0);
            if (!pending)
            {
                integer.SetResult(value);
                response.SetResult(message);
            }
            var result = Invoke();
            if ((int)probe.GetField("Calls")!.GetValue(null)! != 1
                || (int)probe.GetField("Awaits")!.GetValue(null)! != 1)
                throw new InvalidOperationException("Mapped required call/await did not execute exactly once.");
            if (pending)
            {
                if (result.IsCompleted)
                    throw new InvalidOperationException("Async target completed before its actual controlled operation.");
                integer.SetResult(value);
                response.SetResult(message);
            }
            if (result.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult() != Expected(value))
                throw new InvalidOperationException("Wrong controlled async result dependence.");
            executed++;
        }
        // Fault propagation belongs to the same task; an unrelated/dead await cannot supply it.
        foreach (var pending in new[] { false, true })
        {
            var integer = new TaskCompletionSource<int>();
            var response = new TaskCompletionSource<System.Net.Http.HttpResponseMessage>();
            var fault = new InvalidOperationException("independent controlled fault");
            probe.GetField("IntegerTask")!.SetValue(null, integer.Task);
            probe.GetField("HttpTask")!.SetValue(null, response.Task);
            probe.GetField("Calls")!.SetValue(null, 0);
            probe.GetField("Awaits")!.SetValue(null, 0);
            if (!pending)
            {
                if (http) response.SetException(fault); else integer.SetException(fault);
            }
            var result = Invoke();
            if ((int)probe.GetField("Calls")!.GetValue(null)! != 1
                || (int)probe.GetField("Awaits")!.GetValue(null)! != 1 || pending && result.IsCompleted)
                throw new InvalidOperationException("Wrong controlled fault call/await ordering.");
            if (pending)
            {
                if (http) response.SetException(fault); else integer.SetException(fault);
            }
            try
            {
                _ = result.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
                throw new InvalidOperationException("Controlled task fault was swallowed.");
            }
            catch (InvalidOperationException error) when (ReferenceEquals(error, fault)) { }
            executed++;
        }
        // Successful neutralization, not an exception, proves the await matters.
        var witness = witnesses[0].Await;
        var neutral = SyntaxFactory.ParseExpression(http ? "default(global::System.Net.Http.HttpResponseMessage)" : "0")
            .WithTriviaFrom(witness);
        var counterRoot = root.ReplaceNode(witness, neutral);
        var mappedCall = counterRoot.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(call => SyntaxFactory.AreEquivalent(call, calls[0])).ToArray();
        counterRoot = counterRoot.ReplaceNodes(mappedCall, (before, _) =>
            SyntaxFactory.ParseExpression(http ? "global::AsyncTaskProbe.NextHttp()" : "global::AsyncTaskProbe.NextInt()")
                .WithTriviaFrom(before))
            .AddMembers(SyntaxFactory.ParseMemberDeclaration(probeSource)!);
        var counterAssembly = CompileRuntime(counterRoot.ToFullString()).Assembly;
        var counter = Method(counterAssembly);
        var counterProbe = counterAssembly.GetType("AsyncTaskProbe")!;
        counterProbe.GetField("IntegerTask")!.SetValue(null, Task.FromResult(103));
        counterProbe.GetField("HttpTask")!.SetValue(null, new TaskCompletionSource<System.Net.Http.HttpResponseMessage>().Task);
        object? counterInstance = null;
        var counterResult = (Task<int>)counter.Invoke(counterInstance, null)!;
        if (http)
        {
            if (!counterResult.IsCompletedSuccessfully || counterResult.GetAwaiter().GetResult() != 100)
                throw new InvalidOperationException("Await-neutralized HTTP control did not successfully remove completion dependence.");
        }
        else if (counterResult.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult() == Expected(103))
            throw new InvalidOperationException("Async await neutralization did not change the result.");
    }
    Console.WriteLine($"Verification passed: {name}; exact native/CLR async signatures, {executed} controlled cases and same-task await coupling.");
    Console.WriteLine("Limits: finite representable values and controlled completed/pending/faulted tasks; no scheduling/delay or universal body proof. No real network; auxiliary bodies and constructors are not executed.");
}

static void VerifyCarrierPatterns(string profile, string path)
{
    var option = profile == "pm004";
    var name = option ? "UnwrapOrDefault" : "HandleResult";
    var carrierName = option ? "Option" : "Result";
    var compiled = Calor.Compiler.Program.Compile(File.ReadAllText(path), path,
        new Calor.Compiler.CompilationOptions { ContractMode = ContractMode.Debug });
    if (compiled.HasErrors || compiled.Ast == null)
        throw new InvalidOperationException($"Calor compilation failed:\n{string.Join("\n", compiled.Diagnostics)}");
    var module = compiled.Ast;
    RejectRawInterop(module, "carrier pattern behavior");
    var functions = module.Functions.Where(candidate => candidate.Name == name).ToArray();
    if (functions.Length != 1)
        throw new InvalidOperationException($"Expected exactly one native {name} function.");
    var function = functions[0];
    var parameterType = function.Parameters.Count == 0 ? null
        : SyntaxFactory.ParseTypeName(function.Parameters[0].TypeName);
    var generic = parameterType switch
    {
        GenericNameSyntax simple => simple,
        QualifiedNameSyntax { Right: GenericNameSyntax simple } qualified
            when qualified.Left.ToString() == module.Name => simple,
        _ => null
    };
    if (function.Visibility != Visibility.Public || function.IsAsync || function.TypeParameters.Count != 0
        || function.Output == null || AttributeHelper.ExpandType(function.Output.TypeName) != "INT"
        || function.Parameters.Count != (option ? 2 : 1)
        || !function.Parameters.Select(parameter => parameter.Name)
            .SequenceEqual(option ? new[] { "opt", "defaultVal" } : new[] { "res" })
        || function.Parameters.Any(parameter => parameter.Modifier != Calor.Compiler.Ast.ParameterModifier.None)
        || option && AttributeHelper.ExpandType(function.Parameters[1].TypeName) != "INT"
        || generic == null || generic.Identifier.ValueText != carrierName
        || !generic.TypeArgumentList.Arguments.Select(argument => AttributeHelper.ExpandType(argument.ToString()))
            .SequenceEqual(option ? new[] { "INT" } : new[] { "INT", "STRING" }))
        throw new InvalidOperationException($"Wrong native public carrier matching signature for {name}.");
    var native = module.Classes.Where(candidate => candidate.Name == carrierName).ToArray();
    if (native.Length != 1 || native[0].Visibility != Visibility.Public || native[0].IsStatic
        || native[0].TypeParameters.Count != (option ? 1 : 2))
        throw new InvalidOperationException("Missing exact native public generic input carrier.");

    IEnumerable<AstNode> Nodes(AstNode node)
    {
        yield return node;
        foreach (var child in RecursiveAstWalker.GetAllChildren(node))
            foreach (var descendant in Nodes(child))
                yield return descendant;
    }
    var aliases = new HashSet<string>(StringComparer.Ordinal) { function.Parameters[0].Name };
    foreach (var binding in function.Body.OfType<BindStatementNode>())
        if (!binding.IsMutable && binding.Initializer is ReferenceNode reference && aliases.Contains(reference.Name))
            aliases.Add(binding.Name);
    var matches = Nodes(function).OfType<MatchExpressionNode>()
        .Where(match => match.Target is ReferenceNode reference && aliases.Contains(reference.Name)).ToArray();
    if (matches.Length == 0)
        throw new InvalidOperationException("Required native carrier-input switch expression missing.");

    var values = new[] { int.MinValue, int.MinValue + 1, -65536, -100, -17, -2, -1, 0, 1, 2, 17,
        100, 65536, int.MaxValue - 1, int.MaxValue };
    var defaults = option ? new[] { int.MinValue, -101, -1, 0, 1, 73, int.MaxValue } : new[] { 0 };
    var errors = option ? new[] { "" } : new[] { "", "failure", "other payload" };
    var inputs = (from positive in new[] { true, false }
                  from value in values
                  from fallback in defaults
                  from error in errors
                  select (Positive: positive, Value: value, Default: fallback, Error: error)).ToArray();
    int Expected((bool Positive, int Value, int Default, string Error) input)
        => input.Positive ? input.Value : option ? input.Default : -1;
    (MethodInfo Method, CarrierPatternInput Carrier) Target(Assembly assembly)
    {
        var definition = assembly.GetType($"{module.Name}.{carrierName}`{(option ? 1 : 2)}")
            ?? throw new InvalidOperationException("Missing actual native CLR carrier identity.");
        if (!definition.IsPublic || !definition.IsClass || definition.IsAbstract
            || definition.Assembly != assembly || !definition.IsGenericTypeDefinition)
            throw new InvalidOperationException("Wrong reflected generic input carrier identity.");
        var closed = definition.MakeGenericType(option ? new[] { typeof(int) } : new[] { typeof(int), typeof(string) });
        var owner = assembly.GetType($"{module.Name}.{module.Name}Module")
            ?? throw new InvalidOperationException("Missing native carrier pattern module.");
        var methods = owner.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
            | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(method => method.Name == name).ToArray();
        if (!owner.IsPublic || methods.Length != 1 || !methods[0].IsPublic || !methods[0].IsStatic
            || methods[0].IsGenericMethod || methods[0].ReturnType != typeof(int)
            || !methods[0].GetParameters().Select(parameter => parameter.ParameterType)
                .SequenceEqual(option ? new[] { closed, typeof(int) } : new[] { closed }))
            throw new InvalidOperationException("Wrong reflected exact closed CLR carrier matching signature.");
        return (methods[0], new CarrierPatternInput(closed, option));
    }
    int Invoke((MethodInfo Method, CarrierPatternInput Carrier) target,
        (bool Positive, int Value, int Default, string Error) input)
    {
        var saved = Console.Out;
        using var output = new BoundedOutput();
        try
        {
            Console.SetOut(output);
            var instance = target.Carrier.Create(input.Positive, input.Value, input.Error);
            var result = (int)(target.Method.Invoke(null, option ? new object[] { instance, input.Default } : new[] { instance })
                ?? throw new InvalidOperationException("Null carrier pattern output."));
            target.Carrier.Check(instance, input.Positive, input.Value, input.Error);
            return result;
        }
        finally
        {
            Console.SetOut(saved);
        }
    }
    void Behavior((MethodInfo Method, CarrierPatternInput Carrier) target)
    {
        foreach (var input in inputs)
            if (Invoke(target, input) != Expected(input))
                throw new InvalidOperationException($"Wrong carrier matching output for {name}: {input}.");
    }
    var previous = Console.Out;
    using var capture = new BoundedOutput();
    var counterfactuals = 0;
    try
    {
        Console.SetOut(capture);
        var (original, trees) = CompileRuntime(compiled.GeneratedCode, warningsAsErrors: true);
        Behavior(Target(original));
        var options = new CSharpParseOptions(LanguageVersion.Preview);
        var debug = CSharpSyntaxTree.ParseText(
            new CSharpEmitter(ContractMode.Debug) { LineDirectiveFilePath = path }.Emit(module), options);
        if (!SyntaxFactory.AreEquivalent(debug.GetRoot(), trees[1].GetRoot()))
            throw new InvalidOperationException("Mapped selected carrier pattern emission differs from compiled C#.");
        var off = CSharpSyntaxTree.ParseText(
            new CSharpEmitter(ContractMode.Off) { LineDirectiveFilePath = path }.Emit(module), options)
            .GetCompilationUnitRoot();
        Behavior(Target(CompileRuntime(off.ToFullString(), warningsAsErrors: true).Assembly));
        var declaration = off.DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Single(type => type.Identifier.ValueText == module.Name + "Module")
            .Members.OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == name);
        var accepted = 0;
        foreach (var match in matches)
        {
            bool CaseArm(MatchCaseNode arm)
            {
                if (Nodes(arm.Pattern).OfType<PropertyPatternNode>().Any(property =>
                    property.Matches.Any(member => member.PropertyName == (option ? "IsSome" : "IsOk")
                        || member.PropertyName == (option ? "IsNone" : "IsErr"))))
                    return true;
                var binding = arm.Pattern switch
                {
                    VarPatternNode variable => variable.Name,
                    VariablePatternNode variable => variable.Name,
                    TypePatternNode typed => typed.BindingName,
                    _ => null
                };
                return binding != null && arm.Guard != null
                    && Nodes(arm.Guard).OfType<ReferenceNode>().Any(reference =>
                        reference.Name == $"{binding}.{(option ? "IsSome" : "IsOk")}"
                        || reference.Name == $"{binding}.{(option ? "IsNone" : "IsErr")}");
            }
            if (!match.Cases.Any(CaseArm))
                continue;
            var emitted = declaration.DescendantNodes().OfType<SwitchExpressionSyntax>()
                .Where(node => node.GetLocation().GetMappedLineSpan().Path == path
                    && node.GetLocation().GetMappedLineSpan().StartLinePosition.Line + 1 == match.Span.Line).ToArray();
            if (emitted.Length != 1 || emitted[0].Arms.Count != match.Cases.Count)
                throw new InvalidOperationException("Ambiguous native/emitted carrier switch expression or arm count.");
            var selected = emitted[0];
            var probe = "CarrierPatternTrace_" + Guid.NewGuid().ToString("N");
            var traced = selected.WithGoverningExpression(SyntaxFactory.ParseExpression(
                    $"{probe}.Swap({selected.GoverningExpression.ToFullString()})"))
                .WithArms(SyntaxFactory.SeparatedList(selected.Arms.Select((arm, index) => arm.WithExpression(
                    SyntaxFactory.ParseExpression($"{probe}.Observe({index}, ({arm.Expression.ToFullString()}))")))));
            var traceType = SyntaxFactory.ParseMemberDeclaration(
                $"internal static class {probe} {{ public static int Arm = -1; public static int Value; "
                + "public static bool Flip = false; public static object? Replacement = null; "
                + "public static T Swap<T>(T original) => Replacement == null ? original : (T)Replacement; "
                + "public static int Observe(int arm, int value) { Arm = arm; Value = value; return Flip ? ~value : value; } }")
                ?? throw new InvalidOperationException("Carrier pattern trace declaration failed.");
            var instrumented = off.ReplaceNode(selected, traced).AddMembers(traceType);
            var assembly = CompileRuntime(instrumented.ToFullString(), warningsAsErrors: true).Assembly;
            var target = Target(assembly);
            var trace = assembly.GetType(probe) ?? throw new InvalidOperationException("Missing carrier trace identity.");
            var armField = trace.GetField("Arm") ?? throw new InvalidOperationException("Missing carrier arm trace.");
            var valueField = trace.GetField("Value") ?? throw new InvalidOperationException("Missing carrier payload trace.");
            var flip = trace.GetField("Flip") ?? throw new InvalidOperationException("Missing carrier arm intervention.");
            var replacement = trace.GetField("Replacement") ?? throw new InvalidOperationException("Missing carrier input intervention.");
            var positiveArms = new HashSet<int>();
            var negativeArms = new HashSet<int>();
            var supplies = true;
            foreach (var input in inputs)
            {
                armField.SetValue(null, -1);
                if (Invoke(target, input) != Expected(input))
                    throw new InvalidOperationException("Carrier pattern instrumentation changed baseline behavior.");
                var arm = (int)armField.GetValue(null)!;
                if (arm < 0 || arm >= match.Cases.Count || !Equals(valueField.GetValue(null), Expected(input)))
                    supplies = false;
                else
                    (input.Positive ? positiveArms : negativeArms).Add(arm);
            }
            if (!supplies || positiveArms.Overlaps(negativeArms)
                || !positiveArms.Concat(negativeArms).Any(index => CaseArm(match.Cases[index])))
                continue;
            var candidateCounterfactuals = 0;
            var coupled = true;
            flip.SetValue(null, true);
            foreach (var input in inputs)
            {
                if (Invoke(target, input) != ~Expected(input))
                    coupled = false;
                candidateCounterfactuals++;
            }
            if (!coupled)
                continue;
            flip.SetValue(null, false);
            // Replace only this native switch's governing carrier, not the
            // caller argument. Captures must supply the new case and payload.
            foreach (var input in inputs)
            {
                foreach (var changed in new[]
                {
                    (Positive: !input.Positive, input.Value, input.Default, input.Error),
                    (input.Positive, Value: ~input.Value, input.Default, Error: input.Error + " changed"),
                    (Positive: !input.Positive, Value: ~input.Value, input.Default, Error: input.Error + " changed")
                })
                {
                    var changedCarrier = target.Carrier.Create(changed.Positive, changed.Value, changed.Error);
                    replacement.SetValue(null, changedCarrier);
                    if (Invoke(target, input) != Expected(changed))
                        coupled = false;
                    target.Carrier.Check(changedCarrier, changed.Positive, changed.Value, changed.Error);
                    candidateCounterfactuals++;
                }
            }
            replacement.SetValue(null, null);
            if (option)
                foreach (var input in inputs)
                {
                    var changed = (input.Positive, input.Value, Default: ~input.Default, input.Error);
                    if (Invoke(target, changed) != Expected(changed))
                        coupled = false;
                    candidateCounterfactuals++;
                }
            if (!coupled)
                continue;
            counterfactuals = candidateCounterfactuals;
            accepted++;
            break;
        }
        if (accepted == 0)
            throw new InvalidOperationException("Required native carrier case arms missing or do not supply the output.");
    }
    finally
    {
        Console.SetOut(previous);
    }
    Console.WriteLine($"Verification passed: {name}; native/reflected exact closed CLR input carrier; "
        + $"{inputs.Length} independent runtime cases; {counterfactuals} successful complete-case arm/case/payload/default/input-output counterfactuals.");
    Console.WriteLine("Limits: public parameterless writable named carrier APIs; every present active case/payload observation agrees; "
        + "finite signed/boundary payload and default cases, not universal carrier/body/contract proof; optional contracts execute.");
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

static void VerifyCarrier(string profile, ModuleNode module, string generatedCode)
{
    var name = profile switch
    {
        "type001" => "TryDouble", "type002" => "GetNothing",
        "type003" => "SafeDivide", _ => "AlwaysFail"
    };
    var option = profile is "type001" or "type002";
    var carrierName = option ? "Option" : "Result";
    var names = profile switch
    {
        "type001" => new[] { "x" }, "type003" => new[] { "a", "b" }, _ => Array.Empty<string>()
    };
    var pending = new Stack<AstNode>();
    var seen = new HashSet<AstNode>(ReferenceEqualityComparer.Instance);
    pending.Push(module);
    while (pending.TryPop(out var node))
    {
        if (!seen.Add(node))
            continue;
        if (node is CSharpInteropBlockNode or RawCSharpNode or RawCSharpExpressionNode)
            throw new InvalidOperationException("Raw interop cannot establish native carrier behavior.");
        foreach (var child in RecursiveAstWalker.GetAllChildren(node))
            pending.Push(child);
        if (node is LocalFunctionStatementNode local)
            pending.Push(local.Function);
    }
    var functions = module.Functions.Where(function => function.Name == name).ToArray();
    if (functions.Length != 1)
        throw new InvalidOperationException($"Expected exactly one native {name} function.");
    var function = functions[0];
    var output = function.Output == null ? null : SyntaxFactory.ParseTypeName(function.Output.TypeName);
    var genericOutput = output switch
    {
        GenericNameSyntax generic => generic,
        QualifiedNameSyntax { Right: GenericNameSyntax generic } qualified
            when qualified.Left.ToString() == module.Name || qualified.Left.ToString() == "Calor.Runtime" => generic,
        _ => null
    };
    if (function.Visibility != Visibility.Public || function.IsAsync || function.TypeParameters.Count != 0
        || !function.Parameters.Select(parameter => parameter.Name).SequenceEqual(names)
        || function.Parameters.Any(parameter => AttributeHelper.ExpandType(parameter.TypeName) != "INT"
            || parameter.Modifier != Calor.Compiler.Ast.ParameterModifier.None)
        || genericOutput == null || genericOutput.Identifier.ValueText != carrierName
        || !genericOutput.TypeArgumentList.Arguments.Select(argument => AttributeHelper.ExpandType(argument.ToString()))
            .SequenceEqual(option ? new[] { "INT" } : new[] { "INT", "STRING" }))
        throw new InvalidOperationException($"Wrong native public carrier signature for {name}.");

    var (assembly, _) = CompileRuntime(generatedCode, warningsAsErrors: true);
    const BindingFlags members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
        | BindingFlags.Instance | BindingFlags.DeclaredOnly;
    var owner = assembly.GetType($"{module.Name}.{module.Name}Module")
        ?? throw new InvalidOperationException("Missing emitted carrier function owner.");
    var methods = owner.GetMethods(members).Where(method => method.Name == name).ToArray();
    if (!owner.IsPublic || methods.Length != 1)
        throw new InvalidOperationException("Wrong reflected carrier function identity.");
    var method = methods[0];
    var closed = method.ReturnType;
    var arguments = option ? new[] { typeof(int) } : new[] { typeof(int), typeof(string) };
    if (!method.IsPublic || !method.IsStatic || method.IsGenericMethod
        || !method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(names.Select(_ => typeof(int)))
        || !closed.IsGenericType || closed.ContainsGenericParameters
        || closed.Name != $"{carrierName}`{arguments.Length}" || !closed.GetGenericArguments().SequenceEqual(arguments))
        throw new InvalidOperationException("Wrong reflected closed CLR carrier signature.");
    var definition = closed.GetGenericTypeDefinition();
    if (closed.Assembly == assembly)
    {
        var native = module.Classes.Where(candidate => candidate.Name == carrierName).ToArray();
        if (native.Length != 1 || native[0].Visibility != Visibility.Public || native[0].IsStatic
            || native[0].TypeParameters.Count != arguments.Length || !definition.IsPublic
            || definition.FullName != $"{module.Name}.{carrierName}`{arguments.Length}")
            throw new InvalidOperationException("Missing exact native auxiliary generic carrier declaration.");
    }
    else if (definition.Namespace != "Calor.Runtime"
        || definition.Assembly != typeof(Calor.Runtime.ContractKind).Assembly)
        throw new InvalidOperationException("Foreign CLR carrier identity.");

    var parameters = definition.GetGenericArguments();
    // Only named public instance case APIs count. A present API must be valid
    // and agree with every other recognized observation; fallback never hides it.
    MemberInfo? ValueMember(string memberName, Type expected)
    {
        var candidates = closed.GetMember(memberName, members);
        if (candidates.Length == 0)
            return null;
        if (candidates.Length != 1)
            throw new InvalidOperationException($"Ambiguous carrier member {memberName}.");
        var member = candidates[0];
        var genericMember = definition.GetMember(memberName, members).Single();
        var valid = (member, genericMember) switch
        {
            (FieldInfo field, FieldInfo genericField) => field.IsPublic && !field.IsStatic
                && genericField.FieldType == expected,
            (PropertyInfo property, PropertyInfo genericProperty) => property.GetMethod is { IsPublic: true, IsStatic: false }
                && property.GetIndexParameters().Length == 0 && genericProperty.PropertyType == expected,
            _ => false
        };
        if (!valid)
            throw new InvalidOperationException($"Wrong carrier member shape/type: {memberName}.");
        return member;
    }
    MethodInfo? Accessor(string accessorName, Type expected)
    {
        var candidates = closed.GetMember(accessorName, members);
        if (candidates.Length == 0)
            return null;
        if (candidates.Length != 1 || candidates[0] is not MethodInfo accessor
            || !accessor.IsPublic || accessor.IsStatic || accessor.IsGenericMethod || accessor.GetParameters().Length != 0
            || definition.GetMethods(members).Single(candidate => candidate.Name == accessorName).ReturnType != expected)
            throw new InvalidOperationException($"Wrong carrier accessor shape/type: {accessorName}.");
        return accessor;
    }
    var positiveTag = ValueMember(option ? "IsSome" : "IsOk", typeof(bool));
    var negativeTag = ValueMember(option ? "IsNone" : "IsErr", typeof(bool));
    if (positiveTag == null && negativeTag == null)
        throw new InvalidOperationException("Missing authoritative carrier case tag.");
    var value = ValueMember("Value", parameters[0]);
    var unwrap = Accessor("Unwrap", parameters[0]);
    var error = option ? null : ValueMember("Error", parameters[1]);
    var unwrapError = option ? null : Accessor("UnwrapErr", parameters[1]);
    if (value == null && unwrap == null || !option && error == null && unwrapError == null)
        throw new InvalidOperationException("Missing typed carrier payload observation.");
    object? Read(MemberInfo member, object instance) => member switch
    {
        FieldInfo field => field.GetValue(instance),
        PropertyInfo property => property.GetValue(instance),
        MethodInfo accessor => accessor.Invoke(instance, null),
        _ => throw new InvalidOperationException("Unsupported carrier observation.")
    };
    void Payload(object instance, object expected, MemberInfo? field, MethodInfo? accessor)
    {
        foreach (var observation in new[] { field, accessor }.OfType<MemberInfo>())
            if (!Equals(Read(observation, instance), expected))
                throw new InvalidOperationException($"Wrong carrier payload from {observation.Name}; expected {expected}.");
    }
    var count = 0;
    foreach (var (inputs, isPositive, expected) in CarrierCases(profile))
    {
        var previous = Console.Out;
        using var outputCapture = new BoundedOutput();
        try
        {
            Console.SetOut(outputCapture);
            var instance = method.Invoke(null, inputs.Select(input => (object)input).ToArray())
                ?? throw new InvalidOperationException("Null returned carrier.");
            if (instance.GetType() != closed)
                throw new InvalidOperationException("Wrong actual returned carrier identity.");
            if (positiveTag != null && !Equals(Read(positiveTag, instance), isPositive)
                || negativeTag != null && !Equals(Read(negativeTag, instance), !isPositive))
                throw new InvalidOperationException($"Wrong/contradictory carrier case for {name}({string.Join(", ", inputs)}).");
            if (isPositive)
                Payload(instance, expected!, value, unwrap);
            else if (!option)
                Payload(instance, expected!, error, unwrapError);
        }
        finally
        {
            Console.SetOut(previous);
        }
        count++;
    }
    if (count == 0)
        throw new InvalidOperationException("No carrier cases executed.");
    Console.WriteLine($"Verification passed: {name}; native/reflected closed CLR {carrierName} identity; {count} runtime cases. "
        + "Limits: named case tags and active typed payload observations, not universal carrier API/body equivalence; "
        + "TryDouble excludes only positive doubled values above Int32.MaxValue; SafeDivide excludes only Int32.MinValue/-1; "
        + "optional contracts execute on every sampled required case.");
}

static IEnumerable<(int[] Inputs, bool Positive, object? Payload)> CarrierCases(string profile)
{
    var values = new[] { int.MinValue, int.MinValue + 1, -1073741824, -46340, -100, -7, -3, -2, -1,
        0, 1, 2, 3, 7, 100, 46340, 1073741823, 1073741824, int.MaxValue - 1, int.MaxValue };
    if (profile == "type001")
    {
        foreach (var x in values.Concat(Enumerable.Range(-64, 129)).Distinct())
            if (x <= 0 || (long)x * 2 <= int.MaxValue)
                yield return ([x], x > 0, x > 0 ? (int)((long)x * 2) : null);
    }
    else if (profile == "type003")
    {
        foreach (var a in values)
            foreach (var b in values)
                if (a != int.MinValue || b != -1)
                    yield return ([a, b], b != 0, b == 0 ? "Division by zero" : (object)(a / b));
    }
    else
        for (var repetition = 0; repetition < 8; repetition++)
            yield return ([], false, profile == "type004" ? "Operation not supported" : null);
}

static void VerifyRefactoring(string profile, string path)
{
    var name = profile switch
    {
        "ref001" => "DoubleValue", "ref003" => "JustCompute", "ref004" => "AbsoluteValue",
        "ref005" => "SafeMultiply", "ref006" => "Abs", "ref007" => "NoContractAdd", _ => "TryParseDigit"
    };
    var source = File.ReadAllText(path);
    var diagnostics = new DiagnosticBag();
    var parsed = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
    if (diagnostics.HasErrors)
        throw new InvalidOperationException($"Calor compilation failed (original parse):\n{string.Join("\n", diagnostics)}");
    RejectRawInterop(parsed, "refactoring behavior");
    if (profile == "ref008")
    {
        var targets = parsed.Functions.Where(candidate => candidate.Name == name).ToArray();
        if (targets.Length != 1)
            throw new InvalidOperationException($"Expected exactly one native refactoring function named {name}.");
        if (targets[0].Parameters.Count != 1
            || AttributeHelper.ExpandType(targets[0].Parameters[0].TypeName) != AttributeHelper.ExpandType("char"))
            throw new InvalidOperationException("Wrong native Option<int> digit-conversion signature.");
        var digitCompilation = Calor.Compiler.Program.Compile(source, path,
            new Calor.Compiler.CompilationOptions { ContractMode = ContractMode.Debug });
        if (digitCompilation.HasErrors || digitCompilation.Ast == null)
            throw new InvalidOperationException($"Calor compilation failed:\n{string.Join("\n", digitCompilation.Diagnostics)}");
        VerifyDigitConversion(parsed, digitCompilation.Ast, digitCompilation.GeneratedCode, path);
        return;
    }
    string[] names = profile is "ref003" or "ref005" ? ["a", "b"] : profile == "ref007" ? ["x", "y"] : ["x"];
    FunctionNode Function(string target, string[] parameters, bool pure)
    {
        var matches = parsed.Functions.Where(function => function.Name == target).ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException($"Expected exactly one native refactoring function named {target}.");
        var function = matches[0];
        if (function.IsAsync || function.TypeParameters.Count != 0 || function.Output == null
            || AttributeHelper.ExpandType(function.Output.TypeName) != "INT"
            || !function.Parameters.Select(parameter => parameter.Name).SequenceEqual(parameters)
            || function.Parameters.Any(parameter => AttributeHelper.ExpandType(parameter.TypeName) != "INT"
                || parameter.Modifier != Calor.Compiler.Ast.ParameterModifier.None))
            throw new InvalidOperationException($"Wrong native refactoring signature for {target}.");
        if (pure && function.Effects?.Effects.Values.Any(value => !string.IsNullOrWhiteSpace(value)) == true)
            throw new InvalidOperationException($"Original refactoring declaration {target} must be pure.");
        CheckWritingContracts(function, parameters, "i32");
        return function;
    }
    var function = Function(name, names, profile is "ref001" or "ref003");
    if (profile == "ref004" && function.Postconditions.Count < 2)
        throw new InvalidOperationException("AbsoluteValue requires multiple native postconditions.");
    if (profile == "ref005")
    {
        if (function.Preconditions.Count != 0 || function.Parameters.Any(parameter => parameter.InlineRefinement != null))
            throw new InvalidOperationException("SafeMultiply must not declare preconditions.");
        if (!function.Postconditions.SelectMany(contract => RecursiveAstWalker.GetAllChildren(contract.Condition)
            .Prepend(contract.Condition)).Any(node => node is ImplicationExpressionNode))
            throw new InvalidOperationException("SafeMultiply requires native implication postconditions.");
    }
    if (profile == "ref001")
    {
        var caller = Function("LogAndDouble", ["x"], false);
        if (!(caller.Effects?.Effects.TryGetValue("io", out var declared) == true
            && declared.Split(',').Contains("console_write", StringComparer.Ordinal)))
            throw new InvalidOperationException("Original LogAndDouble console-write declaration missing.");
    }
    var compilation = Calor.Compiler.Program.Compile(source, path,
        new Calor.Compiler.CompilationOptions { ContractMode = ContractMode.Debug });
    if (compilation.HasErrors || compilation.Ast == null)
        throw new InvalidOperationException($"Calor compilation failed:\n{string.Join("\n", compilation.Diagnostics)}");
    var (original, trees) = CompileRuntime(compilation.GeneratedCode);
    var mapped = new CSharpEmitter(ContractMode.Debug) { LineDirectiveFilePath = path }.Emit(compilation.Ast);
    if (!SyntaxFactory.AreEquivalent(CSharpSyntaxTree.ParseText(mapped).GetRoot(), trees[1].GetRoot()))
        throw new InvalidOperationException("Mapped selected refactoring emission differs from compiled C#.");
    var off = CSharpSyntaxTree.ParseText(
        new CSharpEmitter(ContractMode.Off) { LineDirectiveFilePath = path }.Emit(compilation.Ast)).GetCompilationUnitRoot();
    var semanticCompilation = CSharpCompilation.Create("RefactoringMapping",
        [trees[0], CSharpSyntaxTree.Create(off, (CSharpParseOptions)trees[0].Options)], GeneratedCSharpCompiler.References,
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    var offTree = semanticCompilation.SyntaxTrees.Last();
    var semantic = semanticCompilation.GetSemanticModel(offTree);
    off = offTree.GetCompilationUnitRoot();
    // Bound the executable external surface before invoking either original or
    // counterfactual code. This finite supervisor is not an IO sandbox.
    foreach (var invocation in off.DescendantNodes().OfType<InvocationExpressionSyntax>())
    {
        if (semantic.GetSymbolInfo(invocation).Symbol is not IMethodSymbol symbol)
            throw new InvalidOperationException("Unresolved refactoring call symbol.");
        if (!symbol.ContainingAssembly.Equals(semanticCompilation.Assembly, SymbolEqualityComparer.Default)
            && !(symbol.ContainingType.ToDisplayString() == "System.Console" && symbol.Name == "WriteLine")
            && symbol.ContainingType.ToDisplayString() != "System.Math")
            throw new InvalidOperationException($"Unsupported finite refactoring call {symbol.ContainingType}.{symbol.Name}.");
    }
    if (off.DescendantNodes().Any(node => node is ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax))
        throw new InvalidOperationException("Unsupported finite scalar refactoring object construction.");
    MethodInfo Method(Assembly assembly, string target, int count)
    {
        var type = assembly.GetType($"{parsed.Name}.{parsed.Name}Module")
            ?? throw new InvalidOperationException("Refactoring CLR module missing.");
        var matches = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => method.Name == target).ToArray();
        if (matches.Length != 1 || matches[0].IsGenericMethod || matches[0].ReturnType != typeof(int)
            || !matches[0].GetParameters().Select(parameter => parameter.ParameterType)
                .SequenceEqual(Enumerable.Repeat(typeof(int), count)))
            throw new InvalidOperationException($"Wrong reflected refactoring signature for {target}.");
        return matches[0];
    }
    int[] values = [-2147483648, -2147483647, -1073741824, -1000001, -1000000, -999999,
        -46341, -46340, -101, -17, -2, -1, 0, 1, 2, 17, 101, 46340, 46341,
        999999, 1000000, 1000001, 1073741823, 2147483646, 2147483647];
    var cases = (names.Length == 1 ? values.Select(value => new[] { value }) :
        from first in values from second in values select new[] { first, second })
        .Where(input => profile != "ref007" || input.All(value => value >= -1000000 && value <= 1000000))
        .Select(input => (Input: input, Expected: profile switch
        {
            "ref001" => (long)input[0] * 2,
            "ref003" or "ref007" => (long)input[0] + input[1],
            "ref005" => (long)input[0] * input[1],
            _ => Math.Abs((long)input[0])
        })).Where(item => item.Expected >= int.MinValue && item.Expected <= int.MaxValue).ToArray();
    bool Behavior(MethodInfo method, bool logging = false)
    {
        var correct = true;
        foreach (var (input, expected) in cases)
        {
            var previous = Console.Out;
            var previousError = Console.Error;
            using var written = new BoundedOutput();
            using var errors = new BoundedOutput();
            object? actual;
            try
            {
                Console.SetOut(written);
                Console.SetError(errors);
                actual = method.Invoke(null, input.Select(value => (object)value).ToArray());
            }
            catch (TargetInvocationException error)
            {
                throw new InvalidOperationException("Refactoring runtime failed; exceptions are not dependence evidence.", error);
            }
            finally
            {
                Console.SetOut(previous);
                Console.SetError(previousError);
            }
            correct &= Equals(actual, (int)expected) && errors.ToString() == ""
                && written.ToString().Replace("\r\n", "\n", StringComparison.Ordinal)
                    == (logging ? input[0].ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n" : "");
        }
        return correct;
    }
    var offAssembly = CompileRuntime(off.ToFullString()).Assembly;
    if (!Behavior(Method(original, name, names.Length)) || !Behavior(Method(offAssembly, name, names.Length)))
        throw new InvalidOperationException("Wrong refactoring baseline behavior (original/contract-off output or purity).");
    var interventions = 0;
    if (profile == "ref001")
    {
        if (!Behavior(Method(original, "LogAndDouble", 1), true)
            || !Behavior(Method(offAssembly, "LogAndDouble", 1), true))
            throw new InvalidOperationException("Wrong LogAndDouble original/contract-off logging or doubling behavior.");
        var calls = off.DescendantNodes().OfType<InvocationExpressionSyntax>().Where(call =>
            semantic.GetSymbolInfo(call).Symbol is IMethodSymbol symbol && symbol.Name == "DoubleValue"
            && symbol.ContainingAssembly.Equals(semanticCompilation.Assembly, SymbolEqualityComparer.Default)).ToArray();
        foreach (var call in calls)
        {
            var counter = off.ReplaceNode(call, SyntaxFactory.ParseExpression($"unchecked(({call}) + 1)"));
            if (!Behavior(Method(CompileRuntime(counter.ToFullString()).Assembly, "LogAndDouble", 1), true))
                interventions++;
        }
        if (interventions == 0)
            throw new InvalidOperationException("DoubleValue call coupling failed: no supplying call changes successful LogAndDouble output.");
    }
    else
    {
        var declaration = off.DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Single(type => type.Identifier.ValueText == parsed.Name + "Module")
            .Members.OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == name);
        var returns = declaration.DescendantNodes().OfType<ReturnStatementSyntax>()
            .Where(statement => statement.Expression != null).ToArray();
        foreach (var statement in returns)
        {
            var counter = off.ReplaceNode(statement, statement.WithExpression(
                SyntaxFactory.ParseExpression($"unchecked(({statement.Expression}) + 1)")));
            if (!Behavior(Method(CompileRuntime(counter.ToFullString()).Assembly, name, names.Length)))
                interventions++;
        }
        if (interventions == 0)
            throw new InvalidOperationException("Refactoring supplying-return coupling failed.");
    }
    Console.WriteLine($"Verification passed: {name}; original native contracts/effects; {cases.Length} independent runtime cases; {interventions} successful source counterfactuals; original and contract-off behavior.");
    Console.WriteLine("Limits: finite Int32 representable-result cases, exact selected predicate proofs, bounded restored console; no overflow policy or universal body proof.");
}

static void VerifyDigitConversion(ModuleNode parsed, ModuleNode module, string generatedCode, string path)
{
    var functions = parsed.Functions.Where(function => function.Name == "TryParseDigit").ToArray();
    if (functions.Length != 1)
        throw new InvalidOperationException("Expected exactly one native refactoring function named TryParseDigit.");
    var function = functions[0];
    var output = SyntaxFactory.ParseTypeName(function.Output?.TypeName ?? "");
    var generic = output switch
    {
        GenericNameSyntax unqualified => unqualified,
        QualifiedNameSyntax { Right: GenericNameSyntax qualified } => qualified,
        _ => null
    };
    if (function.IsAsync || function.TypeParameters.Count != 0 || function.Parameters.Count != 1
        || AttributeHelper.ExpandType(function.Parameters[0].TypeName) != AttributeHelper.ExpandType("char")
        || function.Parameters[0].Modifier != Calor.Compiler.Ast.ParameterModifier.None
        || generic?.Identifier.ValueText != "Option" || generic.TypeArgumentList.Arguments.Count != 1
        || AttributeHelper.ExpandType(generic.TypeArgumentList.Arguments[0].ToString()) != "INT")
        throw new InvalidOperationException("Wrong native Option<int> digit-conversion signature.");
    var (original, trees) = CompileRuntime(generatedCode, warningsAsErrors: true);
    var debug = new CSharpEmitter(ContractMode.Debug) { LineDirectiveFilePath = path }.Emit(module);
    if (!SyntaxFactory.AreEquivalent(CSharpSyntaxTree.ParseText(debug).GetRoot(), trees[1].GetRoot()))
        throw new InvalidOperationException("Mapped selected digit emission differs from compiled C#.");
    var off = CSharpSyntaxTree.ParseText(
        new CSharpEmitter(ContractMode.Off) { LineDirectiveFilePath = path }.Emit(module)).GetCompilationUnitRoot();
    var semanticCompilation = CSharpCompilation.Create("DigitMapping",
        [trees[0], CSharpSyntaxTree.Create(off, (CSharpParseOptions)trees[0].Options)], GeneratedCSharpCompiler.References,
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    var tree = semanticCompilation.SyntaxTrees.Last();
    var semantic = semanticCompilation.GetSemanticModel(tree);
    off = tree.GetCompilationUnitRoot();
    MethodInfo Method(Assembly assembly)
    {
        var type = assembly.GetType($"{module.Name}.{module.Name}Module")
            ?? throw new InvalidOperationException("Digit CLR module missing.");
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => method.Name == "TryParseDigit").ToArray();
        if (methods.Length != 1 || methods[0].IsGenericMethod
            || !methods[0].GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual([typeof(char)]))
            throw new InvalidOperationException("Wrong reflected digit signature.");
        var closed = methods[0].ReturnType;
        if (!closed.IsGenericType || closed.ContainsGenericParameters || closed.Name != "Option`1"
            || !closed.GetGenericArguments().SequenceEqual([typeof(int)]))
            throw new InvalidOperationException("Wrong reflected closed Option<int> digit return.");
        var definition = closed.GetGenericTypeDefinition();
        if (closed.Assembly == assembly)
        {
            var carriers = parsed.Classes.Where(candidate => candidate.Name == "Option").ToArray();
            if (carriers.Length != 1 || carriers[0].TypeParameters.Count != 1
                || definition.FullName != $"{module.Name}.Option`1")
                throw new InvalidOperationException("Missing exact native auxiliary Option<T> declaration.");
            foreach (var factory in new[] { "Some", "None" })
                if (carriers[0].Methods.Count(method => method.Name == factory && method.IsStatic)
                    + parsed.Functions.Count(candidate => candidate.Name == factory) != 1)
                    throw new InvalidOperationException($"Missing native Option {factory} factory.");
        }
        else if (definition.Namespace != "Calor.Runtime"
            || definition.Assembly != typeof(Calor.Runtime.ContractKind).Assembly)
            throw new InvalidOperationException("Foreign CLR Option identity.");
        var some = closed.GetMethod("Some", BindingFlags.Public | BindingFlags.Static, [typeof(int)])
            ?? type.GetMethod("Some", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, [typeof(int)]);
        var none = closed.GetMethod("None", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes)
            ?? type.GetMethod("None", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, Type.EmptyTypes);
        if (some?.ReturnType != closed || none?.ReturnType != closed || some.IsGenericMethod || none.IsGenericMethod)
            throw new InvalidOperationException("Wrong typed Option Some/None factory API.");
        return methods[0];
    }
    const string csharpIdiom = """
        public static class CSharpDigitReference
        {
            public static bool TryParseDigit(char c, out int digit)
            {
                if (c >= '0' && c <= '9') { digit = c - '0'; return true; }
                digit = 0; return false;
            }
        }
        """;
    var reference = CompileRuntime(csharpIdiom).Assembly.GetType("CSharpDigitReference")!.GetMethod("TryParseDigit")!;
    var conversion = new CSharpToCalorConverter(new ConversionOptions { ModuleName = "DigitConversion" })
        .Convert(csharpIdiom, "CSharpDigitReference.cs");
    if (!conversion.Success || conversion.Ast == null || string.IsNullOrWhiteSpace(conversion.CalorSource))
        throw new InvalidOperationException("Actual C# digit idiom conversion failed.");
    RejectRawInterop(conversion.Ast, "native C# digit idiom conversion");
    var converted = Calor.Compiler.Program.Compile(conversion.CalorSource, "ConvertedDigit.calr",
        new Calor.Compiler.CompilationOptions { ContractMode = ContractMode.Debug });
    if (converted.HasErrors || converted.Ast == null)
        throw new InvalidOperationException($"Actual converted digit compilation failed:\n{string.Join("\n", converted.Diagnostics)}");
    RejectRawInterop(converted.Ast, "native C# digit idiom conversion");
    var convertedReference = CompileRuntime(converted.GeneratedCode).Assembly.GetTypes()
        .Single(type => type.Name == "CSharpDigitReference").GetMethod("TryParseDigit")
        ?? throw new InvalidOperationException("Actual converted C# digit signature missing.");
    if (convertedReference.ReturnType != typeof(bool) || !convertedReference.IsStatic
        || !convertedReference.GetParameters().Select(parameter => parameter.ParameterType)
            .SequenceEqual([typeof(char), typeof(int).MakeByRefType()])
        || !convertedReference.GetParameters()[1].IsOut)
        throw new InvalidOperationException("Actual converted C# digit out-parameter signature changed.");
    var expected = Enumerable.Range(0, 65536).Select(code =>
    {
        object[] arguments = [(char)code, 0];
        var present = (bool)reference.Invoke(null, arguments)!;
        object[] convertedArguments = [(char)code, 0];
        var convertedPresent = convertedReference.Invoke(null, convertedArguments);
        if (!Equals(convertedPresent, present) || !Equals(convertedArguments[1], arguments[1]))
            throw new InvalidOperationException($"Actual C# to native Calor conversion changed digit behavior at UTF-16 code {code}.");
        return (Present: present, Value: (int)arguments[1]);
    }).ToArray();
    bool Behavior(MethodInfo method)
    {
        var closed = method.ReturnType;
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
        var tags = new[] { "IsSome", "IsNone" }.SelectMany(name => closed.GetMember(name, flags)).ToArray();
        var payloads = new[] { "Value", "Unwrap" }.SelectMany(name => closed.GetMember(name, flags)).ToArray();
        object? Read(MemberInfo member, object value) => member switch
        {
            FieldInfo field when !field.IsStatic => field.GetValue(value),
            PropertyInfo property when property.GetMethod is { IsPublic: true, IsStatic: false }
                && property.GetIndexParameters().Length == 0 => property.GetValue(value),
            MethodInfo accessor when !accessor.IsStatic && !accessor.IsGenericMethod && accessor.GetParameters().Length == 0
                => accessor.Invoke(value, null),
            _ => throw new InvalidOperationException("Wrong Option observation API.")
        };
        if (tags.Length == 0 || payloads.Length == 0)
            throw new InvalidOperationException("Missing authoritative typed Option case/payload API.");
        var correct = true;
        for (var code = 0; code < expected.Length; code++)
        {
            var previous = Console.Out;
            var previousError = Console.Error;
            using var written = new BoundedOutput();
            using var errors = new BoundedOutput();
            try
            {
                Console.SetOut(written);
                Console.SetError(errors);
                var value = method.Invoke(null, [(char)code])
                    ?? throw new InvalidOperationException("Null is not an Option None value.");
                var observations = tags.Select(member => Read(member, value) is bool tag
                    ? member.Name == "IsSome" ? tag : !tag
                    : throw new InvalidOperationException("Wrong typed Option case API.")).ToArray();
                if (observations.Any(tag => tag != observations[0]))
                    throw new InvalidOperationException("Contradictory Option case observations.");
                correct &= observations[0] == expected[code].Present;
                if (observations[0])
                {
                    var values = payloads.Select(member => Read(member, value) is int payload ? payload
                        : throw new InvalidOperationException("Wrong typed Option active payload API.")).ToArray();
                    if (values.Any(payload => payload != values[0]))
                        throw new InvalidOperationException("Contradictory Option payload observations.");
                    correct &= values[0] == expected[code].Value;
                }
                correct &= written.ToString() == "" && errors.ToString() == "";
            }
            catch (TargetInvocationException error)
            {
                throw new InvalidOperationException("Digit runtime failed; exceptions are not dependence evidence.", error);
            }
            finally
            {
                Console.SetOut(previous);
                Console.SetError(previousError);
            }
        }
        return correct;
    }
    var factories = new List<(InvocationExpressionSyntax Call, string Name)>();
    foreach (var call in off.DescendantNodes().OfType<InvocationExpressionSyntax>())
    {
        if (semantic.GetSymbolInfo(call).Symbol is not IMethodSymbol symbol)
            throw new InvalidOperationException("Unresolved digit conversion call symbol.");
        if (symbol.Name is "Some" or "None" && (symbol.ContainingType.Name == "Option"
            || symbol.ContainingType.Name == module.Name + "Module"
                && symbol.ContainingAssembly.Equals(semanticCompilation.Assembly, SymbolEqualityComparer.Default)))
            factories.Add((call, symbol.Name));
        else if (!symbol.ContainingAssembly.Equals(semanticCompilation.Assembly, SymbolEqualityComparer.Default)
            && !(symbol.ContainingType.Name == "Option" && symbol.Name == "Unwrap"))
            throw new InvalidOperationException($"Unsupported finite digit call {symbol.ContainingType}.{symbol.Name}.");
    }
    foreach (var creation in off.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
        if (semantic.GetSymbolInfo(creation).Symbol is not IMethodSymbol constructor
            || !constructor.ContainingAssembly.Equals(semanticCompilation.Assembly, SymbolEqualityComparer.Default)
            || constructor.ContainingType.Name != "Option")
            throw new InvalidOperationException("Unsupported digit object construction.");
    if (off.DescendantNodes().OfType<ImplicitObjectCreationExpressionSyntax>().Any())
        throw new InvalidOperationException("Unsupported implicit digit object construction.");
    if (!Behavior(Method(original)) || !Behavior(Method(CompileRuntime(off.ToFullString(), warningsAsErrors: true).Assembly)))
        throw new InvalidOperationException("Wrong digit baseline case/payload behavior against actual C# out-parameter idiom.");
    var interventions = 0;
    foreach (var factory in new[] { "Some", "None" })
    {
        var supplying = 0;
        foreach (var (call, name) in factories.Where(candidate => candidate.Name == factory))
        {
            var symbol = semantic.GetSymbolInfo(call).Symbol as IMethodSymbol
                ?? throw new InvalidOperationException("Unresolved Option counterfactual factory.");
            var receiver = call.Expression switch
            {
                MemberAccessExpressionSyntax when symbol.ContainingType.Name == "Option"
                    && symbol.ContainingType.Arity == 0
                    => symbol.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ".",
                MemberAccessExpressionSyntax member => member.Expression + ".",
                IdentifierNameSyntax => "",
                _ => throw new InvalidOperationException("Unsupported Option factory receiver.")
            };
            var replacement = SyntaxFactory.ParseExpression(name == "Some"
                ? $"{receiver}None()" : $"{receiver}Some(7)");
            var counter = off.ReplaceNode(call, replacement);
            if (!Behavior(Method(CompileRuntime(counter.ToFullString(), warningsAsErrors: true).Assembly)))
                supplying++;
        }
        if (supplying == 0)
            throw new InvalidOperationException($"Option {factory} factory coupling failed: no supplying factory changes successful digit case/payload.");
        interventions += supplying;
    }
    Console.WriteLine($"Verification passed: TryParseDigit; native Option<int>/Some/None; 65536 independent C# out-parameter equivalence cases; actual C# to native Calor conversion; {interventions} successful factory counterfactuals; original and contract-off behavior.");
    Console.WriteLine("Limits: exhaustive UTF-16 chars, actual selected CLR carrier and native factory coupling; source-defined Option is not builtin support. No agent/provider or benchmark claims.");
}

static void VerifyEffects(string profile, string path)
{
    var name = profile switch
    {
        "eff001" => "SayHello", "eff002" => "ReadInput", "eff003" => "ReadFile",
        "eff004" => "WriteFile", "eff005" => "FetchUrl", "ref002" => "ProcessAndLog", _ => "LogToFileAndConsole"
    };
    string[] names = profile switch
    {
        "eff001" => ["name"], "eff002" => [], "eff003" => ["path"],
        "eff004" => ["path", "content"], "eff005" => ["url"], "ref002" => ["path"], _ => ["path", "message"]
    };
    string[] required = profile switch
    {
        "eff001" => ["console_write"], "eff002" => ["console_read"], "eff003" => ["file_read"],
        "eff004" => ["file_write"], "eff005" => ["network_read"], "ref002" => ["file_read", "console_write"], _ => ["console_write", "file_write"]
    };
    string[] operations = profile switch
    {
        "eff001" => ["WriteLine"], "eff002" => ["ReadLine"], "eff003" => ["ReadAllText"],
        "eff004" => ["WriteAllText"], "eff005" => [], "ref002" => ["ReadAllText", "WriteLine"], _ => ["WriteLine", "AppendAllText"]
    };
    var output = profile is "eff002" or "eff003" or "eff005" ? typeof(string) : typeof(void);
    var source = File.ReadAllText(path);
    var diagnostics = new DiagnosticBag();
    var parsed = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
    if (diagnostics.HasErrors)
        throw new InvalidOperationException($"Calor compilation failed (original parse):\n{string.Join("\n", diagnostics)}");
    var functions = parsed.Functions.Where(function => function.Name == name).ToArray();
    if (functions.Length != 1)
        throw new InvalidOperationException($"Expected exactly one native module function named {name}.");
    var function = functions[0];
    if (function.Visibility != Visibility.Public || function.IsAsync || function.TypeParameters.Count != 0
        || function.Output == null
        || AttributeHelper.ExpandType(function.Output.TypeName) != AttributeHelper.ExpandType(output == typeof(void) ? "void" : "str")
        || !function.Parameters.Select(parameter => parameter.Name).SequenceEqual(names)
        || function.Parameters.Any(parameter => AttributeHelper.ExpandType(parameter.TypeName) != AttributeHelper.ExpandType("str")
            || parameter.Modifier != Calor.Compiler.Ast.ParameterModifier.None))
        throw new InvalidOperationException($"Wrong public effects signature for {name}.");
    var declared = function.Effects?.Effects.TryGetValue("io", out var effects) == true
        ? effects.Split(',').ToHashSet(StringComparer.Ordinal) : new HashSet<string>(StringComparer.Ordinal);
    bool Declares(string effect) => declared.Contains(effect) || effect switch
    {
        "file_read" => declared.Contains("filesystem_read") || declared.Contains("filesystem_readwrite"),
        "file_write" => declared.Contains("filesystem_write") || declared.Contains("filesystem_readwrite"),
        "network_read" => declared.Contains("network") || declared.Contains("network_readwrite"),
        _ => false
    };
    if (required.Any(effect => !Declares(effect)))
        throw new InvalidOperationException($"Original declared effects missing for {name}: {string.Join(",", required)}.");
    var pending = new Stack<AstNode>();
    var seen = new HashSet<AstNode>(ReferenceEqualityComparer.Instance);
    pending.Push(parsed);
    while (pending.TryPop(out var node))
    {
        if (!seen.Add(node))
            continue;
        if (node is CSharpInteropBlockNode or RawCSharpNode or RawCSharpExpressionNode)
            throw new InvalidOperationException("Raw interop cannot establish native effects behavior.");
        foreach (var child in RecursiveAstWalker.GetAllChildren(node))
            pending.Push(child);
        if (node is LocalFunctionStatementNode local)
            pending.Push(local.Function);
    }
    var compilation = Calor.Compiler.Program.Compile(source, path,
        new Calor.Compiler.CompilationOptions { ContractMode = ContractMode.Debug });
    if (compilation.HasErrors || compilation.Ast == null)
        throw new InvalidOperationException($"Calor compilation failed:\n{string.Join("\n", compilation.Diagnostics)}");
    var (assembly, trees) = CompileRuntime(compilation.GeneratedCode);
    MethodInfo Method(Assembly candidate)
    {
        var type = candidate.GetType($"{parsed.Name}.{parsed.Name}Module")
            ?? throw new InvalidOperationException("Effects CLR module missing.");
        var matches = type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => method.Name == name).ToArray();
        if (!type.IsPublic || matches.Length != 1 || matches[0].IsGenericMethod || matches[0].ReturnType != output
            || !matches[0].GetParameters().Select(parameter => parameter.Name).SequenceEqual(names)
            || matches[0].GetParameters().Any(parameter => parameter.ParameterType != typeof(string)))
            throw new InvalidOperationException($"Wrong reflected CLR effects signature for {name}.");
        return matches[0];
    }
    var original = Method(assembly);
    MethodInfo? contractOff = null;
    if (profile == "ref002")
    {
        var mapped = new CSharpEmitter(ContractMode.Debug) { LineDirectiveFilePath = path }.Emit(compilation.Ast);
        if (!SyntaxFactory.AreEquivalent(CSharpSyntaxTree.ParseText(mapped).GetRoot(), trees[1].GetRoot()))
            throw new InvalidOperationException("Mapped selected effects emission differs from compiled C#.");
        var disabled = new CSharpEmitter(ContractMode.Off) { LineDirectiveFilePath = path }.Emit(compilation.Ast);
        var offRuntime = CompileRuntime(disabled);
        contractOff = Method(offRuntime.Assembly);
        trees = offRuntime.Trees;
    }
    var root = trees[1].GetCompilationUnitRoot();
    var semanticCompilation = CSharpCompilation.Create("EffectsMapping", trees,
        GeneratedCSharpCompiler.References, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    var semantic = semanticCompilation.GetSemanticModel(trees[1]);
    var witnesses = new List<(InvocationExpressionSyntax Call, string Operation)>();
    foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
    {
        if (semantic.GetSymbolInfo(invocation).Symbol is not IMethodSymbol symbol)
            throw new InvalidOperationException("Unresolved effects call symbol.");
        var owner = symbol.ContainingType.ToDisplayString();
        if (symbol.ContainingAssembly.Equals(semanticCompilation.Assembly, SymbolEqualityComparer.Default))
            continue;
        if (symbol.MethodKind == MethodKind.DelegateInvoke
            && invocation.Expression is IdentifierNameSyntax receiver
            && semantic.GetSymbolInfo(receiver).Symbol is ILocalSymbol local
            && local.DeclaringSyntaxReferences.Select(reference => reference.GetSyntax())
                .OfType<VariableDeclaratorSyntax>().Any(variable => variable.Initializer?.Value is LambdaExpressionSyntax))
            continue;
        var supported = owner == "System.Console" && symbol.Name is "WriteLine" or "ReadLine" or "Write"
            || owner == "System.IO.File" && symbol.Name is "ReadAllText" or "WriteAllText" or "AppendAllText" or "ReadAllBytes" or "WriteAllBytes";
        if (supported)
        {
            if (profile == "eff005")
                throw new InvalidOperationException("FetchUrl is a simulation; actual IO is not requested.");
            var types = symbol.Parameters.Select(parameter => parameter.Type.ToDisplayString()).ToArray();
            var bytes = symbol.Name is "ReadAllBytes" or "WriteAllBytes";
            var strings = symbol.Name is "WriteAllText" or "AppendAllText" ? 2 : symbol.Name == "ReadLine" ? 0 : 1;
            if (types.Length < strings || types.Take(strings).Any(type => type is not ("string" or "string?"))
                || bytes && (types.Length != (symbol.Name == "ReadAllBytes" ? 1 : 2)
                    || symbol.Name == "WriteAllBytes" && types[^1] != "byte[]")
                || !bytes && types.Length > strings && (owner != "System.IO.File" || types.Length != strings + 1
                    || types[^1] != "System.Text.Encoding")
                || invocation.ArgumentList.Arguments.Any(argument => argument.NameColon != null || argument.RefKindKeyword.Kind() != SyntaxKind.None))
                throw new InvalidOperationException("Unsupported finite effects BCL overload instrumentation.");
            witnesses.Add((invocation, symbol.Name));
        }
        else if (!(owner == "System.String" || owner == "string" || owner == "System.Math"
            || owner == "System.Text.Encoding" || owner == "System.Text.StringBuilder"
            || owner == "System.Collections.Generic.List<byte>" && symbol.Name is "Add" or "ToArray"
            || owner.StartsWith("Calor.Runtime.", StringComparison.Ordinal)))
            throw new InvalidOperationException($"Unsupported effects call {owner}.{symbol.Name}; no arbitrary IO/service execution is permitted.");
    }
    if (operations.Any(operation => !witnesses.Any(witness => witness.Operation == operation)))
        throw new InvalidOperationException("Required resolved BCL effects operation missing.");
    if (root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>().Any(creation =>
            semantic.GetSymbolInfo(creation).Symbol is not IMethodSymbol constructor
            || !(constructor.ContainingAssembly.Equals(semanticCompilation.Assembly, SymbolEqualityComparer.Default)
                || constructor.ContainingType.ToDisplayString() is "System.Exception" or "System.Text.StringBuilder" or "System.Collections.Generic.List<byte>"
                || constructor.ContainingType.ToDisplayString().StartsWith("Calor.Runtime.", StringComparison.Ordinal)))
        || root.DescendantNodes().OfType<ImplicitObjectCreationExpressionSyntax>().Any())
        throw new InvalidOperationException("Unsupported effects object construction; finite supervision is not a sandbox.");
    var instrumented = root.ReplaceNodes(witnesses.Select(witness => witness.Call), (before, rewritten) =>
        rewritten.WithExpression(SyntaxFactory.ParseExpression("global::EffectsTaskProbe." +
            witnesses.Single(witness => witness.Call == before).Operation)));
    instrumented = instrumented.AddMembers(SyntaxFactory.ParseMemberDeclaration(EffectsTaskProbe.Source)
        ?? throw new InvalidOperationException("Cannot parse effects instrumentation."));
    var instrumentedAssembly = CompileRuntime(instrumented.ToFullString()).Assembly;
    var instrumentedMethod = Method(instrumentedAssembly);
    var probe = instrumentedAssembly.GetType("EffectsTaskProbe")
        ?? throw new InvalidOperationException("Effects probe missing.");
    var reset = probe.GetMethod("Reset") ?? throw new InvalidOperationException("Effects reset missing.");
    var events = probe.GetField("Events") ?? throw new InvalidOperationException("Effects events missing.");
    string[] values = ["", "alpha", "BETA", " \t ", "line\nbreak", "\r\n", "caf\u00e9", "\u4e2d\ud83d\ude00"];
    string[] lines = ["", "alpha", "BETA", " \t ", "caf\u00e9", "\u4e2d\ud83d\ude00"];
    var directory = Directory.CreateTempSubdirectory("calor-effects-owned-");
    try
    {
        var paths = new[] { Path.Combine(directory.FullName, "first \u00e9.txt"), Path.Combine(directory.FullName, "second \u4e2d.txt") };
        var sentinel = Path.Combine(directory.FullName, "unrelated.txt");
        var cases = (from index in Enumerable.Range(0, 2)
                     from value in profile == "eff002" ? lines : values
                     from seed in profile is "eff003" or "eff004" or "eff006" or "ref002" ? values : new[] { "seed" }
                     select (Index: index, Value: value, Seed: seed)).ToArray();
        bool Execute(MethodInfo method, string mode, int index, string value, string seed, bool traced)
        {
            File.WriteAllText(paths[0], seed);
            File.WriteAllText(paths[1], value + "\nindependent second seed");
            File.WriteAllText(sentinel, "unrelated sentinel");
            var before = paths.Select(File.ReadAllText).ToArray();
            var previousIn = Console.In;
            var previousOut = Console.Out;
            var previousError = Console.Error;
            using var input = new StringReader(value + "\nindependent next line\n");
            using var written = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
            using var errors = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
            object? result;
            reset.Invoke(null, [mode, paths]);
            try
            {
                Console.SetIn(input);
                Console.SetOut(written);
                Console.SetError(errors);
                object[] arguments = profile switch
                {
                    "eff001" => [value], "eff002" => [], "eff003" or "ref002" => [paths[index]],
                    "eff004" or "eff006" => [paths[index], value], _ => [value]
                };
                result = method.Invoke(null, arguments);
            }
            catch (TargetInvocationException error)
            {
                throw new InvalidOperationException(mode == "" ? "Effects baseline runtime failed."
                    : "Effects counterfactual runtime failed; exceptions are not dependence evidence.", error);
            }
            finally
            {
                Console.SetIn(previousIn);
                Console.SetOut(previousOut);
                Console.SetError(previousError);
            }
            if (traced && mode == "")
            {
                var trace = (List<string[]>)events.GetValue(null)!;
                foreach (var operation in operations)
                {
                    var matching = trace.Where(entry => entry[0] == operation).ToArray();
                    if (matching.Length == 0 || matching.Any(entry => operation switch
                    {
                        "WriteLine" => entry[1] != (profile == "ref002" ? before[index] : value),
                        "ReadLine" => entry[1] != value,
                        "ReadAllText" => entry[1] != paths[index] || entry[2] != before[index],
                        _ => entry[1] != paths[index] || entry[2] != value
                    }))
                        throw new InvalidOperationException("Effects BCL provenance/arguments differ from supplied inputs.");
                }
            }
            var expectedConsole = profile == "ref002" ? before[index] + written.NewLine
                : profile is "eff001" or "eff006" ? value + written.NewLine : "";
            var expectedFile = profile == "eff004" ? value : profile == "eff006" ? before[index] + value : before[index];
            var expectedReturn = profile switch
            {
                "eff002" => value, "eff003" => before[index], "eff005" => "Response from: " + value, _ => null
            };
            var correct = Equals(result, expectedReturn) && written.ToString() == expectedConsole
                && errors.ToString() == "" && File.ReadAllText(paths[index]) == expectedFile
                && File.ReadAllText(paths[1 - index]) == before[1 - index]
                && File.ReadAllText(sentinel) == "unrelated sentinel";
            return correct;
        }
        // Guarded, instrumented correctness and provenance precede original IO.
        foreach (var (index, value, seed) in cases)
            if (!Execute(instrumentedMethod, "", index, value, seed, true))
                throw new InvalidOperationException("Wrong instrumented effects baseline behavior/state.");
        foreach (var (index, value, seed) in cases)
            if (!Execute(original, "", index, value, seed, false))
                throw new InvalidOperationException("Wrong original effects baseline behavior/state.");
        if (contractOff != null)
            foreach (var (index, value, seed) in cases)
                if (!Execute(contractOff, "", index, value, seed, false))
                    throw new InvalidOperationException("Wrong contract-off effects baseline behavior/state.");
        foreach (var operation in operations)
        {
            var changed = false;
            foreach (var (index, value, seed) in cases)
                changed |= !Execute(instrumentedMethod, operation, index, value, seed, false);
            if (!changed)
                throw new InvalidOperationException($"Effects coupling failed: {operation} does not contribute to successful output/state.");
        }
        Console.WriteLine($"Verification passed: {name}; original declared effects, native/CLR signatures, {cases.Length} runtime cases and BCL output/state coupling.");
        Console.WriteLine(profile == "ref002"
            ? "Limits: finite non-null strings and owned IO, deterministic restored console; no universal body proof, real network or encoding policy."
            : "Limits: finite non-null strings and owned IO, deterministic restored console; process supervision, not a sandbox or universal body proof. No real network, EOF/null, missing-file or encoding policy.");
    }
    finally
    {
        directory.Delete(true);
    }
}

static (Assembly Assembly, SyntaxTree[] Trees) CompileRuntime(string generatedCode, bool warningsAsErrors = false)
{
    var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
    SyntaxTree[] trees =
    [
        CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble, parseOptions),
        CSharpSyntaxTree.ParseText(generatedCode, parseOptions)
    ];
    var runtime = CSharpCompilation.Create(
        "LogicTaskRuntime", trees, GeneratedCSharpCompiler.References,
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
            nullableContextOptions: warningsAsErrors ? NullableContextOptions.Enable : NullableContextOptions.Disable,
            generalDiagnosticOption: warningsAsErrors ? ReportDiagnostic.Error : ReportDiagnostic.Default));
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
    RejectRawInterop(module, "generic behavior");
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

static void RejectRawInterop(ModuleNode module, string purpose)
{
    var pending = new Stack<AstNode>();
    var seen = new HashSet<AstNode>(ReferenceEqualityComparer.Instance);
    pending.Push(module);
    while (pending.TryPop(out var node))
    {
        if (!seen.Add(node))
            continue;
        if (node is CSharpInteropBlockNode or RawCSharpNode or RawCSharpExpressionNode)
            throw new InvalidOperationException($"Raw interop cannot establish native {purpose}.");
        foreach (var child in RecursiveAstWalker.GetAllChildren(node))
            pending.Push(child);
        if (node is LocalFunctionStatementNode local)
            pending.Push(local.Function);
    }
}

static void VerifyOop(string profile, ModuleNode module, string generatedCode, string path)
{
    const BindingFlags members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
        | BindingFlags.Static | BindingFlags.DeclaredOnly;
    var name = profile switch
    {
        "oop001" => "Person", "oop002" => "IRepository", "oop003" => "Calculator",
        "oop004" => "Product", "oop005" => "Counter", _ => "Point"
    };
    void Require(bool condition, string detail)
    {
        if (!condition)
            throw new InvalidOperationException($"{name}: {detail}.");
    }
    bool Integer(string type) => AttributeHelper.ExpandType(type) == "INT";
    bool Parameters(IReadOnlyList<ParameterNode> parameters, string[] names) =>
        parameters.Select(parameter => parameter.Name).SequenceEqual(names)
        && parameters.All(parameter => Integer(parameter.TypeName)
            && parameter.Modifier == Calor.Compiler.Ast.ParameterModifier.None);
    bool ReflectedParameters(ParameterInfo[] parameters, string[] names) =>
        parameters.Select(parameter => parameter.Name).SequenceEqual(names)
        && parameters.All(parameter => parameter.ParameterType == typeof(int));
    RejectRawInterop(module, "OOP declarations");
    var (assembly, trees) = CompileRuntime(generatedCode);
    var mapped = CSharpSyntaxTree.ParseText(
        new CSharpEmitter(ContractMode.Debug) { LineDirectiveFilePath = path }.Emit(module),
        new CSharpParseOptions(LanguageVersion.Preview));
    Require(SyntaxFactory.AreEquivalent(trees[1].GetRoot(), mapped.GetRoot()),
        "source-mapped native emission differs from independently compiled C#");
    var type = assembly.GetType($"{module.Name}.{name}")
        ?? throw new InvalidOperationException($"{name}: missing reflected CLR type.");
    Require(type.IsPublic && !type.IsGenericType, "wrong public reflected CLR identity");
    var caseCount = 0;
    if (profile == "oop002")
    {
        var declarations = module.Interfaces.Where(candidate => candidate.Name == name).ToArray();
        Require(declarations.Length == 1, "Expected exactly one native interface");
        var declaration = declarations[0];
        // Native interfaces have no visibility slot; the selected emitter makes them public.
        Require(declaration.TypeParameters.Count == 0 && type.IsInterface, "wrong native public interface identity");
        var signatures = declaration.Methods.Where(method => method.Name == "GetById"
            && method.TypeParameters.Count == 0 && Parameters(method.Parameters, ["id"])
            && Regex.Replace(method.Output?.TypeName ?? "", @"\s+", "") == "Option<Entity>").ToArray();
        Require(signatures.Length == 1, "wrong native GetById signature; exact Option<Entity> required");
        var signature = signatures[0];
        CheckScalarEntryDomain(signature.Parameters, signature.Preconditions, "GetById");
        var methods = type.GetMethods(members).Where(method => method.Name == "GetById"
            && ReflectedParameters(method.GetParameters(), ["id"])).ToArray();
        Require(methods.Length == 1, "Expected exactly one reflected GetById signature");
        var method = methods[0];
        var option = assembly.GetType($"{module.Name}.Option`1");
        var entity = assembly.GetType($"{module.Name}.Entity");
        Require(method.IsPublic && method.IsAbstract && !method.IsStatic && !method.IsGenericMethod
            && method.GetMethodBody() == null && ReflectedParameters(method.GetParameters(), ["id"])
            && method.ReturnType.IsConstructedGenericType
            && method.ReturnType.GetGenericTypeDefinition().Name == "Option`1"
            && method.ReturnType.GetGenericArguments().Length == 1
            && method.ReturnType.GetGenericArguments()[0].Name == "Entity"
            && (option == null || method.ReturnType.GetGenericTypeDefinition() == option)
            && (entity == null || method.ReturnType.GetGenericArguments()[0] == entity),
            "wrong abstract reflected closed Option<Entity> signature");
        var syntax = mapped.GetRoot().DescendantNodes().OfType<InterfaceDeclarationSyntax>()
            .Single(candidate => candidate.Identifier.ValueText == name);
        var emitted = syntax.Members.OfType<MethodDeclarationSyntax>()
            .Where(candidate => candidate.Identifier.ValueText == "GetById"
                && candidate.ParameterList.Parameters.Count == 1
                && candidate.ParameterList.Parameters[0].Identifier.ValueText == "id"
                && SyntaxFactory.AreEquivalent(candidate.ReturnType, SyntaxFactory.ParseTypeName("Option<Entity>"))).ToArray();
        Require(emitted.Length == 1 && emitted[0].Body == null && emitted[0].ExpressionBody == null,
            "interface signature has an implementation");
    }
    else
    {
        var declarations = module.Classes.Where(candidate => candidate.Name == name).ToArray();
        Require(declarations.Length == 1, "Expected exactly one native class");
        var declaration = declarations[0];
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(File.ReadAllText(path), diagnostics).TokenizeAllForParser();
        Require(!diagnostics.HasErrors && tokens.Any(token => token.Kind == TokenKind.Class
            && token.Span.Start == declaration.Span.Start), "expected a native class declaration, not a named module");
        Require(declaration.Visibility == Visibility.Public && !declaration.IsStruct
            && declaration.TypeParameters.Count == 0 && type.IsClass, "wrong native public class identity");
        var syntax = mapped.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Single(candidate => candidate.Identifier.ValueText == name);
        int[] values = [int.MinValue, int.MinValue + 1, -1073741824, -100, -7, -1, 0, 1, 7, 100,
            1073741823, int.MaxValue - 1, int.MaxValue];
        object Construct()
        {
            var constructors = type.GetConstructors(members).Where(constructor => !constructor.IsStatic).ToArray();
            var empty = constructors.SingleOrDefault(constructor => constructor.GetParameters().Length == 0);
            if (empty != null)
                return empty.Invoke([]);
            var finite = constructors.Where(constructor => constructor.GetParameters().Length is 1 or 2
                && constructor.GetParameters().All(parameter => parameter.ParameterType == typeof(int))).ToArray();
            if (finite.Length != 1)
                throw new InvalidOperationException($"{name}: Unsupported finite constructor setup: need a real zero-argument or unambiguous one/two-integer constructor.");
            return finite[0].Invoke(new object[] { -7, 11 }.Take(finite[0].GetParameters().Length).ToArray());
        }
        FieldInfo Field(string fieldName)
        {
            var fields = declaration.Fields.Where(field => field.Name == fieldName).ToArray();
            Require(fields.Length == 1 && fields[0].Visibility == Visibility.Private && Integer(fields[0].TypeName),
                $"wrong native private integer field {fieldName}");
            var field = type.GetField(fieldName, members);
            Require(field != null && field.IsPrivate && field.FieldType == typeof(int)
                && field.IsStatic == (fields[0].IsStatic || fields[0].Modifiers.HasFlag(MethodModifiers.Const)),
                $"wrong reflected private integer field {fieldName}");
            return field!;
        }
        if (profile == "oop001")
        {
            Require(declaration.Fields.Count + declaration.Properties.Count + declaration.Indexers.Count
                + declaration.Methods.Count + declaration.Constructors.Count + declaration.Events.Count
                + declaration.OperatorOverloads.Count + declaration.NestedClasses.Count
                + declaration.NestedInterfaces.Count + declaration.NestedEnums.Count
                + declaration.NestedDelegates.Count + declaration.PreprocessorBlocks.Count == 0
                && declaration.Items.Count == 0 && syntax.Members.Count == 0, "Person must have no source members");
            Require(type.GetMembers(members).All(member => member is ConstructorInfo constructor
                && !constructor.IsStatic && constructor.GetParameters().Length == 0),
                "Person has non-synthesized CLR members");
        }
        else if (profile == "oop005")
            _ = Field("_count");
        else if (profile == "oop003")
        {
            var methods = declaration.Methods.Where(method => method.Name == "Add"
                && method.Visibility == Visibility.Public && !method.IsAsync && method.TypeParameters.Count == 0
                && Parameters(method.Parameters, ["a", "b"]) && Integer(method.Output?.TypeName ?? "")).ToArray();
            Require(methods.Length == 1, "wrong native public Add signature");
            var native = methods[0];
            CheckScalarEntryDomain(native.Parameters, native.Preconditions, "Add");
            var reflected = type.GetMethods(members).Where(method => method.Name == "Add"
                && ReflectedParameters(method.GetParameters(), ["a", "b"])).ToArray();
            Require(reflected.Length == 1, "Expected exactly one reflected Add method");
            var method = reflected[0];
            Require(method.IsPublic && !method.IsGenericMethod && method.ReturnType == typeof(int)
                && method.IsStatic == native.IsStatic && ReflectedParameters(method.GetParameters(), ["a", "b"]),
                "wrong reflected public Add signature");
            var instance = method.IsStatic ? null : Construct();
            foreach (var a in values)
                foreach (var b in values)
                {
                    var sum = (long)a + b;
                    if (sum < int.MinValue || sum > int.MaxValue)
                        continue;
                    Require(Equals(method.Invoke(instance, [a, b]), (int)sum), $"Add({a}, {b}) expected {sum}");
                    caseCount++;
                }
        }
        else if (profile == "oop006")
        {
            var xField = Field("_x");
            var yField = Field("_y");
            var constructors = declaration.Constructors.Where(constructor => constructor.Visibility == Visibility.Public
                && !constructor.IsStatic && Parameters(constructor.Parameters, ["x", "y"])).ToArray();
            Require(constructors.Length == 1, "Expected exactly one native public Point(x, y) constructor");
            CheckScalarEntryDomain(constructors[0].Parameters, constructors[0].Preconditions, "Point");
            var constructor = type.GetConstructor(members, null, [typeof(int), typeof(int)], null);
            Require(constructor != null && constructor.IsPublic
                && ReflectedParameters(constructor.GetParameters(), ["x", "y"]), "wrong reflected public Point constructor");
            foreach (var x in values)
                foreach (var y in values)
                {
                    var instance = constructor!.Invoke([x, y]);
                    Require(Equals(xField.GetValue(instance), x) && Equals(yField.GetValue(instance), y),
                        $"Point({x}, {y}) expected actual _x/_y state");
                    // Static fields are not prohibited by the prompt. Only instance fields promise isolation.
                    var other = constructor.Invoke([y, x]);
                    Require(Equals(xField.GetValue(other), y) && Equals(yField.GetValue(other), x)
                        && (xField.IsStatic || Equals(xField.GetValue(instance), x))
                        && (yField.IsStatic || Equals(yField.GetValue(instance), y)), "Point changed isolated instance fields");
                    caseCount += 2;
                }
        }
        else
        {
            var properties = declaration.Properties.Where(property => property.Name == "Price").ToArray();
            Require(properties.Length == 1, "Expected exactly one native Price property");
            var native = properties[0];
            Require(native.Visibility == Visibility.Public && Integer(native.TypeName) && native.IsAutoProperty
                && new[] { native.Getter, native.Setter, native.Initer }.Any(accessor => accessor != null),
                "Price must be a native public integer auto-property");
            foreach (var accessor in new[] { native.Getter, native.Setter, native.Initer }.OfType<PropertyAccessorNode>())
                CheckScalarEntryDomain([], accessor.Preconditions, "Price");
            var emitted = syntax.Members.OfType<PropertyDeclarationSyntax>()
                .Where(property => property.Identifier.ValueText == "Price").ToArray();
            Require(emitted.Length == 1 && emitted[0].ExpressionBody == null
                && emitted[0].AccessorList is { Accessors.Count: > 0 } accessors
                && accessors.Accessors.All(accessor => accessor.Body == null && accessor.ExpressionBody == null
                    && !accessor.SemicolonToken.IsMissing), "Price lacks emitted auto-property provenance");
            var property = type.GetProperty("Price", members);
            var storage = type.GetField("<Price>k__BackingField", members);
            Require(property != null && property.PropertyType == typeof(int) && property.GetIndexParameters().Length == 0
                && property.GetAccessors(true).Any(accessor => accessor.IsPublic)
                && storage != null && storage.IsPrivate && storage.FieldType == typeof(int)
                && storage.IsStatic == native.IsStatic
                && storage.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false),
                "wrong reflected Price/backing-field identity");
            var get = property!.GetGetMethod(true);
            var set = property.GetSetMethod(true);
            Require((get != null) == (native.Getter != null)
                && (set != null) == (native.Setter != null || native.Initer != null)
                && property.GetAccessors(true).All(accessor => accessor.IsStatic == native.IsStatic
                    && accessor.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false)),
                "wrong reflected auto-accessor identity");
            var instance = native.IsStatic ? null : Construct();
            var other = native.IsStatic ? null : Construct();
            var untouched = native.IsStatic ? null : storage!.GetValue(other);
            if (storage!.IsStatic && storage.IsInitOnly)
            {
                Require(get != null && Equals(get.Invoke(null, []), storage.GetValue(null)),
                    "Price getter expected actual readonly static backing state");
                caseCount++;
            }
            else
                foreach (var value in values)
                {
                    storage.SetValue(instance, value);
                    if (get != null)
                        Require(Equals(get.Invoke(instance, []), value), "Price getter expected actual backing state");
                    if (set != null)
                    {
                        storage.SetValue(instance, value == 0 ? 1 : 0);
                        set.Invoke(instance, [value]);
                        Require(Equals(storage.GetValue(instance), value), "Price setter expected actual backing state");
                        if (get != null)
                            Require(Equals(get.Invoke(instance, []), value), "Price getter/setter state mismatch");
                    }
                    Require(native.IsStatic || Equals(storage.GetValue(other), untouched),
                        "Price changed another instance");
                    caseCount++;
                }
        }
    }
    Console.WriteLine($"Verification passed: {name}; native/reflected OOP identity; {caseCount} finite runtime cases.");
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

static void VerifyCollection(string profile, string path)
{
    var name = profile switch
    {
        "coll001" => "SumList", "coll002" => "CreateScores", "coll003" => "UniqueCount",
        "coll004" => "BuildCollection", "coll005" => "HasValue", _ => "GetListLength"
    };
    string[] names = name == "HasValue" ? ["value"] : name == "GetListLength" ? ["n"] : [];
    var outputType = name == "HasValue" ? "bool" : "i32";
    var compiled = Calor.Compiler.Program.Compile(File.ReadAllText(path), path,
        new Calor.Compiler.CompilationOptions { ContractMode = ContractMode.Debug });
    if (compiled.HasErrors || compiled.Ast == null)
        throw new InvalidOperationException($"Calor compilation failed:\n{string.Join("\n", compiled.Diagnostics)}");
    var module = compiled.Ast;
    var selected = module.Functions.Where(candidate => candidate.Name == name).ToArray();
    if (selected.Length != 1)
        throw new InvalidOperationException($"Expected exactly one module function named {name}.");
    var function = selected[0];
    if (function.Visibility != Visibility.Public || function.IsAsync || function.TypeParameters.Count != 0
        || function.Output == null
        || AttributeHelper.ExpandType(function.Output.TypeName) != AttributeHelper.ExpandType(outputType)
        || !function.Parameters.Select(parameter => parameter.Name).SequenceEqual(names)
        || function.Parameters.Any(parameter => AttributeHelper.ExpandType(parameter.TypeName) != "INT"
            || parameter.Modifier != Calor.Compiler.Ast.ParameterModifier.None))
        throw new InvalidOperationException($"Wrong public signature for {name}.");
    CheckWritingContracts(function, names, outputType);
    var (assembly, trees) = CompileRuntime(compiled.GeneratedCode);
    MethodInfo Target(Assembly runtime)
    {
        var type = runtime.GetType($"{module.Name}.{module.Name}Module")
            ?? throw new InvalidOperationException("Collection module CLR identity missing.");
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(candidate => candidate.Name == name).ToArray();
        if (!type.IsPublic || methods.Length != 1 || methods[0].IsGenericMethod
            || methods[0].ReturnType != (outputType == "bool" ? typeof(bool) : typeof(int))
            || !methods[0].GetParameters().Select(parameter => parameter.ParameterType)
                .SequenceEqual(names.Select(_ => typeof(int))))
            throw new InvalidOperationException("Wrong generated public/static collection signature.");
        return methods[0];
    }
    var cases = name == "HasValue"
        ? new[] { int.MinValue, int.MinValue + 1, -1000000, -31, -10, -1, 0, 1, 9, 10, 11, 19,
            20, 21, 29, 30, 31, 100, 1000000, int.MaxValue - 1, int.MaxValue }
        : name == "GetListLength" ? new[] { 0, 1, 2, 3, 5, 7, 16, 31, 64, 127, 257 } : new[] { 0 };
    object Expected(int input) => name switch
        { "HasValue" => input is 10 or 20 or 30, "GetListLength" => input, "SumList" => 60,
            "BuildCollection" => 5, _ => 3 };
    object[] Arguments(int input) => names.Length == 0 ? [] : [input];
    void Check(MethodInfo method, int input, string stage)
    {
        var actual = method.Invoke(null, Arguments(input));
        if (!Equals(actual, Expected(input)))
            throw new InvalidOperationException($"Collection {stage}: {name}({input}) returned {actual}; expected {Expected(input)}.");
    }
    var baseline = Target(assembly);
    foreach (var input in cases)
        Check(baseline, input, "runtime");
    if (name == "GetListLength")
        foreach (var input in new[] { int.MinValue, -17, -1 })
        {
            try
            {
                baseline.Invoke(null, [input]);
            }
            catch (TargetInvocationException error) when (error.InnerException is Calor.Runtime.ContractViolationException
                || error.InnerException is ArgumentOutOfRangeException { ParamName: "n" }
                    && function.Parameters.Single().InlineRefinement != null)
            {
                continue;
            }
            throw new InvalidOperationException("GetListLength must reject negative inputs with a native contract.");
        }

    var options = new CSharpParseOptions(LanguageVersion.Preview);
    var debug = CSharpSyntaxTree.ParseText(
        new CSharpEmitter(ContractMode.Debug) { LineDirectiveFilePath = path }.Emit(module), options);
    if (!SyntaxFactory.AreEquivalent(trees[1].GetRoot(), debug.GetRoot()))
        throw new InvalidOperationException("Collection native mapping failed: debug emission differs from compiled C#.");
    var mapped = CSharpSyntaxTree.ParseText(
        new CSharpEmitter(ContractMode.Off) { LineDirectiveFilePath = path }.Emit(module), options);
    var mappingCompilation = CSharpCompilation.Create("CollectionMapping",
        [trees[0], mapped], GeneratedCSharpCompiler.References,
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    var probe = "__CalorCollections_" + Guid.NewGuid().ToString("N");
    var rewriter = new CollectionRewriter(mappingCompilation.GetSemanticModel(mapped), module, path, probe);
    var instrumented = rewriter.Visit(mapped.GetRoot()) as CompilationUnitSyntax
        ?? throw new InvalidOperationException("Collection native instrumentation failed.");
    instrumented = instrumented.AddMembers(SyntaxFactory.ParseMemberDeclaration(CollectionRewriter.Probe(probe))
        ?? throw new InvalidOperationException("Collection probe declaration failed."));
    var (tracedAssembly, _) = CompileRuntime(instrumented.NormalizeWhitespace().ToFullString());
    var traced = Target(tracedAssembly);
    var probeType = tracedAssembly.GetType(probe)
        ?? throw new InvalidOperationException("Collection probe identity missing.");
    var records = probeType.GetField("Records")?.GetValue(null)
        as List<(object Collection, string Operation, object? Key, object? Value, object[] State, int Depth)>
        ?? throw new InvalidOperationException("Collection trace records missing.");
    var reset = probeType.GetMethod("Reset") ?? throw new InvalidOperationException("Collection probe reset missing.");
    var snapshots = probeType.GetMethod("Snapshot") ?? throw new InvalidOperationException("Collection snapshot missing.");
    object[] Snapshot(object collection) => (object[])(snapshots.Invoke(null, [collection])
        ?? throw new InvalidOperationException("Collection snapshot returned null."));
    var couplingChecks = 0;
    foreach (var input in cases)
    {
        reset.Invoke(null, [-1, "", 0]);
        Check(traced, input, "instrumented baseline");
        var originalRecords = records.ToArray();
        var created = originalRecords.Where(record => record.Operation == "create").ToArray();
        var requirements = name switch
        {
            "CreateScores" => new[] { (typeof(Dictionary<string, int>), Array.Empty<object>(),
                new object[] { ("Alice", 95), ("Bob", 87), ("Carol", 92) }, "count") },
            "UniqueCount" => new[] { (typeof(HashSet<int>), Array.Empty<object>(),
                new object[] { 1, 2, 2, 3, 3, 3 }, "count") },
            "BuildCollection" => new[]
            {
                (typeof(List<int>), Array.Empty<object>(), new object[] { 1, 2 }, "count"),
                (typeof(Dictionary<int, string>), Array.Empty<object>(),
                    new object[] { (1, "one"), (2, "two"), (3, "three") }, "count")
            },
            "HasValue" => new[] { (typeof(List<int>), new object[] { 10, 20, 30 }, Array.Empty<object>(), "contains") },
            "GetListLength" => new[] { (typeof(List<int>), Array.Empty<object>(),
                Enumerable.Range(1, input).Cast<object>().ToArray(), "count") },
            _ => new[] { (typeof(List<int>), Array.Empty<object>(), new object[] { 10, 20, 30 }, "iteration") }
        };
        foreach (var (requiredType, initial, additions, operation) in requirements)
        {
            var candidates = new List<(int Identity, int Count)>();
            for (var identity = 0; identity < created.Length; identity++)
            {
                var creation = created[identity];
                var collection = creation.Collection;
                // HasValue initialization may finish with Adds before Contains.
                var expectedInitial = name == "HasValue" ? initial.Take(creation.State.Length).ToArray() : initial;
                var expectedAdditions = name == "HasValue" ? initial.Skip(creation.State.Length).ToArray() : additions;
                if (collection.GetType() != requiredType || !creation.State.SequenceEqual(expectedInitial))
                    continue;
                var events = originalRecords.Where(record => ReferenceEquals(record.Collection, collection)).ToArray();
                var adds = events.Where(record => record.Operation == "add").ToArray();
                object Added((object Collection, string Operation, object? Key, object? Value, object[] State, int Depth) record)
                    => requiredType == typeof(Dictionary<string, int>) ? ((string)record.Key!, (int)record.Value!)
                        : requiredType == typeof(Dictionary<int, string>) ? ((int)record.Key!, (string)record.Value!)
                        : record.Value!;
                if (!adds.Select(Added).SequenceEqual(expectedAdditions)
                    || name == "GetListLength" && adds.Any(record => record.Depth == 0))
                    continue;
                object[] ExpectedState(int added)
                {
                    var state = expectedInitial.Concat(expectedAdditions.Take(added));
                    return requiredType == typeof(HashSet<int>)
                        ? state.Distinct().OrderBy(value => (int)value).ToArray() : state.ToArray();
                }
                // Each Add is observed after the actual BCL operation, including
                // all three duplicate HashSet Adds. Snapshots are not proxy counts.
                if (adds.Where((record, index) => !record.State.SequenceEqual(ExpectedState(index + 1))).Any()
                    || !Snapshot(collection).SequenceEqual(ExpectedState(expectedAdditions.Length)))
                    continue;
                var final = ExpectedState(expectedAdditions.Length);
                if (operation == "iteration")
                {
                    var reads = events.Where(record => record.Operation is "iteration" or "index").ToArray();
                    if (reads.Length != final.Length || reads.Any(record => record.Depth == 0
                            || !record.State.SequenceEqual(final))
                        || !reads.Select(record => (int)record.Value!).Order().SequenceEqual(final.Cast<int>().Order()))
                        continue;
                }
                else if (!events.Any(record => record.Operation == operation && record.State.SequenceEqual(final)
                    && (operation != "contains" || Equals(record.Key, input) && Equals(record.Value, Expected(input)))))
                    continue;
                candidates.Add((identity, final.Length));
            }
            if (candidates.Count == 0)
                throw new InvalidOperationException($"Collection provenance failed: {name} requires actual {requiredType.Name} "
                    + $"with exact initial state, Add sequence, snapshots and {operation} observations.");
            var coupled = false;
            foreach (var (identity, count) in candidates)
            {
                reset.Invoke(null, [identity, operation, count]);
                object? changed;
                try
                {
                    changed = traced.Invoke(null, Arguments(input));
                }
                catch (TargetInvocationException error)
                {
                    throw new InvalidOperationException("Collection counterfactual runtime failed; exceptions are not coupling evidence.", error);
                }
                var counterfactualExpected = operation == "contains" ? (object)!(bool)Expected(input)
                    : operation == "iteration" ? 60 + 7 * additions.Length : (int)Expected(input) + 1;
                if (Equals(changed, counterfactualExpected))
                {
                    coupled = true;
                    couplingChecks++;
                    break;
                }
            }
            if (!coupled)
                throw new InvalidOperationException($"Collection output coupling failed: actual {requiredType.Name} {operation} "
                    + "does not supply the independently expected counterfactual result.");
        }
    }
    Console.WriteLine($"Verification passed: {name}; native/CLR signature; {cases.Length} runtime cases; "
        + $"{couplingChecks} successful identity-specific output counterfactuals.");
    Console.WriteLine("Limits: exact prompt entry-domain proof; finite runtime/state/provenance checks, not a universal body proof; "
        + (name == "GetListLength" ? "n=0,1,2,3,5,7,16,31,64,127,257; large allocations/large-input bodies untested; no accepted precondition cap."
            : name == "HasValue" ? "21 signed/boundary Contains inputs; unsampled bodies unproven."
            : "parameterless observed execution only."));
}

static void CheckBasicEntryDomain(FunctionNode function, string[] names)
{
    CheckScalarEntryDomain(function.Parameters, function.Preconditions, function.Name);
}

static void CheckScalarEntryDomain(
    IReadOnlyList<ParameterNode> parameters, IReadOnlyList<RequiresNode> preconditions, string name)
{
    using var context = new Context();
    var translator = new ContractTranslator(context);
    foreach (var parameter in parameters)
        if (!translator.DeclareVariable(parameter.Name, "i32"))
            throw new InvalidOperationException($"Cannot model parameter {parameter.Name}.");
    BoolExpr Translate(ExpressionNode expression)
    {
        CheckEntryConditionExpression(expression);
        var translated = translator.TranslateBoolExpr(expression);
        if (translated == null || translator.Warnings.Count != 0)
            throw new InvalidOperationException($"Basic precondition translation failed: {string.Join("; ", translator.Warnings)}");
        return translated;
    }
    var conditions = TranslateScalarEntryConditions(parameters, preconditions, translator, Translate);
    BoolExpr domain = context.MkTrue();
    if (name is "Multiply" or "Add")
    {
        // The prompts specify no overflow policy. Only representable results
        // constrain entry contracts or runtime cases, for checked and unchecked bodies.
        var a = context.MkSignExt(32, context.MkBVConst("a", 32));
        var b = context.MkSignExt(32, context.MkBVConst("b", 32));
        var product = name == "Add" ? context.MkBVAdd(a, b) : context.MkBVMul(a, b);
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
    => TranslateScalarEntryConditions(function.Parameters, function.Preconditions, translator, translate);

static BoolExpr[] TranslateScalarEntryConditions(
    IReadOnlyList<ParameterNode> parameters, IReadOnlyList<RequiresNode> preconditions,
    ContractTranslator translator, Func<ExpressionNode, BoolExpr> translate)
{
    var conditions = preconditions.Select(contract => translate(contract.Condition)).ToList();
    foreach (var parameter in parameters)
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

sealed class ArithmeticPolynomial : IDisposable
{
    private readonly Context context = new();
    private readonly Params normalization;
    public ArithmeticPolynomial()
    {
        normalization = context.MkParams();
        normalization.Add("som", true);
    }

    private ArithExpr? Value(ExpressionNode expression, Func<ReferenceNode, ArithExpr?> referenceValue)
    {
        switch (expression)
        {
            case IntLiteralNode literal when !literal.IsUnsigned:
                return context.MkReal(literal.Value);
            case ReferenceNode reference:
                return referenceValue(reference);
            case TypeOperationNode { Operation: TypeOp.Cast } cast
                when AttributeHelper.ExpandType(cast.TargetType) is "INT" or "INT[bits=64][signed=true]":
                return Value(cast.Operand, referenceValue);
            case UnaryOperationNode { Operator: UnaryOperator.Negate } unary:
                return Value(unary.Operand, referenceValue) is { } operand ? context.MkUnaryMinus(operand) : null;
            case ConditionalExpressionNode conditional:
                var yes = Value(conditional.WhenTrue, referenceValue);
                var no = Value(conditional.WhenFalse, referenceValue);
                return yes != null && no != null && Same(yes, no) ? yes : null;
            case BinaryOperationNode binary when binary.Operator is BinaryOperator.Add or BinaryOperator.Subtract
                or BinaryOperator.Multiply or BinaryOperator.Divide:
                var left = Value(binary.Left, referenceValue);
                var right = Value(binary.Right, referenceValue);
                if (left == null || right == null)
                    return null;
                return binary.Operator switch
                {
                    BinaryOperator.Add => context.MkAdd(left, right),
                    BinaryOperator.Subtract => context.MkSub(left, right),
                    BinaryOperator.Multiply => context.MkMul(left, right),
                    BinaryOperator.Divide when right.Simplify(normalization) is RatNum denominator
                        && !Same(denominator, context.MkReal(0)) => context.MkDiv(left, right),
                    _ => null
                };
            default:
                return null;
        }
    }

    private bool Same(ArithExpr left, ArithExpr right) => context.MkEq(left, right).Simplify(normalization).IsTrue;
    public bool IsEvent((string Key, bool Initial, long Value, int[] Reads, int Site)[] events, int target,
        IReadOnlyDictionary<int, ExpressionNode> expressions, IReadOnlyDictionary<string, BindStatementNode> bindings,
        string kind, int[] opaqueVersions)
    {
        var opaque = opaqueVersions.Select((version, index) => (version, value: (ArithExpr)context.MkRealConst("__version" + index)))
            .ToDictionary(pair => pair.version, pair => pair.value);
        var memo = new Dictionary<int, ArithExpr?>();
        ArithExpr? Event(int index)
        {
            if (opaque.TryGetValue(index, out var independent))
                return independent;
            if (index < 0 || index >= events.Length)
                throw new InvalidOperationException("Arithmetic polynomial read has an invalid write version.");
            if (memo.TryGetValue(index, out var cached))
                return cached;
            var record = events[index];
            if (record.Key.StartsWith("p:", StringComparison.Ordinal))
                return context.MkRealConst(record.Key[2..]);
            if (!expressions.TryGetValue(record.Site, out var expression))
                throw new InvalidOperationException("Arithmetic polynomial write/return lacks its exact native expression.");
            var result = Value(expression, reference =>
            {
                var versions = record.Reads.Distinct().Where(read =>
                    events[read].Key == "p:" + reference.Name
                    || bindings.TryGetValue(events[read].Key, out var binding) && binding.Name == reference.Name).ToArray();
                if (versions.Length != 1)
                    return null;
                if (versions[0] >= index)
                    throw new InvalidOperationException("Arithmetic polynomial read does not precede its write.");
                return Event(versions[0]);
            });
            memo.Add(index, result);
            return result;
        }
        // Preserve exact executed write versions, not source-name aliases or
        // substituted input formulas. Overwritten/gated aliases cannot launder a product.
        var expected = kind switch
        {
            "sum" => context.MkAdd(context.MkRealConst("a"), context.MkRealConst("b")),
            "difference" => context.MkSub(context.MkRealConst("a"), context.MkRealConst("b")),
            "zero" => (ArithExpr)context.MkReal(0),
            "product" => context.MkMul(opaque[opaqueVersions[0]], opaque[opaqueVersions[1]]),
            "update" => context.MkAdd(opaque[opaqueVersions[0]], context.MkRealConst("n")),
            "return" => opaque[opaqueVersions[0]],
            _ => throw new InvalidOperationException("Unknown arithmetic polynomial witness.")
        };
        return Event(target) is { } value && Same(value, expected);
    }

    public void Dispose()
    {
        normalization.Dispose();
        context.Dispose();
    }
}

sealed class LambdaAdmissionRewriter(SemanticModel semantic, ISymbol?[] captures, string probe) : CSharpSyntaxRewriter
{
    public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
    {
        var symbol = semantic.GetSymbolInfo(node).Symbol;
        var index = Array.FindIndex(captures, captured => SymbolEqualityComparer.Default.Equals(captured, symbol));
        if (index < 0)
            return base.VisitIdentifierName(node);
        if (node.Parent is AssignmentExpressionSyntax assignment && assignment.Left == node
            || node.Parent is PrefixUnaryExpressionSyntax prefix && prefix.IsKind(SyntaxKind.PreIncrementExpression)
            || node.Parent is PostfixUnaryExpressionSyntax)
            throw new InvalidOperationException("Bounded unsupported lambda scope: captured variable mutation.");
        return SyntaxFactory.ParseExpression($"{probe}.Capture({index}, {node.ToFullString()})");
    }

    public override SyntaxNode? VisitReturnStatement(ReturnStatementSyntax node)
    {
        if (node.Expression == null)
            throw new InvalidOperationException("Wrong native lambda void return.");
        var expression = Visit(node.Expression)
            ?? throw new InvalidOperationException("Missing native lambda return expression.");
        return node.WithExpression(SyntaxFactory.ParseExpression($"{probe}.Output(({expression.ToFullString()}))"));
    }
}

sealed class ArithmeticRewriter : CSharpSyntaxRewriter
{
    private readonly SemanticModel model;
    private readonly FunctionNode function;
    private readonly string module;
    private readonly string path;
    private readonly string probe;
    private readonly StatementNode[] native;
    private readonly Dictionary<ISymbol, string> identities = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<int, ExpressionSyntax> evaluations = [];
    public Dictionary<string, BindStatementNode> Bindings { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, SpecialType> Types { get; } = new(StringComparer.Ordinal);
    public Dictionary<int, ExpressionNode> Expressions { get; } = [];

    public ArithmeticRewriter(SemanticModel model, string module, FunctionNode function, string path, string probe)
    {
        this.model = model;
        this.function = function;
        this.module = module;
        this.path = path;
        this.probe = probe;
        native = RecursiveAstWalker.EnumerateStatements(function.Body).ToArray();
        var method = model.SyntaxTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(candidate => candidate.Identifier.ValueText == function.Name
                && candidate.Parent is ClassDeclarationSyntax type && type.Identifier.ValueText == module + "Module");
        foreach (var parameter in method.ParameterList.Parameters)
            identities.Add(model.GetDeclaredSymbol(parameter) ?? throw new InvalidOperationException("Arithmetic parameter CLR symbol missing."),
                "p:" + parameter.Identifier.ValueText);
        foreach (var declaration in method.DescendantNodes().OfType<LocalDeclarationStatementSyntax>())
        {
            var matched = Match(declaration, required: false);
            if (matched is not BindStatementNode binding)
                continue;
            if (declaration.Declaration.Variables.Count != 1 || declaration.Declaration.Variables[0].Initializer == null)
                throw new InvalidOperationException("Arithmetic mapped binding must have exactly one initializer.");
            var variable = declaration.Declaration.Variables[0];
            var symbol = model.GetDeclaredSymbol(variable) as ILocalSymbol
                ?? throw new InvalidOperationException("Arithmetic binding CLR local identity missing.");
            if (symbol.Name != binding.Name || symbol.Type.SpecialType is not (SpecialType.System_Int32 or SpecialType.System_Int64))
                throw new InvalidOperationException("Arithmetic mapped binding requires matching native name and Int32/Int64 CLR type.");
            var key = "b:" + binding.Span.Line.ToString(System.Globalization.CultureInfo.InvariantCulture);
            identities.Add(symbol, key);
            Bindings.Add(key, binding);
            Types.Add(key, symbol.Type.SpecialType);
            evaluations.Add(binding.Span.Line, variable.Initializer!.Value);
            Expressions.Add(binding.Span.Line, binding.Initializer
                ?? throw new InvalidOperationException("Arithmetic native initializer missing."));
        }
    }

    public bool HasWideInitializer(string key, (string Key, bool Initial, long Value, int[] Reads, int Site)[] events)
    {
        var index = Array.FindIndex(events, record => record.Key == key && record.Initial);
        if (Types[key] != SpecialType.System_Int64
            || index < 0 || model.GetTypeInfo(evaluations[events[index].Site]).Type?.SpecialType != SpecialType.System_Int64)
            return false;
        var visited = new HashSet<int>();
        bool WideEvaluation(int version)
        {
            if (!visited.Add(version) || events[version].Key.StartsWith("p:", StringComparison.Ordinal))
                return true;
            var expression = evaluations[events[version].Site];
            if (expression.DescendantNodesAndSelf().OfType<CheckedExpressionSyntax>().Any(node =>
                node.Keyword.IsKind(SyntaxKind.CheckedKeyword)
                && model.GetTypeInfo(node.Expression).Type?.SpecialType == SpecialType.System_Int32))
                return false;
            return events[version].Reads.All(WideEvaluation);
        }
        return WideEvaluation(index);
    }

    private StatementNode? Match(StatementSyntax statement, bool required = true)
    {
        var directives = statement.GetFirstToken().LeadingTrivia.Select(trivia => trivia.GetStructure())
            .OfType<LineDirectiveTriviaSyntax>().Where(directive => directive.IsActive && directive.File.ValueText == path).ToArray();
        if (directives.Length == 0 && !required)
            return null;
        if (directives.Length != 1 || !int.TryParse(directives[0].Line.ValueText, out var line))
            throw new InvalidOperationException("Arithmetic native mapping failed: missing/ambiguous selected-emitter statement directive.");
        var matches = native.Where(node => node.Span.Line == line).ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException("Arithmetic native mapping failed: ambiguous/foreign AST statement.");
        return matches[0];
    }

    private ExpressionSyntax Wrap(string operation, string key, int initial, int site, ExpressionSyntax value)
    {
        var call = $"{probe}.{operation}({System.Text.Json.JsonSerializer.Serialize(key)}, {initial}, {site}, () => ({value.WithoutTrivia()}))";
        return SyntaxFactory.ParseExpression(call).WithTriviaFrom(value);
    }

    public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        if (node.Identifier.ValueText != function.Name || node.Parent is not ClassDeclarationSyntax type
            || type.Identifier.ValueText != module + "Module")
            return node;
        if (node.Body == null)
            throw new InvalidOperationException("Arithmetic selected-public method has no body.");
        var rewritten = (MethodDeclarationSyntax)(base.VisitMethodDeclaration(node)
            ?? throw new InvalidOperationException("Arithmetic selected-public rewrite failed."));
        var parameters = node.ParameterList.Parameters.Select(parameter => SyntaxFactory.ParseStatement(
            $"{probe}.Parameter(\"p:{parameter.Identifier.ValueText}\", {parameter.Identifier.Text});")).ToArray();
        return rewritten.WithBody(rewritten.Body!.WithStatements(rewritten.Body.Statements.InsertRange(0, parameters)));
    }

    public override SyntaxNode? VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
    {
        var rewritten = (LocalDeclarationStatementSyntax)base.VisitLocalDeclarationStatement(node)!;
        var symbol = model.GetDeclaredSymbol(node.Declaration.Variables[0]);
        if (symbol == null || !identities.TryGetValue(symbol, out var key))
            return rewritten;
        if (Match(node) is not BindStatementNode binding)
            throw new InvalidOperationException("Arithmetic initialization has no native binding.");
        var variable = rewritten.Declaration.Variables[0];
        var initializer = variable.Initializer ?? throw new InvalidOperationException("Arithmetic initializer missing.");
        return rewritten.WithDeclaration(rewritten.Declaration.WithVariables(SyntaxFactory.SingletonSeparatedList(
            variable.WithInitializer(initializer.WithValue(Wrap("Write", key, 1, binding.Span.Line, initializer.Value))))));
    }

    public override SyntaxNode? VisitAssignmentExpression(AssignmentExpressionSyntax node)
    {
        var symbol = model.GetSymbolInfo(node.Left).Symbol;
        if (symbol == null || !identities.TryGetValue(symbol, out var key))
            return base.VisitAssignmentExpression(node);
        if (!key.StartsWith("b:", StringComparison.Ordinal) || !node.IsKind(SyntaxKind.SimpleAssignmentExpression)
            || node.Parent is not ExpressionStatementSyntax statement)
            throw new InvalidOperationException("Arithmetic state provenance supports native simple local assignments only.");
        var matched = Match(statement);
        var expression = matched switch
        {
            AssignmentStatementNode { Target: ReferenceNode reference } assignment when reference.Name == symbol.Name => assignment.Value,
            BindStatementNode binding when binding.IsMutable && binding.Name == symbol.Name && binding.Initializer != null => binding.Initializer,
            _ => throw new InvalidOperationException("Arithmetic assignment has no matching native local write.")
        };
        Expressions.Add(matched!.Span.Line, expression);
        evaluations.Add(matched.Span.Line, node.Right);
        var right = (ExpressionSyntax)Visit(node.Right)!;
        return node.WithRight(Wrap("Write", key, 0, matched.Span.Line, right));
    }

    public override SyntaxNode? VisitReturnStatement(ReturnStatementSyntax node)
    {
        if (Match(node) is not ReturnStatementNode { Expression: { } expression } returned || node.Expression == null)
            throw new InvalidOperationException("Arithmetic returned value has no native selected return.");
        var key = "r:" + returned.Span.Line.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Expressions.Add(returned.Span.Line, expression);
        var value = (ExpressionSyntax)Visit(node.Expression)!;
        return node.WithExpression(Wrap("Returned", key, 0, returned.Span.Line, value));
    }

    public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
    {
        var symbol = model.GetSymbolInfo(node).Symbol;
        if (symbol == null || !identities.TryGetValue(symbol, out var key))
            return node;
        return SyntaxFactory.ParseExpression($"{probe}.Read({System.Text.Json.JsonSerializer.Serialize(key)}, {node.Identifier.Text})")
            .WithTriviaFrom(node);
    }

    public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        => node.Expression is IdentifierNameSyntax { Identifier.ValueText: "nameof" } ? node : base.VisitInvocationExpression(node);

    public static string Probe(string name) => $$"""
        public static class {{name}}
        {
            public static readonly System.Collections.Generic.List<(string Key, bool Initial, long Value, int[] Reads, int Site)> Records = new();
            private static readonly System.Collections.Generic.Dictionary<string, int> Versions = new();
            private static readonly System.Collections.Generic.Dictionary<string, int> Counts = new();
            private static readonly System.Collections.Generic.Stack<System.Collections.Generic.List<int>> Reading = new();
            private static string Target = "";
            private static int Occurrence = -1;
            private static bool Zero;
            public static void Reset(string target, int occurrence, bool zero)
            {
                Records.Clear(); Versions.Clear(); Counts.Clear(); Reading.Clear();
                Target = target; Occurrence = occurrence; Zero = zero;
            }
            private static void Record(string key, bool initial, long value, int[] reads, int site)
            {
                if (Records.Count >= 10000) throw new System.InvalidOperationException("Arithmetic trace limit exceeded.");
                Versions[key] = Records.Count;
                Records.Add((key, initial, value, reads, site));
            }
            public static void Parameter(string key, int value) => Record(key, true, value, System.Array.Empty<int>(), -1);
            public static T Read<T>(string key, T value)
            {
                if (!Versions.TryGetValue(key, out var version))
                    throw new System.InvalidOperationException("Arithmetic read has no native-observed write.");
                if (Reading.Count > 0) Reading.Peek().Add(version);
                return value;
            }
            private static T Evaluate<T>(string key, bool initial, int site, System.Func<T> expression, bool mutate)
            {
                Reading.Push(new());
                T value;
                int[] reads;
                try { value = expression(); }
                finally { reads = Reading.Pop().ToArray(); }
                if (value is not int && value is not long)
                    throw new System.InvalidOperationException("Arithmetic probe requires an actual signed scalar.");
                var count = Counts.TryGetValue(key, out var previous) ? previous : 0;
                Counts[key] = count + 1;
                if (mutate && key == Target && count == Occurrence)
                    value = value is int integer ? (T)(object)(Zero ? 0 : checked(integer + 1))
                        : (T)(object)(Zero ? 0L : checked((long)(object)value + 1));
                Record(key, initial, System.Convert.ToInt64(value), reads, site);
                return value;
            }
            public static T Write<T>(string key, int initial, int site, System.Func<T> expression)
                => Evaluate(key, initial == 1, site, expression, true);
            public static T Returned<T>(string key, int ignored, int site, System.Func<T> expression)
                => Evaluate(key, false, site, expression, false);
        }
        """;
}

sealed class CollectionRewriter : CSharpSyntaxRewriter
{
    private readonly SemanticModel model;
    private readonly string path;
    private readonly string probe;
    private readonly Dictionary<string, AstNode[]> native;

    public CollectionRewriter(SemanticModel model, ModuleNode module, string path, string probe)
    {
        this.model = model;
        this.path = path;
        this.probe = probe;
        AstNode[] Walk(AstNode root)
        {
            var visited = new HashSet<AstNode>(ReferenceEqualityComparer.Instance);
            var pending = new Stack<AstNode>();
            pending.Push(root);
            while (pending.TryPop(out var node))
                if (visited.Add(node))
                    foreach (var child in RecursiveAstWalker.GetAllChildren(node))
                        pending.Push(child);
            return visited.ToArray();
        }
        var all = Walk(module);
        native = all.Where(node => node is FunctionNode or MethodNode)
            .GroupBy(node => node is FunctionNode function ? function.Name : ((MethodNode)node).Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.SelectMany(Walk).Distinct().ToArray(), StringComparer.Ordinal);
    }

    private void RequireNative(SyntaxNode node)
    {
        var method = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (method == null || !native.TryGetValue(method.Identifier.ValueText, out var nodes))
            throw new InvalidOperationException("Collection native mapping failed: operation is outside a native module function.");
        foreach (var statement in node.AncestorsAndSelf().OfType<StatementSyntax>())
        {
            var directives = statement.GetFirstToken().LeadingTrivia.Select(trivia => trivia.GetStructure())
                .OfType<LineDirectiveTriviaSyntax>().Where(directive => directive.IsActive
                    && directive.File.ValueText == path).ToArray();
            if (directives.Length == 0)
                continue;
            if (directives.Length != 1 || !int.TryParse(directives[0].Line.ValueText, out var line)
                || !nodes.Any(candidate => candidate is StatementNode && candidate.Span.Line == line)
                || nodes.Any(candidate => candidate is CSharpInteropBlockNode && candidate.Span.Line == line))
                throw new InvalidOperationException("Collection native mapping failed: unmapped/foreign statement.");
            return;
        }
        throw new InvalidOperationException("Collection native mapping failed: operation has no selected-emitter statement directive.");
    }

    private string? Kind(ExpressionSyntax receiver)
    {
        if (model.GetTypeInfo(receiver).Type is not INamedTypeSymbol type
            || type.ContainingNamespace.ToDisplayString() != "System.Collections.Generic")
            return null;
        var arguments = type.TypeArguments.Select(argument => argument.SpecialType).ToArray();
        return type.Name switch
        {
            "List" or "HashSet" or "ICollection" or "IList" or "ISet" or "IEnumerable"
                when arguments.SequenceEqual(new[] { SpecialType.System_Int32 }) => "integer",
            "Dictionary" or "IDictionary"
                when arguments.SequenceEqual(new[] { SpecialType.System_String, SpecialType.System_Int32 })
                    || arguments.SequenceEqual(new[] { SpecialType.System_Int32, SpecialType.System_String }) => "dictionary",
            _ => null
        };
    }

    private ExpressionSyntax Call(string operation, SyntaxNode original, params ExpressionSyntax[] arguments)
        => SyntaxFactory.InvocationExpression(SyntaxFactory.ParseExpression($"global::{probe}.{operation}"),
            SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(arguments.Select(argument =>
                SyntaxFactory.Argument(argument.WithoutTrivia())))))
            .WithTriviaFrom(original);

    public override SyntaxNode? VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
    {
        var rewritten = (ObjectCreationExpressionSyntax)base.VisitObjectCreationExpression(node)!;
        if (Kind(node) == null)
            return rewritten;
        RequireNative(node);
        return Call("Created", node, rewritten.WithoutTrivia());
    }

    public override SyntaxNode? VisitImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node)
    {
        var rewritten = (ImplicitObjectCreationExpressionSyntax)base.VisitImplicitObjectCreationExpression(node)!;
        if (Kind(node) == null)
            return rewritten;
        RequireNative(node);
        var type = model.GetTypeInfo(node).Type
            ?? throw new InvalidOperationException("Collection implicit constructor type missing.");
        var explicitCreation = SyntaxFactory.ObjectCreationExpression(
            SyntaxFactory.ParseTypeName(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)),
            rewritten.ArgumentList, rewritten.Initializer);
        return Call("Created", node, explicitCreation);
    }

    public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        var rewritten = (InvocationExpressionSyntax)base.VisitInvocationExpression(node)!;
        if (Kind(node) != null && model.GetSymbolInfo(node).Symbol is IMethodSymbol factory
            && factory.ContainingType.ToDisplayString() == "System.Linq.Enumerable"
            && factory.Name is "ToList" or "ToHashSet" or "ToDictionary")
        {
            RequireNative(node);
            return Call("Created", node, rewritten.WithoutTrivia());
        }
        if (node.Expression is not MemberAccessExpressionSyntax access
            || Kind(access.Expression) is not { } kind
            || model.GetSymbolInfo(node).Symbol is not IMethodSymbol method
            || method.ContainingNamespace.ToDisplayString() != "System.Collections.Generic")
            return rewritten;
        var operation = method.Name switch
        {
            "Add" when kind == "integer" => "Add",
            "Add" when kind == "dictionary" => "DictionaryAdd",
            "Contains" when kind == "integer" => "Contains",
            _ => null
        };
        if (operation == null)
            return rewritten;
        RequireNative(node);
        var receiver = ((MemberAccessExpressionSyntax)rewritten.Expression).Expression;
        return Call(operation, node, new[] { receiver }.Concat(rewritten.ArgumentList.Arguments
            .Select(argument => argument.Expression)).ToArray());
    }

    public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        var rewritten = (MemberAccessExpressionSyntax)base.VisitMemberAccessExpression(node)!;
        if (Kind(node.Expression) == null || model.GetSymbolInfo(node).Symbol is not IPropertySymbol { Name: "Count" })
            return rewritten;
        RequireNative(node);
        return Call("Count", node, rewritten.Expression);
    }

    public override SyntaxNode? VisitAssignmentExpression(AssignmentExpressionSyntax node)
    {
        var rewritten = (AssignmentExpressionSyntax)base.VisitAssignmentExpression(node)!;
        if (node.Left is not ElementAccessExpressionSyntax element || Kind(element.Expression) != "dictionary")
            return rewritten;
        RequireNative(node);
        if (!node.IsKind(SyntaxKind.SimpleAssignmentExpression) || element.ArgumentList.Arguments.Count != 1)
            throw new InvalidOperationException("Collection dictionary assignment mapping is unsupported.");
        var left = (ElementAccessExpressionSyntax)rewritten.Left;
        return Call("Put", node, left.Expression, left.ArgumentList.Arguments[0].Expression, rewritten.Right);
    }

    public override SyntaxNode? VisitElementAccessExpression(ElementAccessExpressionSyntax node)
    {
        var rewritten = (ElementAccessExpressionSyntax)base.VisitElementAccessExpression(node)!;
        if (Kind(node.Expression) != "integer" || node.Parent is AssignmentExpressionSyntax { Left: var left }
            && ReferenceEquals(left, node))
            return rewritten;
        RequireNative(node);
        if (node.ArgumentList.Arguments.Count != 1)
            throw new InvalidOperationException("Collection list index mapping is unsupported.");
        return Call("Index", node, rewritten.Expression, rewritten.ArgumentList.Arguments[0].Expression);
    }

    private StatementSyntax LoopBody(StatementSyntax body)
        => SyntaxFactory.Block(
            SyntaxFactory.ParseStatement($"global::{probe}.Depth++;"),
            SyntaxFactory.TryStatement(SyntaxFactory.Block(body), [],
                SyntaxFactory.FinallyClause(SyntaxFactory.Block(
                    SyntaxFactory.ParseStatement($"global::{probe}.Depth--;")))));

    public override SyntaxNode? VisitForEachStatement(ForEachStatementSyntax node)
    {
        RequireNative(node);
        var rewritten = (ForEachStatementSyntax)base.VisitForEachStatement(node)!;
        if (Kind(node.Expression) == "integer")
            rewritten = rewritten.WithExpression(Call("Iterate", node.Expression, rewritten.Expression));
        return rewritten.WithStatement(LoopBody(rewritten.Statement));
    }

    public override SyntaxNode? VisitForEachVariableStatement(ForEachVariableStatementSyntax node)
    {
        RequireNative(node);
        var rewritten = (ForEachVariableStatementSyntax)base.VisitForEachVariableStatement(node)!;
        return rewritten.WithStatement(LoopBody(rewritten.Statement));
    }

    public override SyntaxNode? VisitWhileStatement(WhileStatementSyntax node)
    {
        RequireNative(node);
        var rewritten = (WhileStatementSyntax)base.VisitWhileStatement(node)!;
        return rewritten.WithStatement(LoopBody(rewritten.Statement));
    }

    public override SyntaxNode? VisitForStatement(ForStatementSyntax node)
    {
        RequireNative(node);
        var rewritten = (ForStatementSyntax)base.VisitForStatement(node)!;
        return rewritten.WithStatement(LoopBody(rewritten.Statement));
    }

    public override SyntaxNode? VisitDoStatement(DoStatementSyntax node)
    {
        RequireNative(node);
        var rewritten = (DoStatementSyntax)base.VisitDoStatement(node)!;
        return rewritten.WithStatement(LoopBody(rewritten.Statement));
    }

    public static string Probe(string name) => $$"""
        internal static class {{name}}
        {
            public static int Depth;
            private static int Target;
            private static string Mode = "";
            private static int FinalCount;
            private static readonly System.Collections.Generic.List<object> Identities = new();
            public static readonly System.Collections.Generic.List<(object Collection, string Operation,
                object? Key, object? Value, object[] State, int Depth)> Records = new();
            public static void Reset(int target, string mode, int count)
            {
                Depth = 0; Target = target; Mode = mode; FinalCount = count;
                Identities.Clear(); Records.Clear();
            }
            public static object[] Snapshot(object collection) => collection switch
            {
                System.Collections.Generic.List<int> list => System.Linq.Enumerable.Cast<object>(list).ToArray(),
                System.Collections.Generic.HashSet<int> set => System.Linq.Enumerable.Cast<object>(
                    System.Linq.Enumerable.OrderBy(set, value => value)).ToArray(),
                System.Collections.Generic.Dictionary<string,int> dictionary =>
                    System.Linq.Enumerable.Select(dictionary, pair => (object)(pair.Key, pair.Value)).ToArray(),
                System.Collections.Generic.Dictionary<int,string> dictionary =>
                    System.Linq.Enumerable.Select(dictionary, pair => (object)(pair.Key, pair.Value)).ToArray(),
                _ => throw new System.InvalidOperationException("Unsupported actual collection CLR identity.")
            };
            private static void Record(object collection, string operation, object? key, object? value, int? depth = null)
            {
                if (Records.Count >= 10000) throw new System.InvalidOperationException("Collection trace limit exceeded.");
                if (!Identities.Any(candidate => object.ReferenceEquals(candidate, collection)))
                    throw new System.InvalidOperationException("Collection use has no native-observed construction.");
                Records.Add((collection, operation, key, value, Snapshot(collection), depth ?? Depth));
            }
            private static bool Changed(object collection, string operation)
                => Target >= 0 && Target < Identities.Count && object.ReferenceEquals(collection, Identities[Target])
                    && Mode == operation && Snapshot(collection).Length == FinalCount;
            public static T Created<T>(T collection) where T : class
            {
                Identities.Add(collection); Record(collection, "create", null, null); return collection;
            }
            public static void Add(System.Collections.Generic.ICollection<int> collection, int value)
            {
                collection.Add(value); Record(collection, "add", null, value);
            }
            public static bool Add(System.Collections.Generic.HashSet<int> collection, int value)
            {
                var result = collection.Add(value); Record(collection, "add", null, value); return result;
            }
            public static bool Add(System.Collections.Generic.ISet<int> collection, int value)
            {
                var result = collection.Add(value); Record(collection, "add", null, value); return result;
            }
            public static void DictionaryAdd<K,V>(System.Collections.Generic.IDictionary<K,V> collection, K key, V value)
            {
                collection.Add(key, value); Record(collection, "add", key, value);
            }
            public static V Put<K,V>(System.Collections.Generic.IDictionary<K,V> collection, K key, V value)
            {
                collection[key] = value; Record(collection, "add", key, value); return value;
            }
            public static int Count<T>(System.Collections.Generic.ICollection<T> collection)
            {
                var result = collection.Count; Record(collection, "count", null, result);
                return Changed(collection, "count") ? result + 1 : result;
            }
            public static bool Contains(System.Collections.Generic.ICollection<int> collection, int value)
            {
                var result = collection.Contains(value); Record(collection, "contains", value, result);
                return Changed(collection, "contains") ? !result : result;
            }
            public static int Index(System.Collections.Generic.IList<int> collection, int index)
            {
                var result = collection[index]; Record(collection, "index", index, result);
                return Changed(collection, "iteration") ? result + 7 : result;
            }
            public static System.Collections.Generic.IEnumerable<int> Iterate(System.Collections.Generic.IEnumerable<int> collection)
            {
                foreach (var value in collection)
                {
                    Record(collection, "iteration", null, value, Depth + 1);
                    yield return Changed(collection, "iteration") ? value + 7 : value;
                }
            }
        }
        """;
}

static class EffectsTaskProbe
{
    public const string Source = """
        public static class EffectsTaskProbe
        {
            public static readonly System.Collections.Generic.List<string[]> Events = new();
            static string mode = "";
            static string[] owned = System.Array.Empty<string>();
            public static void Reset(string mutation, string[] paths)
            {
                mode = mutation;
                owned = paths;
                Events.Clear();
            }
            static void Guard(string path)
            {
                if (!System.Array.Exists(owned, candidate => candidate == path))
                    throw new System.InvalidOperationException("Effects probe refused non-owned path.");
            }
            public static void WriteLine(string value)
            {
                Events.Add(new[] { "WriteLine", value });
                if (mode != "WriteLine") System.Console.WriteLine(value);
            }
            public static void Write(string value)
            {
                Events.Add(new[] { "Write", value });
                System.Console.Write(value);
            }
            public static string ReadLine()
            {
                var value = System.Console.ReadLine();
                if (value == null) throw new System.InvalidOperationException("Control supplied no line.");
                Events.Add(new[] { "ReadLine", value });
                return mode == "ReadLine" ? "counterfactual console line" : value;
            }
            public static string ReadAllText(string path)
                => ReadAllText(path, null);
            public static string ReadAllText(string path, System.Text.Encoding encoding)
            {
                Guard(path);
                var value = encoding == null ? System.IO.File.ReadAllText(path) : System.IO.File.ReadAllText(path, encoding);
                Events.Add(new[] { "ReadAllText", path, value });
                return mode == "ReadAllText" ? "counterfactual file content" : value;
            }
            public static byte[] ReadAllBytes(string path)
            {
                Guard(path);
                Events.Add(new[] { "ReadAllBytes", path });
                return System.IO.File.ReadAllBytes(path);
            }
            public static void WriteAllBytes(string path, byte[] content)
            {
                Guard(path);
                Events.Add(new[] { "WriteAllBytes", path });
                System.IO.File.WriteAllBytes(path, content);
            }
            public static void WriteAllText(string path, string content)
                => WriteAllText(path, content, null);
            public static void WriteAllText(string path, string content, System.Text.Encoding encoding)
            {
                Guard(path);
                Events.Add(new[] { "WriteAllText", path, content });
                if (mode != "WriteAllText")
                {
                    if (encoding == null) System.IO.File.WriteAllText(path, content);
                    else System.IO.File.WriteAllText(path, content, encoding);
                }
            }
            public static void AppendAllText(string path, string content)
                => AppendAllText(path, content, null);
            public static void AppendAllText(string path, string content, System.Text.Encoding encoding)
            {
                Guard(path);
                Events.Add(new[] { "AppendAllText", path, content });
                if (mode != "AppendAllText")
                {
                    if (encoding == null) System.IO.File.AppendAllText(path, content);
                    else System.IO.File.AppendAllText(path, content, encoding);
                }
            }
        }
        """;
}

sealed class CarrierPatternInput
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
        | BindingFlags.Instance | BindingFlags.DeclaredOnly;
    private readonly Type closed;
    private readonly bool option;
    private readonly MemberInfo[] tags;
    private readonly MemberInfo[] values;
    private readonly MemberInfo[] errors;
    private readonly MemberInfo[] writableTags;
    private readonly MemberInfo value;
    private readonly MemberInfo? error;

    public CarrierPatternInput(Type closed, bool option)
    {
        this.closed = closed;
        this.option = option;
        var definition = closed.GetGenericTypeDefinition();
        var parameters = definition.GetGenericArguments();
        MemberInfo? Member(string name, Type expected, bool accessor = false)
        {
            var members = closed.GetMember(name, Members);
            if (members.Length == 0)
                return null;
            if (members.Length != 1)
                throw new InvalidOperationException($"Ambiguous carrier member {name}.");
            var generic = definition.GetMember(name, Members).Single();
            var valid = (members[0], generic) switch
            {
                (FieldInfo field, FieldInfo open) when !accessor => field.IsPublic && !field.IsStatic
                    && open.FieldType == expected,
                (PropertyInfo property, PropertyInfo open) when !accessor
                    => property.GetMethod is { IsPublic: true, IsStatic: false }
                        && property.GetIndexParameters().Length == 0 && open.PropertyType == expected,
                (MethodInfo method, MethodInfo open) when accessor => method.IsPublic && !method.IsStatic
                    && !method.IsGenericMethod && method.GetParameters().Length == 0 && open.ReturnType == expected,
                _ => false
            };
            if (!valid)
                throw new InvalidOperationException($"Wrong carrier member shape/type: {name}.");
            return members[0];
        }
        tags = new[] { Member(option ? "IsSome" : "IsOk", typeof(bool)),
            Member(option ? "IsNone" : "IsErr", typeof(bool)) }.OfType<MemberInfo>().ToArray();
        values = new[] { Member("Value", parameters[0]), Member("Unwrap", parameters[0], true) }
            .OfType<MemberInfo>().ToArray();
        errors = option ? [] : new[] { Member("Error", parameters[1]), Member("UnwrapErr", parameters[1], true) }
            .OfType<MemberInfo>().ToArray();
        if (tags.Length == 0 || values.Length == 0 || !option && errors.Length == 0)
            throw new InvalidOperationException("Missing authoritative typed carrier case/payload APIs.");
        static bool Writable(MemberInfo member) => member switch
        {
            FieldInfo field => !field.IsInitOnly && !field.IsLiteral,
            PropertyInfo property => property.SetMethod is { IsPublic: true, IsStatic: false },
            _ => false
        };
        writableTags = tags.Where(Writable).ToArray();
        value = values.FirstOrDefault(Writable)
            ?? throw new InvalidOperationException("Unmodeled carrier input: a public writable Value API is required.");
        error = option ? null : errors.FirstOrDefault(Writable)
            ?? throw new InvalidOperationException("Unmodeled carrier input: a public writable Error API is required.");
        if (writableTags.Length == 0 || closed.GetConstructor(Type.EmptyTypes) == null)
            throw new InvalidOperationException("Unmodeled carrier input: public parameterless construction and writable named case tag required.");
    }

    private static object? Read(MemberInfo member, object instance) => member switch
    {
        FieldInfo field => field.GetValue(instance),
        PropertyInfo property => property.GetValue(instance),
        MethodInfo accessor => accessor.Invoke(instance, null),
        _ => throw new InvalidOperationException("Unsupported carrier observation.")
    };

    private static void Write(MemberInfo member, object instance, object payload)
    {
        switch (member)
        {
            case FieldInfo field: field.SetValue(instance, payload); break;
            case PropertyInfo property: property.SetValue(instance, payload); break;
            default: throw new InvalidOperationException("Unsupported carrier input API.");
        }
    }

    public object Create(bool positive, int payload, string failure)
    {
        var saved = Console.Out;
        using var output = new BoundedOutput();
        try
        {
            Console.SetOut(output);
            var instance = Activator.CreateInstance(closed)
                ?? throw new InvalidOperationException("Null constructed native carrier.");
            foreach (var tag in writableTags)
                Write(tag, instance, tag.Name == (option ? "IsSome" : "IsOk") ? positive : !positive);
            Write(value, instance, payload);
            if (error != null)
                Write(error, instance, failure);
            Check(instance, positive, payload, failure);
            return instance;
        }
        finally
        {
            Console.SetOut(saved);
        }
    }

    public void Check(object instance, bool positive, int payload, string failure)
    {
        var saved = Console.Out;
        using var output = new BoundedOutput();
        try
        {
            Console.SetOut(output);
            if (instance.GetType() != closed)
                throw new InvalidOperationException("Wrong actual carrier input identity.");
            foreach (var tag in tags)
                if (!Equals(Read(tag, instance), tag.Name == (option ? "IsSome" : "IsOk") ? positive : !positive))
                    throw new InvalidOperationException($"Contradictory carrier case observation: {tag.Name}.");
            foreach (var observation in positive ? values : errors)
                if (!Equals(Read(observation, instance), positive ? (object)payload : failure))
                    throw new InvalidOperationException($"Contradictory active carrier payload observation: {observation.Name}.");
        }
        finally
        {
            Console.SetOut(saved);
        }
    }
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
