using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Calor.Compiler.Analysis;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;
using Binder = Calor.Compiler.Binding.Binder;

namespace Calor.Performance.Tests;

public sealed class IdentityEquivalenceTests
{
    private static readonly Func<ModuleNode, string> Snapshot =
        typeof(Binder).GetMethod("SnapshotIdentityKey", BindingFlags.NonPublic | BindingFlags.Static)!
            .CreateDelegate<Func<ModuleNode, string>>();

    [Fact]
    public void PrefixSharingPreservesEagerStringsSerializationAndDictionaryEquality()
    {
        string[] components = ["source", "", "a/b", "%20", "x y", "\u00e9\U0001f600", "[],:\""];
        var expected = "calor://" + string.Join("/", components.Select(Uri.EscapeDataString));
        var eager = new SymbolId(expected);
        var alternatives = Enumerable.Range(1, components.Length)
            .Select(split => SymbolId.Create(components[..split]).Append(components[split..])).ToList();
        alternatives.Add(SymbolId.None.Append(components));
        alternatives.Add(SymbolId.Create(components[..1]).Append(components[1..3]).Append(components[3..]));
        foreach (var alternative in alternatives)
        {
            Assert.Equal(expected, alternative.Value);
            Assert.Equal(eager, alternative);
            Assert.Equal(eager.GetHashCode(), alternative.GetHashCode());
            Assert.Equal(JsonSerializer.Serialize(eager), JsonSerializer.Serialize(alternative));
            Assert.Equal(alternative, alternative.Append());
            Assert.Equal(42, new Dictionary<SymbolId, int> { [eager] = 42 }[alternative]);
        }
        Assert.Single(alternatives.ToHashSet());
        Assert.NotEqual(eager, new SymbolId(expected + "/"));
        Assert.NotEqual(eager, new SymbolId(expected.Replace("a%2Fb", "a%2fb")));
        Assert.Equal(string.Empty, SymbolId.None.Value);
        Assert.Equal("<none>", SymbolId.None.ToString());
        Assert.True(SymbolId.None.IsNone);
        Assert.Throws<ArgumentNullException>(() => SymbolId.Create((string[])null!));
        Assert.Throws<ArgumentException>(() => SymbolId.Create());
        Assert.Throws<ArgumentException>(() => new SymbolId(" "));
        Assert.Throws<ArgumentNullException>(() => eager.Append((string[])null!));
    }

    [Fact]
    public void DeepIdentitiesMaterializeConcurrentlyWithoutChangingEquality()
    {
        var id = new SymbolId("raw://\ud800\u00e9");
        var expected = id.Value;
        for (var depth = 0; depth < 2048; depth++)
        {
            var component = "part:" + depth.ToString(CultureInfo.InvariantCulture);
            expected += "/" + Uri.EscapeDataString(component);
            id = id.Append(component);
        }
        var eager = new SymbolId(expected);
        Assert.Equal(eager, id);
        Assert.Equal(eager.GetHashCode(), id.GetHashCode());
        Parallel.For(0, 128, _ => Assert.Equal(expected, id.Value));
        Assert.Equal(eager, id);
        Assert.Equal(eager.GetHashCode(), id.GetHashCode());
    }

    [Theory]
    [InlineData("", "INT", 0)]
    [InlineData("a/b% \"<>", "System.Func<INT,INT[,]>", -42)]
    [InlineData("\u00e9\U0001f600\ud800", "?System.String", int.MaxValue)]
    public void DirectLocalEncodingEqualsTheOriginalSerializerAndUriEscape(string name, string typeName, int start)
    {
        var binder = new Binder(new DiagnosticBag());
        var suffix = typeof(Binder).GetMethod("LocalIdentitySuffix", BindingFlags.NonPublic | BindingFlags.Instance)!
            .CreateDelegate<Func<string, string, int, string>>(binder);
        Assert.Equal("/" + Uri.EscapeDataString("name:" +
            JsonSerializer.Serialize(new { name, typeName, Start = start })), suffix(name, typeName, start));
    }

