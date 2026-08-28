using System.Text.RegularExpressions;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;
using Xunit.Abstractions;

namespace Calor.Conversion.Tests;

/// <summary>
/// v0.16 W3(a)+(b) — converter reach (roadmap-v0.16.md §3.1 W3; gate 9 in §5).
///
/// <para>#903: the converter emitted Calor its own parser rejected, in three
/// clusters over the A-1.5.3 corpus. Each fixture below is the smallest C# shape
/// that reproduced one cluster; every test converts it and re-parses the output
/// with a fresh <see cref="DiagnosticBag"/>, asserting zero Calor0099 / Calor0100 /
/// Calor0117 (or any other error). Revert the matching emitter/parser change and
/// the test is red.</para>
///
/// <para>#1097: a lambda parameter whose type inference fails was written as
/// <c>§LAM{id:x:?}</c>; the binder's canonicalizer throws on <c>?</c> and the
/// member is abandoned with an AnalysisICE (Calor0932). The (b) fixture binds the
/// converted output and asserts no Calor0932 — the roadmap's discriminating line
/// ("revert (b) → the ICE fixture test is red").</para>
/// </summary>
public class ConverterReachTests
{
    private readonly ITestOutputHelper _output;

    public ConverterReachTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // ----- helpers -------------------------------------------------------

    private sealed record RoundTrip(string Calor, ModuleNode Module, DiagnosticBag ParseDiagnostics);

    /// <summary>
    /// Converts with the exact options the F-2 conversion-leg instrument uses
    /// (<c>BinderIncompleteRatchetTests.MeasureNative</c>): Lossy fidelity,
    /// SelectActiveBranchLossy, empty preprocessor symbols, graceful fallback.
    /// </summary>
    private RoundTrip ConvertAndParse(string csharp, string moduleName)
    {
        var parseOptions = new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions(
            Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview,
            Microsoft.CodeAnalysis.DocumentationMode.Parse,
            Microsoft.CodeAnalysis.SourceCodeKind.Regular,
            preprocessorSymbols: Array.Empty<string>());
        var result = new CSharpToCalorConverter(new ConversionOptions
        {
            Fidelity = ConversionFidelity.Lossy,
            PreprocessorMode = PreprocessorConversionMode.SelectActiveBranchLossy,
            ParseOptions = parseOptions,
            DefinedSymbols = Array.Empty<string>(),
            ModuleName = moduleName,
            GracefulFallback = true,
            AutoGenerateIds = true,
        }).Convert(csharp, moduleName + ".cs");

        // Like the instrument, judge the OUTPUT, not the converter's own verdict:
        // ConversionResult.Success also folds in round-trip C# compilation, which
        // fails for single-file fixtures that reference types they do not declare.
        Assert.False(string.IsNullOrEmpty(result.CalorSource),
            string.Join("; ", result.Issues.Select(i => i.Message)));
        var calor = result.CalorSource!.Replace("\r\n", "\n");
        _output.WriteLine(calor);

        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(calor, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        return new RoundTrip(calor, module, diagnostics);
    }

    private static void AssertParsesClean(RoundTrip rt)
    {
        var errors = rt.ParseDiagnostics.Errors.Select(d => $"{d.Code} L{d.Span.Line}: {d.Message}").ToList();
        Assert.True(errors.Count == 0,
            "Converted Calor does not parse:\n  " + string.Join("\n  ", errors) + "\n\n" + rt.Calor);
        Assert.DoesNotContain(rt.ParseDiagnostics, d => d.Code == DiagnosticCode.MixedIndentation);
        Assert.DoesNotContain(rt.ParseDiagnostics, d => d.Code == "Calor0100");
        Assert.DoesNotContain(rt.ParseDiagnostics, d => d.Code == "Calor0117");
    }

    /// <summary>
    /// Review M4: parsing is not meaning. Compiles the converted Calor back to C#
    /// and asserts the generated C# itself compiles — the check that catches an
    /// emitted name used outside the scope that declares it (review C1), which a
    /// parse-only assertion cannot see. Effects are off: these fixtures call BCL
    /// methods with no effect manifest (Calor0410/0411), which is a separate gap.
    /// </summary>
    private void AssertRoundTripCompiles(RoundTrip rt)
    {
        var result = Compiler.Program.Compile(
            rt.Calor,
            "converted.calr",
            new Compiler.CompilationOptions
            {
                EnforceEffects = false,
                UnknownCallPolicy = Compiler.Effects.UnknownCallPolicy.Permissive,
                DeferGeneratedOutputValidation = true,
            });
        Assert.False(result.HasErrors,
            "Converted Calor does not compile:\n  "
            + string.Join("\n  ", result.Diagnostics.Errors.Select(d => $"{d.Code}: {d.Message}"))
            + "\n\n" + rt.Calor);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.AnalysisICE);
        _output.WriteLine(result.GeneratedCode ?? "");

        var validation = TestHelpers.RoslynCompile(result.GeneratedCode!);
        Assert.True(validation.SyntaxErrors.Count == 0 && validation.CompilationErrors.Count == 0,
            "Round-tripped C# does not compile:\n  "
            + string.Join("\n  ", validation.SyntaxErrors.Concat(validation.CompilationErrors))
            + "\n\n" + result.GeneratedCode);
    }

