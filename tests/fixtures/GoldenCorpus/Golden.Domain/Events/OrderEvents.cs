using System;

namespace Golden.Domain.Events;

public sealed record OrderPlacedNotification(Guid OrderId) : INotification;

public sealed record OrderCancelledNotification(Guid OrderId) : INotification;
