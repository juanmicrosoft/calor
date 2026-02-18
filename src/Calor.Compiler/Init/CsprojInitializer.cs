using System.Xml.Linq;

namespace Calor.Compiler.Init;

/// <summary>
/// Initializes .csproj files with Calor compilation targets.
/// </summary>
public sealed class CsprojInitializer
{
    private const string CalorTargetComment = " Calor Compilation Configuration ";
    private const string CompileCalorFilesTargetName = "CompileCalorFiles";
    private const string IncludeCalorGeneratedFilesTargetName = "IncludeCalorGeneratedFiles";
    private const string ValidateCalorCompilerOverrideTargetName = "ValidateCalorCompilerOverride";
    private const string CleanCalorFilesTargetName = "CleanCalorFiles";

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
    /// Initializes a .csproj file with Calor compilation support.
    /// </summary>
    /// <param name="projectPath">Path to the .csproj file.</param>
    /// <param name="force">If true, replace existing Calor targets.</param>
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
        if (_detector.HasCalorTargets(projectPath) && !force)
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

            // Remove existing Calor configuration if force is true
            if (force)
            {
                RemoveExistingCalorConfig(doc);
            }

            // Add Calor configuration
            AddCalorConfiguration(doc);

            // Save with proper formatting
            await SaveProjectAsync(projectPath, doc);

