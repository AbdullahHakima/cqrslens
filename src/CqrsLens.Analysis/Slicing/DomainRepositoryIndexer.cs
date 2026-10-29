using System.Text.RegularExpressions;
using static CqrsLens.DeepSlicePatterns;

namespace CqrsLens;

/// <summary>
/// Maps repository and service types, plus their non-generic interfaces, to the entities they
/// touch through dependencies, base types or direct DbSet access.
/// </summary>
internal static class DomainRepositoryIndexer
{
    internal static void Index(
        List<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles,
        Dictionary<string, string> dbSetsByProperty,
        Dictionary<string, string> dbSetsByEntity,
        HashSet<string> dbContextTypeNames,
        Dictionary<string, List<HandlerInfo>> handlersByFull,
        Dictionary<string, List<NotificationHandlerInfo>> notifHandlersByFull,
        DomainEventIndexCollector collector)
    {
        foreach (var (file, scan, mask) in parsedFiles)
        {
            foreach (var decl in scan.Classes)
            {
                if (handlersByFull.ContainsKey(decl.FullName) ||
                    notifHandlersByFull.ContainsKey(decl.FullName))
                    continue;

                if (decl.BodySpan.End <= decl.BodySpan.Start)
                    continue;

                var bodyStart = decl.BodySpan.Start;
                var bodyEnd = Math.Min(file.Code.Length, decl.BodySpan.End);

                var classDeps = new List<string>();
                DependencyExtractor.Extract(decl, file.Code, mask, scan.Classes, classDeps);
                var classTouchedLabels = new List<string>();

                void TouchClass(string label)
                {
                    if (!classTouchedLabels.Contains(label, StringComparer.Ordinal))
                        classTouchedLabels.Add(label);
                }

                foreach (var typeRef in classDeps.Concat(CSharpScanner.SplitTopLevel(decl.BaseList)))
                {
                    foreach (Match match in GenericRepositoryRegex().Matches(typeRef))
                    {
                        var entitySimple = CSharpScanner.SimpleName(match.Groups["entity"].Value.Trim());
                        if (!ReceiverTypeMapBuilder.IsEntityCandidate(entitySimple, decl, dbSetsByEntity))
                            continue;

                        TouchClass(dbSetsByEntity.TryGetValue(entitySimple, out var label)
                            ? label
                            : entitySimple);
                    }
                }

                var namedRepo = NamedRepositoryRegex().Match(decl.Name);
                if (namedRepo.Success &&
                    dbSetsByEntity.TryGetValue(namedRepo.Groups["entity"].Value, out var stemLabel))
                    TouchClass(stemLabel);

                if (dbSetsByProperty.Count > 0 || dbSetsByEntity.Count > 0)
                {
                    Dictionary<string, string>? receiverTypes = null;

                    foreach (Match match in MemberAccessRegex().Matches(file.Code, bodyStart))
                    {
                        if (match.Index >= bodyEnd) break;
                        if (!dbSetsByProperty.TryGetValue(match.Groups["prop"].Value, out var label)) continue;
                        if (!mask.IsCode(match.Index)) continue;
                        if (!ReferenceEquals(
                                DeepSliceSyntax.InnermostClass(scan.Classes, match.Index), decl))
                            continue;

                        // Built lazily: most classes never reference a DbSet-named member.
                        receiverTypes ??= ReceiverTypeMapBuilder.Build(
                            decl,
                            file.Code,
                            mask,
                            scan.Classes,
                            dbSetsByProperty,
                            dbContextTypeNames,
                            collector.RepoEntitiesByType);
                        if (ReceiverTypeMapBuilder.IsDbContextReceiver(
                                ReceiverTypeMapBuilder.GetReceiverIdentifier(file.Code, match.Index),
                                receiverTypes,
                                dbContextTypeNames))
                            TouchClass(label);
                    }

                    foreach (Match match in DbContextSetMethodRegex().Matches(file.Code, bodyStart))
                    {
                        if (match.Index >= bodyEnd) break;
                        if (!mask.IsCode(match.Index)) continue;
                        if (!ReferenceEquals(
                                DeepSliceSyntax.InnermostClass(scan.Classes, match.Index), decl))
                            continue;

                        receiverTypes ??= ReceiverTypeMapBuilder.Build(
                            decl,
                            file.Code,
                            mask,
                            scan.Classes,
                            dbSetsByProperty,
                            dbContextTypeNames,
                            collector.RepoEntitiesByType);
                        if (!ReceiverTypeMapBuilder.IsDbContextReceiver(
                                ReceiverTypeMapBuilder.GetReceiverIdentifier(file.Code, match.Index),
                                receiverTypes,
                                dbContextTypeNames))
                            continue;

                        var entitySimple = CSharpScanner.SimpleName(match.Groups["entity"].Value.Trim());
                        if (!ReceiverTypeMapBuilder.IsEntityCandidate(entitySimple, decl, dbSetsByEntity))
                            continue;
                        if (dbSetsByEntity.TryGetValue(entitySimple, out var label))
                            TouchClass(label);
                    }
                }

                // Register the class and its non-generic interfaces (IOrderRepository,
                // IDeleteService). Generic interfaces (IRepository<Order>) are skipped: the map is
                // keyed by simple name, so registering them would attach Order to every
                // IRepository<X> consumer.
                var nonGenericInterfaces = ReceiverTypeMapBuilder.NonGenericInterfaceNames(decl);
                foreach (var label in classTouchedLabels)
                {
                    collector.AddRepoEntity(decl.Name, label);
                    foreach (var iface in nonGenericInterfaces)
                        collector.AddRepoEntity(iface, label);
                }
            }
        }
    }
}
