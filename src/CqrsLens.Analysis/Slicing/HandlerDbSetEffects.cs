using System.Text.RegularExpressions;
using static CqrsLens.DeepSlicePatterns;

namespace CqrsLens;

/// <summary>
/// Detects direct <c>DbContext.DbSet</c> access and <c>Set&lt;T&gt;()</c> calls inside a
/// handler, attributing them only to the handler that declares or infers the receiver.
/// </summary>
internal static class HandlerDbSetEffects
{
    internal static void Apply(
        HandlerEffectContext context,
        Dictionary<string, string> receiverTypes)
    {
        var index = context.Index;
        if (index.DbSetsByProperty.Count == 0 && index.DbSetsByEntity.Count == 0)
            return;

        foreach (Match match in MemberAccessRegex().Matches(context.File.Code, context.BodyStart))
        {
            if (match.Index >= context.BodyEnd) break;
            if (!index.DbSetsByProperty.TryGetValue(match.Groups["prop"].Value, out var label)) continue;
            if (!context.Mask.IsCode(match.Index)) continue;
            if (!ReferenceEquals(DeepSliceSyntax.InnermostClass(context.Scan.Classes, match.Index), context.Decl)) continue;
            if (!ReceiverTypeMapBuilder.IsDbContextReceiver(
                    ReceiverTypeMapBuilder.GetReceiverIdentifier(context.File.Code, match.Index),
                    receiverTypes,
                    index.DbContextTypeNames))
                continue;

            context.Touch(label);
        }

        foreach (Match match in DbContextSetMethodRegex().Matches(context.File.Code, context.BodyStart))
        {
            if (match.Index >= context.BodyEnd) break;
            if (!context.Mask.IsCode(match.Index)) continue;
            if (!ReferenceEquals(DeepSliceSyntax.InnermostClass(context.Scan.Classes, match.Index), context.Decl)) continue;
            if (!ReceiverTypeMapBuilder.IsDbContextReceiver(
                    ReceiverTypeMapBuilder.GetReceiverIdentifier(context.File.Code, match.Index),
                    receiverTypes,
                    index.DbContextTypeNames))
                continue;

            var entitySimple = CSharpScanner.SimpleName(match.Groups["entity"].Value.Trim());
            if (!ReceiverTypeMapBuilder.IsEntityCandidate(entitySimple, context.Decl, index.DbSetsByEntity))
                continue;
            if (index.DbSetsByEntity.TryGetValue(entitySimple, out var label))
                context.Touch(label);
        }
    }
}