    private static DiagnosticBag Bind(RoundTrip rt)
    {
        var bag = new DiagnosticBag();
        new Binder(bag).Bind(rt.Module);
        return bag;
    }

    /// <summary>Every §LAM header that carries a bare "?" (or a nested "?") type.</summary>
    private static IEnumerable<string> UnknownTypedLambdaHeaders(string calor)
        => Regex.Matches(calor, @"§LAM\{[^}\n]*\}")
            .Select(m => m.Value)
            .Where(h => Regex.IsMatch(h, @"[:<]\?[:}>,\]]|:\?$"));

    // ----- #903 cluster 1: Calor0099 dedent mismatch ----------------------

    /// <summary>
    /// Serilog <c>Matching.cs</c> shape: a lambda returned from a method whose body
    /// hoists a temp (<c>TryGetValue(..., out var)</c>) inside a nested block. The
    /// old emitter wrote the body at column 2 and <c>§/LAM</c> at column 0.
    /// </summary>
    [Fact]
    public void Cluster1_ReturnedBlockLambda_WithHoistInNestedBlock_Parses()
    {
        const string csharp = """
            using System;
            using System.Collections.Generic;

            public static class Matching
            {
                public static Func<Dictionary<string, object>, bool> WithProperty(string propertyName, Func<object, bool> predicate)
                {
                    return e =>
                    {
                        if (!e.TryGetValue(propertyName, out var propertyValue)) return false;
                        var scalar = propertyValue as string;
                        if (scalar != null && scalar.Length == 0) return false;
                        return predicate(propertyValue);
                    };
                }
            }
            """;

        var rt = ConvertAndParse(csharp, "Cluster1Return");

        AssertParsesClean(rt);
        Assert.Contains("§/LAM{", rt.Calor);
        // The block lands under a hoisted binding at the statement's own indent —
        // never at column 0 mid-module.
        Assert.DoesNotMatch(new Regex(@"\n§/LAM\{"), rt.Calor);
        Assert.DoesNotMatch(new Regex(@"\n§(R|B|IF)\b"), rt.Calor);
    }

    /// <summary>
    /// FluentValidation tester shape: a two-statement lambda whose first statement
    /// binds a longer block lambda. The old emitter space-joined the statements
    /// even though the first one spanned several lines.
    /// </summary>
    [Fact]
    public void Cluster1_ShortLambda_ContainingBlockLambdaBinding_Parses()
    {
        const string csharp = """
            using System;

            public class Person { public string Surname; public string Forename; public int Age; }

            public class Factory
            {
                public Func<Person> Build()
                {
                    return () =>
                    {
                        Action<Person> fill = p =>
                        {
                            p.Surname = "a";
                            p.Forename = "b";
                            p.Age = 1;
                        };
                        return Make(fill);
                    };
                }

                private static Person Make(Action<Person> fill) { var p = new Person(); fill(p); return p; }
            }
            """;

        var rt = ConvertAndParse(csharp, "Cluster1Nested");

        AssertParsesClean(rt);
        Assert.Equal(2, Regex.Matches(rt.Calor, @"§/LAM\{").Count);
    }

    /// <summary>
    /// FluentValidation <c>new TestValidator { v => { ... }, v => { ... } }</c>
    /// shape: block lambdas as object-initializer values inside a method body.
    /// </summary>
    [Fact]
    public void Cluster1_BlockLambda_AsObjectInitializerValue_Parses()
    {
        const string csharp = """
            using System;
            using System.Collections;
            using System.Collections.Generic;

            public class Person { public string Surname; public int Id; }

            public class TestValidator : IEnumerable<Action<TestValidator>>
            {
                private readonly List<Action<TestValidator>> _actions = new();
                public void Add(Action<TestValidator> action) => _actions.Add(action);
                public TestValidator RuleFor(Func<Person, object> selector) => this;
                public TestValidator NotNull() => this;
                public IEnumerator<Action<TestValidator>> GetEnumerator() => _actions.GetEnumerator();
                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }

            public class Tester
            {
                public TestValidator Build()
                {
                    var validator = new TestValidator
                    {
                        v =>
                        {
                            v.RuleFor(x => x.Surname).NotNull();
                            v.RuleFor(x => x.Id).NotNull();
                            v.RuleFor(x => x.Surname);
                        },
                        v =>
                        {
                            v.RuleFor(x => x.Id).NotNull();
                            v.RuleFor(x => x.Id);
                            v.RuleFor(x => x.Surname);
                        }
                    };
                    return validator;
                }
            }
            """;

        var rt = ConvertAndParse(csharp, "Cluster1Initializer");

        AssertParsesClean(rt);
        Assert.Contains("§NEW{TestValidator}", rt.Calor);
    }

