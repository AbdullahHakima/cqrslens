using System.Text;

namespace CqrsLens;

/// <summary>
/// Freshness gate for <c>--check-docs &lt;dir&gt;</c> (F3 R-GATE / D2):
/// regenerates the docs tree to a temporary directory, compares every file
/// byte-for-byte against the committed directory (read-only), attributes drift
/// to changed model sections, fails closed on scan errors, and integrates with
/// <c>--strict</c> for active error findings.
/// </summary>
internal static class DocsFreshnessGate
{
    internal static int Check(
        AnalysisResult result,
        CliOptions options,
        PreparedScan scan,
        TextWriter stdout,
        TextWriter stderr)
    {
        var targetDir = Path.GetFullPath(options.CheckDocsDirectory!);
        if (File.Exists(targetDir))
        {
            throw new CliFailureException(
                $"--check-docs path is a file, not a directory: {options.CheckDocsDirectory}",
                ExitCodes.Usage);
        }

        if (HasScanFailure(result, out var failureReason))
        {
            stdout.WriteLine($"Docs freshness gate failed: scan failed or is incomplete ({failureReason}).");
            stderr.WriteLine($"error: docs freshness gate failed — scan failed ({failureReason}).");
            return ExitCodes.StrictFailure;
        }

        WriteIntentSummary(result, stdout);

        var tempDir = Path.Combine(Path.GetTempPath(), "cqrslens-check-docs-" + Guid.NewGuid().ToString("N"));
        try
        {
            DocsExporter.Export(result, tempDir, scan.CqrsMapSplit);

            var expectedFiles = CollectRelativeFiles(tempDir);
            var actualFiles = Directory.Exists(targetDir)
                ? CollectRelativeFiles(targetDir)
                : new Dictionary<string, string>(StringComparer.Ordinal);

            var allPaths = expectedFiles.Keys
                .Union(actualFiles.Keys, StringComparer.Ordinal)
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();

            var drift = new List<DocsDriftEntry>();
            foreach (var rel in allPaths)
            {
                var hasExpected = expectedFiles.TryGetValue(rel, out var expectedFull);
                var hasActual = actualFiles.TryGetValue(rel, out var actualFull);

                if (!hasActual && hasExpected)
                {
                    drift.Add(new DocsDriftEntry(
                        rel,
                        "missing",
                        ClassifyDrift(rel, actualContent: null, File.ReadAllText(expectedFull!, Encoding.UTF8))));
                    continue;
                }

                if (hasActual && !hasExpected)
                {
                    var label = rel.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
                        ? $"diagram changed ({Path.GetFileName(rel)} removed)"
                        : $"unexpected file ({rel})";
                    drift.Add(new DocsDriftEntry(rel, "unexpected", new[] { label }));
                    continue;
                }

                var expectedBytes = File.ReadAllBytes(expectedFull!);
                var actualBytes = File.ReadAllBytes(actualFull!);
                if (!expectedBytes.AsSpan().SequenceEqual(actualBytes))
                {
                    var actualText = rel.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                        ? Encoding.UTF8.GetString(actualBytes)
                        : null;
                    var expectedText = rel.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                        ? Encoding.UTF8.GetString(expectedBytes)
                        : null;
                    drift.Add(new DocsDriftEntry(
                        rel,
                        "modified",
                        ClassifyDrift(rel, actualText, expectedText)));
                }
            }

            if (drift.Count > 0)
            {
                stdout.WriteLine($"Docs drift detected in {targetDir} ({drift.Count} file(s)):");
                foreach (var entry in drift)
                {
                    stdout.WriteLine($"  - {entry.RelativePath} [{entry.Status}]: {string.Join("; ", entry.ChangedSections)}");
                }
                stderr.WriteLine($"error: docs freshness gate failed — {drift.Count} drifted file(s) in {targetDir}.");
                return ExitCodes.StrictFailure;
            }

            var activeErrors = result.Checks.Count(c =>
                c.Severity == CheckSeverity.Error && (!result.BaselineApplied || !c.IsKnown));
            if (options.Strict && activeErrors > 0)
            {
                stdout.WriteLine(
                    $"Docs tree is fresh ({expectedFiles.Count} file(s)), but --strict failed on {activeErrors} active check error(s).");
                var suffix = result.BaselineApplied
                    ? "new architecture/intent check error(s)"
                    : "architecture/intent check error(s)";
                stderr.WriteLine($"error: --strict - {activeErrors} {suffix} found.");
                return ExitCodes.StrictFailure;
            }

            stdout.WriteLine($"Docs freshness gate passed: {targetDir} matches regenerated output ({expectedFiles.Count} file(s)).");
            return ExitCodes.Success;
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
                /* best effort cleanup */
            }
        }
    }

    private static bool HasScanFailure(AnalysisResult result, out string reason)
    {
        if (result.SkippedFiles.Count > 0)
        {
            reason = $"{result.SkippedFiles.Count} skipped source file(s)";
            return true;
        }

        var errorDiagnostics = result.Diagnostics.Count(d => d.Severity == AnalysisDiagnosticSeverity.Error);
        if (errorDiagnostics > 0)
        {
            reason = $"{errorDiagnostics} scan error diagnostic(s)";
            return true;
        }

        if (!result.CanRunStrictChecks)
        {
            var codes = string.Join(", ", result.Diagnostics
                .Select(d => d.Code)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(c => c, StringComparer.Ordinal));
            reason = string.IsNullOrEmpty(codes) ? "incomplete scan" : $"incomplete/ambiguous scan ({codes})";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    private static void WriteIntentSummary(AnalysisResult result, TextWriter stdout)
    {
        if (result.Intent is null)
            return;

        var intentFindings = result.Checks
            .Where(c => (!result.BaselineApplied || !c.IsKnown) &&
                        c.Code is ArchitectureChecker.IntentBoundaryCode
                            or ArchitectureChecker.IntentSurfaceCode
                            or ArchitectureChecker.IntentStaleCode)
            .ToList();

        var errors = intentFindings.Count(f => f.Severity == CheckSeverity.Error);
        var warnings = intentFindings.Count - errors;
        stdout.WriteLine($"Intent checks: {errors} error(s), {warnings} warning(s).");
        foreach (var finding in intentFindings)
        {
            var sev = finding.Severity == CheckSeverity.Error ? "error" : "warning";
            var loc = finding.Location is null ? "" : $" [{finding.Location.File}:{finding.Location.Line}]";
            stdout.WriteLine($"  - [{sev}] {finding.Code}: {finding.Message}{loc}");
        }
    }

    private static Dictionary<string, string> CollectRelativeFiles(string root)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var fullPath in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(root, fullPath).Replace('\\', '/');
            map[rel] = fullPath;
        }
        return map;
    }

    private static IReadOnlyList<string> ClassifyDrift(
        string relativePath,
        string? actualContent,
        string? expectedContent)
    {
        if (relativePath.StartsWith("diagrams/", StringComparison.OrdinalIgnoreCase) ||
            relativePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            return new[] { $"diagram changed ({Path.GetFileName(relativePath)})" };
        }

        if (actualContent is null || expectedContent is null)
        {
            return DefaultSectionLabelsForFile(relativePath);
        }

        var actualSections = SplitMarkdownSections(actualContent);
        var expectedSections = SplitMarkdownSections(expectedContent);
        var allHeadings = actualSections.Keys
            .Union(expectedSections.Keys, StringComparer.Ordinal)
            .ToList();

        var labels = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var heading in allHeadings)
        {
            actualSections.TryGetValue(heading, out var actualBody);
            expectedSections.TryGetValue(heading, out var expectedBody);
            if (string.Equals(actualBody, expectedBody, StringComparison.Ordinal))
                continue;

            foreach (var label in MapHeadingToModelChanges(relativePath, heading, actualBody, expectedBody))
            {
                if (seen.Add(label))
                    labels.Add(label);
            }
        }

        if (labels.Count == 0)
            labels.Add($"content changed ({relativePath})");

        return labels;
    }

    private static IReadOnlyList<string> DefaultSectionLabelsForFile(string relativePath)
        => relativePath switch
        {
            "01-context.md" => new[] { "project added/removed", "component added/removed" },
            "02-building-blocks.md" => new[] { "component added/removed", "relation added/removed" },
            "03-runtime.md" => new[] { "relation added/removed", "diagram / sequence change" },
            "04-data-model.md" => new[] { "component added/removed", "relation added/removed" },
            "05-cross-cutting.md" => new[] { "configuration change", "component added/removed" },
            "06-findings.md" => new[] { "finding added/removed" },
            _ => new[] { $"file changed ({relativePath})" },
        };

    private static Dictionary<string, string> SplitMarkdownSections(string markdown)
    {
        var sections = new Dictionary<string, string>(StringComparer.Ordinal);
        var currentHeading = "__header__";
        var sb = new StringBuilder();
        var inCodeFence = false;
        var normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal);

        foreach (var line in normalized.Split('\n'))
        {
            if (line.StartsWith("```", StringComparison.Ordinal))
                inCodeFence = !inCodeFence;

            if (!inCodeFence && (line.StartsWith("## ", StringComparison.Ordinal) || line.StartsWith("### ", StringComparison.Ordinal)))
            {
                sections[currentHeading] = sb.ToString().TrimEnd('\n');
                sb.Clear();
                var rawHeading = line.StartsWith("### ", StringComparison.Ordinal)
                    ? line[4..].Trim()
                    : line[3..].Trim();
                // Strip trailing counts like " (516)" so added/removed items map to the same canonical section key.
                var parenIdx = rawHeading.LastIndexOf(" (", StringComparison.Ordinal);
                if (parenIdx > 0 && rawHeading.EndsWith(')') && int.TryParse(rawHeading[(parenIdx + 2)..^1], out _))
                    rawHeading = rawHeading[..parenIdx];
                currentHeading = rawHeading;
                sb.Append(line).Append('\n');
                continue;
            }

            sb.Append(line).Append('\n');
        }

        sections[currentHeading] = sb.ToString().TrimEnd('\n');
        return sections;
    }

    private static IEnumerable<string> MapHeadingToModelChanges(
        string relativePath,
        string heading,
        string? actualBody,
        string? expectedBody)
    {
        if (string.Equals(heading, "__header__", StringComparison.Ordinal))
        {
            yield return "header / metadata change";
            yield break;
        }

        switch (relativePath)
        {
            case "01-context.md":
                if (heading is "Projects & References")
                {
                    yield return "project added/removed (Projects & References)";
                    yield break;
                }
                if (heading is "External References in Relations")
                {
                    yield return "relation added/removed (External References in Relations)";
                    yield break;
                }
                if (heading is "Entry Points (Endpoints)")
                {
                    yield return "component added/removed (Entry Points)";
                    yield break;
                }
                if (heading is "Declared Public Surface" or "Declared Public Endpoints" or "Declared Public Components")
                {
                    yield return $"intent change ({heading})";
                    yield break;
                }
                if (heading is "System Context Summary")
                {
                    foreach (var label in ClassifySummaryLines(actualBody, expectedBody,
                                 ("Projects", "project added/removed"),
                                 ("Project references", "relation added/removed"),
                                 ("External references", "relation added/removed"),
                                 ("Endpoints", "component added/removed")))
                        yield return label;
                    yield break;
                }
                break;

            case "02-building-blocks.md":
                if (heading is "Component & Relation Summary")
                {
                    foreach (var label in ClassifySummaryLines(actualBody, expectedBody,
                                 ("Unique components", "component added/removed"),
                                 ("Endpoints", "component added/removed"),
                                 ("Relations", "relation added/removed"),
                                 ("Unproven", "unproven population change")))
                        yield return label;
                    yield break;
                }
                if (heading is "Components by Role" or "Building Blocks by Layer" or "Component Inventory")
                {
                    yield return $"component added/removed ({heading})";
                    yield break;
                }
                if (heading is "Relations Summary" or "Relations by Kind & Evidence Rule")
                {
                    yield return $"relation added/removed ({heading})";
                    yield break;
                }
                if (heading is "Declared Intent" or "Declared Modules & Ownership" or "Declared Boundaries")
                {
                    yield return $"intent change ({heading})";
                    yield break;
                }
                if (heading is "Unproven Populations")
                {
                    yield return "unproven population change (Unproven Populations)";
                    yield break;
                }
                break;

            case "03-runtime.md":
                if (heading is "Per-Flow Sequences" or "Flow & Notification Diagrams")
                {
                    yield return $"diagram / sequence change ({heading})";
                    yield break;
                }
                if (heading is "Recorded Runtime Flows" or "Controller Endpoint Exposures" or "Component Call & Data Flows")
                {
                    yield return $"relation added/removed ({heading})";
                    yield break;
                }
                if (heading is "Runtime Summary")
                {
                    foreach (var label in ClassifySummaryLines(actualBody, expectedBody,
                                 ("Requests", "component added/removed"),
                                 ("Handlers", "component added/removed"),
                                 ("Notifications", "component added/removed"),
                                 ("sequences", "diagram / sequence change"),
                                 ("Flow diagram", "diagram / sequence change"),
                                 ("relations", "relation added/removed")))
                        yield return label;
                    yield break;
                }
                break;

            case "04-data-model.md":
                if (heading is "EF Core Data Model" or "Repository Components")
                {
                    yield return $"component added/removed ({heading})";
                    yield break;
                }
                if (heading is "Repository & Data-Access Relations" or "Data-Access Edges")
                {
                    yield return $"relation added/removed ({heading})";
                    yield break;
                }
                if (heading is "Data Model Summary")
                {
                    foreach (var label in ClassifySummaryLines(actualBody, expectedBody,
                                 ("DbContexts", "component added/removed"),
                                 ("Entities", "component added/removed"),
                                 ("Repository", "component added/removed"),
                                 ("relationships", "relation added/removed"),
                                 ("relations", "relation added/removed")))
                        yield return label;
                    yield break;
                }
                break;

            case "05-cross-cutting.md":
                if (heading is "Configuration & Conventions" or "Declared" or "Actual")
                {
                    yield return $"configuration change ({heading})";
                    yield break;
                }
                if (heading is "Pipeline Behaviors" or "Registered Order" or "Validation Coverage & Validators" or "Validator Inventory")
                {
                    yield return $"component added/removed ({heading})";
                    yield break;
                }
                if (heading is "Service Resolution & Unproven Cross-Cutting Observations" or "Ambiguous DI Resolutions" or "Unproven Cross-Cutting Observations")
                {
                    yield return $"relation added/removed ({heading})";
                    yield break;
                }
                break;

            case "06-findings.md":
                if (heading is "Intent Verification Findings" or "Intent Findings Detail")
                {
                    yield return $"intent change ({heading})";
                    yield return $"finding added/removed ({heading})";
                    yield break;
                }
                if (heading is "Findings Summary" or "Architecture & Intent Findings" or "Findings by Rule" or "Findings by Module" or "Findings by Module & Feature" or "Detailed Active Findings" or "Known Findings (Baseline)")
                {
                    yield return $"finding added/removed ({heading})";
                    yield break;
                }
                if (heading is "Analysis Diagnostics")
                {
                    yield return "diagnostic change (Analysis Diagnostics)";
                    yield break;
                }
                break;
        }

        yield return $"section changed ({heading})";
    }

    private static IEnumerable<string> ClassifySummaryLines(
        string? actualBody,
        string? expectedBody,
        params (string Token, string Label)[] rules)
    {
        var actualLines = (actualBody ?? string.Empty).Split('\n');
        var expectedLines = (expectedBody ?? string.Empty).Split('\n');
        var actualSet = new HashSet<string>(actualLines, StringComparer.Ordinal);
        var expectedSet = new HashSet<string>(expectedLines, StringComparer.Ordinal);

        var matched = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in actualLines.Where(l => !expectedSet.Contains(l))
                     .Concat(expectedLines.Where(l => !actualSet.Contains(l))))
        {
            foreach (var (token, label) in rules)
            {
                if (line.Contains(token, StringComparison.OrdinalIgnoreCase) && matched.Add(label))
                    yield return label;
            }
        }

        if (matched.Count == 0)
            yield return "summary changed";
    }
}
