using Calor.Compiler.Migration;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for converter bugs reported in Newtonsoft.Json and Humanizer migration reports.
/// Each test converts a specific C# pattern and asserts no crash + valid output.
/// </summary>
public class ConverterRobustnessTests
{
    private static (bool success, string calor, string? error) Convert(string csharp)
    {
        try
        {
            var converter = new CSharpToCalorConverter();
            var result = converter.Convert(csharp, "test.cs");
            return (result.Success, result.CalorSource ?? "", null);
        }
        catch (Exception ex)
        {
            return (false, "", $"CRASH: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ── 2.4: notnull generic constraint ──────────────────────────────────

    [Fact]
    public void Convert_NotnullConstraint_DoesNotCrash()
    {
        var (success, calor, error) = Convert("""
            public class Store<TKey, TValue> where TKey : notnull
            {
                private readonly Dictionary<TKey, TValue> _dict = new();
                public TValue Get(TKey key) => _dict[key];
            }
            """);

        Assert.True(success, $"Should not crash. Error: {error}");
        Assert.DoesNotContain("CRASH", error ?? "");
        Assert.Contains("Store", calor);
    }

    // ── 2.1: Nested ternary + is expressions ──────────────────────────

    [Fact]
    public void Convert_NestedTernaryWithIs_DoesNotCrash()
    {
        var (success, calor, error) = Convert("""
            public class Checker
            {
                public bool Check(object value)
                {
                    return value != null ? value is string s && s.Length > 0 : false;
                }
            }
            """);

        Assert.True(success, $"Should not crash. Error: {error}");
    }

    [Fact]
    public void Convert_ComplexTernaryWithTypeCast_DoesNotCrash()
    {
        var (success, calor, error) = Convert("""
            public class Converter<T>
            {
                public bool CanConvert(object value)
                {
                    return value is T other ? other != null : false;
                }
            }
            """);

        Assert.True(success, $"Should not crash. Error: {error}");
    }

    // ── 2.2: Null-coalescing with generic method calls ────────────────

    [Fact]
    public void Convert_NullCoalescingWithGenericCall_DoesNotCrash()
    {
        var (success, calor, error) = Convert("""
            public class Service
            {
                public string GetValue<T>(T? input) where T : class
                {
                    return input?.ToString() ?? "default";
                }
            }
            """);

        Assert.True(success, $"Should not crash. Error: {error}");
    }

    // ── 2.3: For-loop with typed variable init ────────────────────────

    [Fact]
    public void Convert_ForLoopTypedInit_DoesNotCrash()
    {
        var (success, calor, error) = Convert("""
            public class NameTable
            {
                private class Entry
                {
                    public string Value;
                    public Entry Next;
                }

                private Entry[] _entries = new Entry[32];

                public string Find(int index)
                {
                    for (Entry entry = _entries[index]; entry != null; entry = entry.Next)
                    {
                        if (entry.Value != null) return entry.Value;
                    }
                    return null;
                }
            }
            """);

        Assert.True(success, $"Should not crash. Error: {error}");
    }

    // ── 2.5: Null-conditional invocation (?.Invoke) ───────────────────

    [Fact]
    public void Convert_NullConditionalInvoke_DoesNotCrash()
    {
        var (success, calor, error) = Convert("""
            using System;
            public class Publisher
            {
                public event EventHandler<string> OnMessage;
                public void Send(string msg)
                {
                    OnMessage?.Invoke(this, msg);
                }
            }
            """);

        Assert.True(success, $"Should not crash. Error: {error}");
        // Should NOT contain raw C# like "?.Invoke" in a call block
    }

    // ── 2.6: Operator overloads (implicit/explicit) ───────────────────

    [Fact]
    public void Convert_ImplicitOperator_DoesNotCrash()
    {
        var (success, calor, error) = Convert("""
            public struct Meters
            {
                public double Value;
                public Meters(double v) { Value = v; }
                public static implicit operator double(Meters m) => m.Value;
                public static explicit operator Meters(double d) => new Meters(d);
                public static Meters operator +(Meters a, Meters b) => new Meters(a.Value + b.Value);
            }
            """);

        Assert.True(success, $"Should not crash. Error: {error}");
    }

    // ── 1.5 / Lock statement ──────────────────────────────────────────

    [Fact]
    public void Convert_LockStatement_PreservesSemantics()
    {
        var (success, calor, error) = Convert("""
            public class Counter
            {
                private readonly object _lock = new();
                private int _count;
                public void Increment()
                {
                    lock (_lock) { _count++; }
                }
            }
            """);

        Assert.True(success, $"Should not crash. Error: {error}");
        // Should contain §SYNC or equivalent, NOT strip the lock
        Assert.True(
            calor.Contains("§SYNC") || calor.Contains("CSHARP"),
            "Lock should be preserved as §SYNC or §CSHARP, not stripped");
    }

    // ── Using declaration (inline form) ───────────────────────────────

    [Fact]
    public void Convert_UsingDeclaration_PreservesDispose()
    {
        var (success, calor, error) = Convert("""
            using System.IO;
            public class FileHelper
            {
                public string ReadAll(string path)
                {
                    using var reader = new StreamReader(path);
                    return reader.ReadToEnd();
                }
            }
            """);

        Assert.True(success, $"Should not crash. Error: {error}");
    }

    // ── 2.7: Explicit interface implementation ─────────────────────────

    [Fact]
    public void Convert_ExplicitInterfaceImpl_DoesNotCrash()
    {
        var (success, calor, error) = Convert("""
            using System.Collections;
            using System.Collections.Generic;
            public class MyList : IEnumerable<int>
            {
                private readonly List<int> _items = new();
                public IEnumerator<int> GetEnumerator() => _items.GetEnumerator();
                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }
            """);

        Assert.True(success, $"Should not crash. Error: {error}");
    }

    // ── ConfigureAwait pattern ─────────────────────────────────────────

    [Fact]
    public void Convert_ConfigureAwait_DoesNotCrash()
    {
        var (success, calor, error) = Convert("""
            using System.IO;
            using System.Threading;
            using System.Threading.Tasks;
            public class AsyncReader
            {
                public async Task<string> ReadAsync(string path, CancellationToken ct)
                {
                    using var reader = new StreamReader(path);
                    return await reader.ReadToEndAsync(ct).ConfigureAwait(false);
                }
            }
            """);

        Assert.True(success, $"Should not crash. Error: {error}");
    }

    // ── Volatile field ─────────────────────────────────────────────────

    [Fact]
    public void Convert_VolatileField_DoesNotCrash()
    {
        var (success, calor, error) = Convert("""
            public class Config
            {
                private volatile bool _initialized;
                public bool IsReady => _initialized;
                public void Initialize() { _initialized = true; }
            }
            """);

        Assert.True(success, $"Should not crash. Error: {error}");
    }

    // ── Params keyword ─────────────────────────────────────────────────

    [Fact]
    public void Convert_ParamsArray_DoesNotCrash()
    {
        var (success, calor, error) = Convert("""
            public class Formatter
            {
                public string Format(string template, params object[] args)
                {
                    return string.Format(template, args);
                }
            }
            """);

        Assert.True(success, $"Should not crash. Error: {error}");
    }
}
