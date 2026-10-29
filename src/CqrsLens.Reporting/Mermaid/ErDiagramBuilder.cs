using System.Text;
using static CqrsLens.MermaidDiagramText;

namespace CqrsLens;

/// <summary>Builds Entity Framework ER diagrams.</summary>
internal static class ErDiagramBuilder
{
    internal static string? ErDiagram(DbContextInfo context)
    {
        if (context.Entities.Count == 0)
            return null;

        var orderedEntities = context.Entities
            .OrderBy(e => e.FullName, StringComparer.Ordinal)
            .ToList();
        var nameCounts = orderedEntities
            .GroupBy(e => e.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var ids = new MermaidIdAllocator();
        var entityIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var entityLabels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entity in orderedEntities)
        {
            var label = nameCounts[entity.Name] > 1 ? entity.FullName : entity.Name;
            entityLabels[entity.FullName] = label;
            entityIds[entity.FullName] = ids.Allocate("entity:" + entity.FullName, "ent", label);
        }

        var orderedRelationships = context.Relationships
            .Where(r => entityIds.ContainsKey(r.PrincipalEntity) &&
                        entityIds.ContainsKey(r.DependentEntity))
            .OrderBy(r => r.PrincipalEntity, StringComparer.Ordinal)
            .ThenBy(r => r.DependentEntity, StringComparer.Ordinal)
            .ThenBy(r => r.PrincipalNavigation, StringComparer.Ordinal)
            .ThenBy(r => r.DependentNavigation, StringComparer.Ordinal)
            .ToList();

        var entityText = orderedEntities.Count == 1 ? "1 entity" : $"{orderedEntities.Count} entities";
        var relationshipText = CountText(orderedRelationships.Count, "relationship");

        var sb = new StringBuilder();
        sb.AppendLine("erDiagram");
        sb.AppendLine($"    accTitle: {MermaidLabel(context.Name)} ER diagram");
        sb.AppendLine($"    accDescr: {entityText}, {relationshipText}.");

        foreach (var entity in orderedEntities)
        {
            var id = entityIds[entity.FullName];
            var label = MermaidLabel(entityLabels[entity.FullName]);
            var scalars = entity.Properties
                .Where(p => !p.IsNavigation)
                .OrderBy(p => p.IsKey ? 0 : 1)
                .ThenBy(p => p.Name, StringComparer.Ordinal)
                .ToList();

            if (scalars.Count == 0)
            {
                sb.AppendLine($"    {id}[\"{label}\"]");
                continue;
            }

            sb.AppendLine($"    {id}[\"{label}\"] {{");
            foreach (var property in scalars)
            {
                var attrType = ErAttributeType(property.Type);
                var attrName = SanitizeMermaidId(property.Name);
                var keySuffix = (property.IsKey, property.IsForeignKey) switch
                {
                    (true, true) => " PK, FK",
                    (true, false) => " PK",
                    (false, true) => " FK",
                    _ => "",
                };
                sb.AppendLine($"        {attrType} {attrName}{keySuffix}");
            }
            sb.AppendLine("    }");
        }

        foreach (var relationship in orderedRelationships)
        {
            var principalId = entityIds[relationship.PrincipalEntity];
            var dependentId = entityIds[relationship.DependentEntity];
            var connector = ErConnector(relationship);
            var label = MermaidLabel(ErRelationshipLabel(relationship));
            sb.AppendLine($"    {principalId} {connector} {dependentId} : \"{label}\"");
        }

        return sb.ToString().TrimEnd();
    }

    private static string ErAttributeType(string? type)
    {
        var normalized = EfAnnotations.NormalizeType(type);
        if (normalized.StartsWith("global::", StringComparison.Ordinal))
            normalized = normalized[8..];

        var openCount = normalized.Count(c => c == '<');
        var closeCount = normalized.Count(c => c == '>');
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if ((c == '<' || c == '>') && openCount == 1 && closeCount == 1)
            {
                sb.Append('~');
            }
            else if (c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9'
                     or '_' or '.' or '[' or ']' or '?')
            {
                sb.Append(c);
            }
            else
            {
                sb.Append('_');
            }
        }

        var result = sb.ToString().Trim('_');
        if (result.Length == 0)
            return "object";
        if (!char.IsLetter(result[0]) && result[0] != '_')
            return "t_" + result;
        return result;
    }

    private static string ErConnector(EntityRelationshipInfo relationship)
        => relationship.Kind switch
        {
            EntityRelationshipKind.ManyToMany => "}o--o{",
            EntityRelationshipKind.OneToOne => relationship.IsRequired ? "||--o|" : "|o--o|",
            _ => relationship.IsRequired ? "||--o{" : "|o--o{",
        };

    private static string ErRelationshipLabel(EntityRelationshipInfo relationship)
    {
        var principalNav = relationship.PrincipalNavigation;
        var dependentNav = relationship.DependentNavigation;
        if (!string.IsNullOrEmpty(principalNav) && !string.IsNullOrEmpty(dependentNav))
        {
            return string.Equals(principalNav, dependentNav, StringComparison.Ordinal)
                ? principalNav
                : $"{principalNav} / {dependentNav}";
        }

        if (!string.IsNullOrEmpty(principalNav))
            return principalNav;
        if (!string.IsNullOrEmpty(dependentNav))
            return dependentNav;
        if (!string.IsNullOrEmpty(relationship.ForeignKey))
            return relationship.ForeignKey;
        return "has";
    }
}