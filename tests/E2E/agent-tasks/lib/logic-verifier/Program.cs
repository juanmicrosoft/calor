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
    var basic = profile is "basic001" or "basic002" or "basic003" or "basic004";
    var name = profile switch
    {
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
        : name == "PrintNumber" || names.Length == 0 || dictionary ? "void" : name == "IsPositive" ? "bool" : "i32";
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
            || !basic && name != "GradeToLetter" && parameter.InlineRefinement != null))
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
    if (basic)
        CheckBasicEntryDomain(function, names);
    else if (!controlFlow || name == "GradeToLetter")
        CheckContracts(function, names, outputType, profile == "ControlMax");

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
    var parameterTypes = names.Select(_ => dictionary ? typeof(Dictionary<string, int>) : typeof(int)).ToArray();
    var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.Static, parameterTypes)
        ?? throw new InvalidOperationException("Generated public signature not found.");
    var returnType = outputType == "void" ? typeof(void) : outputType == "bool" ? typeof(bool) : typeof(int);
    if (!type.IsPublic || !method.IsPublic || !method.IsStatic || method.IsGenericMethod
        || method.ReturnType != returnType
        || !method.GetParameters().Select(parameter => parameter.ParameterType)
            .SequenceEqual(parameterTypes))
        throw new InvalidOperationException("Wrong generated public/static scalar signature.");
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
    Console.WriteLine($"Verification passed: {name}; {(basic ? "basic signature/behavior" : controlFlow ? "native control flow" : "semantic contracts")} and {count} runtime cases.");
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
