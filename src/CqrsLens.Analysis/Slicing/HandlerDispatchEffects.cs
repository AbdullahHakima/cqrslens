using System.Text.RegularExpressions;
using static CqrsLens.DeepSlicePatterns;

namespace CqrsLens;

/// <summary>
/// Resolves object creations, constructor events and generic dispatch calls inside a handler
/// to published notifications and dispatched requests.
/// </summary>
internal static class HandlerDispatchEffects
{
    internal static void Apply(HandlerEffectContext context)
    {
        foreach (var typeRef in EnumerateDispatchedTypeRefs(context))
        {
            var simple = CSharpScanner.UnqualifiedTypeName(typeRef);

            context.Publish(simple);

            if (context.Index.RequestsBySimple.TryGetValue(simple, out var requestHits))
            {
                var request = DeepSliceCandidateResolver.PickRequest(
                    requestHits, context.File.ProjectName, context.Scan.Usings);
                if (request is not null &&
                    !string.Equals(request.Name, context.SelfRequestName, StringComparison.Ordinal) &&
                    !context.DispatchedRequests.Contains(request.FullName, StringComparer.Ordinal))
                {
                    context.DispatchedRequests.Add(request.FullName);
                }
            }

            // `new DomainEntity(...)` only publishes what that entity's *constructor* raises.
            if (context.Index.MemberEvents.TryGetValue((simple, simple), out var ctorEventNames))
            {
                foreach (var eventSimpleName in ctorEventNames)
                    context.Publish(eventSimpleName);
            }
        }
    }

    private static IEnumerable<string> EnumerateDispatchedTypeRefs(HandlerEffectContext context)
    {
        foreach (Match match in ObjectCreationRegex().Matches(context.File.Code, context.BodyStart))
        {
            if (match.Index >= context.BodyEnd) break;
            if (!context.Mask.IsCode(match.Index)) continue;
            if (!ReferenceEquals(
                    DeepSliceSyntax.InnermostClass(context.Scan.Classes, match.Index), context.Decl))
                continue;
            yield return match.Groups["type"].Value;
        }

        foreach (Match match in GenericDispatchRegex().Matches(context.File.Code, context.BodyStart))
        {
            if (match.Index >= context.BodyEnd) break;
            if (!context.Mask.IsCode(match.Index)) continue;
            if (!ReferenceEquals(
                    DeepSliceSyntax.InnermostClass(context.Scan.Classes, match.Index), context.Decl))
                continue;
            yield return match.Groups["type"].Value;
        }
    }
}
