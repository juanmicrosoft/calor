using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Calor.Compiler;

namespace Calor.Tasks;

/// <summary>
/// MSBuild task that compiles Calor source files to C#.
/// </summary>
public sealed class CompileCalor : Microsoft.Build.Utilities.Task
{
    /// <summary>
    /// The Calor source files to compile.
    /// </summary>
    [Required]
    public ITaskItem[] SourceFiles { get; set; } = Array.Empty<ITaskItem>();

    /// <summary>
    /// The output directory for generated C# files.
    /// </summary>
    [Required]
    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>
    /// The generated C# files.
    /// </summary>
    [Output]
    public ITaskItem[] GeneratedFiles { get; set; } = Array.Empty<ITaskItem>();

    /// <summary>
    /// Enable verbose logging.
    /// </summary>
    public bool Verbose { get; set; }

    public override bool Execute()
    {
        if (SourceFiles.Length == 0)
        {
            Log.LogMessage(MessageImportance.Normal, "No Calor source files to compile.");
            return true;
        }

        // Ensure output directory exists
        if (!Directory.Exists(OutputDirectory))
        {
            Directory.CreateDirectory(OutputDirectory);
        }

        var generatedFiles = new List<ITaskItem>();
        var success = true;

        foreach (var sourceFile in SourceFiles)
        {
            var inputPath = sourceFile.GetMetadata("FullPath");
            if (string.IsNullOrEmpty(inputPath))
            {
                inputPath = sourceFile.ItemSpec;
            }

            if (!File.Exists(inputPath))
            {
                Log.LogError("Calor source file not found: {0}", inputPath);
                success = false;
                continue;
            }

            var fileName = Path.GetFileNameWithoutExtension(inputPath);
            var outputPath = Path.Combine(OutputDirectory, $"{fileName}.g.cs");

            if (Verbose)
            {
                Log.LogMessage(MessageImportance.High, "Compiling Calor: {0} -> {1}", inputPath, outputPath);
            }

            try
            {
                var source = File.ReadAllText(inputPath);
                var result = Program.Compile(source, inputPath, false);

                if (result.HasErrors)
                {
                    foreach (var diagnostic in result.Diagnostics)
                    {
                        if (diagnostic.IsError)
                        {
                            Log.LogError(
                                subcategory: "Calor",
                                errorCode: diagnostic.Code,
                                helpKeyword: null,
                                file: diagnostic.FilePath ?? inputPath,
                                lineNumber: diagnostic.Span.Line,
                                columnNumber: diagnostic.Span.Column,
                                endLineNumber: 0,
                                endColumnNumber: 0,
                                message: diagnostic.Message);
                        }
                        else if (diagnostic.IsWarning)
                        {
                            Log.LogWarning(
                                subcategory: "Calor",
                                warningCode: diagnostic.Code,
                                helpKeyword: null,
                                file: diagnostic.FilePath ?? inputPath,
                                lineNumber: diagnostic.Span.Line,
                                columnNumber: diagnostic.Span.Column,
                                endLineNumber: 0,
                                endColumnNumber: 0,
                                message: diagnostic.Message);
                        }
                        else
                        {
                            Log.LogMessage(
                                subcategory: "Calor",
                                code: diagnostic.Code,
                                helpKeyword: null,
                                file: diagnostic.FilePath ?? inputPath,
                                lineNumber: diagnostic.Span.Line,
                                columnNumber: diagnostic.Span.Column,
                                endLineNumber: 0,
                                endColumnNumber: 0,
                                importance: MessageImportance.Normal,
                                message: diagnostic.Message);
                        }
                    }
                    success = false;
                    continue;
                }

                File.WriteAllText(outputPath, result.GeneratedCode);

                var outputItem = new TaskItem(outputPath);
                outputItem.SetMetadata("SourceFile", inputPath);
                generatedFiles.Add(outputItem);

                Log.LogMessage(MessageImportance.Normal, "Generated: {0}", outputPath);
            }
            catch (Exception ex)
            {
                Log.LogError("Failed to compile {0}: {1}", inputPath, ex.Message);
                success = false;
            }
        }

        GeneratedFiles = generatedFiles.ToArray();
        return success;
    }
}
