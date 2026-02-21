namespace Calor.Conversion.Tests;

/// <summary>
/// A single conversion test case derived from the challenge reports.
/// </summary>
public sealed record ConversionSnippet(
    string Id,
    string Feature,
    string Description,
    string CSharpSource,
    bool IsKnownGap = false,
    bool RoundTripSupported = true);

/// <summary>
/// Central catalog of all C# snippets from the conversion-reports/ challenge reports.
/// Each snippet maps to a feature category for breakdown reporting.
/// </summary>
public static class ConversionCatalog
{
    // ── 01: Basic Classes ──

    public static readonly ConversionSnippet SimpleClassWithMethods = new(
        "01-01", "BasicClasses", "Simple class with methods",
        """
        public class Calculator
        {
            public int Add(int a, int b) => a + b;
            public int Subtract(int a, int b) => a - b;
        }
        """);

    public static readonly ConversionSnippet ClassWithProperties = new(
        "01-02", "BasicClasses", "Class with properties",
        """
        public class Person
        {
            public string Name { get; set; }
            public int Age { get; }
            public string Email { get; set; } = "";
        }
        """);

    public static readonly ConversionSnippet ClassWithConstructor = new(
        "01-03", "BasicClasses", "Class with constructor",
        """
        public class Account
        {
            private readonly string _id;
            private decimal _balance;

            public Account(string id, decimal initialBalance)
            {
                _id = id;
                _balance = initialBalance;
            }

            public decimal GetBalance() => _balance;
        }
        """);

    public static readonly ConversionSnippet StaticClass = new(
        "01-04", "BasicClasses", "Static class with static methods",
        """
        public static class MathUtils
        {
            public static double Square(double x) => x * x;
            public static double Cube(double x) => x * x * x;
        }
        """);

    // ── 02: Interfaces and Enums ──

    public static readonly ConversionSnippet SimpleInterface = new(
        "02-01", "Interfaces", "Simple interface",
        """
        public interface IAnimal
        {
            string Speak();
            string Name { get; }
        }
        """);

    public static readonly ConversionSnippet GenericInterface = new(
        "02-02", "Interfaces", "Interface with generic type and constraint",
        """
        using System.Collections.Generic;

        public interface IRepository<T> where T : class
        {
            T GetById(int id);
            void Save(T entity);
            IEnumerable<T> GetAll();
        }
        """);

    public static readonly ConversionSnippet SimpleEnum = new(
        "02-03", "Enums", "Simple enum",
        """
        public enum Color
        {
            Red,
            Green,
            Blue
        }
        """);

    public static readonly ConversionSnippet EnumWithValues = new(
        "02-04", "Enums", "Enum with explicit values",
        """
        public enum HttpStatus
        {
            Ok = 200,
            NotFound = 404,
            ServerError = 500
        }
        """);

    // ── 03: Control Flow ──

    public static readonly ConversionSnippet IfElse = new(
        "03-01", "ControlFlow", "If/else chain",
        """
        public class Guard
        {
            public string Classify(int value)
            {
                if (value > 0)
                {
                    return "positive";
                }
                else if (value < 0)
                {
                    return "negative";
                }
                else
                {
                    return "zero";
                }
            }
        }
        """);

    public static readonly ConversionSnippet ForLoop = new(
        "03-02", "ControlFlow", "For loop",
        """
        public class Loops
        {
            public int Sum(int n)
            {
                int total = 0;
                for (int i = 0; i < n; i++)
                {
                    total += i;
                }
                return total;
            }
        }
        """);

    public static readonly ConversionSnippet WhileLoop = new(
        "03-03", "ControlFlow", "While loop",
        """
        public class Loops
        {
            public int CountDown(int start)
            {
                int count = 0;
                while (start > 0)
                {
                    start--;
                    count++;
                }
                return count;
            }
        }
        """);

    public static readonly ConversionSnippet SwitchStatement = new(
        "03-04", "ControlFlow", "Switch statement",
        """
        public class Router
        {
            public string Route(int code)
            {
                switch (code)
                {
                    case 200: return "OK";
                    case 404: return "Not Found";
                    case 500: return "Server Error";
                    default: return "Unknown";
                }
            }
        }
        """);

    // ── 04: Generics and Inheritance ──

    public static readonly ConversionSnippet GenericClassNotnull = new(
        "04-01", "Generics", "Generic class with notnull constraint",
        """
        public class Box<T> where T : notnull
        {
            public T Value { get; set; }
            public Box(T value) { Value = value; }
        }
        """);

