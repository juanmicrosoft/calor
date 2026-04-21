using Calor.Compiler.Analysis;
using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects.Manifests;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.Effects;

/// <summary>
/// SCC-based interprocedural effect enforcement pass.
/// Uses Tarjan's algorithm to compute strongly connected components,
/// then processes them in reverse topological order to infer and verify effects.
/// </summary>
public sealed class EffectEnforcementPass
{
    private readonly DiagnosticBag _diagnostics;
    private readonly EffectResolver _resolver;
    private readonly UnknownCallPolicy _policy;
    private readonly bool _strictEffects;

    // Delegated call graph analysis (populated by Enforce)
    private CallGraphAnalysis _callGraphAnalysis = null!;

    // Maps function ID to computed effects
    private readonly Dictionary<string, EffectSet> _computedEffects = new(StringComparer.Ordinal);

    public EffectEnforcementPass(
        DiagnosticBag diagnostics,
        UnknownCallPolicy policy = UnknownCallPolicy.Strict,
        EffectResolver? resolver = null,
        bool strictEffects = false,
        string? projectDirectory = null,
        string? solutionDirectory = null)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _policy = policy;
        _strictEffects = strictEffects;

        // Initialize the effect resolver with manifests
        _resolver = resolver ?? new EffectResolver();
        _resolver.Initialize(projectDirectory, solutionDirectory);
    }

    /// <summary>
    /// Enforces effect declarations across all functions and class methods in the module.
    /// </summary>
    public void Enforce(ModuleNode module)
    {
        // Phase 1: Build function map and call graph (includes functions and methods)
        _callGraphAnalysis = CallGraphAnalysis.Build(module);

        // Phase 2+3: Process SCCs in reverse topological order
        // (Tarjan produces them in reverse topological order already)
        foreach (var scc in _callGraphAnalysis.StronglyConnectedComponents)
        {
            ProcessScc(scc);
        }

        // Phase 4: Check each function's computed effects against declared effects
        foreach (var function in module.Functions)
        {
            CheckEffects(function);
        }

        // Phase 4b: Also check class methods and constructors
        foreach (var cls in module.Classes)
        {
            foreach (var method in cls.Methods)
            {
                var wrappedId = $"{cls.Name}.{method.Id}";
                if (_callGraphAnalysis.Functions.TryGetValue(wrappedId, out var wrapped))
                {
                    CheckEffects(wrapped);
                }
            }
            // Note: Constructors participate in the call graph (for SCC analysis)
            // but are not checked for effects here because:
            // 1. CTOR has no E{...} declaration syntax yet
            // 2. Constructors inherently assign to fields (mut) which would always fail
            // Constructor enforcement requires language-level E support on CTOR first.
        }
    }

    /// <summary>
    /// Resolves a call target string to an internal function ID.
    /// Thin wrapper that delegates to CallGraphAnalysis.
    /// </summary>
    private string? ResolveToInternalId(string callee)
    {
        return _callGraphAnalysis.ResolveToInternalId(callee);
    }

    private void ProcessScc(List<string> scc)
    {
        // For single-function SCCs with no self-recursion, compute effects directly
        if (scc.Count == 1)
        {
            var functionId = scc[0];
            var function = _callGraphAnalysis.Functions[functionId];
            var effects = InferEffects(function, new HashSet<string>());
            _computedEffects[functionId] = effects;
            return;
        }

        // For multi-function SCCs (mutual recursion), iterate until fixpoint
        var changed = true;
        var iterations = 0;
        const int maxIterations = 100;

        // Initialize with empty effects
        foreach (var functionId in scc)
        {
            _computedEffects[functionId] = EffectSet.Empty;
        }

        while (changed && iterations < maxIterations)
        {
            changed = false;
            iterations++;

            foreach (var functionId in scc)
            {
                var function = _callGraphAnalysis.Functions[functionId];
                var newEffects = InferEffects(function, new HashSet<string>(scc));
                var oldEffects = _computedEffects[functionId];

                if (!newEffects.Equals(oldEffects))
                {
                    _computedEffects[functionId] = newEffects;
                    changed = true;
                }
            }
        }

        if (changed)
        {
            _diagnostics.ReportWarning(
                _callGraphAnalysis.Functions[scc[0]].Span,
                "Calor0600",
                $"Effect fixpoint iteration did not converge after {maxIterations} iterations for mutually recursive functions. Effects may be incomplete.");
        }
    }

    private EffectSet InferEffects(FunctionNode function, HashSet<string> sccMembers)
    {
        var context = new InferenceContext(
            _resolver, _computedEffects,
            _callGraphAnalysis.Functions,
            _callGraphAnalysis.FunctionNameToId,
            _callGraphAnalysis.MethodNameToIds,
            sccMembers, _policy, _strictEffects, _diagnostics, function.Id);
        var inferrer = new EffectInferrer(context);
        return inferrer.InferFromStatements(function.Body);
    }

    private void CheckEffects(FunctionNode function)
    {
        var declaredEffects = GetDeclaredEffects(function);
        var computedEffects = _computedEffects.GetValueOrDefault(function.Id, EffectSet.Empty);

        // Check if computed effects are a subset of declared effects
        if (!computedEffects.IsSubsetOf(declaredEffects))
        {
            var forbidden = computedEffects.Except(declaredEffects).ToList();

            // In permissive mode, demote forbidden-effect errors to warnings
            var severity = _policy == UnknownCallPolicy.Permissive
                ? DiagnosticSeverity.Warning
                : DiagnosticSeverity.Error;

            // Compute the full correct effect set for the fix
            var correctEffects = declaredEffects.Union(computedEffects);
            // §E{} syntax uses comma-separated codes without spaces
            var correctEffectStr = correctEffects.ToDisplayString().Replace(", ", ",");
            var fixSpan = function.Effects?.Span ?? function.Span;
            var filePath = _diagnostics.CurrentFilePath ?? "unknown";

            // Generate fix: replace existing §E{...} line or insert new one
            SuggestedFix? fix = null;
            if (function.Effects != null)
            {
                // Replace the entire §E{...} line to avoid span-length issues
                // §E{...} always occupies its own line with leading whitespace
                var effectLine = function.Effects.Span.Line;
                fix = new SuggestedFix(
                    $"Update effect declaration to §E{{{correctEffectStr}}}",
                    TextEdit.Replace(filePath,
                        effectLine, 1,
                        effectLine + 1, 1,
                        $"  §E{{{correctEffectStr}}}\n"));
            }
            else
            {
                // Insert §E{...} after the last §O or §I line
                var insertLine = function.Span.Line + 1; // Default: after function declaration
                if (function.Output != null)
                    insertLine = function.Output.Span.Line + 1;
                else if (function.Parameters.Count > 0)
                    insertLine = function.Parameters[^1].Span.Line + 1;

                fix = new SuggestedFix(
                    $"Add effect declaration §E{{{correctEffectStr}}}",
                    TextEdit.Insert(filePath, insertLine, 1, $"  §E{{{correctEffectStr}}}\n"));
            }

            foreach (var (kind, value) in forbidden)
            {
                // Find the call chain that leads to this effect
                var chain = FindCallChain(function.Id, kind, value);
                var chainStr = chain.Count > 0 ? $"\n  Call chain: {string.Join(" → ", chain)}" : "";

                var message = $"Function '{function.Name}' uses effect '{EffectSetExtensions.ToSurfaceCode(kind, value)}' but does not declare it{chainStr}";

                if (fix != null)
                {
                    _diagnostics.ReportWithFix(
                        fixSpan,
                        DiagnosticCode.ForbiddenEffect,
                        message,
                        fix,
                        severity);
                    fix = null; // Only attach fix to the first forbidden effect diagnostic
                }
                else
                {
                    _diagnostics.Report(fixSpan, DiagnosticCode.ForbiddenEffect, message, severity);
                }
            }
        }
    }

    private EffectSet GetDeclaredEffects(FunctionNode function)
    {
        if (function.Effects == null || function.Effects.Effects.Count == 0)
        {
            return EffectSet.Empty;
        }

        // The EffectsNode.Effects dictionary is populated by InterpretEffectsAttributes/ExpandEffectCode
        // in the parser. Keys are categories ("io", "mutation", etc.) and values are internal names
        // ("console_write", "database_write") — potentially comma-separated for multiple effects
        // in the same category.
        //
        // We build (EffectKind, string) tuples directly to match the internal representation
        // used by the enforcement pass and manifest resolver.
        var effects = new List<(EffectKind Kind, string Value)>();
        foreach (var kv in function.Effects.Effects)
        {
            var kind = ParseEffectCategory(kv.Key);
            var values = kv.Value.Split(',');
            foreach (var value in values)
            {
                var trimmedValue = value.Trim();
                if (!string.IsNullOrEmpty(trimmedValue))
                {
                    effects.Add((kind, trimmedValue));
                }
            }
        }
        return EffectSet.FromInternal(effects);
    }

    internal static EffectKind ParseEffectCategory(string category)
    {
        return category.ToLowerInvariant() switch
        {
            "io" => EffectKind.IO,
            "mutation" => EffectKind.Mutation,
            "memory" => EffectKind.Memory,
            "exception" => EffectKind.Exception,
            "nondeterminism" => EffectKind.Nondeterminism,
            _ => EffectKind.Unknown
        };
    }

    private static (string TypeName, string MethodName) ParseCallTargetForChain(string target)
    {
        var lastDot = target.LastIndexOf('.');
        if (lastDot <= 0)
            return ("", "");

        var methodName = target[(lastDot + 1)..];
        var typePart = target[..lastDot];

        if (!typePart.Contains('.'))
        {
            typePart = MapShortTypeNameToFullName(typePart);
        }

        return (typePart, methodName);
    }

    /// <summary>
    /// Maps common short type names to fully-qualified names for manifest resolution.
    /// Used by both ParseCallTarget (in EffectInferrer) and ParseCallTargetForChain.
    /// </summary>
    internal static string MapShortTypeNameToFullName(string shortName) => shortName switch
    {
        // BCL types
        "Console" => "System.Console",
        "File" => "System.IO.File",
        "Directory" => "System.IO.Directory",
        "Path" => "System.IO.Path",
        "Random" => "System.Random",
        "DateTime" => "System.DateTime",
        "Environment" => "System.Environment",
        "Process" => "System.Diagnostics.Process",
        "HttpClient" => "System.Net.Http.HttpClient",
        "Math" => "System.Math",
        "Guid" => "System.Guid",
        "Enumerable" => "System.Linq.Enumerable",
        "String" => "System.String",
        "Int32" => "System.Int32",
        "Int64" => "System.Int64",
        "Double" => "System.Double",
        "Boolean" => "System.Boolean",
        "Convert" => "System.Convert",
        "Array" => "System.Array",
        "StringBuilder" => "System.Text.StringBuilder",
        "Stopwatch" => "System.Diagnostics.Stopwatch",
        "Debug" => "System.Diagnostics.Debug",
        "Trace" => "System.Diagnostics.Trace",
        "Thread" => "System.Threading.Thread",
        "Task" => "System.Threading.Tasks.Task",
        "JsonSerializer" => "System.Text.Json.JsonSerializer",
        "JsonDocument" => "System.Text.Json.JsonDocument",
        "Regex" => "System.Text.RegularExpressions.Regex",
        // Microsoft.Extensions.Logging
        "ILogger" => "Microsoft.Extensions.Logging.ILogger",
        "LoggerExtensions" => "Microsoft.Extensions.Logging.LoggerExtensions",
        "ILoggerFactory" => "Microsoft.Extensions.Logging.ILoggerFactory",
        // Microsoft.Extensions.Configuration
        "IConfiguration" => "Microsoft.Extensions.Configuration.IConfiguration",
        "IConfigurationRoot" => "Microsoft.Extensions.Configuration.IConfigurationRoot",
        "IConfigurationSection" => "Microsoft.Extensions.Configuration.IConfigurationSection",
        "ConfigurationExtensions" => "Microsoft.Extensions.Configuration.ConfigurationExtensions",
        // Microsoft.Extensions.DependencyInjection
        "IServiceProvider" => "Microsoft.Extensions.DependencyInjection.IServiceProvider",
        "IServiceCollection" => "Microsoft.Extensions.DependencyInjection.IServiceCollection",
        "IServiceScopeFactory" => "Microsoft.Extensions.DependencyInjection.IServiceScopeFactory",
        // Microsoft.Extensions.Options
        "IOptions" => "Microsoft.Extensions.Options.IOptions`1",
        "IOptionsSnapshot" => "Microsoft.Extensions.Options.IOptionsSnapshot`1",
        "IOptionsMonitor" => "Microsoft.Extensions.Options.IOptionsMonitor`1",
        // Microsoft.Extensions.Hosting
        "IHost" => "Microsoft.Extensions.Hosting.IHost",
        "IHostBuilder" => "Microsoft.Extensions.Hosting.IHostBuilder",
        "IHostedService" => "Microsoft.Extensions.Hosting.IHostedService",
        // Microsoft.EntityFrameworkCore
        "DbContext" => "Microsoft.EntityFrameworkCore.DbContext",
        "DbSet" => "Microsoft.EntityFrameworkCore.DbSet`1",
        "DatabaseFacade" => "Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade",
        // Microsoft.AspNetCore
        "HttpContext" => "Microsoft.AspNetCore.Http.HttpContext",
        "HttpRequest" => "Microsoft.AspNetCore.Http.HttpRequest",
        "HttpResponse" => "Microsoft.AspNetCore.Http.HttpResponse",
        "ControllerBase" => "Microsoft.AspNetCore.Mvc.ControllerBase",
        "Results" => "Microsoft.AspNetCore.Http.Results",
        "TypedResults" => "Microsoft.AspNetCore.Http.TypedResults",
        // Serilog
        "Log" => "Serilog.Log",
        "SerilogLog" => "Serilog.Log",
        // Newtonsoft.Json
        "JsonConvert" => "Newtonsoft.Json.JsonConvert",
        "JObject" => "Newtonsoft.Json.Linq.JObject",
        "JArray" => "Newtonsoft.Json.Linq.JArray",
        "JToken" => "Newtonsoft.Json.Linq.JToken",
        // Dapper
        "SqlMapper" => "Dapper.SqlMapper",
        // MediatR
        "IMediator" => "MediatR.IMediator",
        "ISender" => "MediatR.ISender",
        "Mediator" => "MediatR.Mediator",
        // AutoMapper
        "IMapper" => "AutoMapper.IMapper",
        "Mapper" => "AutoMapper.Mapper",
        // FluentValidation
        "IValidator" => "FluentValidation.IValidator",
        // Polly
        "Policy" => "Polly.Policy",
        "ResiliencePipeline" => "Polly.ResiliencePipeline",
        _ => shortName
    };

    private List<string> FindCallChain(string startFunctionId, EffectKind targetKind, string targetValue)
    {
        // BFS to find shortest path to the effect
        var queue = new Queue<(string FunctionId, List<string> Path)>();
        var visited = new HashSet<string>();

        queue.Enqueue((startFunctionId, new List<string> { _callGraphAnalysis.Functions[startFunctionId].Name }));
        visited.Add(startFunctionId);

        while (queue.Count > 0)
        {
            var (currentId, path) = queue.Dequeue();

            // Check direct effects from this function's body
            if (_callGraphAnalysis.ForwardGraph.TryGetValue(currentId, out var calls))
            {
                foreach (var (calleeName, span) in calls)
                {
                    // Resolve callee name to ID for internal calls (handles cross-class method calls)
                    var calleeId = ResolveToInternalId(calleeName);

                    // Check external calls via manifest resolver
                    if (calleeId == null)
                    {
                        var (typeName, methodName) = ParseCallTargetForChain(calleeName);
                        if (!string.IsNullOrEmpty(typeName) && !string.IsNullOrEmpty(methodName))
                        {
                            var resolution = _resolver.Resolve(typeName, methodName);
                            if (resolution.Status != EffectResolutionStatus.Unknown &&
                                resolution.Effects.Contains(targetKind, targetValue))
                            {
                                var result = new List<string>(path) { calleeName };
                                return result;
                            }
                        }
                    }
                    // Check internal calls
                    else if (!visited.Contains(calleeId))
                    {
                        visited.Add(calleeId);
                        var newPath = new List<string>(path) { _callGraphAnalysis.Functions[calleeId].Name };
                        queue.Enqueue((calleeId, newPath));
                    }
                }
            }
        }

        return new List<string>();
    }

    /// <summary>
    /// Context for effect inference.
    /// </summary>
    private sealed class InferenceContext
    {
        public EffectResolver Resolver { get; }
        public Dictionary<string, EffectSet> ComputedEffects { get; }
        public Dictionary<string, FunctionNode> Functions { get; }
        public Dictionary<string, string> FunctionNameToId { get; }
        public Dictionary<string, List<string>> MethodNameToIds { get; }
        public HashSet<string> SccMembers { get; }
        public UnknownCallPolicy Policy { get; }
        public bool StrictEffects { get; }
        public DiagnosticBag Diagnostics { get; }
        public string CurrentFunctionId { get; }

        public InferenceContext(
            EffectResolver resolver,
            Dictionary<string, EffectSet> computedEffects,
            Dictionary<string, FunctionNode> functions,
            Dictionary<string, string> functionNameToId,
            Dictionary<string, List<string>> methodNameToIds,
            HashSet<string> sccMembers,
            UnknownCallPolicy policy,
            bool strictEffects,
            DiagnosticBag diagnostics,
            string currentFunctionId)
        {
            Resolver = resolver;
            ComputedEffects = computedEffects;
            Functions = functions;
            FunctionNameToId = functionNameToId;
            MethodNameToIds = methodNameToIds;
            SccMembers = sccMembers;
            Policy = policy;
            StrictEffects = strictEffects;
            Diagnostics = diagnostics;
            CurrentFunctionId = currentFunctionId;
        }
    }

    /// <summary>
    /// Infers effects from AST nodes.
    /// </summary>
    private sealed class EffectInferrer
    {
        private readonly InferenceContext _context;

        /// <summary>
        /// Known-pure method names (LINQ extension methods and similar).
        /// Used as a last-resort fallback when full type resolution fails
        /// because extension methods are called on instances (e.g., items.OrderBy(...)).
        /// </summary>
        private static readonly HashSet<string> KnownPureMethodNames = new(StringComparer.Ordinal)
        {
            "Where", "Select", "SelectMany",
            "OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending",
            "GroupBy", "Join", "GroupJoin",
            "First", "FirstOrDefault", "Single", "SingleOrDefault",
            "Last", "LastOrDefault",
            "Any", "All", "Count", "LongCount",
            "Sum", "Average", "Min", "Max",
            "Distinct", "DistinctBy", "Union", "UnionBy", "Intersect", "IntersectBy", "Except", "ExceptBy",
            "Skip", "Take", "SkipWhile", "TakeWhile", "SkipLast", "TakeLast",
            "Reverse", "Concat", "Zip",
            "Aggregate",
            "ToList", "ToArray", "ToDictionary", "ToHashSet", "ToLookup",
            "OfType", "Cast", "AsEnumerable",
            "DefaultIfEmpty", "Append", "Prepend",
            "Contains", "SequenceEqual",
            "Range", "Repeat", "Empty",
            "ElementAt", "ElementAtOrDefault",
            "Chunk", "Order", "OrderDescending",
            // Common pure instance methods
            "ToString", "GetHashCode", "Equals", "CompareTo", "GetType",
            "Clone", "CopyTo", "GetEnumerator",
            "Substring", "Trim", "TrimStart", "TrimEnd",
            "StartsWith", "EndsWith", "Contains", "IndexOf", "LastIndexOf",
            "Replace", "Split", "Join", "ToUpper", "ToLower", "ToUpperInvariant", "ToLowerInvariant",
            "PadLeft", "PadRight", "Insert", "Remove",
            // Collection methods
            "Add", "Remove", "Clear", "ContainsKey", "ContainsValue",
            "TryGetValue", "AddRange", "RemoveAt", "RemoveAll",
            "Sort", "BinarySearch", "Find", "FindAll", "FindIndex", "FindLast",
            "Exists", "TrueForAll", "ForEach", "ConvertAll",
            // StringBuilder
            "Append", "AppendLine", "AppendFormat",
            // Math functions
            "Abs", "Sqrt", "Pow", "Floor", "Ceiling", "Round",
            "Log", "Log10", "Log2", "Sin", "Cos", "Tan",
            "Clamp", "Sign", "Truncate",
        };

        public EffectInferrer(InferenceContext context)
        {
            _context = context;
        }

        public EffectSet InferFromStatements(IEnumerable<StatementNode> statements)
        {
            var effects = EffectSet.Empty;
            foreach (var statement in statements)
            {
                effects = effects.Union(InferFromStatement(statement));
            }
            return effects;
        }

        private EffectSet InferFromStatement(StatementNode statement)
        {
            return statement switch
            {
                PrintStatementNode => EffectSet.From("cw"),
                CallStatementNode call => InferFromCallStatement(call),
                IfStatementNode ifStmt => InferFromIf(ifStmt),
                ForStatementNode forStmt => InferFromFor(forStmt),
                WhileStatementNode whileStmt => InferFromExpression(whileStmt.Condition).Union(InferFromStatements(whileStmt.Body)),
                DoWhileStatementNode doWhile => InferFromExpression(doWhile.Condition).Union(InferFromStatements(doWhile.Body)),
                ForeachStatementNode foreach_ => InferFromExpression(foreach_.Collection).Union(InferFromStatements(foreach_.Body)),
                MatchStatementNode matchStmt => InferFromMatch(matchStmt),
                TryStatementNode tryStmt => InferFromTry(tryStmt),
                ThrowStatementNode => EffectSet.From("throw"),
                RethrowStatementNode => EffectSet.From("throw"),
                ReturnStatementNode ret => ret.Expression != null ? InferFromExpression(ret.Expression) : EffectSet.Empty,
                BindStatementNode bind => bind.Initializer != null ? InferFromExpression(bind.Initializer) : EffectSet.Empty,
                AssignmentStatementNode assign => InferFromAssignment(assign),
                // Collection mutations
                CollectionPushNode => EffectSet.From("mut"),
                DictionaryPutNode => EffectSet.From("mut"),
                CollectionRemoveNode => EffectSet.From("mut"),
                CollectionSetIndexNode => EffectSet.From("mut"),
                CollectionClearNode => EffectSet.From("mut"),
                CollectionInsertNode => EffectSet.From("mut"),
                DictionaryForeachNode dictForeach => InferFromStatements(dictForeach.Body),
                _ => EffectSet.Empty
            };
        }

        private EffectSet InferFromCallStatement(CallStatementNode call)
        {
            var effects = InferFromCallTarget(call.Target, call.Span);

            // Also infer from arguments
            foreach (var arg in call.Arguments)
            {
                effects = effects.Union(InferFromExpression(arg));
            }

            return effects;
        }

        private EffectSet InferFromCallTarget(string target, TextSpan span)
        {
            // Check if it's an internal function call by name
            var internalFunc = FindInternalFunctionByName(target);
            if (internalFunc != null)
            {
                if (_context.ComputedEffects.TryGetValue(internalFunc.Id, out var computed))
                {
                    return computed;
                }
                // If in same SCC, return current approximation
                if (_context.SccMembers.Contains(internalFunc.Id))
                {
                    return _context.ComputedEffects.GetValueOrDefault(internalFunc.Id, EffectSet.Empty);
                }
            }

            // Try to resolve using the EffectResolver (manifest-based)
            var (typeName, methodName) = ParseCallTarget(target);
            if (!string.IsNullOrEmpty(typeName) && !string.IsNullOrEmpty(methodName))
            {
                var resolution = _context.Resolver.Resolve(typeName, methodName);
                if (resolution.Status != EffectResolutionStatus.Unknown)
                {
                    return resolution.Effects;
                }

                // If type didn't resolve, try variable type resolution:
                // "r.Next" where "r" is a variable declared as "new Random()"
                var resolvedVarType = ResolveVariableType(typeName);
                if (resolvedVarType != null && resolvedVarType != typeName)
                {
                    resolution = _context.Resolver.Resolve(resolvedVarType, methodName);
                    if (resolution.Status != EffectResolutionStatus.Unknown)
                    {
                        return resolution.Effects;
                    }
                }
            }

            // Method-name fallback: extract just the method name and check
            // against known-pure names (handles extension method calls like items.OrderBy(...))
            var lastDotFallback = target.LastIndexOf('.');
            if (lastDotFallback > 0)
            {
                var bareMethodName = target[(lastDotFallback + 1)..];
                if (KnownPureMethodNames.Contains(bareMethodName))
                {
                    return EffectSet.Empty;
                }
            }

            // Single-word targets with no dot are local variable/delegate invocations,
            // not external calls. Assume pure since we lack type information.
            // In strict mode, emit a warning so users know the effects are unverified.
            if (!target.Contains('.'))
            {
                if (_context.StrictEffects)
                {
                    _context.Diagnostics.Report(
                        span,
                        DiagnosticCode.UnknownExternalCall,
                        $"Delegate/variable invocation '{target}' has unverified effects. Consider wrapping in a function with declared effects.",
                        DiagnosticSeverity.Warning);
                }
                return EffectSet.Empty;
            }

            // Permissive mode: assume pure for unknown calls (no diagnostic)
            if (_context.Policy == UnknownCallPolicy.Permissive)
            {
                return EffectSet.Empty;
            }

            // Unknown external call - report diagnostic based on policy
            ReportUnknownCall(target, span);
            return EffectSet.Unknown;
        }

        private void ReportUnknownCall(string target, TextSpan span)
        {
            // Calor0411: Unknown external call
            var severity = _context.StrictEffects
                ? DiagnosticSeverity.Error
                : DiagnosticSeverity.Warning;

            if (_context.Policy == UnknownCallPolicy.Strict || _context.StrictEffects)
            {
                _context.Diagnostics.Report(
                    span,
                    DiagnosticCode.UnknownExternalCall,
                    $"Unknown external call to '{target}'. Add effect declaration in a .calor-effects.json manifest.",
                    severity);
            }
            else if (_context.Policy == UnknownCallPolicy.Warn)
            {
                _context.Diagnostics.Report(
                    span,
                    DiagnosticCode.UnknownExternalCall,
                    $"Unknown external call to '{target}' - assuming worst-case effects. Consider adding to manifest.",
                    DiagnosticSeverity.Warning);
            }
        }

        private static (string TypeName, string MethodName) ParseCallTarget(string target)
        {
            // Handle patterns like "Console.WriteLine", "File.ReadAllText", "System.IO.File.ReadAllText"
            var lastDot = target.LastIndexOf('.');
            if (lastDot <= 0)
                return ("", "");

            var methodName = target[(lastDot + 1)..];
            var typePart = target[..lastDot];

            // If type part doesn't contain a dot, try common namespaces
            if (!typePart.Contains('.'))
            {
                // Map common short names to full types
                typePart = MapShortTypeNameToFullName(typePart);
            }

            return (typePart, methodName);
        }

        private FunctionNode? FindInternalFunctionByName(string name)
        {
            // Try exact name match against computed effects
            foreach (var kvp in _context.ComputedEffects)
            {
                if (_context.Functions.TryGetValue(kvp.Key, out var func) &&
                    func.Name.Equals(name, StringComparison.Ordinal))
                {
                    return func;
                }
            }

            // For dotted targets (e.g., "_calculator.Add", "Helper.Format"),
            // extract the bare method name and resolve via function name index.
            // This handles cross-class method calls within the same module.
            var lastDot = name.LastIndexOf('.');
            if (lastDot > 0)
            {
                var bareMethodName = name[(lastDot + 1)..];

                // Check for ambiguity: if multiple classes define the same method name,
                // return null to fall through to external resolution (conservative).
                if (_context.MethodNameToIds.TryGetValue(bareMethodName, out var candidates) && candidates.Count > 1)
                    return null;

                if (_context.FunctionNameToId.TryGetValue(bareMethodName, out var resolvedId) &&
                    _context.Functions.TryGetValue(resolvedId, out var resolved))
                {
                    // Verify the resolved function has computed effects or is in current SCC
                    if (_context.ComputedEffects.ContainsKey(resolvedId) ||
                        _context.SccMembers.Contains(resolvedId))
                    {
                        return resolved;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Resolves a variable name to its declared type by scanning the current function's
        /// body for §B (bind) statements with §NEW initializers.
        /// E.g., "§B{client} §NEW{HttpClient}" → "client" resolves to "System.Net.Http.HttpClient".
        /// </summary>
        private string? ResolveVariableType(string variableName)
        {
            if (!_context.Functions.TryGetValue(_context.CurrentFunctionId, out var function))
                return null;

            return ScanForVariableType(variableName, function.Body);
        }

        private static string? ScanForVariableType(string variableName, IEnumerable<StatementNode> statements)
        {
            foreach (var stmt in statements)
            {
                if (stmt is BindStatementNode bind && bind.Name == variableName && bind.Initializer != null)
                {
                    // §B{var} §NEW{TypeName} → resolve TypeName
                    if (bind.Initializer is NewExpressionNode newExpr)
                    {
                        return MapShortTypeNameToFullName(newExpr.TypeName);
                    }
                    // §B{var:TypeName} — explicit type declaration
                    if (bind.TypeName != null)
                    {
                        return MapShortTypeNameToFullName(bind.TypeName);
                    }
                }
            }
            return null;
        }

        private EffectSet InferFromIf(IfStatementNode ifStmt)
        {
            var effects = InferFromExpression(ifStmt.Condition);
            effects = effects.Union(InferFromStatements(ifStmt.ThenBody));

            foreach (var elseIf in ifStmt.ElseIfClauses)
            {
                effects = effects.Union(InferFromExpression(elseIf.Condition));
                effects = effects.Union(InferFromStatements(elseIf.Body));
            }

            if (ifStmt.ElseBody != null)
            {
                effects = effects.Union(InferFromStatements(ifStmt.ElseBody));
            }

            return effects;
        }

        private EffectSet InferFromFor(ForStatementNode forStmt)
        {
            var effects = InferFromExpression(forStmt.From);
            effects = effects.Union(InferFromExpression(forStmt.To));
            if (forStmt.Step != null)
            {
                effects = effects.Union(InferFromExpression(forStmt.Step));
            }
            effects = effects.Union(InferFromStatements(forStmt.Body));
            return effects;
        }

        private EffectSet InferFromMatch(MatchStatementNode matchStmt)
        {
            var effects = InferFromExpression(matchStmt.Target);
            foreach (var matchCase in matchStmt.Cases)
            {
                effects = effects.Union(InferFromStatements(matchCase.Body));
            }
            return effects;
        }

        private EffectSet InferFromTry(TryStatementNode tryStmt)
        {
            var effects = InferFromStatements(tryStmt.TryBody);

            foreach (var catchClause in tryStmt.CatchClauses)
            {
                effects = effects.Union(InferFromStatements(catchClause.Body));
            }

            if (tryStmt.FinallyBody != null)
            {
                effects = effects.Union(InferFromStatements(tryStmt.FinallyBody));
            }

            return effects;
        }

        private EffectSet InferFromAssignment(AssignmentStatementNode assign)
        {
            var effects = InferFromExpression(assign.Value);

            // Check if this is a mutation (writing to non-local object)
            if (assign.Target is FieldAccessNode)
            {
                effects = effects.Union(EffectSet.From("mut"));
            }

            return effects;
        }

        private EffectSet InferFromExpression(ExpressionNode expr)
        {
            return expr switch
            {
                CallExpressionNode call => InferFromCallExpression(call),
                MatchExpressionNode match => InferFromMatchExpression(match),
                BinaryOperationNode binOp => InferFromExpression(binOp.Left).Union(InferFromExpression(binOp.Right)),
                UnaryOperationNode unOp => InferFromExpression(unOp.Operand),
                ConditionalExpressionNode cond => InferFromExpression(cond.Condition)
                    .Union(InferFromExpression(cond.WhenTrue))
                    .Union(InferFromExpression(cond.WhenFalse)),
                SomeExpressionNode some => InferFromExpression(some.Value),
                OkExpressionNode ok => InferFromExpression(ok.Value),
                ErrExpressionNode err => InferFromExpression(err.Error),
                NewExpressionNode newExpr => InferFromNewExpression(newExpr),
                FieldAccessNode field => InferFromExpression(field.Target),
                ArrayAccessNode array => InferFromExpression(array.Array).Union(InferFromExpression(array.Index)),
                LambdaExpressionNode lambda => InferFromLambda(lambda),
                AwaitExpressionNode await_ => InferFromExpression(await_.Awaited),
                _ => EffectSet.Empty
            };
        }

        private EffectSet InferFromCallExpression(CallExpressionNode call)
        {
            var effects = InferFromCallTarget(call.Target, call.Span);

            foreach (var arg in call.Arguments)
            {
                effects = effects.Union(InferFromExpression(arg));
            }

            return effects;
        }

        private EffectSet InferFromMatchExpression(MatchExpressionNode match)
        {
            var effects = InferFromExpression(match.Target);
            foreach (var matchCase in match.Cases)
            {
                effects = effects.Union(InferFromStatements(matchCase.Body));
            }
            return effects;
        }

        private EffectSet InferFromNewExpression(NewExpressionNode newExpr)
        {
            var effects = EffectSet.Empty;

            // Check if constructor has effects via manifest resolver
            var ctorResolution = _context.Resolver.ResolveConstructor(newExpr.TypeName);
            if (ctorResolution.Status != EffectResolutionStatus.Unknown)
            {
                effects = effects.Union(ctorResolution.Effects);
            }

            foreach (var arg in newExpr.Arguments)
            {
                effects = effects.Union(InferFromExpression(arg));
            }

            return effects;
        }

        private EffectSet InferFromLambda(LambdaExpressionNode lambda)
        {
            // Lambda body contributes effects to enclosing function
            if (lambda.ExpressionBody != null)
            {
                return InferFromExpression(lambda.ExpressionBody);
            }
            if (lambda.StatementBody != null)
            {
                return InferFromStatements(lambda.StatementBody);
            }
            return EffectSet.Empty;
        }
    }
}

/// <summary>
/// Policy for handling unknown external calls.
/// </summary>
public enum UnknownCallPolicy
{
    /// <summary>
    /// Unknown calls are errors (v1 default).
    /// </summary>
    Strict,

    /// <summary>
    /// Unknown calls produce warnings, assume worst-case effects.
    /// </summary>
    Warn,

    /// <summary>
    /// Unknown calls are errors unless stubbed.
    /// </summary>
    StubRequired,

    /// <summary>
    /// Unknown calls are silently assumed pure.
    /// Forbidden-effect checks (Calor0410) are demoted to warnings.
    /// Designed for converted code that lacks effect annotations.
    /// </summary>
    Permissive
}

/// <summary>
/// Extension methods for EffectSet display.
/// </summary>
internal static class EffectSetExtensions
{
    public static string ToSurfaceCode(EffectKind kind, string value)
    {
        return (kind, value) switch
        {
            // Console I/O
            (EffectKind.IO, "console_write") => "cw",
            (EffectKind.IO, "console_read") => "cr",

            // Filesystem effects
            (EffectKind.IO, "filesystem_read") => "fs:r",
            (EffectKind.IO, "filesystem_write") => "fs:w",
            (EffectKind.IO, "filesystem_readwrite") => "fs:rw",

            // Network effects
            (EffectKind.IO, "network_read") => "net:r",
            (EffectKind.IO, "network_write") => "net:w",
            (EffectKind.IO, "network_readwrite") => "net:rw",

            // Database effects
            (EffectKind.IO, "database_read") => "db:r",
            (EffectKind.IO, "database_write") => "db:w",
            (EffectKind.IO, "database_readwrite") => "db:rw",

            // Environment effects
            (EffectKind.IO, "environment_read") => "env:r",
            (EffectKind.IO, "environment_write") => "env:w",

            // System
            (EffectKind.IO, "process") => "proc",

            // Memory effects
            (EffectKind.Memory, "allocation") => "alloc",
            (EffectKind.Memory, "unsafe") => "unsafe",

            // Nondeterminism
            (EffectKind.Nondeterminism, "time") => "time",
            (EffectKind.Nondeterminism, "random") => "rand",

            // Mutation/Exception
            (EffectKind.Mutation, "heap_write") => "mut",
            (EffectKind.Exception, "intentional") => "throw",

            _ => $"{kind}:{value}"
        };
    }
}
