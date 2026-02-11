using Calor.Compiler.Ast;
using Calor.Compiler.Parsing;

namespace Calor.LanguageServer.Utilities;

/// <summary>
/// Result of a symbol lookup at a position.
/// </summary>
public sealed class SymbolLookupResult
{
    public string Name { get; }
    public string Kind { get; }
    public string? Type { get; }
    public TextSpan Span { get; }
    public TextSpan? DefinitionSpan { get; }
    public AstNode? Node { get; }

    public SymbolLookupResult(string name, string kind, string? type, TextSpan span, TextSpan? definitionSpan = null, AstNode? node = null)
    {
        Name = name;
        Kind = kind;
        Type = type;
        Span = span;
        DefinitionSpan = definitionSpan;
        Node = node;
    }
}

/// <summary>
/// Finds symbols at positions and their definitions.
/// </summary>
public static class SymbolFinder
{
    /// <summary>
    /// Find the symbol at a given position in the AST.
    /// Uses a combination of source text analysis and AST traversal.
    /// </summary>
    public static SymbolLookupResult? FindSymbolAtPosition(ModuleNode ast, int line, int column, string source)
    {
        var offset = GetOffset(source, line, column);

        // Check for literals first (before identifier extraction)
        var literalResult = FindLiteralOrKeywordAtPosition(source, offset, line, ast);
        if (literalResult != null)
        {
            return literalResult;
        }

        // Extract the identifier at the cursor position
        var identifier = ExtractIdentifierAtOffset(source, offset);

        if (string.IsNullOrEmpty(identifier))
        {
            return null;
        }

        // Find the context (which function/class/method we're in based on line)
        var context = FindContextAtLine(ast, line);

        // Search for the identifier in the context
        return FindIdentifierInContext(identifier, context, ast, line, source);
    }

    /// <summary>
    /// Find a symbol definition by name.
    /// </summary>
    public static AstNode? FindDefinition(ModuleNode ast, string name)
    {
        // Check functions
        var func = ast.Functions.FirstOrDefault(f => f.Name == name);
        if (func != null) return func;

        // Check classes
        var cls = ast.Classes.FirstOrDefault(c => c.Name == name);
        if (cls != null) return cls;

        // Check interfaces
        var iface = ast.Interfaces.FirstOrDefault(i => i.Name == name);
        if (iface != null) return iface;

        // Check enums
        var enumDef = ast.Enums.FirstOrDefault(e => e.Name == name);
        if (enumDef != null) return enumDef;

        // Check delegates
        var del = ast.Delegates.FirstOrDefault(d => d.Name == name);
        if (del != null) return del;

        return null;
    }

    /// <summary>
    /// Find a function by name.
    /// </summary>
    public static FunctionNode? FindFunction(ModuleNode ast, string name)
    {
        return ast.Functions.FirstOrDefault(f => f.Name == name);
    }

    /// <summary>
    /// Find a method in a class.
    /// </summary>
    public static MethodNode? FindMethod(ClassDefinitionNode cls, string name)
    {
        return cls.Methods.FirstOrDefault(m => m.Name == name);
    }

    /// <summary>
    /// Extracts an identifier at the given offset in the source.
    /// </summary>
    private static string? ExtractIdentifierAtOffset(string source, int offset)
    {
        if (offset < 0 || offset >= source.Length)
            return null;

        // Find the start of the identifier
        var start = offset;
        while (start > 0 && IsIdentifierChar(source[start - 1]))
        {
            start--;
        }

        // Find the end of the identifier
        var end = offset;
        while (end < source.Length && IsIdentifierChar(source[end]))
        {
            end++;
        }

        if (start == end)
            return null;

        return source.Substring(start, end - start);
    }

    private static bool IsIdentifierChar(char c)
    {
        return char.IsLetterOrDigit(c) || c == '_';
    }

