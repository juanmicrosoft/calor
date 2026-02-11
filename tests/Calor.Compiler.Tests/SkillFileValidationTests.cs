using System.Reflection;
using System.Text.RegularExpressions;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests that validate the calor.md skill documentation against the actual
/// Lexer.cs implementation and verify that all code examples parse correctly.
/// </summary>
public class SkillFileValidationTests
{
    private const double MinimumCoveragePercent = 75.0;

    private static readonly Lazy<HashSet<string>> LexerTokens = new(ExtractLexerTokens);
    private static readonly Lazy<string> CalorMdContent = new(LoadCalorMd);

    #region Token Coverage Tests

    [Fact]
    public void CalorMd_DocumentsCoverageIsAtLeast75Percent()
    {
        var lexerTokens = LexerTokens.Value;
        var documentedTokens = ExtractDocumentedTokens(CalorMdContent.Value);

        var documented = lexerTokens.Count(t => documentedTokens.Contains(t));
        var total = lexerTokens.Count;
        var coveragePercent = total > 0 ? (documented * 100.0 / total) : 0;

        var undocumented = lexerTokens.Where(t => !documentedTokens.Contains(t)).ToList();

        Assert.True(
            coveragePercent >= MinimumCoveragePercent,
            $"Token documentation coverage is {coveragePercent:F1}% ({documented}/{total}), " +
            $"which is below the required {MinimumCoveragePercent}%.\n" +
            $"Undocumented tokens: {string.Join(", ", undocumented.Select(t => $"§{t}"))}");
    }

    [Fact]
    public void LexerTokens_AreExtractedCorrectly()
    {
        var tokens = LexerTokens.Value;

        // Verify we have a reasonable number of tokens
        Assert.True(tokens.Count > 50, $"Expected at least 50 tokens, but found {tokens.Count}");

        // Verify some known tokens exist
        Assert.Contains("M", tokens);      // Module
        Assert.Contains("F", tokens);      // Function
        Assert.Contains("/M", tokens);     // EndModule
        Assert.Contains("IF", tokens);     // If
        Assert.Contains("CL", tokens);     // Class
    }

    [Fact]
    public void DocumentedTokens_AreExtractedCorrectly()
    {
        var documented = ExtractDocumentedTokens(CalorMdContent.Value);

        // Verify we have documented tokens
        Assert.True(documented.Count > 20, $"Expected at least 20 documented tokens, but found {documented.Count}");

        // Verify some known documented tokens
        Assert.Contains("M", documented);
        Assert.Contains("F", documented);
        Assert.Contains("P", documented);
    }

    #endregion

    #region Code Block Parsing Tests

    [Fact]
    public void CalorMd_AllCompleteCodeBlocksParseWithoutSyntaxErrors()
    {
        var codeBlocks = ExtractCodeBlocks(CalorMdContent.Value);
        var completeBlocks = codeBlocks
            .Where(b => IsCompleteBlock(b.Content))
            .ToList();

        Assert.True(completeBlocks.Count > 0, "Expected at least one complete code block with §M and §/M");

        var failures = new List<string>();

        foreach (var block in completeBlocks)
        {
            var diagnostics = new DiagnosticBag();
            var lexer = new Lexer(block.Content, diagnostics);
            var tokens = lexer.TokenizeAll();

            // Check for lexer errors
            var lexerErrors = diagnostics.Errors.ToList();
            if (lexerErrors.Count > 0)
            {
                failures.Add(
                    $"Block {block.Index}: Lexer errors:\n" +
                    string.Join("\n", lexerErrors.Select(e => $"  - {e.Message}")));
                continue;
            }

            // Parse the tokens
            diagnostics.Clear();
            var parser = new Parser(tokens, diagnostics);

            try
            {
                parser.Parse();
            }
            catch (Exception ex)
            {
                failures.Add(
                    $"Block {block.Index}: Parser exception: {ex.Message}\n" +
                    $"  Content:\n{IndentContent(TruncateContent(block.Content, 500))}");
                continue;
            }

            // Check for syntax errors (not effect warnings)
            var syntaxErrors = diagnostics
                .Where(d => d.IsError && !IsEffectWarning(d))
                .ToList();

            if (syntaxErrors.Count > 0)
            {
                failures.Add(
                    $"Block {block.Index}: Syntax errors:\n" +
                    string.Join("\n", syntaxErrors.Select(e => $"  - {e.Message}")) +
                    $"\n  Content:\n{IndentContent(TruncateContent(block.Content, 500))}");
            }
        }

        Assert.True(
            failures.Count == 0,
            $"Found {failures.Count} code block(s) with syntax errors:\n\n" +
            string.Join("\n\n", failures));
    }

