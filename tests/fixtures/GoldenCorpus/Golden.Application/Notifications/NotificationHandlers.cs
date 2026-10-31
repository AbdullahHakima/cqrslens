using System.Threading;
using System.Threading.Tasks;
using Golden.Domain.Events;
using MediatR;

namespace Golden.Application.Notifications;

public sealed class OrderPlacedNotificationHandler : INotificationHandler<OrderPlacedNotification>
{
    public Task Handle(OrderPlacedNotification notification, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

public sealed class OrderPlacedEmailHandler : INotificationHandler<OrderPlacedNotification>
{
    public Task Handle(OrderPlacedNotification notification, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

public sealed class OrderCancelledNotificationHandler : INotificationHandler<OrderCancelledNotification>
{
    public Task Handle(OrderCancelledNotification notification, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