    /// <summary>
    /// Finds the context (function, class, method) at a given line.
    /// </summary>
    private static SymbolContext FindContextAtLine(ModuleNode ast, int line)
    {
        var context = new SymbolContext { Module = ast };

        // Check if we're inside a function
        foreach (var func in ast.Functions)
        {
            if (IsLineInSpan(line, func.Span, ast))
            {
                context.Function = func;
                break;
            }
        }

        // Check if we're inside a class
        foreach (var cls in ast.Classes)
        {
            if (IsLineInSpan(line, cls.Span, ast))
            {
                context.Class = cls;

                // Check if inside a method
                foreach (var method in cls.Methods)
                {
                    if (IsLineInSpan(line, method.Span, ast))
                    {
                        context.Method = method;
                        break;
                    }
                }

                // Check if inside a constructor
                foreach (var ctor in cls.Constructors)
                {
                    if (IsLineInSpan(line, ctor.Span, ast))
                    {
                        context.Constructor = ctor;
                        break;
                    }
                }

                break;
            }
        }

        // Check if we're inside an interface
        foreach (var iface in ast.Interfaces)
        {
            if (IsLineInSpan(line, iface.Span, ast))
            {
                context.Interface = iface;
                break;
            }
        }

        // Check if we're inside an enum
        foreach (var enumDef in ast.Enums)
        {
            if (IsLineInSpan(line, enumDef.Span, ast))
            {
                context.Enum = enumDef;
                break;
            }
        }

        return context;
    }

    /// <summary>
    /// Checks if a line is within a node's span.
    /// Uses the span's line info since offset-based spans may be incomplete.
    /// </summary>
    private static bool IsLineInSpan(int line, TextSpan span, ModuleNode module)
    {
        // The span.Line is the start line. We need to estimate the end line.
        // For now, assume the node extends to the end of the module unless we find a better indicator.
        return line >= span.Line;
    }

