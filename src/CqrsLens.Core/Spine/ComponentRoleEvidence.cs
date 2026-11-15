namespace CqrsLens;

/// <summary>
/// Pairs an architectural <see cref="Role"/> on a component with the non-empty evidence
/// occurrences that established that role (F0 §2.1).
/// </summary>
public sealed record ComponentRoleEvidence
{
    public ComponentRole Role { get; }
    public IReadOnlyList<EvidenceOccurrence> Evidence { get; }

    public ComponentRoleEvidence(ComponentRole role, IReadOnlyList<EvidenceOccurrence> evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (evidence.Count == 0)
            throw new ArgumentException("A component role requires at least one evidence occurrence.", nameof(evidence));

        var copy = new EvidenceOccurrence[evidence.Count];
        for (var i = 0; i < evidence.Count; i++)
            copy[i] = evidence[i] ?? throw new ArgumentNullException(nameof(evidence));

        Role = role;
        Evidence = copy;
    }
}