    /// <summary>
    /// FluentValidation <c>InlineValidatorTester</c> shape: the same collection
    /// initializer of block lambdas, but as a FIELD initializer — no enclosing
    /// statement to hoist ahead of, so the block is rendered in place.
    /// </summary>
    [Fact]
    public void Cluster1_BlockLambda_InFieldInitializer_Parses()
    {
        const string csharp = """
            using System;
            using System.Collections;
            using System.Collections.Generic;

            public class Person { public string Surname; public int Id; }

            public class TestValidator : IEnumerable<Action<TestValidator>>
            {
                private readonly List<Action<TestValidator>> _actions = new();
                public void Add(Action<TestValidator> action) => _actions.Add(action);
                public TestValidator RuleFor(Func<Person, object> selector) => this;
                public TestValidator NotNull() => this;
                public IEnumerator<Action<TestValidator>> GetEnumerator() => _actions.GetEnumerator();
                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }

            public class Customer
            {
                private static readonly TestValidator Validator = new TestValidator
                {
                    v =>
                    {
                        v.RuleFor(x => x.Surname).NotNull();
                        v.RuleFor(x => x.Id).NotNull();
                        v.RuleFor(x => x.Surname);
                    },
                    v =>
                    {
                        v.RuleFor(x => x.Id).NotNull();
                        v.RuleFor(x => x.Id);
                        v.RuleFor(x => x.Surname);
                    }
                };

                public void Validate() { }
            }
            """;

        var rt = ConvertAndParse(csharp, "Cluster1Field");

        AssertParsesClean(rt);
    }

    /// <summary>
    /// Serilog <c>PropertyValueConverter</c> shape: a fluent chain the converter
    /// keeps as one call target, with the C# line break before <c>.Concat</c>.
    /// The target reached <c>§C{receiver\n    .Concat}</c>.
    /// </summary>
    [Fact]
    public void Cluster1_MultiLineFluentCallTarget_Parses()
    {
        const string csharp = """
            using System.Collections.Generic;
            using System.Linq;

            public interface IPolicy { }
            public class DelegatePolicy : IPolicy { }
            public class ReflectionPolicy : IPolicy { }

            public class Converter
            {
                private readonly IPolicy[] _policies;

                public Converter(IEnumerable<IPolicy> additionalPolicies)
                {
                    _policies = additionalPolicies
                        .Concat(new IPolicy[]
                        {
                            new DelegatePolicy(),
                            new ReflectionPolicy()
                        })
                        .ToArray();
                }
            }
            """;

        var rt = ConvertAndParse(csharp, "Cluster1Fluent");

        AssertParsesClean(rt);
        Assert.DoesNotMatch(new Regex(@"§C\{[^}\n]*\n"), rt.Calor);
    }

    /// <summary>
    /// FluentValidation <c>CascadingFailuresTester</c> shape: a list initializer
    /// whose elements are object initializers (multi-line <c>§NEW ... §/NEW</c>).
    /// </summary>
    [Fact]
    public void Cluster1_ListInitializer_WithObjectInitializerElements_Parses()
    {
        const string csharp = """
            using System.Collections.Generic;

            public class Order { public string ProductName; public decimal Amount; }
            public class Person { public List<Order> Orders; }

            public class Tester
            {
                public Person Build()
                {
                    var testData = new List<Order>
                    {
                        new Order { ProductName = null, Amount = 0 },
                        new Order { ProductName = "foo", Amount = 0 }
                    };
                    var person = new Person { Orders = testData };
                    return person;
                }
            }
            """;

        var rt = ConvertAndParse(csharp, "Cluster1List");

        AssertParsesClean(rt);
        Assert.Contains("§LIST{testData:Order}", rt.Calor);
    }

    // ----- #903 cluster 2: Calor0100 empty §IFACE followed by a sibling ----

    [Fact]
    public void Cluster2_EmptyInterface_FollowedBySiblingTypes_Parses()
    {
        const string csharp = """
            namespace Contracts
            {
                /// <summary>Marker interface.</summary>
                public interface IMarker { }

                public class Impl : IMarker { }

                public interface IRequest { }

                public interface IRequest<out TResponse> : IRequest { }

                public enum Mode { Fast, Slow }

                public interface IHandler { void Run(); }

                public interface ILast { }
            }
            """;

        var rt = ConvertAndParse(csharp, "Cluster2");

        AssertParsesClean(rt);
        Assert.Equal(5, rt.Module.Interfaces.Count);
        Assert.Single(rt.Module.Classes);
        Assert.Single(rt.Module.Enums);
        Assert.Single(rt.Module.Interfaces.Single(i => i.Name == "IHandler").Methods);
        Assert.All(rt.Module.Interfaces.Where(i => i.Name != "IHandler"), i => Assert.Empty(i.Methods));
    }

    // ----- #903 cluster 3: Calor0117 §EI misalignment -----------------------

