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
    public static string Canonicalize(string typeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        var type = typeName.Trim();

        if (type.StartsWith("?", StringComparison.Ordinal))
            return "?" + Canonicalize(type[1..]);

        if (TryStripPostfix(type, out var elementType, out var postfix))
            return Canonicalize(elementType) + postfix;

        if (TrySplitGeneric(type, out var genericName, out var arguments))
        {
            return $"{genericName.Trim()}<{string.Join(",", arguments.Select(Canonicalize))}>";
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

        if (parameterType.StartsWith("?", StringComparison.Ordinal)
            && argumentType.StartsWith("?", StringComparison.Ordinal))
        {
            return TryUnifyCanonical(
                parameterType[1..],
                argumentType[1..],
                typeParameters,
                substitutions);
        }

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

    private static bool TrySplitGeneric(
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
    public string IdentityKey => Id.IsNone ? Name : Id.Value;

    protected Symbol(
        SymbolId id,
        string name,
        TextSpan declarationSpan,
        TextSpan? definitionSpan = null)
    {
        Id = id;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        DeclarationSpan = declarationSpan;
        DefinitionSpan = definitionSpan ?? declarationSpan;
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
    public ParameterModifier Modifier { get; }
    public ExpressionNode? DefaultValue { get; }
    public bool IsOptional => DefaultValue != null;

    public VariableSymbol(
        SymbolId id,
        string name,
        string typeName,
        bool isMutable,
        bool isParameter = false,
        ParameterModifier modifier = ParameterModifier.None,
        TextSpan declarationSpan = default,
        ExpressionNode? defaultValue = null)
        : base(id, name, declarationSpan)
    {
        TypeName = typeName ?? throw new ArgumentNullException(nameof(typeName));
        IsMutable = isMutable;
        IsParameter = isParameter;
        Modifier = modifier;
        DefaultValue = defaultValue;
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
    public IReadOnlyList<VariableSymbol> Parameters { get; }
    public IReadOnlyList<string> TypeParameters { get; }
    public Visibility Visibility { get; }
    public string? ContainingTypeName { get; }
    public ConditionalAlternative? ConditionalAlternative { get; }
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
        : base(id, name, declarationSpan, definitionSpan)
    {
        ReturnType = returnType ?? throw new ArgumentNullException(nameof(returnType));
        Parameters = parameters?.ToArray() ?? throw new ArgumentNullException(nameof(parameters));
        TypeParameters = typeParameters?.ToArray() ?? Array.Empty<string>();
        Visibility = visibility;
        ContainingTypeName = containingTypeName;
        ConditionalAlternative = conditionalAlternative;
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

    public TypeSymbol(
        SymbolId id,
        string name,
        string qualifiedName,
        Visibility visibility,
        TextSpan declarationSpan,
        TextSpan? definitionSpan = null)
        : base(id, name, declarationSpan, definitionSpan)
    {
        QualifiedName = qualifiedName ?? throw new ArgumentNullException(nameof(qualifiedName));
        Visibility = visibility;
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
    private readonly Dictionary<string, List<FunctionSymbol>> _overloadSets = new(StringComparer.Ordinal);
    public Scope? Parent { get; }

    public Scope(Scope? parent = null)
    {
        Parent = parent;
    }

    public bool TryDeclare(Symbol symbol)
    {
        if (_symbols.ContainsKey(symbol.Name))
        {
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
        IReadOnlyList<string>? typeArguments = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(argumentTypes);

        if (!_overloadSets.TryGetValue(name, out var overloads))
        {
            if (_symbols.ContainsKey(name))
                return OverloadResolutionResult.NotFound();
            return Parent?.ResolveOverload(name, argumentTypes, argumentNames, argumentModifiers, typeArguments)
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
            return OverloadResolutionResult.ResolvedAlternatives(
                bestFunctions,
                best[0].ReturnType);
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
        return _symbols.Values;
    }

    public Scope CreateChild()
    {
        return new Scope(this);
    }

    private static bool AreMutuallyExclusiveAlternatives(
        FunctionSymbol left,
        FunctionSymbol right) =>
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
                if (parameterType == null
                    || !TypeIdentity.TryUnify(
                        parameterType,
                        argumentTypes[argumentIndex],
                        typeParameterSet,
                        substitutions))
                {
                    matches = false;
                    break;
                }
            }

            if (!matches
                || function.TypeParameters.Any(typeParameter => !substitutions.ContainsKey(typeParameter)))
            {
                continue;
            }

            var candidateScore =
                (mapping.UsesParams ? mapping.UsesExpandedParams ? 200 : 100 : 0)
                + mapping.OmittedOptionalCount * 10;
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
