namespace CqrsLens;

/// <summary>
/// F2 component role discovery and endpoint exposure pass (<see cref="ComponentRole.Controller"/>,
/// <see cref="ComponentRole.Service"/>, <see cref="ComponentRole.Repository"/>, and
/// <see cref="RelationKind.Exposes"/>). Runs once over the shared workspace so text and
/// Roslyn modes produce identical component roles and exposure edges.
/// </summary>
internal static class ComponentRoleDiscovery
{
    public static void Analyze(
        IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles,
        TypeHierarchy serviceHierarchy,
        AnalysisResult result)
    {
        if (parsedFiles.Count == 0)
            return;

        var catalog = new ScannedTypeCatalog(parsedFiles, result);
        var controllerCount = ControllerRoleRules.ApplyControllers(catalog, serviceHierarchy, result);
        catalog.DiscoveredControllerCount = controllerCount;
        ServiceAndRepositoryRoleRules.Apply(parsedFiles, catalog, serviceHierarchy, result);
        if (controllerCount > 0 && result.Endpoints.Count > 0)
            ControllerRoleRules.ApplyExposes(catalog, result);
    }

    public static ScannedTypeCatalog AnalyzePreCall(
        IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles,
        TypeHierarchy serviceHierarchy,
        IReadOnlyDictionary<string, SourceLineIndex> lineIndices,
        AnalysisResult result)
    {
        var catalog = new ScannedTypeCatalog(parsedFiles, result, lineIndices);
        if (parsedFiles.Count == 0)
            return catalog;

        var controllerCount = ControllerRoleRules.ApplyControllers(catalog, serviceHierarchy, result);
        catalog.DiscoveredControllerCount = controllerCount;
        ServiceAndRepositoryRoleRules.ApplyPreCallDiscovery(parsedFiles, catalog, serviceHierarchy, result);
        if (controllerCount > 0 && result.Endpoints.Count > 0)
            ControllerRoleRules.ApplyExposes(catalog, result);
        return catalog;
    }
}
