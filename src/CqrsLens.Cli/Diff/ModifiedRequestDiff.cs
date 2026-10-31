using System.Text.Json.Serialization;

namespace CqrsLens;

/// <summary>A request whose kind, response, project, handlers or validators changed.</summary>
internal sealed class ModifiedRequestDiff
{
    public string FullName { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Project { get; set; }
    public string? OldKind { get; set; }
    public string? NewKind { get; set; }
    public string? OldResponseType { get; set; }
    public string? NewResponseType { get; set; }
    public string? OldProject { get; set; }
    public string? NewProject { get; set; }
    public List<string> AddedHandlers { get; set; } = new();
    public List<string> RemovedHandlers { get; set; } = new();
    public List<string> AddedValidators { get; set; } = new();
    public List<string> RemovedValidators { get; set; } = new();

    [JsonIgnore]
    public JsonRequest HeadSnapshot { get; set; } = new();
}
