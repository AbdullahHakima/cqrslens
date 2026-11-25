using System.Text;
using static CqrsLens.AgentReportLimits;

namespace CqrsLens;

/// <summary>
/// Verifies a structured design claim against the scan model and fails closed. The agent
/// proposes, this verifier decides and the verdict never depends on judgement inside the call:
/// a claim is <c>supported</c> only when a model fact proves it, <c>not supported</c> only when a
/// model fact proves the opposite, and <c>not determinable</c> otherwise (unresolved dispatch,
/// runtime-typed send, package-opaque wrapper, ambiguity, incomplete scan, or a relation the
/// model does not record).
/// </summary>
internal static class AgentClaimVerifier
{
    private sealed record Shape(string Kind, string Name, string FullName, string? ProjectName, SourceLocation? Location);

    private enum Match
    {
        Found,
        Ambiguous,
        Absent,
    }

    internal static ClaimCheck Verify(AnalysisResult result, string? subject, string? relation, string? claimObject)
    {
        ArgumentNullException.ThrowIfNull(result);

        var subjectText = (subject ?? "").Trim();
        var relationText = (relation ?? "").Trim();
        var objectText = string.IsNullOrWhiteSpace(claimObject) ? null : claimObject.Trim();

        if (subjectText.Length == 0)
            return NotDeterminable(subjectText, relationText, objectText, "invalid claim",
                "subject is required.");

        if (relationText.Length == 0)
            return NotDeterminable(subjectText, relationText, objectText, "invalid claim",
                "relation is required.");

        if (!AgentClaimVocabulary.TryResolve(relationText, out var spec, out _))
        {
            return NotDeterminable(subjectText, relationText, objectText, "outside the scan model",
                $"relation \"{relationText}\" is not in the verify_claim vocabulary, so the scan model cannot prove or refute it. " +
                $"Known relations: {AgentClaimVocabulary.KnownRelations}.");
        }

        if (spec.ObjectRequired && objectText is null)
        {
            return NotDeterminable(subjectText, relationText, objectText, "invalid claim",
                $"relation \"{spec.Display}\" requires an object.");
        }

        var shapes = AllShapes(result);

        return spec.Id switch
        {
            "has handler" or "has no handler" => VerifyHandlerLink(result, shapes, spec, subjectText, objectText),
            "has validator" or "has no validator" => VerifyValidatorLink(result, shapes, spec, subjectText, objectText),
            "publishes" or "does not publish" => VerifyPublishes(result, shapes, spec, subjectText, objectText),
            "handled notification" or "does not handle notification" =>
                VerifyHandlesNotification(result, shapes, spec, subjectText, objectText),
            "uses DbContext" or "does not use DbContext" => VerifyUsesDbContext(result, shapes, spec, subjectText, objectText),
            "depends on" or "does not depend on" => VerifyDependsOn(result, shapes, spec, subjectText, objectText),
            "belongs to layer" or "does not belong to layer" => VerifyLayer(result, shapes, spec, subjectText, objectText),
            "dispatched by" or "not dispatched by" => VerifyDispatchedBy(result, shapes, spec, subjectText, objectText),
            "calls service" or "does not call service" => VerifyCallsService(result, shapes, spec, subjectText, objectText),
            "references project" or "does not reference project" =>
                VerifyReferencesProject(result, shapes, spec, subjectText, objectText),
            "communicates with" or "does not communicate with" =>
                VerifyCommunicatesWith(result, shapes, spec, subjectText, objectText),
            "touches entity" or "does not touch entity" => VerifyTouchesEntity(result, shapes, spec, subjectText, objectText),
            "has test reference" or "has no test reference" =>
                VerifyHasTestReference(result, shapes, spec, subjectText, objectText),
            _ => NotDeterminable(subjectText, relationText, objectText, "outside the scan model",
                $"relation \"{relationText}\" is not in the verify_claim vocabulary."),
        };
    }

    // ---------------------------------------------------------------- Request ⇄ handler

    private static ClaimCheck VerifyHandlerLink(
        AnalysisResult result, IReadOnlyList<Shape> shapes, ClaimRelation spec, string subject, string? claimed)
    {
        var (outcome, requestShape) = ResolveShape(shapes, subject, "request");
        if (outcome != Match.Found)
            return SubjectNotResolved(result, subject, spec, claimed, "request");

        var request = FindUnique(result.Requests, subject)!;
        var linked = request.Handlers;
        var list = linked.Count == 0 ? "none" : string.Join(", ", linked.Take(MaxListItems).Select(n => $"`{n}`"));

        if (claimed is null)
        {
            if (spec.IsAbsence)
                return linked.Count == 0
                    ? Supported(subject, spec, claimed, "no handler is linked to the request in the scan model",
                        EvidenceLine($"request `{request.Name}`{Loc(request.Location)} lists no handlers"))
                    : NotSupported(subject, spec, claimed, "the scan model links handler(s) to the request",
                        EvidenceLine($"request `{request.Name}`{Loc(request.Location)} lists handlers: {list}"));

            return linked.Count > 0
                ? Supported(subject, spec, claimed, "the scan model links handler(s) to the request",
                    EvidenceLine($"request `{request.Name}`{Loc(request.Location)} lists handlers: {list}"))
                : NotSupported(subject, spec, claimed, "the scan model links no handler to the request",
                    EvidenceLine($"request `{request.Name}`{Loc(request.Location)} lists no handlers"));
        }

        var matches = linked.Where(h => NamesMatch(h, claimed)).ToList();
        var objectShape = FindShape(shapes, claimed, "handler");

        if (!spec.IsAbsence)
        {
            return matches.Count > 0
                ? Supported(subject, spec, claimed, "handler is linked to the request in the scan model",
                    EvidenceLine($"request `{request.Name}`{Loc(request.Location)} lists handler `{matches[0]}`"),
                    objectShape is null ? "" : EvidenceLine($"handler `{objectShape.Name}`{Loc(objectShape.Location)}"))
                : NotSupported(subject, spec, claimed,
                    objectShape is null
                        ? "no handler by that name is present anywhere in the scanned model"
                        : "the named handler is not linked to this request in the scan model",
                    EvidenceLine($"request `{request.Name}`{Loc(request.Location)} lists handlers: {list}"),
                    objectShape is null
                        ? EvidenceLine($"no handler named `{claimed}` is present in the scanned model")
                        : EvidenceLine($"handler `{objectShape.Name}`{Loc(objectShape.Location)} is not among them"));
        }

        return matches.Count == 0
            ? Supported(subject, spec, claimed, "the named handler is not linked to the request",
                EvidenceLine($"request `{request.Name}`{Loc(request.Location)} lists handlers: {list}"))
            : NotSupported(subject, spec, claimed, "the scan model links the named handler to the request",
                EvidenceLine($"request `{request.Name}`{Loc(request.Location)} lists handler `{matches[0]}`"));
    }

    // ---------------------------------------------------------------- Request ⇄ validator

    private static ClaimCheck VerifyValidatorLink(
        AnalysisResult result, IReadOnlyList<Shape> shapes, ClaimRelation spec, string subject, string? claimed)
    {
        var (outcome, requestShape) = ResolveShape(shapes, subject, "request");
        if (outcome != Match.Found)
            return SubjectNotResolved(result, subject, spec, claimed, "request");

        var request = FindUnique(result.Requests, subject)!;
        var linked = request.Validators;
        var list = linked.Count == 0 ? "none" : string.Join(", ", linked.Take(MaxListItems).Select(n => $"`{n}`"));

        if (claimed is null)
        {
            if (spec.IsAbsence)
                return linked.Count == 0
                    ? Supported(subject, spec, claimed, "no validator is linked to the request in the scan model",
                        EvidenceLine($"request `{request.Name}`{Loc(request.Location)} lists no validators"))
                    : NotSupported(subject, spec, claimed, "the scan model links validator(s) to the request",
                        EvidenceLine($"request `{request.Name}`{Loc(request.Location)} lists validators: {list}"));

            return linked.Count > 0
                ? Supported(subject, spec, claimed, "the scan model links validator(s) to the request",
                    EvidenceLine($"request `{request.Name}`{Loc(request.Location)} lists validators: {list}"))
                : NotSupported(subject, spec, claimed, "the scan model links no validator to the request",
                    EvidenceLine($"request `{request.Name}`{Loc(request.Location)} lists no validators"));
        }

        var matches = linked.Where(v => NamesMatch(v, claimed)).ToList();
        var objectShape = FindShape(shapes, claimed, "validator");

        if (!spec.IsAbsence)
        {
            return matches.Count > 0
                ? Supported(subject, spec, claimed, "validator is linked to the request in the scan model",
                    EvidenceLine($"request `{request.Name}`{Loc(request.Location)} lists validator `{matches[0]}`"),
                    objectShape is null ? "" : EvidenceLine($"validator `{objectShape.Name}`{Loc(objectShape.Location)} validates the request"))
                : NotSupported(subject, spec, claimed,
                    objectShape is null
                        ? "no validator by that name is present anywhere in the scanned model"
                        : "the named validator is linked to a different request (or none) in the scan model",
                    EvidenceLine($"request `{request.Name}`{Loc(request.Location)} lists validators: {list}"),
                    objectShape is null
                        ? EvidenceLine($"no validator named `{claimed}` is present in the scanned model")
                        : EvidenceLine($"validator `{objectShape.Name}`{Loc(objectShape.Location)} is not among them"));
        }

        return matches.Count == 0
            ? Supported(subject, spec, claimed, "the named validator is not linked to the request",
                EvidenceLine($"request `{request.Name}`{Loc(request.Location)} lists validators: {list}"))
            : NotSupported(subject, spec, claimed, "the scan model links the named validator to the request",
                EvidenceLine($"request `{request.Name}`{Loc(request.Location)} lists validator `{matches[0]}`"));
    }

    // ---------------------------------------------------------------- publishes

