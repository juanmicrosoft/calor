using System.Xml.Linq;

namespace Calor.Compiler.Migration.Project;

/// <summary>
/// Updates .csproj files for Calor integration during migration scenarios.
/// For new project initialization, use <see cref="Calor.Compiler.Init.CsprojInitializer"/> instead.
/// </summary>
public sealed class CsprojUpdater
{
    private const string ValidateCalorCompilerOverrideTargetName = "ValidateCalorCompilerOverride";
    private const string CompileCalorFilesTargetName = "CompileCalorFiles";

    /// <summary>
    /// Updates a .csproj file to include Calor compilation support.
    /// Uses proper incremental build support with obj/calor/ output directory.
    /// </summary>
    public async Task<CsprojUpdateResult> UpdateForCalorAsync(string csprojPath)
    {
        if (!File.Exists(csprojPath))
        {
            return new CsprojUpdateResult
            {
                Success = false,
                ErrorMessage = $"Project file not found: {csprojPath}"
            };
        }

        try
        {
            var content = await File.ReadAllTextAsync(csprojPath);
            var doc = XDocument.Parse(content);

            var changes = new List<string>();

            // Add Calor.Runtime reference if not present
            if (!HasPackageReference(doc, "Calor.Runtime"))
            {
                AddPackageReference(doc, "Calor.Runtime", "*");
                changes.Add("Added Calor.Runtime package reference");
            }

            // Add calor tool reference if not present
            if (!HasDotNetTool(doc, "calor"))
            {
                changes.Add("Consider adding 'dotnet tool install -g calor' to enable compilation");
            }

            // Add Calor file compilation items if there are .calr files
            var projectDir = Path.GetDirectoryName(csprojPath) ?? ".";
            var calorFiles = Directory.GetFiles(projectDir, "*.calr", SearchOption.AllDirectories);

            if (calorFiles.Length > 0 && !HasCalorCompileTarget(doc))
            {
                AddCalorCompileTargets(doc);
                changes.Add("Added Calor compilation targets (output: obj/calor/)");
            }

            // Save changes
            if (changes.Count > 0)
            {
                var backupPath = csprojPath + ".bak";
                File.Copy(csprojPath, backupPath, overwrite: true);

                await using var stream = File.Create(csprojPath);
                await doc.SaveAsync(stream, SaveOptions.None, CancellationToken.None);

                return new CsprojUpdateResult
                {
                    Success = true,
                    Changes = changes,
                    BackupPath = backupPath
                };
            }

            return new CsprojUpdateResult
            {
                Success = true,
                Changes = new List<string> { "No changes needed" }
            };
        }
        catch (Exception ex)
        {
            return new CsprojUpdateResult
            {
                Success = false,
                ErrorMessage = $"Failed to update project: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Creates a new .csproj file with Calor support.
    /// </summary>
    public async Task<CsprojUpdateResult> CreateCalorProjectAsync(string projectPath, string projectName)
    {
        var csprojPath = Path.Combine(projectPath, $"{projectName}.csproj");

        if (File.Exists(csprojPath))
        {
            return new CsprojUpdateResult
            {
                Success = false,
                ErrorMessage = $"Project file already exists: {csprojPath}"
            };
        }

        try
        {
            Directory.CreateDirectory(projectPath);

            var csprojContent = $"""
                <Project Sdk="Microsoft.NET.Sdk">

                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net8.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>

                  <ItemGroup>
                    <PackageReference Include="Calor.Runtime" Version="*" />
                  </ItemGroup>

                  <!-- Calor Compilation Configuration -->
                  <PropertyGroup>
                    <CalorOutputDirectory Condition="'$(CalorOutputDirectory)' == ''">$(BaseIntermediateOutputPath)$(Configuration)\$(TargetFramework)\calor\</CalorOutputDirectory>
                    <CalorCompilerPath Condition="'$(CalorCompilerOverride)' != '' and '$(CalorCompilerPath)' == ''">$(CalorCompilerOverride)</CalorCompilerPath>
                    <CalorCompilerPath Condition="'$(CalorCompilerPath)' == ''">calor</CalorCompilerPath>
                  </PropertyGroup>

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

                </Project>
                """;

            await File.WriteAllTextAsync(csprojPath, csprojContent);

            return new CsprojUpdateResult
            {
                Success = true,
                Changes = new List<string>
                {
                    $"Created project file: {csprojPath}",
                    "Added Calor.Runtime reference",
                    "Added Calor compilation targets (output: obj/calor/)"
                }
            };
        }
        catch (Exception ex)
        {
            return new CsprojUpdateResult
            {
                Success = false,
                ErrorMessage = $"Failed to create project: {ex.Message}"
            };
        }
    }

    private static bool HasPackageReference(XDocument doc, string packageName)
    {
        return doc.Descendants("PackageReference")
            .Any(e => e.Attribute("Include")?.Value.Equals(packageName, StringComparison.OrdinalIgnoreCase) == true);
    }

    private static bool HasDotNetTool(XDocument doc, string toolName)
    {
        return doc.Descendants("DotNetCliToolReference")
            .Any(e => e.Attribute("Include")?.Value.Contains(toolName, StringComparison.OrdinalIgnoreCase) == true);
    }

    private static bool HasCalorCompileTarget(XDocument doc)
    {
        return doc.Descendants("Target")
            .Any(e => e.Attribute("Name")?.Value == CompileCalorFilesTargetName ||
                     e.Attribute("Name")?.Value.Equals("CompileCalor", StringComparison.OrdinalIgnoreCase) == true);
    }

    private static void AddPackageReference(XDocument doc, string packageName, string version)
    {
        var itemGroup = doc.Descendants("ItemGroup")
            .FirstOrDefault(g => g.Elements("PackageReference").Any());

        if (itemGroup == null)
        {
            itemGroup = new XElement("ItemGroup");
            doc.Root?.Add(itemGroup);
        }

        itemGroup.Add(new XElement("PackageReference",
            new XAttribute("Include", packageName),
            new XAttribute("Version", version)));
    }

    private static void AddCalorCompileTargets(XDocument doc)
    {
        var root = doc.Root!;

        // Add comment
        root.Add(new XComment(" Calor Compilation Configuration "));

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

        // Add CompileCalorFiles target with proper incremental build support
        var compileTarget = new XElement("Target",
            new XAttribute("Name", CompileCalorFilesTargetName),
            new XAttribute("BeforeTargets", "BeforeCompile"),
            new XAttribute("Inputs", "@(CalorCompile)"),
            new XAttribute("Outputs", @"@(CalorCompile->'$(CalorOutputDirectory)%(RecursiveDir)%(Filename).g.cs')"),
            new XAttribute("Condition", "'@(CalorCompile)' != ''"),
            new XElement("MakeDir",
                new XAttribute("Directories", "$(CalorOutputDirectory)")),
            new XElement("Exec",
                new XAttribute("Command",
                    @"""$(CalorCompilerPath)"" --input ""%(CalorCompile.FullPath)"" --output ""$(CalorOutputDirectory)%(CalorCompile.RecursiveDir)%(CalorCompile.Filename).g.cs""")));

        root.Add(compileTarget);

        // Add IncludeCalorGeneratedFiles target
        var includeTarget = new XElement("Target",
            new XAttribute("Name", "IncludeCalorGeneratedFiles"),
            new XAttribute("BeforeTargets", "CoreCompile"),
            new XAttribute("DependsOnTargets", CompileCalorFilesTargetName),
            new XElement("ItemGroup",
                new XElement("Compile",
                    new XAttribute("Include", @"$(CalorOutputDirectory)**\*.g.cs"))));

        root.Add(includeTarget);

        // Add CleanCalorFiles target
        var cleanTarget = new XElement("Target",
            new XAttribute("Name", "CleanCalorFiles"),
            new XAttribute("BeforeTargets", "Clean"),
            new XElement("RemoveDir",
                new XAttribute("Directories", "$(CalorOutputDirectory)")));

        root.Add(cleanTarget);
    }
}

/// <summary>
/// Result of updating a .csproj file.
/// </summary>
public sealed class CsprojUpdateResult
{
    public required bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public List<string> Changes { get; init; } = new();
    public string? BackupPath { get; init; }
}
