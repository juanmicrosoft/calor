using System.Reflection;
using System.Reflection.Metadata;

namespace Calor.Compiler.Effects.IL;

/// <summary>
/// Detects compiler-generated async and iterator state machines and redirects
/// analysis to the MoveNext() method. Without this, async/iterator methods
/// resolve to zero effects because the stub method body just creates the state machine.
///
/// Detection via [AsyncStateMachine] and [IteratorStateMachine] custom attributes.
/// Reads the Type argument directly from the attribute blob — authoritative.
/// No name-pattern fallback (fragile under IL rewriters).
/// </summary>
public sealed class StateMachineResolver
{
    private readonly AssemblyIndex _assemblyIndex;
    private readonly Dictionary<MethodKey, MethodLocation?> _cache = [];

    // Attribute type names to detect
    private static readonly HashSet<string> StateMachineAttributeNames = new(StringComparer.Ordinal)
    {
        "AsyncStateMachineAttribute",
        "IteratorStateMachineAttribute"
    };

    public StateMachineResolver(AssemblyIndex assemblyIndex)
    {
        _assemblyIndex = assemblyIndex;
    }

    /// <summary>
    /// If the method is an async/iterator stub, returns the MoveNext() location
    /// on the generated state machine type. Otherwise returns null.
    /// </summary>
    public MethodLocation? Redirect(MethodLocation method)
    {
        if (_cache.TryGetValue(method.Key, out var cached))
            return cached;

        var result = TryRedirect(method);
        _cache[method.Key] = result;
        return result;
    }

    private MethodLocation? TryRedirect(MethodLocation method)
    {
        try
        {
            var reader = method.Assembly.MetadataReader;
            var methodDef = reader.GetMethodDefinition(method.Handle);

            foreach (var attrHandle in methodDef.GetCustomAttributes())
            {
                var attr = reader.GetCustomAttribute(attrHandle);
                var attrTypeName = GetAttributeTypeName(reader, attr);
                if (attrTypeName == null || !StateMachineAttributeNames.Contains(attrTypeName))
                    continue;

                // Found a state machine attribute — extract the Type argument
                var stateMachineTypeName = ExtractTypeArgFromAttribute(reader, attr, method.Assembly);
                if (stateMachineTypeName == null)
                    continue;

                // Find MoveNext() on the state machine type
                var moveNext = _assemblyIndex.FindMethod(stateMachineTypeName, "MoveNext");
                if (moveNext != null && moveNext.HasBody)
                    return moveNext;
            }
        }
        catch (BadImageFormatException)
        {
            // Attribute decoding failure — treat method as non-redirectable
        }

        return null;
    }

    private static string? GetAttributeTypeName(MetadataReader reader, CustomAttribute attr)
    {
        try
        {
            if (attr.Constructor.Kind == HandleKind.MemberReference)
            {
                var ctor = reader.GetMemberReference((MemberReferenceHandle)attr.Constructor);
                if (ctor.Parent.Kind == HandleKind.TypeReference)
                {
                    var typeRef = reader.GetTypeReference((TypeReferenceHandle)ctor.Parent);
                    return reader.GetString(typeRef.Name);
                }
            }
            else if (attr.Constructor.Kind == HandleKind.MethodDefinition)
            {
                var ctorDef = reader.GetMethodDefinition((MethodDefinitionHandle)attr.Constructor);
                var typeHandle = ctorDef.GetDeclaringType();
                var typeDef = reader.GetTypeDefinition(typeHandle);
                return reader.GetString(typeDef.Name);
            }
        }
        catch (BadImageFormatException) { }
        return null;
    }

    /// <summary>
    /// Extracts the Type argument from the [AsyncStateMachine(typeof(NestedType))] attribute blob.
    ///
    /// Attribute blob format (ECMA-335 II.23.3):
    ///   Prolog: 0x0001 (2 bytes)
    ///   FixedArgs: SerString containing the assembly-qualified type name
    ///   NamedArgs: (not used for this attribute)
    /// </summary>
    private static string? ExtractTypeArgFromAttribute(MetadataReader reader, CustomAttribute attr,
        LoadedAssembly assembly)
    {
        try
        {
            var blobReader = reader.GetBlobReader(attr.Value);

            // Read prolog (must be 0x0001)
            if (blobReader.ReadUInt16() != 0x0001)
                return null;

            // The first fixed argument is a System.Type, serialized as a SerString
            // containing the assembly-qualified type name.
            var typeNameLength = blobReader.ReadCompressedInteger();
            if (typeNameLength <= 0)
                return null;

            var typeNameBytes = blobReader.ReadBytes(typeNameLength);
            var assemblyQualifiedName = System.Text.Encoding.UTF8.GetString(typeNameBytes);

            // Extract just the type name (before the comma that starts assembly qualification)
            var commaIndex = assemblyQualifiedName.IndexOf(',');
            var typeName = commaIndex >= 0
                ? assemblyQualifiedName[..commaIndex].Trim()
                : assemblyQualifiedName.Trim();

            // Normalize nested type separator: '+' in reflection names → '+' in our format
            // (already matches our MethodKey.GetFullTypeName convention)
            return typeName;
        }
        catch
        {
            return null;
        }
    }
}
