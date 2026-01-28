namespace Opal.Compiler.Parsing;

/// <summary>
/// Represents a span of text in source code for error reporting.
/// </summary>
public readonly struct TextSpan : IEquatable<TextSpan>
{
    public static readonly TextSpan Empty = new(0, 0, 1, 1);

    public int Start { get; }
    public int Length { get; }
    public int Line { get; }
    public int Column { get; }

    public int End => Start + Length;

    public TextSpan(int start, int length, int line, int column)
    {
        Start = start;
        Length = length;
        Line = line;
        Column = column;
    }

    public static TextSpan FromBounds(int start, int end, int line, int column)
        => new(start, end - start, line, column);

    public bool Contains(int position) => position >= Start && position < End;

    public bool OverlapsWith(TextSpan other)
        => Start < other.End && End > other.Start;

    public TextSpan Union(TextSpan other)
    {
        var newStart = Math.Min(Start, other.Start);
        var newEnd = Math.Max(End, other.End);
        var newLine = Start <= other.Start ? Line : other.Line;
        var newColumn = Start <= other.Start ? Column : other.Column;
        return new TextSpan(newStart, newEnd - newStart, newLine, newColumn);
    }

    public override string ToString() => $"({Line},{Column})";

    public bool Equals(TextSpan other)
        => Start == other.Start && Length == other.Length;

    public override bool Equals(object? obj)
        => obj is TextSpan other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(Start, Length);

    public static bool operator ==(TextSpan left, TextSpan right) => left.Equals(right);
    public static bool operator !=(TextSpan left, TextSpan right) => !left.Equals(right);
}
