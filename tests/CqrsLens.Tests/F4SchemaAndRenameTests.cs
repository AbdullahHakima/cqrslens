using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// Stage F4 tests proving the public model schema (<c>noctua.model.schema.json</c>),
/// stability doc (<c>docs/model-schema.md</c>), config schema, rename, and compatibility guarantees.
/// </summary>
public sealed class F4SchemaAndRenameTests : IDisposable
{
    private readonly string _tempRoot;

    public F4SchemaAndRenameTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "cqrslens-f4-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { /* best effort */ }
    }

    [Theory]
    [InlineData("tests/fixtures/GoldenCorpus", AnalysisMode.Text, false)]
    [InlineData("tests/fixtures/GoldenCorpus", AnalysisMode.Roslyn, false)]
    [InlineData("tests/fixtures/GoldenCorpus", AnalysisMode.Text, true)]
    [InlineData("tests/fixtures/Adversarial", AnalysisMode.Text, false)]
    [InlineData("tests/fixtures/Adversarial", AnalysisMode.Roslyn, false)]
    [InlineData("tests/fixtures/DiffPair/base", AnalysisMode.Text, false)]
    [InlineData("tests/fixtures/DiffPair/base", AnalysisMode.Roslyn, false)]
    [InlineData("tests/fixtures/DiffPair/head", AnalysisMode.Text, false)]
    [InlineData("tests/fixtures/DiffPair/head", AnalysisMode.Roslyn, false)]
    [InlineData("tests/fixtures/StrictFailure", AnalysisMode.Text, false)]
    [InlineData("tests/fixtures/StrictFailure", AnalysisMode.Roslyn, false)]
    [InlineData("samples/SampleShop", AnalysisMode.Text, false)]
    [InlineData("samples/SampleShop", AnalysisMode.Roslyn, false)]
    public void R_SCHEMA_AllFixtures_BothModes_ValidateAgainstModelSchema(
        string relativeCorpusPath,
        AnalysisMode mode,
        bool withWatermark)
    {
        var root = FindRepositoryRoot();
        var corpusPath = Path.Combine(root, relativeCorpusPath.Replace('/', Path.DirectorySeparatorChar));
        var result = Analyzer.Analyze(
            corpusPath,
            mode,
            new AnalysisOptions
            {
                GeneratedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                Watermark = withWatermark ? "CONTRACT TEST" : null,
            });

        var json = JsonReportGenerator.Generate(result);
        using var schemaDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "noctua.model.schema.json")));
        using var instanceDoc = JsonDocument.Parse(json);

        var errors = JsonSchema202012Validator.Validate(instanceDoc.RootElement, schemaDoc.RootElement);
        Assert.Empty(errors);
    }

    [Fact]
    public void R_SCHEMA_SyntheticFullFeatureModel_ValidatesAgainstModelSchema_AndHasZeroUndeclaredPaths()
    {
        var root = FindRepositoryRoot();
        var result = BuildComprehensiveSampleResult();
        var json = JsonReportGenerator.Generate(result);

        using var schemaDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "noctua.model.schema.json")));
        using var instanceDoc = JsonDocument.Parse(json);

        var errors = JsonSchema202012Validator.Validate(instanceDoc.RootElement, schemaDoc.RootElement);
        Assert.Empty(errors);

        // Confirm every conditional section exercised in the comprehensive sample was emitted.
        foreach (var conditionalSection in new[]
                 {
                     "watermark",
                     "conventions",
                     "endpoints",
                     "flowEdges",
                     "resolvedEdges",
                     "serviceCallEdges",
                     "dispatchCallSites",
                     "unresolvedDispatches",
                     "relations",
                     "components",
                     "spineEndpoints",
                     "unproven",
                 })
        {
            Assert.True(
                instanceDoc.RootElement.TryGetProperty(conditionalSection, out _),
                $"Expected comprehensive sample to emit conditional section '{conditionalSection}'.");
        }
    }

    [Theory]
    [InlineData("missing_schema_version")]
    [InlineData("wrong_schema_version")]
    [InlineData("wrong_type_file_count")]
    [InlineData("unknown_top_level_property")]
    [InlineData("unknown_nested_summary_property")]
    [InlineData("unknown_nested_component_property")]
    [InlineData("invalid_scanner_enum")]
    [InlineData("invalid_relation_kind_enum")]
    [InlineData("empty_conditional_relations_array")]
    [InlineData("missing_summary_field")]
    public void R_SCHEMA_CorruptedSamples_AreRejected(string mutationCase)
    {
        var root = FindRepositoryRoot();
        var result = BuildComprehensiveSampleResult();
        var json = JsonReportGenerator.Generate(result);
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();

        switch (mutationCase)
        {
            case "missing_schema_version":
                node.Remove("schemaVersion");
                break;
            case "wrong_schema_version":
                node["schemaVersion"] = 2;
                break;
            case "wrong_type_file_count":
                node["fileCount"] = "12";
                break;
            case "unknown_top_level_property":
                node["unexpectedExtension"] = true;
                break;
            case "unknown_nested_summary_property":
                node["summary"]!.AsObject()["unexpectedCount"] = 1;
                break;
            case "unknown_nested_component_property":
                node["components"]![0]!.AsObject()["extraField"] = "nope";
                break;
            case "invalid_scanner_enum":
                node["scanner"] = "magic";
                break;
            case "invalid_relation_kind_enum":
                node["relations"]![0]!.AsObject()["kind"] = "inventedRelation";
                break;
            case "empty_conditional_relations_array":
                node["relations"] = new System.Text.Json.Nodes.JsonArray();
                break;
            case "missing_summary_field":
                node["summary"]!.AsObject().Remove("unresolvedDispatches");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutationCase), mutationCase, null);
        }

        using var schemaDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "noctua.model.schema.json")));
        using var mutatedDoc = JsonDocument.Parse(node.ToJsonString());

        var errors = JsonSchema202012Validator.Validate(mutatedDoc.RootElement, schemaDoc.RootElement);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void R_SCHEMA_StabilityDoc_MatchesModelSchemaFieldForField()
    {
        var root = FindRepositoryRoot();
        using var schemaDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "noctua.model.schema.json")));
        var docText = File.ReadAllText(Path.Combine(root, "docs", "model-schema.md"));

        var topProps = schemaDoc.RootElement.GetProperty("properties")
            .EnumerateObject()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        var topStart = docText.IndexOf("## Top-Level Model Document", StringComparison.Ordinal);
        var defsStart = docText.IndexOf("## Object Definitions", StringComparison.Ordinal);
        Assert.True(topStart >= 0 && defsStart > topStart);

        var topTableFields = ExtractMarkdownTableFields(docText[topStart..defsStart]);
        Assert.Equal(
            topProps.OrderBy(x => x, StringComparer.Ordinal),
            topTableFields.OrderBy(x => x, StringComparer.Ordinal));

        var defs = schemaDoc.RootElement.GetProperty("$defs");
        foreach (var defProp in defs.EnumerateObject())
        {
            var heading = $"### `{defProp.Name}`";
            var headingIdx = docText.IndexOf(heading, StringComparison.Ordinal);
            Assert.True(headingIdx >= 0, $"Missing heading {heading} in docs/model-schema.md");

            var bodyStart = headingIdx + heading.Length;
            var nextHeadingIdx = docText.IndexOf("\n### ", bodyStart, StringComparison.Ordinal);
            var section = nextHeadingIdx >= 0 ? docText[bodyStart..nextHeadingIdx] : docText[bodyStart..];

            if (defProp.Value.TryGetProperty("properties", out var defFields))
            {
                var expectedFields = defFields.EnumerateObject()
                    .Select(p => p.Name)
                    .OrderBy(x => x, StringComparer.Ordinal)
                    .ToList();
                var actualFields = ExtractMarkdownTableFields(section)
                    .OrderBy(x => x, StringComparer.Ordinal)
                    .ToList();
                Assert.Equal(expectedFields, actualFields);
            }
            else if (defProp.Value.TryGetProperty("enum", out var enumValues))
            {
                var expectedValues = enumValues.EnumerateArray()
                    .Select(e => e.GetString()!)
                    .ToList();
                var match = Regex.Match(section, @"Allowed values:\s*(.+)\.", RegexOptions.CultureInvariant);
                Assert.True(match.Success, $"Missing Allowed values line in {heading}");
                var actualValues = Regex.Matches(match.Groups[1].Value, @"`([^`]+)`")
                    .Select(m => m.Groups[1].Value)
                    .ToList();
                Assert.Equal(expectedValues, actualValues);
            }
        }
    }

    [Fact]
    public void R_SCHEMA_CONFIG_NoctuaSchema_CoversAllKnownRules_AndValidatesIntent()
    {
        var root = FindRepositoryRoot();
        using var schemaDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "noctua.schema.json")));
        var rootProps = schemaDoc.RootElement.GetProperty("properties");

        var ruleProps = rootProps.GetProperty("rules").GetProperty("properties")
            .EnumerateObject()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var knownRule in KnownRules.All)
            Assert.Contains(knownRule, ruleProps);

        Assert.Equal(KnownRules.All.Length, ruleProps.Count);

        var validFullConfig = new JsonObject
        {
            ["$schema"] = "https://raw.githubusercontent.com/AbdullahHakima/noctua/main/noctua.schema.json",
            ["analysis"] = "text",
            ["render"] = "svg",
            ["cqrsMapSplit"] = "feature",
            ["watermark"] = "NOCTUA CORP",
            ["markers"] = new JsonArray("ICommand", "IQuery"),
            ["handlerMarkers"] = new JsonArray("ICommandHandler"),
            ["include"] = new JsonArray("src/**"),
            ["exclude"] = new JsonArray("**/obj/**"),
            ["layers"] = new JsonObject { ["Sample.Domain"] = "Domain" },
            ["rules"] = new JsonObject
            {
                ["MissingHandler"] = "error",
                ["UnreachableRequest"] = "warning",
                ["UnpublishedNotification"] = "off",
                ["C-BOUNDARY"] = "error",
                ["C-SURFACE"] = "warning",
                ["C-INTENT-STALE"] = "warning",
            },
            ["intent"] = new JsonObject
            {
                ["modules"] = new JsonObject
                {
                    ["Ordering"] = new JsonObject
                    {
                        ["projects"] = new JsonArray("Sample.Ordering"),
                        ["components"] = new JsonArray("Sample.Ordering.CreateOrder"),
                    },
                    ["Billing"] = new JsonObject
                    {
                        ["components"] = new JsonArray("Sample.Billing"),
                    },
                },
                ["boundaries"] = new JsonArray
                {
                    new JsonObject { ["from"] = "Billing", ["to"] = "Ordering", ["severity"] = "error" },
                },
                ["publicSurface"] = new JsonObject
                {
                    ["endpoints"] = new JsonArray("POST /orders"),
                    ["components"] = new JsonArray(),
                    ["severity"] = "warning",
                },
                ["ownership"] = new JsonObject
                {
                    ["Ordering"] = "Team Checkout",
                },
            },
        };
        using (var doc = JsonDocument.Parse(validFullConfig.ToJsonString()))
            Assert.Empty(JsonSchema202012Validator.Validate(doc.RootElement, schemaDoc.RootElement));

        using (var nullIntent = JsonDocument.Parse("""{ "intent": null }"""))
            Assert.Empty(JsonSchema202012Validator.Validate(nullIntent.RootElement, schemaDoc.RootElement));

        foreach (var invalidJson in new[]
                 {
                     """{ "unknownTopKey": true }""",
                     """{ "rules": { "UnknownRule": "error" } }""",
                     """{ "intent": { "modules": { "Mod": {} } } }""",
                     """{ "intent": { "modules": { "   ": { "projects": ["P"] } } } }""",
                     """{ "intent": { "modules": { "Mod": { "components": ["Sample.*"] } } } }""",
                     """{ "intent": { "boundaries": [{ "from": "A", "to": "B", "severity": "off" }] } }""",
                     """{ "intent": { "publicSurface": {} } }""",
                     """{ "intent": { "ownership": { "Mod": "   " } } }""",
                 })
        {
            using var badDoc = JsonDocument.Parse(invalidJson);
            var errs = JsonSchema202012Validator.Validate(badDoc.RootElement, schemaDoc.RootElement);
            Assert.NotEmpty(errs);
        }
    }

    [Fact]
    public void R_RENAME_GeneratedOutputs_UseNoctuaAndPreserveCompat()
    {
        var root = FindRepositoryRoot();
        var sampleShop = Path.Combine(root, "samples", "SampleShop");
        var outDir = Path.Combine(_tempRoot, "rename-out");
        Directory.CreateDirectory(outDir);

        var mdPath = Path.Combine(outDir, "ARCHITECTURE.md");
        var htmlPath = Path.Combine(outDir, "ARCHITECTURE.html");
        var sarifPath = Path.Combine(outDir, "noctua.sarif");
        var summaryPath = Path.Combine(outDir, "summary.md");
        var agentPath = Path.Combine(outDir, "ARCHITECTURE.agent.md");
        var diffPath = Path.Combine(outDir, "diff.md");
        var docsDir = Path.Combine(outDir, "docs");

        var versionRes = RunCli(["--version"]);
        Assert.Equal(ExitCodes.Success, versionRes.Code);
        Assert.Equal("Noctua 2.0.0", versionRes.Stdout.Trim());

        var helpRes = RunCli(["--help", "--no-banner", "--no-color"]);
        Assert.Equal(ExitCodes.Success, helpRes.Code);
        Assert.Contains("Usage: noctua <path> [options]", helpRes.Stdout);
        Assert.Contains("Default: <path>/.noctua.json (or .cqrslens.json)", helpRes.Stdout);

        var mdRes = RunCli(
        [
            sampleShop, "-o", mdPath,
            "--generated-at", "2026-01-01T00:00:00Z",
            "--sarif", sarifPath,
            "--summary", summaryPath,
            "--diff", sampleShop,
            "--diff-output", diffPath,
            "--export-docs", docsDir,
            "--no-banner", "--no-color",
        ]);
        Assert.Equal(ExitCodes.Success, mdRes.Code);

        var htmlRes = RunCli([sampleShop, "-o", htmlPath, "--render", "svg", "--generated-at", "2026-01-01T00:00:00Z", "--no-banner", "--no-color"]);
        Assert.Equal(ExitCodes.Success, htmlRes.Code);

        var agentRes = RunCli([sampleShop, "--format", "agent", "-o", agentPath, "--generated-at", "2026-01-01T00:00:00Z", "--no-banner", "--no-color"]);
        Assert.Equal(ExitCodes.Success, agentRes.Code);

        var filesToInspect = new List<string> { mdPath, htmlPath, sarifPath, summaryPath, agentPath, diffPath };
        filesToInspect.AddRange(Directory.GetFiles(docsDir, "*.md", SearchOption.AllDirectories));

        foreach (var file in filesToInspect)
        {
            var text = File.ReadAllText(file);
            var scrubbed = text
                .Replace("<!-- cqrslens-architecture-diff -->", "", StringComparison.Ordinal)
                .Replace("\"cqrslens/v1\"", "", StringComparison.Ordinal)
                .Replace("cqrslens-diagram-", "", StringComparison.Ordinal)
                .Replace(".noctua.json` (or legacy `.cqrslens.json`)", "", StringComparison.Ordinal);

            Assert.DoesNotContain("cqrslens", scrubbed, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void R_COMPAT_ConfigDiscovery_PrecedenceAndFallbackWithIntent()
    {
        var root = FindRepositoryRoot();
        var srcDir = Path.Combine(root, "tests", "fixtures", "GoldenCorpus");

        // Case A: legacy .cqrslens.json fallback with intent works end-to-end.
        var legacyWork = Path.Combine(_tempRoot, "cfg-legacy");
        CopyDirectory(srcDir, legacyWork);
        File.WriteAllText(
            Path.Combine(legacyWork, ".cqrslens.json"),
            """
            {
              "watermark": "LEGACY CONFIG",
              "intent": {
                "modules": {
                  "MissingMod": { "projects": ["NoSuch.Project"] }
                }
              }
            }
            """);

        var legacyJsonOut = Path.Combine(_tempRoot, "legacy.json");
        var legacyRes = RunCli([legacyWork, "-o", legacyJsonOut, "--generated-at", "2026-01-01T00:00:00Z", "--no-banner", "--no-color"]);
        Assert.Equal(ExitCodes.Success, legacyRes.Code);
        Assert.DoesNotContain("takes precedence over", legacyRes.Stdout);
        var legacyJsonText = File.ReadAllText(legacyJsonOut);
        Assert.Contains("\"watermark\": \"LEGACY CONFIG\"", legacyJsonText);
        Assert.Contains("\"C-INTENT-STALE\"", legacyJsonText);

        // Case B: both .noctua.json and .cqrslens.json present -> .noctua.json wins + stdout notice, JSON clean.
        var bothWork = Path.Combine(_tempRoot, "cfg-both");
        CopyDirectory(srcDir, bothWork);
        File.WriteAllText(
            Path.Combine(bothWork, ".noctua.json"),
            """{ "watermark": "NOCTUA PRIMARY", "intent": { "modules": { "PrimaryMod": { "projects": ["NoSuch.Project"] } } } }""");
        File.WriteAllText(
            Path.Combine(bothWork, ".cqrslens.json"),
            """{ "watermark": "LEGACY IGNORED" }""");

        var bothJsonOut = Path.Combine(_tempRoot, "both.json");
        var bothRes = RunCli([bothWork, "-o", bothJsonOut, "--generated-at", "2026-01-01T00:00:00Z", "--no-banner", "--no-color"]);
        Assert.Equal(ExitCodes.Success, bothRes.Code);
        Assert.Contains(".noctua.json takes precedence over .cqrslens.json", bothRes.Stdout);
        var bothJsonText = File.ReadAllText(bothJsonOut);
        Assert.Contains("\"watermark\": \"NOCTUA PRIMARY\"", bothJsonText);
        Assert.Contains("PrimaryMod:NoSuch.Project", bothJsonText);
        Assert.DoesNotContain("LEGACY IGNORED", bothJsonText);

        // Null intent behaves identically to omitted config (no-intent parity).
        var parityWork = Path.Combine(_tempRoot, "cfg-parity");
        CopyDirectory(srcDir, parityWork);

        var noCfgOut = Path.Combine(_tempRoot, "none.json");
        var nullIntentOut = Path.Combine(_tempRoot, "null-intent.json");
        Assert.Equal(ExitCodes.Success, RunCli([parityWork, "-o", noCfgOut, "--generated-at", "2026-01-01T00:00:00Z", "--no-banner", "--no-color"]).Code);
        File.WriteAllText(Path.Combine(parityWork, ".noctua.json"), """{ "intent": null }""");
        Assert.Equal(ExitCodes.Success, RunCli([parityWork, "-o", nullIntentOut, "--generated-at", "2026-01-01T00:00:00Z", "--no-banner", "--no-color"]).Code);
        Assert.Equal(File.ReadAllText(noCfgOut), File.ReadAllText(nullIntentOut));
    }

    [Fact]
    public void R_COMPAT_Legacy110Baseline_F3DiffJson_AndMcpContract()
    {
        var root = FindRepositoryRoot();
        var strictCorpus = Path.Combine(root, "tests", "fixtures", "StrictFailure");

        // 1. Legacy 1.1.0-era baseline file suppresses pre-existing error under --strict.
        var withoutBaseline = RunCli([strictCorpus, "--strict", "-o", Path.Combine(_tempRoot, "strict-fail.md"), "--no-banner", "--no-color"]);
        Assert.Equal(ExitCodes.StrictFailure, withoutBaseline.Code);

        var strictResult = Analyzer.Analyze(strictCorpus, AnalysisMode.Text);
        var errFinding = Assert.Single(strictResult.Checks, c => c.Severity == CheckSeverity.Error);
        var legacyBaselinePath = Path.Combine(_tempRoot, "v110-baseline.json");
        File.WriteAllText(
            legacyBaselinePath,
            JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                findings = new[]
                {
                    new
                    {
                        code = errFinding.Code,
                        target = errFinding.Target,
                        file = errFinding.Location!.File,
                        project = errFinding.ProjectName,
                        severity = "error",
                        message = errFinding.Message,
                        line = errFinding.Location.Line,
                        column = errFinding.Location.Column,
                    },
                },
            }));

        var withBaseline = RunCli([strictCorpus, "--baseline", legacyBaselinePath, "--strict", "-o", Path.Combine(_tempRoot, "strict-ok.md"), "--no-banner", "--no-color"]);
        Assert.Equal(ExitCodes.Success, withBaseline.Code);
        Assert.Contains("Baseline:", withBaseline.Stdout);

        // 2. F3-era 1.1.0 JSON report is accepted as --diff input cleanly.
        var baseDir = Path.Combine(root, "tests", "fixtures", "DiffPair", "base");
        var headDir = Path.Combine(root, "tests", "fixtures", "DiffPair", "head");
        var baseJson = JsonReportGenerator.Generate(
            Analyzer.Analyze(baseDir, AnalysisMode.Text, new AnalysisOptions { GeneratedAt = DateTimeOffset.Parse("2026-11-13T00:00:00Z") }));
        var f3BaseJsonPath = Path.Combine(_tempRoot, "f3-base.json");
        File.WriteAllText(f3BaseJsonPath, baseJson.Replace($"\"toolVersion\": \"{ToolInfo.Version}\"", "\"toolVersion\": \"1.1.0\"", StringComparison.Ordinal));

        var diffOutMd = Path.Combine(_tempRoot, "compat-diff.md");
        var diffOutJson = Path.Combine(_tempRoot, "compat-diff.json");
        Assert.Equal(ExitCodes.Success, RunCli([headDir, "--diff", f3BaseJsonPath, "--diff-output", diffOutMd, "-o", Path.Combine(_tempRoot, "head.md"), "--no-banner", "--no-color"]).Code);
        Assert.Equal(ExitCodes.Success, RunCli([headDir, "--diff", f3BaseJsonPath, "--diff-output", diffOutJson, "-o", Path.Combine(_tempRoot, "head2.md"), "--no-banner", "--no-color"]).Code);
        Assert.Contains("## Noctua Architecture Diff", File.ReadAllText(diffOutMd));
        Assert.Contains("\"schemaVersion\": 1", File.ReadAllText(diffOutJson));

        // 3. MCP tool names unchanged while serverInfo reports noctua 2.0.0.
        var sampleShop = Path.Combine(root, "samples", "SampleShop");
        var reqLines = string.Join("\n", new[]
        {
            new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = "initialize", ["params"] = new JsonObject { ["protocolVersion"] = "2024-11-05", ["capabilities"] = new JsonObject() } }.ToJsonString(),
            new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "notifications/initialized" }.ToJsonString(),
            new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 2, ["method"] = "tools/list" }.ToJsonString(),
            new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 3, ["method"] = "tools/call", ["params"] = new JsonObject { ["name"] = "get_architecture_dna", ["arguments"] = new JsonObject() } }.ToJsonString(),
        }) + "\n";

        using var input = new StringReader(reqLines);
        using var output = new StringWriter();
        using var err = new StringWriter();
        var mcpExit = McpServer.Run(new CliOptions { Path = sampleShop, Mcp = true }, input, output, err);
        Assert.Equal(ExitCodes.Success, mcpExit);

        var respLines = output.ToString().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, respLines.Length);
        var init = JsonNode.Parse(respLines[0])!;
        Assert.Equal("noctua", init["result"]!["serverInfo"]!["name"]!.GetValue<string>());
        Assert.Equal(ToolInfo.Version, init["result"]!["serverInfo"]!["version"]!.GetValue<string>());

        var tools = JsonNode.Parse(respLines[1])!["result"]!["tools"]!.AsArray()
            .Select(t => t!["name"]!.GetValue<string>())
            .ToArray();
        Assert.Equal(
            ["get_architecture_dna", "trace_slice", "trace_entity_impact", "check_architecture", "trace_request_impact", "verify_claim", "diff_architecture"],
            tools);
        Assert.Contains("SampleShop", JsonNode.Parse(respLines[2])!.ToJsonString());
    }

    private static (int Code, string Stdout, string Stderr) RunCli(string[] args)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var code = CliRunner.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);
        foreach (var file in Directory.GetFiles(sourceDir))
            File.Copy(file, Path.Combine(destinationDir, Path.GetFileName(file)), overwrite: true);
        foreach (var subDir in Directory.GetDirectories(sourceDir))
            CopyDirectory(subDir, Path.Combine(destinationDir, Path.GetFileName(subDir)));
    }

    private static List<string> ExtractMarkdownTableFields(string markdownSection)
    {
        var fields = new List<string>();
        foreach (var line in markdownSection.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var match = Regex.Match(line, @"^\|\s*`([^`]+)`\s*\|", RegexOptions.CultureInvariant);
            if (match.Success)
                fields.Add(match.Groups[1].Value);
        }
        return fields;
    }

    private static AnalysisResult BuildComprehensiveSampleResult()
    {
        var apiProjectPath = "src/Sample.Api/Sample.Api.csproj";
        var appProjectPath = "src/Sample.App/Sample.App.csproj";
        var infraProjectPath = "src/Sample.Infra/Sample.Infra.csproj";

        var apiProjId = ProjectId.FromRelativePath(apiProjectPath);
        var appProjId = ProjectId.FromRelativePath(appProjectPath);
        var infraProjId = ProjectId.FromRelativePath(infraProjectPath);

        var result = new AnalysisResult
        {
            RootPath = "/tmp/nop-shaped-sample",
            ScannerName = "text",
            GeneratedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            ElapsedMilliseconds = 0,
            DiscoveredFileCount = 24,
            FileCount = 22,
            Watermark = "SCHEMA SAMPLE",
            BaselineApplied = true,
            FlowAnalyzed = true,
        };
        result.Conventions.Add("Mediator");
        result.SkippedFiles.Add("src/Sample.App/Broken.cs");

        result.Projects.Add(new ProjectInfo
        {
            Name = "Sample.Api",
            FilePath = apiProjectPath,
            DirectoryPath = "src/Sample.Api",
            Layer = "Api",
            ReferenceNames = { "Sample.App" },
            PackageReferences = { "Microsoft.AspNetCore.App" },
        });
        result.Projects.Add(new ProjectInfo
        {
            Name = "Sample.App",
            FilePath = appProjectPath,
            DirectoryPath = "src/Sample.App",
            Layer = "Application",
            PackageReferences = { "MediatR" },
        });
        result.Projects.Add(new ProjectInfo
        {
            Name = "Sample.Infra",
            FilePath = infraProjectPath,
            DirectoryPath = "src/Sample.Infra",
            Layer = "Infrastructure",
            ReferenceNames = { "Sample.App" },
            PackageReferences = { "linq2db" },
        });

        var loc = new SourceLocation("src/Sample.App/CreateOrder.cs", 10, 5);
        var request = new CqrsRequest
        {
            Name = "CreateOrderCommand",
            Namespace = "Sample.App",
            Kind = "Command",
            ResponseType = "int",
            ViaMarker = "ICommand",
            ProjectName = "Sample.App",
            Location = loc,
        };
        request.Properties.Add(new RequestPropertyInfo("CustomerId", "Guid"));
        request.Handlers.Add("Sample.App.CreateOrderHandler");
        request.Validators.Add("Sample.App.CreateOrderValidator");
        request.Entrypoints.Add("POST /orders");
        request.TouchedEntities.Add("Orders (Order)");
        request.PublishedNotifications.Add("Sample.App.OrderCreatedNotification");
        request.DispatchedRequests.Add("Sample.App.AuditCommand");
        result.Requests.Add(request);

        var handler = new HandlerInfo
        {
            Name = "CreateOrderHandler",
            Namespace = "Sample.App",
            RequestName = "Sample.App.CreateOrderCommand",
            ProjectName = "Sample.App",
            Location = new SourceLocation("src/Sample.App/CreateOrderHandler.cs", 12, 5),
        };
        handler.Dependencies.Add("IOrderService");
        handler.TouchedEntities.Add("Orders (Order)");
        handler.PublishedNotifications.Add("Sample.App.OrderCreatedNotification");
        handler.DispatchedRequests.Add("Sample.App.AuditCommand");
        result.Handlers.Add(handler);

        result.Validators.Add(new ValidatorInfo
        {
            Name = "CreateOrderValidator",
            Namespace = "Sample.App",
            TargetType = "Sample.App.CreateOrderCommand",
            ProjectName = "Sample.App",
            Location = new SourceLocation("src/Sample.App/CreateOrderValidator.cs", 8, 5),
        });

        result.Behaviors.Add(new BehaviorInfo
        {
            Name = "LoggingBehavior",
            ProjectName = "Sample.App",
            Location = new SourceLocation("src/Sample.App/LoggingBehavior.cs", 6, 5),
        });

        var dbContext = new DbContextInfo
        {
            Name = "AppDbContext",
            Namespace = "Sample.Infra",
            ProjectName = "Sample.Infra",
            Location = new SourceLocation("src/Sample.Infra/AppDbContext.cs", 7, 5),
        };
        dbContext.DbSets.Add(new DbSetInfo("Order", "Orders") { EntityFullName = "Sample.App.Order" });
        var entity = new EntityInfo
        {
            Name = "Order",
            Namespace = "Sample.App",
            ProjectName = "Sample.App",
            Location = new SourceLocation("src/Sample.App/Order.cs", 5, 5),
        };
        entity.Properties.Add(new EntityPropertyInfo
        {
            Name = "Id",
            Type = "Guid",
            IsKey = true,
            IsForeignKey = false,
            IsNavigation = false,
            IsCollection = false,
            TargetEntity = null,
            IsRequired = true,
            Location = new SourceLocation("src/Sample.App/Order.cs", 7, 9),
        });
        dbContext.Entities.Add(entity);
        dbContext.Relationships.Add(new EntityRelationshipInfo(
            "Customer", "Order", "Orders", "Customer", "CustomerId", true, EntityRelationshipKind.OneToMany, "HasOne/WithMany"));
        result.DbContexts.Add(dbContext);

        result.Notifications.Add(new CqrsNotification
        {
            Name = "OrderCreatedNotification",
            Namespace = "Sample.App",
            ViaMarker = "IDomainEvent",
            ProjectName = "Sample.App",
            Handlers = { "Sample.App.OrderCreatedHandler" },
            Location = new SourceLocation("src/Sample.App/OrderCreatedNotification.cs", 4, 5),
        });
        var notifHandler = new NotificationHandlerInfo
        {
            Name = "OrderCreatedHandler",
            Namespace = "Sample.App",
            NotificationName = "Sample.App.OrderCreatedNotification",
            ProjectName = "Sample.App",
            Location = new SourceLocation("src/Sample.App/OrderCreatedHandler.cs", 6, 5),
        };
        notifHandler.Dependencies.Add("IEmailSender");
        notifHandler.TouchedEntities.Add("Orders (Order)");
        notifHandler.PublishedNotifications.Add("Sample.App.AuditNotification");
        notifHandler.DispatchedRequests.Add("Sample.App.AuditCommand");
        result.NotificationHandlers.Add(notifHandler);

        result.Endpoints.Add(new EndpointInfo
        {
            Name = "OrdersController.Create",
            HttpMethod = "POST",
            Route = "/orders",
            TargetRequestName = "CreateOrderCommand",
            TargetRequestFullName = "Sample.App.CreateOrderCommand",
            IsInferred = false,
            ProjectName = "Sample.Api",
            Location = new SourceLocation("src/Sample.Api/OrdersController.cs", 15, 9),
        });

        result.Checks.Add(new CheckFinding(
            CheckSeverity.Warning,
            ArchitectureChecker.CommandWithoutValidatorCode,
            "Sample warning",
            "Sample.App.CreateOrderCommand",
            loc,
            "Sample.App",
            IsKnown: true));
        result.RecordDiagnostic(new AnalysisDiagnostic(
            AnalysisResult.UnresolvedContractCode,
            AnalysisDiagnosticSeverity.Info,
            "Sample unresolved contract",
            loc,
            "Sample.App"));

        // Spine components (including multi-role service+repository and controller)
        var orderServiceSymbol = SymbolId.Create(appProjId, "Sample.App", null, "OrderService");
        var ordersControllerSymbol = SymbolId.Create(apiProjId, "Sample.Api", null, "OrdersController");
        var serviceId = new ComponentId(orderServiceSymbol);
        var controllerId = new ComponentId(ordersControllerSymbol);
        var serviceLoc = new SourceLocation("src/Sample.App/OrderService.cs", 5, 5);
        var controllerLoc = new SourceLocation("src/Sample.Api/OrdersController.cs", 8, 5);

        result.Spine.AddOrMergeComponent(
            serviceId,
            serviceLoc,
            ComponentRole.Service,
            new EvidenceOccurrence(EvidenceRule.ServiceDiscovery, EvidenceOrigin.Declaration, serviceLoc, EvidenceStrength.Proven,
                scopeProjectName: "Sample.App"));
        result.Spine.AddOrMergeComponent(
            serviceId,
            serviceLoc,
            ComponentRole.Repository,
            new EvidenceOccurrence(EvidenceRule.RepositoryDiscovery, EvidenceOrigin.Declaration, serviceLoc, EvidenceStrength.Proven,
                scopeProjectName: "Sample.App"));
        result.Spine.AddOrMergeComponent(
            controllerId,
            controllerLoc,
            ComponentRole.Controller,
            new EvidenceOccurrence(EvidenceRule.ControllerDiscovery, EvidenceOrigin.Declaration, controllerLoc, EvidenceStrength.Proven,
                scopeProjectName: "Sample.Api"));

        var endpointLoc = new SourceLocation("src/Sample.Api/OrdersController.cs", 15, 9);
        var epId = EndpointId.Create(apiProjId, endpointLoc, 0);
        result.Spine.AddEndpoint(new EndpointNode(
            epId,
            "OrdersController.Create",
            "POST",
            "/orders",
            EvidenceStrength.Proven,
            EvidenceStrength.Inferred,
            new EvidenceOccurrence(EvidenceRule.EndpointMapping, EvidenceOrigin.MappingOrAttribute, endpointLoc, EvidenceStrength.Proven,
                scopeProjectName: "Sample.Api")));

        // Spine relations (references without location, resolvesTo with semantics, calls with via+method, exposes, usesDataResolved, dispatches)
        result.Spine.AddOrMergeRelation(
            RelationKind.References,
            NodeReference.ForProject(apiProjId),
            NodeReference.ForProject(appProjId),
            new EvidenceOccurrence(EvidenceRule.ProjectReference, EvidenceOrigin.ProjectFile, null, EvidenceStrength.Proven,
                locationOmittedReason: "ProjectReference in .csproj", scopeProjectName: "Sample.Api"));
        var orderServiceInterface = SymbolId.Create(appProjId, "Sample.App", null, "IOrderService");
        result.Spine.AddOrMergeRelation(
            RelationKind.ResolvesTo,
            NodeReference.ForInterface(orderServiceInterface),
            NodeReference.ForComponent(serviceId),
            new EvidenceOccurrence(EvidenceRule.ServiceConfirmedBinding, EvidenceOrigin.Registration,
                new SourceLocation("src/Sample.Api/Program.cs", 12, 9),
                EvidenceStrength.Resolved,
                resolutionSemantics: ResolutionSemantics.Confirmed,
                scopeProjectName: "Sample.Api"));
        var viaRef = NodeReference.ForInterface(orderServiceInterface);
        result.Spine.AddOrMergeRelation(
            RelationKind.Calls,
            NodeReference.ForComponent(controllerId),
            NodeReference.ForComponent(serviceId),
            new EvidenceOccurrence(EvidenceRule.InjectedMemberInvocation, EvidenceOrigin.CallSite,
                new SourceLocation("src/Sample.Api/OrdersController.cs", 18, 13),
                EvidenceStrength.Resolved,
                via: new[] { viaRef },
                scopeProjectName: "Sample.Api",
                isTestProject: true,
                detail: "CreateAsync"),
            method: "CreateAsync");
        result.Spine.AddOrMergeRelation(
            RelationKind.Exposes,
            NodeReference.ForComponent(controllerId),
            NodeReference.ForEndpoint(epId),
            new EvidenceOccurrence(EvidenceRule.ControllerEndpointExposure, EvidenceOrigin.MappingOrAttribute, endpointLoc, EvidenceStrength.Proven,
                scopeProjectName: "Sample.Api"));

        var handlerId = new ComponentId(SymbolId.Create(appProjId, "Sample.App", null, "CreateOrderHandler"));
        var dbContextId = new ComponentId(SymbolId.Create(infraProjId, "Sample.Infra", null, "AppDbContext"));
        var dbContextInterface = SymbolId.Create(appProjId, "Sample.App", null, "IAppDbContext");
        var commandId = new ComponentId(SymbolId.Create(appProjId, "Sample.App", null, "CreateOrderCommand"));

        result.Spine.AddOrMergeRelation(
            RelationKind.UsesDataResolved,
            NodeReference.ForComponent(handlerId),
            NodeReference.ForComponent(dbContextId),
            new EvidenceOccurrence(EvidenceRule.ResolvedDbContextDependency, EvidenceOrigin.Registration,
                new SourceLocation("src/Sample.App/CreateOrderHandler.cs", 14, 9),
                EvidenceStrength.Resolved,
                via: new[] { NodeReference.ForInterface(dbContextInterface) },
                scopeProjectName: "Sample.App"));
        result.Spine.AddOrMergeRelation(
            RelationKind.Dispatches,
            NodeReference.ForEndpoint(epId),
            NodeReference.ForComponent(commandId),
            new EvidenceOccurrence(EvidenceRule.DispatchCallSite, EvidenceOrigin.CallSite,
                new SourceLocation("src/Sample.Api/OrdersController.cs", 17, 13),
                EvidenceStrength.Proven,
                scopeProjectName: "Sample.Api"));
        result.Spine.AddOrMergeRelation(
            RelationKind.Serves,
            NodeReference.ForComponent(commandId),
            NodeReference.ForComponent(handlerId),
            new EvidenceOccurrence(EvidenceRule.HandlerMarker, EvidenceOrigin.Declaration,
                new SourceLocation("src/Sample.App/CreateOrderHandler.cs", 12, 5),
                EvidenceStrength.Proven,
                scopeProjectName: "Sample.App"));

        // All unproven buckets
        result.Spine.Unproven.AddUnresolvedDispatch(new UnresolvedDispatch
        {
            Method = "Send",
            ReceiverName = "_mediator",
            Location = new SourceLocation("src/Sample.Api/OrdersController.cs", 25, 13),
            ProjectName = "Sample.Api",
            IsTestProject = true,
        });
        result.Spine.Unproven.AddAmbiguousResolution(new AmbiguousResolutionEntry(
            "Sample.App.INotifier",
            "INotifier",
            new[]
            {
                new ServiceCandidate
                {
                    Type = "Sample.App.EmailNotifier",
                    DeclaredInScan = true,
                    FromRegistration = true,
                    Location = new SourceLocation("src/Sample.App/EmailNotifier.cs", 3, 5),
                    RegistrationLocation = new SourceLocation("src/Sample.Api/Program.cs", 20, 9),
                },
                new ServiceCandidate
                {
                    Type = "Sample.App.SmsNotifier",
                    DeclaredInScan = true,
                    FromRegistration = false,
                    Location = new SourceLocation("src/Sample.App/SmsNotifier.cs", 3, 5),
                },
            },
            new SourceLocation("src/Sample.App/INotifier.cs", 3, 5)));
        result.Spine.Unproven.AddAmbiguousLink(new UnprovenDiagnosticEntry(
            UnprovenItemKind.AmbiguousLink, "AmbiguousLink", "Sample.App", "Ambiguous link", loc, "Sample.App"));
        result.Spine.Unproven.AddUnestablishedBehavior(new UnestablishedBehaviorEntry(
            new ComponentId(SymbolId.Create(appProjId, "Sample.App", null, "LoggingBehavior")),
            "LoggingBehavior",
            UnestablishedBehaviorReason.AssemblyScanningOrExternal,
            "Program.cs:10",
            "Sample.App",
            new SourceLocation("src/Sample.App/LoggingBehavior.cs", 6, 5)));
        result.Spine.Unproven.AddObservation(new UnprovenDiagnosticEntry(
            UnprovenItemKind.ServiceLocator, "ServiceLocator", "Sample.Api.OrdersController", "GetService call", loc, "Sample.Api"));

        return result;
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CqrsLens.sln")))
                return directory.FullName;
        }

        throw new InvalidOperationException("Could not locate CqrsLens.sln above test output directory.");
    }

    internal static class JsonSchema202012Validator
    {
        public static List<string> Validate(JsonElement instance, JsonElement rootSchema)
            => ValidateNode(instance, rootSchema, rootSchema, "$");

        private static List<string> ValidateNode(
            JsonElement instance,
            JsonElement schema,
            JsonElement rootSchema,
            string path)
        {
            var errors = new List<string>();
            if (schema.TryGetProperty("$ref", out var refProp))
            {
                schema = ResolveRef(rootSchema, refProp.GetString()!);
            }

            if (schema.TryGetProperty("allOf", out var allOf))
            {
                foreach (var sub in allOf.EnumerateArray())
                    errors.AddRange(ValidateNode(instance, sub, rootSchema, path));
            }

            if (schema.TryGetProperty("anyOf", out var anyOf))
            {
                var matchedAny = false;
                foreach (var sub in anyOf.EnumerateArray())
                {
                    if (ValidateNode(instance, sub, rootSchema, path).Count == 0)
                    {
                        matchedAny = true;
                        break;
                    }
                }
                if (!matchedAny)
                    errors.Add($"{path}: failed all branches of anyOf");
            }

            if (schema.TryGetProperty("not", out var notSchema))
            {
                if (ValidateNode(instance, notSchema, rootSchema, path).Count == 0)
                    errors.Add($"{path}: matched forbidden 'not' subschema");
            }

            if (schema.TryGetProperty("type", out var typeProp))
            {
                var expectedType = typeProp.GetString();
                if (!MatchesType(instance, expectedType))
                {
                    errors.Add($"{path}: expected {expectedType}, got {instance.ValueKind}");
                    return errors;
                }
            }

            if (schema.TryGetProperty("const", out var constProp))
            {
                if (!JsonElementEquals(instance, constProp))
                    errors.Add($"{path}: value does not equal const {constProp}");
            }

            if (schema.TryGetProperty("enum", out var enumProp))
            {
                var found = enumProp.EnumerateArray().Any(e => JsonElementEquals(instance, e));
                if (!found)
                    errors.Add($"{path}: value '{instance}' not in enum");
            }

            if (instance.ValueKind == JsonValueKind.String)
            {
                var str = instance.GetString()!;
                if (schema.TryGetProperty("minLength", out var minLength) && str.Length < minLength.GetInt32())
                    errors.Add($"{path}: string length {str.Length} < minLength {minLength.GetInt32()}");
                if (schema.TryGetProperty("pattern", out var pattern) &&
                    !Regex.IsMatch(str, pattern.GetString()!, RegexOptions.CultureInvariant))
                {
                    errors.Add($"{path}: string '{str}' does not match pattern '{pattern.GetString()}'");
                }
            }
            else if (instance.ValueKind == JsonValueKind.Number)
            {
                if (schema.TryGetProperty("minimum", out var minimum) && instance.GetDouble() < minimum.GetDouble())
                    errors.Add($"{path}: value {instance} < minimum {minimum}");
            }
            else if (instance.ValueKind == JsonValueKind.Array)
            {
                var count = instance.GetArrayLength();
                if (schema.TryGetProperty("minItems", out var minItems) && count < minItems.GetInt32())
                    errors.Add($"{path}: array count {count} < minItems {minItems.GetInt32()}");

                if (schema.TryGetProperty("items", out var itemSchema))
                {
                    var idx = 0;
                    foreach (var item in instance.EnumerateArray())
                    {
                        errors.AddRange(ValidateNode(item, itemSchema, rootSchema, $"{path}[{idx}]"));
                        idx++;
                    }
                }
            }
            else if (instance.ValueKind == JsonValueKind.Object)
            {
                if (schema.TryGetProperty("required", out var required))
                {
                    foreach (var req in required.EnumerateArray())
                    {
                        var reqName = req.GetString()!;
                        if (!instance.TryGetProperty(reqName, out _))
                            errors.Add($"{path}: missing required property '{reqName}'");
                    }
                }

                if (schema.TryGetProperty("minProperties", out var minProps))
                {
                    var count = instance.EnumerateObject().Count();
                    if (count < minProps.GetInt32())
                        errors.Add($"{path}: property count {count} < minProperties {minProps.GetInt32()}");
                }

                if (schema.TryGetProperty("propertyNames", out var propNameSchema))
                {
                    foreach (var prop in instance.EnumerateObject())
                    {
                        using var nameDoc = JsonDocument.Parse(JsonSerializer.Serialize(prop.Name));
                        errors.AddRange(ValidateNode(nameDoc.RootElement, propNameSchema, rootSchema, $"{path}.$key({prop.Name})"));
                    }
                }

                var hasProps = schema.TryGetProperty("properties", out var propsObj);
                var hasAdditional = schema.TryGetProperty("additionalProperties", out var additionalProp);

                foreach (var prop in instance.EnumerateObject())
                {
                    if (hasProps && propsObj.TryGetProperty(prop.Name, out var propSchema))
                    {
                        errors.AddRange(ValidateNode(prop.Value, propSchema, rootSchema, $"{path}.{prop.Name}"));
                    }
                    else if (hasAdditional)
                    {
                        if (additionalProp.ValueKind == JsonValueKind.False)
                            errors.Add($"{path}: additional property '{prop.Name}' is forbidden");
                        else if (additionalProp.ValueKind == JsonValueKind.Object)
                            errors.AddRange(ValidateNode(prop.Value, additionalProp, rootSchema, $"{path}.{prop.Name}"));
                    }
                }
            }

            return errors;
        }

        private static JsonElement ResolveRef(JsonElement rootSchema, string refValue)
        {
            const string prefix = "#/$defs/";
            if (!refValue.StartsWith(prefix, StringComparison.Ordinal))
                throw new InvalidOperationException($"Unsupported $ref '{refValue}'");

            var defName = refValue[prefix.Length..];
            return rootSchema.GetProperty("$defs").GetProperty(defName);
        }

        private static bool MatchesType(JsonElement instance, string? expected) => expected switch
        {
            "object" => instance.ValueKind == JsonValueKind.Object,
            "array" => instance.ValueKind == JsonValueKind.Array,
            "string" => instance.ValueKind == JsonValueKind.String,
            "integer" => instance.ValueKind == JsonValueKind.Number && instance.TryGetInt64(out _),
            "number" => instance.ValueKind == JsonValueKind.Number,
            "boolean" => instance.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "null" => instance.ValueKind == JsonValueKind.Null,
            _ => true,
        };

        private static bool JsonElementEquals(JsonElement a, JsonElement b)
        {
            if (a.ValueKind != b.ValueKind)
            {
                if ((a.ValueKind == JsonValueKind.True || a.ValueKind == JsonValueKind.False) &&
                    (b.ValueKind == JsonValueKind.True || b.ValueKind == JsonValueKind.False))
                    return a.GetBoolean() == b.GetBoolean();
                return false;
            }

            return a.ValueKind switch
            {
                JsonValueKind.String => string.Equals(a.GetString(), b.GetString(), StringComparison.Ordinal),
                JsonValueKind.Number => a.TryGetInt64(out var la) && b.TryGetInt64(out var lb)
                    ? la == lb
                    : Math.Abs(a.GetDouble() - b.GetDouble()) < 1e-12,
                JsonValueKind.True or JsonValueKind.False => a.GetBoolean() == b.GetBoolean(),
                JsonValueKind.Null => true,
                _ => string.Equals(a.GetRawText(), b.GetRawText(), StringComparison.Ordinal),
            };
        }
    }
}
