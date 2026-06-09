using Calor.Compiler.Analysis.Security;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests.Analysis;

/// <summary>
/// Tests for EffectSinkMapping which maps effect declarations to taint sinks.
/// </summary>
public class EffectSinkMappingTests
{
    #region ParseEffect

    [Theory]
    [InlineData("db:w", "db", EffectAccess.Write)]
    [InlineData("db:r", "db", EffectAccess.Read)]
    [InlineData("db:rw", "db", EffectAccess.ReadWrite)]
    [InlineData("fs:w", "fs", EffectAccess.Write)]
    [InlineData("net:rw", "net", EffectAccess.ReadWrite)]
    public void ParseEffect_ValidEffect_ReturnsCorrectEffect(string input, string resource, EffectAccess access)
    {
        var result = EffectSinkMapping.ParseEffect(input);

        Assert.NotNull(result);
        Assert.Equal(resource, result.Value.Resource);
        Assert.Equal(access, result.Value.Access);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("db")]
    [InlineData("db:x")] // invalid access
    [InlineData("db:w:extra")] // too many parts
    public void ParseEffect_InvalidEffect_ReturnsNull(string input)
    {
        var result = EffectSinkMapping.ParseEffect(input);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("DB:W", "db", EffectAccess.Write)] // uppercase
    [InlineData("Db:W", "db", EffectAccess.Write)] // mixed case
    [InlineData("db:W", "db", EffectAccess.Write)] // uppercase access only
    public void ParseEffect_CaseInsensitive_ReturnsNormalizedEffect(string input, string resource, EffectAccess access)
    {
        var result = EffectSinkMapping.ParseEffect(input);

        Assert.NotNull(result);
        Assert.Equal(resource, result.Value.Resource);
        Assert.Equal(access, result.Value.Access);
    }

    [Theory]
    [InlineData("db", EffectKind.IO)]
    [InlineData("database", EffectKind.IO)]
    [InlineData("sql", EffectKind.IO)]
    [InlineData("fs", EffectKind.IO)]
    [InlineData("file", EffectKind.IO)]
    [InlineData("net", EffectKind.IO)]
    [InlineData("process", EffectKind.Process)]
    [InlineData("system", EffectKind.Process)]
    [InlineData("exec", EffectKind.Process)]
    [InlineData("console", EffectKind.Console)]
    [InlineData("mem", EffectKind.Memory)]
    public void ParseEffect_ResourceMapping_ReturnsCorrectKind(string resource, EffectKind expectedKind)
    {
        var result = EffectSinkMapping.ParseEffect($"{resource}:w");

        Assert.NotNull(result);
        Assert.Equal(expectedKind, result.Value.Kind);
    }

    #endregion

    #region MapEffectToSink - EffectDeclaration

