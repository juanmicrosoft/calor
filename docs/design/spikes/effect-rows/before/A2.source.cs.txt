namespace MediatR.Pipeline;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();

public interface IRequestPreProcessor<in TRequest>
{
    Task Process(TRequest request, CancellationToken cancellationToken);
}

public interface IPipelineBehavior<TRequest, TResponse>
{
    Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken);
}

public class RequestPreProcessorBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
{
    private readonly IEnumerable<IRequestPreProcessor<TRequest>> _preProcessors;

    public RequestPreProcessorBehavior(IEnumerable<IRequestPreProcessor<TRequest>> preProcessors)
        => _preProcessors = preProcessors;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        foreach (var processor in _preProcessors)
        {
            await processor.Process(request, cancellationToken).ConfigureAwait(false);
        }

        return await next().ConfigureAwait(false);
    }
}
