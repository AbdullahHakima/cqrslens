namespace CqrsLens;

/// <summary>
/// Mapping-derived HTTP endpoint component in the spine (F0 §2 v2.1, §3.1, §3.3).
/// Identified by <see cref="Id"/> (<see cref="EndpointId"/>), carrying per-fact
/// <see cref="VerbStrength"/> and <see cref="RouteStrength"/> provenance while preserving
/// <see cref="IsInferred"/>.
/// </summary>
public sealed class EndpointNode
{
    public EndpointId Id { get; }
    public ProjectId Project => Id.Project;
    public string Name { get; }
    public string HttpMethod { get; }
    public string Route { get; }
    public string DisplayLabel => string.IsNullOrEmpty(Route)
        ? $"{HttpMethod} ({Name})"
        : $"{HttpMethod} {Route}";
    public EvidenceStrength VerbStrength { get; }
    public EvidenceStrength RouteStrength { get; }
    public bool IsInferred => VerbStrength == EvidenceStrength.Inferred || RouteStrength == EvidenceStrength.Inferred;
    public SourceLocation? Location => Id.MappingSite;
    public IReadOnlyList<EvidenceOccurrence> Evidence { get; }

    public IReadOnlyList<FactProvenance> FactProvenances { get; }

    public EndpointNode(
        EndpointId id,
        string name,
        string httpMethod,
        string route,
        EvidenceStrength verbStrength,
        EvidenceStrength routeStrength,
        EvidenceOccurrence evidence)
        : this(id, name, httpMethod, route, verbStrength, routeStrength, [evidence])
    {
    }

    public EndpointNode(
        EndpointId id,
        string name,
        string httpMethod,
        string route,
        EvidenceStrength verbStrength,
        EvidenceStrength routeStrength,
        IReadOnlyList<EvidenceOccurrence> evidence)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(httpMethod);
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(evidence);
        if (evidence.Count == 0)
            throw new ArgumentException("An endpoint requires at least one evidence occurrence.", nameof(evidence));

        var copy = new EvidenceOccurrence[evidence.Count];
        for (var i = 0; i < evidence.Count; i++)
            copy[i] = evidence[i] ?? throw new ArgumentNullException(nameof(evidence));
        Array.Sort(copy);

        Id = id;
        Name = name.Trim();
        HttpMethod = httpMethod.Trim();
        Route = route;
        VerbStrength = verbStrength;
        RouteStrength = routeStrength;
        Evidence = copy;
        FactProvenances =
        [
            new FactProvenance("HttpMethod", verbStrength),
            new FactProvenance("Route", routeStrength),
        ];
    }
}