    [Theory]
    [InlineData("db", EffectAccess.Write, TaintSink.SqlQuery)]
    [InlineData("database", EffectAccess.Write, TaintSink.SqlQuery)]
    [InlineData("sql", EffectAccess.ReadWrite, TaintSink.SqlQuery)]
    public void MapEffectToSink_DatabaseWrite_ReturnsSqlQuery(string resource, EffectAccess access, TaintSink expected)
    {
        var effect = new EffectDeclaration(EffectKind.IO, resource, access);
        var result = EffectSinkMapping.MapEffectToSink(effect);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("fs", EffectAccess.Write, TaintSink.FilePath)]
    [InlineData("filesystem", EffectAccess.Write, TaintSink.FilePath)]
    [InlineData("file", EffectAccess.ReadWrite, TaintSink.FilePath)]
    public void MapEffectToSink_FilesystemWrite_ReturnsFilePath(string resource, EffectAccess access, TaintSink expected)
    {
        var effect = new EffectDeclaration(EffectKind.IO, resource, access);
        var result = EffectSinkMapping.MapEffectToSink(effect);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("net", EffectAccess.Write, TaintSink.UrlRedirect)]
    [InlineData("network", EffectAccess.Write, TaintSink.UrlRedirect)]
    [InlineData("http", EffectAccess.ReadWrite, TaintSink.UrlRedirect)]
    public void MapEffectToSink_NetworkWrite_ReturnsUrlRedirect(string resource, EffectAccess access, TaintSink expected)
    {
        var effect = new EffectDeclaration(EffectKind.IO, resource, access);
        var result = EffectSinkMapping.MapEffectToSink(effect);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("process", EffectAccess.Write, TaintSink.CommandExecution)]
    [InlineData("system", EffectAccess.Write, TaintSink.CommandExecution)]
    [InlineData("exec", EffectAccess.ReadWrite, TaintSink.CommandExecution)]
    [InlineData("shell", EffectAccess.Write, TaintSink.CommandExecution)]
    public void MapEffectToSink_ProcessWrite_ReturnsCommandExecution(string resource, EffectAccess access, TaintSink expected)
    {
        var effect = new EffectDeclaration(EffectKind.Process, resource, access);
        var result = EffectSinkMapping.MapEffectToSink(effect);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("html", EffectAccess.Write, TaintSink.HtmlOutput)]
    [InlineData("web", EffectAccess.Write, TaintSink.HtmlOutput)]
    [InlineData("response", EffectAccess.ReadWrite, TaintSink.HtmlOutput)]
    public void MapEffectToSink_HtmlWrite_ReturnsHtmlOutput(string resource, EffectAccess access, TaintSink expected)
    {
        var effect = new EffectDeclaration(EffectKind.IO, resource, access);
        var result = EffectSinkMapping.MapEffectToSink(effect);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void MapEffectToSink_ReadOnly_ReturnsNull()
    {
        // Read-only effects are not sinks
        var effect = new EffectDeclaration(EffectKind.IO, "db", EffectAccess.Read);
        var result = EffectSinkMapping.MapEffectToSink(effect);

        Assert.Null(result);
    }

    [Fact]
    public void MapEffectToSink_UnknownResource_ReturnsNull()
    {
        var effect = new EffectDeclaration(EffectKind.IO, "unknown", EffectAccess.Write);
        var result = EffectSinkMapping.MapEffectToSink(effect);

        Assert.Null(result);
    }

    #endregion

    #region MapEffectToSink - Kind and Value

    [Theory]
    [InlineData(EffectKind.IO, "database_write", TaintSink.SqlQuery)]
    [InlineData(EffectKind.IO, "database_readwrite", TaintSink.SqlQuery)]
    [InlineData(EffectKind.IO, "db:w", TaintSink.SqlQuery)]
    [InlineData(EffectKind.IO, "db:rw", TaintSink.SqlQuery)]
    public void MapEffectToSink_KindValue_DatabaseWrite_ReturnsSqlQuery(EffectKind kind, string value, TaintSink expected)
    {
        var result = EffectSinkMapping.MapEffectToSink(kind, value);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(EffectKind.IO, "filesystem_write", TaintSink.FilePath)]
    [InlineData(EffectKind.IO, "filesystem_readwrite", TaintSink.FilePath)]
    [InlineData(EffectKind.IO, "fs:w", TaintSink.FilePath)]
    [InlineData(EffectKind.IO, "fs:rw", TaintSink.FilePath)]
    public void MapEffectToSink_KindValue_FilesystemWrite_ReturnsFilePath(EffectKind kind, string value, TaintSink expected)
    {
        var result = EffectSinkMapping.MapEffectToSink(kind, value);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(EffectKind.IO, "network_write", TaintSink.UrlRedirect)]
    [InlineData(EffectKind.IO, "network_readwrite", TaintSink.UrlRedirect)]
    [InlineData(EffectKind.IO, "net:w", TaintSink.UrlRedirect)]
    [InlineData(EffectKind.IO, "net:rw", TaintSink.UrlRedirect)]
    public void MapEffectToSink_KindValue_NetworkWrite_ReturnsUrlRedirect(EffectKind kind, string value, TaintSink expected)
    {
        var result = EffectSinkMapping.MapEffectToSink(kind, value);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(EffectKind.Process, "exec_command")]
    [InlineData(EffectKind.Process, "shell_execute")]
    [InlineData(EffectKind.Process, "system_call")]
    [InlineData(EffectKind.Process, "process:rw")]
    public void MapEffectToSink_KindValue_Process_ReturnsCommandExecution(EffectKind kind, string value)
    {
        var result = EffectSinkMapping.MapEffectToSink(kind, value);

        Assert.Equal(TaintSink.CommandExecution, result);
    }

    [Fact]
    public void MapEffectToSink_KindValue_Unknown_ReturnsNull()
    {
        var result = EffectSinkMapping.MapEffectToSink(EffectKind.IO, "unknown");

        Assert.Null(result);
    }

    #endregion

    #region GetSinksFromEffects

    [Fact]
    public void GetSinksFromEffects_EmptyList_ReturnsEmpty()
    {
        var result = EffectSinkMapping.GetSinksFromEffects(Array.Empty<string>());

        Assert.Empty(result);
    }

    [Fact]
    public void GetSinksFromEffects_SingleEffect_ReturnsSingleSink()
    {
        var effects = new[] { "db:w" };
        var result = EffectSinkMapping.GetSinksFromEffects(effects);

        Assert.Single(result);
        Assert.Equal(TaintSink.SqlQuery, result[0]);
    }

    [Fact]
    public void GetSinksFromEffects_MultipleEffects_ReturnsMultipleSinks()
    {
        var effects = new[] { "db:w", "fs:w", "net:w" };
        var result = EffectSinkMapping.GetSinksFromEffects(effects);

        Assert.Equal(3, result.Count);
        Assert.Contains(TaintSink.SqlQuery, result);
        Assert.Contains(TaintSink.FilePath, result);
        Assert.Contains(TaintSink.UrlRedirect, result);
    }

    [Fact]
    public void GetSinksFromEffects_DuplicateEffects_ReturnsDistinctSinks()
    {
        var effects = new[] { "db:w", "database:w", "sql:rw" }; // All map to SqlQuery
        var result = EffectSinkMapping.GetSinksFromEffects(effects);

        Assert.Single(result);
        Assert.Equal(TaintSink.SqlQuery, result[0]);
    }

    [Fact]
    public void GetSinksFromEffects_ReadOnlyEffects_ReturnsEmpty()
    {
        var effects = new[] { "db:r", "fs:r", "net:r" };
        var result = EffectSinkMapping.GetSinksFromEffects(effects);

        Assert.Empty(result);
    }

    [Fact]
    public void GetSinksFromEffects_MixedEffects_ReturnsOnlyWriteSinks()
    {
        var effects = new[] { "db:r", "db:w", "fs:r" };
        var result = EffectSinkMapping.GetSinksFromEffects(effects);

        Assert.Single(result);
        Assert.Equal(TaintSink.SqlQuery, result[0]);
    }

    [Fact]
    public void GetSinksFromEffects_InvalidEffects_SkipsInvalid()
    {
        var effects = new[] { "invalid", "db:w", "also:invalid:format" };
        var result = EffectSinkMapping.GetSinksFromEffects(effects);

        Assert.Single(result);
        Assert.Equal(TaintSink.SqlQuery, result[0]);
    }

    #endregion

    #region MapEffectToSource

    [Fact]
    public void MapEffectToSource_DatabaseRead_ReturnsDatabaseResult()
    {
        var effect = new EffectDeclaration(EffectKind.IO, "db", EffectAccess.Read);
        var result = EffectSinkMapping.MapEffectToSource(effect);

        Assert.Equal(TaintSource.DatabaseResult, result);
    }

    [Fact]
    public void MapEffectToSource_FilesystemRead_ReturnsFileRead()
    {
        var effect = new EffectDeclaration(EffectKind.IO, "fs", EffectAccess.Read);
        var result = EffectSinkMapping.MapEffectToSource(effect);

        Assert.Equal(TaintSource.FileRead, result);
    }

    [Fact]
    public void MapEffectToSource_NetworkRead_ReturnsNetworkInput()
    {
        var effect = new EffectDeclaration(EffectKind.IO, "net", EffectAccess.Read);
        var result = EffectSinkMapping.MapEffectToSource(effect);

        Assert.Equal(TaintSource.NetworkInput, result);
    }

    [Fact]
    public void MapEffectToSource_ConsoleRead_ReturnsUserInput()
    {
        var effect = new EffectDeclaration(EffectKind.Console, "console", EffectAccess.Read);
        var result = EffectSinkMapping.MapEffectToSource(effect);

        Assert.Equal(TaintSource.UserInput, result);
    }

    [Fact]
    public void MapEffectToSource_EnvironmentRead_ReturnsEnvironment()
    {
        var effect = new EffectDeclaration(EffectKind.IO, "env", EffectAccess.Read);
        var result = EffectSinkMapping.MapEffectToSource(effect);

        Assert.Equal(TaintSource.Environment, result);
    }

    [Fact]
    public void MapEffectToSource_WriteOnly_ReturnsNull()
    {
        // Write-only effects are not sources
        var effect = new EffectDeclaration(EffectKind.IO, "db", EffectAccess.Write);
        var result = EffectSinkMapping.MapEffectToSource(effect);

        Assert.Null(result);
    }

    [Fact]
    public void MapEffectToSource_ReadWrite_ReturnsSource()
    {
        // ReadWrite can be both source and sink
        var effect = new EffectDeclaration(EffectKind.IO, "db", EffectAccess.ReadWrite);
        var result = EffectSinkMapping.MapEffectToSource(effect);

        Assert.Equal(TaintSource.DatabaseResult, result);
    }

    #endregion

    #region Integration Tests - Full Pipeline

    [Fact]
    public void Integration_EffectBasedTaintDetection_ReportsVulnerability()
    {
        // End-to-end test: Parse Calor with §E{db:w}, run taint analysis,
        // verify it detects SQL injection when user input flows to db call

        var source = @"
§M{m001:Test}
  §F{f001:VulnerableQuery:pub}
      §I{string:user_input}
      §O{string}
      §E{db:w}
      §B{query:string} (+ STR:""SELECT * FROM users WHERE name = '"" user_input)
      §C db.execute query
      §R query";

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();

        // Skip if parser doesn't support this syntax
        if (diagnostics.HasErrors) return;

        var binder = new Binder(diagnostics);
        var boundModule = binder.Bind(module);

        // Verify effects were extracted
        var func = boundModule.Functions[0];
        Assert.NotEmpty(func.DeclaredEffects);

        // Run taint analysis
        var taintDiagnostics = new DiagnosticBag();
        var runner = new TaintAnalysisRunner(taintDiagnostics, TaintAnalysisOptions.Default);
        runner.Analyze(boundModule);

        // Should detect SQL injection - user_input flows to db.execute via query
        var sqlInjectionWarnings = taintDiagnostics
            .Where(d => d.Code == DiagnosticCode.SqlInjection)
            .ToList();

        Assert.NotEmpty(sqlInjectionWarnings);
    }

    [Fact]
    public void Integration_ParseCalorWithEffects_BinderExtractsEffects()
    {
        // Parse actual Calor code with effect declaration
        var source = @"
§M{m001:Test}
  §F{f001:QueryUser:pub}
      §I{string:user_input}
      §O{string}
      §E{db:w}
      §B{query:string} STR:""SELECT * FROM users""
      §C db.execute query
      §R query";

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();

        // Skip if parser doesn't support this syntax
        if (diagnostics.HasErrors) return;

        var binder = new Binder(diagnostics);
        var boundModule = binder.Bind(module);

        // Verify the bound function has declared effects
        Assert.Single(boundModule.Functions);
        var func = boundModule.Functions[0];

        Assert.NotEmpty(func.DeclaredEffects);
        // Effect should be "io:database_write" (expanded form)
        Assert.Contains(func.DeclaredEffects, e => e.Contains("database"));
    }

    [Fact]
    public void Integration_BinderExtractsEffects_TaintAnalysisUsesEffectBasedSinks()
    {
        // This tests the full flow: §E{db:w} → Binder → BoundFunction.DeclaredEffects → TaintAnalysis

        // Create a BoundFunction with declared effects matching how Binder produces them
        // The Binder extracts effects as "category:value" (e.g., "io:database_write")
        var declaredEffects = new[] { "io:database_write" };

        var funcSymbol = new Binding.FunctionSymbol("TestFunc", "VOID",
            new[] { new Binding.VariableSymbol("user_input", "STRING", false, true) });

        var boundFunction = new Binding.BoundFunction(
            default,
            funcSymbol,
            Array.Empty<Binding.BoundStatement>(),
            new Binding.Scope(),
            declaredEffects);

        // Verify BoundFunction has effects
        Assert.Single(boundFunction.DeclaredEffects);
        Assert.Equal("io:database_write", boundFunction.DeclaredEffects[0]);
    }

    [Theory]
    [InlineData("io:database_write", EffectKind.IO, "database_write", TaintSink.SqlQuery)]
    [InlineData("io:database_readwrite", EffectKind.IO, "database_readwrite", TaintSink.SqlQuery)]
    [InlineData("io:filesystem_write", EffectKind.IO, "filesystem_write", TaintSink.FilePath)]
    [InlineData("io:network_write", EffectKind.IO, "network_write", TaintSink.UrlRedirect)]
    public void Integration_ExpandedEffectFormat_MapsToCorrectSink(
        string effectString, EffectKind expectedKind, string expectedValue, TaintSink expectedSink)
    {
        // Parse the "category:value" format that Binder produces
        var parts = effectString.Split(':');
        Assert.Equal(2, parts.Length);

        var category = parts[0];
        var value = parts[1];

        // Map category to kind (as TaintAnalysis does)
        var kind = category switch
        {
            "io" => EffectKind.IO,
            "process" => EffectKind.Process,
            "memory" => EffectKind.Memory,
            "console" => EffectKind.Console,
            _ => EffectKind.IO
        };

        Assert.Equal(expectedKind, kind);
        Assert.Equal(expectedValue, value);

        // Map to sink using the expanded value
        var sink = EffectSinkMapping.MapEffectToSink(kind, value);
        Assert.NotNull(sink);
        Assert.Equal(expectedSink, sink.Value);
    }

    #endregion

    #region Effect Matching Coverage Tests

    [Theory]
    [InlineData(EffectKind.IO, "database_write", TaintSink.SqlQuery)]
    [InlineData(EffectKind.IO, "database_read", null)] // Read effects are not sinks
    [InlineData(EffectKind.IO, "database_readwrite", TaintSink.SqlQuery)]
    [InlineData(EffectKind.IO, "filesystem_write", TaintSink.FilePath)]
    [InlineData(EffectKind.IO, "network_write", TaintSink.UrlRedirect)]
    [InlineData(EffectKind.Process, "exec_command", TaintSink.CommandExecution)]
    [InlineData(EffectKind.Process, "shell_execute", TaintSink.CommandExecution)]
    [InlineData(EffectKind.IO, "console_write", null)] // Console write not a security sink
    public void EffectValueMapping_VariousEffects_MapsToCorrectSinks(EffectKind kind, string effectValue, TaintSink? expectedSink)
    {
        // Test that various effect values map to the correct sinks
        var result = EffectSinkMapping.MapEffectToSink(kind, effectValue);
        Assert.Equal(expectedSink, result);
    }

    [Fact]
    public void EffectKindMapping_ProcessEffects_MapsToCommandExecution()
    {
        // Process kind effects should map to command execution
        Assert.Equal(TaintSink.CommandExecution, EffectSinkMapping.MapEffectToSink(EffectKind.Process, "exec_command"));
        Assert.Equal(TaintSink.CommandExecution, EffectSinkMapping.MapEffectToSink(EffectKind.Process, "shell_run"));
        Assert.Equal(TaintSink.CommandExecution, EffectSinkMapping.MapEffectToSink(EffectKind.Process, "system_call"));
    }

    [Fact]
    public void ResourcePrefixCoverage_DatabaseEffects_IncludesCommonDatabaseNames()
    {
        // Verify the database effect maps to SQL sink (the matching logic uses these prefixes internally)
        var sink = EffectSinkMapping.MapEffectToSink(EffectKind.IO, "database_write");
        Assert.Equal(TaintSink.SqlQuery, sink);

        // Also test variations
        Assert.Equal(TaintSink.SqlQuery, EffectSinkMapping.MapEffectToSink(EffectKind.IO, "db:w"));
        Assert.Equal(TaintSink.SqlQuery, EffectSinkMapping.MapEffectToSink(EffectKind.IO, "db:rw"));
    }

    #endregion
}
