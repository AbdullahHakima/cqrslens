namespace CqrsLens;

/// <summary>Static reference from a test method (or test dispatch site) to a scanned CQRS slice shape.</summary>
public sealed record TestMethodReference(
    string ProjectName,
    string File,
    int Line,
    string ClassName,
    string MethodName,
    string TargetFullName,
    string TargetKind,
    string ReferenceKind);
