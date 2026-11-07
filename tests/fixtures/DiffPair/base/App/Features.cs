using MediatR;

namespace App;

public sealed record DoThingQuery : IRequest<int>;

public sealed class DoThingQueryHandler : IRequestHandler<DoThingQuery, int>
{
    public Task<int> Handle(DoThingQuery request, CancellationToken ct) => Task.FromResult(1);
}
