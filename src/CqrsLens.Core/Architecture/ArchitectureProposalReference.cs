namespace CqrsLens;

/// <summary>A proposed addition or removal of a directed project reference.</summary>
public sealed record ArchitectureProposalReference(string From, string To, string Operation = "add");
