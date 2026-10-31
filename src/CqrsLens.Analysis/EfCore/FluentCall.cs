namespace CqrsLens;

/// <summary>One fluent builder call: method name, optional generic argument and raw arguments.</summary>
internal readonly record struct FluentCall(string Name, string? Generic, string Args);
