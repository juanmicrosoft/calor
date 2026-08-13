using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Calor.Compiler.Effects.IL;

namespace Calor.Compiler.Effects.Manifests;

/// <summary>
/// Productizes the three-tier effect-resolution strategy
/// (docs/plans/dotnet-ecosystem-effect-manifests.md v4) into a manifest
/// generator for a package's public surface — the engine behind
/// <c>calor import</c> (wedge plan v0.11 D-W3.1):
///
/// <list type="bullet">
/// <item><b>Tier A</b> — compile-time IL derivation for concrete call chains,
/// via the existing IL analysis machinery. Emitted with provenance
/// <c>inferred</c> (derived) — and ONLY for members whose entire transitive
/// chain resolved without unverified assumptions: a chain that touches a
/// callee that is missing from the loaded assemblies, has no IL body, or is
/// a delegate invocation is routed to Tier C instead (review C1 invariant —
/// derived-pure must never rest on an assumed-pure leaf).</item>
/// <item><b>Tier B</b> — methods already covered by curated manifests
/// (built-in, user, solution, project) are reported as <c>curated</c> and
/// never re-emitted.</item>
/// <item><b>Tier C</b> — anything unresolved (irreducible dynamic dispatch,
/// analysis limits, bodiless declarations without curated coverage) is
/// surfaced loudly in the report, never silently under-approximated.</item>
/// </list>
///
/// Contract synthesis (D-W3.2, narrow mechanical set): non-null §Q facts from
/// NRT metadata and non-negativity facts from unsigned parameter types. Every
/// synthesized contract carries <c>assumed</c> provenance and is emitted as
/// annotation-only output — the consumption path into verification is
/// deliberately not wired (recorded shed point; see the import command docs).
/// Nothing this generator emits ever carries <c>verified</c> confidence.
/// </summary>
public sealed class PackageManifestGenerator
{
    /// <summary>Provenance value for Tier-A IL-derived entries.</summary>
    public const string DerivedConfidence = "inferred";

    /// <summary>Provenance value for synthesized contracts. Never trusted.</summary>
    public const string AssumedProvenance = "assumed";

    private PackageManifestGenerator() { }

