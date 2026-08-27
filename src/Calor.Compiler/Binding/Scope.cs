using Calor.Compiler.Ast;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.Binding;

/// <summary>
/// Stable structural identity for a bound symbol.
/// </summary>
public readonly record struct SymbolId
{
    private readonly string? _value;

    public string Value => _value ?? string.Empty;
    public bool IsNone => string.IsNullOrEmpty(_value);
    public static SymbolId None => default;

    public SymbolId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        _value = value;
    }

    public static SymbolId Create(params string[] components)
    {
        ArgumentNullException.ThrowIfNull(components);
        if (components.Length == 0)
            throw new ArgumentException("At least one structural component is required.", nameof(components));

        return new SymbolId("calor://" + string.Join("/", components.Select(Escape)));
    }

    public SymbolId Append(params string[] components)
    {
        ArgumentNullException.ThrowIfNull(components);
        if (components.Length == 0)
            return this;
        if (IsNone)
            return Create(components);

        return new SymbolId(Value + "/" + string.Join("/", components.Select(Escape)));
    }

    public override string ToString() => IsNone ? "<none>" : Value;

    private static string Escape(string component) =>
        Uri.EscapeDataString(component ?? throw new ArgumentNullException(nameof(component)));
}

/// <summary>
/// Defines which binder diagnostics are part of compilation semantics rather
/// than optional analysis instrumentation.
/// </summary>
public static class BindingDiagnosticPolicy
{
    public static bool IsCompilationError(Diagnostics.Diagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        if (!diagnostic.IsError)
            return false;

        return diagnostic.Code is
            Diagnostics.DiagnosticCode.DuplicateDefinition
            or Diagnostics.DiagnosticCode.DuplicateFunctionSignature
            or Diagnostics.DiagnosticCode.AmbiguousOverload
            or Diagnostics.DiagnosticCode.NoMatchingOverload
            or Diagnostics.DiagnosticCode.BindRequiresTypeOrInitializer
            or Diagnostics.DiagnosticCode.InstanceMemberInStaticContext
            // v0.15 E2 slice b — the binder's half of the effect-row rules.
            // Calor0405 (a row on a position that is not function-typed, §3.5)
            // and Calor0404 (an effect variable no enclosing declaration binds,
            // §7.3) are declaration-shape errors, not analysis instrumentation:
            // the program is ill-formed, so they must reach the author the way
            // the parser's half of Calor0405 already does. Neither can fire on a
            // program with no rows in it, and the committed corpus has none.
            or Diagnostics.DiagnosticCode.EffectRowMisplaced
            or Diagnostics.DiagnosticCode.EffectVariableScope;
    }

    public static void PropagateCompilationErrors(
        IEnumerable<Diagnostics.Diagnostic> source,
        Diagnostics.DiagnosticBag destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        foreach (var diagnostic in source.Where(IsCompilationError))
        {
            if (destination.Any(existing =>
                    existing.Code == diagnostic.Code
                    && existing.Span == diagnostic.Span
                    && existing.Message == diagnostic.Message
                    && existing.Severity == diagnostic.Severity))
            {
                continue;
            }

            destination.Add(diagnostic);
        }
    }
}

public static class SymbolSourceIdentity
{
    public static string Canonicalize(string? sourceIdentity)
    {
        if (string.IsNullOrWhiteSpace(sourceIdentity))
            return "<memory>";

        var normalized = sourceIdentity.Replace('\\', '/').Trim();
        if (normalized.StartsWith("workspace:", StringComparison.Ordinal)
            || normalized.StartsWith("canonical:", StringComparison.Ordinal))
        {
            return normalized;
        }

        if (Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
        {
            if (uri.IsFile)
                return CanonicalizePath(uri.LocalPath);

            return $"uri:{uri.Scheme}:{uri.Host}{uri.AbsolutePath}".TrimEnd('/');
        }

        return Path.IsPathRooted(normalized)
            ? CanonicalizePath(normalized)
            : normalized.TrimStart('.', '/');
    }

    private static string CanonicalizePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var projectRoot = FindProjectRoot(Path.GetDirectoryName(fullPath));
        if (projectRoot != null)
        {
            return "workspace:" + Path.GetRelativePath(projectRoot, fullPath)
                .Replace('\\', '/');
        }

        var segments = fullPath.Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        return "path:" + string.Join('/', segments.TakeLast(Math.Min(3, segments.Length)));
    }

