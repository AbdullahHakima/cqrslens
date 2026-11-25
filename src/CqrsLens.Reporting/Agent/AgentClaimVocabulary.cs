namespace CqrsLens;

/// <summary>
/// The frozen relation vocabulary for <c>verify_claim</c>. Every entry is backed by a fact the
/// scan model actually holds (see <see cref="AgentClaimVerifier"/>). Aliases are accepted but
/// normalise to one canonical id.
/// </summary>
internal static class AgentClaimVocabulary
{
    private static readonly ClaimRelation[] Relations =
    {
        new("has handler", "has handler", IsAbsence: false, ObjectRequired: false,
            new[] { ClaimSubjectKind.Request }),
        new("has no handler", "has no handler", IsAbsence: true, ObjectRequired: false,
            new[] { ClaimSubjectKind.Request }),

        new("has validator", "has validator", IsAbsence: false, ObjectRequired: false,
            new[] { ClaimSubjectKind.Request }),
        new("has no validator", "has no validator", IsAbsence: true, ObjectRequired: false,
            new[] { ClaimSubjectKind.Request }),

        new("publishes", "publishes", IsAbsence: false, ObjectRequired: true,
            new[] { ClaimSubjectKind.Request, ClaimSubjectKind.Handler, ClaimSubjectKind.NotificationHandler, ClaimSubjectKind.Entrypoint }),
        new("does not publish", "does not publish", IsAbsence: true, ObjectRequired: true,
            new[] { ClaimSubjectKind.Request, ClaimSubjectKind.Handler, ClaimSubjectKind.NotificationHandler, ClaimSubjectKind.Entrypoint }),

        new("handled notification", "handled notification", IsAbsence: false, ObjectRequired: true,
            new[] { ClaimSubjectKind.NotificationHandler }),
        new("does not handle notification", "does not handle notification", IsAbsence: true, ObjectRequired: true,
            new[] { ClaimSubjectKind.NotificationHandler }),

        new("uses DbContext", "uses DbContext", IsAbsence: false, ObjectRequired: true,
            new[] { ClaimSubjectKind.Handler, ClaimSubjectKind.Request, ClaimSubjectKind.Entrypoint }),
        new("does not use DbContext", "does not use DbContext", IsAbsence: true, ObjectRequired: true,
            new[] { ClaimSubjectKind.Handler, ClaimSubjectKind.Request, ClaimSubjectKind.Entrypoint }),

        new("depends on", "depends on", IsAbsence: false, ObjectRequired: true,
            new[] { ClaimSubjectKind.Handler, ClaimSubjectKind.NotificationHandler }),
        new("does not depend on", "does not depend on", IsAbsence: true, ObjectRequired: true,
            new[] { ClaimSubjectKind.Handler, ClaimSubjectKind.NotificationHandler }),

        new("belongs to layer", "belongs to layer", IsAbsence: false, ObjectRequired: true,
            AllShapeKinds()),
        new("does not belong to layer", "does not belong to layer", IsAbsence: true, ObjectRequired: true,
            AllShapeKinds()),

        new("dispatched by", "dispatched by", IsAbsence: false, ObjectRequired: true,
            new[] { ClaimSubjectKind.Request, ClaimSubjectKind.Notification }),
        new("not dispatched by", "not dispatched by", IsAbsence: true, ObjectRequired: true,
            new[] { ClaimSubjectKind.Request, ClaimSubjectKind.Notification }),

        new("calls service", "calls service", IsAbsence: false, ObjectRequired: true,
            new[] { ClaimSubjectKind.Request, ClaimSubjectKind.Handler, ClaimSubjectKind.NotificationHandler, ClaimSubjectKind.Entrypoint }),
        new("does not call service", "does not call service", IsAbsence: true, ObjectRequired: true,
            new[] { ClaimSubjectKind.Request, ClaimSubjectKind.Handler, ClaimSubjectKind.NotificationHandler, ClaimSubjectKind.Entrypoint }),

        new("references project", "references project", IsAbsence: false, ObjectRequired: true,
            new[] { ClaimSubjectKind.Project }),
        new("does not reference project", "does not reference project", IsAbsence: true, ObjectRequired: true,
            new[] { ClaimSubjectKind.Project }),

        new("communicates with", "communicates with", IsAbsence: false, ObjectRequired: true,
            new[] { ClaimSubjectKind.Project }),
        new("does not communicate with", "does not communicate with", IsAbsence: true, ObjectRequired: true,
            new[] { ClaimSubjectKind.Project }),

        new("touches entity", "touches entity", IsAbsence: false, ObjectRequired: true,
            new[] { ClaimSubjectKind.Handler, ClaimSubjectKind.Request, ClaimSubjectKind.Entrypoint }),
        new("does not touch entity", "does not touch entity", IsAbsence: true, ObjectRequired: true,
            new[] { ClaimSubjectKind.Handler, ClaimSubjectKind.Request, ClaimSubjectKind.Entrypoint }),

        new("has test reference", "has test reference", IsAbsence: false, ObjectRequired: false,
            new[] { ClaimSubjectKind.Request, ClaimSubjectKind.Handler, ClaimSubjectKind.Validator }),
        new("has no test reference", "has no test reference", IsAbsence: true, ObjectRequired: false,
            new[] { ClaimSubjectKind.Request, ClaimSubjectKind.Handler, ClaimSubjectKind.Validator }),
    };