    /// <summary>
    /// Serilog <c>MessageTemplateRenderer</c> shape: an else-if whose condition
    /// hoists a temp (an indexer read). The hoisted <c>§B{~_hoistNNN}</c> line
    /// used to land between the then-body and <c>§EI</c>, closing the chain.
    /// </summary>
    [Fact]
    public void Cluster3_ElseIf_WithHoistedCondition_Parses()
    {
        const string csharp = """
            public static class Renderer
            {
                public static (bool, bool) Flags(string format)
                {
                    var isLiteral = false;
                    var isJson = false;
                    if (format != null)
                    {
                        for (var i = 0; i < format.Length; ++i)
                        {
                            if (format[i] == 'l')
                                isLiteral = true;
                            else if (format[i] == 'j')
                                isJson = true;
                            else if (format[i] == 'x')
                                isLiteral = false;
                            else
                                isJson = false;
                        }
                    }
                    return (isLiteral, isJson);
                }
            }
            """;

        var rt = ConvertAndParse(csharp, "Cluster3");

        AssertParsesClean(rt);
        // The chain is re-nested: §EL + hoisted binding + nested §IF, with the
        // trailing else preserved once at the innermost level.
        Assert.Contains("§EL", rt.Calor);
        Assert.Matches(new Regex(@"§EL\n\s+§B\{~_hoist\d+\}"), rt.Calor);
        var method = rt.Module.Classes.Single().Methods.Single();
        var outerIf = method.Body.OfType<IfStatementNode>().Single();
        var loop = outerIf.ThenBody.OfType<ForStatementNode>().Single();
        var chain = loop.Body.OfType<IfStatementNode>().Single();
        Assert.NotNull(chain.ElseBody);
        // Every else-if condition hoisted here, so each level is one nested §IF.
        var nested = chain.ElseBody!.OfType<IfStatementNode>().Single();
        var innermost = nested.ElseBody!.OfType<IfStatementNode>().Single();
        Assert.NotNull(innermost.ElseBody);
        Assert.Empty(innermost.ElseIfClauses);
    }

    [Fact]
    public void Cluster3_ElseIf_WithoutHoist_KeepsFlatChain()
    {
        const string csharp = """
            public static class Grade
            {
                public static string Of(int score)
                {
                    if (score > 90) return "A";
                    else if (score > 80) return "B";
                    else if (score > 70) return "C";
                    else return "F";
                }
            }
            """;

        var rt = ConvertAndParse(csharp, "Cluster3Flat");

        AssertParsesClean(rt);
        var chain = rt.Module.Classes.Single().Methods.Single().Body.OfType<IfStatementNode>().Single();
        Assert.Equal(2, chain.ElseIfClauses.Count);
        Assert.NotNull(chain.ElseBody);
        Assert.Equal(2, Regex.Matches(rt.Calor, @"§EI ").Count);
    }

    // ----- #1097: bare "?" lambda parameter type -> AnalysisICE -------------

    /// <summary>
    /// FluentValidation <c>AssemblyScannerTester</c> shape: <c>scanner.ForEach(x
    /// => results.Add(x))</c> where <c>scanner</c>'s type is not resolvable in the
    /// single-file conversion compilation, so <c>x</c> gets a nameless error type
    /// ("?"). Discriminating for W3(b): revert <c>TryInferLambdaParameterType</c>
    /// and the header reads <c>§LAM{...:x:?}</c>, binding reports Calor0932.
    /// </summary>
    [Fact]
    public void Issue1097_UninferrableLambdaParameter_EmitsNoBareQuestionMark_AndBindsWithoutIce()
    {
        const string csharp = """
            using System.Collections.Generic;

            public class Tester
            {
                public int Run(AssemblyScanner scanner)
                {
                    var results = new List<ScanResult>();
                    scanner.ForEach(x => results.Add(x));
                    return results.Count;
                }
            }
            """;

        var rt = ConvertAndParse(csharp, "Issue1097");

        AssertParsesClean(rt);
        Assert.Empty(UnknownTypedLambdaHeaders(rt.Calor));
        Assert.Contains("§LAM{", rt.Calor);

        var bag = Bind(rt);
        var ices = bag.Where(d => d.Code == DiagnosticCode.AnalysisICE).Select(d => d.Message).ToList();
        Assert.True(ices.Count == 0, "AnalysisICE reported:\n  " + string.Join("\n  ", ices) + "\n\n" + rt.Calor);
    }

    /// <summary>
    /// FluentValidation <c>ValidationResult.ToDictionary</c> shape: <c>Failure</c>
    /// is declared elsewhere, so <c>x</c> keeps the written name but the group KEY
    /// (<c>x.PropertyName</c>) cannot be typed and the error type is NESTED —
    /// <c>IGrouping&lt;?, Failure&gt;</c> — which the old guard
    /// (<c>SpecialType.System_Object</c> only) let through as well.
    /// </summary>
    [Fact]
    public void Issue1097_NestedUninferrableTypeArgument_EmitsNoQuestionMark_AndBindsWithoutIce()
    {
        const string csharp = """
            using System.Collections.Generic;
            using System.Linq;

            public class Result
            {
                public List<Failure> Errors = new();

                public IDictionary<string, string[]> ToDictionary()
                {
                    return Errors
                        .GroupBy(x => x.PropertyName)
                        .ToDictionary(
                            g => g.Key,
                            g => g.Select(x => x.ErrorMessage).ToArray()
                        );
                }
            }
            """;

        var rt = ConvertAndParse(csharp, "Issue1097Nested");

        AssertParsesClean(rt);
        Assert.Empty(UnknownTypedLambdaHeaders(rt.Calor));
        Assert.DoesNotContain("<?", rt.Calor);

        var bag = Bind(rt);
        Assert.DoesNotContain(bag, d => d.Code == DiagnosticCode.AnalysisICE);
    }