    private static string? FindProjectRoot(string? directory)
    {
        while (!string.IsNullOrEmpty(directory))
        {
            try
            {
                if (Directory.Exists(Path.Combine(directory, ".git"))
                    || File.Exists(Path.Combine(directory, "Directory.Build.props"))
                    || File.Exists(Path.Combine(directory, "global.json")))
                {
                    return directory;
                }
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        return null;
    }
}

/// <summary>
/// Exact type identity used by overload resolution. Aliases for the same Calor
/// type normalize together, while distinct numeric and nominal types do not.
/// </summary>
public static class TypeIdentity
{
    /// <summary>
    /// Produces the metadata-style lookup identity used for declared generic
    /// types while preserving namespace and nested-type qualification.
    /// Constructed spellings such as <c>Alpha.Outer&lt;T&gt;.Inner&lt;U,V&gt;</c>
    /// become <c>Alpha.Outer`1.Inner`2</c>. An explicit arity applies to the
    /// final type segment when the caller stores generic arguments separately.
    /// </summary>
    public static string ToLookupName(string typeName, int? explicitArity = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        var type = StripLookupDecorators(typeName.Trim());
        const string globalPrefix = "global::";
        if (type.StartsWith(globalPrefix, StringComparison.Ordinal))
            type = type[globalPrefix.Length..];

        var builder = new System.Text.StringBuilder(type.Length);
        for (var index = 0; index < type.Length;)
        {
            if (type[index] != '<')
            {
                if (!char.IsWhiteSpace(type[index]))
                    builder.Append(type[index]);
                index++;
                continue;
            }

            var depth = 0;
            var arity = 1;
            var close = -1;
            for (var nested = index + 1; nested < type.Length; nested++)
            {
                switch (type[nested])
                {
                    case '<':
                        depth++;
                        break;
                    case '>':
                        if (depth == 0)
                        {
                            close = nested;
                        }
                        else
                        {
                            depth--;
                        }
                        break;
                    case ',' when depth == 0:
                        arity++;
                        break;
                }

                if (close >= 0)
                    break;
            }

            if (close < 0)
            {
                builder.Append(type[index..]);
                break;
            }

            builder.Append('`').Append(arity);
            index = close + 1;
        }

        var lookupName = builder.ToString();
        if (explicitArity is > 0)
        {
            var finalSeparator = lookupName.LastIndexOf('.');
            var finalSegment = lookupName[(finalSeparator + 1)..];
            if (!finalSegment.Contains('`'))
                lookupName += $"`{explicitArity.Value}";
        }

        return lookupName;
    }

    public static bool TryUnwrapOptionOrNullable(string typeName, out string elementType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        var type = typeName.Trim();

        if (type.StartsWith("?", StringComparison.Ordinal) && type.Length > 1)
        {
            elementType = type[1..];
            return true;
        }

        if (type.EndsWith("?", StringComparison.Ordinal) && type.Length > 1)
        {
            elementType = type[..^1];
            return true;
        }

        const string expandedPrefix = "OPTION[inner=";
        if (type.StartsWith(expandedPrefix, StringComparison.OrdinalIgnoreCase)
            && type.EndsWith(']'))
        {
            elementType = type[expandedPrefix.Length..^1];
            return true;
        }

        if (TrySplitGeneric(type, out var genericName, out var arguments)
            && genericName.Equals("Option", StringComparison.OrdinalIgnoreCase)
            && arguments.Count == 1)
        {
            elementType = arguments[0];
            return true;
        }

        elementType = string.Empty;
        return false;
    }

    private static string StripLookupDecorators(string type)
    {
        while (type.StartsWith('?') && type.Length > 1)
            type = type[1..].TrimStart();

        while (type.Length > 1)
        {
            if (type.EndsWith('?') || type.EndsWith('*'))
            {
                type = type[..^1].TrimEnd();
                continue;
            }

            if (!type.EndsWith(']'))
                break;

            var openBracket = type.LastIndexOf('[');
            if (openBracket <= 0
                || type[(openBracket + 1)..^1].Any(character => character != ','))
            {
                break;
            }

            type = type[..openBracket].TrimEnd();
        }

        return type;
    }

    public static string Canonicalize(string typeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        var type = typeName.Trim();

        if (TryUnwrapOptionOrNullable(type, out var optionElement))
            return $"OPTION<{Canonicalize(optionElement)}>";

        if (TryStripPostfix(type, out var elementType, out var postfix))
            return Canonicalize(elementType) + postfix;

        if (TrySplitGeneric(type, out var genericName, out var arguments))
        {
            var canonicalName = genericName.Equals("Option", StringComparison.OrdinalIgnoreCase)
                ? "OPTION"
                : genericName.Trim();
            return $"{canonicalName}<{string.Join(",", arguments.Select(Canonicalize))}>";
        }

        return type.ToLowerInvariant() switch
        {
            "i8" or "sbyte" or "int[bits=8][signed=true]" => "INT[bits=8][signed=true]",
            "u8" or "byte" or "int[bits=8][signed=false]" => "INT[bits=8][signed=false]",
            "i16" or "short" or "int[bits=16][signed=true]" => "INT[bits=16][signed=true]",
            "u16" or "ushort" or "int[bits=16][signed=false]" => "INT[bits=16][signed=false]",
            "i32" or "int" or "int32" or "int[bits=32][signed=true]" => "INT",
            "u32" or "uint" or "uint32" or "int[bits=32][signed=false]" => "UINT",
            "i64" or "long" or "int64" or "int[bits=64][signed=true]" => "LONG",
            "u64" or "ulong" or "uint64" or "int[bits=64][signed=false]" => "ULONG",
            "f32" or "single" or "float[bits=32]" => "FLOAT[bits=32]",
            "f64" or "float" or "double" => "FLOAT",
            "dec" or "decimal" => "DECIMAL",
            "str" or "string" => "STRING",
            "bool" or "boolean" => "BOOL",
            "any" or "object" or "unknown" => "OBJECT",
            "void" => "VOID",
            "never" => "NEVER",
            _ => type,
        };
    }

    /// <summary>
    /// The canonical spellings <see cref="Canonicalize(string)"/> produces for
    /// the built-in types — the ones that provably cannot be a function type.
    /// </summary>
    private static readonly HashSet<string> ProvablyNonFunctionCanonicalNames = new(StringComparer.Ordinal)
    {
        "INT", "UINT", "LONG", "ULONG", "DECIMAL", "STRING", "BOOL", "OBJECT", "VOID", "NEVER",
        "FLOAT", "FLOAT[bits=32]",
        "INT[bits=8][signed=true]", "INT[bits=8][signed=false]",
        "INT[bits=16][signed=true]", "INT[bits=16][signed=false]",
        "INT[bits=32][signed=true]", "INT[bits=32][signed=false]",
        "INT[bits=64][signed=true]", "INT[bits=64][signed=false]",
    };

    /// <summary>
    /// v0.15 E3 slice a, review round 1 (F2) — "is this type PROVABLY not a
    /// function type?", the conservative complement of
    /// <see cref="IsFunctionTypeName"/>.
    ///
    /// <para>Calor0405 (§3.5) says a row on a non-function-typed position is
    /// misplaced. It was implemented as <c>!IsFunctionTypeName(…)</c>, which is
    /// the wrong complement: that predicate is a LIST of known function-type
    /// spellings, so everything it has not heard of — an aliased delegate, a
    /// delegate declared in a <c>§CSHARP</c> block, a type parameter that
    /// resolves to a delegate — answered "not a function type" and drew a HARD
    /// ERROR on a legally-declared delegate. Measured on the frozen PP-E1 fixture
    /// <c>docs/design/spikes/effect-rows/after/A2.calr</c>, whose
    /// <c>RequestHandlerDelegate&lt;TResponse&gt;</c> is declared inside a
    /// <c>§CSHARP</c> block: 2× Calor0405, and PP-E1's row bars that code
    /// anywhere in a control compile.</para>
    ///
    /// <para>So the rule is inverted to fail OPEN. A row is misplaced only when
    /// the compiler can PROVE the position cannot carry one — a built-in scalar,
    /// <c>void</c>, an array, a tuple, a pointer, or an <c>Option</c>. An unknown
    /// NOMINAL is never Calor0405: the honest answer there is "I do not know what
    /// this is", and §4.3's whole discipline is that an unknown is not a
    /// verdict.</para>
    ///
    /// <para>Every case P6 pins — <c>i32</c>, <c>str</c>, <c>void</c> at all four
    /// positions — is a built-in and still reports.</para>
    /// </summary>
    public static bool IsProvablyNonFunctionType(string? typeName)
    {
        // No type at all is "nothing is known" and must stay silent.
        if (typeName is null) return false;

        // A type position the author left BLANK (`§FLD{:c:pri}`) is different:
        // there is no type for a row to attach to, so the row is provably
        // misplaced. This is the R2-A case PR #1102's review round 2 added, and
        // it must keep reporting rather than fall through the fail-open rule.
        if (typeName.Trim().Length == 0) return true;

        var trimmed = typeName.Trim().TrimEnd('?');
        if (trimmed.Length == 0) return false;

        // Arrays, pointers and tuples: shapes, not names, and none of them is a
        // delegate however the element type is spelled.
        if (trimmed.EndsWith("]", StringComparison.Ordinal)
            || trimmed.EndsWith("*", StringComparison.Ordinal)
            || trimmed.StartsWith("(", StringComparison.Ordinal))
        {
            return !trimmed.StartsWith("(", StringComparison.Ordinal) || trimmed.Contains(',');
        }

        var canonical = Canonicalize(trimmed);
        return ProvablyNonFunctionCanonicalNames.Contains(canonical)
            || canonical.StartsWith("OPTION<", StringComparison.Ordinal);
    }

    /// <summary>
    /// v0.15 E3 slice a, review round 1 (F2) — the delegate names declared inside
    /// <c>§CSHARP</c> interop text. A <c>§CSHARP</c> block is opaque C#, but a
    /// <c>delegate</c> declaration in it introduces a real type that the rest of
    /// the module may then use as a parameter, field or return spelling — and
    /// once it does, that position IS function-typed and may carry a row.
    ///
    /// <para>Deliberately a textual scan and not a Roslyn parse: the block is
    /// preserved verbatim precisely because the compiler does not model it, and
    /// pulling Roslyn into the binder to read one identifier would be a far
    /// larger dependency than the problem. A missed name degrades to "unknown
    /// nominal", which after <see cref="IsProvablyNonFunctionType"/> is silent
    /// rather than wrong.</para>
    /// </summary>
    public static IEnumerable<string> DelegateNamesDeclaredInInteropText(string? csharpCode)
    {
        if (string.IsNullOrWhiteSpace(csharpCode)) yield break;

        foreach (System.Text.RegularExpressions.Match match in InteropDelegateDeclaration.Matches(csharpCode))
        {
            var name = match.Groups["name"].Value;
            if (name.Length > 0)
                yield return name;
        }
    }

    // `delegate <return type> <Name>[<type args>](` — the return type may itself
    // be generic (`Task<TResponse>`), so it is matched lazily and the NAME is the
    // last identifier before the parameter list.
    private static readonly System.Text.RegularExpressions.Regex InteropDelegateDeclaration =
        new(@"\bdelegate\s+[^;{}()]+?\b(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?:<[^<>()]*>)?\s*\(",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    public static string CanonicalizeSignature(
        string typeName,
        IReadOnlyList<string> typeParameters)
    {
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < typeParameters.Count; index++)
            replacements.TryAdd(typeParameters[index], $"!{index}");
        return Canonicalize(ReplaceIdentifiers(typeName, replacements));
    }

    public static bool TryUnify(
        string parameterType,
        string argumentType,
        IReadOnlySet<string> typeParameters,
        IDictionary<string, string> substitutions)
    {
        return TryUnifyCanonical(
            Canonicalize(parameterType),
            Canonicalize(argumentType),
            typeParameters,
            substitutions);
    }

    public static string Substitute(string typeName, IReadOnlyDictionary<string, string> substitutions) =>
        Canonicalize(ReplaceIdentifiers(typeName, substitutions));

    private static bool TryUnifyCanonical(
        string parameterType,
        string argumentType,
        IReadOnlySet<string> typeParameters,
        IDictionary<string, string> substitutions)
    {
        if (typeParameters.Contains(parameterType))
        {
            if (substitutions.TryGetValue(parameterType, out var existing))
                return string.Equals(existing, argumentType, StringComparison.Ordinal);

            substitutions[parameterType] = argumentType;
            return true;
        }

        if (string.Equals(parameterType, argumentType, StringComparison.Ordinal))
            return true;

        if (TryStripPostfix(parameterType, out var parameterElement, out var parameterPostfix)
            && TryStripPostfix(argumentType, out var argumentElement, out var argumentPostfix)
            && string.Equals(parameterPostfix, argumentPostfix, StringComparison.Ordinal))
        {
            return TryUnifyCanonical(
                Canonicalize(parameterElement),
                Canonicalize(argumentElement),
                typeParameters,
                substitutions);
        }

        if (!TrySplitGeneric(parameterType, out var parameterName, out var parameterArguments)
            || !TrySplitGeneric(argumentType, out var argumentName, out var argumentArguments)
            || !string.Equals(parameterName, argumentName, StringComparison.Ordinal)
            || parameterArguments.Count != argumentArguments.Count)
        {
            return false;
        }

        for (var index = 0; index < parameterArguments.Count; index++)
        {
            if (!TryUnifyCanonical(
                    Canonicalize(parameterArguments[index]),
                    Canonicalize(argumentArguments[index]),
                    typeParameters,
                    substitutions))
            {
                return false;
            }
        }

        return true;
    }

    private static string ReplaceIdentifiers(
        string typeName,
        IReadOnlyDictionary<string, string> replacements)
    {
        if (replacements.Count == 0)
            return typeName;

        var builder = new System.Text.StringBuilder(typeName.Length);
        for (var index = 0; index < typeName.Length;)
        {
            if (!char.IsLetter(typeName[index]) && typeName[index] != '_')
            {
                builder.Append(typeName[index++]);
                continue;
            }

            var start = index++;
            while (index < typeName.Length
                   && (char.IsLetterOrDigit(typeName[index]) || typeName[index] == '_'))
            {
                index++;
            }

            var identifier = typeName[start..index];
            builder.Append(replacements.TryGetValue(identifier, out var replacement)
                ? replacement
                : identifier);
        }

        return builder.ToString();
    }

    private static bool TryStripPostfix(string type, out string elementType, out string postfix)
    {
        if (type.EndsWith('*'))
        {
            elementType = type[..^1];
            postfix = "*";
            return true;
        }

        if (type.EndsWith("?", StringComparison.Ordinal))
        {
            elementType = type[..^1];
            postfix = "?";
            return true;
        }

        if (!type.EndsWith(']'))
        {
            elementType = string.Empty;
            postfix = string.Empty;
            return false;
        }

        var openBracket = type.LastIndexOf('[');
        if (openBracket <= 0)
        {
            elementType = string.Empty;
            postfix = string.Empty;
            return false;
        }

        var candidate = type[openBracket..];
        if (candidate[1..^1].Any(character => character != ','))
        {
            elementType = string.Empty;
            postfix = string.Empty;
            return false;
        }

        elementType = type[..openBracket];
        postfix = candidate;
        return true;
    }

    /// <summary>
    /// Splits <c>Func&lt;i32, str&gt;</c> into <c>Func</c> and <c>[i32, str]</c>,
    /// respecting nesting. <c>internal</c> since v0.15 E2 slice b so the binder
    /// can read a function type's arity off its declared spelling when building
    /// a rowed <c>FunctionBoundType</c>.
    /// </summary>
    internal static bool TrySplitGeneric(
        string type,
        out string genericName,
        out IReadOnlyList<string> arguments)
    {
        var open = type.IndexOf('<');
        if (open <= 0 || !type.EndsWith('>'))
        {
            genericName = string.Empty;
            arguments = Array.Empty<string>();
            return false;
        }

        var depth = 0;
        var start = open + 1;
        var result = new List<string>();
        for (var index = open + 1; index < type.Length - 1; index++)
        {
            switch (type[index])
            {
                case '<':
                    depth++;
                    break;
                case '>':
                    depth--;
                    if (depth < 0)
                    {
                        genericName = string.Empty;
                        arguments = Array.Empty<string>();
                        return false;
                    }
                    break;
                case ',' when depth == 0:
                    result.Add(type[start..index].Trim());
                    start = index + 1;
                    break;
            }
        }

        if (depth != 0)
        {
            genericName = string.Empty;
            arguments = Array.Empty<string>();
            return false;
        }

        result.Add(type[start..^1].Trim());
        genericName = type[..open].Trim();
        arguments = result;
        return true;
    }

    /// <summary>
    /// Maps common short type names to fully-qualified names for manifest resolution.
    /// Used by both ParseCallTarget (in EffectInferrer) and ParseCallTargetForChain.
    /// </summary>
    /// <remarks>
    /// v0.15 E1 slice 2b — moved here from <c>Effects/EffectEnforcementPass.cs</c>.
    /// The binder needs this expansion (a short BCL receiver spelling such as
    /// <c>Random</c> must reach <c>System.Random</c>) and <c>Binding/</c> is not
    /// allowed to reference <c>Effects/</c>; the pass keeps an internal forwarder
    /// so its own call sites are unchanged. Pure string function, no state.
    /// </remarks>
    public static string MapShortTypeNameToFullName(string shortName)
    {
        // Calor surface syntax for the runtime's Option/Result types:
        // ?T is Option<T>; T!E is Result<T,E>. Their combinators are
        // manifest-entered as pure-modulo-arguments (delegate arguments are
        // charged at the lambda definition site by the effect pass).
        if (shortName.StartsWith('?') || shortName.StartsWith("Option<"))
            return "Calor.Runtime.Option`1";
        if (shortName.StartsWith("Result<"))
            return "Calor.Runtime.Result`2";
        if (shortName.Contains('!') && !shortName.Contains('.') && !shortName.Contains('('))
            return "Calor.Runtime.Result`2";

        // Normalize declared generic collection types ("List<i32>") to their
        // manifest identities so typed receivers resolve to the correct entries
        // (e.g. List`1.Add = mut) instead of hitting the unknown-call chain.
        var genericIdx = shortName.IndexOf('<');
        if (genericIdx > 0 && shortName.EndsWith('>'))
        {
            var baseName = shortName[..genericIdx];
            var mapped = baseName switch
            {
                "List" or "System.Collections.Generic.List" =>
                    "System.Collections.Generic.List`1",
                "Dictionary" or "Dict" or "System.Collections.Generic.Dictionary" =>
                    "System.Collections.Generic.Dictionary`2",
                "HashSet" or "Set" or "System.Collections.Generic.HashSet" =>
                    "System.Collections.Generic.HashSet`1",
                "Task" or "System.Threading.Tasks.Task" =>
                    "System.Threading.Tasks.Task`1",
                _ => null
            };
            if (mapped != null)
                return mapped;
        }

        return MapKnownShortTypeName(shortName);
    }

    private static string MapKnownShortTypeName(string shortName) => shortName switch
    {
        // Calor runtime static helper classes
        "Option" => "Calor.Runtime.Option",
        "Result" => "Calor.Runtime.Result",
        // BCL types
        "Console" => "System.Console",
        // Generic collections referenced by bare name (e.g. §NEW{List<i32>}
        // stores TypeName "List" with separate type arguments)
        "List" => "System.Collections.Generic.List`1",
        "System.Collections.Generic.List" => "System.Collections.Generic.List`1",
        "Dictionary" => "System.Collections.Generic.Dictionary`2",
        "Dict" => "System.Collections.Generic.Dictionary`2",
        "System.Collections.Generic.Dictionary" => "System.Collections.Generic.Dictionary`2",
        "HashSet" => "System.Collections.Generic.HashSet`1",
        "Set" => "System.Collections.Generic.HashSet`1",
        "System.Collections.Generic.HashSet" => "System.Collections.Generic.HashSet`1",
        "File" => "System.IO.File",
        "Directory" => "System.IO.Directory",
        "Path" => "System.IO.Path",
        "StreamReader" => "System.IO.StreamReader",
        "StreamWriter" => "System.IO.StreamWriter",
        "FileStream" => "System.IO.FileStream",
        "BinaryReader" => "System.IO.BinaryReader",
        "BinaryWriter" => "System.IO.BinaryWriter",
        "Random" => "System.Random",
        "DateTime" => "System.DateTime",
        "Environment" => "System.Environment",
        "Process" => "System.Diagnostics.Process",
        "HttpClient" => "System.Net.Http.HttpClient",
        "Math" => "System.Math",
        "Guid" => "System.Guid",
        "Enumerable" => "System.Linq.Enumerable",
        "str" => "System.String",
        "string" => "System.String",
        "STRING" => "System.String",
        "String" => "System.String",
        "i32" => "System.Int32",
        "int" => "System.Int32",
        "INT" => "System.Int32",
        "Int32" => "System.Int32",
        "i64" => "System.Int64",
        "long" => "System.Int64",
        "Int64" => "System.Int64",
        "f64" => "System.Double",
        "double" => "System.Double",
        "FLOAT" => "System.Double",
        "Double" => "System.Double",
        "bool" => "System.Boolean",
        "BOOL" => "System.Boolean",
        "Boolean" => "System.Boolean",
        "Convert" => "System.Convert",
        "Array" => "System.Array",
        "StringBuilder" => "System.Text.StringBuilder",
        "Stopwatch" => "System.Diagnostics.Stopwatch",
        "Debug" => "System.Diagnostics.Debug",
        "Trace" => "System.Diagnostics.Trace",
        "Thread" => "System.Threading.Thread",
        "Task" => "System.Threading.Tasks.Task",
        "JsonSerializer" => "System.Text.Json.JsonSerializer",
        "JsonDocument" => "System.Text.Json.JsonDocument",
        "Regex" => "System.Text.RegularExpressions.Regex",
        "Exception" => "System.Exception",
        "ArgumentException" => "System.ArgumentException",
        "ArgumentNullException" => "System.ArgumentNullException",
        "ArgumentOutOfRangeException" => "System.ArgumentOutOfRangeException",
        "InvalidOperationException" => "System.InvalidOperationException",
        "NotSupportedException" => "System.NotSupportedException",
        "NotImplementedException" => "System.NotImplementedException",
        "IndexOutOfRangeException" => "System.IndexOutOfRangeException",
        "FormatException" => "System.FormatException",
        "ObjectDisposedException" => "System.ObjectDisposedException",
        // Microsoft.Extensions.Logging
        "ILogger" => "Microsoft.Extensions.Logging.ILogger",
        "LoggerExtensions" => "Microsoft.Extensions.Logging.LoggerExtensions",
        "ILoggerFactory" => "Microsoft.Extensions.Logging.ILoggerFactory",
        // Microsoft.Extensions.Configuration
        "IConfiguration" => "Microsoft.Extensions.Configuration.IConfiguration",
        "IConfigurationRoot" => "Microsoft.Extensions.Configuration.IConfigurationRoot",
        "IConfigurationSection" => "Microsoft.Extensions.Configuration.IConfigurationSection",
        "ConfigurationExtensions" => "Microsoft.Extensions.Configuration.ConfigurationExtensions",
        // Microsoft.Extensions.DependencyInjection
        "IServiceProvider" => "Microsoft.Extensions.DependencyInjection.IServiceProvider",
        "IServiceCollection" => "Microsoft.Extensions.DependencyInjection.IServiceCollection",
        "IServiceScopeFactory" => "Microsoft.Extensions.DependencyInjection.IServiceScopeFactory",
        // Microsoft.Extensions.Options
        "IOptions" => "Microsoft.Extensions.Options.IOptions`1",
        "IOptionsSnapshot" => "Microsoft.Extensions.Options.IOptionsSnapshot`1",
        "IOptionsMonitor" => "Microsoft.Extensions.Options.IOptionsMonitor`1",
        // Microsoft.Extensions.Hosting
        "IHost" => "Microsoft.Extensions.Hosting.IHost",
        "IHostBuilder" => "Microsoft.Extensions.Hosting.IHostBuilder",
        "IHostedService" => "Microsoft.Extensions.Hosting.IHostedService",
        // Microsoft.EntityFrameworkCore
        "DbContext" => "Microsoft.EntityFrameworkCore.DbContext",
        "DbSet" => "Microsoft.EntityFrameworkCore.DbSet`1",
        "DatabaseFacade" => "Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade",
        // Microsoft.AspNetCore
        "HttpContext" => "Microsoft.AspNetCore.Http.HttpContext",
        "HttpRequest" => "Microsoft.AspNetCore.Http.HttpRequest",
        "HttpResponse" => "Microsoft.AspNetCore.Http.HttpResponse",
        "ControllerBase" => "Microsoft.AspNetCore.Mvc.ControllerBase",
        "Results" => "Microsoft.AspNetCore.Http.Results",
        "TypedResults" => "Microsoft.AspNetCore.Http.TypedResults",
        // Serilog
        "Log" => "Serilog.Log",
        "SerilogLog" => "Serilog.Log",
        // Newtonsoft.Json
        "JsonConvert" => "Newtonsoft.Json.JsonConvert",
        "JObject" => "Newtonsoft.Json.Linq.JObject",
        "JArray" => "Newtonsoft.Json.Linq.JArray",
        "JToken" => "Newtonsoft.Json.Linq.JToken",
        // Dapper
        "SqlMapper" => "Dapper.SqlMapper",
        // MediatR
        "IMediator" => "MediatR.IMediator",
        "ISender" => "MediatR.ISender",
        "Mediator" => "MediatR.Mediator",
        // AutoMapper
        "IMapper" => "AutoMapper.IMapper",
        "Mapper" => "AutoMapper.Mapper",
        // FluentValidation
        "IValidator" => "FluentValidation.IValidator",
        // Polly
        "Policy" => "Polly.Policy",
        "ResiliencePipeline" => "Polly.ResiliencePipeline",
        _ => shortName
    };

    /// <summary>
    /// True when every dot-separated segment of <paramref name="receiver"/> is a
    /// capitalized identifier — the shape of a namespace/type reference written
    /// in source. Variables, fields, <c>this</c>, and member chains through them
    /// fail this test.
    ///
    /// <para>v0.15 E1 slice 2b — moved here from
    /// <c>Effects.ExternalCallCollector</c>, which is where the binder used to
    /// reach for it (PR #1095 review finding 10). The collector keeps an internal
    /// forwarder.</para>
    /// </summary>
    /// <summary>
    /// True when <paramref name="typeName"/> SPELLS a function type: one of the
    /// BCL delegate shapes, or a name <paramref name="isDeclaredDelegate"/>
    /// recognises as a <c>§DEL</c> declared in this compilation.
    ///
    /// <para>v0.15 E2 slice b — moved here from
    /// <c>EffectEnforcementPass.IsFunctionTypeName</c>, which keeps a
    /// forwarder. The binder needs the same predicate to answer §3.5's "can
    /// this position carry an effect row?" (Calor0405, pin P6), and
    /// <c>Binding/</c> may not reference <c>Effects/</c>. One list, two
    /// callers — a second copy would drift the moment either side learned a new
    /// shape.</para>
    ///
    /// <para>This is the STRING test. Structural function-typedness — a
    /// <c>FunctionBoundType</c>, or a nominal type whose declaration is a
    /// <c>§DEL</c> — is <c>EffectEnforcementPass.IsFunctionBoundType</c>, and
    /// is asked first wherever a bound type is in hand.</para>
    /// </summary>
    public static bool IsFunctionTypeName(
        string? typeName,
        Func<string, bool>? isDeclaredDelegate = null)
    {
        if (string.IsNullOrWhiteSpace(typeName)) return false;
        var t = typeName.Trim().TrimEnd('?');
        if (isDeclaredDelegate != null)
        {
            var open = t.IndexOf('<');
            var stripped = open > 0 ? t[..open] : t;
            if (stripped.Length > 0 && isDeclaredDelegate(stripped))
                return true;
        }
        return t.Equals("Action", StringComparison.Ordinal)
            || t.StartsWith("Action<", StringComparison.Ordinal)
            || t.StartsWith("Func<", StringComparison.Ordinal)
            || t.StartsWith("Predicate<", StringComparison.Ordinal)
            || t.StartsWith("Comparison<", StringComparison.Ordinal)
            || t.StartsWith("Converter<", StringComparison.Ordinal)
            || t.Equals("Delegate", StringComparison.Ordinal)
            || t.Equals("MulticastDelegate", StringComparison.Ordinal)
            || t.Equals("EventHandler", StringComparison.Ordinal)
            || t.StartsWith("EventHandler<", StringComparison.Ordinal);
    }

    public static bool IsTypeQualifiedReference(string receiver)
    {
        if (string.IsNullOrEmpty(receiver))
            return false;
        foreach (var segment in receiver.Split('.'))
        {
            if (segment.Length == 0 || !char.IsUpper(segment[0]))
                return false;
            for (var i = 1; i < segment.Length; i++)
            {
                if (!(char.IsLetterOrDigit(segment[i]) || segment[i] == '_' || segment[i] == '`'))
                    return false;
            }
        }
        return true;
    }
}

/// <summary>
/// Represents a symbol in the program (variable, function, etc.).
/// </summary>
public abstract class Symbol
{
    /// <summary>
    /// Stable, source-qualified identity used by analysis and language-server features.
    /// </summary>
    public SymbolId Id { get; }
    public string Name { get; }

    /// <summary>
    /// Exact identifier token span used for definition and rename edits.
    /// </summary>
    public TextSpan DeclarationSpan { get; }
    public TextSpan IdentifierSpan => DeclarationSpan;
    public TextSpan DefinitionSpan { get; }
    public ConditionalAlternative? ConditionalAlternative { get; }
    public string IdentityKey => Id.IsNone ? Name : Id.Value;

    protected Symbol(
        SymbolId id,
        string name,
        TextSpan declarationSpan,
        TextSpan? definitionSpan = null,
        ConditionalAlternative? conditionalAlternative = null)
    {
        Id = id;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        DeclarationSpan = declarationSpan;
        DefinitionSpan = definitionSpan ?? declarationSpan;
        ConditionalAlternative = conditionalAlternative;
    }
}

/// <summary>
/// Represents a variable symbol.
/// </summary>
public sealed class VariableSymbol : Symbol
{
    public string TypeName { get; }
    public bool IsMutable { get; }
    public bool IsParameter { get; }
    public bool IsField { get; }
    public bool IsProperty { get; }
    public bool IsStatic { get; }
    public ParameterModifier Modifier { get; }
    public ExpressionNode? DefaultValue { get; }
    public bool IsOptional => DefaultValue != null;
    public Visibility Visibility { get; }
    public string? DeclaringTypeName { get; }

    /// <summary>
    /// v0.14 nullability workstream — declared nullability of the variable's
    /// type, captured at binding time from the surface syntax (e.g. <c>:string</c>
    /// vs <c>:?string</c>). Consumed by <see cref="BoundVariableExpression.Type"/>
    /// so that reads of a declared-non-null local flow the <c>NotAnnotated</c>
    /// annotation into downstream nullability checks (Calor0272 and siblings).
    ///
    /// <para>Defaults to <see cref="BoundTypes.NullableAnnotation.Oblivious"/>
    /// so pre-existing symbol construction paths keep their conservative
    /// behavior. S3-scoped: today only STRING-target bind statements populate
    /// a non-<c>Oblivious</c> value; other targets stay Oblivious per §D6.</para>
    /// </summary>
    public BoundTypes.NullableAnnotation NullableAnnotation { get; }

    /// <summary>
    /// v0.15 E2 slice b, design-doc §8.2 — the function type this variable
    /// denotes, when it denotes one AND an effect row is attached to it. Set for
    /// a rowed function-typed parameter, field or <c>§B</c>, and for a <c>§B</c>
    /// whose row is INFERRED from a function-typed initializer (§3.5).
    ///
    /// <para><c>null</c> for everything else, including a row-less
    /// <c>Func&lt;i32,i32&gt;</c> parameter — whose row is
    /// <see cref="BoundTypes.EffectRow.Unknown"/> by §3.5, and which E3 will
    /// widen this property to cover once it has a checking site to serve. Slice
    /// b deliberately does not build one for every function-typed position,
    /// because doing so would make <c>EffectEnforcementPass.IsFunctionBoundType</c>
    /// answer true where it answers false today and move Calor0418's
    /// behaviour on programs that have no rows at all.</para>
    ///
    /// <para><see cref="TypeName"/> is untouched, and the function type's
    /// <c>DisplayString</c> is the declared spelling, so nothing that reads
    /// either moves.</para>
    /// </summary>
    public BoundTypes.FunctionBoundType? FunctionType { get; }

    public VariableSymbol(
        SymbolId id,
        string name,
        string typeName,
        bool isMutable,
        bool isParameter = false,
        ParameterModifier modifier = ParameterModifier.None,
        TextSpan declarationSpan = default,
        ExpressionNode? defaultValue = null,
        Visibility visibility = Visibility.Public,
        string? declaringTypeName = null,
        bool isField = false,
        bool isProperty = false,
        bool isStatic = false,
        ConditionalAlternative? conditionalAlternative = null,
        BoundTypes.NullableAnnotation nullableAnnotation = BoundTypes.NullableAnnotation.Oblivious,
        BoundTypes.FunctionBoundType? functionType = null)
        : base(id, name, declarationSpan, conditionalAlternative: conditionalAlternative)
    {
        TypeName = typeName ?? throw new ArgumentNullException(nameof(typeName));
        IsMutable = isMutable;
        IsParameter = isParameter;
        IsField = isField;
        IsProperty = isProperty;
        IsStatic = isStatic;
        Modifier = modifier;
        DefaultValue = defaultValue;
        Visibility = visibility;
        DeclaringTypeName = declaringTypeName;
        NullableAnnotation = nullableAnnotation;
        FunctionType = functionType;
    }

    public VariableSymbol(
        string name,
        string typeName,
        bool isMutable,
        bool isParameter = false,
        ParameterModifier modifier = ParameterModifier.None)
        : this(SymbolId.None, name, typeName, isMutable, isParameter, modifier)
    {
    }
}

/// <summary>
/// Represents a function symbol.
/// </summary>
public sealed class FunctionSymbol : Symbol
{
    public string ReturnType { get; }

    /// <summary>
    /// v0.15 E2 slice b, design-doc §8.2 — the RETURN's function type when the
    /// declaration writes a row on it (<c>§O{Func&lt;i32&gt;} §E{cw}</c> or
    /// <c>-&gt; Func&lt;i32&gt; §E{cw}</c>, position 6). <c>null</c> otherwise;
    /// <see cref="ReturnType"/> is unchanged either way.
    /// </summary>
    public BoundTypes.FunctionBoundType? ReturnFunctionType { get; init; }
    public IReadOnlyList<VariableSymbol> Parameters { get; }
    public IReadOnlyList<string> TypeParameters { get; }
    public Visibility Visibility { get; }
    public string? ContainingTypeName { get; }
    public int GenericArity => TypeParameters.Count;

    public string SignatureKey => string.Join(
        "|",
        GenericArity,
        string.Join(
            ";",
            Parameters.Select(parameter =>
                $"{(int)parameter.Modifier}:{TypeIdentity.CanonicalizeSignature(parameter.TypeName, TypeParameters)}")));

    public string DisplaySignature
    {
        get
        {
            var generic = GenericArity == 0 || Name.EndsWith(">", StringComparison.Ordinal)
                ? string.Empty
                : $"<{string.Join(",", TypeParameters)}>";
            var parameters = string.Join(
                ", ",
                Parameters.Select(parameter =>
                    $"{FormatModifier(parameter.Modifier)}{parameter.TypeName}"));
            return $"{Name}{generic}({parameters})";
        }
    }

    public FunctionSymbol(
        SymbolId id,
        string name,
        string returnType,
        IReadOnlyList<VariableSymbol> parameters,
        IReadOnlyList<string>? typeParameters = null,
        TextSpan declarationSpan = default,
        Visibility visibility = Visibility.Public,
        string? containingTypeName = null,
        TextSpan? definitionSpan = null,
        ConditionalAlternative? conditionalAlternative = null)
        : base(id, name, declarationSpan, definitionSpan, conditionalAlternative)
    {
        ReturnType = returnType ?? throw new ArgumentNullException(nameof(returnType));
        Parameters = parameters?.ToArray() ?? throw new ArgumentNullException(nameof(parameters));
        TypeParameters = typeParameters?.ToArray() ?? Array.Empty<string>();
        Visibility = visibility;
        ContainingTypeName = containingTypeName;
    }

    public FunctionSymbol(
        string name,
        string returnType,
        IReadOnlyList<VariableSymbol> parameters,
        IReadOnlyList<string>? typeParameters = null)
        : this(SymbolId.None, name, returnType, parameters, typeParameters)
    {
    }

    private static string FormatModifier(ParameterModifier modifier)
    {
        var callModifier = modifier & (ParameterModifier.Ref | ParameterModifier.Out | ParameterModifier.In);
        return callModifier switch
        {
            ParameterModifier.Ref => "ref ",
            ParameterModifier.Out => "out ",
            ParameterModifier.In => "in ",
            _ when modifier.HasFlag(ParameterModifier.Params) => "params ",
            _ when modifier.HasFlag(ParameterModifier.This) => "this ",
            _ => string.Empty,
        };
    }
}

public readonly record struct ConditionalAlternative(string GroupId, int BranchIndex)
{
    public bool IsMutuallyExclusiveWith(ConditionalAlternative other) =>
        string.Equals(GroupId, other.GroupId, StringComparison.Ordinal)
        && BranchIndex != other.BranchIndex;
}

public sealed class TypeSymbol : Symbol
{
    public string QualifiedName { get; }
    public Visibility Visibility { get; }

    /// <summary>
    /// v0.15 E1 slice 2b — true for a type declared with <c>§DEL</c>. This is
    /// how a <see cref="BoundTypes.NominalBoundType"/> gets marked as
    /// delegate-typed, so consumers can answer "is this a function type?"
    /// structurally instead of prefix-matching <c>"Func&lt;"</c> on a type name
    /// (see <c>EffectEnforcementPass.IsFunctionBoundType</c>). Only reachable
    /// when the nominal type carries its <c>Declaration</c>; a receiver whose
    /// BoundType was built from a bare type string still falls back to the
    /// string test.
    /// </summary>
    public bool IsDelegate { get; }

    public TypeSymbol(
        SymbolId id,
        string name,
        string qualifiedName,
        Visibility visibility,
        TextSpan declarationSpan,
        TextSpan? definitionSpan = null,
        bool isDelegate = false)
        : base(id, name, declarationSpan, definitionSpan)
    {
        QualifiedName = qualifiedName ?? throw new ArgumentNullException(nameof(qualifiedName));
        Visibility = visibility;
        IsDelegate = isDelegate;
    }
}

public enum OverloadResolutionKind
{
    NotFound,
    Inaccessible,
    NoMatch,
    Ambiguous,
    Resolved,
}

public sealed class OverloadResolutionResult
{
    public OverloadResolutionKind Kind { get; }
    public FunctionSymbol? Function { get; }
    public string? ResolvedReturnType { get; }
    public IReadOnlyList<FunctionSymbol> Candidates { get; }
    public IReadOnlyList<FunctionSymbol> Functions { get; }

    private OverloadResolutionResult(
        OverloadResolutionKind kind,
        FunctionSymbol? function,
        string? resolvedReturnType,
        IReadOnlyList<FunctionSymbol> candidates,
        IReadOnlyList<FunctionSymbol>? functions = null)
    {
        Kind = kind;
        Function = function;
        ResolvedReturnType = resolvedReturnType;
        Candidates = candidates;
        Functions = functions ?? Array.Empty<FunctionSymbol>();
    }

    public static OverloadResolutionResult NotFound() =>
        new(OverloadResolutionKind.NotFound, null, null, Array.Empty<FunctionSymbol>());

    public static OverloadResolutionResult Inaccessible(
        IReadOnlyList<FunctionSymbol> candidates) =>
        new(OverloadResolutionKind.Inaccessible, null, null, candidates);

    public static OverloadResolutionResult NoMatch(IReadOnlyList<FunctionSymbol> candidates) =>
        new(OverloadResolutionKind.NoMatch, null, null, candidates);

    public static OverloadResolutionResult Ambiguous(IReadOnlyList<FunctionSymbol> candidates) =>
        new(OverloadResolutionKind.Ambiguous, null, null, candidates);

    public static OverloadResolutionResult Resolved(FunctionSymbol function, string returnType) =>
        new(OverloadResolutionKind.Resolved, function, returnType, [function], [function]);

    public static OverloadResolutionResult ResolvedAlternatives(
        IReadOnlyList<FunctionSymbol> functions,
        string returnType)
    {
        ArgumentNullException.ThrowIfNull(functions);
        if (functions.Count == 0)
            throw new ArgumentException("At least one resolved function is required.", nameof(functions));

        return new(
            OverloadResolutionKind.Resolved,
            functions[0],
            returnType,
            functions,
            functions);
    }
}

/// <summary>
/// Represents a scope for variable and symbol resolution.
/// </summary>
public sealed class Scope
{
    private readonly Dictionary<string, Symbol> _symbols = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Symbol>> _symbolAlternatives = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<FunctionSymbol>> _overloadSets = new(StringComparer.Ordinal);
    public Scope? Parent { get; }

    public Scope(Scope? parent = null)
    {
        Parent = parent;
    }

    public bool TryDeclare(Symbol symbol)
    {
        if (_symbols.TryGetValue(symbol.Name, out var existing))
        {
            var existingSymbols = _symbolAlternatives.TryGetValue(symbol.Name, out var alternatives)
                ? alternatives
                : [existing];
            if (existingSymbols.All(candidate => AreMutuallyExclusiveAlternatives(candidate, symbol)))
            {
                if (alternatives == null)
                {
                    alternatives = [existing];
                    _symbolAlternatives.Add(symbol.Name, alternatives);
                }
                alternatives.Add(symbol);
                return true;
            }

            return false;
        }

        _symbols[symbol.Name] = symbol;
        return true;
    }

    public bool DeclareOverload(FunctionSymbol symbol) =>
        TryDeclareOverload(symbol.Name, symbol, out _);

    /// <summary>
    /// Declares a function under a lookup name. Duplicate signatures are rejected
    /// unless every same-signature declaration is in a different branch of the
    /// same conditional-compilation group.
    /// </summary>
    public bool TryDeclareOverload(
        string lookupName,
        FunctionSymbol symbol,
        out FunctionSymbol? duplicate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lookupName);
        ArgumentNullException.ThrowIfNull(symbol);
        duplicate = null;

        if (_symbols.TryGetValue(lookupName, out var existing) && existing is not FunctionSymbol)
            return false;

        if (!_overloadSets.TryGetValue(lookupName, out var overloads))
        {
            overloads = new List<FunctionSymbol>();
            _overloadSets[lookupName] = overloads;
        }

        duplicate = overloads.FirstOrDefault(candidate =>
            string.Equals(candidate.SignatureKey, symbol.SignatureKey, StringComparison.Ordinal)
            && !AreMutuallyExclusiveAlternatives(candidate, symbol));
        if (duplicate != null)
            return false;

        overloads.Add(symbol);
        _symbols.TryAdd(lookupName, symbol);
        return true;
    }

    public Symbol? Lookup(string name)
    {
        if (_symbols.TryGetValue(name, out var symbol))
        {
            return symbol;
        }

        return Parent?.Lookup(name);
    }

    public IReadOnlyList<Symbol> LookupAll(string name)
    {
        if (_symbolAlternatives.TryGetValue(name, out var alternatives))
            return alternatives;
        if (_symbols.TryGetValue(name, out var symbol))
            return [symbol];
        return Parent?.LookupAll(name) ?? Array.Empty<Symbol>();
    }

    public IReadOnlyList<Symbol> LookupAllLocal(string name)
    {
        if (_symbolAlternatives.TryGetValue(name, out var alternatives))
            return alternatives;
        return _symbols.TryGetValue(name, out var symbol)
            ? [symbol]
            : Array.Empty<Symbol>();
    }

    /// <summary>
    /// Compatibility lookup that returns a function only when exactly one overload
    /// has the requested arity. It never falls back to an incompatible declaration.
    /// </summary>
    public FunctionSymbol? LookupByArity(string name, int argCount)
    {
        if (_overloadSets.TryGetValue(name, out var overloads))
        {
            var matches = overloads.Where(function => function.Parameters.Count == argCount).Take(2).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }

        if (_symbols.ContainsKey(name))
            return null;

        return Parent?.LookupByArity(name, argCount);
    }

    public OverloadResolutionResult ResolveOverload(
        string name,
        IReadOnlyList<string> argumentTypes,
        IReadOnlyList<string?>? argumentNames = null,
        IReadOnlyList<string?>? argumentModifiers = null,
        IReadOnlyList<string>? typeArguments = null,
        Func<string, string, int?>? implicitConversionCost = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(argumentTypes);

        if (!_overloadSets.TryGetValue(name, out var overloads))
        {
            if (_symbols.ContainsKey(name))
                return OverloadResolutionResult.NotFound();
            return Parent?.ResolveOverload(
                    name,
                    argumentTypes,
                    argumentNames,
                    argumentModifiers,
                    typeArguments,
                    implicitConversionCost)
                ?? OverloadResolutionResult.NotFound();
        }

        var applicable = new List<(FunctionSymbol Function, string ReturnType, int Score)>();
        foreach (var function in overloads)
        {
            if (TryMatch(
                    function,
                    argumentTypes,
                    argumentNames,
                    argumentModifiers,
                    typeArguments,
                    implicitConversionCost,
                    out var resolvedReturnType,
                    out var score))
            {
                applicable.Add((function, resolvedReturnType, score));
            }
        }

        if (applicable.Count == 0)
            return OverloadResolutionResult.NoMatch(overloads);

        var bestScore = applicable.Min(item => item.Score);
        var best = applicable.Where(item => item.Score == bestScore).ToArray();
        if (best.Length == 1)
            return OverloadResolutionResult.Resolved(best[0].Function, best[0].ReturnType);

        var bestFunctions = best.Select(item => item.Function).ToArray();
        if (bestFunctions
            .Skip(1)
            .All(candidate =>
                string.Equals(
                    candidate.SignatureKey,
                    bestFunctions[0].SignatureKey,
                    StringComparison.Ordinal)
                && bestFunctions
                    .Where(other => !ReferenceEquals(other, candidate))
                    .All(other => AreMutuallyExclusiveAlternatives(candidate, other))))
        {
            var alternativeReturnTypes = best
                .Select(item => TypeIdentity.Canonicalize(item.ReturnType))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            return OverloadResolutionResult.ResolvedAlternatives(
                bestFunctions,
                alternativeReturnTypes.Length == 1
                    ? alternativeReturnTypes[0]
                    : "OBJECT");
        }

        return OverloadResolutionResult.Ambiguous(bestFunctions);
    }

    public IReadOnlyList<FunctionSymbol> GetOverloads(string name)
    {
        if (_overloadSets.TryGetValue(name, out var overloads))
            return overloads;
        if (_symbols.ContainsKey(name))
            return Array.Empty<FunctionSymbol>();
        return Parent?.GetOverloads(name) ?? Array.Empty<FunctionSymbol>();
    }

    public bool TryLookup(string name, out Symbol? symbol)
    {
        symbol = Lookup(name);
        return symbol != null;
    }

    public Symbol? LookupLocal(string name)
    {
        return _symbols.TryGetValue(name, out var symbol) ? symbol : null;
    }

    public IEnumerable<Symbol> GetDeclaredSymbols()
    {
        foreach (var (name, symbol) in _symbols)
        {
            if (_symbolAlternatives.TryGetValue(name, out var alternatives))
            {
                foreach (var alternative in alternatives)
                    yield return alternative;
            }
            else
            {
                yield return symbol;
            }
        }
    }

    public Scope CreateChild()
    {
        return new Scope(this);
    }

    private static bool AreMutuallyExclusiveAlternatives(
        Symbol left,
        Symbol right) =>
        left.ConditionalAlternative is { } leftAlternative
        && right.ConditionalAlternative is { } rightAlternative
        && leftAlternative.IsMutuallyExclusiveWith(rightAlternative);

    /// <summary>
    /// Gets all symbols visible from this scope (including parent scopes).
    /// </summary>
    public IEnumerable<Symbol> GetAllVisibleSymbols()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var current = this;
        while (current != null)
        {
            foreach (var symbol in current._symbols)
            {
                if (seen.Add(symbol.Key))
                {
                    yield return symbol.Value;
                }
            }
            current = current.Parent;
        }
    }

