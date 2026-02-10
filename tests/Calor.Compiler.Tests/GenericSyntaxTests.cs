using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public class GenericSyntaxTests
{
    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    private static string CompileToCS(string calorSource)
    {
        var result = Program.Compile(calorSource);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        return result.GeneratedCode;
    }

    [Fact]
    public void Parse_GenericFunction_SingleTypeParam()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Identity:pub}<T>
              §I{T:value}
              §O{T}
              §R value
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("; ", diagnostics.Errors));
        Assert.Single(module.Functions);

        var func = module.Functions[0];
        Assert.Equal("Identity", func.Name);
        Assert.Single(func.TypeParameters);
        Assert.Equal("T", func.TypeParameters[0].Name);
    }

    [Fact]
    public void Parse_GenericFunction_MultipleTypeParams()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Map:pub}<T, U>
              §I{T:input}
              §I{Func<T, U>:mapper}
              §O{U}
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("; ", diagnostics.Errors));
        Assert.Single(module.Functions);

        var func = module.Functions[0];
        Assert.Equal("Map", func.Name);
        Assert.Equal(2, func.TypeParameters.Count);
        Assert.Equal("T", func.TypeParameters[0].Name);
        Assert.Equal("U", func.TypeParameters[1].Name);
    }

    [Fact]
    public void Parse_GenericClass_WithConstraints()
    {
        var source = """
            §M{m001:Test}
            §CL{c001:Repository:pub}<T>
              §WHERE T : class
              §FLD{T:_item:pri}
            §/CL{c001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("; ", diagnostics.Errors));
        Assert.Single(module.Classes);

        var cls = module.Classes[0];
        Assert.Equal("Repository", cls.Name);
        Assert.Single(cls.TypeParameters);
        Assert.Equal("T", cls.TypeParameters[0].Name);
        Assert.Single(cls.TypeParameters[0].Constraints);
        Assert.Equal(TypeConstraintKind.Class, cls.TypeParameters[0].Constraints[0].Kind);
    }

    [Fact]
    public void Parse_GenericTypeRef_Simple()
    {
        var source = """
            §M{m001:Test}
            §F{f001:GetItems:pub}
              §I{List<i32>:items}
              §O{void}
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("; ", diagnostics.Errors));
        var func = module.Functions[0];
        Assert.Single(func.Parameters);
        Assert.Equal("List<INT>", func.Parameters[0].TypeName);
    }

    [Fact]
    public void Parse_GenericTypeRef_Nested()
    {
        var source = """
            §M{m001:Test}
            §F{f001:GetLookup:pub}<T>
              §I{Dictionary<str, List<T>>:lookup}
              §O{void}
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("; ", diagnostics.Errors));
        var func = module.Functions[0];
        Assert.Single(func.Parameters);
        Assert.Equal("Dictionary<STRING, List<T>>", func.Parameters[0].TypeName);
    }

    [Fact]
    public void Parse_Where_MultipleConstraints()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Sort:pub}<T>
              §WHERE T : class, IComparable<T>
              §I{List<T>:items}
              §O{void}
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("; ", diagnostics.Errors));
        var func = module.Functions[0];
        Assert.Single(func.TypeParameters);
        Assert.Equal(2, func.TypeParameters[0].Constraints.Count);
        Assert.Equal(TypeConstraintKind.Class, func.TypeParameters[0].Constraints[0].Kind);
        Assert.Equal(TypeConstraintKind.TypeName, func.TypeParameters[0].Constraints[1].Kind);
        Assert.Equal("IComparable<T>", func.TypeParameters[0].Constraints[1].TypeName);
    }

    [Fact]
    public void Parse_GenericInterface()
    {
        var source = """
            §M{m001:Test}
            §IFACE{i001:IRepository:pub}<T>
              §WHERE T : class
              §MT{m001:Get}
                §I{i32:id}
                §O{T}
              §/MT{m001}
            §/IFACE{i001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("; ", diagnostics.Errors));
        Assert.Single(module.Interfaces);

        var iface = module.Interfaces[0];
        Assert.Equal("IRepository", iface.Name);
        Assert.Single(iface.TypeParameters);
        Assert.Equal("T", iface.TypeParameters[0].Name);
        Assert.Single(iface.TypeParameters[0].Constraints);
    }

    [Fact]
    public void Parse_OldSyntax_StillSupported()
    {
        // Old §WR{T:class} syntax should still work during transition
        var source = """
            §M{m001:Test}
            §F{f001:Identity:pub}<T>
              §WR{T:class}
              §I{T:value}
              §O{T}
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("; ", diagnostics.Errors));
        var func = module.Functions[0];
        Assert.Single(func.TypeParameters);
        Assert.Single(func.TypeParameters[0].Constraints);
        Assert.Equal(TypeConstraintKind.Class, func.TypeParameters[0].Constraints[0].Kind);
    }

    #region E2E Compilation Tests

    [Fact]
    public void E2E_GenericFunction_CompilesAndRuns()
    {
        var calor = """
            §M{m001:Test}
            §F{f001:Identity:pub}<T>
              §I{T:value}
              §O{T}
              §R value
            §/F{f001}
            §/M{m001}
            """;

        var csharp = CompileToCS(calor);

        Assert.Contains("public static T Identity<T>(T value)", csharp);
        Assert.Contains("return value;", csharp);
    }

    [Fact]
    public void E2E_GenericClass_WithConstraint_CompilesCorrectly()
    {
        var calor = """
            §M{m001:Test}
            §CL{c001:Repository:pub}<T>
              §WHERE T : class
              §FLD{List<T>:_items:pri}
            §/CL{c001}
            §/M{m001}
            """;

        var csharp = CompileToCS(calor);

        Assert.Contains("public class Repository<T>", csharp);
        Assert.Contains("where T : class", csharp);
        Assert.Contains("List<T> _items", csharp);
    }

    [Fact]
    public void E2E_GenericInterface_EmitsCorrectly()
    {
        var calor = """
            §M{m001:Test}
            §IFACE{i001:IRepository:pub}<T>
              §WHERE T : class
              §MT{m001:GetById}
                §I{i32:id}
                §O{T}
              §/MT{m001}
              §MT{m002:Save}
                §I{T:entity}
                §O{void}
              §/MT{m002}
            §/IFACE{i001}
            §/M{m001}
            """;

        var csharp = CompileToCS(calor);

        Assert.Contains("public interface IRepository<T>", csharp);
        Assert.Contains("where T : class", csharp);
        Assert.Contains("T GetById(int id)", csharp);
        Assert.Contains("void Save(T entity)", csharp);
    }

    [Fact]
    public void E2E_NestedGenericTypes_HandleCorrectly()
    {
        var calor = """
            §M{m001:Test}
            §F{f001:GetLookup:pub}<T>
              §I{Dictionary<str, List<T>>:lookup}
              §O{Dictionary<str, List<T>>}
              §R lookup
            §/F{f001}
            §/M{m001}
            """;

        var csharp = CompileToCS(calor);

        Assert.Contains("Dictionary<string, List<T>>", csharp);
        Assert.Contains("public static Dictionary<string, List<T>> GetLookup<T>", csharp);
    }

    [Fact]
    public void E2E_GenericMethod_InClass_CompilesCorrectly()
    {
        var calor = """
            §M{m001:Test}
            §CL{c001:Container:pub}
              §MT{m001:Convert:pub}<T, U>
                §I{T:input}
                §I{Func<T, U>:converter}
                §O{U}
                §R §C{converter} §A input §/C
              §/MT{m001}
            §/CL{c001}
            §/M{m001}
            """;

        var csharp = CompileToCS(calor);

        Assert.Contains("public class Container", csharp);
        Assert.Contains("public U Convert<T, U>(T input, Func<T, U> converter)", csharp);
    }

    [Fact]
    public void E2E_GenericFunction_WithStructConstraint_CompilesCorrectly()
    {
        var calor = """
            §M{m001:Test}
            §F{f001:GetDefault:pub}<T>
              §WHERE T : struct
              §O{T}
              §R default
            §/F{f001}
            §/M{m001}
            """;

        var csharp = CompileToCS(calor);

        Assert.Contains("public static T GetDefault<T>()", csharp);
        Assert.Contains("where T : struct", csharp);
    }

    [Fact]
    public void E2E_GenericFunction_WithNewConstraint_CompilesCorrectly()
    {
        var calor = """
            §M{m001:Test}
            §F{f001:Create:pub}<T>
              §WHERE T : new
              §O{T}
              §B{result} §NEW{T}
              §R result
            §/F{f001}
            §/M{m001}
            """;

        var csharp = CompileToCS(calor);

        Assert.Contains("public static T Create<T>()", csharp);
        Assert.Contains("where T : new()", csharp);
    }

    [Fact]
    public void E2E_GenericClass_MultipleTypeParams_CompilesCorrectly()
    {
        var calor = """
            §M{m001:Test}
            §CL{c001:Pair:pub}<TKey, TValue>
              §FLD{TKey:Key:pub}
              §FLD{TValue:Value:pub}
            §/CL{c001}
            §/M{m001}
            """;

        var csharp = CompileToCS(calor);

        Assert.Contains("public class Pair<TKey, TValue>", csharp);
        Assert.Contains("public TKey Key", csharp);
        Assert.Contains("public TValue Value", csharp);
    }

    #endregion

    #region CSharpEmitter Tests

    [Fact]
    public void CSharpEmitter_GenericFunction_EmitsTypeParams()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Transform:pub}<T, U>
              §I{T:input}
              §O{U}
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var emitter = new CSharpEmitter();
        var csharp = emitter.Emit(module);

        Assert.Contains("<T, U>", csharp);
        Assert.Contains("Transform<T, U>", csharp);
    }

    [Fact]
    public void CSharpEmitter_WhereClause_EmitsConstraints()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Sort:pub}<T>
              §WHERE T : class, IComparable<T>
              §I{List<T>:items}
              §O{void}
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var emitter = new CSharpEmitter();
        var csharp = emitter.Emit(module);

        Assert.Contains("where T : class, IComparable<T>", csharp);
    }

    [Fact]
    public void CSharpEmitter_GenericClass_EmitsCorrectly()
    {
        var source = """
            §M{m001:Test}
            §CL{c001:Cache:pub}<TKey, TValue>
              §WHERE TKey : class
              §FLD{Dictionary<TKey, TValue>:_data:pri}
            §/CL{c001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var emitter = new CSharpEmitter();
        var csharp = emitter.Emit(module);

        Assert.Contains("public class Cache<TKey, TValue>", csharp);
        Assert.Contains("where TKey : class", csharp);
        Assert.Contains("Dictionary<TKey, TValue>", csharp);
    }

    [Fact]
    public void CSharpEmitter_GenericInterface_EmitsCorrectly()
    {
        var source = """
            §M{m001:Test}
            §IFACE{i001:IFactory:pub}<T>
              §WHERE T : new
              §MT{m001:Create}
                §O{T}
              §/MT{m001}
            §/IFACE{i001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var emitter = new CSharpEmitter();
        var csharp = emitter.Emit(module);

        Assert.Contains("public interface IFactory<T>", csharp);
        Assert.Contains("where T : new()", csharp);
        Assert.Contains("T Create()", csharp);
    }

    [Fact]
    public void CSharpEmitter_GenericMethod_InClass_EmitsCorrectly()
    {
        var source = """
            §M{m001:Test}
            §CL{c001:Mapper:pub}
              §MT{m001:Map:pub}<TSource, TDest>
                §WHERE TDest : new
                §I{TSource:source}
                §O{TDest}
              §/MT{m001}
            §/CL{c001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var emitter = new CSharpEmitter();
        var csharp = emitter.Emit(module);

        Assert.Contains("public TDest Map<TSource, TDest>(TSource source)", csharp);
        Assert.Contains("where TDest : new()", csharp);
    }

    #endregion

    #region CalorEmitter Tests

    [Fact]
    public void CalorEmitter_GenericFunction_EmitsNewSyntax()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Identity:pub}<T>
              §I{T:value}
              §O{T}
              §R value
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var emitter = new CalorEmitter();
        var calorOutput = emitter.Emit(module);

        Assert.Contains("§F{f001:Identity<T>:pub}", calorOutput);
        Assert.Contains("§I{T:value}", calorOutput);
        Assert.Contains("§O{T}", calorOutput);
    }

    [Fact]
    public void CalorEmitter_WhereClause_EmitsCorrectly()
    {
        var source = """
            §M{m001:Test}
            §CL{c001:Repository:pub}<T>
              §WHERE T : class
              §FLD{T:_item:pri}
            §/CL{c001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var emitter = new CalorEmitter();
        var calorOutput = emitter.Emit(module);

        Assert.Contains("§CL{c001:Repository<T>", calorOutput);
        Assert.Contains("§WHERE T : class", calorOutput);
    }

    [Fact]
    public void CalorEmitter_GenericClass_MultipleConstraints_EmitsCorrectly()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Sort:pub}<T>
              §WHERE T : class, IComparable<T>
              §I{List<T>:items}
              §O{void}
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var emitter = new CalorEmitter();
        var calorOutput = emitter.Emit(module);

        Assert.Contains("§WHERE T : class, IComparable<T>", calorOutput);
    }

    #endregion

    #region C# to Calor Conversion Tests

    [Fact]
    public void Convert_GenericClass_EmitsCorrectCalor()
    {
        var csharp = """
            public class Box<T>
            {
                public T Value { get; set; }
            }
            """;

        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join("\n", result.Issues.Select(i => i.Message)));
        Assert.NotNull(result.CalorSource);
        Assert.Contains("§CL{", result.CalorSource);
        Assert.Contains("<T>", result.CalorSource);
    }

    [Fact]
    public void Convert_GenericMethodWithConstraint_EmitsWhere()
    {
        var csharp = """
            public class Utils
            {
                public T Clone<T>(T item) where T : ICloneable
                {
                    return (T)item.Clone();
                }
            }
            """;

        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join("\n", result.Issues.Select(i => i.Message)));
        Assert.NotNull(result.CalorSource);
        Assert.Contains("<T>", result.CalorSource);
        Assert.Contains("§WHERE T : ICloneable", result.CalorSource);
    }

    [Fact]
    public void Convert_GenericInterface_EmitsCorrectCalor()
    {
        var csharp = """
            public interface IRepository<T> where T : class
            {
                T GetById(int id);
                void Save(T entity);
            }
            """;

        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join("\n", result.Issues.Select(i => i.Message)));
        Assert.NotNull(result.CalorSource);
        Assert.Contains("§IFACE{", result.CalorSource);
        Assert.Contains("IRepository", result.CalorSource);
        // Verify method signatures are present
        Assert.Contains("GetById", result.CalorSource);
        Assert.Contains("Save", result.CalorSource);
    }

    [Fact]
    public void Convert_GenericFunction_EmitsCorrectCalor()
    {
        var csharp = """
            public static class Utils
            {
                public static T Identity<T>(T value)
                {
                    return value;
                }
            }
            """;

        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join("\n", result.Issues.Select(i => i.Message)));
        Assert.NotNull(result.CalorSource);
        Assert.Contains("<T>", result.CalorSource);
        Assert.Contains("§I{T:value}", result.CalorSource);
    }

    [Fact]
    public void Convert_NestedGenericTypes_EmitsCorrectCalor()
    {
        var csharp = """
            public class Cache
            {
                public Dictionary<string, List<int>> GetData()
                {
                    return new Dictionary<string, List<int>>();
                }
            }
            """;

        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join("\n", result.Issues.Select(i => i.Message)));
        Assert.NotNull(result.CalorSource);
        // Verify nested generic types are converted (Dict is Calor shorthand for Dictionary)
        Assert.Contains("Dict<str, List<i32>>", result.CalorSource);
    }

    #endregion
}
