using System;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using MediatR;

namespace Golden.Application.Orders;

public interface ICommand<TResponse> : IRequest<TResponse>
{
}

public sealed record ArchiveOrderCommand(Guid Id) : ICommand<bool>;

public sealed class ArchiveOrderCommandHandler : IRequestHandler<ArchiveOrderCommand, bool>
{
    public Task<bool> Handle(ArchiveOrderCommand request, CancellationToken cancellationToken)
        => Task.FromResult(true);
}

public sealed class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderCommandValidator()
    {
        RuleFor(command => command.CustomerId).NotEmpty();
        RuleForEach(command => command.Lines).ChildRules(line =>
            line.RuleFor(item => item.Quantity).GreaterThan(0));
    }
}

public sealed class ArchiveOrderCommandValidator : AbstractValidator<ArchiveOrderCommand>
{
    public ArchiveOrderCommandValidator()
    {
        RuleFor(command => command.Id).NotEmpty();
    }
}