    /// <summary>
    /// The guard must NOT erase a written type name that merely fails to resolve
    /// in the single-file compilation (a named error type): that spelling is
    /// real information for the binder and was never an ICE.
    /// </summary>
    [Fact]
    public void Issue1097_ExplicitlyTypedParameter_OfUnresolvedNamedType_KeepsItsSpelling()
    {
        const string csharp = """
            using System;

            public class Rule
            {
                public Func<ValidationContext, bool> Condition;

                public Rule()
                {
                    Condition = (ValidationContext ctx) => ctx != null;
                }
            }
            """;

        var rt = ConvertAndParse(csharp, "Issue1097Named");

        AssertParsesClean(rt);
        Assert.Contains("ctx:ValidationContext", rt.Calor);
        Assert.Empty(UnknownTypedLambdaHeaders(rt.Calor));
        Assert.DoesNotContain(Bind(rt), d => d.Code == DiagnosticCode.AnalysisICE);
    }

    [Fact]
    public void Issue1097_ResolvableParameter_StillInfersItsType()
    {
        const string csharp = """
            using System.Collections.Generic;
            using System.Linq;

            public class Tester
            {
                public int Count(List<string> names) => names.Count(n => n.Length > 2);
            }
            """;

        var rt = ConvertAndParse(csharp, "Issue1097Inferred");

        AssertParsesClean(rt);
        Assert.Contains("n:str", rt.Calor);
    }

    // ----- review C1: a block lambda inside another lambda's scope -----------

    /// <summary>
    /// Review C1 (regression pin). A block lambda as an object-initializer value
    /// INSIDE an expression lambda must stay inside that lambda: hoisting it to a
    /// §B ahead of the enclosing statement puts it outside the outer lambda's
    /// parameter scope. The emitted Calor still PARSES, so only the round-trip
    /// compile sees it — on main this fixture converted (via the §CSHARP
    /// fallback); with the hoist ungated it failed conversion outright.
    /// </summary>
    [Fact]
    public void C1_BlockLambda_InsideExpressionLambda_StaysInScope()
    {
        const string csharp = """
            using System;
            using System.Collections.Generic;
            using System.Linq;

            public class Handler { public int Id; public Action OnClick; }

            public class Demo
            {
                public List<Handler> Build(List<int> xs)
                {
                    return xs.Select(x => new Handler
                    {
                        Id = x,
                        OnClick = () =>
                        {
                            Console.WriteLine(x);
                            Console.WriteLine(x + 1);
                            Console.WriteLine(x + 2);
                        }
                    }).ToList();
                }
            }
            """;

        var rt = ConvertAndParse(csharp, "C1Scope");

        AssertParsesClean(rt);
        // The lambda parameter must not be referenced from a hoisted binding that
        // sits outside the lambda: binding reports Calor0200 if it is.
        Assert.DoesNotContain(Bind(rt), d => d.Code == "Calor0200");
        Assert.DoesNotContain("§B{_hoistLam", rt.Calor);
        AssertRoundTripCompiles(rt);
    }

    /// <summary>
    /// Review C1(b) / N2: under <c>RescueUnusableMembers</c> — the option the CLI
    /// sets, because it discards the output entirely on failure — the #717 rewrap
    /// also fires when the emitted Calor PARSES but does not survive the C# round
    /// trip. The member's original C# is preserved as a §CSHARP
    /// block and the conversion still yields usable output, instead of failing
    /// with round-trip errors and writing nothing.
    ///
    /// The fixture is a construct the converter genuinely cannot round-trip: a
    /// local function taking a <c>ref</c> parameter and yielding. It reaches the
    /// same rewrap path C1(a) would otherwise have needed.
    /// </summary>
    [Fact]
    public void C1b_RescueUnusableMembers_PreservesAMemberThatParsesButDoesNotRoundTrip()
    {
        const string csharp = """
            using System.Collections.Generic;

            public class Iterators
            {
                public void Run()
                {
                    IEnumerable<int> Local(ref int value)
                    {
                        yield return value;
                    }
                }
            }
            """;

        var parseOptions = new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions(
            Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview,
            Microsoft.CodeAnalysis.DocumentationMode.Parse,
            Microsoft.CodeAnalysis.SourceCodeKind.Regular,
            preprocessorSymbols: Array.Empty<string>());
        var result = new CSharpToCalorConverter(new ConversionOptions
        {
            Fidelity = ConversionFidelity.Lossless,
            ParseOptions = parseOptions,
            ModuleName = "C1bRescue",
            GracefulFallback = true,
            AutoGenerateIds = true,
            RescueUnusableMembers = true,
        }).Convert(csharp, "C1bRescue.cs");

        // The contract: whatever else happened, the output exists and parses.
        Assert.False(string.IsNullOrEmpty(result.CalorSource));
        var diagnostics = new DiagnosticBag();
        _ = new Parser(
            new Lexer(result.CalorSource!.Replace("\r\n", "\n"), diagnostics).TokenizeAllForParser(),
            diagnostics).Parse();
        Assert.False(diagnostics.HasErrors,
            string.Join("\n", diagnostics.Errors.Select(d => $"{d.Code}: {d.Message}"))
            + "\n\n" + result.CalorSource);
        Assert.Contains("§CSHARP", result.CalorSource);
    }

