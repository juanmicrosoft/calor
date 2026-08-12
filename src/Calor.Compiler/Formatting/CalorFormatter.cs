using System.Buffers.Binary;
using System.Text;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.Formatting;

/// <summary>
/// Formats Calor source without reconstructing it from the AST.
/// </summary>
/// <remarks>
/// Source formatting is intentionally limited to transformations that can be
/// proven trivia-only: canonical indentation, comment attachment indentation,
/// and trailing whitespace outside comments and raw C# regions. User text,
/// strings, raw C#, attributes, member targets, types, and every identifier
/// (including structural IDs) are retained from the original source.
///
/// The AST overload remains for C# → Calor conversion tests and emit-only
/// callers. It performs no identifier post-processing and must not be used for
/// formatting an existing source document because an AST has no source trivia.
/// </remarks>
public sealed class CalorFormatter
{
    /// <summary>
    /// Emits an AST as Calor. This is not a lossless source-formatting API.
    /// </summary>
    public string Format(ModuleNode module)
    {
        ArgumentNullException.ThrowIfNull(module);
        return new CalorEmitter().Emit(module).TrimEnd('\r', '\n');
    }

    /// <summary>
    /// Formats source while preserving all non-trivia source text.
    /// </summary>
    public SourceFormatResult FormatSource(string source, string? filePath = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        var core = FormatCore(source, filePath);
        if (!core.Success)
        {
            return SourceFormatResult.Failure(source, core.Diagnostics, core.Errors);
        }

        var validation = Validate(source, core.Formatted, filePath);
        if (validation.IsUnsupported)
        {
            var diagnostics = core.Diagnostics.ToList();
            diagnostics.Add(new Diagnostic(
                DiagnosticCode.FormatConservativeFallback,
                validation.Error
                    ?? "Source is unsupported by the safe formatting gates.",
                TextSpan.Empty,
                DiagnosticSeverity.Warning,
                filePath));
            return SourceFormatResult.Successful(
                source,
                source,
                diagnostics,
                usedConservativeFallback: true,
                validation.Error);
        }
        if (!validation.Success)
        {
            return SourceFormatResult.Failure(
                source,
                core.Diagnostics,
                [validation.Error ?? "Formatted source failed validation."]);
        }

        var idempotence = FormatCore(core.Formatted, filePath);
        if (!idempotence.Success
            || !string.Equals(core.Formatted, idempotence.Formatted, StringComparison.Ordinal))
        {
            return SourceFormatResult.Failure(
                source,
                core.Diagnostics,
                ["Formatter idempotence validation failed."]);
        }

        return SourceFormatResult.Successful(source, core.Formatted, core.Diagnostics);
    }

    /// <summary>
    /// Re-runs all safety gates used before a source write.
    /// </summary>
    internal SourceValidationResult Validate(string original, string formatted, string? filePath)
    {
        if (!LosslessSourceDocument.HasEquivalentLineShape(original, formatted, out var shapeError))
        {
            return SourceValidationResult.Failed(shapeError);
        }

        var originalParse = Parse(original, filePath);
        var formattedParse = Parse(formatted, filePath);
        if (!originalParse.Success || !formattedParse.Success)
        {
            return SourceValidationResult.Failed("Original or formatted source no longer parses.");
        }

        if (!TokensEqual(originalParse.Tokens, formattedParse.Tokens))
        {
            return SourceValidationResult.Failed(
                "Semantic token sequence changed during formatting.");
        }

        // Classify generated-C# failures separately below instead of folding them
        // into the semantic-error fallback.
        var structuralOptions = new CompilationOptions
        {
            DeferGeneratedOutputValidation = true
        };
        var originalCompilation = Program.Compile(original, filePath, structuralOptions);
        var formattedCompilation = Program.Compile(formatted, filePath, structuralOptions);
        if (originalCompilation.HasErrors != formattedCompilation.HasErrors)
        {
            return SourceValidationResult.Failed(
                "Compilation success changed during formatting.");
        }

        if (originalCompilation.HasErrors)
        {
            var originalErrors = ErrorFingerprint(originalCompilation.Diagnostics);
            var formattedErrors = ErrorFingerprint(formattedCompilation.Diagnostics);
            if (!originalErrors.SequenceEqual(formattedErrors, StringComparer.Ordinal))
            {
                return SourceValidationResult.Failed(
                    "Compilation diagnostics changed during formatting.");
            }

            return SourceValidationResult.Unsupported(
                "Source has semantic errors; conservative formatting fallback left it unchanged.");
        }

        if (!string.Equals(
                originalCompilation.GeneratedCode,
                formattedCompilation.GeneratedCode,
                StringComparison.Ordinal))
        {
            return SourceValidationResult.Failed(
                "Generated C# changed during formatting.");
        }

        var generatedValidation = GeneratedCSharpCompiler.Validate(
            formattedCompilation.GeneratedCode);
        if (!generatedValidation.CompilationSuccess)
        {
            return SourceValidationResult.Unsupported(
                "Source does not generate Roslyn-clean C#; conservative formatting fallback left it unchanged.");
        }

        return SourceValidationResult.Passed();
    }

