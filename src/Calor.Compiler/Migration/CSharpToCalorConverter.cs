using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Calor.Compiler.Ast;

namespace Calor.Compiler.Migration;

/// <summary>
/// Result of a C# to Calor conversion.
/// </summary>
public sealed class ConversionResult
{
    public bool Success { get; init; }
    public string? CalorSource { get; init; }
    public ModuleNode? Ast { get; init; }
    public ConversionContext Context { get; init; } = new();
    public TimeSpan Duration { get; init; }

    public bool HasErrors => Context.HasErrors;
    public bool HasWarnings => Context.HasWarnings;
    public IReadOnlyList<ConversionIssue> Issues => Context.Issues;
}

/// <summary>
/// Conversion mode controlling how unsupported constructs are handled.
/// </summary>
public enum ConversionMode
{
    /// <summary>
    /// Standard mode: unsupported constructs produce FallbackCommentNode (TODO comments).
    /// </summary>
    Standard,

    /// <summary>
    /// Interop mode: unsupported members are wrapped in §CSHARP{...}§/CSHARP blocks,
    /// preserving the original C# code verbatim for round-trip compilation.
    /// </summary>
    Interop
}

/// <summary>
/// Options for C# to Calor conversion.
/// </summary>
public sealed class ConversionOptions
{
    /// <summary>
    /// The module name to use in the generated Calor code.
    /// If not specified, derived from the source file name.
    /// </summary>
    public string? ModuleName { get; set; }

    /// <summary>
    /// Whether to preserve original comments in the output.
    /// </summary>
    public bool PreserveComments { get; set; } = true;

    /// <summary>
    /// Whether to include benchmark metrics comparison.
    /// </summary>
    public bool IncludeBenchmark { get; set; }

    /// <summary>
    /// Whether to enable verbose output.
    /// </summary>
    public bool Verbose { get; set; }

    /// <summary>
    /// Whether to auto-generate unique IDs for Calor elements.
    /// </summary>
    public bool AutoGenerateIds { get; set; } = true;

    /// <summary>
    /// Whether to emit graceful fallback comments for unsupported constructs.
    /// When true, unsupported C# code is emitted as TODO comments instead of invalid Calor.
    /// Default is true.
    /// </summary>
    public bool GracefulFallback { get; set; } = true;

    /// <summary>
    /// Whether to include explanation details about unsupported features.
    /// When true, conversion results include a detailed explanation of what was not converted.
    /// </summary>
    public bool Explain { get; set; }

    /// <summary>
    /// Conversion mode controlling how unsupported constructs are handled.
    /// Standard: produces FallbackCommentNode (TODO comments).
    /// Interop: wraps unsupported members in §CSHARP{...}§/CSHARP blocks.
    /// </summary>
    public ConversionMode Mode { get; set; } = ConversionMode.Standard;

    /// <summary>
    /// Whether to strip C# preprocessor directives (#if, #region, #pragma, etc.) before conversion.
    /// Prevents infinite hangs and OOM from conditional compilation blocks.
    /// </summary>
    public bool StripPreprocessor { get; set; } = true;

    /// <summary>When true, wraps unsupported constructs in §CSHARP blocks instead of emitting broken Calor.</summary>
    public bool PassthroughOnError { get; set; } = false;

    /// <summary>
    /// Whether the emitter should elide `§/C` for zero-argument calls (v0.6.1 default behaviour).
    /// Set to <c>false</c> to produce v0.6.0-compatible output that always emits explicit `§/C` closers.
    /// Default is <c>true</c> (matches <see cref="ConversionContext.UseImplicitCallCloser"/>).
    /// </summary>
    public bool UseImplicitCallCloser { get; set; } = true;
}

/// <summary>
/// Main converter that orchestrates the C# to Calor conversion pipeline.
///
/// Pipeline: C# Source → Roslyn Parse → RoslynSyntaxVisitor → Calor AST → CalorEmitter → Calor Source
/// </summary>
public sealed class CSharpToCalorConverter
{
    private readonly ConversionOptions _options;

