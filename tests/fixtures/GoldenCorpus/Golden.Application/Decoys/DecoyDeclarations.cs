using System;
using MediatR;

namespace Golden.Application.Decoys;

// public sealed record CommentedOutCommand(Guid Id) : IRequest<int>;
// public sealed class CommentedOutHandler : IRequestHandler<CommentedOutCommand, int> { }

public sealed class CreateOrderCommandFactory
{
    public const string FakeSource =
        "public sealed record StringLiteralCommand(Guid Id) : IRequest<int>;";
}

/*
public sealed class BlockCommentedHandler : IRequestHandler<BlockCommentedCommand, int>
{
}
*/

public sealed class NotARequest
{
}

public sealed class NotAHandler : IRequestHandlerFactory<CreateOrderCommand>
{
}

public interface IRequestHandlerFactory<TRequest>
{
}
