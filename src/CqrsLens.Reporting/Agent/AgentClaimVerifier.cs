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
        if (outcome != Match.Found)
            return SubjectNotResolved(result, subject, spec, claimed, "request, handler or notification handler");

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
        if (outcome != Match.Found)
            return SubjectNotResolved(result, subject, spec, claimed, "handler or request");

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
        var uses = owners.Any(h => HandlerUsesContext(result, h, contextShape));
        var ownerNames = owners.Count == 0
            ? "none resolved"
            : string.Join(", ", owners.Take(MaxListItems).Select(h => $"`{h.Name}`"));

        var modelNote =
            $"the scan model holds {edgeCount} handler→DbContext edge(s); interface/repository→DbContext hops are not modelled " +
            "(Stage 3 H1), so absence is not proof of non-use.";

        if (!spec.IsAbsence)
        {
            return uses
                ? Supported(subject, spec, claimed, "the scan model records direct DbContext use",
                    EvidenceLine($"{shape!.Kind} `{shape.Name}`{Loc(shape.Location)} owner handler(s): {ownerNames}"),
                    EvidenceLine($"DbContext `{contextShape.Name}`{Loc(contextShape.Location)} is named by a handler dependency or flow edge"))
                : NotDeterminable(subject, spec.Display, claimed, "outside the scan model",
                    $"{modelNote} No direct dependency or handler→DbContext edge to `{claimed}` was recorded for {ownerNames}.",
                    EvidenceLine($"{shape!.Kind} `{shape.Name}`{Loc(shape.Location)}"),
                    EvidenceLine($"DbContext `{contextShape.Name}`{Loc(contextShape.Location)}"));
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
