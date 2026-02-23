// C# equivalent - no effect system
using System.IO;

public static class FileService
{
    // Effect: file read (not tracked in C#)
    public static string ReadFile(string path) => File.ReadAllText(path);

    // Effect: file write (not tracked in C#)
    public static void WriteFile(string path, string content) => File.WriteAllText(path, content);

    // Effects: file read + file write
    public static void ProcessFile(string input, string output)
    {
        string content = ReadFile(input);
        WriteFile(output, content.ToUpper());
    }
}
