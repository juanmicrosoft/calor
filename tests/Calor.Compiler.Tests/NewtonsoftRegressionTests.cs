using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Migration;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Regression tests for language gaps found during Newtonsoft.Json conversion.
/// Covers: verbatim identifiers, lock/§SYNC, conditional usings in §PP.
/// </summary>
public class NewtonsoftRegressionTests
{
    private readonly CSharpToCalorConverter _converter = new(new ConversionOptions { StripPreprocessor = false });

    private ConversionResult Convert(string csharpSource)
    {
        var result = _converter.Convert(csharpSource);
        Assert.True(result.Success,
            string.Join("\n", result.Issues.Select(i => $"[{i.Severity}] {i.Message}")));
        Assert.NotNull(result.Ast);
        return result;
    }

    private string RoundTrip(string csharpSource)
    {
        var result = Convert(csharpSource);

        var calrEmitter = new CalorEmitter();
        var calrText = calrEmitter.Emit(result.Ast!);

        var compileResult = Program.Compile(calrText, "newtonsoft-regression.calr", new CompilationOptions
        {
            EnforceEffects = false
        });

        Assert.False(compileResult.HasErrors,
            $"Round-trip compilation failed:\n" +
            string.Join("\n", compileResult.Diagnostics.Select(d => d.Message)));

        return compileResult.GeneratedCode;
    }

    private string ConvertToCalor(string csharpSource)
    {
        var result = Convert(csharpSource);
        var calrEmitter = new CalorEmitter();
        return calrEmitter.Emit(result.Ast!);
    }

    // ── Phase 2: Verbatim Identifiers ──

    [Fact]
    public void VerbatimIdentifier_ParameterName()
    {
        var csharp = @"
namespace Test
{
    public class Foo
    {
        public void Bar(string @operator)
        {
            System.Console.WriteLine(@operator);
        }
    }
}";
        var calor = ConvertToCalor(csharp);
        Assert.Contains("`operator`", calor);

        // Round-trip should produce valid C#
        var generated = RoundTrip(csharp);
        Assert.Contains("@operator", generated);
    }

    [Fact]
    public void VerbatimIdentifier_VariableName()
    {
        var csharp = @"
namespace Test
{
    public class Foo
    {
        public void Bar()
        {
            var @class = ""test"";
            System.Console.WriteLine(@class);
        }
    }
}";
        var calor = ConvertToCalor(csharp);
        Assert.Contains("`class`", calor);

        var generated = RoundTrip(csharp);
        Assert.Contains("@class", generated);
    }

    [Fact]
    public void VerbatimIdentifier_RegularName_NoBackticks()
    {
        var csharp = @"
namespace Test
{
    public class Foo
    {
        public void Bar(string name)
        {
            System.Console.WriteLine(name);
        }
    }
}";
        var calor = ConvertToCalor(csharp);
        // Regular identifiers should NOT have backticks
        Assert.DoesNotContain("`name`", calor);
        Assert.Contains("name", calor);
    }

    [Fact]
    public void VerbatimIdentifier_FieldName()
    {
        var csharp = @"
namespace Test
{
    public class Foo
    {
        private string @event = ""click"";
        public string GetEvent() { return @event; }
    }
}";
        var calor = ConvertToCalor(csharp);
        Assert.Contains("`event`", calor);

        var generated = RoundTrip(csharp);
        Assert.Contains("@event", generated);
    }

    [Fact]
    public void VerbatimIdentifier_PropertyName()
    {
        var csharp = @"
namespace Test
{
    public class Foo
    {
        public string @class { get; set; }
    }
}";
        var calor = ConvertToCalor(csharp);
        Assert.Contains("`class`", calor);

        var generated = RoundTrip(csharp);
        Assert.Contains("@class", generated);
    }

    [Fact]
    public void VerbatimIdentifier_ReferenceInExpression()
    {
        var csharp = @"
namespace Test
{
    public class Foo
    {
        public string Bar(string @operator)
        {
            return @operator;
        }
    }
}";
        var calor = ConvertToCalor(csharp);
        // Both the parameter declaration and the reference in the return should use backticks
        var backtickCount = calor.Split("`operator`").Length - 1;
        Assert.True(backtickCount >= 2, $"Expected at least 2 backtick-wrapped 'operator' (param + reference), found {backtickCount}");

        var generated = RoundTrip(csharp);
        Assert.Contains("@operator", generated);
    }

    // ── Phase 3: Lock / §SYNC ──

    [Fact]
    public void Lock_SimpleRoundTrip()
    {
        var csharp = @"
namespace Test
{
    public class Foo
    {
        private readonly object _obj = new object();
        public void Bar()
        {
            lock (_obj)
            {
                var x = 1;
            }
        }
    }
}";
        var calor = ConvertToCalor(csharp);
        Assert.Contains("§SYNC", calor);
        Assert.Contains("§/SYNC", calor);

        var generated = RoundTrip(csharp);
        Assert.Contains("lock", generated);
    }