    [Fact]
    public void CalorMd_ContainsCompleteCodeBlocks()
    {
        var codeBlocks = ExtractCodeBlocks(CalorMdContent.Value);
        var completeBlocks = codeBlocks.Where(b => IsCompleteBlock(b.Content)).ToList();

        Assert.True(
            completeBlocks.Count >= 5,
            $"Expected at least 5 complete code blocks (with §M and §/M), but found {completeBlocks.Count}");
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Extracts all tokens from the Lexer.cs Keywords dictionary using reflection.
    /// </summary>
    private static HashSet<string> ExtractLexerTokens()
    {
        // Use reflection to get the Keywords dictionary from Lexer
        var lexerType = typeof(Lexer);
        var keywordsField = lexerType.GetField("Keywords", BindingFlags.NonPublic | BindingFlags.Static);

        if (keywordsField == null)
        {
            throw new InvalidOperationException("Could not find Keywords field in Lexer class");
        }

        var keywords = keywordsField.GetValue(null) as Dictionary<string, TokenKind>;
        if (keywords == null)
        {
            throw new InvalidOperationException("Keywords field is not a Dictionary<string, TokenKind>");
        }

        return new HashSet<string>(keywords.Keys, StringComparer.Ordinal);
    }

    /// <summary>
    /// Loads the calor.md content from embedded resources or file system.
    /// </summary>
    private static string LoadCalorMd()
    {
        // Try to load from the file system first (for development)
        var possiblePaths = new[]
        {
            // Relative to test output directory
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Calor.Compiler", "Resources", "Skills", "calor.md"),
            // From solution root
            Path.Combine(GetSolutionDirectory(), "src", "Calor.Compiler", "Resources", "Skills", "calor.md"),
        };

        foreach (var path in possiblePaths)
        {
            var normalizedPath = Path.GetFullPath(path);
            if (File.Exists(normalizedPath))
            {
                return File.ReadAllText(normalizedPath);
            }
        }

        throw new FileNotFoundException(
            "Could not find calor.md. Searched paths:\n" +
            string.Join("\n", possiblePaths.Select(p => $"  - {Path.GetFullPath(p)}")));
    }

    /// <summary>
    /// Gets the solution directory by walking up from the current directory.
    /// </summary>
    private static string GetSolutionDirectory()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (Directory.GetFiles(dir, "*.sln").Length > 0)
            {
                return dir;
            }
            dir = Directory.GetParent(dir)?.FullName;
        }

        return AppContext.BaseDirectory;
    }

    /// <summary>
    /// Extracts all documented tokens from the calor.md content.
    /// Looks for patterns like §X, §XX, §XXX, §/X, §??, §?., §^, etc.
    /// </summary>
    private static HashSet<string> ExtractDocumentedTokens(string content)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);

        // Match §TOKEN patterns where TOKEN can include letters, numbers, and underscores
        // Also match closing tags like §/M, §/F, etc.
        var alphanumericRegex = new Regex(@"§(/?[A-Za-z_][A-Za-z0-9_]*)");

        foreach (Match match in alphanumericRegex.Matches(content))
        {
            var token = match.Groups[1].Value;

            // Filter out tokens that are clearly IDs (like m001, f001, etc.)
            // These are parameter values, not token types
            if (!Regex.IsMatch(token, @"^[a-z]+\d+$"))
            {
                tokens.Add(token);
            }
        }

        // Match special character tokens that the alphanumeric regex can't catch
        // §?? = NullCoalesce, §?. = NullConditional, §^ = IndexFromEnd
        if (Regex.IsMatch(content, @"§\?\?"))
        {
            tokens.Add("??");
        }

        if (Regex.IsMatch(content, @"§\?\."))
        {
            tokens.Add("?.");
        }

        if (Regex.IsMatch(content, @"§\^"))
        {
            tokens.Add("^");
        }

        return tokens;
    }

    /// <summary>
    /// Extracts all Calor code blocks from the markdown content.
    /// </summary>
    private static List<CodeBlock> ExtractCodeBlocks(string content)
    {
        var blocks = new List<CodeBlock>();
        var lines = content.Split('\n');
        var inBlock = false;
        var currentBlock = new List<string>();
        var blockIndex = 0;

        foreach (var line in lines)
        {
            var trimmedLine = line.TrimEnd('\r');

            if (trimmedLine == "```calor")
            {
                inBlock = true;
                blockIndex++;
                currentBlock.Clear();
            }
            else if (trimmedLine == "```" && inBlock)
            {
                inBlock = false;
                blocks.Add(new CodeBlock(blockIndex, string.Join("\n", currentBlock)));
            }
            else if (inBlock)
            {
                currentBlock.Add(trimmedLine);
            }
        }

        return blocks;
    }

    /// <summary>
    /// Determines if a code block is complete (has both §M and §/M).
    /// </summary>
    private static bool IsCompleteBlock(string content)
    {
        // A complete block should have both module open and close tags
        return content.Contains("§M{") && content.Contains("§/M{");
    }

    /// <summary>
    /// Determines if a diagnostic is an effect warning (which is OK).
    /// </summary>
    private static bool IsEffectWarning(Diagnostic diagnostic)
    {
        // Effect warnings start with Calor04xx
        return diagnostic.Code.StartsWith("Calor04");
    }

    private record CodeBlock(int Index, string Content);

    /// <summary>
    /// Truncates content to a maximum length, adding ellipsis if truncated.
    /// </summary>
    private static string TruncateContent(string content, int maxLength)
    {
        if (content.Length <= maxLength)
        {
            return content;
        }

        return content[..maxLength] + "\n... (truncated)";
    }

    /// <summary>
    /// Indents each line of content for better formatting in error messages.
    /// </summary>
    private static string IndentContent(string content)
    {
        var lines = content.Split('\n');
        return string.Join("\n", lines.Select(l => "    " + l));
    }

    #endregion
}
