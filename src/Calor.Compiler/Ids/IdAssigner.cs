using System.Text.RegularExpressions;

namespace Calor.Compiler.Ids;

/// <summary>
/// Result of an ID assignment operation.
/// </summary>
public sealed class IdAssignmentResult
{
    /// <summary>
    /// The number of IDs that were assigned.
    /// </summary>
    public int AssignedCount { get; init; }

    /// <summary>
    /// The number of duplicate IDs that were fixed.
    /// </summary>
    public int FixedDuplicatesCount { get; init; }

    /// <summary>
    /// Files that were modified.
    /// </summary>
    public IReadOnlyList<string> ModifiedFiles { get; init; } = Array.Empty<string>();

    /// <summary>
    /// The assignments that were made (for dry-run reporting).
    /// </summary>
    public IReadOnlyList<IdAssignment> Assignments { get; init; } = Array.Empty<IdAssignment>();

    /// <summary>
    /// Any errors that occurred during assignment.
    /// </summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Returns true if the operation succeeded.
    /// </summary>
    public bool Success => Errors.Count == 0;
}

/// <summary>
/// Represents a single ID assignment.
/// </summary>
public sealed record IdAssignment(
    string FilePath,
    IdKind Kind,
    string Name,
    int Line,
    string OldId,
    string NewId);

/// <summary>
/// Assigns IDs to Calor declarations that are missing them.
/// </summary>
public static partial class IdAssigner
{
    // Patterns to match declaration tags that need IDs
    private static readonly Regex ModulePattern = ModulePatternRegex();
    private static readonly Regex FunctionPattern = FunctionPatternRegex();
    private static readonly Regex ClassPattern = ClassPatternRegex();
    private static readonly Regex InterfacePattern = InterfacePatternRegex();
    private static readonly Regex PropertyPattern = PropertyPatternRegex();
    private static readonly Regex MethodPattern = MethodPatternRegex();
    private static readonly Regex ConstructorPattern = ConstructorPatternRegex();
    private static readonly Regex EnumPattern = EnumPatternRegex();
    private static readonly Regex EnumExtensionPattern = EnumExtensionPatternRegex();

    /// <summary>
    /// Assigns IDs to declarations in a file.
    /// </summary>
    /// <param name="content">The file content.</param>
    /// <param name="filePath">The file path (for reporting).</param>
    /// <param name="fixDuplicates">If true, reassign duplicate IDs.</param>
    /// <param name="existingIds">Set of IDs that already exist (to avoid duplicates).</param>
    /// <returns>The modified content and list of assignments made.</returns>
    public static (string Content, IReadOnlyList<IdAssignment> Assignments) AssignIds(
        string content,
        string filePath,
        bool fixDuplicates = false,
        HashSet<string>? existingIds = null)
    {
        existingIds ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var assignments = new List<IdAssignment>();
        var lines = content.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var lineNum = i + 1;

            // Try each pattern
            var (newLine, assignment) = TryAssignId(line, filePath, lineNum, IdKind.Module, ModulePattern, existingIds, fixDuplicates);
            if (assignment != null) { lines[i] = newLine; assignments.Add(assignment); continue; }

            (newLine, assignment) = TryAssignId(line, filePath, lineNum, IdKind.Function, FunctionPattern, existingIds, fixDuplicates);
            if (assignment != null) { lines[i] = newLine; assignments.Add(assignment); continue; }

            (newLine, assignment) = TryAssignId(line, filePath, lineNum, IdKind.Class, ClassPattern, existingIds, fixDuplicates);
            if (assignment != null) { lines[i] = newLine; assignments.Add(assignment); continue; }

            (newLine, assignment) = TryAssignId(line, filePath, lineNum, IdKind.Interface, InterfacePattern, existingIds, fixDuplicates);
            if (assignment != null) { lines[i] = newLine; assignments.Add(assignment); continue; }

            (newLine, assignment) = TryAssignId(line, filePath, lineNum, IdKind.Property, PropertyPattern, existingIds, fixDuplicates);
            if (assignment != null) { lines[i] = newLine; assignments.Add(assignment); continue; }

            (newLine, assignment) = TryAssignId(line, filePath, lineNum, IdKind.Method, MethodPattern, existingIds, fixDuplicates);
            if (assignment != null) { lines[i] = newLine; assignments.Add(assignment); continue; }

            (newLine, assignment) = TryAssignId(line, filePath, lineNum, IdKind.Constructor, ConstructorPattern, existingIds, fixDuplicates);
            if (assignment != null) { lines[i] = newLine; assignments.Add(assignment); continue; }

            (newLine, assignment) = TryAssignId(line, filePath, lineNum, IdKind.Enum, EnumPattern, existingIds, fixDuplicates);
            if (assignment != null) { lines[i] = newLine; assignments.Add(assignment); continue; }

            (newLine, assignment) = TryAssignId(line, filePath, lineNum, IdKind.EnumExtension, EnumExtensionPattern, existingIds, fixDuplicates);
            if (assignment != null) { lines[i] = newLine; assignments.Add(assignment); continue; }
        }

