using MediatR;

namespace App;

public sealed record DoThingQuery : IRequest<int>;
