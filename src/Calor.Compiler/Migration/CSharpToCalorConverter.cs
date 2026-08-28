using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Effects;

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
    public IReadOnlyList<ConversionLoss> Losses => Context.Losses;
    public ConversionMetadata Metadata => Context.Metadata;
    public int NativeConversionCount => Context.Stats.ConvertedNodes;
    public int InteropPreservationCount => Context.Losses.Count(
        loss => loss.Kind is ConversionLossKind.InteropPreserved or ConversionLossKind.EmitterFallback);
    public int LossySubstitutionCount => Context.Losses.Count(
        loss => loss.Kind is ConversionLossKind.FallbackTodo
            or ConversionLossKind.PreprocessorStripped
            or ConversionLossKind.DirectiveRemoved);
    public int DropCount => Context.Losses.Count(loss => loss.Kind == ConversionLossKind.Dropped);
}

/// <summary>
/// Fidelity contract for C# to Calor conversion.
/// </summary>
public enum ConversionFidelity
{
    /// <summary>
    /// Unsupported code must be preserved verbatim at a compilable boundary.
    /// Emitted Calor and its generated C# must validate before success is reported.
    /// </summary>
    Lossless,

    /// <summary>
    /// Explicit opt-in allowing substitutions or drops. Every loss remains recorded.
    /// </summary>
    Lossy
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
/// Controls how C# conditional-compilation branches are handled.
/// </summary>
public enum PreprocessorConversionMode
{
    /// <summary>Preserve every #if/#elif/#else branch as explicit Calor preprocessor AST.</summary>
    PreserveAllBranches,

    /// <summary>
    /// Explicitly lossy opt-in that keeps only the branch Roslyn activates for the
    /// supplied parse options and symbols.
    /// </summary>
    SelectActiveBranchLossy
}

/// <summary>
/// Options for C# to Calor conversion.
/// </summary>
public sealed class ConversionOptions
{
    /// <summary>
    /// Fidelity contract for conversion. Lossless is the safe default; lossy
    /// substitutions and drops require explicit caller opt-in.
    /// </summary>
    public ConversionFidelity Fidelity { get; set; } = ConversionFidelity.Lossless;

    /// <summary>
    /// The module name to use in the generated Calor code.
    /// If not specified, derived from the source file name.
    /// </summary>
    public string? ModuleName { get; set; }

    /// <summary>
    /// Whether to preserve XML documentation comments in the output.
    /// </summary>
    public bool PreserveDocumentationComments { get; set; } = true;

    /// <summary>
    /// Compatibility alias for <see cref="PreserveDocumentationComments"/>.
    /// This option has always applied only to XML documentation comments, not
    /// ordinary source comments.
    /// </summary>
    public bool PreserveComments
    {
        get => PreserveDocumentationComments;
        set => PreserveDocumentationComments = value;
    }

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
    /// Conditional-compilation handling. The safe default preserves every branch.
    /// </summary>
    public PreprocessorConversionMode PreprocessorMode { get; set; } =
        PreprocessorConversionMode.PreserveAllBranches;

    /// <summary>
    /// Compatibility alias for callers that explicitly set the former option.
    /// Omitted callers now get lossless preservation; <c>true</c> maps to the
    /// explicitly lossy Roslyn-selected mode.
    /// </summary>
    [Obsolete("Use PreprocessorMode. true maps to SelectActiveBranchLossy; false maps to PreserveAllBranches.")]
    public bool StripPreprocessor
    {
        get => PreprocessorMode == PreprocessorConversionMode.SelectActiveBranchLossy;
        set
        {
            PreprocessorMode = value
                ? PreprocessorConversionMode.SelectActiveBranchLossy
                : PreprocessorConversionMode.PreserveAllBranches;
            if (value)
                Fidelity = ConversionFidelity.Lossy;
        }
    }

    /// <summary>
    /// Roslyn parse options to use. When omitted, Preview language syntax is accepted.
    /// Symbols from <see cref="DefinedSymbols"/> are merged with these options.
    /// </summary>
    public CSharpParseOptions? ParseOptions { get; set; }

    /// <summary>Additional conditional-compilation symbols supplied by the caller.</summary>
    public IReadOnlyCollection<string> DefinedSymbols { get; set; } = Array.Empty<string>();

    /// <summary>Optional project configuration name recorded in conversion metadata.</summary>
    public string? Configuration { get; set; }

    /// <summary>Optional target framework recorded in conversion metadata.</summary>
    public string? TargetFramework { get; set; }

    /// <summary>Output kind used for semantic and generated-C# validation.</summary>
    public OutputKind OutputKind { get; set; } = OutputKind.DynamicallyLinkedLibrary;

    /// <summary>Additional metadata references, including extern aliases.</summary>
    public IReadOnlyCollection<ConversionReference> References { get; set; } =
        Array.Empty<ConversionReference>();

    /// <summary>When true, wraps unsupported constructs in §CSHARP blocks instead of emitting broken Calor.</summary>
    public bool PassthroughOnError { get; set; } = false;

