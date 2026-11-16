using System.Runtime.CompilerServices;

namespace CqrsLens;

/// <summary>
/// Builds the service-resolution index: interface → implementation, from concrete
/// class declarations (the declaration index, via <see cref="TypeHierarchy"/>) and
/// explicit MS-DI/Autofac registrations (confirmation and disambiguation). The pass
/// runs once over the shared workspace, so text and Roslyn modes produce the same
/// index by construction.
///
/// Fail closed: two or more candidates with no registration naming one of them is
/// ambiguous and chooses no implementation; an interface with no implementation gets
/// no entry at all.
/// </summary>
internal static class ServiceResolutionIndexBuilder
{
    public static void Build(
        IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles,
        AnalysisResult result,
        TypeHierarchy? hierarchy = null,
        IReadOnlyDictionary<string, SourceLineIndex>? lineIndices = null)
    {
        if (parsedFiles.Count == 0) return;

        HashSet<string>? testProjects = null;
        var projects = result.Projects;
        for (var i = 0; i < projects.Count; i++)
        {
            var project = projects[i];
            if (string.Equals(project.Layer, "Tests", StringComparison.OrdinalIgnoreCase))
            {
                testProjects ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                testProjects.Add(project.Name);
            }
        }

        // Exclude test projects from the service-resolution index so test doubles and test-host
        // registrations never pollute or create false ambiguity on production contracts.
        IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> activeFiles;
        if (testProjects is null)
        {
            activeFiles = parsedFiles;
        }
        else
        {
            var filtered = new List<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)>(parsedFiles.Count);
            for (var i = 0; i < parsedFiles.Count; i++)
            {
                var item = parsedFiles[i];
                if (item.File.ProjectName is null || !testProjects.Contains(item.File.ProjectName))
                    filtered.Add(item);
            }
            activeFiles = filtered;
        }
        if (activeFiles.Count == 0) return;

        var classCount = 0;
        var interfaceCount = 0;
        for (var i = 0; i < activeFiles.Count; i++)
        {
            var scan = activeFiles[i].Scan;
            classCount += scan.Classes.Count;
            interfaceCount += scan.InterfaceDecls.Count;
        }
        if (classCount == 0 && interfaceCount == 0) return;

        var registrations = ServiceRegistrationParser.Parse(activeFiles);
        if (interfaceCount == 0 && registrations.Count == 0)
            return;

        BuildCore(
            parsedFiles,
            activeFiles,
            classCount,
            interfaceCount,
            testProjects is not null,
            registrations,
            result,
            hierarchy,
            lineIndices);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void BuildCore(
        IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles,
        IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> activeFiles,
        int classCount,
        int interfaceCount,
        bool hasTestProjects,
        List<ServiceRegistration> registrations,
        AnalysisResult result,
        TypeHierarchy? hierarchy,
        IReadOnlyDictionary<string, SourceLineIndex>? lineIndices)
    {
        lineIndices ??= SourceLineIndex.BuildLookup(parsedFiles, result.RootPath);

        var classes = new List<CSharpScanner.ClassDecl>(classCount);
        var interfaces = new List<CSharpScanner.InterfaceDecl>(interfaceCount);
        for (var i = 0; i < activeFiles.Count; i++)
        {
            var scan = activeFiles[i].Scan;
            classes.AddRange(scan.Classes);
            interfaces.AddRange(scan.InterfaceDecls);
        }

        // The hierarchy is a pure function of the scanned declarations and is shared with the
        // two later resolution consumers; rebuilding it here would duplicate its index and
        // discard the resolver cache it has already warmed.
        hierarchy = hierarchy is null || hasTestProjects
            ? new TypeHierarchy(classes, interfaces)
            : hierarchy;
        var classFiles = new Dictionary<CSharpScanner.ClassDecl, SourceFile>(classCount);
        var interfaceLocations = new Dictionary<string, SourceLocation>(interfaceCount, StringComparer.Ordinal);
        foreach (var (file, scan, _) in activeFiles)
        {
            foreach (var declaration in scan.Classes)
                classFiles[declaration] = file;
            foreach (var declaration in scan.InterfaceDecls)
            {
                var identity = CSharpScanner.TypeIdentity(
                    declaration.FullName, declaration.TypeParameters.Count);
                var location = lineIndices.TryGetValue(file.Path, out var lineIndex)
                    ? lineIndex.Create(declaration.BodySpan.Start)
                    : SourceLocation.Create(file.Path, result.RootPath, file.Code, declaration.BodySpan.Start);
                if (location is not null)
                    interfaceLocations.TryAdd(identity, location);
            }
        }

        var entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        if (interfaceCount > 0)
            AddDeclarationCandidates(classes, hierarchy, classFiles, interfaceLocations, lineIndices, result, entries);
        if (registrations.Count > 0)
            AddRegistrationCandidates(activeFiles, registrations, hierarchy, interfaceLocations, lineIndices, result, entries);

        foreach (var entry in entries.Values)
            Finalize(entry);

        result.ServiceResolutions.AddRange(entries.Values
            .OrderBy(entry => entry.Interface, StringComparer.Ordinal)
            .Select(entry => entry.ToResolution()));
    }

