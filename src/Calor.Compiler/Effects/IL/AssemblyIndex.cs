using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;

namespace Calor.Compiler.Effects.IL;

/// <summary>
/// A loaded assembly with its PEReader and MetadataReader.
/// </summary>
public sealed class LoadedAssembly : IDisposable
{
    public string FilePath { get; }
    public string Name { get; }
    public PEReader PEReader { get; }
    public MetadataReader MetadataReader { get; }

    public LoadedAssembly(string filePath, PEReader peReader)
    {
        FilePath = filePath;
        PEReader = peReader;
        MetadataReader = peReader.GetMetadataReader();
        var assemblyDef = MetadataReader.GetAssemblyDefinition();
        Name = MetadataReader.GetString(assemblyDef.Name);
    }

    public void Dispose() => PEReader.Dispose();
}

/// <summary>
/// A resolved method location within a loaded assembly.
/// </summary>
public sealed class MethodLocation
{
    public LoadedAssembly Assembly { get; }
    public MethodDefinitionHandle Handle { get; }
    public MethodKey Key { get; }
    public bool HasBody { get; }
    public bool IsAbstract { get; }
    public bool IsVirtual { get; }

    public MethodLocation(LoadedAssembly assembly, MethodDefinitionHandle handle, MethodKey key,
        bool hasBody, bool isAbstract, bool isVirtual)
    {
        Assembly = assembly;
        Handle = handle;
        Key = key;
        HasBody = hasBody;
        IsAbstract = isAbstract;
        IsVirtual = isVirtual;
    }
}

/// <summary>
/// Loads .NET assemblies and builds cross-assembly type hierarchy and
/// interface implementation indexes for virtual dispatch resolution.
///
/// Two-phase loading:
///   Phase 1 (eager): Scan all assembly TypeDefinition tables for type names (~1-5ms/assembly).
///   Phase 2 (lazy): Load method bodies on demand when FindMethod() is called.
///
/// Handles reference assembly detection and resolution to implementation assemblies.
/// </summary>
public sealed class AssemblyIndex : IDisposable
{
    private readonly ILAnalysisOptions _options;
    private readonly List<LoadedAssembly> _assemblies = [];

    // Phase 1: type name → assembly (eager, built on construction)
    // Key: fully qualified type name. On collision, first-loaded wins (higher-priority assembly).
    private readonly Dictionary<string, LoadedAssembly> _typeIndex = new(StringComparer.Ordinal);

    // Phase 1: type name → TypeDefinitionHandle (for looking up methods)
    private readonly Dictionary<string, (LoadedAssembly Assembly, TypeDefinitionHandle Handle)> _typeHandleIndex
        = new(StringComparer.Ordinal);

    // Interface/abstract method → list of concrete implementations
    private readonly Dictionary<(string TypeName, string MethodName), List<MethodKey>> _implementationIndex = [];

    // Type → direct subtypes (for abstract class resolution)
    private readonly Dictionary<string, List<string>> _subtypeIndex = new(StringComparer.Ordinal);

    // Deps.json runtime mappings: assembly simple name → implementation path
    private readonly Dictionary<string, string> _depsRuntimePaths = new(StringComparer.OrdinalIgnoreCase);

    private bool _disposed;

    public AssemblyIndex(IReadOnlyList<string> assemblyPaths, ILAnalysisOptions? options = null)
    {
        _options = options ?? new ILAnalysisOptions();
        LoadDepsJson();
        LoadAssemblies(assemblyPaths);
        BuildImplementationIndex();
    }