    /// <summary>
    /// Generates the import report for a package's public surface.
    /// </summary>
    /// <param name="targetAssemblyPaths">Assemblies whose public surface is imported.</param>
    /// <param name="referenceAssemblyPaths">Additional assemblies loaded for call-chain resolution only.</param>
    /// <param name="resolver">Initialized manifest resolver (curated tiers).</param>
    /// <param name="library">NuGet package id, when known.</param>
    /// <param name="libraryVersion">Package version, when known.</param>
    /// <param name="synthesizeContracts">Whether to run the D-W3.2 mechanical contract synthesis.</param>
    /// <param name="ilOptions">IL analysis options (runtime directory etc.).</param>
    public static PackageImportReport Generate(
        IReadOnlyList<string> targetAssemblyPaths,
        IReadOnlyList<string> referenceAssemblyPaths,
        EffectResolver resolver,
        string? library = null,
        string? libraryVersion = null,
        bool synthesizeContracts = true,
        ILAnalysisOptions? ilOptions = null)
    {
        var report = new PackageImportReport
        {
            Library = library,
            LibraryVersion = libraryVersion,
            TargetAssemblies = targetAssemblyPaths.Select(Path.GetFileName).ToList()!
        };

        // Enumerate the public surface of the target assemblies directly —
        // visibility and body information is needed for honest classification,
        // which AssemblyIndex does not expose.
        var surface = new List<PublicMethod>();
        foreach (var path in targetAssemblyPaths)
            EnumeratePublicSurface(path, surface, report);

        // The unit of classification is the (type, member, kind) group —
        // manifest entries are name-keyed, so overloads are unioned and any
        // unresolved overload makes the whole member unresolved (dropping it
        // silently would under-approximate the member).
        var groups = surface
            .GroupBy(m => (m.Key.TypeName, m.DisplayMember, m.Kind))
            .OrderBy(g => g.Key.TypeName, StringComparer.Ordinal)
            .ThenBy(g => g.Key.DisplayMember, StringComparer.Ordinal)
            .ToList();

        report.PublicMethodCount = groups.Count;

        // Tier B first: members already covered by curated manifests.
        var uncovered = new List<IGrouping<(string TypeName, string DisplayMember, ImportMemberKind Kind), PublicMethod>>();
        foreach (var group in groups)
        {
            var resolution = ResolveCurated(resolver, group.First());
            if (resolution.Status != EffectResolutionStatus.Unknown)
            {
                report.Curated.Add(new ImportedEntry(
                    group.Key.TypeName, group.Key.DisplayMember, KindLabel(group.Key.Kind),
                    SurfaceCodes(resolution.Effects), resolution.Source ?? "manifest"));
            }
            else
            {
                uncovered.Add(group);
            }
        }

        // Tier A: IL derivation for concrete chains — only members whose
        // every overload has a body. A bodiless method (interface/abstract/
        // extern) has no chain to trace; without curated coverage the member
        // is unresolved — loudly (Tier C).
        var bodied = uncovered.Where(g => g.All(m => m.HasBody)).ToList();
        var bodiless = uncovered.Where(g => g.Any(m => !m.HasBody)).ToList();

        var ilResults = AnalyzeBodied(
            bodied.SelectMany(g => g).ToList(),
            targetAssemblyPaths, referenceAssemblyPaths, resolver, ilOptions);

        foreach (var group in bodied)
        {
            var union = EffectSet.Empty;
            var incomplete = false;
            var assumedLeaves = new SortedSet<MethodKey>();
            foreach (var overload in group)
            {
                if (ilResults.TryGetValue(overload.Key, out var result)
                    && result.Status != ILResolutionStatus.Incomplete)
                {
                    union = union.Union(result.Effects);
                    foreach (var leaf in result.AssumedPureLeaves)
                        assumedLeaves.Add(leaf);
                }
                else
                {
                    incomplete = true;
                    break;
                }
            }

            if (incomplete)
            {
                report.Unresolved.Add(new UnresolvedEntry(
                    group.Key.TypeName, group.Key.DisplayMember, KindLabel(group.Key.Kind),
                    "incomplete: dynamic dispatch, indirect calls, or analysis limits — supply a curated or supplemental manifest entry"));
            }
            else if (assumedLeaves.Count > 0)
            {
                // The chain rests on assumed-pure leaves that no manifest
                // covered — emitting the union as a clean Tier-A entry would
                // silently under-approximate (the honesty invariant of the
                // adversarial review C1 finding). Route to Tier C, naming the
                // assumptions so the user knows what to cover.
                report.Unresolved.Add(new UnresolvedEntry(
                    group.Key.TypeName, group.Key.DisplayMember, KindLabel(group.Key.Kind),
                    DescribeAssumedLeaves(assumedLeaves)));
            }
            else
            {
                report.Derived.Add(new ImportedEntry(
                    group.Key.TypeName, group.Key.DisplayMember, KindLabel(group.Key.Kind),
                    SurfaceCodes(union), "il-analysis"));
            }
        }

        foreach (var group in bodiless)
        {
            report.Unresolved.Add(new UnresolvedEntry(
                group.Key.TypeName, group.Key.DisplayMember, KindLabel(group.Key.Kind),
                "no body: interface, abstract, or extern declaration — needs a curated (Tier B) or supplemental (Tier C) manifest entry"));
        }

        report.Unresolved.Sort((a, b) =>
        {
            var byType = string.Compare(a.Type, b.Type, StringComparison.Ordinal);
            return byType != 0 ? byType : string.Compare(a.Member, b.Member, StringComparison.Ordinal);
        });

        report.Manifest = BuildManifest(report, library, libraryVersion);

        if (synthesizeContracts)
        {
            foreach (var path in targetAssemblyPaths)
                SynthesizeContracts(path, report);
        }

        return report;
    }

