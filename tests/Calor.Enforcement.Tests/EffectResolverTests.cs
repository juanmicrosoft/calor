using Calor.Compiler.Effects;
using Calor.Compiler.Effects.Manifests;
using Xunit;

namespace Calor.Enforcement.Tests;

/// <summary>
/// Tests for the effect resolver (layered manifest resolution).
/// </summary>
public class EffectResolverTests
{
    [Fact]
    public void Resolve_ReturnsManifestEffects_ForKnownBclMethods()
    {
        var resolver = new EffectResolver();
        resolver.Initialize();

        var resolution = resolver.Resolve("System.Console", "WriteLine");

        Assert.Equal(EffectResolutionStatus.Resolved, resolution.Status);
        Assert.Contains(resolution.Effects.Effects, e => e.Value == "console_write");
        Assert.Contains("embedded:", resolution.Source);
    }

    [Fact]
    public void Resolve_ReturnsPureExplicit_ForMathMethods()
    {
        var resolver = new EffectResolver();
        resolver.Initialize();

        var resolution = resolver.Resolve("System.Math", "Max");

        // Math.Max should be pure (no effects)
        Assert.True(resolution.Effects.IsEmpty || resolution.Status == EffectResolutionStatus.PureExplicit);
    }

    [Fact]
    public void Resolve_ReturnsUnknown_ForUnknownMethod()
    {
        var resolver = new EffectResolver();
        resolver.Initialize();

        var resolution = resolver.Resolve("UnknownNamespace.UnknownType", "UnknownMethod");

        Assert.Equal(EffectResolutionStatus.Unknown, resolution.Status);
        Assert.True(resolution.Effects.IsUnknown);
    }

    [Fact]
    public void Resolve_UsesManifestForSpecificMethod()
    {
        var json = @"{
            ""version"": ""1.0"",
            ""mappings"": [
                {
                    ""type"": ""MyApp.CustomService"",
                    ""methods"": {
                        ""DoWork"": [""net:w"", ""db:w""]
                    }
                }
            ]
        }";

        var loader = new ManifestLoader();
        loader.LoadFromJson(json, "test");

        var resolver = new EffectResolver(loader);
        resolver.Initialize();

        var resolution = resolver.Resolve("MyApp.CustomService", "DoWork");

