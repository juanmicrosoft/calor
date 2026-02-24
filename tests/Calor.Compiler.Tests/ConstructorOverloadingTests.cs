using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests verifying that multiple constructors per class (constructor overloading) work
/// end-to-end: parsing, code generation, conversion, and round-trip.
///
/// Closes Challenge 8 from the conversion campaign: "Constructor overloading not supported."
/// This was a documentation gap — the compiler has always supported multiple §CTOR blocks.
/// </summary>
public class ConstructorOverloadingTests
{
    private readonly CSharpToCalorConverter _converter = new();

    #region Calor → C# (Parser + CodeGen)

    [Fact]
    public void Compile_TwoConstructors_EmitsBoth()
    {
        var source = """
            §M{m001:MultiCtor}
            §CL{c001:Person:pub}
              §FLD{fld1:name:str:priv}
              §FLD{fld2:age:i32:priv}

              §CTOR{ctor001:pub}
                §I{str:name}
                §I{i32:age}
                §ASSIGN name name
                §ASSIGN age age
              §/CTOR{ctor001}

              §CTOR{ctor002:pub}
                §I{str:name}
                §ASSIGN name name
                §ASSIGN age 0
              §/CTOR{ctor002}
            §/CL{c001}
            §/M{m001}
            """;

        var result = Program.Compile(source, "test.calr", new CompilationOptions
        {
            EnforceEffects = false
        });

        Assert.False(result.HasErrors, FormatDiagnostics(result));
        var code = result.GeneratedCode;

        // Both constructors should appear
        Assert.Contains("public Person(string name, int age)", code);
        Assert.Contains("public Person(string name)", code);
    }

    [Fact]
    public void Compile_ThreeConstructors_EmitsAll()
    {
        var source = """
            §M{m001:TripleCtor}
            §CL{c001:Config:pub}
              §FLD{fld1:host:str:priv}
              §FLD{fld2:port:i32:priv}

              §CTOR{ctor001:pub}
                §I{str:host}
                §I{i32:port}
                §ASSIGN host host
                §ASSIGN port port
              §/CTOR{ctor001}

              §CTOR{ctor002:pub}
                §I{str:host}
                §ASSIGN host host
                §ASSIGN port 8080
              §/CTOR{ctor002}

              §CTOR{ctor003:pub}
                §ASSIGN host "localhost"
                §ASSIGN port 8080
              §/CTOR{ctor003}
            §/CL{c001}
            §/M{m001}
            """;

        var result = Program.Compile(source, "test.calr", new CompilationOptions
        {
            EnforceEffects = false
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
            §M{m001:BaseCall}
            §CL{c001:AppException:pub}
              §EXT{Exception}

              §CTOR{ctor001:pub}
                §I{str:message}
                §I{i32:code}
                §BASE
                  §A message
                §/BASE
              §/CTOR{ctor001}

              §CTOR{ctor002:pub}
                §I{str:message}
                §BASE
                  §A message
                §/BASE
              §/CTOR{ctor002}
            §/CL{c001}
            §/M{m001}
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
            §M{m001:ThisChain}
            §CL{c001:Point:pub}
              §FLD{fld1:x:i32:priv}
              §FLD{fld2:y:i32:priv}

              §CTOR{ctor001:pub}
                §I{i32:x}
                §I{i32:y}
                §ASSIGN x x
                §ASSIGN y y
              §/CTOR{ctor001}

              §CTOR{ctor002:pub}
                §I{i32:val}
                §THIS
                  §A val
                  §A val
                §/THIS
              §/CTOR{ctor002}
            §/CL{c001}
            §/M{m001}
            """;

        var result = Program.Compile(source, "test.calr", new CompilationOptions
        {
            EnforceEffects = false
        });

        Assert.False(result.HasErrors, FormatDiagnostics(result));
        var code = result.GeneratedCode;

        Assert.Contains("Point(int x, int y)", code);
        Assert.Contains("Point(int val) : this(val, val)", code);
    }

    [Fact]
    public void Compile_ConstructorWithPrecondition_EmitsContractCheck()
    {
        var source = """
            §M{m001:PrecondCtor}
            §CL{c001:PositiveValue:pub}
              §FLD{fld1:value:i32:priv}

              §CTOR{ctor001:pub}
                §I{i32:value}
                §Q (> value 0)
                §ASSIGN value value
              §/CTOR{ctor001}

              §CTOR{ctor002:pub}
                §ASSIGN value 1
              §/CTOR{ctor002}
            §/CL{c001}
            §/M{m001}
            """;

        var result = Program.Compile(source, "test.calr", new CompilationOptions
        {
            EnforceEffects = false,
            ContractMode = ContractMode.Debug
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
            §M{m001:MixedVis}
            §CL{c001:Singleton:pub}
              §CTOR{ctor001:priv}
              §/CTOR{ctor001}

              §CTOR{ctor002:int}
                §I{i32:seed}
              §/CTOR{ctor002}
            §/CL{c001}
            §/M{m001}
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

        // Should have two §CTOR blocks
        var ctorCount = calrText.Split("§CTOR{").Length - 1;
        Assert.Equal(2, ctorCount);

        // Should have two §/CTOR blocks
        var endCtorCount = calrText.Split("§/CTOR{").Length - 1;
        Assert.Equal(2, endCtorCount);
    }

    #endregion

    #region Parser Edge Cases

    [Fact]
    public void Parse_ConstructorWithUniqueIds_NoIdConflict()
    {
        var source = """
            §M{m001:IdTest}
            §CL{c001:Widget:pub}
              §CTOR{ctor001:pub}
                §I{str:name}
              §/CTOR{ctor001}

              §CTOR{ctor002:pub}
                §I{str:name}
                §I{i32:size}
              §/CTOR{ctor002}

              §CTOR{ctor003:priv}
              §/CTOR{ctor003}
            §/CL{c001}
            §/M{m001}
            """;

        var result = Program.Compile(source, "test.calr", new CompilationOptions
        {
            EnforceEffects = false
        });

        Assert.False(result.HasErrors, FormatDiagnostics(result));
        Assert.NotNull(result.Ast);
        var classNode = Assert.Single(result.Ast.Classes);
        Assert.Equal(3, classNode.Constructors.Count);
    }

    #endregion

    #region Helpers

    private static string FormatDiagnostics(CompilationResult result)
    {
        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
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
