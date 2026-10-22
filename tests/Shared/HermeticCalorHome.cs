using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace Calor.Tests.Shared;

/// <summary>
/// #1564: points <c>CALOR_HOME</c> at an empty directory for the whole test process, so no
/// in-process conversion, compile or effect resolution reads the user-level manifests in
/// the developer's <c>~/.calor/manifests</c>.
///
/// <para>The converter resolves its user-level manifest root from <c>CALOR_HOME</c> (then
/// <c>HOME</c>) when each conversion starts, so this covers every entry point that cannot
/// take <c>ConversionOptions.UserManifestRoot</c>: the MCP tools, project migration, and
/// <c>Program.Compile</c>'s effect pass. CLI children inherit the variable as well. Harnesses
/// that build their own <c>ConversionOptions</c> also inject <see cref="EmptyManifestRoot"/>
/// directly, so they stay hermetic even if a test changes the variable.</para>
///
/// <para>A test that changes <c>CALOR_HOME</c> or <c>HOME</c> must run in a collection with
/// parallelization disabled and restore the value, or it would move the root under
/// concurrently running tests.</para>
///
/// <para>Linked into the test assemblies that convert or resolve effects in process
/// (Calor.Compiler.Tests, Calor.Conversion.Tests, Calor.Enforcement.Tests,
/// Calor.RoundTrip.Harness.Tests) via <c>&lt;Compile Include&gt;</c>.</para>
/// </summary>
internal static class HermeticCalorHome
{
    /// <summary>The isolated <c>CALOR_HOME</c> this process uses. Created empty at load.</summary>
    public static string Root { get; } =
        Directory.CreateTempSubdirectory("calor-test-home-").FullName;

    /// <summary>The user-level manifest directory under <see cref="Root"/>. It is never created, so it holds no manifests.</summary>
    public static string EmptyManifestRoot { get; } = Path.Combine(Root, "manifests");

    [ModuleInitializer]
    public static void Initialize()
    {
        Environment.SetEnvironmentVariable("CALOR_HOME", Root);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Directory.Delete(Root, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        };
    }
}
