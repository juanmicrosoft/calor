using System.Text;
using Calor.Compiler.Ast;
using Calor.Compiler.Ids;
using Calor.Compiler.Migration;
using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace Calor.Compiler.Tests.PropertyTests;

/// <summary>
/// Parse/emit never serializes declaration IDs. Sidecar synchronization and
/// attachment preserve identity across formatting and fresh parser snapshots.
/// </summary>
public sealed class NodeIdCanonicalizationPropertyTests
{
    public sealed record Sample(int Rotation, int FunctionCount, int MethodCount, int Value, string Payload);

    public static Arbitrary<Sample> Modules() => Arb.From(
        from rotation in Gen.Choose(0, 3)
        from functions in Gen.Choose(4, 7)
        from methods in Gen.Choose(1, 3)
        from value in Gen.Choose(-1000, 1000)
        from payload in Gen.ArrayOf(26, Gen.Elements("0123456789ABCDEFGHJKMNPQRSTVWXYZ".ToCharArray()))
        select new Sample(rotation, functions, methods, value, new string(payload)));

    [Property(MaxTest = 100)]
    public Property SidecarIdentity_IsIdempotentAndPreservesDeclarations() =>
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
    public void Controls_ExerciseSourceOrdersOnModuleFunctionsAndMethods(int rotation) =>
        Check(new Sample(rotation, 4, 3, -17, "01J5X7K9M2NPQRSTABWXYZ12345"));

    private static void Check(Sample sample)
    {
        var root = Path.Combine(Path.GetTempPath(), $"calor-generated-ids-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var file = Path.Combine(root, "generated.calr");
            var source = Render(sample);
            File.WriteAllText(file, source);
            var store = new DeclarationIdentityStore(root);
            store.Sync([file]);
            var before = Parse(source);
            store.Attach(file, source, before);
            var scanner = new IdScanner();
            var original = scanner.Scan(before, file);
            var expectedCount = 2 + sample.FunctionCount + sample.MethodCount;
            Assert.Equal(expectedCount, original.Count);
            Assert.All(original, entry => Assert.Equal(
                IdValidationResult.Valid, IdValidator.Validate(entry.Id, entry.Kind, isTestPath: false)));

            var emitted = new CalorEmitter().Emit(before);
            Assert.All(original, entry => Assert.DoesNotContain(entry.Id, emitted, StringComparison.Ordinal));
            File.WriteAllText(file, emitted);
            store.Sync();
            var canonicalModule = Parse(emitted);
            store.Attach(file, emitted, canonicalModule);
            var canonicalEntries = scanner.Scan(canonicalModule, file);
            Assert.Equal(original.Select(Identity), canonicalEntries.Select(Identity));
            Assert.Equal(expectedCount, canonicalEntries.Select(entry => entry.Id).Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(Shape(before), Shape(canonicalModule));

            var emittedAgain = new CalorEmitter().Emit(canonicalModule);
            Assert.Equal(emitted, emittedAgain);
            var sidecar = File.ReadAllBytes(store.SidecarPath);
            store.Sync();
            Assert.Equal(sidecar, File.ReadAllBytes(store.SidecarPath));
            var fresh = Parse(emittedAgain);
            store.Attach(file, emittedAgain, fresh);
            Assert.Equal(canonicalEntries, new IdScanner().Scan(fresh, file));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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
        var source = new StringBuilder();
        source.AppendLine($"§M{{GeneratedIds{sample.Payload[..4]}R{sample.Rotation}}}");
        foreach (var index in Enumerable.Range(1, sample.FunctionCount)
                     .OrderBy(index => (index + sample.Rotation) % sample.FunctionCount))
        {
            source.AppendLine($"  §F{{Compute{index}:pub}} (i32:x) -> i32");
            source.AppendLine($"    §B{{offset:i32}} {sample.Value}");
            source.AppendLine($"    §R (+ x (+ offset {index}))");
        }
        source.AppendLine("  §CL{Calculator:pub}");
        for (var index = 1; index <= sample.MethodCount; index++)
        {
            source.AppendLine($"    §MT{{Apply{index}:pub}} (bool:flag) -> bool");
            source.AppendLine($"      §R (|| flag BOOL:{(index % 2 == 0 ? "true" : "false")})");
        }
        return source.ToString();
    }
}
