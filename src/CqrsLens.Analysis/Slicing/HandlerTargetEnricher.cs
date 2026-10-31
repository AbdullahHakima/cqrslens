namespace CqrsLens;

/// <summary>
/// Enriches one handler target by running the dependency, DbSet, dispatch and domain-member
/// effect passes in the same order.
/// </summary>
internal static class HandlerTargetEnricher
{
    internal static void Enrich(HandlerEffectContext context)
    {
        HandlerDependencyEffects.Apply(context);
        if (!context.HasBody)
            return;

        var receiverTypes = ReceiverTypeMapBuilder.Build(
            context.Decl,
            context.File.Code,
            context.Mask,
            context.Scan.Classes,
            context.Index.DbSetsByProperty,
            context.Index.DbContextTypeNames,
            context.Index.RepoEntitiesByType);

        HandlerDbSetEffects.Apply(context, receiverTypes);
        HandlerDispatchEffects.Apply(context);
        HandlerMemberEffects.Apply(context, receiverTypes);
    }
}