    private static CoreFormatResult FormatCore(string source, string? filePath)
    {
        var parsed = Parse(source, filePath);
        if (!parsed.Success)
        {
            return CoreFormatResult.Failure(
                source,
                parsed.Diagnostics,
                parsed.Diagnostics.Errors.Select(d => d.Message).ToList());
        }

        var indentation = parsed.Diagnostics.DiagnosticsWithFixes
            .Where(d => d.Code is DiagnosticCode.TabIndentation
                or DiagnosticCode.NonStandardIndentWidth)
            .SelectMany(d => d.Fix.Edits)
            .Where(e => e.StartLine == e.EndLine
                && e.StartColumn == 1)
            .GroupBy(e => e.StartLine)
            .ToDictionary(g => g.Key, g => g.Last().NewText);

        var document = LosslessSourceDocument.Parse(source);
        return CoreFormatResult.Successful(
            document.Format(indentation, document.GetLineProtections(parsed.Tokens)),
            parsed.Diagnostics.ToList());
    }

    private static ParsedSource Parse(string source, string? filePath)
    {
        var diagnostics = new DiagnosticBag();
        diagnostics.SetFilePath(filePath);
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        if (!diagnostics.HasErrors)
        {
            var parser = new Parser(tokens, diagnostics);
            parser.Parse();
        }

        return new ParsedSource(!diagnostics.HasErrors, tokens, diagnostics);
    }

