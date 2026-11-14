using System.Reflection;
using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public class GenericSyntaxTests
{
    [Theory]
    [InlineData("result")]
    [InlineData("Result")]
    public void Convert_GenericSizedArray_PreservesElementType(string name)
    {
        var source = $$"""
            public static class Arrays
            {
                public static T[] Create<T>(int count)
                {
                    T[] {{name}} = new T[count];
                    return {{name}};
                }
            }
            """;

        var result = new CSharpToCalorConverter().Convert(source);
        Assert.True(result.Success, string.Join("\n", result.Issues));
        Assert.Empty(result.Losses);
        var compiled = Program.Compile(result.CalorSource!, "array.calr");
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        Assert.Contains("new T[count]", compiled.GeneratedCode);
        Assert.True(GeneratedCSharpCompiler.Validate(compiled.GeneratedCode!).CompilationSuccess);
    }

    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
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
            module Test
              func Identity<T> public (T:value) → T
                !
                return value
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
            module Test
              func Map<T, U> public (T:input, Func<T, U>:mapper ! unknown) → U
                !
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
            module Test
              class Repository<T> public
                where T : class
                field T:_item private
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
            module Test
              func GetItems public (List<int>:items) → void
                !
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("; ", diagnostics.Errors));
        var func = module.Functions[0];
        Assert.Single(func.Parameters);
        Assert.Equal("List<int>", func.Parameters[0].TypeName);
    }

    [Fact]
    public void Parse_GenericTypeRef_Nested()
    {
        var source = """
            module Test
              func GetLookup<T> public (Dictionary<string, List<T>>:lookup) → void
                !
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("; ", diagnostics.Errors));
        var func = module.Functions[0];
        Assert.Single(func.Parameters);
        Assert.Equal("Dictionary<string,List<T>>", func.Parameters[0].TypeName);
    }

    [Fact]
    public void Parse_Where_MultipleConstraints()
    {
        var source = """
            module Test
              func Sort<T> public (List<T>:items) → void
                !
                where T : class, IComparable<T>
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
            module Test
              interface IRepository<T>
                where T : class
                func Get (int:id) → T
                  !
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
    public void Parse_BracedWhereConstraint()
    {
        var source = """
            module Test
              func Identity<T> public (T:value) → T
                !
                where T : class
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("; ", diagnostics.Errors));
        var func = module.Functions[0];
        Assert.Single(func.TypeParameters);
        Assert.Single(func.TypeParameters[0].Constraints);
        Assert.Equal(TypeConstraintKind.Class, func.TypeParameters[0].Constraints[0].Kind);
        _ = Parse("module Test\n  func Identity<T> (T:value) → T\n    §WHERE{T:class}", out var retired);
        Assert.Contains(retired.Errors, error => error.Code == "Calor0006"
            && error.Message.Contains("§WHERE", StringComparison.Ordinal));
    }

    #region E2E Compilation Tests

    [Fact]
    public void E2E_GenericFunction_CompilesAndRuns()
    {
        var calor = """
            module Test
              func Identity<T> public (T:value) → T
                !
                return value
            """;

        var csharp = CompileToCS(calor);

        Assert.Contains("public static T Identity<T>(T value)", csharp);
        Assert.Contains("return value;", csharp);
        var compilation = CSharpCompilation.Create("GenericIdentity_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble + csharp)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        var identity = Assembly.Load(stream.ToArray()).GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Single(method => method.Name == "Identity");
        Assert.Equal(42, identity.MakeGenericMethod(typeof(int)).Invoke(null, [42]));
        Assert.Equal("value", identity.MakeGenericMethod(typeof(string)).Invoke(null, ["value"]));
    }

    [Fact]
    public void E2E_GenericClass_WithConstraint_CompilesCorrectly()
    {
        var calor = """
            module Test
              class Repository<T> public
                where T : class
                field List<T>:_items private
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
            module Test
              interface IRepository<T>
                where T : class
                func GetById (int:id) → T
                  !
                func Save (T:entity) → void
                  !
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
            module Test
              func GetLookup<T> public (Dictionary<string, List<T>>:lookup) → Dictionary<string, List<T>>
                !
                return lookup
            """;

        var csharp = CompileToCS(calor);

        Assert.Contains("Dictionary<string, List<T>>", csharp);
        Assert.Contains("public static Dictionary<string, List<T>> GetLookup<T>", csharp);
    }

    [Fact]
    public void E2E_GenericMethod_InClass_CompilesCorrectly()
    {
        // This emission-only positive retains its explicit permissive waiver.
        // The same native callback must still charge unknown under the default policy.
        var calor = """
            module Test
              class Container public
                func Convert<T, U> public (T:input, Func<T, U>:converter ! unknown) → U
                  !
                  return converter(input)
            """;

        var result = Program.Compile(calor, null, new CompilationOptions
        {
            UnknownCallPolicy = Calor.Compiler.Effects.UnknownCallPolicy.Permissive
        });
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        var csharp = result.GeneratedCode;

        Assert.Contains("public class Container", csharp);
        Assert.Contains("public U Convert<T, U>(T input, Func<T, U> converter)", csharp);
        var strict = Program.Compile(calor);
        Assert.Contains(strict.Diagnostics.Errors, error => error.Code == "Calor0410"
            && error.Message.Contains("'unknown'", StringComparison.Ordinal));
        Assert.DoesNotContain(strict.Diagnostics.Errors,
            error => error.Code is "Calor0003" or "Calor0006" or "Calor0007" or "Calor0100");
    }

    [Fact]
    public void E2E_GenericFunction_WithStructConstraint_CompilesCorrectly()
    {
        var calor = """
            module Test
              func GetDefault<T> public () → T
                !
                where T : struct
                return default
            """;

        var csharp = CompileToCS(calor);

        Assert.Contains("public static T GetDefault<T>()", csharp);
        Assert.Contains("where T : struct", csharp);
    }

    [Fact]
    public void E2E_GenericFunction_WithNewConstraint_CompilesCorrectly()
    {
        var calor = """
            module Test
              func Create<T> public () → T
                where T : new()
                ! alloc
                let result = new T()
                return result
            """;

        var csharp = CompileToCS(calor);

        Assert.Contains("public static T Create<T>()", csharp);
        Assert.Contains("where T : new()", csharp);
    }

    [Fact]
    public void E2E_GenericClass_MultipleTypeParams_CompilesCorrectly()
    {
        var calor = """
            module Test
              class Pair<TKey, TValue> public
                field TKey:Key public
                field TValue:Value public
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
            module Test
              func Transform<T, U> public (T:input) → U
                !
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
            module Test
              func Sort<T> public (List<T>:items) → void
                !
                where T : class, IComparable<T>
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
            module Test
              class Cache<TKey, TValue> public
                where TKey : class
                field Dictionary<TKey, TValue>:_data private
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
            module Test
              interface IFactory<T>
                where T : new()
                func Create () → T
                  !
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
            module Test
              class Mapper public
                func Map<TSource, TDest> public (TSource:source) → TDest
                  !
                  where TDest : new()
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
            module Test
              func Identity<T> public (T:value) → T
                !
                return value
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var emitter = new CalorEmitter();
        var calorOutput = emitter.Emit(module);

        Assert.Contains("func Identity<T>", calorOutput);
        Assert.Contains("T:value", calorOutput);
        Assert.Contains("→ T", calorOutput);
        var reparsed = Parse(calorOutput, out var reparsedDiagnostics);
        Assert.False(reparsedDiagnostics.HasErrors, string.Join("\n", reparsedDiagnostics));
        Assert.Single(Assert.Single(reparsed.Functions).TypeParameters);
    }

    [Fact]
    public void CalorEmitter_WhereClause_EmitsCorrectly()
    {
        var source = """
            module Test
              class Repository<T> public
                where T : class
                field T:_item private
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var emitter = new CalorEmitter();
        var calorOutput = emitter.Emit(module);

        Assert.Contains("class Repository<T>", calorOutput);
        Assert.Contains("where T : class", calorOutput);
        var reparsed = Parse(calorOutput, out var reparsedDiagnostics);
        Assert.False(reparsedDiagnostics.HasErrors, string.Join("\n", reparsedDiagnostics));
        Assert.Single(Assert.Single(Assert.Single(reparsed.Classes).TypeParameters).Constraints);
    }

    [Fact]
    public void CalorEmitter_GenericClass_MultipleConstraints_EmitsCorrectly()
    {
        var source = """
            module Test
              func Sort<T> public (List<T>:items) → void
                !
                where T : class, IComparable<T>
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var emitter = new CalorEmitter();
        var calorOutput = emitter.Emit(module);

        Assert.Contains("where T : class, IComparable<T>", calorOutput);
        var reparsed = Parse(calorOutput, out var reparsedDiagnostics);
        Assert.False(reparsedDiagnostics.HasErrors, string.Join("\n", reparsedDiagnostics));
        Assert.Equal(2, Assert.Single(Assert.Single(reparsed.Functions).TypeParameters).Constraints.Count);
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

        var converter = new CSharpToCalorConverter(new ConversionOptions { Fidelity = ConversionFidelity.Lossy });
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join("\n", result.Issues.Select(i => i.Message)));
        Assert.NotNull(result.CalorSource);
        Assert.Contains("class Box<T>", result.CalorSource);
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

        var converter = new CSharpToCalorConverter(new ConversionOptions { Fidelity = ConversionFidelity.Lossy });
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join("\n", result.Issues.Select(i => i.Message)));
        Assert.NotNull(result.CalorSource);
        Assert.Contains("<T>", result.CalorSource);
        Assert.Contains("where T : ICloneable", result.CalorSource);
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

        var converter = new CSharpToCalorConverter(new ConversionOptions { Fidelity = ConversionFidelity.Lossy });
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join("\n", result.Issues.Select(i => i.Message)));
        Assert.NotNull(result.CalorSource);
        Assert.Contains("interface IRepository<T>", result.CalorSource);
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

        var converter = new CSharpToCalorConverter(new ConversionOptions { Fidelity = ConversionFidelity.Lossy });
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join("\n", result.Issues.Select(i => i.Message)));
        Assert.NotNull(result.CalorSource);
        Assert.Contains("<T>", result.CalorSource);
        Assert.Contains("(T:value)", result.CalorSource);
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

        var converter = new CSharpToCalorConverter(new ConversionOptions { Fidelity = ConversionFidelity.Lossy });
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join("\n", result.Issues.Select(i => i.Message)));
        Assert.NotNull(result.CalorSource);
        var reparsed = Parse(result.CalorSource!, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var method = Assert.Single(Assert.Single(reparsed.Classes).Methods);
        Assert.Equal("Dictionary<string,List<int>>", method.Output!.TypeName);
        var regenerated = new CSharpEmitter().Emit(reparsed);
        Assert.Contains("Dictionary<string, List<int>>", regenerated);
        Assert.True(GeneratedCSharpCompiler.Validate(regenerated).CompilationSuccess);
    }

    #endregion

    #region Type Checker Tests

    [Fact]
    public void TypeChecker_TypeParamInScope_Resolves()
    {
        var source = """
            module Test
              func Identity<T> public (T:value) → T
                !
                return value
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("; ", diagnostics.Errors));

        var checker = new TypeChecking.TypeChecker(diagnostics);
        checker.Check(module);

        // No errors should occur - T should be in scope
        Assert.False(diagnostics.HasErrors, string.Join("; ", diagnostics.Errors));
    }

    [Fact]
    public void TypeChecker_GenericListType_Resolves()
    {
        var source = """
            module Test
              func Process<T> public (List<T>:items) → void
                !
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("; ", diagnostics.Errors));

        var checker = new TypeChecking.TypeChecker(diagnostics);
        checker.Check(module);

        // List<T> should be resolved without errors
        Assert.False(diagnostics.HasErrors, string.Join("; ", diagnostics.Errors));
    }

    [Fact]
    public void TypeChecker_NestedGenericType_Resolves()
    {
        var source = """
            module Test
              func GetData<T> public (Dictionary<string, List<T>>:data) → void
                !
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("; ", diagnostics.Errors));

        var checker = new TypeChecking.TypeChecker(diagnostics);
        checker.Check(module);

        // Nested generic Dictionary<str, List<T>> should resolve without errors
        Assert.False(diagnostics.HasErrors, string.Join("; ", diagnostics.Errors));
    }

    [Fact]
    public void TypeChecker_MultipleTypeParams_AllResolve()
    {
        var source = """
            module Test
              func Combine<T, U> public (T:first, U:second) → T
                !
                return first
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("; ", diagnostics.Errors));

        var checker = new TypeChecking.TypeChecker(diagnostics);
        checker.Check(module);

        // Both T and U should be in scope
        Assert.False(diagnostics.HasErrors, string.Join("; ", diagnostics.Errors));
    }

    #endregion

    #region Parser Edge Case Tests

    [Fact]
    public void Parse_MultipleWhereClauses_DifferentTypeParams()
    {
        var source = """
            module Test
              func Combine<T, U> public (T:a, U:b) → void
                !
                where T : class
                where U : struct
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("; ", diagnostics.Errors));
        var func = module.Functions[0];
        Assert.Equal(2, func.TypeParameters.Count);
        Assert.Single(func.TypeParameters[0].Constraints); // T : class
        Assert.Equal(TypeConstraintKind.Class, func.TypeParameters[0].Constraints[0].Kind);
        Assert.Single(func.TypeParameters[1].Constraints); // U : struct
        Assert.Equal(TypeConstraintKind.Struct, func.TypeParameters[1].Constraints[0].Kind);
    }

    [Fact]
    public void Parse_Where_InvalidTypeParam_ReportsError()
    {
        var source = """
            module Test
              func Test<T> public () → void
                where X : class
            """;

        var module = Parse(source, out var diagnostics);

        // Should report error: X is not a declared type parameter
        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics.Errors, e => e.Message.Contains("not found"));
        Assert.DoesNotContain(diagnostics.Errors, error => error.Code is "Calor0003" or "Calor0006" or "Calor0007");
    }

    [Fact]
    public void E2E_GenericMethodInInterface_WithTypeParams_CompilesCorrectly()
    {
        var calor = """
            module Test
              interface IMapper
                func Map<TSource, TDest> (TSource:source) → TDest
                  !
            """;

        var csharp = CompileToCS(calor);

        Assert.Contains("TDest Map<TSource, TDest>(TSource source)", csharp);
    }

    [Fact]
    public void E2E_CombinedInterfaceAndMethodTypeParams_CompilesCorrectly()
    {
        var calor = """
            module Test
              interface IConverter<T>
                where T : class
                func Convert<U> (T:input) → U
                  !
            """;

        var csharp = CompileToCS(calor);

        Assert.Contains("public interface IConverter<T>", csharp);
        Assert.Contains("where T : class", csharp);
        Assert.Contains("U Convert<U>(T input)", csharp);
    }

    [Fact]
    public void Parse_GenericTypeInFieldDeclaration_Parses()
    {
        var source = """
            module Test
              class Container<T> public
                field List<T>:_items private
                field Dictionary<string, T>:_lookup private
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("; ", diagnostics.Errors));
        var cls = module.Classes[0];
        Assert.Equal(2, cls.Fields.Count);
        Assert.Equal("List<T>", cls.Fields[0].TypeName);
        Assert.Equal("Dictionary<string,T>", cls.Fields[1].TypeName);
    }

    #endregion
}