    public CSharpToCalorConverter(ConversionOptions? options = null)
    {
        _options = options ?? new ConversionOptions();
    }

    /// <summary>
    /// Converts C# source code to Calor source code.
    /// </summary>
    public ConversionResult Convert(string csharpSource, string? sourceFile = null)
    {
        var startTime = DateTime.UtcNow;
        var context = CreateContext(sourceFile);
        context.OriginalSource = csharpSource;

        try
        {
            // Step 0: Strip preprocessor directives to avoid Roslyn hangs/OOM
            if (_options.StripPreprocessor)
            {
                try { csharpSource = PreprocessorStripper.Strip(csharpSource); }
                catch (Exception stripEx)
                {
                    context.AddError($"Preprocessor stripping failed: {stripEx.GetType().Name}: {stripEx.Message}");
                    return new ConversionResult { Success = false, Context = context, Duration = DateTime.UtcNow - startTime };
                }
            }

            // Step 1: Parse C# with Roslyn (use Latest language version to accept all C# features)
            var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
            var syntaxTree = CSharpSyntaxTree.ParseText(csharpSource, parseOptions);
            var root = syntaxTree.GetCompilationUnitRoot();

            // Check for parse errors.
            // Skip CS1028 ("Unexpected preprocessor directive") — occurs with "# endregion"
            // (space before endregion). Valid C# that Roslyn recovers from.
            List<Microsoft.CodeAnalysis.Diagnostic> diagnostics;
            try
            {
                diagnostics = root.GetDiagnostics()
                    .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error
                             && d.Id != "CS1028")
                    .ToList();
            }
            catch
            {
                diagnostics = new List<Microsoft.CodeAnalysis.Diagnostic>();
            }

            if (diagnostics.Count > 0)
            {
                foreach (var diag in diagnostics)
                {
                    var lineSpan = diag.Location.GetLineSpan();
                    context.AddError(
                        $"C# parse error: {diag.GetMessage()}",
                        line: lineSpan.StartLinePosition.Line + 1,
                        column: lineSpan.StartLinePosition.Character + 1);
                }

                return new ConversionResult
                {
                    Success = false,
                    Context = context,
                    Duration = DateTime.UtcNow - startTime
                };
            }

            // Step 2: Create semantic model for type inference (best-effort)
            SemanticModel? semanticModel = null;
            try
            {
                var compilation = CSharpCompilation.Create("ConversionAnalysis",
                    new[] { syntaxTree },
                    GetBasicMetadataReferences(),
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
                semanticModel = compilation.GetSemanticModel(syntaxTree);
            }
            catch
            {
                // Semantic model creation is best-effort; proceed without it
            }

            // Visit C# AST and build Calor AST
            ModuleNode? calorAst;
            try
            {
                var moduleName = _options.ModuleName ?? DeriveModuleName(sourceFile, root);
                var visitor = new RoslynSyntaxVisitor(context, semanticModel);
                calorAst = visitor.Convert(root, moduleName);
            }
            catch (Exception visitorEx)
            {
                // Visitor crashed (e.g., NullReferenceException on complex class patterns).
                // Return a graceful failure with a clear error instead of crashing.
                context.AddError($"Conversion visitor crashed: {visitorEx.GetType().Name}: {visitorEx.Message}");
                return new ConversionResult
                {
                    Success = false,
                    Context = context,
                    Duration = DateTime.UtcNow - startTime
                };
            }

            if (context.HasErrors)
            {
                return new ConversionResult
                {
                    Success = false,
                    Ast = calorAst,
                    Context = context,
                    Duration = DateTime.UtcNow - startTime
                };
            }

            // Step 3: Emit Calor source code
            var emitter = new CalorEmitter(context);
            var calorSource = emitter.Emit(calorAst);

            // Step 3b (#717): post-conversion parse validation. If the emitted Calor
            // does not parse and we are in a C#-preserving mode (Interop /
            // PassthroughOnError), rewrap each offending top-level member as a §CSHARP
            // interop block carrying its original C#, so the output is always valid
            // Calor rather than silently-broken text.
            if (context.ShouldPreserveCSharp && !ParsesCleanly(calorSource))
            {
                var rewrapped = TryRewrapUnparseableMembers(calorAst, root, context);
                if (rewrapped != null)
                {
                    calorAst = rewrapped;
                    calorSource = new CalorEmitter(context).Emit(calorAst);
                }

                // Re-validate the (possibly rewrapped) output. If it still does not
                // parse — nothing was rewrappable, a member could not be recovered, or
                // the rewrap itself was insufficient — never ship it silently. Warn
                // always, and fail under passthroughOnError, whose contract is exactly
                // "never hand me broken output".
                if (!ParsesCleanly(calorSource))
                {
                    context.AddWarning(
                        "Emitted Calor does not parse and could not be fully preserved as " +
                        "§CSHARP interop blocks; the output may be invalid.",
                        feature: "post-validation-fallback");

                    if (_options.PassthroughOnError)
                    {
                        return new ConversionResult
                        {
                            Success = false,
                            CalorSource = calorSource,
                            Ast = calorAst,
                            Context = context,
                            Duration = DateTime.UtcNow - startTime
                        };
                    }
                }
            }

            if (_options.Verbose)
            {
                Console.WriteLine($"Converted {context.Stats.ConvertedNodes} nodes");
                Console.WriteLine($"  Classes: {context.Stats.ClassesConverted}");
                Console.WriteLine($"  Interfaces: {context.Stats.InterfacesConverted}");
                Console.WriteLine($"  Methods: {context.Stats.MethodsConverted}");
                Console.WriteLine($"  Properties: {context.Stats.PropertiesConverted}");
                Console.WriteLine($"  Fields: {context.Stats.FieldsConverted}");
            }

            return new ConversionResult
            {
                Success = true,
                CalorSource = calorSource,
                Ast = calorAst,
                Context = context,
                Duration = DateTime.UtcNow - startTime
            };
        }
        catch (Exception ex)
        {
            // If the visitor crashed partway through, try to emit whatever was
            // converted so far rather than returning nothing. This handles
            // NullReferenceException in complex class hierarchies where some
            // members convert fine but one triggers an unhandled null.
            context.AddError($"Conversion failed: {ex.GetType().Name}: {ex.Message}");

            return new ConversionResult
            {
                Success = false,
                Context = context,
                Duration = DateTime.UtcNow - startTime
            };
        }
    }