    [Fact]
    public void Lock_NestedLocks()
    {
        var csharp = @"
namespace Test
{
    public class Foo
    {
        private readonly object _a = new object();
        private readonly object _b = new object();
        public void Bar()
        {
            lock (_a)
            {
                lock (_b)
                {
                    var x = 1;
                }
            }
        }
    }
}";
        var calor = ConvertToCalor(csharp);
        // Should have two §SYNC blocks
        var syncCount = calor.Split("§SYNC{").Length - 1;
        Assert.True(syncCount >= 2, $"Expected at least 2 §SYNC blocks, found {syncCount}");

        var generated = RoundTrip(csharp);
        Assert.Contains("lock", generated);
    }

    [Fact]
    public void Lock_ExpressionTarget()
    {
        var csharp = @"
namespace Test
{
    public class Foo
    {
        private readonly object _syncRoot = new object();
        public void Bar()
        {
            lock (this._syncRoot)
            {
                var x = 42;
            }
        }
    }
}";
        var calor = ConvertToCalor(csharp);
        Assert.Contains("§SYNC", calor);

        var generated = RoundTrip(csharp);
        Assert.Contains("lock", generated);
    }

    [Fact]
    public void Lock_WithMethodCalls()
    {
        var csharp = @"
namespace Test
{
    public class Foo
    {
        private readonly object _lock = new object();
        private int _count;
        public int GetAndIncrement()
        {
            lock (_lock)
            {
                _count = _count + 1;
                return _count;
            }
        }
    }
}";
        var calor = ConvertToCalor(csharp);
        Assert.Contains("§SYNC", calor);

        var generated = RoundTrip(csharp);
        Assert.Contains("lock", generated);
        Assert.Contains("return", generated);
    }

    [Fact]
    public void Lock_CalorParseSyncBlock()
    {
        // Parse Calor §SYNC directly, verify AST
        var calorSource = @"
§M{m1:Test}
  §CL{c1:Foo:pub}
    §FLD{object:_obj:priv}
    §MT{mt1:Bar:pub}
      §SYNC{s1} (_obj)
        §B{~x:i32} INT:1
      §/SYNC{s1}
    §/MT{mt1}
  §/CL{c1}
§/M{m1}
";
        var compileResult = Program.Compile(calorSource, "sync-test.calr", new CompilationOptions
        {
            EnforceEffects = false
        });

        Assert.False(compileResult.HasErrors,
            $"Compilation failed:\n" +
            string.Join("\n", compileResult.Diagnostics.Select(d => d.Message)));

        Assert.Contains("lock", compileResult.GeneratedCode);
        Assert.Contains("_obj", compileResult.GeneratedCode);
    }

    // ── Phase 4: Conditional Usings in §PP ──

    [Fact]
    public void ConditionalUsing_CSharpRoundTrip()
    {
        var csharp = @"
#if NET6_0_OR_GREATER
using System.Text.Json;
#endif

namespace Test
{
    public class Foo
    {
        public void Bar()
        {
            var x = 1;
        }
    }
}";
        var calor = ConvertToCalor(csharp);
        // The conditional using should appear inside a §PP block
        Assert.Contains("§PP", calor);

        var generated = RoundTrip(csharp);
        // The #if and using should be in the output
        Assert.Contains("#if", generated);
        Assert.Contains("System.Text.Json", generated);
    }

    [Fact]
    public void ConditionalUsing_ParseCalor()
    {
        // Parse Calor with §U inside §PP directly
        var calorSource = @"
§M{m1:Test}
  §PP{NET6_0_OR_GREATER}
  §U{System.Text.Json}
  §/PP{NET6_0_OR_GREATER}
  §CL{c1:Foo:pub}
    §MT{mt1:Bar:pub}
      §B{~x:i32} INT:1
    §/MT{mt1}
  §/CL{c1}
§/M{m1}
";
        var compileResult = Program.Compile(calorSource, "conditional-using.calr", new CompilationOptions
        {
            EnforceEffects = false
        });

        Assert.False(compileResult.HasErrors,
            $"Compilation failed:\n" +
            string.Join("\n", compileResult.Diagnostics.Select(d => d.Message)));

        Assert.Contains("#if NET6_0_OR_GREATER", compileResult.GeneratedCode);
        Assert.Contains("using System.Text.Json;", compileResult.GeneratedCode);
        Assert.Contains("#endif", compileResult.GeneratedCode);
    }

    [Fact]
    public void ConditionalUsing_MixedWithTypes()
    {
        // Parse Calor with both §U and type declarations inside §PP
        var calorSource = @"
§M{m1:Test}
  §PP{NET6_0_OR_GREATER}
  §U{System.Text.Json}
  §CL{c1:JsonHelper:pub}
    §MT{mt1:GetValue:pub}
      §O{i32}
      §R INT:42
    §/MT{mt1}
  §/CL{c1}
  §/PP{NET6_0_OR_GREATER}
§/M{m1}
";
        var compileResult = Program.Compile(calorSource, "mixed-pp.calr", new CompilationOptions
        {
            EnforceEffects = false
        });

        Assert.False(compileResult.HasErrors,
            $"Compilation failed:\n" +
            string.Join("\n", compileResult.Diagnostics.Select(d => d.Message)));

        var code = compileResult.GeneratedCode;
        Assert.Contains("#if NET6_0_OR_GREATER", code);
        Assert.Contains("using System.Text.Json;", code);
        Assert.Contains("JsonHelper", code);
        Assert.Contains("#endif", code);
    }
}
