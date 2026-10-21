using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Foo.Bar.Orders;

// The scanner must ignore declarations in comments, strings and raw literals.
public static class ParserNoise
{
    public const string FakeType = "public sealed class Fake : IRequest<int> { }";

    public static string FakeText = """
        namespace Fake;
        public sealed class NestedFake : IRequestHandler<Fake, int> { }
        """;

    // public sealed class CommentOnly : IRequest<int> { }
}

public sealed record CreateWidgetCommand : IRequest<int>;

public sealed class CreateWidgetCommandHandler : IRequestHandler<CreateWidgetCommand, int>
{
    public Task<int> Handle(CreateWidgetCommand request, CancellationToken cancellationToken)
        => Task.FromResult(1);
}

public sealed class CreateWidgetCommandValidator : AbstractValidator<CreateWidgetCommand>
{
}

public sealed class LoggingBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
}

public sealed class WidgetDbContext : DbContext
{
    public DbSet<CreateWidgetCommand> Widgets { get; set; } = null!;
}