    /// <summary>
    /// Finds the most similar symbol name using Levenshtein distance.
    /// Returns null if no sufficiently similar name is found.
    /// </summary>
    public string? FindSimilarName(string name, int maxDistance = 2)
    {
        string? bestMatch = null;
        var bestDistance = int.MaxValue;

        foreach (var symbol in GetAllVisibleSymbols())
        {
            var distance = LevenshteinDistance(name, symbol.Name);
            if (distance <= maxDistance && distance < bestDistance)
            {
                bestDistance = distance;
                bestMatch = symbol.Name;
            }
        }

        return bestMatch;
    }

    private static bool TryMatch(
        FunctionSymbol function,
        IReadOnlyList<string> argumentTypes,
        IReadOnlyList<string?>? argumentNames,
        IReadOnlyList<string?>? argumentModifiers,
        IReadOnlyList<string>? typeArguments,
        Func<string, string, int?>? implicitConversionCost,
        out string resolvedReturnType,
        out int score)
    {
        resolvedReturnType = function.ReturnType;
        score = int.MaxValue;

        if (typeArguments != null && function.GenericArity != typeArguments.Count)
            return false;
        if (typeArguments == null && function.GenericArity == 0)
        {
            // Non-generic candidate; no substitutions are needed.
        }

        var typeParameterSet = function.TypeParameters.ToHashSet(StringComparer.Ordinal);
        foreach (var mapping in MapArguments(function, argumentTypes.Count, argumentNames))
        {
            var substitutions = new Dictionary<string, string>(StringComparer.Ordinal);
            if (typeArguments != null)
            {
                for (var index = 0; index < typeArguments.Count; index++)
                    substitutions[function.TypeParameters[index]] = TypeIdentity.Canonicalize(typeArguments[index]);
            }

            var matches = true;
            var conversionScore = 0;
            for (var argumentIndex = 0; argumentIndex < argumentTypes.Count; argumentIndex++)
            {
                var parameter = function.Parameters[mapping.ParameterMap[argumentIndex]];
                if (!ModifiersMatch(parameter.Modifier, GetArgumentModifier(argumentModifiers, argumentIndex)))
                {
                    matches = false;
                    break;
                }

                var parameterType = mapping.ExpandedParams[argumentIndex]
                    ? GetParamsElementType(parameter.TypeName)
                    : parameter.TypeName;
                if (parameterType == null)
                {
                    matches = false;
                    break;
                }

                if (TypeIdentity.TryUnify(
                        parameterType,
                        argumentTypes[argumentIndex],
                        typeParameterSet,
                        substitutions))
                {
                    continue;
                }

                var resolvedParameterType = substitutions.Count == 0
                    ? parameterType
                    : TypeIdentity.Substitute(parameterType, substitutions);
                var conversionCost = implicitConversionCost?.Invoke(
                    resolvedParameterType,
                    argumentTypes[argumentIndex]);
                if (conversionCost == null)
                {
                    matches = false;
                    break;
                }

                conversionScore += conversionCost.Value;
            }

            if (!matches
                || function.TypeParameters.Any(typeParameter => !substitutions.ContainsKey(typeParameter)))
            {
                continue;
            }

            var candidateScore =
                (mapping.UsesParams ? mapping.UsesExpandedParams ? 200 : 100 : 0)
                + mapping.OmittedOptionalCount * 10
                + conversionScore;
            if (candidateScore >= score)
                continue;

            score = candidateScore;
            resolvedReturnType = substitutions.Count == 0
                ? function.ReturnType
                : TypeIdentity.Substitute(function.ReturnType, substitutions);
        }

        return score != int.MaxValue;
    }

