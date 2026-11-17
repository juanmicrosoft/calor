using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Analysis;
using Calor.Compiler.Effects;
using Calor.Compiler.Effects.Manifests;

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

    /// <summary>#1144: the paths this conversion took (native, interop, rescue, passthrough, lossy, refused).</summary>
    public ConversionPathSummary Paths => ConversionPathSummary.From(this);
}

/// <summary>One output of an explicitly multi-file conversion, in source namespace order.</summary>
public sealed record ConversionOutput(string FileName, ConversionResult Conversion);

/// <summary>
/// Project conversion outputs. Unsupported topologies retain the ordinary single-file result;
/// a failed split never exposes a partially converted output set.
/// </summary>
public sealed record ProjectConversionResult(IReadOnlyList<ConversionOutput> Outputs)
{
    public bool Success => Outputs.Count > 0 && Outputs.All(output => output.Conversion.Success);
    public bool IsSplit => Outputs.Count > 1;
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
    /// Lossy interop lowering retains a uniform annotation-disabled source context,
    /// but does not certify effects or flatten lexical nullable directives.
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
    /// Writer for <see cref="Verbose"/> progress lines (node and member counts).
    /// Null (the default) means standard output. Machine-readable CLI modes
    /// (<c>calor convert --format json</c>) set this to standard error so stdout
    /// carries only the envelope document.
    /// </summary>
    public TextWriter? StatusWriter { get; set; }

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

    /// <summary>
    /// Apply the System-only project import to original and generated C# equally.
    /// False selects standalone validation; preserved source is never rewritten.
    /// </summary>
    public bool IncludeImplicitSystemUsing { get; set; }

    /// <summary>
    /// Evaluated project nullable context used by semantic conversion. Per-file
    /// <c>#nullable</c> directives continue to override this compilation default.
    /// </summary>
    public NullableContextOptions? NullableContextOptions { get; set; }

    /// <summary>Additional metadata references, including extern aliases.</summary>
    public IReadOnlyCollection<ConversionReference> References { get; set; } =
        Array.Empty<ConversionReference>();

    /// <summary>
    /// Whether <see cref="References"/> is the evaluated compilation's complete
    /// reference set. Project migration sets this to avoid mixing reference
    /// assemblies with the converter host's implementation assemblies.
    /// </summary>
    public bool ReferencesAreComplete { get; set; }

    /// <summary>
    /// Other syntax trees from the evaluated project compilation. These let
    /// per-file conversion resolve symbols declared in sibling source files.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyCollection<SyntaxTree> AdditionalSemanticSyntaxTrees { get; set; } =
        Array.Empty<SyntaxTree>();

    /// <summary>When true, wraps unsupported constructs in §CSHARP blocks instead of emitting broken Calor.</summary>
    public bool PassthroughOnError { get; set; } = false;

    /// <summary>
    /// Explicit rescue preference, recorded separately from fidelity.
    /// Lossless conversion already rescues unusable output at member, type,
    /// and whole-file boundaries without requiring this flag.
    /// Historical lossy producers may request rescue in C#-preserving modes.
    /// </summary>
    public bool RescueUnusableMembers { get; set; } = false;

    /// <summary>
    /// Whether lossless conversion validates generated C# as a standalone unit.
    /// Project migration disables this per file and validates all generated files together.
    /// </summary>
    public bool ValidateRoundTripCSharp { get; set; } = true;

    /// <summary>
    /// Where the project-local effect manifest (<c>.calor-effects.json</c>) lives, if
    /// there is one. #1173: the <c>§E</c> rows this conversion writes are derived from
    /// effect resolution, and the compile that later CHECKS them resolves with the
    /// project and solution manifests loaded (<c>Program.Compile</c> passes its
    /// <c>ProjectDirectory</c> straight into the effect pass). If a project manifest
    /// widens a member the embedded BCL manifests describe more narrowly, a synthesis
    /// that cannot see it writes a row missing a NAMED effect — and since the
    /// 2026-09-04 adjudication a named forbidden effect is an error even under
    /// <c>--permissive-effects</c>. Setting this makes the two sides resolve alike.
    ///
    /// <para>Null means "no project manifest", which is what every caller that does not
    /// set it gets: embedded manifests only, exactly as before.</para>
    /// </summary>
    public string? ProjectDirectory { get; set; }

    /// <summary>
    /// Where solution-level manifests (<c>.calor-effects/</c>) live, if anywhere. Same
    /// reason as <see cref="ProjectDirectory"/>.
    /// </summary>
    public string? SolutionDirectory { get; set; }

    /// <summary>
    /// #1564: the directory user-level effect manifests are read from when this
    /// conversion writes <c>§E</c> rows. Set it to make conversion hermetic: point it at
    /// an empty directory, or set it to <see cref="string.Empty"/>, and no user-level
    /// manifest applies.
    ///
    /// <para>Null (the default) resolves the directory from the environment when each
    /// conversion starts: <c>$CALOR_HOME/manifests</c> if <c>CALOR_HOME</c> is set,
    /// otherwise <c>.calor/manifests</c> under the user's home: <c>HOME</c> on Linux and
    /// macOS, and on Windows a fully qualified <c>USERPROFILE</c>, else the account's profile
    /// folder. It is never cached for the process, so changing <c>CALOR_HOME</c> or the
    /// home variable affects the next conversion. This is the
    /// same directory the CLI's compile step reads, so the rows the converter writes
    /// agree with the compile that checks them.</para>
    /// </summary>
    public string? UserManifestRoot { get; set; }
}

/// <summary>
/// Main converter that orchestrates the C# to Calor conversion pipeline.
///
/// Pipeline: C# Source → Roslyn Parse → RoslynSyntaxVisitor → Calor AST → CalorEmitter → Calor Source
/// </summary>
public sealed class CSharpToCalorConverter
{
    // Runtime assemblies are process-stable; share Roslyn's native PE metadata.
    private static readonly Lazy<MetadataReference[]> BasicMetadataReferences = new(BuildBasicMetadataReferences);
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
        => ConvertSingleFile(csharpSource, sourceFile, cancellationToken, capturePreservationContext: false);

    /// <summary>
    /// 0.30: a file that is exactly one namespace with its own imports converts through the
    /// scoped-import projection by default, as <c>calor convert</c> does (ConversionWriteTransaction).
    /// That path compiles the original and certifies that declared metadata is unchanged; when it
    /// refuses, the ordinary single-file path (preservation included) decides the file.
    /// </summary>
    private ConversionResult ConvertSingleFile(
        string csharpSource, string? sourceFile, CancellationToken cancellationToken, bool capturePreservationContext)
    {
        if (IsSingleScopedNamespace(csharpSource, sourceFile)
            && ConvertProjectCore(csharpSource, sourceFile, cancellationToken, capturePreservationContext)
                .Outputs.Single().Conversion is { Success: true } scoped)
            return scoped;
        // The scoped path refuses an original it cannot compile (for example standalone code that
        // relies on an implicit System import); the ordinary single-file path decides that file.
        return ConvertWithContext(csharpSource, sourceFile, cancellationToken, capturePreservationContext);
    }

    /// <summary>
    /// Explicitly allows native scoped imports and separate files for distinct top-level namespace blocks.
    /// Each visitor uses the complete original syntax tree and project semantic context.
    /// Other topologies and non-native conversions retain the single-result fidelity contract.
    /// </summary>
    public ProjectConversionResult ConvertProject(
        string csharpSource, string? sourceFile = null, CancellationToken cancellationToken = default)
        => ConvertProjectCore(csharpSource, sourceFile, cancellationToken, capturePreservationContext: false);

    internal ProjectConversionResult ConvertProjectCore(
        string csharpSource, string? sourceFile, CancellationToken cancellationToken, bool capturePreservationContext)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var parseOptions = GetEffectiveParseOptions(sourceFile);
        var tree = CSharpSyntaxTree.ParseText(
            csharpSource, parseOptions, sourceFile ?? "", cancellationToken: cancellationToken);
        var root = tree.GetCompilationUnitRoot(cancellationToken);
        var namespaces = GetSplittableNamespaces(root);
        var scopedImports = CanConvertScopedNamespaceImports(root);
        if (namespaces.Count == 0 && !scopedImports)
            return Single();
        if (_options.Fidelity != ConversionFidelity.Lossless
            || _options.PreprocessorMode != PreprocessorConversionMode.PreserveAllBranches)
            return Single("Native namespace splitting requires lossless, all-branches conversion.");
        var missingReferences = _options.References.Where(reference => !File.Exists(reference.Path)).ToArray();
        if (missingReferences.Length > 0)
        {
            var context = CreateContext(sourceFile);
            foreach (var reference in missingReferences)
                context.AddError($"Namespace splitting requires the supplied reference: {reference.Path}",
                    "namespace-split-source-validation");
            return new([new(Path.ChangeExtension(Path.GetFileName(sourceFile ?? "input.cs"), ".calr"),
                new ConversionResult { Success = false, Context = context })]);
        }

        // A missing sibling/reference is not authority to guess a selected source symbol.
        var original = CSharpCompilation.Create("NamespaceSplitAnalysis",
            GeneratedCSharpCompiler.AddImplicitUsings(
                new[] { tree }.Concat(_options.AdditionalSemanticSyntaxTrees
                    .Where(sibling => !IsSameSyntaxTree(sibling, tree))),
                _options.IncludeImplicitSystemUsing),
            GetSemanticMetadataReferences(),
            new CSharpCompilationOptions(_options.OutputKind, allowUnsafe: true)
                .WithNullableContextOptions(_options.NullableContextOptions ?? NullableContextOptions.Disable));
        var originalErrors = original.GetDiagnostics(cancellationToken)
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        if (originalErrors.Length > 0)
        {
            var context = CreateContext(sourceFile);
            foreach (var error in originalErrors)
                context.AddError($"Original C# cannot be split ({error.Id}): {error.GetMessage()}",
                    "namespace-split-source-validation");
            return new([new(Path.ChangeExtension(Path.GetFileName(sourceFile ?? "input.cs"), ".calr"),
                new ConversionResult { Success = false, Context = context })]);
        }

        var outputs = new List<ConversionOutput>();
        var originalSemanticModel = original.GetSemanticModel(tree);
        for (var index = 0; index < (scopedImports ? 1 : namespaces.Count); index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = ConvertCore(csharpSource, sourceFile, cancellationToken,
                capturePreservationContext, scopedImports ? null : namespaces[index].SpanStart,
                originalSemanticModel, allowScopedNamespaceImports: scopedImports);
            if (scopedImports)
                result = TryConvertObliviousModule(csharpSource, sourceFile, result,
                    cancellationToken, capturePreservationContext, allowScopedNamespaceImports: true);
            if (!result.Success || result.Losses.Count != 0)
                return Single("A namespace did not convert entirely natively.", result);
            outputs.Add(new(scopedImports
                ? Path.ChangeExtension(Path.GetFileName(sourceFile ?? "input.cs"), ".calr")
                : GetNamespaceOutputPath(Path.GetFileName(sourceFile ?? "input.cs"), index), result));
        }

