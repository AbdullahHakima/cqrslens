using System.Text.RegularExpressions;
using static CqrsLens.DeepSlicePatterns;

namespace CqrsLens;

/// <summary>
/// Extracts handler dependencies and resolves the entities they imply, both from generic
/// repositories and from non-generic repository/service type names.
/// </summary>
internal static class HandlerDependencyEffects
{
    internal static void Apply(HandlerEffectContext context)
    {
        DependencyExtractor.Extract(
            context.Decl, context.File.Code, context.Mask, context.Scan.Classes, context.Dependencies);

        foreach (var dependency in context.Dependencies)
        {
            NodeReference[]? via = null;
            NodeReference[] GetVia() => via ??= [NodeReference.ForExternalInterface(dependency)];

            foreach (Match match in GenericRepositoryRegex().Matches(dependency))
            {
                var entitySimple = CSharpScanner.SimpleName(match.Groups["entity"].Value.Trim());
                if (!ReceiverTypeMapBuilder.IsEntityCandidate(entitySimple, context.Decl, context.Index.DbSetsByEntity))
                    continue;
                context.Touch(
                    context.Index.DbSetsByEntity.TryGetValue(entitySimple, out var dbLabel)
                        ? dbLabel
                        : entitySimple,
                    via: GetVia(),
                    strength: EvidenceStrength.Resolved);
            }

            var dependencySimple = CSharpScanner.UnqualifiedTypeName(dependency);
            if (string.IsNullOrEmpty(dependencySimple))
                continue;
            if (dependency.Contains('<'))
                continue;

            if (context.Index.RepoEntitiesByType.TryGetValue(dependencySimple, out var repoLabels))
            {
                foreach (var label in repoLabels)
                    context.Touch(label, via: GetVia(), strength: EvidenceStrength.Resolved);
            }
            else
            {
                var namedMatch = NamedRepositoryRegex().Match(dependencySimple);
                if (namedMatch.Success)
                {
                    var stem = namedMatch.Groups["entity"].Value;
                    if (context.Index.DbSetsByEntity.TryGetValue(stem, out var stemLabel))
                        context.Touch(stemLabel, via: GetVia(), strength: EvidenceStrength.Inferred);
                }
            }
        }
    }
}