    /// <summary>
    /// Review N2 (discriminating): the triple-nested shape — a block lambda inside
    /// an object initializer inside a lambda inside another lambda — converted
    /// with the CLI's own options (<c>RescueUnusableMembers</c>, and deliberately
    /// NOT <c>PassthroughOnError</c>). On `main` the member is preserved as
    /// §CSHARP; at 4b65dc34 the conversion failed with four CS0103s and wrote
    /// nothing. It must produce output — and in fact now converts fully native.
    /// </summary>
    [Fact]
    public void N2_TripleNestedLambda_WithCliOptions_ProducesOutput()
    {
        const string csharp = """
            using System;
            using System.Collections.Generic;
            using System.Linq;

            public class Node { public int Id; public Func<int, Node> Make; public Action Run; }

            public class Demo
            {
                public List<Node> Build(List<int> xs)
                {
                    return xs.Select(x => new Node
                    {
                        Id = x,
                        Make = y => new Node
                        {
                            Run = () =>
                            {
                                Console.WriteLine(x);
                                Console.WriteLine(y);
                                Console.WriteLine(x + y);
                            }
                        }
                    }).ToList();
                }
            }
            """;

        var parseOptions = new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions(
            Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview,
            Microsoft.CodeAnalysis.DocumentationMode.Parse,
            Microsoft.CodeAnalysis.SourceCodeKind.Regular,
            preprocessorSymbols: Array.Empty<string>());
        var result = new CSharpToCalorConverter(new ConversionOptions
        {
            ParseOptions = parseOptions,
            ModuleName = "N2Triple",
            GracefulFallback = true,
            AutoGenerateIds = true,
            RescueUnusableMembers = true,
        }).Convert(csharp, "N2Triple.cs");

        Assert.True(result.Success, string.Join("; ", result.Issues.Select(i => i.Message)));
        Assert.False(string.IsNullOrEmpty(result.CalorSource));
        _output.WriteLine(result.CalorSource);
        // Not merely rescued — the nesting is emitted natively, with no §B hoisted
        // out of the lambdas that declare the captured names.
        Assert.DoesNotContain("§CSHARP", result.CalorSource);
        Assert.DoesNotContain("§B{_hoistLam", result.CalorSource);

        var diagnostics = new DiagnosticBag();
        _ = new Parser(
            new Lexer(result.CalorSource!.Replace("\r\n", "\n"), diagnostics).TokenizeAllForParser(),
            diagnostics).Parse();
        Assert.False(diagnostics.HasErrors,
            string.Join("\n", diagnostics.Errors.Select(d => $"{d.Code}: {d.Message}")));
    }

    /// <summary>
    /// Review m13 — the shape the verification pass actually found, and the one
    /// the rescue path exists for. Same triple nesting as above, but the inner
    /// object initializer is followed by a MEMBER ACCESS (<c>…}.Run</c>), which
    /// forces the object-initializer hoist: that hoist is not gated by
    /// <c>_lambdaBodyDepth</c> (deliberately — gating it would move
    /// currently-parsing output and the ledgers), so the block leaves the
    /// enclosing lambdas' scope, the output still PARSES, and the round trip
    /// fails. Under <c>RescueUnusableMembers</c> the member is preserved as
    /// §CSHARP and the conversion still yields output.
    ///
    /// <para>This is the honest boundary of the landing state: the lambda hoist
    /// is closed at any depth, while the object-initializer and collection-element
    /// hoists are CONTAINED by the rescue rather than fixed.</para>
    /// </summary>
    [Fact]
    public void M13_TripleNestedLambda_WithTrailingMemberAccess_IsRescuedNotNative()
    {
        const string csharp = """
            using System;
            using System.Collections.Generic;
            using System.Linq;

            public class Node { public int Id; public Func<int, Action> Make; public Action Run; }

            public class Demo
            {
                public List<Node> Build(List<int> xs)
                {
                    return xs.Select(x => new Node
                    {
                        Id = x,
                        Make = y => new Node
                        {
                            Run = () =>
                            {
                                Console.WriteLine(x);
                                Console.WriteLine(y);
                                Console.WriteLine(x + y);
                            }
                        }.Run
                    }).ToList();
                }
            }
            """;

        var parseOptions = new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions(
            Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview,
            Microsoft.CodeAnalysis.DocumentationMode.Parse,
            Microsoft.CodeAnalysis.SourceCodeKind.Regular,
            preprocessorSymbols: Array.Empty<string>());
        // The CLI's own option shape. ModuleName is deliberately NOT overridden:
        // #717's rewrap matches a failed member against the C# it collected by
        // fully-qualified identity, and an overridden module name breaks that
        // match — a pre-existing limitation of the rewrap, unrelated to this PR,
        // but one this test would otherwise trip over silently.
        var result = new CSharpToCalorConverter(new ConversionOptions
        {
            ParseOptions = parseOptions,
            GracefulFallback = true,
            RescueUnusableMembers = true,
        }).Convert(csharp, "M13Triple.cs");

        // Output exists and parses — the contract the rescue is there to keep.
        Assert.False(string.IsNullOrEmpty(result.CalorSource));
        _output.WriteLine(result.CalorSource);
        var diagnostics = new DiagnosticBag();
        _ = new Parser(
            new Lexer(result.CalorSource!.Replace("\r\n", "\n"), diagnostics).TokenizeAllForParser(),
            diagnostics).Parse();
        Assert.False(diagnostics.HasErrors,
            string.Join("\n", diagnostics.Errors.Select(d => $"{d.Code}: {d.Message}")));

        // ...and it is RESCUED, not native: this pins the boundary, so a later
        // change that closed the object-initializer hoist would fail here and be
        // recorded rather than sliding by.
        Assert.Contains("§CSHARP", result.CalorSource);
    }

