using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace Calor.Compiler.Effects.IL;

/// <summary>
/// Uniquely identifies a method across assemblies using full parameter type signatures.
/// Uses canonical string forms for type/method names to enable cross-assembly matching.
/// </summary>
public readonly record struct MethodKey(
    string TypeName,       // Fully qualified, e.g. "System.Data.Common.DbCommand"
    string MethodName,     // e.g. "ExecuteNonQuery"
    string ParameterSig)   // Serialized parameter types, e.g. "(System.String,System.Int32)"
    : IComparable<MethodKey>
{
    /// <summary>
    /// Name-only key for fallback lookups when caller lacks parameter type info.
    /// </summary>
    public (string TypeName, string MethodName) NameKey => (TypeName, MethodName);

    public int CompareTo(MethodKey other)
    {
        var cmp = string.Compare(TypeName, other.TypeName, StringComparison.Ordinal);
        if (cmp != 0) return cmp;
        cmp = string.Compare(MethodName, other.MethodName, StringComparison.Ordinal);
        if (cmp != 0) return cmp;
        return string.Compare(ParameterSig, other.ParameterSig, StringComparison.Ordinal);
    }

    public override string ToString() => $"{TypeName}::{MethodName}{ParameterSig}";

    /// <summary>
    /// Creates a MethodKey from a MethodDefinition in the same assembly.
    /// </summary>
    public static MethodKey FromDefinition(MetadataReader reader, MethodDefinitionHandle handle)
    {
        var methodDef = reader.GetMethodDefinition(handle);
        var methodName = reader.GetString(methodDef.Name);
        var declaringType = methodDef.GetDeclaringType();
        var typeName = GetFullTypeName(reader, declaringType);
        var paramSig = DecodeParameterSignature(reader, methodDef.Signature);
        return new MethodKey(typeName, methodName, paramSig);
    }

    /// <summary>
    /// Creates a MethodKey from a MemberReference (cross-assembly call target).
    /// </summary>
    public static MethodKey FromReference(MetadataReader reader, MemberReferenceHandle handle)
    {
        var memberRef = reader.GetMemberReference(handle);
        var methodName = reader.GetString(memberRef.Name);
        var typeName = ResolveParentTypeName(reader, memberRef.Parent);
        var paramSig = DecodeParameterSignature(reader, memberRef.Signature);
        return new MethodKey(typeName, methodName, paramSig);
    }

    /// <summary>
    /// Creates a MethodKey from a MethodSpecification (generic method instantiation).
    /// Unwraps to the underlying generic method definition, ignoring type arguments.
    /// </summary>
    public static MethodKey? FromSpecification(MetadataReader reader, MethodSpecificationHandle handle)
    {
        var spec = reader.GetMethodSpecification(handle);
        // Unwrap to the underlying method (MethodDefinition or MemberReference)
        if (spec.Method.Kind == HandleKind.MethodDefinition)
            return FromDefinition(reader, (MethodDefinitionHandle)spec.Method);
        if (spec.Method.Kind == HandleKind.MemberReference)
            return FromReference(reader, (MemberReferenceHandle)spec.Method);
        return null;
    }

    /// <summary>
    /// Creates a name-only MethodKey for lookups where parameter types are unknown.
    /// ParameterSig is set to "*" to indicate wildcard matching.
    /// </summary>
    public static MethodKey NameOnly(string typeName, string methodName)
        => new(typeName, methodName, "*");

    internal static string GetFullTypeName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var typeDef = reader.GetTypeDefinition(handle);
        var name = reader.GetString(typeDef.Name);
        var ns = reader.GetString(typeDef.Namespace);

        // Handle nested types
        if (typeDef.IsNested)
        {
            var declaringType = typeDef.GetDeclaringType();
            var parentName = GetFullTypeName(reader, declaringType);
            return $"{parentName}+{name}";
        }

        return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
    }

    private static string ResolveParentTypeName(MetadataReader reader, EntityHandle parent)
    {
        switch (parent.Kind)
        {
            case HandleKind.TypeReference:
                return ResolveTypeReferenceName(reader, (TypeReferenceHandle)parent);

            case HandleKind.TypeDefinition:
                return GetFullTypeName(reader, (TypeDefinitionHandle)parent);

            case HandleKind.TypeSpecification:
                // Generic type instantiation — extract the underlying type
                var typeSpec = reader.GetTypeSpecification((TypeSpecificationHandle)parent);
                return DecodeTypeSpecName(reader, typeSpec.Signature);

            default:
                return "<unknown>";
        }
    }

    private static string ResolveTypeReferenceName(MetadataReader reader, TypeReferenceHandle handle)
    {
        var typeRef = reader.GetTypeReference(handle);
        var name = reader.GetString(typeRef.Name);
        var ns = reader.GetString(typeRef.Namespace);

        // Handle nested type references
        if (typeRef.ResolutionScope.Kind == HandleKind.TypeReference)
        {
            var parentName = ResolveTypeReferenceName(reader, (TypeReferenceHandle)typeRef.ResolutionScope);
            return $"{parentName}+{name}";
        }

        return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
    }

    private static string DecodeParameterSignature(MetadataReader reader, BlobHandle signatureBlob)
    {
        var blobReader = reader.GetBlobReader(signatureBlob);

        // Read calling convention header
        var header = blobReader.ReadSignatureHeader();

        // Generic parameter count (if generic method)
        if (header.IsGeneric)
            blobReader.ReadCompressedInteger();

        // Parameter count
        var paramCount = blobReader.ReadCompressedInteger();

        if (paramCount == 0)
            return "()";

        // Skip return type
        SkipType(ref blobReader);

        // Read parameter types
        var paramTypes = new string[paramCount];
        for (var i = 0; i < paramCount; i++)
        {
            paramTypes[i] = ReadTypeName(ref blobReader, reader);
        }

        return $"({string.Join(",", paramTypes)})";
    }

    private static string DecodeTypeSpecName(MetadataReader reader, BlobHandle signatureBlob)
    {
        var blobReader = reader.GetBlobReader(signatureBlob);
        return ReadTypeName(ref blobReader, reader);
    }

    private static string ReadTypeName(ref BlobReader reader, MetadataReader metadataReader)
    {
        var typeCode = reader.ReadCompressedInteger();
        return typeCode switch
        {
            0x01 => "System.Void",
            0x02 => "System.Boolean",
            0x03 => "System.Char",
            0x04 => "System.SByte",
            0x05 => "System.Byte",
            0x06 => "System.Int16",
            0x07 => "System.UInt16",
            0x08 => "System.Int32",
            0x09 => "System.UInt32",
            0x0A => "System.Int64",
            0x0B => "System.UInt64",
            0x0C => "System.Single",
            0x0D => "System.Double",
            0x0E => "System.String",
            0x10 => ReadTypeName(ref reader, metadataReader), // BYREF — skip modifier, read inner
            0x0F => // PTR
                $"{ReadTypeName(ref reader, metadataReader)}*",
            0x11 or 0x12 => // VALUETYPE or CLASS — followed by TypeDefOrRef coded index
                ResolveCodedTypeName(ref reader, metadataReader),
            0x13 => // Generic type parameter (from declaring type)
                $"!{reader.ReadCompressedInteger()}",
            0x14 => // ARRAY
                ReadArrayTypeName(ref reader, metadataReader),
            0x15 => // GENERIC_INST
                ReadGenericInstName(ref reader, metadataReader),
            0x16 => "System.TypedReference",
            0x18 => "System.IntPtr",
            0x19 => "System.UIntPtr",
            0x1B => // FNPTR — function pointer
                ReadFnPtrName(ref reader, metadataReader),
            0x1C => "System.Object",
            0x1D => // SZARRAY — single-dimension zero-lower-bound array
                $"{ReadTypeName(ref reader, metadataReader)}[]",
            0x1E => // MVAR — generic method parameter
                $"!!{reader.ReadCompressedInteger()}",
            0x20 => // CMOD_REQD
                SkipCustomMod(ref reader, metadataReader),
            0x1F => // CMOD_OPT
                SkipCustomMod(ref reader, metadataReader),
            0x41 => // SENTINEL (vararg)
                ReadTypeName(ref reader, metadataReader),
            0x45 => // PINNED
                ReadTypeName(ref reader, metadataReader),
            _ => $"<type:0x{typeCode:X2}>"
        };
    }

    private static string ResolveCodedTypeName(ref BlobReader reader, MetadataReader metadataReader)
    {
        var codedIndex = reader.ReadCompressedInteger();
        var handle = MetadataTokens.EntityHandle(0x01000000 | codedIndex); // TypeRef table

        // TypeDefOrRef coded index: tag is lowest 2 bits
        var tag = codedIndex & 0x3;
        var rowId = codedIndex >> 2;

        return tag switch
        {
            0 => // TypeDef
                rowId > 0 && rowId <= metadataReader.TypeDefinitions.Count
                    ? GetFullTypeName(metadataReader, MetadataTokens.TypeDefinitionHandle(rowId))
                    : $"<typedef:{rowId}>",
            1 => // TypeRef
                rowId > 0 && rowId <= metadataReader.GetTableRowCount(TableIndex.TypeRef)
                    ? ResolveTypeReferenceName(metadataReader, MetadataTokens.TypeReferenceHandle(rowId))
                    : $"<typeref:{rowId}>",
            2 => // TypeSpec
                $"<typespec:{rowId}>",
            _ => $"<coded:{tag}:{rowId}>"
        };
    }

    private static string ReadArrayTypeName(ref BlobReader reader, MetadataReader metadataReader)
    {
        var elementType = ReadTypeName(ref reader, metadataReader);
        var rank = reader.ReadCompressedInteger();
        var numSizes = reader.ReadCompressedInteger();
        for (var i = 0; i < numSizes; i++) reader.ReadCompressedInteger();
        var numLoBounds = reader.ReadCompressedInteger();
        for (var i = 0; i < numLoBounds; i++) reader.ReadCompressedSignedInteger();
        return $"{elementType}[{new string(',', rank - 1)}]";
    }

    private static string ReadGenericInstName(ref BlobReader reader, MetadataReader metadataReader)
    {
        var isValueType = reader.ReadCompressedInteger(); // CLASS or VALUETYPE
        var typeName = ResolveCodedTypeName(ref reader, metadataReader);
        var argCount = reader.ReadCompressedInteger();
        var args = new string[argCount];
        for (var i = 0; i < argCount; i++)
            args[i] = ReadTypeName(ref reader, metadataReader);
        return $"{typeName}<{string.Join(",", args)}>";
    }

    private static string ReadFnPtrName(ref BlobReader reader, MetadataReader metadataReader)
    {
        // Skip the full method signature
        reader.ReadSignatureHeader();
        var paramCount = reader.ReadCompressedInteger();
        SkipType(ref reader); // return type
        for (var i = 0; i < paramCount; i++) SkipType(ref reader);
        return "<fnptr>";
    }

    private static string SkipCustomMod(ref BlobReader reader, MetadataReader metadataReader)
    {
        reader.ReadCompressedInteger(); // skip the modifier type token
        return ReadTypeName(ref reader, metadataReader); // read the actual type
    }

    private static void SkipType(ref BlobReader reader)
    {
        var typeCode = reader.ReadCompressedInteger();
        switch (typeCode)
        {
            case 0x01 or 0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x08
                or 0x09 or 0x0A or 0x0B or 0x0C or 0x0D or 0x0E or 0x16 or 0x18
                or 0x19 or 0x1C:
                break; // Primitive — no further data
            case 0x10: // BYREF
            case 0x0F: // PTR
            case 0x1D: // SZARRAY
            case 0x45: // PINNED
                SkipType(ref reader);
                break;
            case 0x11 or 0x12: // VALUETYPE or CLASS
                reader.ReadCompressedInteger(); // TypeDefOrRef coded index
                break;
            case 0x13 or 0x1E: // VAR or MVAR
                reader.ReadCompressedInteger(); // index
                break;
            case 0x14: // ARRAY
                SkipType(ref reader); // element type
                var rank = reader.ReadCompressedInteger();
                var numSizes = reader.ReadCompressedInteger();
                for (var i = 0; i < numSizes; i++) reader.ReadCompressedInteger();
                var numLoBounds = reader.ReadCompressedInteger();
                for (var i = 0; i < numLoBounds; i++) reader.ReadCompressedSignedInteger();
                break;
            case 0x15: // GENERIC_INST
                reader.ReadCompressedInteger(); // CLASS or VALUETYPE
                reader.ReadCompressedInteger(); // TypeDefOrRef coded index
                var argCount = reader.ReadCompressedInteger();
                for (var i = 0; i < argCount; i++) SkipType(ref reader);
                break;
            case 0x1B: // FNPTR
                reader.ReadSignatureHeader();
                var paramCount = reader.ReadCompressedInteger();
                SkipType(ref reader); // return type
                for (var i = 0; i < paramCount; i++) SkipType(ref reader);
                break;
            case 0x1F or 0x20: // CMOD_OPT or CMOD_REQD
                reader.ReadCompressedInteger(); // modifier token
                SkipType(ref reader);
                break;
        }
    }
}

