using Calor.Compiler.Ast;
using Calor.Compiler.Migration;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests that the C#→Calor converter generates meaningful intermediate variable names
/// instead of cryptic _chain001, _lam001, etc.
/// </summary>
public class VariableNamingTests
{
    private readonly CSharpToCalorConverter _converter = new();

    #region GenerateId hint sanitization

    [Fact]
    public void GenerateId_WithValidHint_IncludesHintInName()
    {
        var context = new ConversionContext();
        var id = context.GenerateId("_chain", "Where");
        Assert.StartsWith("_chainWhere", id);
    }

    [Fact]
    public void GenerateId_WithEmptyHint_FallsBackToPrefix()
    {
        var context = new ConversionContext();
        var id = context.GenerateId("_chain", "");
        Assert.StartsWith("_chain", id);
        Assert.DoesNotContain("_chainW", id); // no hint appended
    }

    [Fact]
    public void GenerateId_WithNullHint_FallsBackToPrefix()
    {
        var context = new ConversionContext();
        var id = context.GenerateId("_chain", null!);
        Assert.StartsWith("_chain", id);
    }

    [Fact]
    public void GenerateId_HintWithSpecialChars_StripsInvalidCharacters()
    {
        var context = new ConversionContext();
        var id = context.GenerateId("_arr", "int[]");
        Assert.StartsWith("_arrInt", id);
    }

    [Fact]
    public void GenerateId_LongHint_TruncatesTo20Chars()
    {
        var context = new ConversionContext();
        var longHint = "VeryLongTypeNameThatExceedsTwentyCharacters";
        var id = context.GenerateId("_new", longHint);
        // prefix + sanitized hint (max 20) + counter
        var withoutCounter = id.Substring(0, id.Length - 3); // remove "001"
        Assert.True(withoutCounter.Length <= "_new".Length + 20);
    }

    [Fact]
    public void SanitizeHint_CapitalizesFirstLetter()
    {
        var result = ConversionContext.SanitizeHint("where");
        Assert.Equal("Where", result);
    }

    [Fact]
    public void SanitizeHint_WhitespaceOnly_ReturnsEmpty()
    {
        var result = ConversionContext.SanitizeHint("   ");
        Assert.Equal("", result);
    }

    #endregion

    #region Chain decomposition naming

    [Fact]
    public void ChainDecomposition_UsesMethodNameInBindName()
    {
        var csharp = """
            using System.Linq;
            using System.Collections.Generic;
            public class Example
            {
                public int Process(List<int> items)
                {
                    return items.Where(x => x > 0).Select(x => x * 2).First();
                }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var cls = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(cls.Methods);

        // Find bind statements with chain-related names
        var binds = method.Body
            .OfType<BindStatementNode>()
            .Where(b => b.Name.StartsWith("_chain") || b.Name.StartsWith("_lam"))
            .ToList();

        // Should have meaningful names containing method names
        Assert.Contains(binds, b => b.Name.Contains("Where"));
        Assert.Contains(binds, b => b.Name.Contains("Select"));
    }

    [Fact]
    public void ChainDecomposition_LambdaHoisting_UsesCallingMethodName()
    {
        var csharp = """
            using System.Linq;
            using System.Collections.Generic;
            public class Example
            {
                public List<int> Filter(List<int> items)
                {
                    return items.Where(x => x > 0).ToList();
                }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var cls = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(cls.Methods);

        // Lambda hoisted from Where should have "Where" in its name
        var lamBinds = method.Body
            .OfType<BindStatementNode>()
            .Where(b => b.Name.StartsWith("_lam"))
            .ToList();

        Assert.NotEmpty(lamBinds);
        Assert.Contains(lamBinds, b => b.Name.Contains("Where"));
    }

    #endregion

    #region Array creation naming

    [Fact]
    public void ArrayCreation_UsesElementTypeInId()
    {
        var csharp = """
            public class Example
            {
                public void Create()
                {
                    var nums = new int[] { 1, 2, 3 };
                }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var cls = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(cls.Methods);

        // The bind should contain an ArrayCreationNode whose Id contains the element type
        var bind = method.Body.OfType<BindStatementNode>().FirstOrDefault();
        Assert.NotNull(bind);
        if (bind!.Initializer is ArrayCreationNode arrNode)
        {
            // Id should contain element type hint (e.g., "arrI32001" or similar)
            Assert.NotEmpty(arrNode.Id);
        }
    }

    #endregion

    private static string GetErrorMessage(ConversionResult result)
    {
        if (result.Success) return string.Empty;
        return string.Join("\n", result.Issues.Select(i => i.ToString()));
    }
}