    private sealed record ArgumentMapping(
        int[] ParameterMap,
        bool[] ExpandedParams,
        int OmittedOptionalCount,
        bool UsesParams,
        bool UsesExpandedParams);

    private static IEnumerable<ArgumentMapping> MapArguments(
        FunctionSymbol function,
        int argumentCount,
        IReadOnlyList<string?>? argumentNames)
    {
        var parameterMap = new int[argumentCount];
        var expandedParams = new bool[argumentCount];
        var assigned = new bool[function.Parameters.Count];
        var paramsCandidates = function.Parameters
            .Select((parameter, index) => (parameter, index))
            .Where(item => item.parameter.Modifier.HasFlag(ParameterModifier.Params))
            .Select(item => item.index)
            .Take(2)
            .ToArray();
        if (paramsCandidates.Length > 1)
            yield break;
        var paramsIndex = paramsCandidates.Length == 0 ? -1 : paramsCandidates[0];
        if (paramsIndex >= 0 && paramsIndex != function.Parameters.Count - 1)
            yield break;

        var nextPositional = 0;
        var positionalParamsArguments = new List<int>();
        var namedParamsAssigned = false;

        for (var argumentIndex = 0; argumentIndex < argumentCount; argumentIndex++)
        {
            var argumentName = argumentNames != null && argumentIndex < argumentNames.Count
                ? argumentNames[argumentIndex]
                : null;

            int parameterIndex;
            if (!string.IsNullOrWhiteSpace(argumentName))
            {
                parameterIndex = function.Parameters
                    .Select((parameter, index) => (parameter, index))
                    .Where(item => string.Equals(item.parameter.Name, argumentName, StringComparison.Ordinal))
                    .Select(item => item.index)
                    .DefaultIfEmpty(-1)
                    .First();

                if (parameterIndex == paramsIndex)
                {
                    if (assigned[parameterIndex])
                        yield break;
                    assigned[parameterIndex] = true;
                    namedParamsAssigned = true;
                }
            }
            else
            {
                while (nextPositional < assigned.Length
                       && assigned[nextPositional]
                       && nextPositional != paramsIndex)
                {
                    nextPositional++;
                }

                if (nextPositional < assigned.Length && nextPositional != paramsIndex)
                {
                    parameterIndex = nextPositional++;
                    assigned[parameterIndex] = true;
                }
                else if (paramsIndex >= 0)
                {
                    if (assigned[paramsIndex])
                        yield break;
                    parameterIndex = paramsIndex;
                    expandedParams[argumentIndex] = true;
                    positionalParamsArguments.Add(argumentIndex);
                }
                else
                {
                    yield break;
                }
            }

            if (parameterIndex < 0 || parameterIndex >= assigned.Length)
                yield break;
            if (!string.IsNullOrWhiteSpace(argumentName)
                && parameterIndex != paramsIndex
                && assigned[parameterIndex])
            {
                yield break;
            }

            parameterMap[argumentIndex] = parameterIndex;
            if (parameterIndex != paramsIndex)
                assigned[parameterIndex] = true;
        }

        var omittedOptionalCount = 0;
        for (var index = 0; index < function.Parameters.Count; index++)
        {
            if (index == paramsIndex)
                continue;
            if (assigned[index])
                continue;
            if (!function.Parameters[index].IsOptional)
                yield break;
            omittedOptionalCount++;
        }

        if (paramsIndex < 0)
        {
            yield return new ArgumentMapping(
                parameterMap,
                expandedParams,
                omittedOptionalCount,
                UsesParams: false,
                UsesExpandedParams: false);
            yield break;
        }

        if (positionalParamsArguments.Count == 1)
        {
            var normalExpanded = (bool[])expandedParams.Clone();
            normalExpanded[positionalParamsArguments[0]] = false;
            yield return new ArgumentMapping(
                (int[])parameterMap.Clone(),
                normalExpanded,
                omittedOptionalCount,
                UsesParams: true,
                UsesExpandedParams: false);
        }

        yield return new ArgumentMapping(
            parameterMap,
            expandedParams,
            omittedOptionalCount,
            UsesParams: true,
            UsesExpandedParams: !namedParamsAssigned
                && (positionalParamsArguments.Count != 1
                    || expandedParams.Any(value => value)));
    }