    /// <summary>
    /// When the emitted Calor PARSES but does not survive the C# round trip,
    /// preserve the offending member's original C# as a §CSHARP interop block
    /// (#717's rewrap, extended by review C1(b)) instead of letting round-trip
    /// validation fail the whole conversion — and only in a C#-preserving mode
    /// (<see cref="ConversionContext.ShouldPreserveCSharp"/>: Lossless fidelity,
    /// Interop mode, or <see cref="PassthroughOnError"/>). Setting this flag alone
    /// in a mode that does not preserve C# changes nothing: there is no original
    /// C# kept to rewrap with, so the conversion still reports the failure.
    ///
    /// <para>The axis is what the CALLER does on failure, not the mode it asked
    /// for. The CLI discards the output entirely when conversion fails, so
    /// rewrapping one member is strictly better than writing nothing, and no
    /// consumer is left holding a degraded tree — <c>ConvertCommand</c> therefore
    /// sets this true. Library callers read <see cref="ConversionResult.Ast"/>
    /// even when <see cref="ConversionResult.Success"/> is false and must keep the
    /// natively converted tree, so the default is false.</para>
    /// </summary>
    public bool RescueUnusableMembers { get; set; } = false;

    /// <summary>
    /// Whether the emitter should elide `§/C` for zero-argument calls (v0.6.1 default behaviour).
    /// Set to <c>false</c> to produce v0.6.0-compatible output that always emits explicit `§/C` closers.
    /// Default is <c>true</c> (matches <see cref="ConversionContext.UseImplicitCallCloser"/>).
    /// </summary>
    public bool UseImplicitCallCloser { get; set; } = true;

    /// <summary>
    /// Whether lossless conversion validates generated C# as a standalone unit.
    /// Project migration disables this per file and validates all generated files together.
    /// </summary>
    public bool ValidateRoundTripCSharp { get; set; } = true;
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
    public ConversionResult Convert(
        string csharpSource,
        string? sourceFile = null)
        => Convert(csharpSource, sourceFile, CancellationToken.None);