    // ----- review N1: hoists must land before the §ARR2D opener --------------

    /// <summary>
    /// Review N1: a multi-dimensional array whose elements are object
    /// initializers. Each element becomes a temp, and those <c>§B{~_hoistNNN}</c>
    /// lines must be emitted BEFORE the <c>§ARR2D</c> opener — the block body
    /// accepts only <c>§ROW</c>, so flushing them inside it is <c>Calor0100</c>
    /// (nine of them, for this fixture). `main` cannot parse this construct at
    /// all; here it converts fully native and the round trip compiles.
    /// </summary>
    [Fact]
    public void N1_MultiDimArray_WithObjectInitializerElements_IsNativeAndParses()
    {
        const string csharp = """
            public class Cell { public int V; }

            public class Grid
            {
                public int Build()
                {
                    Cell[,] cells = new Cell[,]
                    {
                        { new Cell { V = 1 }, new Cell { V = 2 } },
                        { new Cell { V = 3 }, new Cell { V = 4 } }
                    };
                    return cells[0, 0].V;
                }
            }
            """;

        var rt = ConvertAndParse(csharp, "N1Arr2D");

        AssertParsesClean(rt);
        Assert.DoesNotContain("§CSHARP", rt.Calor);
        var lines = rt.Calor.Split('\n');
        var opener = Array.FindIndex(lines, l => l.Contains("§ARR2D{", StringComparison.Ordinal));
        Assert.True(opener >= 0, rt.Calor);
        var closer = Array.FindIndex(lines, opener, l => l.TrimStart().StartsWith("§R", StringComparison.Ordinal));
        // Every hoisted binding sits above the opener, never between it and the rows.
        for (var i = opener + 1; i < (closer < 0 ? lines.Length : closer); i++)
        {
            if (lines[i].Trim().Length == 0) continue;
            Assert.DoesNotContain("§B{~_hoist", lines[i]);
        }
        Assert.Contains(lines.Take(opener), l => l.Contains("§B{~_hoist", StringComparison.Ordinal));
        AssertRoundTripCompiles(rt);
    }

    /// <summary>
    /// Review m12: the <c>§B{…} §LAM</c> binding-initializer path is a lambda body
    /// too, so a block lambda nested inside it must not be hoisted out of scope.
    /// A three-statement lambda bound to a local takes that path.
    /// </summary>
    [Fact]
    public void M12_BlockLambdaBindingInitializer_KeepsNestedLambdaInScope()
    {
        const string csharp = """
            using System;

            public class Runner
            {
                public Action Build(int seed)
                {
                    Action outer = () =>
                    {
                        var local = seed + 1;
                        Action inner = () =>
                        {
                            Console.WriteLine(local);
                            Console.WriteLine(local + 1);
                            Console.WriteLine(local + 2);
                        };
                        inner();
                    };
                    return outer;
                }
            }
            """;

        var rt = ConvertAndParse(csharp, "M12Nested");

        AssertParsesClean(rt);
        Assert.DoesNotContain(Bind(rt), d => d.Code == "Calor0200");
        AssertRoundTripCompiles(rt);
    }

    // ----- review M1: comments inside a multi-line call target ---------------

    /// <summary>
    /// Review M1: a fluent chain can carry a comment across the line break. The
    /// join must not bake the comment into the member path — a `//` comment would
    /// swallow the member, a `/*…*/` one would leave it inside the identifier.
    /// </summary>
    [Theory]
    [InlineData("// sort in place")]
    [InlineData("/* sort in place */")]
    public void M1_MultiLineCallTarget_WithComment_IsNotCorrupted(string comment)
    {
        var csharp = $$"""
            using System.Collections.Generic;

            public class Sorter
            {
                public void Run(List<int> xs)
                {
                    xs
                        {{comment}}
                        .Sort();
                }
            }
            """;

        var rt = ConvertAndParse(csharp, "M1Comment");

        AssertParsesClean(rt);
        Assert.DoesNotContain("sort in place.Sort", rt.Calor);
        Assert.DoesNotContain("§C{xs//", rt.Calor);
        // Either the comment was stripped and the member joined cleanly, or the
        // target was left alone for the §CS{…} fallback — never a corrupt member.
        foreach (var target in System.Text.RegularExpressions.Regex
                     .Matches(rt.Calor, @"§C\{([^}\n]*)\}")
                     .Select(m => m.Groups[1].Value))
        {
            Assert.DoesNotContain("//", target);
            Assert.DoesNotContain("/*", target);
        }
    }