    private static string? GetParamsElementType(string typeName)
    {
        var type = typeName.Trim();
        if (type.StartsWith('[') && type.EndsWith(']') && type.Length > 2)
            return type[1..^1].Trim();
        if (type.EndsWith("[]", StringComparison.Ordinal) && type.Length > 2)
            return type[..^2].Trim();
        return null;
    }

    private static ParameterModifier GetArgumentModifier(
        IReadOnlyList<string?>? argumentModifiers,
        int argumentIndex)
    {
        if (argumentModifiers == null || argumentIndex >= argumentModifiers.Count)
            return ParameterModifier.None;

        return argumentModifiers[argumentIndex]?.ToLowerInvariant() switch
        {
            null or "" => ParameterModifier.None,
            "ref" => ParameterModifier.Ref,
            "out" => ParameterModifier.Out,
            "in" => ParameterModifier.In,
            _ => (ParameterModifier)(-1),
        };
    }

    private static bool ModifiersMatch(ParameterModifier parameterModifier, ParameterModifier argumentModifier)
    {
        var callSiteModifier = parameterModifier
            & (ParameterModifier.Ref | ParameterModifier.Out | ParameterModifier.In);
        return callSiteModifier == argumentModifier;
    }

    /// <summary>
    /// Calculates the Levenshtein distance between two strings.
    /// </summary>
    private static int LevenshteinDistance(string s1, string s2)
    {
        if (string.IsNullOrEmpty(s1))
            return string.IsNullOrEmpty(s2) ? 0 : s2.Length;
        if (string.IsNullOrEmpty(s2))
            return s1.Length;

        var m = s1.Length;
        var n = s2.Length;

        var prev = new int[n + 1];
        var curr = new int[n + 1];

        for (var j = 0; j <= n; j++)
            prev[j] = j;

        for (var i = 1; i <= m; i++)
        {
            curr[0] = i;

            for (var j = 1; j <= n; j++)
            {
                var cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                curr[j] = Math.Min(
                    Math.Min(curr[j - 1] + 1, prev[j] + 1),
                    prev[j - 1] + cost);
            }

            (prev, curr) = (curr, prev);
        }

        return prev[n];
    }
}
