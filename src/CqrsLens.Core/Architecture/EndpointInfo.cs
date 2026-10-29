namespace CqrsLens;

public sealed class EndpointInfo
{
    public required string Name { get; init; }
    public required string HttpMethod { get; init; }
    public required string Route { get; init; }
    public required string TargetRequestName { get; init; }
    public string? TargetRequestNamespace { get; set; }

    /// <summary>
    /// Full name of the request this endpoint dispatches, when it could be resolved.
    /// Simple names alone are ambiguous across modules with same-named commands.
    /// </summary>
    public string? TargetRequestFullName { get; set; }

    public string? ProjectName { get; set; }
    public SourceLocation? Location { get; set; }

    /// <summary>
    /// True when the route or HTTP verb was guessed from a naming convention (e.g. an
    /// EndpointGroup's class name, a verb implied by Query/Command) rather than read from source.
    /// Agent-facing output must flag these so consumers do not treat them as facts.
    /// </summary>
    public bool IsInferred { get; init; }

    public string DisplayLabel => string.IsNullOrEmpty(Route)
        ? $"{HttpMethod} ({Name})"
        : $"{HttpMethod} {Route}";
}
