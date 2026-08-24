using Calor.Compiler.Diagnostics;

namespace Calor.Compiler;

/// <summary>
/// Defines the current semantics version supported by this compiler.
/// See docs/semantics/versioning.md for versioning specification.
/// </summary>
public static class SemanticsVersion
{
    /// <summary>Major version - breaking semantic changes.</summary>
    /// <remarks>
    /// Bumped 1 → 2 as the v0.14 nullability workstream precursor (task #14).
    /// Unblocks S5 severity flip: Calor0272/0273/0274 emit at Error when
    /// <c>SemanticsVersion.Major &gt;= 2</c> (or no <c>§SEMVER</c> directive).
    /// See <c>docs/plans/v0.14-nullability-enforcement-scoping.md</c> D7/F-3
    /// and <c>docs/plans/v0.14-metadata-binding-scoping.md</c> F-7.
    /// </remarks>
    public const int Major = 2;

    /// <summary>Minor version - backward-compatible semantic additions.</summary>
    public const int Minor = 0;

    /// <summary>Patch version - clarifications and bug fixes.</summary>
    public const int Patch = 0;

    /// <summary>Full version object.</summary>
    public static readonly Version Current = new(Major, Minor, Patch);

    /// <summary>Version string for display.</summary>
    public static string VersionString => $"{Major}.{Minor}.{Patch}";

    /// <summary>
    /// Checks if a declared semantics version is compatible with this compiler.
    /// </summary>
    /// <param name="declared">The version declared by a module.</param>
    /// <returns>The compatibility status.</returns>
    public static VersionCompatibility CheckCompatibility(Version declared)
    {
        if (declared.Major > Major)
            return VersionCompatibility.Incompatible;

        if (declared.Major == Major && declared.Minor > Minor)
            return VersionCompatibility.PossiblyIncompatible;

        return VersionCompatibility.Compatible;
    }

    /// <summary>
    /// Checks if a declared semantics version string is compatible with this compiler.
    /// </summary>
    /// <param name="versionString">Version string in format "MAJOR.MINOR.PATCH".</param>
    /// <returns>The compatibility status, or null if the version string is invalid.</returns>
    public static VersionCompatibility? CheckCompatibility(string versionString)
    {
        if (Version.TryParse(versionString, out var declared))
        {
            return CheckCompatibility(declared);
        }
        return null;
    }

    /// <summary>
    /// v0.14 nullability §S5 severity gate for Calor0272 (NullableToNonNullableBinding),
    /// Calor0273 (NullableReturnFromNonNullable), and Calor0274
    /// (NullableArgumentToNonNullableParameter): the three checks emit
    /// <see cref="DiagnosticSeverity.Error"/> once the effective SemVer.Major is at
    /// or past 2, and <see cref="DiagnosticSeverity.Info"/> for legacy §SEMVER[1.0.0]
    /// modules (Phase A behavior). Task #14 bumped <see cref="Major"/> to 2, so the
    /// gate is open by default now; the parameter overload exists to receive a
    /// per-module effective major once the <c>§SEMVER</c> directive is threaded
    /// through the binder in a follow-up slice.
    /// </summary>
    /// <remarks>
    /// See <c>docs/plans/v0.14-nullability-enforcement-scoping.md</c> D7 / F-3
    /// for the gate design and <c>docs/plans/v0.14-metadata-binding-scoping.md</c>
    /// F-7 for the SemVer coupling.
    /// </remarks>
    /// <param name="effectiveMajor">The effective SemVer major of the module under
    /// analysis (from a <c>§SEMVER</c> directive when present, otherwise
    /// <see cref="Major"/>).</param>
    /// <returns><see cref="DiagnosticSeverity.Error"/> when <paramref name="effectiveMajor"/>
    /// is at least 2, otherwise <see cref="DiagnosticSeverity.Info"/>.</returns>
    public static DiagnosticSeverity NullabilitySeverityFor(int effectiveMajor)
        => effectiveMajor >= 2
            ? DiagnosticSeverity.Error
            : DiagnosticSeverity.Info;

    /// <summary>
    /// Convenience overload of <see cref="NullabilitySeverityFor(int)"/> that reads
    /// the current compiler's <see cref="Major"/>. Callers that have not yet been
    /// wired to the per-module effective SemVer use this overload; the binder emit
    /// sites for Calor0272/0273/0274 route through here today.
    /// </summary>
    /// <returns>The severity for nullability diagnostics under the current
    /// compiler <see cref="Major"/>.</returns>
    public static DiagnosticSeverity NullabilitySeverityFor()
        => NullabilitySeverityFor(Major);
}

/// <summary>
/// Represents the compatibility status between a declared semantics version
/// and the compiler's supported version.
/// </summary>
public enum VersionCompatibility
{
    /// <summary>
    /// The declared version is fully compatible with this compiler.
    /// </summary>
    Compatible,

    /// <summary>
    /// The declared version may use features not supported by this compiler.
    /// Emits diagnostic Calor0700 (Warning).
    /// </summary>
    PossiblyIncompatible,

    /// <summary>
    /// The declared version is incompatible with this compiler.
    /// Emits diagnostic Calor0701 (Error).
    /// </summary>
    Incompatible
}
