namespace CqrsLens;

/// <summary>
/// A DI registration occurrence captured for service role attribution (concrete
/// self-registrations, <c>typeof</c> pairs, factory-lambda targets, <c>AddHttpClient</c>,
/// and Autofac <c>RegisterType</c>) without modifying F1's interface resolution table.
/// </summary>
internal sealed record ExplicitServiceRegistration(
    SourceFile File,
    int SourceIndex,
    string TypeName,
    int Arity,
    bool IsRepositoryContract);
