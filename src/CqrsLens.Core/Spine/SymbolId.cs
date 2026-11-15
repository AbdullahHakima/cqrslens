namespace CqrsLens;

/// <summary>
/// Stable identity of a declared or constructed type symbol: declaring <see cref="Project"/>
/// plus qualified declaration identity composed via <see cref="TypeFullName"/>, generic arity,
/// and constructed type arguments when closed (F0 §2).
/// </summary>
public sealed record SymbolId : IComparable<SymbolId>
{
    private readonly int _hashCode;
    private string? _toString;

    public ProjectId Project { get; }
    public string Namespace { get; }
    public IReadOnlyList<string> ContainingTypes { get; }
    public string Name { get; }
    public int GenericArity { get; }
    public IReadOnlyList<string> TypeArguments { get; }
    public string FullName { get; }
    public string QualifiedName { get; }

    public bool IsGeneric => GenericArity > 0;
    public bool IsOpenGeneric => GenericArity > 0 && TypeArguments.Count == 0;
    public bool IsClosedGeneric => GenericArity > 0 && TypeArguments.Count > 0;

    private SymbolId(
        ProjectId project,
        string ns,
        string[] containingTypes,
        string name,
        int genericArity,
        string[] typeArguments,
        string fullName,
        string qualifiedName)
    {
        Project = project;
        Namespace = ns;
        ContainingTypes = containingTypes;
        Name = name;
        GenericArity = genericArity;
        TypeArguments = typeArguments;
        FullName = fullName;
        QualifiedName = qualifiedName;
        _hashCode = ComputeHashCode(project, ns, containingTypes, name, genericArity, typeArguments);
    }

    public static SymbolId Create(
        ProjectId project,
        string? @namespace,
        IReadOnlyList<string>? containingTypes,
        string name,
        int genericArity = 0,
        IReadOnlyList<string>? typeArguments = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (genericArity < 0)
            throw new ArgumentOutOfRangeException(nameof(genericArity), "Generic arity cannot be negative.");

        var ns = @namespace?.Trim() ?? string.Empty;
        var containing = CopyNonEmpty(containingTypes);
        var args = CopyNonEmpty(typeArguments);
        var trimmedName = name.Trim();

        if (args.Length > 0)
        {
            if (genericArity == 0)
                genericArity = args.Length;
            else if (args.Length != genericArity)
                throw new ArgumentException(
                    $"Closed generic type argument count ({args.Length}) must match generic arity ({genericArity}).",
                    nameof(typeArguments));
        }

        var fullName = TypeFullName.Compose(ns, containing, trimmedName);
        var qualifiedName = FormatQualifiedName(fullName, genericArity, args);

        return new SymbolId(
            project,
            ns,
            containing,
            trimmedName,
            genericArity,
            args,
            fullName,
            qualifiedName);
    }

    public bool Equals(SymbolId? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (_hashCode != other._hashCode) return false;

        return Project.Equals(other.Project)
               && GenericArity == other.GenericArity
               && string.Equals(Namespace, other.Namespace, StringComparison.Ordinal)
               && string.Equals(Name, other.Name, StringComparison.Ordinal)
               && SequenceEquals(ContainingTypes, other.ContainingTypes)
               && SequenceEquals(TypeArguments, other.TypeArguments);
    }

    public override int GetHashCode() => _hashCode;

    public int CompareTo(SymbolId? other)
    {
        if (other is null) return 1;
        if (ReferenceEquals(this, other)) return 0;

        var cmp = Project.CompareTo(other.Project);
        if (cmp != 0) return cmp;

        cmp = string.Compare(QualifiedName, other.QualifiedName, StringComparison.Ordinal);
        if (cmp != 0) return cmp;

        cmp = string.Compare(Namespace, other.Namespace, StringComparison.Ordinal);
        if (cmp != 0) return cmp;

        cmp = CompareSequences(ContainingTypes, other.ContainingTypes);
        if (cmp != 0) return cmp;

        cmp = string.Compare(Name, other.Name, StringComparison.Ordinal);
        if (cmp != 0) return cmp;

        cmp = GenericArity.CompareTo(other.GenericArity);
        return cmp != 0 ? cmp : CompareSequences(TypeArguments, other.TypeArguments);
    }

    public static bool operator <(SymbolId? left, SymbolId? right)
        => left is null ? right is not null : left.CompareTo(right) < 0;

    public static bool operator <=(SymbolId? left, SymbolId? right)
        => left is null || left.CompareTo(right) <= 0;

    public static bool operator >(SymbolId? left, SymbolId? right)
        => left is not null && left.CompareTo(right) > 0;

    public static bool operator >=(SymbolId? left, SymbolId? right)
        => left is null ? right is null : left.CompareTo(right) >= 0;

    public override string ToString() => _toString ??= $"{Project.Value}::{QualifiedName}";

    private static string FormatQualifiedName(string fullName, int arity, string[] args)
    {
        if (arity == 0)
            return fullName;
        if (args.Length == 0)
            return $"{fullName}`{arity}";
        return $"{fullName}`{arity}<{string.Join(", ", args)}>";
    }

    private static string[] CopyNonEmpty(IReadOnlyList<string>? items)
    {
        if (items is null || items.Count == 0)
            return Array.Empty<string>();

        var result = new string[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            var value = items[i];
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Identity segment cannot be null or whitespace.", nameof(items));
            result[i] = value.Trim();
        }
        return result;
    }

    private static bool SequenceEquals(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        if (left.Count != right.Count) return false;
        for (var i = 0; i < left.Count; i++)
        {
            if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    private static int CompareSequences(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        var count = Math.Min(left.Count, right.Count);
        for (var i = 0; i < count; i++)
        {
            var cmp = string.Compare(left[i], right[i], StringComparison.Ordinal);
            if (cmp != 0) return cmp;
        }
        return left.Count.CompareTo(right.Count);
    }

    private static int ComputeHashCode(
        ProjectId project,
        string ns,
        string[] containingTypes,
        string name,
        int genericArity,
        string[] typeArguments)
    {
        var hash = new HashCode();
        hash.Add(project);
        hash.Add(ns, StringComparer.Ordinal);
        hash.Add(containingTypes.Length);
        foreach (var item in containingTypes)
            hash.Add(item, StringComparer.Ordinal);
        hash.Add(name, StringComparer.Ordinal);
        hash.Add(genericArity);
        hash.Add(typeArguments.Length);
        foreach (var arg in typeArguments)
            hash.Add(arg, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
}
