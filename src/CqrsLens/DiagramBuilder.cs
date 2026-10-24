using System.Text;

namespace CqrsLens;

/// <summary>Builds the Mermaid diagram sources shared by the Markdown and HTML reports.</summary>
public static class DiagramBuilder
{
    /// <summary>
    /// Maximum requests in one CQRS diagram. The accurate request-only subgraph
    /// layout is roughly 100 px per request on GitHub; 15 keeps each part under
    /// about 1,600 px while staying far below Mermaid's 50,000-character limit.
    /// </summary>
    internal const int MaxRequestsPerCqrsDiagram = 15;

    // GitHub's Mermaid configuration uses maxTextSize: 50000 and maxEdges: 500.
    // Leave headroom for the renderer's own preprocessing.
    private const int MaxMermaidTextLength = 45_000;
    private const int MaxMermaidEdges = 400;
    private const string UnassignedProject = "(unassigned)";

    /// <summary>
    /// Mermaid keywords that cannot be used as a bare node or subgraph id. The
    /// builders additionally role-prefix every id (req_, hdl_, val_, prj_, lyr_),
    /// so this set only protects callers of the public helper.
    /// </summary>
    private static readonly HashSet<string> ReservedMermaidIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "end", "graph", "flowchart", "subgraph", "style", "linkStyle",
        "class", "classDef", "click", "default", "direction",
        "accTitle", "accDescr", "accDescrMultiline", "call", "href",
    };

    /// <summary>
    /// Makes an ASCII-only, Mermaid-safe base id from an arbitrary name. Reserved
    /// words are prefixed with <c>n_</c>; diagram builders add role prefixes.
    /// </summary>
    public static string MermaidId(string? name)
    {
        var id = SanitizeMermaidId(name);
        return ReservedMermaidIds.Contains(id) ? "n_" + id : id;
    }

    /// <summary>
    /// Escapes text used inside a quoted Mermaid label. GitHub HTML-decodes its
    /// input before Mermaid sees it, so HTML entities such as <c>&amp;quot;</c>
    /// are unsafe. Mermaid's own <c>#...;</c> entities are decoded exactly once.
    /// The label must not be able to terminate its quote or inject a new line.
    /// </summary>
    public static string MermaidLabel(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        var sb = new StringBuilder(value.Length);
        foreach (var c in value.ReplaceLineEndings(" "))
        {
            switch (c)
            {
                case '&':
                    sb.Append("#38;");
                    break;
                case '"':
                    sb.Append("#quot;");
                    break;
                case '<':
                    sb.Append("#lt;");
                    break;
                case '>':
                    sb.Append("#gt;");
                    break;
                case '|':
                    sb.Append("#124;");
                    break;
                case '#':
                    sb.Append("#35;");
                    break;
                default:
                    sb.Append(char.IsControl(c) ? ' ' : c);
                    break;
            }
        }

        return sb.ToString();
    }

    public static string? ProjectGraph(AnalysisResult result)
    {
        if (result.Projects.Count == 0) return null;

        var projects = OrderedProjects(result);
        var labels = ProjectLabels(result, projects);
        var projectsByName = UniqueProjectsByName(projects);
        var referenceCount = ResolvedReferenceCount(projects, projectsByName);
        var violations = LayerViolationPairs(result.Checks);
        var ids = new MermaidIdAllocator();
        var projectIds = projects.ToDictionary(
            p => p.FilePath,
            p => ids.Allocate("project:" + p.FilePath, "prj", labels[p.FilePath]),
            StringComparer.Ordinal);

        var sb = new StringBuilder();
        sb.AppendLine("flowchart TD");
        sb.AppendLine("    accTitle: Project dependency graph");
        sb.AppendLine(
            $"    accDescr: {projects.Count} projects, {referenceCount} references.");
        foreach (var project in projects)
            sb.AppendLine(
                $"    {projectIds[project.FilePath]}[\"{MermaidLabel(labels[project.FilePath])}\"]" +
                $":::{LayerClass(project.Layer)}");

        var linkIndexes = EmitProjectEdges(sb, projects, projectsByName, projectIds, violations);
        EmitLinkStyles(sb, linkIndexes);
        EmitLayerClassDefs(sb);
        return sb.ToString().TrimEnd();
    }

    public static string? LayerGraph(AnalysisResult result)
    {
        if (result.Projects.Count == 0) return null;

        var projects = OrderedProjects(result);
        var labels = ProjectLabels(result, projects);
        var projectsByName = UniqueProjectsByName(projects);
        var referenceCount = ResolvedReferenceCount(projects, projectsByName);
        var violations = LayerViolationPairs(result.Checks);
        var projectsByLayer = projects
            .GroupBy(p => p.Layer, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var layers = LayerClassifier.OrderLayers(projects.Select(p => p.Layer)).ToList();
        var ids = new MermaidIdAllocator();
        var projectIds = projects.ToDictionary(
            p => p.FilePath,
            p => ids.Allocate("project:" + p.FilePath, "prj", labels[p.FilePath]),
            StringComparer.Ordinal);
        var layerIds = layers.ToDictionary(
            layer => layer,
            layer => ids.Allocate("layer:" + layer, "lyr", layer),
            StringComparer.Ordinal);

        var sb = new StringBuilder();
        sb.AppendLine("flowchart TD");
        sb.AppendLine("    accTitle: Layer diagram");
        sb.AppendLine(
            $"    accDescr: {projects.Count} projects, {layers.Count} layers, " +
            $"{referenceCount} references.");
        foreach (var layer in layers)
        {
            sb.AppendLine($"    subgraph {layerIds[layer]}[\"{MermaidLabel(layer)}\"]");
            foreach (var project in projectsByLayer[layer])
                sb.AppendLine(
                    $"        {projectIds[project.FilePath]}[\"{MermaidLabel(labels[project.FilePath])}\"]" +
                    $":::{LayerClass(project.Layer)}");
            sb.AppendLine("    end");
        }

        var linkIndexes = EmitProjectEdges(sb, projects, projectsByName, projectIds, violations);
        foreach (var layer in layers)
            sb.AppendLine(
                $"    style {layerIds[layer]} fill:none,stroke:#94a3b8,stroke-dasharray:4 3");
        EmitLinkStyles(sb, linkIndexes);
        EmitLayerClassDefs(sb);
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Legacy single-diagram helper. Prefer <see cref="CqrsMapSections"/> for
    /// reports: large solutions are split into multiple bounded diagrams.
    /// </summary>
    public static string? CqrsMap(AnalysisResult result)
    {
        var sections = CqrsMapSections(result);
        return sections.Count > 0 ? sections[0].Source : null;
    }

    /// <summary>
    /// Builds one CQRS map for small solutions, or splits larger solutions by
    /// project and then into balanced parts. Every request appears exactly once.
    /// </summary>
    public static IReadOnlyList<DiagramSection> CqrsMapSections(AnalysisResult result)
    {
        if (result.Requests.Count == 0)
            return Array.Empty<DiagramSection>();

        var missingHandlers = new HashSet<string>(StringComparer.Ordinal);
        var missingValidators = new HashSet<string>(StringComparer.Ordinal);
        foreach (var check in result.Checks)
        {
            if (check.Target is null) continue;
            if (check.Code == ArchitectureChecker.MissingHandlerCode)
                missingHandlers.Add(check.Target);
            else if (check.Code == ArchitectureChecker.CommandWithoutValidatorCode)
                missingValidators.Add(check.Target);
        }

        var groups = result.Requests
            .GroupBy(ProjectKey, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new
            {
                Project = g.Key,
                Requests = OrderRequests(g),
            })
            .ToList();

        if (result.Requests.Count <= MaxRequestsPerCqrsDiagram)
        {
            var requests = groups.SelectMany(g => g.Requests).ToList();
            var stats = GetStats(requests, missingHandlers, missingValidators);
            var source = BuildCqrsSource(requests, missingHandlers, missingValidators, stats);
            var projectName = groups.Count == 1 && groups[0].Project != UnassignedProject
                ? groups[0].Project
                : null;
            return new[]
            {
                new DiagramSection(
                    projectName,
                    requests,
                    source,
                    Part: 1,
                    PartCount: 1,
                    ProjectRequestCount: requests.Count,
                    stats.Handlers,
                    stats.Validators,
                    stats.MissingHandlers,
                    stats.MissingValidators),
            };
        }

        var sections = new List<DiagramSection>();
        foreach (var group in groups)
        {
            var built = new List<(List<CqrsRequest> Requests, string Source, CqrsStats Stats)>();
            foreach (var chunk in BalancedChunks(group.Requests))
                built.AddRange(BuildBounded(chunk, missingHandlers, missingValidators));

            for (var i = 0; i < built.Count; i++)
            {
                var part = built[i];
                sections.Add(new DiagramSection(
                    group.Project == UnassignedProject ? null : group.Project,
                    part.Requests,
                    part.Source,
                    Part: i + 1,
                    PartCount: built.Count,
                    ProjectRequestCount: group.Requests.Count,
                    part.Stats.Handlers,
                    part.Stats.Validators,
                    part.Stats.MissingHandlers,
                    part.Stats.MissingValidators));
            }
        }

        return sections;
    }

    /// <summary>Whether a Mermaid source is safely inside GitHub's renderer limits.</summary>
    internal static bool IsWithinMermaidLimits(string source)
    {
        if (source.Length > MaxMermaidTextLength)
            return false;

        var edges = source
            .Split('\n')
            .Count(IsMermaidEdgeLine);
        return edges <= MaxMermaidEdges;
    }

    private static bool IsMermaidEdgeLine(string line)
        => line.Contains("-->", StringComparison.Ordinal) ||
           line.Contains("-.->", StringComparison.Ordinal) ||
           line.Contains("||--", StringComparison.Ordinal) ||
           line.Contains("|o--", StringComparison.Ordinal) ||
           line.Contains("}o--", StringComparison.Ordinal);

    /// <summary>
    /// Builds a Mermaid <c>erDiagram</c> for one <see cref="DbContextInfo"/>,
    /// or returns <see langword="null"/> when the context has no discovered entities.
    /// </summary>
    public static string? ErDiagram(DbContextInfo context)
    {
        if (context.Entities.Count == 0)
            return null;

        var orderedEntities = context.Entities
            .OrderBy(e => e.FullName, StringComparer.Ordinal)
            .ToList();
        var nameCounts = orderedEntities
            .GroupBy(e => e.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var ids = new MermaidIdAllocator();
        var entityIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var entityLabels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entity in orderedEntities)
        {
            var label = nameCounts[entity.Name] > 1 ? entity.FullName : entity.Name;
            entityLabels[entity.FullName] = label;
            entityIds[entity.FullName] = ids.Allocate("entity:" + entity.FullName, "ent", label);
        }

        var orderedRelationships = context.Relationships
            .Where(r => entityIds.ContainsKey(r.PrincipalEntity) &&
                        entityIds.ContainsKey(r.DependentEntity))
            .OrderBy(r => r.PrincipalEntity, StringComparer.Ordinal)
            .ThenBy(r => r.DependentEntity, StringComparer.Ordinal)
            .ThenBy(r => r.PrincipalNavigation, StringComparer.Ordinal)
            .ThenBy(r => r.DependentNavigation, StringComparer.Ordinal)
            .ToList();

        var entityText = orderedEntities.Count == 1 ? "1 entity" : $"{orderedEntities.Count} entities";
        var relationshipText = CountText(orderedRelationships.Count, "relationship");

        var sb = new StringBuilder();
        sb.AppendLine("erDiagram");
        sb.AppendLine($"    accTitle: {MermaidLabel(context.Name)} ER diagram");
        sb.AppendLine($"    accDescr: {entityText}, {relationshipText}.");

        foreach (var entity in orderedEntities)
        {
            var id = entityIds[entity.FullName];
            var label = MermaidLabel(entityLabels[entity.FullName]);
            var scalars = entity.Properties
                .Where(p => !p.IsNavigation)
                .OrderBy(p => p.IsKey ? 0 : 1)
                .ThenBy(p => p.Name, StringComparer.Ordinal)
                .ToList();

            if (scalars.Count == 0)
            {
                sb.AppendLine($"    {id}[\"{label}\"]");
                continue;
            }

            sb.AppendLine($"    {id}[\"{label}\"] {{");
            foreach (var property in scalars)
            {
                var attrType = ErAttributeType(property.Type);
                var attrName = SanitizeMermaidId(property.Name);
                var keySuffix = (property.IsKey, property.IsForeignKey) switch
                {
                    (true, true) => " PK, FK",
                    (true, false) => " PK",
                    (false, true) => " FK",
                    _ => "",
                };
                sb.AppendLine($"        {attrType} {attrName}{keySuffix}");
            }
            sb.AppendLine("    }");
        }

        foreach (var relationship in orderedRelationships)
        {
            var principalId = entityIds[relationship.PrincipalEntity];
            var dependentId = entityIds[relationship.DependentEntity];
            var connector = ErConnector(relationship);
            var label = MermaidLabel(ErRelationshipLabel(relationship));
            sb.AppendLine($"    {principalId} {connector} {dependentId} : \"{label}\"");
        }

        return sb.ToString().TrimEnd();
    }

    private static string ErAttributeType(string? type)
    {
        var normalized = EfAnnotations.NormalizeType(type);
        if (normalized.StartsWith("global::", StringComparison.Ordinal))
            normalized = normalized[8..];

        var openCount = normalized.Count(c => c == '<');
        var closeCount = normalized.Count(c => c == '>');
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if ((c == '<' || c == '>') && openCount == 1 && closeCount == 1)
            {
                sb.Append('~');
            }
            else if (c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9'
                     or '_' or '.' or '[' or ']' or '?')
            {
                sb.Append(c);
            }
            else
            {
                sb.Append('_');
            }
        }

        var result = sb.ToString().Trim('_');
        if (result.Length == 0)
            return "object";
        if (!char.IsLetter(result[0]) && result[0] != '_')
            return "t_" + result;
        return result;
    }

    private static string ErConnector(EntityRelationshipInfo relationship)
        => relationship.Kind switch
        {
            EntityRelationshipKind.ManyToMany => "}o--o{",
            EntityRelationshipKind.OneToOne => relationship.IsRequired ? "||--o|" : "|o--o|",
            _ => relationship.IsRequired ? "||--o{" : "|o--o{",
        };

    private static string ErRelationshipLabel(EntityRelationshipInfo relationship)
    {
        var principalNav = relationship.PrincipalNavigation;
        var dependentNav = relationship.DependentNavigation;
        if (!string.IsNullOrEmpty(principalNav) && !string.IsNullOrEmpty(dependentNav))
        {
            return string.Equals(principalNav, dependentNav, StringComparison.Ordinal)
                ? principalNav
                : $"{principalNav} / {dependentNav}";
        }

        if (!string.IsNullOrEmpty(principalNav))
            return principalNav;
        if (!string.IsNullOrEmpty(dependentNav))
            return dependentNav;
        if (!string.IsNullOrEmpty(relationship.ForeignKey))
            return relationship.ForeignKey;
        return "has";
    }

    private static string BuildCqrsSource(
        IReadOnlyList<CqrsRequest> requests,
        HashSet<string> missingHandlers,
        HashSet<string> missingValidators,
        CqrsStats stats)
    {
        var ids = new MermaidIdAllocator();
        var groups = requests
            .GroupBy(ProjectKey, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new
            {
                Project = g.Key,
                Requests = OrderRequests(g),
            })
            .ToList();
        var orderedRequests = groups.SelectMany(g => g.Requests).ToList();

        var requestNameCounts = orderedRequests
            .GroupBy(r => r.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var projectIds = new Dictionary<string, string>(StringComparer.Ordinal);

        var description =
            $"{CountText(requests.Count, "request")}, " +
            $"{CountText(stats.Handlers, "handler")}, " +
            $"{CountText(stats.Validators, "validator")}";
        if (stats.MissingHandlers > 0)
            description += $", {stats.MissingHandlers} MissingHandler";
        if (stats.MissingValidators > 0)
            description += $", {stats.MissingValidators} CommandWithoutValidator";
        description += ".";

        var sb = new StringBuilder();
        sb.AppendLine("flowchart LR");
        sb.AppendLine("    accTitle: CQRS map");
        sb.AppendLine($"    accDescr: {description}");

        foreach (var group in groups)
        {
            var projectId = ids.Allocate("project-group:" + group.Project, "prj", group.Project);
            projectIds[group.Project] = projectId;
            var subgraphLabel = group.Project == UnassignedProject
                ? "Unassigned requests"
                : $"Requests in {group.Project}";
            sb.AppendLine($"    subgraph {projectId}[\"{MermaidLabel(subgraphLabel)}\"]");
            foreach (var request in group.Requests)
            {
                var requestLabel = requestNameCounts[request.Name] > 1
                    ? request.FullName
                    : request.Name;
                var requestId = ids.Allocate(
                    "request:" + request.FullName,
                    "req",
                    requestLabel);
                sb.AppendLine(
                    $"        {requestId}[\"{MermaidLabel(requestLabel)}\"]" +
                    $":::{RequestClass(request, missingHandlers, missingValidators)}");
            }
            sb.AppendLine("    end");
        }

        var handlerFqns = orderedRequests
            .SelectMany(r => r.Handlers)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(h => h, StringComparer.Ordinal)
            .ToList();
        var handlerCounts = handlerFqns
            .GroupBy(CSharpScanner.SimpleName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var handlerIds = handlerFqns.ToDictionary(
            h => h,
            h => ids.Allocate(
                "handler:" + h,
                "hdl",
                handlerCounts[CSharpScanner.SimpleName(h)] > 1
                    ? h
                    : CSharpScanner.SimpleName(h)),
            StringComparer.Ordinal);
        foreach (var handler in handlerFqns)
        {
            var handlerName = CSharpScanner.SimpleName(handler);
            var handlerLabel = handlerCounts[handlerName] > 1 ? handler : handlerName;
            sb.AppendLine(
                $"    {handlerIds[handler]}([\"{MermaidLabel(handlerLabel)}\"])" +
                ":::clsHandler");
        }

        var validatorFqns = orderedRequests
            .SelectMany(r => r.Validators)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();
        var validatorCounts = validatorFqns
            .GroupBy(CSharpScanner.SimpleName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var validatorIds = validatorFqns.ToDictionary(
            v => v,
            v => ids.Allocate(
                "validator:" + v,
                "val",
                validatorCounts[CSharpScanner.SimpleName(v)] > 1
                    ? v
                    : CSharpScanner.SimpleName(v)),
            StringComparer.Ordinal);
        foreach (var validator in validatorFqns)
        {
            var validatorName = CSharpScanner.SimpleName(validator);
            var validatorLabel = validatorCounts[validatorName] > 1 ? validator : validatorName;
            sb.AppendLine(
                $"    {validatorIds[validator]}[\"{MermaidLabel(validatorLabel)}\"]" +
                ":::clsValidator");
        }

        foreach (var request in orderedRequests)
        {
            var requestId = ids.Allocate(
                "request:" + request.FullName,
                "req",
                requestNameCounts[request.Name] > 1 ? request.FullName : request.Name);
            foreach (var handler in request.Handlers
                         .Distinct(StringComparer.Ordinal)
                         .OrderBy(h => h, StringComparer.Ordinal))
                sb.AppendLine($"    {requestId} --> {handlerIds[handler]}");
            foreach (var validator in request.Validators
                         .Distinct(StringComparer.Ordinal)
                         .OrderBy(v => v, StringComparer.Ordinal))
                sb.AppendLine($"    {validatorIds[validator]} -.->|validates| {requestId}");
        }

        foreach (var projectId in projectIds.Values)
            sb.AppendLine(
                $"    style {projectId} fill:none,stroke:#94a3b8,stroke-dasharray:4 3");
        EmitCqrsClassDefs(sb, stats);
        return sb.ToString().TrimEnd();
    }

    private static List<(List<CqrsRequest> Requests, string Source, CqrsStats Stats)> BuildBounded(
        IReadOnlyList<CqrsRequest> requests,
        HashSet<string> missingHandlers,
        HashSet<string> missingValidators)
    {
        var result = new List<(List<CqrsRequest>, string, CqrsStats)>();
        var pending = new Stack<List<CqrsRequest>>();
        pending.Push(requests.ToList());

        while (pending.Count > 0)
        {
            var chunk = pending.Pop();
            var stats = GetStats(chunk, missingHandlers, missingValidators);
            var source = BuildCqrsSource(chunk, missingHandlers, missingValidators, stats);
            if (chunk.Count <= 1 || IsWithinMermaidLimits(source))
            {
                result.Add((chunk, source, stats));
                continue;
            }

            var middle = (chunk.Count + 1) / 2;
            pending.Push(chunk.Skip(middle).ToList());
            pending.Push(chunk.Take(middle).ToList());
        }

        return result;
    }

    private static IReadOnlyList<List<CqrsRequest>> BalancedChunks(IReadOnlyList<CqrsRequest> requests)
    {
        if (requests.Count == 0)
            return Array.Empty<List<CqrsRequest>>();

        var partCount = (requests.Count + MaxRequestsPerCqrsDiagram - 1) / MaxRequestsPerCqrsDiagram;
        if (partCount <= 1)
            return new[] { requests.ToList() };

        var partSize = (requests.Count + partCount - 1) / partCount;
        var chunks = new List<List<CqrsRequest>>(partCount);
        for (var i = 0; i < requests.Count; i += partSize)
            chunks.Add(requests.Skip(i).Take(partSize).ToList());
        return chunks;
    }

    private static List<CqrsRequest> OrderRequests(IEnumerable<CqrsRequest> requests)
        => requests
            .OrderBy(KindOrder)
            .ThenBy(r => r.FullName, StringComparer.Ordinal)
            .ToList();

    private static int KindOrder(CqrsRequest request)
        => request.Kind switch
        {
            "Command" => 0,
            "Query" => 1,
            _ => 2,
        };

    private static string ProjectKey(CqrsRequest request)
        => string.IsNullOrEmpty(request.ProjectName) ? UnassignedProject : request.ProjectName;

    private static string RequestClass(
        CqrsRequest request,
        HashSet<string> missingHandlers,
        HashSet<string> missingValidators)
    {
        if (missingHandlers.Contains(request.FullName))
            return "clsMissingHandler";
        if (missingValidators.Contains(request.FullName))
            return "clsCommandNoValidator";

        return request.Kind switch
        {
            "Command" => "clsCommand",
            "Query" => "clsQuery",
            _ => "clsRequest",
        };
    }

    private static CqrsStats GetStats(
        IReadOnlyList<CqrsRequest> requests,
        HashSet<string> missingHandlers,
        HashSet<string> missingValidators)
    {
        var requestNames = requests
            .Select(r => r.FullName)
            .ToHashSet(StringComparer.Ordinal);
        return new CqrsStats(
            requests.SelectMany(r => r.Handlers).Distinct(StringComparer.Ordinal).Count(),
            requests.SelectMany(r => r.Validators).Distinct(StringComparer.Ordinal).Count(),
            missingHandlers.Count(requestNames.Contains),
            missingValidators.Count(requestNames.Contains));
    }

    private static List<ProjectInfo> OrderedProjects(AnalysisResult result)
        => result.Projects
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.FilePath, StringComparer.Ordinal)
            .ToList();

    private static Dictionary<string, string> ProjectLabels(
        AnalysisResult result,
        IReadOnlyList<ProjectInfo> projects)
    {
        var nameCounts = projects
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var project in projects)
        {
            labels[project.FilePath] = nameCounts[project.Name] == 1
                ? project.Name
                : DisambiguatedProjectLabel(result, project);
        }

        return labels;
    }

    private static string DisambiguatedProjectLabel(AnalysisResult result, ProjectInfo project)
    {
        var relative = Path.GetRelativePath(result.RootPath, project.DirectoryPath)
            .Replace(Path.DirectorySeparatorChar, '/');
        if (relative is "." or "")
            relative = Path.GetFileName(project.FilePath);
        return $"{project.Name} ({relative})";
    }

    private static Dictionary<string, ProjectInfo?> UniqueProjectsByName(
        IReadOnlyList<ProjectInfo> projects)
        => projects
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Count() == 1 ? g.Single() : null,
                StringComparer.OrdinalIgnoreCase);

    private static int ResolvedReferenceCount(
        IReadOnlyList<ProjectInfo> projects,
        IReadOnlyDictionary<string, ProjectInfo?> projectsByName)
        => projects.Sum(project => project.ReferenceNames.Count(dependency =>
            projectsByName.TryGetValue(dependency, out var target) && target is not null));

    private static HashSet<(string Source, string Target)> LayerViolationPairs(
        IEnumerable<CheckFinding> checks)
    {
        var pairs = new HashSet<(string, string)>();
        foreach (var check in checks)
        {
            if (check.Code != ArchitectureChecker.LayerViolationCode ||
                string.IsNullOrEmpty(check.Target))
                continue;

            var separator = check.Target.IndexOf(" -> ", StringComparison.Ordinal);
            if (separator > 0 && separator + 4 < check.Target.Length)
                pairs.Add((check.Target[..separator], check.Target[(separator + 4)..]));
        }

        return pairs;
    }

    private static List<int> EmitProjectEdges(
        StringBuilder sb,
        IReadOnlyList<ProjectInfo> projects,
        IReadOnlyDictionary<string, ProjectInfo?> projectsByName,
        IReadOnlyDictionary<string, string> projectIds,
        HashSet<(string Source, string Target)> violations)
    {
        var linkIndexes = new List<int>();
        var edgeIndex = 0;
        foreach (var project in projects)
        {
            foreach (var dependency in project.ReferenceNames
                         .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(d => d, StringComparer.Ordinal))
            {
                if (!projectsByName.TryGetValue(dependency, out var target) || target is null)
                    continue;

                var violation = violations.Contains((project.Name, target.Name));
                var label = violation ? "|violates|" : "";
                sb.AppendLine(
                    $"    {projectIds[project.FilePath]} -->{label} {projectIds[target.FilePath]}");
                if (violation)
                    linkIndexes.Add(edgeIndex);
                edgeIndex++;
            }
        }

        return linkIndexes;
    }

    private static void EmitLinkStyles(StringBuilder sb, IEnumerable<int> indexes)
    {
        foreach (var index in indexes)
            sb.AppendLine(
                $"    linkStyle {index} stroke:#dc2626,stroke-width:2px,stroke-dasharray:4 3");
    }

    private static void EmitLayerClassDefs(StringBuilder sb)
    {
        sb.AppendLine("    classDef clsLayerDomain fill:#ecfdf5,stroke:#059669,stroke-width:2px,color:#064e3b");
        sb.AppendLine("    classDef clsLayerApplication fill:#eff6ff,stroke:#2563eb,stroke-width:2px,color:#1e3a8a");
        sb.AppendLine("    classDef clsLayerInfrastructure fill:#fff7ed,stroke:#c2410c,stroke-width:2px,color:#431407");
        sb.AppendLine("    classDef clsLayerApi fill:#f5f3ff,stroke:#7c3aed,stroke-width:2px,color:#2e1065");
        sb.AppendLine("    classDef clsLayerTests fill:#f8fafc,stroke:#64748b,stroke-width:1.5px,color:#1e293b");
        sb.AppendLine("    classDef clsLayerOther fill:#f8fafc,stroke:#64748b,stroke-width:1.5px,color:#1e293b");
    }

    private static void EmitCqrsClassDefs(StringBuilder sb, CqrsStats stats)
    {
        sb.AppendLine("    classDef clsCommand fill:#e8f1ff,stroke:#2563eb,stroke-width:2px,color:#102a43");
        sb.AppendLine("    classDef clsQuery fill:#e6fffa,stroke:#0f766e,stroke-width:2px,color:#0f2e2e");
        sb.AppendLine("    classDef clsRequest fill:#f5f3ff,stroke:#6d28d9,stroke-width:2px,color:#2e1065");
        sb.AppendLine("    classDef clsHandler fill:#f8fafc,stroke:#475569,stroke-width:1.5px,color:#1e293b");
        sb.AppendLine("    classDef clsValidator fill:#fff7ed,stroke:#c2410c,stroke-width:1.5px,color:#431407");
        if (stats.MissingHandlers > 0)
            sb.AppendLine("    classDef clsMissingHandler fill:#fef2f2,stroke:#dc2626,stroke-width:2px,stroke-dasharray:4 3,color:#7f1d1d");
        if (stats.MissingValidators > 0)
            sb.AppendLine("    classDef clsCommandNoValidator fill:#e8f1ff,stroke:#d97706,stroke-width:2px,stroke-dasharray:4 3,color:#102a43");
    }

    private static string LayerClass(string layer)
        => layer.ToLowerInvariant() switch
        {
            "domain" => "clsLayerDomain",
            "application" => "clsLayerApplication",
            "infrastructure" => "clsLayerInfrastructure",
            "api" or "web" or "ui" => "clsLayerApi",
            "tests" => "clsLayerTests",
            _ => "clsLayerOther",
        };

    private static string CountText(int count, string noun)
        => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    private static string SanitizeMermaidId(string? name)
    {
        var sb = new StringBuilder(name?.Length ?? 0);
        foreach (var c in name ?? "")
            sb.Append(c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_' ? c : '_');
        var id = sb.ToString();
        if (id.Length == 0)
            id = "n";
        if (char.IsDigit(id[0]))
            id = "n_" + id;
        return id;
    }

    private readonly record struct CqrsStats(
        int Handlers,
        int Validators,
        int MissingHandlers,
        int MissingValidators);

    /// <summary>Allocates collision-free ids for one diagram, preserving readable bases.</summary>
    private sealed class MermaidIdAllocator
    {
        private readonly Dictionary<string, string> _ids = new(StringComparer.Ordinal);
        private readonly HashSet<string> _used = new(StringComparer.Ordinal);

        public string Allocate(string key, string prefix, string preferredName)
        {
            if (_ids.TryGetValue(key, out var existing))
                return existing;

            var baseId = prefix + "_" + SanitizeMermaidId(preferredName);
            var id = baseId;
            var suffix = 2;
            while (!_used.Add(id))
                id = $"{baseId}_{suffix++}";

            _ids[key] = id;
            return id;
        }
    }
}
