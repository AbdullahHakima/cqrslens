using MediatR;

namespace SampleShop.Application.Orders.GetOrderDetails;

public sealed record OrderDetailsDto(Guid Id, int CustomerId, string Status, int LineCount);

public sealed record GetOrderDetailsQuery(Guid OrderId) : IRequest<OrderDetailsDto>;

public sealed class GetOrderDetailsQueryHandler : IRequestHandler<GetOrderDetailsQuery, OrderDetailsDto>
{
    public Task<OrderDetailsDto> Handle(GetOrderDetailsQuery request, CancellationToken cancellationToken)
        => Task.FromResult(new OrderDetailsDto(request.OrderId, 1, "Pending", 3));
}
