namespace CsvRowLib;

/// <summary>
/// RFC-4180-style CSV row encoding. A field needs quoting when it
/// contains a comma, a double-quote, CR, or LF; quoted fields double
/// their internal quotes.
/// </summary>
public static class CsvRow
{
    public static bool NeedsQuoting(string field)
    {
        return field.Contains(',') || field.Contains('"')
            || field.Contains('\r') || field.Contains('\n');
    }
}
