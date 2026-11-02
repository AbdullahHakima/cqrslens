using static CqrsLens.MermaidDiagramText;

namespace CqrsLens;

/// <summary>
/// Groups the existing architecture findings into module (project) and feature
/// clusters and computes aggregate counts by rule, severity and module. This is
/// presentation/aggregation only: it reads <see cref="AnalysisResult.Checks"/> and
/// the already-discovered shapes, and never invents findings or severities.
/// </summary>
internal static class FindingsClustering
{
    internal const string Unassigned = "(unassigned)";

    /// <summary>Findings and severity counts for one rule.</summary>
    internal sealed record RuleMetric(string Code, int Errors, int Warnings)
    {
        internal int Total => Errors + Warnings;
    }

    /// <summary>Findings and severity counts for one module (project).</summary>
    internal sealed record ModuleMetric(string Module, int Errors, int Warnings)
    {
        internal int Total => Errors + Warnings;
    }

    /// <summary>
    /// One module + feature cluster: its total and its per-rule breakdown. Every
    /// finding lands in exactly one cluster (feature may be <see cref="Unassigned"/>).
    /// </summary>
    internal sealed record FeatureCluster(
        string Module,
        string Feature,
        IReadOnlyList<RuleMetric> Rules,
        int Errors,
        int Warnings)
    {
        internal int Total => Errors + Warnings;
        internal string Label => string.Equals(Module, Feature, StringComparison.Ordinal)
            ? Module
            : $"{Module} — {Feature}";
    }

    internal sealed record FindingsMetrics(
        int Errors,
        int Warnings,
        IReadOnlyList<RuleMetric> ByRule,
        IReadOnlyList<ModuleMetric> ByModule,
        IReadOnlyList<FeatureCluster> Clusters)
    {
        internal int Total => Errors + Warnings;
    }

    /// <summary>Clusters the report's active findings (new findings when a baseline applied).</summary>
    internal static FindingsMetrics Analyze(AnalysisResult result)
        => Analyze(ActiveFindings(result), result);

    internal static FindingsMetrics Analyze(
        IReadOnlyList<CheckFinding> findings,
        AnalysisResult result)
    {
        var resolver = new TargetResolver(result);
        var errors = findings.Count(f => f.Severity == CheckSeverity.Error);
        var warnings = findings.Count - errors;

        var byRule = findings
            .GroupBy(f => f.Code, StringComparer.Ordinal)
            .Select(g => new RuleMetric(
                g.Key,
                g.Count(f => f.Severity == CheckSeverity.Error),
                g.Count(f => f.Severity == CheckSeverity.Warning)))
            .OrderBy(m => m.Errors > 0 ? 0 : 1)
            .ThenByDescending(m => m.Total)
            .ThenBy(m => m.Code, StringComparer.Ordinal)
            .ToList();

        var byModule = findings
            .GroupBy(f => ModuleOf(f, resolver), StringComparer.Ordinal)
            .Select(g => new ModuleMetric(
                g.Key,
                g.Count(f => f.Severity == CheckSeverity.Error),
                g.Count(f => f.Severity == CheckSeverity.Warning)))
            .OrderByDescending(m => m.Total)
            .ThenBy(m => m.Module, StringComparer.Ordinal)
            .ToList();

        var clusters = findings
            .GroupBy(f => (Module: ModuleOf(f, resolver), Feature: FeatureOf(f, resolver)))
            .Select(g => new FeatureCluster(
                g.Key.Module,
                g.Key.Feature,
                g.GroupBy(f => f.Code, StringComparer.Ordinal)
                    .Select(r => new RuleMetric(
                        r.Key,
                        r.Count(f => f.Severity == CheckSeverity.Error),
                        r.Count(f => f.Severity == CheckSeverity.Warning)))
                    .OrderBy(m => m.Errors > 0 ? 0 : 1)
                    .ThenByDescending(m => m.Total)
                    .ThenBy(m => m.Code, StringComparer.Ordinal)
                    .ToList(),
                g.Count(f => f.Severity == CheckSeverity.Error),
                g.Count(f => f.Severity == CheckSeverity.Warning)))
            .OrderBy(c => c.Module, StringComparer.Ordinal)
            .ThenBy(c => c.Feature, StringComparer.Ordinal)
            .ToList();

        return new FindingsMetrics(errors, warnings, byRule, byModule, clusters);
    }

    internal static List<CheckFinding> ActiveFindings(AnalysisResult result)
        => result.BaselineApplied
            ? result.Checks.Where(c => !c.IsKnown).ToList()
            : result.Checks.ToList();

    private static string ModuleOf(CheckFinding finding, TargetResolver resolver)
    {
        if (!string.IsNullOrEmpty(finding.ProjectName))
            return finding.ProjectName!;

        return resolver.Resolve(finding.Target)?.Project ?? Unassigned;
    }

    private static string FeatureOf(CheckFinding finding, TargetResolver resolver)
    {
        var ns = resolver.Resolve(finding.Target)?.Namespace;
        if (string.IsNullOrEmpty(ns))
            return Unassigned;

        // Reuse the exact feature definition the CQRS map split uses.
        return CqrsRequestOrdering.FeatureName(ns, ModuleOf(finding, resolver));
    }

    /// <summary>Resolves a finding target full/simple name to its declaring shape.</summary>
    private sealed class TargetResolver
    {
        private readonly Dictionary<string, (string? Namespace, string? Project)> _byFullName =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, (string? Namespace, string? Project)> _bySimpleName =
            new(StringComparer.Ordinal);
        private readonly HashSet<string> _ambiguousSimpleNames = new(StringComparer.Ordinal);

        internal TargetResolver(AnalysisResult result)
        {
            Add(result.Requests.Select(r => (r.FullName, r.Name, r.Namespace, r.ProjectName)));
            Add(result.Notifications.Select(n => (n.FullName, n.Name, n.Namespace, n.ProjectName)));
            Add(result.Validators.Select(v => (v.FullName, v.Name, v.Namespace, v.ProjectName)));
        }

        private void Add(IEnumerable<(string Full, string Simple, string? Namespace, string? Project)> shapes)
        {
            foreach (var (full, simple, ns, project) in shapes)
            {
                _byFullName.TryAdd(full, (ns, project));
                if (!_bySimpleName.TryAdd(simple, (ns, project)))
                    _ambiguousSimpleNames.Add(simple);
            }
        }

        internal (string? Namespace, string? Project)? Resolve(string? target)
        {
            if (string.IsNullOrEmpty(target))
                return null;

            if (_byFullName.TryGetValue(target, out var full))
                return full;

            if (_bySimpleName.TryGetValue(target, out var simple) && !_ambiguousSimpleNames.Contains(target))
                return simple;

            return null;
        }
    }
}
