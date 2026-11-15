namespace CqrsLens;

/// <summary>
/// Addressable node reference used as relation endpoints and <c>Via</c> intermediates:
/// structural containers, components, endpoints, and typed references (F0 §3).
/// </summary>
public sealed record NodeReference : IComparable<NodeReference>
{
    private readonly int _hashCode;

    public NodeReferenceKind Kind { get; }
    public ProjectId? ProjectId { get; }
    public SymbolId? SymbolId { get; }
    public ComponentId? ComponentId { get; }
    public EndpointId? EndpointId { get; }
    public string Name { get; }
    public string? MemberName { get; }
    public string? Reason { get; }
    public string Key { get; }
    public bool IsExternal => Kind == NodeReferenceKind.External ||
                              (Kind is NodeReferenceKind.Interface or NodeReferenceKind.Method or NodeReferenceKind.Member && SymbolId is null);

    private NodeReference(
        NodeReferenceKind kind,
        ProjectId? projectId,
        SymbolId? symbolId,
        ComponentId? componentId,
        EndpointId? endpointId,
        string name,
        string? memberName,
        string? reason,
        string key)
    {
        Kind = kind;
        ProjectId = projectId;
        SymbolId = symbolId;
        ComponentId = componentId;
        EndpointId = endpointId;
        Name = name;
        MemberName = memberName;
        Reason = reason;
        Key = key;

        var hash = new HashCode();
        hash.Add(kind);
        hash.Add(key, StringComparer.Ordinal);
        _hashCode = hash.ToHashCode();
    }

    public static NodeReference ForProject(ProjectId project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return project.AsNodeReference();
    }

    internal static NodeReference CreateProject(ProjectId project)
        => new(
            NodeReferenceKind.Project,
            project,
            null,
            null,
            null,
            project.Value,
            null,
            null,
            $"Project\u001f{project.Value}");

