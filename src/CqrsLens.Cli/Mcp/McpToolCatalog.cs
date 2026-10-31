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
    };

    public static string List(JsonNode? idNode)
    {
        var tools = new JsonArray
        {
            new JsonObject
            {
                ["name"] = "get_architecture_dna",
                ["description"] = "Returns a compact, token-efficient Architectural DNA summary of the .NET solution: layer & project topology, CQRS conventions (mediator library, co-located vs separate handler layout, active pipeline behaviors), EF Core DbContexts, and counts. Call this first to orient yourself. Set includeIndex=true only when you need the full one-line slice index.",
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
                ["description"] = "Traces end-to-end CQRS vertical slices matching a keyword (request/handler/validator name, API route, entity, or domain event). Results are ranked exact > prefix > substring and capped. Returns File:Line source locations for API Entrypoint -> Request(Payload) -> Validator -> Handler(DI Dependencies) -> Touched DbSets + Published Events -> Event Handlers. Values marked (inferred) were guessed from a convention, not read from source.",
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
                ["description"] = "Runs CqrsLens static architecture rules (MissingHandler, MultipleHandlers, CommandWithoutValidator, LayerViolation, ApplicationLayerViolation, CircularProjectDependency, OrphanValidator, OrphanNotification, etc.) and returns all active findings with File:Line locations.",
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["path"] = PathProperty(),
                        ["analysis"] = AnalysisProperty(),
                    },
                },
            },
        };

        return McpResponses.BuildResult(idNode, new JsonObject { ["tools"] = tools });
    }

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
}
