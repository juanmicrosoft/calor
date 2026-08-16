using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// W1 Slice 1 (#781 preservation half, wedge-w1-prereqs.md §1.1):
/// ContractSimplificationPass runs unconditionally on the main compile path and
/// must preserve EVERY module/interface field when it reconstructs nodes. The
/// pre-fix overloads silently dropped module InteropBlocks, EnumExtensions,
/// RefinementTypes, IndexedTypes, and TypePreprocessorBlocks — and interface
/// Properties/Indexers — whenever any contract simplified.
/// </summary>
public class W1Slice1SimplificationPreservationTests
{
    [Fact]
    public void ModuleInteropBlocks_SurviveContractSimplification()
    {
        // §Q (> 2 1) constant-folds → the pass reconstructs the module. The
        // interop block must survive the reconstruction.
        var source = @"§M{m001:T}
  §F{f001:F:pub} (i32:x) -> i32
    §Q (> 2 1)
    §R x
  §CSHARP{public static class Keep { }}§/CSHARP
";
        var diagnostics = new DiagnosticBag();
        diagnostics.SetFilePath("test.calr");
        var lexer = new Lexer(source, diagnostics);
        var parser = new Parser(lexer.TokenizeAllForParser(), diagnostics);
        var module = parser.Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        Assert.Single(module.InteropBlocks);

        var pass = new ContractSimplificationPass(new DiagnosticBag());
        var simplified = pass.Simplify(module);

        // The contract actually simplified (the reconstruction path ran)…
        Assert.False(ReferenceEquals(simplified, module));
        // …and nothing was dropped.
        Assert.Single(simplified.InteropBlocks);

        var transformedProperties = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(ModuleNode.Functions),
            nameof(ModuleNode.Classes),
            nameof(ModuleNode.Interfaces),
            nameof(ModuleNode.Invariants),
            nameof(ModuleNode.Items)
        };
        foreach (var property in typeof(ModuleNode).GetProperties()
                     .Where(property => property.GetIndexParameters().Length == 0)
                     .Where(property => !transformedProperties.Contains(property.Name)))
        {
            Assert.Equal(
                property.GetValue(module),
                property.GetValue(simplified));
        }
    }

    [Fact]
    public void InterfaceProperties_SurviveContractSimplification()
    {
        // An interface whose method-signature contract simplifies must keep its
        // properties and indexers (the pre-fix 8-arg overload defaulted them empty).
        var span = TextSpan.Empty;
        var attrs = new AttributeCollection();

        var foldableContract = new RequiresNode(
            span,
            new BinaryOperationNode(span,
                BinaryOperator.GreaterThan,
                new IntLiteralNode(span, 2),
                new IntLiteralNode(span, 1)),
            null,
            attrs);

        var method = new MethodSignatureNode(
            span, "ms1", "Do",
            Array.Empty<TypeParameterNode>(),
            Array.Empty<ParameterNode>(),
            null, null,
            new[] { foldableContract },
            Array.Empty<EnsuresNode>(),
            attrs,
            Array.Empty<CalorAttributeNode>());

        var property = new PropertyNode(
            span, "p1", "Count", "i32", Visibility.Public,
            null, null, null, null, attrs);

        var iface = new InterfaceDefinitionNode(
            span, "i1", "IThing",
            Array.Empty<string>(),
            Array.Empty<TypeParameterNode>(),
            new[] { method },
            new[] { property },
            attrs,
            Array.Empty<CalorAttributeNode>(),
            indexers: null);

        var module = new ModuleNode(
            span, "m1", "T",
            Array.Empty<UsingDirectiveNode>(),
            new[] { iface },
            Array.Empty<ClassDefinitionNode>(),
            Array.Empty<FunctionNode>(),
            attrs);

        var pass = new ContractSimplificationPass(new DiagnosticBag());
        var simplified = pass.Simplify(module);

        Assert.False(ReferenceEquals(simplified, module));
        var simplifiedIface = Assert.Single(simplified.Interfaces);
        Assert.Single(simplifiedIface.Properties);
    }
}