        return (string.Join('\n', lines), assignments);
    }

    private static (string Line, IdAssignment? Assignment) TryAssignId(
        string line,
        string filePath,
        int lineNum,
        IdKind kind,
        Regex pattern,
        HashSet<string> existingIds,
        bool fixDuplicates)
    {
        var match = pattern.Match(line);
        if (!match.Success)
            return (line, null);

        var oldId = match.Groups["id"].Value;
        var name = match.Groups["name"].Value;
        var needsNewId = false;

        // Check if we need to assign a new ID
        if (string.IsNullOrEmpty(oldId))
        {
            needsNewId = true;
        }
        else if (fixDuplicates)
        {
            // Only mark as duplicate if we've ALREADY seen this ID
            // (the first occurrence should be kept, only subsequent ones reassigned)
            if (existingIds.Contains(oldId))
            {
                needsNewId = true;
            }
            else
            {
                // First occurrence - track it but don't reassign
                existingIds.Add(oldId);
            }
        }

        if (!needsNewId)
        {
            // Track the existing ID
            if (!string.IsNullOrEmpty(oldId))
            {
                existingIds.Add(oldId);
            }
            return (line, null);
        }

        // Generate a new ID
        var newId = IdGenerator.Generate(kind);

        // Ensure uniqueness
        while (existingIds.Contains(newId))
        {
            newId = IdGenerator.Generate(kind);
        }
        existingIds.Add(newId);

        // Replace the ID in the line
        var newLine = ReplaceId(line, match, oldId, newId);

        return (newLine, new IdAssignment(filePath, kind, name, lineNum, oldId, newId));
    }

    private static string ReplaceId(string line, Match match, string oldId, string newId)
    {
        var idGroup = match.Groups["id"];
        var prefix = line.Substring(0, idGroup.Index);
        var suffix = line.Substring(idGroup.Index + idGroup.Length);
        return prefix + newId + suffix;
    }

    /// <summary>
    /// Updates closing tags to match their opening tag IDs.
    /// </summary>
    public static string UpdateClosingTags(string content, IReadOnlyList<IdAssignment> assignments)
    {
        foreach (var assignment in assignments)
        {
            if (string.IsNullOrEmpty(assignment.OldId))
                continue;

            // Update closing tags that reference the old ID
            var closingPatterns = assignment.Kind switch
            {
                IdKind.Module => $@"§/M\{{{Regex.Escape(assignment.OldId)}\}}",
                IdKind.Function => $@"§/F\{{{Regex.Escape(assignment.OldId)}\}}",
                IdKind.Class => $@"§/CL\{{{Regex.Escape(assignment.OldId)}\}}",
                IdKind.Interface => $@"§/IFACE\{{{Regex.Escape(assignment.OldId)}\}}",
                IdKind.Property => $@"§/PROP\{{{Regex.Escape(assignment.OldId)}\}}",
                IdKind.Method => $@"§/MT\{{{Regex.Escape(assignment.OldId)}\}}",
                IdKind.Constructor => $@"§/CTOR\{{{Regex.Escape(assignment.OldId)}\}}",
                IdKind.Enum => $@"§/(?:EN|ENUM)\{{{Regex.Escape(assignment.OldId)}\}}",
                IdKind.EnumExtension => $@"§/EXT\{{{Regex.Escape(assignment.OldId)}\}}",
                _ => null
            };

            if (closingPatterns != null)
            {
                var closingReplace = assignment.Kind switch
                {
                    IdKind.Module => $"§/M{{{assignment.NewId}}}",
                    IdKind.Function => $"§/F{{{assignment.NewId}}}",
                    IdKind.Class => $"§/CL{{{assignment.NewId}}}",
                    IdKind.Interface => $"§/IFACE{{{assignment.NewId}}}",
                    IdKind.Property => $"§/PROP{{{assignment.NewId}}}",
                    IdKind.Method => $"§/MT{{{assignment.NewId}}}",
                    IdKind.Constructor => $"§/CTOR{{{assignment.NewId}}}",
                    IdKind.Enum => $"§/EN{{{assignment.NewId}}}",
                    IdKind.EnumExtension => $"§/EXT{{{assignment.NewId}}}",
                    _ => ""
                };

                content = Regex.Replace(content, closingPatterns, closingReplace, RegexOptions.IgnoreCase);
            }
        }

        return content;
    }

    // Patterns for each declaration type
    // §M{id:name} or §M{:name} (missing ID)
    [GeneratedRegex(@"§M\{(?<id>[^:}]*):(?<name>[^}]+)\}", RegexOptions.IgnoreCase)]
    private static partial Regex ModulePatternRegex();

    // §F{id:name:visibility} or §F{:name:visibility}
    [GeneratedRegex(@"§F\{(?<id>[^:}]*):(?<name>[^:}]+)(?::[^}]*)?\}", RegexOptions.IgnoreCase)]
    private static partial Regex FunctionPatternRegex();

    // §CL{id:name...} or §CLASS{id:name...}
    [GeneratedRegex(@"§(?:CL|CLASS)\{(?<id>[^:}]*):(?<name>[^:}]+)(?::[^}]*)?\}", RegexOptions.IgnoreCase)]
    private static partial Regex ClassPatternRegex();

    // §IFACE{id:name...}
    [GeneratedRegex(@"§IFACE\{(?<id>[^:}]*):(?<name>[^:}]+)(?::[^}]*)?\}", RegexOptions.IgnoreCase)]
    private static partial Regex InterfacePatternRegex();

    // §PROP{id:name:type:visibility}
    [GeneratedRegex(@"§PROP\{(?<id>[^:}]*):(?<name>[^:}]+)(?::[^}]*)?\}", RegexOptions.IgnoreCase)]
    private static partial Regex PropertyPatternRegex();

    // §MT{id:name:visibility...} or §METHOD{id:name...}
    [GeneratedRegex(@"§(?:MT|METHOD)\{(?<id>[^:}]*):(?<name>[^:}]+)(?::[^}]*)?\}", RegexOptions.IgnoreCase)]
    private static partial Regex MethodPatternRegex();

    // §CTOR{id:visibility}
    [GeneratedRegex(@"§CTOR\{(?<id>[^:}]*)(?::(?<name>[^}]+))?\}", RegexOptions.IgnoreCase)]
    private static partial Regex ConstructorPatternRegex();

    // §EN{id:name} or §ENUM{id:name}
    [GeneratedRegex(@"§(?:EN|ENUM)\{(?<id>[^:}]*):(?<name>[^:}]+)(?::[^}]*)?\}", RegexOptions.IgnoreCase)]
    private static partial Regex EnumPatternRegex();

    // §EXT{id:enumName}
    [GeneratedRegex(@"§EXT\{(?<id>[^:}]*):(?<name>[^:}]+)(?::[^}]*)?\}", RegexOptions.IgnoreCase)]
    private static partial Regex EnumExtensionPatternRegex();
}
