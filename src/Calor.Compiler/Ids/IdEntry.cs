using Calor.Compiler.Parsing;
using Calor.Compiler.Ast;
using Calor.Compiler.Analysis;
using Calor.Compiler.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Calor.Compiler.Ids;

/// <summary>
/// Represents an ID entry found in a Calor file.
/// </summary>
/// <param name="Id">The ID string value.</param>
/// <param name="Kind">The declaration kind this ID belongs to.</param>
/// <param name="Name">The name of the declaration (function name, class name, etc.).</param>
/// <param name="Span">The text span where this ID appears.</param>
/// <param name="FilePath">The file path where this ID was found.</param>
public sealed record IdEntry(
    string Id,
    IdKind Kind,
    string Name,
    TextSpan Span,
    string FilePath)
{
    /// <summary>
    /// Returns true if this is a test ID (e.g., f001, m001).
    /// </summary>
    public bool IsTestId => IdValidator.IsTestId(Id);

    /// <summary>
    /// Returns true if this ID is missing or empty.
    /// </summary>
    public bool IsMissing => string.IsNullOrEmpty(Id);
}

/// <summary>A persisted identity, independent of source spelling and source offsets.</summary>
public sealed record DeclarationIdentity(
    string Id, string File, string Container, IdKind Kind, string Name, string Signature)
{
    [JsonIgnore]
    public string Key => JsonSerializer.Serialize(new[] { File, Container, Kind.ToString(), Name, Signature });
}

public sealed record DeclarationRelocation(string Id, string DestinationKey);

public sealed class DeclarationIdentityException(string message, Exception innerException)
    : InvalidOperationException(message, innerException);

public sealed class DeclarationIdentityParseException(string filePath, DiagnosticBag diagnostics)
    : InvalidOperationException($"Cannot synchronize identity for invalid source '{filePath}'.")
{
    public string FilePath { get; } = filePath;
    public DiagnosticBag Diagnostics { get; } = diagnostics;
}

/// <summary>
/// Project identity store. Compilation entry points supply active input files;
/// discovery alone never enrolls historical fixtures in synchronization.
/// </summary>
public sealed class DeclarationIdentityStore
{
    public const int SchemaVersion = 1;
    public const string RelativePath = ".calor/ids.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public sealed record State(int Version, IReadOnlyList<string> Files, IReadOnlyList<DeclarationIdentity> Declarations);
    public sealed record SourceChange(string File, string? Before, string? After);
    public sealed record SidecarEdit(string File, string Before, string After);
    private sealed record Journal(string? BeforeSidecar, string AfterSidecar, IReadOnlyList<SourceChange> Changes);
    private sealed record ParsedDeclaration(DeclarationIdentity Identity, AstNode Node);

    public string Root { get; }
    public string SidecarPath => Path.Combine(Root, ".calor", "ids.json");
    private string JournalPath => Path.Combine(Root, ".calor", "ids.transaction.json");
    private string LockPath => Path.Combine(Root, ".calor", "ids.lock");

    public DeclarationIdentityStore(string root) => Root = Path.GetFullPath(root);

    public static DeclarationIdentityStore? Find(string file)
    {
        for (var directory = Path.GetDirectoryName(Path.GetFullPath(file)); directory != null;
             directory = Path.GetDirectoryName(directory))
        {
            if (IsAmbientRoot(directory)) return null;
            var store = new DeclarationIdentityStore(directory);
            if (File.Exists(store.SidecarPath) || File.Exists(store.JournalPath))
                return store;
        }
        return null;
    }

    internal static DeclarationIdentityStore? FindForActiveInput(string file, string? projectRoot = null)
    {
        var store = Find(file);
        if (store == null) return null;
        if (store.IncludesRegistration(file)) return store;
        if (projectRoot != null)
            return CanonicalPath.IsUnder(CanonicalPath.Resolve(store.Root), CanonicalPath.Resolve(projectRoot)) ? store : null;

        // An inferred input directory is not an ownership boundary. A nearer
        // actual project is. Find already excludes ambient ownership boundaries.
        for (var directory = Path.GetDirectoryName(Path.GetFullPath(file)); directory != null && directory != store.Root;
             directory = Path.GetDirectoryName(directory))
        {
            if (File.Exists(Path.Combine(directory, ".git")) || Directory.Exists(Path.Combine(directory, ".git"))
                || File.Exists(Path.Combine(directory, "Directory.Build.props"))
                || File.Exists(Path.Combine(directory, "global.json"))
                || Directory.EnumerateFiles(directory, "*.csproj", SearchOption.TopDirectoryOnly).Any())
                return null;
        }
        return store;
    }

