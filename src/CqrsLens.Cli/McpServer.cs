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

    private static readonly HashSet<string> SupportedProtocolVersions = new(StringComparer.Ordinal)
    {
        "2024-11-05",
        "2025-03-26",
        "2025-06-18",
    };

    private static readonly HashSet<string> KnownTools = new(StringComparer.Ordinal)
    {
        "get_architecture_dna",
        "trace_slice",
        "trace_entity_impact",
        "check_architecture",
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
    };

    internal readonly record struct ScanFingerprint(
        int FileCount,
        long MaxWriteTicks,
        ulong PathSizeAndTimeHash);

    internal sealed record CachedScan(
        AnalysisResult Result,
        ScanFingerprint Fingerprint);

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
        var method = ReadString(obj["method"]);

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
        var requestedVersion = ReadString(paramsObj?["protocolVersion"]);
        var protocolVersion = !string.IsNullOrWhiteSpace(requestedVersion) &&
                              SupportedProtocolVersions.Contains(requestedVersion)
            ? requestedVersion
            : DefaultProtocolVersion;

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
                ["description"] = "Returns a compact, token-efficient Architectural DNA summary of the .NET solution: layer & project topology, CQRS conventions (mediator library, co-located vs separate handler layout, active pipeline behaviors), EF Core DbContexts, and counts. Call this first to orient yourself. Set includeIndex=true only when you need the full one-line slice index.",
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
                        ["limit"] = new JsonObject
                        {
                            ["type"] = "integer",
                            ["minimum"] = 1,
                            ["maximum"] = 25,
                            ["description"] = "Maximum slices to return (default 5). Remaining matches are counted, not printed.",
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
                        ["limit"] = new JsonObject
                        {
                            ["type"] = "integer",
                            ["minimum"] = 1,
                            ["maximum"] = 50,
                            ["description"] = "Maximum impacted slices to return (default 25).",
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
        var toolName = ReadString(paramsObj?["name"]);
        if (string.IsNullOrEmpty(toolName))
            return BuildErrorResponse(idNode, -32602, "Invalid params: missing tool name");

        if (!KnownTools.Contains(toolName))
            return BuildErrorResponse(idNode, -32602, $"Unknown tool: {toolName}");

        var argsObj = paramsObj?["arguments"] as JsonObject;

        string? pathArg;
        string? analysisOverride;
        string? query = null;
        string? entity = null;
        string? matchMode = null;
        var includeIndex = false;
        var limit = 0;
        try
        {
            pathArg = argsObj?["path"]?.GetValue<string>();
            analysisOverride = argsObj?["analysis"]?.GetValue<string>();
            query = argsObj?["query"]?.GetValue<string>() ?? argsObj?["filter"]?.GetValue<string>();
            entity = argsObj?["entity"]?.GetValue<string>();
            matchMode = argsObj?["match"]?.GetValue<string>();
            includeIndex = argsObj?["includeIndex"]?.GetValue<bool>() ?? false;
            if (argsObj?["limit"] is { } limitNode)
                limit = limitNode.GetValue<int>();
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            return BuildErrorResponse(idNode, -32602, "Invalid params: argument values have the wrong type");
        }

        // Validate every argument before touching the filesystem or running a scan.
        if (toolName == "trace_slice" && string.IsNullOrWhiteSpace(query))
            return BuildToolResult(idNode, "error: 'query' argument is required for trace_slice.", isError: true);
        if (toolName == "trace_entity_impact" && string.IsNullOrWhiteSpace(entity))
            return BuildToolResult(idNode, "error: 'entity' argument is required for trace_entity_impact.", isError: true);
        if (!string.IsNullOrWhiteSpace(analysisOverride) &&
            !string.Equals(analysisOverride, "text", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(analysisOverride, "roslyn", StringComparison.OrdinalIgnoreCase))
        {
            return BuildToolResult(idNode, "error: 'analysis' must be 'text' or 'roslyn'.", isError: true);
        }
        var maxLimit = toolName == "trace_entity_impact" ? 50 : 25;
        if (limit < 0 || limit > maxLimit)
            return BuildToolResult(idNode, $"error: 'limit' must be between 1 and {maxLimit} (omit it for the default).", isError: true);
        if (!string.IsNullOrWhiteSpace(matchMode) &&
            !string.Equals(matchMode, "exact", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(matchMode, "contains", StringComparison.OrdinalIgnoreCase))
        {
            return BuildToolResult(idNode, "error: 'match' must be 'exact' or 'contains'.", isError: true);
        }

        var targetPath = string.IsNullOrWhiteSpace(pathArg)
            ? defaultOptions.Path ?? Directory.GetCurrentDirectory()
            : pathArg;

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
                var text = AgentReportGenerator.GenerateDnaAndIndex(result, includeIndex);
                return BuildToolResult(idNode, text, isError: false);
            }
            case "trace_slice":
            {
                var text = AgentReportGenerator.Generate(result, query, limit);
                return BuildToolResult(idNode, text, isError: false);
            }
            case "trace_entity_impact":
            {
                var contains = string.Equals(matchMode, "contains", StringComparison.OrdinalIgnoreCase);
                var text = AgentReportGenerator.GenerateEntityImpact(result, entity!, contains, limit);
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
        var fingerprint = ComputeScanFingerprint(fullRoot, defaultOptions.ConfigPath, defaultOptions.BaselinePath);

        if (cache.TryGetValue(cacheKey, out var cached) && cached.Fingerprint == fingerprint)
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
            var baseline = BaselineStore.Read(defaultOptions.BaselinePath);
            BaselineMatcher.Apply(result, baseline, defaultOptions.BaselinePath);
        }

        cache[cacheKey] = new CachedScan(result, fingerprint);
        return result;
    }

    /// <summary>
    /// Cache invalidation must notice added/removed/renamed inputs as well as edits whose
    /// timestamp is older than the newest file. Hash every source, project, build-config,
    /// solution, explicit config, and baseline file name with its size and write time.
    /// </summary>
    private static ScanFingerprint ComputeScanFingerprint(
        string fullRoot,
        string? configPath,
        string? baselinePath)
    {
        var fileCount = 0;
        var maxTicks = 0L;
        var hash = 14695981039346656037UL;

        void AddHash(ulong value)
        {
            for (var i = 0; i < 8; i++)
            {
                hash ^= (byte)(value >> (i * 8));
                hash *= 1099511628211UL;
            }
        }

        void AddString(string value)
        {
            foreach (var ch in value)
            {
                hash ^= ch;
                hash *= 1099511628211UL;
            }
            AddHash(0xFFFF); // separator so "ab"+"c" != "a"+"bc"
        }

        void Include(string path)
        {
            try
            {
                var info = new FileInfo(Path.GetFullPath(path));
                if (!info.Exists) return;

                fileCount++;
                var ticks = info.LastWriteTimeUtc.Ticks;
                if (ticks > maxTicks) maxTicks = ticks;
                AddString(Path.GetRelativePath(fullRoot, info.FullName));
                AddHash((ulong)info.Length);
                AddHash((ulong)ticks);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // Unreadable inputs simply do not participate in the fingerprint.
            }
        }

        try
        {
            foreach (var file in CsprojParser.FindSourceFiles(fullRoot))
                Include(file);
            foreach (var file in CsprojParser.FindProjectFiles(fullRoot))
                Include(file);

            var discoveryOptions = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
                ReturnSpecialDirectories = false,
            };
            foreach (var name in new[] { "Directory.Build.props", "Directory.Build.targets", ".cqrslens.json" })
            {
                foreach (var file in Directory.EnumerateFiles(fullRoot, name, discoveryOptions))
                {
                    if (!CsprojParser.IsSkippedPath(file, fullRoot))
                        Include(file);
                }
            }
            foreach (var file in Directory.EnumerateFiles(fullRoot, "*.sln", discoveryOptions))
            {
                if (!CsprojParser.IsSkippedPath(file, fullRoot))
                    Include(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Fall back to whatever was included before enumeration failed.
        }

        if (!string.IsNullOrWhiteSpace(configPath))
            Include(configPath);
        if (!string.IsNullOrWhiteSpace(baselinePath))
            Include(baselinePath);

        return new ScanFingerprint(fileCount, maxTicks, hash);
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

    /// <summary>Reads a JSON string, returning null for null/non-string values instead of throwing.</summary>
    private static string? ReadString(JsonNode? node)
        => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

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

                // Content-Length is a UTF-8 byte count, while TextReader exposes characters.
                // Read character by character (keeping surrogate pairs together) until the
                // encoded byte count is reached, otherwise non-ASCII payloads swallow or
                // truncate the next message.
                const int MaxContentLength = 32 * 1024 * 1024;
                if (contentLength > MaxContentLength)
                    return (null, true);

                var sb = new StringBuilder(Math.Min(contentLength, 64 * 1024));
                var bytesRead = 0;
                while (bytesRead < contentLength)
                {
                    var ch = reader.Read();
                    if (ch < 0)
                        return (null, true);

                    if (char.IsHighSurrogate((char)ch))
                    {
                        var low = reader.Read();
                        if (low < 0)
                            return (null, true);
                        sb.Append((char)ch).Append((char)low);
                        bytesRead += 4;
                        continue;
                    }

                    sb.Append((char)ch);
                    bytesRead += ch < 0x80 ? 1 : ch < 0x800 ? 2 : 3;
                }

                return (sb.ToString(), true);
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
