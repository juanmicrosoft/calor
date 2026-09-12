// NON-SHIPPING observation driver. No compiler policy or converted source is mutated.
using System.Collections.Immutable;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Binding.BoundTypes;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Calor.RoundTrip.Harness;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Binder = Calor.Compiler.Binding.Binder;
using Annotation = Microsoft.CodeAnalysis.NullableAnnotation;

var root = Path.GetFullPath(args[0]);
var reports = Path.GetFullPath(args[1]);
var output = Path.GetFullPath(args[2]);
Directory.CreateDirectory(output);
var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
var checker = typeof(Binder).Assembly.GetType("Calor.Compiler.Binding.NullabilityChecker", true)!
    .GetMethod("IsPossiblyNullAssignedTo", BindingFlags.Static | BindingFlags.Public)!;
var targetBuilder = typeof(Binder).GetMethod("TryBuildStringTarget", BindingFlags.Static | BindingFlags.NonPublic)!;
var projects = new List<object>();
foreach (var name in ProjectConfigs.KnownProjects)
{
    var reportPath = Path.Combine(reports, name + "-roundtrip.json");
    if (!File.Exists(reportPath))
    {
        projects.Add(new { name, state = "report-unavailable", candidateRejections = (int?)null });
        continue;
    }
    using var reportDocument = JsonDocument.Parse(File.ReadAllText(reportPath));
    var report = reportDocument.RootElement;
    var config = ProjectConfigs.Get(name, Path.Combine(root, "bench/corpus"), "dotnet")!;
    var library = Path.Combine(config.OriginalProjectPath, config.LibrarySourceRelativePath);
    var files = report.GetProperty("file_detail").EnumerateArray().ToArray();
    var contexts = files.SelectMany(f => f.GetProperty("validated_contexts").EnumerateArray())
        .DistinctBy(c => c.GetRawText()).ToArray();
    var sourceRows = new List<object>();
    var contextRows = new List<object>();
    var boundRows = new List<object>();
    foreach (var contextElement in contexts)
    {
        var context = contextElement.Deserialize<FileContextDetail>()!;
        if (!Enum.TryParse<LanguageVersion>(context.LanguageVersion, out var languageVersion) &&
            !LanguageVersionFacts.TryParse(context.LanguageVersion, out languageVersion))
            throw new InvalidOperationException("Unknown observed language version: " + context.LanguageVersion);
        var parseOptions = new CSharpParseOptions(languageVersion,
            preprocessorSymbols: context.DefinedSymbols);
        var trees = new List<SyntaxTree>();
        var inputRows = new List<object>();
        foreach (var encoded in context.CompileInputHashes)
        {
            var rest = encoded.AsSpan();
            var relative = Decode(ref rest);
            var expected = Decode(ref rest);
            var original = Path.GetFullPath(Path.Combine(library, relative));
            var observedCopy = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(context.ProjectFile)!, relative));
            var chosen = new[] { original, observedCopy }.FirstOrDefault(p =>
                File.Exists(p) && HashFile(p).Equals(expected, StringComparison.OrdinalIgnoreCase));
            inputRows.Add(new { relative, expected, chosen, state = chosen is null ? "missing-or-hash-mismatch" : "verified" });
            if (chosen != null)
                trees.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(chosen), parseOptions, chosen));
        }
        var references = new List<MetadataReference>();
        var referenceRows = new List<object>();
        foreach (var reference in context.References)
        {
            if (reference.Path is not null && File.Exists(reference.Path))
            {
                var actual = HashFile(reference.Path);
                var match = string.IsNullOrEmpty(reference.Sha256) ? (bool?)null :
                    reference.Sha256.Equals(actual, StringComparison.OrdinalIgnoreCase);
                referenceRows.Add(new { reference, actualSha256 = actual, matchesE1Hash = match });
                if (match == false)
                    throw new InvalidOperationException("Reference changed since E1 capture: " + reference.Path);
                references.Add(MetadataReference.CreateFromFile(reference.Path,
                    MetadataReferenceProperties.Assembly.WithAliases(reference.Aliases.ToImmutableArray())));
            }
            else
                referenceRows.Add(new { reference, unavailable = true });
        }
        var nullable = context.CompilationProperties.GetValueOrDefault("Nullable", "disable");
        var compilation = CSharpCompilation.Create("D1_" + name, trees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: Enum.Parse<NullableContextOptions>(nullable, true),
                allowUnsafe: context.CompilationProperties.GetValueOrDefault("AllowUnsafeBlocks") == "true"));
        contextRows.Add(new
        {
            context.ProjectFile, context.TargetFramework, context.CompilationProperties,
            context.DefinedSymbols, inputs = inputRows, references = referenceRows,
            reconstructionErrors = compilation.GetDiagnostics()
                .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).Select(d => d.ToString()).ToArray(),
            limit = "Source-side Roslyn reconstruction, NOT private Calor MetadataBinder reference selection."
        });
        foreach (var tree in trees.Where(t =>
            t.FilePath.StartsWith(library + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
            !t.FilePath.Contains("/obj/", StringComparison.Ordinal)))
        {
            var relative = Path.GetRelativePath(config.OriginalProjectPath, tree.FilePath);
            var reportFile = files.FirstOrDefault(f => f.GetProperty("path").GetString() == relative);
            var excluded = report.GetProperty("evidence").GetProperty("Inputs").EnumerateArray()
                .FirstOrDefault(f => f.GetProperty("Path").GetString() == relative);
            sourceRows.Add(new
            {
                file = relative, sourceSha256 = HashFile(tree.FilePath),
                e1CandidateId = reportFile.ValueKind == JsonValueKind.Undefined ? null : StringProperty(reportFile.GetProperty("candidate"), "CandidateId"),
                e1Status = reportFile.ValueKind == JsonValueKind.Undefined ? null : reportFile.GetProperty("status").GetString(),
                input = excluded.ValueKind == JsonValueKind.Undefined ? (JsonElement?)null : excluded.Clone(),
                directives = tree.GetRoot().DescendantTrivia(descendIntoTrivia: true)
                    .Select(t => t.GetStructure()).OfType<NullableDirectiveTriviaSyntax>()
                    .Select(d => new { line = d.GetLocation().GetLineSpan().StartLinePosition.Line + 1, text = d.ToString(), d.IsActive }).ToArray(),
                boundaries = ObserveSource(compilation.GetSemanticModel(tree), tree)
            });
        }
    }
    foreach (var file in files)
    {
        var candidate = file.GetProperty("candidate");
        var converted = StringProperty(candidate, "ConvertedCalor");
        if (!string.IsNullOrEmpty(converted))
            boundRows.Add(new
            {
                file = file.GetProperty("path").GetString(), status = file.GetProperty("status").GetString(),
                candidateId = StringProperty(candidate, "CandidateId"),
                observation = ObserveBound(converted, file.GetProperty("path").GetString()!)
            });
    }
    projects.Add(new
    {
        name, reportPath, reportSha256 = HashFile(reportPath),
        physicalInputs = report.GetProperty("evidence").GetProperty("Inputs").EnumerateArray().Select(input =>
        {
            var path = Path.Combine(config.OriginalProjectPath, input.GetProperty("Path").GetString()!);
            return new
            {
                input = input.Clone(), actualSha256 = HashFile(path),
                physicalNullableDirectiveLines = File.ReadAllLines(path).Select((text, index) => new { text, line = index + 1 })
                    .Where(l => l.text.TrimStart().StartsWith("#nullable", StringComparison.Ordinal)).ToArray()
            };
        }).ToArray(),
        declaredNullableSettings = Directory.EnumerateFiles(config.OriginalProjectPath, "*", SearchOption.AllDirectories)
            .Where(p => !p.Contains("/obj/") && !p.Contains("/bin/") &&
                (p.EndsWith(".csproj") || p.EndsWith(".props") || p.EndsWith(".targets")))
            .Select(p => new
            {
                path = Path.GetRelativePath(config.OriginalProjectPath, p), sha256 = HashFile(p),
                nullableLines = File.ReadAllLines(p).Select((text, index) => new { text, line = index + 1 })
                    .Where(l => l.text.Contains("<Nullable", StringComparison.Ordinal)).ToArray()
            }).Where(p => p.nullableLines.Length != 0).ToArray(),
        measuredE1 = report.GetProperty("evidence").GetProperty("Provenance").Clone(),
        denominators = report.GetProperty("evidence_counts").Clone(),
        contexts = contextRows, sourceFiles = sourceRows, convertedFiles = boundRows,
        limit = "Source candidate obligations are not compiler rejection predictions. No mapped source-to-Calor boundary identity or private selected-reference proof."
    });
}
Write("corpus-observations.json", projects);

