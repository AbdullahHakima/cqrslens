using System.Text;

namespace CqrsLens;

/// <summary>Renders <c>05-cross-cutting.md</c> (configuration surface, pipeline behaviors, validators, and DI resolution).</summary>
internal static class DocsCrossCuttingPageWriter
{
    internal static string Write(AnalysisResult result)
    {
        var md = new StringBuilder();
        DocsFormatting.WriteHeader(md, "05 — Cross-Cutting Concerns & Configuration", result);

        WriteConfiguration(md, result);
        WriteBehaviors(md, result);
        WriteValidators(md, result);
        WriteServiceResolution(md, result);

        return md.ToString();
    }

    private static void WriteConfiguration(StringBuilder md, AnalysisResult result)
    {
        var view = ConfigurationView.Build(result);

        DocsFormatting.Line(md, "## Configuration & Conventions");
        DocsFormatting.Line(md);
        DocsFormatting.Line(md, "_Resolved scan configuration (`declared`) beside scan outcome (`actual`). Layer classification uses explicit overrides where configured and name/folder heuristics (`guessed (inferred)`) otherwise._");
        DocsFormatting.Line(md);

        DocsFormatting.Line(md, "### Declared");
        DocsFormatting.Line(md);
        DocsFormatting.Line(md, "| Setting | Value |");
        DocsFormatting.Line(md, "| --- | --- |");
        foreach (var row in view.Declared)
        {
            var value = string.Equals(row.Label, "Config file", StringComparison.Ordinal)
                ? DocsFormatting.FormatConfigPath(result, result.Configuration?.ConfigFilePath)
                : row.Value;
            DocsFormatting.Line(
                md,
                $"| {MarkdownEscaping.TableCell(row.Label)} | {MarkdownEscaping.TableCell(value)} |");
        }
        DocsFormatting.Line(md);

        DocsFormatting.Line(md, "### Actual");
        DocsFormatting.Line(md);
        DocsFormatting.Line(md, "| Fact | Value |");
        DocsFormatting.Line(md, "| --- | --- |");
        DocsFormatting.Line(md, $"| Layer-model status | {MarkdownEscaping.TableCell(view.LayerStatus)} |");
        DocsFormatting.Line(md, $"| Layer checks | {MarkdownEscaping.TableCell(view.LayerChecks)} |");
        DocsFormatting.Line(md, $"| Requests discovered | {view.RequestsDiscovered} |");
        DocsFormatting.Line(md, $"| Conventions detected | {MarkdownEscaping.TableCell(view.Conventions)} |");
        DocsFormatting.Line(md);
    }

