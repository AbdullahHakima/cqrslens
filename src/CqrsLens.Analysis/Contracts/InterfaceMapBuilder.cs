namespace CqrsLens;

/// <summary>
/// Builds the interface closure using each declaration's own using directives.
/// This makes module-local ICommand/IQuery contracts resolve to the contract
/// imported by the file rather than to a same-named interface from another module.
/// </summary>
internal static class InterfaceMapBuilder
{
    public static Dictionary<string, List<string>> Build(
        IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan)> ordered,
        TypeHierarchy hierarchy,
        AnalysisResult result)
    {
        var interfaceMap = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (file, scan) in ordered)
        {
            foreach (var declaration in scan.InterfaceDecls)
            {
                var fullName = CSharpScanner.TypeIdentity(
                    declaration.FullName, declaration.TypeParameters.Count);
                if (!interfaceMap.TryGetValue(fullName, out var list))
                    interfaceMap[fullName] = list = new List<string>();

                foreach (var part in CSharpScanner.SplitTopLevel(declaration.BaseList))
                {
                    var (baseName, baseArgs) = CSharpScanner.ParseTypeReference(part);
                    var resolved = hierarchy.ResolveInterfaceReference(
                        baseName,
                        declaration.Namespace,
                        declaration.Usings,
                        declaration.ContainingTypes,
                        baseArgs.Count,
                        out var ambiguous);
                    if (resolved is null)
                    {
                        ContractDiagnostics.RecordAmbiguous(
                            result, file, declaration.BodySpan.Start, declaration.Name,
                            baseName, ambiguous, "interface");
                        continue;
                    }

                    if (!list.Contains(resolved))
                        list.Add(resolved);
                }
            }
        }

        return interfaceMap;
    }
}
