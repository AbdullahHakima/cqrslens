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
        foreach (var (typeRef, index) in EnumerateDispatchedTypeRefs(context))
        {
            var simple = CSharpScanner.UnqualifiedTypeName(typeRef);
            var location = context.LocationAt(index);

            context.Publish(simple, location, EvidenceStrength.Proven);

            if (context.Index.RequestsBySimple.TryGetValue(simple, out var requestHits))
            {
                var request = DeepSliceCandidateResolver.PickRequest(
                    requestHits, context.File.ProjectName, context.Scan.Usings);
                if (request is not null &&
                    !string.Equals(request.Name, context.SelfRequestName, StringComparison.Ordinal))
                {
                    context.Dispatch(request, location, EvidenceStrength.Proven);
                }
            }

            // `new DomainEntity(...)` only publishes what that entity's *constructor* raises.
            if (context.Index.MemberEvents.TryGetValue((simple, simple), out var ctorEventNames))
            {
                foreach (var eventSimpleName in ctorEventNames)
                    context.Publish(eventSimpleName, location, EvidenceStrength.Resolved);
            }
        }
    }

    private static IEnumerable<(string TypeRef, int Index)> EnumerateDispatchedTypeRefs(HandlerEffectContext context)
    {
        foreach (Match match in ObjectCreationRegex().Matches(context.File.Code, context.BodyStart))
        {
            if (match.Index >= context.BodyEnd) break;
            if (!context.Mask.IsCode(match.Index)) continue;
            if (!ReferenceEquals(
                    DeepSliceSyntax.InnermostClass(context.Scan.Classes, match.Index), context.Decl))
                continue;
            yield return (match.Groups["type"].Value, match.Index);
        }

        foreach (Match match in GenericDispatchRegex().Matches(context.File.Code, context.BodyStart))
        {
            if (match.Index >= context.BodyEnd) break;
            if (!context.Mask.IsCode(match.Index)) continue;
            if (!ReferenceEquals(
                    DeepSliceSyntax.InnermostClass(context.Scan.Classes, match.Index), context.Decl))
                continue;
            yield return (match.Groups["type"].Value, match.Index);
        }
    }
}