    /// <summary>
    /// Converts C# source code to Calor source code with cooperative cancellation.
    /// </summary>
    public ConversionResult Convert(
        string csharpSource,
        string? sourceFile,
        CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        var context = CreateContext(sourceFile);
        context.OriginalSource = csharpSource;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parseOptions = GetEffectiveParseOptions(sourceFile);
            var effectiveOutputKind = _options.OutputKind;
            context.Metadata = new ConversionMetadata
            {
                PreprocessorMode = _options.PreprocessorMode,
                Configuration = _options.Configuration,
                TargetFramework = _options.TargetFramework,
                LanguageVersion = parseOptions.LanguageVersion.ToString(),
                DocumentationMode = parseOptions.DocumentationMode.ToString(),
                SourceCodeKind = parseOptions.Kind.ToString(),
                DefinedSymbols = parseOptions.PreprocessorSymbolNames
                    .OrderBy(symbol => symbol, StringComparer.Ordinal)
                    .ToArray(),
                Features = parseOptions.Features
                    .OrderBy(feature => feature.Key, StringComparer.Ordinal)
                    .ToDictionary(
                        feature => feature.Key,
                        feature => feature.Value,
                        StringComparer.Ordinal),
                References = _options.References
                    .Select(reference => reference with
                    {
                        Path = Path.GetFullPath(reference.Path)
                    })
                    .ToArray(),
                OutputKind = _options.OutputKind.ToString()
            };

            if (_options.PreprocessorMode == PreprocessorConversionMode.SelectActiveBranchLossy
                && _options.Fidelity != ConversionFidelity.Lossy)
            {
                context.AddError(
                    "SelectActiveBranchLossy requires explicit lossy conversion fidelity.",
                    feature: "preprocessor-selected-branch");
                return new ConversionResult
                {
                    Success = false,
                    Context = context,
                    Duration = DateTime.UtcNow - startTime
                };
            }

            // Explicitly lossy branch selection is delegated to Roslyn so #if false,
            // defined symbols, boolean expressions, and #elif chains use compiler
            // semantics rather than source order.
            if (_options.PreprocessorMode == PreprocessorConversionMode.SelectActiveBranchLossy)
            {
                try
                {
                    var originalTree = CSharpSyntaxTree.ParseText(
                        csharpSource,
                        parseOptions,
                        cancellationToken: cancellationToken);
                    var malformedDirective = originalTree.GetDiagnostics(
                            cancellationToken)
                        .FirstOrDefault(diagnostic =>
                            diagnostic.Id is "CS1027" or "CS1028");
                    if (malformedDirective != null)
                    {
                        var span = malformedDirective.Location.GetLineSpan()
                            .StartLinePosition;
                        context.AddError(
                            $"C# parse error: {malformedDirective.GetMessage()}",
                            line: span.Line + 1,
                            column: span.Character + 1,
                            feature: "malformed-preprocessor-directive");
                        return new ConversionResult
                        {
                            Success = false,
                            Context = context,
                            Duration = DateTime.UtcNow - startTime
                        };
                    }
                    var stripResult = PreprocessorStripper.SelectActiveBranchLossy(
                        csharpSource,
                        parseOptions,
                        cancellationToken);
                    csharpSource = stripResult.Source;
                    foreach (var directive in stripResult.RemovedConditionalDirectives)
                    {
                        context.RecordLoss(ConversionLossKind.PreprocessorStripped,
                            "preprocessor-directive",
                            $"'{directive.Directive}' removed after Roslyn selected the active branch",
                            directive.Line);
                    }
                    foreach (var directive in stripResult.RemovedNonconditionalDirectives)
                    {
                        context.RecordLoss(
                            ConversionLossKind.DirectiveRemoved,
                            directive.Feature,
                            $"Inactive directive removed after Roslyn selected the active branch: '{directive.Directive}'",
                            directive.Line);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception stripEx)
                {
                    context.AddError($"Preprocessor stripping failed: {stripEx.GetType().Name}: {stripEx.Message}");
                    return new ConversionResult { Success = false, Context = context, Duration = DateTime.UtcNow - startTime };
                }
            }

            // Step 1: Parse C# with Roslyn using the caller/project configuration.
            var syntaxTree = CSharpSyntaxTree.ParseText(
                csharpSource,
                parseOptions,
                cancellationToken: cancellationToken);
            var root = syntaxTree.GetCompilationUnitRoot();
            effectiveOutputKind = parseOptions.Kind == SourceCodeKind.Script
                ? OutputKind.DynamicallyLinkedLibrary
                : root.Members.OfType<GlobalStatementSyntax>().Any()
                    ? OutputKind.ConsoleApplication
                    : _options.OutputKind;
            context.Metadata = context.Metadata with
            {
                OutputKind = effectiveOutputKind.ToString()
            };
            cancellationToken.ThrowIfCancellationRequested();
            var activeErrorDirectives = root.GetDiagnostics()
                .Where(diagnostic => diagnostic.Id == "CS1029"
                    && diagnostic.Severity == DiagnosticSeverity.Error)
                .ToList();

            // Check for parse errors. Unexpected or unmatched directives are
            // never globally suppressed; callers receive an explicit parse error.
            List<Microsoft.CodeAnalysis.Diagnostic> diagnostics;
            try
            {
                diagnostics = root.GetDiagnostics()
                    .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error
                             && d.Id != "CS1029")
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

            var namespaceFeature = GetNamespaceFeature(root);
            if (namespaceFeature != null)
                context.RecordFeatureUsage(namespaceFeature);

            if (_options.Fidelity == ConversionFidelity.Lossless
                && RequiresWholeFileNamespaceInterop(root))
            {
                return ConvertWholeFileNamespaceInterop(
                    csharpSource,
                    root,
                    context,
                    startTime,
                    parseOptions,
                    effectiveOutputKind,
                    cancellationToken);
            }

            // Step 2: Create semantic model for type inference (best-effort)
            SemanticModel? semanticModel = null;
            try
            {
                var compilation = CSharpCompilation.Create("ConversionAnalysis",
                    new[] { syntaxTree },
                    GetSemanticMetadataReferences(),
                    new CSharpCompilationOptions(effectiveOutputKind));
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
                var visitor = new RoslynSyntaxVisitor(
                    context,
                    semanticModel,
                    cancellationToken);
                calorAst = visitor.Convert(root, moduleName);
            }
            catch (OperationCanceledException)
            {
                throw;
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
            cancellationToken.ThrowIfCancellationRequested();

            // Step 3b (#717): post-conversion parse validation. If the emitted Calor
            // does not parse and we are in a C#-preserving mode (Interop /
            // PassthroughOnError), rewrap each offending top-level member as a §CSHARP
            // interop block carrying its original C#, so the output is always valid
            // Calor rather than silently-broken text.
            // Review C1(b) / N2: when RescueUnusableMembers is set, the trigger is
            // "the output is not USABLE", not merely "the output does not parse".
            // A member can emit Calor that parses and still not survive the round
            // trip (a name referenced outside the scope that declares it), in which
            // case round-trip validation below fails the whole conversion and
            // NOTHING is written - strictly worse, for a caller that discards the
            // output on failure, than preserving that one member's original C#.
            // The option is the axis rather than the fidelity/preserve mode,
            // because what distinguishes the two callers is what they DO on
            // failure: the CLI throws the result away (so it opts in), while a
            // library caller reads `Ast` even when Success is false and must keep
            // the natively converted tree (so it stays opted out by default).
            var roundTripGate = _options.ValidateRoundTripCSharp
                && (_options.Fidelity == ConversionFidelity.Lossless
                    || _options.PreprocessorMode
                        == PreprocessorConversionMode.SelectActiveBranchLossy);
            var parseFailed = !ParsesCleanly(calorSource);
            // Only rescue an otherwise-CLEAN conversion. When the context already
            // holds errors, the member-level fallbacks have already shaped the
            // output (an unsupported construct preserved as a member §CSHARP, say)
            // and re-wrapping the whole type over the top would discard that.
            var roundTripFailed = !parseFailed
                // Review m15: PassthroughOnError's documented contract is
                // literally "never hand me broken output", so it reaches the
                // rescue on its own as well. No user-visible change today (the
                // CLI sets RescueUnusableMembers, and --passthrough sets both),
                // but the contract now holds without relying on that pairing.
                && (_options.RescueUnusableMembers || _options.PassthroughOnError)
                && !context.HasErrors
                && context.ShouldPreserveCSharp
                && roundTripGate
                && !RoundTripCompiles(calorSource, context, parseOptions, effectiveOutputKind, cancellationToken);
            if (context.ShouldPreserveCSharp && (parseFailed || roundTripFailed))
            {
                var rewrapped = TryRewrapUnparseableMembers(
                    calorAst, root, context,
                    requireRoundTrip: roundTripFailed,
                    parseOptions: parseOptions,
                    outputKind: effectiveOutputKind,
                    cancellationToken: cancellationToken);
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
                if (!ParsesCleanly(calorSource)
                    || (roundTripFailed
                        && !RoundTripCompiles(calorSource, context, parseOptions, effectiveOutputKind, cancellationToken)))
                {
                    context.AddWarning(
                        "Emitted Calor does not parse (or does not survive the C# round trip) " +
                        "and could not be fully preserved as §CSHARP interop blocks; the output " +
                        "may be invalid.",
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

            // #836 M2: reconcile the emitted output with the loss ledger. The
            // CalorEmitter has internal raw-C# fallback paths (§CS{…} in chain
            // steps, §RAW) that do not thread through the ledger — without this
            // check they coexisted with a zero-loss "fully native" claim and a
            // false "✓ Conversion successful". Any raw-C# marker beyond the
            // ledgered interop preservations is counted as an EmitterFallback loss.
            ReconcileEmitterFallbacks(calorSource, context);

            if (!ParsesCleanly(calorSource))
            {
                context.AddError(
                    "Generated Calor failed mandatory parse validation.",
                    feature: "generated-calor-validation");
            }

            if (_options.ValidateRoundTripCSharp
                && (_options.Fidelity == ConversionFidelity.Lossless
                    || _options.PreprocessorMode
                        == PreprocessorConversionMode.SelectActiveBranchLossy))
            {
                ValidateLosslessRoundTrip(
                    calorSource,
                    context,
                    _options.ValidateRoundTripCSharp,
                    parseOptions,
                    effectiveOutputKind,
                    cancellationToken);
            }
            foreach (var diagnostic in activeErrorDirectives)
            {
                var span = diagnostic.Location.GetLineSpan().StartLinePosition;
                context.AddError(
                    $"Active #error directive ({diagnostic.Id}): {diagnostic.GetMessage()}",
                    line: span.Line + 1,
                    column: span.Character + 1,
                    feature: "active-error-directive");
            }

            var destructiveLosses = context.Losses.Count(loss => loss.IsSemanticLoss);
            if (_options.Fidelity == ConversionFidelity.Lossless && destructiveLosses > 0)
            {
                context.AddError(
                    $"Lossless conversion refused {destructiveLosses} lossy substitution(s) or drop(s). " +
                    "Use explicit lossy mode to acknowledge semantic loss.",
                    feature: "lossless-contract");
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
                Success = !context.HasErrors,
                CalorSource = calorSource,
                Ast = calorAst,
                Context = context,
                Duration = DateTime.UtcNow - startTime
            };
        }
        catch (OperationCanceledException)
        {
            throw;
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
    public async Task<ConversionResult> ConvertFileAsync(
        string csharpFilePath,
        CancellationToken cancellationToken = default)
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
        var source = await File.ReadAllTextAsync(csharpFilePath, encoding, cancellationToken);
        var result = Convert(source, csharpFilePath, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    /// <summary>
    /// Converts a C# file and writes the output to an Calor file.
    /// </summary>
    public async Task<ConversionResult> ConvertFileAndSaveAsync(
        string csharpFilePath,
        string? outputPath = null,
        CancellationToken cancellationToken = default)
    {
        var result = await ConvertFileAsync(csharpFilePath, cancellationToken);

        if (result.Success && result.CalorSource != null)
        {
            var calorPath = outputPath ?? Path.ChangeExtension(csharpFilePath, ".calr");
            var writeEncoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
            await ConversionFileWriter.WriteAtomicAsync(
                calorPath,
                result.CalorSource,
                writeEncoding,
                cancellationToken);
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
            ".csx" => ConversionDirection.CSharpToCalor,
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

    /// <summary>
    /// #836 M2: counts raw-C# markers (§CSHARP{, §CS{, §RAW) in the emitted
    /// Calor and records an <see cref="ConversionLossKind.EmitterFallback"/>
    /// loss for every marker beyond the ledgered interop preservations, so the
    /// "zero losses = fully native output" invariant holds even for fallbacks
    /// produced inside the CalorEmitter (which cannot reach the ledger yet).
    /// </summary>
    private static void ReconcileEmitterFallbacks(string calorSource, ConversionContext context)
    {
        var markerLines = FindFallbackTokenLines(calorSource);
        var ledgered = context.Losses.Count(loss =>
            loss.Kind is ConversionLossKind.InteropPreserved or ConversionLossKind.EmitterFallback);
        foreach (var line in markerLines.Skip(ledgered))
        {
            context.RecordLoss(ConversionLossKind.EmitterFallback, "emitter-fallback",
                "Raw C# fallback (§CS{…}/§RAW/§CSHARP) present in the emitted Calor without a ledger entry — " +
                "produced by an emitter-internal fallback path; location is in the generated Calor",
                line);
        }
    }

    internal static bool RequiresWholeFileNamespaceInterop(CompilationUnitSyntax root)
    {
        var namespaceDeclarations = root.DescendantNodes()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .ToList();
        if (namespaceDeclarations.Count > 1)
            return true;

        return namespaceDeclarations.Count == 1
               && root.Members.Any(member =>
                   member is not BaseNamespaceDeclarationSyntax
                   && member is BaseTypeDeclarationSyntax
                       or DelegateDeclarationSyntax
                       or GlobalStatementSyntax);
    }

    internal static string? GetNamespaceFeature(CompilationUnitSyntax root)
    {
        if (!root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>().Any())
            return null;

        return RequiresWholeFileNamespaceInterop(root)
            ? "namespace-topology"
            : "namespace-single-scope";
    }

    private ConversionResult ConvertWholeFileNamespaceInterop(
            string csharpSource,
            CompilationUnitSyntax root,
            ConversionContext context,
            DateTime startTime,
            CSharpParseOptions parseOptions,
            OutputKind outputKind,
            CancellationToken cancellationToken)
        {
            var interop = new CSharpInteropBlockNode(
                new Parsing.TextSpan(
                    0,
                    csharpSource.Length,
                    1,
                    1),
                csharpSource,
                featureName: "namespace-topology",
                reason:
                    "Multiple lexical namespace scopes are preserved as one whole-file interop boundary in lossless mode.")
            {
                NamespaceIdentity = "",
                NamespaceScopeId = "",
                FullyQualifiedSymbolIdentity = "global::<compilation-unit>"
            };
            context.RecordLoss(
                ConversionLossKind.InteropPreserved,
                "namespace-topology",
                "Whole C# file preserved because lossless conversion does not split multi-namespace topology.",
                line: 1);
            context.Stats.InteropBlocksEmitted++;
            context.AddInfo(
                "Multiple namespace scopes preserved as whole-file C# interop in lossless mode.",
                feature: "namespace-topology",
                line: 1);

            var module = new ModuleNode(
                GetRootSpan(root),
                "m001",
                "_global",
                Array.Empty<UsingDirectiveNode>(),
                Array.Empty<InterfaceDefinitionNode>(),
                Array.Empty<ClassDefinitionNode>(),
                Array.Empty<EnumDefinitionNode>(),
                Array.Empty<EnumExtensionNode>(),
                Array.Empty<DelegateDefinitionNode>(),
                Array.Empty<FunctionNode>(),
                new AttributeCollection(),
                Array.Empty<IssueNode>(),
                Array.Empty<AssumeNode>(),
                Array.Empty<InvariantNode>(),
                Array.Empty<DecisionNode>(),
                null,
                [interop]);
            var calorSource = new CalorEmitter(context).Emit(module);
            if (!ParsesCleanly(calorSource))
            {
                context.AddError(
                    "Whole-file namespace interop failed generated Calor validation.",
                    feature: "namespace-topology");
            }
            ValidateLosslessRoundTrip(
                calorSource,
                context,
                _options.ValidateRoundTripCSharp,
                parseOptions,
                outputKind,
                cancellationToken);
            return new ConversionResult
            {
                Success = !context.HasErrors,
                CalorSource = calorSource,
                Ast = module,
                Context = context,
                Duration = DateTime.UtcNow - startTime
            };
        }

    private static Parsing.TextSpan GetRootSpan(CompilationUnitSyntax root)
        {
            var lineSpan = root.GetLocation().GetLineSpan();
            return new Parsing.TextSpan(
                root.SpanStart,
                root.Span.Length,
                lineSpan.StartLinePosition.Line + 1,
                lineSpan.StartLinePosition.Character + 1);
    }

    private static List<int> FindFallbackTokenLines(string text)
    {
        var diagnostics = new Diagnostics.DiagnosticBag();
        var tokens = new Parsing.Lexer(text, diagnostics).TokenizeAllForParser();
        if (diagnostics.HasErrors)
        {
            return [];
        }

        return tokens
            .Where(token => token.Kind is
                Parsing.TokenKind.RawCSharp or
                Parsing.TokenKind.RawCSharpExpression or
                Parsing.TokenKind.CSharpInterop)
            .Select(token => token.Span.Line)
            .OrderBy(line => line)
            .ToList();
    }

    /// <summary>True if <paramref name="calorSource"/> lexes and parses without errors.</summary>
    private bool ParsesCleanly(string calorSource)
    {
        if (ParseValidatorOverride != null)
        {
            return ParseValidatorOverride(calorSource);
        }

        var diagnostics = new Diagnostics.DiagnosticBag();
        var tokens = new Parsing.Lexer(
            calorSource,
            diagnostics).TokenizeAllForParser();
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
        ModuleNode module,
        CompilationUnitSyntax root,
        ConversionContext context,
        bool requireRoundTrip = false,
        CSharpParseOptions? parseOptions = null,
        OutputKind outputKind = OutputKind.DynamicallyLinkedLibrary,
        CancellationToken cancellationToken = default)
    {
        // requireRoundTrip (review C1(b)): a member is "failed" when its emitted
        // Calor does not parse OR - when the whole output's round trip failed -
        // when the member alone does not survive the round trip.
        bool MemberFailed(
            IReadOnlyList<ClassDefinitionNode>? classes = null,
            IReadOnlyList<InterfaceDefinitionNode>? interfaces = null,
            IReadOnlyList<EnumDefinitionNode>? enums = null,
            IReadOnlyList<DelegateDefinitionNode>? delegates = null)
            => !MemberParsesCleanly(module, classes, interfaces, enums, delegates)
               || (requireRoundTrip
                   && !MemberRoundTripsCleanly(
                       module, context, parseOptions, outputKind, cancellationToken,
                       classes, interfaces, enums, delegates));

        var sources = CollectTopLevelTypeSources(root);

        var failedClasses = new List<ClassDefinitionNode>();
        var failedInterfaces = new List<InterfaceDefinitionNode>();
        var failedEnums = new List<EnumDefinitionNode>();
        var failedDelegates = new List<DelegateDefinitionNode>();
        var interops = new List<CSharpInteropBlockNode>();
        var replacements = new Dictionary<AstNode, CSharpInteropBlockNode>();

        foreach (var cls in module.Classes)
        {
            if (MemberFailed(classes: new[] { cls }) &&
                TryTakeSource(sources, "class", GetSymbolIdentity(cls, module), out var csharp))
            {
                var interop = MakeFallbackInterop(csharp, cls);
                interops.Add(interop);
                replacements[cls] = interop;
                failedClasses.Add(cls);
            }
        }

        foreach (var iface in module.Interfaces)
        {
            if (MemberFailed(interfaces: new[] { iface }) &&
                TryTakeSource(sources, "interface", GetSymbolIdentity(iface, module), out var csharp))
            {
                var interop = MakeFallbackInterop(csharp, iface);
                interops.Add(interop);
                replacements[iface] = interop;
                failedInterfaces.Add(iface);
            }
        }

        foreach (var en in module.Enums)
        {
            if (MemberFailed(enums: new[] { en }) &&
                TryTakeSource(sources, "enum", GetSymbolIdentity(en, module), out var csharp))
            {
                var interop = MakeFallbackInterop(csharp, en);
                interops.Add(interop);
                replacements[en] = interop;
                failedEnums.Add(en);
            }
        }

        foreach (var del in module.Delegates)
        {
            if (MemberFailed(delegates: new[] { del }) &&
                TryTakeSource(sources, "delegate", GetSymbolIdentity(del, module), out var csharp))
            {
                var interop = MakeFallbackInterop(csharp, del);
                interops.Add(interop);
                replacements[del] = interop;
                failedDelegates.Add(del);
            }
        }

        if (interops.Count == 0)
        {
            return null;
        }

        context.Stats.InteropBlocksEmitted += interops.Count;
        context.Stats.FallbackInteropBlocksEmitted += interops.Count;
        foreach (var interop in interops)
        {
            context.RecordLoss(ConversionLossKind.InteropPreserved, "post-validation-fallback",
                interop.Reason ?? "Member re-preserved as §CSHARP after emitted Calor failed to parse (#717)");
        }
        var items = module.Items
            .Select(item => replacements.TryGetValue(item, out var replacement)
                ? (AstNode)replacement
                : item)
            .ToList();

        return module.With(update =>
        {
            update.Interfaces = module.Interfaces
                .Where(iface => !failedInterfaces.Contains(iface))
                .ToList();
            update.Classes = module.Classes
                .Where(cls => !failedClasses.Contains(cls))
                .ToList();
            update.Enums = module.Enums
                .Where(enumDefinition => !failedEnums.Contains(enumDefinition))
                .ToList();
            update.Delegates = module.Delegates
                .Where(delegateDefinition => !failedDelegates.Contains(delegateDefinition))
                .ToList();
            update.InteropBlocks = module.InteropBlocks.Concat(interops).ToList();
            update.Items = items;
        });
    }

    /// <summary>Emits a module containing only the given member(s) and reports whether
    /// that emission parses — used to isolate which top-level member is unparseable.</summary>
    private bool MemberParsesCleanly(
        ModuleNode module,
        IReadOnlyList<ClassDefinitionNode>? classes = null,
        IReadOnlyList<InterfaceDefinitionNode>? interfaces = null,
        IReadOnlyList<EnumDefinitionNode>? enums = null,
        IReadOnlyList<DelegateDefinitionNode>? delegates = null)
        => MemberParsesCleanly(module, out _, classes, interfaces, enums, delegates);

    /// <summary>
    /// As above, also handing back the emitted Calor so a caller can put the
    /// member through a further check without constructing a second solo module
    /// (this is the one module-construction site the architecture test allows).
    /// </summary>
    private bool MemberParsesCleanly(
        ModuleNode module,
        out string emitted,
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
            Array.Empty<InvariantNode>(), Array.Empty<DecisionNode>(), null,
            namespaceScopes: module.NamespaceScopes);

        // Fresh context so the probe emission does not perturb the real conversion's stats.
        emitted = new CalorEmitter(CreateContext(null)).Emit(solo);
        return ParsesCleanly(emitted);
    }

    /// <summary>
    /// Review C1(b): true when <paramref name="calorSource"/> compiles to C# that
    /// itself compiles - the same check <see cref="ValidateLosslessRoundTrip"/>
    /// performs, but as a predicate that records no diagnostics, so it can gate
    /// the #717 rewrap before the real validation runs.
    /// </summary>
    private static bool RoundTripCompiles(
        string calorSource,
        ConversionContext context,
        CSharpParseOptions parseOptions,
        OutputKind outputKind,
        CancellationToken cancellationToken)
    {
        try
        {
            var compileResult = global::Calor.Compiler.Program.Compile(
                calorSource,
                context.SourceFile ?? "converted-output.calr",
                new CompilationOptions
                {
                    EnforceEffects = false,
                    UnknownCallPolicy = UnknownCallPolicy.Permissive,
                    DeferGeneratedOutputValidation = true,
                    CancellationToken = cancellationToken
                });
            if (compileResult.HasErrors)
                return false;

            var validation = GeneratedCSharpCompiler.Validate(
                [new GeneratedCSharpSource(
                    compileResult.GeneratedCode,
                    context.SourceFile ?? "converted-output.g.cs")],
                new GeneratedCSharpCompilationContext
                {
                    LanguageVersion = parseOptions.LanguageVersion,
                    DocumentationMode = parseOptions.DocumentationMode,
                    SourceCodeKind = parseOptions.Kind,
                    Features = parseOptions.Features,
                    PreprocessorSymbols = parseOptions.PreprocessorSymbolNames,
                    References = context.Metadata.References.Select(
                        reference => new GeneratedCSharpReference(
                            reference.Path,
                            reference.Aliases)),
                    OutputKind = outputKind
                });
            return validation.SyntaxErrors.Count == 0 && validation.CompilationErrors.Count == 0;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// The <see cref="MemberParsesCleanly"/> sibling for review C1(b): emits the
    /// member alone (through that method, so the solo module is built in exactly
    /// one place) and asks whether that Calor survives the C# round trip.
    /// </summary>
    private bool MemberRoundTripsCleanly(
        ModuleNode module,
        ConversionContext context,
        CSharpParseOptions? parseOptions,
        OutputKind outputKind,
        CancellationToken cancellationToken,
        IReadOnlyList<ClassDefinitionNode>? classes = null,
        IReadOnlyList<InterfaceDefinitionNode>? interfaces = null,
        IReadOnlyList<EnumDefinitionNode>? enums = null,
        IReadOnlyList<DelegateDefinitionNode>? delegates = null)
    {
        if (parseOptions == null)
            return true;

        return MemberParsesCleanly(module, out var emitted, classes, interfaces, enums, delegates)
            && RoundTripCompiles(emitted, context, parseOptions, outputKind, cancellationToken);
    }

    private static CSharpInteropBlockNode MakeFallbackInterop(
        string csharpSource,
        TypeDefinitionNode member)
        => member.CopyMetadataTo(new CSharpInteropBlockNode(
            Parsing.TextSpan.Empty,
            csharpSource,
            featureName: "post-validation-fallback",
            reason: $"Converted Calor for '{member.FullyQualifiedSymbolIdentity ?? member.Name}' did not parse or did not survive the C# round trip; original C# preserved (#717)."));

    private sealed record TypeSource(bool IsPartial, string Text);

    /// <summary>
    /// Collects top-level type declarations by kind and fully-qualified symbol
    /// identity. Several entries for one identity are legitimate partial
    /// declarations; same-named types in other namespaces have different keys.
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
            var identity = GetSyntaxTypeIdentity(member, name);
            var key = $"{kind}/{identity}";
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
    /// Safe cases: exactly one declaration with that identity, or several that
    /// are all <c>partial</c> (one symbol — concatenate). Entries are removed on
    /// take so a second failure cannot reuse them.
    /// </summary>
    private static bool TryTakeSource(
        Dictionary<string, List<TypeSource>> sources,
        string kind,
        string symbolIdentity,
        out string csharp)
    {
        csharp = "";
        var key = $"{kind}/{symbolIdentity}";
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

    private static string GetSymbolIdentity(
        TypeDefinitionNode member,
        ModuleNode module)
    {
        if (!string.IsNullOrEmpty(member.FullyQualifiedSymbolIdentity))
            return member.FullyQualifiedSymbolIdentity;

        var namespaceIdentity = member.NamespaceIdentity ?? module.Name;
        var arity = member switch
        {
            ClassDefinitionNode cls => cls.TypeParameters.Count,
            InterfaceDefinitionNode iface => iface.TypeParameters.Count,
            _ => 0
        };
        return $"global::{(string.IsNullOrEmpty(namespaceIdentity) ? "" : namespaceIdentity + ".")}" +
               member.Name +
               (arity > 0 ? $"`{arity}" : "");
    }

    private static string GetSyntaxTypeIdentity(
        MemberDeclarationSyntax member,
        string name)
    {
        var namespaceIdentity = string.Join(
            ".",
            member.Ancestors()
                .OfType<BaseNamespaceDeclarationSyntax>()
                .Select(item => item.Name.ToString().Replace("@", ""))
                .Reverse());
        var arity = member switch
        {
            TypeDeclarationSyntax type => type.TypeParameterList?.Parameters.Count ?? 0,
            DelegateDeclarationSyntax @delegate =>
                @delegate.TypeParameterList?.Parameters.Count ?? 0,
            _ => 0
        };
        return $"global::{(string.IsNullOrEmpty(namespaceIdentity) ? "" : namespaceIdentity + ".")}" +
               name +
               (arity > 0 ? $"`{arity}" : "");
    }

    private ConversionContext CreateContext(string? sourceFile)
    {
        return new ConversionContext
        {
            SourceFile = sourceFile,
            Verbose = _options.Verbose,
            IncludeBenchmark = _options.IncludeBenchmark,
            PreserveDocumentationComments = _options.PreserveDocumentationComments,
            AutoGenerateIds = _options.AutoGenerateIds,
            ModuleName = _options.ModuleName,
            GracefulFallback = _options.GracefulFallback,
            Fidelity = _options.Fidelity,
            Mode = _options.Mode,
            PassthroughOnError = _options.PassthroughOnError,
            UseImplicitCallCloser = _options.UseImplicitCallCloser
        };
    }

    private CSharpParseOptions GetEffectiveParseOptions(string? sourceFile)
    {
        var parseOptions = _options.ParseOptions
            ?? new CSharpParseOptions(
                LanguageVersion.Preview,
                kind: string.Equals(
                    Path.GetExtension(sourceFile),
                    ".csx",
                    StringComparison.OrdinalIgnoreCase)
                    ? SourceCodeKind.Script
                    : SourceCodeKind.Regular);
        if (string.Equals(
                Path.GetExtension(sourceFile),
                ".csx",
                StringComparison.OrdinalIgnoreCase))
        {
            parseOptions = parseOptions.WithKind(SourceCodeKind.Script);
        }
        var symbols = parseOptions.PreprocessorSymbolNames
            .Concat(_options.DefinedSymbols ?? Array.Empty<string>())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return parseOptions.WithPreprocessorSymbols(symbols);
    }

    private static void ValidateLosslessRoundTrip(
        string calorSource,
        ConversionContext context,
        bool validateGeneratedCSharp,
        CSharpParseOptions parseOptions,
        OutputKind outputKind,
        CancellationToken cancellationToken)
    {
        CompilationResult compileResult;
        if (!validateGeneratedCSharp)
        {
            return;
        }

        try
        {
            compileResult = global::Calor.Compiler.Program.Compile(
                calorSource,
                context.SourceFile ?? "converted-output.calr",
                new CompilationOptions
                {
                    EnforceEffects = false,
                    UnknownCallPolicy = UnknownCallPolicy.Permissive,
                    DeferGeneratedOutputValidation = true,
                    CancellationToken = cancellationToken
                });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            context.AddError(
                $"Generated Calor validation crashed: {ex.GetType().Name}: {ex.Message}",
                feature: "roundtrip-calor-validation");
            return;
        }

        foreach (var diagnostic in compileResult.Diagnostics.Errors)
        {
            context.AddError(
                $"Generated Calor failed compilation: {diagnostic.Message}",
                line: diagnostic.Span.Line,
                column: diagnostic.Span.Column,
                feature: "roundtrip-calor-validation");
        }

        if (compileResult.HasErrors)
        {
            return;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var validation = GeneratedCSharpCompiler.Validate(
                [new GeneratedCSharpSource(
                    compileResult.GeneratedCode,
                    context.SourceFile ?? "converted-output.g.cs")],
                new GeneratedCSharpCompilationContext
                {
                    LanguageVersion = parseOptions.LanguageVersion,
                    DocumentationMode = parseOptions.DocumentationMode,
                    SourceCodeKind = parseOptions.Kind,
                    Features = parseOptions.Features,
                    PreprocessorSymbols = parseOptions.PreprocessorSymbolNames,
                    References = context.Metadata.References.Select(
                        reference => new GeneratedCSharpReference(
                            reference.Path,
                            reference.Aliases)),
                    OutputKind = outputKind
                });
            foreach (var diagnostic in validation.SyntaxErrors.Concat(validation.CompilationErrors))
            {
                var span = diagnostic.Location.GetLineSpan().StartLinePosition;
                context.AddError(
                    $"Round-tripped C# failed compilation ({diagnostic.Id}): {diagnostic.GetMessage()}",
                    line: span.Line + 1,
                    column: span.Character + 1,
                    feature: "roundtrip-csharp-validation");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            context.AddError(
                $"Round-tripped C# validation crashed: {ex.GetType().Name}: {ex.Message}",
                feature: "roundtrip-csharp-validation");
        }
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

    private MetadataReference[] GetSemanticMetadataReferences()
        => GetBasicMetadataReferences()
            .Concat(_options.References
                .Where(reference => File.Exists(reference.Path))
                .Select(reference => MetadataReference.CreateFromFile(
                    Path.GetFullPath(reference.Path),
                    reference.Aliases.Count == 0
                        ? MetadataReferenceProperties.Assembly
                        : MetadataReferenceProperties.Assembly.WithAliases(
                            reference.Aliases.ToImmutableArray()))))
            .ToArray();

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

internal static class ConversionFileWriter
{
    public static void WriteAtomic(
        string path,
        string content,
        System.Text.Encoding? encoding = null)
    {
        WriteAtomicAsync(path, content, encoding).GetAwaiter().GetResult();
    }

    public static async Task WriteAtomicAsync(
        string path,
        string content,
        System.Text.Encoding? encoding = null,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException($"Output path has no directory: {path}");
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        UnixFileMode? existingMode = null;
        if (!OperatingSystem.IsWindows() && File.Exists(fullPath))
        {
            existingMode = File.GetUnixFileMode(fullPath);
        }

        try
        {
            await File.WriteAllTextAsync(
                tempPath,
                content,
                encoding ?? new System.Text.UTF8Encoding(false),
                cancellationToken);
            if (!OperatingSystem.IsWindows() && existingMode.HasValue)
            {
                File.SetUnixFileMode(tempPath, existingMode.Value);
            }
            File.Move(tempPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
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
            await ConversionFileWriter.WriteAtomicAsync(calorPath, result.CalorSource);
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
            await ConversionFileWriter.WriteAtomicAsync(csPath, result.GeneratedCode);
        }

        return result;
    }
}
