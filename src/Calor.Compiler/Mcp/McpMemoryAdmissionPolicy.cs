namespace Calor.Compiler.Mcp;

/// <summary>
/// Admission control for heavy MCP tools: refuse to start a compile/convert/analyze
/// when the process is already near its memory ceiling.
///
/// The measurement behind it is whole-process (<c>Process.WorkingSet64</c>), so the
/// policy is only sound where the MCP server owns the process. A handler embedded in
/// somebody else's process would be charging that process's memory to the next tool
/// call, so embedding gets <see cref="Disabled"/> and the shipped stdio server opts
/// in via <see cref="FromEnvironment"/>.
/// </summary>
/// <param name="Enabled">Whether to gate heavy tools on process memory at all.</param>
/// <param name="ThresholdBytes">Process working set above which heavy tools wait, then are refused.</param>
/// <param name="MaxWait">How long to wait for memory to drop before refusing.</param>
/// <param name="PollInterval">How often to re-measure while waiting.</param>
internal sealed record McpMemoryAdmissionPolicy(
    bool Enabled,
    long ThresholdBytes,
    TimeSpan MaxWait,
    TimeSpan PollInterval)
{
    /// <summary>No gating — the correct policy when the handler does not own the process.</summary>
    public static McpMemoryAdmissionPolicy Disabled { get; } =
        new(Enabled: false, ThresholdBytes: long.MaxValue, TimeSpan.Zero, TimeSpan.Zero);

    /// <summary>
    /// The server policy: 50% of available physical memory (minimum 512 MB), or an
    /// explicit ceiling from <c>CALOR_MCP_MAX_MEMORY_MB</c>.
    /// </summary>
    public static McpMemoryAdmissionPolicy FromEnvironment() => new(
        Enabled: true,
        ThresholdBytes: long.TryParse(
            Environment.GetEnvironmentVariable("CALOR_MCP_MAX_MEMORY_MB"), out var megabytes)
                ? megabytes * 1024L * 1024L
                : Math.Max(
                    512L * 1024L * 1024L,
                    (long)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes * 0.5)),
        MaxWait: TimeSpan.FromSeconds(30),
        PollInterval: TimeSpan.FromSeconds(2));
}
