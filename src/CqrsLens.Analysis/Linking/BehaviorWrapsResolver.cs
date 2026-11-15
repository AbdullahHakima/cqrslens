using System.Text.RegularExpressions;

namespace CqrsLens;

/// <summary>
/// Evaluates F0 §4.3 <see cref="RelationKind.Wraps"/> applicability for discovered pipeline
/// behaviors: (1) closed request marker binding, (2) open-generic registration whose
/// generic constraints are statically satisfied, otherwise (3) recorded in
/// <see cref="UnprovenPopulations.UnestablishedBehaviors"/> without emitting edges.
/// </summary>
internal static partial class BehaviorWrapsResolver
{
    [GeneratedRegex(@"\.(?:Scan|RegisterAssemblyTypes)\s*\(", RegexOptions.Singleline)]
    private static partial Regex AssemblyScanRegex();

    internal static void Resolve(SourceWorkspace workspace, AnalysisResult result)
    {
        if (result.Behaviors.Count == 0)
            return;

        var requestIndex = new LinkIndex<CqrsRequest>(
            result.Requests,
            r => r.FullName,
            r => r.Name,
            r => r.ProjectName);

        var classBaseTypesByFull = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
        var hasAssemblyScanning = false;

        foreach (var file in workspace.Files)
        {
            if (!hasAssemblyScanning && file.Code.Length > 0 && AssemblyScanRegex().IsMatch(file.Code))
                hasAssemblyScanning = true;
        }

        foreach (var parsed in workspace.SuccessfullyParsed)
        {
            foreach (var decl in parsed.Scan!.Classes)
            {
                var set = new HashSet<string>(StringComparer.Ordinal);
                foreach (var b in decl.BaseTypeNames)
                    set.Add(b);
                foreach (var r in decl.BaseRawNames)
                    set.Add(r);
                classBaseTypesByFull.TryAdd(decl.FullName, set);
            }
        }

        foreach (var behavior in result.Behaviors)
        {
            var behaviorId = behavior.SpineId ?? result.IdentityResolver.ResolveComponentId(
                behavior.ProjectName, behavior.Namespace, behavior.ContainingTypes, behavior.Name, behavior.GenericArity);
            var behaviorRef = NodeReference.ForComponent(behaviorId);

            // (1) Closed over a specific request in its marker binding.
            if (!string.IsNullOrWhiteSpace(behavior.ClosedRequestTarget))
            {
                var target = requestIndex.Resolve(
                    behavior.ClosedRequestTarget,
                    behavior.ClosedRequestNamespace,
                    behavior.Namespace,
                    behavior.ImportedNamespaces,
                    behavior.ProjectName);

                if (target is null)
                {
                    result.Spine.Unproven.AddUnestablishedBehavior(new UnestablishedBehaviorEntry(
                        behaviorId,
                        behavior.FullName,
                        UnestablishedBehaviorReason.ClosedFormUnknown,
                        behavior.RegistrationSite,
                        behavior.ProjectName,
                        behavior.Location));
                    continue;
                }

                var reqId = target.SpineId ?? result.IdentityResolver.ResolveComponentId(
                    target.ProjectName, target.Namespace, target.ContainingTypes, target.Name);
                result.Spine.AddOrMergeRelation(
                    RelationKind.Wraps,
                    behaviorRef,
                    NodeReference.ForComponent(reqId),
                    SpineIdentityResolver.SyntheticOccurrence(
                        EvidenceRule.BehaviorClosedBinding,
                        EvidenceOrigin.Declaration,
                        behavior.Location,
                        EvidenceStrength.Proven,
                        scopeProjectName: behavior.ProjectName));

                if (behavior.RegistrationLocation is not null)
                {
                    result.Spine.AddOrMergeRelation(
                        RelationKind.Wraps,
                        behaviorRef,
                        NodeReference.ForComponent(reqId),
                        SpineIdentityResolver.SyntheticOccurrence(
                            EvidenceRule.BehaviorClosedBinding,
                            EvidenceOrigin.Registration,
                            behavior.RegistrationLocation,
                            EvidenceStrength.Proven,
                            scopeProjectName: behavior.ProjectName));
                }

                continue;
            }

            // (2) Registered as an open generic with statically evaluable constraints.
            if (behavior.IsOpenGenericRegistration && behavior.RegistrationLocation is not null)
            {
                if (!TryEvaluateConstraints(behavior.RequestTypeConstraints, result.Requests, classBaseTypesByFull, out var matchingRequests))
                {
                    result.Spine.Unproven.AddUnestablishedBehavior(new UnestablishedBehaviorEntry(
                        behaviorId,
                        behavior.FullName,
                        UnestablishedBehaviorReason.GenericConstraintsNotEvaluated,
                        behavior.RegistrationSite,
                        behavior.ProjectName,
                        behavior.Location));
                    continue;
                }

                foreach (var req in matchingRequests)
                {
                    var reqId = req.SpineId ?? result.IdentityResolver.ResolveComponentId(
                        req.ProjectName, req.Namespace, req.ContainingTypes, req.Name);
                    result.Spine.AddOrMergeRelation(
                        RelationKind.Wraps,
                        behaviorRef,
                        NodeReference.ForComponent(reqId),
                        new EvidenceOccurrence(
                            EvidenceRule.BehaviorOpenGenericRegistration,
                            EvidenceOrigin.Registration,
                            behavior.RegistrationLocation,
                            EvidenceStrength.Proven,
                            scopeProjectName: behavior.ProjectName));
                }

                continue;
            }

            // (3) Applicability cannot be established -> record in unproven population only.
            var reason = hasAssemblyScanning && behavior.RegistrationSite is null
                ? UnestablishedBehaviorReason.AssemblyScanningOrExternal
                : UnestablishedBehaviorReason.NoRegistrationSite;

            result.Spine.Unproven.AddUnestablishedBehavior(new UnestablishedBehaviorEntry(
                behaviorId,
                behavior.FullName,
                reason,
                behavior.RegistrationSite,
                behavior.ProjectName,
                behavior.Location));
        }
    }

