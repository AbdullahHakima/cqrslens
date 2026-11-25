namespace CqrsLens;

/// <summary>The transport or hosting trigger mechanism of a discovered system entrypoint.</summary>
public enum SystemEntrypointKind
{
    Http,
    Grpc,
    Blazor,
    RazorPage,
    Worker,
    MessageConsumer,
}