    internal static bool IsAmbientRoot(string directory)
    {
        var canonical = CanonicalPath.Resolve(directory);
        return canonical == Path.GetPathRoot(canonical)
            || canonical == CanonicalPath.Resolve(Path.GetTempPath())
            || canonical == CanonicalPath.Resolve(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }

    public static void AttachIfRegistered(string? file, string source, ModuleNode module,
        DiagnosticBag? partialDiagnostics = null)
    {
        if (file == null || !file.EndsWith(".calr", StringComparison.OrdinalIgnoreCase) || !File.Exists(file)) return;
        file = Path.GetFullPath(file);
        try
        {
            var store = Find(file);
            if (store != null && store.IncludesRegistration(file)) store.Attach(file, source, module, partialDiagnostics);
        }
        catch (DeclarationIdentityParseException) { throw; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or JsonException or UnauthorizedAccessException)
        {
            throw new DeclarationIdentityException($"Declaration identity unavailable: {ex.Message}", ex);
        }
    }

    public static void SyncIfRegistered(string? file)
    {
        if (file == null || !file.EndsWith(".calr", StringComparison.OrdinalIgnoreCase) || !Path.IsPathFullyQualified(file)) return;
        var store = Find(file);
        if (store == null || !store.IncludesRegistration(file)) return;
        store.Recover();
        if (store.IsRegistered(file)) store.Sync();
    }

    public static void SyncActiveFileInExistingProject(string file)
    {
        if (!file.EndsWith(".calr", StringComparison.OrdinalIgnoreCase)) return;
        var store = FindForActiveInput(file);
        if (store == null) return;
        store.Recover();
        store.Sync([file]);
    }

    /// <summary>
    /// The caller classifies these as active compilation inputs. This never
    /// discovers .calr files and must not be passed historical replay subjects.
    /// </summary>
    public static void SyncActiveInputs(IEnumerable<string> activeFiles, string? projectRoot = null)
    {
        var files = activeFiles.Select(Path.GetFullPath).Distinct(StringComparer.Ordinal).ToArray();
        if (files.Length == 0) return;
        var common = projectRoot == null
            ? Incremental.BuildStateCache.ComputeCommonDirectory(files.Select(file => new FileInfo(file)).ToArray())
            : Path.GetFullPath(projectRoot);
        string RootFor(string file)
        {
            return FindForActiveInput(file, projectRoot)?.Root ?? common;
        }
        foreach (var group in files.GroupBy(RootFor, StringComparer.Ordinal))
        {
            if (!IsAmbientRoot(group.Key))
                new DeclarationIdentityStore(group.Key).Sync(group);
        }
    }

    public static void CopyToSnapshot(IReadOnlyDictionary<string, string> originalsToCopies)
    {
        if (originalsToCopies.Count == 0) return;
        var root = Incremental.BuildStateCache.ComputeCommonDirectory(
            originalsToCopies.Values.Select(file => new FileInfo(file)).ToArray());
        var snapshot = new DeclarationIdentityStore(root);
        var declarations = new List<DeclarationIdentity>();
        foreach (var group in originalsToCopies.GroupBy(pair => Find(pair.Key)?.Root
                     ?? throw new InvalidOperationException($"Active source has no identity store: {pair.Key}"), StringComparer.Ordinal))
        {
            var store = new DeclarationIdentityStore(group.Key);
            using var gate = store.Acquire();
            if (File.Exists(store.JournalPath)) throw new IOException("Cannot snapshot an incomplete identity transaction.");
            var state = store.Read();
            foreach (var (original, copy) in group)
            {
                var relative = store.Relative(original);
                declarations.AddRange(state.Declarations.Where(d => d.File == relative)
                    .Select(d => d with { File = snapshot.Relative(copy) }));
            }
        }
        var next = new State(SchemaVersion, originalsToCopies.Values.Select(snapshot.Relative).Order(StringComparer.Ordinal).ToArray(),
            declarations.OrderBy(d => d.Key, StringComparer.Ordinal).ToArray());
        using var snapshotGate = snapshot.Acquire();
        snapshot.ValidateState(next);
        snapshot.Commit(next, [], File.Exists(snapshot.SidecarPath) ? File.ReadAllText(snapshot.SidecarPath) : null);
    }

    public bool IsRegistered(string file)
    {
        using var gate = Acquire();
        if (File.Exists(JournalPath))
            throw new IOException("Incomplete identity transaction; run calor ids sync to recover.");
        return Read().Files.Contains(Relative(file), StringComparer.Ordinal);
    }

    /// <summary>Validated current and pending registrations, without replaying a transaction.</summary>
    public IReadOnlyList<string> ReadRegisteredInputs()
    {
        using var gate = Acquire();
        var files = Read(resolveMerge: true).Files;
        var pending = File.Exists(JournalPath) ? ReadTransaction().Next.Files : [];
        return files.Concat(pending).Select(ValidatePath).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
    }

    private bool IncludesRegistration(string file) =>
        ReadRegisteredInputs().Contains(ValidatePath(Relative(file)), StringComparer.Ordinal);

    private string Relative(string file)
    {
        var relative = Path.GetRelativePath(Root, Path.GetFullPath(file)).Replace('\\', '/');
        if (relative == ".." || relative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relative)
            || !relative.EndsWith(".calr", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Identity source must be a .calr file under '{Root}': {file}");
        ValidatePath(relative);
        return relative;
    }

    private string ValidatePath(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Contains('\\') || Path.IsPathRooted(relative)
            || relative.Split('/').Any(part => part is "" or "." or ".."))
            throw new InvalidDataException($"Invalid identity path: '{relative}'.");
        var full = Path.GetFullPath(Path.Combine(Root, relative));
        for (var part = full; part != Root; part = Path.GetDirectoryName(part)!)
        {
            if (part == null || new FileInfo(part).LinkTarget != null || new DirectoryInfo(part).LinkTarget != null)
                throw new InvalidDataException($"Identity transactions do not follow symbolic links: '{relative}'.");
        }
        return full;
    }

    private FileStream Acquire()
    {
        if (new DirectoryInfo(Root).LinkTarget != null)
            throw new InvalidDataException("Identity project root cannot be a symbolic link.");
        Directory.CreateDirectory(Path.Combine(Root, ".calor"));
        ValidatePath(".calor/ids.lock");
        ValidatePath(".calor/ids.json");
        ValidatePath(".calor/ids.transaction.json");
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            try { return new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException ex) when ((ex.HResult & 0xFFFF) is 11 or 32 or 33
                && timer.Elapsed < TimeSpan.FromSeconds(5))
            {
                Thread.Sleep(10);
            }
        }
    }

