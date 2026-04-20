namespace Calor.Compiler.Binding;

/// <summary>
/// Represents a symbol in the program (variable, function, etc.).
/// </summary>
public abstract class Symbol
{
    public string Name { get; }

    protected Symbol(string name)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
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

    public VariableSymbol(string name, string typeName, bool isMutable, bool isParameter = false)
        : base(name)
    {
        TypeName = typeName ?? throw new ArgumentNullException(nameof(typeName));
        IsMutable = isMutable;
        IsParameter = isParameter;
    }
}

/// <summary>
/// Represents a function symbol.
/// </summary>
public sealed class FunctionSymbol : Symbol
{
    public string ReturnType { get; }
    public IReadOnlyList<VariableSymbol> Parameters { get; }

    public FunctionSymbol(string name, string returnType, IReadOnlyList<VariableSymbol> parameters)
        : base(name)
    {
        ReturnType = returnType ?? throw new ArgumentNullException(nameof(returnType));
        Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
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

    /// <summary>
    /// Declares a function overload. Multiple overloads with the same name are stored
    /// in an overload set for arity-aware resolution. The first overload is also
    /// registered in _symbols for backward compatibility with Lookup callers.
    /// </summary>
    public void DeclareOverload(FunctionSymbol symbol)
    {
        if (!_overloadSets.TryGetValue(symbol.Name, out var list))
        {
            list = new List<FunctionSymbol>();
            _overloadSets[symbol.Name] = list;
        }
        list.Add(symbol);
        _symbols.TryAdd(symbol.Name, symbol);
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
    /// Looks up a function by name and argument count (arity-aware overload resolution).
    /// Prefers exact arity match; falls back to first overload if no arity match.
    /// </summary>
    public FunctionSymbol? LookupByArity(string name, int argCount)
    {
        if (_overloadSets.TryGetValue(name, out var list))
        {
            var match = list.FirstOrDefault(f => f.Parameters.Count == argCount);
            return match ?? list[0];
        }

        return Parent?.LookupByArity(name, argCount);
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

    /// <summary>
    /// Gets all symbols visible from this scope (including parent scopes).
    /// </summary>
    public IEnumerable<Symbol> GetAllVisibleSymbols()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var current = this;
        while (current != null)
        {
            foreach (var symbol in current._symbols.Values)
            {
                if (seen.Add(symbol.Name))
                {
                    yield return symbol;
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

        // Use two rows instead of full matrix for memory efficiency
        var prev = new int[n + 1];
        var curr = new int[n + 1];

        // Initialize first row
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

            // Swap rows
            (prev, curr) = (curr, prev);
        }

        return prev[n];
    }
}
