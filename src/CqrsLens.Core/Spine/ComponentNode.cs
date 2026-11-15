namespace CqrsLens;

/// <summary>
/// One symbol-derived architectural participant in the spine (F0 §2.1, §3.1).
/// Identified uniquely by <see cref="Id"/> (<see cref="ComponentId"/>) and carrying an
/// evidenced set of roles so a multi-role symbol never produces duplicate nodes.
/// </summary>
public sealed class ComponentNode
{
    private readonly Dictionary<ComponentRole, List<EvidenceOccurrence>> _roles = new();
    private IReadOnlyList<ComponentRoleEvidence>? _cachedRoles;

    public ComponentId Id { get; }
    public SymbolId Symbol => Id.Symbol;
    public ProjectId Project => Id.Project;
    public string Name => Id.Symbol.Name;
    public string FullName => Id.Symbol.QualifiedName;
    public SourceLocation? Location { get; }

    public ComponentNode(
        ComponentId id,
        SourceLocation? location,
        ComponentRole initialRole,
        EvidenceOccurrence initialEvidence)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(initialEvidence);

        Id = id;
        Location = location;
        AddRole(initialRole, initialEvidence);
    }

    public IReadOnlyList<ComponentRoleEvidence> Roles
    {
        get
        {
            if (_cachedRoles is not null)
                return _cachedRoles;

            _cachedRoles = _roles
                .OrderBy(pair => pair.Key)
                .Select(pair => new ComponentRoleEvidence(
                    pair.Key,
                    pair.Value.OrderBy(e => e).ToArray()))
                .ToArray();
            return _cachedRoles;
        }
    }

    public void AddRole(ComponentRole role, EvidenceOccurrence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        if (!_roles.TryGetValue(role, out var occurrences))
        {
            occurrences = new List<EvidenceOccurrence>();
            _roles[role] = occurrences;
        }

        if (!occurrences.Contains(evidence))
        {
            occurrences.Add(evidence);
            _cachedRoles = null;
        }
    }

    public bool HasRole(ComponentRole role) => _roles.ContainsKey(role);

    internal int RoleCount => _roles.Count;

    internal bool RemoveRole(ComponentRole role)
    {
        if (!_roles.Remove(role))
            return false;

        _cachedRoles = null;
        return true;
    }

    public IReadOnlyList<EvidenceOccurrence> GetRoleEvidence(ComponentRole role)
    {
        if (!_roles.TryGetValue(role, out var occurrences))
            return Array.Empty<EvidenceOccurrence>();
        return occurrences.OrderBy(e => e).ToArray();
    }
}
