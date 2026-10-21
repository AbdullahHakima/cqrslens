using FluentValidation;
using MediatR;

namespace SampleShop.Application.Orders.DeleteOrder;

public sealed record DeleteOrderCommand(Guid OrderId) : IRequest;

public sealed class DeleteOrderCommandHandler : IRequestHandler<DeleteOrderCommand>
{
    public Task Handle(DeleteOrderCommand request, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

public sealed class DeleteOrderCommandValidator : AbstractValidator<DeleteOrderCommand>
{
    public DeleteOrderCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
    }
}
