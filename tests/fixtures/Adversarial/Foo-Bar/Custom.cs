using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Foo.Bar.Custom;

public interface IAppHandler<TRequest> : IRequestHandler<TRequest>
    where TRequest : IRequest
{
}

public sealed record ArchiveWidgetCommand : IRequest<bool>;

// Indirect handler through a custom interface. Text mode must follow the
// hierarchy; Roslyn sees the transitive IRequestHandler<> interface.
public sealed class ArchiveWidgetCommandHandler : IAppHandler<ArchiveWidgetCommand>
{
}

public sealed class ArchiveWidgetCommandValidator : AbstractValidator<ArchiveWidgetCommand>
{
}

public sealed class AuditDbContext : DbContext
{
    public DbSet<ArchiveWidgetCommand> Archives { get; set; } = null!;
}