    // ------------------------------------------------------------------
    // Public-surface enumeration
    // ------------------------------------------------------------------

    private sealed record PublicMethod(
        MethodKey Key,
        string DisplayMember,
        ImportMemberKind Kind,
        bool HasBody);

    private static void EnumeratePublicSurface(string assemblyPath, List<PublicMethod> surface, PackageImportReport report)
    {
        if (!File.Exists(assemblyPath))
        {
            report.LoadErrors.Add($"Assembly not found: {assemblyPath}");
            return;
        }

        try
        {
            using var stream = File.OpenRead(assemblyPath);
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata)
            {
                report.LoadErrors.Add($"No metadata: {assemblyPath}");
                return;
            }

            var reader = peReader.GetMetadataReader();
            foreach (var typeHandle in reader.TypeDefinitions)
            {
                TypeDefinition typeDef;
                try { typeDef = reader.GetTypeDefinition(typeHandle); }
                catch (BadImageFormatException) { continue; }

                if (!IsPubliclyVisible(reader, typeDef))
                    continue;

                var typeName = MethodKey.GetFullTypeName(reader, typeHandle);
                if (typeName.StartsWith('<') || typeName.Contains("<PrivateImplementationDetails>", StringComparison.Ordinal))
                    continue;

                foreach (var methodHandle in typeDef.GetMethods())
                {
                    MethodDefinition methodDef;
                    try { methodDef = reader.GetMethodDefinition(methodHandle); }
                    catch (BadImageFormatException) { continue; }

                    var attrs = methodDef.Attributes;
                    if ((attrs & MethodAttributes.MemberAccessMask) != MethodAttributes.Public)
                        continue;

                    var name = reader.GetString(methodDef.Name);
                    if (name == ".cctor" || name.StartsWith('<'))
                        continue;

                    var kind = ClassifyMember(name, attrs);
                    if (kind == ImportMemberKind.Skipped)
                        continue;

                    var isAbstract = (attrs & MethodAttributes.Abstract) != 0;
                    var hasBody = !isAbstract
                        && (attrs & MethodAttributes.PinvokeImpl) == 0
                        && methodDef.RelativeVirtualAddress != 0;

                    MethodKey key;
                    try { key = MethodKey.FromDefinition(reader, methodHandle); }
                    catch (BadImageFormatException) { continue; }

                    var display = kind switch
                    {
                        ImportMemberKind.Getter or ImportMemberKind.Setter => name[4..],
                        ImportMemberKind.Constructor => ".ctor",
                        _ => name
                    };

                    surface.Add(new PublicMethod(key, display, kind, hasBody));
                }
            }
        }
        catch (BadImageFormatException)
        {
            report.LoadErrors.Add($"Malformed assembly: {assemblyPath}");
        }
        catch (IOException ex)
        {
            report.LoadErrors.Add($"Cannot read {assemblyPath}: {ex.Message}");
        }
    }

    private static bool IsPubliclyVisible(MetadataReader reader, TypeDefinition typeDef)
    {
        var visibility = typeDef.Attributes & TypeAttributes.VisibilityMask;
        if (visibility == TypeAttributes.Public)
            return true;
        if (visibility is TypeAttributes.NestedPublic)
        {
            try
            {
                var declaring = reader.GetTypeDefinition(typeDef.GetDeclaringType());
                return IsPubliclyVisible(reader, declaring);
            }
            catch (BadImageFormatException) { return false; }
        }
        return false;
    }

    /// <summary>
    /// Builds the Tier-C reason for a chain that touched assumed-pure leaves,
    /// naming up to three so the user knows what to cover with manifests.
    /// Delegate invocations are called out — they are the classic laundering
    /// vector, not merely a missing reference.
    /// </summary>
    private static string DescribeAssumedLeaves(IReadOnlyCollection<MethodKey> leaves)
    {
        static bool IsDelegateInvoke(MethodKey key)
            => key.MethodName is "Invoke" or "BeginInvoke" or "EndInvoke" or "DynamicInvoke";

        static string Describe(MethodKey key)
            => IsDelegateInvoke(key)
                ? $"delegate invocation ({key.TypeName}.{key.MethodName})"
                : $"assumed-pure leaf {key.TypeName}.{key.MethodName}";

        var named = leaves.Take(3).Select(Describe).ToList();
        var suffix = leaves.Count > 3 ? $" (+{leaves.Count - 3} more)" : "";
        return "chain rests on unverified assumptions: " + string.Join("; ", named) + suffix
            + " — cover the leaves with curated or supplemental manifest entries, or pass their assemblies via --references";
    }

    private static string KindLabel(ImportMemberKind kind) => kind switch
    {
        ImportMemberKind.Getter => "getter",
        ImportMemberKind.Setter => "setter",
        ImportMemberKind.Constructor => "constructor",
        _ => "method"
    };

    private static ImportMemberKind ClassifyMember(string name, MethodAttributes attrs)
    {
        if (name == ".ctor")
            return ImportMemberKind.Constructor;

        if ((attrs & MethodAttributes.SpecialName) != 0)
        {
            if (name.StartsWith("get_", StringComparison.Ordinal) && name.Length > 4)
                return ImportMemberKind.Getter;
            if (name.StartsWith("set_", StringComparison.Ordinal) && name.Length > 4)
                return ImportMemberKind.Setter;
            // Event accessors and operators have no manifest surface; skipping
            // them is a representation gap, not an effect under-approximation —
            // the resolver never consults them.
            return ImportMemberKind.Skipped;
        }

        return ImportMemberKind.Method;
    }

    // ------------------------------------------------------------------
    // Tier A / Tier B resolution
    // ------------------------------------------------------------------

    private static EffectResolution ResolveCurated(EffectResolver resolver, PublicMethod method)
    {
        var parameterTypes = EffectResolver.ParseParameterSignature(method.Key.ParameterSig);
        return method.Kind switch
        {
            ImportMemberKind.Getter => resolver.ResolveGetter(method.Key.TypeName, method.DisplayMember),
            ImportMemberKind.Setter => resolver.ResolveSetter(method.Key.TypeName, method.DisplayMember),
            ImportMemberKind.Constructor => resolver.ResolveConstructor(method.Key.TypeName, parameterTypes),
            _ => resolver.Resolve(method.Key.TypeName, method.DisplayMember, parameterTypes)
        };
    }

    private static Dictionary<MethodKey, ILMethodOutcome> AnalyzeBodied(
        List<PublicMethod> bodied,
        IReadOnlyList<string> targetAssemblyPaths,
        IReadOnlyList<string> referenceAssemblyPaths,
        EffectResolver resolver,
        ILAnalysisOptions? ilOptions)
    {
        if (bodied.Count == 0)
            return [];

        var allPaths = targetAssemblyPaths.Concat(referenceAssemblyPaths).Distinct().ToList();
        var options = ilOptions ?? new ILAnalysisOptions();

        using var index = new AssemblyIndex(allPaths, options);
        var builder = new ILCallGraphBuilder(index);
        var stateMachines = new StateMachineResolver(index);
        var propagator = new TransitiveEffectPropagator(builder, index, stateMachines, resolver, options);

        return propagator.PropagateDetailed(bodied.Select(m => m.Key));
    }

    private static List<string> SurfaceCodes(EffectSet effects)
    {
        if (effects.IsUnknown)
            return ["unknown"];
        return effects.Effects
            .Select(e => EffectSetExtensions.ToSurfaceCode(e.Kind, e.Value))
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();
    }

    private static EffectManifest BuildManifest(PackageImportReport report, string? library, string? libraryVersion)
    {
        var manifest = new EffectManifest
        {
            Version = "1.0",
            Description = (library is null
                ? "Effects derived by calor import"
                : $"Effects derived by calor import for {library}")
                + " (Tier A IL analysis; only members whose full call chains resolved with no unverified assumptions)",
            Library = library,
            LibraryVersion = libraryVersion,
            GeneratedBy = "calor import",
            Confidence = DerivedConfidence,
            GeneratedAt = DateTime.UtcNow.ToString("O")
        };

        foreach (var typeGroup in report.Derived
            .GroupBy(e => e.Type)
            .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var mapping = new TypeMapping { Type = typeGroup.Key };
            foreach (var entry in typeGroup.OrderBy(e => e.Member, StringComparer.Ordinal))
            {
                switch (entry.Kind)
                {
                    case "constructor":
                        mapping.Constructors ??= [];
                        mapping.Constructors["()"] = entry.Effects.ToList();
                        break;
                    case "getter":
                        mapping.Getters ??= [];
                        mapping.Getters[entry.Member] = entry.Effects.ToList();
                        break;
                    case "setter":
                        mapping.Setters ??= [];
                        mapping.Setters[entry.Member] = entry.Effects.ToList();
                        break;
                    default:
                        mapping.Methods ??= [];
                        mapping.Methods[entry.Member] = entry.Effects.ToList();
                        break;
                }
            }
            manifest.Mappings.Add(mapping);
        }

        return manifest;
    }

    // ------------------------------------------------------------------
    // D-W3.2 — mechanical contract synthesis (assumed provenance only)
    // ------------------------------------------------------------------

    private static void SynthesizeContracts(string assemblyPath, PackageImportReport report)
    {
        if (!File.Exists(assemblyPath))
            return;

        try
        {
            using var stream = File.OpenRead(assemblyPath);
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata)
                return;

            var reader = peReader.GetMetadataReader();
            foreach (var typeHandle in reader.TypeDefinitions)
            {
                TypeDefinition typeDef;
                try { typeDef = reader.GetTypeDefinition(typeHandle); }
                catch (BadImageFormatException) { continue; }

                if (!IsPubliclyVisible(reader, typeDef))
                    continue;

                var typeName = MethodKey.GetFullTypeName(reader, typeHandle);
                if (typeName.StartsWith('<'))
                    continue;

                var typeNullableContext = ReadNullableContext(reader, typeDef.GetCustomAttributes());

                foreach (var methodHandle in typeDef.GetMethods())
                {
                    try
                    {
                        SynthesizeForMethod(reader, methodHandle, typeName, typeNullableContext, report);
                    }
                    catch (BadImageFormatException)
                    {
                        // Malformed method metadata — skip; synthesis is best-effort
                        // and never fabricates a contract it could not decode.
                    }
                }
            }
        }
        catch (BadImageFormatException) { /* skip malformed assembly */ }
        catch (IOException) { /* skip unreadable assembly */ }
    }

    private static void SynthesizeForMethod(
        MetadataReader reader,
        MethodDefinitionHandle methodHandle,
        string typeName,
        byte? typeNullableContext,
        PackageImportReport report)
    {
        var methodDef = reader.GetMethodDefinition(methodHandle);
        var attrs = methodDef.Attributes;
        if ((attrs & MethodAttributes.MemberAccessMask) != MethodAttributes.Public)
            return;

        var methodName = reader.GetString(methodDef.Name);
        if (methodName == ".cctor" || methodName.StartsWith('<'))
            return;

        var paramCategories = DecodeParameterCategories(reader, methodDef.Signature);
        if (paramCategories.Count == 0)
            return;

        var methodNullableContext = ReadNullableContext(reader, methodDef.GetCustomAttributes())
            ?? typeNullableContext;

        // Sequence 0 is the return value; parameters are 1..N.
        foreach (var paramHandle in methodDef.GetParameters())
        {
            var param = reader.GetParameter(paramHandle);
            if (param.SequenceNumber == 0 || param.SequenceNumber > paramCategories.Count)
                continue;

            var paramName = reader.GetString(param.Name);
            if (string.IsNullOrEmpty(paramName))
                continue;

            var category = paramCategories[param.SequenceNumber - 1];
            switch (category)
            {
                case ParameterCategory.ReferenceType:
                {
                    var nullable = ReadNullableAnnotation(reader, param.GetCustomAttributes())
                        ?? methodNullableContext;
                    // 1 = non-nullable under NRT. No annotation and no context
                    // means nullable-oblivious code: no fact is derivable, so
                    // none is synthesized (never guess).
                    if (nullable == 1)
                    {
                        report.SynthesizedContracts.Add(new SynthesizedContract(
                            typeName, methodName, paramName,
                            Kind: "requires-not-null",
                            Expression: $"(!= {paramName} null)",
                            Provenance: AssumedProvenance,
                            Source: "nrt-metadata"));
                    }
                    break;
                }
                case ParameterCategory.UnsignedInteger:
                    report.SynthesizedContracts.Add(new SynthesizedContract(
                        typeName, methodName, paramName,
                        Kind: "requires-non-negative",
                        Expression: $"(>= {paramName} 0)",
                        Provenance: AssumedProvenance,
                        Source: "unsigned-parameter-type"));
                    break;
            }
        }
    }

    private enum ParameterCategory { Other, ReferenceType, UnsignedInteger }

    /// <summary>
    /// Decodes only what the mechanical synthesis needs from a method signature:
    /// whether each parameter is a reference type or an unsigned integer.
    /// Anything ambiguous (byref, generic parameters, pointers) is Other —
    /// synthesis never guesses.
    /// </summary>
    private static List<ParameterCategory> DecodeParameterCategories(MetadataReader reader, BlobHandle signatureBlob)
    {
        var categories = new List<ParameterCategory>();
        try
        {
            var blob = reader.GetBlobReader(signatureBlob);
            var header = blob.ReadSignatureHeader();
            if (header.IsGeneric)
                blob.ReadCompressedInteger();

            var paramCount = blob.ReadCompressedInteger();
            SkipTypeShape(ref blob); // return type

            for (var i = 0; i < paramCount; i++)
                categories.Add(ReadCategory(ref blob));
        }
        catch (BadImageFormatException)
        {
            return [];
        }
        return categories;
    }

    private static ParameterCategory ReadCategory(ref BlobReader blob)
    {
        var typeCode = blob.ReadCompressedInteger();
        switch (typeCode)
        {
            case 0x05: // U1
            case 0x07: // U2
            case 0x09: // U4
            case 0x0B: // U8
                return ParameterCategory.UnsignedInteger;
            case 0x0E: // STRING
            case 0x1C: // OBJECT
                return ParameterCategory.ReferenceType;
            case 0x12: // CLASS
                blob.ReadCompressedInteger(); // TypeDefOrRef token
                return ParameterCategory.ReferenceType;
            case 0x1D: // SZARRAY
                SkipTypeShape(ref blob);
                return ParameterCategory.ReferenceType;
            case 0x14: // ARRAY
                SkipTypeShape(ref blob);
                var rank = blob.ReadCompressedInteger();
                _ = rank;
                var numSizes = blob.ReadCompressedInteger();
                for (var i = 0; i < numSizes; i++) blob.ReadCompressedInteger();
                var numLoBounds = blob.ReadCompressedInteger();
                for (var i = 0; i < numLoBounds; i++) blob.ReadCompressedSignedInteger();
                return ParameterCategory.ReferenceType;
            case 0x15: // GENERIC_INST — CLASS/VALUETYPE byte, then type + args
            {
                var elementKind = blob.ReadCompressedInteger();
                blob.ReadCompressedInteger(); // TypeDefOrRef token
                var argCount = blob.ReadCompressedInteger();
                for (var i = 0; i < argCount; i++) SkipTypeShape(ref blob);
                return elementKind == 0x12 ? ParameterCategory.ReferenceType : ParameterCategory.Other;
            }
            case 0x1F: // CMOD_OPT
            case 0x20: // CMOD_REQD
                blob.ReadCompressedInteger(); // modifier token
                return ReadCategory(ref blob);
            default:
                SkipTypeShapeAfterCode(ref blob, typeCode);
                return ParameterCategory.Other;
        }
    }

    private static void SkipTypeShape(ref BlobReader blob)
    {
        var typeCode = blob.ReadCompressedInteger();
        SkipTypeShapeAfterCode(ref blob, typeCode);
    }

    private static void SkipTypeShapeAfterCode(ref BlobReader blob, int typeCode)
    {
        switch (typeCode)
        {
            case 0x01 or 0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x08
                or 0x09 or 0x0A or 0x0B or 0x0C or 0x0D or 0x0E or 0x16 or 0x18
                or 0x19 or 0x1C:
                break;
            case 0x10 or 0x0F or 0x1D or 0x45: // BYREF, PTR, SZARRAY, PINNED
                SkipTypeShape(ref blob);
                break;
            case 0x11 or 0x12: // VALUETYPE, CLASS
                blob.ReadCompressedInteger();
                break;
            case 0x13 or 0x1E: // VAR, MVAR
                blob.ReadCompressedInteger();
                break;
            case 0x14: // ARRAY
                SkipTypeShape(ref blob);
                var rank = blob.ReadCompressedInteger();
                _ = rank;
                var numSizes = blob.ReadCompressedInteger();
                for (var i = 0; i < numSizes; i++) blob.ReadCompressedInteger();
                var numLoBounds = blob.ReadCompressedInteger();
                for (var i = 0; i < numLoBounds; i++) blob.ReadCompressedSignedInteger();
                break;
            case 0x15: // GENERIC_INST
                blob.ReadCompressedInteger();
                blob.ReadCompressedInteger();
                var argCount = blob.ReadCompressedInteger();
                for (var i = 0; i < argCount; i++) SkipTypeShape(ref blob);
                break;
            case 0x1B: // FNPTR
                blob.ReadSignatureHeader();
                var paramCount = blob.ReadCompressedInteger();
                SkipTypeShape(ref blob);
                for (var i = 0; i < paramCount; i++) SkipTypeShape(ref blob);
                break;
            case 0x1F or 0x20: // CMOD
                blob.ReadCompressedInteger();
                SkipTypeShape(ref blob);
                break;
        }
    }

    /// <summary>
    /// Reads the scalar byte from a NullableContextAttribute, if present.
    /// </summary>
    private static byte? ReadNullableContext(MetadataReader reader, CustomAttributeHandleCollection attributes)
        => ReadNullableByte(reader, attributes, "NullableContextAttribute");

    /// <summary>
    /// Reads the top-level nullability byte from a NullableAttribute — the
    /// scalar form directly, or the first element of the byte[] form (which
    /// describes the top-level type of a generic/array parameter).
    /// </summary>
    private static byte? ReadNullableAnnotation(MetadataReader reader, CustomAttributeHandleCollection attributes)
        => ReadNullableByte(reader, attributes, "NullableAttribute");

    private static byte? ReadNullableByte(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        string attributeName)
    {
        foreach (var attrHandle in attributes)
        {
            try
            {
                var attr = reader.GetCustomAttribute(attrHandle);
                if (GetAttributeTypeName(reader, attr) != attributeName)
                    continue;

                var blob = reader.GetBlobReader(attr.Value);
                if (blob.Length < 4)
                    return null;
                var prolog = blob.ReadUInt16();
                if (prolog != 0x0001)
                    return null;

                // Two ctors exist: (byte) and (byte[]).
                // (byte): 1 value byte + 2 named-arg-count bytes remain.
                // (byte[]): int32 length + bytes + 2 named-arg-count bytes remain.
                if (blob.RemainingBytes == 3)
                    return blob.ReadByte();

                var length = blob.ReadInt32();
                if (length >= 1 && blob.RemainingBytes >= length)
                    return blob.ReadByte(); // first element = top-level type
                return null;
            }
            catch (BadImageFormatException)
            {
                return null;
            }
        }
        return null;
    }

    private static string? GetAttributeTypeName(MetadataReader reader, CustomAttribute attr)
    {
        try
        {
            switch (attr.Constructor.Kind)
            {
                case HandleKind.MemberReference:
                {
                    var ctor = reader.GetMemberReference((MemberReferenceHandle)attr.Constructor);
                    if (ctor.Parent.Kind != HandleKind.TypeReference)
                        return null;
                    var typeRef = reader.GetTypeReference((TypeReferenceHandle)ctor.Parent);
                    return reader.GetString(typeRef.Name);
                }
                case HandleKind.MethodDefinition:
                {
                    // Roslyn embeds NullableAttribute/NullableContextAttribute
                    // into the assembly itself; the ctor is then a MethodDefinition.
                    var ctorDef = reader.GetMethodDefinition((MethodDefinitionHandle)attr.Constructor);
                    var typeDef = reader.GetTypeDefinition(ctorDef.GetDeclaringType());
                    return reader.GetString(typeDef.Name);
                }
                default:
                    return null;
            }
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }
}

