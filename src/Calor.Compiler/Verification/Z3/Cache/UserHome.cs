namespace Calor.Compiler.Verification.Z3.Cache;

/// <summary>
/// The root of Calor's user-level state (<c>~/.calor</c>: the default verification cache and the
/// user effect manifests).
/// </summary>
/// <remarks>
/// On Windows, <see cref="System.Environment.SpecialFolder.UserProfile"/> comes from the shell's
/// known folder for the account and ignores the <c>USERPROFILE</c> variable, so a process cannot
/// be given its own user-level cache. NuGet reads the variable for the same reason, and so does
/// this resolver; when <c>USERPROFILE</c> is unset, empty, or not a fully qualified path, it
/// falls back to the known folder. On Linux and macOS it is
/// <see cref="System.Environment.SpecialFolder.UserProfile"/> unchanged, which already follows
/// <c>HOME</c>. In an ordinary Windows session the variable and the known folder are the same
/// directory, so only a process whose <c>USERPROFILE</c> was redirected (a test host isolating
/// its cache, #1135) sees a different root.
/// <para>
/// This file has no dependencies: the #1421 determinism protocol's environment check compiles it
/// as-is to confirm that the root follows the isolated home on every runner.
/// </para>
/// </remarks>
public static class UserHome
{
    /// <summary>The user-level root, or an empty string when none can be determined.</summary>
    public static string Resolve()
    {
        // Fully qualified names: the protocol's probe compiles this file without implicit usings.
        if (System.OperatingSystem.IsWindows())
        {
            var profile = System.Environment.GetEnvironmentVariable("USERPROFILE");
            if (!string.IsNullOrEmpty(profile) && System.IO.Path.IsPathFullyQualified(profile))
                return profile;
        }

        return System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
    }
}