    private static void WriteBehaviors(StringBuilder md, AnalysisResult result)
    {
        var wrapsCount = result.Spine.GetRelations(RelationKind.Wraps).Count;
        var unestablishedCount = result.Spine.Unproven.UnestablishedBehaviors.Count;

        DocsFormatting.Line(md, "## Pipeline Behaviors");
        DocsFormatting.Line(md);
        DocsFormatting.Line(md, $"> {BehaviorPresentationText.RegisteredOrderNote}");
        DocsFormatting.Line(md);
        DocsFormatting.Line(md, $"_Discovered behaviors: {result.Behaviors.Count} · `wraps` relations: {wrapsCount} · unestablished behaviors: {unestablishedCount}._");
        DocsFormatting.Line(md);

        if (result.Behaviors.Count == 0)
        {
            DocsFormatting.Line(md, "_No pipeline behavior implementations found in the model._");
            DocsFormatting.Line(md);
            return;
        }

        var chain = DiagramBuilder.BehaviorChain(result);
        if (chain is not null && DiagramBuilder.IsWithinMermaidLimits(chain))
        {
            DocsFormatting.Line(md, "Diagram: [diagrams/behavior-chain.svg](diagrams/behavior-chain.svg)");
            DocsFormatting.Line(md);
        }

        var registered = BehaviorOrdering.Registered(result.Behaviors);
        if (registered.Count > 0)
        {
            DocsFormatting.Line(md, "### Registered Order");
            DocsFormatting.Line(md);
            foreach (var group in registered)
            {
                DocsFormatting.Line(md, $"#### {MarkdownEscaping.CodeSpan(group.Site)}");
                DocsFormatting.Line(md);
                DocsFormatting.Line(md, "| # | Behavior | Project | Location |");
                DocsFormatting.Line(md, "| --- | --- | --- | --- |");
                foreach (var b in group.Behaviors)
                {
                    DocsFormatting.Line(
                        md,
                        $"| {b.RegistrationOrder} | {MarkdownEscaping.TableCell(b.Name)} | " +
                        $"{MarkdownEscaping.TableCell(b.ProjectName ?? "—")} | " +
                        $"{DocsFormatting.FormatLocation(b.Location)} |");
                }
                DocsFormatting.Line(md);
            }
        }

        var unregistered = BehaviorOrdering.Unregistered(result.Behaviors);
        if (unregistered.Count > 0)
        {
            DocsFormatting.Line(md, $"### {BehaviorPresentationText.UnregisteredHeading}");
            DocsFormatting.Line(md);
            DocsFormatting.Line(md, "| Behavior | Project | Location |");
            DocsFormatting.Line(md, "| --- | --- | --- |");
            foreach (var b in unregistered)
            {
                DocsFormatting.Line(
                    md,
                    $"| {MarkdownEscaping.TableCell(b.Name)} | " +
                    $"{MarkdownEscaping.TableCell(b.ProjectName ?? "—")} | " +
                    $"{DocsFormatting.FormatLocation(b.Location)} |");
            }
            DocsFormatting.Line(md);
        }
    }

    private static void WriteValidators(StringBuilder md, AnalysisResult result)
    {
        var coverage = ValidationCoverageView.Build(result);

        DocsFormatting.Line(md, "## Validation Coverage & Validators");
        DocsFormatting.Line(md);
        DocsFormatting.Line(md, "| Project | Requests | With linked validator | Coverage | Validators |");
        DocsFormatting.Line(md, "| --- | ---: | ---: | ---: | ---: |");
        foreach (var row in coverage.Projects)
        {
            DocsFormatting.Line(
                md,
                $"| {MarkdownEscaping.TableCell(row.Project)} | {row.Requests} | {row.Covered} | " +
                $"{MarkdownEscaping.TableCell(ValidationCoverageView.CoverageText(row))} | {row.Validators} |");
        }
        var sol = coverage.Solution;
        DocsFormatting.Line(
            md,
            $"| **{MarkdownEscaping.TableCell(sol.Project)}** | **{sol.Requests}** | **{sol.Covered}** | " +
            $"**{MarkdownEscaping.TableCell(ValidationCoverageView.CoverageText(sol))}** | **{sol.Validators}** |");
        DocsFormatting.Line(md);

        var orderedValidators = result.Validators
            .OrderBy(v => v.SpineId)
            .ThenBy(v => v.FullName, StringComparer.Ordinal)
            .ThenBy(v => v.TargetType, StringComparer.Ordinal)
            .ToList();
        DocsFormatting.Line(md, $"### Validator Inventory ({orderedValidators.Count})");
        DocsFormatting.Line(md);
        if (orderedValidators.Count == 0)
        {
            DocsFormatting.Line(md, "_No validators discovered in the model._");
        }
        else
        {
            DocsFormatting.Line(md, "| ComponentId | Validator | Target Type | Project | Location |");
            DocsFormatting.Line(md, "| --- | --- | --- | --- | --- |");
            foreach (var v in orderedValidators)
            {
                DocsFormatting.Line(
                    md,
                    $"| {MarkdownEscaping.CodeSpan(v.SpineId?.Value ?? v.FullName)} | " +
                    $"{MarkdownEscaping.TableCell(v.FullName)} | " +
                    $"{MarkdownEscaping.TableCell(v.TargetType)} | " +
                    $"{MarkdownEscaping.TableCell(v.ProjectName ?? "—")} | " +
                    $"{DocsFormatting.FormatLocation(v.Location)} |");
            }
        }
        DocsFormatting.Line(md);
    }

