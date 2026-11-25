using System.Security;
using System.Text.RegularExpressions;

namespace CqrsLens;

/// <summary>
/// Discovers test projects, test frameworks/mocking/assertion packages, and static
/// test method references to scanned requests, handlers, and validators.
/// </summary>
public static partial class TestProjectDiscovery
{
    private const string KindRequest = "request";
    private const string KindHandler = "handler";
    private const string KindValidator = "validator";
    private const string KindEntrypoint = "entrypoint";
    private const string KindService = "service";

    private const string RefDispatch = "dispatch";
    private const string RefInstantiation = "instantiation";
    private const string RefTypeReference = "type-reference";

    private static readonly HashSet<string> TrivialGenericTargetNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Get", "Post", "Put", "Delete", "Patch", "Head", "Options",
        "Index", "List", "Show", "Edit", "New", "Create", "Update", "Remove", "Save", "Read", "Write", "Find", "Search", "Details",
        "Handle", "HandleAsync", "Execute", "ExecuteAsync", "Run", "RunAsync", "Start", "StartAsync", "Stop", "StopAsync",
        "Invoke", "InvokeAsync", "Consume", "ConsumeAsync", "Process", "ProcessAsync", "Publish", "PublishAsync", "Send", "SendAsync",
        "Build", "Init", "Dispose", "DisposeAsync", "ToString", "Equals", "GetHashCode", "Configure", "Map", "Register",
        "Test", "Tests", "Api", "Service", "Services", "Controller", "Endpoint", "Endpoints",
        "Worker", "Consumer", "Handler", "Request", "Command", "Query", "Program", "Startup", "Default", "Base", "Helper",
        "MapGet", "MapPost", "MapPut", "MapDelete", "MapPatch",
    };

    private static readonly HashSet<string> TestMethodAttributes = new(StringComparer.Ordinal)
    {
        "Fact",
        "Theory",
        "Test",
        "TestCase",
        "TestCaseSource",
        "TestMethod",
        "DataTestMethod",
        "SkippableFact",
        "SkippableTheory",
    };

    [GeneratedRegex(
        @"\bnew\s+(?:global\s*::\s*)?(?<type>@?[A-Za-z_]\w*(?:\s*\.\s*@?[A-Za-z_]\w*)*)\s*(?:<|[\(\{\[])",
        RegexOptions.None,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex NewObjectRegex();

    [GeneratedRegex(
        @"(?<![\w@])(?:global\s*::\s*)?(?<name>@?[A-Za-z_]\w*(?:\s*\.\s*@?[A-Za-z_]\w*)*)(?!\w)",
        RegexOptions.None,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex IdentifierRegex();

    [GeneratedRegex(
        @"\b(?<lhs>@?[A-Za-z_]\w*)\s*=\s*new\b",
        RegexOptions.None,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex TargetTypedAssignmentRegex();

    [GeneratedRegex(
        @"\b(?<name>@?[A-Za-z_]\w*)\s*(?:<[^;{}()]*>)?\s*\(",
        RegexOptions.None,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex CallIdentifierRegex();

    [GeneratedRegex(@"\s+", RegexOptions.None, matchTimeoutMilliseconds: 2000)]
    private static partial Regex WhitespaceRegex();

    /// <summary>
    /// Classifies test projects in <paramref name="result"/> by framework, mocking,
    /// assertion libraries, and referenced non-test production projects.
    /// </summary>
    public static IReadOnlyList<TestProjectInfo> DiscoverTestProjects(AnalysisResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var projectsByName = BuildProjectsByName(result.Projects);
        var discovered = new List<TestProjectInfo>();

        foreach (var project in result.Projects)
        {
            if (!IsTestProject(project))
                continue;

            var frameworks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var mocking = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var assertions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var pkg in project.PackageReferences)
            {
                if (TryClassifyFramework(pkg, out var framework))
                    frameworks.Add(framework);
                if (TryClassifyMocking(pkg, out var mockLib))
                    mocking.Add(mockLib);
                if (TryClassifyAssertion(pkg, out var assertLib))
                    assertions.Add(assertLib);
            }

            var prodRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var refName in project.ReferenceNames)
            {
                if (projectsByName.TryGetValue(refName, out var targets))
                {
                    if (targets.Any(t => !string.Equals(t.Layer, "Tests", StringComparison.OrdinalIgnoreCase)))
                        prodRefs.Add(refName);
                }
                else
                {
                    prodRefs.Add(refName);
                }
            }

            discovered.Add(new TestProjectInfo(
                project.Name,
                NormalizeRelativePath(project.FilePath, result.RootPath),
                OrderDistinct(frameworks),
                OrderDistinct(mocking),
                OrderDistinct(assertions),
                OrderDistinct(prodRefs)));
        }

        return discovered
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.FilePath, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Returns all static test references targeting <paramref name="request"/>, its linked
    /// handlers, or its linked validators.
    /// </summary>
    public static IReadOnlyList<TestMethodReference> FindTestReferencesForSlice(
        AnalysisResult result,
        CqrsRequest request)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(request);

        var sliceTargets = new HashSet<(string Kind, string FullName)>
        {
            (KindRequest, request.FullName),
        };
        foreach (var handler in request.Handlers)
            sliceTargets.Add((KindHandler, handler));
        foreach (var validator in request.Validators)
            sliceTargets.Add((KindValidator, validator));

        var index = TargetIndex.Create(result, request);
        return FindAllInternal(result, index)
            .Where(r => sliceTargets.Contains((r.TargetKind, r.TargetFullName)))
            .ToArray();
    }

    /// <summary>
    /// Scans all test project source files and test dispatch call sites for static
    /// references to scanned requests, handlers, and validators.
    /// </summary>
    public static IReadOnlyList<TestMethodReference> FindAllTestReferences(AnalysisResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var index = TargetIndex.Create(result, extraRequest: null);
        return FindAllInternal(result, index);
    }

    /// <summary>
    /// Returns all static test references targeting <paramref name="targetName"/>
    /// (a CQRS request, system entrypoint, gRPC RPC, background worker, message consumer,
    /// or concrete service class), optionally scoped to <paramref name="projectName"/>.
    /// </summary>
    public static IReadOnlyList<TestMethodReference> FindTestReferencesForTarget(
        AnalysisResult result,
        string targetName,
        string? projectName = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);

        var query = targetName.Trim();
        var scopedProject = ResolveProjectName(result, projectName);

        var matchingRequests = FindMatchingRequestsForTarget(result, query, scopedProject);
        if (matchingRequests.Length == 1)
            return FindTestReferencesForSlice(result, matchingRequests[0]);

        var canonicalTarget = query;
        var targetKind = KindService;
        var index = new TargetIndex();
        var seen = new HashSet<(string Kind, string FullName)>();
        var routeLiterals = new HashSet<string>(StringComparer.Ordinal);
        var matchRequests = new HashSet<string>(StringComparer.Ordinal);

        var matchedEntrypoint = FindMatchingEntrypointForTarget(result, query, scopedProject);
        if (matchedEntrypoint is not null)
        {
            targetKind = KindEntrypoint;
            canonicalTarget = query;
            var epProject = matchedEntrypoint.ProjectName ?? scopedProject;
            RegisterEntrypointTokens(index, seen, matchedEntrypoint, canonicalTarget, epProject, routeLiterals);

            foreach (var reqFull in matchedEntrypoint.TargetRequests)
            {
                var req = result.Requests.FirstOrDefault(r => string.Equals(r.FullName, reqFull, StringComparison.Ordinal));
                if (req is not null)
                {
                    matchRequests.Add(req.FullName);
                    index.AddEntry(seen, KindRequest, req.FullName, req.Name, req.ProjectName);
                    foreach (var h in req.Handlers)
                    {
                        var simpleH = CSharpScanner.SimpleName(h);
                        index.AddEntry(seen, KindHandler, h, simpleH, req.ProjectName);
                    }
                    foreach (var v in req.Validators)
                    {
                        var simpleV = CSharpScanner.SimpleName(v);
                        index.AddEntry(seen, KindValidator, v, simpleV, req.ProjectName);
                    }
                }
            }
        }
        else if (matchingRequests.Length > 1)
        {
            foreach (var req in matchingRequests)
            {
                matchRequests.Add(req.FullName);
                index.AddEntry(seen, KindRequest, req.FullName, req.Name, req.ProjectName);
            }
        }
        else if (query.StartsWith('/'))
        {
            targetKind = KindEntrypoint;
            if (query.Length > 1)
                routeLiterals.Add(query);
        }
        else if (TrySplitMethodTarget(query, result, scopedProject, out var classPart, out var methodPart))
        {
            targetKind = KindEntrypoint;
            index.AddEntry(seen, KindEntrypoint, query, query, scopedProject);
            if (!IsTrivialTargetToken(classPart) && classPart.Length >= 3)
                index.AddEntry(seen, KindEntrypoint, classPart, classPart, scopedProject);
            if (!IsTrivialTargetToken(methodPart) && methodPart.Length >= 4)
                index.AddEntry(seen, KindEntrypoint, methodPart, methodPart, scopedProject);
        }
        else
        {
            targetKind = IsKnownTypeInModel(result, query) ? KindService : KindEntrypoint;
            var simple = CSharpScanner.SimpleName(query);
            index.AddEntry(seen, targetKind, query, IsTrivialTargetToken(simple) ? query : simple, scopedProject);
        }

        var refs = FindAllInternal(
            result,
            index,
            scopedProject,
            routeLiterals.Count > 0 ? (canonicalTarget, targetKind, routeLiterals) : null,
            includeRequestDispatchFallback: matchRequests.Count > 0);

        var deduped = new Dictionary<(string Project, string File, string Class, string Method, string Target, string Kind), TestMethodReference>();
        foreach (var rawRef in refs)
        {
            var normalized = string.Equals(rawRef.TargetKind, KindEntrypoint, StringComparison.Ordinal) ||
                             string.Equals(rawRef.TargetKind, KindService, StringComparison.Ordinal)
                ? rawRef with { TargetFullName = canonicalTarget }
                : rawRef;

            var key = (
                normalized.ProjectName,
                normalized.File,
                normalized.ClassName,
                normalized.MethodName,
                normalized.TargetFullName,
                normalized.TargetKind);

            if (!deduped.TryGetValue(key, out var existing) ||
                ReferencePriority(normalized.ReferenceKind) > ReferencePriority(existing.ReferenceKind) ||
                (ReferencePriority(normalized.ReferenceKind) == ReferencePriority(existing.ReferenceKind) &&
                 normalized.Line < existing.Line))
            {
                deduped[key] = normalized;
            }
        }

        return deduped.Values
            .OrderBy(r => r.ProjectName, StringComparer.Ordinal)
            .ThenBy(r => r.File, StringComparer.Ordinal)
            .ThenBy(r => r.Line)
            .ThenBy(r => r.ClassName, StringComparer.Ordinal)
            .ThenBy(r => r.MethodName, StringComparer.Ordinal)
            .ThenBy(r => r.TargetKind, StringComparer.Ordinal)
            .ThenBy(r => r.TargetFullName, StringComparer.Ordinal)
            .ThenBy(r => r.ReferenceKind, StringComparer.Ordinal)
            .ToArray();
    }

    private static int ReferencePriority(string refKind) => refKind switch
    {
        RefDispatch => 3,
        RefInstantiation => 2,
        _ => 1,
    };

    internal static bool IsTrivialTargetToken(string token)
        => string.IsNullOrWhiteSpace(token) || TrivialGenericTargetNames.Contains(token.Trim());

    private static IReadOnlyList<TestMethodReference> FindAllInternal(
        AnalysisResult result,
        TargetIndex index,
        string? scopedProject = null,
        (string CanonicalTarget, string TargetKind, HashSet<string> RouteLiterals)? routeContext = null,
        bool includeRequestDispatchFallback = true)
    {
        var testProjects = DiscoverTestProjects(result);
        var testProjectLookup = new Dictionary<string, TestProjectInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var tp in testProjects)
            testProjectLookup.TryAdd(tp.Name, tp);

        var testProjectObjects = new HashSet<ProjectInfo>(
            result.Projects.Where(p => testProjectLookup.ContainsKey(p.Name)));

        var transitiveRefsByTestProject = scopedProject is not null
            ? BuildTransitiveProductionRefs(result, testProjects)
            : null;

        var collected = new Dictionary<RefKey, (int Priority, int Line, string RefKind)>();
        var matchedDispatchSites = new HashSet<DispatchCallSite>();

        var dispatchSitesByFile = BuildTestDispatchMap(result);
        var testFiles = LoadTestSourceFiles(result, testProjectObjects);

        if (testFiles.Count > 0)
        {
            var forUsings = testFiles
                .Select(f => (f.Source, f.Scan))
                .ToList();
            GlobalUsingApplier.Apply(forUsings, result.Projects);

            foreach (var parsed in testFiles)
            {
                var relPath = NormalizeRelativePath(parsed.Source.Path, result.RootPath);
                var projectName = parsed.Source.ProjectName ?? string.Empty;
                var prodRefs = testProjectLookup.TryGetValue(projectName, out var tp)
                    ? new HashSet<string>(tp.ReferencedProductionProjects, StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (scopedProject is not null &&
                    transitiveRefsByTestProject is not null &&
                    transitiveRefsByTestProject.TryGetValue(projectName, out var transRefs) &&
                    transRefs.Count > 0 &&
                    !transRefs.Contains(scopedProject))
                {
                    continue;
                }

                dispatchSitesByFile.TryGetValue(relPath, out var fileDispatches);
                ScanFile(
                    parsed,
                    relPath,
                    projectName,
                    prodRefs,
                    index,
                    fileDispatches,
                    matchedDispatchSites,
                    collected,
                    routeContext);
            }
        }

        if (includeRequestDispatchFallback)
        {
            // Preserve test-project request dispatch sites that did not land inside a scanned
            // test method (e.g. synthetic test models or test helper classes without [Fact]).
            foreach (var site in result.DispatchCallSites)
            {
                if (!site.IsTestProject || site.TargetKind != DispatchTargetKind.Request)
                    continue;
                if (matchedDispatchSites.Contains(site))
                    continue;

                var relPath = NormalizeRelativePath(site.Location?.File, result.RootPath);
                var className = site.SenderFullName ?? string.Empty;
                var projectName = site.ProjectName ?? string.Empty;

                if (HasStrongerOrEqualForClassTarget(collected, projectName, relPath, className, site.TargetFullName))
                    continue;

                var key = new RefKey(projectName, relPath, className, "<dispatch>", site.TargetFullName, KindRequest);
                AddOrUpgrade(collected, key, priority: 3, site.Location?.Line ?? 1, RefDispatch);
            }
        }

        return collected
            .Select(kv => new TestMethodReference(
                kv.Key.ProjectName,
                kv.Key.File,
                kv.Value.Line,
                kv.Key.ClassName,
                kv.Key.MethodName,
                kv.Key.TargetFullName,
                kv.Key.TargetKind,
                kv.Value.RefKind))
            .OrderBy(r => r.ProjectName, StringComparer.Ordinal)
            .ThenBy(r => r.File, StringComparer.Ordinal)
            .ThenBy(r => r.Line)
            .ThenBy(r => r.ClassName, StringComparer.Ordinal)
            .ThenBy(r => r.MethodName, StringComparer.Ordinal)
            .ThenBy(r => r.TargetKind, StringComparer.Ordinal)
            .ThenBy(r => r.TargetFullName, StringComparer.Ordinal)
            .ThenBy(r => r.ReferenceKind, StringComparer.Ordinal)
            .ToArray();
    }

    private static void ScanFile(
        ParsedTestFile parsed,
        string relPath,
        string projectName,
        HashSet<string> prodRefs,
        TargetIndex index,
        List<DispatchCallSite>? fileDispatches,
        HashSet<DispatchCallSite> matchedDispatchSites,
        Dictionary<RefKey, (int Priority, int Line, string RefKind)> collected,
        (string CanonicalTarget, string TargetKind, HashSet<string> RouteLiterals)? routeContext = null)
    {
        var code = parsed.Source.Code;
        var mask = parsed.Mask;
        var maskedCode = mask.CodeOnly(0, code.Length);
        var lineStarts = ComputeLineStarts(code);

        foreach (var decl in parsed.Scan.Classes)
        {
            if (decl.BodySpan.End <= decl.BodySpan.Start)
                continue;

            var parsedClass = ParseClassMembers(decl, parsed.Scan, code, maskedCode, mask, lineStarts);
            if (parsedClass.TestMethods.Count == 0)
                continue;

            var memberTargets = CollectClassMemberTargets(
                decl, parsedClass, code, maskedCode, mask, prodRefs, index);

            var helperRefsByName = AnalyzeHelperMethods(
                decl, parsedClass.HelperMethods, code, maskedCode, lineStarts, prodRefs, index,
                fileDispatches, matchedDispatchSites, routeContext);

            var testMethodRefs = AnalyzeMethods(
                decl, parsedClass.TestMethods, code, maskedCode, lineStarts, prodRefs, index,
                fileDispatches, matchedDispatchSites, routeContext);

            // Propagate references from called helper methods and referenced class fields/ctors.
            for (var i = 0; i < parsedClass.TestMethods.Count; i++)
            {
                var testMethod = parsedClass.TestMethods[i];
                var refs = testMethodRefs[i];
                var methodLine = testMethod.DeclarationLine;

                var visitedHelpers = new HashSet<string>(StringComparer.Ordinal);
                var usedTokens = new HashSet<string>(refs.IdentifierTokens, StringComparer.Ordinal);
                var queue = new Queue<string>(refs.CalledMethods);

                while (queue.Count > 0)
                {
                    var helperName = queue.Dequeue();
                    if (!visitedHelpers.Add(helperName))
                        continue;
                    if (!helperRefsByName.TryGetValue(helperName, out var helperMatches))
                        continue;

                    foreach (var helperMatch in helperMatches)
                    {
                        foreach (var t in helperMatch.Targets)
                            refs.AddTarget(t.Kind, t.FullName, t.Priority, methodLine, t.RefKind);
                        foreach (var tok in helperMatch.IdentifierTokens)
                            usedTokens.Add(tok);
                        foreach (var nestedCall in helperMatch.CalledMethods)
                        {
                            if (!visitedHelpers.Contains(nestedCall))
                                queue.Enqueue(nestedCall);
                        }
                    }
                }

                foreach (var token in usedTokens)
                {
                    if (!memberTargets.TryGetValue(token, out var memberRefs))
                        continue;
                    foreach (var mr in memberRefs)
                        refs.AddTarget(mr.Kind, mr.FullName, mr.Priority, methodLine, mr.RefKind);
                }

                foreach (var target in refs.Targets)
                {
                    var key = new RefKey(
                        projectName, relPath, decl.FullName, testMethod.Name, target.FullName, target.Kind);
                    AddOrUpgrade(collected, key, target.Priority, target.Line, target.RefKind);
                }
            }
        }
    }

    private static List<MethodAnalysis> AnalyzeMethods(
        CSharpScanner.ClassDecl decl,
        List<ParsedMethod> methods,
        string rawCode,
        string maskedCode,
        int[] lineStarts,
        HashSet<string> prodRefs,
        TargetIndex index,
        List<DispatchCallSite>? fileDispatches,
        HashSet<DispatchCallSite> matchedDispatchSites,
        (string CanonicalTarget, string TargetKind, HashSet<string> RouteLiterals)? routeContext = null)
    {
        var results = new List<MethodAnalysis>(methods.Count);
        foreach (var method in methods)
        {
            var analysis = new MethodAnalysis();
            var start = method.ScanSpan.Start;
            var end = Math.Min(maskedCode.Length, method.ScanSpan.End);
            if (end <= start)
            {
                results.Add(analysis);
                continue;
            }

            if (fileDispatches is not null)
            {
                foreach (var site in fileDispatches)
                {
                    if (!string.IsNullOrEmpty(site.SenderFullName) &&
                        !string.Equals(site.SenderFullName, decl.FullName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var siteLine = site.Location?.Line ?? 0;
                    if (siteLine >= method.StartLine && siteLine <= method.EndLine)
                    {
                        matchedDispatchSites.Add(site);
                        analysis.AddTarget(KindRequest, site.TargetFullName, priority: 3, siteLine, RefDispatch);
                    }
                }
            }

            var spanText = maskedCode[start..end];
            CollectMatchesInSpan(
                spanText, start, lineStarts, decl, prodRefs, index, analysis,
                collectTokens: true);

            if (routeContext is { } rc && end <= rawCode.Length)
            {
                var rawSpan = rawCode[start..end];
                foreach (var route in rc.RouteLiterals)
                {
                    var hitIdx = rawSpan.IndexOf($"\"{route}\"", StringComparison.Ordinal);
                    if (hitIdx < 0)
                        hitIdx = rawSpan.IndexOf($"\"{route}?", StringComparison.Ordinal);
                    if (hitIdx >= 0)
                    {
                        var line = LineAt(lineStarts, start + hitIdx);
                        analysis.AddTarget(rc.TargetKind, rc.CanonicalTarget, priority: 2, line, RefTypeReference);
                    }
                }
            }

            results.Add(analysis);
        }

        return results;
    }

    private static Dictionary<string, List<MethodAnalysis>> AnalyzeHelperMethods(
        CSharpScanner.ClassDecl decl,
        List<ParsedMethod> methods,
        string rawCode,
        string maskedCode,
        int[] lineStarts,
        HashSet<string> prodRefs,
        TargetIndex index,
        List<DispatchCallSite>? fileDispatches,
        HashSet<DispatchCallSite> matchedDispatchSites,
        (string CanonicalTarget, string TargetKind, HashSet<string> RouteLiterals)? routeContext = null)
    {
        var list = AnalyzeMethods(
            decl, methods, rawCode, maskedCode, lineStarts, prodRefs, index,
            fileDispatches, matchedDispatchSites, routeContext);
        var map = new Dictionary<string, List<MethodAnalysis>>(StringComparer.Ordinal);
        for (var i = 0; i < methods.Count; i++)
        {
            if (!map.TryGetValue(methods[i].Name, out var bucket))
                map[methods[i].Name] = bucket = new List<MethodAnalysis>();
            bucket.Add(list[i]);
        }
        return map;
    }

    private static void CollectMatchesInSpan(
        string spanText,
        int baseOffset,
        int[] lineStarts,
        CSharpScanner.ClassDecl decl,
        HashSet<string> prodRefs,
        TargetIndex index,
        MethodAnalysis analysis,
        bool collectTokens)
    {
        try
        {
            foreach (Match m in NewObjectRegex().Matches(spanText))
            {
                var raw = NormalizeDotted(m.Groups["type"].Value);
                if (raw.Length == 0) continue;
                var line = LineAt(lineStarts, baseOffset + m.Index);
                foreach (var target in index.ResolveAllKinds(raw, decl, prodRefs))
                    analysis.AddTarget(target.Kind, target.FullName, priority: 2, line, RefInstantiation);
            }

            foreach (Match m in IdentifierRegex().Matches(spanText))
            {
                var raw = NormalizeDotted(m.Groups["name"].Value);
                if (raw.Length == 0) continue;
                if (collectTokens)
                {
                    var dot = raw.IndexOf('.');
                    var firstSegment = dot >= 0 ? raw[..dot] : raw;
                    analysis.IdentifierTokens.Add(firstSegment);
                }

                var line = LineAt(lineStarts, baseOffset + m.Index);
                foreach (var target in index.ResolveAllKinds(raw, decl, prodRefs))
                    analysis.AddTarget(target.Kind, target.FullName, priority: 1, line, RefTypeReference);
            }

            if (collectTokens)
            {
                foreach (Match m in CallIdentifierRegex().Matches(spanText))
                {
                    var name = m.Groups["name"].Value.TrimStart('@');
                    if (name.Length > 0)
                        analysis.CalledMethods.Add(name);
                }
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // Keep discovery bounded and deterministic on pathological inputs.
        }
    }

    private static Dictionary<string, List<TargetMatch>> CollectClassMemberTargets(
        CSharpScanner.ClassDecl decl,
        ParsedClassMembers parsedClass,
        string code,
        string maskedCode,
        SourceMask mask,
        HashSet<string> prodRefs,
        TargetIndex index)
    {
        var members = new List<(string Type, string Name)>();
        DependencyExtractor.ExtractParameters(decl, code, mask, [decl], members);
        ReceiverTypeMapBuilder.CollectClassLevelDeclarations(decl, code, mask, [decl], members);

        foreach (var prop in decl.Properties)
        {
            if (prop.Name.Length > 0 && prop.Type.Length > 0)
                members.Add((prop.Type, prop.Name));
        }

        // Map member name -> resolved targets from its declared type.
        var byMember = new Dictionary<string, List<TargetMatch>>(StringComparer.Ordinal);
        foreach (var (type, name) in members)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(type))
                continue;

            var temp = new MethodAnalysis();
            CollectMatchesInSpan(type, 0, [0], decl, prodRefs, index, temp, collectTokens: false);
            if (temp.Targets.Count == 0)
                continue;

            if (!byMember.TryGetValue(name, out var list))
                byMember[name] = list = new List<TargetMatch>();
            list.AddRange(temp.Targets);
        }

        // Check field declarations with `= new(...)` or `= new Type(...)`, and constructor bodies.
        var instantiatedMembers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var fieldText in parsedClass.FieldSegments)
        {
            CheckTargetTypedNewInSegment(fieldText, instantiatedMembers, byMember, decl, prodRefs, index);
        }
        foreach (var ctor in parsedClass.Constructors)
        {
            if (ctor.ScanSpan.End <= ctor.ScanSpan.Start)
                continue;
            var end = Math.Min(maskedCode.Length, ctor.ScanSpan.End);
            var span = maskedCode[ctor.ScanSpan.Start..end];
            CheckTargetTypedNewInSegment(span, instantiatedMembers, byMember, decl, prodRefs, index);
        }

        foreach (var memberName in instantiatedMembers)
        {
            if (!byMember.TryGetValue(memberName, out var list))
                continue;
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].Priority < 2)
                    list[i] = list[i] with { Priority = 2, RefKind = RefInstantiation };
            }
        }

        return byMember;
    }

    private static void CheckTargetTypedNewInSegment(
        string maskedSegment,
        HashSet<string> instantiatedMembers,
        Dictionary<string, List<TargetMatch>> byMember,
        CSharpScanner.ClassDecl decl,
        HashSet<string> prodRefs,
        TargetIndex index)
    {
        try
        {
            foreach (Match m in TargetTypedAssignmentRegex().Matches(maskedSegment))
            {
                var lhs = m.Groups["lhs"].Value.TrimStart('@');
                if (lhs.Length == 0) continue;
                instantiatedMembers.Add(lhs);

                // Also inspect if `lhs = new ConcreteTarget(...)` names a more specific type
                // than the field's declared interface type.
                var tailStart = m.Index + m.Length;
                var tailLen = Math.Min(200, maskedSegment.Length - tailStart);
                if (tailLen <= 0) continue;
                var tail = maskedSegment[tailStart..(tailStart + tailLen)].TrimStart();
                if (tail.StartsWith('(') || tail.StartsWith('{'))
                    continue;

                var idMatch = IdentifierRegex().Match(tail);
                if (!idMatch.Success || idMatch.Index != 0)
                    continue;

                var raw = NormalizeDotted(idMatch.Groups["name"].Value);
                if (raw.Length == 0) continue;

                foreach (var target in index.ResolveAllKinds(raw, decl, prodRefs))
                {
                    if (!byMember.TryGetValue(lhs, out var list))
                        byMember[lhs] = list = new List<TargetMatch>();
                    list.Add(new TargetMatch(target.Kind, target.FullName, 2, 1, RefInstantiation));
                }
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // Bounded fallback.
        }
    }

    private static ParsedClassMembers ParseClassMembers(
        CSharpScanner.ClassDecl decl,
        CSharpScanner.FileScan scan,
        string code,
        string maskedCode,
        SourceMask mask,
        int[] lineStarts)
    {
        var result = new ParsedClassMembers();
        var end = Math.Min(code.Length, decl.BodySpan.End);
        var openBrace = FindBodyOpenBrace(code, mask, decl.BodySpan.Start, end);
        if (openBrace < 0)
            return result;

        var bodyClose = mask.FindMatchingBrace(openBrace);
        var limit = bodyClose > openBrace ? Math.Min(end, bodyClose) : end;

        var nestedSpans = scan.Classes
            .Where(c => !ReferenceEquals(c, decl) && c.BodySpan.Start > openBrace && c.BodySpan.Start < limit)
            .Select(c => c.BodySpan)
            .Concat(scan.InterfaceDecls
                .Where(i => i.BodySpan.Start > openBrace && i.BodySpan.Start < limit)
                .Select(i => i.BodySpan))
            .OrderBy(s => s.Start)
            .ToList();

        var memberStart = openBrace + 1;
        var parenDepth = 0;
        var bracketDepth = 0;
        var seenEquals = false;
        var seenArrow = false;

        for (var i = openBrace + 1; i < limit; i++)
        {
            var nested = FindNestedAt(nestedSpans, i);
            if (nested.HasValue)
            {
                i = Math.Max(i, Math.Min(limit, nested.Value.End) - 1);
                memberStart = Math.Min(limit, nested.Value.End);
                parenDepth = 0;
                bracketDepth = 0;
                seenEquals = false;
                seenArrow = false;
                continue;
            }

            if (!mask.IsCode(i))
                continue;

            var ch = code[i];
            if (ch == '(')
            {
                parenDepth++;
                continue;
            }
            if (ch == ')')
            {
                if (parenDepth > 0) parenDepth--;
                continue;
            }
            if (ch == '[')
            {
                bracketDepth++;
                continue;
            }
            if (ch == ']')
            {
                if (bracketDepth > 0) bracketDepth--;
                continue;
            }

            if (parenDepth != 0 || bracketDepth != 0)
                continue;

            if (ch == '=')
            {
                if (i + 1 < limit && code[i + 1] == '>')
                {
                    seenArrow = true;
                    i++;
                }
                else if (!(i > 0 && code[i - 1] is '=' or '!' or '<' or '>') &&
                         !(i + 1 < limit && code[i + 1] == '='))
                {
                    seenEquals = true;
                }
                continue;
            }

            if (ch == '{')
            {
                var close = mask.FindMatchingBrace(i);
                if (close <= i) break;

                if (seenEquals || seenArrow)
                {
                    i = close;
                    continue;
                }

                TryRecordMember(decl, maskedCode, lineStarts, memberStart, i, close + 1, isArrow: false, result);
                i = close;
                memberStart = close + 1;
                seenEquals = false;
                seenArrow = false;
                continue;
            }

            if (ch == ';')
            {
                if (seenArrow)
                {
                    TryRecordMember(decl, maskedCode, lineStarts, memberStart, i, i + 1, isArrow: true, result);
                }
                else if (i + 1 > memberStart)
                {
                    result.FieldSegments.Add(maskedCode[memberStart..(i + 1)]);
                }

                memberStart = i + 1;
                seenEquals = false;
                seenArrow = false;
            }
        }

        return result;
    }

    private static void TryRecordMember(
        CSharpScanner.ClassDecl decl,
        string maskedCode,
        int[] lineStarts,
        int headerStart,
        int bodyOrSemiStart,
        int memberEnd,
        bool isArrow,
        ParsedClassMembers target)
    {
        if (bodyOrSemiStart <= headerStart)
            return;

        var rawHeader = maskedCode[headerStart..bodyOrSemiStart];
        if (!TryParseMethodHeader(
                rawHeader,
                headerStart,
                decl.Name,
                isArrow,
                out var attributes,
                out var methodName,
                out var nameOffset,
                out var paramsOpenOffset,
                out var isConstructor))
        {
            return;
        }

        var declLine = LineAt(lineStarts, nameOffset);
        var startLine = LineAt(lineStarts, headerStart);
        var endLine = LineAt(lineStarts, Math.Max(headerStart, memberEnd - 1));
        var scanSpan = (paramsOpenOffset, memberEnd);
        var parsed = new ParsedMethod(methodName, declLine, startLine, endLine, scanSpan);

        if (isConstructor)
        {
            target.Constructors.Add(parsed);
        }
        else if (attributes.Any(IsTestAttribute))
        {
            target.TestMethods.Add(parsed);
        }
        else
        {
            target.HelperMethods.Add(parsed);
        }
    }

    private static bool TryParseMethodHeader(
        string maskedHeader,
        int baseOffset,
        string className,
        bool isArrow,
        out List<string> attributes,
        out string methodName,
        out int nameOffset,
        out int paramsOpenOffset,
        out bool isConstructor)
    {
        attributes = new List<string>();
        methodName = string.Empty;
        nameOffset = baseOffset;
        paramsOpenOffset = baseOffset;
        isConstructor = false;

        var pos = 0;
        while (pos < maskedHeader.Length)
        {
            while (pos < maskedHeader.Length && char.IsWhiteSpace(maskedHeader[pos]))
                pos++;

            if (pos >= maskedHeader.Length || maskedHeader[pos] != '[')
                break;

            var close = FindMatchingBracket(maskedHeader, pos);
            if (close < 0)
                return false;

            var inner = maskedHeader[(pos + 1)..close];
            ExtractAttributeNames(inner, attributes);
            pos = close + 1;
        }

        var sigStart = pos;
        var sigEnd = maskedHeader.Length;
        if (isArrow)
        {
            var arrowIdx = FindTopLevelArrow(maskedHeader, sigStart);
            if (arrowIdx < 0)
                return false;
            sigEnd = arrowIdx;
        }

        if (sigEnd <= sigStart)
            return false;

        var sig = maskedHeader[sigStart..sigEnd];
        var sigOffset = baseOffset + sigStart;

        // Cut off trailing `where` constraints or constructor `: base(...)` / `: this(...)`.
        var effectiveEnd = FindSignatureCutoff(sig);
        var paramOpen = FindMethodParameterListOpen(sig, effectiveEnd);
        if (paramOpen < 0)
            return false;

        var beforeParams = sig[..paramOpen].TrimEnd();
        if (beforeParams.Length == 0)
            return false;

        // Strip method generic type parameters <T1, T2> if present.
        if (beforeParams.EndsWith('>'))
        {
            var angleOpen = FindMatchingOpenAngle(beforeParams, beforeParams.Length - 1);
            if (angleOpen < 0)
                return false;
            beforeParams = beforeParams[..angleOpen].TrimEnd();
        }

        var identEnd = beforeParams.Length;
        var identStart = identEnd;
        while (identStart > 0 && (char.IsLetterOrDigit(beforeParams[identStart - 1]) || beforeParams[identStart - 1] is '_' or '@'))
            identStart--;

        if (identStart == identEnd)
            return false;

        var rawIdent = beforeParams[identStart..identEnd].TrimStart('@');
        if (rawIdent.Length == 0 || char.IsDigit(rawIdent[0]))
            return false;

        if (rawIdent is "this" or "base" or "operator" or "get" or "set" or "init" or "add" or "remove")
            return false;

        methodName = rawIdent;
        nameOffset = sigOffset + identStart;
        paramsOpenOffset = sigOffset + paramOpen;
        isConstructor = string.Equals(methodName, className, StringComparison.Ordinal);
        return true;
    }

    private static int FindSignatureCutoff(string sig)
    {
        var paren = 0;
        var angle = 0;
        for (var i = 0; i < sig.Length; i++)
        {
            var c = sig[i];
            if (c == '(') paren++;
            else if (c == ')' && paren > 0) paren--;
            else if (c == '<') angle++;
            else if (c == '>' && angle > 0) angle--;
            else if (paren == 0 && angle == 0)
            {
                if (c == ':' && !(i + 1 < sig.Length && sig[i + 1] == ':'))
                    return i;
                if (c == 'w' &&
                    i + 5 <= sig.Length &&
                    string.CompareOrdinal(sig, i, "where", 0, 5) == 0 &&
                    (i == 0 || !IsIdentChar(sig[i - 1])) &&
                    (i + 5 == sig.Length || !IsIdentChar(sig[i + 5])))
                {
                    return i;
                }
            }
        }
        return sig.Length;
    }

    private static int FindMethodParameterListOpen(string sig, int limit)
    {
        var paren = 0;
        var lastTopLevelOpen = -1;
        for (var i = 0; i < limit; i++)
        {
            var c = sig[i];
            if (c == '(')
            {
                if (paren == 0)
                    lastTopLevelOpen = i;
                paren++;
            }
            else if (c == ')' && paren > 0)
            {
                paren--;
            }
        }
        return lastTopLevelOpen;
    }

    private static int FindTopLevelArrow(string text, int start)
    {
        var paren = 0;
        var bracket = 0;
        for (var i = start; i + 1 < text.Length; i++)
        {
            var c = text[i];
            if (c == '(') paren++;
            else if (c == ')' && paren > 0) paren--;
            else if (c == '[') bracket++;
            else if (c == ']' && bracket > 0) bracket--;
            else if (paren == 0 && bracket == 0 && c == '=' && text[i + 1] == '>')
                return i;
        }
        return -1;
    }

    private static int FindMatchingBracket(string text, int openIndex)
    {
        var bracket = 0;
        var paren = 0;
        for (var i = openIndex; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '[') bracket++;
            else if (c == ']')
            {
                bracket--;
                if (bracket == 0 && paren == 0) return i;
            }
            else if (c == '(') paren++;
            else if (c == ')' && paren > 0) paren--;
        }
        return -1;
    }

    private static int FindMatchingOpenAngle(string text, int closeIndex)
    {
        var angle = 0;
        for (var i = closeIndex; i >= 0; i--)
        {
            if (text[i] == '>') angle++;
            else if (text[i] == '<')
            {
                angle--;
                if (angle == 0) return i;
            }
        }
        return -1;
    }

    private static void ExtractAttributeNames(string inner, List<string> target)
    {
        var parts = CSharpScanner.SplitTopLevel(inner);
        foreach (var part in parts)
        {
            var spec = part.Trim();
            var colon = spec.IndexOf(':');
            if (colon >= 0 && !(colon + 1 < spec.Length && spec[colon + 1] == ':'))
                spec = spec[(colon + 1)..].TrimStart();

            var paren = spec.IndexOf('(');
            if (paren >= 0) spec = spec[..paren].TrimEnd();
            var angle = spec.IndexOf('<');
            if (angle >= 0) spec = spec[..angle].TrimEnd();

            var simple = CSharpScanner.SimpleName(spec).TrimStart('@');
            if (simple.EndsWith("Attribute", StringComparison.Ordinal) && simple.Length > "Attribute".Length)
                simple = simple[..^"Attribute".Length];

            if (simple.Length > 0)
                target.Add(simple);
        }
    }

    private static bool IsTestAttribute(string attributeName)
        => TestMethodAttributes.Contains(attributeName);

    private static int FindBodyOpenBrace(string code, SourceMask mask, int declarationStart, int end)
    {
        for (var i = declarationStart; i < end; i++)
        {
            if (!mask.IsCode(i)) continue;
            var ch = code[i];
            if (ch == '(')
            {
                var close = DeepSliceSyntax.FindMatchingParen(code, mask, i);
                if (close < i) return -1;
                i = close;
            }
            else if (ch == '{')
            {
                return i;
            }
            else if (ch == ';')
            {
                return -1;
            }
        }
        return -1;
    }

    private static (int Start, int End)? FindNestedAt(List<(int Start, int End)> nestedSpans, int index)
    {
        foreach (var span in nestedSpans)
        {
            if (span.Start == index)
                return span;
        }
        return null;
    }

    private static List<ParsedTestFile> LoadTestSourceFiles(
        AnalysisResult result,
        HashSet<ProjectInfo> testProjects)
    {
        var parsedFiles = new List<ParsedTestFile>();
        if (testProjects.Count == 0 ||
            string.IsNullOrWhiteSpace(result.RootPath) ||
            !Directory.Exists(result.RootPath))
        {
            return parsedFiles;
        }

        var sourcePaths = ProjectFileDiscovery.FindSourceFiles(result.RootPath);
        foreach (var file in sourcePaths)
        {
            var owner = ProjectLocator.FindOwningProject(result.Projects, file);
            if (owner is null || !testProjects.Contains(owner))
                continue;

            string code;
            try
            {
                code = File.ReadAllText(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                continue;
            }

            var mask = SourceMask.Build(code);
            var scan = CSharpScanner.ScanText(code, mask);
            parsedFiles.Add(new ParsedTestFile(new SourceFile(file, code, owner.Name), scan, mask));
        }

        return parsedFiles;
    }

    private static Dictionary<string, List<DispatchCallSite>> BuildTestDispatchMap(AnalysisResult result)
    {
        var map = new Dictionary<string, List<DispatchCallSite>>(StringComparer.OrdinalIgnoreCase);
        foreach (var site in result.DispatchCallSites)
        {
            if (!site.IsTestProject || site.TargetKind != DispatchTargetKind.Request)
                continue;
            if (site.Location is null || string.IsNullOrEmpty(site.Location.File))
                continue;

            var rel = NormalizeRelativePath(site.Location.File, result.RootPath);
            if (!map.TryGetValue(rel, out var list))
                map[rel] = list = new List<DispatchCallSite>();
            list.Add(site);
        }
        return map;
    }

    private static bool HasStrongerOrEqualForClassTarget(
        Dictionary<RefKey, (int Priority, int Line, string RefKind)> collected,
        string projectName,
        string relPath,
        string className,
        string targetFullName)
    {
        foreach (var kv in collected)
        {
            if (kv.Value.Priority >= 3 &&
                string.Equals(kv.Key.ProjectName, projectName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(kv.Key.File, relPath, StringComparison.Ordinal) &&
                string.Equals(kv.Key.ClassName, className, StringComparison.Ordinal) &&
                string.Equals(kv.Key.TargetFullName, targetFullName, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private static void AddOrUpgrade(
        Dictionary<RefKey, (int Priority, int Line, string RefKind)> collected,
        RefKey key,
        int priority,
        int line,
        string refKind)
    {
        if (collected.TryGetValue(key, out var existing))
        {
            if (priority > existing.Priority || (priority == existing.Priority && line < existing.Line))
                collected[key] = (priority, line, refKind);
            return;
        }
        collected[key] = (priority, line, refKind);
    }

    private static bool IsTestProject(ProjectInfo project)
    {
        if (string.Equals(project.Layer, "Tests", StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var pkg in project.PackageReferences)
        {
            if (TryClassifyFramework(pkg, out _))
                return true;
        }
        return false;
    }

    private static bool TryClassifyFramework(string packageId, out string framework)
    {
        if (packageId.Equals("xunit", StringComparison.OrdinalIgnoreCase) ||
            packageId.Equals("xunit.v3", StringComparison.OrdinalIgnoreCase) ||
            packageId.Equals("xunit.core", StringComparison.OrdinalIgnoreCase) ||
            packageId.Equals("xunit.v3.core", StringComparison.OrdinalIgnoreCase) ||
            packageId.Equals("Xunit.SkippableFact", StringComparison.OrdinalIgnoreCase))
        {
            framework = "xUnit";
            return true;
        }

        if (packageId.Equals("NUnit", StringComparison.OrdinalIgnoreCase) ||
            packageId.Equals("NUnit.Framework", StringComparison.OrdinalIgnoreCase))
        {
            framework = "NUnit";
            return true;
        }

        if (packageId.Equals("MSTest.TestFramework", StringComparison.OrdinalIgnoreCase) ||
            packageId.Equals("MSTest", StringComparison.OrdinalIgnoreCase))
        {
            framework = "MSTest";
            return true;
        }

        if (packageId.Equals("TUnit", StringComparison.OrdinalIgnoreCase) ||
            packageId.Equals("TUnit.Core", StringComparison.OrdinalIgnoreCase) ||
            packageId.Equals("TUnit.Engine", StringComparison.OrdinalIgnoreCase))
        {
            framework = "TUnit";
            return true;
        }

        if (packageId.Equals("Fixie", StringComparison.OrdinalIgnoreCase) ||
            packageId.StartsWith("Fixie.", StringComparison.OrdinalIgnoreCase))
        {
            framework = "Fixie";
            return true;
        }

        framework = string.Empty;
        return false;
    }

    private static bool TryClassifyMocking(string packageId, out string library)
    {
        if (HasPackagePrefix(packageId, "Moq"))
        {
            library = "Moq";
            return true;
        }
        if (HasPackagePrefix(packageId, "NSubstitute"))
        {
            library = "NSubstitute";
            return true;
        }
        if (HasPackagePrefix(packageId, "FakeItEasy"))
        {
            library = "FakeItEasy";
            return true;
        }
        library = string.Empty;
        return false;
    }

    private static bool TryClassifyAssertion(string packageId, out string library)
    {
        if (HasPackagePrefix(packageId, "FluentAssertions"))
        {
            library = "FluentAssertions";
            return true;
        }
        if (HasPackagePrefix(packageId, "Shouldly"))
        {
            library = "Shouldly";
            return true;
        }
        if (HasPackagePrefix(packageId, "AwesomeAssertions"))
        {
            library = "AwesomeAssertions";
            return true;
        }
        library = string.Empty;
        return false;
    }

    private static bool HasPackagePrefix(string packageId, string name)
        => packageId.Equals(name, StringComparison.OrdinalIgnoreCase) ||
           packageId.StartsWith(name + ".", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, List<ProjectInfo>> BuildProjectsByName(IReadOnlyList<ProjectInfo> projects)
    {
        var map = new Dictionary<string, List<ProjectInfo>>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in projects)
        {
            if (!map.TryGetValue(project.Name, out var list))
                map[project.Name] = list = new List<ProjectInfo>();
            list.Add(project);
        }
        return map;
    }

    private static string[] OrderDistinct(IEnumerable<string> values)
        => values
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
            .ThenBy(v => v, StringComparer.Ordinal)
            .ToArray();

    private static string NormalizeRelativePath(string? filePath, string rootPath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return string.Empty;

        if (!Path.IsPathRooted(filePath))
            return filePath.Replace('\\', '/');

        if (!string.IsNullOrWhiteSpace(rootPath))
        {
            return SourceLocation.FromLine(filePath, rootPath, 1, 1)?.File ??
                   filePath.Replace('\\', '/');
        }

        return filePath.Replace('\\', '/');
    }

    private static string NormalizeDotted(string raw)
    {
        var compact = WhitespaceRegex().Replace(raw.Trim(), string.Empty);
        var parts = compact.Split('.', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
            parts[i] = parts[i].TrimStart('@');
        return string.Join('.', parts);
    }

    private static int[] ComputeLineStarts(string code)
    {
        var starts = new List<int> { 0 };
        for (var i = 0; i < code.Length; i++)
        {
            if (code[i] == '\n')
                starts.Add(i + 1);
        }
        return starts.ToArray();
    }

    private static int LineAt(int[] lineStarts, int offset)
    {
        var idx = Array.BinarySearch(lineStarts, Math.Max(0, offset));
        return idx >= 0 ? idx + 1 : Math.Max(1, ~idx);
    }

    private static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private sealed record ParsedTestFile(SourceFile Source, CSharpScanner.FileScan Scan, SourceMask Mask);

    private sealed record ParsedMethod(
        string Name,
        int DeclarationLine,
        int StartLine,
        int EndLine,
        (int Start, int End) ScanSpan);

    private sealed class ParsedClassMembers
    {
        public List<ParsedMethod> TestMethods { get; } = new();
        public List<ParsedMethod> HelperMethods { get; } = new();
        public List<ParsedMethod> Constructors { get; } = new();
        public List<string> FieldSegments { get; } = new();
    }

    private sealed record TargetMatch(string Kind, string FullName, int Priority, int Line, string RefKind);

    private sealed class MethodAnalysis
    {
        public List<TargetMatch> Targets { get; } = new();
        public HashSet<string> IdentifierTokens { get; } = new(StringComparer.Ordinal);
        public HashSet<string> CalledMethods { get; } = new(StringComparer.Ordinal);

        public void AddTarget(string kind, string fullName, int priority, int line, string refKind)
        {
            for (var i = 0; i < Targets.Count; i++)
            {
                var current = Targets[i];
                if (current.Kind == kind && string.Equals(current.FullName, fullName, StringComparison.Ordinal))
                {
                    if (priority > current.Priority || (priority == current.Priority && line < current.Line))
                        Targets[i] = new TargetMatch(kind, fullName, priority, line, refKind);
                    return;
                }
            }
            Targets.Add(new TargetMatch(kind, fullName, priority, line, refKind));
        }
    }

    private readonly record struct RefKey(
        string ProjectName,
        string File,
        string ClassName,
        string MethodName,
        string TargetFullName,
        string TargetKind);

    private sealed record TargetEntry(string Kind, string FullName, string SimpleName, string? ProjectName);

    private sealed class TargetIndex
    {
        private readonly Dictionary<string, List<TargetEntry>> _byFull = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<TargetEntry>> _bySimple = new(StringComparer.Ordinal);

        public static TargetIndex Create(AnalysisResult result, CqrsRequest? extraRequest)
        {
            var index = new TargetIndex();
            var seen = new HashSet<(string Kind, string FullName)>();

            foreach (var r in result.Requests)
                index.Add(seen, KindRequest, r.FullName, r.Name, r.ProjectName);
            if (extraRequest is not null)
                index.Add(seen, KindRequest, extraRequest.FullName, extraRequest.Name, extraRequest.ProjectName);
            foreach (var h in result.Handlers)
                index.Add(seen, KindHandler, h.FullName, h.Name, h.ProjectName);
            foreach (var v in result.Validators)
                index.Add(seen, KindValidator, v.FullName, v.Name, v.ProjectName);

            return index;
        }

        public void AddEntry(
            HashSet<(string Kind, string FullName)> seen,
            string kind,
            string fullName,
            string simpleName,
            string? projectName)
            => Add(seen, kind, fullName, simpleName, projectName);

        private void Add(
            HashSet<(string Kind, string FullName)> seen,
            string kind,
            string fullName,
            string simpleName,
            string? projectName)
        {
            if (string.IsNullOrEmpty(fullName))
                return;
            if (!seen.Add((kind, fullName)))
                return;

            var entry = new TargetEntry(kind, fullName, simpleName, projectName);
            AddToMap(_byFull, fullName, entry);
            AddToMap(_bySimple, simpleName, entry);
        }

        public IReadOnlyList<TargetEntry> ResolveAllKinds(
            string dottedName,
            CSharpScanner.ClassDecl enclosingClass,
            IReadOnlySet<string> referencedProjects)
        {
            if (_byFull.Count == 0)
                return Array.Empty<TargetEntry>();

            var parts = dottedName.Split('.', StringSplitOptions.RemoveEmptyEntries);
            for (var len = parts.Length; len >= 1; len--)
            {
                var candidate = len == parts.Length
                    ? dottedName
                    : string.Join('.', parts, 0, len);

                var resolved = ResolveCandidate(candidate, enclosingClass, referencedProjects);
                if (resolved.Count > 0)
                    return resolved;
            }

            return Array.Empty<TargetEntry>();
        }

        private IReadOnlyList<TargetEntry> ResolveCandidate(
            string candidate,
            CSharpScanner.ClassDecl enclosingClass,
            IReadOnlySet<string> referencedProjects)
        {
            var expanded = ExpandAlias(candidate, enclosingClass.Usings);
            if (expanded is not null && _byFull.TryGetValue(expanded, out var aliasHits))
                return PickUnambiguousByKind(aliasHits, referencedProjects);

            // Check enclosing containing types and namespaces from innermost to outermost.
            var scopeCandidates = new List<string>();
            if (enclosingClass.ContainingTypes.Count > 0)
            {
                var prefix = string.IsNullOrEmpty(enclosingClass.Namespace)
                    ? string.Join('.', enclosingClass.ContainingTypes)
                    : $"{enclosingClass.Namespace}.{string.Join('.', enclosingClass.ContainingTypes)}";
                var end = prefix.Length;
                for (var count = enclosingClass.ContainingTypes.Count; count >= 1; count--)
                {
                    scopeCandidates.Add(string.Concat(prefix.AsSpan(0, end), ".", candidate));
                    var dot = prefix.LastIndexOf('.', end - 1);
                    if (dot < 0) break;
                    end = dot;
                }
            }

            var nsEnd = enclosingClass.Namespace.Length;
            while (nsEnd > 0)
            {
                scopeCandidates.Add(string.Concat(enclosingClass.Namespace.AsSpan(0, nsEnd), ".", candidate));
                var dot = enclosingClass.Namespace.LastIndexOf('.', nsEnd - 1);
                if (dot < 0) break;
                nsEnd = dot;
            }

            scopeCandidates.Add(candidate);

            foreach (var scoped in scopeCandidates)
            {
                if (_byFull.TryGetValue(scoped, out var hits))
                    return PickUnambiguousByKind(hits, referencedProjects);
            }

            // Imported namespaces (from file usings, static usings, and project global usings).
            var usingHits = new List<TargetEntry>();
            var seenNamespaces = new HashSet<string>(StringComparer.Ordinal);
            foreach (var directive in enclosingClass.Usings)
            {
                if (directive.IsStatic && !string.IsNullOrEmpty(directive.Target))
                {
                    if (seenNamespaces.Add(directive.Target) &&
                        _byFull.TryGetValue($"{directive.Target}.{candidate}", out var staticHits))
                    {
                        foreach (var hit in PickUnambiguousByKind(staticHits, referencedProjects))
                            usingHits.Add(hit);
                    }
                    continue;
                }

                foreach (var ns in directive.NamespaceCandidates())
                {
                    if (!seenNamespaces.Add(ns)) continue;
                    if (_byFull.TryGetValue($"{ns}.{candidate}", out var hits))
                    {
                        foreach (var hit in PickUnambiguousByKind(hits, referencedProjects))
                            usingHits.Add(hit);
                    }
                }
            }

            if (usingHits.Count > 0)
                return PickUnambiguousByKind(usingHits, referencedProjects);

            // Unambiguous simple-name fallback (only for bare unqualified identifiers).
            if (!candidate.Contains('.') && _bySimple.TryGetValue(candidate, out var simpleHits))
                return PickUnambiguousByKind(simpleHits, referencedProjects);

            return Array.Empty<TargetEntry>();
        }

        private static string? ExpandAlias(string candidate, IReadOnlyList<UsingDirective> usings)
        {
            foreach (var directive in usings)
            {
                if (directive.Alias is null) continue;
                var target = CSharpScanner.ParseTypeReference(directive.Target).Name;
                if (string.Equals(candidate, directive.Alias, StringComparison.Ordinal))
                    return target;
                if (candidate.StartsWith(directive.Alias + ".", StringComparison.Ordinal))
                    return target + candidate[directive.Alias.Length..];
            }
            return null;
        }

        private static IReadOnlyList<TargetEntry> PickUnambiguousByKind(
            List<TargetEntry> candidates,
            IReadOnlySet<string> referencedProjects)
        {
            if (candidates.Count == 1)
                return candidates;

            var results = new List<TargetEntry>();
            foreach (var group in candidates.GroupBy(c => c.Kind, StringComparer.Ordinal))
            {
                var distinctFull = group
                    .GroupBy(c => c.FullName, StringComparer.Ordinal)
                    .Select(g => g.First())
                    .ToList();

                if (distinctFull.Count == 1)
                {
                    results.Add(distinctFull[0]);
                    continue;
                }

                if (referencedProjects.Count > 0)
                {
                    var scoped = distinctFull
                        .Where(c => c.ProjectName is not null && referencedProjects.Contains(c.ProjectName))
                        .ToList();
                    if (scoped.Count == 1)
                        results.Add(scoped[0]);
                }
            }

            return results;
        }

        private static void AddToMap(Dictionary<string, List<TargetEntry>> map, string key, TargetEntry entry)
        {
            if (!map.TryGetValue(key, out var list))
                map[key] = list = new List<TargetEntry>();
            list.Add(entry);
        }
    }

    private static string? ResolveProjectName(AnalysisResult result, string? projectName)
    {
        if (string.IsNullOrWhiteSpace(projectName))
            return null;

        var trimmed = projectName.Trim();
        var normalized = trimmed.Replace('\\', '/');
        if (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..];

        var match = result.Projects.FirstOrDefault(p =>
            p.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(NormalizeRelativePath(p.FilePath, result.RootPath), normalized, StringComparison.OrdinalIgnoreCase));

        return match?.Name ?? (trimmed.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ? null : trimmed);
    }

    private static CqrsRequest[] FindMatchingRequestsForTarget(
        AnalysisResult result,
        string query,
        string? scopedProject)
    {
        var matches = result.Requests
            .Where(r => string.Equals(r.FullName, query, StringComparison.Ordinal) ||
                        (!query.Contains('.', StringComparison.Ordinal) &&
                         string.Equals(r.Name, query, StringComparison.Ordinal)))
            .Where(r => scopedProject is null ||
                        string.Equals(r.ProjectName, scopedProject, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return matches;
    }

    private static SystemEntrypointInfo? FindMatchingEntrypointForTarget(
        AnalysisResult result,
        string query,
        string? scopedProject)
    {
        var candidates = result.SystemEntrypoints
            .Where(e => scopedProject is null ||
                        string.Equals(e.ProjectName, scopedProject, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var exact = candidates.Where(e =>
            string.Equals(e.Name, query, StringComparison.Ordinal) ||
            string.Equals(e.RouteOrTrigger, query, StringComparison.Ordinal) ||
            string.Equals(e.DisplayLabel, query, StringComparison.Ordinal) ||
            string.Equals($"{e.Name} ({e.RouteOrTrigger})", query, StringComparison.Ordinal))
            .ToArray();
        if (exact.Length > 0)
            return exact[0];

        var ci = candidates.Where(e =>
            string.Equals(e.Name, query, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(e.RouteOrTrigger, query, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(e.DisplayLabel, query, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return ci.Length >= 1 ? ci[0] : null;
    }

    private static void RegisterEntrypointTokens(
        TargetIndex index,
        HashSet<(string Kind, string FullName)> seen,
        SystemEntrypointInfo ep,
        string canonicalTarget,
        string? projectName,
        HashSet<string> routeLiterals)
    {
        if (!string.IsNullOrWhiteSpace(ep.Name) &&
            !ep.Name.Contains(' ', StringComparison.Ordinal) &&
            ep.Name is not ("MapGet" or "MapPost" or "MapPut" or "MapDelete" or "MapPatch"))
        {
            index.AddEntry(seen, KindEntrypoint, ep.Name, ep.Name, projectName);

            var dot = ep.Name.LastIndexOf('.');
            if (dot > 0 && dot < ep.Name.Length - 1)
            {
                var classPart = CSharpScanner.SimpleName(ep.Name[..dot]);
                var methodPart = ep.Name[(dot + 1)..];
                if (!IsTrivialTargetToken(classPart) && classPart.Length >= 3)
                    index.AddEntry(seen, KindEntrypoint, classPart, classPart, projectName);
                if (!IsTrivialTargetToken(methodPart) && methodPart.Length >= 4)
                    index.AddEntry(seen, KindEntrypoint, methodPart, methodPart, projectName);
            }
        }

        if (ep.Kind is SystemEntrypointKind.Http or SystemEntrypointKind.Blazor or SystemEntrypointKind.RazorPage)
        {
            if (!string.IsNullOrWhiteSpace(ep.RouteOrTrigger) &&
                ep.RouteOrTrigger.StartsWith('/') &&
                ep.RouteOrTrigger.Length > 1)
            {
                routeLiterals.Add(ep.RouteOrTrigger);
            }
        }
        else if (ep.Kind == SystemEntrypointKind.MessageConsumer &&
                 !string.IsNullOrWhiteSpace(ep.RouteOrTrigger) &&
                 !ep.RouteOrTrigger.Contains(' ', StringComparison.Ordinal) &&
                 !ep.RouteOrTrigger.Contains('/', StringComparison.Ordinal))
        {
            var simpleMsg = CSharpScanner.SimpleName(ep.RouteOrTrigger);
            index.AddEntry(seen, KindEntrypoint, ep.RouteOrTrigger,
                IsTrivialTargetToken(simpleMsg) ? ep.RouteOrTrigger : simpleMsg, projectName);
        }

        if (!canonicalTarget.Contains(' ', StringComparison.Ordinal) &&
            !canonicalTarget.StartsWith('/') &&
            !string.Equals(canonicalTarget, ep.Name, StringComparison.Ordinal))
        {
            index.AddEntry(seen, KindEntrypoint, canonicalTarget, canonicalTarget, projectName);
        }
    }

    private static bool TrySplitMethodTarget(
        string query,
        AnalysisResult result,
        string? scopedProject,
        out string classPart,
        out string methodPart)
    {
        classPart = string.Empty;
        methodPart = string.Empty;

        if (query.Contains(' ', StringComparison.Ordinal) || query.StartsWith('/'))
            return false;

        var dot = query.LastIndexOf('.');
        if (dot <= 0 || dot >= query.Length - 1)
            return false;

        if (IsKnownTypeInModel(result, query))
            return false;

        var left = query[..dot];
        if (left.Contains('.', StringComparison.Ordinal))
            return false;

        if (IsProjectNameSegment(left, scopedProject))
            return false;

        classPart = left;
        methodPart = query[(dot + 1)..];
        return true;
    }

    private static bool IsKnownTypeInModel(AnalysisResult result, string fullName)
    {
        foreach (var sr in result.ServiceResolutions)
        {
            if (string.Equals(sr.Implementation, fullName, StringComparison.Ordinal) ||
                string.Equals(sr.InterfaceName, fullName, StringComparison.Ordinal))
                return true;
        }
        foreach (var edge in result.ServiceCallEdges)
        {
            if (string.Equals(edge.From, fullName, StringComparison.Ordinal) ||
                string.Equals(edge.To, fullName, StringComparison.Ordinal))
                return true;
        }
        foreach (var edge in result.ResolvedEdges)
        {
            if (string.Equals(edge.From, fullName, StringComparison.Ordinal) ||
                string.Equals(edge.To, fullName, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static bool IsProjectNameSegment(string token, string? scopedProject)
    {
        if (string.IsNullOrWhiteSpace(scopedProject))
            return false;

        return scopedProject
            .Split(['.', '-', '_'], StringSplitOptions.RemoveEmptyEntries)
            .Any(seg => string.Equals(seg, token, StringComparison.OrdinalIgnoreCase));
    }

    private static Dictionary<string, HashSet<string>> BuildTransitiveProductionRefs(
        AnalysisResult result,
        IReadOnlyList<TestProjectInfo> testProjects)
    {
        var projectsByName = BuildProjectsByName(result.Projects);
        var map = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var tp in testProjects)
        {
            var reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>();
            foreach (var direct in tp.ReferencedProductionProjects)
            {
                if (reachable.Add(direct))
                    queue.Enqueue(direct);
            }

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!projectsByName.TryGetValue(current, out var list))
                    continue;

                foreach (var proj in list)
                {
                    foreach (var next in proj.ReferenceNames)
                    {
                        if (reachable.Add(next))
                            queue.Enqueue(next);
                    }
                }
            }

            map[tp.Name] = reachable;
        }

        return map;
    }
}
