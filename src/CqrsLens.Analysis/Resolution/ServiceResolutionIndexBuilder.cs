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
        AnalysisResult result)
    {
        if (parsedFiles.Count == 0) return;

        var classes = parsedFiles.SelectMany(f => f.Scan.Classes).ToList();
        var interfaces = parsedFiles.SelectMany(f => f.Scan.InterfaceDecls).ToList();
        if (classes.Count == 0 && interfaces.Count == 0) return;

        var hierarchy = new TypeHierarchy(classes, interfaces);
        var classFiles = new Dictionary<CSharpScanner.ClassDecl, SourceFile>();
        var interfaceLocations = new Dictionary<string, SourceLocation>(StringComparer.Ordinal);
        foreach (var (file, scan, _) in parsedFiles)
        {
            foreach (var declaration in scan.Classes)
                classFiles[declaration] = file;
            foreach (var declaration in scan.InterfaceDecls)
            {
                var identity = CSharpScanner.TypeIdentity(
                    declaration.FullName, declaration.TypeParameters.Count);
                interfaceLocations.TryAdd(identity, SourceLocation.Create(
                    file.Path, result.RootPath, file.Code, declaration.BodySpan.Start));
            }
        }

        var entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        AddDeclarationCandidates(classes, hierarchy, classFiles, interfaceLocations, result, entries);
        AddRegistrationCandidates(parsedFiles, hierarchy, interfaceLocations, result, entries);

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
            var location = SourceLocation.Create(
                file.Path, result.RootPath, file.Code, declaration.NameIndex);

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
        TypeHierarchy hierarchy,
        IReadOnlyDictionary<string, SourceLocation> interfaceLocations,
        AnalysisResult result,
        Dictionary<string, Entry> entries)
    {
        var registrations = ServiceRegistrationParser.Parse(parsedFiles);
        if (registrations.Count == 0) return;

        var masks = parsedFiles.ToDictionary(
            f => f.File.Path, f => f.Mask, StringComparer.OrdinalIgnoreCase);
        var scans = parsedFiles.ToDictionary(
            f => f.File.Path, f => f.Scan, StringComparer.OrdinalIgnoreCase);

        foreach (var registration in registrations)
        {
            var file = registration.File;
            if (!masks.TryGetValue(file.Path, out var mask) ||
                !scans.TryGetValue(file.Path, out var scan))
                continue;

            var currentNamespace = NamespaceMap.Build(file.Code, mask).NamespaceAt(registration.SourceIndex);

            var serviceIdentity = ResolveIdentity(
                hierarchy, registration.ServiceName, currentNamespace, scan.Usings, registration.ServiceArity);
            var signature = Signature(
                serviceIdentity, registration.ServiceArity,
                registration.ServiceArguments, registration.ServiceIsOpen);
            var entry = GetOrAdd(
                entries, signature, serviceIdentity, registration.ServiceArity,
                registration.ServiceIsOpen, registration.ServiceArguments, interfaceLocations);

            var implementation = hierarchy.TryResolveClass(
                registration.ImplementationName, currentNamespace, scan.Usings,
                Array.Empty<string>(), registration.ImplementationArity, out _);
            var implementationName = implementation?.FullName ?? registration.ImplementationName;
            var location = SourceLocation.Create(
                file.Path, result.RootPath, file.Code, registration.SourceIndex);
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
                });
            }
            return resolution;
        }

        public sealed class CandidateState
        {
            public bool DeclaredInScan { get; set; }
            public bool FromRegistration { get; set; }
            public SourceLocation? Location { get; set; }
        }
    }
}
