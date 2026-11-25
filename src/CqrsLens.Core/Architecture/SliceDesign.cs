using System.Text.Json.Serialization;

namespace CqrsLens;

/// <summary>A saved architecture slice design. A null response type denotes a request or operation with no response.</summary>
public sealed record SliceDesign(
    int SchemaVersion,
    string Project,
    string Request,
    string Kind,
    string? ResponseType,
    string? Handler,
    string? Validator,
    SliceDesignEndpoint? Endpoint,
    IReadOnlyList<SliceDesignScenario> Scenarios,
    IReadOnlyList<SliceDesignProperty>? Properties = null,
    IReadOnlyList<string>? Publishes = null,
    IReadOnlyList<string>? Dispatches = null,
    SliceDesignPersistence? Persistence = null,
    IReadOnlyList<string>? Dependencies = null,
    IReadOnlyList<string>? ForbiddenDependencies = null)
{
    public const int CurrentSchemaVersion = 1;
    public const int MaxScenarios = 20;

    [JsonIgnore]
    public bool IsCqrsKind => Kind is "Command" or "Query" or "Request";
}