    /// <summary>
    /// Converts a C# file to Calor.
    /// </summary>
    public async Task<ConversionResult> ConvertFileAsync(string csharpFilePath)
    {
        if (!File.Exists(csharpFilePath))
        {
            var context = new ConversionContext { SourceFile = csharpFilePath };
            context.AddError($"Source file not found: {csharpFilePath}");
            return new ConversionResult { Success = false, Context = context };
        }

        // Use replacement fallback to handle files with unpaired surrogates
        // (e.g., regex patterns containing \uD800-\uDBFF in string literals)
        var encoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
        var source = await File.ReadAllTextAsync(csharpFilePath, encoding);
        return Convert(source, csharpFilePath);
    }

    /// <summary>
    /// Converts a C# file and writes the output to an Calor file.
    /// </summary>
    public async Task<ConversionResult> ConvertFileAndSaveAsync(string csharpFilePath, string? outputPath = null)
    {
        var result = await ConvertFileAsync(csharpFilePath);

        if (result.Success && result.CalorSource != null)
        {
            var calorPath = outputPath ?? Path.ChangeExtension(csharpFilePath, ".calr");
            var writeEncoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
            await File.WriteAllTextAsync(calorPath, result.CalorSource, writeEncoding);
        }

        return result;
    }

    /// <summary>
    /// Detects the direction of conversion based on file extension.
    /// </summary>
    public static ConversionDirection DetectDirection(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".cs" => ConversionDirection.CSharpToCalor,
            ".calr" => ConversionDirection.CalorToCSharp,
            _ => ConversionDirection.Unknown
        };
    }

    /// <summary>
    /// Test seam (#717): overrides the post-conversion parse check. Natural inputs
    /// that emit unparseable Calor are extremely rare (the converter's visitor-level
    /// §CSHARP wrapping already handles unsupported features), so tests inject this to
    /// exercise the fallback path deterministically. Null in production.
    /// </summary>
    internal Func<string, bool>? ParseValidatorOverride { get; set; }

    /// <summary>True if <paramref name="calorSource"/> lexes and parses without errors.</summary>
    private bool ParsesCleanly(string calorSource)
    {
        if (ParseValidatorOverride != null)
        {
            return ParseValidatorOverride(calorSource);
        }

        var diagnostics = new Diagnostics.DiagnosticBag();
        var tokens = new Parsing.Lexer(calorSource, diagnostics).TokenizeAllForParser();
        if (diagnostics.HasErrors)
        {
            return false;
        }

        _ = new Parsing.Parser(tokens, diagnostics).Parse();
        return !diagnostics.HasErrors;
    }

    /// <summary>
    /// Post-conversion fallback (#717): the full emitted Calor did not parse. Find each
    /// top-level member whose own emission does not parse, and — when its original C#
    /// can be recovered from <paramref name="root"/> — replace it with a §CSHARP interop
    /// block preserving that C#. Returns a rewritten module, or null if nothing could be
    /// rewrapped (leave the output unchanged for the caller to surface).
    /// </summary>
    private ModuleNode? TryRewrapUnparseableMembers(
        ModuleNode module, CompilationUnitSyntax root, ConversionContext context)
    {
        var sources = CollectTopLevelTypeSources(root);

        var failedClasses = new List<ClassDefinitionNode>();
        var failedInterfaces = new List<InterfaceDefinitionNode>();
        var failedEnums = new List<EnumDefinitionNode>();
        var failedDelegates = new List<DelegateDefinitionNode>();
        var interops = new List<CSharpInteropBlockNode>();

        foreach (var cls in module.Classes)
        {
            if (!MemberParsesCleanly(module, classes: new[] { cls }) &&
                TryTakeSource(sources, "class", cls.Name, out var csharp))
            {
                interops.Add(MakeFallbackInterop(csharp, cls.Name));
                failedClasses.Add(cls);
            }
        }

        foreach (var iface in module.Interfaces)
        {
            if (!MemberParsesCleanly(module, interfaces: new[] { iface }) &&
                TryTakeSource(sources, "interface", iface.Name, out var csharp))
            {
                interops.Add(MakeFallbackInterop(csharp, iface.Name));
                failedInterfaces.Add(iface);
            }
        }

        foreach (var en in module.Enums)
        {
            if (!MemberParsesCleanly(module, enums: new[] { en }) &&
                TryTakeSource(sources, "enum", en.Name, out var csharp))
            {
                interops.Add(MakeFallbackInterop(csharp, en.Name));
                failedEnums.Add(en);
            }
        }

        foreach (var del in module.Delegates)
        {
            if (!MemberParsesCleanly(module, delegates: new[] { del }) &&
                TryTakeSource(sources, "delegate", del.Name, out var csharp))
            {
                interops.Add(MakeFallbackInterop(csharp, del.Name));
                failedDelegates.Add(del);
            }
        }

        if (interops.Count == 0)
        {
            return null;
        }

        context.Stats.InteropBlocksEmitted += interops.Count;
        context.Stats.FallbackInteropBlocksEmitted += interops.Count;

        return new ModuleNode(
            module.Span, module.Id, module.Name, module.Usings,
            module.Interfaces.Where(i => !failedInterfaces.Contains(i)).ToList(),
            module.Classes.Where(c => !failedClasses.Contains(c)).ToList(),
            module.Enums.Where(e => !failedEnums.Contains(e)).ToList(),
            module.EnumExtensions,
            module.Delegates.Where(d => !failedDelegates.Contains(d)).ToList(),
            module.Functions, module.Attributes, module.Issues, module.Assumptions,
            module.Invariants, module.Decisions, module.Context,
            module.InteropBlocks.Concat(interops).ToList(),
            module.RefinementTypes, module.IndexedTypes, module.TypePreprocessorBlocks);
    }

    /// <summary>Emits a module containing only the given member(s) and reports whether
    /// that emission parses — used to isolate which top-level member is unparseable.</summary>
    private bool MemberParsesCleanly(
        ModuleNode module,
        IReadOnlyList<ClassDefinitionNode>? classes = null,
        IReadOnlyList<InterfaceDefinitionNode>? interfaces = null,
        IReadOnlyList<EnumDefinitionNode>? enums = null,
        IReadOnlyList<DelegateDefinitionNode>? delegates = null)
    {
        var solo = new ModuleNode(
            module.Span, module.Id, module.Name, module.Usings,
            interfaces ?? Array.Empty<InterfaceDefinitionNode>(),
            classes ?? Array.Empty<ClassDefinitionNode>(),
            enums ?? Array.Empty<EnumDefinitionNode>(),
            Array.Empty<EnumExtensionNode>(),
            delegates ?? Array.Empty<DelegateDefinitionNode>(),
            Array.Empty<FunctionNode>(), module.Attributes,
            Array.Empty<IssueNode>(), Array.Empty<AssumeNode>(),
            Array.Empty<InvariantNode>(), Array.Empty<DecisionNode>(), null);

        // Fresh context so the probe emission does not perturb the real conversion's stats.
        var emitted = new CalorEmitter(CreateContext(null)).Emit(solo);
        return ParsesCleanly(emitted);
    }

    private static CSharpInteropBlockNode MakeFallbackInterop(string csharpSource, string memberName)
        => new(
            Parsing.TextSpan.Empty,
            csharpSource,
            featureName: "post-validation-fallback",
            reason: $"Converted Calor for '{memberName}' did not parse; original C# preserved (#717).");

    private sealed record TypeSource(bool IsPartial, string Text);

    /// <summary>
    /// Collects top-level (compilation-unit or namespace level) type declarations from the
    /// C# tree, keyed by "kind/name" — the resolution the Calor side can address, since the
    /// module flattens namespaces so a Calor member carries only its bare name. The value is
    /// the list of C# declarations with that key: usually one, several when the type is
    /// <c>partial</c>, or — the ambiguous case — several distinct types of the same name in
    /// different namespaces. <see cref="TryTakeSource"/> decides what is safely recoverable.
    /// </summary>
    private static Dictionary<string, List<TypeSource>> CollectTopLevelTypeSources(CompilationUnitSyntax root)
    {
        var map = new Dictionary<string, List<TypeSource>>(StringComparer.Ordinal);
        foreach (var member in root.DescendantNodes()
                     .OfType<MemberDeclarationSyntax>()
                     .Where(m => m.Parent is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax))
        {
            var (kind, name) = member switch
            {
                ClassDeclarationSyntax c => ("class", c.Identifier.Text),
                StructDeclarationSyntax s => ("class", s.Identifier.Text),
                RecordDeclarationSyntax r => ("class", r.Identifier.Text),
                InterfaceDeclarationSyntax i => ("interface", i.Identifier.Text),
                EnumDeclarationSyntax e => ("enum", e.Identifier.Text),
                DelegateDeclarationSyntax d => ("delegate", d.Identifier.Text),
                _ => (null, null),
            };

            if (kind == null || name == null)
            {
                continue;
            }

            var isPartial = member.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));
            var key = $"{kind}/{name}";
            (map.TryGetValue(key, out var list) ? list : map[key] = new List<TypeSource>())
                .Add(new TypeSource(isPartial, DeclarationSourceText(member)));
        }

        return map;
    }

    /// <summary>
    /// The declaration's source text with its doc-comment trivia preserved (so §CSHARP
    /// fallbacks keep the docs — agents lose docs exactly on the members that most needed
    /// them). Uses ToString() (not ToFullString()) for the body so leading namespace
    /// indentation does not bleed in, then prepends only the XML doc trivia.
    /// </summary>
    private static string DeclarationSourceText(MemberDeclarationSyntax member)
    {
        var docs = string.Concat(member.GetLeadingTrivia()
            .Where(t => t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
                     || t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
            .Select(t => t.ToFullString()));
        return docs + member.ToString();
    }

    /// <summary>
    /// Recovers the original C# for a top-level member the Calor emitter could not render.
    /// Safe cases: exactly one declaration with that kind/name, or several that are all
    /// <c>partial</c> (one merged type — concatenate). The ambiguous case — two or more
    /// distinct same-named types in different namespaces — is refused (returns false) rather
    /// than risk dragging a healthy type into another's interop block. Entries are removed on
    /// take so a second same-named failure cannot reuse them.
    /// </summary>
    private static bool TryTakeSource(
        Dictionary<string, List<TypeSource>> sources, string kind, string name, out string csharp)
    {
        csharp = "";
        var key = $"{kind}/{name}";
        if (!sources.TryGetValue(key, out var list) || list.Count == 0)
        {
            return false;
        }

        if (list.Count == 1)
        {
            csharp = list[0].Text;
        }
        else if (list.All(s => s.IsPartial))
        {
            csharp = string.Join("\n\n", list.Select(s => s.Text));
        }
        else
        {
            return false; // ambiguous cross-namespace collision — not safely recoverable
        }

        sources.Remove(key);
        return true;
    }

    private ConversionContext CreateContext(string? sourceFile)
    {
        return new ConversionContext
        {
            SourceFile = sourceFile,
            Verbose = _options.Verbose,
            IncludeBenchmark = _options.IncludeBenchmark,
            PreserveComments = _options.PreserveComments,
            AutoGenerateIds = _options.AutoGenerateIds,
            ModuleName = _options.ModuleName,
            GracefulFallback = _options.GracefulFallback,
            Mode = _options.Mode,
            PassthroughOnError = _options.PassthroughOnError,
            UseImplicitCallCloser = _options.UseImplicitCallCloser
        };
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("SingleFile", "IL3000",
        Justification = "Assembly.Location is checked for empty string; gracefully returns no references in single-file mode.")]
    private static MetadataReference[] GetBasicMetadataReferences()
    {
        var refs = new List<MetadataReference>();

        // Add core runtime assembly
        var objectLocation = typeof(object).Assembly.Location;
        if (!string.IsNullOrEmpty(objectLocation))
            refs.Add(MetadataReference.CreateFromFile(objectLocation));

        // Add System.Linq for LINQ method resolution
        var linqLocation = typeof(System.Linq.Enumerable).Assembly.Location;
        if (!string.IsNullOrEmpty(linqLocation))
            refs.Add(MetadataReference.CreateFromFile(linqLocation));

        // Add System.Runtime for core types
        var runtimeDir = System.IO.Path.GetDirectoryName(objectLocation);
        if (runtimeDir != null)
        {
            var runtimePath = System.IO.Path.Combine(runtimeDir, "System.Runtime.dll");
            if (System.IO.File.Exists(runtimePath))
                refs.Add(MetadataReference.CreateFromFile(runtimePath));

            var collectionsPath = System.IO.Path.Combine(runtimeDir, "System.Collections.dll");
            if (System.IO.File.Exists(collectionsPath))
                refs.Add(MetadataReference.CreateFromFile(collectionsPath));

            var consolePath = System.IO.Path.Combine(runtimeDir, "System.Console.dll");
            if (System.IO.File.Exists(consolePath))
                refs.Add(MetadataReference.CreateFromFile(consolePath));
        }

        return refs.ToArray();
    }

    private static string DeriveModuleName(string? sourceFile, CompilationUnitSyntax root)
    {
        // Try file-scoped namespace first (namespace X.Y.Z;)
        var fileScopedNs = root.Members.OfType<FileScopedNamespaceDeclarationSyntax>().FirstOrDefault();
        if (fileScopedNs != null)
            return StripVerbatimPrefix(fileScopedNs.Name.ToString());

        // Try block-scoped namespace (namespace X.Y.Z { ... })
        var blockNs = root.Members.OfType<NamespaceDeclarationSyntax>().FirstOrDefault();
        if (blockNs != null)
            return StripVerbatimPrefix(blockNs.Name.ToString());

        // Fall back to file name
        if (!string.IsNullOrEmpty(sourceFile))
        {
            // Sanitize characters that are not valid in Calor module names
            // (e.g., '#' from Verify snapshot filenames like "TestName#HintName.verified.cs")
            return Path.GetFileNameWithoutExtension(sourceFile).Replace('#', '_');
        }

        return "ConvertedModule";
    }

    /// <summary>
    /// Strips C# verbatim identifier prefix (@) from namespace names.
    /// In C#, @is means "use 'is' as an identifier". Calor doesn't need this escape.
    /// </summary>
    private static string StripVerbatimPrefix(string name)
        => name.Replace("@", "");
}

