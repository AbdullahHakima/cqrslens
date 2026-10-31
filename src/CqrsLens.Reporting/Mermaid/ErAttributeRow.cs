namespace CqrsLens;

/// <summary>A single attribute row inside an ER entity.</summary>
internal sealed record ErAttributeRow(string Type, string Name, string Keys);
