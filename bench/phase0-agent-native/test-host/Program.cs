using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

if (args.Length is not (1 or 3) || (args.Length == 3 && args[1] != "--ppw-result-nonce"))
{
    Console.Error.WriteLine(
        "Usage: ppw-xunit-host <compiled-test-assembly> [--ppw-result-nonce <64-hex>]");
    return 2;
}

var assemblyPath = Path.GetFullPath(args[0]);
var resultNonce = args.Length == 3 ? args[2] : null;
if (resultNonce is not null &&
    (resultNonce.Length != 64 || resultNonce.Any(value => !Uri.IsHexDigit(value))))
{
    Console.Error.WriteLine("PPW_XUNIT_INFRASTRUCTURE_ERROR: invalid result nonce.");
    return 2;
}
var resolver = new AssemblyDependencyResolver(assemblyPath);
Assembly? ResolveManaged(AssemblyLoadContext context, AssemblyName name)
{
    var path = resolver.ResolveAssemblyToPath(name);
    return path is null ? null : context.LoadFromAssemblyPath(path);
}
nint ResolveNative(Assembly assembly, string name)
{
    var path = resolver.ResolveUnmanagedDllToPath(name);
    return path is null ? nint.Zero : NativeLibrary.Load(path);
}

AssemblyLoadContext.Default.Resolving += ResolveManaged;
AssemblyLoadContext.Default.ResolvingUnmanagedDll += ResolveNative;
try
{
    AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
    var warnings = new List<string>();
    var configuration = ConfigReader.Load(assemblyPath, warnings: warnings);
    foreach (var warning in warnings)
        Console.Error.WriteLine($"xUnit configuration warning: {warning}");

    var results = new List<TestResult>();
    var errors = new List<string>();
    var resultLock = new object();
    void Record(string state, ITestResultMessage message, string? failure = null)
    {
        lock (resultLock)
            results.Add(new TestResult(state, message.Test.DisplayName, message.ExecutionTime, failure));
    }
    void Error(IFailureInformation failure)
    {
        lock (resultLock)
            errors.Add(ExceptionUtility.CombineMessages(failure));
    }

    using var messages = new TestMessageSink();
    messages.Execution.TestPassedEvent += value => Record("Passed", value.Message);
    messages.Execution.TestFailedEvent += value =>
        Record("Failed", value.Message, ExceptionUtility.CombineMessages(value.Message));
    messages.Execution.TestSkippedEvent += value =>
        Record("Skipped", value.Message, value.Message.Reason);
    messages.Diagnostics.ErrorMessageEvent += value => Error(value.Message);
    messages.Execution.TestAssemblyCleanupFailureEvent += value => Error(value.Message);
    messages.Execution.TestCaseCleanupFailureEvent += value => Error(value.Message);
    messages.Execution.TestClassCleanupFailureEvent += value => Error(value.Message);
    messages.Execution.TestCollectionCleanupFailureEvent += value => Error(value.Message);
    messages.Execution.TestCleanupFailureEvent += value => Error(value.Message);
    messages.Execution.TestMethodCleanupFailureEvent += value => Error(value.Message);

    using var controller = new XunitFrontController(
        AppDomainSupport.Denied, assemblyPath, configFileName: null,
        shadowCopy: false, diagnosticMessageSink: messages);
    using var discovery = new TestDiscoverySink();
    controller.Find(false, discovery, TestFrameworkOptions.ForDiscovery(configuration));
    discovery.Finished.WaitOne();
    if (discovery.TestCases.Count == 0)
    {
        Console.Error.WriteLine("PPW_XUNIT_INFRASTRUCTURE_ERROR: no tests were discovered.");
        return 2;
    }

    using var execution = new ExecutionSink(messages, new ExecutionSinkOptions
    {
        FailSkips = configuration.FailSkipsOrDefault,
        DiagnosticMessageSink = messages,
        LongRunningTestTime = TimeSpan.FromSeconds(configuration.LongRunningTestSecondsOrDefault)
    });
    controller.RunTests(discovery.TestCases, execution, TestFrameworkOptions.ForExecution(configuration));
    execution.Finished.WaitOne();
    var summary = execution.ExecutionSummary;
    if (summary.Errors != 0 || errors.Count != 0 || summary.Total != results.Count)
    {
        Console.Error.WriteLine("PPW_XUNIT_INFRASTRUCTURE_ERROR: execution did not complete cleanly.");
        foreach (var error in errors)
            Console.Error.WriteLine(error);
        return 2;
    }

    // Emit normal result lines only after complete xUnit execution. A partial or
    // crashed run must not leave a success-shaped held-out observation.
    foreach (var result in results)
    {
        var name = result.Name.Replace("\r", "\\r").Replace("\n", "\\n");
        var milliseconds = (result.Seconds * 1000).ToString("0.###", CultureInfo.InvariantCulture);
        Console.WriteLine($"  {result.State} {name} [{milliseconds} ms]");
        if (result.Message is not null)
        {
            Console.WriteLine("  Error Message:");
            foreach (var line in result.Message.Replace("\r\n", "\n").Split('\n'))
                Console.WriteLine("   " + line);
        }
    }
    var passed = summary.Total - summary.Failed - summary.Skipped;
    var label = summary.Failed == 0 ? "Passed!" : "Failed!";
    Console.WriteLine($"{label} - Failed: {summary.Failed}, Passed: {passed}, " +
                      $"Skipped: {summary.Skipped}, Total: {summary.Total}");
    if (resultNonce is not null)
    {
        var receipt = JsonSerializer.Serialize(new
        {
            failed = summary.Failed,
            passed,
            skipped = summary.Skipped,
            total = summary.Total,
        });
        Console.WriteLine($"PPW_XUNIT_RESULT_V1:{resultNonce}:{receipt}");
    }
    return summary.Failed == 0 ? 0 : 1;
}
finally
{
    AssemblyLoadContext.Default.Resolving -= ResolveManaged;
    AssemblyLoadContext.Default.ResolvingUnmanagedDll -= ResolveNative;
}

internal sealed record TestResult(string State, string Name, decimal Seconds, string? Message);