/// <summary>
/// Direction of conversion.
/// </summary>
public enum ConversionDirection
{
    Unknown,
    CSharpToCalor,
    CalorToCSharp
}

/// <summary>
/// Provides a simple facade for bidirectional conversion.
/// </summary>
public static class Converter
{
    /// <summary>
    /// Converts a file in the detected direction.
    /// </summary>
    public static async Task<object> ConvertFileAsync(string filePath, string? outputPath = null)
        => await ConvertFileAsync(filePath, outputPath, options: null);

    /// <summary>
    /// Converts a file in the detected direction with optional <see cref="ConversionOptions"/>
    /// applied to the C#→Calor path (ignored for Calor→C#).
    /// </summary>
    public static async Task<object> ConvertFileAsync(string filePath, string? outputPath, ConversionOptions? options)
    {
        var direction = CSharpToCalorConverter.DetectDirection(filePath);

        return direction switch
        {
            ConversionDirection.CSharpToCalor => await ConvertCSharpToCalorAsync(filePath, outputPath, options),
            ConversionDirection.CalorToCSharp => await ConvertCalorToCSharpAsync(filePath, outputPath),
            _ => throw new ArgumentException($"Unknown file type: {filePath}")
        };
    }

    /// <summary>
    /// Converts C# to Calor.
    /// </summary>
    public static Task<ConversionResult> ConvertCSharpToCalorAsync(string csharpPath, string? outputPath = null)
        => ConvertCSharpToCalorAsync(csharpPath, outputPath, options: null);

