namespace StrictFailure;

// Intentionally has no handler, so --strict must exit with code 4.
public sealed record UnhandledQuery : IRequest<int>;
