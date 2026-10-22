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
/// <para>Linked via <c>&lt;Compile Include&gt;</c> into every registered test host (the same set
/// that links <c>Z3ConsumerGuardTests.cs</c>), so no test project can read a developer's
/// manifests when it runs on its own.</para>
/// </summary>
internal static class HermeticCalorHome
{
    /// <summary>
    /// Records the root for the whole process. This file is linked into several test assemblies, and
    /// one test host can load more than one of them (a test assembly that references another), each
    /// with its own copy of this class. Without the marker every copy would create its own directory
    /// and the last one loaded would move <c>CALOR_HOME</c> under tests that are already running.
    /// </summary>
    private const string MarkerVariable = "CALOR_TEST_HERMETIC_HOME";

    private static readonly bool CreatedHere;

    /// <summary>The isolated <c>CALOR_HOME</c> this process uses. Created empty by the first copy loaded.</summary>
    public static string Root { get; } = ResolveRoot(out CreatedHere);

    /// <summary>The user-level manifest directory under <see cref="Root"/>. It is never created, so it holds no manifests.</summary>
    public static string EmptyManifestRoot { get; } = Path.Combine(Root, "manifests");

    private static string ResolveRoot(out bool created)
    {
        var existing = Environment.GetEnvironmentVariable(MarkerVariable);
        if (!string.IsNullOrEmpty(existing) && Directory.Exists(existing))
        {
            created = false;
            return existing;
        }

        var root = Directory.CreateTempSubdirectory("calor-test-home-").FullName;
        Environment.SetEnvironmentVariable(MarkerVariable, root);
        created = true;
        return root;
    }

    [ModuleInitializer]
    public static void Initialize()
    {
        // Only the copy that created the root sets CALOR_HOME, so a later assembly load never
        // resets a value a serial test changed on purpose.
        if (!CreatedHere)
            return;
        Environment.SetEnvironmentVariable("CALOR_HOME", Root);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Directory.Delete(Root, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        };
    }
}