    private State Read(bool resolveMerge = false)
    {
        if (!File.Exists(SidecarPath))
            return new State(SchemaVersion, [], []);
        var text = File.ReadAllText(SidecarPath);
        State state;
        if (resolveMerge && text.Split('\n').Any(line => line.StartsWith("<<<<<<<", StringComparison.Ordinal)))
        {
            var (left, right) = ConflictVersions(text);
            var ours = CanonicalizeState(JsonSerializer.Deserialize<State>(left, JsonOptions)
                ?? throw new InvalidDataException("Empty identity merge side."));
            var theirs = CanonicalizeState(JsonSerializer.Deserialize<State>(right, JsonOptions)
                ?? throw new InvalidDataException("Empty identity merge side."));
            var declarations = ours.Declarations.Concat(theirs.Declarations).Distinct().ToArray();
            if (declarations.Select(d => d.Id).Distinct(StringComparer.Ordinal).Count() != declarations.Length)
                throw new InvalidDataException("Concurrent edits to the same declaration identity require an explicit relocation; sidecar left untouched.");
            state = new State(SchemaVersion, ours.Files.Concat(theirs.Files).Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal).ToArray(), declarations);
        }
        else
            state = JsonSerializer.Deserialize<State>(text, JsonOptions) ?? throw new InvalidDataException("Empty identity sidecar.");
        return CanonicalizeState(state);
    }

    private State CanonicalizeState(State state)
    {
        ValidateState(state);
        state = state with { Declarations = state.Declarations.Select(CanonicalizeIdentity).ToArray() };
        ValidateState(state);
        return state;
    }

    private static DeclarationIdentity CanonicalizeIdentity(DeclarationIdentity identity) =>
        identity with
        {
            Container = CanonicalizeContainer(identity.Container),
            Signature = identity.Kind switch
            {
                IdKind.Function or IdKind.Method or IdKind.Constructor or IdKind.OperatorOverload
                    or IdKind.Indexer or IdKind.Delegate => CanonicalizeCallableSignature(identity.Signature),
                IdKind.Field or IdKind.Property or IdKind.Event or IdKind.RefinementType or IdKind.IndexedType =>
                    CanonicalIdentityType(identity.Signature),
                _ => identity.Signature
            }
        };

    // Keep sidecar types in one internal spelling, independent of source formatting.
    private static string CanonicalIdentityType(string type) =>
        Migration.TypeMapper.CSharpToCalor(Migration.TypeMapper.CalorToCSharp(type));

    private static string CanonicalizeCallableSignature(string signature)
    {
        if (System.Text.Json.Nodes.JsonNode.Parse(signature) is not System.Text.Json.Nodes.JsonObject json
            || json["parameters"] is not System.Text.Json.Nodes.JsonArray parameters
            || json["output"] is not System.Text.Json.Nodes.JsonValue output
            || !output.TryGetValue<string>(out var outputType) || outputType == null)
            throw new InvalidDataException("Invalid callable identity signature.");
        foreach (var parameter in parameters)
        {
            if (parameter is not System.Text.Json.Nodes.JsonObject item
                || item["type"] is not System.Text.Json.Nodes.JsonValue type
                || !type.TryGetValue<string>(out var typeName) || typeName == null)
                throw new InvalidDataException("Invalid callable identity parameter type.");
            item["type"] = CanonicalIdentityType(typeName);
        }
        json["output"] = CanonicalIdentityType(outputType);
        return json.ToJsonString();
    }

    private static string CanonicalizeContainer(string container)
    {
        var result = new System.Text.StringBuilder();
        var offset = 0;
        // Local declarations embed their enclosing callable's serialized signature.
        while (container.IndexOf("#{\"arity\":", offset, StringComparison.Ordinal) is var index && index >= 0)
        {
            var reader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(container[(index + 1)..]));
            using var document = JsonDocument.ParseValue(ref reader);
            var signature = document.RootElement.GetRawText();
            result.Append(container.AsSpan(offset, index + 1 - offset));
            result.Append(CanonicalizeCallableSignature(signature));
            offset = index + 1 + signature.Length;
        }
        result.Append(container.AsSpan(offset));
        return result.ToString();
    }

    private static (string Left, string Right) ConflictVersions(string text)
    {
        var left = new System.Text.StringBuilder();
        var right = new System.Text.StringBuilder();
        var section = 0;
        foreach (var line in text.Split('\n'))
        {
            if (line.StartsWith("<<<<<<<", StringComparison.Ordinal) && section == 0) section = 1;
            else if (line.StartsWith("|||||||", StringComparison.Ordinal) && section == 1) section = 3;
            else if (line == "=======" && section is 1 or 3) section = 2;
            else if (line.StartsWith(">>>>>>>", StringComparison.Ordinal) && section == 2) section = 0;
            else if (line.StartsWith("<<<<<<<", StringComparison.Ordinal) || line.StartsWith(">>>>>>>", StringComparison.Ordinal)
                || line == "=======")
                throw new InvalidDataException("Malformed identity merge conflict.");
            else
            {
                if (section is 0 or 1) left.AppendLine(line);
                if (section is 0 or 2) right.AppendLine(line);
            }
        }
        if (section != 0) throw new InvalidDataException("Unterminated identity merge conflict.");
        return (left.ToString(), right.ToString());
    }

    private void ValidateState(State state)
    {
        if (state.Version != SchemaVersion || state.Files == null || state.Declarations == null)
            throw new InvalidDataException("Unsupported or incomplete identity sidecar.");
        foreach (var file in state.Files)
        {
            ValidatePath(file);
            if (!file.EndsWith(".calr", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Not a Calor identity source: {file}");
        }
        if (state.Declarations.Any(d => d == null))
            throw new InvalidDataException("Null identity declaration.");
        if (state.Files.Distinct(StringComparer.Ordinal).Count() != state.Files.Count
            || state.Declarations.Select(d => d.Id).Distinct(StringComparer.Ordinal).Count() != state.Declarations.Count
            || state.Declarations.Select(d => d.Key).Distinct(StringComparer.Ordinal).Count() != state.Declarations.Count
            || state.Declarations.Any(d => !state.Files.Contains(d.File, StringComparer.Ordinal)
                || !IdValidator.IsCanonicalId(d.Id) || !Enum.IsDefined(d.Kind)
                || IdGenerator.GetKindFromId(d.Id) != d.Kind
                || d.Container == null || string.IsNullOrEmpty(d.Name) || d.Signature == null))
            throw new InvalidDataException("Identity sidecar contains duplicate or invalid declarations. Resolve conflicts with ids sync, not by choosing an ID.");
    }

    /// <summary>
    /// Registers explicit inputs and reconciles the complete registered set.
    /// No source bytes are changed. A failed parse or ambiguous match writes nothing.
    /// </summary>
    public State Sync(IEnumerable<string>? register = null)
    {
        using var gate = Acquire();
        RecoverCore();
        var beforeSidecar = File.Exists(SidecarPath) ? File.ReadAllText(SidecarPath) : null;
        var previous = Read(resolveMerge: true);
        var registered = (register ?? []).Select(Relative).ToArray();
        foreach (var file in registered)
            if (!previous.Files.Contains(file, StringComparer.Ordinal) && !File.Exists(ValidatePath(file)))
                throw new FileNotFoundException($"Active identity source not found: {file}");
        var files = previous.Files.Concat(registered)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var expected = files.ToDictionary(file => file, file => File.Exists(ValidatePath(file))
            ? File.ReadAllText(ValidatePath(file)) : null, StringComparer.Ordinal);
        var sources = expected.Where(pair => pair.Value != null)
            .ToDictionary(pair => pair.Key, pair => pair.Value!, StringComparer.Ordinal);
        var next = Reconcile(previous, sources, []);
        ValidateSnapshot(expected);
        Commit(next, [], beforeSidecar);
        return next;
    }

    /// <summary>
    /// Applies a source edit set and its identity update under one recoverable journal.
    /// Explicit relocations address IDs, never source text, and can cross files/containers.
    /// </summary>
    public State Apply(IReadOnlyList<SourceChange> changes, IReadOnlyList<DeclarationRelocation>? relocations = null,
        string? expectedSidecar = null)
        => Update(changes, relocations, persist: true, expectedSidecar);

    public State Preview(IReadOnlyList<SourceChange> changes, IReadOnlyList<DeclarationRelocation>? relocations = null,
        string? expectedSidecar = null)
        => Update(changes, relocations, persist: false, expectedSidecar);

    public SidecarEdit PreviewSidecar(IReadOnlyList<SourceChange> changes, IReadOnlyList<DeclarationRelocation>? relocations = null)
    {
        using var gate = Acquire();
        var before = File.ReadAllText(SidecarPath);
        var next = UpdateCore(changes, relocations, persist: false);
        if (File.ReadAllText(SidecarPath) != before)
            throw new IOException("Sidecar changed during edit preparation.");
        return new(SidecarPath, before, JsonSerializer.Serialize(next, JsonOptions));
    }

    private State Update(IReadOnlyList<SourceChange> changes, IReadOnlyList<DeclarationRelocation>? relocations, bool persist,
        string? expectedSidecar)
    {
        using var gate = Acquire();
        if (expectedSidecar != null && (File.Exists(JournalPath) || !File.Exists(SidecarPath)
            || File.ReadAllText(SidecarPath) != expectedSidecar))
            throw new IOException("Sidecar changed concurrently; prepare the operation again.");
        return UpdateCore(changes, relocations, persist);
    }

    private State UpdateCore(IReadOnlyList<SourceChange> changes, IReadOnlyList<DeclarationRelocation>? relocations, bool persist)
    {
        if (persist) RecoverCore();
        else if (File.Exists(JournalPath)) throw new IOException("Incomplete identity transaction; recover before previewing.");
        var beforeSidecar = File.Exists(SidecarPath) ? File.ReadAllText(SidecarPath) : null;
        var previous = Read();
        if (!File.Exists(SidecarPath))
            throw new InvalidOperationException("Run calor ids sync to register this project first.");
        var normalized = changes.Select(c => c with { File = Relative(c.File) }).ToArray();
        if (normalized.Select(c => c.File).Distinct(StringComparer.Ordinal).Count() != normalized.Length)
            throw new InvalidOperationException("Duplicate source change.");
        foreach (var change in normalized)
        {
            var path = ValidatePath(change.File);
            var current = File.Exists(path) ? File.ReadAllText(path) : null;
            if (current != change.Before)
                throw new IOException($"Source changed concurrently: {change.File}");
        }
        var expected = previous.Files.ToDictionary(file => file, file => File.Exists(ValidatePath(file))
            ? File.ReadAllText(ValidatePath(file)) : null, StringComparer.Ordinal);
        foreach (var change in normalized)
            expected[change.File] = change.Before;
        var sources = expected.Where(pair => pair.Value != null)
            .ToDictionary(pair => pair.Key, pair => pair.Value!, StringComparer.Ordinal);
        foreach (var change in normalized)
        {
            if (change.After == null) sources.Remove(change.File);
            else sources[change.File] = change.After;
        }
        var next = Reconcile(previous, sources, relocations ?? []);
        ValidateSnapshot(expected);
        if (persist) Commit(next, normalized, beforeSidecar);
        return next;
    }

    private void ValidateSnapshot(IReadOnlyDictionary<string, string?> expected)
    {
        foreach (var (file, before) in expected)
        {
            var path = ValidatePath(file);
            if ((File.Exists(path) ? File.ReadAllText(path) : null) != before)
                throw new IOException($"Source changed concurrently: {file}");
        }
    }

    public void Recover()
    {
        using var gate = Acquire();
        RecoverCore();
    }

    private void RecoverCore()
    {
        if (!File.Exists(JournalPath)) return;
        var (journal, _) = ReadTransaction();
        if (journal.Changes.Select(c => c.File).Distinct(StringComparer.Ordinal).Count() != journal.Changes.Count)
            throw new InvalidDataException("Duplicate source change in identity transaction.");
        var sidecar = File.Exists(SidecarPath) ? File.ReadAllText(SidecarPath) : null;
        if (sidecar != journal.BeforeSidecar && sidecar != journal.AfterSidecar)
            throw new IOException("Identity recovery blocked by an external sidecar edit; transaction retained.");
        // Preflight every artifact before writing any: never clobber an external edit.
        foreach (var change in journal.Changes)
        {
            if (!change.File.EndsWith(".calr", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Identity transaction can only change .calr sources.");
            var path = ValidatePath(change.File);
            var current = File.Exists(path) ? File.ReadAllText(path) : null;
            if (current != change.Before && current != change.After)
                throw new IOException($"Identity recovery blocked by an external edit to '{change.File}'; transaction retained.");
        }
        foreach (var change in journal.Changes)
            Write(ValidatePath(change.File), change.After);
        Write(SidecarPath, journal.AfterSidecar);
        File.Delete(JournalPath);
    }

    private (Journal Journal, State Next) ReadTransaction()
    {
        var journal = JsonSerializer.Deserialize<Journal>(File.ReadAllText(JournalPath), JsonOptions)
            ?? throw new InvalidDataException("Empty identity transaction.");
        var next = JsonSerializer.Deserialize<State>(journal.AfterSidecar, JsonOptions)
            ?? throw new InvalidDataException("Empty transaction sidecar.");
        ValidateState(next);
        return (journal, next);
    }

    private void Commit(State next, IReadOnlyList<SourceChange> changes, string? before)
    {
        ValidateState(next);
        if ((File.Exists(SidecarPath) ? File.ReadAllText(SidecarPath) : null) != before)
            throw new IOException("Sidecar changed concurrently; identity update refused.");
        var after = JsonSerializer.Serialize(next, JsonOptions);
        if (changes.Count == 0 && before == after) return;
        Write(JournalPath, JsonSerializer.Serialize(new Journal(before, after, changes), JsonOptions));
        RecoverCore();
    }

    private static void Write(string path, string? content)
    {
        if (content == null) { File.Delete(path); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        UnixFileMode? mode = !OperatingSystem.IsWindows() && File.Exists(path) ? File.GetUnixFileMode(path) : null;
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false), leaveOpen: true);
                writer.Write(content);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            if (!OperatingSystem.IsWindows() && mode.HasValue)
                File.SetUnixFileMode(temp, mode.Value);
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private State Reconcile(State previous, IReadOnlyDictionary<string, string> sources,
        IReadOnlyList<DeclarationRelocation> relocations)
    {
        var current = sources.OrderBy(p => p.Key, StringComparer.Ordinal)
            .SelectMany(p => ParseDeclarations(p.Key, p.Value, ValidatePath(p.Key)))
            .GroupBy(d => d.Identity.Key, StringComparer.Ordinal).Select(group =>
            {
                var parts = group.ToArray();
                if (parts.Length == 1) return parts[0].Identity;
                if (!IsPartialDeclaration(parts))
                    throw new InvalidOperationException("Ambiguous declaration keys; identity sync cannot distinguish these declarations.");
                // A logical partial declaration has several legacy source IDs,
                // not one source identity that tooling can import.
                return parts[0].Identity with { Id = "" };
            }).ToArray();
        var remainingOld = previous.Declarations.ToDictionary(d => d.Id, StringComparer.Ordinal);
        var remainingNew = current.ToDictionary(d => d.Key, StringComparer.Ordinal);
        var result = new List<DeclarationIdentity>();
        var containerMoves = new Dictionary<string, string>(StringComparer.Ordinal);
        static string? Scope(DeclarationIdentity declaration) => declaration.Kind switch
        {
            IdKind.Module when declaration.Signature.Length == 0 => declaration.Name,
            IdKind.Module or IdKind.Class or IdKind.Interface => declaration.Container + "." + declaration.Name + "`" + declaration.Signature,
            IdKind.EnumExtension => declaration.Container + "." + declaration.Name + "Extensions",
            IdKind.Function or IdKind.Method or IdKind.OperatorOverload => declaration.Container + "." + declaration.Name + "#" + declaration.Signature,
            IdKind.Property or IdKind.Event or IdKind.Record or IdKind.Union or IdKind.Variant => declaration.Container + "." + declaration.Name,
            IdKind.Constructor => declaration.Container + ".ctor#" + declaration.Signature,
            _ => null
        };
        string TranslateContainer(string container)
        {
            var move = containerMoves.Where(pair => container == pair.Key || container.StartsWith(pair.Key + ".", StringComparison.Ordinal))
                .OrderByDescending(pair => pair.Key.Length).FirstOrDefault();
            return move.Key == null ? container : move.Value + container[move.Key.Length..];
        }
        bool SameScopeOrFile(DeclarationIdentity old, DeclarationIdentity added) =>
            TranslateContainer(old.Container) == added.Container
            || (old.File == added.File && !old.Container.Contains(".#if", StringComparison.Ordinal)
                && !added.Container.Contains(".#if", StringComparison.Ordinal));
        void Match(DeclarationIdentity old, DeclarationIdentity added)
        {
            if (!remainingOld.Remove(old.Id) || !remainingNew.Remove(added.Key))
                throw new InvalidOperationException("Identity mapped more than once.");
            result.Add(added with { Id = old.Id });
            if (Scope(old) is { } oldScope && Scope(added) is { } newScope && oldScope != newScope)
            {
                if (containerMoves.TryGetValue(oldScope, out var existing) && existing != newScope)
                    throw new InvalidOperationException($"Ambiguous container relocation: {oldScope}");
                containerMoves[oldScope] = newScope;
            }
        }
        foreach (var relocation in relocations)
        {
            if (!remainingOld.TryGetValue(relocation.Id, out var old)
                || !remainingNew.TryGetValue(relocation.DestinationKey, out var added)
                || old.Kind != added.Kind)
                throw new InvalidOperationException($"Invalid explicit identity relocation: {relocation.Id}");
            Match(old, added);
        }
        foreach (var old in remainingOld.Values.ToArray())
            if (remainingNew.TryGetValue(old.Key, out var exact)) Match(old, exact);

        void MatchSignatureEdits()
        {
            // Unchanged overloads are matched first. A remaining signature edit
            // must have a unique match in the translated declaration scope.
            var signatures = remainingOld.Values.ToDictionary(old => old.Id, old => remainingNew.Values
                .Where(added => old.Kind == added.Kind && old.Name == added.Name && old.File == added.File
                    && TranslateContainer(old.Container) == added.Container).ToArray(), StringComparer.Ordinal);
            foreach (var old in remainingOld.Values.ToArray())
            {
                var matches = signatures[old.Id];
                if (matches.Length > 1 || (matches.Length == 1 && signatures.Values.Count(set => set.Any(d => d.Key == matches[0].Key)) > 1))
                    throw new InvalidOperationException($"Ambiguous signature edit for '{old.Name}'; use an explicit tooling relocation.");
                if (matches.Length == 1) Match(old, matches[0]);
            }
        }

        var moved = remainingOld.Values.ToDictionary(old => old.Id, old => remainingNew.Values
            .Where(added => old.Kind == added.Kind && old.Signature == added.Signature
                && TranslateContainer(old.Container) == added.Container && old.Name == added.Name).ToArray(), StringComparer.Ordinal);
        foreach (var old in remainingOld.Values.ToArray())
        {
            var matches = moved[old.Id];
            if (matches.Length == 1 && moved.Values.Count(set => set.Any(d => d.Key == matches[0].Key)) == 1)
                Match(old, matches[0]);
        }
        MatchSignatureEdits();

        bool mappedContainer;
        do
        {
            mappedContainer = false;
            var exactScopeAvailable = remainingOld.Values.Where(old => Scope(old) != null).Any(old =>
                remainingNew.Values.Any(added => old.Kind == added.Kind && old.Signature == added.Signature
                    && TranslateContainer(old.Container) == added.Container));
            var containers = remainingOld.Values.Where(old => Scope(old) != null).ToDictionary(old => old.Id, old => remainingNew.Values
                .Where(added => old.Kind == added.Kind && old.Signature == added.Signature
                    && (exactScopeAvailable ? TranslateContainer(old.Container) == added.Container
                        : SameScopeOrFile(old, added))).ToArray(), StringComparer.Ordinal);
            foreach (var old in remainingOld.Values.Where(old => Scope(old) != null).ToArray())
            {
                var matches = containers[old.Id];
                if (matches.Length != 1 || containers.Values.Count(set => set.Any(d => d.Key == matches[0].Key)) != 1) continue;
                Match(old, matches[0]);
                mappedContainer = true;
            }
            if (!mappedContainer) break;
            var members = remainingOld.Values.ToDictionary(old => old.Id, old => remainingNew.Values
                .Where(added => old.Kind == added.Kind && old.Name == added.Name && old.Signature == added.Signature
                    && TranslateContainer(old.Container) == added.Container).ToArray(), StringComparer.Ordinal);
            foreach (var old in remainingOld.Values.ToArray())
            {
                var matches = members[old.Id];
                if (matches.Length == 1 && members.Values.Count(set => set.Any(d => d.Key == matches[0].Key)) == 1)
                    Match(old, matches[0]);
            }
            MatchSignatureEdits();
        } while (mappedContainer);

        // Match an entire bipartite graph, not a greedy sequence: both ends must
        // have degree one. Otherwise two renames could silently swap identities.
        var candidates = remainingOld.Values.ToDictionary(old => old.Id, old => remainingNew.Values
            .Where(added => old.Kind == added.Kind && old.Signature == added.Signature
                && SameScopeOrFile(old, added)).ToArray(), StringComparer.Ordinal);
        foreach (var old in remainingOld.Values.ToArray())
        {
            var matches = candidates[old.Id];
            if (matches.Length == 0) continue;
            if (matches.Length != 1 || candidates.Values.Count(set => set.Any(d => d.Key == matches[0].Key)) != 1)
                throw new InvalidOperationException($"Ambiguous declaration identity for '{old.Container}.{old.Name}' ({old.Id}); use an explicit tooling relocation.");
            Match(old, matches[0]);
        }
        var used = previous.Declarations.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var added in remainingNew.Values)
        {
            string id;
            do { id = CompactIdGenerator.Generate(added.Kind); } while (!used.Add(id));
            result.Add(added with { Id = id });
        }
        return new State(SchemaVersion, sources.Keys.Order(StringComparer.Ordinal).ToArray(),
            result.OrderBy(d => d.Key, StringComparer.Ordinal).ToArray());
    }

    /// <summary>Read-only projection for unsaved buffers and all identity consumers.</summary>
    public void Attach(string file, string source, ModuleNode module, DiagnosticBag? partialDiagnostics = null)
    {
        using var gate = Acquire();
        if (File.Exists(JournalPath))
            throw new IOException("Incomplete identity transaction; run calor ids sync to recover.");
        var state = Read();
        var relative = Relative(file);
        if (!state.Files.Contains(relative, StringComparer.Ordinal)) return;
        var parsed = Describe(relative, module);
        if (partialDiagnostics != null)
        {
            if (parsed.GroupBy(declaration => declaration.Identity.Key, StringComparer.Ordinal)
                .Any(group => group.Count() > 1 && !IsPartialDeclaration(group.ToArray())))
                throw new InvalidDataException("Partial source has ambiguous declaration headers; identity cannot be attached.");
            var existing = state.Declarations.ToDictionary(declaration => declaration.Key, StringComparer.Ordinal);
            foreach (var declaration in parsed)
            {
                var headerEnd = declaration.Node switch
                {
                    FunctionNode function => function.Body.FirstOrDefault()?.Span.Start ?? function.Span.End,
                    MethodNode method => method.Body.FirstOrDefault()?.Span.Start ?? method.Span.End,
                    _ => declaration.Node.Span.End
                };
                if (partialDiagnostics.Errors.Any(error => error.Span.Start >= declaration.Node.Span.Start
                    && error.Span.Start <= headerEnd))
                    continue;
                if (existing.TryGetValue(declaration.Identity.Key, out var identity))
                    declaration.Node.ToolingId = identity.Id;
            }
            return;
        }
        var sources = state.Files.Where(f => File.Exists(ValidatePath(f)))
            .ToDictionary(f => f, f => f == relative ? source : File.ReadAllText(ValidatePath(f)), StringComparer.Ordinal);
        sources[relative] = source;
        var projected = Reconcile(state, sources, []);
        var identities = projected.Declarations.ToDictionary(d => d.Key, d => d.Id, StringComparer.Ordinal);
        foreach (var declaration in parsed)
        {
            var id = identities[declaration.Identity.Key];
            // Unsaved additions are not persisted identities. Never present a
            // newly randomized projection as stable across independent reads.
            if (state.Declarations.Any(d => d.Id == id))
                declaration.Node.ToolingId = id;
        }
    }

    private static bool IsPartialDeclaration(IReadOnlyList<ParsedDeclaration> parts) =>
        parts.All(d => d.Node is ClassDefinitionNode { IsPartial: true })
        || (parts.Count == 2 && parts.All(d => d.Node is MethodNode { IsPartial: true })
            && parts.Count(d => ((MethodNode)d.Node).Body.Count == 0) == 1);

    private static IReadOnlyList<ParsedDeclaration> ParseDeclarations(string file, string source, string? diagnosticPath = null)
    {
        var diagnostics = new DiagnosticBag();
        diagnostics.SetFilePath(diagnosticPath ?? file);
        var tokens = new Lexer(source, diagnostics).TokenizeAllForParser();
        if (diagnostics.HasErrors)
            throw new DeclarationIdentityParseException(diagnosticPath ?? file, diagnostics);
        var module = new Parser(tokens, diagnostics).Parse();
        if (diagnostics.HasErrors)
            throw new DeclarationIdentityParseException(diagnosticPath ?? file, diagnostics);
        var declarations = Describe(file, module);
        if (declarations.GroupBy(d => d.Identity.Key, StringComparer.Ordinal)
            .Any(group => group.Count() > 1 && !IsPartialDeclaration(group.ToArray())))
        {
            new Binding.Binder(diagnostics, diagnosticPath ?? file).Bind(module);
            if (diagnostics.HasErrors)
                throw new DeclarationIdentityParseException(diagnosticPath ?? file, diagnostics);
        }
        return declarations;
    }

    public static IReadOnlyList<DeclarationIdentity> Describe(string file, string source) =>
        ParseDeclarations(file, source).Select(d => d.Identity).ToArray();

    public static DeclarationIdentity Locate(string file, string source, int line, int column)
    {
        var lines = source.Split('\n');
        if (line < 1 || line > lines.Length || column < 1 || column > lines[line - 1].Length + 1)
            throw new InvalidOperationException("Declaration location is outside the source.");
        var offset = lines.Take(line - 1).Sum(text => text.Length + 1) + column - 1;
        var candidates = ParseDeclarations(file, source).Where(d => d.Node.Span.Start <= offset && offset < d.Node.Span.End)
            .OrderBy(d => d.Node.Span.Length).ToArray();
        if (candidates.Length == 0 || (candidates.Length > 1 && candidates[0].Node.Span == candidates[1].Node.Span))
            throw new InvalidOperationException("Declaration location is missing or ambiguous.");
        return candidates[0].Identity;
    }

    internal static IEnumerable<string> ToolingIds(ModuleNode module) =>
        Describe("", module).Select(d => d.Node.ToolingId).OfType<string>();

    internal static IEnumerable<IdEntry> ScanDeclarations(ModuleNode module, string file) =>
        Describe(file, module)
            .Where(d => d.Node.ToolingId != null || d.Identity.Kind is not (IdKind.Field or IdKind.Variant))
            .GroupBy(d => d.Node.ToolingId)
            .SelectMany(group => group.Key == null ? group : group.Take(1))
            .Select(d => new IdEntry(d.Node.ToolingId ?? d.Identity.Id, d.Identity.Kind,
                d.Identity.Name, d.Node.Span, file));

    private static IReadOnlyList<ParsedDeclaration> Describe(string file, ModuleNode module)
    {
        var result = new List<ParsedDeclaration>();
        void Add(AstNode node, string container, IdKind kind, string name, string signature)
        {
            var sourceId = node switch
            {
                ModuleNode n => n.LocalId,
                FunctionNode n => n.LocalId,
                TypeDefinitionNode n => n.LocalId,
                MethodNode n => n.LocalId,
                MethodSignatureNode n => n.LocalId,
                PropertyNode n => n.LocalId,
                IndexerNode n => n.LocalId,
                ConstructorNode n => n.LocalId,
                OperatorOverloadNode n => n.LocalId,
                EnumExtensionNode n => n.LocalId,
                RefinementTypeNode n => n.LocalId,
                IndexedTypeNode n => n.LocalId,
                EventDefinitionNode n => n.LocalId,
                ProofObligationNode n => n.LocalId,
                _ => ""
            };
            result.Add(new ParsedDeclaration(CanonicalizeIdentity(
                new DeclarationIdentity(sourceId, file, container, kind, name, signature)), node));
        }
        static string Signature(IReadOnlyList<ParameterNode> parameters, OutputNode? output, int arity = 0) =>
            JsonSerializer.Serialize(new
            {
                arity,
                parameters = parameters.Select(p => new { type = p.TypeName, modifier = p.Modifier.ToString() }),
                output = output?.TypeName ?? "void"
            });
        void Members(IEnumerable<AstNode> nodes, string container, bool topLevel = false, string condition = "")
        {
            foreach (var node in nodes)
            {
                var scope = (topLevel && node.NamespaceIdentity is { } ns ? ns : container) + condition;
                switch (node)
                {
                    case FunctionNode f:
                        Add(f, scope, IdKind.Function, f.Name, Signature(f.Parameters, f.Output, f.TypeParameters.Count));
                        Members(f.Body, scope + "." + f.Name + "#" + Signature(f.Parameters, f.Output, f.TypeParameters.Count));
                        break;
                    case ClassDefinitionNode c:
                        Add(c, scope, c.IsModule ? IdKind.Module : IdKind.Class, c.Name,
                            c.TypeParameters.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        Members(c.Methods.Cast<AstNode>().Concat(c.Properties).Concat(c.Indexers).Concat(c.Constructors)
                            .Concat(c.OperatorOverloads).Concat(c.NestedClasses).Concat(c.NestedInterfaces).Concat(c.NestedEnums)
                            .Concat(c.NestedDelegates).Concat(c.Events).Concat(c.Fields),
                            scope + "." + c.Name + "`" + c.TypeParameters.Count);
                        Members(c.PreprocessorBlocks, scope + "." + c.Name + "`" + c.TypeParameters.Count);
                        break;
                    case InterfaceDefinitionNode i:
                        Add(i, scope, IdKind.Interface, i.Name, i.TypeParameters.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        Members(i.Methods.Cast<AstNode>().Concat(i.Properties).Concat(i.Indexers), scope + "." + i.Name + "`" + i.TypeParameters.Count);
                        Members(i.PreprocessorBlocks, scope + "." + i.Name + "`" + i.TypeParameters.Count);
                        break;
                    case TypePreprocessorBlockNode p:
                        Members(p.Items, scope, topLevel, ".#if" + JsonSerializer.Serialize(p.Condition));
                        if (p.ElseBranch != null)
                            Members([p.ElseBranch], scope, topLevel, ".#else" + JsonSerializer.Serialize(p.Condition));
                        break;
                    case MemberPreprocessorBlockNode p:
                        Members(p.Items, scope, condition: ".#if" + JsonSerializer.Serialize(p.Condition));
                        if (p.ElseBranch != null)
                            Members([p.ElseBranch], scope, condition: ".#else" + JsonSerializer.Serialize(p.Condition));
                        break;
                    case MethodNode m:
                        Add(m, scope, IdKind.Method, m.Name, Signature(m.Parameters, m.Output, m.TypeParameters.Count));
                        Members(m.Body, scope + "." + m.Name + "#" + Signature(m.Parameters, m.Output, m.TypeParameters.Count));
                        break;
                    case MethodSignatureNode m:
                        Add(m, scope, IdKind.Method, m.Name, Signature(m.Parameters, m.Output, m.TypeParameters.Count));
                        break;
                    case PropertyNode p:
                        Add(p, scope, IdKind.Property, p.Name, p.TypeName);
                        Members(RecursiveAstWalker.GetAllChildren(p), scope + "." + p.Name);
                        break;
                    case IndexerNode i:
                        Add(i, scope, IdKind.Indexer, "this[]", Signature(i.Parameters, new OutputNode(TextSpan.Empty, i.TypeName)));
                        Members(RecursiveAstWalker.GetAllChildren(i), scope + ".this[]#" + Signature(i.Parameters, new OutputNode(TextSpan.Empty, i.TypeName)));
                        break;
                    case PropertyAccessorNode accessor:
                        Members(RecursiveAstWalker.GetAllChildren(accessor), scope + ".#" + accessor.Kind.ToString().ToLowerInvariant());
                        break;
                    case ConstructorNode c:
                        Add(c, scope, IdKind.Constructor, c.IsStatic ? ".cctor" : ".ctor", Signature(c.Parameters, null));
                        Members(RecursiveAstWalker.GetAllChildren(c).Where(n => n is StatementNode), scope + ".ctor#" + Signature(c.Parameters, null));
                        break;
                    case OperatorOverloadNode o:
                        Add(o, scope, IdKind.OperatorOverload, o.OperatorToken, Signature(o.Parameters, o.Output));
                        Members(RecursiveAstWalker.GetAllChildren(o).Where(n => n is StatementNode), scope + "." + o.OperatorToken + "#" + Signature(o.Parameters, o.Output));
                        break;
                    case DelegateDefinitionNode d: Add(d, scope, IdKind.Delegate, d.Name, Signature(d.Parameters, d.Output)); break;
                    case EventDefinitionNode e:
                        Add(e, scope, IdKind.Event, e.Name, e.DelegateType);
                        Members(e.AddBody ?? [], scope + "." + e.Name + ".#add");
                        Members(e.RemoveBody ?? [], scope + "." + e.Name + ".#remove");
                        break;
                    case RecordDefinitionNode r:
                        Add(r, scope, IdKind.Record, r.Name, "");
                        Members(r.Fields, scope + "." + r.Name);
                        break;
                    case UnionTypeDefinitionNode u:
                        Add(u, scope, IdKind.Union, u.Name, "");
                        Members(u.Variants, scope + "." + u.Name);
                        break;
                    case VariantDefinitionNode v:
                        Add(v, scope, IdKind.Variant, v.Name, "");
                        Members(v.Fields, scope + "." + v.Name);
                        break;
                    case FieldDefinitionNode f: Add(f, scope, IdKind.Field, f.Name, f.TypeName); break;
                    case ClassFieldNode f: Add(f, scope, IdKind.Field, f.Name, f.TypeName); break;
                    case ProofObligationNode p: Add(p, scope, IdKind.ProofObligation, p.Description ?? "proof", ""); break;
                    case LocalFunctionStatementNode l:
                        Members([l.Function], scope);
                        break;
                    case EnumDefinitionNode e: Add(e, scope, IdKind.Enum, e.Name, ""); break;
                    case EnumExtensionNode e:
                        Add(e, scope, IdKind.EnumExtension, e.EnumName, "");
                        Members(e.Methods, scope + "." + e.EnumName + "Extensions");
                        break;
                    case RefinementTypeNode r: Add(r, scope, IdKind.RefinementType, r.Name, r.BaseTypeName); break;
                    case IndexedTypeNode i: Add(i, scope, IdKind.IndexedType, i.Name, i.BaseTypeName); break;
                    case TypeDefinitionNode:
                        throw new InvalidOperationException($"Unsupported declaration identity kind: {node.GetType().Name}");
                    default:
                        Members(RecursiveAstWalker.GetAllChildren(node), scope);
                        break;
                }
            }
        }
        Add(module, "", IdKind.Module, module.Name, "");
        Members(module.Functions.Cast<AstNode>().Concat(module.Classes).Concat(module.Interfaces).Concat(module.Enums)
            .Concat(module.EnumExtensions).Concat(module.RefinementTypes).Concat(module.IndexedTypes).Concat(module.TypePreprocessorBlocks)
            .Concat(module.Delegates).Concat(module.Items).Distinct(), module.Name, topLevel: true);
        return result;
    }
}
