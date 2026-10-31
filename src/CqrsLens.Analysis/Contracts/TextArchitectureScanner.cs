namespace CqrsLens;

/// <summary>
/// Text-mode scanner. Orchestrates one pass over the shared workspace: global
/// usings, hierarchy and interface map, direct shape application, indirect marker
/// discovery and the EF model. Each step is owned by a dedicated collaborator.
/// </summary>
internal sealed class TextArchitectureScanner : IArchitectureScanner
{
    public void Scan(
        SourceWorkspace workspace,
        IReadOnlySet<string> markers,
        IReadOnlySet<string> handlerMarkers,
        AnalysisResult result)
    {
        var ordered = workspace.SuccessfullyParsed
            .OrderBy(f => f.File.Path, StringComparer.OrdinalIgnoreCase)
            .Select(f => (File: f.File, Scan: f.Scan!))
            .ToList();
        var phases = new AnalysisPhaseRecorder(result);

        GlobalUsingApplier.Apply(ordered, result.Projects);
        phases.Record("Scan: global usings");

        var hierarchy = new TypeHierarchy(
            ordered.SelectMany(o => o.Scan.Classes),
            ordered.SelectMany(o => o.Scan.InterfaceDecls));
        phases.Record("Scan: hierarchy");

        var interfaceMap = InterfaceMapBuilder.Build(ordered, hierarchy, result);
        phases.Record("Scan: interface map");

        var handlerRoots = ContractRoots.BuildHandlerRoots(handlerMarkers);
        var identities = new ScanIdentityIndex(result);
        var masks = workspace.SuccessfullyParsed
            .ToDictionary(f => f.File.Path, f => f.Mask!, StringComparer.OrdinalIgnoreCase);
        phases.Record("Scan: indexes");

        foreach (var (file, scan) in ordered)
        {
            CSharpScanner.ApplyScan(
                scan,
                file.Code,
                file.ProjectName,
                result,
                file.Path,
                masks[file.Path],
                identities);
        }
        phases.Record("Scan: ApplyScan");

        foreach (var (file, scan) in ordered)
            MarkerRequestDiscoverer.Discover(scan, file, hierarchy, interfaceMap, markers, result);
        phases.Record("Scan: marker requests");

        foreach (var (file, scan) in ordered)
            MarkerNotificationDiscoverer.Discover(scan, file, hierarchy, interfaceMap, result);
        phases.Record("Scan: marker notifications");

        foreach (var (file, scan) in ordered)
            IndirectShapeDiscoverer.Discover(scan, file, hierarchy, handlerRoots, result);
        phases.Record("Scan: indirect shapes");

        EfCoreTextModelBuilder.Build(ordered, hierarchy, result, masks);
        phases.Record("Scan: EF model");
    }
}