    private static bool TokensEqual(IReadOnlyList<Token> left, IReadOnlyList<Token> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (left[i].Kind != right[i].Kind
                || !string.Equals(left[i].Text, right[i].Text, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static IEnumerable<string> ErrorFingerprint(DiagnosticBag diagnostics) =>
        diagnostics.Errors
            .Select(d => $"{d.Code}\0{d.Message}")
            .OrderBy(value => value, StringComparer.Ordinal);

    private sealed record ParsedSource(
        bool Success,
        IReadOnlyList<Token> Tokens,
        DiagnosticBag Diagnostics);

    private sealed record CoreFormatResult(
        bool Success,
        string Formatted,
        List<Diagnostic> Diagnostics,
        List<string> Errors)
    {
        public static CoreFormatResult Successful(string formatted, List<Diagnostic> diagnostics) =>
            new(true, formatted, diagnostics, []);

        public static CoreFormatResult Failure(
            string source,
            DiagnosticBag diagnostics,
            List<string> errors) =>
            new(false, source, diagnostics.ToList(), errors);
    }
}

/// <summary>
/// Result of lossless source formatting.
/// </summary>
public sealed record SourceFormatResult(
    bool Success,
    string Original,
    string Formatted,
    IReadOnlyList<Diagnostic> Diagnostics,
    IReadOnlyList<string> Errors,
    bool UsedConservativeFallback,
    string? ConservativeFallbackReason)
{
    internal static SourceFormatResult Successful(
        string original,
        string formatted,
        IReadOnlyList<Diagnostic> diagnostics,
        bool usedConservativeFallback = false,
        string? conservativeFallbackReason = null) =>
        new(
            true,
            original,
            formatted,
            diagnostics,
            [],
            usedConservativeFallback,
            conservativeFallbackReason);

    internal static SourceFormatResult Failure(
        string original,
        IReadOnlyList<Diagnostic> diagnostics,
        IReadOnlyList<string> errors)
    {
        var effectiveDiagnostics = diagnostics.ToList();
        if (!effectiveDiagnostics.Any(d => d.IsError) && errors.Count > 0)
        {
            effectiveDiagnostics.Add(new Diagnostic(
                DiagnosticCode.FormatProcessingError,
                errors[0],
                TextSpan.Empty,
                DiagnosticSeverity.Error));
        }
        return new(
            false,
            original,
            original,
            effectiveDiagnostics,
            errors,
            false,
            null);
    }
}

internal sealed record SourceValidationResult(bool Success, bool IsUnsupported, string? Error)
{
    public static SourceValidationResult Passed() => new(true, false, null);
    public static SourceValidationResult Failed(string? error) => new(false, false, error);
    public static SourceValidationResult Unsupported(string error) => new(false, true, error);
}

/// <summary>
/// A line-oriented lossless source representation. Every original line
/// terminator is retained, including mixed LF/CRLF/CR input.
/// </summary>
internal sealed class LosslessSourceDocument
{
    private readonly SourceLine[] _lines;

    private LosslessSourceDocument(SourceLine[] lines)
    {
        _lines = lines;
    }

    public static LosslessSourceDocument Parse(string source)
    {
        var lines = new List<SourceLine>();
        var start = 0;
        for (var i = 0; i < source.Length; i++)
        {
            if (source[i] == '\r')
            {
                var lineBreak = i + 1 < source.Length && source[i + 1] == '\n'
                    ? "\r\n"
                    : "\r";
                lines.Add(new SourceLine(start, source[start..i], lineBreak));
                if (lineBreak.Length == 2)
                {
                    i++;
                }
                start = i + 1;
            }
            else if (source[i] == '\n')
            {
                lines.Add(new SourceLine(start, source[start..i], "\n"));
                start = i + 1;
            }
        }

        if (start < source.Length || source.Length == 0)
        {
            lines.Add(new SourceLine(start, source[start..], string.Empty));
        }

        return new LosslessSourceDocument(lines.ToArray());
    }

    public string Format(
        IReadOnlyDictionary<int, string> indentationByLine,
        IReadOnlyDictionary<int, SourceLineProtection>? protections = null)
    {
        var formatted = new FormattedLine[_lines.Length];

        for (var i = 0; i < _lines.Length; i++)
        {
            var line = _lines[i];
            var trimmedStart = line.Content.TrimStart(' ', '\t');
            var protection = protections != null
                && protections.TryGetValue(i + 1, out var value)
                    ? value
                    : SourceLineProtection.None;
            var leadingLength = line.Content.Length - trimmedStart.Length;
            var leading = !protection.Leading
                && indentationByLine.TryGetValue(i + 1, out var replacement)
                    ? replacement
                    : line.Content[..leadingLength];
            var content = leading + trimmedStart;

            var isBlank = trimmedStart.Length == 0;
            var isComment = trimmedStart.StartsWith("//", StringComparison.Ordinal);
            if (!protection.Trailing
                && !isBlank
                && !isComment
                && FindLineCommentStart(trimmedStart) < 0)
            {
                content = content.TrimEnd(' ', '\t');
            }

            formatted[i] = new FormattedLine(
                content,
                line.LineBreak,
                isBlank,
                isComment,
                protection.Leading);
        }

        ReindentAttachedComments(formatted);

        var builder = new StringBuilder();
        foreach (var line in formatted)
        {
            builder.Append(line.Content);
            builder.Append(line.LineBreak);
        }
        return builder.ToString();
    }

    /// <summary>
    /// Returns lines whose trailing whitespace is outside comments and raw C#,
    /// and is therefore removable by <see cref="Format"/>.
    /// </summary>
    internal static IReadOnlySet<int> GetTrimmableTrailingWhitespaceLines(string source)
    {
        var document = Parse(source);
        var result = new HashSet<int>();
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, diagnostics).TokenizeAllForParser();
        var protections = document.GetLineProtections(tokens);

        for (var i = 0; i < document._lines.Length; i++)
        {
            var content = document._lines[i].Content;
            var trimmedStart = content.TrimStart(' ', '\t');
            var protectedTrailing = protections.TryGetValue(i + 1, out var protection)
                && protection.Trailing;
            if (!protectedTrailing
                && trimmedStart.Length > 0
                && !trimmedStart.StartsWith("//", StringComparison.Ordinal)
                && FindLineCommentStart(trimmedStart) < 0
                && content.Length > 0
                && content[^1] is ' ' or '\t')
            {
                result.Add(i + 1);
            }
        }

        return result;
    }

    /// <summary>
    /// Maps lexer-owned multiline token spans to the exact leading/trailing
    /// trivia ranges they overlap. Formatting never edits trivia that is part
    /// of a multiline string or raw/interop token.
    /// </summary>
    internal IReadOnlyDictionary<int, SourceLineProtection> GetLineProtections(
        IReadOnlyList<Token> tokens)
    {
        var protections = new Dictionary<int, SourceLineProtection>();
        foreach (var token in tokens)
        {
            if (!IsProtectedToken(token.Kind)
                || token.Text.IndexOfAny(['\r', '\n']) < 0)
            {
                continue;
            }

            for (var i = 0; i < _lines.Length; i++)
            {
                var line = _lines[i];
                var contentStart = line.Start;
                var contentEnd = contentStart + line.Content.Length;
                var lineEnd = contentEnd + line.LineBreak.Length;
                if (token.Span.End <= contentStart || token.Span.Start >= lineEnd)
                {
                    continue;
                }

                var leadingEnd = contentStart;
                while (leadingEnd < contentEnd
                    && line.Content[leadingEnd - contentStart] is ' ' or '\t')
                {
                    leadingEnd++;
                }

                var trailingStart = contentEnd;
                while (trailingStart > contentStart
                    && line.Content[trailingStart - contentStart - 1] is ' ' or '\t')
                {
                    trailingStart--;
                }

                var leadingOverlap = token.Span.Start < leadingEnd
                    && token.Span.End > contentStart;
                var trailingOverlap = token.Span.Start < contentEnd
                    && token.Span.End > trailingStart;
                if (!leadingOverlap && !trailingOverlap)
                {
                    continue;
                }

                var lineNumber = i + 1;
                var existing = protections.TryGetValue(lineNumber, out var value)
                    ? value
                    : SourceLineProtection.None;
                protections[lineNumber] = new SourceLineProtection(
                    existing.Leading || leadingOverlap,
                    existing.Trailing || trailingOverlap);
            }
        }

        return protections;
    }

    public static bool HasEquivalentLineShape(
        string original,
        string formatted,
        out string? error)
    {
        var before = Parse(original)._lines;
        var after = Parse(formatted)._lines;
        if (before.Length != after.Length)
        {
            error = "Formatting changed the number of source lines.";
            return false;
        }

        for (var i = 0; i < before.Length; i++)
        {
            if (!string.Equals(before[i].LineBreak, after[i].LineBreak, StringComparison.Ordinal))
            {
                error = $"Formatting changed the newline sequence on line {i + 1}.";
                return false;
            }

            var beforeText = NormalizeTrivia(before[i].Content);
            var afterText = NormalizeTrivia(after[i].Content);
            if (!string.Equals(beforeText, afterText, StringComparison.Ordinal))
            {
                error = $"Formatting changed non-trivia source text on line {i + 1}.";
                return false;
            }
        }

        error = null;
        return true;
    }

    private static void ReindentAttachedComments(FormattedLine[] lines)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].IsComment || lines[i].LeadingProtected)
            {
                continue;
            }

