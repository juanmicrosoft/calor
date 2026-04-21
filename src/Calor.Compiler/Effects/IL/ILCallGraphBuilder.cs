using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Calor.Compiler.Effects.IL;

/// <summary>
/// Extracts call edges from IL method bodies using System.Reflection.Metadata.
/// Handles: call (0x28), callvirt (0x6F), newobj (0x73), ldftn (0xFE06), ldvirtftn (0xFE07).
/// Marks methods containing calli (0x29) as having indirect calls (Incomplete boundary).
/// </summary>
public sealed class ILCallGraphBuilder
{
    private readonly AssemblyIndex _assemblyIndex;

    public ILCallGraphBuilder(AssemblyIndex assemblyIndex)
    {
        _assemblyIndex = assemblyIndex;
    }

    /// <summary>
    /// Extracts all outgoing call edges from a method body.
    /// </summary>
    public CallEdgeResult ExtractCallEdges(MethodLocation method)
    {
        var edges = new List<CallEdge>();
        var hasIndirectCalls = false;

        try
        {
            var reader = method.Assembly.MetadataReader;
            var methodDef = reader.GetMethodDefinition(method.Handle);

            if (methodDef.RelativeVirtualAddress == 0)
                return new CallEdgeResult(edges, false);

            var body = method.Assembly.PEReader.GetMethodBody(methodDef.RelativeVirtualAddress);
            if (body == null)
                return new CallEdgeResult(edges, false);

            var ilReader = body.GetILReader();

            while (ilReader.Offset < ilReader.Length)
            {
                var opcode = ilReader.ReadByte();

                // Two-byte opcode prefix
                if (opcode == 0xFE)
                {
                    if (ilReader.Offset >= ilReader.Length) break;
                    var opcode2 = ilReader.ReadByte();

                    switch (opcode2)
                    {
                        case 0x06: // ldftn — function pointer load (delegate creation)
                        {
                            var token = ilReader.ReadInt32();
                            var callee = ResolveMethodToken(reader, MetadataTokens.EntityHandle(token));
                            if (callee != null)
                                edges.Add(new CallEdge(callee.Value, IsVirtual: false,
                                    IsConstructor: false, IsDelegate: true));
                            break;
                        }
                        case 0x07: // ldvirtftn — virtual function pointer load
                        {
                            var token = ilReader.ReadInt32();
                            var callee = ResolveMethodToken(reader, MetadataTokens.EntityHandle(token));
                            if (callee != null)
                                edges.Add(new CallEdge(callee.Value, IsVirtual: true,
                                    IsConstructor: false, IsDelegate: true));
                            break;
                        }
                        default:
                            SkipTwoByteOperand(opcode2, ref ilReader);
                            break;
                    }
                    continue;
                }

                switch (opcode)
                {
                    case 0x28: // call
                    {
                        var token = ilReader.ReadInt32();
                        var callee = ResolveMethodToken(reader, MetadataTokens.EntityHandle(token));
                        if (callee != null)
                            edges.Add(new CallEdge(callee.Value, IsVirtual: false,
                                IsConstructor: false, IsDelegate: false));
                        break;
                    }
                    case 0x6F: // callvirt
                    {
                        var token = ilReader.ReadInt32();
                        var callee = ResolveMethodToken(reader, MetadataTokens.EntityHandle(token));
                        if (callee != null)
                            edges.Add(new CallEdge(callee.Value, IsVirtual: true,
                                IsConstructor: false, IsDelegate: false));
                        break;
                    }
                    case 0x73: // newobj
                    {
                        var token = ilReader.ReadInt32();
                        var callee = ResolveMethodToken(reader, MetadataTokens.EntityHandle(token));
                        if (callee != null)
                            edges.Add(new CallEdge(callee.Value, IsVirtual: false,
                                IsConstructor: true, IsDelegate: false));
                        break;
                    }
                    case 0x29: // calli — indirect call, mark as boundary
                    {
                        ilReader.ReadInt32(); // skip signature token
                        hasIndirectCalls = true;
                        break;
                    }
                    default:
                        SkipOneByteOperand(opcode, ref ilReader);
                        break;
                }
            }
        }
        catch (BadImageFormatException)
        {
            // Malformed IL — treat as incomplete
            hasIndirectCalls = true;
        }

        return new CallEdgeResult(edges, hasIndirectCalls);
    }

