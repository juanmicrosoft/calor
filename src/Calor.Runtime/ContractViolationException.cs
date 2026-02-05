namespace Calor.Runtime;

/// <summary>
/// Exception thrown when a contract (precondition, postcondition, or invariant) is violated.
/// </summary>
public class ContractViolationException : Exception
{
    /// <summary>
    /// The stable identifier of the function where the violation occurred.
    /// </summary>
    public string FunctionId { get; }

    /// <summary>
    /// The kind of contract that was violated.
    /// </summary>
    public ContractKind Kind { get; }

    /// <summary>
    /// The start offset of the contract in the source file (0-indexed).
    /// </summary>
    public int StartOffset { get; }

    /// <summary>
    /// The length of the contract span in the source file.
    /// </summary>
    public int Length { get; }

    /// <summary>
    /// The source file path where the contract is defined, if available.
    /// </summary>
    public string? SourceFile { get; }

    /// <summary>
    /// The line number in the source file (1-indexed).
    /// </summary>
    public int Line { get; }

    /// <summary>
    /// The column number in the source file (1-indexed).
    /// </summary>
    public int Column { get; }

    /// <summary>
    /// The condition expression that failed, if available.
    /// </summary>
    public string? Condition { get; }

    /// <summary>
    /// Creates a new ContractViolationException with full details (debug mode).
    /// </summary>
    public ContractViolationException(
        string message,
        string functionId,
        ContractKind kind,
        int startOffset,
        int length,
        string? sourceFile = null,
        int line = 0,
        int column = 0,
        string? condition = null)
        : base(message)
    {
        FunctionId = functionId ?? throw new ArgumentNullException(nameof(functionId));
        Kind = kind;
        StartOffset = startOffset;
        Length = length;
        SourceFile = sourceFile;
        Line = line;
        Column = column;
        Condition = condition;
    }

    /// <summary>
    /// Creates a new ContractViolationException with minimal details (release mode).
    /// </summary>
    public ContractViolationException(string functionId, ContractKind kind)
        : base($"{kind} contract violation in {functionId}")
    {
        FunctionId = functionId ?? throw new ArgumentNullException(nameof(functionId));
        Kind = kind;
        StartOffset = 0;
        Length = 0;
        Line = 0;
        Column = 0;
    }

    /// <summary>
    /// Creates a formatted location string for diagnostics.
    /// </summary>
    public string GetLocationString()
    {
        if (SourceFile == null)
        {
            return Line > 0 ? $"({Line},{Column})" : "";
        }
        return Line > 0 ? $"{SourceFile}({Line},{Column})" : SourceFile;
    }

    /// <summary>
    /// Returns a detailed string representation of the violation.
    /// </summary>
    public override string ToString()
    {
        var location = GetLocationString();
        var details = new List<string>();

        if (!string.IsNullOrEmpty(location))
        {
            details.Add($"Location: {location}");
        }

        details.Add($"Function: {FunctionId}");
        details.Add($"Contract: {Kind}");

        if (Condition != null)
        {
            details.Add($"Condition: {Condition}");
        }

        return $"{Message}\n{string.Join("\n", details)}";
    }
}

/// <summary>
/// The kind of contract that was violated.
/// </summary>
public enum ContractKind
{
    /// <summary>
    /// A precondition (§Q/REQUIRES) was violated.
    /// </summary>
    Requires,

    /// <summary>
    /// A postcondition (§S/ENSURES) was violated.
    /// </summary>
    Ensures,

    /// <summary>
    /// An invariant was violated.
    /// </summary>
    Invariant
}
