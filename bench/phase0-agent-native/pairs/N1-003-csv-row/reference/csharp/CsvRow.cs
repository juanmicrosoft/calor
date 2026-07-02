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

    public static string EscapeField(string field)
    {
        if (!NeedsQuoting(field))
        {
            return field;
        }
        return "\"" + field.Replace("\"", "\"\"") + "\"";
    }

    public static string JoinRow(List<string> fields)
    {
        string outp = string.Empty;
        for (int i = 0; i < fields.Count; i++)
        {
            if (i > 0)
            {
                outp += ",";
            }
            outp += EscapeField(fields[i]);
        }
        return outp;
    }

    public static List<string> SplitRow(string line)
    {
        var fields = new List<string>();
        string cur = string.Empty;
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        cur += "\"";
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    cur += c;
                }
            }
            else
            {
                if (c == ',')
                {
                    fields.Add(cur);
                    cur = string.Empty;
                }
                else if (c == '"' && cur.Length == 0)
                {
                    inQuotes = true;
                }
                else
                {
                    cur += c;
                }
            }
        }
        fields.Add(cur);
        return fields;
    }
}
