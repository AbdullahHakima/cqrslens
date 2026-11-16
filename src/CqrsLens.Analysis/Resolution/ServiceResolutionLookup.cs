namespace CqrsLens;

/// <summary>
/// Resolves written member/parameter types against <see cref="AnalysisResult.ServiceResolutions"/>
/// using closed-generic exact lookup first (failing closed when a closed entry is ambiguous)
/// and falling back to open-generic registrations only when no closed entry exists.
/// </summary>
internal sealed class ServiceResolutionLookup
{
    private readonly TypeHierarchy _hierarchy;
    private readonly Dictionary<string, ServiceResolution> _closedBySignature;
    private readonly Dictionary<(string Identity, int Arity), ServiceResolution> _openByIdentityAndArity;
    private readonly Dictionary<string, SourceLocation?> _interfaceLocations;
    private readonly HashSet<string> _knownIdentities;
    private readonly HashSet<string> _contexts;

    public ServiceResolutionLookup(AnalysisResult result, TypeHierarchy hierarchy)
    {
        _hierarchy = hierarchy;
        _closedBySignature = new Dictionary<string, ServiceResolution>(
            result.ServiceResolutions.Count, StringComparer.Ordinal);
        _openByIdentityAndArity = new Dictionary<(string, int), ServiceResolution>();
        _interfaceLocations = new Dictionary<string, SourceLocation?>(StringComparer.Ordinal);
        _knownIdentities = new HashSet<string>(StringComparer.Ordinal);
        _contexts = new HashSet<string>(StringComparer.Ordinal);

        foreach (var ctx in result.DbContexts)
            _contexts.Add(ctx.FullName);

        foreach (var resolution in result.ServiceResolutions)
        {
            _knownIdentities.Add(resolution.Identity);
            if (resolution.InterfaceLocation is not null)
                _interfaceLocations.TryAdd(resolution.Identity, resolution.InterfaceLocation);

            if (resolution.IsOpenGeneric)
                _openByIdentityAndArity.TryAdd((resolution.Identity, resolution.Arity), resolution);
            else
                _closedBySignature.TryAdd(resolution.Interface, resolution);
        }
    }

    public ResolvedServiceTarget? Resolve(string writtenType, CSharpScanner.ClassDecl declaring)
    {
        var cleaned = CleanTypeReference(writtenType);
        if (cleaned.Length == 0)
            return null;

        var (name, arguments) = CSharpScanner.ParseTypeReference(cleaned);
        if (name.Length == 0)
            return null;

        var identity = _hierarchy.ResolveInterfaceReference(
            name, declaring.Namespace, declaring.Usings, declaring.ContainingTypes, arguments.Count, out _);
        if (identity is null)
        {
            var simple = CSharpScanner.SimpleName(name);
            identity = arguments.Count > 0 && !simple.EndsWith("`" + arguments.Count, StringComparison.Ordinal)
                ? CSharpScanner.TypeIdentity(simple, arguments.Count)
                : simple;
        }

        var signature = arguments.Count == 0
            ? identity
            : identity + "<" + string.Join(", ", arguments) + ">";

        // 1. Check closed-generic entry first; fail closed on closed ambiguity (never fall back to open generic).
        if (_closedBySignature.TryGetValue(signature, out var closed))
            return FromResolution(closed, signature, identity, arguments);

        // 2. Check open-generic fallback only when no closed entry exists.
        if (arguments.Count > 0 &&
            _openByIdentityAndArity.TryGetValue((identity, arguments.Count), out var open))
        {
            return FromResolution(open, signature, identity, arguments);
        }

        // 3. Direct concrete class injection declared in scan.
        var classDecl = _hierarchy.TryResolveClass(
            name, declaring.Namespace, declaring.Usings, declaring.ContainingTypes, arguments.Count, out _);
        if (classDecl is not null)
        {
            var isDb = _contexts.Contains(classDecl.FullName);
            return new ResolvedServiceTarget(
                IsResolved: !isDb,
                IsConcrete: true,
                IsAmbiguous: false,
                IsDbContext: isDb,
                IsUnimplementedInterface: false,
                InterfaceSignature: classDecl.FullName,
                InterfaceIdentity: classDecl.TypeParameters.Count > 0
                    ? CSharpScanner.TypeIdentity(classDecl.FullName, classDecl.TypeParameters.Count)
                    : classDecl.FullName,
                InterfaceTypeArgs: arguments,
                InterfaceLocation: null,
                ImplementationFullName: classDecl.FullName,
                ImplementationArity: classDecl.TypeParameters.Count);
        }

        // 4. Interface exists in scan with zero registrations and zero implementing candidates.
        if (_hierarchy.HasInterface(identity) && !_knownIdentities.Contains(identity))
        {
            _interfaceLocations.TryGetValue(identity, out var ifaceLoc);
            return new ResolvedServiceTarget(
                IsResolved: false,
                IsConcrete: false,
                IsAmbiguous: false,
                IsDbContext: false,
                IsUnimplementedInterface: true,
                InterfaceSignature: signature,
                InterfaceIdentity: identity,
                InterfaceTypeArgs: arguments,
                InterfaceLocation: ifaceLoc,
                ImplementationFullName: string.Empty,
                ImplementationArity: 0);
        }

        return null;
    }

    private ResolvedServiceTarget FromResolution(
        ServiceResolution resolution,
        string signature,
        string identity,
        List<string> arguments)
    {
        if (resolution.IsAmbiguous || string.IsNullOrWhiteSpace(resolution.Implementation))
        {
            return new ResolvedServiceTarget(
                IsResolved: false,
                IsConcrete: false,
                IsAmbiguous: true,
                IsDbContext: false,
                IsUnimplementedInterface: false,
                InterfaceSignature: signature,
                InterfaceIdentity: identity,
                InterfaceTypeArgs: arguments,
                InterfaceLocation: resolution.InterfaceLocation,
                ImplementationFullName: string.Empty,
                ImplementationArity: 0);
        }

        var isDb = _contexts.Contains(resolution.Implementation);
        return new ResolvedServiceTarget(
            IsResolved: !isDb,
            IsConcrete: false,
            IsAmbiguous: false,
            IsDbContext: isDb,
            IsUnimplementedInterface: false,
            InterfaceSignature: signature,
            InterfaceIdentity: identity,
            InterfaceTypeArgs: arguments,
            InterfaceLocation: resolution.InterfaceLocation,
            ImplementationFullName: resolution.Implementation,
            ImplementationArity: resolution.IsOpenGeneric ? arguments.Count : resolution.Arity);
    }

    internal static string CleanTypeReference(string written)
    {
        var trimmed = written.Trim();
        if (trimmed.EndsWith('?'))
            trimmed = trimmed[..^1].TrimEnd();
        return trimmed;
    }
}
