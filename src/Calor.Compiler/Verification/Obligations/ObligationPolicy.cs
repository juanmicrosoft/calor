namespace Calor.Compiler.Verification.Obligations;

/// <summary>
/// Action to take for an obligation with a given status.
/// </summary>
public enum ObligationAction
{
    /// <summary>Ignore the obligation entirely.</summary>
    Ignore,

    /// <summary>Emit a warning diagnostic only.</summary>
    WarnOnly,

    /// <summary>Emit a warning and generate a runtime guard in C#.</summary>
    WarnAndGuard,

    /// <summary>Always generate a runtime guard in C# (no warning).</summary>
    AlwaysGuard,

    /// <summary>Treat as a compilation error.</summary>
    Error
}

/// <summary>
/// Configurable policy that controls how each obligation status maps to compiler behavior.
/// </summary>
public sealed class ObligationPolicy
{
    /// <summary>Action for obligations that the solver discharged (proven).</summary>
    public ObligationAction Discharged { get; init; } = ObligationAction.Ignore;

    /// <summary>Action for obligations that failed (counterexample found).</summary>
    public ObligationAction Failed { get; init; } = ObligationAction.Error;

    /// <summary>Action for obligations where the solver timed out.</summary>
    public ObligationAction Timeout { get; init; } = ObligationAction.WarnAndGuard;

    /// <summary>Action for boundary obligations (public API entry points).</summary>
    public ObligationAction Boundary { get; init; } = ObligationAction.AlwaysGuard;

    /// <summary>Action for obligations with unsupported constructs.</summary>
    public ObligationAction Unsupported { get; init; } = ObligationAction.WarnOnly;

    /// <summary>Action for obligations still pending (solver not run).</summary>
    public ObligationAction Pending { get; init; } = ObligationAction.WarnOnly;

    /// <summary>
    /// Default policy: failed=Error, boundary=AlwaysGuard, timeout=WarnAndGuard.
    /// </summary>
    public static ObligationPolicy Default => new();

    /// <summary>
    /// Strict policy: everything that isn't discharged is an error.
    /// </summary>
    public static ObligationPolicy Strict => new()
    {
        Failed = ObligationAction.Error,
        Timeout = ObligationAction.Error,
        Boundary = ObligationAction.Error,
        Unsupported = ObligationAction.Error,
        Pending = ObligationAction.Error
    };

    /// <summary>
    /// Permissive policy: nothing is an error, guards emitted for failed/boundary/timeout.
    /// </summary>
    public static ObligationPolicy Permissive => new()
    {
        Failed = ObligationAction.WarnAndGuard,
        Timeout = ObligationAction.WarnAndGuard,
        Boundary = ObligationAction.AlwaysGuard,
        Unsupported = ObligationAction.Ignore,
        Pending = ObligationAction.Ignore
    };

    /// <summary>
    /// Gets the action for a given obligation status.
    /// </summary>
    public ObligationAction GetAction(ObligationStatus status) => status switch
    {
        ObligationStatus.Discharged => Discharged,
        ObligationStatus.Failed => Failed,
        ObligationStatus.Timeout => Timeout,
        ObligationStatus.Boundary => Boundary,
        ObligationStatus.Unsupported => Unsupported,
        ObligationStatus.Pending => Pending,
        _ => ObligationAction.WarnOnly
    };

    /// <summary>
    /// Returns true if the action requires emitting a runtime guard.
    /// </summary>
    public static bool RequiresGuard(ObligationAction action) =>
        action is ObligationAction.WarnAndGuard or ObligationAction.AlwaysGuard;

    /// <summary>
    /// Returns true if the action is a compilation error.
    /// </summary>
    public static bool IsError(ObligationAction action) =>
        action == ObligationAction.Error;
}
