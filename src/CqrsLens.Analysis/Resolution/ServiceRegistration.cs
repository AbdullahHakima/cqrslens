namespace CqrsLens;

/// <summary>
/// One explicit DI registration recovered from source, before name resolution.
/// Names and generic arguments are kept as written; the index builder resolves
/// them with the declaring file's namespace and usings.
/// </summary>
internal sealed record ServiceRegistration(
    SourceFile File,
    int SourceIndex,
    string ServiceName,
    List<string> ServiceArguments,
    bool ServiceIsOpen,
    int ServiceArity,
    string ImplementationName,
    List<string> ImplementationArguments,
    bool ImplementationIsOpen,
    int ImplementationArity,
    ServiceRegistrationKind Kind,
    ServiceRegistrationForm Form);
