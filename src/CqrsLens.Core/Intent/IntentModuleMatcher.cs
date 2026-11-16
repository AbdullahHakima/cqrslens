namespace CqrsLens;

/// <summary>
/// Shared deterministic selector matching for <see cref="IntentModuleDefinition"/>
/// (projects, components, and typed symbol references) used by both intent
/// checks and documentation views without duplicating rules.
/// </summary>
internal static class IntentModuleMatcher
{
    internal static bool MatchesProjectSelector(string selector, ProjectNode project)
        => string.Equals(project.Name, selector, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(project.Id.Value, selector, StringComparison.Ordinal);

    internal static bool MatchesComponentSelector(string selector, ComponentNode component)
        => MatchesSelectorOrPrefix(selector, component.Id.Value) ||
           MatchesSelectorOrPrefix(selector, component.Symbol.QualifiedName) ||
           MatchesSelectorOrPrefix(selector, component.Symbol.FullName) ||
           string.Equals(component.Name, selector, StringComparison.Ordinal);

    internal static bool MatchesSymbolSelector(string selector, SymbolId symbol)
        => MatchesSelectorOrPrefix(selector, symbol.ToString()) ||
           MatchesSelectorOrPrefix(selector, symbol.QualifiedName) ||
           MatchesSelectorOrPrefix(selector, symbol.FullName) ||
           string.Equals(symbol.Name, selector, StringComparison.Ordinal);

    internal static bool ComponentBelongsToModule(
        ComponentNode component,
        IntentModuleDefinition module,
        IReadOnlyDictionary<ProjectId, ProjectNode> projectsById)
    {
        if (module.Projects.Count > 0 &&
            projectsById.TryGetValue(component.Project, out var project))
        {
            for (var i = 0; i < module.Projects.Count; i++)
            {
                if (MatchesProjectSelector(module.Projects[i], project))
                    return true;
            }
        }

        for (var i = 0; i < module.Components.Count; i++)
        {
            if (MatchesComponentSelector(module.Components[i], component))
                return true;
        }

        return false;
    }

    internal static bool NodeBelongsToModule(
        NodeReference node,
        IntentModuleDefinition module,
        ArchitectureSpine spine,
        IReadOnlyDictionary<ProjectId, ProjectNode> projectsById)
    {
        if (node.ComponentId is not null &&
            spine.TryGetComponent(node.ComponentId, out var comp) &&
            comp is not null)
        {
            return ComponentBelongsToModule(comp, module, projectsById);
        }

        if (node.SymbolId is not null)
        {
            var symCompId = ComponentId.FromSymbol(node.SymbolId);
            if (spine.TryGetComponent(symCompId, out var symComp) && symComp is not null)
                return ComponentBelongsToModule(symComp, module, projectsById);

            if (module.Projects.Count > 0 &&
                projectsById.TryGetValue(node.SymbolId.Project, out var symProject))
            {
                for (var i = 0; i < module.Projects.Count; i++)
                {
                    if (MatchesProjectSelector(module.Projects[i], symProject))
                        return true;
                }
            }

            for (var i = 0; i < module.Components.Count; i++)
            {
                if (MatchesSymbolSelector(module.Components[i], node.SymbolId))
                    return true;
            }

            return false;
        }

        if (node.ProjectId is not null &&
            module.Projects.Count > 0 &&
            projectsById.TryGetValue(node.ProjectId, out var project))
        {
            for (var i = 0; i < module.Projects.Count; i++)
            {
                if (MatchesProjectSelector(module.Projects[i], project))
                    return true;
            }
        }

        return false;
    }

    internal static bool MatchesSelectorOrPrefix(string selector, string candidate)
    {
        if (string.Equals(candidate, selector, StringComparison.Ordinal))
            return true;

        if (!candidate.StartsWith(selector, StringComparison.Ordinal) ||
            candidate.Length <= selector.Length)
        {
            return false;
        }

        if (selector.EndsWith('.') || selector.EndsWith("::", StringComparison.Ordinal))
            return true;

        var next = candidate[selector.Length];
        return next is '.' or '`' or '<' ||
               (next == ':' && selector.Length + 1 < candidate.Length && candidate[selector.Length + 1] == ':');
    }
}