    /// <summary>
    /// Converts C# to Calor with optional <see cref="ConversionOptions"/> (e.g.
    /// <c>UseImplicitCallCloser = false</c> for v0.6.0-compatible output).
    /// </summary>
    public static async Task<ConversionResult> ConvertCSharpToCalorAsync(string csharpPath, string? outputPath, ConversionOptions? options)
    {
        var converter = options != null ? new CSharpToCalorConverter(options) : new CSharpToCalorConverter();
        var result = await converter.ConvertFileAsync(csharpPath);

        if (result.Success && result.CalorSource != null)
        {
            var calorPath = outputPath ?? Path.ChangeExtension(csharpPath, ".calr");
            await File.WriteAllTextAsync(calorPath, result.CalorSource);
        }

        return result;
    }

    /// <summary>
    /// Converts Calor to C# using the existing compiler.
    /// </summary>
    public static async Task<CompilationResult> ConvertCalorToCSharpAsync(string calorPath, string? outputPath = null)
    {
        var source = await File.ReadAllTextAsync(calorPath);
        var result = Program.Compile(source, calorPath);

        if (!result.HasErrors && !string.IsNullOrEmpty(result.GeneratedCode))
        {
            var csPath = outputPath ?? Path.ChangeExtension(calorPath, ".g.cs");
            await File.WriteAllTextAsync(csPath, result.GeneratedCode);
        }

        return result;
    }
}