/// <summary>
/// Represents a call edge extracted from IL.
/// </summary>
public readonly record struct CallEdge(
    MethodKey Callee,
    bool IsVirtual,     // callvirt vs call
    bool IsConstructor, // newobj
    bool IsDelegate);   // ldftn/ldvirtftn

/// <summary>
/// Result of extracting call edges from a method body.
/// </summary>
public sealed class CallEdgeResult
{
    public IReadOnlyList<CallEdge> Edges { get; }
    public bool HasIndirectCalls { get; } // calli detected — method is Incomplete

    public CallEdgeResult(IReadOnlyList<CallEdge> edges, bool hasIndirectCalls)
    {
        Edges = edges;
        HasIndirectCalls = hasIndirectCalls;
    }
}

/// <summary>
/// Three-state resolution status for IL analysis.
/// </summary>
public enum ILResolutionStatus
{
    /// <summary>
    /// Analysis completed; all callees fully resolved. Effects are precise.
    /// </summary>
    Resolved,

    /// <summary>
    /// Analysis completed; all callees fully resolved, no effects found. Method is pure.
    /// </summary>
    ResolvedPure,

    /// <summary>
    /// Analysis was cut short — depth limit, missing body, too many implementations,
    /// calli instruction, etc. Effects are UNKNOWN, not empty.
    /// Incomplete never reports a method as pure.
    /// </summary>
    Incomplete
}
