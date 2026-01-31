using System.Xml.Linq;

namespace Opal.Compiler.Migration.Project;

/// <summary>
/// Updates .csproj files for OPAL integration during migration scenarios.
/// For new project initialization, use <see cref="Opal.Compiler.Init.CsprojInitializer"/> instead.
/// </summary>
public sealed class CsprojUpdater
{
    private const string CompileOpalFilesTargetName = "CompileOpalFiles";

    /// <summary>
    /// Updates a .csproj file to include OPAL compilation support.
    /// Uses proper incremental build support with obj/opal/ output directory.
    /// </summary>
    public async Task<CsprojUpdateResult> UpdateForOpalAsync(string csprojPath)
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

            // Add Opal.Runtime reference if not present
            if (!HasPackageReference(doc, "Opal.Runtime"))
            {
                AddPackageReference(doc, "Opal.Runtime", "*");
                changes.Add("Added Opal.Runtime package reference");
            }

            // Add opalc tool reference if not present
            if (!HasDotNetTool(doc, "opalc"))
            {
                changes.Add("Consider adding 'dotnet tool install -g opalc' to enable compilation");
            }

            // Add OPAL file compilation items if there are .opal files
            var projectDir = Path.GetDirectoryName(csprojPath) ?? ".";
            var opalFiles = Directory.GetFiles(projectDir, "*.opal", SearchOption.AllDirectories);

            if (opalFiles.Length > 0 && !HasOpalCompileTarget(doc))
            {
                AddOpalCompileTargets(doc);
                changes.Add("Added OPAL compilation targets (output: obj/opal/)");
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
    /// Creates a new .csproj file with OPAL support.
    /// </summary>
    public async Task<CsprojUpdateResult> CreateOpalProjectAsync(string projectPath, string projectName)
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
                    <PackageReference Include="Opal.Runtime" Version="*" />
                  </ItemGroup>

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

                </Project>
                """;

            await File.WriteAllTextAsync(csprojPath, csprojContent);

            return new CsprojUpdateResult
            {
                Success = true,
                Changes = new List<string>
                {
                    $"Created project file: {csprojPath}",
                    "Added Opal.Runtime reference",
                    "Added OPAL compilation targets (output: obj/opal/)"
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

    private static bool HasOpalCompileTarget(XDocument doc)
    {
        return doc.Descendants("Target")
            .Any(e => e.Attribute("Name")?.Value == CompileOpalFilesTargetName ||
                     e.Attribute("Name")?.Value.Equals("CompileOpal", StringComparison.OrdinalIgnoreCase) == true);
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

    private static void AddOpalCompileTargets(XDocument doc)
    {
        var root = doc.Root!;

        // Add comment
        root.Add(new XComment(" OPAL Compilation Configuration "));

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

        // Add CompileOpalFiles target with proper incremental build support
        var compileTarget = new XElement("Target",
            new XAttribute("Name", CompileOpalFilesTargetName),
            new XAttribute("BeforeTargets", "BeforeCompile"),
            new XAttribute("Inputs", "@(OpalCompile)"),
            new XAttribute("Outputs", @"@(OpalCompile->'$(OpalOutputDirectory)%(RecursiveDir)%(Filename).g.cs')"),
            new XAttribute("Condition", "'@(OpalCompile)' != ''"),
            new XElement("MakeDir",
                new XAttribute("Directories", "$(OpalOutputDirectory)")),
            new XElement("Exec",
                new XAttribute("Command",
                    @"""$(OpalCompilerPath)"" --input ""%(OpalCompile.FullPath)"" --output ""$(OpalOutputDirectory)%(OpalCompile.RecursiveDir)%(OpalCompile.Filename).g.cs""")));

        root.Add(compileTarget);

        // Add IncludeOpalGeneratedFiles target
        var includeTarget = new XElement("Target",
            new XAttribute("Name", "IncludeOpalGeneratedFiles"),
            new XAttribute("BeforeTargets", "CoreCompile"),
            new XAttribute("DependsOnTargets", CompileOpalFilesTargetName),
            new XElement("ItemGroup",
                new XElement("Compile",
                    new XAttribute("Include", @"$(OpalOutputDirectory)**\*.g.cs"))));

        root.Add(includeTarget);

        // Add CleanOpalFiles target
        var cleanTarget = new XElement("Target",
            new XAttribute("Name", "CleanOpalFiles"),
            new XAttribute("BeforeTargets", "Clean"),
            new XElement("RemoveDir",
                new XAttribute("Directories", "$(OpalOutputDirectory)")));

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