    private static void AddDeclarationCandidates(
        List<CSharpScanner.ClassDecl> classes,
        TypeHierarchy hierarchy,
        IReadOnlyDictionary<CSharpScanner.ClassDecl, SourceFile> classFiles,
        IReadOnlyDictionary<string, SourceLocation> interfaceLocations,
        IReadOnlyDictionary<string, SourceLineIndex> lineIndices,
        AnalysisResult result,
        Dictionary<string, Entry> entries)
    {
        var implementations = new List<ImplementedInterface>();
        foreach (var declaration in classes)
        {
            // Only a concrete class can be instantiated as an implementation.
            if (declaration.IsAbstract) continue;

            implementations.Clear();
            hierarchy.CollectImplementedInterfaces(declaration, implementations);
            if (implementations.Count == 0) continue;

            var file = classFiles[declaration];
            var location = lineIndices.TryGetValue(file.Path, out var lineIndex)
                ? lineIndex.Create(declaration.NameIndex)
                : SourceLocation.Create(file.Path, result.RootPath, file.Code, declaration.NameIndex);

            foreach (var implementation in implementations)
            {
                var signature = Signature(
                    implementation.Identity, implementation.Arity,
                    implementation.Arguments, implementation.IsOpenGeneric);
                var entry = GetOrAdd(
                    entries, signature, implementation.Identity, implementation.Arity,
                    implementation.IsOpenGeneric, implementation.Arguments, interfaceLocations);
                entry.AddCandidate(declaration.FullName, declaredInScan: true, fromRegistration: false, location);
            }
        }
    }

    private static void AddRegistrationCandidates(
        IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles,
        List<ServiceRegistration> registrations,
        TypeHierarchy hierarchy,
        IReadOnlyDictionary<string, SourceLocation> interfaceLocations,
        IReadOnlyDictionary<string, SourceLineIndex> lineIndices,
        AnalysisResult result,
        Dictionary<string, Entry> entries)
    {
        if (registrations.Count == 0) return;

        var masks = parsedFiles.ToDictionary(
            f => f.File.Path, f => f.Mask, StringComparer.OrdinalIgnoreCase);
        var scans = parsedFiles.ToDictionary(
            f => f.File.Path, f => f.Scan, StringComparer.OrdinalIgnoreCase);
        var namespaceMaps = new Dictionary<string, NamespaceMap>(StringComparer.OrdinalIgnoreCase);
        var interfaceBuffer = new List<ImplementedInterface>();

        foreach (var registration in registrations)
        {
            var file = registration.File;
            if (!masks.TryGetValue(file.Path, out var mask) ||
                !scans.TryGetValue(file.Path, out var scan))
                continue;

            if (!namespaceMaps.TryGetValue(file.Path, out var namespaceMap))
            {
                namespaceMap = NamespaceMap.Build(file.Code, mask);
                namespaceMaps[file.Path] = namespaceMap;
            }
            var currentNamespace = namespaceMap.NamespaceAt(registration.SourceIndex);

            var serviceIdentity = ResolveIdentity(
                hierarchy, registration.ServiceName, currentNamespace, scan.Usings, registration.ServiceArity);
            var signature = Signature(
                serviceIdentity, registration.ServiceArity,
                registration.ServiceArguments, registration.ServiceIsOpen);

            var implementation = hierarchy.TryResolveClass(
                registration.ImplementationName, currentNamespace, scan.Usings,
                Array.Empty<string>(), registration.ImplementationArity, out _);
            if (registration.IsFactoryNew &&
                (implementation is null ||
                 !TypeImplementsContract(hierarchy, implementation, serviceIdentity, interfaceBuffer)))
                continue;

            var entry = GetOrAdd(
                entries, signature, serviceIdentity, registration.ServiceArity,
                registration.ServiceIsOpen, registration.ServiceArguments, interfaceLocations);

            var implementationName = implementation?.FullName ?? registration.ImplementationName;
            var location = lineIndices.TryGetValue(file.Path, out var lineIndex)
                ? lineIndex.Create(registration.SourceIndex)
                : SourceLocation.Create(file.Path, result.RootPath, file.Code, registration.SourceIndex);
            entry.AddCandidate(
                implementationName,
                declaredInScan: implementation is not null,
                fromRegistration: true,
                location);
        }
    }

    private static string ResolveIdentity(
        TypeHierarchy hierarchy,
        string name,
        string currentNamespace,
        IReadOnlyList<UsingDirective> usings,
        int arity)
    {
        var resolved = hierarchy.ResolveInterfaceReference(
            name, currentNamespace, usings, Array.Empty<string>(), arity, out _);
        var identity = resolved ?? CSharpScanner.SimpleName(name);
        if (arity > 0 && !identity.EndsWith("`" + arity, StringComparison.Ordinal))
            identity = CSharpScanner.TypeIdentity(identity, arity);
        return identity;
    }