    private static IReadOnlyList<ClaimSubjectKind> AllShapeKinds() => new[]
    {
        ClaimSubjectKind.Project, ClaimSubjectKind.Request, ClaimSubjectKind.Handler,
        ClaimSubjectKind.Notification, ClaimSubjectKind.NotificationHandler,
        ClaimSubjectKind.Validator, ClaimSubjectKind.Behavior, ClaimSubjectKind.DbContext,
        ClaimSubjectKind.Entity, ClaimSubjectKind.Entrypoint,
    };

    private static readonly Dictionary<string, ClaimRelation> ByCanonical =
        Relations.ToDictionary(r => r.Id, StringComparer.Ordinal);

    /// <summary>Accepted spellings, including synonyms and their inflections.</summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["has handler"] = "has handler",
        ["has a handler"] = "has handler",
        ["handled by"] = "has handler",
        ["is handled by"] = "has handler",

        ["has no handler"] = "has no handler",
        ["has no handlers"] = "has no handler",
        ["no handler"] = "has no handler",
        ["not handled by"] = "has no handler",
        ["is not handled by"] = "has no handler",
        ["does not have a handler"] = "has no handler",
        ["does not have handler"] = "has no handler",

        ["has validator"] = "has validator",
        ["has a validator"] = "has validator",
        ["validated by"] = "has validator",
        ["is validated by"] = "has validator",

        ["has no validator"] = "has no validator",
        ["has no validators"] = "has no validator",
        ["no validator"] = "has no validator",
        ["not validated by"] = "has no validator",
        ["is not validated by"] = "has no validator",
        ["does not have a validator"] = "has no validator",

        ["publishes"] = "publishes",
        ["publish"] = "publishes",
        ["publishes notification"] = "publishes",
        ["publishes a notification"] = "publishes",

        ["does not publish"] = "does not publish",
        ["publishes no"] = "does not publish",
        ["publishes no notification"] = "does not publish",
        ["does not publish notification"] = "does not publish",

        ["handled notification"] = "handled notification",
        ["handles notification"] = "handled notification",
        ["handles"] = "handled notification",
        ["handle notification"] = "handled notification",

        ["does not handle notification"] = "does not handle notification",
        ["does not handle"] = "does not handle notification",
        ["handles no notification"] = "does not handle notification",

        ["uses dbcontext"] = "uses DbContext",
        ["uses db context"] = "uses DbContext",
        ["uses efcore dbcontext"] = "uses DbContext",

        ["does not use dbcontext"] = "does not use DbContext",
        ["does not use db context"] = "does not use DbContext",
        ["uses no dbcontext"] = "does not use DbContext",

        ["depends on"] = "depends on",
        ["depends upon"] = "depends on",
        ["has dependency"] = "depends on",