    /// <summary>
    /// Finds an identifier in the given context.
    /// </summary>
    private static SymbolLookupResult? FindIdentifierInContext(string identifier, SymbolContext context, ModuleNode ast, int line, string source)
    {
        // Check if it's a type name
        if (IsTypeName(identifier))
        {
            return CreateTypeResult(identifier, line, source);
        }

        // If we're in a function, check parameters and locals
        if (context.Function != null)
        {
            var func = context.Function;

            // Check if it's the function name itself
            if (func.Name == identifier)
            {
                return new SymbolLookupResult(
                    func.Name, "function", func.Output?.TypeName ?? "void",
                    func.Span, func.Span, func);
            }

            // Check parameters
            foreach (var param in func.Parameters)
            {
                if (param.Name == identifier)
                {
                    return new SymbolLookupResult(
                        param.Name, "parameter", param.TypeName,
                        param.Span, param.Span, param);
                }
            }

            // Check local bindings
            foreach (var stmt in func.Body)
            {
                if (stmt is BindStatementNode bind && bind.Name == identifier && bind.Span.Line <= line)
                {
                    return new SymbolLookupResult(
                        bind.Name,
                        bind.IsMutable ? "mutable variable" : "variable",
                        bind.TypeName,
                        bind.Span, bind.Span, bind);
                }
            }

            // It might be a reference to a parameter or local - return as reference
            var paramRef = func.Parameters.FirstOrDefault(p => p.Name == identifier);
            if (paramRef != null)
            {
                return new SymbolLookupResult(
                    identifier, "variable reference", paramRef.TypeName,
                    CreateSpanAtLine(line, source), paramRef.Span, null);
            }
        }

        // If we're in a method, check parameters and locals
        if (context.Method != null)
        {
            var method = context.Method;

            if (method.Name == identifier)
            {
                return new SymbolLookupResult(
                    method.Name, "method", method.Output?.TypeName ?? "void",
                    method.Span, method.Span, method);
            }

            foreach (var param in method.Parameters)
            {
                if (param.Name == identifier)
                {
                    return new SymbolLookupResult(
                        param.Name, "parameter", param.TypeName,
                        param.Span, param.Span, param);
                }
            }

            foreach (var stmt in method.Body)
            {
                if (stmt is BindStatementNode bind && bind.Name == identifier && bind.Span.Line <= line)
                {
                    return new SymbolLookupResult(
                        bind.Name,
                        bind.IsMutable ? "mutable variable" : "variable",
                        bind.TypeName,
                        bind.Span, bind.Span, bind);
                }
            }
        }

        // If we're in a class, check fields, properties, methods
        if (context.Class != null)
        {
            var cls = context.Class;

            if (cls.Name == identifier)
            {
                return new SymbolLookupResult(cls.Name, "class", null, cls.Span, cls.Span, cls);
            }

            foreach (var field in cls.Fields)
            {
                if (field.Name == identifier)
                {
                    return new SymbolLookupResult(
                        field.Name, "field", field.TypeName,
                        field.Span, field.Span, field);
                }
            }

            foreach (var prop in cls.Properties)
            {
                if (prop.Name == identifier)
                {
                    return new SymbolLookupResult(
                        prop.Name, "property", prop.TypeName,
                        prop.Span, prop.Span, prop);
                }
            }

            foreach (var method in cls.Methods)
            {
                if (method.Name == identifier)
                {
                    return new SymbolLookupResult(
                        method.Name, "method", method.Output?.TypeName ?? "void",
                        method.Span, method.Span, method);
                }
            }
        }

        // Check if it's an interface
        if (context.Interface != null)
        {
            var iface = context.Interface;

            if (iface.Name == identifier)
            {
                return new SymbolLookupResult(iface.Name, "interface", null, iface.Span, iface.Span, iface);
            }

            foreach (var method in iface.Methods)
            {
                if (method.Name == identifier)
                {
                    return new SymbolLookupResult(
                        method.Name, "method signature", method.Output?.TypeName ?? "void",
                        method.Span, method.Span, method);
                }
            }
        }

        // Check if it's an enum
        if (context.Enum != null)
        {
            var enumDef = context.Enum;

            if (enumDef.Name == identifier)
            {
                return new SymbolLookupResult(
                    enumDef.Name, "enum", enumDef.UnderlyingType,
                    enumDef.Span, enumDef.Span, enumDef);
            }

            foreach (var member in enumDef.Members)
            {
                if (member.Name == identifier)
                {
                    return new SymbolLookupResult(
                        member.Name, "enum member", null,
                        member.Span, member.Span, member);
                }
            }
        }

        // Check module-level definitions
        if (context.Module.Name == identifier)
        {
            return new SymbolLookupResult(
                context.Module.Name, "module", null,
                context.Module.Span, context.Module.Span, context.Module);
        }

        // Check all functions at module level
        foreach (var func in ast.Functions)
        {
            if (func.Name == identifier)
            {
                return new SymbolLookupResult(
                    func.Name, "function", func.Output?.TypeName ?? "void",
                    func.Span, func.Span, func);
            }
        }

        // Check all classes at module level
        foreach (var cls in ast.Classes)
        {
            if (cls.Name == identifier)
            {
                return new SymbolLookupResult(cls.Name, "class", null, cls.Span, cls.Span, cls);
            }
        }

        // Check all interfaces
        foreach (var iface in ast.Interfaces)
        {
            if (iface.Name == identifier)
            {
                return new SymbolLookupResult(iface.Name, "interface", null, iface.Span, iface.Span, iface);
            }
        }

        // Check all enums
        foreach (var enumDef in ast.Enums)
        {
            if (enumDef.Name == identifier)
            {
                return new SymbolLookupResult(
                    enumDef.Name, "enum", enumDef.UnderlyingType,
                    enumDef.Span, enumDef.Span, enumDef);
            }
        }

        // Check all delegates
        foreach (var del in ast.Delegates)
        {
            if (del.Name == identifier)
            {
                return new SymbolLookupResult(
                    del.Name, "delegate", del.Output?.TypeName ?? "void",
                    del.Span, del.Span, del);
            }
        }

        // Unknown identifier - return as reference
        return new SymbolLookupResult(
            identifier, "reference", null,
            CreateSpanAtLine(line, source), null, null);
    }