/// <summary>Member kind on the import surface.</summary>
public enum ImportMemberKind
{
    Method,
    Getter,
    Setter,
    Constructor,
    Skipped
}

/// <summary>A resolved (derived or curated) entry on the import surface.</summary>
public sealed record ImportedEntry(
    string Type,
    string Member,
    string Kind,
    IReadOnlyList<string> Effects,
    string Source);

/// <summary>An unresolved public member — surfaced loudly, never emitted as pure.</summary>
public sealed record UnresolvedEntry(
    string Type,
    string Member,
    string Kind,
    string Reason);

/// <summary>
/// A mechanically synthesized contract fact. Provenance is always
/// <see cref="PackageManifestGenerator.AssumedProvenance"/>; these facts are
/// annotation-only output and are never consumed by verification as trusted.
/// </summary>
public sealed record SynthesizedContract(
    string Type,
    string Method,
    string Parameter,
    string Kind,
    string Expression,
    string Provenance,
    string Source);

/// <summary>
/// The full result of a package import: the derived-only manifest plus the
/// three-tier classification counts and loud unresolved surface.
/// </summary>
public sealed class PackageImportReport
{
    public string? Library { get; set; }
    public string? LibraryVersion { get; set; }
    public List<string> TargetAssemblies { get; set; } = [];

    /// <summary>
    /// Distinct public members — (type, member, kind) groups; overloads are
    /// one member. Always equals Derived + Curated + Unresolved.
    /// </summary>
    public int PublicMethodCount { get; set; }

    /// <summary>Tier A: IL-derived entries (provenance: inferred).</summary>
    public List<ImportedEntry> Derived { get; } = [];

    /// <summary>Tier B: already covered by curated/loaded manifests (provenance: per manifest, at most reviewed).</summary>
    public List<ImportedEntry> Curated { get; } = [];

    /// <summary>Tier C: unresolved — surfaced loudly, never silently under-approximated.</summary>
    public List<UnresolvedEntry> Unresolved { get; } = [];

    /// <summary>D-W3.2 synthesized contracts (provenance: assumed, annotation-only).</summary>
    public List<SynthesizedContract> SynthesizedContracts { get; } = [];

    /// <summary>Assembly-level load problems encountered during enumeration.</summary>
    public List<string> LoadErrors { get; } = [];

    /// <summary>The derived-only manifest (never contains curated or unresolved members).</summary>
    public EffectManifest Manifest { get; set; } = new();
}
