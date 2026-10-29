using System.Threading;
using System.Threading.Tasks;
using MediatR;

namespace Golden.Application.Common;

public sealed class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
        => next();
}

public interface IRequestHandlerFactory<TRequest>
{
}
