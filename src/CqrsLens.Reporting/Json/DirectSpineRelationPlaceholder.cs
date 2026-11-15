namespace CqrsLens;

/// <summary>
/// Lightweight carrier allowing <see cref="DirectSpineRelationsConverter"/> to stream
/// spine relations directly to JSON without allocating intermediate DTO graphs.
/// </summary>
internal sealed class DirectSpineRelationPlaceholder : List<JsonSpineRelation>
{
    internal DirectSpineRelationPlaceholder(IReadOnlyList<SpineRelation> spineRelations) : base(0)
    {
        SpineRelations = spineRelations;
    }

    internal IReadOnlyList<SpineRelation> SpineRelations { get; }
}