    /// <summary>
    /// Finds a literal or keyword at the given position.
    /// </summary>
    private static SymbolLookupResult? FindLiteralOrKeywordAtPosition(string source, int offset, int line, ModuleNode ast)
    {
        if (offset < 0 || offset >= source.Length)
            return null;

        var c = source[offset];

        // Check for numeric literal
        if (char.IsDigit(c))
        {
            var (numStr, _) = ExtractNumber(source, offset);
            if (numStr.Contains('.'))
            {
                return new SymbolLookupResult(numStr, "float literal", "FLOAT", CreateSpanAtLine(line, source));
            }
            return new SymbolLookupResult(numStr, "integer literal", "INT", CreateSpanAtLine(line, source));
        }

        // Check for string literal
        if (c == '"')
        {
            var strContent = ExtractString(source, offset);
            return new SymbolLookupResult(strContent, "string literal", "STRING", CreateSpanAtLine(line, source));
        }

        return null;
    }

    private static (string Value, int EndOffset) ExtractNumber(string source, int offset)
    {
        var start = offset;
        while (start > 0 && (char.IsDigit(source[start - 1]) || source[start - 1] == '.'))
        {
            start--;
        }

        var end = offset;
        while (end < source.Length && (char.IsDigit(source[end]) || source[end] == '.'))
        {
            end++;
        }

        return (source.Substring(start, end - start), end);
    }

    private static string ExtractString(string source, int offset)
    {
        // Find the end of the string
        var end = offset + 1;
        while (end < source.Length && source[end] != '"')
        {
            if (source[end] == '\\' && end + 1 < source.Length)
            {
                end += 2; // Skip escaped character
            }
            else
            {
                end++;
            }
        }

        if (end < source.Length)
        {
            end++; // Include closing quote
        }

        return source.Substring(offset, end - offset);
    }

    private static bool IsTypeName(string identifier)
    {
        // Check for built-in types
        return identifier switch
        {
            "i8" or "i16" or "i32" or "i64" or "INT" => true,
            "u8" or "u16" or "u32" or "u64" => true,
            "f32" or "f64" or "FLOAT" => true,
            "str" or "string" or "STRING" => true,
            "bool" or "BOOL" => true,
            "void" or "VOID" => true,
            "char" or "CHAR" => true,
            _ => false
        };
    }

    private static SymbolLookupResult CreateTypeResult(string typeName, int line, string source)
    {
        var normalizedType = typeName.ToUpperInvariant() switch
        {
            "I8" or "I16" or "I32" or "I64" or "INT" => "INT",
            "U8" or "U16" or "U32" or "U64" => "INT",
            "F32" or "F64" or "FLOAT" => "FLOAT",
            "STR" or "STRING" => "STRING",
            "BOOL" => "BOOL",
            "VOID" => "VOID",
            "CHAR" => "CHAR",
            _ => typeName
        };

        return new SymbolLookupResult(
            typeName, "type", normalizedType,
            CreateSpanAtLine(line, source));
    }

    private static TextSpan CreateSpanAtLine(int line, string source)
    {
        var offset = GetLineStartOffset(source, line);
        return new TextSpan(offset, 1, line, 1);
    }

    private static int GetLineStartOffset(string source, int line)
    {
        var currentLine = 1;
        var offset = 0;

        for (var i = 0; i < source.Length; i++)
        {
            if (currentLine == line)
            {
                return offset;
            }

            if (source[i] == '\n')
            {
                currentLine++;
                offset = i + 1;
            }
        }

        return offset;
    }

    internal static int GetOffset(string source, int line, int column)
    {
        var currentLine = 1;
        var offset = 0;

        for (var i = 0; i < source.Length; i++)
        {
            if (currentLine == line)
            {
                return offset + column - 1;
            }

            if (source[i] == '\n')
            {
                currentLine++;
                offset = i + 1;
            }
        }

        if (currentLine == line)
        {
            return offset + column - 1;
        }

        return source.Length;
    }
}

/// <summary>
/// Represents the context at a position in the source.
/// </summary>
internal sealed class SymbolContext
{
    public required ModuleNode Module { get; init; }
    public FunctionNode? Function { get; set; }
    public ClassDefinitionNode? Class { get; set; }
    public MethodNode? Method { get; set; }
    public ConstructorNode? Constructor { get; set; }
    public InterfaceDefinitionNode? Interface { get; set; }
    public EnumDefinitionNode? Enum { get; set; }
}
