// 0.25 R0 (#1426) baseline fixture F2-INTERP-03 (#906): a custom interpolated-string handler
// parameter. The handler records each append, so flattening the argument to a string changes
// the observable log.
using System;
using System.Runtime.CompilerServices;
using System.Text;

[InterpolatedStringHandler]
public ref struct RecordingHandler
{
    private StringBuilder builder;

    public RecordingHandler(int literalLength, int formattedCount)
    {
        builder = new StringBuilder();
        builder.Append("H(" + literalLength + "," + formattedCount + ")");
    }

    public void AppendLiteral(string s)
    {
        builder.Append("L[" + s + "]");
    }

    public void AppendFormatted<T>(T value)
    {
        builder.Append("F[" + value + "]");
    }

    public string Result()
    {
        return builder.ToString();
    }
}

public static class Probe
{
    public static string Log(RecordingHandler handler)
    {
        return handler.Result();
    }

    public static string Run()
    {
        int x = 7;
        return Log($"a{x}b");
    }
}
