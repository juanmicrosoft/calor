using System.Xml.Linq;

namespace Opal.Compiler.Init;

/// <summary>
/// Initializes .csproj files with OPAL compilation targets.
/// </summary>
public sealed class CsprojInitializer
{
    private const string OpalTargetComment = " OPAL Compilation Configuration ";
    private const string CompileOpalFilesTargetName = "CompileOpalFiles";
    private const string IncludeOpalGeneratedFilesTargetName = "IncludeOpalGeneratedFiles";
    private const string CleanOpalFilesTargetName = "CleanOpalFiles";

    private readonly ProjectDetector _detector;

    public CsprojInitializer()
    {
        _detector = new ProjectDetector();
    }

    public CsprojInitializer(ProjectDetector detector)
    {
        _detector = detector;
    }

    /// <summary>
    /// Initializes a .csproj file with OPAL compilation support.
    /// </summary>
    /// <param name="projectPath">Path to the .csproj file.</param>
    /// <param name="force">If true, replace existing OPAL targets.</param>
    /// <returns>The result of the initialization.</returns>
    public async Task<CsprojInitResult> InitializeAsync(string projectPath, bool force = false)
    {
        if (!File.Exists(projectPath))
        {
            return CsprojInitResult.Error($"Project file not found: {projectPath}");
        }

        // Validate SDK-style project
        var validation = _detector.ValidateProject(projectPath);
        if (!validation.IsValid)
        {
            return CsprojInitResult.Error(validation.ErrorMessage!);
        }

        // Check if already initialized
        if (_detector.HasOpalTargets(projectPath) && !force)
        {
            return CsprojInitResult.AlreadyInitialized(projectPath);
        }

        try
        {
            var content = await File.ReadAllTextAsync(projectPath);
            var doc = XDocument.Parse(content, LoadOptions.PreserveWhitespace);

            if (doc.Root == null)
            {
                return CsprojInitResult.Error("Invalid project file: no root element.");
            }

            // Remove existing OPAL configuration if force is true
            if (force)
            {
                RemoveExistingOpalConfig(doc);
            }

            // Add OPAL configuration
            AddOpalConfiguration(doc);

            // Save with proper formatting
            await SaveProjectAsync(projectPath, doc);

            return CsprojInitResult.Success(projectPath, new[]
            {
                "Added OPAL compilation targets",
                "Generated .cs files will be placed in obj/opal/"
            });
        }
        catch (Exception ex)
        {
            return CsprojInitResult.Error($"Failed to update project: {ex.Message}");
        }
    }

    private void RemoveExistingOpalConfig(XDocument doc)
    {
        // Remove existing OPAL targets
        var targetsToRemove = doc.Root!.Elements()
            .Where(e => e.Name.LocalName == "Target" &&
                       (e.Attribute("Name")?.Value == CompileOpalFilesTargetName ||
                        e.Attribute("Name")?.Value == IncludeOpalGeneratedFilesTargetName ||
                        e.Attribute("Name")?.Value == CleanOpalFilesTargetName))
            .ToList();

        foreach (var target in targetsToRemove)
        {
            target.Remove();
        }

        // Remove existing OPAL property groups
        var propsToRemove = doc.Root.Elements()
            .Where(e => e.Name.LocalName == "PropertyGroup" &&
                       e.Elements().Any(p => p.Name.LocalName == "OpalOutputDirectory"))
            .ToList();

        foreach (var prop in propsToRemove)
        {
            prop.Remove();
        }

        // Remove existing OpalCompile item groups
        var itemsToRemove = doc.Root.Elements()
            .Where(e => e.Name.LocalName == "ItemGroup" &&
                       e.Elements().Any(i => i.Name.LocalName == "OpalCompile"))
            .ToList();

        foreach (var item in itemsToRemove)
        {
            item.Remove();
        }

        // Remove OPAL comments
        var comments = doc.Root.Nodes()
            .OfType<XComment>()
            .Where(c => c.Value.Contains("OPAL"))
            .ToList();

        foreach (var comment in comments)
        {
            comment.Remove();
        }
    }

