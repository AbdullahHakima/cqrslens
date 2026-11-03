using System.Text.RegularExpressions;

namespace CqrsLens;

/// <summary>
/// Resolves where each detected pipeline behavior is registered, so the reports can
/// present behaviors in <em>registered order</em>. This is deliberately source-text
/// based (not symbol based) so text and Roslyn scans resolve identically.
/// <para>
/// It reads the registration expressions the source spells out — Microsoft DI
/// <c>Add{Transient,Scoped,Singleton}(typeof(IPipelineBehavior&lt;,&gt;), typeof(X&lt;,&gt;))</c>
/// (and the generic-method form) and the MediatR configuration
/// <c>AddOpenBehavior</c>/<c>AddBehavior</c>. Behaviors only reachable through
/// assembly scanning (Scrutor <c>Scan</c>, Autofac <c>RegisterAssemblyTypes</c>)
/// have no source-spelled order and keep null fields. It never guesses: an ambiguous
/// name match (several projects, or several sites within a project) leaves the fields
/// null.
/// </para>
/// <para>
/// The order recorded here is registration order. CqrsLens makes no execution-order
/// claim; the mapping from registrations to the runtime pipeline lives in the
/// mediator library, not in the scanned source.
/// </para>
/// </summary>
internal static partial class BehaviorRegistrationResolver
{
    // Microsoft DI: AddTransient(typeof(IPipelineBehavior<,>), typeof(X<,>))
    [GeneratedRegex(
        @"\bAdd(?:Transient|Scoped|Singleton)\s*\(\s*typeof\s*\(\s*IPipelineBehavior\s*<[^>]*>\s*\)\s*,\s*typeof\s*\(\s*(?<name>[A-Za-z_]\w*)\s*(?:<[^>]*>)?\s*\)",
        RegexOptions.Singleline)]
    private static partial Regex DirectDiRegex();

    // Microsoft DI generic-method form: AddTransient<IPipelineBehavior<,>, X<,>>()
    [GeneratedRegex(
        @"\bAdd(?:Transient|Scoped|Singleton)\s*<\s*IPipelineBehavior\s*<[^>]*>\s*,\s*(?<name>[A-Za-z_]\w*)\s*(?:<[^>]*>)?\s*>",
        RegexOptions.Singleline)]
    private static partial Regex DirectDiGenericRegex();

    // MediatR two-type form: AddBehavior(typeof(service), typeof(X<,>)) /
    // AddOpenBehavior(typeof(service), typeof(X<,>)).
    [GeneratedRegex(
        @"\bAdd(?:Open)?Behavior\s*\(\s*typeof\s*\(\s*[A-Za-z_][\w.]*\s*(?:<[^>]*>)?\s*\)\s*,\s*typeof\s*\(\s*(?<name>[A-Za-z_]\w*)\s*(?:<[^>]*>)?\s*\)",
        RegexOptions.Singleline)]
    private static partial Regex MediatRTwoArgRegex();

    // MediatR open form: AddOpenBehavior(typeof(X<,>)).
    [GeneratedRegex(
        @"\bAddOpenBehavior\s*\(\s*typeof\s*\(\s*(?<name>[A-Za-z_]\w*)\s*(?:<[^>]*>)?\s*\)",
        RegexOptions.Singleline)]
    private static partial Regex MediatROpenRegex();

    // MediatR generic-method form: AddOpenBehavior<X>() / AddBehavior<X>().
    [GeneratedRegex(
        @"\bAdd(?:Open)?Behavior\s*<\s*(?<name>[A-Za-z_]\w*)\s*(?:<[^>]*>)?\s*>",
        RegexOptions.Singleline)]
    private static partial Regex MediatRGenericRegex();