    [Theory]
    [InlineData("synthetic")]
    [InlineData("metadata")]
    [InlineData("shared")]
    [InlineData("local-function")]
    public void StreamingFingerprintIsExactlyTheParentFingerprint(string shape)
    {
        var module = shape == "synthetic" ? Parse(SyntheticCodeGenerator.Generate(10, 50)) : Parse("""
            §M{Demo}
              §F{Run:pub}<eff e, eff f> (i32:value) -> i32
                §E{cw,e,f}
                §B{offset:i32} 1
                §B{callback:System.Func<i32,i32>} §LAM{x:i32} (+ x value)
                §R value
            """);
        if (shape == "metadata")
        {
            module.DocComment = new string('x', 4095) + "\U0001f600\ud800\"<>&\r\n";
            module.NamespaceIdentity = "\u00e9.global";
            module.Functions[0].Attributes.Add("z", "last");
            module.Functions[0].Attributes.Add("a", "first");
        }
        if (shape is "shared" or "local-function")
        {
            var function = module.Functions[0];
            var shared = new ReferenceNode(TextSpan.Empty, "value");
            var body = new List<StatementNode>
            {
                new ReturnStatementNode(TextSpan.Empty, new BinaryOperationNode(TextSpan.Empty, BinaryOperator.Add, shared, shared)),
                new ReturnStatementNode(TextSpan.Empty, shared)
            };
            if (shape == "local-function")
            {
                var local = new FunctionNode(TextSpan.Empty, "local", "Nested", Visibility.Private,
                    [], null, null, [], new AttributeCollection());
                typeof(AstNode).GetProperty(nameof(AstNode.ToolingId))!.SetValue(local, "persisted-local");
                body.Insert(0, new LocalFunctionStatementNode(TextSpan.Empty, local));
            }
            var replacement = new FunctionNode(function.Span, function.LocalId, function.Name, function.Visibility,
                function.Parameters, function.Output, function.Effects, body, function.Attributes);
            module = new ModuleNode(module.Span, module.LocalId, module.Name, module.Usings, [replacement], module.Attributes);
        }
        var expected = ParentSnapshot(module);
        Assert.Equal(expected, Snapshot(module));
        Parallel.For(0, 32, _ => Assert.Equal(expected, Snapshot(module)));
        var bound = new Binder(new DiagnosticBag(), "memory:equivalence").Bind(module);
        foreach (var symbol in bound.SymbolsById.Values.OfType<VariableSymbol>()
                     .Where(symbol => symbol.Id.Value.Contains("/snapshot/")))
        {
            var value = symbol.Id.Value;
            Assert.Contains("/snapshot/" + expected + "/", value);
            var tail = value[value.LastIndexOf("/name%3A", StringComparison.Ordinal)..].Split("/duplicate%3A")[0];
            Assert.Equal("/" + Uri.EscapeDataString("name:" + JsonSerializer.Serialize(new
            { name = symbol.Name, typeName = symbol.TypeName, symbol.DeclarationSpan.Start })), tail);
        }
        if (shape == "local-function")
            Assert.Contains(bound.SymbolsById.Keys, id => id.Value == "calor://declaration/persisted-local");
    }

    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        return module;
    }

    [Fact]
    public void SnapshotScratchDoesNotRetainAstsOrSurviveAnUnsupportedMetadataFailure()
    {
        var source = Parse("§M{Demo}\n  §F{Run:pub} () -> void\n");
        var function = new FunctionNode(TextSpan.Empty, "bad", "Bad", Visibility.Private,
            [], null, null, [new UnsupportedSnapshotNode()], new AttributeCollection());
        var invalid = new ModuleNode(TextSpan.Empty, "bad", "Bad", [], [function], new AttributeCollection());
        Assert.Equal(Assert.Throws<InvalidOperationException>(() => ParentSnapshot(invalid)).Message,
            Assert.Throws<InvalidOperationException>(() => Snapshot(invalid)).Message);
        Assert.Equal(ParentSnapshot(source), Snapshot(source));
        var weak = FingerprintAndRelease();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(weak.IsAlive);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference FingerprintAndRelease()
    {
        var module = Parse("§M{Demo}\n  §F{Run:pub} () -> void\n");
        _ = Snapshot(module);
        return new WeakReference(module);
    }

    private sealed class UnsupportedSnapshotNode() : StatementNode(TextSpan.Empty)
    {
        public Uri Unsupported => new("https://example.invalid/");
    }

    [Fact]
    public void SnapshotPreservesTheParentCurrentCultureHeaderAndInvariantJsonBytes()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            culture.NumberFormat.NegativeSign = new string('-', 80) + "\U0001f600";
            CultureInfo.CurrentCulture = culture;
            var module = new ModuleNode(new TextSpan(-42, -9, -3, -4), "local", "Demo", [], [], new AttributeCollection());
            Assert.Equal(ParentSnapshot(module), Snapshot(module));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    // Independent original implementation from parent 732afda985, not a new golden.
    private static string ParentSnapshot(ModuleNode module)
    {
        var text = new StringBuilder();
        var pending = new Stack<AstNode>();
        var visited = new HashSet<AstNode>(ReferenceEqualityComparer.Instance);
        pending.Push(module);
        while (pending.TryPop(out var node))
        {
            if (!visited.Add(node)) continue;
            text.Append(node.GetType().FullName).Append(':').Append(node.Span.Start).Append(':').Append(node.Span.Length);
            foreach (var property in node.GetType().GetProperties().OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                if (!property.CanRead || property.GetIndexParameters().Length != 0
                    || property.Name is "Id" or "LocalId" or "ToolingId") continue;
                var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                var value = property.GetValue(node);
                var encoded = value == null ? "null"
                    : type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal)
                        ? JsonSerializer.Serialize(Convert.ToString(value, CultureInfo.InvariantCulture))
                        : ParentMetadata(value);
                if (encoded == null)
                {
                    if (value is AstNode or IEnumerable<AstNode>
                        || RecursiveAstWalker.GetAllChildProperties(node.GetType()).Contains(property)) continue;
                    throw new InvalidOperationException($"Unsupported identity snapshot metadata: {node.GetType().Name}.{property.Name} ({type.Name}).");
                }
                text.Append('|').Append(property.Name).Append('=').Append(encoded);
            }
            text.AppendLine();
            foreach (var child in RecursiveAstWalker.GetAllChildren(node).Reverse()) pending.Push(child);
        }
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private static string? ParentMetadata(object value) => value switch
    {
        AttributeCollection attributes => JsonSerializer.Serialize(attributes.All().OrderBy(pair => pair.Key, StringComparer.Ordinal)),
        IReadOnlyDictionary<string, string> map => JsonSerializer.Serialize(map.OrderBy(pair => pair.Key, StringComparer.Ordinal)),
        IReadOnlyList<string> strings => JsonSerializer.Serialize(strings),
        IReadOnlyList<int> ordinals => JsonSerializer.Serialize(ordinals),
        IReadOnlyList<EffectParameterInfo> parameters => JsonSerializer.Serialize(parameters),
        IReadOnlyList<NamespaceScopeInfo> scopes => JsonSerializer.Serialize(
            scopes.Select(scope => new { scope.Name, scope.FullName, scope.IsFileScoped, scope.Span, scope.Kind })),
        IReadOnlyList<TextSpan> spans => JsonSerializer.Serialize(spans),
        IReadOnlyList<ObjectInitializerAssignment> initializers => JsonSerializer.Serialize(initializers.Select(i => i.PropertyName)),
        IReadOnlyList<CalorAttributeArgument> arguments => JsonSerializer.Serialize(
            arguments.Select(a => new { a.Name, Value = a.GetCalorFormattedValue() })),
        InlineRefinementInfo refinement => JsonSerializer.Serialize(refinement.BaseTypeName),
        TextSpan span => JsonSerializer.Serialize(span),
        DateOnly date => JsonSerializer.Serialize(date),
        DateTime date => JsonSerializer.Serialize(date),
        _ => null
    };
}
