using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CqrsLens;

/// <summary>
/// Emits SARIF 2.1.0 for architecture checks. Results carry rule metadata,
/// source locations, stable fingerprints and (when a baseline was used) the
/// SARIF baselineState so code-scanning UIs can separate known from new work.
/// </summary>
public static class SarifReportGenerator
{
    public const string Schema = "https://json.schemastore.org/sarif-2.1.0.json";
    public const string InformationUri = "https://github.com/AbdullahHakima/cqrslens";

    private static readonly Dictionary<string, (string Name, string Description, string Level)> Rules =
        new(StringComparer.Ordinal)
        {
            [ArchitectureChecker.MissingHandlerCode] =
                ("MissingHandler", "Request has no handler", "error"),
            [ArchitectureChecker.LayerViolationCode] =
                ("LayerViolation", "Domain dependency points to an outer layer", "error"),
            [ArchitectureChecker.CommandWithoutValidatorCode] =
                ("CommandWithoutValidator", "Command has no validator", "warning"),
            [ArchitectureChecker.AmbiguousProjectNameCode] =
                ("AmbiguousProjectName", "Duplicate project name makes references ambiguous", "warning"),
            [ArchitectureChecker.MultipleHandlersCode] =
                ("MultipleHandlers", "Request has more than one handler", "error"),
            [ArchitectureChecker.ApplicationLayerViolationCode] =
                ("ApplicationLayerViolation", "Application dependency points to an outer layer", "error"),
            [ArchitectureChecker.CircularProjectDependencyCode] =
                ("CircularProjectDependency", "Project reference cycle detected", "error"),
            [ArchitectureChecker.OrphanValidatorCode] =
                ("OrphanValidator", "Validator target does not match any discovered CQRS request", "warning"),
            [ArchitectureChecker.OrphanNotificationCode] =
                ("OrphanNotification", "Notification has no handler", "warning"),
        };

    public static string Generate(AnalysisResult result, string? ciRoot = null)
    {
        var checks = result.Checks
            .OrderBy(c => c.IsKnown ? 1 : 0)
            .ThenByDescending(c => c.Severity)
            .ThenBy(c => c.Code, StringComparer.Ordinal)
            .ThenBy(c => c.Message, StringComparer.Ordinal)
            .ToList();
        var ruleCodes = checks.Select(c => c.Code).Distinct(StringComparer.Ordinal).ToList();
        var ruleIndexes = ruleCodes
            .Select((code, index) => (code, index))
            .ToDictionary(x => x.code, x => x.index, StringComparer.Ordinal);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("$schema", Schema);
            writer.WriteString("version", "2.1.0");
            writer.WriteStartArray("runs");
            writer.WriteStartObject();

            writer.WriteStartObject("tool");
            writer.WriteStartObject("driver");
            writer.WriteString("name", "CqrsLens");
            writer.WriteString("fullName", "CqrsLens .NET architecture scanner");
            writer.WriteString("version", ToolInfo.Version);
            writer.WriteString("informationUri", InformationUri);
            writer.WriteStartArray("rules");
            foreach (var code in ruleCodes)
            {
                var rule = Rules[code];
                writer.WriteStartObject();
                writer.WriteString("id", code);
                writer.WriteString("name", rule.Name);
                writer.WriteStartObject("shortDescription");
                writer.WriteString("text", rule.Description);
                writer.WriteEndObject();
                writer.WriteString("helpUri", $"{InformationUri}#architecture-checks");
                writer.WriteStartObject("defaultConfiguration");
                writer.WriteString("level", rule.Level);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndObject();

            writer.WriteStartArray("results");
            foreach (var check in checks)
            {
                writer.WriteStartObject();
                writer.WriteString("ruleId", check.Code);
                writer.WriteNumber("ruleIndex", ruleIndexes[check.Code]);
                writer.WriteString("level", check.Severity == CheckSeverity.Error ? "error" : "warning");
                writer.WriteStartObject("message");
                writer.WriteString("text", check.Message);
                writer.WriteEndObject();

                if (result.BaselineApplied)
                    writer.WriteString("baselineState", check.IsKnown ? "unchanged" : "new");

                writer.WriteStartObject("partialFingerprints");
                writer.WriteString("cqrslens/v1", Fingerprint(check.BaselineKey));
                writer.WriteEndObject();

                if (check.Location is not null)
                {
                    writer.WriteStartArray("locations");
                    writer.WriteStartObject();
                    writer.WriteStartObject("physicalLocation");
                    writer.WriteStartObject("artifactLocation");
                    writer.WriteString("uri", CiPath.Uri(result, check.Location, ciRoot));
                    writer.WriteEndObject();
                    writer.WriteStartObject("region");
                    writer.WriteNumber("startLine", check.Location.Line);
                    writer.WriteNumber("startColumn", check.Location.Column);
                    writer.WriteEndObject();
                    writer.WriteEndObject();
                    writer.WriteEndObject();
                    writer.WriteEndArray();
                }

                writer.WriteStartObject("properties");
                if (!string.IsNullOrEmpty(check.Target))
                    writer.WriteString("target", check.Target);
                if (!string.IsNullOrEmpty(check.ProjectName))
                    writer.WriteString("project", check.ProjectName);
                writer.WriteBoolean("known", check.IsKnown);
                writer.WriteEndObject();

                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            writer.WriteStartArray("invocations");
            writer.WriteStartObject();
            writer.WriteBoolean(
                "executionSuccessful",
                !result.Diagnostics.Any(d => d.Severity == AnalysisDiagnosticSeverity.Error));
            writer.WriteString("endTimeUtc", result.GeneratedAt.UtcDateTime.ToString("O"));
            writer.WriteStartArray("toolExecutionNotifications");
            foreach (var diagnostic in result.Diagnostics)
            {
                writer.WriteStartObject();
                writer.WriteString("level", diagnostic.Severity switch
                {
                    AnalysisDiagnosticSeverity.Error => "error",
                    AnalysisDiagnosticSeverity.Warning => "warning",
                    _ => "note",
                });
                writer.WriteStartObject("message");
                writer.WriteString("text", diagnostic.Message);
                writer.WriteEndObject();
                writer.WriteStartObject("descriptor");
                writer.WriteString("id", diagnostic.Code);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndArray();

            writer.WriteString("columnKind", "utf16CodeUnits");
            writer.WriteStartObject("properties");
            writer.WriteString("scanner", result.ScannerName);
            writer.WriteBoolean("baselineApplied", result.BaselineApplied);
            writer.WriteString("generatedAt", result.GeneratedAt.UtcDateTime.ToString("O"));
            writer.WriteNumber("projects", result.Projects.Count);
            writer.WriteNumber("requests", result.Requests.Count);
            writer.WriteNumber("handlers", result.Handlers.Count);
            writer.WriteNumber("validators", result.Validators.Count);
            writer.WriteEndObject();

            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    private static string Fingerprint(string key)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
}