const string control = """
#nullable disable
namespace D1;
public class Item { public Item Next() => this; }
public static class Cases {
    public static Item Echo(Item value) => value;
    public static Item New() => new Item();
    public static Item LocalNew() { var value = new Item(); return value; }
    public static Item Coalesce(Item value) => value ?? new Item();
    public static Item Default(Item value) => value ?? default(Item);
    public static string Literal() => "safe";
    public static Item Receiver(Item value) => value.Next();
#nullable enable
    public static Item Explicit(Item? value) => value!;
    public static Item Declared(Item value) => value;
    public static Item? NullableTarget(Item? value) => value;
}
""";
var controlTree = CSharpSyntaxTree.ParseText(control, path: "controls.cs");
var controlCompilation = CSharpCompilation.Create("D1Controls", [controlTree],
    GeneratedCSharpCompiler.References, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
var conversion = new CSharpToCalorConverter(new ConversionOptions { ModuleName = "D1" }).Convert(control, "controls.cs");
var result = string.IsNullOrWhiteSpace(conversion.CalorSource) ? null :
    Calor.Compiler.Program.Compile(conversion.CalorSource, "controls.calr",
        new Calor.Compiler.CompilationOptions
        {
            EnforceEffects = false, VerifyContracts = false
        });
Write("controls.json", new
{
    originalSource = control, originalSha256 = HashText(control),
    sourceObservations = ObserveSource(controlCompilation.GetSemanticModel(controlTree), controlTree),
    originalRuntime = RunControls(controlCompilation),
    conversion.Success, conversion.CalorSource,
    conversionIssues = conversion.Issues.Select(i => new { i.Message }).ToArray(),
    conversionLosses = conversion.Losses.Select(l => new { kind = l.Kind.ToString(), l.Feature, l.Description }).ToArray(),
    compileDiagnostics = result?.Diagnostics.Select(Diagnostic).ToArray(),
    generatedCSharp = result?.GeneratedCode,
    convertedRuntime = result?.GeneratedCode is { Length: > 0 } code ? RunControls(CSharpCompilation.Create("D1Converted",
        [CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble + code)],
        GeneratedCSharpCompiler.References, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))) : null,
    bound = conversion.CalorSource is { Length: > 0 } calor ? ObserveBound(calor, "controls.calr") : null,
    policy = "Non-shipping source-side contract projection only. Bare C# declarations under disable do not assert non-null; mapping them to non-null Calor is not behavior-preserving."
});