    /// <summary>
    /// Finds a method in the loaded assemblies.
    /// Returns null if the type or method is not found.
    /// </summary>
    public MethodLocation? FindMethod(string typeName, string methodName, string? parameterSig = null)
    {
        if (!_typeHandleIndex.TryGetValue(typeName, out var entry))
            return null;

        var (assembly, typeHandle) = entry;
        var reader = assembly.MetadataReader;
        var typeDef = reader.GetTypeDefinition(typeHandle);

        foreach (var methodHandle in typeDef.GetMethods())
        {
            try
            {
                var methodDef = reader.GetMethodDefinition(methodHandle);
                var name = reader.GetString(methodDef.Name);
                if (name != methodName)
                    continue;

                var key = MethodKey.FromDefinition(reader, methodHandle);

                // If parameterSig specified, match exactly
                if (parameterSig != null && parameterSig != "*" && key.ParameterSig != parameterSig)
                    continue;

                var attributes = methodDef.Attributes;
                var isAbstract = (attributes & MethodAttributes.Abstract) != 0;
                var isVirtual = (attributes & MethodAttributes.Virtual) != 0;
                var hasBody = !isAbstract
                    && (attributes & MethodAttributes.PinvokeImpl) == 0
                    && methodDef.RelativeVirtualAddress != 0;

                return new MethodLocation(assembly, methodHandle, key, hasBody, isAbstract, isVirtual);
            }
            catch (BadImageFormatException)
            {
                continue;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds a method by its MethodKey (exact signature match).
    /// </summary>
    public MethodLocation? FindMethod(MethodKey key)
    {
        return FindMethod(key.TypeName, key.MethodName,
            key.ParameterSig == "*" ? null : key.ParameterSig);
    }

    /// <summary>
    /// Gets all concrete implementations of an interface or abstract method.
    /// </summary>
    public IReadOnlyList<MethodKey> GetImplementations(MethodKey key)
    {
        var nameKey = (key.TypeName, key.MethodName);
        if (_implementationIndex.TryGetValue(nameKey, out var impls))
            return impls;

        // Check subtypes for abstract class methods
        if (_subtypeIndex.TryGetValue(key.TypeName, out var subtypes))
        {
            var results = new List<MethodKey>();
            foreach (var subtype in subtypes)
            {
                var location = FindMethod(subtype, key.MethodName);
                if (location != null && location.HasBody)
                    results.Add(location.Key);
            }
            if (results.Count > 0)
            {
                _implementationIndex[nameKey] = results;
                return results;
            }
        }

        return [];
    }

    /// <summary>
    /// Gets all loaded assemblies (for diagnostics/debugging).
    /// </summary>
    public IReadOnlyList<LoadedAssembly> LoadedAssemblies => _assemblies;

    private void LoadAssemblies(IReadOnlyList<string> assemblyPaths)
    {
        foreach (var path in assemblyPaths)
        {
            try
            {
                var resolvedPath = ResolveAssemblyPath(path);
                if (resolvedPath == null)
                    continue;

                var stream = File.OpenRead(resolvedPath);
                var peReader = new PEReader(stream);

                if (!peReader.HasMetadata)
                {
                    peReader.Dispose();
                    continue;
                }

                var assembly = new LoadedAssembly(resolvedPath, peReader);
                _assemblies.Add(assembly);

                // Phase 1: scan type definitions (cheap — just reads names)
                IndexTypes(assembly);
            }
            catch (BadImageFormatException) { /* malformed PE — skip */ }
            catch (IOException) { /* file access error — skip */ }
            catch (InvalidOperationException) { /* no metadata — skip */ }
        }
    }

    /// <summary>
    /// Resolves a reference assembly path to its implementation assembly.
    /// Returns the original path if it's already an implementation assembly,
    /// the resolved impl path if found, or null if unresolvable.
    /// </summary>
    private string? ResolveAssemblyPath(string path)
    {
        if (!File.Exists(path))
            return null;

        // Quick check: is this a reference assembly?
        if (!IsReferenceAssembly(path))
            return path; // Already an implementation assembly

        // Strategy 1: NuGet ref→lib path swap
        var implPath = TryRefToLibSwap(path);
        if (implPath != null)
            return implPath;

        // Strategy 2: deps.json runtime mapping
        var assemblyName = Path.GetFileNameWithoutExtension(path);
        if (_depsRuntimePaths.TryGetValue(assemblyName, out var depsPath) && File.Exists(depsPath))
            return depsPath;

        // Strategy 3: Runtime directory for BCL assemblies
        if (!string.IsNullOrEmpty(_options.RuntimeDirectory))
        {
            var runtimePath = Path.Combine(_options.RuntimeDirectory, Path.GetFileName(path));
            if (File.Exists(runtimePath))
                return runtimePath;
        }

        // Unresolvable — will be skipped
        return null;
    }

    private static bool IsReferenceAssembly(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata) return false;

            var reader = peReader.GetMetadataReader();
            foreach (var attrHandle in reader.GetAssemblyDefinition().GetCustomAttributes())
            {
                var attr = reader.GetCustomAttribute(attrHandle);
                if (attr.Constructor.Kind == HandleKind.MemberReference)
                {
                    var ctor = reader.GetMemberReference((MemberReferenceHandle)attr.Constructor);
                    if (ctor.Parent.Kind == HandleKind.TypeReference)
                    {
                        var typeRef = reader.GetTypeReference((TypeReferenceHandle)ctor.Parent);
                        var name = reader.GetString(typeRef.Name);
                        if (name == "ReferenceAssemblyAttribute")
                            return true;
                    }
                }
            }
            return false;
        }
        catch { return false; }
    }

    private static string? TryRefToLibSwap(string refPath)
    {
        // NuGet layout: packages/{id}/{version}/ref/{tfm}/Assembly.dll
        // Implementation: packages/{id}/{version}/lib/{tfm}/Assembly.dll
        var normalized = refPath.Replace('\\', '/');
        var refIndex = normalized.LastIndexOf("/ref/", StringComparison.OrdinalIgnoreCase);
        if (refIndex < 0) return null;

        var libPath = normalized[..refIndex] + "/lib/" + normalized[(refIndex + 5)..];
        libPath = libPath.Replace('/', Path.DirectorySeparatorChar);
        return File.Exists(libPath) ? libPath : null;
    }

    private void LoadDepsJson()
    {
        if (string.IsNullOrEmpty(_options.DepsFilePath) || !File.Exists(_options.DepsFilePath))
            return;

        try
        {
            var json = File.ReadAllText(_options.DepsFilePath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("targets", out var targets))
                return;

            // Get the first target (e.g., ".NETCoreApp,Version=v10.0")
            foreach (var target in targets.EnumerateObject())
            {
                foreach (var package in target.Value.EnumerateObject())
                {
                    if (!package.Value.TryGetProperty("runtime", out var runtime))
                        continue;

                    foreach (var asset in runtime.EnumerateObject())
                    {
                        // asset.Name is like "lib/net10.0/Assembly.dll"
                        var assemblyFileName = Path.GetFileNameWithoutExtension(asset.Name);

                        // Find the package path in libraries
                        var packageName = package.Name; // e.g., "Newtonsoft.Json/13.0.3"
                        if (root.TryGetProperty("libraries", out var libraries)
                            && libraries.TryGetProperty(packageName, out var lib)
                            && lib.TryGetProperty("path", out var pathProp))
                        {
                            var packagePath = pathProp.GetString();
                            if (packagePath != null && !string.IsNullOrEmpty(_options.NuGetPackageRoot))
                            {
                                var fullPath = Path.Combine(
                                    _options.NuGetPackageRoot, packagePath, asset.Name);
                                fullPath = Path.GetFullPath(fullPath);
                                if (!_depsRuntimePaths.ContainsKey(assemblyFileName))
                                    _depsRuntimePaths[assemblyFileName] = fullPath;
                            }
                        }
                    }
                }
                break; // Only process the first target
            }
        }
        catch { /* deps.json parsing failure — non-fatal */ }
    }

    private void IndexTypes(LoadedAssembly assembly)
    {
        var reader = assembly.MetadataReader;
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            try
            {
                var typeDef = reader.GetTypeDefinition(typeHandle);
                var typeName = MethodKey.GetFullTypeName(reader, typeHandle);

                // Skip compiler-generated types (but not nested types like state machines)
                if (typeName.StartsWith("<") || typeName.Contains("<PrivateImplementationDetails>"))
                    continue;

                // Register in type index (first-loaded wins)
                _typeIndex.TryAdd(typeName, assembly);
                _typeHandleIndex.TryAdd(typeName, (assembly, typeHandle));

                // Build subtype index
                var baseTypeHandle = typeDef.BaseType;
                if (!baseTypeHandle.IsNil)
                {
                    var baseTypeName = ResolveEntityTypeName(reader, baseTypeHandle);
                    if (baseTypeName != null && baseTypeName != "System.Object"
                        && baseTypeName != "System.ValueType" && baseTypeName != "System.Enum")
                    {
                        if (!_subtypeIndex.TryGetValue(baseTypeName, out var subtypes))
                        {
                            subtypes = [];
                            _subtypeIndex[baseTypeName] = subtypes;
                        }
                        subtypes.Add(typeName);
                    }
                }
            }
            catch (BadImageFormatException) { continue; }
        }
    }

