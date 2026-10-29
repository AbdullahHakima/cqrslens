namespace CqrsLens;

/// <summary>A relationship edge between two ER entities.</summary>
internal sealed record ErRelationshipEdge(
    string PrincipalId,
    string DependentId,
    string Connector,
    string Label);