    public static NodeReference ForLayer(string layerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layerName);
        var trimmed = layerName.Trim();
        return new NodeReference(
            NodeReferenceKind.Layer,
            null,
            null,
            null,
            null,
            trimmed,
            null,
            null,
            $"Layer\u001f{trimmed}");
    }

    public static NodeReference ForComponent(ComponentId component)
    {
        ArgumentNullException.ThrowIfNull(component);
        return component.AsNodeReference();
    }

    internal static NodeReference CreateComponent(ComponentId component)
        => new(
            NodeReferenceKind.Component,
            component.Project,
            component.Symbol,
            component,
            null,
            component.Symbol.QualifiedName,
            null,
            null,
            $"Component\u001f{component}");

    public static NodeReference ForEndpoint(EndpointId endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return endpoint.AsNodeReference();
    }

    internal static NodeReference CreateEndpoint(EndpointId endpoint)
        => new(
            NodeReferenceKind.Endpoint,
            endpoint.Project,
            null,
            null,
            endpoint,
            endpoint.ToString(),
            null,
            null,
            $"Endpoint\u001f{endpoint}");

    public static NodeReference ForInterface(SymbolId symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        return new NodeReference(
            NodeReferenceKind.Interface,
            symbol.Project,
            symbol,
            null,
            null,
            symbol.QualifiedName,
            null,
            null,
            $"Interface\u001f{symbol}");
    }

    public static NodeReference ForExternalInterface(string signature)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signature);
        var trimmed = signature.Trim();
        return new NodeReference(
            NodeReferenceKind.Interface,
            null,
            null,
            null,
            null,
            trimmed,
            null,
            null,
            $"Interface\u001fexternal\u001f{trimmed}");
    }

    public static NodeReference ForMethod(SymbolId declaringType, string methodName)
    {
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
        var trimmedMethod = methodName.Trim();
        return new NodeReference(
            NodeReferenceKind.Method,
            declaringType.Project,
            declaringType,
            null,
            null,
            $"{declaringType.QualifiedName}.{trimmedMethod}",
            trimmedMethod,
            null,
            $"Method\u001f{declaringType}\u001f{trimmedMethod}");
    }

    public static NodeReference ForExternalMethod(string declaringTypeName, string methodName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(declaringTypeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
        var trimmedType = declaringTypeName.Trim();
        var trimmedMethod = methodName.Trim();
        return new NodeReference(
            NodeReferenceKind.Method,
            null,
            null,
            null,
            null,
            $"{trimmedType}.{trimmedMethod}",
            trimmedMethod,
            null,
            $"Method\u001fexternal\u001f{trimmedType}\u001f{trimmedMethod}");
    }

    public static NodeReference ForMember(SymbolId declaringType, string memberName)
    {
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentException.ThrowIfNullOrWhiteSpace(memberName);
        var trimmedMember = memberName.Trim();
        return new NodeReference(
            NodeReferenceKind.Member,
            declaringType.Project,
            declaringType,
            null,
            null,
            $"{declaringType.QualifiedName}.{trimmedMember}",
            trimmedMember,
            null,
            $"Member\u001f{declaringType}\u001f{trimmedMember}");
    }

    public static NodeReference ForExternalMember(string declaringTypeName, string memberName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(declaringTypeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(memberName);
        var trimmedType = declaringTypeName.Trim();
        var trimmedMember = memberName.Trim();
        return new NodeReference(
            NodeReferenceKind.Member,
            null,
            null,
            null,
            null,
            $"{trimmedType}.{trimmedMember}",
            trimmedMember,
            null,
            $"Member\u001fexternal\u001f{trimmedType}\u001f{trimmedMember}");
    }

    public static NodeReference ForType(SymbolId symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        return new NodeReference(
            NodeReferenceKind.Type,
            symbol.Project,
            symbol,
            null,
            null,
            symbol.QualifiedName,
            null,
            null,
            $"Type\u001f{symbol}");
    }

    public static NodeReference ForExternal(string typeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        var trimmed = typeName.Trim();
        return new NodeReference(
            NodeReferenceKind.External,
            null,
            null,
            null,
            null,
            trimmed,
            null,
            null,
            $"External\u001f{trimmed}");
    }

    public static NodeReference ForUnresolved(string writtenName, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(writtenName);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var trimmedName = writtenName.Trim();
        var trimmedReason = reason.Trim();
        return new NodeReference(
            NodeReferenceKind.Unresolved,
            null,
            null,
            null,
            null,
            trimmedName,
            null,
            trimmedReason,
            $"Unresolved\u001f{trimmedName}\u001f{trimmedReason}");
    }

    public bool Equals(NodeReference? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (_hashCode != other._hashCode) return false;

        return Kind == other.Kind
               && Equals(ProjectId, other.ProjectId)
               && Equals(SymbolId, other.SymbolId)
               && Equals(ComponentId, other.ComponentId)
               && Equals(EndpointId, other.EndpointId)
               && string.Equals(Name, other.Name, StringComparison.Ordinal)
               && string.Equals(MemberName, other.MemberName, StringComparison.Ordinal)
               && string.Equals(Reason, other.Reason, StringComparison.Ordinal)
               && string.Equals(Key, other.Key, StringComparison.Ordinal);
    }

    public override int GetHashCode() => _hashCode;

    public int CompareTo(NodeReference? other)
    {
        if (other is null) return 1;
        if (ReferenceEquals(this, other)) return 0;

        var cmp = ((int)Kind).CompareTo((int)other.Kind);
        if (cmp != 0) return cmp;

        cmp = string.Compare(Key, other.Key, StringComparison.Ordinal);
        return cmp != 0 ? cmp : string.Compare(Name, other.Name, StringComparison.Ordinal);
    }

    public static bool operator <(NodeReference? left, NodeReference? right)
        => left is null ? right is not null : left.CompareTo(right) < 0;

    public static bool operator <=(NodeReference? left, NodeReference? right)
        => left is null || left.CompareTo(right) <= 0;

    public static bool operator >(NodeReference? left, NodeReference? right)
        => left is not null && left.CompareTo(right) > 0;

    public static bool operator >=(NodeReference? left, NodeReference? right)
        => left is null ? right is null : left.CompareTo(right) >= 0;

    public override string ToString()
        => Kind == NodeReferenceKind.Unresolved
            ? $"Unresolved:{Name} ({Reason})"
            : $"{Kind}:{Name}";
}