            var nextCode = -1;
            var separatedFromNext = false;
            for (var j = i + 1; j < lines.Length; j++)
            {
                if (lines[j].IsBlank)
                {
                    separatedFromNext = true;
                    break;
                }
                if (!lines[j].IsComment && !lines[j].LeadingProtected)
                {
                    nextCode = j;
                    break;
                }
            }

            var anchor = !separatedFromNext && nextCode >= 0
                ? nextCode
                : FindPreviousCodeLine(lines, i);
            if (anchor < 0)
            {
                continue;
            }

            var indent = GetLeadingWhitespace(lines[anchor].Content);
            var commentText = lines[i].Content.TrimStart(' ', '\t');
            lines[i] = lines[i] with { Content = indent + commentText };
        }
    }

    private static int FindPreviousCodeLine(FormattedLine[] lines, int start)
    {
        for (var i = start - 1; i >= 0; i--)
        {
            if (!lines[i].IsBlank && !lines[i].IsComment && !lines[i].LeadingProtected)
            {
                return i;
            }
        }
        return -1;
    }

    private static string GetLeadingWhitespace(string content)
    {
        var length = 0;
        while (length < content.Length && content[length] is ' ' or '\t')
        {
            length++;
        }
        return content[..length];
    }

    private static int FindLineCommentStart(string content)
    {
        var inString = false;
        var escaped = false;
        for (var i = 0; i + 1 < content.Length; i++)
        {
            var current = content[i];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (current == '\\')
                {
                    escaped = true;
                }
                else if (current == '"')
                {
                    inString = false;
                }
                continue;
            }

            if (current == '"')
            {
                inString = true;
            }
            else if (current == '/' && content[i + 1] == '/')
            {
                return i;
            }
        }
        return -1;
    }

    private static bool IsProtectedToken(TokenKind kind) =>
        kind is TokenKind.StrLiteral
            or TokenKind.RawCSharp
            or TokenKind.RawCSharpExpression
            or TokenKind.CSharpInterop;

    private static string NormalizeTrivia(string content) =>
        content.TrimStart(' ', '\t').TrimEnd(' ', '\t');

    private sealed record SourceLine(int Start, string Content, string LineBreak);

    private sealed record FormattedLine(
        string Content,
        string LineBreak,
        bool IsBlank,
        bool IsComment,
        bool LeadingProtected);
}

