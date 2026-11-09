using System.Reflection;
using System.Text;
using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class CanonicalControlFlowTests
{
    [Theory]
    [InlineData("for i from 0 to 3 step 1", "set n n + i", "6")]
    [InlineData("for i from 3 to 0 step -1", "set n n + i", "6")]
    [InlineData("for i from 3 to 0 step 1", "set n n + 1", "0")]
    [InlineData("for i from 0 to 3 step -1", "set n n + 1", "0")]
    [InlineData("for i from 0 to 3 step 0", "set n n + 1", "throws:ArgumentOutOfRangeException")]
    [InlineData("for i from 3 to 0 step 0", "set n n + 1", "throws:ArgumentOutOfRangeException")]
    [InlineData("for i from 2147483646 to 2147483647 step 2", "set n n + 1", "1")]
    [InlineData("for i from -2147483647 to -2147483648 step -2", "set n n + 1", "1")]
    public void SnapshotRange_PreservesDirectionZeroIterationAndOverflow(string header, string body, string expected)
        => AssertObservation($"var int:n = 0\n{header}\n  {body}\nreturn n", expected);

    [Fact]
    public void SnapshotRange_EvaluatesHeaderOnceAndContinueStillSteps()
        => AssertObservation("""
            var int:n = 0
            for i from Start() to Bound() step Step()
              if i == 1
                continue
              set n n + i
            return n
            """, "5:ABS", """
            public static string Trace = "";
            private static int Start() { Trace += "A"; return 0; }
            private static int Bound() { Trace += "B"; return 3; }
            private static int Step() { Trace += "S"; return 1; }
            """);

    [Fact]
    public void ReEvaluatedFor_PreservesTestUpdateContinueAndFinalFailingTest()
        => AssertObservation("""
            var int:n = 0
            for i = Start(); i < Bound(); i = Next(i)
              if i == 1
                continue
              set n n + i
            return n
            """, "2:ABSBSBSB", """
            public static string Trace = "";
            private static int Start() { Trace += "A"; return 0; }
            private static int Bound() { Trace += "B"; return 3; }
            private static int Next(int i) { Trace += "S"; return i + 1; }
            """);

    [Fact]
    public void ReEvaluatedFor_BreakDoesNotExecuteUpdateOrTestAgain()
        => AssertObservation("""
            var int:n = 0
            for i = 0; i < Bound(); i = Next(i)
              set n n + 1
              break
            return n
            """, "1:B", """
            public static string Trace = "";
            private static int Bound() { Trace += "B"; return 3; }
            private static int Next(int i) { Trace += "S"; return i + 1; }
            """);

    [Fact]
    public void ReEvaluatedFor_AllowsBodyMutationAndInvocationUpdate()
        => AssertObservation("""
            var int:n = 0
            for i = 0; i < 3; Tick()
              set i i + 1
              set n n + i
            return n
            """, "6:TTT", """
            public static string Trace = "";
            private static void Tick() { Trace += "T"; }
            """);

    [Theory]
    [InlineData("checked", "throws:OverflowException")]
    [InlineData("unchecked", "1")]
    public void ReEvaluatedFor_PreservesOverflowPolicy(string overflow, string expected)
        => AssertObservation("""
            var int:n = 0
            for i = 2147483647; n < 2; i = i + 1
              if i < 0
                return 1
              set n n + 1
            return 0
            """, expected, overflow: overflow);

    [Fact]
    public void ReEvaluatedFor_UpdateComputedStoreKeepsOperandOrder()
        => AssertObservation("""
            var int:n = 0
            for i = 0; i < 2; Store()[Key()] = Value()
              set i i + 1
              set n n + 1
            return n
            """, "2:AKVAKV", """
            public static string Trace = "";
            public static int[] Values = new int[1];
            private static int[] Store() { Trace += "A"; return Values; }
            private static int Key() { Trace += "K"; return 0; }
            private static int Value() { Trace += "V"; return 1; }
            """);

    [Fact]
    public void NativeSet_ComputedStoreEvaluatesReceiverIndexAndValueOnce()
        => AssertObservation("""
            set Store()[Key()] Value()
            return 1
            """, "1:AKV", """
            public static string Trace = "";
            public static int[] Values = new int[1];
            private static int[] Store() { Trace += "A"; return Values; }
            private static int Key() { Trace += "K"; return 0; }
            private static int Value() { Trace += "V"; return 1; }
            """);

    [Fact]
    public void NativeDo_PreservesUnassignedLocalAndFirstIteration()
        => AssertObservation("""
            var int:result
            var int:i = 0
            do
              set result i
              set i i + 1
            while i < 2
            return result
            """, "1");

    [Fact]
    public void NativeDo_PreservesOutParameterDefiniteAssignment()
    {
        var result = Compile("""
            module Migrated
              func Assign (out int:result) → void
                var int:i = 0
                do
                  set result i
                  set i i + 1
                while i < 2
            """);
        var type = Load(result.GeneratedCode);
        object?[] arguments = [null];
        type.GetMethod("Assign")!.Invoke(null, arguments);
        Assert.Equal(1, arguments[0]);
    }

    [Fact]
    public void EachDeconstruction_EvaluatesOnceAndDisposesOnContinueAndBreak()
        => AssertObservation("""
            var int:n = 0
            each k, v in Pairs()
              if k == 1
                continue
              set n n + v
              break
            return n
            """, "20:ABCD", """
            public static string Trace = "";
            private static System.Collections.Generic.IEnumerable<(int, int)> Pairs()
            {
                Trace += "A";
                try
                {
                    Trace += "B"; yield return (1, 10);
                    Trace += "C"; yield return (2, 20);
                    Trace += "X"; yield return (3, 30);
                }
                finally { Trace += "D"; }
            }
            """);

    [Fact]
    public void CanonicalFor_RoundTripRetainsDistinctSemanticsAndHeaderSpans()
    {
        var source = """
            module Loops
              func Run (int:n) → void
                for i from 0 to n step 1
                  continue
                for j = 0; j < n; j = j + 1
                  break
            """;
        var module = Parse(source);
        var loops = module.Functions[0].Body.OfType<ForStatementNode>().ToArray();
        Assert.False(loops[0].ReevaluatesCondition);
        Assert.True(loops[1].ReevaluatesCondition);
        Assert.Equal("i", source.Substring(loops[0].VariableSpan.Start, loops[0].VariableSpan.Length));
        var emitted = new CalorEmitter().Emit(module);
        Assert.DoesNotContain('§', emitted);
        var reparsed = Parse(emitted).Functions[0].Body.OfType<ForStatementNode>().ToArray();
        Assert.False(reparsed[0].ReevaluatesCondition);
        Assert.True(reparsed[1].ReevaluatesCondition);
    }

    [Fact]
    public void NativeSet_EmitsAndRoundTripsWithoutLegacyStatementTags()
    {
        var source = """
            module Stores
              func Probe () → int
                var int:n = 0
                set n n + 3
                return n
            """;
        var emitted = new CalorEmitter().Emit(Parse(source));
        Assert.Contains("set n (n + 3)", emitted);
        Assert.DoesNotContain('§', emitted);
        var type = Load(Compile(emitted).GeneratedCode);
        Assert.Equal(3, type.GetMethod("Probe")!.Invoke(null, null));
    }

    [Theory]
    [InlineData("§L{i:0:3:1}")]
    [InlineData("§WH true")]
    [InlineData("§DO")]
    [InlineData("§EACH{item:int} xs")]
    [InlineData("§EACHKV{k:v} xs")]
    [InlineData("for {old:i:0:3:1}")]
    public void RemovedLoopForms_AreErrorsNotCompatibility(string header)
    {
        var diagnostics = new DiagnosticBag();
        _ = new Parser(new Lexer($"module Bad\n  func Run (int[]:xs) → void\n    {header}\n      break",
            diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.True(diagnostics.HasErrors);
    }

    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors));
        return module;
    }

    private static CompilationResult Compile(string source)
    {
        var result = Program.Compile(source, "canonical-controls.calr", new CompilationOptions
        {
            EnableTypeChecking = true,
            EnforceEffects = true,
            StatusWriter = TextWriter.Null
        });
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics.Errors) + "\n" + source);
        return result;
    }

    private static void AssertObservation(string body, string expected, string members = "", string overflow = "checked")
    {
        var raw = EmitInstrumentation(members);
        var effects = members.Length == 0 ? "" : "    ! mut\n";
        var source = $"module Migrated overflow={overflow}\n{raw}  func Probe () → int\n{effects}" +
            string.Join("\n", body.Split('\n').Select(line => "    " + line));
        AssertObservationSource(source, expected);
        var emitted = new CalorEmitter().Emit(Parse(source));
        AssertObservationSource(emitted, expected);
    }

    private static void AssertObservationSource(string source, string expected)
    {
        var result = Compile(source);
        var type = Load(result.GeneratedCode);
        string observed;
        try
        {
            observed = type.GetMethod("Probe")!.Invoke(null, null)!.ToString()!;
        }
        catch (TargetInvocationException exception)
        {
            observed = "throws:" + exception.InnerException!.GetType().Name;
        }
        var instrumentation = type.Assembly.GetTypes().SingleOrDefault(candidate => candidate.Name == "Instrumentation");
        var trace = instrumentation?.GetField("Trace")?.GetValue(null) as string;
        Assert.Equal(expected, observed + (string.IsNullOrEmpty(trace) ? "" : ":" + trace));
    }

    private static string EmitInstrumentation(string members)
    {
        var source = CSharpSyntaxTree.ParseText("class Helpers {\n" + members + "\n}")
            .GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
        var text = new StringBuilder();
        var fields = source.Members.OfType<FieldDeclarationSyntax>().ToArray();
        var fieldNames = fields.SelectMany(field => field.Declaration.Variables)
            .Select(variable => variable.Identifier.Text).ToHashSet(StringComparer.Ordinal);
        if (fields.Length != 0)
        {
            var declaration = "public static class Instrumentation {\n" +
                string.Concat(fields.Select(field => field.ToFullString())) + "\n}";
            text.AppendLine($"  raw member {Encoding.UTF8.GetByteCount(declaration)}");
            text.Append(declaration).AppendLine();
        }
        foreach (var method in source.Members.OfType<MethodDeclarationSyntax>())
        {
            var parameters = string.Join(", ", method.ParameterList.Parameters
                .Select(parameter => $"{parameter.Type}:{parameter.Identifier.Text}"));
            text.AppendLine($"  func {method.Identifier.Text} private ({parameters}) → {method.ReturnType}");
            text.AppendLine("    ! mut");
            var body = method.Body!;
            var qualified = body.ReplaceNodes(body.DescendantNodes().OfType<IdentifierNameSyntax>()
                .Where(identifier => fieldNames.Contains(identifier.Identifier.Text)),
                (identifier, _) => SyntaxFactory.ParseExpression("Instrumentation." + identifier.Identifier.Text)
                    .WithTriviaFrom(identifier));
            var payload = string.Concat(qualified.Statements.Select(statement => statement.ToFullString()));
            text.AppendLine($"    raw statement {Encoding.UTF8.GetByteCount(payload)}");
            text.Append(payload).AppendLine();
        }
        return text.ToString();
    }

    private static Type Load(string code)
    {
        var compilation = CSharpCompilation.Create("CanonicalControls_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(code)], GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics) + "\n" + code);
        return Assembly.Load(image.ToArray()).GetTypes()
            .Single(type => type.GetMethod("Probe") != null || type.GetMethod("Assign") != null);
    }
}
