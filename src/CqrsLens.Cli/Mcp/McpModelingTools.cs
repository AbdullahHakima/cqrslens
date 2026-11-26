using System.Text.Json.Nodes;

namespace CqrsLens;

internal static class McpModelingTools
{
    public const string EvaluateArchitectureProposal = "evaluate_architecture_proposal";
    public const string PlanSliceTests = "plan_slice_tests";
    public const string VerifySliceDesign = "verify_slice_design";
    public const string DraftSliceDesign = "draft_slice_design";
    public const string AnalyzeChangeImpact = "analyze_change_impact";
    public const string VerifyTddCycle = "verify_tdd_cycle";

    private static readonly HashSet<string> ValidImpactKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "project",
        "entity",
        "dbcontext",
        "datastore",
        "service",
        "notification",
        "handler",
        "request",
        "entrypoint",
        "event",
    };

    public static JsonObject ArchitectureProposalSchema() => new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["properties"] = new JsonObject
        {
            ["projects"] = ArrayOfObjects(new JsonObject
            {
                ["name"] = StringProperty("Name of a proposed project."),
                ["layer"] = StringProperty("Layer of the proposed project, such as Application or Domain."),
            }, new JsonArray("name", "layer"), allowExtraProperties: false),
            ["references"] = ArrayOfObjects(new JsonObject
            {
                ["from"] = StringProperty("Name of the project declaring the reference, or the root-relative .csproj path for an existing project."),
                ["to"] = StringProperty("Name of the project being referenced, or the root-relative .csproj path for an existing project."),
                ["operation"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray("add", "remove"),
                    ["default"] = "add",
                },
            }, new JsonArray("from", "to"), allowExtraProperties: false),
            ["path"] = McpToolCatalog.PathProperty(),
            ["analysis"] = McpToolCatalog.AnalysisProperty(),
        },
        ["anyOf"] = new JsonArray(
            NonEmptyArray("projects"),
            NonEmptyArray("references")),
    };

    public static JsonObject SliceTestPlanSchema()
    {
        var scenarios = ArrayOfObjects(new JsonObject
        {
            ["name"] = StringProperty("A distinct name for the business scenario."),
            ["given"] = StringProperty("The starting state and relevant conditions."),
            ["when"] = StringProperty("The action that invokes the slice."),
            ["then"] = StringProperty("The observable result or invariant the test must assert."),
        }, new JsonArray("name", "given", "when", "then"));
        scenarios["minItems"] = 1;
        scenarios["maxItems"] = AgentSliceTestPlanGenerator.MaxScenarios;

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["request"] = StringProperty("An exact existing or proposed target name: CQRS request, Minimal API/MVC endpoint (e.g. 'CatalogApi.CreateItem'), gRPC method (e.g. 'BasketService.UpdateBasket'), worker, consumer, or service."),
                ["scenarios"] = scenarios,
                ["path"] = McpToolCatalog.PathProperty(),
                ["analysis"] = McpToolCatalog.AnalysisProperty(),
            },
            ["required"] = new JsonArray("request", "scenarios"),
        };
    }

    public static JsonObject DraftSliceSchema() => new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["properties"] = new JsonObject
        {
            ["request"] = StringProperty("Target name or full type name to draft a slice design for (CQRS request, Minimal API/MVC endpoint, gRPC method, worker, consumer, or service)."),
            ["path"] = McpToolCatalog.PathProperty(),
            ["analysis"] = McpToolCatalog.AnalysisProperty(),
        },
        ["required"] = new JsonArray("request"),
    };

    public static JsonObject ChangeImpactSchema() => new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["properties"] = new JsonObject
        {
            ["target"] = StringProperty("Target symbol, entrypoint, event, or project name to trace bidirectional dependency, slice, flow, and test impact for."),
            ["targetKind"] = new JsonObject
            {
                ["type"] = "string",
                ["enum"] = new JsonArray("project", "entity", "dbcontext", "datastore", "service", "notification", "handler", "request", "entrypoint", "event"),
                ["description"] = "Optional target kind used to disambiguate targets with the same name.",
            },
            ["maxDepth"] = new JsonObject
            {
                ["type"] = "integer",
                ["minimum"] = 1,
                ["maximum"] = 10,
                ["description"] = "Maximum upstream and downstream traversal depth (1-10, default 5).",
            },
            ["path"] = McpToolCatalog.PathProperty(),
            ["analysis"] = McpToolCatalog.AnalysisProperty(),
        },
        ["required"] = new JsonArray("target"),
    };

    public static JsonObject SliceDesignSchema() => BuildDesignToolSchema();

    public static JsonObject VerifyTddCycleSchema() => BuildDesignToolSchema(
        new KeyValuePair<string, JsonNode>(
            "testResultsPath",
            StringProperty("Optional path to a Visual Studio .trx test results file inside allowed workspace roots.")));

    public static Func<AnalysisResult, string> PrepareProposal(JsonNode? id, JsonObject input)
    {
        ModelJson.ValidateKeys(input, "architecture proposal arguments", "projects", "references", "path", "analysis");
        var proposal = ArchitectureProposalInput.Read(input);
        return result => McpResponses.BuildStructuredToolResult(
            id, ArchitectureProposalEvaluator.Evaluate(result, proposal.Projects, proposal.References));
    }

    public static Func<AnalysisResult, string> PrepareSliceDesign(JsonNode? id, JsonObject input)
    {
        ModelJson.ValidateKeys(input, "slice design arguments", "design", "path", "analysis");
        if (input["design"] is not JsonObject document)
            throw new ArgumentException("'design' must be the saved slice design JSON object.");

        var design = SliceDesignInput.Read(document);
        return result => McpResponses.BuildStructuredToolResult(id, SliceDesignEvaluator.Evaluate(result, design));
    }

    public static Func<AnalysisResult, string> PrepareDraftSlice(JsonNode? id, JsonObject input)
    {
        ModelJson.ValidateKeys(input, "draft slice design arguments", "request", "path", "analysis");
        var request = McpResponses.ReadString(input["request"]);
        if (string.IsNullOrWhiteSpace(request))
            throw new ArgumentException("'request' is required for draft_slice_design.");

        return result => McpResponses.BuildStructuredToolResult(id, SliceDesignDrafter.Draft(result, request));
    }

    public static Func<AnalysisResult, string> PrepareChangeImpact(
        JsonNode? id,
        JsonObject input,
        McpToolArguments arguments)
    {
        ModelJson.ValidateKeys(input, "change impact arguments", "target", "targetKind", "maxDepth", "path", "analysis");
        if (string.IsNullOrWhiteSpace(arguments.CheckTarget))
            throw new ArgumentException("'target' is required for analyze_change_impact.");

        string? targetKind = null;
        if (input.TryGetPropertyValue("targetKind", out var kindNode) && kindNode is not null)
        {
            var rawKind = McpResponses.ReadString(kindNode);
            if (string.IsNullOrWhiteSpace(rawKind))
                throw new ArgumentException("'targetKind' must be a non-empty string when supplied.");

            var trimmed = rawKind.Trim();
            if (!ValidImpactKinds.Contains(trimmed))
            {
                throw new ArgumentException(
                    $"Unsupported targetKind '{rawKind}'. Expected one of: project, entity, dbcontext, datastore, service, notification, handler, request, entrypoint, event.");
            }

            targetKind = trimmed;
        }

        if (arguments.MaxDepth is < 1 or > 10)
            throw new ArgumentException("'maxDepth' must be between 1 and 10 (omit it for the default).");

        var maxDepth = arguments.MaxDepth ?? 5;
        return result => McpResponses.BuildStructuredToolResult(
            id,
            ChangeImpactAnalyzer.Analyze(result, arguments.CheckTarget!, targetKind, maxDepth));
    }

    public static (SliceDesign Design, string? TestResultsPath) PrepareVerifyTddCycle(JsonObject input)
    {
        ModelJson.ValidateKeys(input, "TDD cycle verification arguments", "design", "testResultsPath", "path", "analysis");
        if (input["design"] is not JsonObject document)
            throw new ArgumentException("'design' must be the saved slice design JSON object.");

        var design = SliceDesignInput.Read(document);

        string? testResultsPath = null;
        if (input.TryGetPropertyValue("testResultsPath", out var trxNode) && trxNode is not null)
        {
            testResultsPath = McpResponses.ReadString(trxNode);
            if (string.IsNullOrWhiteSpace(testResultsPath))
                throw new ArgumentException("'testResultsPath' must be a non-empty string when supplied.");
        }

        return (design, testResultsPath);
    }

    public static Func<AnalysisResult, string> PrepareTestPlan(JsonNode? id, JsonObject input, string? request)
    {
        if (string.IsNullOrWhiteSpace(request))
            throw new ArgumentException("'request' is required for plan_slice_tests.");

        const int maxScenarios = AgentSliceTestPlanGenerator.MaxScenarios;
        if (input["scenarios"] is not JsonArray scenarios || scenarios.Count is 0 or > maxScenarios)
            throw new ArgumentException($"'scenarios' must contain between 1 and {maxScenarios} Given/When/Then scenarios.");

        var entries = new List<SliceTestScenario>(scenarios.Count);
        for (var index = 0; index < scenarios.Count; index++)
        {
            if (scenarios[index] is not JsonObject scenario)
                throw new ArgumentException($"scenarios[{index}] must be an object.");

            entries.Add(new SliceTestScenario(
                ReadScenarioField(scenario, index, "name"),
                ReadScenarioField(scenario, index, "given"),
                ReadScenarioField(scenario, index, "when"),
                ReadScenarioField(scenario, index, "then")));
        }

        return result => McpResponses.BuildStructuredToolResult(
            id, AgentSliceTestPlanGenerator.Generate(result, request, entries));
    }

    private static JsonObject BuildDesignToolSchema(params KeyValuePair<string, JsonNode>[] extraProperties)
    {
        var designSchema = SliceDesignInput.Schema();
        var dialect = designSchema["$schema"];
        designSchema.Remove("$schema");
        designSchema.Remove("$id");
        var definitions = designSchema["$defs"];
        designSchema.Remove("$defs");

        var properties = new JsonObject
        {
            ["design"] = designSchema,
        };
        foreach (var (key, value) in extraProperties)
            properties[key] = value;

        properties["path"] = McpToolCatalog.PathProperty();
        properties["analysis"] = McpToolCatalog.AnalysisProperty();

        var toolSchema = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = properties,
            ["required"] = new JsonArray("design"),
        };

        if (dialect is not null)
            toolSchema["$schema"] = dialect;

        // References within the embedded design schema start at the tool schema root.
        if (definitions is not null)
            toolSchema["$defs"] = definitions;
        return toolSchema;
    }

    private static string ReadScenarioField(JsonObject scenario, int index, string field)
    {
        var value = McpResponses.ReadString(scenario[field]);
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"scenarios[{index}].{field} must be a non-empty string.");

        return value;
    }

    private static JsonObject StringProperty(string description) => new()
    {
        ["type"] = "string",
        ["minLength"] = 1,
        ["description"] = description,
    };

    private static JsonObject ArrayOfObjects(
        JsonObject properties, JsonArray required, bool allowExtraProperties = true) => new()
    {
        ["type"] = "array",
        ["items"] = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = allowExtraProperties,
            ["properties"] = properties,
            ["required"] = required,
        },
    };

    private static JsonObject NonEmptyArray(string property) => new()
    {
        ["required"] = new JsonArray(property),
        ["properties"] = new JsonObject
        {
            [property] = new JsonObject { ["minItems"] = 1 },
        },
    };
}
