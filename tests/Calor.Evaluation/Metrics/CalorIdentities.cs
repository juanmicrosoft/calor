using System.Collections.Concurrent;
using Calor.Compiler.Ids;

namespace Calor.Evaluation.Metrics;

/// <summary>A declaration and the stable ID the identity sidecar gives it.</summary>
public sealed record CalorDeclarationId(string Id, IdKind Kind, string Name, string Container);

/// <summary>
/// Native Calor keeps declaration IDs in the project sidecar (<c>.calor/ids.json</c>), not in the
/// source. These helpers read IDs through the compiler's own identity store, in a private
/// temporary project, so the metrics see exactly what <c>calor ids sync</c> would record:
/// the IDs an existing sidecar already holds for the source, or fresh IDs when the input has no
/// sidecar. An edit is carried with the store's own reconciliation (exact match, signature
/// edits, moves and unique renames), so "the ID survived" means what it means to the tooling.
/// Loops, branches and local bindings have no IDs in native Calor.
/// </summary>
public static class CalorIdentities
{
    private const string DefaultFile = "input.calr";

    private static readonly ConcurrentDictionary<(string Source, string? Sidecar, string File),
        IReadOnlyList<CalorDeclarationId>> Cache = new();

    public static bool IsCallable(IdKind kind) =>
        kind is IdKind.Function or IdKind.Method or IdKind.Constructor or IdKind.OperatorOverload;

    public static bool IsType(IdKind kind) =>
        kind is IdKind.Class or IdKind.Interface or IdKind.Record or IdKind.Union or IdKind.Enum or IdKind.Delegate;

    public static bool IsMemberData(IdKind kind) =>
        kind is IdKind.Property or IdKind.Field or IdKind.Indexer or IdKind.Event;

    /// <summary>The context's declarations, using its own sidecar when the input has one.</summary>
    public static IReadOnlyList<CalorDeclarationId> Of(Core.EvaluationContext context) =>
        Read(context.CalorSource, context.CalorIdentitySidecar, context.CalorIdentityFile);

    /// <summary>
    /// The declarations of <paramref name="source"/> with their IDs. Returns no declarations
    /// when the source does not parse and bind, since the store cannot assign IDs to it.
    /// </summary>
    public static IReadOnlyList<CalorDeclarationId> Read(string source, string? sidecar = null, string? file = null)
    {
        file ??= DefaultFile;
        return Cache.GetOrAdd((source, sidecar, file), key => InProject(key.Source, key.Sidecar, key.File,
            (store, path) => Project(store.Sync([path]))));
    }

    /// <summary>
    /// The IDs before an edit, and the declarations after it as the store reconciles them.
    /// An ID in both lists survived the edit. When either side does not parse and bind,
    /// the after list is empty.
    /// </summary>
    public static (IReadOnlyList<CalorDeclarationId> Before, IReadOnlyList<CalorDeclarationId> After) Track(
        string before, string after, string? sidecar = null, string? file = null)
    {
        file ??= DefaultFile;
        IReadOnlyList<CalorDeclarationId> beforeIds = [];
        var afterIds = InProject(before, sidecar, file, (store, path) =>
        {
            beforeIds = Project(store.Sync([path]));
            return Project(store.Preview([new DeclarationIdentityStore.SourceChange(path, before, after)]));
        });
        return (beforeIds, afterIds);
    }

    /// <summary>Count of <paramref name="before"/> IDs, filtered by kind, that are still present after.</summary>
    public static int Preserved(IReadOnlyList<CalorDeclarationId> before, IReadOnlyList<CalorDeclarationId> after,
        Func<IdKind, bool>? kind = null)
    {
        var afterIds = after.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
        return before.Count(d => (kind == null || kind(d.Kind)) && afterIds.Contains(d.Id));
    }

    private static IReadOnlyList<CalorDeclarationId> Project(DeclarationIdentityStore.State state) =>
        state.Declarations.Select(d => new CalorDeclarationId(d.Id, d.Kind, d.Name, d.Container)).ToArray();

    private static IReadOnlyList<CalorDeclarationId> InProject(string source, string? sidecar, string file,
        Func<DeclarationIdentityStore, string, IReadOnlyList<CalorDeclarationId>> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "calor-eval-ids-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(root, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, source);
            if (sidecar != null)
            {
                Directory.CreateDirectory(Path.Combine(root, ".calor"));
                File.WriteAllText(Path.Combine(root, DeclarationIdentityStore.RelativePath), sidecar);
            }
            return action(new DeclarationIdentityStore(root), path);
        }
        catch (DeclarationIdentityParseException)
        {
            return [];
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
