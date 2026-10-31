namespace CqrsLens;

/// <summary>
/// Finds notifications and domain events declared through a source interface chain
/// or a custom base class that reaches INotification or IDomainEvent.
/// </summary>
internal static class MarkerNotificationDiscoverer
{
    public static void Discover(
        CSharpScanner.FileScan scan,
        SourceFile file,
        TypeHierarchy hierarchy,
        IReadOnlyDictionary<string, List<string>> interfaceMap,
        AnalysisResult result)
    {
        var markerRoots = ContractRoots.NotificationMarkerRoots;
        foreach (var declaration in scan.Classes)
        {
            if (declaration.IsAbstract) continue;
            if (declaration.BaseRawNames.Contains("INotification") ||
                declaration.BaseRawNames.Contains("IDomainEvent") ||
                declaration.BaseRawNames.Contains("INotificationHandler") ||
                declaration.BaseRawNames.Contains("IDomainEventHandler"))
                continue;
            if (declaration.BaseRawNames.Count == 0) continue;

            if (declaration.BaseRawNames.Any(b => b is "DbContext" or "IdentityDbContext" or
                                                      "AbstractValidator" or "IValidator" or
                                                      "Validator" or "IPipelineBehavior" or
                                                      "IRequest" or "IRequestHandler"))
                continue;

            var directInterfaces = new List<string>();
            foreach (var part in CSharpScanner.SplitTopLevel(declaration.BaseList))
            {
                var (name, args) = CSharpScanner.ParseTypeReference(part);
                var resolved = hierarchy.ResolveInterfaceReference(
                    name,
                    declaration.Namespace,
                    declaration.Usings,
                    declaration.ContainingTypes,
                    args.Count,
                    out _);
                if (resolved is not null)
                    directInterfaces.Add(resolved);
            }

            var reached = MarkerResolver.ReachableMarkers(directInterfaces, interfaceMap, markerRoots)
                .Where(markerRoots.Contains)
                .ToList();
            if (reached.Count == 0)
            {
                var shape = hierarchy.ResolveRequestShape(declaration, markerRoots);
                reached = shape.Roots.Where(markerRoots.Contains).ToList();
            }

            if (reached.Count == 0)
                continue;

            if (result.Notifications.Any(n =>
                    n.Name == declaration.Name &&
                    n.ProjectName == file.ProjectName &&
                    n.Namespace == declaration.Namespace &&
                    n.ContainingTypes.SequenceEqual(declaration.ContainingTypes)))
                continue;

            var via = declaration.BaseRawNames.FirstOrDefault(b => b is not "INotification")
                      ?? reached.FirstOrDefault(m => m != "INotification");
            var notification = new CqrsNotification
            {
                Name = declaration.Name,
                Namespace = declaration.Namespace,
                ProjectName = file.ProjectName,
                ViaMarker = via,
                Location = SourceLocation.Create(
                    file.Path, result.RootPath, file.Code, declaration.NameIndex),
            };
            notification.ContainingTypes.AddRange(declaration.ContainingTypes);
            result.Notifications.Add(notification);
        }
    }
}