    private static bool TypeImplementsContract(
        TypeHierarchy hierarchy,
        CSharpScanner.ClassDecl implementation,
        string serviceIdentity,
        List<ImplementedInterface> buffer)
    {
        if (string.Equals(implementation.FullName, serviceIdentity, StringComparison.Ordinal))
            return true;

        var implIdentity = implementation.TypeParameters.Count > 0
            ? CSharpScanner.TypeIdentity(implementation.FullName, implementation.TypeParameters.Count)
            : implementation.FullName;
        if (string.Equals(implIdentity, serviceIdentity, StringComparison.Ordinal))
            return true;

        buffer.Clear();
        hierarchy.CollectImplementedInterfaces(implementation, buffer);
        foreach (var implemented in buffer)
        {
            if (string.Equals(implemented.Identity, serviceIdentity, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static void Finalize(Entry entry)
    {
        var declared = entry.Candidates
            .Where(pair => pair.Value.DeclaredInScan)
            .Select(pair => pair.Key)
            .ToList();
        var registered = entry.Candidates
            .Where(pair => pair.Value.FromRegistration)
            .Select(pair => pair.Key)
            .ToList();

        if (registered.Count == 1)
        {
            entry.Implementation = registered[0];
            // "Both" is a registration that merely confirms an already-unambiguous
            // declaration; a registration that had to pick among several candidates (or
            // named a type that is not declared in the scan) is the source of truth.
            entry.Source = declared.Count == 1 && declared[0] == registered[0]
                ? ServiceResolutionSource.Both
                : ServiceResolutionSource.Registration;
        }
        else if (registered.Count > 1)
        {
            // Conflicting registrations: no safe choice.
            entry.IsAmbiguous = true;
        }
        else if (declared.Count == 1)
        {
            entry.Implementation = declared[0];
            entry.Source = ServiceResolutionSource.Declaration;
        }
        else if (declared.Count > 1)
        {
            entry.IsAmbiguous = true;
        }
    }

    private static Entry GetOrAdd(
        Dictionary<string, Entry> entries,
        string signature,
        string identity,
        int arity,
        bool isOpen,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, SourceLocation> interfaceLocations)
    {
        if (entries.TryGetValue(signature, out var existing))
            return existing;

        var entry = new Entry
        {
            Interface = signature,
            Identity = identity,
            InterfaceName = NameFromIdentity(identity),
            Arity = arity,
            IsOpenGeneric = isOpen,
            InterfaceLocation = interfaceLocations.TryGetValue(identity, out var location)
                ? location
                : null,
        };
        if (!isOpen)
            entry.Arguments.AddRange(arguments.Where(argument => argument.Length > 0));
        entries[signature] = entry;
        return entry;
    }

    private static string Signature(
        string identity,
        int arity,
        IReadOnlyList<string> arguments,
        bool isOpen)
    {
        if (arity == 0 || isOpen || arguments.Count == 0)
            return identity;
        return identity + "<" + string.Join(", ", arguments) + ">";
    }

    private static string NameFromIdentity(string identity)
    {
        var tick = identity.LastIndexOf('`');
        return tick > 0 ? identity[..tick] : identity;
    }

    private sealed class Entry
    {
        public required string Interface { get; init; }
        public required string Identity { get; init; }
        public required string InterfaceName { get; init; }
        public int Arity { get; init; }
        public bool IsOpenGeneric { get; init; }
        public List<string> Arguments { get; } = new();
        public SourceLocation? InterfaceLocation { get; init; }
        public Dictionary<string, CandidateState> Candidates { get; } = new(StringComparer.Ordinal);
        public string? Implementation { get; set; }
        public ServiceResolutionSource Source { get; set; }
        public bool IsAmbiguous { get; set; }

        public void AddCandidate(
            string type,
            bool declaredInScan,
            bool fromRegistration,
            SourceLocation? location)
        {
            if (!Candidates.TryGetValue(type, out var candidate))
                Candidates[type] = candidate = new CandidateState();
            candidate.DeclaredInScan |= declaredInScan;
            candidate.FromRegistration |= fromRegistration;
            candidate.Location ??= location;
            if (fromRegistration && location is not null)
                candidate.RegistrationLocation ??= location;
        }

        public ServiceResolution ToResolution()
        {
            var resolution = new ServiceResolution
            {
                Interface = Interface,
                InterfaceName = InterfaceName,
                Identity = Identity,
                Arity = Arity,
                IsOpenGeneric = IsOpenGeneric,
                Implementation = Implementation,
                Source = Source,
                IsAmbiguous = IsAmbiguous,
                InterfaceLocation = InterfaceLocation,
            };
            resolution.Arguments.AddRange(Arguments);
            foreach (var pair in Candidates.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                resolution.Candidates.Add(new ServiceCandidate
                {
                    Type = pair.Key,
                    DeclaredInScan = pair.Value.DeclaredInScan,
                    FromRegistration = pair.Value.FromRegistration,
                    Location = pair.Value.Location,
                    RegistrationLocation = pair.Value.RegistrationLocation,
                });
            }
            return resolution;
        }

        public sealed class CandidateState
        {
            public bool DeclaredInScan { get; set; }
            public bool FromRegistration { get; set; }
            public SourceLocation? Location { get; set; }
            public SourceLocation? RegistrationLocation { get; set; }
        }
    }
}
