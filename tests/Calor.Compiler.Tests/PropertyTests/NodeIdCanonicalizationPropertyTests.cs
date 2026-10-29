using System.Text;
using Calor.Compiler.Ast;
using Calor.Compiler.Ids;
using Calor.Compiler.Migration;
using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace Calor.Compiler.Tests.PropertyTests;

/// <summary>
/// IdScanner observes declaration identities; it does not rewrite them.
/// Parse/emit materializes optional IDs; the production compact-ID migrator
/// canonicalizes legacy ULIDs. Short, compact, and parser-assigned IDs stay stable.
/// </summary>
public sealed class NodeIdCanonicalizationPropertyTests
{
    private const string Path = "tests/generated-ids.calr";

    public sealed record Sample(int Rotation, int FunctionCount, int MethodCount, int Value, string Payload);

    public static Arbitrary<Sample> Modules() => Arb.From(
        from rotation in Gen.Choose(0, 3)
        from functions in Gen.Choose(4, 7)
        from methods in Gen.Choose(1, 3)
        from value in Gen.Choose(-1000, 1000)
        from payload in Gen.ArrayOf(26, Gen.Elements("0123456789ABCDEFGHJKMNPQRSTVWXYZ".ToCharArray()))
        select new Sample(rotation, functions, methods, value, new string(payload)));

    [Property(MaxTest = 100)]
    public Property NodeIdCanonicalization_IsIdempotentAndPreservesDeclarations() =>
        Prop.ForAll(Modules(), sample =>
        {
            Check(sample);
            return true;
        });

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Controls_ExerciseEveryIdStyleOnModuleFunctionsAndMethods(int rotation) =>
        Check(new Sample(rotation, 4, 3, -17, "01J5X7K9M2NPQRSTABWXYZ12345"));

    private static void Check(Sample sample)
    {
        var source = Render(sample);
        var before = Parse(source);
        var scanner = new IdScanner();
        var original = scanner.Scan(before, Path);
        var expectedCount = 2 + sample.FunctionCount + sample.MethodCount;
        Assert.Equal(expectedCount, original.Count);
        Assert.Contains(original, entry => entry.Id.Contains("_auto", StringComparison.Ordinal));
        Assert.Contains(original, entry => IdValidator.IsTestId(entry.Id));
        Assert.Contains(original, entry => IdValidator.IsLegacyUlidId(entry.Id));
        Assert.Contains(original, entry => IdValidator.IsCompactId(entry.Id));

        var emitted = new CalorEmitter().Emit(before);
        Assert.Equal(original.Select(Identity), scanner.Scan(Parse(emitted), Path).Select(Identity));
        var migrator = new CompactIdMigrator();
        var (canonical, log) = migrator.Migrate(new Dictionary<string, string> { [Path] = emitted });
        Assert.Equal(original.Count(entry => IdValidator.IsLegacyUlidId(entry.Id)), log.Entries.Count);
        Assert.NotEmpty(log.Entries);
        var canonicalModule = Parse(canonical[Path]);
        var canonicalEntries = scanner.Scan(canonicalModule, Path);
        Assert.Equal(expectedCount, canonicalEntries.Count);
        Assert.Equal(expectedCount, canonicalEntries.Select(entry => entry.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(canonicalEntries, entry => string.IsNullOrEmpty(entry.Id) || IdValidator.IsLegacyUlidId(entry.Id));
        Assert.Equal(Shape(before), Shape(canonicalModule));
        foreach (var (oldEntry, newEntry) in original.Zip(canonicalEntries))
        {
            Assert.Equal((oldEntry.Kind, oldEntry.Name), (newEntry.Kind, newEntry.Name));
            if (IdValidator.IsLegacyUlidId(oldEntry.Id))
            {
                Assert.NotEqual(oldEntry.Id, newEntry.Id);
                Assert.True(IdValidator.IsCompactId(newEntry.Id));
            }
            else
                Assert.Equal(oldEntry.Id, newEntry.Id);
            if (!newEntry.Id.Contains("_auto", StringComparison.Ordinal))
                Assert.Equal(IdValidationResult.Valid, IdValidator.Validate(newEntry.Id, newEntry.Kind, isTestPath: true));
        }

        // Fresh passes must discover a fixed point, not depend on cached scanner
        // state or a retained migration mapping.
        var emittedAgain = new CalorEmitter().Emit(Parse(canonical[Path]));
        var (twice, noMigrations) = new CompactIdMigrator().Migrate(
            new Dictionary<string, string> { [Path] = emittedAgain });
        Assert.Empty(noMigrations.Entries);
        Assert.Equal(canonical[Path], twice[Path]);
        var rescanned = new IdScanner().Scan(Parse(twice[Path]), Path);
        Assert.Equal(canonicalEntries, rescanned);
        Assert.Equal(emitted, migrator.Revert(canonical, log)[Path]);
    }

    private static ModuleNode Parse(string source)
    {
        var (module, diagnostics) = GeneratedProductionPipelinePropertyTests.Parse(source);
        Assert.False(diagnostics.HasErrors, string.Join("; ", diagnostics.Errors));
        return module;
    }

    private static string Shape(ModuleNode module) =>
        GeneratedProductionPipelinePropertyTests.SemanticTree(module);

    private static (IdKind Kind, string Name, string Id) Identity(IdEntry entry) =>
        (entry.Kind, entry.Name, entry.Id);

    private static string Render(Sample sample)
    {
        string Id(IdKind kind, string shortPrefix, int number)
        {
            var style = (sample.Rotation + number) % 4;
            var suffix = number.ToString("D3", System.Globalization.CultureInfo.InvariantCulture);
            return style switch
            {
                0 => shortPrefix + suffix,
                1 => IdGenerator.GetPrefix(kind) + sample.Payload[..23] + suffix,
                2 => IdGenerator.GetPrefix(kind) + sample.Payload[..9].ToLowerInvariant() + suffix,
                3 => "",
                _ => throw new InvalidOperationException()
            };
        }

        string Header(IdKind kind, string prefix, int number, string rest)
        {
            var id = Id(kind, prefix, number);
            return string.IsNullOrEmpty(id) ? rest : $"{id}:{rest}";
        }

        var source = new StringBuilder();
        source.AppendLine($"§M{{{Header(IdKind.Module, "m", 0, "GeneratedIds")}}}");
        for (var index = 1; index <= sample.FunctionCount; index++)
        {
            source.AppendLine($"  §F{{{Header(IdKind.Function, "f", index, $"Compute{index}:pub")}}} (i32:x) -> i32");
            source.AppendLine($"    §B{{offset:i32}} {sample.Value}");
            source.AppendLine($"    §R (+ x (+ offset {index}))");
        }
        // Class header disambiguation currently only recognizes numeric IDs.
        // Keep the container stable; mixed IDs are exercised on its methods.
        source.AppendLine("  §CL{c020:Calculator:pub}");
        for (var index = 1; index <= sample.MethodCount; index++)
        {
            source.AppendLine($"    §MT{{{Header(IdKind.Method, "mt", 20 + index, $"Apply{index}:pub")}}} (bool:flag) -> bool");
            source.AppendLine($"      §R (or flag BOOL:{(index % 2 == 0 ? "true" : "false")})");
        }
        return source.ToString();
    }
}
