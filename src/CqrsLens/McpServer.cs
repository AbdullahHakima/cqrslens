using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CqrsLens;

/// <summary>
/// Built-in Model Context Protocol (MCP) server (<c>cqrslens --mcp</c>) over JSON-RPC 2.0 stdio.
/// Exposes token-efficient architectural intelligence tools (<c>get_architecture_dna</c>,
/// <c>trace_slice</c>, <c>trace_entity_impact</c>, and <c>check_architecture</c>) so AI coding
/// agents can inspect any .NET CQRS/Clean Architecture solution in milliseconds without burning
/// context tokens on manual file searches.
/// </summary>
public static class McpServer
{
    private const string DefaultProtocolVersion = "2024-11-05";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
    };

    internal sealed record CachedScan(
        AnalysisResult Result,
        long LatestWriteTicks);

    public static int Run(
        CliOptions defaultOptions,
        TextReader? stdin = null,
        TextWriter? stdout = null,
        TextWriter? stderr = null)
    {
        stdin ??= Console.In;
        stdout ??= Console.Out;
        stderr ??= Console.Error;

        var cache = new Dictionary<string, CachedScan>(StringComparer.OrdinalIgnoreCase);

        while (true)
        {
            var (payload, useContentLength) = ReadMessage(stdin);
            if (payload is null)
                break;

            if (string.IsNullOrWhiteSpace(payload))
                continue;

            var response = ProcessMessage(payload, defaultOptions, cache, stderr);
            if (response is not null)
                WriteMessage(stdout, response, useContentLength);
        }

        return ExitCodes.Success;
    }

    internal static string? ProcessMessage(
        string jsonPayload,
        CliOptions defaultOptions,
        Dictionary<string, CachedScan>? cache = null,
        TextWriter? stderr = null)
    {
        cache ??= new Dictionary<string, CachedScan>(StringComparer.OrdinalIgnoreCase);

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(jsonPayload);
        }
        catch (JsonException ex)
        {
            stderr?.WriteLine($"mcp parse error: {ex.Message}");
            return BuildErrorResponse(null, -32700, "Parse error");
        }

        if (root is not JsonObject obj)
            return BuildErrorResponse(null, -32600, "Invalid Request");

        var hasId = obj.TryGetPropertyValue("id", out var idNode);
        var method = obj["method"]?.GetValue<string>();

        if (string.IsNullOrEmpty(method))
            return hasId ? BuildErrorResponse(idNode, -32600, "Invalid Request: missing method") : null;

        // JSON-RPC notifications (no "id" property) do not receive a response.
        if (!hasId)
            return null;

        var paramsObj = obj["params"] as JsonObject;

        return method switch
        {
            "initialize" => HandleInitialize(idNode, paramsObj),
            "ping" => BuildResultResponse(idNode, new JsonObject()),
            "tools/list" => HandleToolsList(idNode),
            "tools/call" => HandleToolsCall(idNode, paramsObj, defaultOptions, cache),
            _ => BuildErrorResponse(idNode, -32601, $"Method not found: {method}"),
        };
    }

    private static string HandleInitialize(JsonNode? idNode, JsonObject? paramsObj)
    {
        var requestedVersion = paramsObj?["protocolVersion"]?.GetValue<string>();
        var protocolVersion = string.IsNullOrWhiteSpace(requestedVersion)
            ? DefaultProtocolVersion
            : requestedVersion;

        var result = new JsonObject
        {
            ["protocolVersion"] = protocolVersion,
            ["capabilities"] = new JsonObject
            {
                ["tools"] = new JsonObject
                {
                    ["listChanged"] = false,
                },
            },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = "cqrslens",
                ["version"] = ToolInfo.Version,
            },
        };

        return BuildResultResponse(idNode, result);
    }

    private static string HandleToolsList(JsonNode? idNode)
    {
        var tools = new JsonArray
        {
            new JsonObject
            {
                ["name"] = "get_architecture_dna",
                ["description"] = "Returns a compact, token-efficient Architectural DNA summary of the .NET solution: layer & project topology, CQRS conventions (mediator library, co-located vs separate handler layout, active pipeline behaviors), EF Core DbContexts, and a 1-line index of all vertical slices (commands/queries) and domain events. Call this first to orient yourself in the codebase.",
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["path"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Optional solution or repository folder path. Defaults to the path configured when launching the MCP server.",
                        },
                        ["analysis"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["enum"] = new JsonArray("text", "roslyn"),
                            ["description"] = "Optional scanner backend: 'text' (default, fast) or 'roslyn'.",
                        },
                    },
                },
            },
            new JsonObject
            {
                ["name"] = "trace_slice",
                ["description"] = "Traces one or more end-to-end CQRS vertical slices matching a keyword (matches API endpoint route, command/query name, handler, validator, DbSet/entity, or domain event). Returns exact File:Line source locations for API Entrypoint -> Request(Payload) -> Validator -> Handler(DI Dependencies) -> Touched DbSets + Published Events -> Event Handlers.",
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["query"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Keyword or substring to filter vertical slices (e.g. 'Order', 'TodoItem', '/api/orders', 'Checkout').",
                        },
                        ["path"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Optional solution or repository folder path.",
                        },
                        ["analysis"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["enum"] = new JsonArray("text", "roslyn"),
                            ["description"] = "Optional scanner backend: 'text' or 'roslyn'.",
                        },
                    },
                    ["required"] = new JsonArray("query"),
                },
            },
            new JsonObject
            {
                ["name"] = "trace_entity_impact",
                ["description"] = "Traces an EF Core entity or DbSet across the architecture. Returns the entity's schema (PKs, FKs, scalar properties, relationships) and every Command, Query, Handler, and API Endpoint that reads, writes, or depends on that entity.",
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
                        ["path"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Optional solution or repository folder path.",
                        },
                        ["analysis"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["enum"] = new JsonArray("text", "roslyn"),
                            ["description"] = "Optional scanner backend: 'text' or 'roslyn'.",
                        },
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
                        ["path"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Optional solution or repository folder path.",
                        },
                        ["analysis"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["enum"] = new JsonArray("text", "roslyn"),
                            ["description"] = "Optional scanner backend: 'text' or 'roslyn'.",
                        },
                    },
                },
            },
        };

        var result = new JsonObject
        {
            ["tools"] = tools,
        };
        return BuildResultResponse(idNode, result);
    }

    private static string HandleToolsCall(
        JsonNode? idNode,
        JsonObject? paramsObj,
        CliOptions defaultOptions,
        Dictionary<string, CachedScan> cache)
    {
        var toolName = paramsObj?["name"]?.GetValue<string>();
        if (string.IsNullOrEmpty(toolName))
            return BuildErrorResponse(idNode, -32602, "Invalid params: missing tool name");

        var argsObj = paramsObj?["arguments"] as JsonObject;
        var targetPath = argsObj?["path"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(targetPath))
            targetPath = defaultOptions.Path ?? Directory.GetCurrentDirectory();

        var analysisOverride = argsObj?["analysis"]?.GetValue<string>();

        if (!Directory.Exists(targetPath))
            return BuildToolResult(idNode, $"error: path not found: {targetPath}", isError: true);

        AnalysisResult result;
        try
        {
            result = GetOrRunAnalysis(targetPath, analysisOverride, defaultOptions, cache);
        }
        catch (Exception ex)
        {
            return BuildToolResult(idNode, $"error: {ex.Message}", isError: true);
        }

        switch (toolName)
        {
            case "get_architecture_dna":
            {
                var text = AgentReportGenerator.GenerateDnaAndIndex(result);
                return BuildToolResult(idNode, text, isError: false);
            }
            case "trace_slice":
            {
                var query = argsObj?["query"]?.GetValue<string>() ?? argsObj?["filter"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(query))
                    return BuildToolResult(idNode, "error: 'query' argument is required for trace_slice.", isError: true);
                var text = AgentReportGenerator.Generate(result, query);
                return BuildToolResult(idNode, text, isError: false);
            }
            case "trace_entity_impact":
            {
                var entity = argsObj?["entity"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(entity))
                    return BuildToolResult(idNode, "error: 'entity' argument is required for trace_entity_impact.", isError: true);
                var text = AgentReportGenerator.GenerateEntityImpact(result, entity);
                return BuildToolResult(idNode, text, isError: false);
            }
            case "check_architecture":
            {
                var text = AgentReportGenerator.GenerateChecksSummary(result);
                return BuildToolResult(idNode, text, isError: false);
            }
            default:
                return BuildErrorResponse(idNode, -32602, $"Unknown tool: {toolName}");
        }
    }

    private static AnalysisResult GetOrRunAnalysis(
        string path,
        string? analysisOverride,
        CliOptions defaultOptions,
        Dictionary<string, CachedScan> cache)
    {
        var fullRoot = Path.GetFullPath(path);
        var settings = ConfigurationLoader.Load(fullRoot, defaultOptions.ConfigPath);

        var mode = !string.IsNullOrWhiteSpace(analysisOverride)
            ? string.Equals(analysisOverride, "roslyn", StringComparison.OrdinalIgnoreCase)
                ? AnalysisMode.Roslyn
                : AnalysisMode.Text
            : defaultOptions.AnalysisModeSpecified
                ? defaultOptions.AnalysisMode
                : settings.AnalysisMode ?? defaultOptions.AnalysisMode;

        var cacheKey = $"{fullRoot}|{mode}";
        var latestWrite = ComputeLatestSourceWriteTicks(fullRoot);

        if (cache.TryGetValue(cacheKey, out var cached) && cached.LatestWriteTicks == latestWrite)
            return cached.Result;

        var markers = defaultOptions.MarkersSpecified ? defaultOptions.Markers : settings.Markers;
        var handlerMarkers = defaultOptions.HandlerMarkersSpecified ? defaultOptions.HandlerMarkers : settings.HandlerMarkers;

        var analysisOptions = new AnalysisOptions
        {
            Markers = markers,
            HandlerMarkers = handlerMarkers,
            Include = settings.Include,
            Exclude = settings.Exclude,
            LayerOverrides = settings.LayerOverrides,
            RuleSeverities = settings.RuleSeverities,
        };

        var result = Analyzer.Analyze(fullRoot, mode, analysisOptions);
        if (defaultOptions.BaselinePath is not null && File.Exists(defaultOptions.BaselinePath))
        {
            var baseline = Baseline.Read(defaultOptions.BaselinePath);
            Baseline.Apply(result, baseline, defaultOptions.BaselinePath);
        }

        cache[cacheKey] = new CachedScan(result, latestWrite);
        return result;
    }

    private static long ComputeLatestSourceWriteTicks(string fullRoot)
    {
        var maxTicks = 0L;
        try
        {
            foreach (var file in CsprojParser.FindSourceFiles(fullRoot))
            {
                var ticks = File.GetLastWriteTimeUtc(file).Ticks;
                if (ticks > maxTicks) maxTicks = ticks;
            }
        }
        catch
        {
            // Fall back to uncached / 0 ticks if file enumeration fails.
        }
        return maxTicks;
    }

    private static string BuildToolResult(JsonNode? idNode, string text, bool isError)
    {
        var result = new JsonObject
        {
            ["content"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = text,
                },
            },
            ["isError"] = isError,
        };
        return BuildResultResponse(idNode, result);
    }

    private static string BuildResultResponse(JsonNode? idNode, JsonObject result)
    {
        var response = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = idNode?.DeepClone(),
            ["result"] = result,
        };
        return response.ToJsonString(JsonOptions);
    }

    private static string BuildErrorResponse(JsonNode? idNode, int code, string message)
    {
        var response = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = idNode?.DeepClone(),
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message,
            },
        };
        return response.ToJsonString(JsonOptions);
    }

    private static (string? Payload, bool UseContentLength) ReadMessage(TextReader reader)
    {
        while (true)
        {
            var line = reader.ReadLine();
            if (line is null)
                return (null, false);

            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                var lengthText = line["Content-Length:".Length..].Trim();
                if (!int.TryParse(lengthText, out var contentLength) || contentLength < 0)
                    return (null, true);

                // Read remaining headers until blank line
                while (true)
                {
                    var headerLine = reader.ReadLine();
                    if (headerLine is null || headerLine.Length == 0)
                        break;
                }

                var buffer = new char[contentLength];
                var readTotal = 0;
                while (readTotal < contentLength)
                {
                    var read = reader.Read(buffer, readTotal, contentLength - readTotal);
                    if (read <= 0) break;
                    readTotal += read;
                }

                return (new string(buffer, 0, readTotal), true);
            }

            return (line, false);
        }
    }

    private static void WriteMessage(TextWriter writer, string json, bool useContentLength)
    {
        if (useContentLength)
        {
            var byteCount = Encoding.UTF8.GetByteCount(json);
            writer.Write($"Content-Length: {byteCount}\r\n\r\n{json}");
        }
        else
        {
            writer.WriteLine(json);
        }
        writer.Flush();
    }
}