            return CsprojInitResult.Success(projectPath, new[]
            {
                "Added Calor compilation targets",
                "Generated .cs files will be placed in obj/calor/"
            });
        }
        catch (Exception ex)
        {
            return CsprojInitResult.Error($"Failed to update project: {ex.Message}");
        }
    }

    private void RemoveExistingCalorConfig(XDocument doc)
    {
        // Remove existing Calor targets
        var targetsToRemove = doc.Root!.Elements()
            .Where(e => e.Name.LocalName == "Target" &&
                       (e.Attribute("Name")?.Value == ValidateCalorCompilerOverrideTargetName ||
                        e.Attribute("Name")?.Value == CompileCalorFilesTargetName ||
                        e.Attribute("Name")?.Value == IncludeCalorGeneratedFilesTargetName ||
                        e.Attribute("Name")?.Value == CleanCalorFilesTargetName))
            .ToList();

        foreach (var target in targetsToRemove)
        {
            target.Remove();
        }

        // Remove existing Calor property groups
        var propsToRemove = doc.Root.Elements()
            .Where(e => e.Name.LocalName == "PropertyGroup" &&
                       e.Elements().Any(p => p.Name.LocalName == "CalorOutputDirectory" ||
                                            p.Name.LocalName == "CalorCompilerPath" ||
                                            p.Name.LocalName == "CalorCompilerOverride" ||
                                            p.Name.LocalName == "CalorToolVersion" ||
                                            p.Name.LocalName == "CalorRuntimePath"))
            .ToList();

        foreach (var prop in propsToRemove)
        {
            prop.Remove();
        }

        // Remove existing Calor.Runtime references
        var refsToRemove = doc.Root.Elements()
            .Where(e => e.Name.LocalName == "ItemGroup" &&
                       e.Elements().Any(i => i.Name.LocalName == "Reference" &&
                                            i.Attribute("Include")?.Value == "Calor.Runtime"))
            .ToList();

        foreach (var refItem in refsToRemove)
        {
            refItem.Remove();
        }

        // Remove existing CalorCompile item groups
        var itemsToRemove = doc.Root.Elements()
            .Where(e => e.Name.LocalName == "ItemGroup" &&
                       e.Elements().Any(i => i.Name.LocalName == "CalorCompile"))
            .ToList();

        foreach (var item in itemsToRemove)
        {
            item.Remove();
        }

        // Remove Calor comments
        var comments = doc.Root.Nodes()
            .OfType<XComment>()
            .Where(c => c.Value.Contains("Calor"))
            .ToList();

        foreach (var comment in comments)
        {
            comment.Remove();
        }
    }

    private void AddCalorConfiguration(XDocument doc)
    {
        var root = doc.Root!;

        // Add comment
        root.Add(new XComment(CalorTargetComment));

        // Add PropertyGroup for Calor configuration
        // Use $(BaseIntermediateOutputPath) (defaults to obj/) since $(IntermediateOutputPath) may not be set yet
        var propertyGroup = new XElement("PropertyGroup",
            new XElement("CalorOutputDirectory",
                new XAttribute("Condition", "'$(CalorOutputDirectory)' == ''"),
                @"$(BaseIntermediateOutputPath)$(Configuration)\$(TargetFramework)\calor\"),
            new XElement("CalorCompilerPath",
                new XAttribute("Condition", "'$(CalorCompilerOverride)' != '' and '$(CalorCompilerPath)' == ''"),
                "$(CalorCompilerOverride)"),
            new XElement("CalorCompilerPath",
                new XAttribute("Condition", "'$(CalorCompilerPath)' == ''"),
                "calor"));

        root.Add(propertyGroup);

        // Add PropertyGroup for Calor.Runtime reference
        var runtimePropertyGroup = new XElement("PropertyGroup",
            new XElement("CalorToolVersion",
                new XAttribute("Condition", "'$(CalorToolVersion)' == ''"),
                EmbeddedResourceHelper.GetVersion()),
            new XElement("CalorRuntimePath",
                @"$(HOME)/.dotnet/tools/.store/calor/$(CalorToolVersion)/calor/$(CalorToolVersion)/tools/net8.0/any/Calor.Runtime.dll"));

        root.Add(runtimePropertyGroup);

        // Add ItemGroup for Calor.Runtime reference
        var runtimeItemGroup = new XElement("ItemGroup",
            new XElement("Reference",
                new XAttribute("Include", "Calor.Runtime"),
                new XElement("HintPath", "$(CalorRuntimePath)")));

        root.Add(runtimeItemGroup);

        // Add ItemGroup for Calor source files
        var itemGroup = new XElement("ItemGroup",
            new XElement("CalorCompile",
                new XAttribute("Include", @"**\*.calr"),
                new XAttribute("Exclude", "$(DefaultItemExcludes);$(DefaultExcludesInProjectFolder)")));

        root.Add(itemGroup);

        // Add ValidateCalorCompilerOverride target (no Inputs/Outputs so it always runs)
        var validateTarget = new XElement("Target",
            new XAttribute("Name", ValidateCalorCompilerOverrideTargetName),
            new XAttribute("BeforeTargets", CompileCalorFilesTargetName),
            new XAttribute("Condition", "'$(CalorCompilerOverride)' != '' and '@(CalorCompile)' != ''"),
            new XElement("Error",
                new XAttribute("Condition", "!Exists('$(CalorCompilerOverride)')"),
                new XAttribute("Text", "CalorCompilerOverride points to '$(CalorCompilerOverride)' which does not exist.")),
            new XElement("Warning",
                new XAttribute("Text", "CalorCompilerOverride is set — using local compiler from '$(CalorCompilerOverride)'")));

        root.Add(validateTarget);

        // Add CompileCalorFiles target
        var compileTarget = new XElement("Target",
            new XAttribute("Name", CompileCalorFilesTargetName),
            new XAttribute("BeforeTargets", "BeforeCompile"),
            new XAttribute("Inputs", "@(CalorCompile)"),
            new XAttribute("Outputs", @"@(CalorCompile->'$(CalorOutputDirectory)%(RecursiveDir)%(Filename).g.cs')"),
            new XAttribute("Condition", "'@(CalorCompile)' != ''"),
            new XComment(" Compile Calor files before C# compilation "),
            new XElement("MakeDir",
                new XAttribute("Directories", "$(CalorOutputDirectory)")),
            new XElement("Exec",
                new XAttribute("Command",
                    @"""$(CalorCompilerPath)"" --input ""%(CalorCompile.FullPath)"" --output ""$(CalorOutputDirectory)%(CalorCompile.RecursiveDir)%(CalorCompile.Filename).g.cs""")));

        root.Add(compileTarget);

        // Add IncludeCalorGeneratedFiles target
        var includeTarget = new XElement("Target",
            new XAttribute("Name", IncludeCalorGeneratedFilesTargetName),
            new XAttribute("BeforeTargets", "CoreCompile"),
            new XAttribute("DependsOnTargets", CompileCalorFilesTargetName),
            new XComment(" Include generated files in compilation "),
            new XElement("ItemGroup",
                new XElement("Compile",
                    new XAttribute("Include", @"$(CalorOutputDirectory)**\*.g.cs"))));

        root.Add(includeTarget);

        // Add CleanCalorFiles target
        var cleanTarget = new XElement("Target",
            new XAttribute("Name", CleanCalorFilesTargetName),
            new XAttribute("BeforeTargets", "Clean"),
            new XComment(" Clean generated files "),
            new XElement("RemoveDir",
                new XAttribute("Directories", "$(CalorOutputDirectory)")));

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
    /// Generates the Calor MSBuild targets XML as a string (for preview/testing).
    /// </summary>
    public static string GenerateCalorTargetsXml()
    {
        return $"""
            <!-- Calor Compilation Configuration -->
            <PropertyGroup>
              <CalorOutputDirectory Condition="'$(CalorOutputDirectory)' == ''">$(BaseIntermediateOutputPath)$(Configuration)\$(TargetFramework)\calor\</CalorOutputDirectory>
              <CalorCompilerPath Condition="'$(CalorCompilerOverride)' != '' and '$(CalorCompilerPath)' == ''">$(CalorCompilerOverride)</CalorCompilerPath>
              <CalorCompilerPath Condition="'$(CalorCompilerPath)' == ''">calor</CalorCompilerPath>
            </PropertyGroup>

            <PropertyGroup>
              <CalorToolVersion Condition="'$(CalorToolVersion)' == ''">{EmbeddedResourceHelper.GetVersion()}</CalorToolVersion>
              <CalorRuntimePath>$(HOME)/.dotnet/tools/.store/calor/$(CalorToolVersion)/calor/$(CalorToolVersion)/tools/net8.0/any/Calor.Runtime.dll</CalorRuntimePath>
            </PropertyGroup>

            <ItemGroup>
              <Reference Include="Calor.Runtime">
                <HintPath>$(CalorRuntimePath)</HintPath>
              </Reference>
            </ItemGroup>

            <!-- Calor source files -->
            <ItemGroup>
              <CalorCompile Include="**\*.calr" Exclude="$(DefaultItemExcludes);$(DefaultExcludesInProjectFolder)" />
            </ItemGroup>

            <!-- Validate CalorCompilerOverride (runs unconditionally, not subject to incremental build skip) -->
            <Target Name="ValidateCalorCompilerOverride"
                    BeforeTargets="CompileCalorFiles"
                    Condition="'$(CalorCompilerOverride)' != '' and '@(CalorCompile)' != ''">
              <Error Condition="!Exists('$(CalorCompilerOverride)')"
                     Text="CalorCompilerOverride points to '$(CalorCompilerOverride)' which does not exist." />
              <Warning Text="CalorCompilerOverride is set — using local compiler from '$(CalorCompilerOverride)'" />
            </Target>

            <!-- Compile Calor files before C# compilation -->
            <Target Name="CompileCalorFiles"
                    BeforeTargets="BeforeCompile"
                    Inputs="@(CalorCompile)"
                    Outputs="@(CalorCompile->'$(CalorOutputDirectory)%(RecursiveDir)%(Filename).g.cs')"
                    Condition="'@(CalorCompile)' != ''">
              <MakeDir Directories="$(CalorOutputDirectory)" />
              <Exec Command="&quot;$(CalorCompilerPath)&quot; --input &quot;%(CalorCompile.FullPath)&quot; --output &quot;$(CalorOutputDirectory)%(CalorCompile.RecursiveDir)%(CalorCompile.Filename).g.cs&quot;" />
            </Target>

            <!-- Include generated files in compilation -->
            <Target Name="IncludeCalorGeneratedFiles"
                    BeforeTargets="CoreCompile"
                    DependsOnTargets="CompileCalorFiles">
              <ItemGroup>
                <Compile Include="$(CalorOutputDirectory)**\*.g.cs" />
              </ItemGroup>
            </Target>

            <!-- Clean generated files -->
            <Target Name="CleanCalorFiles" BeforeTargets="Clean">
              <RemoveDir Directories="$(CalorOutputDirectory)" />
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
            Changes = new[] { "Calor targets already present. Use --force to replace." }
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
