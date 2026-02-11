using System.Reflection;
using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Calor.LanguageServer.Handlers;
using Calor.LanguageServer.State;
using Calor.LanguageServer.Utilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Calor.LanguageServer.Tests.Helpers;

/// <summary>
/// Test harness for LSP testing. Provides utilities for creating documents,
/// finding symbols, and testing completions.
/// </summary>
public static class LspTestHarness
{
    /// <summary>
    /// Creates a DocumentState from source code.
    /// </summary>
    public static DocumentState CreateDocument(string source, string uri = "file:///test.calr")
    {
        var state = new DocumentState(new Uri(uri), source, 1);
        state.Reanalyze();
        return state;
    }

    /// <summary>
    /// Finds a symbol at a specific position in the source.
    /// </summary>
    public static SymbolLookupResult? FindSymbol(string source, int line, int column)
    {
        var state = CreateDocument(source);
        if (state.Ast == null) return null;
        return SymbolFinder.FindSymbolAtPosition(state.Ast, line, column, source);
    }

    /// <summary>
    /// Wraps source code in a module and function for testing.
    /// </summary>
    public static string WrapInFunction(string bodySource)
    {
        return $$"""
            §M{m001:TestModule}
            §F{f001:TestFunc}
            {{bodySource}}
            §/F{f001}
            §/M{m001}
            """;
    }

    /// <summary>
    /// Wraps source code in a module for testing.
    /// </summary>
    public static string WrapInModule(string moduleContent)
    {
        return $$"""
            §M{m001:TestModule}
            {{moduleContent}}
            §/M{m001}
            """;
    }

    /// <summary>
    /// Compiles source and returns the AST (null if errors).
    /// </summary>
    public static ModuleNode? GetAst(string source)
    {
        var state = CreateDocument(source);
        return state.Ast;
    }

    /// <summary>
    /// Compiles source and returns diagnostics.
    /// </summary>
    public static DiagnosticBag GetDiagnostics(string source)
    {
        var state = CreateDocument(source);
        return state.Diagnostics;
    }

    /// <summary>
    /// Compiles source and returns diagnostics with fixes.
    /// </summary>
    public static List<DiagnosticWithFix> GetDiagnosticsWithFixes(string source)
    {
        var state = CreateDocument(source);
        return state.DiagnosticsWithFixes;
    }

    /// <summary>
    /// Converts line/column (1-based) to offset.
    /// </summary>
    public static int GetOffset(string source, int line, int column)
    {
        var currentLine = 1;
        var offset = 0;

        for (var i = 0; i < source.Length; i++)
        {
            if (currentLine == line)
            {
                return offset + column - 1;
            }

            if (source[i] == '\n')
            {
                currentLine++;
                offset = i + 1;
            }
        }

        if (currentLine == line)
        {
            return offset + column - 1;
        }

        return source.Length;
    }

    /// <summary>
    /// Converts offset to line/column (1-based).
    /// </summary>
    public static (int Line, int Column) GetLineColumn(string source, int offset)
    {
        var line = 1;
        var column = 1;

        for (var i = 0; i < offset && i < source.Length; i++)
        {
            if (source[i] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        return (line, column);
    }

    /// <summary>
    /// Finds the position of a marker in source code (e.g., "/*cursor*/").
    /// </summary>
    public static (string CleanSource, int Line, int Column) FindMarker(string sourceWithMarker, string marker = "/*cursor*/")
    {
        var markerIndex = sourceWithMarker.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            throw new ArgumentException($"Marker '{marker}' not found in source");
        }

        var cleanSource = sourceWithMarker.Remove(markerIndex, marker.Length);
        var (line, column) = GetLineColumn(sourceWithMarker, markerIndex);

        return (cleanSource, line, column);
    }

    /// <summary>
    /// Gets completion items for a given source at the position of a pattern (e.g., "obj.").
    /// </summary>
    public static IEnumerable<CompletionItem> GetCompletions(string source, string pattern)
    {
        var patternIndex = source.IndexOf(pattern, StringComparison.Ordinal);
        if (patternIndex < 0)
        {
            return Enumerable.Empty<CompletionItem>();
        }

        // Position at the end of the pattern (after the dot)
        var offset = patternIndex + pattern.Length;
        var state = CreateDocument(source);
        var workspace = new WorkspaceState();
        workspace.GetOrCreate(state.Uri, state.Source);

        // Use reflection to call the private static GetMemberCompletions method
        var handlerType = typeof(CompletionHandler);
        var getMemberCompletions = handlerType.GetMethod("GetMemberCompletions",
            BindingFlags.NonPublic | BindingFlags.Static);

        if (getMemberCompletions == null || state.Ast == null)
        {
            return Enumerable.Empty<CompletionItem>();
        }

        try
        {
            var result = getMemberCompletions.Invoke(null, new object[] { state, offset, workspace });
            return result as IEnumerable<CompletionItem> ?? Enumerable.Empty<CompletionItem>();
        }
        catch
        {
            return Enumerable.Empty<CompletionItem>();
        }
    }
}
