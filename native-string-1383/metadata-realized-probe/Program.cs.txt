using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Calor.Compiler.Binding;
using Calor.Compiler.Binding.Metadata;
using Calor.Compiler.CodeGen;
using Microsoft.CodeAnalysis;

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: MetadataRealizedProbe <repoRoot> <outJson>");
    return 2;
}

var repo = Path.GetFullPath(args[0]);
var outJson = args[1];
Directory.SetCurrentDirectory(repo);

using var ctx = MetadataContext.Create();
var host = ctx.HostCompilationForBinder;
var contextReferences = host.References.OfType<PortableExecutableReference>().ToArray();
var poolReferences = GeneratedCSharpCompiler.References.OfType<PortableExecutableReference>().ToArray();
var compilerAssembly = typeof(Calor.Compiler.Binding.Binder).Assembly;
var compilerLocation = compilerAssembly.Location;

var result = new
{
    Repo = repo,
    GitHead = RunChecked("git", "rev-parse HEAD", repo).Trim(),
    GitBinderBlob = RunChecked("git", "rev-parse HEAD:src/Calor.Compiler/Binding/Binder.cs", repo).Trim(),
    GitScopeBlob = RunChecked("git", "rev-parse HEAD:src/Calor.Compiler/Binding/Scope.cs", repo).Trim(),
    DotnetInfo = RunChecked("dotnet", "--info", repo),
    LoadedCompilerAssembly = new
    {
        compilerAssembly.GetName().Name,
        compilerAssembly.GetName().Version,
        Location = compilerLocation,
        Sha256 = ShaFile(compilerLocation),
        InformationalVersion = compilerAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
        FileExists = File.Exists(compilerLocation)
    },
    GeneratedCompilerReferencePool = new
    {
        Count = poolReferences.Length,
        References = poolReferences.Select(ReferenceRecord).OrderBy(r => r.FileName, StringComparer.Ordinal).ToArray()
    },
    MetadataContextCompilationReferences = new
    {
        Count = contextReferences.Length,
        References = contextReferences.Select(ReferenceRecord).OrderBy(r => r.FileName, StringComparer.Ordinal).ToArray()
    },
    ContextReferencePathSetEqualsPoolSubset = contextReferences.All(r => poolReferences.Any(p => SamePath(p.FilePath, r.FilePath))),
    PoolMinusContext = poolReferences.Where(p => !contextReferences.Any(r => SamePath(p.FilePath, r.FilePath))).Select(ReferenceRecord).OrderBy(r => r.FileName, StringComparer.Ordinal).ToArray()
};

File.WriteAllText(outJson, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }) + "\n");
return 0;

static bool SamePath(string? a, string? b) =>
    string.Equals(Path.GetFullPath(a ?? string.Empty), Path.GetFullPath(b ?? string.Empty), StringComparison.Ordinal);

static RefRecord ReferenceRecord(PortableExecutableReference reference)
{
    var path = reference.FilePath ?? throw new InvalidOperationException("PortableExecutableReference had null FilePath.");
    if (!File.Exists(path))
        throw new FileNotFoundException("Metadata reference path does not exist.", path);
    AssemblyName? name = null;
    try { name = AssemblyName.GetAssemblyName(path); }
    catch (Exception ex) { throw new InvalidOperationException($"Could not read assembly identity for '{path}'.", ex); }
    return new RefRecord(
        Path.GetFileName(path),
        path,
        ShaFile(path),
        name.Name ?? string.Empty,
        name.Version?.ToString() ?? string.Empty,
        name.CultureName ?? string.Empty,
        Convert.ToHexStringLower(name.GetPublicKeyToken() ?? Array.Empty<byte>()),
        reference.Properties.Kind.ToString(),
        reference.Properties.Aliases.ToArray());
}

static string ShaFile(string path)
{
    using var stream = File.OpenRead(path);
    return Convert.ToHexStringLower(SHA256.HashData(stream));
}

static string RunChecked(string fileName, string arguments, string workingDirectory)
{
    var start = new System.Diagnostics.ProcessStartInfo(fileName, arguments)
    {
        WorkingDirectory = workingDirectory,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    };
    using var process = System.Diagnostics.Process.Start(start)
        ?? throw new InvalidOperationException($"Failed to start {fileName} {arguments}.");
    var stdout = process.StandardOutput.ReadToEnd();
    var stderr = process.StandardError.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0)
        throw new InvalidOperationException($"Command failed ({process.ExitCode}): {fileName} {arguments}\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
    return stdout;
}

sealed record RefRecord(string FileName, string Path, string Sha256, string AssemblyName, string Version, string CultureName, string PublicKeyToken, string Kind, string[] Aliases);