        Assert.Equal(EffectResolutionStatus.Resolved, resolution.Status);
        Assert.Contains(resolution.Effects.Effects, e => e.Value == "network_write");
        Assert.Contains(resolution.Effects.Effects, e => e.Value == "database_write");
    }

    [Fact]
    public void Resolve_UsesWildcard_WhenNoSpecificMethod()
    {
        var json = @"{
            ""version"": ""1.0"",
            ""mappings"": [
                {
                    ""type"": ""MyApp.PureService"",
                    ""methods"": {
                        ""*"": []
                    }
                }
            ]
        }";

        var loader = new ManifestLoader();
        loader.LoadFromJson(json, "test");

        var resolver = new EffectResolver(loader);
        resolver.Initialize();

        var resolution = resolver.Resolve("MyApp.PureService", "AnyMethod");

        Assert.Equal(EffectResolutionStatus.PureExplicit, resolution.Status);
        Assert.True(resolution.Effects.IsEmpty);
    }

    [Fact]
    public void Resolve_UsesDefaultEffects_WhenNoMethodMatch()
    {
        var json = @"{
            ""version"": ""1.0"",
            ""mappings"": [
                {
                    ""type"": ""MyApp.IoService"",
                    ""defaultEffects"": [""fs:rw""]
                }
            ]
        }";

        var loader = new ManifestLoader();
        loader.LoadFromJson(json, "test");

        var resolver = new EffectResolver(loader);
        resolver.Initialize();

        var resolution = resolver.Resolve("MyApp.IoService", "SomeMethod");

        Assert.Equal(EffectResolutionStatus.Resolved, resolution.Status);
        Assert.Contains(resolution.Effects.Effects, e => e.Value == "filesystem_readwrite");
    }

    [Fact]
    public void Resolve_UsesNamespaceDefaults_WhenNoTypeMatch()
    {
        var json = @"{
            ""version"": ""1.0"",
            ""mappings"": [],
            ""namespaceDefaults"": {
                ""MyApp.Data"": [""db:rw""]
            }
        }";

        var loader = new ManifestLoader();
        loader.LoadFromJson(json, "test");

        var resolver = new EffectResolver(loader);
        resolver.Initialize();

        var resolution = resolver.Resolve("MyApp.Data.Repository", "GetAll");

        Assert.Equal(EffectResolutionStatus.Resolved, resolution.Status);
        Assert.Contains(resolution.Effects.Effects, e => e.Value == "database_readwrite");
    }

    [Fact]
    public void Resolve_SpecificMethodOverridesWildcard()
    {
        var json = @"{
            ""version"": ""1.0"",
            ""mappings"": [
                {
                    ""type"": ""MyApp.Service"",
                    ""methods"": {
                        ""SpecificMethod"": [""cw""],
                        ""*"": [""fs:rw""]
                    }
                }
            ]
        }";

        var loader = new ManifestLoader();
        loader.LoadFromJson(json, "test");

        var resolver = new EffectResolver(loader);
        resolver.Initialize();

        var specificResolution = resolver.Resolve("MyApp.Service", "SpecificMethod");
        var otherResolution = resolver.Resolve("MyApp.Service", "OtherMethod");

        Assert.Contains(specificResolution.Effects.Effects, e => e.Value == "console_write");
        Assert.DoesNotContain(specificResolution.Effects.Effects, e => e.Value == "filesystem_readwrite");

        Assert.Contains(otherResolution.Effects.Effects, e => e.Value == "filesystem_readwrite");
    }

    [Fact]
    public void ResolveGetter_ReturnsPropertyEffects()
    {
        var json = @"{
            ""version"": ""1.0"",
            ""mappings"": [
                {
                    ""type"": ""MyApp.Config"",
                    ""getters"": {
                        ""Value"": [""env:r""]
                    }
                }
            ]
        }";

        var loader = new ManifestLoader();
        loader.LoadFromJson(json, "test");

        var resolver = new EffectResolver(loader);
        resolver.Initialize();

        var resolution = resolver.ResolveGetter("MyApp.Config", "Value");

        Assert.Equal(EffectResolutionStatus.Resolved, resolution.Status);
        Assert.Contains(resolution.Effects.Effects, e => e.Value == "environment_read");
    }

    [Fact]
    public void ResolveSetter_ReturnsPropertyEffects()
    {
        var json = @"{
            ""version"": ""1.0"",
            ""mappings"": [
                {
                    ""type"": ""MyApp.Config"",
                    ""setters"": {
                        ""Value"": [""env:w""]
                    }
                }
            ]
        }";

        var loader = new ManifestLoader();
        loader.LoadFromJson(json, "test");

        var resolver = new EffectResolver(loader);
        resolver.Initialize();

        var resolution = resolver.ResolveSetter("MyApp.Config", "Value");

        Assert.Equal(EffectResolutionStatus.Resolved, resolution.Status);
        Assert.Contains(resolution.Effects.Effects, e => e.Value == "environment_write");
    }

    [Fact]
    public void ResolveConstructor_ReturnsConstructorEffects()
    {
        var json = @"{
            ""version"": ""1.0"",
            ""mappings"": [
                {
                    ""type"": ""MyApp.FileService"",
                    ""constructors"": {
                        ""(String)"": [""fs:r""]
                    }
                }
            ]
        }";

        var loader = new ManifestLoader();
        loader.LoadFromJson(json, "test");

        var resolver = new EffectResolver(loader);
        resolver.Initialize();

        var resolution = resolver.ResolveConstructor("MyApp.FileService", "String");

        Assert.Equal(EffectResolutionStatus.Resolved, resolution.Status);
        Assert.Contains(resolution.Effects.Effects, e => e.Value == "filesystem_read");
    }

    [Fact]
    public void Resolve_CachesResults()
    {
        var resolver = new EffectResolver();
        resolver.Initialize();

        var resolution1 = resolver.Resolve("System.Console", "WriteLine");
        var resolution2 = resolver.Resolve("System.Console", "WriteLine");

        // Should return same reference due to caching
        Assert.Same(resolution1, resolution2);
    }

    [Fact]
    public void LoadErrors_ReportsManifestProblems()
    {
        var loader = new ManifestLoader();
        loader.LoadFromJson("{ invalid }", "bad-manifest");

        var resolver = new EffectResolver(loader);
        resolver.Initialize();

        Assert.NotEmpty(resolver.LoadErrors);
    }

    // ========================================================================
    // Tier B: Framework interface manifest tests
    // ========================================================================

    [Theory]
    [InlineData("Microsoft.Extensions.Logging.ILogger", "Log", "console_write")]
    [InlineData("Microsoft.Extensions.Logging.LoggerExtensions", "LogInformation", "console_write")]
    [InlineData("Microsoft.Extensions.Logging.LoggerExtensions", "LogError", "console_write")]
    [InlineData("Microsoft.Extensions.Logging.LoggerExtensions", "LogWarning", "console_write")]
    [InlineData("Microsoft.Extensions.Logging.LoggerExtensions", "LogDebug", "console_write")]
    [InlineData("Microsoft.Extensions.Logging.LoggerExtensions", "LogCritical", "console_write")]
    public void TierB_Logger_ResolvesToConsoleWrite(string type, string method, string expectedValue)
    {
        var resolver = new EffectResolver();
        resolver.Initialize();

        var resolution = resolver.Resolve(type, method);

        Assert.NotEqual(EffectResolutionStatus.Unknown, resolution.Status);
        Assert.Contains(resolution.Effects.Effects, e => e.Value == expectedValue);
    }

    [Fact]
    public void TierB_ILogger_IsEnabled_IsPure()
    {
        var resolver = new EffectResolver();
        resolver.Initialize();

        var resolution = resolver.Resolve("Microsoft.Extensions.Logging.ILogger", "IsEnabled");

        Assert.Equal(EffectResolutionStatus.PureExplicit, resolution.Status);
    }

    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore.DbContext", "SaveChanges", "database_write")]
    [InlineData("Microsoft.EntityFrameworkCore.DbContext", "SaveChangesAsync", "database_write")]
    [InlineData("Microsoft.EntityFrameworkCore.DbContext", "Find", "database_read")]
    [InlineData("Microsoft.EntityFrameworkCore.DbContext", "FindAsync", "database_read")]
    public void TierB_DbContext_ResolvesToDatabaseEffects(string type, string method, string expectedValue)
    {
        var resolver = new EffectResolver();
        resolver.Initialize();

        var resolution = resolver.Resolve(type, method);

        Assert.NotEqual(EffectResolutionStatus.Unknown, resolution.Status);
        Assert.Contains(resolution.Effects.Effects, e => e.Value == expectedValue);
    }

    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore.DbContext", "Add", "heap_write")]
    [InlineData("Microsoft.EntityFrameworkCore.DbContext", "Update", "heap_write")]
    [InlineData("Microsoft.EntityFrameworkCore.DbContext", "Remove", "heap_write")]
    public void TierB_DbContext_MutationMethods_ResolveToMut(string type, string method, string expectedValue)
    {
        var resolver = new EffectResolver();
        resolver.Initialize();

        var resolution = resolver.Resolve(type, method);

        Assert.NotEqual(EffectResolutionStatus.Unknown, resolution.Status);
        Assert.Contains(resolution.Effects.Effects, e => e.Value == expectedValue);
    }

    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions", "ToListAsync", "database_read")]
    [InlineData("Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions", "FirstOrDefaultAsync", "database_read")]
    [InlineData("Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions", "CountAsync", "database_read")]
    [InlineData("Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions", "AnyAsync", "database_read")]
    public void TierB_EfCoreQueryExtensions_ResolveToDbRead(string type, string method, string expectedValue)
    {
        var resolver = new EffectResolver();
        resolver.Initialize();

        var resolution = resolver.Resolve(type, method);

        Assert.NotEqual(EffectResolutionStatus.Unknown, resolution.Status);
        Assert.Contains(resolution.Effects.Effects, e => e.Value == expectedValue);
    }

    [Theory]
    [InlineData("Microsoft.Extensions.Configuration.IConfiguration", "GetSection", "environment_read")]
    [InlineData("Microsoft.Extensions.Configuration.IConfigurationSection", "GetSection", "environment_read")]
    [InlineData("Microsoft.Extensions.Configuration.ConfigurationExtensions", "GetConnectionString", "environment_read")]
    public void TierB_Configuration_ResolvesToEnvRead(string type, string method, string expectedValue)
    {
        var resolver = new EffectResolver();
        resolver.Initialize();

        var resolution = resolver.Resolve(type, method);

        Assert.NotEqual(EffectResolutionStatus.Unknown, resolution.Status);
        Assert.Contains(resolution.Effects.Effects, e => e.Value == expectedValue);
    }

    [Fact]
    public void TierB_IServiceProvider_GetService_IsPure()
    {
        var resolver = new EffectResolver();
        resolver.Initialize();

        var resolution = resolver.Resolve("Microsoft.Extensions.DependencyInjection.IServiceProvider", "GetService");

        Assert.Equal(EffectResolutionStatus.PureExplicit, resolution.Status);
    }

    [Theory]
    [InlineData("Microsoft.Extensions.Hosting.IHost", "RunAsync", "network_readwrite")]
    [InlineData("Microsoft.Extensions.Hosting.IHost", "StartAsync", "network_readwrite")]
    [InlineData("Microsoft.Extensions.Hosting.HostingAbstractionsHostExtensions", "Run", "network_readwrite")]
    public void TierB_Hosting_ResolvesToNetRw(string type, string method, string expectedValue)
    {
        var resolver = new EffectResolver();
        resolver.Initialize();

        var resolution = resolver.Resolve(type, method);

        Assert.NotEqual(EffectResolutionStatus.Unknown, resolution.Status);
        Assert.Contains(resolution.Effects.Effects, e => e.Value == expectedValue);
    }

    [Fact]
    public void TierB_IOptions_Value_IsPure()
    {
        var resolver = new EffectResolver();
        resolver.Initialize();

        var resolution = resolver.ResolveGetter("Microsoft.Extensions.Options.IOptions`1", "Value");

        Assert.Equal(EffectResolutionStatus.PureExplicit, resolution.Status);
    }

    [Theory]
    [InlineData("Microsoft.AspNetCore.Mvc.ControllerBase", "Ok")]
    [InlineData("Microsoft.AspNetCore.Mvc.ControllerBase", "NotFound")]
    [InlineData("Microsoft.AspNetCore.Mvc.ControllerBase", "BadRequest")]
    public void TierB_MvcController_ResultMethods_ArePure(string type, string method)
    {
        var resolver = new EffectResolver();
        resolver.Initialize();

        var resolution = resolver.Resolve(type, method);

        Assert.Equal(EffectResolutionStatus.PureExplicit, resolution.Status);
    }

    [Fact]
    public void TierB_HttpResponse_WriteAsync_IsNetWrite()
    {
        var resolver = new EffectResolver();
        resolver.Initialize();

        var resolution = resolver.Resolve("Microsoft.AspNetCore.Http.HttpResponse", "WriteAsync");

        Assert.Equal(EffectResolutionStatus.Resolved, resolution.Status);
        Assert.Contains(resolution.Effects.Effects, e => e.Value == "network_write");
    }

    [Fact]
    public void TierB_DatabaseFacade_Migrate_IsDbWrite()
    {
        var resolver = new EffectResolver();
        resolver.Initialize();

        var resolution = resolver.Resolve("Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade", "Migrate");

        Assert.Equal(EffectResolutionStatus.Resolved, resolution.Status);
        Assert.Contains(resolution.Effects.Effects, e => e.Value == "database_write");
    }

    // ========================================================================
    // BCL manifest tests: System.Text.Json
    // ========================================================================

    [Theory]
    [InlineData("System.Text.Json.JsonSerializer", "Serialize")]
    [InlineData("System.Text.Json.JsonSerializer", "Deserialize")]
    [InlineData("System.Text.Json.JsonSerializer", "SerializeToUtf8Bytes")]
    public void BCL_JsonSerializer_PureMethods_ArePure(string type, string method)
    {
        var resolver = new EffectResolver();
        resolver.Initialize();
        var result = resolver.Resolve(type, method);
        Assert.Equal(EffectResolutionStatus.PureExplicit, result.Status);
    }

    [Fact]
    public void BCL_JsonSerializer_SerializeAsync_HasFsWrite()
    {
        var resolver = new EffectResolver();
        resolver.Initialize();
        var result = resolver.Resolve("System.Text.Json.JsonSerializer", "SerializeAsync");
        Assert.Equal(EffectResolutionStatus.Resolved, result.Status);
        Assert.Contains(result.Effects.Effects, e => e.Value == "filesystem_write");
    }

    [Fact]
    public void BCL_JsonSerializer_DeserializeAsync_HasFsRead()
    {
        var resolver = new EffectResolver();
        resolver.Initialize();
        var result = resolver.Resolve("System.Text.Json.JsonSerializer", "DeserializeAsync");
        Assert.Equal(EffectResolutionStatus.Resolved, result.Status);
        Assert.Contains(result.Effects.Effects, e => e.Value == "filesystem_read");
    }

    [Fact]
    public void BCL_JsonElement_IsPure()
    {
        var resolver = new EffectResolver();
        resolver.Initialize();
        var result = resolver.Resolve("System.Text.Json.JsonElement", "GetString");
        Assert.Equal(EffectResolutionStatus.PureExplicit, result.Status);
    }

    // ========================================================================
    // BCL manifest tests: Regex
    // ========================================================================

    [Theory]
    [InlineData("System.Text.RegularExpressions.Regex", "IsMatch")]
    [InlineData("System.Text.RegularExpressions.Regex", "Match")]
    [InlineData("System.Text.RegularExpressions.Regex", "Replace")]
    [InlineData("System.Text.RegularExpressions.Regex", "Split")]
    public void BCL_Regex_AllMethods_ArePure(string type, string method)
    {
        var resolver = new EffectResolver();
        resolver.Initialize();
        var result = resolver.Resolve(type, method);
        Assert.Equal(EffectResolutionStatus.PureExplicit, result.Status);
    }

    // ========================================================================
    // BCL manifest tests: Concurrent collections
    // ========================================================================

    [Theory]
    [InlineData("System.Collections.Concurrent.ConcurrentDictionary`2", "TryAdd", "heap_write")]
    [InlineData("System.Collections.Concurrent.ConcurrentDictionary`2", "TryRemove", "heap_write")]
    [InlineData("System.Collections.Concurrent.ConcurrentQueue`1", "Enqueue", "heap_write")]
    [InlineData("System.Collections.Concurrent.ConcurrentStack`1", "Push", "heap_write")]
    [InlineData("System.Collections.Concurrent.ConcurrentBag`1", "Add", "heap_write")]
    public void BCL_Concurrent_MutationMethods_HaveMut(string type, string method, string expectedValue)
    {
        var resolver = new EffectResolver();
        resolver.Initialize();
        var result = resolver.Resolve(type, method);
        Assert.Equal(EffectResolutionStatus.Resolved, result.Status);
        Assert.Contains(result.Effects.Effects, e => e.Value == expectedValue);
    }

    [Theory]
    [InlineData("System.Collections.Concurrent.ConcurrentDictionary`2", "TryGetValue")]
    [InlineData("System.Collections.Concurrent.ConcurrentDictionary`2", "ContainsKey")]
    [InlineData("System.Collections.Concurrent.ConcurrentQueue`1", "TryPeek")]
    public void BCL_Concurrent_ReadMethods_ArePure(string type, string method)
    {
        var resolver = new EffectResolver();
        resolver.Initialize();
        var result = resolver.Resolve(type, method);
        Assert.Equal(EffectResolutionStatus.PureExplicit, result.Status);
    }

    // ========================================================================
    // BCL manifest tests: Crypto
    // ========================================================================

    [Theory]
    [InlineData("System.Security.Cryptography.SHA256", "HashData")]
    [InlineData("System.Security.Cryptography.SHA256", "ComputeHash")]
    [InlineData("System.Security.Cryptography.Aes", "Create")]
    [InlineData("System.Security.Cryptography.RSA", "Encrypt")]
    public void BCL_Crypto_DeterministicMethods_ArePure(string type, string method)
    {
        var resolver = new EffectResolver();
        resolver.Initialize();
        var result = resolver.Resolve(type, method);
        Assert.Equal(EffectResolutionStatus.PureExplicit, result.Status);
    }

    [Theory]
    [InlineData("System.Security.Cryptography.RandomNumberGenerator", "GetBytes", "random")]
    [InlineData("System.Security.Cryptography.RandomNumberGenerator", "GetInt32", "random")]
    public void BCL_Crypto_RandomMethods_HaveRand(string type, string method, string expectedValue)
    {
        var resolver = new EffectResolver();
        resolver.Initialize();
        var result = resolver.Resolve(type, method);
        Assert.Equal(EffectResolutionStatus.Resolved, result.Status);
        Assert.Contains(result.Effects.Effects, e => e.Value == expectedValue);
    }

    // ========================================================================
    // Ecosystem manifest tests: Serilog
    // ========================================================================

    [Theory]
    [InlineData("Serilog.Log", "Information", "console_write")]
    [InlineData("Serilog.Log", "Warning", "console_write")]
    [InlineData("Serilog.Log", "Error", "console_write")]
    [InlineData("Serilog.Log", "Fatal", "console_write")]
    [InlineData("Serilog.Log", "Debug", "console_write")]
    [InlineData("Serilog.ILogger", "Information", "console_write")]
    [InlineData("Serilog.ILogger", "Write", "console_write")]
    public void Ecosystem_Serilog_LogMethods_ResolveToCw(string type, string method, string expectedValue)
    {
        var resolver = new EffectResolver();
        resolver.Initialize();
        var result = resolver.Resolve(type, method);
        Assert.NotEqual(EffectResolutionStatus.Unknown, result.Status);
        Assert.Contains(result.Effects.Effects, e => e.Value == expectedValue);
    }

    [Fact]
    public void Ecosystem_Serilog_IsEnabled_IsPure()
    {
        var resolver = new EffectResolver();
        resolver.Initialize();
        var result = resolver.Resolve("Serilog.ILogger", "IsEnabled");
        Assert.Equal(EffectResolutionStatus.PureExplicit, result.Status);
    }

    // ========================================================================
    // Ecosystem manifest tests: Newtonsoft.Json
    // ========================================================================

    [Theory]
    [InlineData("Newtonsoft.Json.JsonConvert", "SerializeObject")]
    [InlineData("Newtonsoft.Json.JsonConvert", "DeserializeObject")]
    [InlineData("Newtonsoft.Json.Linq.JObject", "Parse")]
    [InlineData("Newtonsoft.Json.Linq.JArray", "Parse")]
    [InlineData("Newtonsoft.Json.Linq.JToken", "Parse")]
    public void Ecosystem_NewtonsoftJson_PureMethods(string type, string method)
    {
        var resolver = new EffectResolver();
        resolver.Initialize();
        var result = resolver.Resolve(type, method);
        Assert.Equal(EffectResolutionStatus.PureExplicit, result.Status);
    }

    [Theory]
    [InlineData("Newtonsoft.Json.Linq.JObject", "Add", "heap_write")]
    [InlineData("Newtonsoft.Json.Linq.JObject", "Remove", "heap_write")]
    [InlineData("Newtonsoft.Json.Linq.JArray", "Add", "heap_write")]
    [InlineData("Newtonsoft.Json.JsonConvert", "PopulateObject", "heap_write")]
    public void Ecosystem_NewtonsoftJson_MutationMethods(string type, string method, string expectedValue)
    {
        var resolver = new EffectResolver();
        resolver.Initialize();
        var result = resolver.Resolve(type, method);
        Assert.Equal(EffectResolutionStatus.Resolved, result.Status);
        Assert.Contains(result.Effects.Effects, e => e.Value == expectedValue);
    }

    // ========================================================================
    // Ecosystem manifest tests: Dapper
    // ========================================================================

    [Theory]
    [InlineData("Dapper.SqlMapper", "Query", "database_read")]
    [InlineData("Dapper.SqlMapper", "QueryAsync", "database_read")]
    [InlineData("Dapper.SqlMapper", "QueryFirst", "database_read")]
    [InlineData("Dapper.SqlMapper", "QueryFirstOrDefault", "database_read")]
    [InlineData("Dapper.SqlMapper", "QuerySingle", "database_read")]
    [InlineData("Dapper.SqlMapper", "ExecuteScalar", "database_read")]
    public void Ecosystem_Dapper_ReadMethods(string type, string method, string expectedValue)
    {
        var resolver = new EffectResolver();
        resolver.Initialize();
        var result = resolver.Resolve(type, method);
        Assert.Equal(EffectResolutionStatus.Resolved, result.Status);
        Assert.Contains(result.Effects.Effects, e => e.Value == expectedValue);
    }

    [Theory]
    [InlineData("Dapper.SqlMapper", "Execute", "database_write")]
    [InlineData("Dapper.SqlMapper", "ExecuteAsync", "database_write")]
    public void Ecosystem_Dapper_WriteMethods(string type, string method, string expectedValue)
    {
        var resolver = new EffectResolver();
        resolver.Initialize();
        var result = resolver.Resolve(type, method);
        Assert.Equal(EffectResolutionStatus.Resolved, result.Status);
        Assert.Contains(result.Effects.Effects, e => e.Value == expectedValue);
    }

    // ========================================================================
    // Ecosystem manifest tests: MediatR, AutoMapper, FluentValidation, Polly
    // ========================================================================

    [Theory]
    [InlineData("MediatR.IMediator", "Send")]
    [InlineData("MediatR.IMediator", "Publish")]
    [InlineData("AutoMapper.IMapper", "Map")]
    [InlineData("AutoMapper.IMapper", "ProjectTo")]
    [InlineData("FluentValidation.IValidator", "Validate")]
    [InlineData("FluentValidation.IValidator", "ValidateAsync")]
    [InlineData("Polly.Policy", "Execute")]
    [InlineData("Polly.Policy", "ExecuteAsync")]
    public void Ecosystem_MediatR_AutoMapper_FluentValidation_Polly_ArePure(string type, string method)
    {
        var resolver = new EffectResolver();
        resolver.Initialize();
        var result = resolver.Resolve(type, method);
        Assert.Equal(EffectResolutionStatus.PureExplicit, result.Status);
    }

    [Fact]
    public void Ecosystem_FluentValidation_ValidateAndThrow_HasThrowEffect()
    {
        var resolver = new EffectResolver();
        resolver.Initialize();
        var result = resolver.Resolve("FluentValidation.AbstractValidator`1", "ValidateAndThrow");
        Assert.Equal(EffectResolutionStatus.Resolved, result.Status);
        Assert.Contains(result.Effects.Effects, e => e.Value == "intentional");
    }
}
