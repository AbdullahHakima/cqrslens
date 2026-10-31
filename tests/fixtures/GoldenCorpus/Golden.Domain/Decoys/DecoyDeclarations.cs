using System;

namespace Golden.Domain.Decoys;

// Every CQRS-looking declaration below is text that must never become a shape.
// public sealed record CommentedOutCommand(Guid Id) : IRequest<int>;

public sealed class NotACommand
{
    public const string SourceText =
        "public sealed record StringLiteralCommand(Guid Id) : IRequest<int>;";
}

/*
public sealed class BlockCommentedHandler : IRequestHandler<BlockCommentedCommand, int>
{
}
*/

public sealed class CreateOrderCommandFactory
{
}

public interface IRequestHandlerFactory<TRequest>
{
}
