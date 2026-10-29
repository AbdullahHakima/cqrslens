using System.Text;

namespace CqrsLens;

/// <summary>Lookup tables and stable anchors shared by the request table and explorer.</summary>
internal sealed class RequestExplorerIndex
{
    private RequestExplorerIndex(
        Dictionary<CqrsRequest, string> anchors,
        Dictionary<string, HandlerInfo> handlersByFullName,
        Dictionary<string, HandlerInfo> handlersBySimpleName,
        Dictionary<string, ValidatorInfo> validatorsByFullName,
        Dictionary<string, ValidatorInfo> validatorsBySimpleName,
        Dictionary<string, List<CheckFinding>> checksByTarget)
    {
        Anchors = anchors;
        HandlersByFullName = handlersByFullName;
        HandlersBySimpleName = handlersBySimpleName;
        ValidatorsByFullName = validatorsByFullName;
        ValidatorsBySimpleName = validatorsBySimpleName;
        ChecksByTarget = checksByTarget;
    }

    public Dictionary<CqrsRequest, string> Anchors { get; }
    public Dictionary<string, HandlerInfo> HandlersByFullName { get; }
    public Dictionary<string, HandlerInfo> HandlersBySimpleName { get; }
    public Dictionary<string, ValidatorInfo> ValidatorsByFullName { get; }
    public Dictionary<string, ValidatorInfo> ValidatorsBySimpleName { get; }
    public Dictionary<string, List<CheckFinding>> ChecksByTarget { get; }

    internal static RequestExplorerIndex Create(AnalysisResult result)
    {
        var usedAnchors = new HashSet<string>(StringComparer.Ordinal);
        var anchors = new Dictionary<CqrsRequest, string>();
        foreach (var r in result.Requests)
        {
            var baseId = "explorer-" + DiagramBuilder.MermaidId(r.FullName);
            var id = baseId;
            var suffix = 2;
            while (!usedAnchors.Add(id))
                id = $"{baseId}-{suffix++}";
            anchors[r] = id;
        }

        return new RequestExplorerIndex(
            anchors,
            result.Handlers
                .GroupBy(h => h.FullName, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal),
            result.Handlers
                .GroupBy(h => h.Name, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal),
            result.Validators
                .GroupBy(v => v.FullName, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal),
            result.Validators
                .GroupBy(v => v.Name, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal),
            result.Checks
                .Where(c => !string.IsNullOrEmpty(c.Target))
                .GroupBy(c => c.Target!, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal));
    }

    internal string BuildSearchBlob(CqrsRequest r)
    {
        var sb = new StringBuilder();
        sb.Append(r.FullName).Append(' ')
          .Append(r.Kind).Append(' ')
          .Append(r.ResponseType).Append(' ')
          .Append(r.ProjectName).Append(' ')
          .Append(r.Location?.File).Append(' ');

        foreach (var ep in r.Entrypoints)
            sb.Append(ep).Append(' ');
        foreach (var prop in r.Properties)
            sb.Append(prop.Name).Append(' ').Append(prop.Type).Append(' ');
        foreach (var entity in r.TouchedEntities)
            sb.Append(entity).Append(' ');
        foreach (var notif in r.PublishedNotifications)
            sb.Append(notif).Append(' ');

        foreach (var h in r.Handlers)
        {
            sb.Append(h).Append(' ');
            var info = ResolveHandler(h);
            if (info?.Location is not null)
                sb.Append(info.Location.File).Append(' ');
        }

        foreach (var v in r.Validators)
        {
            sb.Append(v).Append(' ');
            var info = ResolveValidator(v);
            if (info?.Location is not null)
                sb.Append(info.Location.File).Append(' ');
        }

        return sb.ToString().Trim();
    }

    internal HandlerInfo? ResolveHandler(string name)
    {
        if (HandlersByFullName.TryGetValue(name, out var h))
            return h;
        return HandlersBySimpleName.TryGetValue(CSharpScanner.SimpleName(name), out h) ? h : null;
    }

    internal ValidatorInfo? ResolveValidator(string name)
    {
        if (ValidatorsByFullName.TryGetValue(name, out var v))
            return v;
        return ValidatorsBySimpleName.TryGetValue(CSharpScanner.SimpleName(name), out v) ? v : null;
    }

    internal static string FormatLocation(SourceLocation? location)
        => location is null
            ? "—"
            : $"<code>{HtmlReportText.Encode($"{location.File}:{location.Line}:{location.Column}")}</code>";
}
