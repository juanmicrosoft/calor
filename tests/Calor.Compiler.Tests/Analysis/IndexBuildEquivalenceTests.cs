using System.Collections;
using System.Security.Cryptography;
using System.Text;
using Calor.Compiler.Analysis;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Ids;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// #1738: a project index build spent much of its time and allocation in three helpers
/// that every pass uses: the AST child walk, the binder's snapshot hash and the identity
/// store's declaration parse. Each was made cheaper without changing what it returns.
/// These tests compare each one with the definition it replaced.
/// </summary>
public sealed class IndexBuildEquivalenceTests
{
    // --- RecursiveAstWalker ---------------------------------------------------------

    // The walk as it was written before #1738: a reference-equality hash set per node and
    // one nested iterator per property value.
    private static IEnumerable<RecursiveAstWalker.ChildEdge> ReferenceEdges(AstNode node)
    {
        var yielded = new HashSet<AstNode>(ReferenceEqualityComparer.Instance);
        foreach (var property in RecursiveAstWalker.GetAllChildProperties(node.GetType()))
        {
            foreach (var child in ReferenceUnwrap(property.GetValue(node)))
            {
                if (yielded.Add(child))
                    yield return new RecursiveAstWalker.ChildEdge(child, property);
            }
        }
    }

    private static IEnumerable<AstNode> ReferenceUnwrap(object? value)
    {
        switch (value)
        {
            case AstNode node:
                yield return node;
                break;
            case ObjectInitializerAssignment initializer:
                yield return initializer.Value;
                break;
            case InlineRefinementInfo refinement:
                yield return refinement.Predicate;
                break;
            case IEnumerable sequence when value is not string:
                foreach (var item in sequence)
                    foreach (var child in ReferenceUnwrap(item))
                        yield return child;
                break;
        }
    }

    private static void AssertSameChildren(AstNode node)
    {
        var expected = ReferenceEdges(node).ToArray();
        var edges = RecursiveAstWalker.GetAllChildEdges(node).ToArray();
        var children = RecursiveAstWalker.GetAllChildren(node).ToArray();
        Assert.Equal(expected.Length, edges.Length);
        Assert.Equal(expected.Length, children.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            Assert.Same(expected[index].Node, edges[index].Node);
            Assert.Same(expected[index].Property, edges[index].Property);
            Assert.Same(expected[index].Node, children[index]);
        }
    }

    [Fact]
    public void ChildWalk_MatchesTheHashSetWalk_OnEveryNodeOfTheActiveSyntaxInputs()
    {
        var root = Path.Combine(ActiveSyntaxInputs.RepositoryRoot, ActiveSyntaxInputs.RelativeRoot);
        var files = Directory.EnumerateFiles(root, "*.calr", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal).ToArray();
        Assert.NotEmpty(files);
        var nodes = 0;
        var parsedFiles = 0;
        foreach (var file in files)
        {
            var diagnostics = new DiagnosticBag();
            var module = new Parser(new Lexer(File.ReadAllText(file), diagnostics).TokenizeAllForParser(), diagnostics)
                .Parse();
            if (module is null || diagnostics.HasErrors)
                continue;
            parsedFiles++;
            var visited = new HashSet<AstNode>(ReferenceEqualityComparer.Instance);
            var pending = new Stack<AstNode>();
            pending.Push(module);
            while (pending.TryPop(out var node))
            {
                if (!visited.Add(node))
                    continue;
                nodes++;
                AssertSameChildren(node);
                foreach (var edge in ReferenceEdges(node))
                    if (edge.Node is not null)
                        pending.Push(edge.Node);
            }
        }
        Assert.True(parsedFiles > 100, $"expected the active-syntax corpus, parsed {parsedFiles} files");
        Assert.True(nodes > 10_000, $"expected a large walk, visited {nodes} nodes");
    }

    [Fact]
    public void ChildWalk_MatchesTheHashSetWalk_PastTheScanLimitAndWithRepeatedChildren()
    {
        var span = new TextSpan(0, 1, 1, 1);
        var statements = Enumerable.Range(0, 40)
            .Select(value => (StatementNode)new ReturnStatementNode(span, new IntLiteralNode(span, value)))
            .ToList();
        // Repeats before, at and after the switch from scanning to hashing.
        statements.Insert(3, statements[0]);
        statements.Insert(17, statements[5]);
        statements.Insert(30, statements[16]);
        statements.Add(statements[39]);
        var function = new FunctionNode(span, "f", "F", Visibility.Public, [], null, null, statements,
            new AttributeCollection());

        AssertSameChildren(function);
        Assert.Equal(40, RecursiveAstWalker.GetAllChildren(function).OfType<ReturnStatementNode>().Count());
    }