internal readonly record struct SourceLineProtection(bool Leading, bool Trailing)
{
    public static SourceLineProtection None => new(false, false);
}

/// <summary>
/// A byte snapshot of a source file with its original encoding policy.
/// </summary>
internal sealed class SourceFileSnapshot
{
    private readonly Encoding _encoding;
    private readonly byte[] _preamble;

    private SourceFileSnapshot(
        string path,
        byte[] bytes,
        string text,
        Encoding encoding,
        byte[] preamble)
    {
        Path = path;
        Bytes = bytes;
        Text = text;
        _encoding = encoding;
        _preamble = preamble;
    }

    public string Path { get; }
    public byte[] Bytes { get; }
    public string Text { get; }

    public static async Task<SourceFileSnapshot> ReadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var fullPath = System.IO.Path.GetFullPath(path);
        var resolvedTarget = File.ResolveLinkTarget(fullPath, returnFinalTarget: true);
        var effectivePath = resolvedTarget?.FullName ?? fullPath;
        var bytes = await File.ReadAllBytesAsync(effectivePath, cancellationToken);
        var (encoding, preambleLength) = DetectEncoding(bytes);
        var text = encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);
        var preamble = bytes[..preambleLength];
        return new SourceFileSnapshot(effectivePath, bytes, text, encoding, preamble);
    }

    public byte[] Encode(string text)
    {
        var payload = _encoding.GetBytes(text);
        if (_preamble.Length == 0)
        {
            return payload;
        }

        var bytes = new byte[_preamble.Length + payload.Length];
        _preamble.CopyTo(bytes, 0);
        payload.CopyTo(bytes, _preamble.Length);
        return bytes;
    }

    internal static string DecodeLike(byte[] bytes, SourceFileSnapshot policy)
    {
        var preambleLength = policy._preamble.Length;
        if (bytes.Length < preambleLength
            || !bytes.AsSpan(0, preambleLength).SequenceEqual(policy._preamble))
        {
            throw new InvalidDataException("Temporary file did not preserve the source BOM.");
        }
        return policy._encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);
    }

    private static (Encoding Encoding, int PreambleLength) DetectEncoding(byte[] bytes)
    {
        if (HasPrefix(bytes, 0x00, 0x00, 0xFE, 0xFF))
        {
            return (new UTF32Encoding(bigEndian: true, byteOrderMark: false, throwOnInvalidCharacters: true), 4);
        }
        if (HasPrefix(bytes, 0xFF, 0xFE, 0x00, 0x00))
        {
            return (new UTF32Encoding(bigEndian: false, byteOrderMark: false, throwOnInvalidCharacters: true), 4);
        }
        if (HasPrefix(bytes, 0xEF, 0xBB, 0xBF))
        {
            return (new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true), 3);
        }
        if (HasPrefix(bytes, 0xFE, 0xFF))
        {
            return (new UnicodeEncoding(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true), 2);
        }
        if (HasPrefix(bytes, 0xFF, 0xFE))
        {
            return (new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true), 2);
        }

        return (new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true), 0);
    }

    private static bool HasPrefix(byte[] bytes, params byte[] prefix) =>
        bytes.Length >= prefix.Length
        && bytes.AsSpan(0, prefix.Length).SequenceEqual(prefix);
}

