using System.Reflection;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests verifying that multiple constructors per class (constructor overloading) work
/// end-to-end: parsing, code generation, conversion, and round-trip.
///
/// Closes Challenge 8 from the conversion campaign: "Constructor overloading not supported."
/// Native classes retain multiple constructor declarations.
/// </summary>
public class ConstructorOverloadingTests
{
    private readonly CSharpToCalorConverter _converter = new();

    #region Calor → C# (Parser + CodeGen)

    [Fact]
    public void Compile_TwoConstructors_EmitsBoth()
    {
        var source = """
            module MultiCtor
              class Person public
                field string:name private
                field int:age private

                ctor (string:name, int:age)
                  set this.name name
                  set this.age age

                ctor (string:name)
                  set this.name name
                  set this.age 0
            """;

        var result = Program.Compile(source, "test.calr", new CompilationOptions
        {
            EnforceEffects = false,
        });

        Assert.False(result.HasErrors, FormatDiagnostics(result));
        var code = result.GeneratedCode;

        // Both constructors should appear
        Assert.Contains("public Person(string name, int age)", code);
        Assert.Contains("public Person(string name)", code);
        var type = Emit(code).GetTypes().Single(type => type.Name == "Person");
        var full = Activator.CreateInstance(type, "Ada", 37)!;
        var shorter = Activator.CreateInstance(type, "Grace")!;
        Assert.Equal("Ada", type.GetField("name", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(full));
        Assert.Equal(37, type.GetField("age", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(full));
        Assert.Equal("Grace", type.GetField("name", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shorter));
        Assert.Equal(0, type.GetField("age", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shorter));
    }

    [Fact]
    public void Compile_ThreeConstructors_EmitsAll()
    {
        var source = """
            module TripleCtor
              class Config public
                field string:host private
                field int:port private

                ctor (string:host, int:port)
                  set this.host host
                  set this.port port

                ctor (string:host)
                  set this.host host
                  set this.port 8080

                ctor ()
                  set this.host "localhost"
                  set this.port 8080
            """;

        var result = Program.Compile(source, "test.calr", new CompilationOptions
        {
            EnforceEffects = false,
        });

        Assert.False(result.HasErrors, FormatDiagnostics(result));
        var code = result.GeneratedCode;

        Assert.Contains("public Config(string host, int port)", code);
        Assert.Contains("public Config(string host)", code);
        Assert.Contains("public Config()", code);
    }

    [Fact]
    public void Compile_ConstructorWithBaseCall_EmitsBaseInitializer()
    {
        var source = """
            module BaseCall
              class AppException public
                extends Exception

                ctor (string:message, int:code)
                  base(message)

                ctor (string:message)
                  base(message)
            """;

        var result = Program.Compile(source, "test.calr", new CompilationOptions
        {
            EnforceEffects = false
        });

        Assert.False(result.HasErrors, FormatDiagnostics(result));
        var code = result.GeneratedCode;

        // Both constructors with base calls
        Assert.Contains("AppException(string message, int code) : base(message)", code);
        Assert.Contains("AppException(string message) : base(message)", code);
    }

    [Fact]
    public void Compile_ConstructorWithThisChaining_EmitsThisInitializer()
    {
        var source = """
            module ThisChain
              class Point public
                field int:x private
                field int:y private

                ctor (int:x, int:y)
                  set this.x x
                  set this.y y

                ctor (int:val)
                  this(val, val)
            """;

        var result = Program.Compile(source, "test.calr", new CompilationOptions
        {
            EnforceEffects = false,
        });

        Assert.False(result.HasErrors, FormatDiagnostics(result));
        var code = result.GeneratedCode;

        Assert.Contains("Point(int x, int y)", code);
        Assert.Contains("Point(int val) : this(val, val)", code);
        var type = Emit(code).GetTypes().Single(type => type.Name == "Point");
        var point = Activator.CreateInstance(type, 12)!;
        Assert.Equal(12, type.GetField("x", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(point));
        Assert.Equal(12, type.GetField("y", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(point));
    }

    [Fact]
    public void Compile_ThisAndBaseMembers_AreNotConstructorInitializers()
    {
        var source = """
            module ConstructorMembers
              class Base public
                field int:Value public
                func Bump () → void
                  set this.Value this.Value + 1
              class Derived public
                extends Base
                ctor (int:value)
                  base()
                  set this.Value value
                  set base.Value base.Value + 1
                  this.Bump()
                  base.Bump()
            """;
        var result = Program.Compile(source, "constructors.calr.txt", new CompilationOptions
        {
            EnforceEffects = false
        });
        Assert.False(result.HasErrors, FormatDiagnostics(result));
        var constructor = Assert.Single(result.Ast!.Classes.Single(type => type.Name == "Derived").Constructors);
        Assert.True(constructor.Initializer!.IsBaseCall);
        Assert.Equal(4, constructor.Body.Count);
        var type = Emit(result.GeneratedCode).GetTypes().Single(type => type.Name == "Derived");
        var value = Activator.CreateInstance(type, 9)!;
        Assert.Equal(12, type.GetField("Value")!.GetValue(value));
    }

    [Fact]
    public void Compile_ConstructorWithPrecondition_EmitsContractCheck()
    {
        var source = """
            module PrecondCtor
              class PositiveValue public
                field int:value private

                ctor (int:value)
                  requires value > 0
                  set this.value value

                ctor ()
                  set this.value 1
            """;

        var result = Program.Compile(source, "test.calr", new CompilationOptions
        {
            EnforceEffects = false,
            ContractMode = ContractMode.Debug,
        });

        Assert.False(result.HasErrors, FormatDiagnostics(result));
        var code = result.GeneratedCode;

        // First constructor should have precondition
        Assert.Contains("Precondition failed", code);
        // Second constructor should work without precondition
        Assert.Contains("public PositiveValue()", code);
    }

    [Fact]
    public void Compile_MixedVisibilityConstructors_EmitsCorrectAccess()
    {
        var source = """
            module MixedVis
              class Singleton public
                ctor private ()

                ctor internal (int:seed)
            """;

        var result = Program.Compile(source, "test.calr", new CompilationOptions
        {
            EnforceEffects = false
        });

        Assert.False(result.HasErrors, FormatDiagnostics(result));
        var code = result.GeneratedCode;

        Assert.Contains("private Singleton()", code);
        Assert.Contains("internal Singleton(int seed)", code);
    }

    #endregion

    #region C# → Calor (Converter)

    [Fact]
    public void Convert_TwoConstructors_ProducesTwoCtorNodes()
    {
        var csharpSource = """
            public class Person
            {
                public string Name { get; }
                public int Age { get; }

                public Person(string name, int age)
                {
                    Name = name;
                    Age = age;
                }

                public Person(string name) : this(name, 0) { }
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.Ast);

        var classNode = Assert.Single(result.Ast.Classes);
        Assert.Equal(2, classNode.Constructors.Count);
        Assert.Equal(2, classNode.Constructors[0].Parameters.Count); // (name, age)
        Assert.Single(classNode.Constructors[1].Parameters); // (name)
    }

    [Fact]
    public void Convert_ConstructorWithBaseCall_PreservesInitializer()
    {
        var csharpSource = """
            using System;
            public class AppException : Exception
            {
                public int Code { get; }

                public AppException(string message, int code) : base(message)
                {
                    Code = code;
                }

                public AppException(string message) : base(message)
                {
                    Code = 0;
                }
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));

        var classNode = Assert.Single(result.Ast!.Classes);
        Assert.Equal(2, classNode.Constructors.Count);

        // Both should have base initializers
        Assert.NotNull(classNode.Constructors[0].Initializer);
        Assert.True(classNode.Constructors[0].Initializer!.IsBaseCall);
        Assert.NotNull(classNode.Constructors[1].Initializer);
        Assert.True(classNode.Constructors[1].Initializer!.IsBaseCall);
    }

    [Fact]
    public void Convert_ContractViolationException_HandlesBothConstructors()
    {
        // This is the exact scenario from Challenge 8
        var csharpSource = """
            using System;
            public class ContractViolationException : Exception
            {
                public string FunctionId { get; }
                public string Kind { get; }

                // Debug constructor (many params)
                public ContractViolationException(
                    string message,
                    string functionId,
                    string kind,
                    int startOffset,
                    int length,
                    string? sourceFile = null,
                    int line = 0,
                    int column = 0,
                    string? condition = null)
                    : base(message)
                {
                    FunctionId = functionId;
                    Kind = kind;
                }

                // Release constructor (2 params)
                public ContractViolationException(string functionId, string kind)
                    : base("Contract violation in " + functionId)
                {
                    FunctionId = functionId;
                    Kind = kind;
                }
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));

        var classNode = Assert.Single(result.Ast!.Classes);
        Assert.Equal(2, classNode.Constructors.Count);

        // Debug constructor: 9 params
        Assert.Equal(9, classNode.Constructors[0].Parameters.Count);
        // Release constructor: 2 params
        Assert.Equal(2, classNode.Constructors[1].Parameters.Count);

        // Both should have base initializers
        Assert.All(classNode.Constructors, ctor =>
        {
            Assert.NotNull(ctor.Initializer);
            Assert.True(ctor.Initializer!.IsBaseCall);
        });
    }

    #endregion

    #region Round-Trip (C# → Calor → C#)

    [Fact]
    public void RoundTrip_TwoConstructors_PreservedInGeneration()
    {
        var csharpSource = """
            public class Config
            {
                public string Host { get; set; }
                public int Port { get; set; }

                public Config(string host, int port)
                {
                    Host = host;
                    Port = port;
                }

                public Config(string host)
                {
                    Host = host;
                    Port = 8080;
                }

                public Config()
                {
                    Host = "localhost";
                    Port = 8080;
                }
            }
            """;

        // C# → Calor
        var convertResult = _converter.Convert(csharpSource);
        Assert.True(convertResult.Success, GetErrorMessage(convertResult));

        var classNode = Assert.Single(convertResult.Ast!.Classes);
        Assert.Equal(3, classNode.Constructors.Count);

        // Calor → C# (regenerate)
        var emitter = new CSharpEmitter();
        var regenerated = emitter.Emit(convertResult.Ast);

        // All three constructors should appear in regenerated C#
        Assert.Contains("Config(string host, int port)", regenerated);
        Assert.Contains("Config(string host)", regenerated);
        Assert.Contains("Config()", regenerated);
    }

    [Fact]
    public void RoundTrip_ContractViolationException_FullCycle()
    {
        var csharpSource = """
            using System;
            public class ContractViolation : Exception
            {
                public string FuncId { get; }

                public ContractViolation(string message, string funcId) : base(message)
                {
                    FuncId = funcId;
                }

                public ContractViolation(string funcId) : base("Violation")
                {
                    FuncId = funcId;
                }
            }
            """;

        // C# → Calor
        var convertResult = _converter.Convert(csharpSource);
        Assert.True(convertResult.Success, GetErrorMessage(convertResult));

        // Calor → C#
        var emitter = new CSharpEmitter();
        var regenerated = emitter.Emit(convertResult.Ast!);

        // Both constructors preserved
        Assert.Contains("ContractViolation(string message, string funcId) : base(message)", regenerated);
        Assert.Contains("ContractViolation(string funcId) : base(", regenerated);
    }

    [Fact]
    public void RoundTrip_ConstructorChaining_PreservesThisCall()
    {
        var csharpSource = """
            public class Range
            {
                public int Min { get; }
                public int Max { get; }

                public Range(int min, int max)
                {
                    Min = min;
                    Max = max;
                }

                public Range(int max) : this(0, max) { }
                public Range() : this(0, 100) { }
            }
            """;

        var convertResult = _converter.Convert(csharpSource);
        Assert.True(convertResult.Success, GetErrorMessage(convertResult));

        var classNode = Assert.Single(convertResult.Ast!.Classes);
        Assert.Equal(3, classNode.Constructors.Count);

        // First constructor: no initializer (or base)
        // Second and third: this() initializer
        var chainedCtors = classNode.Constructors.Where(c => c.Initializer != null && !c.Initializer.IsBaseCall).ToList();
        Assert.Equal(2, chainedCtors.Count);

        // Regenerate
        var emitter = new CSharpEmitter();
        var regenerated = emitter.Emit(convertResult.Ast!);

        Assert.Contains("Range(int min, int max)", regenerated);
        Assert.Contains(": this(0, max)", regenerated);
        Assert.Contains(": this(0, 100)", regenerated);
    }

    #endregion

    #region Calor Emit (Calor → Calor text)

    [Fact]
    public void CalorEmit_MultipleConstructors_EmitsAllCtorBlocks()
    {
        var csharpSource = """
            public class Logger
            {
                public string Prefix { get; }
                public bool Verbose { get; }

                public Logger(string prefix, bool verbose)
                {
                    Prefix = prefix;
                    Verbose = verbose;
                }

                public Logger(string prefix)
                {
                    Prefix = prefix;
                    Verbose = false;
                }
            }
            """;

        var convertResult = _converter.Convert(csharpSource);
        Assert.True(convertResult.Success, GetErrorMessage(convertResult));

        var calrEmitter = new CalorEmitter();
        var calrText = calrEmitter.Emit(convertResult.Ast!);

        var ctorCount = calrText.Split("ctor (").Length - 1;
        Assert.Equal(2, ctorCount);

        Assert.DoesNotContain("§", calrText);
        var reparsed = Program.Compile(calrText, "constructors.calr.txt", new CompilationOptions
        {
            EnforceEffects = false
        });
        Assert.False(reparsed.HasErrors, FormatDiagnostics(reparsed));
        Assert.Equal(2, Assert.Single(reparsed.Ast!.Classes).Constructors.Count);
    }

    #endregion

    #region Parser Edge Cases

    [Fact]
    public void Parse_ConstructorWithUniqueIds_NoIdConflict()
    {
        var source = """
            module IdTest
              class Widget public
                ctor (string:name)

                ctor (string:name, int:size)

                ctor private ()
            """;

        var result = Program.Compile(source, "test.calr", new CompilationOptions
        {
            EnforceEffects = false
        });

        Assert.False(result.HasErrors, FormatDiagnostics(result));
        Assert.NotNull(result.Ast);
        var classNode = Assert.Single(result.Ast.Classes);
        Assert.Equal(3, classNode.Constructors.Count);
        Assert.All(classNode.Constructors, ctor => Assert.False(string.IsNullOrEmpty(ctor.Id)));
        Assert.Equal(3, classNode.Constructors.Select(ctor => ctor.Id).Distinct().Count());
    }

    #endregion

    #region Helpers

    private static Assembly Emit(string source)
    {
        var compilation = CSharpCompilation.Create("Constructors_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble + source)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        return Assembly.Load(stream.ToArray());
    }

    private static string FormatDiagnostics(CompilationResult result)
    {
        var errors = result.Diagnostics
            .Where(d => d.Severity == Calor.Compiler.Diagnostics.DiagnosticSeverity.Error)
            .Select(d => $"[{d.Code}] {d.Message}");
        return string.Join("\n", errors);
    }

    private static string GetErrorMessage(ConversionResult result)
    {
        if (result.Issues.Count > 0)
            return string.Join("\n", result.Issues.Select(i => i.Message));
        return "Conversion failed with no specific error message";
    }

    #endregion
}