    public static readonly ConversionSnippet GenericMethod = new(
        "04-02", "Generics", "Generic method with class constraint",
        """
        public class Converter
        {
            public T Identity<T>(T value) where T : class => value;
        }
        """);

    public static readonly ConversionSnippet Inheritance = new(
        "04-03", "Inheritance", "Class inheritance with virtual/override",
        """
        using System;

        public class Shape
        {
            public virtual double Area() => 0;
        }

        public class Circle : Shape
        {
            public double Radius { get; }
            public Circle(double radius) { Radius = radius; }
            public override double Area() => Math.PI * Radius * Radius;
        }
        """);

    public static readonly ConversionSnippet Variance = new(
        "04-04", "Generics", "Covariant and contravariant interfaces",
        """
        public interface IProducer<out T>
        {
            T Produce();
        }
        public interface IConsumer<in T>
        {
            void Consume(T item);
        }
        """);

    // ── 05: Async/LINQ ──

    public static readonly ConversionSnippet AsyncMethod = new(
        "05-01", "Async", "Simple async method",
        """
        using System.Threading.Tasks;

        public class DataService
        {
            public async Task<string> FetchDataAsync()
            {
                await Task.Delay(100);
                return "data";
            }
        }
        """);

    public static readonly ConversionSnippet LinqWhere = new(
        "05-02", "LINQ", "LINQ Where with lambda",
        """
        using System.Linq;
        using System.Collections.Generic;

        public class Filter
        {
            public List<int> GetPositive(List<int> items)
            {
                return items.Where(x => x > 0).ToList();
            }
        }
        """);

    public static readonly ConversionSnippet LinqChain = new(
        "05-03", "LINQ", "LINQ method chain with Select/OrderBy",
        """
        using System.Linq;
        using System.Collections.Generic;

        public class Transform
        {
            public List<string> GetNames(List<string> items)
            {
                return items.Where(s => s.Length > 0)
                            .Select(s => s.ToUpper())
                            .OrderBy(s => s)
                            .ToList();
            }
        }
        """);

    // ── 06: Patterns and Expressions ──

    public static readonly ConversionSnippet SwitchExpression = new(
        "06-01", "Patterns", "Switch expression with relational patterns",
        """
        public class Grader
        {
            public string GetGrade(int score) => score switch
            {
                >= 90 => "A",
                >= 80 => "B",
                >= 70 => "C",
                _ => "F"
            };
        }
        """);

    public static readonly ConversionSnippet DeclarationPattern = new(
        "06-03", "Patterns", "Is pattern with variable binding",
        """
        public class PatternDemo
        {
            public int GetLength(object value)
            {
                if (value is string text)
                {
                    return text.Length;
                }
                return 0;
            }
        }
        """);

    public static readonly ConversionSnippet PatternCombinators = new(
        "06-04", "Patterns", "Not/Or/And pattern combinators",
        """
        public class Combinators
        {
            public string Check(object obj)
            {
                if (obj is not null)
                    return "has value";
                return "null";
            }
        }
        """);

    // ── 07: Delegates and Lambdas ──

    public static readonly ConversionSnippet DelegateDeclarations = new(
        "07-01", "Delegates", "Delegate types",
        """
        public delegate void MyHandler(int x);
        public delegate bool Predicate<T>(T item);
        """);

    public static readonly ConversionSnippet LambdaExpressions = new(
        "07-02", "Lambdas", "Lambda expressions with Func/Action",
        """
        using System;

        public class LambdaDemo
        {
            public void Test()
            {
                Func<int, int> doubler = x => x * 2;
            }
        }
        """);

    public static readonly ConversionSnippet StaticLambda = new(
        "07-03", "Lambdas", "Static lambda expression",
        """
        using System;

        public class StaticLambdaDemo
        {
            public void Test()
            {
                Func<int, int> doubler = static (int x) => x * 2;
            }
        }
        """);

    // ── 08: Modern C# ──

    public static readonly ConversionSnippet RequiredProperties = new(
        "08-01", "ModernCSharp", "Required properties (C# 11+)",
        """
        public class UserDto
        {
            public required string Name { get; set; }
            public required int Age { get; set; }
            public string? Email { get; set; }
        }
        """);

    public static readonly ConversionSnippet TupleLiteral = new(
        "08-02", "ModernCSharp", "Tuple return type",
        """
        public class TupleDemo
        {
            public (int, string) GetPair() => (42, "hello");
        }
        """,
        RoundTripSupported: false);