    private void BuildImplementationIndex()
    {
        foreach (var assembly in _assemblies)
        {
            var reader = assembly.MetadataReader;
            foreach (var typeHandle in reader.TypeDefinitions)
            {
                try
                {
                    var typeDef = reader.GetTypeDefinition(typeHandle);
                    var attributes = typeDef.Attributes;

                    // Skip abstract/interface types — we want concrete implementations
                    if ((attributes & TypeAttributes.Abstract) != 0
                        || (attributes & TypeAttributes.Interface) != 0)
                        continue;

                    var typeName = MethodKey.GetFullTypeName(reader, typeHandle);

                    // Index interface implementations
                    foreach (var ifaceHandle in typeDef.GetInterfaceImplementations())
                    {
                        var iface = reader.GetInterfaceImplementation(ifaceHandle);
                        var ifaceName = ResolveEntityTypeName(reader, iface.Interface);
                        if (ifaceName == null) continue;

                        // For each virtual method on this type, register as a potential
                        // implementation of the same-named interface method.
                        // Filter: must be virtual (interface impls are marked virtual in IL),
                        // must have a body, must not be a constructor or static.
                        foreach (var methodHandle in typeDef.GetMethods())
                        {
                            var methodDef = reader.GetMethodDefinition(methodHandle);
                            var methodAttrs = methodDef.Attributes;
                            if ((methodAttrs & MethodAttributes.Abstract) != 0)
                                continue;
                            if ((methodAttrs & MethodAttributes.Virtual) == 0)
                                continue;
                            if ((methodAttrs & MethodAttributes.Static) != 0)
                                continue;
                            if (methodDef.RelativeVirtualAddress == 0)
                                continue;

                            var methodName = reader.GetString(methodDef.Name);

                            // Skip constructors and special names that aren't interface members
                            if (methodName is ".ctor" or ".cctor")
                                continue;

                            // Strip explicit interface prefix if present
                            // e.g., "TestAssembly.Scenarios.IStore.Save" → "Save"
                            var dotIndex = methodName.LastIndexOf('.');
                            var bareMethodName = dotIndex >= 0 ? methodName[(dotIndex + 1)..] : methodName;

                            var implKey = MethodKey.FromDefinition(reader, methodHandle);
                            var ifaceMethodKey = (ifaceName, bareMethodName);

                            if (!_implementationIndex.TryGetValue(ifaceMethodKey, out var impls))
                            {
                                impls = [];
                                _implementationIndex[ifaceMethodKey] = impls;
                            }
                            impls.Add(implKey);
                        }
                    }
                }
                catch (BadImageFormatException) { continue; }
            }
        }
    }

    private static string? ResolveEntityTypeName(MetadataReader reader, EntityHandle handle)
    {
        try
        {
            return handle.Kind switch
            {
                HandleKind.TypeDefinition => MethodKey.GetFullTypeName(reader, (TypeDefinitionHandle)handle),
                HandleKind.TypeReference => ResolveTypeRefName(reader, (TypeReferenceHandle)handle),
                HandleKind.TypeSpecification => null, // Generic instantiation — skip for indexing
                _ => null
            };
        }
        catch { return null; }
    }

    private static string ResolveTypeRefName(MetadataReader reader, TypeReferenceHandle handle)
    {
        var typeRef = reader.GetTypeReference(handle);
        var name = reader.GetString(typeRef.Name);
        var ns = reader.GetString(typeRef.Namespace);
        if (typeRef.ResolutionScope.Kind == HandleKind.TypeReference)
        {
            var parentName = ResolveTypeRefName(reader, (TypeReferenceHandle)typeRef.ResolutionScope);
            return $"{parentName}+{name}";
        }
        return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var assembly in _assemblies)
            assembly.Dispose();
        _assemblies.Clear();
    }
}