    private static void WriteServiceResolution(StringBuilder md, AnalysisResult result)
    {
        var resolvesTo = result.Spine.GetRelations(RelationKind.ResolvesTo);
        var confirmed = 0;
        var declaredBinding = 0;
        var staticCandidate = 0;
        foreach (var rel in resolvesTo)
        {
            var hasConfirmed = false;
            var hasBinding = false;
            foreach (var occ in rel.Occurrences)
            {
                if (occ.ResolutionSemantics == ResolutionSemantics.Confirmed)
                    hasConfirmed = true;
                else if (occ.ResolutionSemantics == ResolutionSemantics.DeclaredBinding)
                    hasBinding = true;
            }

            if (hasConfirmed)
                confirmed++;
            else if (hasBinding)
                declaredBinding++;
            else
                staticCandidate++;
        }

        DocsFormatting.Line(md, "## Service Resolution & Unproven Cross-Cutting Observations");
        DocsFormatting.Line(md);
        DocsFormatting.Line(md, "_`resolvesTo` records static declaration/registration evidence and does not prove runtime container resolution._");
        DocsFormatting.Line(md);
        DocsFormatting.Line(md, "| Semantics | Relations |");
        DocsFormatting.Line(md, "| --- | ---: |");
        DocsFormatting.Line(md, $"| `confirmed` (declaration + registration) | {confirmed} |");
        DocsFormatting.Line(md, $"| `declaredBinding` (registration only) | {declaredBinding} |");
        DocsFormatting.Line(md, $"| `staticCandidate` (single declaration only) | {staticCandidate} |");
        DocsFormatting.Line(md, $"| **Total `resolvesTo`** | **{resolvesTo.Count}** |");
        DocsFormatting.Line(md);

        var ambiguous = result.Spine.Unproven.AmbiguousResolutions
            .OrderBy(a => a.InterfaceSignature, StringComparer.Ordinal)
            .ToList();
        DocsFormatting.Line(md, $"### Ambiguous DI Resolutions ({ambiguous.Count})");
        DocsFormatting.Line(md);
        if (ambiguous.Count == 0)
        {
            DocsFormatting.Line(md, "_No ambiguous interface resolutions recorded._");
        }
        else
        {
            DocsFormatting.Line(md, "| Interface | Candidates | Candidate Types | Location |");
            DocsFormatting.Line(md, "| --- | ---: | --- | --- |");
            foreach (var entry in ambiguous)
            {
                var candidates = string.Join(", ", entry.Candidates
                    .Select(c => c.Type)
                    .OrderBy(t => t, StringComparer.Ordinal));
                DocsFormatting.Line(
                    md,
                    $"| {MarkdownEscaping.CodeSpan(entry.InterfaceSignature)} | " +
                    $"{entry.Candidates.Count} | " +
                    $"{MarkdownEscaping.TableCell(candidates)} | " +
                    $"{DocsFormatting.FormatLocation(entry.InterfaceLocation)} |");
            }
        }
        DocsFormatting.Line(md);

        var observations = result.Spine.Unproven.Observations;
        DocsFormatting.Line(md, $"### Unproven Cross-Cutting Observations ({observations.Count})");
        DocsFormatting.Line(md);
        if (observations.Count == 0)
        {
            DocsFormatting.Line(md, "_No unproven cross-cutting observations recorded._");
        }
        else
        {
            DocsFormatting.Line(md, "| Observation Kind | Count |");
            DocsFormatting.Line(md, "| --- | ---: |");
            foreach (var group in observations
                         .GroupBy(o => o.Kind)
                         .OrderBy(g => (int)g.Key))
            {
                DocsFormatting.Line(
                    md,
                    $"| `{JsonSpineNames.UnprovenItemKindName(group.Key)}` | {group.Count()} |");
            }
        }
        DocsFormatting.Line(md);
    }
}