        var generated = new List<GeneratedCSharpSource>();
        var modules = new List<ModuleNode>();
        foreach (var output in outputs)
        {
            if (output.Conversion.CalorSource is not { } source || TryParseModule(source) is not { } module)
                return Single("An emitted namespace module could not be parsed.");
            modules.Add(module);
        }
        var registry = CrossModuleTypeRegistry.Build(modules);
        foreach (var output in outputs)
        {
            var compiled = Program.Compile(output.Conversion.CalorSource!, null, new CompilationOptions
            {
                CrossModuleTypes = registry,
                ProjectDirectory = _options.ProjectDirectory,
                IncludeImplicitSystemUsing = _options.IncludeImplicitSystemUsing,
                DeferGeneratedOutputValidation = true,
                CancellationToken = cancellationToken
            });
            if (compiled.HasErrors)
                return Single("Native split Calor failed compilation: "
                    + string.Join("; ", compiled.Diagnostics.Errors.Select(error => error.Message)));
            generated.Add(new(compiled.GeneratedCode, output.FileName));
        }
        cancellationToken.ThrowIfCancellationRequested();
        var validation = GeneratedCSharpCompiler.Validate(generated, new GeneratedCSharpCompilationContext
        {
            IncludeImplicitSystemUsing = _options.IncludeImplicitSystemUsing,
            LanguageVersion = parseOptions.LanguageVersion,
            DocumentationMode = parseOptions.DocumentationMode,
            Features = parseOptions.Features,
            PreprocessorSymbols = parseOptions.PreprocessorSymbolNames,
            References = _options.References.Select(reference =>
                new GeneratedCSharpReference(reference.Path, reference.Aliases)),
            AdditionalSources = _options.AdditionalSemanticSyntaxTrees
                .Where(sibling => !IsSameSyntaxTree(sibling, tree))
                .Select(sibling => new GeneratedCSharpSource(
                    sibling.GetText(cancellationToken).ToString(), sibling.FilePath)),
            OutputKind = _options.OutputKind,
            NullableContextOptions = _options.NullableContextOptions ?? NullableContextOptions.Disable
        });
        cancellationToken.ThrowIfCancellationRequested();
        if (!validation.CompilationSuccess)
            return Single("The complete split output set failed C# validation: "
                + string.Join("; ", validation.CompilationErrors.Select(error => error.GetMessage())));
        if (scopedImports)
        {
            var generatedTree = CSharpSyntaxTree.ParseText(
                generated.Single().Text, parseOptions, cancellationToken: cancellationToken);
            var generatedCompilation = original.ReplaceSyntaxTree(tree, generatedTree);
            var before = DeclaredTypeMetadata(original.GetSemanticModel(tree), root);
            var after = DeclaredTypeMetadata(generatedCompilation.GetSemanticModel(generatedTree),
                generatedTree.GetCompilationUnitRoot(cancellationToken));
            if (!before.SequenceEqual(after, StringComparer.Ordinal))
                return Single("Native scoped imports changed declared type/nullability metadata. Original: "
                    + string.Join("; ", before.Except(after, StringComparer.Ordinal))
                    + ". Generated: " + string.Join("; ", after.Except(before, StringComparer.Ordinal)));
        }
        foreach (var output in outputs)
            output.Conversion.Context.RoundTrip = ConversionRoundTrip.Verified;
        return new(outputs);