    private static bool TryEvaluateConstraints(
        IReadOnlyList<string> constraints,
        IReadOnlyList<CqrsRequest> requests,
        IReadOnlyDictionary<string, IReadOnlySet<string>> classBaseTypesByFull,
        out List<CqrsRequest> matches)
    {
        matches = new List<CqrsRequest>();

        var requiredInterfaces = new List<string>();
        foreach (var raw in constraints)
        {
            var c = raw.Trim();
            if (c is "notnull" or "class" or "class?" or "new()" or "IBaseRequest" or "IRequest")
                continue;
            if (c.StartsWith("IRequest<", StringComparison.Ordinal) ||
                c.EndsWith(".IBaseRequest", StringComparison.Ordinal) ||
                c.EndsWith(".IRequest", StringComparison.Ordinal))
                continue;

            // Generics/value-type constraints cannot be evaluated purely from base names.
            if (c.Contains('<') || c is "struct" or "unmanaged")
                return false;

            requiredInterfaces.Add(CSharpScanner.SimpleName(c));
        }

        if (requiredInterfaces.Count == 0)
        {
            matches.AddRange(requests);
            return true;
        }

        foreach (var req in requests)
        {
            var allSatisfied = true;
            classBaseTypesByFull.TryGetValue(req.FullName, out var baseTypes);
            foreach (var iface in requiredInterfaces)
            {
                var hasViaMarker = string.Equals(req.ViaMarker, iface, StringComparison.Ordinal);
                var hasBase = baseTypes is not null && baseTypes.Contains(iface);
                if (!hasViaMarker && !hasBase)
                {
                    allSatisfied = false;
                    break;
                }
            }

            if (allSatisfied)
                matches.Add(req);
        }

        return matches.Count > 0;
    }
}
