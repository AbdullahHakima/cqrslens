using System.Runtime.CompilerServices;

namespace CqrsLens;

/// <summary>
/// Implements F2 R-REPO (option a: repository contract implementation that resolves
/// cleanly in <see cref="AnalysisResult.ServiceResolutions"/>) and R-SVC (service
/// attribution from non-repository DI bindings, explicit registrations, and
/// <see cref="AnalysisResult.ServiceCallEdges"/> targets), while excluding abstract/static
/// classes, DbContexts, Settings/Config types, and external framework types.
/// </summary>
internal static class ServiceAndRepositoryRoleRules
{
    private const int MaxInheritanceDepth = 16;

    /// <summary>Discovers <see cref="ComponentRole.Repository"/> and <see cref="ComponentRole.Service"/> roles.</summary>
    public static void Apply(
        IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles,
        ScannedTypeCatalog catalog,
        TypeHierarchy hierarchy,
        AnalysisResult result)
    {
        ApplyPreCallDiscovery(parsedFiles, catalog, hierarchy, result);
        ApplyServiceCallTargets(
            result.ServiceCallEdges.Select(e => (e.Via, e.To, e.ProjectName, e.Location)),
            catalog,
            hierarchy,
            result);
    }

    public static void ApplyPreCallDiscovery(
        IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles,
        ScannedTypeCatalog catalog,
        TypeHierarchy hierarchy,
        AnalysisResult result)
    {
        if (result.ServiceResolutions.Count > 0)
        {
            ApplyRepositories(catalog, hierarchy, result);
            ApplyServiceResolutions(catalog, hierarchy, result);
        }

        var explicitRegs = ServiceRegistrationParser.ParseExplicitRegistrations(parsedFiles);
        if (explicitRegs.Count > 0)
            ApplyExplicitRegistrations(explicitRegs, catalog, hierarchy, result);
    }