    public static readonly ConversionSnippet EmptyCollection = new(
        "08-03", "ModernCSharp", "Empty collection expression",
        """
        using System.Collections.Generic;

        public class Container
        {
            public List<string> Items { get; set; } = [];
        }
        """,
        RoundTripSupported: false);

    public static readonly ConversionSnippet NullCoalescing = new(
        "08-04", "ModernCSharp", "Null coalescing operator",
        """
        public class NullDemo
        {
            public string GetValue(string? input)
            {
                return input ?? "default";
            }
        }
        """);

    // ── 09: Known Gaps ──

    public static readonly ConversionSnippet GapRangeExpression = new(
        "09-01", "KnownGaps", "Range expressions (C# 8+)",
        """
        public class RangeDemo
        {
            public int[] Slice(int[] array)
            {
                return array[0..5];
            }
        }
        """,
        IsKnownGap: true);

    public static readonly ConversionSnippet GapIndexFromEnd = new(
        "09-02", "KnownGaps", "Index from end (C# 8+)",
        """
        public class IndexDemo
        {
            public int GetLast(int[] items)
            {
                return items[^1];
            }
        }
        """,
        IsKnownGap: true);

    public static readonly ConversionSnippet GapListPatterns = new(
        "09-03", "KnownGaps", "List patterns (C# 11+)",
        """
        public class ListPatternDemo
        {
            public bool IsFirstAndLast(int[] list)
            {
                return list is [var first, .., var last];
            }
        }
        """,
        IsKnownGap: true);

    public static readonly ConversionSnippet GapRawStringLiterals = new(
        "09-04", "KnownGaps", "Raw string literals (C# 11+)",
        "public class RawStringDemo\n{\n    public string GetJson()\n    {\n        return \"\"\"\n            {\n                \"name\": \"test\"\n            }\n            \"\"\";\n    }\n}",
        IsKnownGap: true);

    public static readonly ConversionSnippet GapCollectionSpread = new(
        "09-05", "KnownGaps", "Collection spread (C# 12+)",
        """
        using System.Collections.Generic;
        using System.Linq;

        public class SpreadDemo
        {
            public int[] Combine(int[] first, int[] second)
            {
                return [..first, ..second];
            }
        }
        """,
        IsKnownGap: true);

    public static readonly ConversionSnippet GapThrowExpression = new(
        "09-06", "KnownGaps", "Throw expression (C# 7+)",
        """
        using System;

        public class ThrowExprDemo
        {
            private string _name = "";
            public void SetName(string value)
            {
                _name = value ?? throw new ArgumentNullException(nameof(value));
            }
        }
        """,
        IsKnownGap: true);

    /// <summary>
    /// All snippets that should successfully convert.
    /// </summary>
    public static IReadOnlyList<ConversionSnippet> SupportedSnippets { get; } = new[]
    {
        SimpleClassWithMethods,
        ClassWithProperties,
        ClassWithConstructor,
        StaticClass,
        SimpleInterface,
        GenericInterface,
        SimpleEnum,
        EnumWithValues,
        IfElse,
        ForLoop,
        WhileLoop,
        SwitchStatement,
        GenericClassNotnull,
        GenericMethod,
        Inheritance,
        Variance,
        AsyncMethod,
        LinqWhere,
        LinqChain,
        SwitchExpression,
        DeclarationPattern,
        PatternCombinators,
        DelegateDeclarations,
        LambdaExpressions,
        StaticLambda,
        RequiredProperties,
        TupleLiteral,
        EmptyCollection,
        NullCoalescing,
    };

    /// <summary>
    /// All snippets for known gaps (should not crash).
    /// </summary>
    public static IReadOnlyList<ConversionSnippet> KnownGapSnippets { get; } = new[]
    {
        GapRangeExpression,
        GapIndexFromEnd,
        GapListPatterns,
        GapRawStringLiterals,
        GapCollectionSpread,
        GapThrowExpression,
    };

    /// <summary>
    /// Supported snippets that also support full round-trip (C# → Calor → C# → Roslyn).
    /// </summary>
    public static IReadOnlyList<ConversionSnippet> RoundTripSnippets { get; } =
        SupportedSnippets.Where(s => s.RoundTripSupported).ToList();

    /// <summary>
    /// All snippets combined.
    /// </summary>
    public static IReadOnlyList<ConversionSnippet> AllSnippets { get; } =
        SupportedSnippets.Concat(KnownGapSnippets).ToList();
}
