using System.Text.Json;

namespace Calor.Verification.Tests.VerifierRuntimeDifferential;

/// <summary>
/// #1421 (0.24 G2): opt-in record for the registered verifier determinism protocol
/// (tests/TestData/DeterminismProtocol/protocol.json). It writes nothing unless
/// CALOR_DETERMINISM_RECORD_DIR is set, and it never changes a verdict, the report, or an
/// assertion. A change to the recorded format is reviewed with the protocol (#1584 removed its hash freeze).
/// Cells are written as compact JSON (no platform newline); generated reports are
/// written as the exact bytes the gate compares, so the protocol can compare them unnormalized.
/// </summary>
internal static class DeterminismRecord
{
    public const string DirectoryVariable = "CALOR_DETERMINISM_RECORD_DIR";

    private static readonly JsonSerializerOptions CellOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static void WriteCells(IReadOnlyList<CaseResult> results) => WriteCells(results, Configured());

    public static void WriteGenerated(string name, byte[] bytes) => WriteGenerated(name, bytes, Configured());

    internal static void WriteCells(IReadOnlyList<CaseResult> results, string? directory)
    {
        if (!string.IsNullOrEmpty(directory))
            File.WriteAllBytes(Path.Combine(Directory.CreateDirectory(directory).FullName, "cells.json"),
                JsonSerializer.SerializeToUtf8Bytes(results, CellOptions));
    }

    internal static void WriteGenerated(string name, byte[] bytes, string? directory)
    {
        if (!string.IsNullOrEmpty(directory))
            File.WriteAllBytes(Path.Combine(Directory.CreateDirectory(Path.Combine(directory, "generated")).FullName, name), bytes);
    }

    private static string? Configured() => Environment.GetEnvironmentVariable(DirectoryVariable);
}