    public static void ApplyServiceCallTargets(
        IEnumerable<(string Via, string To, string? ProjectName, SourceLocation? Location)> targets,
        ScannedTypeCatalog catalog,
        TypeHierarchy hierarchy,
        AnalysisResult result)
    {
        foreach (var (via, to, projectName, edgeLocation) in targets)
        {
            if (IsRepositoryContractVia(via))
                continue;

            var entry = catalog.ResolveByFullName(to, preferredProject: projectName);
            if (entry is null || entry.IsTestProject || IsExcludedFromService(entry, hierarchy, result))
                continue;

            var location = edgeLocation ?? entry.PrimaryLocation;
            var scopeProject = projectName ?? entry.ProjectName;
            var strength = string.Equals(via, to, StringComparison.Ordinal)
                ? EvidenceStrength.Proven
                : EvidenceStrength.Resolved;

            result.Spine.AddOrMergeComponent(
                entry.Id,
                entry.PrimaryLocation,
                ComponentRole.Service,
                new EvidenceOccurrence(
                    EvidenceRule.ServiceDiscovery,
                    EvidenceOrigin.CallSite,
                    location,
                    strength,
                    scopeProjectName: scopeProject,
                    isTestProject: catalog.IsTestProject(scopeProject)));
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ApplyRepositories(
        ScannedTypeCatalog catalog,
        TypeHierarchy hierarchy,
        AnalysisResult result)
    {
        foreach (var resolution in result.ServiceResolutions)
        {
            if (resolution.IsAmbiguous || string.IsNullOrWhiteSpace(resolution.Implementation))
                continue;

            var contractSimple = CSharpSyntax.SimpleNameFromIdentity(resolution.Identity);
            if (!ServiceRegistrationParser.IsRepositoryContractSimpleName(contractSimple))
                continue;

            var candidate = FindCandidate(resolution, resolution.Implementation);
            if (candidate is null || !candidate.DeclaredInScan)
                continue;

            var entry = catalog.ResolveByFullName(
                resolution.Implementation,
                preferredLocation: candidate.Location,
                preferredArity: resolution.Arity);
            if (entry is null || entry.IsTestProject || !entry.IsConcrete || IsSettingsOrConfig(entry, hierarchy))
                continue;

            if (!ImplementsRepositoryContract(entry, hierarchy))
                continue;

            var isFromRegistration = candidate.FromRegistration ||
                                     resolution.Source is ServiceResolutionSource.Registration or ServiceResolutionSource.Both;
            var origin = isFromRegistration ? EvidenceOrigin.Registration : EvidenceOrigin.Declaration;
            var strength = resolution.Source == ServiceResolutionSource.Both
                ? EvidenceStrength.Proven
                : EvidenceStrength.Resolved;
            var evidenceLocation = candidate.Location ?? entry.PrimaryLocation;

            result.Spine.AddOrMergeComponent(
                entry.Id,
                entry.PrimaryLocation,
                ComponentRole.Repository,
                new EvidenceOccurrence(
                    EvidenceRule.RepositoryDiscovery,
                    origin,
                    evidenceLocation,
                    strength,
                    scopeProjectName: entry.ProjectName,
                    isTestProject: entry.IsTestProject));
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ApplyServiceResolutions(
        ScannedTypeCatalog catalog,
        TypeHierarchy hierarchy,
        AnalysisResult result)
    {
        // Source 1: ServiceResolutions with FromRegistration on a non-repository contract.
        foreach (var resolution in result.ServiceResolutions)
        {
            if (resolution.IsAmbiguous || string.IsNullOrWhiteSpace(resolution.Implementation))
                continue;

            var contractSimple = CSharpSyntax.SimpleNameFromIdentity(resolution.Identity);
            if (ServiceRegistrationParser.IsRepositoryContractSimpleName(contractSimple))
                continue;

            var candidate = FindCandidate(resolution, resolution.Implementation);
            if (candidate is null || !candidate.FromRegistration || !candidate.DeclaredInScan)
                continue;

            var entry = catalog.ResolveByFullName(
                resolution.Implementation,
                preferredLocation: candidate.Location,
                preferredArity: resolution.Arity);
            if (entry is null || entry.IsTestProject || IsExcludedFromService(entry, hierarchy, result))
                continue;

            var location = candidate.RegistrationLocation ?? candidate.Location ?? entry.PrimaryLocation;
            var scopeProject = catalog.GetProjectForLocation(location) ?? entry.ProjectName;
            var isTest = catalog.IsTestProjectForLocation(location, entry.IsTestProject);

            result.Spine.AddOrMergeComponent(
                entry.Id,
                entry.PrimaryLocation,
                ComponentRole.Service,
                new EvidenceOccurrence(
                    EvidenceRule.ServiceDiscovery,
                    EvidenceOrigin.Registration,
                    location,
                    resolution.Source == ServiceResolutionSource.Both
                        ? EvidenceStrength.Proven
                        : EvidenceStrength.Resolved,
                    scopeProjectName: scopeProject,
                    isTestProject: isTest));
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ApplyExplicitRegistrations(
        List<ExplicitServiceRegistration> explicitRegs,
        ScannedTypeCatalog catalog,
        TypeHierarchy hierarchy,
        AnalysisResult result)
    {
        // Source 2: Explicit concrete registrations (AddScoped<T>, AddHttpClient<...>, factories, RegisterType<T>).
        foreach (var reg in explicitRegs)
        {
            if (reg.IsRepositoryContract || catalog.IsTestProject(reg.File.ProjectName))
                continue;

            var entry = catalog.ResolveExplicitRegistration(reg, hierarchy);
            if (entry is null || entry.IsTestProject || IsExcludedFromService(entry, hierarchy, result))
                continue;

            var location = catalog.CreateLocation(reg.File, reg.SourceIndex)
                           ?? entry.PrimaryLocation;

            result.Spine.AddOrMergeComponent(
                entry.Id,
                entry.PrimaryLocation,
                ComponentRole.Service,
                new EvidenceOccurrence(
                    EvidenceRule.ServiceDiscovery,
                    EvidenceOrigin.Registration,
                    location,
                    EvidenceStrength.Proven,
                    scopeProjectName: reg.File.ProjectName,
                    isTestProject: catalog.IsTestProject(reg.File.ProjectName)));
        }
    }

    private static ServiceCandidate? FindCandidate(ServiceResolution resolution, string implementation)
    {
        foreach (var candidate in resolution.Candidates)
        {
            if (string.Equals(candidate.Type, implementation, StringComparison.Ordinal))
                return candidate;
        }
        return null;
    }

    private static bool IsExcludedFromService(
        ScannedComponentEntry entry,
        TypeHierarchy hierarchy,
        AnalysisResult result)
    {
        if (!entry.IsConcrete)
            return true;

        if (result.Spine.TryGetComponent(entry.Id, out var existing) &&
            existing!.HasRole(ComponentRole.DbContext))
            return true;

        return IsSettingsOrConfig(entry, hierarchy);
    }

    private static bool IsSettingsOrConfig(ScannedComponentEntry entry, TypeHierarchy hierarchy)
    {
        if (entry.SimpleName.EndsWith("Settings", StringComparison.Ordinal) ||
            entry.SimpleName.EndsWith("Config", StringComparison.Ordinal))
            return true;

        var interfaces = new List<ImplementedInterface>();
        foreach (var part in entry.Declarations)
        {
            interfaces.Clear();
            hierarchy.CollectImplementedInterfaces(part.Declaration, interfaces);
            foreach (var iface in interfaces)
            {
                if (IsSettingsContractSimpleName(CSharpSyntax.SimpleNameFromIdentity(iface.Identity)))
                    return true;
            }

            if (InheritsExternalBaseMatching(part.Declaration, hierarchy, IsSettingsContractSimpleName))
                return true;
        }

        return false;
    }

    private static bool ImplementsRepositoryContract(ScannedComponentEntry entry, TypeHierarchy hierarchy)
    {
        var interfaces = new List<ImplementedInterface>();
        foreach (var part in entry.Declarations)
        {
            interfaces.Clear();
            hierarchy.CollectImplementedInterfaces(part.Declaration, interfaces);
            foreach (var iface in interfaces)
            {
                if (ServiceRegistrationParser.IsRepositoryContractSimpleName(
                        CSharpSyntax.SimpleNameFromIdentity(iface.Identity)))
                    return true;
            }

            if (InheritsExternalBaseMatching(
                    part.Declaration,
                    hierarchy,
                    ServiceRegistrationParser.IsRepositoryContractSimpleName))
                return true;
        }

        return false;
    }

    private static bool InheritsExternalBaseMatching(
        CSharpScanner.ClassDecl declaration,
        TypeHierarchy hierarchy,
        Func<string, bool> predicate)
    {
        var queue = new Queue<(CSharpScanner.ClassDecl Decl, int Depth)>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        queue.Enqueue((declaration, 0));

        while (queue.Count > 0)
        {
            var (current, depth) = queue.Dequeue();
            if (depth >= MaxInheritanceDepth || string.IsNullOrWhiteSpace(current.BaseList))
                continue;

            var visitKey = CSharpSyntax.TypeIdentity(current.FullName, current.TypeParameters.Count);
            if (!visited.Add(visitKey))
                continue;

            foreach (var basePart in CSharpScanner.SplitTopLevel(current.BaseList))
            {
                var (baseName, args) = CSharpSyntax.ParseTypeReference(basePart);
                if (baseName.Length == 0) continue;

                var simple = CSharpSyntax.SimpleName(baseName);
                if (predicate(simple))
                    return true;

                var resolved = hierarchy.TryResolveClass(
                    baseName, current.Namespace, current.Usings, current.ContainingTypes, args.Count, out _);
                if (resolved is not null)
                    queue.Enqueue((resolved, depth + 1));
            }
        }

        return false;
    }

    private static bool IsSettingsContractSimpleName(string simple)
        => simple is "ISettings" or "IConfig" or "IOptions" or "IOptionsSnapshot" or "IOptionsMonitor";

    private static bool IsRepositoryContractVia(string via)
    {
        var (name, _) = CSharpSyntax.ParseTypeReference(via);
        if (name.Length == 0) return false;
        return ServiceRegistrationParser.IsRepositoryContractSimpleName(
            CSharpSyntax.SimpleNameFromIdentity(name));
    }
}