const string legacySource = """
#nullable disable
namespace Legacy;
public class Item { }
public static class API { public static Item Read() => null; }
""";
var legacyCompilation = CSharpCompilation.Create("D1Legacy",
    [CSharpSyntaxTree.ParseText(legacySource)], GeneratedCSharpCompiler.References,
    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
using var legacyBytes = new MemoryStream();
if (!legacyCompilation.Emit(legacyBytes).Success) throw new InvalidOperationException("Legacy calibration did not compile.");
const string metadataSource = """
#nullable enable
public static class MetadataCases {
    public static Legacy.Item FromLibrary() => Legacy.API.Read();
    public static System.IO.DirectoryInfo Parent() => System.IO.Directory.GetParent("/");
    public static Missing Unresolved() => null;
}
""";
var metadataTree = CSharpSyntaxTree.ParseText(metadataSource, path: "metadata-controls.cs");
var metadataCompilation = CSharpCompilation.Create("D1MetadataControls", [metadataTree],
    GeneratedCSharpCompiler.References.Append(MetadataReference.CreateFromImage(legacyBytes.ToArray())),
    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
Write("metadata-controls.json", new
{
    legacySource, legacyAssemblySha256 = Convert.ToHexString(SHA256.HashData(legacyBytes.ToArray())),
    metadataSource,
    boundaries = ObserveSource(metadataCompilation.GetSemanticModel(metadataTree), metadataTree),
    diagnostics = metadataCompilation.GetDiagnostics().Select(d => d.ToString()).ToArray(),
    references = GeneratedCSharpCompiler.References.OfType<PortableExecutableReference>().Select(r => new
    {
        r.FilePath, sha256 = r.FilePath == null ? null : HashFile(r.FilePath),
        identity = (metadataCompilation.GetAssemblyOrModuleSymbol(r) as IAssemblySymbol)?.Identity.ToString()
    }).ToArray(),
    limitation = "Actual reference set of this explicit calibration compilation, NOT private Binder selection, and legacy fixture is not an observed NuGet package."
});

object[] ObserveSource(SemanticModel model, SyntaxTree tree)
{
    var rows = new List<object>();
    foreach (var node in tree.GetRoot().DescendantNodes())
    {
        if (node is VariableDeclaratorSyntax { Initializer.Value: { } value } variable &&
            variable.Parent is VariableDeclarationSyntax declaration && !declaration.Type.IsVar &&
            declaration.Parent is LocalDeclarationStatementSyntax &&
            model.GetDeclaredSymbol(variable) is ILocalSymbol local)
            Add("explicit-local-initialization", value, local.Type, local.NullableAnnotation, local, null);
        if (node is ReturnStatementSyntax { Expression: { } expression } &&
            model.GetEnclosingSymbol(node.SpanStart) is IMethodSymbol { MethodKind: MethodKind.Ordinary } method)
            Add("native-return", expression, method.ReturnType, method.ReturnNullableAnnotation, method, null);
        if (node is ArrowExpressionClauseSyntax arrow && arrow.Parent is MethodDeclarationSyntax declarationMethod &&
            model.GetDeclaredSymbol(declarationMethod) is IMethodSymbol methodSymbol)
            Add("native-return", arrow.Expression, methodSymbol.ReturnType, methodSymbol.ReturnNullableAnnotation, methodSymbol, null);
        if (node is ArgumentSyntax argument && argument.Parent?.Parent is InvocationExpressionSyntax invocation)
        {
            var operation = model.GetOperation(argument) as IArgumentOperation;
            var call = model.GetOperation(invocation) as IInvocationOperation;
            Add("method-input", argument.Expression, operation?.Parameter?.Type,
                operation?.Parameter?.NullableAnnotation ?? Annotation.None, operation?.Parameter, new
                {
                    selectedMethod = call?.TargetMethod.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    selectedAssembly = call?.TargetMethod.ContainingAssembly.Identity.ToString(),
                    receiverType = Type(call?.Instance?.Type),
                    parameter = operation?.Parameter?.Name,
                    parameterOrdinal = operation?.Parameter?.Ordinal,
                    argumentKind = operation?.ArgumentKind.ToString(),
                    refKind = operation?.Parameter?.RefKind.ToString(),
                    candidateReason = model.GetSymbolInfo(invocation).CandidateReason.ToString()
                });
        }
    }
    return rows.ToArray();
    void Add(string boundary, ExpressionSyntax expression, ITypeSymbol? target, Annotation targetAnnotation,
        ISymbol? receivingSymbol, object? mapping)
    {
        var info = model.GetTypeInfo(expression);
        var symbol = model.GetSymbolInfo(expression).Symbol;
        var annotation = symbol switch
        {
            IMethodSymbol m => m.ReturnNullableAnnotation,
            IPropertySymbol p => p.NullableAnnotation,
            IFieldSymbol f => f.NullableAnnotation,
            ILocalSymbol l => l.NullableAnnotation,
            IParameterSymbol p => p.NullableAnnotation,
            _ => info.Nullability.Annotation
        };
        var knownSafe = expression is ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax ||
            expression is LiteralExpressionSyntax literal && !literal.IsKind(SyntaxKind.NullLiteralExpression)
                && !literal.IsKind(SyntaxKind.DefaultLiteralExpression);
        var knownNull = expression.IsKind(SyntaxKind.NullLiteralExpression) ||
            expression.IsKind(SyntaxKind.DefaultLiteralExpression) ||
            expression is DefaultExpressionSyntax && info.Type?.IsReferenceType == true;
        var direct = symbol is IMethodSymbol or IPropertySymbol or IFieldSymbol or ILocalSymbol or IParameterSymbol;
        var errors = model.GetDiagnostics(expression.Span)
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).Select(d => d.ToString()).ToArray();
        var unsupportedReceiving = receivingSymbol is IParameterSymbol { RefKind: not RefKind.None } ||
            receivingSymbol is IMethodSymbol { ReturnsByRef: true } or IMethodSymbol { ReturnsByRefReadonly: true };
        var inScope = !unsupportedReceiving && errors.Length == 0 && NominalReference(target) &&
            (knownNull || NominalReference(info.Type) && model.Compilation.ClassifyConversion(info.Type!, target!).IsImplicit);
        var category = !inScope ? "out-of-scope-or-unresolved" :
            targetAnnotation == Annotation.Annotated ? "nullable-target" :
            knownSafe ? "known-safe-expression" :
            knownNull ? "explicit-null-expression" :
            !direct ? "expression-transfer-unassessed" :
            annotation == Annotation.Annotated ? "explicit-nullable" :
            annotation == Annotation.None ? "genuinely-oblivious-source" : "declared-not-annotated";
        var location = expression.GetLocation().GetLineSpan();
        rows.Add(new
        {
            boundary, expression = expression.ToString(), expressionKind = expression.Kind().ToString(),
            span = new { expression.SpanStart, expression.Span.Length, line = location.StartLinePosition.Line + 1, column = location.StartLinePosition.Character + 1 },
            source = Type(info.Type), sourceAnnotation = annotation.ToString(),
            target = Type(target), targetAnnotation = targetAnnotation.ToString(),
            sourceSymbol = symbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            sourceSymbolAssembly = symbol?.ContainingAssembly?.Identity.ToString(),
            sourceSymbolOrigin = symbol == null ? "unresolved" : symbol.Locations.Any(l => l.IsInSource) ? "source" : "metadata",
            sourceMethodKind = (symbol as IMethodSymbol)?.MethodKind.ToString(),
            annotationOrigin = symbol is IMethodSymbol { MethodKind: MethodKind.DelegateInvoke }
                ? "instantiated-delegate-signature-not-evidence-of-unannotated-BCL-definition"
                : symbol?.Locations.Any(l => l.IsInSource) == true ? "source-declaration"
                : symbol == null ? "expression-or-unresolved" : "metadata-signature",
            receivingSymbol = receivingSymbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            effectiveContext = model.GetNullableContext(expression.SpanStart).ToString(),
            category, knownSafeExpression = knownSafe, unsupportedReceiving, mapping, errors,
            hypotheticalBareTargetObligation = category is "genuinely-oblivious-source" or "explicit-nullable" or "explicit-null-expression",
            caveat = "Not an activated Calor rejection. C# None target does not promise non-null. No flow state or null-forgiving guarantee is used."
        });
    }
}

object ObserveBound(string source, string path)
{
    var bag = new DiagnosticBag();
    var ast = new Parser(new Lexer(source, bag).TokenizeAllForParser(), bag).Parse();
    if (bag.HasErrors) return new { parseErrors = bag.Select(Diagnostic).ToArray() };
    var bound = new Binder(bag, path).Bind(ast);
    var boundaries = new List<object>();
    var calls = new List<object>();
    var astBindings = Descendants(ast).OfType<BindStatementNode>().GroupBy(b => b.Span.Start)
        .ToDictionary(g => g.Key, g => g.Select(b => b.TypeName).Distinct().ToArray());
    foreach (var function in bound.Functions)
    foreach (var node in Walk(function))
    {
        if (node is BoundBindStatement { Initializer: { } initializer } binding &&
            astBindings.TryGetValue(binding.Span.Start, out var explicitTypes) &&
            explicitTypes is [not null])
            Add("explicit-local-initialization", initializer, explicitTypes[0]!, function.Symbol.Name);
        if (node is BoundReturnStatement { Expression: { } returned } &&
            function.MemberKind is BoundMemberKind.TopLevelFunction or BoundMemberKind.Method)
            Add("native-return", returned, function.Symbol.ReturnType, function.Symbol.Name);
        if (node is BoundCallExpression call)
            calls.Add(new
            {
                call.Target, span = call.Span, returnType = BoundTypeInfo(call.Type),
                call.ResolvedTypeName, call.ResolvedMethodName, call.ResolvedParameterTypes,
                nativeResolvedSymbol = call.ResolvedSymbol?.DisplaySignature,
                nativeCandidateSymbols = call.ResolvedSymbols.Select(s => s.DisplaySignature).ToArray(),
                receiver = call.Receiver == null ? null : BoundTypeInfo(call.Receiver.Type),
                arguments = call.Arguments.Select(a => BoundTypeInfo(a.Type)).ToArray(),
                call.ArgumentNames, call.ArgumentModifiers,
                disposition = "Argument-policy projection unassessed: pre-N3 selected map not exposed; strings/names alone are not identity proof."
            });
    }
    return new
    {
        diagnostics = bag.Select(Diagnostic).ToArray(), boundaries, expressionCalls = calls,
        statementCallCount = Walk(bound).OfType<BoundCallStatement>().Count(),
        ambiguousBindingTargetSpans = astBindings.Where(p => p.Value.Length != 1).Select(p => p.Key).ToArray(),
        limitation = "Statement-call mappings unassessed; separate expression calls are not all call forms. Binder private selected references not observed."
    };
    void Add(string boundary, BoundExpression expression, string targetName, string function)
    {
        object?[] targetArgs = [targetName, null];
        var supported = (bool)targetBuilder.Invoke(null, targetArgs)!;
        var target = targetArgs[1] as BoundType;
        var current = supported && (bool)checker.Invoke(null, [expression, target!])!;
        boundaries.Add(new
        {
            boundary, function, span = expression.Span, expressionKind = expression.GetType().Name,
            source = BoundTypeInfo(expression.Type), targetName,
            target = target == null ? null : BoundTypeInfo(target), currentPredicate = current,
            shadowConservative = target is NominalBoundType t && t.QualifiedName is not ("STRING" or "string" or "str")
                ? "unassessed-target-identity" : "outside-D1-nominal-projection",
            reason = "Existing target builder constructs names without declaration/Roslyn identity. Do not widen on short-name equality or infer unknown safe."
        });
    }
}

static IEnumerable<BoundNode> Walk(BoundNode node) =>
    new[] { node }.Concat(node.ChildNodes.SelectMany(Walk));
static IEnumerable<AstNode> Descendants(AstNode node)
{
    yield return node;
    foreach (var property in node.GetType().GetProperties())
    {
        if (property.GetIndexParameters().Length != 0) continue;
        var value = property.GetValue(node);
        if (value is AstNode child)
            foreach (var descendant in Descendants(child)) yield return descendant;
        else if (value is System.Collections.IEnumerable children and not string)
            foreach (var item in children.OfType<AstNode>())
                foreach (var descendant in Descendants(item)) yield return descendant;
    }
}
static object BoundTypeInfo(BoundType type) => new
{
    kind = type.GetType().Name, type.DisplayString,
    annotation = type switch
    {
        NominalBoundType n => n.NullableAnnotation.ToString(),
        ArrayBoundType a => a.NullableAnnotation.ToString(),
        GenericInstantiationBoundType g => g.NullableAnnotation.ToString(), _ => null
    },
    declaration = (type as NominalBoundType)?.Declaration?.Name,
    roslynIdentity = (type as NominalBoundType)?.RoslynSymbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
    roslynIsReferenceType = (type as NominalBoundType)?.RoslynSymbol?.IsReferenceType,
    unresolved = (type as UnresolvedBoundType)?.Reason
};
static bool NominalReference(ITypeSymbol? type) => type is INamedTypeSymbol
    { IsReferenceType: true, IsGenericType: false, TypeKind: not TypeKind.Error and not TypeKind.Delegate, SpecialType: not SpecialType.System_String };
static object? Type(ITypeSymbol? type) => type == null ? null : new
{
    display = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
    kind = type.TypeKind.ToString(), type.IsReferenceType, type.IsValueType,
    annotation = type.NullableAnnotation.ToString(), assembly = type.ContainingAssembly?.Identity.ToString()
};
static object Diagnostic(Calor.Compiler.Diagnostics.Diagnostic d) =>
    new { d.Code, severity = d.Severity.ToString(), d.Message, d.Span, d.BindingContext };
static string Decode(ref ReadOnlySpan<char> encoded)
{
    var colon = encoded.IndexOf(':');
    var count = int.Parse(encoded[..colon]);
    var result = encoded.Slice(colon + 1, count).ToString();
    encoded = encoded[(colon + 1 + count)..];
    return result;
}
static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
static string? StringProperty(JsonElement element, string key) => element.TryGetProperty(key, out var value) ? value.GetString() : null;
static string HashText(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
void Write(string file, object value) => File.WriteAllText(Path.Combine(output, file), JsonSerializer.Serialize(value, jsonOptions));
static object RunControls(CSharpCompilation compilation)
{
    using var bytes = new MemoryStream();
    var emit = compilation.Emit(bytes);
    if (!emit.Success) return new { errors = emit.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).Select(d => d.ToString()).ToArray() };
    var assembly = Assembly.Load(bytes.ToArray());
    var type = assembly.GetType("D1.Cases");
    if (type == null) return new { missingType = true, actualTypes = assembly.GetTypes().Select(t => t.FullName).ToArray() };
    var item = assembly.GetType("D1.Item")!;
    return new[] { "Echo", "New", "LocalNew", "Coalesce", "Default", "Literal", "Declared", "NullableTarget" }.Select(name =>
    {
        var method = type.GetMethod(name)!;
        return new
        {
            method = name,
            attempts = (method.GetParameters().Length == 0 ? new[] { false } : new[] { false, true }).Select(present =>
            {
                var value = method.Invoke(null, method.GetParameters().Length == 0 ? [] : [present ? Activator.CreateInstance(item) : null]);
                return new { presentInput = present, resultIsNull = value == null, stringResult = value as string };
            }).ToArray()
        };
    }).ToArray();
}
