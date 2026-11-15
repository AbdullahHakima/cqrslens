namespace CqrsLens;

public sealed class DbContextInfo
{
    public required string Name { get; init; }
    public string Namespace { get; set; } = "";
    public List<string> ContainingTypes { get; } = new();

    public string FullName => TypeFullName.Compose(Namespace, ContainingTypes, Name);

    public string? ProjectName { get; set; }
    public SourceLocation? Location { get; set; }
    internal ComponentId? SpineId { get; set; }
    public List<DbSetInfo> DbSets { get; } = new();

    /// <summary>Entities reachable from this context's DbSets through navigations.</summary>
    public SpineTrackedList<EntityInfo> Entities { get; } = new();

    /// <summary>Relationships discovered for <see cref="Entities"/>.</summary>
    public SpineTrackedList<EntityRelationshipInfo> Relationships { get; } = new();

    internal void AttachSpine(AnalysisResult result, ComponentId id)
    {
        SpineId = id;
        Entities.BindHooks(
            entity =>
            {
                var entityId = result.IdentityResolver.ResolveComponentId(
                    entity.ProjectName ?? ProjectName,
                    entity.Namespace,
                    entity.ContainingTypes,
                    entity.Name);
                result.Spine.AddOrMergeComponent(
                    entityId,
                    entity.Location ?? Location,
                    ComponentRole.Entity,
                    SpineIdentityResolver.SyntheticOccurrence(
                        EvidenceRule.EntityDiscovery,
                        EvidenceOrigin.Declaration,
                        entity.Location ?? Location,
                        EvidenceStrength.Proven,
                        scopeProjectName: entity.ProjectName ?? ProjectName));
            },
            entity =>
            {
                var entityId = result.IdentityResolver.ResolveComponentId(
                    entity.ProjectName ?? ProjectName,
                    entity.Namespace,
                    entity.ContainingTypes,
                    entity.Name);
                result.Spine.RemoveComponentRole(entityId, ComponentRole.Entity);
            },
            () => { });

        Relationships.BindHooks(
            rel =>
            {
                var resolver = result.IdentityResolver;
                var from = resolver.ResolveComponentOrExternalRef(rel.PrincipalEntity, ComponentRole.Entity, ProjectName);
                var to = resolver.ResolveComponentOrExternalRef(rel.DependentEntity, ComponentRole.Entity, ProjectName);
                result.Spine.AddOrMergeRelation(
                    RelationKind.Relates,
                    from,
                    to,
                    SpineIdentityResolver.SyntheticOccurrence(
                        rel.EvidenceRule,
                        rel.EvidenceRule == EvidenceRule.EntityFluentConfiguration
                            ? EvidenceOrigin.Declaration
                            : EvidenceOrigin.MappingOrAttribute,
                        rel.Location ?? Location,
                        EvidenceStrength.Proven,
                        scopeProjectName: ProjectName));
            },
            rel =>
            {
                result.Spine.RemoveRelations(r =>
                    r.Kind == RelationKind.Relates &&
                    string.Equals(r.From.SymbolId?.FullName ?? r.From.Name, rel.PrincipalEntity, StringComparison.Ordinal) &&
                    string.Equals(r.To.SymbolId?.FullName ?? r.To.Name, rel.DependentEntity, StringComparison.Ordinal));
            },
            () =>
            {
                var entityNames = new HashSet<string>(Entities.Select(e => e.FullName), StringComparer.Ordinal);
                result.Spine.RemoveRelations(r =>
                    r.Kind == RelationKind.Relates &&
                    entityNames.Contains(r.From.SymbolId?.FullName ?? r.From.Name));
            });
    }
}