    // --- Binder.SnapshotHashWriter ----------------------------------------------------

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4095)]
    [InlineData(4096)]
    [InlineData(4097)]
    [InlineData(20_000)]
    public void SnapshotHash_IsTheSha256OfTheWholeUtf8Text_HoweverItIsSplit(int length)
    {
        // Surrogate pairs, two- and three-byte characters, and fragments that split a pair,
        // so encoder state and buffer boundaries land everywhere.
        var alphabet = new[] { "😀", "é", "中", "a", "𝄞", "\n", "€" };
        var text = new StringBuilder();
        for (var index = 0; text.Length < length; index++)
            text.Append(alphabet[index * 5 % alphabet.Length]);
        var whole = text.ToString();
        var random = new Random(length);
        var fragments = new List<string>();
        var json = new List<(int Position, string Value)>();
        var expected = new StringBuilder();
        for (var position = 0; position < whole.Length;)
        {
            var size = Math.Min(whole.Length - position, random.Next(1, 700));
            var fragment = whole.Substring(position, size);
            fragments.Add(fragment);
            expected.Append(fragment);
            position += size;
            // A JSON value goes only between whole characters: the writer appends it after the
            // text encoded so far, and a split pair's second half is encoded after it.
            if (random.Next(4) == 0 && !char.IsHighSurrogate(whole[position - 1]))
            {
                var value = "v" + position;
                json.Add((fragments.Count, value));
                // Utf8JsonWriter writes a string value with quotes.
                expected.Append('"').Append(value).Append('"');
            }
        }

        string actual;
        using (var writer = new Binder.SnapshotHashWriter())
        {
            var next = 0;
            for (var index = 0; index < fragments.Count; index++)
            {
                writer.Append(fragments[index]);
                while (next < json.Count && json[next].Position == index + 1)
                {
                    writer.ResetJson();
                    writer.Json.WriteStringValue(json[next].Value);
                    writer.AppendJson();
                    next++;
                }
            }
            actual = writer.Finish();
        }

        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(expected.ToString()))), actual);
    }

    // --- DeclarationIdentityStore shared parses ---------------------------------------

    [Fact]
    public void SharedDeclarationParses_GiveTheUnsharedDeclarations_AndFailuresStillThrow()
    {
        var root = Path.Combine(ActiveSyntaxInputs.RepositoryRoot, ActiveSyntaxInputs.RelativeRoot);
        var files = Directory.EnumerateFiles(root, "*.calr", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal).Take(60).ToArray();
        var expected = new List<IReadOnlyList<DeclarationIdentity>?>();
        foreach (var file in files)
        {
            try { expected.Add(DeclarationIdentityStore.Describe(Path.GetFileName(file), File.ReadAllText(file))); }
            catch (DeclarationIdentityParseException) { expected.Add(null); }
        }
        Assert.Contains(expected, declarations => declarations is { Count: > 0 });

        using (DeclarationIdentityStore.ShareDeclarationParses())
        {
            for (var pass = 0; pass < 2; pass++)
            {
                for (var index = 0; index < files.Length; index++)
                {
                    IReadOnlyList<DeclarationIdentity>? actual;
                    try { actual = DeclarationIdentityStore.Describe(Path.GetFileName(files[index]), File.ReadAllText(files[index])); }
                    catch (DeclarationIdentityParseException) { actual = null; }
                    if (expected[index] is null)
                        Assert.Null(actual);
                    else
                        Assert.Equal(expected[index]!, actual!);
                }
            }

            const string broken = "module Broken\n  func F public (int:x → int\n";
            Assert.Throws<DeclarationIdentityParseException>(() => DeclarationIdentityStore.Describe("broken.calr", broken));
            Assert.Throws<DeclarationIdentityParseException>(() => DeclarationIdentityStore.Describe("broken.calr", broken));
        }
    }
}
