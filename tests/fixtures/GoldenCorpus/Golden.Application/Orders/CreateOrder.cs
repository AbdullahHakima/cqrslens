using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Golden.Domain;
using MediatR;

namespace Golden.Application.Orders;

public sealed record CreateOrderCommand(Guid CustomerId, IReadOnlyList<CreateOrderLine> Lines)
    : IRequest<Guid>;

public sealed record CreateOrderLine(Guid ProductId, int Quantity);

public interface IOrderRepository
{
    void Add(Order order);
    Order? Find(Guid id);
}

public sealed class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, Guid>
{
    private readonly IOrderRepository _orders;
    private readonly IPublisher _publisher;

    public CreateOrderCommandHandler(IOrderRepository orders, IPublisher publisher)
    {
        _orders = orders;
        _publisher = publisher;
    }

    public async Task<Guid> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerId = request.CustomerId,
            Status = OrderStatus.Draft,
        };

        _orders.Add(order);
        await _publisher.Publish(new Golden.Domain.Events.OrderPlacedNotification(order.Id), cancellationToken);
        return order.Id;
    }
}
