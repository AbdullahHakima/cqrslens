using System.Text.RegularExpressions;
using static CqrsLens.DeepSlicePatterns;

namespace CqrsLens;

/// <summary>
/// Detects domain events raised by static factories and instance member calls on a handler's
/// receiver, matching only the receiver's declared or inferred type.
/// </summary>
internal static class HandlerMemberEffects
{
    internal static void Apply(
        HandlerEffectContext context,
        Dictionary<string, string> receiverTypes)
    {
        if (context.Index.MemberEvents.Count == 0)
            return;

        foreach (Match match in StaticMethodCallRegex().Matches(context.File.Code, context.BodyStart))
        {
            if (match.Index >= context.BodyEnd) break;
            if (!context.Mask.IsCode(match.Index)) continue;
            if (!ReferenceEquals(
                    DeepSliceSyntax.InnermostClass(context.Scan.Classes, match.Index), context.Decl))
                continue;

            var targetType = match.Groups["type"].Value;
            if (!context.Index.MemberEvents.TryGetValue(
                    (targetType, match.Groups["method"].Value), out var staticEvents))
                continue;
            foreach (var eventSimpleName in staticEvents)
                context.Publish(eventSimpleName);
        }

        // Instance member calls and property sets (`order.Update()`, `entity.Done = true`). The
        // member is matched only against the receiver's declared or inferred type, never against
        // every active type, so `order.Update()` cannot report `Customer.Update()`'s event.
        foreach (Match match in MemberAccessRegex().Matches(context.File.Code, context.BodyStart))
        {
            if (match.Index >= context.BodyEnd) break;
            if (!context.Mask.IsCode(match.Index)) continue;
            if (!ReferenceEquals(
                    DeepSliceSyntax.InnermostClass(context.Scan.Classes, match.Index), context.Decl))
                continue;

            var member = match.Groups["prop"].Value;
            var receiver = ReceiverTypeMapBuilder.GetReceiverIdentifier(context.File.Code, match.Index);
            foreach (var receiverType in ReceiverTypeMapBuilder.ResolveReceiverTypes(receiver, receiverTypes))
            {
                if (!context.Index.MemberEvents.TryGetValue((receiverType, member), out var raisedEvents))
                    continue;
                foreach (var eventSimpleName in raisedEvents)
                    context.Publish(eventSimpleName);
            }
        }
    }
}