    // ----- review M3: a dictionary KEY that is an object initializer ---------

    [Fact]
    public void M3_DictionaryWithObjectInitializerKey_Parses()
    {
        const string csharp = """
            using System.Collections.Generic;

            public class Key { public int A; public int B; }

            public class Table
            {
                public Dictionary<Key, string> Build()
                {
                    return new Dictionary<Key, string>
                    {
                        { new Key { A = 1, B = 2 }, "first" },
                        { new Key { A = 3, B = 4 }, "second" }
                    };
                }
            }
            """;

        var rt = ConvertAndParse(csharp, "M3DictKey");

        AssertParsesClean(rt);
    }

    // ----- review M4: one round-trip COMPILE pin per recovered cluster -------

    /// <summary>
    /// The recovered cluster-1 shape whose Calor is now emitted as a hoisted
    /// block: a returned statement lambda with a nested block.
    ///
    /// <para>Review m11 — what this substitution costs, stated rather than
    /// hidden: the corpus shape (Serilog <c>Matching.cs</c>) reached the nested
    /// block through <c>TryGetValue(…, out var …)</c>, and that is the
    /// interaction — "a hoist inside a nested block inside a block lambda" —
    /// that names cluster 1. The converter still emits <c>out var</c> as a
    /// typeless <c>§B</c> (Calor0250, a pre-existing gap outside W3), so this
    /// COMPILE pin uses an indexer read instead and exercises the nesting but
    /// not the hoist. The PARSE assertion for the genuine shape, hoist and all,
    /// is <see cref="Cluster1_ReturnedBlockLambda_WithHoistInNestedBlock_Parses"/>
    /// above, which converts the corpus form verbatim.</para>
    /// </summary>
    [Fact]
    public void M4_Cluster1_ReturnedBlockLambda_RoundTripCompiles()
    {
        const string csharp = """
            using System;
            using System.Collections.Generic;

            public static class Matching
            {
                public static Func<Dictionary<string, string>, bool> WithProperty(string propertyName)
                {
                    return e =>
                    {
                        var scalar = e[propertyName];
                        if (scalar == null) return false;
                        if (scalar.Length == 0) return false;
                        return true;
                    };
                }
            }
            """;

        AssertRoundTripCompiles(ConvertAndParse(csharp, "M4Cluster1"));
    }

    [Fact]
    public void M4_Cluster1_MultiLineFluentCallTarget_RoundTripCompiles()
    {
        const string csharp = """
            using System.Collections.Generic;
            using System.Linq;

            public class Joiner
            {
                public string[] Run(IEnumerable<string> xs)
                {
                    return xs
                        .Concat(new[] { "a", "b" })
                        .ToArray();
                }
            }
            """;

        AssertRoundTripCompiles(ConvertAndParse(csharp, "M4Fluent"));
    }

    [Fact]
    public void M4_Cluster2_EmptyInterfaceThenSibling_RoundTripCompiles()
    {
        const string csharp = """
            namespace Contracts
            {
                public interface IMarker { }

                public class Impl : IMarker { }

                public enum Mode { Fast, Slow }

                public interface ILast { }
            }
            """;

        AssertRoundTripCompiles(ConvertAndParse(csharp, "M4Cluster2"));
    }

    [Fact]
    public void M4_Cluster3_ElseIfWithHoistedCondition_RoundTripCompiles()
    {
        const string csharp = """
            public static class Renderer
            {
                public static string Flags(string format)
                {
                    var result = "";
                    for (var i = 0; i < format.Length; ++i)
                    {
                        if (format[i] == 'l')
                            result = result + "L";
                        else if (format[i] == 'j')
                            result = result + "J";
                        else
                            result = result + "-";
                    }
                    return result;
                }
            }
            """;

        AssertRoundTripCompiles(ConvertAndParse(csharp, "M4Cluster3"));
    }

    [Fact]
    public void M4_Issue1097_UninferredLambdaParameter_RoundTripCompiles()
    {
        const string csharp = """
            using System;
            using System.Collections.Generic;

            public class Runner
            {
                public int Run(List<int> xs)
                {
                    Func<int, int> doubler = x => x * 2;
                    var total = 0;
                    foreach (var x in xs) total = total + doubler(x);
                    return total;
                }
            }
            """;

        var rt = ConvertAndParse(csharp, "M4Issue1097");
        Assert.Empty(UnknownTypedLambdaHeaders(rt.Calor));
        AssertRoundTripCompiles(rt);
    }
}
