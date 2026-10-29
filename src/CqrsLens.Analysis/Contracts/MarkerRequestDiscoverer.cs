namespace CqrsLens;

/// <summary>
/// Finds requests declared through a source interface chain or a custom base class
/// that reaches IRequest or a configured marker. Direct declarations are already
/// applied by <see cref="CSharpScanner.ApplyScan"/>; this adds only indirect shapes.
/// </summary>
internal static class MarkerRequestDiscoverer
{
    public static void Discover(
        CSharpScanner.FileScan scan,
        SourceFile file,
        TypeHierarchy hierarchy,
        IReadOnlyDictionary<string, List<string>> interfaceMap,
        IReadOnlySet<string> markers,
        AnalysisResult result)
    {
        foreach (var declaration in scan.Classes)
        {
            if (declaration.IsAbstract) continue;
            if (declaration.BaseRawNames.Contains("IRequest")) continue;
            if (declaration.BaseRawNames.Contains("IRequestHandler")) continue;
            if (declaration.BaseRawNames.Count == 0) continue;

            if (declaration.BaseRawNames.Any(b => b is "DbContext" or "IdentityDbContext" or
                                                      "AbstractValidator" or "IValidator" or
                                                      "Validator" or "IPipelineBehavior" or
                                                      "INotification" or "IDomainEvent" or
                                                      "INotificationHandler" or "IDomainEventHandler"))
                continue;

            var location = SourceLocation.Create(
                file.Path, result.RootPath, file.Code, declaration.NameIndex);
            var directInterfaces = new List<string>();
            var directAmbiguity = false;
            foreach (var part in CSharpScanner.SplitTopLevel(declaration.BaseList))
            {
                var (name, args) = CSharpScanner.ParseTypeReference(part);
                var resolved = hierarchy.ResolveInterfaceReference(
                    name,
                    declaration.Namespace,
                    declaration.Usings,
                    declaration.ContainingTypes,
                    args.Count,
                    out var ambiguous);
                if (resolved is null)
                {
                    var simple = CSharpScanner.SimpleName(name);
                    if (markers.Contains(simple) || simple is "IRequest")
                    {
                        directAmbiguity = true;
                        ContractDiagnostics.RecordAmbiguous(
                            result, file, declaration.NameIndex, declaration.Name,
                            name, ambiguous, "request marker");
                    }
                    continue;
                }

                directInterfaces.Add(resolved);
            }

            var reached = MarkerResolver.ReachableMarkers(directInterfaces, interfaceMap, markers);
            string? shapeResponseType = null;
            if (reached.Count == 0)
            {
                // Direct interfaces did not reach a root. Follow source-declared
                // base classes (QueryBase<T>, custom command bases) as well; this
                // is deliberately attempted only as a fallback so direct-interface
                // behavior and its diagnostics stay unchanged.
                var shape = hierarchy.ResolveRequestShape(declaration, markers);
                if (shape.Roots.Count == 0)
                {
                    if (!directAmbiguity)
                    {
                        ContractDiagnostics.RecordRequestResolutionGap(
                            result, file, declaration.NameIndex, declaration.Name, shape);
                    }
                    continue;
                }

                reached = shape.Roots;
                shapeResponseType = shape.ResponseType;
            }

            if (!reached.Contains("IRequest") && !reached.Any(markers.Contains))
                continue;

            var via = reached.FirstOrDefault(m => m != "IRequest")
                      ?? declaration.BaseRawNames.FirstOrDefault(b => b != "IRequest");

            if (result.Requests.Any(r =>
                    r.Name == declaration.Name &&
                    r.ProjectName == file.ProjectName &&
                    r.Namespace == declaration.Namespace &&
                    r.ContainingTypes.SequenceEqual(declaration.ContainingTypes)))
                continue;

            var request = new CqrsRequest
            {
                Name = declaration.Name,
                Namespace = declaration.Namespace,
                ProjectName = file.ProjectName,
                ViaMarker = via,
                Location = location,
            };
            request.ContainingTypes.AddRange(declaration.ContainingTypes);
            request.ResponseType = shapeResponseType
                ?? MarkerResolver.TryExtractMarkerResponse(declaration, reached)
                ?? declaration.BaseRawNames
                    .Select(b => CSharpScanner.ExtractGenericArguments(declaration.BaseList, b)?.FirstOrDefault())
                    .FirstOrDefault(a => a is not null);
            if (!request.Name.EndsWith("Command", StringComparison.Ordinal) &&
                !request.Name.EndsWith("Query", StringComparison.Ordinal))
                request.Kind = MarkerResolver.ClassifyMarkerRequest(
                    request.Name, declaration.BaseRawNames.Concat(reached));
            result.Requests.Add(request);
        }
    }
}
