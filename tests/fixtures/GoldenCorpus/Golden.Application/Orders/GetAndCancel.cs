using System;
using System.Threading;
using System.Threading.Tasks;
using Golden.Domain;
using MediatR;

namespace Golden.Application.Orders;

public sealed record GetOrderQuery(Guid Id) : IRequest<OrderDto>;

public sealed record OrderDto(Guid Id, decimal Total);

public sealed class GetOrderQueryHandler : IRequestHandler<GetOrderQuery, OrderDto>
{
    public Task<OrderDto> Handle(GetOrderQuery request, CancellationToken cancellationToken)
        => Task.FromResult(new OrderDto(request.Id, 0m));
}

public sealed record CancelOrderCommand(Guid Id) : IRequest;

public sealed class CancelOrderCommandHandler : IRequestHandler<CancelOrderCommand>
{
    private readonly IPublisher _publisher;

    public CancelOrderCommandHandler(IPublisher publisher) => _publisher = publisher;

    public Task Handle(CancelOrderCommand request, CancellationToken cancellationToken)
        => _publisher.Publish(new Golden.Domain.Events.OrderCancelledNotification(request.Id), cancellationToken);
}