        ["does not depend on"] = "does not depend on",
        ["does not depend upon"] = "does not depend on",
        ["does not have dependency"] = "does not depend on",

        ["belongs to layer"] = "belongs to layer",
        ["belongs to"] = "belongs to layer",
        ["is in layer"] = "belongs to layer",
        ["in layer"] = "belongs to layer",

        ["does not belong to layer"] = "does not belong to layer",
        ["does not belong to"] = "does not belong to layer",
        ["is not in layer"] = "does not belong to layer",
        ["not in layer"] = "does not belong to layer",

        ["dispatched by"] = "dispatched by",
        ["is dispatched by"] = "dispatched by",
        ["sent by"] = "dispatched by",
        ["is sent by"] = "dispatched by",

        ["not dispatched by"] = "not dispatched by",
        ["is not dispatched by"] = "not dispatched by",
        ["not sent by"] = "not dispatched by",
        ["is not sent by"] = "not dispatched by",

        ["calls service"] = "calls service",
        ["call service"] = "calls service",
        ["calls"] = "calls service",
        ["invokes"] = "calls service",
        ["invokes service"] = "calls service",

        ["does not call service"] = "does not call service",
        ["does not call"] = "does not call service",
        ["calls no service"] = "does not call service",
        ["does not invoke"] = "does not call service",
        ["does not invoke service"] = "does not call service",

        ["references project"] = "references project",
        ["reference project"] = "references project",
        ["project references"] = "references project",
        ["references"] = "references project",

        ["does not reference project"] = "does not reference project",
        ["does not reference"] = "does not reference project",
        ["references no project"] = "does not reference project",
        ["project does not reference"] = "does not reference project",

        ["communicates with"] = "communicates with",
        ["communicate with"] = "communicates with",
        ["integrates with"] = "communicates with",
        ["integrate with"] = "communicates with",

        ["does not communicate with"] = "does not communicate with",
        ["does not integrate with"] = "does not communicate with",
        ["communicates with no"] = "does not communicate with",
        ["integrates with no"] = "does not communicate with",

        ["touches entity"] = "touches entity",
        ["touch entity"] = "touches entity",
        ["touches"] = "touches entity",
        ["reads or writes entity"] = "touches entity",
        ["uses entity"] = "touches entity",

        ["does not touch entity"] = "does not touch entity",
        ["does not touch"] = "does not touch entity",
        ["touches no entity"] = "does not touch entity",
        ["does not read or write entity"] = "does not touch entity",
        ["does not use entity"] = "does not touch entity",

        ["has test reference"] = "has test reference",
        ["has test references"] = "has test reference",
        ["has a test reference"] = "has test reference",
        ["referenced by tests"] = "has test reference",
        ["is referenced by tests"] = "has test reference",
        ["has tests"] = "has test reference",
        ["has test"] = "has test reference",

        ["has no test reference"] = "has no test reference",
        ["has no test references"] = "has no test reference",
        ["no test reference"] = "has no test reference",
        ["not referenced by tests"] = "has no test reference",
        ["is not referenced by tests"] = "has no test reference",
        ["has no tests"] = "has no test reference",
        ["has no test"] = "has no test reference",
        ["does not have tests"] = "has no test reference",
        ["does not have test reference"] = "has no test reference",
    };

    /// <summary>Canonical relation spellings, for the out-of-vocabulary answer.</summary>
    internal static string KnownRelations => string.Join(", ", Relations.Select(r => $"`{r.Id}`"));

    internal static bool TryResolve(string? relation, out ClaimRelation relationSpec, out string normalized)
    {
        normalized = Normalize(relation);
        if (Aliases.TryGetValue(normalized, out var canonical) &&
            ByCanonical.TryGetValue(canonical, out relationSpec!))
        {
            return true;
        }

        relationSpec = null!;
        return false;
    }

    /// <summary>Lower-cases, trims and collapses whitespace so spelling variants resolve to one id.</summary>
    internal static string Normalize(string? relation)
    {
        if (string.IsNullOrWhiteSpace(relation))
            return "";

        var collapsed = string.Join(' ', relation.Trim()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.ToLowerInvariant();
    }
}