    private static ClaimCheck VerifyPublishes(
        AnalysisResult result, IReadOnlyList<Shape> shapes, ClaimRelation spec, string subject, string? claimed)
    {
        var (outcome, shape) = ResolveShape(shapes, subject, "request", "handler", "notification handler");
        if (outcome == Match.Absent)
        {
            var (epOutcome, ep) = ResolveEntrypoint(result, subject);
            if (epOutcome == Match.Found && ep is not null)
                return VerifyEntrypointPublishes(result, shapes, spec, subject, claimed, ep);
            if (epOutcome == Match.Ambiguous)
                return AmbiguousEntrypointSubject(subject, spec, claimed);
        }
        if (outcome != Match.Found)
            return SubjectNotResolved(result, subject, spec, claimed, "request, handler, notification handler or entrypoint");

        var published = PublishedNotifications(result, shape!);
        var list = published.Count == 0 ? "none" : string.Join(", ", published.Take(MaxListItems).Select(n => $"`{n}`"));
        var objectShape = claimed is null ? null : FindShape(shapes, claimed, "notification");
        var matches = claimed is null ? new List<string>() : published.Where(n => NamesMatch(n, claimed)).ToList();

        if (!spec.IsAbsence)
        {
            return matches.Count > 0
                ? Supported(subject, spec, claimed, "the scan model records the published notification",
                    EvidenceLine($"{shape!.Kind} `{shape.Name}`{Loc(shape.Location)} publishes notification `{matches[0]}`"),
                    objectShape is null ? "" : EvidenceLine($"notification `{objectShape.Name}`{Loc(objectShape.Location)}"))
                : NotSupported(subject, spec, claimed,
                    objectShape is null
                        ? "no notification by that name is present anywhere in the scanned model"
                        : "the named notification is not published by this shape in the scan model",
                    EvidenceLine($"{shape!.Kind} `{shape.Name}`{Loc(shape.Location)} publishes: {list}"),
                    objectShape is null
                        ? EvidenceLine($"no notification named `{claimed}` is present in the scanned model")
                        : EvidenceLine($"notification `{objectShape.Name}`{Loc(objectShape.Location)} is not among them"));
        }

        return matches.Count == 0
            ? Supported(subject, spec, claimed, "the scan model does not record that notification as published",
                EvidenceLine($"{shape!.Kind} `{shape.Name}`{Loc(shape.Location)} publishes: {list}"))
            : NotSupported(subject, spec, claimed, "the scan model records the notification as published",
                EvidenceLine($"{shape!.Kind} `{shape.Name}`{Loc(shape.Location)} publishes notification `{matches[0]}`"));
    }

    // ---------------------------------------------------------------- notification handler ⇄ notification

    private static ClaimCheck VerifyHandlesNotification(
        AnalysisResult result, IReadOnlyList<Shape> shapes, ClaimRelation spec, string subject, string? claimed)
    {
        var (outcome, shape) = ResolveShape(shapes, subject, "notification handler");
        if (outcome != Match.Found)
            return SubjectNotResolved(result, subject, spec, claimed, "notification handler");

        var handler = FindUnique(result.NotificationHandlers, subject)!;
        var handled = handler.NotificationName ?? "";
        var matches = claimed is not null && NamesMatch(handled, claimed);
        var objectShape = claimed is null ? null : FindShape(shapes, claimed, "notification");

        if (!spec.IsAbsence)
        {
            return matches
                ? Supported(subject, spec, claimed, "the scan model links the handler to the notification",
                    EvidenceLine($"notification handler `{handler.Name}`{Loc(handler.Location)} handles `{handled}`"),
                    objectShape is null ? "" : EvidenceLine($"notification `{objectShape.Name}`{Loc(objectShape.Location)}"))
                : NotSupported(subject, spec, claimed,
                    objectShape is null
                        ? "no notification by that name is present anywhere in the scanned model"
                        : "the handler handles a different notification in the scan model",
                    EvidenceLine($"notification handler `{handler.Name}`{Loc(handler.Location)} handles `{handled}` (not `{claimed}`)"));
        }

        return !matches
            ? Supported(subject, spec, claimed, "the handler is not linked to the named notification",
                EvidenceLine($"notification handler `{handler.Name}`{Loc(handler.Location)} handles `{handled}`"))
            : NotSupported(subject, spec, claimed, "the scan model links the handler to the named notification",
                EvidenceLine($"notification handler `{handler.Name}`{Loc(handler.Location)} handles `{claimed}`"));
    }

    // ---------------------------------------------------------------- handler ⇄ DbContext

    private static ClaimCheck VerifyUsesDbContext(
        AnalysisResult result, IReadOnlyList<Shape> shapes, ClaimRelation spec, string subject, string? claimed)
    {
        var (outcome, shape) = ResolveShape(shapes, subject, "handler", "request");
        if (outcome == Match.Absent)
        {
            var (epOutcome, ep) = ResolveEntrypoint(result, subject);
            if (epOutcome == Match.Found && ep is not null)
                return VerifyEntrypointUsesDbContext(result, shapes, spec, subject, claimed, ep);
            if (epOutcome == Match.Ambiguous)
                return AmbiguousEntrypointSubject(subject, spec, claimed);
        }
        if (outcome != Match.Found)
            return SubjectNotResolved(result, subject, spec, claimed, "handler, request or entrypoint");

        if (claimed is null)
            return NotDeterminable(subject, spec.Display, claimed, "invalid claim", "relation requires an object.");

        var contextShape = FindShape(shapes, claimed, "DbContext");
        if (contextShape is null)
        {
            return NotSupported(subject, spec, claimed, "no DbContext by that name is present in the scanned model",
                EvidenceLine($"no DbContext named `{claimed}` is present in the scanned model ({result.DbContexts.Count} DbContext(s) inventoried)"));
        }

        var owners = Owners(result, shape!);
        var edgeCount = result.FlowEdges.Count(e => e.Kind == FlowEdgeKind.HandlerToDbContext);
        var usesDirect = owners.Any(h => HandlerUsesContext(result, h, contextShape));
        var resolvedHops = result.ResolvedEdges
            .Where(e => e.Kind == FlowEdgeKind.HandlerToDbContextResolved &&
                        owners.Any(h => NamesMatch(e.From, h.FullName)) &&
                        NamesMatch(e.To, contextShape.FullName))
            .ToList();
        var uses = usesDirect || resolvedHops.Count > 0;
        var ownerNames = owners.Count == 0
            ? "none resolved"
            : string.Join(", ", owners.Take(MaxListItems).Select(h => $"`{h.Name}`"));

        var modelNote =
            $"the scan model holds {edgeCount} direct handler→DbContext edge(s) and {result.ResolvedEdges.Count} DI-resolved edge(s); " +
            "unresolved interface/repository hops stop fail-closed, so absence is not proof of non-use.";

        if (!spec.IsAbsence)
        {
            if (usesDirect)
            {
                return Supported(subject, spec, claimed, "the scan model records direct DbContext use",
                    EvidenceLine($"{shape!.Kind} `{shape.Name}`{Loc(shape.Location)} owner handler(s): {ownerNames}"),
                    EvidenceLine($"DbContext `{contextShape.Name}`{Loc(contextShape.Location)} is named by a handler dependency or flow edge"));
            }

            if (resolvedHops.Count > 0)
            {
                var hopList = resolvedHops
                    .Take(MaxListItems)
                    .Select(e =>
                    {
                        var chain = e.ViaChain is { Count: > 1 } ? string.Join(" -> ", e.ViaChain) : e.Via;
                        return $"`{e.From}` -> `{e.To}` via `{chain}`{Loc(e.Location)}";
                    });
                return Supported(subject, spec, claimed, "the scan model records DI-resolved DbContext use",
                    EvidenceLine($"{shape!.Kind} `{shape.Name}`{Loc(shape.Location)} owner handler(s): {ownerNames}"),
                    EvidenceLine($"DI hop chain to `{contextShape.Name}`{Loc(contextShape.Location)}: {string.Join("; ", hopList)}"));
            }

            return NotDeterminable(subject, spec.Display, claimed, "outside the scan model",
                $"{modelNote} No direct or DI-resolved DbContext edge to `{claimed}` was recorded for {ownerNames}.",
                EvidenceLine($"{shape!.Kind} `{shape.Name}`{Loc(shape.Location)}"),
                EvidenceLine($"DbContext `{contextShape.Name}`{Loc(contextShape.Location)}"));
        }

        if (uses)
        {
            return NotSupported(subject, spec, claimed, "the scan model records DbContext use",
                EvidenceLine($"{shape!.Kind} `{shape.Name}`{Loc(shape.Location)} owner handler(s): {ownerNames}"),
                EvidenceLine($"DbContext `{contextShape.Name}`{Loc(contextShape.Location)} is reached by {ownerNames}"));
        }

        if (result.DbContexts.Count == 0)
        {
            return Supported(subject, spec, claimed, "the scan model holds no DbContexts at all",
                EvidenceLine("this scan inventories 0 DbContexts"));
        }

        return NotDeterminable(subject, spec.Display, claimed, "outside the scan model", modelNote,
            EvidenceLine($"{shape!.Kind} `{shape.Name}`{Loc(shape.Location)}"),
            EvidenceLine($"DbContext `{contextShape.Name}`{Loc(contextShape.Location)}"));
    }

    // ---------------------------------------------------------------- depends on

    private static ClaimCheck VerifyDependsOn(
        AnalysisResult result, IReadOnlyList<Shape> shapes, ClaimRelation spec, string subject, string? claimed)
    {
        var (outcome, shape) = ResolveShape(shapes, subject, "handler", "notification handler");
        if (outcome != Match.Found)
            return SubjectNotResolved(result, subject, spec, claimed, "handler or notification handler");

        var dependencies = Dependencies(result, shape!);
        var list = dependencies.Count == 0 ? "none" : string.Join(", ", dependencies.Take(MaxListItems).Select(d => $"`{d}`"));
        var matches = claimed is null ? false : dependencies.Any(d => NamesMatch(d, claimed));

        if (!spec.IsAbsence)
        {
            return matches
                ? Supported(subject, spec, claimed, "the scan model records the dependency",
                    EvidenceLine($"{shape!.Kind} `{shape.Name}`{Loc(shape.Location)} dependencies: {list}"))
                : NotSupported(subject, spec, claimed, "the dependency is not in the model's dependency set",
                    EvidenceLine($"{shape!.Kind} `{shape.Name}`{Loc(shape.Location)} dependencies: {list} (no `{claimed}`)"));
        }

        return !matches
            ? Supported(subject, spec, claimed, "the dependency is not in the model's dependency set",
                EvidenceLine($"{shape!.Kind} `{shape.Name}`{Loc(shape.Location)} dependencies: {list}"))
            : NotSupported(subject, spec, claimed, "the scan model records the dependency",
                EvidenceLine($"{shape!.Kind} `{shape.Name}`{Loc(shape.Location)} dependencies include `{claimed}`"));
    }