    private static MethodKey? ResolveMethodToken(MetadataReader reader, EntityHandle handle)
    {
        try
        {
            return handle.Kind switch
            {
                HandleKind.MethodDefinition =>
                    MethodKey.FromDefinition(reader, (MethodDefinitionHandle)handle),
                HandleKind.MemberReference =>
                    MethodKey.FromReference(reader, (MemberReferenceHandle)handle),
                HandleKind.MethodSpecification =>
                    MethodKey.FromSpecification(reader, (MethodSpecificationHandle)handle),
                _ => null
            };
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Skips the operand bytes for a single-byte opcode.
    /// </summary>
    private static void SkipOneByteOperand(byte opcode, ref BlobReader reader)
    {
        // Operand sizes for single-byte opcodes
        // Reference: ECMA-335 Partition III
        switch (opcode)
        {
            // No operand (0 bytes)
            case 0x00: // nop
            case 0x01: // break
            case 0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x08 or 0x09: // ldarg.0-3, ldloc.0-3
            case 0x0A or 0x0B or 0x0C or 0x0D: // stloc.0-3
            case 0x14: // ldnull
            case 0x15 or 0x16 or 0x17 or 0x18 or 0x19 or 0x1A or 0x1B or 0x1C or 0x1D or 0x1E: // ldc.i4.m1 - ldc.i4.8
            case 0x25: // dup
            case 0x26: // pop
            case 0x2A: // ret
            case 0x46 or 0x47 or 0x48 or 0x49 or 0x4A or 0x4B or 0x4C or 0x4D or 0x4E or 0x4F: // ldind.*
            case 0x50 or 0x51 or 0x52 or 0x53 or 0x54 or 0x55 or 0x56: // stind.*
            case 0x57 or 0x58 or 0x59 or 0x5A or 0x5B or 0x5C or 0x5D or 0x5E or 0x5F or 0x60 or 0x61 or 0x62 or 0x63: // add-conv
            case 0x64 or 0x65 or 0x66 or 0x67: // conv.*
            case 0x82 or 0x83: // conv.ovf.*
            case 0x90 or 0x91 or 0x92 or 0x93 or 0x94 or 0x95 or 0x96 or 0x97 or 0x98 or 0x99 or 0x9A: // ldelem.*
            case 0x9B or 0x9C or 0x9D or 0x9E or 0x9F or 0xA0 or 0xA1 or 0xA2: // stelem.*, conv.*
            case 0xB3 or 0xB4 or 0xB5 or 0xB6 or 0xB7 or 0xB8 or 0xB9 or 0xBA or 0xBB or 0xBC or 0xBD or 0xBE: // conv.ovf.*
            case 0x8E: // ldlen
            case 0xC3: // ckfinite
            case 0xD1 or 0xD2: // conv.u2, conv.u1
            case 0xD3: // conv.i
            case 0xD4: // conv.ovf.i
            case 0xD5: // conv.ovf.u
            case 0xD6: // add.ovf
            case 0xD7: // add.ovf.un
            case 0xD8: // mul.ovf
            case 0xD9: // mul.ovf.un
            case 0xDA: // sub.ovf
            case 0xDB: // sub.ovf.un
            case 0xDC: // endfinally
            case 0xDE: // conv.u
            case 0xE0: // prefix opcodes — shouldn't reach here
                break;

            // Inline int8 (1 byte)
            case 0x0E: // ldarg.s
            case 0x0F: // ldarga.s
            case 0x10: // starg.s
            case 0x11: // ldloc.s
            case 0x12: // ldloca.s
            case 0x13: // stloc.s
            case 0x1F: // ldc.i4.s
            case 0x2B: // br.s
            case 0x2C or 0x2D or 0x2E or 0x2F or 0x30 or 0x31 or 0x32 or 0x33 or 0x34 or 0x35 or 0x36 or 0x37: // br*.s
                if (reader.Offset < reader.Length) reader.ReadByte();
                break;

            // Inline int32 (4 bytes) — includes branch targets, field/method/type tokens
            case 0x20: // ldc.i4
            case 0x38: // br
            case 0x39 or 0x3A or 0x3B or 0x3C or 0x3D or 0x3E or 0x3F or 0x40 or 0x41 or 0x42 or 0x43 or 0x44: // br*
            case 0x27: // jmp
            // case 0x28: call — handled above
            // case 0x29: calli — handled above
            case 0x6D: // castclass
            case 0x6E: // isinst
            // case 0x6F: callvirt — handled above
            case 0x70: // cpobj
            case 0x71: // ldobj
            case 0x72: // ldstr
            // case 0x73: newobj — handled above
            case 0x74: // castclass
            case 0x75: // isinst
            case 0x79: // unbox
            case 0x7B or 0x7C or 0x7D or 0x7E: // ldfld, ldflda, stfld, ldsfld
            case 0x7F or 0x80: // ldsflda, stsfld
            case 0x81: // stobj
            case 0x8C: // box
            case 0x8D: // newarr
            case 0x8F: // ldelema
            case 0xA3: // ldelem
            case 0xA4: // stelem
            case 0xA5: // unbox.any
            case 0xC2: // refanyval
            case 0xC6: // mkrefany
            case 0xD0: // ldtoken
            case 0xDD: // leave
                if (reader.RemainingBytes >= 4) reader.ReadInt32();
                break;

            // Inline int64 (8 bytes)
            case 0x21: // ldc.i8
                if (reader.RemainingBytes >= 8) reader.ReadInt64();
                break;

            // Inline float32 (4 bytes)
            case 0x22: // ldc.r4
                if (reader.RemainingBytes >= 4) reader.ReadSingle();
                break;

            // Inline float64 (8 bytes)
            case 0x23: // ldc.r8
                if (reader.RemainingBytes >= 8) reader.ReadDouble();
                break;

            // Switch instruction — int32 count + count × int32 targets
            case 0x45: // switch
                if (reader.RemainingBytes >= 4)
                {
                    var count = reader.ReadInt32();
                    var bytesToSkip = count * 4;
                    if (reader.RemainingBytes >= bytesToSkip)
                        reader.Offset += bytesToSkip;
                    else
                        reader.Offset = reader.Length;
                }
                break;

            // Inline short branch (1 byte) — leave.s
            case 0xDF: // leave.s
                if (reader.Offset < reader.Length) reader.ReadByte();
                break;

            default:
                // Unknown opcode — try to skip safely by assuming no operand
                break;
        }
    }

    /// <summary>
    /// Skips the operand bytes for a two-byte opcode (0xFE prefix).
    /// </summary>
    private static void SkipTwoByteOperand(byte opcode2, ref BlobReader reader)
    {
        switch (opcode2)
        {
            // No operand
            case 0x00: // arglist
            case 0x01: // ceq
            case 0x02: // cgt
            case 0x03: // cgt.un
            case 0x04: // clt
            case 0x05: // clt.un
            // case 0x06: ldftn — handled in caller
            // case 0x07: ldvirtftn — handled in caller
            case 0x09: // ldarg
            case 0x0A: // ldarga
            case 0x0B: // starg
            case 0x0C: // ldloc
            case 0x0D: // ldloca
            case 0x0E: // stloc
                // These are actually InlineVar (2 bytes) — handle below
                break;
            case 0x0F: // localloc
            case 0x11: // endfilter
            case 0x12: // unaligned. (1 byte prefix)
            case 0x13: // volatile.
            case 0x14: // tail.
            case 0x15: // initobj (4 bytes)
            case 0x16: // constrained. (4 bytes)
            case 0x17: // cpblk
            case 0x18: // initblk
            case 0x1A: // rethrow
            case 0x1C: // sizeof (4 bytes)
            case 0x1D: // refanytype
            case 0x19: // no. (1 byte prefix)
                break;

            default:
                break;
        }

        // Handle operand sizes for common two-byte opcodes
        switch (opcode2)
        {
            case 0x09 or 0x0A or 0x0B or 0x0C or 0x0D or 0x0E: // InlineVar (2 bytes)
                if (reader.RemainingBytes >= 2) reader.ReadInt16();
                break;
            case 0x12 or 0x19: // InlineI (1 byte prefix)
                if (reader.Offset < reader.Length) reader.ReadByte();
                break;
            case 0x15 or 0x16 or 0x1C: // InlineTok (4 bytes)
                if (reader.RemainingBytes >= 4) reader.ReadInt32();
                break;
        }
    }
}