    private void AddOpalConfiguration(XDocument doc)
    {
        var root = doc.Root!;

        // Add comment
        root.Add(new XComment(OpalTargetComment));

        // Add PropertyGroup for OPAL configuration
        // Use $(BaseIntermediateOutputPath) (defaults to obj/) since $(IntermediateOutputPath) may not be set yet
        var propertyGroup = new XElement("PropertyGroup",
            new XElement("OpalOutputDirectory",
                new XAttribute("Condition", "'$(OpalOutputDirectory)' == ''"),
                @"$(BaseIntermediateOutputPath)$(Configuration)\$(TargetFramework)\opal\"),
            new XElement("OpalCompilerPath",
                new XAttribute("Condition", "'$(OpalCompilerPath)' == ''"),
                "opalc"));

        root.Add(propertyGroup);

        // Add ItemGroup for OPAL source files
        var itemGroup = new XElement("ItemGroup",
            new XElement("OpalCompile",
                new XAttribute("Include", @"**\*.opal"),
                new XAttribute("Exclude", "$(DefaultItemExcludes);$(DefaultExcludesInProjectFolder)")));

        root.Add(itemGroup);

        // Add CompileOpalFiles target
        var compileTarget = new XElement("Target",
            new XAttribute("Name", CompileOpalFilesTargetName),
            new XAttribute("BeforeTargets", "BeforeCompile"),
            new XAttribute("Inputs", "@(OpalCompile)"),
            new XAttribute("Outputs", @"@(OpalCompile->'$(OpalOutputDirectory)%(RecursiveDir)%(Filename).g.cs')"),
            new XAttribute("Condition", "'@(OpalCompile)' != ''"),
            new XComment(" Compile OPAL files before C# compilation "),
            new XElement("MakeDir",
                new XAttribute("Directories", "$(OpalOutputDirectory)")),
            new XElement("Exec",
                new XAttribute("Command",
                    @"""$(OpalCompilerPath)"" --input ""%(OpalCompile.FullPath)"" --output ""$(OpalOutputDirectory)%(OpalCompile.RecursiveDir)%(OpalCompile.Filename).g.cs""")));

        root.Add(compileTarget);

        // Add IncludeOpalGeneratedFiles target
        var includeTarget = new XElement("Target",
            new XAttribute("Name", IncludeOpalGeneratedFilesTargetName),
            new XAttribute("BeforeTargets", "CoreCompile"),
            new XAttribute("DependsOnTargets", CompileOpalFilesTargetName),
            new XComment(" Include generated files in compilation "),
            new XElement("ItemGroup",
                new XElement("Compile",
                    new XAttribute("Include", @"$(OpalOutputDirectory)**\*.g.cs"))));

        root.Add(includeTarget);

        // Add CleanOpalFiles target
        var cleanTarget = new XElement("Target",
            new XAttribute("Name", CleanOpalFilesTargetName),
            new XAttribute("BeforeTargets", "Clean"),
            new XComment(" Clean generated files "),
            new XElement("RemoveDir",
                new XAttribute("Directories", "$(OpalOutputDirectory)")));

        root.Add(cleanTarget);
    }

    private static async Task SaveProjectAsync(string projectPath, XDocument doc)
    {
        // Create backup
        var backupPath = projectPath + ".bak";
        if (File.Exists(projectPath))
        {
            File.Copy(projectPath, backupPath, overwrite: true);
        }

        await using var stream = File.Create(projectPath);
        await using var writer = new StreamWriter(stream);

        // Save with declaration
        await writer.WriteLineAsync("<?xml version=\"1.0\" encoding=\"utf-8\"?>");

        // Format and write the document
        var settings = new System.Xml.XmlWriterSettings
        {
            Async = true,
            Indent = true,
            IndentChars = "  ",
            OmitXmlDeclaration = true
        };

        await using var xmlWriter = System.Xml.XmlWriter.Create(writer, settings);
        doc.WriteTo(xmlWriter);
    }

    /// <summary>
    /// Generates the OPAL MSBuild targets XML as a string (for preview/testing).
    /// </summary>
    public static string GenerateOpalTargetsXml()
    {
        return """
            <!-- OPAL Compilation Configuration -->
            <PropertyGroup>
              <OpalOutputDirectory Condition="'$(OpalOutputDirectory)' == ''">$(BaseIntermediateOutputPath)$(Configuration)\$(TargetFramework)\opal\</OpalOutputDirectory>
              <OpalCompilerPath Condition="'$(OpalCompilerPath)' == ''">opalc</OpalCompilerPath>
            </PropertyGroup>

            <!-- OPAL source files -->
            <ItemGroup>
              <OpalCompile Include="**\*.opal" Exclude="$(DefaultItemExcludes);$(DefaultExcludesInProjectFolder)" />
            </ItemGroup>

            <!-- Compile OPAL files before C# compilation -->
            <Target Name="CompileOpalFiles"
                    BeforeTargets="BeforeCompile"
                    Inputs="@(OpalCompile)"
                    Outputs="@(OpalCompile->'$(OpalOutputDirectory)%(RecursiveDir)%(Filename).g.cs')"
                    Condition="'@(OpalCompile)' != ''">
              <MakeDir Directories="$(OpalOutputDirectory)" />
              <Exec Command="&quot;$(OpalCompilerPath)&quot; --input &quot;%(OpalCompile.FullPath)&quot; --output &quot;$(OpalOutputDirectory)%(OpalCompile.RecursiveDir)%(OpalCompile.Filename).g.cs&quot;" />
            </Target>

            <!-- Include generated files in compilation -->
            <Target Name="IncludeOpalGeneratedFiles"
                    BeforeTargets="CoreCompile"
                    DependsOnTargets="CompileOpalFiles">
              <ItemGroup>
                <Compile Include="$(OpalOutputDirectory)**\*.g.cs" />
              </ItemGroup>
            </Target>

            <!-- Clean generated files -->
            <Target Name="CleanOpalFiles" BeforeTargets="Clean">
              <RemoveDir Directories="$(OpalOutputDirectory)" />
            </Target>
            """;
    }
}

/// <summary>
/// Result of .csproj initialization.
/// </summary>
public sealed class CsprojInitResult
{
    public bool IsSuccess { get; private init; }
    public bool WasAlreadyInitialized { get; private init; }
    public string? ProjectPath { get; private init; }
    public string? ErrorMessage { get; private init; }
    public IReadOnlyList<string> Changes { get; private init; } = Array.Empty<string>();

    private CsprojInitResult() { }

    public static CsprojInitResult Success(string projectPath, IEnumerable<string> changes)
    {
        return new CsprojInitResult
        {
            IsSuccess = true,
            ProjectPath = projectPath,
            Changes = changes.ToList()
        };
    }

    public static CsprojInitResult AlreadyInitialized(string projectPath)
    {
        return new CsprojInitResult
        {
            IsSuccess = true,
            WasAlreadyInitialized = true,
            ProjectPath = projectPath,
            Changes = new[] { "OPAL targets already present. Use --force to replace." }
        };
    }

    public static CsprojInitResult Error(string message)
    {
        return new CsprojInitResult
        {
            IsSuccess = false,
            ErrorMessage = message
        };
    }
}