    // ---------------------------------------------------------------- belongs to layer

    private static ClaimCheck VerifyLayer(
        AnalysisResult result, IReadOnlyList<Shape> shapes, ClaimRelation spec, string subject, string? claimed)
    {
        var (outcome, shape) = ResolveShape(shapes, subject, shapes.Select(s => s.Kind).Distinct(StringComparer.Ordinal).ToArray());
        if (outcome == Match.Absent)
        {
            var (epOutcome, ep) = ResolveEntrypoint(result, subject);
            if (epOutcome == Match.Found && ep is not null)
            {
                shape = new Shape("entrypoint", ep.Name, ep.Name, ep.ProjectName, ep.Location);
                outcome = Match.Found;
            }
            else if (epOutcome == Match.Ambiguous)
            {
                return AmbiguousEntrypointSubject(subject, spec, claimed);
            }
        }
        if (outcome != Match.Found)
            return SubjectNotResolved(result, subject, spec, claimed, "project or shape with a project");

        if (claimed is null)
            return NotDeterminable(subject, spec.Display, claimed, "invalid claim", "relation requires an object.");

        string layer;
        ProjectInfo? project;
        if (shape!.Kind == "project")
        {
            project = result.Projects.First(p => string.Equals(p.Name, shape.FullName, StringComparison.Ordinal));
            layer = project.Layer;
        }
        else if (string.IsNullOrEmpty(shape.ProjectName))
        {
            return NotDeterminable(subject, spec.Display, claimed, "outside the scan model",
                $"{shape.Kind} `{shape.Name}` has no resolved project in the scan model, so its layer is not recorded.",
                EvidenceLine($"{shape.Kind} `{shape.Name}`{Loc(shape.Location)}"));
        }
        else
        {
            var (projectOutcome, resolved) = ResolveTyped(result.Projects, shape.ProjectName);
            if (projectOutcome != Match.Found || resolved is null)
            {
                return NotDeterminable(subject, spec.Display, claimed, "could not resolve",
                    $"{shape.Kind} `{shape.Name}` belongs to project `{shape.ProjectName}`, which did not resolve uniquely " +
                    $"({DescribeOutcome(projectOutcome)}).",
                    EvidenceLine($"{shape.Kind} `{shape.Name}`{Loc(shape.Location)} project: `{shape.ProjectName}`"));
            }

            project = resolved;
            layer = resolved.Layer;
        }

        var isInLayer = string.Equals(layer, claimed, StringComparison.OrdinalIgnoreCase);
        var evidence = new[]
        {
            EvidenceLine($"project `{project.Name}`{Loc(ProjectLocation(result, project))} layer: `{layer}`"),
            shape.Kind == "project" ? "" : EvidenceLine($"{shape.Kind} `{shape.Name}`{Loc(shape.Location)} project: `{project.Name}`"),
        };

        return spec.IsAbsence
            ? isInLayer
                ? NotSupported(subject, spec, claimed, "the project's layer is the named layer", evidence)
                : Supported(subject, spec, claimed, "the project's layer differs from the named layer", evidence)
            : isInLayer
                ? Supported(subject, spec, claimed, "the project's layer is the named layer", evidence)
                : NotSupported(subject, spec, claimed, "the project's layer differs from the named layer", evidence);
    }

    // ---------------------------------------------------------------- dispatched by

    private static ClaimCheck VerifyDispatchedBy(
        AnalysisResult result, IReadOnlyList<Shape> shapes, ClaimRelation spec, string subject, string? claimed)
    {
        var (outcome, shape) = ResolveShape(shapes, subject, "request", "notification");
        if (outcome != Match.Found)
            return SubjectNotResolved(result, subject, spec, claimed, "request or notification");

        var targetKind = shape!.Kind == "notification" ? DispatchTargetKind.Notification : DispatchTargetKind.Request;
        var sites = result.DispatchCallSites
            .Where(s => s.TargetKind == targetKind && NamesMatch(s.TargetFullName, shape.FullName))
            .ToList();

        var matches = claimed is null
            ? new List<DispatchCallSite>()
            : sites.Where(s => SenderMatches(s.SenderFullName, claimed)).ToList();

        var unresolved = result.UnresolvedDispatches;
        var unresolvedNote = unresolved.Count == 0
            ? ""
            : $"the scan holds {unresolved.Count} unresolved dispatch call site(s), e.g. " +
              string.Join("; ", unresolved.Take(3).Select(u => $"`{u.Method}(...)` at {Loc(u.Location, plain: true)}")) +
              " — their targets could not be resolved (runtime-typed send).";

        if (!spec.IsAbsence)
        {
            if (matches.Count > 0)
            {
                return Supported(subject, spec, claimed, "a resolved dispatch call site matches",
                    EvidenceLine($"dispatch site `{matches[0].SenderFullName ?? "?"}` → `{shape.FullName}`{Loc(matches[0].Location)}"));
            }

            if (unresolved.Count > 0)
            {
                return NotDeterminable(subject, spec.Display, claimed, "unresolved dispatch (runtime-typed send)",
                    unresolvedNote,
                    EvidenceLine($"{shape.Kind} `{shape.Name}`{Loc(shape.Location)} has no resolved dispatch site from `{claimed}`"),
                    EdgeList(sites));
            }

            return NotSupported(subject, spec, claimed, "the scan model records no dispatch of this shape at all",
                EvidenceLine($"{shape.Kind} `{shape.Name}`{Loc(shape.Location)} has 0 resolved dispatch call sites and 0 unresolved dispatches"));
        }

        if (matches.Count > 0)
        {
            return NotSupported(subject, spec, claimed, "a resolved dispatch call site matches",
                EvidenceLine($"dispatch site `{matches[0].SenderFullName ?? "?"}` → `{shape.FullName}`{Loc(matches[0].Location)}"));
        }

        if (unresolved.Count > 0)
        {
            return NotDeterminable(subject, spec.Display, claimed, "unresolved dispatch (runtime-typed send)",
                unresolvedNote,
                EvidenceLine($"{shape.Kind} `{shape.Name}`{Loc(shape.Location)} has no resolved dispatch site from `{claimed}`"),
                EdgeList(sites));
        }

        return Supported(subject, spec, claimed, "the scan model records no dispatch of this shape at all",
            EvidenceLine($"{shape.Kind} `{shape.Name}`{Loc(shape.Location)} has 0 resolved dispatch call sites and 0 unresolved dispatches"));
    }

    // ---------------------------------------------------------------- calls service

    private static ClaimCheck VerifyCallsService(
        AnalysisResult result, IReadOnlyList<Shape> shapes, ClaimRelation spec, string subject, string? claimed)
    {
        var (outcome, shape) = ResolveShape(shapes, subject, "request", "handler", "notification handler");
        if (outcome == Match.Absent)
        {
            var (epOutcome, ep) = ResolveEntrypoint(result, subject);
            if (epOutcome == Match.Found && ep is not null)
                return VerifyEntrypointCallsService(result, spec, subject, claimed, ep);
            if (epOutcome == Match.Ambiguous)
                return AmbiguousEntrypointSubject(subject, spec, claimed);
        }
        if (outcome != Match.Found)
            return SubjectNotResolved(result, subject, spec, claimed, "request, handler, notification handler or entrypoint");

        if (claimed is null)
            return NotDeterminable(subject, spec.Display, claimed, "invalid claim", "relation requires an object.");

        var (ownerNames, ownerDeps, ownerIncomplete) = ResolveServiceCallOwners(result, shape!);
        var ownerEdges = result.ServiceCallEdges
            .Where(e => ownerNames.Any(owner => NamesMatch(e.From, owner)))
            .ToList();
        var matches = ownerEdges.Where(e => ServiceCallEdgeMatches(e, claimed)).ToList();

        if (matches.Count > 0)
        {
            var first = matches[0];
            var evidence = new[]
            {
                EvidenceLine($"service call `{first.From}` → `{first.To}`.`{first.Method}` via `{first.Via}`{Loc(first.Location)}"),
                EvidenceLine($"{shape!.Kind} `{shape.Name}`{Loc(shape.Location)}"),
            };
            return spec.IsAbsence
                ? NotSupported(subject, spec, claimed, "the scan model records a matching service-call edge", evidence)
                : Supported(subject, spec, claimed, "the scan model records a matching service-call edge", evidence);
        }

        if (CheckIncompleteOrFiltered(result, shape!, subject, spec, claimed,
                "service call", includeContractDiagnostics: true) is { } guardFailure)
        {
            return guardFailure;
        }

        if (ownerIncomplete)
        {
            return NotDeterminable(subject, spec.Display, claimed, "could not resolve",
                $"{shape!.Kind} `{shape.Name}` has an unlinked or ambiguous handler declaration in the scan model, so service calls cannot be resolved.",
                EvidenceLine($"{shape.Kind} `{shape.Name}`{Loc(shape.Location)}"));
        }

        if (HasAmbiguousServiceResolution(result, ownerDeps))
        {
            return NotDeterminable(subject, spec.Display, claimed, "ambiguous service resolution",
                $"{shape!.Kind} `{shape.Name}` injects an interface with an ambiguous service resolution, so calls through it cannot be settled statically.",
                EvidenceLine($"{shape.Kind} `{shape.Name}`{Loc(shape.Location)}"));
        }

        if (spec.IsAbsence && result.UnresolvedDispatches.Count > 0)
        {
            return NotDeterminable(subject, spec.Display, claimed, "unresolved dispatch (runtime-typed send)",
                $"the scan holds {result.UnresolvedDispatches.Count} unresolved dispatch call site(s), so non-invocation cannot be guaranteed.",
                EvidenceLine($"{shape!.Kind} `{shape.Name}`{Loc(shape.Location)} has 0 matching service-call edges"));
        }

        var edgeList = ownerEdges.Count == 0
            ? "none"
            : string.Join(", ", ownerEdges.Take(MaxListItems).Select(e => $"`{e.To}.{e.Method}`"));
        var summaryEvidence = EvidenceLine($"{shape!.Kind} `{shape.Name}`{Loc(shape.Location)} service calls: {edgeList}");

        return spec.IsAbsence
            ? Supported(subject, spec, claimed, "no matching service-call edge is recorded for this shape in the scanned model", summaryEvidence)
            : NotSupported(subject, spec, claimed, "no matching service-call edge is recorded for this shape in the scanned model", summaryEvidence);
    }

