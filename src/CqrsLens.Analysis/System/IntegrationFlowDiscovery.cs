using System.Text.RegularExpressions;

namespace CqrsLens;

/// <summary>
/// Discovers cross-project integration flows across four channels: integration event pub/sub,
/// .NET Aspire orchestration wiring, typed/named HTTP &amp; gRPC clients, and shared EF Core
/// DbContexts across distinct non-test projects.
/// </summary>
internal static partial class IntegrationFlowDiscovery
{
    [GeneratedRegex(
        @"(?:(?:\bvar|\bI?ResourceBuilder\s*<[^>]*>)\s+(?<var>[A-Za-z_]\w*)\s*=\s*)?(?<receiver>[A-Za-z_]\w*)\s*\.\s*Add(?:Dotnet)?Project\s*(?:<\s*(?<projType>(?:global::)?[A-Za-z_]\w*(?:\s*\.\s*[A-Za-z_]\w*)*)\s*>)?\s*\(\s*(?:name\s*:\s*)?@?""(?<resName>[^""]+)""(?:\s*,\s*(?:projectPath\s*:\s*)?@?""(?<projPath>[^""]+)"")?",
        RegexOptions.None,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex AspireAddProjectRegex();

    [GeneratedRegex(
        @"(?:\bvar\s+)?(?<var>[A-Za-z_]\w*)\s*=\s*(?<receiver>[A-Za-z_]\w*)\s*\.\s*Add(?<kind>Postgres|PostgresDatabase|Npgsql|SqlServer|MySql|Sqlite|MongoDB|Redis|Garnet|Valkey|RabbitMQ|Kafka|AzureServiceBus|ServiceBus|AzureStorage|Storage|AzureBlobStorage|AzureQueueStorage|AzureCosmosDB|CosmosDB|Keycloak|Seq|MailDev|Orleans|Elasticsearch|Qdrant|Milvus|ConnectionString|Parameter|Container|Executable|NpmApp|Dockerfile|Database)\s*(?:<[^>]*>)?\s*\(\s*@?""(?<resName>[^""]+)""",
        RegexOptions.None,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex AspireAddResourceRegex();

    [GeneratedRegex(
        @"\.\s*AddDatabase\s*\(\s*@?""(?<dbName>[^""]+)""",
        RegexOptions.None,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex ChainedAddDatabaseRegex();

    [GeneratedRegex(
        @"\.\s*WithReference\s*\(\s*(?<arg>[^)]+?)\s*\)",
        RegexOptions.None,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex AspireWithReferenceRegex();

    [GeneratedRegex(
        @"\.\s*(?<kind>AddHttpClient|AddGrpcClient)\s*(?:<\s*(?<targs>[^;{}()]+?)\s*>)?\s*\(",
        RegexOptions.None,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex ClientRegistrationRegex();

    [GeneratedRegex(
        @"""(?<scheme>[a-zA-Z][a-zA-Z0-9+.-]*)://(?<host>[A-Za-z0-9_.-]+)(?::\d+)?(?:/[^""]*)?""",
        RegexOptions.None,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex ServiceUriRegex();

    [GeneratedRegex(
        @"^@?""(?<name>[^""]+)""",
        RegexOptions.None,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex LeadingStringLiteralRegex();

    private static readonly string[] ConsumerInterfaces =
    {
        "IIntegrationEventHandler",
        "IConsumer",
        "IHandleMessages",
        "IEventHandler",
    };

    private static readonly HashSet<string> LoopbackOrPlaceholderHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "localhost", "127.0.0.1", "0.0.0.0", "::1", "[::1]", "example.com", "api.example.com",
    };

    private sealed record ConsumerTarget(
        string ProjectName,
        string DeclFullName,
        string SimpleName,
        string? ResolvedFullName,
        bool IsTopic);

    private sealed record ProjectVar(string ProjectName, string ResourceName);

    private sealed record InfraVar(string DisplayName, string ResourceName, string Tech);

    public static void Discover(
        IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles,
        AnalysisResult result,
        TypeHierarchy? hierarchy = null)
        => Discover(SystemAnalysisContext.Create(parsedFiles, result, hierarchy));

    internal static void Discover(SystemAnalysisContext context)
    {
        var flows = new List<IntegrationFlowEdge>();

        DiscoverIntegrationEventFlows(context, flows);
        var aspireResourceToProject = DiscoverAspireReferenceFlows(context, flows);
        DiscoverClientFlows(context, aspireResourceToProject, flows);
        DiscoverSharedDbContextFlows(context, flows);

        var deduped = new Dictionary<(IntegrationFlowKind Kind, string From, string To, string Label, string? Msg), IntegrationFlowEdge>();
        foreach (var edge in flows)
        {
            var key = (
                edge.Kind,
                edge.FromProject,
                edge.ToProject,
                edge.Label,
                edge.MessageType);
            deduped.TryAdd(key, edge);
        }

        context.Result.IntegrationFlows.AddRange(deduped.Values
            .OrderBy(f => f.Kind)
            .ThenBy(f => f.FromProject, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.ToProject, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.Label, StringComparer.Ordinal)
            .ThenBy(f => f.MessageType ?? string.Empty, StringComparer.Ordinal));
    }

    private static void DiscoverIntegrationEventFlows(
        SystemAnalysisContext context,
        List<IntegrationFlowEdge> flows)
    {
        var consumers = CollectConsumerTargets(context);
        if (consumers.Count == 0)
            return;

        var byFullName = new Dictionary<string, List<ConsumerTarget>>(StringComparer.Ordinal);
        var bySimpleName = new Dictionary<string, List<ConsumerTarget>>(StringComparer.Ordinal);
        var byTopic = new Dictionary<string, List<ConsumerTarget>>(StringComparer.Ordinal);

        foreach (var c in consumers)
        {
            if (c.IsTopic)
            {
                if (!byTopic.TryGetValue(c.SimpleName, out var tList))
                    byTopic[c.SimpleName] = tList = new List<ConsumerTarget>();
                tList.Add(c);
                continue;
            }

            if (!string.IsNullOrEmpty(c.ResolvedFullName))
            {
                if (!byFullName.TryGetValue(c.ResolvedFullName, out var fList))
                    byFullName[c.ResolvedFullName] = fList = new List<ConsumerTarget>();
                fList.Add(c);
            }

            if (!bySimpleName.TryGetValue(c.SimpleName, out var sList))
                bySimpleName[c.SimpleName] = sList = new List<ConsumerTarget>();
            sList.Add(c);
        }

        foreach (var hit in context.GetPublishedEvents())
        {
            if (string.IsNullOrWhiteSpace(hit.ProjectName))
                continue;

            List<ConsumerTarget>? matched = null;
            if (hit.IsTopic)
            {
                byTopic.TryGetValue(hit.EventSimpleName, out matched);
            }
            else
            {
                if (!string.IsNullOrEmpty(hit.ResolvedFullName) &&
                    byFullName.TryGetValue(hit.ResolvedFullName, out var exactMatches) &&
                    exactMatches.Count > 0)
                {
                    matched = exactMatches;
                }
                else if (bySimpleName.TryGetValue(hit.EventSimpleName, out var simpleMatches) &&
                         simpleMatches.Count > 0)
                {
                    matched = simpleMatches;
                }
            }

            if (matched is null || matched.Count == 0)
                continue;

            var crossProject = matched
                .Where(c => !string.Equals(c.ProjectName, hit.ProjectName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var targets = crossProject.Count > 0
                ? crossProject
                : matched
                    .Where(c => !string.Equals(c.DeclFullName, hit.SenderFullName, StringComparison.Ordinal))
                    .ToList();

            foreach (var target in targets)
            {
                flows.Add(new IntegrationFlowEdge
                {
                    Kind = IntegrationFlowKind.IntegrationEvent,
                    FromProject = hit.ProjectName,
                    ToProject = target.ProjectName,
                    Label = hit.EventSimpleName,
                    MessageType = hit.ResolvedFullName ?? hit.EventSimpleName,
                    Location = hit.Location,
                });
            }
        }
    }

    private static List<ConsumerTarget> CollectConsumerTargets(SystemAnalysisContext context)
    {
        var list = new List<ConsumerTarget>();
        var seenDecl = new HashSet<(string Project, string DeclFullName, string Simple, bool IsTopic)>();

        foreach (var ep in context.Result.SystemEntrypoints)
        {
            if (ep.Kind != SystemEntrypointKind.MessageConsumer ||
                string.IsNullOrWhiteSpace(ep.ProjectName) ||
                string.IsNullOrWhiteSpace(ep.RouteOrTrigger))
            {
                continue;
            }

            var isTopic = ep.Name.Contains('.', StringComparison.Ordinal);
            var classSimple = isTopic ? ep.Name[..ep.Name.IndexOf('.')] : ep.Name;
            string declFullName = ep.Name;
            string? resolvedMsgFullName = null;

            if (!isTopic && context.ClassesBySimpleName.TryGetValue(classSimple, out var candidates))
            {
                var ownerDecl = candidates.FirstOrDefault(c =>
                    context.FileByClass.TryGetValue(c, out var entry) &&
                    string.Equals(entry.File.ProjectName, ep.ProjectName, StringComparison.OrdinalIgnoreCase));
                if (ownerDecl is not null)
                {
                    declFullName = ownerDecl.FullName;
                    resolvedMsgFullName = ResolveConsumedMessageFullName(context, ownerDecl, ep.RouteOrTrigger);
                }
            }

            if (seenDecl.Add((ep.ProjectName, declFullName, ep.RouteOrTrigger, isTopic)))
            {
                list.Add(new ConsumerTarget(
                    ep.ProjectName,
                    declFullName,
                    ep.RouteOrTrigger,
                    resolvedMsgFullName,
                    isTopic));
            }
        }

        return list;
    }

    private static string? ResolveConsumedMessageFullName(
        SystemAnalysisContext context,
        CSharpScanner.ClassDecl consumerDecl,
        string simpleTarget)
    {
        foreach (var root in ConsumerInterfaces)
        {
            foreach (var args in CSharpScanner.ExtractAllGenericArguments(consumerDecl.BaseList, root))
            {
                if (args.Count < 1)
                    continue;
                var (name, typeArgs) = CSharpScanner.ParseTypeReference(args[0]);
                if (!string.Equals(CSharpScanner.SimpleName(name), simpleTarget, StringComparison.Ordinal))
                    continue;
                var resolved = context.Hierarchy.TryResolveClass(
                    name, consumerDecl.Namespace, consumerDecl.Usings, consumerDecl.ContainingTypes, typeArgs.Count, out _);
                if (resolved is not null)
                    return resolved.FullName;
            }
        }

        return null;
    }

    private static Dictionary<string, string> DiscoverAspireReferenceFlows(
        SystemAnalysisContext context,
        List<IntegrationFlowEdge> flows)
    {
        var aspireResourceToProject = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (file, _, mask) in context.ParsedFiles)
        {
            if (context.IsTestProject(file.ProjectName))
                continue;
            if (!IsAspireFile(context, file))
                continue;

            var fileDir = Path.GetDirectoryName(file.Path) ?? context.Result.RootPath;
            var projectVars = new Dictionary<string, ProjectVar>(StringComparer.Ordinal);
            var infraVars = new Dictionary<string, InfraVar>(StringComparer.Ordinal);
            var resourceToInfra = new Dictionary<string, InfraVar>(StringComparer.OrdinalIgnoreCase);
            var resourceToProject = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // 1. Collect infrastructure resource declarations.
            try
            {
                foreach (Match m in AspireAddResourceRegex().Matches(file.Code))
                {
                    if (!mask.IsCode(m.Index))
                        continue;
                    var varName = m.Groups["var"].Value;
                    var receiver = m.Groups["receiver"].Value;
                    var kind = m.Groups["kind"].Value;
                    var resName = m.Groups["resName"].Value;

                    var tech = NormalizeAspireInfraTech(kind, receiver, infraVars);
                    var infra = new InfraVar($"{resName} ({tech})", resName, tech);
                    if (!string.IsNullOrEmpty(varName))
                    {
                        var stmtEnd = FindStatementEnd(file.Code, mask, m.Index + m.Length);
                        var chainTail = file.Code[(m.Index + m.Length)..stmtEnd];
                        Match? dbMatch = null;
                        if (!string.Equals(kind, "Database", StringComparison.Ordinal))
                        {
                            try
                            {
                                dbMatch = ChainedAddDatabaseRegex().Match(chainTail);
                            }
                            catch (RegexMatchTimeoutException)
                            {
                            }
                        }
                        if (dbMatch is { Success: true })
                        {
                            var dbName = dbMatch.Groups["dbName"].Value;
                            var dbInfra = new InfraVar($"{dbName} ({tech})", dbName, tech);
                            infraVars[varName] = dbInfra;
                            resourceToInfra.TryAdd(dbName, dbInfra);
                        }
                        else
                        {
                            infraVars[varName] = infra;
                        }
                    }
                    resourceToInfra.TryAdd(resName, infra);
                }
            }
            catch (RegexMatchTimeoutException)
            {
            }

            // 2. Collect AddProject declarations and inline chained .WithReference(...) calls.
            var projectCalls = new List<(int Index, string ProjectName, string ResourceName)>();
            try
            {
                foreach (Match m in AspireAddProjectRegex().Matches(file.Code))
                {
                    if (!mask.IsCode(m.Index))
                        continue;

                    var resName = m.Groups["resName"].Value;
                    string? resolvedProject = null;
                    if (m.Groups["projType"].Success && m.Groups["projType"].Value.Length > 0)
                        resolvedProject = ResolveAspireProjectType(context, m.Groups["projType"].Value);
                    else if (m.Groups["projPath"].Success && m.Groups["projPath"].Value.Length > 0)
                        resolvedProject = ResolveProjectByPath(context, fileDir, m.Groups["projPath"].Value);

                    if (string.IsNullOrEmpty(resolvedProject))
                        continue;

                    var varName = m.Groups["var"].Value;
                    if (!string.IsNullOrEmpty(varName))
                        projectVars[varName] = new ProjectVar(resolvedProject, resName);

                    resourceToProject.TryAdd(resName, resolvedProject);
                    aspireResourceToProject.TryAdd(resName, resolvedProject);
                    projectCalls.Add((m.Index, resolvedProject, resName));
                }
            }
            catch (RegexMatchTimeoutException)
            {
            }

            // 3. Scan every .WithReference(...) call and bind to its owning AddProject/variable.
            try
            {
                foreach (Match m in AspireWithReferenceRegex().Matches(file.Code))
                {
                    if (!mask.IsCode(m.Index))
                        continue;

                    var fromProject = ResolveWithReferenceOwner(
                        file.Code, mask, m.Index, projectVars, projectCalls);
                    if (string.IsNullOrEmpty(fromProject))
                        continue;

                    var argRaw = m.Groups["arg"].Value.Trim();
                    var location = SourceLocation.Create(file.Path, context.Result.RootPath, file.Code, m.Index);

                    if (LeadingStringLiteralRegex().Match(argRaw) is { Success: true } strMatch)
                    {
                        var refName = strMatch.Groups["name"].Value;
                        if (resourceToProject.TryGetValue(refName, out var targetProj) &&
                            !string.Equals(fromProject, targetProj, StringComparison.OrdinalIgnoreCase))
                        {
                            flows.Add(new IntegrationFlowEdge
                            {
                                Kind = IntegrationFlowKind.AspireReference,
                                FromProject = fromProject,
                                ToProject = targetProj,
                                Label = refName,
                                Location = location,
                            });
                        }
                        else if (resourceToInfra.TryGetValue(refName, out var targetInfra))
                        {
                            flows.Add(new IntegrationFlowEdge
                            {
                                Kind = IntegrationFlowKind.AspireReference,
                                FromProject = fromProject,
                                ToProject = targetInfra.DisplayName,
                                Label = targetInfra.ResourceName,
                                Location = location,
                            });
                        }
                        continue;
                    }

                    // Extract variable identifier (handling `targetApi.GetEndpoint("https")`).
                    var argIdent = ExtractLeadingIdentifier(argRaw);
                    if (string.IsNullOrEmpty(argIdent))
                        continue;

                    if (projectVars.TryGetValue(argIdent, out var targetProjectVar) &&
                        !string.Equals(fromProject, targetProjectVar.ProjectName, StringComparison.OrdinalIgnoreCase))
                    {
                        flows.Add(new IntegrationFlowEdge
                        {
                            Kind = IntegrationFlowKind.AspireReference,
                            FromProject = fromProject,
                            ToProject = targetProjectVar.ProjectName,
                            Label = targetProjectVar.ResourceName,
                            Location = location,
                        });
                    }
                    else if (infraVars.TryGetValue(argIdent, out var targetInfraVar))
                    {
                        flows.Add(new IntegrationFlowEdge
                        {
                            Kind = IntegrationFlowKind.AspireReference,
                            FromProject = fromProject,
                            ToProject = targetInfraVar.DisplayName,
                            Label = targetInfraVar.ResourceName,
                            Location = location,
                        });
                    }
                }
            }
            catch (RegexMatchTimeoutException)
            {
            }
        }

        return aspireResourceToProject;
    }

    private static string? ResolveWithReferenceOwner(
        string code,
        SourceMask mask,
        int withRefIndex,
        Dictionary<string, ProjectVar> projectVars,
        List<(int Index, string ProjectName, string ResourceName)> projectCalls)
    {
        var stmtStart = 0;
        for (var i = withRefIndex - 1; i >= 0; i--)
        {
            if (mask.IsCode(i) && code[i] is ';' or '{' or '}')
            {
                stmtStart = i + 1;
                break;
            }
        }

        // Inline fluent chain on AddProject<...>(...)
        foreach (var (idx, projName, _) in projectCalls)
        {
            if (idx >= stmtStart && idx < withRefIndex)
                return projName;
        }

        // Separate statement on a project variable: `webApp.WithReference(catalogApi);`
        var prefix = mask.CodeOnly(stmtStart, withRefIndex - stmtStart).TrimStart();
        var rootIdent = ExtractLeadingIdentifier(prefix);
        if (!string.IsNullOrEmpty(rootIdent) &&
            projectVars.TryGetValue(rootIdent, out var pv))
        {
            return pv.ProjectName;
        }

        return null;
    }

    private static void DiscoverClientFlows(
        SystemAnalysisContext context,
        Dictionary<string, string> aspireResourceToProject,
        List<IntegrationFlowEdge> flows)
    {
        var uniqueProjectsByNormalized = BuildUniqueNormalizedProjectMap(context);
        var uniqueGrpcServiceByStem = BuildUniqueGrpcServiceProjectMap(context);

        foreach (var (file, scan, mask) in context.ParsedFiles)
        {
            if (context.IsTestProject(file.ProjectName) || string.IsNullOrWhiteSpace(file.ProjectName))
                continue;

            var fromProject = file.ProjectName;
            if (!file.Code.Contains("AddHttpClient", StringComparison.Ordinal) &&
                !file.Code.Contains("AddGrpcClient", StringComparison.Ordinal))
                continue;

            MatchCollection matches;
            try
            {
                matches = ClientRegistrationRegex().Matches(file.Code);
            }
            catch (RegexMatchTimeoutException)
            {
                continue;
            }

            foreach (Match match in matches)
            {
                if (!mask.IsCode(match.Index))
                    continue;

                var kindStr = match.Groups["kind"].Value;
                var isGrpc = string.Equals(kindStr, "AddGrpcClient", StringComparison.Ordinal);
                var flowKind = isGrpc ? IntegrationFlowKind.GrpcClient : IntegrationFlowKind.HttpClient;

                var openParen = match.Index + match.Length - 1;
                var closeParen = DeepSliceSyntax.FindMatchingParen(file.Code, mask, openParen);
                if (closeParen < 0)
                    continue;

                var chainEnd = FindStatementEnd(file.Code, mask, closeParen + 1);
                var fullCallText = file.Code[openParen..chainEnd];
                var innerArgs = file.Code[(openParen + 1)..closeParen];

                var typeArgs = match.Groups["targs"].Success
                    ? CSharpScanner.SplitTopLevel(match.Groups["targs"].Value)
                    : new List<string>();
                var clientTypeWritten = typeArgs.Count > 0 ? typeArgs[^1] : null;
                var clientSimple = clientTypeWritten is not null
                    ? CSharpScanner.UnqualifiedTypeName(clientTypeWritten)
                    : null;

                string? namedClient = null;
                if (typeArgs.Count == 0)
                {
                    try
                    {
                        var strMatch = LeadingStringLiteralRegex().Match(innerArgs.TrimStart());
                        if (strMatch.Success)
                            namedClient = strMatch.Groups["name"].Value;
                    }
                    catch (RegexMatchTimeoutException)
                    {
                    }
                }

                var currentNs = DeepSliceSyntax.InnermostClass(scan.Classes, match.Index)?.Namespace ?? scan.Namespace;
                var targetProject = ResolveClientTargetProject(
                    context,
                    fromProject,
                    isGrpc,
                    typeArgs,
                    namedClient,
                    fullCallText,
                    currentNs,
                    scan.Usings,
                    aspireResourceToProject,
                    uniqueProjectsByNormalized,
                    uniqueGrpcServiceByStem);

                if (string.IsNullOrEmpty(targetProject) ||
                    string.Equals(targetProject, fromProject, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var label = clientSimple ?? namedClient ?? targetProject;
                flows.Add(new IntegrationFlowEdge
                {
                    Kind = flowKind,
                    FromProject = fromProject,
                    ToProject = targetProject,
                    Label = label,
                    MessageType = clientTypeWritten,
                    Location = SourceLocation.Create(file.Path, context.Result.RootPath, file.Code, match.Index),
                });
            }
        }
    }

    private static string? ResolveClientTargetProject(
        SystemAnalysisContext context,
        string fromProject,
        bool isGrpc,
        List<string> typeArgs,
        string? namedClient,
        string fullCallText,
        string currentNs,
        IReadOnlyList<UsingDirective> usings,
        Dictionary<string, string> aspireResourceToProject,
        Dictionary<string, string?> uniqueProjectsByNormalized,
        Dictionary<string, string?> uniqueGrpcServiceByStem)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void TryAddHostOrName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || LoopbackOrPlaceholderHosts.Contains(name))
                return;

            if (aspireResourceToProject.TryGetValue(name, out var aspireTarget) &&
                !string.Equals(aspireTarget, fromProject, StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(aspireTarget);
                return;
            }

            if (context.ProjectsByName.TryGetValue(name, out var exact) &&
                !context.IsTestProject(exact.Name) &&
                !string.Equals(exact.Name, fromProject, StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(exact.Name);
                return;
            }

            var norm = NormalizeProjectKey(name);
            if (norm.Length > 0 &&
                uniqueProjectsByNormalized.TryGetValue(norm, out var uniqueProj) &&
                uniqueProj is not null &&
                !string.Equals(uniqueProj, fromProject, StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(uniqueProj);
            }
        }

        // 1. Explicit BaseAddress / Address URI in configuration lambda
        try
        {
            foreach (Match uriMatch in ServiceUriRegex().Matches(fullCallText))
            {
                TryAddHostOrName(uriMatch.Groups["host"].Value);
            }
        }
        catch (RegexMatchTimeoutException)
        {
        }

        if (candidates.Count == 1)
            return candidates.First();
        if (candidates.Count > 1)
            return null;

        // 2. Named client string (`AddHttpClient("catalog-api")`)
        if (!string.IsNullOrWhiteSpace(namedClient))
        {
            TryAddHostOrName(namedClient);
            if (candidates.Count == 1)
                return candidates.First();
            if (candidates.Count > 1)
                return null;
        }

        // 3. gRPC client `Basket.BasketClient` -> unique gRPC service `Basket.BasketBase` in another project
        if (isGrpc && typeArgs.Count > 0)
        {
            var (name, _) = CSharpScanner.ParseTypeReference(typeArgs[^1]);
            var simple = CSharpScanner.SimpleName(name);
            var ns = CSharpScanner.NamespaceOfType(name);
            foreach (var rawStem in new[]
                     {
                         StripSuffix(simple, "Client"),
                         ns.Length > 0 ? CSharpScanner.SimpleName(ns) : string.Empty,
                     })
            {
                if (rawStem.Length > 0 &&
                    uniqueGrpcServiceByStem.TryGetValue(rawStem, out var grpcProject) &&
                    grpcProject is not null &&
                    !string.Equals(grpcProject, fromProject, StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(grpcProject);
                }
            }

            if (candidates.Count == 1)
                return candidates.First();
            if (candidates.Count > 1)
                return null;
        }

        // 4. Typed HTTP client class/interface declared inside another non-test project
        if (!isGrpc && typeArgs.Count > 0)
        {
            foreach (var rawType in typeArgs)
            {
                var (name, args) = CSharpScanner.ParseTypeReference(rawType);
                var decl = context.Hierarchy.TryResolveClass(
                    name, currentNs, usings, Array.Empty<string>(), args.Count, out _);
                if (decl is not null &&
                    context.FileByClass.TryGetValue(decl, out var declEntry) &&
                    !context.IsTestProject(declEntry.File.ProjectName) &&
                    !string.IsNullOrEmpty(declEntry.File.ProjectName) &&
                    !string.Equals(declEntry.File.ProjectName, fromProject, StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(declEntry.File.ProjectName);
                }
            }

            if (candidates.Count == 1)
                return candidates.First();
        }

        return null;
    }

    private static void DiscoverSharedDbContextFlows(
        SystemAnalysisContext context,
        List<IntegrationFlowEdge> flows)
    {
        if (context.Result.DbContexts.Count == 0)
            return;

        // Pre-index projects using each DbContext from entrypoints + CQRS flow/resolved edges.
        var usageByDbContext = new Dictionary<string, Dictionary<string, SourceLocation?>>(StringComparer.Ordinal);

        void RecordUsage(string dbContextFullName, string? projectName, SourceLocation? location)
        {
            if (string.IsNullOrWhiteSpace(projectName) || context.IsTestProject(projectName))
                return;
            if (!usageByDbContext.TryGetValue(dbContextFullName, out var projMap))
                usageByDbContext[dbContextFullName] = projMap = new Dictionary<string, SourceLocation?>(StringComparer.OrdinalIgnoreCase);
            projMap.TryAdd(projectName, location);
        }

        foreach (var ep in context.Result.SystemEntrypoints)
        {
            if (ep.ExecutionStyle == SystemExecutionStyle.Cqrs)
                continue;
            foreach (var ctxFull in ep.TouchedDbContexts)
                RecordUsage(ctxFull, ep.ProjectName, ep.Location);
        }

        foreach (var edge in context.Result.FlowEdges)
        {
            if (edge.Kind != FlowEdgeKind.HandlerToDbContext)
                continue;
            if (context.ClassByFullName.TryGetValue(edge.From, out var fromDecl) &&
                context.FileByClass.TryGetValue(fromDecl, out var fromEntry))
            {
                RecordUsage(edge.To, fromEntry.File.ProjectName,
                    SourceLocation.Create(fromEntry.File.Path, context.Result.RootPath, fromEntry.File.Code, fromDecl.NameIndex));
            }
        }

        foreach (var edge in context.Result.ResolvedEdges)
        {
            if (edge.Kind != FlowEdgeKind.HandlerToDbContextResolved)
                continue;
            if (context.ClassByFullName.TryGetValue(edge.From, out var fromDecl) &&
                context.FileByClass.TryGetValue(fromDecl, out var fromEntry))
            {
                RecordUsage(edge.To, fromEntry.File.ProjectName,
                    SourceLocation.Create(fromEntry.File.Path, context.Result.RootPath, fromEntry.File.Code, fromDecl.NameIndex));
            }
        }

        // Also scan concrete non-DbContext, non-Migration, non-DesignTimeFactory classes for direct DbContext usage.
        foreach (var (file, scan, mask) in context.ParsedFiles)
        {
            if (context.IsTestProject(file.ProjectName) || string.IsNullOrWhiteSpace(file.ProjectName))
                continue;

            foreach (var decl in scan.Classes)
            {
                if (decl.IsAbstract || context.DbContextsByFullName.ContainsKey(decl.FullName))
                    continue;
                if (decl.BaseRawNames.Any(b => b is "Migration" || b.StartsWith("IDesignTimeDbContextFactory", StringComparison.Ordinal)))
                    continue;

                var deps = new List<string>();
                DependencyExtractor.Extract(decl, file.Code, mask, scan.Classes, deps);
                foreach (var prop in decl.Properties)
                {
                    if (prop.Type.Length > 0 && !deps.Contains(prop.Type, StringComparer.Ordinal))
                        deps.Add(prop.Type);
                }
                foreach (var dep in deps)
                {
                    var (name, args) = CSharpScanner.ParseTypeReference(dep);
                    var simple = CSharpScanner.SimpleName(name);
                    if (simple is "IDbContextFactory" or "DbContextFactory" && args.Count == 1)
                    {
                        var innerCtx = SystemDependencyTracer.ResolveConcreteDbContext(
                            context, args[0], decl.Namespace, decl.Usings, decl.ContainingTypes);
                        if (innerCtx is not null)
                        {
                            RecordUsage(innerCtx.FullName, file.ProjectName,
                                SourceLocation.Create(file.Path, context.Result.RootPath, file.Code, decl.NameIndex));
                        }
                        continue;
                    }

                    var directCtx = SystemDependencyTracer.ResolveConcreteDbContext(
                        context, dep, decl.Namespace, decl.Usings, decl.ContainingTypes);
                    if (directCtx is not null)
                    {
                        RecordUsage(directCtx.FullName, file.ProjectName,
                            SourceLocation.Create(file.Path, context.Result.RootPath, file.Code, decl.NameIndex));
                        continue;
                    }

                    var sr = SystemDependencyTracer.LookupResolution(
                        context, dep, decl.Namespace, decl.Usings, decl.ContainingTypes);
                    if (sr?.Implementation is not null &&
                        context.DbContextsByFullName.ContainsKey(sr.Implementation))
                    {
                        RecordUsage(sr.Implementation, file.ProjectName,
                            SourceLocation.Create(file.Path, context.Result.RootPath, file.Code, decl.NameIndex));
                    }
                }
            }
        }

        // Emit edges when >= 2 distinct non-test projects reference and use the same DbContext.
        foreach (var ctx in context.Result.DbContexts)
        {
            if (string.IsNullOrWhiteSpace(ctx.ProjectName) || context.IsTestProject(ctx.ProjectName))
                continue;
            if (!usageByDbContext.TryGetValue(ctx.FullName, out var usersByProject))
                continue;

            var validUsers = new List<(string Project, SourceLocation? Location)>();
            foreach (var (projName, loc) in usersByProject)
            {
                if (!context.ProjectsByName.TryGetValue(projName, out var projInfo))
                    continue;

                var referencesContextProject =
                    string.Equals(projInfo.Name, ctx.ProjectName, StringComparison.OrdinalIgnoreCase) ||
                    projInfo.ReferenceNames.Contains(ctx.ProjectName, StringComparer.OrdinalIgnoreCase);

                if (referencesContextProject)
                    validUsers.Add((projInfo.Name, loc));
            }

            if (validUsers.Count < 2)
                continue;

            foreach (var (userProject, usageLoc) in validUsers)
            {
                if (string.Equals(userProject, ctx.ProjectName, StringComparison.OrdinalIgnoreCase))
                    continue;

                flows.Add(new IntegrationFlowEdge
                {
                    Kind = IntegrationFlowKind.SharedDbContext,
                    FromProject = userProject,
                    ToProject = ctx.ProjectName,
                    Label = ctx.Name,
                    MessageType = ctx.FullName,
                    Location = usageLoc,
                });
            }
        }
    }

    private static bool IsAspireFile(SystemAnalysisContext context, SourceFile file)
    {
        if (!string.IsNullOrEmpty(file.ProjectName) &&
            context.ProjectsByName.TryGetValue(file.ProjectName, out var proj))
        {
            if ((!string.IsNullOrEmpty(proj.Sdk) && proj.Sdk.Contains("Aspire", StringComparison.OrdinalIgnoreCase)) ||
                proj.PackageReferences.Any(p => p.StartsWith("Aspire.Hosting", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return file.Code.Contains("DistributedApplication.CreateBuilder", StringComparison.Ordinal) ||
               file.Code.Contains("AddProject<", StringComparison.Ordinal) ||
               file.Code.Contains("AddDotnetProject", StringComparison.Ordinal) ||
               file.Code.Contains("AddProject(", StringComparison.Ordinal);
    }

    private static string NormalizeAspireInfraTech(
        string kind,
        string receiver,
        Dictionary<string, InfraVar> infraVars)
    {
        if (string.Equals(kind, "Database", StringComparison.Ordinal))
        {
            return infraVars.TryGetValue(receiver, out var parent)
                ? parent.Tech
                : "Database";
        }

        return kind switch
        {
            "Postgres" or "PostgresDatabase" or "Npgsql" => "PostgreSQL",
            "SqlServer" => "SQL Server",
            "MySql" => "MySQL",
            "Sqlite" => "SQLite",
            "MongoDB" => "MongoDB",
            "Redis" or "Garnet" or "Valkey" => kind,
            "RabbitMQ" => "RabbitMQ",
            "Kafka" => "Kafka",
            "AzureServiceBus" or "ServiceBus" => "Azure Service Bus",
            "AzureStorage" or "Storage" or "AzureBlobStorage" or "AzureQueueStorage" => "Azure Storage",
            "AzureCosmosDB" or "CosmosDB" => "Cosmos DB",
            _ => kind,
        };
    }

    private static string? ResolveAspireProjectType(SystemAnalysisContext context, string rawType)
    {
        var type = rawType.Trim();
        if (type.StartsWith("global::", StringComparison.Ordinal))
            type = type[8..];
        if (type.StartsWith("Projects.", StringComparison.Ordinal))
            type = type["Projects.".Length..];
        else
            type = CSharpScanner.SimpleName(type);

        // 1. Exact project name match
        if (context.ProjectsByName.TryGetValue(type, out var exact))
            return exact.Name;

        // 2. Aspire MSBuild Projects.<Name_With_Underscores> match (unambiguous)
        var normalizedTarget = type.Replace('.', '_').Replace('-', '_');
        var matches = context.Result.Projects
            .Where(p => string.Equals(
                p.Name.Replace('.', '_').Replace('-', '_').Replace(' ', '_'),
                normalizedTarget,
                StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return matches.Count == 1 ? matches[0] : null;
    }

    private static string? ResolveProjectByPath(
        SystemAnalysisContext context,
        string baseDirectory,
        string relativeProjectPath)
    {
        if (ProjectReferenceResolver.ResolvePath(baseDirectory, relativeProjectPath) is not { } fullTarget)
            return null;

        var trimmedFullTarget = Path.TrimEndingDirectorySeparator(fullTarget);
        var matched = context.Result.Projects
            .FirstOrDefault(p =>
                string.Equals(Path.GetFullPath(p.FilePath), trimmedFullTarget, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    Path.TrimEndingDirectorySeparator(Path.GetFullPath(p.DirectoryPath)),
                    trimmedFullTarget,
                    StringComparison.OrdinalIgnoreCase));
        if (matched is not null)
            return matched.Name;

        // Directory paths like "../Catalog.API" keep their dot suffix; only strip ".csproj".
        var leaf = Path.GetFileName(trimmedFullTarget);
        var stem = leaf.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(leaf)
            : leaf;
        return context.ProjectsByName.TryGetValue(stem, out var byName)
            ? byName.Name
            : null;
    }

    private static Dictionary<string, string?> BuildUniqueNormalizedProjectMap(SystemAnalysisContext context)
    {
        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in context.Result.Projects)
        {
            if (context.IsTestProject(p.Name))
                continue;
            var key = NormalizeProjectKey(p.Name);
            if (key.Length == 0)
                continue;
            if (!map.TryGetValue(key, out var existing))
                map[key] = p.Name;
            else if (existing is not null && !string.Equals(existing, p.Name, StringComparison.OrdinalIgnoreCase))
                map[key] = null;
        }
        return map;
    }

    private static Dictionary<string, string?> BuildUniqueGrpcServiceProjectMap(SystemAnalysisContext context)
    {
        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        void AddStem(string stem, string projectName)
        {
            if (string.IsNullOrEmpty(stem))
                return;
            if (!map.TryGetValue(stem, out var existing))
                map[stem] = projectName;
            else if (existing is not null && !string.Equals(existing, projectName, StringComparison.OrdinalIgnoreCase))
                map[stem] = null;
        }

        foreach (var ep in context.Result.SystemEntrypoints)
        {
            if (ep.Kind != SystemEntrypointKind.Grpc || string.IsNullOrWhiteSpace(ep.ProjectName))
                continue;

            var className = ep.RouteOrTrigger.Contains('.', StringComparison.Ordinal)
                ? ep.RouteOrTrigger[..ep.RouteOrTrigger.IndexOf('.')]
                : ep.Name;

            AddStem(StripSuffix(className, "Service"), ep.ProjectName);
            AddStem(className, ep.ProjectName);

            if (context.ClassesBySimpleName.TryGetValue(className, out var decls))
            {
                foreach (var decl in decls)
                {
                    foreach (var bt in decl.BaseTypeNames)
                    {
                        var simpleBase = CSharpScanner.SimpleName(bt);
                        if (simpleBase.EndsWith("Base", StringComparison.Ordinal))
                            AddStem(StripSuffix(simpleBase, "Base"), ep.ProjectName);
                    }
                }
            }
        }

        return map;
    }

    private static string NormalizeProjectKey(string name)
        => name.Replace(".", string.Empty, StringComparison.Ordinal)
               .Replace("-", string.Empty, StringComparison.Ordinal)
               .Replace("_", string.Empty, StringComparison.Ordinal)
               .Replace(" ", string.Empty, StringComparison.Ordinal);

    private static string StripSuffix(string value, string suffix)
        => value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && value.Length > suffix.Length
            ? value[..^suffix.Length]
            : value;

    private static string ExtractLeadingIdentifier(string text)
    {
        var s = text.TrimStart();
        var end = 0;
        while (end < s.Length && (char.IsLetterOrDigit(s[end]) || s[end] == '_'))
            end++;
        return end > 0 && !char.IsDigit(s[0]) ? s[..end] : string.Empty;
    }

    private static int FindStatementEnd(string code, SourceMask mask, int start)
    {
        var braceDepth = 0;
        for (var i = start; i < code.Length; i++)
        {
            if (!mask.IsCode(i))
                continue;
            var c = code[i];
            if (c == '{')
                braceDepth++;
            else if (c == '}')
            {
                if (braceDepth == 0)
                    return i;
                braceDepth--;
            }
            else if (c == ';' && braceDepth == 0)
                return i;
        }
        return code.Length;
    }
}
