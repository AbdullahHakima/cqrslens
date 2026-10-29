namespace CqrsLens;

/// <summary>
/// Builds an <see cref="EntityInfo"/> from backend-neutral mapped properties, applying the
/// shared annotation, requiredness, navigation and ordering rules for text and Roslyn modes.
/// </summary>
internal static class EfEntityFactory
{
    internal static EntityInfo Create(
        string name,
        string @namespace,
        IReadOnlyList<string> containingTypes,
        string? projectName,
        SourceLocation? location,
        IReadOnlyList<EfMappedProperty> properties,
        IReadOnlyList<string> keyNames)
    {
        var entity = new EntityInfo
        {
            Name = name,
            Namespace = @namespace,
            ProjectName = projectName,
            Location = location,
        };
        entity.ContainingTypes.AddRange(containingTypes);

        var keys = keyNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var property in properties)
        {
            if (!property.IsPublic || property.IsStatic) continue;
            if (EfAnnotations.HasAttribute(property.Attributes, "NotMapped") ||
                EfAnnotations.HasAttribute(property.Attributes, "Ignore"))
                continue;

            var isNavigation = property.TargetEntityFullName is not null;
            var isRequired = EfAnnotations.HasAttribute(property.Attributes, "Required") ||
                             (!isNavigation && !property.IsNullable &&
                              EfAnnotations.IsLikelyValueType(property.Type));
            var info = new EntityPropertyInfo
            {
                Name = property.Name,
                Type = property.Type,
                IsNavigation = isNavigation,
                IsCollection = isNavigation && property.IsCollection,
                TargetEntity = property.TargetEntityFullName,
                IsRequired = isRequired && !property.IsNullable,
                Location = property.Location,
            };
            info.IsKey = keys.Contains(property.Name);
            info.Attributes.AddRange(property.Attributes);
            if (!isNavigation && EfAnnotations.HasAttribute(property.Attributes, "ForeignKey"))
                info.IsForeignKey = true;
            entity.Properties.Add(info);
        }

        // Mapped-property lineage is base-first, but a derived override wins.
        var deduped = entity.Properties
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .OrderBy(p => p.IsKey ? 0 : 1)
            .ThenBy(p => p.Name, StringComparer.Ordinal)
            .ToList();
        entity.Properties.Clear();
        entity.Properties.AddRange(deduped);
        return entity;
    }
}