        ProjectConversionResult Single(string? reason = null, ConversionResult? rejectedCandidate = null)
        {
            var result = ConvertWithContext(csharpSource, sourceFile, cancellationToken, capturePreservationContext);
            if (reason != null)
                result.Context.AddWarning(reason + " Retaining the single-file fidelity boundary.", "namespace-split-refused");
            if (rejectedCandidate != null)
                foreach (var issue in rejectedCandidate.Issues
                    .DistinctBy(issue => (issue.Severity, issue.Message, issue.Feature,
                        issue.Line, issue.Column, issue.Suggestion))
                    .Where(issue => !result.Issues.Any(existing =>
                        existing.Severity == issue.Severity && existing.Message == issue.Message
                        && existing.Feature == issue.Feature && existing.Line == issue.Line
                        && existing.Column == issue.Column && existing.Suggestion == issue.Suggestion)))
                    result.Context.AddWarning(
                        $"Rejected native namespace candidate ({issue.Severity}): {issue.Message}",
                        issue.Feature, issue.Line, issue.Column, issue.Suggestion);
            return new([new(Path.ChangeExtension(Path.GetFileName(sourceFile ?? "input.cs"), ".calr"), result)]);
        }
    }

    internal static IReadOnlyList<NamespaceDeclarationSyntax> GetSplittableNamespaces(CompilationUnitSyntax root)
    {
        var namespaces = root.Members.OfType<NamespaceDeclarationSyntax>().ToArray();
        if (root.SyntaxTree.Options.Kind != SourceCodeKind.Regular
            || root.ContainsDiagnostics || root.ContainsDirectives
            || root.AttributeLists.Count != 0 || root.Externs.Count != 0
            || root.Usings.Any(directive => directive.Alias != null
                || !directive.GlobalKeyword.IsKind(SyntaxKind.None))
            || namespaces.Length < 2 || namespaces.Length != root.Members.Count
            || namespaces.Any(scope => scope.Usings.Count != 0 || scope.Externs.Count != 0
                || scope.Members.Count == 0
                || scope.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>().Any())
            || root.DescendantNodes().OfType<TypeDeclarationSyntax>().Any(type => type.BaseList != null)
            || root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>()
                .Any(type => type.Parent is TypeDeclarationSyntax)
            || root.DescendantNodes().OfType<EqualsValueClauseSyntax>().Any(initializer =>
                initializer.Parent is VariableDeclaratorSyntax { Parent.Parent: BaseFieldDeclarationSyntax }
                    or PropertyDeclarationSyntax
                && initializer.Value is not LiteralExpressionSyntax and not DefaultExpressionSyntax)
            || namespaces.Select(scope => string.Concat(scope.Name.DescendantTokens().Select(token => token.ValueText)))
                .Distinct(StringComparer.Ordinal).Count() != namespaces.Length)
            return [];
        return namespaces;
    }

    /// <summary>
    /// True when <paramref name="source"/> is exactly one named namespace whose
    /// local imports the scoped-import projection can carry natively.
    /// </summary>
    internal bool IsSingleScopedNamespace(string source, string? sourceFile)
    {
        var root = CSharpSyntaxTree.ParseText(source, GetEffectiveParseOptions(sourceFile), sourceFile ?? "")
            .GetCompilationUnitRoot();
        return _options.Fidelity == ConversionFidelity.Lossless
            && _options.PreprocessorMode == PreprocessorConversionMode.PreserveAllBranches
            && CanConvertScopedNamespaceImports(root);
    }

    private static bool CanConvertScopedNamespaceImports(
        CompilationUnitSyntax root, bool allowRegionDirectives = false)
        => root.SyntaxTree.Options.Kind == SourceCodeKind.Regular
           && !root.ContainsDiagnostics
           && (!root.ContainsDirectives || allowRegionDirectives
               && root.DescendantTrivia(descendIntoTrivia: true)
                   .Where(trivia => trivia.IsDirective)
                   .All(trivia => trivia.GetStructure() is
                       RegionDirectiveTriviaSyntax or EndRegionDirectiveTriviaSyntax))
           && root.AttributeLists.Count == 0 && root.Externs.Count == 0
           && root.Members.Count == 1
           && root.Members[0] is BaseNamespaceDeclarationSyntax scope
           && scope.Usings.Count > 0 && scope.Externs.Count == 0
           && !scope.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>().Any()
           && scope.Usings.All(directive => directive.UnsafeKeyword.IsKind(SyntaxKind.None)
               && directive.NamespaceOrType is NameSyntax);

    internal static string[] DeclaredTypeMetadata(SemanticModel model, CompilationUnitSyntax root)
        => root.DescendantNodes()
            .Where(node => node is MemberDeclarationSyntax or VariableDeclaratorSyntax)
            .Select(node => model.GetDeclaredSymbol(node))
            .Where(symbol => symbol is INamedTypeSymbol or IMethodSymbol or IPropertySymbol or IFieldSymbol or IEventSymbol)
            .Select(symbol => $"{symbol!.Kind} {symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)} "
                + $"{symbol.DeclaredAccessibility} static={symbol.IsStatic}: "
                + (symbol switch
                {
                    INamedTypeSymbol type => TypeParameterMetadata(type.TypeParameters)
                        + $"|{type.TypeKind}:{type.IsAbstract}:{type.IsSealed}|"
                        + (type.BaseType is { } baseType ? DeclaredTypeKey(baseType) : "")
                        + "|" + string.Join(",", type.Interfaces.Select(DeclaredTypeKey))
                        + (type.DelegateInvokeMethod is { } invoke ? MethodTypes(invoke) : ""),
                    IMethodSymbol method => MethodTypes(method),
                    IPropertySymbol property => DeclaredTypeKey(property.Type) + Parameters(property.Parameters),
                    IFieldSymbol field => DeclaredTypeKey(field.Type),
                    IEventSymbol @event => DeclaredTypeKey(@event.Type),
                    _ => ""
                }))
            .OrderBy(value => value, StringComparer.Ordinal).ToArray();

    private static string MethodTypes(IMethodSymbol method)
        => $"{method.RefKind}:{DeclaredTypeKey(method.ReturnType)}{Parameters(method.Parameters)}"
           + TypeParameterMetadata(method.TypeParameters);

    private static string TypeParameterMetadata(IEnumerable<ITypeParameterSymbol> parameters)
        => string.Join(",", parameters.Select(parameter =>
            $"{parameter.Name}:{parameter.HasReferenceTypeConstraint}:{parameter.ReferenceTypeConstraintNullableAnnotation}:"
            + string.Join(",", parameter.ConstraintTypes.Select(DeclaredTypeKey))));

    private static string Parameters(IEnumerable<IParameterSymbol> parameters)
        => "(" + string.Join(",", parameters.Select(parameter =>
            $"{parameter.RefKind}:{DeclaredTypeKey(parameter.Type)}")) + ")";

    private static string DeclaredTypeKey(ITypeSymbol type)
        => $"{type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}[{type.NullableAnnotation}]"
           + (type switch
           {
               IArrayTypeSymbol array => DeclaredTypeKey(array.ElementType),
               INamedTypeSymbol named => "<" + string.Join(",", named.TypeArguments.Select(DeclaredTypeKey)) + ">"
                   + (named.ContainingType is { } containing ? DeclaredTypeKey(containing) : ""),
               IPointerTypeSymbol pointer => DeclaredTypeKey(pointer.PointedAtType),
               IFunctionPointerTypeSymbol pointer => MethodTypes(pointer.Signature),
               _ => ""
           });

    internal static string GetNamespaceOutputPath(string outputPath, int namespaceIndex)
        => Path.Combine(Path.GetDirectoryName(outputPath) ?? "",
            $"{Path.GetFileNameWithoutExtension(outputPath)}.namespace{namespaceIndex + 1}.calr");

    internal ConversionResult ConvertWithPreservationContext(
        string csharpSource, string? sourceFile = null, CancellationToken cancellationToken = default)
        => ConvertWithContext(csharpSource, sourceFile, cancellationToken, capturePreservationContext: true);

    private ConversionResult ConvertWithContext(
        string csharpSource, string? sourceFile, CancellationToken cancellationToken, bool capturePreservationContext)
    {
        var result = ConvertCore(csharpSource, sourceFile, cancellationToken, capturePreservationContext);
        result = TryConvertObliviousModule(csharpSource, sourceFile, result,
            cancellationToken, capturePreservationContext);
        if (_options.Fidelity != ConversionFidelity.Lossless
            || (result.Success && !result.Losses.Any(loss => loss.IsSemanticLoss)))
            return result;

        return PreserveOriginal(csharpSource, sourceFile, result, cancellationToken);
    }

    private ConversionResult TryConvertObliviousModule(
        string source, string? sourceFile, ConversionResult attempted,
        CancellationToken cancellationToken, bool capturePreservationContext,
        bool allowScopedNamespaceImports = false)
    {
        if (_options.Fidelity != ConversionFidelity.Lossless
            || !_options.ValidateRoundTripCSharp
            || _options.PreprocessorMode != PreprocessorConversionMode.PreserveAllBranches
            || _options.References.Any(reference => !File.Exists(reference.Path)))
            return attempted;

        var parseOptions = GetEffectiveParseOptions(sourceFile);
        if (parseOptions.Kind != SourceCodeKind.Regular)
            return attempted;
        var tree = CSharpSyntaxTree.ParseText(source, parseOptions, sourceFile ?? "",
            cancellationToken: cancellationToken);
        var root = tree.GetCompilationUnitRoot(cancellationToken);
        if (root.ContainsDiagnostics)
            return attempted;
        var original = CSharpCompilation.Create("ObliviousModuleAnalysis",
            GeneratedCSharpCompiler.AddImplicitUsings(
                new[] { tree }.Concat(_options.AdditionalSemanticSyntaxTrees
                    .Where(sibling => !IsSameSyntaxTree(sibling, tree))),
                _options.IncludeImplicitSystemUsing),
            GetSemanticMetadataReferences(),
            new CSharpCompilationOptions(
                Enum.TryParse<OutputKind>(attempted.Metadata.OutputKind, out var outputKind)
                    ? outputKind : _options.OutputKind, allowUnsafe: true)
                .WithNullableContextOptions(_options.NullableContextOptions ?? NullableContextOptions.Disable));
        var model = original.GetSemanticModel(tree);
        if (original.GetDiagnostics(cancellationToken).Any(diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error))
            return attempted;
        var before = DeclaredTypeMetadata(model, root);
        if (!before.Any(record => record.Contains("[None]", StringComparison.Ordinal)))
            return attempted;

        bool MetadataMatches(string generated, out Diagnostic[] errors)
        {
            var generatedTree = CSharpSyntaxTree.ParseText(generated, parseOptions,
                cancellationToken: cancellationToken);
            var compilation = original.ReplaceSyntaxTree(tree, generatedTree);
            errors = compilation.GetDiagnostics(cancellationToken)
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
            return errors.Length == 0
                && before.SequenceEqual(DeclaredTypeMetadata(compilation.GetSemanticModel(generatedTree),
                    generatedTree.GetCompilationUnitRoot(cancellationToken)), StringComparer.Ordinal);
        }

        // The certification compiles check the binder and effects here, and check the
        // generated C# in MetadataMatches, which compiles it inside the original compilation
        // (its references with their aliases, and sibling project trees). Program.Compile's
        // own standalone C# validation has neither, so a call into a referenced assembly or a
        // sibling type failed it and a faithful native conversion was preserved whole.

        // Preserved declarations also inherit the emitted nullable context.
        if (attempted.Success && attempted.CalorSource is { } current)
        {
            var compiledCurrent = Program.Compile(current, sourceFile, new CompilationOptions
            {
                IncludeImplicitSystemUsing = _options.IncludeImplicitSystemUsing,
                ProjectDirectory = _options.ProjectDirectory,
                DeferGeneratedOutputValidation = true,
                CancellationToken = cancellationToken
            });
            if (!compiledCurrent.HasErrors && MetadataMatches(compiledCurrent.GeneratedCode, out _))
                return attempted;
        }

        ConversionResult PreserveUncertifiedModule(string? reason = null, ConversionResult? candidate = null)
        {
            if (reason != null)
                attempted.Context.AddWarning(reason, "nullable-module-fidelity");
            var result = attempted.Success
                ? PreserveOriginal(source, sourceFile, attempted, cancellationToken)
                : attempted;
            // Candidate-only diagnostics explain the rejection, not the original preservation path.
            var prefix = attempted.Success ? "Before whole-file preservation (Warning): " : "";
            if (candidate != null)
            {
                foreach (var issue in candidate.Issues
                    .DistinctBy(issue => (issue.Severity, issue.Message, issue.Feature,
                        issue.Line, issue.Column, issue.Suggestion))
                    .Where(issue => !attempted.Issues.Any(existing =>
                        existing.Severity == issue.Severity && existing.Message == issue.Message
                        && existing.Feature == issue.Feature && existing.Line == issue.Line
                        && existing.Column == issue.Column && existing.Suggestion == issue.Suggestion)))
                    result.Context.AddWarning(
                        $"{prefix}Oblivious module candidate ({issue.Severity}): {issue.Message}",
                        issue.Feature, issue.Line, issue.Column, issue.Suggestion);
                foreach (var loss in candidate.Losses
                    .DistinctBy(loss => (loss.Kind, loss.Feature, loss.Description, loss.Line))
                    .Where(loss => !attempted.Losses.Any(existing =>
                        existing.Kind == loss.Kind && existing.Feature == loss.Feature
                        && existing.Description == loss.Description && existing.Line == loss.Line)))
                    result.Context.AddWarning(
                        $"{prefix}Oblivious module candidate ({loss.Kind}): {loss.Description}",
                        loss.Feature, loss.Line);
            }
            return result;
        }

        // One module policy cannot represent lexical changes of annotation context.
        if (root.DescendantTrivia(descendIntoTrivia: true)
                .Any(trivia => trivia.GetStructure() is NullableDirectiveTriviaSyntax)
            || root.DescendantNodes().OfType<TypeSyntax>().Any(type =>
                model.GetNullableContext(type.SpanStart).HasFlag(NullableContext.AnnotationsEnabled)))
            return PreserveUncertifiedModule(
                "A single oblivious module context cannot represent the original lexical nullable contexts; "
                + "the native replacement could not be certified.");

        var candidate = ConvertCore(source, sourceFile, cancellationToken, capturePreservationContext,
            originalSemanticModel: model, allowScopedNamespaceImports: allowScopedNamespaceImports,
            obliviousModule: true);
        var entirelyNative = candidate.InteropPreservationCount == 0 && candidate.Losses.Count == 0;
        var directPreservation = candidate.InteropPreservationCount > 0 && candidate.Losses.Count > 0
            && candidate.Losses.All(loss => loss.Kind == ConversionLossKind.InteropPreserved
                && loss.Path == ConversionPath.Interop);
        if (!candidate.Success || !(entirelyNative || directPreservation)
            || candidate.CalorSource is not { } calorSource)
            return PreserveUncertifiedModule(
                "The oblivious module candidate did not produce successful, entirely native Calor "
                + "or direct, lossless member preservation; the replacement could not be certified.", candidate);
        // Reparse the actual deliverable and enforce the normal binder/effect
        // checks. No unchecked output can authorize this recovery: the generated
        // C# is compiled in the original context by MetadataMatches below.
        var compiled = Program.Compile(calorSource, sourceFile, new CompilationOptions
        {
            IncludeImplicitSystemUsing = _options.IncludeImplicitSystemUsing,
            ProjectDirectory = _options.ProjectDirectory,
            DeferGeneratedOutputValidation = true,
            CancellationToken = cancellationToken
        });
        if (compiled.HasErrors)
        {
            foreach (var error in compiled.Diagnostics.Errors)
                attempted.Context.AddWarning(
                    $"Native oblivious module failed Calor compilation ({error.Code}): {error.Message}",
                    "roundtrip-calor-validation", error.Span.Line, error.Span.Column);
            return PreserveUncertifiedModule(candidate: candidate);
        }
        if (!MetadataMatches(compiled.GeneratedCode, out var generatedErrors))
        {
            if (generatedErrors.Length == 0)
                return PreserveUncertifiedModule(
                    "The native projection changed declared type/nullability metadata; "
                    + "a single oblivious module context could not certify a faithful replacement.", candidate);
            foreach (var error in generatedErrors)
            {
                var span = error.Location.GetLineSpan().StartLinePosition;
                attempted.Context.AddWarning(
                    $"Native oblivious module failed C# validation ({error.Id}): {error.GetMessage()}",
                    "roundtrip-csharp-validation", span.Line + 1, span.Character + 1);
            }
            return PreserveUncertifiedModule(candidate: candidate);
        }
        candidate.Context.RoundTrip = ConversionRoundTrip.Verified;
        return candidate;
    }

    internal ConversionResult PreserveOriginal(
        string source,
        string? sourceFile,
        ConversionResult attempted,
        CancellationToken cancellationToken,
        IEnumerable<ConversionIssue>? validationIssues = null)
    {
        var preservationStart = DateTime.UtcNow;
        cancellationToken.ThrowIfCancellationRequested();
        var parseOptions = GetEffectiveParseOptions(sourceFile);
        var tree = CSharpSyntaxTree.ParseText(
            source, parseOptions, sourceFile ?? "", cancellationToken: cancellationToken);
        if (tree.GetDiagnostics(cancellationToken).Any(
                diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            || _options.References.Any(reference => !File.Exists(reference.Path))
            || _options.PreprocessorMode != PreprocessorConversionMode.PreserveAllBranches)
        {
            if (!attempted.Success)
                return attempted;
            attempted.Context.AddError(
                "Original C# cannot be preserved: malformed source, missing references, or destructive preprocessor options.",
                "original-csharp-validation");
            return new ConversionResult
            {
                Success = false,
                CalorSource = attempted.CalorSource,
                Ast = attempted.Ast,
                Context = attempted.Context,
                Duration = attempted.Duration + (DateTime.UtcNow - preservationStart)
            };
        }

        var context = CreateContext(sourceFile);
        context.CapturePreservationContext = attempted.Context.CapturePreservationContext;
        context.Metadata = attempted.Metadata;
        context.OriginalSource = source;
        if (context.CapturePreservationContext)
            context.OriginalSyntaxTree = tree;
        context.UserManifestRoot = ResolveUserManifestRoot();
        context.Stats.TotalNodes = attempted.Context.Stats.TotalNodes;
        context.Stats.InteropBlocksEmitted = 1;
        foreach (var issue in attempted.Issues.Concat(validationIssues ?? []))
            context.AddWarning(
                $"Before whole-file preservation ({issue.Severity}): {issue.Message}",
                issue.Feature, issue.Line, issue.Column, issue.Suggestion);
        foreach (var loss in attempted.Losses)
            context.AddWarning(
                $"Before whole-file preservation ({loss.Kind}): {loss.Description}",
                loss.Feature, loss.Line);
        var trigger = validationIssues != null || attempted.Context.RoundTrip == ConversionRoundTrip.Failed
            ? ConversionTrigger.RoundTripFailure
            : attempted.Issues.Any(issue => issue.Feature is "generated-calor-validation" or "post-validation-fallback")
                ? ConversionTrigger.ParseFailure : ConversionTrigger.UnsupportedConstruct;
        context.RecordLoss(
            ConversionLossKind.InteropPreserved,
            "whole-file-preservation",
            "Original compilation unit preserved exactly after native/member/type conversion or validation failed.",
            1, ConversionPath.Rescue, trigger,
            ConversionEnabledBy.LosslessFidelity);

        var module = PreserveCompilationUnit(source, "whole-file-preservation",
            "Native/member/type conversion or validation failed; preserve the original compilation unit.", context);
        var calorSource = new CalorEmitter(context).Emit(module);
        var compiled = Program.Compile(calorSource, sourceFile ?? "preserved.calr",
            new CompilationOptions
            {
                DeferGeneratedOutputValidation = true,
                CancellationToken = cancellationToken
            });
        if (compiled.HasErrors || !string.Equals(source, compiled.GeneratedCode, StringComparison.Ordinal))
        {
            foreach (var error in compiled.Diagnostics.Errors)
                context.AddError(error.Message, "whole-file-preservation", error.Span.Line, error.Span.Column);
            if (!compiled.HasErrors)
                context.AddError("Whole-file preservation did not reproduce the original C# exactly.",
                    "whole-file-preservation");
        }
        else
        {
            var originalValidation = GeneratedCSharpCompiler.Validate(
                [new GeneratedCSharpSource(source, sourceFile ?? "original.cs")],
                new GeneratedCSharpCompilationContext
                {
                    IncludeImplicitSystemUsing = _options.IncludeImplicitSystemUsing,
                    LanguageVersion = parseOptions.LanguageVersion,
                    DocumentationMode = parseOptions.DocumentationMode,
                    SourceCodeKind = parseOptions.Kind,
                    Features = parseOptions.Features,
                    PreprocessorSymbols = parseOptions.PreprocessorSymbolNames,
                    References = _options.References.Select(reference =>
                        new GeneratedCSharpReference(reference.Path, reference.Aliases)),
                    AdditionalSources = parseOptions.Kind == SourceCodeKind.Script
                        ? null : _options.AdditionalSemanticSyntaxTrees
                            .Where(sibling => !IsSameSyntaxTree(sibling, tree))
                            .Select(sibling => new GeneratedCSharpSource(
                                sibling.GetText(cancellationToken).ToString(), sibling.FilePath)),
                    OutputKind = Enum.TryParse<OutputKind>(attempted.Metadata.OutputKind, out var originalKind)
                        ? originalKind : _options.OutputKind,
                    NullableContextOptions = _options.NullableContextOptions ?? NullableContextOptions.Disable
                });
            var originalErrors = originalValidation.SyntaxErrors
                .Concat(originalValidation.CompilationErrors).ToList();
            var incompleteContext = originalErrors.Any(error => error.Id is
                "CS0246" or "CS0234" or "CS0103" or "CS0012" or "CS0518"
                or "CS1069" or "CS0115" or "CS1061" or "CS0117"
                or "CS8795" or "CS0759"
                || (error.Id == "CS5001" && !_options.ReferencesAreComplete));
            if (originalErrors.Any(error => error.Id is
                    "CS0006" or "CS1504" or "CS8915" or "CS0104"
                    || HasIntrinsicConversionError(error))
                || (originalErrors.Count > 0 && !incompleteContext))
                foreach (var error in originalErrors)
                    context.AddError($"Original C# failed validation ({error.Id}): {error.GetMessage()}",
                        "original-csharp-validation",
                        error.Location.GetLineSpan().StartLinePosition.Line + 1);
            // Exact source is faithful even when a standalone file lacks its project
            // references. Keep that compile failure visible, never label it verified.
            ValidateLosslessRoundTrip(
                calorSource, context, _options.ValidateRoundTripCSharp,
                parseOptions, Enum.TryParse<OutputKind>(attempted.Metadata.OutputKind, out var outputKind)
                    ? outputKind : _options.OutputKind,
                _options.NullableContextOptions ?? NullableContextOptions.Disable,
                cancellationToken, refuseOnFailure: false);
        }
        return new ConversionResult
        {
            Success = !context.HasErrors,
            Ast = module,
            CalorSource = calorSource,
            Context = context,
            Duration = attempted.Duration + (DateTime.UtcNow - preservationStart)
        };
    }

    private static bool HasIntrinsicConversionError(Diagnostic error)
    {
        if (error.Id is not ("CS0029" or "CS0266" or "CS0031")
            || error.Location.SourceTree is not { } tree
            || tree.GetRoot().FindNode(error.Location.SourceSpan,
                getInnermostNodeForTie: true) is not ExpressionSyntax expression)
            return false;

        // Missing project types/generators can cause conversion and return-flow
        // errors. Only a conversion between known built-in types is independent
        // of that missing context.
        var model = CSharpCompilation.Create(
            "PreservationSourceValidation", [tree],
            GeneratedCSharpCompiler.References)
            .GetSemanticModel(tree);
        var types = model.GetTypeInfo(expression);
        return types.Type is { SpecialType: not SpecialType.None, TypeKind: not TypeKind.Error }
            && types.ConvertedType is { SpecialType: not SpecialType.None, TypeKind: not TypeKind.Error };
    }

    private ConversionResult ConvertCore(
        string csharpSource,
        string? sourceFile,
        CancellationToken cancellationToken,
        bool capturePreservationContext,
        int? namespaceStart = null,
        SemanticModel? originalSemanticModel = null,
        bool allowScopedNamespaceImports = false,
        bool obliviousModule = false)
    {
        var startTime = DateTime.UtcNow;
        var context = CreateContext(sourceFile);
        context.CapturePreservationContext = capturePreservationContext;
        context.UserManifestRoot = ResolveUserManifestRoot();
        context.OriginalSource = csharpSource;
        context.ReserveNamesFrom(_options.AdditionalSemanticSyntaxTrees);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parseOptions = GetEffectiveParseOptions(sourceFile);
            var effectiveOutputKind = _options.OutputKind;
            context.Metadata = new ConversionMetadata
            {
                PreprocessorMode = _options.PreprocessorMode,
                IncludeImplicitSystemUsing = _options.IncludeImplicitSystemUsing,
                Configuration = _options.Configuration,
                TargetFramework = _options.TargetFramework,
                LanguageVersion = parseOptions.LanguageVersion.ToString(),
                DocumentationMode = parseOptions.DocumentationMode.ToString(),
                SourceCodeKind = parseOptions.Kind.ToString(),
                NullableContextOptions =
                    _options.NullableContextOptions?.ToString() ?? "Unspecified",
                ReferencesAreComplete = _options.ReferencesAreComplete,
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
            var syntaxTree = originalSemanticModel?.SyntaxTree ?? CSharpSyntaxTree.ParseText(
                csharpSource,
                parseOptions,
                path: sourceFile ?? "",
                cancellationToken: cancellationToken);
            var root = syntaxTree.GetCompilationUnitRoot();
            if (context.CapturePreservationContext
                && string.Equals(csharpSource, context.OriginalSource, StringComparison.Ordinal))
                context.OriginalSyntaxTree = syntaxTree;
            // #1132: the tree actually converted (its preprocessor symbols) also reserves names.
            context.ReserveNamesFrom([syntaxTree]);
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
            var diagnostics = root.GetDiagnostics()
                    .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error
                             && d.Id != "CS1029")
                    .ToList();

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

            var lossyScopedImports = namespaceStart == null && !allowScopedNamespaceImports
                && _options.Fidelity == ConversionFidelity.Lossy
                && _options.PreprocessorMode == PreprocessorConversionMode.SelectActiveBranchLossy
                && _options.ValidateRoundTripCSharp
                && CanConvertScopedNamespaceImports(root, allowRegionDirectives: true)
                && root.Usings.All(directive => directive.UnsafeKeyword.IsKind(SyntaxKind.None)
                    && directive.NamespaceOrType is NameSyntax);
            if (namespaceStart == null && RequiresWholeFileNamespaceInterop(root)
                && !lossyScopedImports
                && !(allowScopedNamespaceImports && CanConvertScopedNamespaceImports(root)))
            {
                if (!context.PreservesCSharp)
                {
                    context.AddError("Native module headers cannot flatten this namespace or using topology.",
                        feature: "namespace-topology");
                    return new ConversionResult { Success = false, Context = context, Duration = DateTime.UtcNow - startTime };
                }
                return ConvertWholeFileNamespaceInterop(
                    csharpSource,
                    context,
                    startTime,
                    parseOptions,
                    effectiveOutputKind,
                    cancellationToken);
            }

            // Step 2: Create semantic model for type inference (best-effort)
            SemanticModel? semanticModel = originalSemanticModel;
            try
            {
                if (semanticModel == null)
                {
                    var semanticTrees = new[] { syntaxTree }
                        .Concat(_options.AdditionalSemanticSyntaxTrees.Where(tree =>
                            !IsSameSyntaxTree(tree, syntaxTree)));
                    var compilation = CSharpCompilation.Create("ConversionAnalysis",
                        GeneratedCSharpCompiler.AddImplicitUsings(
                            semanticTrees, _options.IncludeImplicitSystemUsing),
                        GetSemanticMetadataReferences(),
                        new CSharpCompilationOptions(effectiveOutputKind)
                            .WithNullableContextOptions(
                                _options.NullableContextOptions
                                ?? NullableContextOptions.Disable));
                    semanticModel = compilation.GetSemanticModel(syntaxTree);
                }
            }
            catch
            {
                // Semantic model creation is best-effort; proceed without it
            }

            if (lossyScopedImports)
            {
                if (semanticModel == null || _options.References.Any(reference => !File.Exists(reference.Path)))
                    context.AddError("Scoped namespace imports require the original semantic context and supplied references.",
                        "scoped-namespace-source-validation");
                else
                    foreach (var error in semanticModel.Compilation.GetDiagnostics(cancellationToken)
                        .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
                        context.AddError($"Original C# cannot convert scoped imports ({error.Id}): {error.GetMessage()}",
                            "scoped-namespace-source-validation",
                            error.Location.GetLineSpan().StartLinePosition.Line + 1);
                if (context.HasErrors)
                    return new ConversionResult { Success = false, Context = context, Duration = DateTime.UtcNow - startTime };

                // This path admits no lexical nullable directives, so a single
                // module context can retain annotation-disabled declarations.
                obliviousModule = !semanticModel!.GetNullableContext(root.SpanStart)
                    .HasFlag(NullableContext.AnnotationsEnabled);
            }

            // Noncertifying lowering still preserves the source's annotation context.
            if (_options.Fidelity == ConversionFidelity.Lossy
                && _options.Mode == ConversionMode.Interop
                && semanticModel != null
                && !semanticModel.GetNullableContext(root.SpanStart)
                    .HasFlag(NullableContext.AnnotationsEnabled)
                && !root.DescendantTrivia(descendIntoTrivia: true)
                    .Any(trivia => trivia.GetStructure() is NullableDirectiveTriviaSyntax)
                && !semanticModel.Compilation.GetDiagnostics(cancellationToken)
                    .Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
                obliviousModule = true;

            // Visit C# AST and build Calor AST
            ModuleNode? calorAst;
            try
            {
                var moduleName = _options.ModuleName ?? DeriveModuleName(sourceFile, root);
                var visitor = new RoslynSyntaxVisitor(
                    context,
                    semanticModel,
                    cancellationToken,
                    _options.NullableContextOptions.HasValue)
                {
                    PreserveObliviousDeclarations = obliviousModule
                };
                calorAst = visitor.Convert(root, moduleName,
                    namespaceStart.HasValue
                        ? root.Members.OfType<NamespaceDeclarationSyntax>()
                            .Single(scope => scope.SpanStart == namespaceStart)
                        : null);
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

            // Step 2b (#1173): derive every §E row from the Calor that will ship,
            // using the compiler's own effect inference. Before this, the rows came
            // from a second walker maintained beside the emitter, so the converter
            // could emit a §NEW the walker never visited and write "pure" over it —
            // and the next compile of that file reported Calor0410 against code the
            // converter itself had written. The inference that CHECKS the row now
            // also WRITES it, so the two cannot disagree.
            SynthesizeEffectRows(calorAst, context, sourceFile, cancellationToken);

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
            // #1514: plain lossy now runs the round-trip check too (below), but only
            // to REPORT a failure. It deliberately does not open the rescue: a
            // standalone compile also fails when the file only depends on a sibling
            // file, and under PassthroughOnError a rescue that cannot recover would
            // refuse a conversion lossy mode used to complete.
            var roundTripGate = namespaceStart == null && !allowScopedNamespaceImports
                && !lossyScopedImports && _options.ValidateRoundTripCSharp
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
                && (_options.RescueUnusableMembers || _options.PassthroughOnError
                    || _options.Fidelity == ConversionFidelity.Lossless)
                && !context.HasErrors
                && context.PreservesCSharp
                && roundTripGate
                && !RoundTripCompiles(calorSource, context, parseOptions, effectiveOutputKind, cancellationToken);
            if (context.PreservesCSharp && (parseFailed || roundTripFailed))
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
                    // The rewrap replaced member bodies with their original C#
                    // (§CSHARP), whose effects are ASSUMED rather than inferred, so
                    // re-synthesise over the tree that actually ships. Note what this
                    // does and does not do: synthesis only ever WIDENS, so a row that
                    // covered the native body and is now broader than the §CSHARP one
                    // survives. Over-declaration is not diagnosed (nothing reports
                    // Calor0401), so the rewrapped output still compiles; what the
                    // second pass buys is coverage of anything the rewrap ADDED.
                    SynthesizeEffectRows(calorAst, context, sourceFile, cancellationToken);
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

            // #1514: the round trip is checked in every fidelity. Lossless and
            // selected-branch-lossy conversions are refused when it fails (as
            // before). Plain lossy keeps its output, because substitutions it was
            // allowed to make (a statement replaced by a TODO comment) can leave C#
            // that does not compile, but the failure is recorded as warnings and as
            // RoundTrip = failed, so the file can never be reported as clean.
            ValidateLosslessRoundTrip(
                calorSource,
                context,
                namespaceStart == null && !allowScopedNamespaceImports
                    && !lossyScopedImports && _options.ValidateRoundTripCSharp,
                parseOptions,
                effectiveOutputKind,
                _options.NullableContextOptions
                    ?? NullableContextOptions.Disable,
                cancellationToken,
                refuseOnFailure: _options.Fidelity == ConversionFidelity.Lossless
                    || _options.PreprocessorMode
                        == PreprocessorConversionMode.SelectActiveBranchLossy);
            foreach (var diagnostic in activeErrorDirectives)
            {
                var span = diagnostic.Location.GetLineSpan().StartLinePosition;
                context.AddError(
                    $"Active #error directive ({diagnostic.Id}): {diagnostic.GetMessage()}",
                    line: span.Line + 1,
                    column: span.Character + 1,
                    feature: "active-error-directive");
            }

            if (lossyScopedImports && !context.HasErrors)
            {
                if (context.Losses.Any(loss => loss.Kind is
                    ConversionLossKind.InteropPreserved or ConversionLossKind.EmitterFallback
                    or ConversionLossKind.FallbackTodo or ConversionLossKind.Dropped))
                    context.AddError("Scoped namespace imports require native declarations and bodies, not preservation or substitutions.",
                        "scoped-namespace-fidelity");
                else
                {
                    var compiled = Program.Compile(calorSource, sourceFile, new CompilationOptions
                    {
                        IncludeImplicitSystemUsing = _options.IncludeImplicitSystemUsing,
                        ProjectDirectory = _options.ProjectDirectory,
                        StrictEffects = true,
                        DeferGeneratedOutputValidation = true,
                        CancellationToken = cancellationToken
                    });
                    foreach (var error in compiled.Diagnostics.Errors)
                        context.AddError($"Native scoped imports failed Calor compilation: {error.Message}",
                            "scoped-namespace-fidelity", error.Span.Line, error.Span.Column);
                    if (!compiled.HasErrors)
                    {
                        var generatedTree = CSharpSyntaxTree.ParseText(
                            compiled.GeneratedCode, parseOptions,
                            syntaxTree.FilePath, cancellationToken: cancellationToken);
                        var generatedCompilation = semanticModel!.Compilation.ReplaceSyntaxTree(syntaxTree, generatedTree);
                        foreach (var error in generatedCompilation.GetDiagnostics(cancellationToken)
                            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
                            context.AddError($"Native scoped imports failed C# validation ({error.Id}): {error.GetMessage()}",
                                "scoped-namespace-fidelity");
                        if (!DeclaredTypeMetadata(semanticModel, root).SequenceEqual(
                                DeclaredTypeMetadata(generatedCompilation.GetSemanticModel(generatedTree),
                                    generatedTree.GetCompilationUnitRoot(cancellationToken)), StringComparer.Ordinal))
                            context.AddError("Native scoped imports changed declared type/nullability metadata.",
                                "scoped-namespace-fidelity");
                    }
                }
            }
            if (lossyScopedImports)
                context.RoundTrip = context.HasErrors ? ConversionRoundTrip.Failed : ConversionRoundTrip.Verified;

            var destructiveLosses = context.Losses.Count(loss => loss.IsSemanticLoss);
            if (_options.Fidelity == ConversionFidelity.Lossless && destructiveLosses > 0)
            {
                context.AddError(
                    $"Lossless conversion refused {destructiveLosses} lossy substitution(s) or drop(s). " +
                    "Faithful C# preservation is required instead.",
                    feature: "lossless-contract");
            }

            if (_options.Verbose)
            {
                var status = _options.StatusWriter ?? Console.Out;
                status.WriteLine($"Converted {context.Stats.ConvertedNodes} nodes");
                status.WriteLine($"  Classes: {context.Stats.ClassesConverted}");
                status.WriteLine($"  Interfaces: {context.Stats.InterfacesConverted}");
                status.WriteLine($"  Methods: {context.Stats.MethodsConverted}");
                status.WriteLine($"  Properties: {context.Stats.PropertiesConverted}");
                status.WriteLine($"  Fields: {context.Stats.FieldsConverted}");
            }

            return new ConversionResult
            {
                Success = !context.HasErrors,
                CalorSource = lossyScopedImports && context.HasErrors ? null : calorSource,
                Ast = lossyScopedImports && context.HasErrors ? null : calorAst,
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
            var context = CreateContext(csharpFilePath);
            context.AddError($"Source file not found: {csharpFilePath}");
            return new ConversionResult { Success = false, Context = context };
        }

        // Decode strictly and retain source BOMs; malformed bytes must not
        // become replacement text that could be reported as preserved.
        var source = await ReadSourceFileAsync(csharpFilePath, cancellationToken);
        var result = Convert(source, csharpFilePath, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    internal static async Task<string> ReadSourceFileAsync(
        string path, CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        if (bytes.AsSpan().StartsWith(System.Text.Encoding.UTF32.GetPreamble()))
            return new System.Text.UTF32Encoding(false, false, true).GetString(bytes);
        var bigEndianUtf32 = new System.Text.UTF32Encoding(true, true, true);
        if (bytes.AsSpan().StartsWith(bigEndianUtf32.GetPreamble()))
            return bigEndianUtf32.GetString(bytes);
        if (bytes.AsSpan().StartsWith(System.Text.Encoding.Unicode.GetPreamble()))
            return new System.Text.UnicodeEncoding(false, false, true).GetString(bytes);
        if (bytes.AsSpan().StartsWith(System.Text.Encoding.BigEndianUnicode.GetPreamble()))
            return new System.Text.UnicodeEncoding(true, false, true).GetString(bytes);
        // Decode the entire source, including a UTF-8 BOM, so exact passthrough
        // does not silently remove it. Invalid byte sequences are read failures.
        return new System.Text.UTF8Encoding(false, true).GetString(bytes);
    }

    /// <summary>
    /// Converts a C# file and writes the output to an Calor file.
    /// </summary>
    /// <remarks>
    /// Persists original conversion context beside the output. Existing output
    /// must match its saved generation exactly; edited or unassociated output
    /// is retained and the refresh fails with an actionable I/O error.
    /// </remarks>
    public async Task<ConversionResult> ConvertFileAndSaveAsync(
        string csharpFilePath,
        string? outputPath = null,
        CancellationToken cancellationToken = default)
    {
        using var generation = CreateGeneration(csharpFilePath,
            outputPath ?? Path.ChangeExtension(csharpFilePath, ".calr"));
        var result = await generation.ConvertAsync(cancellationToken);
        if (result.Success && result.CalorSource != null)
            await generation.WriteAsync(result, cancellationToken);
        return result;
    }

    internal ConversionGeneration CreateGeneration(
        string sourcePath, string outputPath, bool dryRun = false, int? namespaceIndex = null,
        bool projectConversion = false)
        => new(this, _options, sourcePath, outputPath, dryRun, namespaceIndex, projectConversion);

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
    /// Resolvers the row synthesis reuses. Loading the embedded BCL manifests is the
    /// expensive part and does not depend on the file being converted, so a resolver
    /// is worth keeping — but NOT worth sharing across threads:
    /// <see cref="EffectResolver"/> memoises into plain dictionaries, and conversion
    /// runs in parallel (<c>ProjectMigrator</c> fans out under a semaphore, the
    /// round-trip harness under <c>Parallel.ForEachAsync</c>). Renting gives one
    /// conversion exclusive use of one resolver, so the pool grows to the degree of
    /// parallelism actually used and no further.
    ///
    /// <para>The alternative — one static resolver, as the deleted
    /// <c>RoslynSyntaxVisitor._migrationResolver</c> was — races on those
    /// dictionaries. That race predates this change, but synthesis resolves far more
    /// call targets per file than the old walker did, so it is not a risk to inherit
    /// quietly.</para>
    /// </summary>
    /// <para>Keyed by manifest SCOPE, because <see cref="EffectResolver.Initialize"/> is
    /// idempotent per instance: a resolver initialized for one project's manifests would
    /// silently ignore a second project's. Callers that set neither directory share one
    /// pool of embedded-manifest resolvers, which is every caller that has no project.</para>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<
        string, System.Collections.Concurrent.ConcurrentBag<EffectResolver>> SynthesisResolvers = new(StringComparer.Ordinal);

    /// <para>#1564: the scope includes the user-level manifest root. Before, a pooled
    /// resolver kept whatever <c>~/.calor/manifests</c> it saw when it was first built, so
    /// the root was effectively fixed for the life of the process and a caller could not
    /// isolate a conversion by changing <c>HOME</c>. Each conversion now resolves its root
    /// when it starts (<see cref="ResolveUserManifestRoot"/>) and rents from that root's
    /// pool. Like the project and solution manifests, a root's files are read once per
    /// pooled resolver; it is the choice of root that is never cached.</para>
    private static string ManifestScopeKey(string? projectDirectory, string? solutionDirectory, string userManifestRoot)
        => (projectDirectory ?? "") + "\u0000" + (solutionDirectory ?? "") + "\u0000" + userManifestRoot;

    /// <summary>
    /// #1564: the user-level manifest directory for one conversion: the injected
    /// <see cref="ConversionOptions.UserManifestRoot"/>, else <c>CALOR_HOME</c> or
    /// <c>HOME</c> as they are right now. Empty means no user-level manifests.
    /// </summary>
    internal string ResolveUserManifestRoot()
    {
        var root = _options.UserManifestRoot ?? ManifestLoader.GetUserManifestsDirectory();
        if (string.IsNullOrWhiteSpace(root))
            return "";
        try
        {
            return Path.GetFullPath(root);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
        {
            return root;
        }
    }

    private static EffectResolver RentSynthesisResolver(
        string? projectDirectory,
        string? solutionDirectory,
        string userManifestRoot)
    {
        var pool = SynthesisResolvers.GetOrAdd(
            ManifestScopeKey(projectDirectory, solutionDirectory, userManifestRoot),
            _ => new System.Collections.Concurrent.ConcurrentBag<EffectResolver>());
        if (pool.TryTake(out var pooled))
            return pooled;
        var loader = userManifestRoot.Length == 0
            ? new ManifestLoader(loadUserLevelManifests: false)
            : new ManifestLoader(userManifestRoot);
        var resolver = new EffectResolver(loader);
        resolver.Initialize(projectDirectory, solutionDirectory);
        return resolver;
    }

    private static void ReturnSynthesisResolver(
        EffectResolver resolver,
        string? projectDirectory,
        string? solutionDirectory,
        string userManifestRoot)
        => SynthesisResolvers
            .GetOrAdd(
                ManifestScopeKey(projectDirectory, solutionDirectory, userManifestRoot),
                _ => new System.Collections.Concurrent.ConcurrentBag<EffectResolver>())
            .Add(resolver);

    /// <summary>
    /// #1173 — writes each declaration's <c>§E</c> row from the compiler's own
    /// effect inference, over the Calor that will actually ship.
    ///
    /// <para>The rows used to come from a second walker maintained beside the
    /// emitter (<c>RoslynSyntaxVisitor.InferEffectsFromBody</c>), and the two
    /// drifted: the walker never visited a <c>foreach</c>'s collection, a
    /// <c>§USE</c> resource or a lambda body, so the converter wrote "pure" over
    /// bodies that allocate, and the next compile of that file reported Calor0410
    /// against code the converter itself had written.</para>
    ///
    /// <para>Inference runs on the module RE-PARSED from emitted text, not on the
    /// tree in hand, and that is the whole point. The emitter is not a printer: it
    /// desugars a method chain into <c>§B</c> temporaries, for one, so the tree
    /// that ships is not the tree the converter built, and a row derived from the
    /// latter is a row about code nobody will compile. Deriving it from the emitted
    /// text closes the loop — the rows are computed from the same characters the
    /// next compile will read.</para>
    ///
    /// <para>The probe emit goes to a scratch context so the loss ledger sees
    /// exactly one emit, the real one in step 3. Rows travel back onto the tree in
    /// hand by the exact header offsets recorded by that probe emitter. Parser-local
    /// identities do not survive a text round trip. Step 3 emits once with the
    /// correlated rows; the correlation never becomes public syntax.</para>
    ///
    /// <para>Resolution must see what the later compile will see, or the two agree
    /// about the body and disagree about the manifests — see
    /// <see cref="ConversionOptions.ProjectDirectory"/>, which callers that have a
    /// project set so the rented resolver loads the same manifests.</para>
    ///
    /// <para>Best-effort by design: a row that is missing or too narrow is a
    /// diagnostic on a later compile, never a reason to throw away a conversion
    /// that otherwise succeeded. Own-row synthesis only ever WIDENS a row, so a run that
    /// stops early — cap reached, unparseable probe, or an exception — leaves rows
    /// that are sound but possibly incomplete, and says so in the context.</para>
    /// <para>Callback contracts use bound native parameter identities and strict
    /// lambda body facts. Effect binders and position rows travel back through the
    /// same declaration offsets; unproven returned rows are never copied as proof.</para>
    /// </summary>
    private void SynthesizeEffectRows(
        ModuleNode? module,
        ConversionContext context,
        string? sourceFile,
        CancellationToken cancellationToken)
    {
        if (module == null)
            return;

        cancellationToken.ThrowIfCancellationRequested();
        var resolver = RentSynthesisResolver(_options.ProjectDirectory, _options.SolutionDirectory, context.UserManifestRoot);
        try
        {
            var declarationOffsets = new Dictionary<AstNode, int>(ReferenceEqualityComparer.Instance);
            var emitter = new CalorEmitter(CreateContext(sourceFile))
            {
                EffectRowDeclarationEmitted = declarationOffsets.Add
            };
            var probe = emitter.Emit(module);
            var reparsed = TryParseModule(probe);
            if (reparsed == null)
            {
                // The emitted text does not parse. Step 3b rescues that case by
                // rewrapping members as §CSHARP and calls back here on the result;
                // there is nothing to infer from in the meantime.
                return;
            }

            var converged = EffectEnforcementPass.SynthesizeConvertedRows(reparsed, resolver);
            CopyEffectRows(reparsed, module, declarationOffsets);
            var binding = new Binding.Binder(new Diagnostics.DiagnosticBag());
            binding.Bind(module, null, declarationsOnly: true);
            binding.MaterializeUnknownCallableRows();

            if (!converged)
            {
                context.AddWarning(
                    "Effect-row synthesis did not converge within "
                    + $"{EffectEnforcementPass.DefaultRowSynthesisRounds} rounds; the emitted §E rows "
                    + "cover the bodies they were computed from but may still be narrower than the "
                    + "effects those bodies perform.",
                    feature: "effect-row-synthesis");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            context.AddWarning(
                $"Effect-row synthesis failed ({ex.GetType().Name}: {ex.Message}); the emitted §E rows "
                + "may be narrower than the effects the converted bodies perform.",
                feature: "effect-row-synthesis");
        }
        finally
        {
            ReturnSynthesisResolver(resolver, _options.ProjectDirectory, _options.SolutionDirectory, context.UserManifestRoot);
        }
    }

    /// <summary>
    /// Parses emitted Calor back into a module, or null when it does not parse
    /// cleanly. Deliberately independent of <see cref="ParseValidatorOverride"/>:
    /// that hook exists to let tests force the rescue path, and row synthesis must
    /// read the real tree or nothing.
    /// </summary>
    private static ModuleNode? TryParseModule(string calorSource)
    {
        var diagnostics = new Diagnostics.DiagnosticBag();
        var tokens = new Parsing.Lexer(calorSource, diagnostics).TokenizeAllForParser();
        if (diagnostics.HasErrors)
            return null;
        var module = new Parsing.Parser(tokens, diagnostics).Parse();
        return diagnostics.HasErrors ? null : module;
    }

    /// <summary>
    /// Copies synthesised boundary rows from the re-parsed module onto the tree
    /// the converter holds. Non-public bodies omit their own inferred rows, but
    /// retain callback positions and effect binders. Exact emitted header offsets
    /// distinguish overloads, namespaces and conditional branches.
    /// Every target must match before any rows are copied.
    /// </summary>
    private static void CopyEffectRows(
        ModuleNode source, ModuleNode target, IReadOnlyDictionary<AstNode, int> declarationOffsets)
    {
        var rows = EnumerateRowSites(source)
            .ToDictionary(site => site.Declaration.Span.Start);
        var assignments = new List<Action>();

        foreach (var (declaration, _, assign) in EnumerateRowSites(target))
        {
            // A constructor printed without its own header (a record's synthesized one)
            // has no row position; it keeps its intrinsic contract.
            if (declaration is ConstructorNode && !declarationOffsets.ContainsKey(declaration))
                continue;
            if (!declarationOffsets.TryGetValue(declaration, out var offset)
                || !rows.TryGetValue(offset, out var synthesized))
            {
                throw new InvalidOperationException(
                    $"Effect-row declaration at {declaration.Span} did not survive the probe emit/reparse.");
            }
            if (declaration is FunctionNode { Visibility: Visibility.Private or Visibility.Internal }
                or MethodNode { Visibility: Visibility.Private or Visibility.Internal,
                    IsAbstract: false, IsExtern: false })
                // An inferred row cannot be unknown (Calor0410), so a non-public body
                // that calls an API without an effect contract keeps ! unknown.
                assignments.Add(() => assign(synthesized.Effects is { IsUnknown: true } unknown ? unknown : null));
            else if (synthesized.Effects != null)
                assignments.Add(() => assign(synthesized.Effects));
            var sourceParameters = synthesized.Declaration switch
            {
                FunctionNode function => function.Parameters,
                MethodNode method => method.Parameters,
                _ => null
            };
            var targetParameters = declaration switch
            {
                FunctionNode function => function.Parameters,
                MethodNode method => method.Parameters,
                _ => null
            };
            if (sourceParameters == null || targetParameters == null)
                continue;
            if (sourceParameters.Count != targetParameters.Count
                || !sourceParameters.Zip(targetParameters).All(pair => pair.First.Name == pair.Second.Name))
                throw new InvalidOperationException("Callback parameters did not survive the probe emit/reparse.");
            for (var index = 0; index < sourceParameters.Count; index++)
            {
                var sourceParameter = sourceParameters[index];
                var targetParameter = targetParameters[index];
                assignments.Add(() => targetParameter.Row = sourceParameter.Row);
            }
            if (synthesized.Declaration is FunctionNode sourceFunction && declaration is FunctionNode targetFunction)
            {
                assignments.Add(() => targetFunction.SetSynthesizedEffectParameters(sourceFunction.EffectParameters));
                if (sourceFunction.Output != null && targetFunction.Output != null)
                    assignments.Add(() => targetFunction.Output.Row = sourceFunction.Output.Row);
            }
            if (synthesized.Declaration is MethodNode sourceMethod && declaration is MethodNode targetMethod)
            {
                assignments.Add(() => targetMethod.SetSynthesizedEffectParameters(sourceMethod.EffectParameters));
                if (sourceMethod.Output != null && targetMethod.Output != null)
                    assignments.Add(() => targetMethod.Output.Row = sourceMethod.Output.Row);
            }
        }
        foreach (var assign in assignments)
            assign();
    }

    /// <summary>
    /// Every declaration in a module that can carry a <c>§E</c> row, as
    /// (declaration, current row, setter). Uses the same reach as the effect pass,
    /// while leaving parser-local identities inside their own trees.
    /// </summary>
    private static IEnumerable<(AstNode Declaration, EffectsNode? Effects, Action<EffectsNode?> Assign)>
        EnumerateRowSites(ModuleNode module)
    {
        foreach (var function in module.Functions)
            yield return (function, function.Effects, row => function.Effects = row);

        foreach (var cls in CallGraphAnalysis.EnumerateClasses(module))
        {
            foreach (var method in CallGraphAnalysis.EnumerateMethods(cls))
                yield return (method, method.Effects, row => method.Effects = row);
            foreach (var op in CallGraphAnalysis.EnumerateOperators(cls))
                yield return (op, op.Effects, row => op.Effects = row);
            foreach (var ctor in CallGraphAnalysis.EnumerateConstructors(cls))
                yield return (ctor, ctor.Effects, row => ctor.Effects = row);
        }

        foreach (var iface in CallGraphAnalysis.EnumerateInterfaces(module))
        {
            foreach (var signature in iface.Methods)
                yield return (signature, signature.Effects, row => signature.Effects = row);
        }
    }

    /// <summary>
    /// #836 M2: counts raw-C# frames in the emitted
    /// Calor and records an <see cref="ConversionLossKind.EmitterFallback"/>
    /// loss for every marker beyond the ledgered interop preservations, so the
    /// "zero losses = fully native output" invariant holds even for fallbacks
    /// produced inside the CalorEmitter (which cannot reach the ledger yet).
    /// </summary>
    internal static void ReconcileEmitterFallbacks(string calorSource, ConversionContext context)
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
        if (namespaceDeclarations.Any(declaration => declaration.Usings.Count > 0))
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
            ConversionContext context,
            DateTime startTime,
            CSharpParseOptions parseOptions,
            OutputKind outputKind,
            CancellationToken cancellationToken)
        {
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

            var module = PreserveCompilationUnit(
                csharpSource,
                "namespace-topology",
                "Multiple lexical namespace scopes are preserved as one whole-file interop boundary.", context);
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
                _options.NullableContextOptions
                    ?? NullableContextOptions.Disable,
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

    // Transport-independent final fallback boundary. In particular, do not
    // reconstruct source from Roslyn tokens or normalize literal indentation.
    internal static ModuleNode PreserveCompilationUnit(
        string source,
        string feature,
        string reason,
        ConversionContext? context = null)
    {
        var span = new Parsing.TextSpan(0, source.Length, 1, 1);
        var interop = new CSharpInteropBlockNode(
            span, source, feature, reason,
            isCompilationUnitPassthrough: true)
        {
            NamespaceIdentity = "",
            NamespaceScopeId = "",
            FullyQualifiedSymbolIdentity = "global::<compilation-unit>"
        };
        context?.AssociatePreservation(interop, context.OriginalSyntaxTree?.GetRoot());
        return new ModuleNode(
            span, "m001", "_global",
            [], [], [], [], [], [], [],
            new AttributeCollection(),
            [], [], [], [], null, [interop]);
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
        // #1144: returns the trigger that fired for the member, or null when it is usable.
        string? MemberFailed(
            IReadOnlyList<ClassDefinitionNode>? classes = null,
            IReadOnlyList<InterfaceDefinitionNode>? interfaces = null,
            IReadOnlyList<EnumDefinitionNode>? enums = null,
            IReadOnlyList<DelegateDefinitionNode>? delegates = null)
            => !MemberParsesCleanly(module, classes, interfaces, enums, delegates)
                ? ConversionTrigger.ParseFailure
                : requireRoundTrip
                  && !MemberRoundTripsCleanly(
                      module, context, parseOptions, outputKind, cancellationToken,
                      classes, interfaces, enums, delegates)
                    ? ConversionTrigger.RoundTripFailure
                    : null;

        // #1144 (F6-REPORT-11): the source side must key a type exactly as the
        // visitor does, or a custom module name disables the rescue. 0.30: ModuleName
        // is only a file label and a global type keeps its global identity
        // (`global::Probe`), so global sources are keyed without the module name.
        var sources = CollectTopLevelTypeSources(root, globalNamespaceIdentity: null);
        var triggers = new List<string>();

        var failedClasses = new List<ClassDefinitionNode>();
        var failedInterfaces = new List<InterfaceDefinitionNode>();
        var failedEnums = new List<EnumDefinitionNode>();
        var failedDelegates = new List<DelegateDefinitionNode>();
        var interops = new List<CSharpInteropBlockNode>();
        var replacements = new Dictionary<AstNode, CSharpInteropBlockNode>();

        foreach (var cls in module.Classes)
        {
            if (MemberFailed(classes: new[] { cls }) is { } trigger &&
                TryTakeSource(sources, "class", GetSymbolIdentity(cls, module), out var csharp))
            {
                var interop = MakeFallbackInterop(csharp, cls);
                interops.Add(interop);
                triggers.Add(trigger);
                replacements[cls] = interop;
                failedClasses.Add(cls);
            }
        }

        foreach (var iface in module.Interfaces)
        {
            if (MemberFailed(interfaces: new[] { iface }) is { } trigger &&
                TryTakeSource(sources, "interface", GetSymbolIdentity(iface, module), out var csharp))
            {
                var interop = MakeFallbackInterop(csharp, iface);
                interops.Add(interop);
                triggers.Add(trigger);
                replacements[iface] = interop;
                failedInterfaces.Add(iface);
            }
        }

        foreach (var en in module.Enums)
        {
            if (MemberFailed(enums: new[] { en }) is { } trigger &&
                TryTakeSource(sources, "enum", GetSymbolIdentity(en, module), out var csharp))
            {
                var interop = MakeFallbackInterop(csharp, en);
                interops.Add(interop);
                triggers.Add(trigger);
                replacements[en] = interop;
                failedEnums.Add(en);
            }
        }

        foreach (var del in module.Delegates)
        {
            if (MemberFailed(delegates: new[] { del }) is { } trigger &&
                TryTakeSource(sources, "delegate", GetSymbolIdentity(del, module), out var csharp))
            {
                var interop = MakeFallbackInterop(csharp, del);
                interops.Add(interop);
                triggers.Add(trigger);
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
        for (var i = 0; i < interops.Count; i++)
        {
            var (path, enabledBy) = RescueProvenance(triggers[i], context);
            context.RecordLoss(ConversionLossKind.InteropPreserved, "post-validation-fallback",
                interops[i].Reason ?? "Member re-preserved as §CSHARP after emitted Calor failed to parse (#717)",
                line: null, path, triggers[i], enabledBy);
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

    /// <summary>
    /// Records explicit rescue/passthrough preferences separately from default
    /// lossless rescue. Historical lossy producers retain their requested path.
    /// </summary>
    private static (string Path, string EnabledBy) RescueProvenance(string trigger, ConversionContext context)
    {
        var preserveGate = context.Fidelity == ConversionFidelity.Lossless
            ? ConversionEnabledBy.LosslessFidelity
            : context.Mode == ConversionMode.Interop ? ConversionEnabledBy.InteropMode : null;
        if (preserveGate == null)
            return (ConversionPath.Passthrough, ConversionEnabledBy.PassthroughOnError);
        if (trigger == ConversionTrigger.RoundTripFailure)
            return context.RescueUnusableMembers
                ? (ConversionPath.Rescue, ConversionEnabledBy.RescueUnusableMembers)
                : context.PassthroughOnError
                    ? (ConversionPath.Passthrough, ConversionEnabledBy.PassthroughOnError)
                    : (ConversionPath.Rescue, preserveGate);
        return (ConversionPath.Rescue, preserveGate);
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
            module.Span, module.LocalId, module.Name, module.Usings,
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
                    IncludeImplicitSystemUsing = context.Metadata.IncludeImplicitSystemUsing,
                    LanguageVersion = parseOptions.LanguageVersion,
                    DocumentationMode = parseOptions.DocumentationMode,
                    SourceCodeKind = parseOptions.Kind,
                    Features = parseOptions.Features,
                    PreprocessorSymbols = parseOptions.PreprocessorSymbolNames,
                    References = context.Metadata.References.Select(
                        reference => new GeneratedCSharpReference(
                            reference.Path,
                            reference.Aliases)),
                    OutputKind = outputKind,
                    NullableContextOptions = Enum.TryParse<NullableContextOptions>(
                        context.Metadata.NullableContextOptions,
                        out var nullableContext)
                        ? nullableContext
                        : NullableContextOptions.Disable
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

    /// <param name="Aliased">#1144: a global-namespace declaration keyed under the caller's ModuleName.</param>
    private sealed record TypeSource(bool IsPartial, string Text, bool Aliased = false);

    /// <summary>
    /// Collects top-level type declarations by kind and fully-qualified symbol
    /// identity. Several entries for one identity are legitimate partial
    /// declarations; same-named types in other namespaces have different keys.
    /// </summary>
    private static Dictionary<string, List<TypeSource>> CollectTopLevelTypeSources(
        CompilationUnitSyntax root,
        string? globalNamespaceIdentity)
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
            var identity = GetSyntaxTypeIdentity(member, name, globalNamespaceIdentity);
            var aliased = globalNamespaceIdentity != null
                && !member.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Any();
            var key = $"{kind}/{identity}";
            (map.TryGetValue(key, out var list) ? list : map[key] = new List<TypeSource>())
                .Add(new TypeSource(isPartial, DeclarationSourceText(member), aliased));
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

        if (list.Select(s => s.Aliased).Distinct().Count() > 1)
        {
            return false; // a global type and a real namespace share the ModuleName identity
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
        string name,
        string? globalNamespaceIdentity = null)
    {
        var namespaceIdentity = string.Join(
            ".",
            member.Ancestors()
                .OfType<BaseNamespaceDeclarationSyntax>()
                .Select(item => item.Name.ToString().Replace("@", ""))
                .Reverse());
        if (namespaceIdentity.Length == 0)
            namespaceIdentity = globalNamespaceIdentity ?? "";
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
            RescueUnusableMembers = _options.RescueUnusableMembers
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

    /// <summary>
    /// #1514: the feature name of the summary warning a plain lossy conversion
    /// records when its output fails the round-trip compile check.
    /// </summary>
    internal const string LossyRoundTripUnverifiedFeature = "lossy-roundtrip-unverified";

    /// <summary>
    /// Compiles the emitted Calor, then the C# it generates, and records the result
    /// in <see cref="ConversionContext.RoundTrip"/>. With
    /// <paramref name="refuseOnFailure"/> (lossless and selected-branch lossy) each
    /// failure is a conversion error, so the conversion is refused. Without it (plain
    /// lossy, #1514) each failure is a warning, plus one summary warning, and the
    /// output is kept but reported as unverified.
    /// </summary>
    private static void ValidateLosslessRoundTrip(
        string calorSource,
        ConversionContext context,
        bool validateGeneratedCSharp,
        CSharpParseOptions parseOptions,
        OutputKind outputKind,
        NullableContextOptions nullableContextOptions,
        CancellationToken cancellationToken,
        bool refuseOnFailure = true)
    {
        if (!validateGeneratedCSharp)
        {
            // The caller compiles the generated C# itself (project migration, the
            // round-trip harness); say so rather than leaving the field implying a check.
            context.RoundTrip = ConversionRoundTrip.Deferred;
            return;
        }

        var failures = 0;
        void Fail(string message, string feature, int? line = null, int? column = null)
        {
            failures++;
            if (refuseOnFailure)
                context.AddError(message, feature: feature, line: line, column: column);
            else
                context.AddWarning(message, feature: feature, line: line, column: column);
        }

        RunRoundTripCheck(
            calorSource, context, parseOptions, outputKind,
            nullableContextOptions, cancellationToken, Fail);

        context.RoundTrip = failures == 0
            ? ConversionRoundTrip.Verified
            : ConversionRoundTrip.Failed;
        if (failures > 0 && !refuseOnFailure)
        {
            context.AddWarning(
                context.Fidelity == ConversionFidelity.Lossless
                    ? $"Exact original C# was preserved, but the standalone round-trip compile check found {failures} error(s). " +
                      "Writing is not proof of a build; supply the original project references and configuration."
                    : $"Lossy output is unverified: the round-trip compile check found {failures} error(s), " +
                      "so the generated C# does not compile. The output was kept because lossy mode was requested; " +
                      "fix it by hand, or convert losslessly, which keeps a failing type as C# where it can.",
                feature: context.Fidelity == ConversionFidelity.Lossless
                    ? "preserved-roundtrip-unverified" : LossyRoundTripUnverifiedFeature);
        }
    }

    private static void RunRoundTripCheck(
        string calorSource,
        ConversionContext context,
        CSharpParseOptions parseOptions,
        OutputKind outputKind,
        NullableContextOptions nullableContextOptions,
        CancellationToken cancellationToken,
        Action<string, string, int?, int?> fail)
    {
        CompilationResult compileResult;
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
            fail(
                $"Generated Calor validation crashed: {ex.GetType().Name}: {ex.Message}",
                "roundtrip-calor-validation", null, null);
            return;
        }

        foreach (var diagnostic in compileResult.Diagnostics.Errors)
        {
            fail(
                $"Generated Calor failed compilation ({diagnostic.Code}): {diagnostic.Message}",
                "roundtrip-calor-validation",
                diagnostic.Span.Line,
                diagnostic.Span.Column);
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
                    IncludeImplicitSystemUsing = context.Metadata.IncludeImplicitSystemUsing,
                    LanguageVersion = parseOptions.LanguageVersion,
                    DocumentationMode = parseOptions.DocumentationMode,
                    SourceCodeKind = parseOptions.Kind,
                    Features = parseOptions.Features,
                    PreprocessorSymbols = parseOptions.PreprocessorSymbolNames,
                    References = context.Metadata.References.Select(
                        reference => new GeneratedCSharpReference(
                            reference.Path,
                            reference.Aliases)),
                    OutputKind = outputKind,
                    NullableContextOptions = nullableContextOptions
                });
            foreach (var diagnostic in validation.SyntaxErrors.Concat(validation.CompilationErrors))
            {
                var span = diagnostic.Location.GetLineSpan().StartLinePosition;
                fail(
                    $"Round-tripped C# failed compilation ({diagnostic.Id}): {diagnostic.GetMessage()}",
                    "roundtrip-csharp-validation",
                    span.Line + 1,
                    span.Character + 1);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            fail(
                $"Round-tripped C# validation crashed: {ex.GetType().Name}: {ex.Message}",
                "roundtrip-csharp-validation", null, null);
        }
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("SingleFile", "IL3000",
        Justification = "Assembly.Location is checked for empty string; gracefully returns no references in single-file mode.")]
    private static MetadataReference[] BuildBasicMetadataReferences()
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

        var regexLocation = typeof(System.Text.RegularExpressions.Regex).Assembly.Location;
        if (!string.IsNullOrEmpty(regexLocation))
            refs.Add(MetadataReference.CreateFromFile(regexLocation));

        var concurrentLocation = typeof(System.Collections.Concurrent.ConcurrentDictionary<,>).Assembly.Location;
        if (!string.IsNullOrEmpty(concurrentLocation))
            refs.Add(MetadataReference.CreateFromFile(concurrentLocation));

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

    internal MetadataReference[] GetSemanticMetadataReferences()
    {
        var configured = _options.References
            .Where(reference => File.Exists(reference.Path))
            .Select(reference => MetadataReference.CreateFromFile(
                Path.GetFullPath(reference.Path),
                reference.Aliases.Count == 0
                    ? MetadataReferenceProperties.Assembly
                    : MetadataReferenceProperties.Assembly.WithAliases(
                        reference.Aliases.ToImmutableArray())))
            .ToArray();
        return _options.ReferencesAreComplete
            ? configured
            : BasicMetadataReferences.Value.Concat(configured).ToArray();
    }

    private static bool IsSameSyntaxTree(SyntaxTree left, SyntaxTree right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (string.IsNullOrWhiteSpace(left.FilePath)
            || string.IsNullOrWhiteSpace(right.FilePath))
        {
            return false;
        }

        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(
            Path.GetFullPath(left.FilePath),
            Path.GetFullPath(right.FilePath),
            comparison);
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
            // This is a file label, not a CLR namespace or static container.
            var fileLabel = Path.GetFileNameWithoutExtension(sourceFile)
                .Replace('#', '_').Replace(':', '_');
            return CSharpEmitter.SanitizeIdentifier(fileLabel);
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

/// <summary>Owns one prepared generation base and its cooperating writer lock; conversion never writes output.</summary>
internal sealed class ConversionGeneration(
    CSharpToCalorConverter converter, ConversionOptions options, string sourcePath, string outputPath,
    bool dryRun, int? namespaceIndex = null, bool projectConversion = false) : IDisposable
{
    private readonly string _sourcePath = Path.GetFullPath(sourcePath);
    private readonly string _outputPath = Path.GetFullPath(outputPath);
    private FileStream? _generationLock;
    private string? _before;
    private string? _beforeReceipt;
    private IReadOnlyList<ConversionInputSnapshot> _inputs = [];
    private ConversionResult? _result;
    private bool _started;
    private bool _writeAttempted;

    internal string? Before => _before;
    internal ConversionResult Result => _result
        ?? throw new InvalidOperationException("No original conversion result has been captured.");

    internal async Task<ConversionResult> ConvertAsync(CancellationToken cancellationToken = default)
    {
        if (_started) throw new InvalidOperationException("A generation can execute conversion only once.");
        _started = true;
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(_sourcePath))
            return _result = await converter.ConvertFileAsync(_sourcePath, cancellationToken);

        var receiptPath = _outputPath + ".conversion.json";
        var artifacts = new[] { _outputPath, receiptPath, _outputPath + ".conversion.lock" };
        var sourceIdentity = CanonicalPath.Resolve(_sourcePath);
        if (artifacts.Any(path => Incremental.BuildStateCache.GetPathComparer()
                .Equals(CanonicalPath.Resolve(path), sourceIdentity)))
            throw new IOException("Conversion artifacts must not overwrite the original C# source.");
        if (!dryRun)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_outputPath)!);
            _generationLock = new FileStream(
                _outputPath + ".conversion.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        _before = File.Exists(_outputPath)
            ? await CSharpToCalorConverter.ReadSourceFileAsync(_outputPath, cancellationToken) : null;
        _beforeReceipt = File.Exists(receiptPath)
            ? await CSharpToCalorConverter.ReadSourceFileAsync(receiptPath, cancellationToken) : null;
        if (_before != null || _beforeReceipt != null)
        {
            if (_before == null || _beforeReceipt == null)
                throw new IOException("Existing output has no matching conversion generation context. Keep it and convert the original C# to a new destination.");
            var previous = ConversionReceipt.Deserialize(_beforeReceipt);
            if (!string.Equals(_before, previous.GeneratedCalor, StringComparison.Ordinal)
                || !string.Equals(previous.Original.Path, _sourcePath, StringComparison.Ordinal))
                throw new IOException("Generated Calor was edited or belongs to another original source. Keep those edits; automatic refresh is refused.");
        }
        _inputs = ConversionReceipt.CaptureInputs(
            _sourcePath, converter.GetSemanticMetadataReferences(), options, cancellationToken);
        if (_inputs.Any(input => artifacts.Any(path => Incremental.BuildStateCache.GetPathComparer()
                .Equals(CanonicalPath.Resolve(path), CanonicalPath.Resolve(input.Path)))))
            throw new IOException("Conversion artifacts must not overwrite original source or reference inputs.");
        var original = await CSharpToCalorConverter.ReadSourceFileAsync(_sourcePath, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        ConversionResult result;
        // 0.30: one named namespace with local imports needs no split, so the native
        // scoped-import projection (formerly behind --split-namespaces) is the default.
        // It falls back to the ordinary single-file path, preservation included.
        var scopedDefault = !namespaceIndex.HasValue && !projectConversion
            && converter.IsSingleScopedNamespace(original, _sourcePath);
        if (namespaceIndex.HasValue || projectConversion || scopedDefault)
        {
            var project = converter.ConvertProjectCore(
                original, _sourcePath, cancellationToken, capturePreservationContext: true);
            if (scopedDefault && (project.IsSplit || !project.Outputs[0].Conversion.Success))
                result = converter.ConvertWithPreservationContext(original, _sourcePath, cancellationToken);
            else if (namespaceIndex.HasValue
                    && (!project.IsSplit || namespaceIndex.Value >= project.Outputs.Count)
                || !namespaceIndex.HasValue && project.IsSplit)
            {
                var refused = project.Outputs[0].Conversion;
                refused.Context.AddError(
                    "Native namespace splitting was refused. No partial split output may be committed.",
                    "namespace-split-refused");
                result = new ConversionResult { Success = false, Context = refused.Context };
            }
            else
                result = project.Outputs[namespaceIndex ?? 0].Conversion;
        }
        else
            result = converter.ConvertWithPreservationContext(original, _sourcePath, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return _result = result;
    }

    internal async Task WriteAsync(ConversionResult result, CancellationToken cancellationToken = default,
        Action? outputWritten = null)
    {
        if (!ReferenceEquals(result, _result))
            throw new InvalidOperationException("Only the actual captured conversion can be committed.");
        var receipt = await PrepareReceiptAsync(result.CalorSource, cancellationToken);
        await ConversionFileWriter.WriteGenerationAsync(
            _outputPath, _before, _beforeReceipt, receipt, cancellationToken, outputWritten);
    }

    internal ConversionResult PreserveOriginal(CancellationToken cancellationToken,
        IEnumerable<ConversionIssue> validationIssues)
    {
        if (_writeAttempted || Result.Context.OriginalSource == null)
            throw new InvalidOperationException("Whole-file rescue requires the actual original conversion context before commit.");
        return _result = converter.PreserveOriginal(
            Result.Context.OriginalSource, _sourcePath, Result, cancellationToken, validationIssues);
    }

    internal async Task<ConversionReceipt> PrepareReceiptAsync(string? emittedSource,
        CancellationToken cancellationToken)
    {
        if (_writeAttempted || _generationLock == null || !Result.Success || emittedSource == null)
            throw new InvalidOperationException("A successful prepared generation can be written only once.");
        if (!string.Equals(emittedSource, Result.CalorSource, StringComparison.Ordinal))
            throw new IOException("Staged output differs from its actual captured conversion; commit refused.");
        _writeAttempted = true;
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(Result.Context.OriginalSource,
                await CSharpToCalorConverter.ReadSourceFileAsync(_sourcePath, cancellationToken), StringComparison.Ordinal))
            throw new IOException("Original C# changed during conversion; output was not refreshed.");
        return ConversionReceipt.Capture(Result, options, _inputs);
    }

    internal Task VerifyPreparedAsync(ConversionReceipt receipt, CancellationToken cancellationToken)
        => ConversionFileWriter.VerifyGenerationAsync(
            _outputPath, _before, _beforeReceipt, receipt, cancellationToken);

    internal Task FinalizeReceiptAsync(ConversionReceipt receipt)
        => ConversionFileWriter.WriteReceiptAsync(_outputPath, _beforeReceipt, receipt);

    public void Dispose()
    {
        _generationLock?.Dispose();
        _generationLock = null;
    }
}

internal static class ConversionFileWriter
{
    internal static async Task WriteGenerationAsync(
        string path, string? before, string? beforeReceipt, ConversionReceipt receipt,
        CancellationToken cancellationToken = default, Action? outputWritten = null)
    {
        var fullPath = Path.GetFullPath(path);
        await VerifyGenerationAsync(fullPath, before, beforeReceipt, receipt, cancellationToken);
        var directory = Path.GetDirectoryName(fullPath)!;
        if (fullPath.EndsWith(".calr", StringComparison.OrdinalIgnoreCase)
            && !Ids.DeclarationIdentityStore.IsAmbientRoot(directory))
        {
            var store = Ids.DeclarationIdentityStore.FindForActiveInput(fullPath)
                ?? new Ids.DeclarationIdentityStore(directory);
            store.Sync(before == null ? [] : [fullPath]);
            store.Apply([new(fullPath, before, receipt.GeneratedCalor)]);
        }
        else
        {
            await WriteAtomicAsync(fullPath, receipt.GeneratedCalor, cancellationToken: cancellationToken);
        }
        outputWritten?.Invoke();
        await WriteReceiptAsync(fullPath, beforeReceipt, receipt);
    }

    internal static async Task VerifyGenerationAsync(
        string path, string? before, string? beforeReceipt, ConversionReceipt receipt,
        CancellationToken cancellationToken)
    {
        ConversionReceipt.VerifyInputs(receipt.Inputs, cancellationToken);
        if ((File.Exists(path) ? await CSharpToCalorConverter.ReadSourceFileAsync(path, cancellationToken) : null) != before
            || (File.Exists(path + ".conversion.json")
                ? await CSharpToCalorConverter.ReadSourceFileAsync(path + ".conversion.json", cancellationToken) : null) != beforeReceipt)
            throw new IOException("Conversion output or generation context changed concurrently; refresh refused.");
        cancellationToken.ThrowIfCancellationRequested();
    }

    internal static async Task WriteReceiptAsync(string path, string? beforeReceipt, ConversionReceipt receipt)
    {
        var receiptPath = path + ".conversion.json";
        // Output and receipt are separate artifacts. An interrupted final receipt
        // write leaves a detectable mismatch, never a trusted new generation.
        try
        {
            if ((File.Exists(receiptPath) ? await CSharpToCalorConverter.ReadSourceFileAsync(receiptPath) : null) != beforeReceipt)
                throw new IOException("Generation context changed concurrently.");
            await WriteAtomicAsync(receiptPath, receipt.Serialize());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.DecoderFallbackException)
        {
            throw new IOException(
                $"Output was written but its conversion context could not be saved: {ex.Message} Keep both artifacts; automatic refresh is blocked.", ex);
        }
    }

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

        if (fullPath.EndsWith(".calr", StringComparison.Ordinal)
            && !Ids.DeclarationIdentityStore.IsAmbientRoot(directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (encoding != null && encoding.CodePage != System.Text.Encoding.UTF8.CodePage)
                throw new InvalidOperationException("Identity-managed Calor output must use UTF-8.");
            var store = Ids.DeclarationIdentityStore.FindForActiveInput(fullPath)
                ?? new Ids.DeclarationIdentityStore(directory);
            var before = File.Exists(fullPath) ? await File.ReadAllTextAsync(fullPath, cancellationToken) : null;
            store.Sync(before == null ? [] : [fullPath]);
            store.Apply([new(fullPath, before, content)]);
            return;
        }

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
    /// Converts C# to Calor with optional <see cref="ConversionOptions"/>.
    /// </summary>
    public static async Task<ConversionResult> ConvertCSharpToCalorAsync(string csharpPath, string? outputPath, ConversionOptions? options)
    {
        var converter = options != null ? new CSharpToCalorConverter(options) : new CSharpToCalorConverter();
        return await converter.ConvertFileAndSaveAsync(csharpPath, outputPath);
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
