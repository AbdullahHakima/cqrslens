namespace CqrsLens;

/// <summary>
/// Decides whether a solution's name-inferred layer assignments describe a
/// trustworthy Clean Architecture model.
/// <para>
/// Layers are always inferred from project names. Layer words also occur in module
/// names by coincidence — a navigation-contracts library called
/// <c>...UI.Navigation</c>, or a framework target project called
/// <c>...Application.Cms.Core.Targets</c> — so a single convention match is weak
/// evidence. The assignment is only asserted when it actually describes the
/// solution: at least half of the non-test projects must classify into a layer
/// naming convention rather than into an ad-hoc folder fallback (framework,
/// modules, templates, src, ...). When most projects spill into such buckets the
/// handful of layer-named projects are coincidence, not a layer model.
/// </para>
/// </summary>
internal static class LayerModelConfidence
{
    /// <summary>
    /// True when convention classification describes the majority of the
    /// solution's non-test projects, so layer rules can be asserted as errors.
    /// </summary>
    public static bool IsAsserted(IReadOnlyList<ProjectInfo> projects)
    {
        var convention = 0;
        var nonTest = 0;

        foreach (var project in projects)
        {
            if (string.Equals(project.Layer, "Tests", StringComparison.OrdinalIgnoreCase))
                continue;

            nonTest++;
            if (LayerClassifier.IsConventionLayer(project.Layer))
                convention++;
        }

        return nonTest > 0 && convention * 2 >= nonTest;
    }
}
