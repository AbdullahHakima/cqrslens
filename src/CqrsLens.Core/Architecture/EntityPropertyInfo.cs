namespace CqrsLens;

/// <summary>
/// One property on an EF Core entity. Scalar properties become ER attributes;
/// navigation properties become relationships and are omitted from attributes.
/// </summary>
public sealed class EntityPropertyInfo
{
    public required string Name { get; init; }

    /// <summary>The property type as written (generics preserved for navigations).</summary>
    public required string Type { get; init; }

    /// <summary>True for a primary-key property (including one part of a composite key).</summary>
    public bool IsKey { get; set; }

    /// <summary>True when the property is used as a foreign key by a resolved relationship.</summary>
    public bool IsForeignKey { get; set; }

    public bool IsNavigation { get; init; }
    public bool IsCollection { get; init; }

    /// <summary>Full source name of the navigation target, when it resolves.</summary>
    public string? TargetEntity { get; init; }

    /// <summary>
    /// EF requiredness when it can be established from a value type, a data
    /// annotation or explicit configuration. Reference navigation requiredness is
    /// never guessed from naming alone.
    /// </summary>
    public bool IsRequired { get; set; }

    public SourceLocation? Location { get; init; }

    /// <summary>Raw data-annotation payloads used during model construction.</summary>
    internal List<string> Attributes { get; } = new();
}