    internal static void Resolve(SourceWorkspace workspace, AnalysisResult result)
    {
        if (result.Behaviors.Count == 0)
            return;

        var registrations = new List<Registration>();
        foreach (var file in workspace.Files.OrderBy(f => f.Path, StringComparer.Ordinal))
        {
            if (file.Code.Length == 0)
                continue;

            var mask = SourceMask.Build(file.Code);
            var found = new List<(int Index, string Name)>();
            Collect(DirectDiRegex(), file.Code, mask, found);
            Collect(DirectDiGenericRegex(), file.Code, mask, found);
            Collect(MediatRTwoArgRegex(), file.Code, mask, found);
            Collect(MediatROpenRegex(), file.Code, mask, found);
            Collect(MediatRGenericRegex(), file.Code, mask, found);

            foreach (var (index, name) in found
                         .Distinct()
                         .OrderBy(f => f.Index))
            {
                registrations.Add(new Registration(
                    file.Path,
                    file.ProjectName,
                    name,
                    index,
                    LineOf(file.Code, index),
                    EnclosingBlockStart(file.Code, mask, index)));
            }
        }

        if (registrations.Count == 0)
            return;

        var sites = BuildSites(registrations, result.RootPath);
        foreach (var behavior in result.Behaviors)
            Assign(behavior, registrations, sites);
    }

    private static void Collect(Regex regex, string code, SourceMask mask, List<(int, string)> found)
    {
        foreach (Match match in regex.Matches(code))
        {
            if (!mask.IsCode(match.Index))
                continue;
            var name = match.Groups["name"].Value;
            if (name.Length == 0)
                continue;
            found.Add((match.Index, name));
        }
    }

    private sealed record Registration(
        string File,
        string? ProjectName,
        string Name,
        int Index,
        int Line,
        int Block);

    private sealed class Site
    {
        public required string File { get; init; }
        public required int Block { get; init; }
        public required List<Registration> Registrations { get; init; }
        public required string Label { get; init; }
    }

    private static List<Site> BuildSites(List<Registration> registrations, string rootPath)
    {
        var sites = new List<Site>();
        foreach (var group in registrations
                     .GroupBy(r => (r.File, r.Block))
                     .OrderBy(g => g.Key.File, StringComparer.Ordinal)
                     .ThenBy(g => g.Key.Block))
        {
            var ordered = group.OrderBy(r => r.Index).ToList();
            var min = ordered.Min(r => r.Line);
            var max = ordered.Max(r => r.Line);
            var relative = RelativePath(rootPath, group.Key.File);
            var label = min == max ? $"{relative}:{min}" : $"{relative}:{min}-{max}";
            sites.Add(new Site
            {
                File = group.Key.File,
                Block = group.Key.Block,
                Registrations = ordered,
                Label = label,
            });
        }
        return sites;
    }

    private static void Assign(
        BehaviorInfo behavior,
        List<Registration> registrations,
        List<Site> sites)
    {
        var matches = registrations.Where(r => r.Name == behavior.Name).ToList();
        if (matches.Count == 0)
            return;

        var projects = matches
            .Select(m => m.ProjectName)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var candidates = matches;
        if (projects.Count > 1)
        {
            // Prefer the behavior's own project; if it is not among the matches the
            // name is ambiguous across projects and we must not guess.
            candidates = matches
                .Where(m => m.ProjectName == behavior.ProjectName)
                .ToList();
            if (candidates.Count == 0)
                return;
        }

        var candidateSites = candidates
            .Select(c => (c.File, c.Block))
            .Distinct()
            .ToList();
        if (candidateSites.Count != 1)
            return; // Several sites in the same scope: ambiguous, leave null.

        var key = candidateSites[0];
        var site = sites.First(s => s.File == key.File && s.Block == key.Block);
        var ordinal = site.Registrations.FindIndex(r => r.Name == behavior.Name);
        if (ordinal < 0)
            return;

        behavior.RegistrationOrder = ordinal + 1;
        behavior.RegistrationSite = site.Label;
    }

    private static int EnclosingBlockStart(string code, SourceMask mask, int index)
    {
        var stack = new Stack<int>();
        for (var i = 0; i < index && i < code.Length; i++)
        {
            if (!mask.IsCode(i))
                continue;
            if (code[i] == '{')
                stack.Push(i);
            else if (code[i] == '}' && stack.Count > 0)
                stack.Pop();
        }
        return stack.Count > 0 ? stack.Peek() : -1;
    }

    private static int LineOf(string code, int index)
    {
        var line = 1;
        var limit = Math.Min(index, code.Length);
        for (var i = 0; i < limit; i++)
        {
            if (code[i] == '\n')
                line++;
        }
        return line;
    }

    private static string RelativePath(string rootPath, string file)
        => Path.GetRelativePath(rootPath, file).Replace(Path.DirectorySeparatorChar, '/');
}
