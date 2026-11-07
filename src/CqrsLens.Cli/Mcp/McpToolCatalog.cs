using System.Text.Json.Nodes;

namespace CqrsLens;

/// <summary>Declares the MCP tool catalog and its JSON input schemas.</summary>
internal static class McpToolCatalog
{
    public static readonly HashSet<string> KnownTools = new(StringComparer.Ordinal)
    {
        "get_architecture_dna",
        "trace_slice",
        "trace_entity_impact",
        "check_architecture",
        "trace_request_impact",
        "verify_claim",
        "diff_architecture",
    };

    public static string List(JsonNode? idNode)
    {
        var tools = new JsonArray
        {
            new JsonObject
            {
                ["name"] = "get_architecture_dna",
                ["description"] = "Returns a compact, token-efficient Architectural DNA summary of the .NET solution: layer & project topology, CQRS conventions (mediator library, co-located vs separate handler layout, active pipeline behaviors), EF Core DbContexts, and counts. Call this first to orient yourself. Set includeIndex=true only when you need the full one-line slice index.",
                ["annotations"] = ReadOnlyAnnotation(),
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["path"] = PathProperty(
                            "Optional solution or repository folder path. Defaults to the path configured when launching the MCP server."),
                        ["analysis"] = AnalysisProperty(
                            "Optional scanner backend: 'text' (default, fast) or 'roslyn'."),
                        ["includeIndex"] = new JsonObject
                        {
                            ["type"] = "boolean",
                            ["description"] = "Include the full one-line index of every slice (default false; the compact summary is usually enough).",
                        },
                    },
                },
            },
            new JsonObject
            {
                ["name"] = "trace_slice",
                ["description"] = "Traces end-to-end CQRS vertical slices matching a keyword (request/handler/validator name, API route, entity, or domain event). Results are ranked exact > prefix > substring and capped. Returns File:Line source locations for API Entrypoint -> Request(Payload) -> Validator -> Handler(DI Dependencies) -> Touched DbSets + Published Events -> Event Handlers. Values marked (inferred) were guessed from a convention, not read from source. Set format='json' to receive the same slice as structured typed data (requests, handlers, validators, behaviors in registered order, notifications and their handlers, endpoint and DbContext edges as the scan model holds them).",
                ["annotations"] = ReadOnlyAnnotation(),
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["query"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Keyword or substring to filter vertical slices (e.g. 'Order', 'TodoItem', '/api/orders', 'Checkout'). A bare kind such as 'Command' is rejected as too broad.",
                        },
                        ["limit"] = LimitProperty(25, "Maximum slices to return (default 5). Remaining matches are counted, not printed."),
                        ["format"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["enum"] = new JsonArray("text", "json"),
                            ["description"] = "Output form: 'text' (default, the rendered slice trace) or 'json' (the same slice as structured typed data). Structured values come from the scan model alone.",
                        },
                        ["path"] = PathProperty(),
                        ["analysis"] = AnalysisProperty(),
                    },
                    ["required"] = new JsonArray("query"),
                },
            },
            new JsonObject
            {
                ["name"] = "trace_entity_impact",
                ["description"] = "Traces an EF Core entity or DbSet across the architecture. By default the entity name must match exactly, so 'Order' never matches OrderItem or PurchaseOrder. Returns the entity's schema (PKs, FKs, scalar properties, relationships) and every Command, Query, Handler, and API Endpoint that reads, writes, or depends on it.",
                ["annotations"] = ReadOnlyAnnotation(),
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["entity"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Entity or DbSet name to trace (e.g. 'Order', 'TodoItem', 'Contributor').",
                        },
                        ["match"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["enum"] = new JsonArray("exact", "contains"),
                            ["description"] = "Entity-name matching mode (default 'exact'). Use 'contains' only when you intentionally want substrings.",
                        },
                        ["limit"] = LimitProperty(50, "Maximum impacted slices to return (default 25)."),
                        ["path"] = PathProperty(),
                        ["analysis"] = AnalysisProperty(),
                    },
                    ["required"] = new JsonArray("entity"),
                },
            },
            new JsonObject
            {
                ["name"] = "check_architecture",
                ["description"] = "Runs CqrsLens static architecture rules (MissingHandler, MultipleHandlers, CommandWithoutValidator, LayerViolation, ApplicationLayerViolation, CircularProjectDependency, OrphanValidator, OrphanNotification, etc.) and returns all active findings with File:Line locations. Optionally filter by rule code, severity and target scope; filters compose (AND). A filtered query states the filter and the total matching count, then a page of findings. With no filter the answer is the unfiltered finding list. Set groupBy='project' to return counts first, grouped by project (module) with the per-project rule breakdown, ordered by count descending.",
                ["annotations"] = ReadOnlyAnnotation(),
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["rules"] = new JsonObject
                        {
                            ["type"] = "array",
                            ["items"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["enum"] = RuleCodeEnum(),
                            },
                            ["description"] = "Optional rule-code filter: one or more of the eleven architecture rules. A finding matches when its code is any of them. Composes (AND) with severity and target.",
                        },
                        ["severity"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["enum"] = new JsonArray("error", "warning"),
                            ["description"] = "Optional severity filter: 'error' or 'warning'. Composes (AND) with rules and target.",
                        },
                        ["target"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Optional target scope: a project name (matched exactly) or a substring matched against the finding's target. Composes (AND) with rules and severity.",
                        },
                        ["groupBy"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["enum"] = new JsonArray("project"),
                            ["description"] = "Optional grouping form: 'project' returns findings grouped by project (module), counts first (total, errors, warnings, rule breakdown), ordered by count descending and paged by 'limit'. Composes with rules/severity/target; a grouped reply states the filter. Omit for the flat (filtered) finding list, which is unchanged.",
                        },
                        ["limit"] = LimitProperty(
                            AgentReportLimits.MaxCheckLimit,
                            "Maximum findings to return in a filtered query, or maximum project groups to return when groupBy is set (omit to return every match). Remaining matches are counted, not printed."),
                        ["path"] = PathProperty(),
                        ["analysis"] = AnalysisProperty(),
                    },
                },
            },
            new JsonObject
            {
                ["name"] = "trace_request_impact",
                ["description"] = "Traces what one request touches and what touches it, across the architecture. By default the request name must match exactly, so 'CreateCustomerCommand' never matches another request. Returns the request's handler, validators, the pipeline behaviors in registered order (execution order is not claimed), the endpoints that dispatch it, the notifications it publishes and their handlers, and the handler→DbContext edges the model holds — plus reverse reach: every shape the model records as referencing the request. Absence is stated as absence; an unknown request name is a not-found answer, not an error.",
                ["annotations"] = ReadOnlyAnnotation(),
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["request"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Request name to trace (e.g. 'CreateCustomerCommand', 'CreateOrderCommand', or a full type name).",
                        },
                        ["match"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["enum"] = new JsonArray("exact", "contains"),
                            ["description"] = "Request-name matching mode (default 'exact'). Use 'contains' only when you intentionally want substrings.",
                        },
                        ["limit"] = LimitProperty(50, "Maximum impacted requests to return (default 25)."),
                        ["path"] = PathProperty(),
                        ["analysis"] = AnalysisProperty(),
                    },
                    ["required"] = new JsonArray("request"),
                },
            },
            new JsonObject
            {
                ["name"] = "verify_claim",
                ["description"] = "Verifies one structured design claim against the scan model and fails closed. Give a subject (a named request, handler, validator, behavior, DbContext, entity, or project), a relation from the fixed vocabulary (e.g. 'has handler', 'has no validator', 'has validator', 'publishes', 'handled notification', 'uses DbContext', 'depends on', 'belongs to layer', 'dispatched by', and their absence forms), and an object (or omit it for a bare existence/absence claim). Returns exactly one verdict — supported (with the model facts and File:Line provenance), not supported (with the contradicting facts), or not determinable (with the reason: unresolved dispatch, runtime-typed send, package-opaque wrapper, ambiguity, or outside the scan model). Claims about execution order or conventions are outside the model and return not determinable.",
                ["annotations"] = ReadOnlyAnnotation(),
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["subject"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "The named shape the claim is about, e.g. 'CreateOrderCommand' or 'CreateOrderCommandHandler'.",
                        },
                        ["relation"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "The relation to verify, e.g. 'has handler', 'has no validator', 'publishes', 'handled notification', 'uses DbContext', 'depends on', 'belongs to layer', 'dispatched by', or an absence form. Unknown relations return not determinable.",
                        },
                        ["object"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "The named target, when the relation takes one, e.g. 'CreateOrderCommandHandler'. Omit for a bare existence/absence claim such as 'has no validator'.",
                        },
                        ["path"] = PathProperty(),
                        ["analysis"] = AnalysisProperty(),
                    },
                    ["required"] = new JsonArray("subject", "relation"),
                },
            },
            new JsonObject
            {
                ["name"] = "diff_architecture",
                ["description"] = "Diffs the current scan against a base — a saved report JSON snapshot produced by this tool, or a second folder to scan — with exactly the CLI's --diff operand semantics. Returns counts first (added / removed / unchanged for requests, handlers, validators, pipeline behaviors, DbContexts and endpoints), then the findings delta (new and resolved findings, each identified by its stable baseline key of rule + target + normalized file), then the paged item lists. Identical scans return an explicitly empty delta; a base that cannot be read or parsed is a clean error, never a partial delta.",
                ["annotations"] = ReadOnlyAnnotation(),
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["base"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "The base to compare against: a saved report JSON produced by this tool (-o ARCHITECTURE.json / --format json), or a folder to scan as the base. Same operand semantics as the CLI's --diff.",
                        },
                        ["limit"] = LimitProperty(
                            AgentReportLimits.MaxDiffLimit,
                            "Maximum items to return per item list (omit to return every item). Remaining items are counted, not printed."),
                        ["path"] = PathProperty(),
                        ["analysis"] = AnalysisProperty(),
                    },
                    ["required"] = new JsonArray("base"),
                },
            },
        };

        return McpResponses.BuildResult(idNode, new JsonObject { ["tools"] = tools });
    }

    private static JsonObject ReadOnlyAnnotation() => new()
    {
        ["readOnlyHint"] = true,
    };

    private static JsonObject PathProperty(
        string description = "Optional solution or repository folder path.") => new()
    {
        ["type"] = "string",
        ["description"] = description,
    };

    private static JsonObject AnalysisProperty(
        string description = "Optional scanner backend: 'text' or 'roslyn'.") => new()
    {
        ["type"] = "string",
        ["enum"] = new JsonArray("text", "roslyn"),
        ["description"] = description,
    };

    private static JsonObject LimitProperty(int maximum, string description) => new()
    {
        ["type"] = "integer",
        ["minimum"] = 1,
        ["maximum"] = maximum,
        ["description"] = description,
    };

    private static JsonArray RuleCodeEnum()
    {
        var codes = new JsonArray();
        foreach (var code in AgentCheckRules.All)
            codes.Add(code);
        return codes;
    }
}
