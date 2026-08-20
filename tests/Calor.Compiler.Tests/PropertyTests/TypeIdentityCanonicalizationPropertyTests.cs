using Calor.Compiler.Binding;
using FsCheck;
using FsCheck.Xunit;

namespace Calor.Compiler.Tests.PropertyTests;

/// <summary>
/// Property-based tests for <see cref="TypeIdentity.Canonicalize"/>.
///
/// Canonicalization is the entry point for the compiler's type-identity model
/// (overload resolution, generic unification, Option/Nullable unwrapping,
/// alias collapsing). If it isn't idempotent, downstream comparisons that
/// call it defensively — for instance, both sides of a lookup in
/// <c>TypedBugPatternAnalysis</c> — can silently disagree.
///
/// The core invariant checked here is:
///   Canonicalize(Canonicalize(x)) == Canonicalize(x)
/// with a couple of related sanity properties.
/// </summary>
public class TypeIdentityCanonicalizationPropertyTests
{
    #region Generators

    /// <summary>
    /// A generator for type-name strings the canonicalizer is designed to
    /// handle. Values are drawn from the aliases enumerated inside
    /// <see cref="TypeIdentity.Canonicalize"/> plus a handful of user-defined
    /// / nominal names, wrapped in Option, Nullable, arrays, and generics of
    /// bounded depth so runs stay short.
    /// </summary>
    public static Arbitrary<string> TypeNameStrings()
    {
        var leaves = new[]
        {
            // Primitive aliases — every arm of the switch in Canonicalize.
            "int", "Int32", "i32", "i16", "short", "byte", "u8",
            "long", "i64", "ulong", "u64", "uint", "u32",
            "float", "double", "f64", "single", "f32",
            "decimal", "dec", "bool", "boolean",
            "string", "str", "object", "any", "void", "never",
            // A few nominal / user types that pass through untouched.
            "MyType", "Foo.Bar.Baz", "Widget", "Customer",
        };

        var leafGen = Gen.Elements(leaves);

        // Recursive wrapping: Option<T>, T?, ?T, T[], Generic<T>, Generic<T,U>.
        // Depth is capped at 3 to keep sample cost low.
        Gen<string> Grow(Gen<string> inner, int depth)
        {
            if (depth <= 0) return inner;

            var wrap = Gen.OneOf(
                inner,
                inner.Select(t => $"Option<{t}>"),
                inner.Select(t => $"{t}?"),
                inner.Select(t => $"?{t}"),
                inner.Select(t => $"{t}[]"),
                inner.Select(t => $"List<{t}>"),
                from a in inner from b in inner select $"Dictionary<{a},{b}>");

            return Grow(wrap, depth - 1);
        }

        return Arb.From(Grow(leafGen, depth: 3));
    }

    #endregion

    #region Property Tests

    [Property(MaxTest = 200)]
    public Property Canonicalize_IsIdempotent()
    {
        // Core property: applying Canonicalize twice is the same as once.
        return Prop.ForAll(TypeNameStrings(), typeName =>
        {
            var once = TypeIdentity.Canonicalize(typeName);
            var twice = TypeIdentity.Canonicalize(once);
            return string.Equals(once, twice, StringComparison.Ordinal);
        });
    }

    [Property(MaxTest = 200)]
    public Property Canonicalize_IsDeterministic()
    {
        // Determinism: no hidden state, same input maps to same output.
        return Prop.ForAll(TypeNameStrings(), typeName =>
        {
            var a = TypeIdentity.Canonicalize(typeName);
            var b = TypeIdentity.Canonicalize(typeName);
            return string.Equals(a, b, StringComparison.Ordinal);
        });
    }

    [Property(MaxTest = 100)]
    public Property Canonicalize_AliasesCollapse()
    {
        // For the numeric aliases the switch in Canonicalize lists, differently
        // spelled inputs that mean the same thing must produce the same output.
        // If this property fails, overload resolution and generic unification
        // will start disagreeing on which types are "the same".
        var aliasGroups = new[]
        {
            new[] { "int", "Int32", "i32", "int32" },
            new[] { "long", "i64", "Int64" },
            new[] { "byte", "u8" },
            new[] { "short", "i16" },
            new[] { "float", "f64", "double" },
            new[] { "bool", "boolean" },
            new[] { "string", "str" },
            new[] { "any", "object", "unknown" },
        };

        return Prop.ForAll(Gen.Elements(aliasGroups).ToArbitrary(), group =>
        {
            var canonical = TypeIdentity.Canonicalize(group[0]);
            return group.All(alias =>
                string.Equals(
                    TypeIdentity.Canonicalize(alias),
                    canonical,
                    StringComparison.Ordinal));
        });
    }

    #endregion
}
