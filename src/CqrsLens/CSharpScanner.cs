using System.Text.RegularExpressions;

namespace CqrsLens;

/// <summary>
/// Lightweight text-based detection of MediatR / FluentValidation / EF Core shapes.
/// v1 deliberately uses regex over source text (no Roslyn, no build) — fast and good
/// enough for a map; it can miss exotic declarations (see README limitations).
/// </summary>
public static partial class CSharpScanner
{
    // Matches: class|record|struct Name<...> (primary-ctor) : <base list> { or ;
    [GeneratedRegex(@"(?:class|record(?:\s+class|\s+struct)?|struct)\s+(\w+)(?:<[^>]*>)?\s*(?:\([^)]*\))?\s*:\s*([^{;]+)[{;]",
        RegexOptions.Singleline)]
    private static partial Regex DeclarationRegex();

    [GeneratedRegex(@"\bDbSet\s*<\s*(\w+)\s*>\s+(\w+)")]
    private static partial Regex DbSetRegex();

    [GeneratedRegex(@"(?:^|[,\s])(\w*DbContext)\b")]
    private static partial Regex DbContextBaseRegex();

    public static void ScanFile(string path, string? projectName, AnalysisResult result)
        => ScanText(File.ReadAllText(path), projectName, result);

    public static void ScanText(string code, string? projectName, AnalysisResult result)
    {
        var contextsInFile = new List<DbContextInfo>();

        foreach (Match m in DeclarationRegex().Matches(code))
        {
            var typeName = m.Groups[1].Value;
            var baseList = m.Groups[2].Value;

            // IRequest<...> / IRequest  -> a CQRS request
            if (Regex.IsMatch(baseList, @"\bIRequest\b"))
            {
                var response = ExtractGenericArguments(baseList, "IRequest")?.FirstOrDefault();
                result.Requests.Add(new CqrsRequest
                {
                    Name = typeName,
                    ResponseType = response,
                    ProjectName = projectName,
                });
            }

            // IRequestHandler<TRequest, TResponse> -> a handler
            var handlerArgs = ExtractGenericArguments(baseList, "IRequestHandler");
            if (handlerArgs is { Count: >= 1 })
            {
                result.Handlers.Add(new HandlerInfo
                {
                    Name = typeName,
                    RequestName = handlerArgs[0],
                    ProjectName = projectName,
                });
            }

            // AbstractValidator<T> / IValidator<T> -> a validator for T
            var validatorTarget = ExtractGenericArguments(baseList, "AbstractValidator")?.FirstOrDefault()
                                  ?? ExtractGenericArguments(baseList, "IValidator")?.FirstOrDefault();
            if (validatorTarget is not null)
            {
                result.Validators.Add(new ValidatorInfo
                {
                    Name = typeName,
                    TargetType = validatorTarget,
                    ProjectName = projectName,
                });
            }

            // IPipelineBehavior<...> -> a pipeline behavior
            if (baseList.Contains("IPipelineBehavior<", StringComparison.Ordinal))
            {
                result.Behaviors.Add(new BehaviorInfo { Name = typeName, ProjectName = projectName });
            }

            // : DbContext (or *DbContext, e.g. IdentityDbContext) -> a DbContext
            if (DbContextBaseRegex().IsMatch(baseList))
            {
                var ctx = new DbContextInfo { Name = typeName, ProjectName = projectName };
                contextsInFile.Add(ctx);
                result.DbContexts.Add(ctx);
            }
        }

        if (contextsInFile.Count > 0)
        {
            var sets = DbSetRegex().Matches(code)
                .Select(m => new DbSetInfo(m.Groups[1].Value, m.Groups[2].Value))
                .Distinct()
                .ToList();
            foreach (var ctx in contextsInFile)
                ctx.DbSets.AddRange(sets);
        }
    }

    /// <summary>
    /// Extracts the top-level generic arguments of <paramref name="interfaceName"/>
    /// as written in a base list, e.g. IRequestHandler&lt;Foo, Bar&gt; -> ["Foo", "Bar"].
    /// Returns null when the interface (with generic arguments) is not present.
    /// </summary>
    public static List<string>? ExtractGenericArguments(string baseList, string interfaceName)
    {
        var idx = baseList.IndexOf(interfaceName, StringComparison.Ordinal);
        while (idx >= 0)
        {
            var after = idx + interfaceName.Length;
            // The next char must be '<' (possibly after spaces) — otherwise this is a
            // longer identifier that merely starts with the same text.
            var i = after;
            while (i < baseList.Length && char.IsWhiteSpace(baseList[i])) i++;
            if (i < baseList.Length && baseList[i] == '<')
            {
                var depth = 0;
                var start = i + 1;
                for (var j = i; j < baseList.Length; j++)
                {
                    if (baseList[j] == '<') depth++;
                    else if (baseList[j] == '>')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            var inner = baseList.Substring(start, j - start);
                            return SplitTopLevel(inner);
                        }
                    }
                }
                return null;
            }
            idx = baseList.IndexOf(interfaceName, idx + 1, StringComparison.Ordinal);
        }
        return null;
    }

    private static List<string> SplitTopLevel(string inner)
    {
        var parts = new List<string>();
        var depth = 0;
        var current = "";
        foreach (var c in inner)
        {
            if (c == '<') depth++;
            if (c == '>') depth--;
            if (c == ',' && depth == 0)
            {
                parts.Add(current.Trim());
                current = "";
            }
            else current += c;
        }
        if (current.Trim().Length > 0) parts.Add(current.Trim());
        return parts;
    }
}
