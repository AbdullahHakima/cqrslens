namespace CqrsLens;

/// <summary>
/// Interprets the call chain of one fluent configuration statement and extracts the keys
/// and relationship it declares.
/// </summary>
internal static class FluentStatementParser
{
    internal static FluentStatementResult Parse(
        (string Text, int Index) statement,
        CSharpScanner.ClassDecl declaration,
        string? defaultEntity,
        TypeHierarchy hierarchy,
        IReadOnlyDictionary<string, CSharpScanner.ClassDecl> classesByFull)
    {
        var calls = FluentCallReader.ParseCalls(statement.Text);
        if (calls.Count == 0) return FluentStatementResult.Empty;

        var keys = new List<EfKeyConfiguration>();
        var entity = defaultEntity;
        var writtenEntity = FluentCallReader.ParseEntity(statement.Text);
        if (writtenEntity is not null)
            entity = FluentTypeResolver.ResolveType(writtenEntity, declaration, hierarchy);

        string? firstNavigation = null;
        string? inverseNavigation = null;
        string? foreignKey = null;
        string? firstGenericTarget = null;
        string? foreignKeyTarget = null;
        bool? firstIsCollection = null;
        bool? inverseIsCollection = null;
        bool? required = null;

        foreach (var call in calls)
        {
            switch (call.Name)
            {
                case "Entity" when call.Generic is not null:
                    entity = FluentTypeResolver.ResolveType(call.Generic, declaration, hierarchy);
                    firstNavigation = null;
                    inverseNavigation = null;
                    foreignKey = null;
                    firstIsCollection = null;
                    inverseIsCollection = null;
                    required = null;
                    break;

                case "HasKey" when entity is not null:
                    foreach (var property in FluentCallReader.ParseMembers(call.Args))
                        keys.Add(new EfKeyConfiguration(entity, new[] { property }));
                    break;

                case "HasOne":
                case "HasMany":
                    firstNavigation = FluentCallReader.ParseMember(call.Args);
                    firstIsCollection = call.Name == "HasMany";
                    firstGenericTarget = call.Generic is null
                        ? null
                        : FluentTypeResolver.ResolveType(call.Generic, declaration, hierarchy);
                    inverseNavigation = null;
                    inverseIsCollection = null;
                    foreignKey = null;
                    required = null;
                    break;

                case "WithOne":
                case "WithMany":
                    inverseNavigation = FluentCallReader.ParseMember(call.Args);
                    inverseIsCollection = call.Name == "WithMany";
                    break;

                case "HasForeignKey":
                    foreignKey = FluentCallReader.ParseMember(call.Args);
                    foreignKeyTarget = call.Generic is null
                        ? null
                        : FluentTypeResolver.ResolveType(call.Generic, declaration, hierarchy);
                    break;

                case "IsRequired":
                    required = true;
                    break;
            }
        }

        if (entity is null || firstNavigation is null && firstIsCollection is null && firstGenericTarget is null)
            return new FluentStatementResult(keys, null);

        var entityDecl = FluentTypeResolver.FindClass(hierarchy, entity, declaration, classesByFull);
        var target = firstGenericTarget ??
                     (firstNavigation is null
                         ? null
                         : FluentTypeResolver.ResolveNavigationTarget(entityDecl, firstNavigation, hierarchy));
        if (target is null)
            return new FluentStatementResult(keys, null);

        var relationship = FluentRelationshipBuilder.Build(
            entity,
            target,
            firstNavigation,
            firstIsCollection,
            inverseNavigation,
            inverseIsCollection,
            foreignKey,
            foreignKeyTarget,
            required,
            entityDecl,
            hierarchy);
        return new FluentStatementResult(keys, relationship);
    }
}
