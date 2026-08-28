// Calor-arm shim (harness-provided, fixed, not agent-editable). Both arms of
// this pair are Calor arms and share the module/namespace, so one shim serves
// both. The Calor module emits a file-scoped namespace MediatR.Pipeline with
// the interfaces, the behaviour class, the delegate and (after the task) the
// TracePreProcessor class. The shim instantiates everything at
// <TRequest = string, TResponse = int> and runs Handle synchronously.
namespace PipelineTrace.HeldOut;

internal static class TestShim
{
    // The pre-processor contract, re-exported so held-out tests can implement it.
    public interface IPreProcessor : global::MediatR.Pipeline.IRequestPreProcessor<string>
    {
    }

    public static IPreProcessor NewTracePreProcessor() => new TraceAdapter();

    private sealed class TraceAdapter : IPreProcessor
    {
        private readonly global::MediatR.Pipeline.TracePreProcessor<string> _inner = new();
        public Task Process(string request, CancellationToken cancellationToken) => _inner.Process(request, cancellationToken);
    }

    public static int Handle(IEnumerable<IPreProcessor> preProcessors, string request, Func<int> next)
    {
        var behavior = new global::MediatR.Pipeline.RequestPreProcessorBehavior<string, int>(
            preProcessors.Cast<global::MediatR.Pipeline.IRequestPreProcessor<string>>());
        global::MediatR.Pipeline.RequestHandlerDelegate<int> del = () => Task.FromResult(next());
        return behavior.Handle(request, del, CancellationToken.None).GetAwaiter().GetResult();
    }
}