/// <summary>
/// Atomically replaces a source file only after byte and semantic validation.
/// </summary>
internal static class SafeSourceFile
{
    internal static async Task WriteFormattedAsync(
        SourceFileSnapshot original,
        string formatted,
        CalorFormatter formatter,
        Func<CancellationToken, Task>? beforeReplace = null,
        CancellationToken cancellationToken = default)
    {
        await WriteAsync(
            original,
            formatted,
            candidate =>
            {
                var validation = formatter.Validate(original.Text, candidate, original.Path);
                if (!validation.Success)
                {
                    throw new InvalidDataException(validation.Error);
                }
            },
            beforeReplace,
            cancellationToken);
    }

    internal static async Task WriteParsedAsync(
        SourceFileSnapshot original,
        string candidate,
        Action<string> validate,
        Func<CancellationToken, Task>? beforeReplace = null,
        CancellationToken cancellationToken = default)
    {
        await WriteAsync(original, candidate, validate, beforeReplace, cancellationToken);
    }

    private static async Task WriteAsync(
        SourceFileSnapshot original,
        string candidate,
        Action<string> validate,
        Func<CancellationToken, Task>? beforeReplace,
        CancellationToken cancellationToken)
    {
        validate(candidate);
        EnsureSafeReplacementTarget(original.Path);
        var candidateBytes = original.Encode(candidate);
        var directory = System.IO.Path.GetDirectoryName(original.Path)
            ?? throw new InvalidOperationException("Source file has no parent directory.");
        var tempPath = System.IO.Path.Combine(
            directory,
            $".{System.IO.Path.GetFileName(original.Path)}.{Guid.NewGuid():N}.format.tmp");

        UnixFileMode? originalMode = null;
        if (!OperatingSystem.IsWindows())
        {
            originalMode = File.GetUnixFileMode(original.Path);
        }

        try
        {
            var streamOptions = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                BufferSize = 4096,
                Options = FileOptions.Asynchronous | FileOptions.WriteThrough
            };
            if (!OperatingSystem.IsWindows())
            {
                streamOptions.UnixCreateMode =
                    UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            await using (var stream = new FileStream(tempPath, streamOptions))
            {
                await stream.WriteAsync(candidateBytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            var persistedBytes = await File.ReadAllBytesAsync(tempPath, cancellationToken);
            if (!candidateBytes.AsSpan().SequenceEqual(persistedBytes))
            {
                throw new IOException("Temporary format file did not persist the expected bytes.");
            }

            var persistedText = SourceFileSnapshot.DecodeLike(persistedBytes, original);
            validate(persistedText);

            if (beforeReplace != null)
            {
                await beforeReplace(cancellationToken);
            }

            EnsureSafeReplacementTarget(original.Path);
            var currentBytes = await File.ReadAllBytesAsync(original.Path, cancellationToken);
            if (!original.Bytes.AsSpan().SequenceEqual(currentBytes))
            {
                throw new IOException("Source changed while formatting; refusing to overwrite it.");
            }

            if (!OperatingSystem.IsWindows() && originalMode.HasValue)
            {
                File.SetUnixFileMode(tempPath, originalMode.Value);
            }

            File.Move(tempPath, original.Path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static void EnsureSafeReplacementTarget(string path)
    {
        if (File.ResolveLinkTarget(path, returnFinalTarget: false) != null)
        {
            throw new IOException(
                "Source replacement target became a symbolic link; refusing to overwrite it.");
        }

        EnsureKnownSingleLinkCount(NativeFileLinks.TryGetLinkCount(path));
    }

    internal static void EnsureKnownSingleLinkCount(int? linkCount)
    {
        if (linkCount is null or < 1)
        {
            throw new IOException(
                "Could not determine the source hard-link count; refusing replacement.");
        }

        if (linkCount > 1)
        {
            throw new IOException(
                "Source has multiple hard links; refusing non-atomic replacement.");
        }
    }
}

internal static class NativeFileLinks
{
    private const int StatBufferSize = 256;

    internal static int? TryGetLinkCount(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var handle = File.OpenHandle(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                if (!GetFileInformationByHandle(handle, out var information))
                {
                    throw new IOException(
                        $"Could not inspect source hard links: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
                }
                return checked((int)information.NumberOfLinks);
            }

            if (!Environment.Is64BitProcess)
            {
                throw new IOException(
                    $"Could not inspect source hard links on unsupported 32-bit architecture " +
                    $"'{RuntimeInformation.ProcessArchitecture}'.");
            }

            if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            {
                return null;
            }

            return OperatingSystem.IsMacOS()
                ? GetDarwinLinkCount(path, RuntimeInformation.ProcessArchitecture)
                : GetLinuxLinkCount(path, RuntimeInformation.ProcessArchitecture);
        }
        catch (DllNotFoundException ex)
        {
            throw new IOException(
                "Could not inspect source hard links because the native platform library is unavailable.",
                ex);
        }
        catch (OverflowException ex)
        {
            throw new IOException(
                "Could not inspect source hard links because the link count exceeds the supported range.",
                ex);
        }
    }

    private static int GetDarwinLinkCount(string path, Architecture architecture)
    {
        var entryPoint = GetDarwinStatEntryPoint(architecture);
        var buffer = Marshal.AllocHGlobal(StatBufferSize);
        try
        {
            int result;
            try
            {
                result = architecture switch
                {
                    Architecture.Arm64 => StatDarwinArm64(path, buffer),
                    Architecture.X64 => StatDarwin64(path, buffer),
                    _ => throw UnsupportedDarwinArchitecture(architecture)
                };
            }
            catch (EntryPointNotFoundException ex)
            {
                throw new IOException(
                    $"Could not inspect source hard links on macOS architecture '{architecture}' " +
                    $"because libc entry point '{entryPoint}' is unavailable.",
                    ex);
            }

            if (result != 0)
            {
                throw NativeStatFailure("macOS", architecture, entryPoint);
            }

            var statBytes = new byte[8];
            Marshal.Copy(buffer, statBytes, 0, statBytes.Length);
            return DecodeDarwin64LinkCount(statBytes);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static int GetLinuxLinkCount(string path, Architecture architecture)
    {
        EnsureSupportedLinuxArchitecture(architecture);

        var buffer = Marshal.AllocHGlobal(StatBufferSize);
        try
        {
            var entryPoint = "stat";
            int result;
            try
            {
                result = StatLinux(path, buffer);
            }
            catch (EntryPointNotFoundException statException)
            {
                entryPoint = "__xstat";
                try
                {
                    result = XStatLinux(
                        GetLinuxXStatVersion(architecture),
                        path,
                        buffer);
                }
                catch (EntryPointNotFoundException xstatException)
                {
                    throw new IOException(
                        $"Could not inspect source hard links on Linux architecture " +
                        $"'{architecture}' because libc exposes neither 'stat' nor the " +
                        $"compatible '__xstat' fallback.",
                        new AggregateException(statException, xstatException));
                }
            }

            if (result != 0)
            {
                throw NativeStatFailure("Linux", architecture, entryPoint);
            }

            var statBytes = new byte[24];
            Marshal.Copy(buffer, statBytes, 0, statBytes.Length);
            return DecodeLinuxLinkCount(statBytes, architecture);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    internal static string GetDarwinStatEntryPoint(Architecture architecture) =>
        architecture switch
        {
            Architecture.Arm64 => "stat",
            Architecture.X64 => "stat$INODE64",
            _ => throw UnsupportedDarwinArchitecture(architecture)
        };

    internal static int DecodeDarwin64LinkCount(ReadOnlySpan<byte> statBuffer)
    {
        if (statBuffer.Length < 8)
        {
            throw new IOException(
                "Could not inspect source hard links because the Darwin64 stat buffer was too small.");
        }

        return BinaryPrimitives.ReadUInt16LittleEndian(
            statBuffer.Slice(6, sizeof(ushort)));
    }

    internal static int DecodeLinuxLinkCount(
        ReadOnlySpan<byte> statBuffer,
        Architecture architecture)
    {
        if (statBuffer.Length < 24)
        {
            throw new IOException(
                "Could not inspect source hard links because the Linux stat buffer was too small.");
        }

        try
        {
            return architecture switch
            {
                Architecture.X64 => checked((int)BinaryPrimitives.ReadUInt64LittleEndian(
                    statBuffer.Slice(16, sizeof(ulong)))),
                Architecture.Arm64
                    or Architecture.RiscV64
                    or Architecture.LoongArch64 => checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                        statBuffer.Slice(20, sizeof(uint)))),
                Architecture.Ppc64le => checked((int)BinaryPrimitives.ReadUInt64LittleEndian(
                    statBuffer.Slice(16, sizeof(ulong)))),
                Architecture.S390x => checked((int)BinaryPrimitives.ReadUInt64BigEndian(
                    statBuffer.Slice(16, sizeof(ulong)))),
                _ => throw new IOException(
                    $"Could not inspect source hard links on unsupported Linux architecture " +
                    $"'{architecture}'.")
            };
        }
        catch (OverflowException ex)
        {
            throw new IOException(
                "Could not inspect source hard links because the link count exceeds the supported range.",
                ex);
        }
    }

    internal static int GetLinuxXStatVersion(Architecture architecture)
    {
        // glibc generic 64-bit ABIs use _STAT_VER_KERNEL (0). The x86-64,
        // powerpc64, and s390x architecture-specific ABIs use version 1.
        return architecture switch
        {
            Architecture.Arm64
                or Architecture.RiscV64
                or Architecture.LoongArch64 => 0,
            Architecture.X64
                or Architecture.Ppc64le
                or Architecture.S390x => 1,
            _ => throw new IOException(
                $"Could not inspect source hard links on unsupported Linux architecture " +
                $"'{architecture}'.")
        };
    }

    private static void EnsureSupportedLinuxArchitecture(Architecture architecture)
    {
        _ = GetLinuxXStatVersion(architecture);
    }

    private static IOException UnsupportedDarwinArchitecture(Architecture architecture) =>
        new(
            $"Could not inspect source hard links on unsupported macOS architecture " +
            $"'{architecture}'.");

    private static IOException NativeStatFailure(
        string operatingSystem,
        Architecture architecture,
        string entryPoint) =>
        new(
            $"Could not inspect source hard links on {operatingSystem} architecture " +
            $"'{architecture}' because libc '{entryPoint}' failed: " +
            $"{new Win32Exception(Marshal.GetLastWin32Error()).Message}");

    [DllImport("libc", EntryPoint = "stat", SetLastError = true)]
    private static extern int StatLinux(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        IntPtr buffer);

    [DllImport("libc", EntryPoint = "__xstat", SetLastError = true)]
    private static extern int XStatLinux(
        int version,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        IntPtr buffer);

    [DllImport("libc", EntryPoint = "stat", SetLastError = true)]
    private static extern int StatDarwinArm64(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        IntPtr buffer);

    [DllImport("libc", EntryPoint = "stat$INODE64", SetLastError = true)]
    private static extern int StatDarwin64(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        IntPtr buffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle file,
        out ByHandleFileInformation information);

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public FileTime CreationTime;
        public FileTime LastAccessTime;
        public FileTime LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint LowDateTime;
        public uint HighDateTime;
    }
}