    // ---------------------------------------------------------------- references project

    private static ClaimCheck VerifyReferencesProject(
        AnalysisResult result, IReadOnlyList<Shape> shapes, ClaimRelation spec, string subject, string? claimed)
    {
        var (outcome, shape) = ResolveShape(shapes, subject, "project");
        if (outcome != Match.Found)
            return SubjectNotResolved(result, subject, spec, claimed, "project");

        if (claimed is null)
            return NotDeterminable(subject, spec.Display, claimed, "invalid claim", "relation requires an object.");

        var project = result.Projects.First(p => string.Equals(p.Name, shape!.FullName, StringComparison.Ordinal));
        var projectLoc = ProjectLocation(result, project);
        var targetBase = ProjectBaseName(claimed);
        var targetProjects = result.Projects
            .Where(p => string.Equals(p.Name, claimed, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(p.Name, targetBase, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (targetProjects.Count > 1)
        {
            return NotDeterminable(subject, spec.Display, claimed, "could not resolve",
                $"target project name \"{claimed}\" matches more than one project in the scan model.",
                EvidenceLine($"project `{project.Name}`{Loc(projectLoc)}"));
        }

        var matchedRef = project.ReferenceNames.FirstOrDefault(r =>
            string.Equals(r, claimed, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(r, targetBase, StringComparison.OrdinalIgnoreCase));
        if (matchedRef is not null)
        {
            var targetProject = targetProjects.Count == 1 ? targetProjects[0] : null;
            var evidence = new[]
            {
                EvidenceLine($"project `{project.Name}`{Loc(projectLoc)} references `{matchedRef}`"),
                targetProject is null ? "" : EvidenceLine($"target project `{targetProject.Name}`{Loc(ProjectLocation(result, targetProject))}"),
            };
            return spec.IsAbsence
                ? NotSupported(subject, spec, claimed, "the project unconditionally references the target project", evidence)
                : Supported(subject, spec, claimed, "the project unconditionally references the target project", evidence);
        }

        var matchingConditional = project.ConditionalReferences
            .Where(cr => ConditionalReferenceMatches(cr, claimed, targetBase))
            .ToList();
        if (matchingConditional.Count > 0)
        {
            var conditions = string.Join("; ", matchingConditional.Select(c =>
                $"`{c.Include}` when Condition=\"{c.Condition}\""));
            return NotDeterminable(subject, spec.Display, claimed, "conditional project reference",
                $"project `{project.Name}` declares conditional reference(s) ({conditions}); conditional reference activation cannot be resolved statically.",
                matchingConditional
                    .Take(MaxListItems)
                    .Select(c => EvidenceLine(
                        $"project `{project.Name}`{Loc(projectLoc)} conditional reference `{c.Include}` (Condition: `{c.Condition}`)"))
                    .ToArray());
        }

        var unresolvedInclude = project.ReferenceIncludes.FirstOrDefault(inc => IncludeMatchesProject(inc, claimed, targetBase));
        if (unresolvedInclude is not null)
        {
            return NotDeterminable(subject, spec.Display, claimed, "unresolved project reference",
                $"project `{project.Name}` declares ProjectReference `{unresolvedInclude}`, which did not resolve to a scanned project.",
                EvidenceLine($"project `{project.Name}`{Loc(projectLoc)} raw include `{unresolvedInclude}`"));
        }

        if (CheckIncompleteOrFiltered(result, shape!, subject, spec, claimed,
                "project reference", includeContractDiagnostics: false) is { } guardFailure)
        {
            return guardFailure;
        }

        var refList = project.ReferenceNames.Count == 0
            ? "none"
            : string.Join(", ", project.ReferenceNames.Take(MaxListItems).Select(r => $"`{r}`"));
        var targetInfo = targetProjects.Count == 1
            ? EvidenceLine($"target project `{targetProjects[0].Name}`{Loc(ProjectLocation(result, targetProjects[0]))} is not referenced")
            : EvidenceLine($"no project named `{claimed}` is referenced by `{project.Name}`");
        var absentBasis = targetProjects.Count == 0
            ? "no project by that name is referenced or present in the scanned model"
            : "the project does not reference the target project in the scan model";

        return spec.IsAbsence
            ? Supported(subject, spec, claimed, absentBasis,
                EvidenceLine($"project `{project.Name}`{Loc(projectLoc)} references: {refList}"), targetInfo)
            : NotSupported(subject, spec, claimed, absentBasis,
                EvidenceLine($"project `{project.Name}`{Loc(projectLoc)} references: {refList}"), targetInfo);
    }

    // ---------------------------------------------------------------- touches entity

    private static ClaimCheck VerifyTouchesEntity(
        AnalysisResult result, IReadOnlyList<Shape> shapes, ClaimRelation spec, string subject, string? claimed)
    {
        var (outcome, shape) = ResolveShape(shapes, subject, "handler", "request");
        if (outcome == Match.Absent)
        {
            var (epOutcome, ep) = ResolveEntrypoint(result, subject);
            if (epOutcome == Match.Found && ep is not null)
                return VerifyEntrypointTouchesEntity(result, shapes, spec, subject, claimed, ep);
            if (epOutcome == Match.Ambiguous)
                return AmbiguousEntrypointSubject(subject, spec, claimed);
        }
        if (outcome != Match.Found)
            return SubjectNotResolved(result, subject, spec, claimed, "handler, request or entrypoint");

        if (claimed is null)
            return NotDeterminable(subject, spec.Display, claimed, "invalid claim", "relation requires an object.");

        var (touched, ownerIncomplete) = CollectTouchedEntities(result, shape!);
        var matches = touched.Where(label => TouchedEntityMatches(label, claimed)).ToList();
        var entityShape = FindShape(shapes, claimed, "entity");

        if (matches.Count > 0)
        {
            var evidence = new[]
            {
                EvidenceLine($"{shape!.Kind} `{shape.Name}`{Loc(shape.Location)} touches `{matches[0]}`"),
                entityShape is null ? "" : EvidenceLine($"entity `{entityShape.Name}`{Loc(entityShape.Location)}"),
            };
            return spec.IsAbsence
                ? NotSupported(subject, spec, claimed, "the scan model records the entity touch", evidence)
                : Supported(subject, spec, claimed, "the scan model records the entity touch", evidence);
        }

        if (CheckIncompleteOrFiltered(result, shape!, subject, spec, claimed,
                "entity touch", includeContractDiagnostics: true, includeEfUnresolved: true) is { } guardFailure)
        {
            return guardFailure;
        }

        if (ownerIncomplete)
        {
            return NotDeterminable(subject, spec.Display, claimed, "could not resolve",
                $"{shape!.Kind} `{shape.Name}` has an unlinked or ambiguous handler declaration in the scan model, so entity touches cannot be settled.",
                EvidenceLine($"{shape.Kind} `{shape.Name}`{Loc(shape.Location)}"));
        }

        var list = touched.Count == 0
            ? "none"
            : string.Join(", ", touched.Take(MaxListItems).Select(e => $"`{e}`"));
        var absentEvidence = EvidenceLine($"{shape!.Kind} `{shape.Name}`{Loc(shape.Location)} touched entities: {list}");
        const string absentBasis = "the scan model records no matching entity touch for this shape";

        return spec.IsAbsence
            ? Supported(subject, spec, claimed, absentBasis, absentEvidence)
            : NotSupported(subject, spec, claimed, absentBasis, absentEvidence);
    }

    // ---------------------------------------------------------------- has test reference

    private static ClaimCheck VerifyHasTestReference(
        AnalysisResult result, IReadOnlyList<Shape> shapes, ClaimRelation spec, string subject, string? claimed)
    {
        const string staticCouplingCaveat =
            "static test references show test-project coupling, not runtime assertion coverage";

        var (outcome, shape) = ResolveShape(shapes, subject, "request", "handler", "validator");
        if (outcome != Match.Found)
            return SubjectNotResolved(result, subject, spec, claimed, "request, handler or validator");

        IReadOnlyList<TestMethodReference> allShapeRefs;
        if (shape!.Kind == "request")
        {
            var request = FindUnique(result.Requests, shape.FullName);
            if (request is null)
                return SubjectNotResolved(result, subject, spec, claimed, "request");
            allShapeRefs = TestProjectDiscovery.FindTestReferencesForSlice(result, request);
        }
        else
        {
            var targetKind = shape.Kind;
            allShapeRefs = TestProjectDiscovery.FindAllTestReferences(result)
                .Where(r =>
                    (string.IsNullOrEmpty(r.TargetKind) ||
                     string.Equals(r.TargetKind, targetKind, StringComparison.OrdinalIgnoreCase)) &&
                    NamesMatch(r.TargetFullName, shape.FullName))
                .ToList();
        }

        var matchingRefs = claimed is null
            ? allShapeRefs
            : allShapeRefs.Where(r => TestReferenceMatchesFilter(r, claimed)).ToList();

        if (matchingRefs.Count > 0)
        {
            var evidence = matchingRefs
                .Take(MaxListItems)
                .Select(r => EvidenceLine(
                    $"test `{r.ClassName}.{r.MethodName}` in `{r.ProjectName}` ({r.ReferenceKind} → `{r.TargetFullName}`) (`{r.File}:{r.Line}`)"))
                .Append(EvidenceLine($"{shape.Kind} `{shape.Name}`{Loc(shape.Location)}"))
                .Append(EvidenceLine($"note: {staticCouplingCaveat}."))
                .ToArray();

            return spec.IsAbsence
                ? NotSupported(subject, spec, claimed,
                    $"the scan model records static test reference(s) ({staticCouplingCaveat})", evidence)
                : Supported(subject, spec, claimed,
                    $"the scan model records static test reference(s) ({staticCouplingCaveat})", evidence);
        }

        if (CheckIncompleteOrFiltered(result, shape, subject, spec, claimed,
                "test reference", includeContractDiagnostics: true, noteSuffix: $" ({staticCouplingCaveat}.)") is { } guardFailure)
        {
            return guardFailure;
        }

        var summary = claimed is null
            ? "0 static test references were discovered"
            : $"0 static test references matching `{claimed}` were discovered ({allShapeRefs.Count} total for this shape)";
        var absentEvidence = new[]
        {
            EvidenceLine($"{shape.Kind} `{shape.Name}`{Loc(shape.Location)}: {summary}"),
            EvidenceLine($"note: {staticCouplingCaveat}."),
        };
        var absentBasis = $"no matching static test references were discovered in the scanned model ({staticCouplingCaveat})";

        return spec.IsAbsence
            ? Supported(subject, spec, claimed, absentBasis, absentEvidence)
            : NotSupported(subject, spec, claimed, absentBasis, absentEvidence);
    }

    // ---------------------------------------------------------------- shared resolution

    private static IReadOnlyList<Shape> AllShapes(AnalysisResult result)
    {
        var shapes = new List<Shape>();
        foreach (var r in result.Requests)
            shapes.Add(new Shape("request", r.Name, r.FullName, r.ProjectName, r.Location));
        foreach (var h in result.Handlers)
            shapes.Add(new Shape("handler", h.Name, h.FullName, h.ProjectName, h.Location));
        foreach (var nh in result.NotificationHandlers)
            shapes.Add(new Shape("notification handler", nh.Name, nh.FullName, nh.ProjectName, nh.Location));
        foreach (var v in result.Validators)
            shapes.Add(new Shape("validator", v.Name, v.FullName, v.ProjectName, v.Location));
        foreach (var b in result.Behaviors)
            shapes.Add(new Shape("behavior", b.Name, b.Name, b.ProjectName, b.Location));
        foreach (var d in result.DbContexts)
            shapes.Add(new Shape("DbContext", d.Name, d.FullName, d.ProjectName, d.Location));
        foreach (var n in result.Notifications)
            shapes.Add(new Shape("notification", n.Name, n.FullName, n.ProjectName, n.Location));
        foreach (var p in result.Projects)
            shapes.Add(new Shape("project", p.Name, p.Name, p.Name, ProjectLocation(result, p)));

        foreach (var d in result.DbContexts)
        {
            foreach (var e in d.Entities)
                shapes.Add(new Shape("entity", e.Name, e.FullName, e.ProjectName ?? d.ProjectName, e.Location));

            foreach (var s in d.DbSets)
            {
                var simple = CSharpScanner.SimpleName(s.Entity);
                if (shapes.Any(x => x.Kind == "entity" && string.Equals(x.Name, simple, StringComparison.Ordinal)))
                    continue;
                shapes.Add(new Shape("entity", simple, s.EntityFullName ?? simple, d.ProjectName, d.Location));
            }
        }

        return shapes;
    }

    private static (Match Outcome, Shape? Shape) ResolveShape(IReadOnlyList<Shape> shapes, string name, params string[] kinds)
    {
        var candidates = shapes.Where(s => kinds.Contains(s.Kind, StringComparer.Ordinal)).ToList();

        var full = candidates.Where(s => string.Equals(s.FullName, name, StringComparison.Ordinal)).ToList();
        if (full.Count == 1) return (Match.Found, full[0]);
        if (full.Count > 1) return (Match.Ambiguous, null);

        var simple = candidates.Where(s => string.Equals(s.Name, name, StringComparison.Ordinal)).ToList();
        if (simple.Count == 1) return (Match.Found, simple[0]);
        if (simple.Count > 1) return (Match.Ambiguous, null);

        return (Match.Absent, null);
    }

    private static Shape? FindShape(IReadOnlyList<Shape> shapes, string name, params string[] kinds)
        => ResolveShape(shapes, name, kinds).Shape;

    private static T? FindUnique<T>(IEnumerable<T> items, string name) where T : class
        => ResolveTyped(items, name).Value;

    private static (Match Outcome, T? Value) ResolveTyped<T>(IEnumerable<T> items, string name) where T : class
    {
        var list = items.ToList();
        string Full(T i) => i switch
        {
            CqrsRequest r => r.FullName,
            HandlerInfo h => h.FullName,
            NotificationHandlerInfo nh => nh.FullName,
            ValidatorInfo v => v.FullName,
            DbContextInfo d => d.FullName,
            CqrsNotification n => n.FullName,
            ProjectInfo p => p.Name,
            _ => "",
        };
        string Simple(T i) => i switch
        {
            CqrsRequest r => r.Name,
            HandlerInfo h => h.Name,
            NotificationHandlerInfo nh => nh.Name,
            ValidatorInfo v => v.Name,
            DbContextInfo d => d.Name,
            CqrsNotification n => n.Name,
            ProjectInfo p => p.Name,
            _ => "",
        };

        var full = list.Where(i => string.Equals(Full(i), name, StringComparison.Ordinal)).ToList();
        if (full.Count == 1) return (Match.Found, full[0]);
        if (full.Count > 1) return (Match.Ambiguous, null);

        var simple = list.Where(i => string.Equals(Simple(i), name, StringComparison.Ordinal)).ToList();
        if (simple.Count == 1) return (Match.Found, simple[0]);
        if (simple.Count > 1) return (Match.Ambiguous, null);

        return (Match.Absent, null);
    }

    private static ClaimCheck SubjectNotResolved(
        AnalysisResult result, string subject, ClaimRelation spec, string? claimed, string kindWord)
    {
        var (outcome, _) = ResolveShape(AllShapes(result), subject, spec.SubjectKinds.Select(ToKindLabel).ToArray());
        if (outcome == Match.Ambiguous)
        {
            return NotDeterminable(subject, spec.Display, claimed, "could not resolve",
                $"subject name \"{subject}\" matches more than one {kindWord} shape in the scan model; qualify it with a namespace.",
                EvidenceLine("ambiguous subject — no fact can be proven or refuted"));
        }

        var hint = DiagnosticHint(result, subject, spec.Display, claimed);
        if (hint is not null)
            return hint;

        if (!ScanComplete(result))
        {
            return NotDeterminable(subject, spec.Display, claimed, "scan incomplete",
                $"the scan reported error diagnostics, so the absence of \"{subject}\" from the {kindWord} model is not a proof.",
                EvidenceLine("scan has error-severity diagnostics"));
        }

        return NotSupported(subject, spec, claimed, $"subject absent from the scanned model: no {kindWord} named `{subject}` exists in the scan",
            EvidenceLine($"no {kindWord} named `{subject}` is present in the scanned model"));
    }

    private static ClaimCheck? DiagnosticHint(AnalysisResult result, string subject, string relation, string? claimed)
    {
        foreach (var diagnostic in result.Diagnostics)
        {
            if (!diagnostic.Message.Contains(subject, StringComparison.Ordinal))
                continue;

            if (string.Equals(diagnostic.Code, AnalysisResult.UnresolvedContractCode, StringComparison.Ordinal))
            {
                return NotDeterminable(subject, relation, claimed, "package-opaque wrapper",
                    $"the scan stopped discovery at an external base type: {diagnostic.Message}",
                    EvidenceLine($"diagnostic `{diagnostic.Code}`{Loc(diagnostic.Location)}"));
            }

            if (string.Equals(diagnostic.Code, AnalysisResult.AmbiguousContractCode, StringComparison.Ordinal))
            {
                return NotDeterminable(subject, relation, claimed, "could not resolve",
                    diagnostic.Message,
                    EvidenceLine($"diagnostic `{diagnostic.Code}`{Loc(diagnostic.Location)}"));
            }
        }

        return null;
    }

    private static string ToKindLabel(ClaimSubjectKind kind) => kind switch
    {
        ClaimSubjectKind.Request => "request",
        ClaimSubjectKind.Handler => "handler",
        ClaimSubjectKind.NotificationHandler => "notification handler",
        ClaimSubjectKind.Validator => "validator",
        ClaimSubjectKind.Behavior => "behavior",
        ClaimSubjectKind.DbContext => "DbContext",
        ClaimSubjectKind.Entity => "entity",
        ClaimSubjectKind.Project => "project",
        ClaimSubjectKind.Notification => "notification",
        ClaimSubjectKind.Entrypoint => "entrypoint",
        _ => "shape",
    };

    private static List<string> PublishedNotifications(AnalysisResult result, Shape shape) => shape.Kind switch
    {
        "request" => result.Requests.First(r => r.FullName == shape.FullName).PublishedNotifications,
        "handler" => result.Handlers.First(h => h.FullName == shape.FullName).PublishedNotifications,
        "notification handler" => result.NotificationHandlers.First(nh => nh.FullName == shape.FullName).PublishedNotifications,
        _ => new List<string>(),
    };

    private static List<string> Dependencies(AnalysisResult result, Shape shape) => shape.Kind switch
    {
        "handler" => result.Handlers.First(h => h.FullName == shape.FullName).Dependencies,
        "notification handler" => result.NotificationHandlers.First(nh => nh.FullName == shape.FullName).Dependencies,
        _ => new List<string>(),
    };

    private static List<HandlerInfo> Owners(AnalysisResult result, Shape shape)
    {
        if (shape.Kind == "handler")
        {
            var handler = result.Handlers.FirstOrDefault(h => h.FullName == shape.FullName);
            return handler is null ? new List<HandlerInfo>() : new List<HandlerInfo> { handler };
        }

        var request = result.Requests.FirstOrDefault(r => r.FullName == shape.FullName);
        if (request is null)
            return new List<HandlerInfo>();

        return result.Handlers.Where(h => request.Handlers.Any(name => NamesMatch(name, h.FullName))).ToList();
    }

    private static bool HandlerUsesContext(AnalysisResult result, HandlerInfo handler, Shape context)
    {
        if (handler.Dependencies.Any(d => NamesMatch(d, context.Name)))
            return true;

        return result.FlowEdges.Any(e =>
            e.Kind == FlowEdgeKind.HandlerToDbContext &&
            NamesMatch(e.From, handler.FullName) &&
            NamesMatch(e.To, context.FullName));
    }

    private static bool SenderMatches(string? senderFullName, string claimed)
    {
        if (string.IsNullOrEmpty(senderFullName))
            return false;
        return NamesMatch(senderFullName, claimed);
    }

    /// <summary>
    /// Compares a name the model stored (a full type name, a nested full name, or a simple name)
    /// against a claimed name. Exact, then dotted-suffix match; a bare simple name collides with
    /// <c>Ns.Foo</c> but not with <c>OtherNs.Foo</c>.
    /// </summary>
    private static bool NamesMatch(string stored, string claimed)
    {
        if (string.Equals(stored, claimed, StringComparison.Ordinal))
            return true;
        return stored.EndsWith("." + claimed, StringComparison.Ordinal) ||
               claimed.EndsWith("." + stored, StringComparison.Ordinal);
    }

    private static bool ScanComplete(AnalysisResult result)
        => !result.Diagnostics.Any(d => d.Severity == AnalysisDiagnosticSeverity.Error);

    private static SourceLocation? ProjectLocation(AnalysisResult result, ProjectInfo project)
        => SourceLocation.FromLine(project.FilePath, result.RootPath, 1, 1);

    private static (List<string> OwnerNames, List<string> OwnerDeps, bool Incomplete) ResolveServiceCallOwners(
        AnalysisResult result, Shape shape)
    {
        if (shape.Kind == "handler")
        {
            var handler = FindUnique(result.Handlers, shape.FullName);
            return handler is null
                ? ([shape.FullName], [], true)
                : ([handler.FullName], handler.Dependencies, false);
        }

        if (shape.Kind == "notification handler")
        {
            var nh = FindUnique(result.NotificationHandlers, shape.FullName);
            return nh is null
                ? ([shape.FullName], [], true)
                : ([nh.FullName], nh.Dependencies, false);
        }

        var request = FindUnique(result.Requests, shape.FullName);
        if (request is null)
            return ([], [], true);

        var ownerNames = new List<string>();
        var ownerDeps = new List<string>();
        var incomplete = false;
        foreach (var handlerName in request.Handlers)
        {
            var (outcome, handler) = ResolveTyped(result.Handlers, handlerName);
            if (outcome == Match.Found && handler is not null)
            {
                ownerNames.Add(handler.FullName);
                ownerDeps.AddRange(handler.Dependencies);
            }
            else
            {
                incomplete = true;
                ownerNames.Add(handlerName);
            }
        }

        return (ownerNames, ownerDeps, incomplete);
    }

    private static bool ServiceCallEdgeMatches(ServiceCallEdge edge, string claimed)
    {
        if (NamesMatch(edge.To, claimed) ||
            NamesMatch(edge.Via, claimed) ||
            string.Equals(edge.Method, claimed, StringComparison.Ordinal))
        {
            return true;
        }

        var lastDot = claimed.LastIndexOf('.');
        if (lastDot <= 0 || lastDot >= claimed.Length - 1)
            return false;

        var targetPart = claimed[..lastDot];
        var methodPart = claimed[(lastDot + 1)..];
        return string.Equals(edge.Method, methodPart, StringComparison.Ordinal) &&
               (NamesMatch(edge.To, targetPart) || NamesMatch(edge.Via, targetPart));
    }

    private static bool HasAmbiguousServiceResolution(AnalysisResult result, IReadOnlyList<string> dependencies)
    {
        if (dependencies.Count == 0 || result.ServiceResolutions.Count == 0)
            return false;

        foreach (var dep in dependencies)
        {
            var depBase = StripGenericArguments(dep);
            foreach (var sr in result.ServiceResolutions)
            {
                if (!sr.IsAmbiguous && sr.Implementation is not null)
                    continue;
                if (NamesMatch(sr.InterfaceName, depBase) || NamesMatch(sr.Interface, dep))
                    return true;
            }
        }

        return false;
    }

    private static string StripGenericArguments(string typeName)
    {
        var angle = typeName.IndexOf('<');
        return angle > 0 ? typeName[..angle].Trim() : typeName;
    }

    private static string ProjectBaseName(string name)
    {
        var normalized = name.Replace('\\', '/').Trim();
        if (normalized.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            return Path.GetFileNameWithoutExtension(normalized);
        var lastSlash = normalized.LastIndexOf('/');
        return lastSlash >= 0 ? normalized[(lastSlash + 1)..] : normalized;
    }

    private static bool ConditionalReferenceMatches(
        ConditionalProjectReference conditional, string claimed, string targetBase)
    {
        if (!string.IsNullOrWhiteSpace(conditional.ResolvedProjectName) &&
            (string.Equals(conditional.ResolvedProjectName, claimed, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(conditional.ResolvedProjectName, targetBase, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return IncludeMatchesProject(conditional.Include, claimed, targetBase);
    }

    private static bool IncludeMatchesProject(string include, string claimed, string targetBase)
    {
        var normalizedInclude = include.Replace('\\', '/').Trim();
        var normalizedClaimed = claimed.Replace('\\', '/').Trim();
        if (string.Equals(normalizedInclude, normalizedClaimed, StringComparison.OrdinalIgnoreCase))
            return true;

        var fileName = Path.GetFileName(normalizedInclude);
        var projectName = Path.GetFileNameWithoutExtension(normalizedInclude);
        return string.Equals(fileName, claimed, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(projectName, targetBase, StringComparison.OrdinalIgnoreCase);
    }

    private static (List<string> Touched, bool Incomplete) CollectTouchedEntities(AnalysisResult result, Shape shape)
    {
        var touched = new List<string>();
        void AddUnique(IEnumerable<string> items)
        {
            foreach (var item in items)
            {
                if (!touched.Contains(item, StringComparer.Ordinal))
                    touched.Add(item);
            }
        }

        if (shape.Kind == "handler")
        {
            var handler = FindUnique(result.Handlers, shape.FullName);
            if (handler is null)
                return (touched, true);
            AddUnique(handler.TouchedEntities);
            return (touched, false);
        }

        var request = FindUnique(result.Requests, shape.FullName);
        if (request is null)
            return (touched, true);

        AddUnique(request.TouchedEntities);
        var incomplete = false;
        foreach (var handlerName in request.Handlers)
        {
            var (outcome, handler) = ResolveTyped(result.Handlers, handlerName);
            if (outcome == Match.Found && handler is not null)
                AddUnique(handler.TouchedEntities);
            else
                incomplete = true;
        }

        return (touched, incomplete);
    }

    private static bool TouchedEntityMatches(string storedLabel, string claimed)
    {
        if (string.Equals(storedLabel, claimed, StringComparison.OrdinalIgnoreCase))
            return true;

        var extracted = AgentReportFormatter.EntityNameFromLabel(storedLabel);
        return NamesMatch(extracted, claimed) ||
               string.Equals(extracted, claimed, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TestReferenceMatchesFilter(TestMethodReference reference, string filter)
    {
        if (string.Equals(reference.ProjectName, filter, StringComparison.OrdinalIgnoreCase))
            return true;
        if (NamesMatch(reference.ClassName, filter))
            return true;

        var dot = filter.LastIndexOf('.');
        if (dot <= 0 || dot >= filter.Length - 1)
            return false;

        var classPart = filter[..dot];
        var methodPart = filter[(dot + 1)..];
        return NamesMatch(reference.ClassName, classPart) &&
               string.Equals(reference.MethodName, methodPart, StringComparison.Ordinal);
    }

    private static ClaimCheck? CheckIncompleteOrFiltered(
        AnalysisResult result,
        Shape shape,
        string subject,
        ClaimRelation spec,
        string? claimed,
        string factNoun,
        bool includeContractDiagnostics,
        bool includeEfUnresolved = false,
        string noteSuffix = "")
    {
        if (!result.CanRunStrictChecks || !ScanComplete(result))
        {
            return NotDeterminable(subject, spec.Display, claimed, "scan incomplete",
                $"the scan reported errors or incomplete analysis diagnostics, so absence of a {factNoun} cannot be proved.{noteSuffix}",
                EvidenceLine($"{shape.Kind} `{shape.Name}`{Loc(shape.Location)}"));
        }

        if (result.HasSourceFilters)
        {
            return NotDeterminable(subject, spec.Display, claimed, "source filters active",
                $"the scan was restricted by include/exclude source filters, so omitted files may contain a {factNoun}.{noteSuffix}",
                EvidenceLine($"{shape.Kind} `{shape.Name}`{Loc(shape.Location)}"));
        }

        if (includeContractDiagnostics && DiagnosticHint(result, subject, spec.Display, claimed) is { } hint)
            return hint;

        if (includeEfUnresolved &&
            result.Diagnostics.Any(d => string.Equals(d.Code, AnalysisResult.EfEntityUnresolvedCode, StringComparison.Ordinal)))
        {
            return NotDeterminable(subject, spec.Display, claimed, "unresolved entity",
                $"the scan reported an unresolved EF Core entity diagnostic, so entity-touch absence cannot be proved.{noteSuffix}",
                EvidenceLine($"{shape.Kind} `{shape.Name}`{Loc(shape.Location)}"));
        }

        return null;
    }

    // ---------------------------------------------------------------- communicates with & entrypoint extensions

    private static ClaimCheck VerifyCommunicatesWith(
        AnalysisResult result, IReadOnlyList<Shape> shapes, ClaimRelation spec, string subject, string? claimed)
    {
        var (outcome, shape) = ResolveShape(shapes, subject, "project");
        if (outcome == Match.Absent)
        {
            var ci = shapes.Where(s => s.Kind == "project" &&
                string.Equals(s.Name, subject, StringComparison.OrdinalIgnoreCase)).ToList();
            if (ci.Count == 1)
                (outcome, shape) = (Match.Found, ci[0]);
            else if (ci.Count > 1)
                (outcome, shape) = (Match.Ambiguous, null);
        }

        if (outcome != Match.Found)
            return SubjectNotResolved(result, subject, spec, claimed, "project");

        if (claimed is null)
            return NotDeterminable(subject, spec.Display, claimed, "invalid claim", "relation requires an object.");

        var project = result.Projects.First(p => string.Equals(p.Name, shape!.FullName, StringComparison.Ordinal));
        var projectLoc = ProjectLocation(result, project);
        var targetBase = ProjectBaseName(claimed);
        var targetProjects = result.Projects
            .Where(p => string.Equals(p.Name, claimed, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(p.Name, targetBase, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (targetProjects.Count > 1)
        {
            return NotDeterminable(subject, spec.Display, claimed, "could not resolve",
                $"target project name \"{claimed}\" matches more than one project in the scan model.",
                EvidenceLine($"project `{project.Name}`{Loc(projectLoc)}"));
        }

        var matchingFlows = result.IntegrationFlows
            .Where(f => string.Equals(f.FromProject, project.Name, StringComparison.OrdinalIgnoreCase) &&
                        (string.Equals(f.ToProject, claimed, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(f.ToProject, targetBase, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (matchingFlows.Count > 0)
        {
            var first = matchingFlows[0];
            var targetProject = targetProjects.Count == 1 ? targetProjects[0] : null;
            var evidence = new[]
            {
                EvidenceLine($"integration flow `{first.FromProject}` → `{first.ToProject}` [{SystemViewFilter.FlowKindLabel(first.Kind)}: `{first.Label}`]{Loc(first.Location ?? projectLoc)}"),
                targetProject is null ? "" : EvidenceLine($"target project `{targetProject.Name}`{Loc(ProjectLocation(result, targetProject))}"),
            };
            return spec.IsAbsence
                ? NotSupported(subject, spec, claimed, "the scan model records an integration flow between the projects", evidence)
                : Supported(subject, spec, claimed, "the scan model records an integration flow between the projects", evidence);
        }

        if (CheckIncompleteOrFiltered(result, shape!, subject, spec, claimed,
                "integration flow", includeContractDiagnostics: false) is { } guardFailure)
        {
            return guardFailure;
        }

        var recordedTargets = result.IntegrationFlows
            .Where(f => string.Equals(f.FromProject, project.Name, StringComparison.OrdinalIgnoreCase))
            .Select(f => $"`{f.ToProject}` ({SystemViewFilter.FlowKindLabel(f.Kind)})")
            .Distinct(StringComparer.Ordinal)
            .Take(MaxListItems)
            .ToList();
        var flowSummary = recordedTargets.Count == 0
            ? "none"
            : string.Join(", ", recordedTargets);

        return NotDeterminable(subject, spec.Display, claimed, "outside the scan model",
            $"no static integration flow edge from `{project.Name}` to `{claimed}` was recorded in the scan model " +
            $"(recorded outgoing targets: {flowSummary}); static analysis cannot prove or refute runtime HTTP, gRPC, or broker communication without full wiring proof.",
            EvidenceLine($"project `{project.Name}`{Loc(projectLoc)}"));
    }

    private static ClaimCheck VerifyEntrypointPublishes(
        AnalysisResult result,
        IReadOnlyList<Shape> shapes,
        ClaimRelation spec,
        string subject,
        string? claimed,
        SystemEntrypointInfo ep)
    {
        if (claimed is null)
            return NotDeterminable(subject, spec.Display, claimed, "invalid claim", "relation requires an object.");

        var shape = new Shape("entrypoint", ep.Name, ep.Name, ep.ProjectName, ep.Location);
        var published = ep.PublishedMessages;
        var list = published.Count == 0 ? "none" : string.Join(", ", published.Take(MaxListItems).Select(n => $"`{n}`"));
        var matches = published.Where(m => NamesMatch(m, claimed) || string.Equals(CSharpScanner.SimpleName(m), claimed, StringComparison.OrdinalIgnoreCase)).ToList();
        var objectShape = FindShape(shapes, claimed, "notification");

        if (matches.Count > 0)
        {
            var evidence = new[]
            {
                EvidenceLine($"entrypoint `{ep.Name}` (`{ep.DisplayLabel}`){Loc(ep.Location)} publishes `{matches[0]}`"),
                objectShape is null ? "" : EvidenceLine($"notification `{objectShape.Name}`{Loc(objectShape.Location)}"),
            };
            return spec.IsAbsence
                ? NotSupported(subject, spec, claimed, "the scan model records the published message/event on this entrypoint", evidence)
                : Supported(subject, spec, claimed, "the scan model records the published message/event on this entrypoint", evidence);
        }

        if (CheckIncompleteOrFiltered(result, shape, subject, spec, claimed,
                "published message", includeContractDiagnostics: false) is { } guardFailure)
        {
            return guardFailure;
        }

        if (spec.IsAbsence && result.UnresolvedDispatches.Count > 0)
        {
            return NotDeterminable(subject, spec.Display, claimed, "unresolved dispatch (runtime-typed send)",
                $"the scan holds {result.UnresolvedDispatches.Count} unresolved dispatch call site(s), so non-publication cannot be guaranteed.",
                EvidenceLine($"entrypoint `{ep.Name}` (`{ep.DisplayLabel}`){Loc(ep.Location)} publishes: {list}"));
        }

        var summaryEvidence = EvidenceLine($"entrypoint `{ep.Name}` (`{ep.DisplayLabel}`){Loc(ep.Location)} publishes: {list}");
        return spec.IsAbsence
            ? Supported(subject, spec, claimed, "the scan model does not record that message/event as published by this entrypoint", summaryEvidence)
            : NotSupported(subject, spec, claimed, "the named message/event is not published by this entrypoint in the scan model", summaryEvidence);
    }

    private static ClaimCheck VerifyEntrypointUsesDbContext(
        AnalysisResult result,
        IReadOnlyList<Shape> shapes,
        ClaimRelation spec,
        string subject,
        string? claimed,
        SystemEntrypointInfo ep)
    {
        if (claimed is null)
            return NotDeterminable(subject, spec.Display, claimed, "invalid claim", "relation requires an object.");

        var contextShape = FindShape(shapes, claimed, "DbContext");
        if (contextShape is null)
        {
            return NotSupported(subject, spec, claimed, "no DbContext by that name is present in the scanned model",
                EvidenceLine($"no DbContext named `{claimed}` is present in the scanned model ({result.DbContexts.Count} DbContext(s) inventoried)"));
        }

        var shape = new Shape("entrypoint", ep.Name, ep.Name, ep.ProjectName, ep.Location);
        var uses = ep.TouchedDbContexts.Any(c =>
            NamesMatch(c, contextShape.FullName) ||
            NamesMatch(c, contextShape.Name) ||
            string.Equals(CSharpScanner.SimpleName(c), contextShape.Name, StringComparison.OrdinalIgnoreCase));

        if (uses)
        {
            var evidence = new[]
            {
                EvidenceLine($"entrypoint `{ep.Name}` (`{ep.DisplayLabel}`){Loc(ep.Location)} reaches DbContext `{contextShape.Name}`"),
                EvidenceLine($"DbContext `{contextShape.Name}`{Loc(contextShape.Location)}"),
            };
            return spec.IsAbsence
                ? NotSupported(subject, spec, claimed, "the scan model records DbContext use by this entrypoint", evidence)
                : Supported(subject, spec, claimed, "the scan model records DbContext use by this entrypoint", evidence);
        }

        if (CheckIncompleteOrFiltered(result, shape, subject, spec, claimed,
                "DbContext use", includeContractDiagnostics: false) is { } guardFailure)
        {
            return guardFailure;
        }

        if (spec.IsAbsence && result.DbContexts.Count == 0)
        {
            return Supported(subject, spec, claimed, "the scan model holds no DbContexts at all",
                EvidenceLine("this scan inventories 0 DbContexts"));
        }

        var ctxList = ep.TouchedDbContexts.Count == 0
            ? "none"
            : string.Join(", ", ep.TouchedDbContexts.Take(MaxListItems).Select(c => $"`{c}`"));
        return NotDeterminable(subject, spec.Display, claimed, "outside the scan model",
            $"no DbContext hop to `{claimed}` was recorded for entrypoint `{ep.Name}` (recorded DbContexts: {ctxList}); unresolved interface/repository hops stop fail-closed, so absence is not proof of non-use.",
            EvidenceLine($"entrypoint `{ep.Name}` (`{ep.DisplayLabel}`){Loc(ep.Location)}"),
            EvidenceLine($"DbContext `{contextShape.Name}`{Loc(contextShape.Location)}"));
    }

    private static ClaimCheck VerifyEntrypointCallsService(
        AnalysisResult result,
        ClaimRelation spec,
        string subject,
        string? claimed,
        SystemEntrypointInfo ep)
    {
        if (claimed is null)
            return NotDeterminable(subject, spec.Display, claimed, "invalid claim", "relation requires an object.");

        var shape = new Shape("entrypoint", ep.Name, ep.Name, ep.ProjectName, ep.Location);
        var matchedDirect = ep.CalledServices.Where(s =>
            NamesMatch(s, claimed) ||
            string.Equals(CSharpScanner.SimpleName(s), claimed, StringComparison.OrdinalIgnoreCase)).ToList();

        var ownerPrefix = OwnerTypeName(ep.Name);
        var edgeMatches = result.ServiceCallEdges
            .Where(e =>
                (NamesMatch(e.From, ep.Name) ||
                 (!string.IsNullOrEmpty(ownerPrefix) && NamesMatch(e.From, ownerPrefix)) ||
                 ep.CalledServices.Any(s => NamesMatch(e.From, s))) &&
                ServiceCallEdgeMatches(e, claimed))
            .ToList();

        if (edgeMatches.Count > 0 || matchedDirect.Count > 0)
        {
            var evidence = new List<string>
            {
                EvidenceLine($"entrypoint `{ep.Name}` (`{ep.DisplayLabel}`){Loc(ep.Location)}"),
            };
            if (edgeMatches.Count > 0)
            {
                var first = edgeMatches[0];
                evidence.Add(EvidenceLine($"service call `{first.From}` → `{first.To}`.`{first.Method}` via `{first.Via}`{Loc(first.Location)}"));
            }
            else
            {
                evidence.Add(EvidenceLine($"traced service dependency `{matchedDirect[0]}`"));
            }

            return spec.IsAbsence
                ? NotSupported(subject, spec, claimed, "the scan model records a matching service call/dependency on this entrypoint", evidence.ToArray())
                : Supported(subject, spec, claimed, "the scan model records a matching service call/dependency on this entrypoint", evidence.ToArray());
        }

        if (CheckIncompleteOrFiltered(result, shape, subject, spec, claimed,
                "service call", includeContractDiagnostics: false) is { } guardFailure)
        {
            return guardFailure;
        }

        if (HasAmbiguousServiceResolution(result, ep.CalledServices))
        {
            return NotDeterminable(subject, spec.Display, claimed, "ambiguous service resolution",
                $"entrypoint `{ep.Name}` depends on a service interface with an ambiguous service resolution, so calls through it cannot be settled statically.",
                EvidenceLine($"entrypoint `{ep.Name}` (`{ep.DisplayLabel}`){Loc(ep.Location)}"));
        }

        if (spec.IsAbsence && result.UnresolvedDispatches.Count > 0)
        {
            return NotDeterminable(subject, spec.Display, claimed, "unresolved dispatch (runtime-typed send)",
                $"the scan holds {result.UnresolvedDispatches.Count} unresolved dispatch call site(s), so non-invocation cannot be guaranteed.",
                EvidenceLine($"entrypoint `{ep.Name}` (`{ep.DisplayLabel}`){Loc(ep.Location)} has 0 matching service calls"));
        }

        var svcList = ep.CalledServices.Count == 0
            ? "none"
            : string.Join(", ", ep.CalledServices.Take(MaxListItems).Select(s => $"`{s}`"));
        var summaryEvidence = EvidenceLine($"entrypoint `{ep.Name}` (`{ep.DisplayLabel}`){Loc(ep.Location)} called services: {svcList}");

        return spec.IsAbsence
            ? Supported(subject, spec, claimed, "no matching service call is recorded for this entrypoint in the scanned model", summaryEvidence)
            : NotSupported(subject, spec, claimed, "no matching service call is recorded for this entrypoint in the scanned model", summaryEvidence);
    }

    private static ClaimCheck VerifyEntrypointTouchesEntity(
        AnalysisResult result,
        IReadOnlyList<Shape> shapes,
        ClaimRelation spec,
        string subject,
        string? claimed,
        SystemEntrypointInfo ep)
    {
        if (claimed is null)
            return NotDeterminable(subject, spec.Display, claimed, "invalid claim", "relation requires an object.");

        var shape = new Shape("entrypoint", ep.Name, ep.Name, ep.ProjectName, ep.Location);
        var matches = ep.TouchedEntities.Where(label => TouchedEntityMatches(label, claimed)).ToList();
        var entityShape = FindShape(shapes, claimed, "entity");

        if (matches.Count > 0)
        {
            var evidence = new[]
            {
                EvidenceLine($"entrypoint `{ep.Name}` (`{ep.DisplayLabel}`){Loc(ep.Location)} touches `{matches[0]}`"),
                entityShape is null ? "" : EvidenceLine($"entity `{entityShape.Name}`{Loc(entityShape.Location)}"),
            };
            return spec.IsAbsence
                ? NotSupported(subject, spec, claimed, "the scan model records the entity touch on this entrypoint", evidence)
                : Supported(subject, spec, claimed, "the scan model records the entity touch on this entrypoint", evidence);
        }

        if (CheckIncompleteOrFiltered(result, shape, subject, spec, claimed,
                "entity touch", includeContractDiagnostics: false, includeEfUnresolved: true) is { } guardFailure)
        {
            return guardFailure;
        }

        if (HasAmbiguousServiceResolution(result, ep.CalledServices))
        {
            return NotDeterminable(subject, spec.Display, claimed, "ambiguous service resolution",
                $"entrypoint `{ep.Name}` depends on a service interface with an ambiguous service resolution, so entity touches cannot be settled statically.",
                EvidenceLine($"entrypoint `{ep.Name}` (`{ep.DisplayLabel}`){Loc(ep.Location)}"));
        }

        var list = ep.TouchedEntities.Count == 0
            ? "none"
            : string.Join(", ", ep.TouchedEntities.Take(MaxListItems).Select(e => $"`{e}`"));
        var absentEvidence = EvidenceLine($"entrypoint `{ep.Name}` (`{ep.DisplayLabel}`){Loc(ep.Location)} touched entities: {list}");
        const string absentBasis = "the scan model records no matching entity touch for this entrypoint";

        return spec.IsAbsence
            ? Supported(subject, spec, claimed, absentBasis, absentEvidence)
            : NotSupported(subject, spec, claimed, absentBasis, absentEvidence);
    }

    private static (Match Outcome, SystemEntrypointInfo? Entrypoint) ResolveEntrypoint(
        AnalysisResult result,
        string subject)
    {
        var nonCqrs = result.SystemEntrypoints
            .Where(e => e.ExecutionStyle != SystemExecutionStyle.Cqrs)
            .ToList();
        if (nonCqrs.Count == 0)
            return (Match.Absent, null);

        var exact = nonCqrs
            .Where(e =>
                string.Equals(e.Name, subject, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(e.RouteOrTrigger, subject, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(e.DisplayLabel, subject, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (exact.Count == 1)
            return (Match.Found, exact[0]);
        if (exact.Count > 1)
            return (Match.Ambiguous, null);

        var suffix = nonCqrs
            .Where(e =>
                e.Name.EndsWith("." + subject, StringComparison.OrdinalIgnoreCase) ||
                e.RouteOrTrigger.EndsWith("." + subject, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (suffix.Count == 1)
            return (Match.Found, suffix[0]);
        if (suffix.Count > 1)
            return (Match.Ambiguous, null);

        return (Match.Absent, null);
    }

    private static ClaimCheck AmbiguousEntrypointSubject(string subject, ClaimRelation spec, string? claimed)
        => NotDeterminable(subject, spec.Display, claimed, "could not resolve",
            $"subject name \"{subject}\" matches more than one entrypoint shape in the scan model; qualify it with class.method or route.",
            EvidenceLine("ambiguous subject — no fact can be proven or refuted"));

    private static string OwnerTypeName(string entrypointName)
    {
        var dot = entrypointName.LastIndexOf('.');
        return dot > 0 ? entrypointName[..dot] : entrypointName;
    }

    // ---------------------------------------------------------------- result construction

    private static string EvidenceLine(string text) => text;

    private static string EdgeList(IReadOnlyList<DispatchCallSite> sites)
        => sites.Count == 0
            ? "no resolved dispatch call sites target this shape"
            : $"resolved dispatch sites: {string.Join("; ", sites.Take(3).Select(s => $"`{s.SenderFullName ?? "?"}` at {Loc(s.Location, plain: true)}"))}";

    private static string DescribeOutcome(Match outcome) => outcome switch
    {
        Match.Found => "1 match",
        Match.Ambiguous => "2 or more matches",
        _ => "no match",
    };

    private static ClaimCheck Supported(string subject, ClaimRelation spec, string? claimed, string basis, params string[] evidence)
        => new(ClaimVerdict.Supported, subject, spec.Display, claimed, basis, Clean(evidence), null, null);

    private static ClaimCheck NotSupported(string subject, ClaimRelation spec, string? claimed, string basis, params string[] evidence)
        => new(ClaimVerdict.NotSupported, subject, spec.Display, claimed, basis, Clean(evidence), null, null);

    private static ClaimCheck NotDeterminable(
        string subject, string relation, string? claimed, string reason, string detail, params string[] evidence)
        => new(ClaimVerdict.NotDeterminable, subject, relation, claimed, "not determinable", Clean(evidence), reason, detail);

    private static IReadOnlyList<string> Clean(IEnumerable<string> evidence)
        => evidence.Where(e => !string.IsNullOrEmpty(e)).ToList();

    private static string Loc(SourceLocation? location, bool plain = false)
        => location is null ? "" : plain ? $"{location.File}:{location.Line}" : $" (`{location.File}:{location.Line}`)";

    // ---------------------------------------------------------------- formatting

    internal static string Format(ClaimCheck check)
    {
        var sb = new StringBuilder();
        sb.Append("verdict: ").AppendLine(VerdictText(check.Verdict));
        sb.Append("claim: subject=\"").Append(check.Subject)
          .Append("\" relation=\"").Append(check.Relation)
          .Append("\" object=").AppendLine(check.Object is null ? "(none)" : $"\"{check.Object}\"");

        if (check.Verdict == ClaimVerdict.NotDeterminable)
        {
            sb.Append("reason: ").AppendLine(check.Reason ?? "not determinable");
            sb.Append("detail: ").AppendLine(check.Detail ?? "");
        }
        else
        {
            sb.Append("basis: ").AppendLine(check.Basis);
        }

        sb.Append("evidence: ").Append(check.Evidence.Count).AppendLine(" fact(s)");
        foreach (var line in check.Evidence)
            sb.Append("- ").AppendLine(line);

        return sb.ToString();
    }

    private static string VerdictText(ClaimVerdict verdict) => verdict switch
    {
        ClaimVerdict.Supported => "supported",
        ClaimVerdict.NotSupported => "not supported",
        _ => "not determinable",
    };
}
