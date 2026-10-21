using FluentValidation;
using MediatR;

namespace SampleShop.Application.Orders.CreateOrder;

public sealed record CreateOrderLine(int ProductId, int Quantity);

public sealed record CreateOrderCommand(int CustomerId, List<CreateOrderLine> Lines) : IRequest<Guid>;

public sealed class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, Guid>
{
    public Task<Guid> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
        => Task.FromResult(Guid.NewGuid());
}

public sealed class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderCommandValidator()
    {
        RuleFor(x => x.CustomerId).GreaterThan(0);
        RuleFor(x => x.Lines).NotEmpty();
    }
}
